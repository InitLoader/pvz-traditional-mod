#include "hook_modules.h"

#include "hook_utils.h"
#include "logger.h"
#include "spawn_config.h"
#include "wave_generator.h"
#include "wave_multiplier.h"
#include "wave_multiplier_config.h"

#include <array>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <memory>
#include <mutex>
#include <optional>

extern "C" void* g_originalPickZombieWaves = nullptr;
extern "C" void PickZombieWavesDetour();
extern "C" void __stdcall ApplyWaveOverrideFromHook(void* board);

namespace pvzmod {
namespace {

constexpr std::uintptr_t kPickZombieWavesRva = 0x000092E0;
constexpr std::array<std::uint8_t, 12> kPickZombieWavesPrologue = {
    0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8, 0x8B, 0x8F, 0x8C, 0x00, 0x00, 0x00};
constexpr std::ptrdiff_t kBoardLawnAppOffset = 0x008C;
constexpr std::ptrdiff_t kSpawnListOffset = 0x06B4;
constexpr std::ptrdiff_t kSpawnTypeOffset = 0x54D4;
constexpr std::ptrdiff_t kAdventureLevelOffset = 0x5550;
constexpr std::ptrdiff_t kActiveWaveCountOffset = 0x5564;
constexpr std::ptrdiff_t kGameModeOffset = 0x07F8;

class SpawnConfigRuntime {
public:
    void Initialize(const std::filesystem::path& path) {
        std::lock_guard lock(mutex_);
        path_ = path;
        ReloadLocked(true);
    }
    std::shared_ptr<const SpawnConfig> Current() {
        std::lock_guard lock(mutex_);
        ReloadLocked(false);
        return config_;
    }

private:
    void ReloadLocked(const bool force) {
        std::error_code error;
        if (!std::filesystem::exists(path_, error)) {
            if (force || !missingWasLogged_) {
                LogWarning("Spawn config not found: " + path_.string() + "; all levels use original spawns.");
                missingWasLogged_ = true;
            }
            return;
        }
        const auto writeTime = std::filesystem::last_write_time(path_, error);
        if (error || (!force && lastWriteTime_.has_value() && writeTime == *lastWriteTime_)) {
            return;
        }
        lastWriteTime_ = writeTime;
        missingWasLogged_ = false;
        ConfigLoadResult loaded = LoadSpawnConfig(path_);
        if (!loaded.Ok()) {
            LogError(loaded.error + "; keeping the last valid spawn config.");
            return;
        }
        config_ = std::make_shared<SpawnConfig>(std::move(*loaded.config));
        for (const std::string& warning : loaded.warnings) {
            LogWarning(warning);
        }
        LogInfo("Loaded spawn config with " + std::to_string(config_->adventureLevels.size()) +
                " sparse level override(s).");
    }
    std::mutex mutex_;
    std::filesystem::path path_;
    std::optional<std::filesystem::file_time_type> lastWriteTime_;
    std::shared_ptr<const SpawnConfig> config_;
    bool missingWasLogged_ = false;
};

class WaveMultiplierRuntime {
public:
    void Initialize(const std::filesystem::path& path) {
        std::lock_guard lock(mutex_);
        path_ = path;
        ReloadLocked(true);
    }
    std::shared_ptr<const WaveMultiplierConfig> Current() {
        std::lock_guard lock(mutex_);
        ReloadLocked(false);
        return config_;
    }

private:
    void ReloadLocked(const bool force) {
        std::error_code error;
        if (!std::filesystem::exists(path_, error)) {
            if (force || !missingWasLogged_) {
                LogWarning("Wave multiplier config not found: " + path_.string() + "; multipliers are disabled.");
                missingWasLogged_ = true;
            }
            return;
        }
        const auto writeTime = std::filesystem::last_write_time(path_, error);
        if (error || (!force && lastWriteTime_.has_value() && writeTime == *lastWriteTime_)) {
            return;
        }
        lastWriteTime_ = writeTime;
        missingWasLogged_ = false;
        WaveMultiplierConfigLoadResult loaded = LoadWaveMultiplierConfig(path_);
        if (!loaded.Ok()) {
            LogError(loaded.error + "; keeping the last valid wave multiplier config.");
            return;
        }
        config_ = std::make_shared<WaveMultiplierConfig>(std::move(*loaded.config));
        for (const std::string& warning : loaded.warnings) {
            LogWarning(warning);
        }
        LogInfo("Loaded wave multiplier config: global x" + std::to_string(config_->globalCountMultiplier) +
                ", " + std::to_string(config_->adventureLevels.size()) + " sparse level rule(s).");
    }
    std::mutex mutex_;
    std::filesystem::path path_;
    std::optional<std::filesystem::file_time_type> lastWriteTime_;
    std::shared_ptr<const WaveMultiplierConfig> config_;
    bool missingWasLogged_ = false;
};

SpawnConfigRuntime g_spawnRuntime;
WaveMultiplierRuntime g_multiplierRuntime;

}  // namespace

bool InstallWaveHooks(std::uint8_t* moduleBase) {
    if (!VerifyHookTarget(moduleBase, kPickZombieWavesRva, kPickZombieWavesPrologue, "Board::PickZombieWaves")) {
        return false;
    }
    g_spawnRuntime.Initialize(ModuleDirectory() / L"pvzmod" / L"config" / L"levels" / L"spawn.json");
    g_multiplierRuntime.Initialize(
        ModuleDirectory() / L"pvzmod" / L"config" / L"levels" / L"wave_multipliers.json");
    return CreateAndEnableHook(
        moduleBase, kPickZombieWavesRva, reinterpret_cast<void*>(&PickZombieWavesDetour),
        &g_originalPickZombieWaves, "Board::PickZombieWaves");
}

}  // namespace pvzmod

extern "C" void __declspec(naked) PickZombieWavesDetour() {
    __asm {
        call dword ptr [g_originalPickZombieWaves]
        pushfd
        pushad
        push edi
        call ApplyWaveOverrideFromHook
        popad
        popfd
        ret
    }
}

extern "C" void __stdcall ApplyWaveOverrideFromHook(void* board) {
    if (board == nullptr) {
        return;
    }
    try {
        auto* bytes = static_cast<std::uint8_t*>(board);
        void* app = *reinterpret_cast<void**>(bytes + pvzmod::kBoardLawnAppOffset);
        if (app == nullptr || *reinterpret_cast<const int*>(
                static_cast<const std::uint8_t*>(app) + pvzmod::kGameModeOffset) != 0) {
            return;
        }
        const int level = *reinterpret_cast<const int*>(bytes + pvzmod::kAdventureLevelOffset);
        const int waveCount = *reinterpret_cast<const int*>(bytes + pvzmod::kActiveWaveCountOffset);
        if (level < 1 || level > 50 || waveCount < 1 || waveCount > static_cast<int>(pvzmod::kWaveCount)) {
            return;
        }
        const std::shared_ptr<const pvzmod::SpawnConfig> spawnConfig = pvzmod::g_spawnRuntime.Current();
        const pvzmod::LevelSpawnConfig* spawnLevel = spawnConfig ? spawnConfig->FindAdventureLevel(level) : nullptr;
        const std::shared_ptr<const pvzmod::WaveMultiplierConfig> multiplier = pvzmod::g_multiplierRuntime.Current();
        const bool scale = multiplier && multiplier->HasEffectForLevel(level, waveCount);
        if (spawnLevel == nullptr && !scale) {
            return;
        }
        std::array<int, pvzmod::kSpawnListCount> list{};
        std::array<std::uint8_t, pvzmod::kZombieTypeCount> allowed{};
        std::memcpy(list.data(), bytes + pvzmod::kSpawnListOffset, sizeof(list));
        std::memcpy(allowed.data(), bytes + pvzmod::kSpawnTypeOffset, sizeof(allowed));

        std::optional<pvzmod::WaveApplyStats> spawnStats;
        std::optional<pvzmod::WaveMultiplierApplyStats> multiplierStats;
        if (spawnLevel) {
            spawnStats = pvzmod::ApplyWeightedWaveOverride(list, allowed, *spawnLevel, level, waveCount);
        }
        if (scale) {
            multiplierStats = pvzmod::ApplyWaveCountMultipliers(list, *multiplier, level, waveCount);
        }
        pvzmod::RebuildAllowedZombieTypes(list, allowed, waveCount);
        std::memcpy(bytes + pvzmod::kSpawnListOffset, list.data(), sizeof(list));
        std::memcpy(bytes + pvzmod::kSpawnTypeOffset, allowed.data(), sizeof(allowed));

        if (spawnStats) {
            pvzmod::LogInfo("Applied sparse wave override for " + pvzmod::FormatAdventureLevelKey(level) +
                            ": replaced " + std::to_string(spawnStats->replacedSlots) + " slot(s).");
        }
        if (multiplierStats) {
            pvzmod::LogInfo("Applied zombie count multipliers for " + pvzmod::FormatAdventureLevelKey(level) +
                            ": changed " + std::to_string(multiplierStats->changedWaves) + " wave(s).");
        }
    } catch (const std::exception& exception) {
        pvzmod::LogError(std::string("Wave override failed: ") + exception.what());
    }
}
