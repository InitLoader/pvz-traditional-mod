#include "hook_modules.h"

#include "global_config.h"
#include "hook_utils.h"
#include "logger.h"

#include <array>
#include <cstdint>
#include <filesystem>
#include <memory>
#include <mutex>
#include <optional>

extern "C" void* g_originalCoinGetSunValue = nullptr;
extern "C" void* g_originalCoinScore = nullptr;
extern "C" void CoinGetSunValueDetour();
extern "C" void CoinScoreDetour();
extern "C" int __stdcall ResolveSunValueFromCoin(void* coin);
extern "C" void __stdcall PrepareSunScore(void* coin);
extern "C" void __stdcall FinishSunScore();

namespace pvzmod {
namespace {

constexpr std::uintptr_t kCoinGetSunValueRva = 0x000329A0;
constexpr std::uintptr_t kCoinScoreRva = 0x000309D0;
constexpr std::array<std::uint8_t, 15> kCoinGetSunValuePrologue = {
    0x8B, 0x40, 0x58, 0x83, 0xF8, 0x04, 0x75, 0x06, 0xB8, 0x19, 0x00, 0x00, 0x00, 0xC3, 0x83};
constexpr std::array<std::uint8_t, 14> kCoinScorePrologue = {
    0x56, 0x8B, 0xF0, 0xE8, 0xF8, 0x23, 0x00, 0x00, 0x8B, 0x46, 0x58, 0x83, 0xF8, 0x04};

struct PendingSunScore {
    int* sunTotal = nullptr;
    int beforePickup = 0;
    int configuredPickup = 0;
    bool active = false;
};

thread_local PendingSunScore g_pendingSunScore;

class SunConfigRuntime {
public:
    void Initialize(const std::filesystem::path& path) {
        std::lock_guard lock(mutex_);
        path_ = path;
        ReloadLocked(true);
    }

    std::shared_ptr<const GlobalConfig> Current() {
        std::lock_guard lock(mutex_);
        ReloadLocked(false);
        return config_;
    }

private:
    void ReloadLocked(const bool force) {
        std::error_code error;
        const bool exists = std::filesystem::exists(path_, error);
        if (error || !exists) {
            if (force || !missingWasLogged_) {
                LogWarning("Global config not found: " + path_.string() + "; original global values are used.");
                missingWasLogged_ = true;
            }
            return;
        }
        const auto writeTime = std::filesystem::last_write_time(path_, error);
        if (error) {
            LogWarning("Cannot read global config modification time: " + error.message());
            return;
        }
        if (!force && lastWriteTime_.has_value() && writeTime == *lastWriteTime_) {
            return;
        }
        lastWriteTime_ = writeTime;
        missingWasLogged_ = false;
        GlobalConfigLoadResult loaded = LoadGlobalConfig(path_);
        if (!loaded.Ok()) {
            LogError(loaded.error + "; keeping the last valid global config.");
            return;
        }
        config_ = std::make_shared<GlobalConfig>(std::move(*loaded.config));
        LogInfo(
            "Loaded global config: sun normal=" + std::to_string(config_->sunPickupValues.normal) +
            ", small=" + std::to_string(config_->sunPickupValues.small) +
            ", large=" + std::to_string(config_->sunPickupValues.large) + '.');
    }

    std::mutex mutex_;
    std::filesystem::path path_;
    std::optional<std::filesystem::file_time_type> lastWriteTime_;
    std::shared_ptr<const GlobalConfig> config_;
    bool missingWasLogged_ = false;
};

SunConfigRuntime g_sunConfigRuntime;

}  // namespace

bool InstallSunHooks(std::uint8_t* moduleBase) {
    if (!VerifyHookTarget(moduleBase, kCoinGetSunValueRva, kCoinGetSunValuePrologue, "Coin::GetSunValue") ||
        !VerifyHookTarget(moduleBase, kCoinScoreRva, kCoinScorePrologue, "Coin::ScoreCoin")) {
        return false;
    }
    g_sunConfigRuntime.Initialize(ModuleDirectory() / L"pvzmod" / L"config" / L"settings" / L"global.json");
    return CreateAndEnableHook(
               moduleBase, kCoinGetSunValueRva, reinterpret_cast<void*>(&CoinGetSunValueDetour),
               &g_originalCoinGetSunValue, "Coin::GetSunValue") &&
        CreateAndEnableHook(
               moduleBase, kCoinScoreRva, reinterpret_cast<void*>(&CoinScoreDetour),
               &g_originalCoinScore, "Coin::ScoreCoin");
}

}  // namespace pvzmod

extern "C" void __declspec(naked) CoinGetSunValueDetour() {
    __asm {
        push ecx
        push edx
        push eax
        call ResolveSunValueFromCoin
        pop edx
        pop ecx
        ret
    }
}

extern "C" void __declspec(naked) CoinScoreDetour() {
    __asm {
        push eax
        push ecx
        push edx
        push eax
        call PrepareSunScore
        pop edx
        pop ecx
        pop eax
        call dword ptr [g_originalCoinScore]
        push eax
        push ecx
        push edx
        call FinishSunScore
        pop edx
        pop ecx
        pop eax
        ret
    }
}

extern "C" int __stdcall ResolveSunValueFromCoin(void* coin) {
    if (coin == nullptr) {
        return 0;
    }
    pvzmod::SunPickupValues values;
    try {
        const std::shared_ptr<const pvzmod::GlobalConfig> config = pvzmod::g_sunConfigRuntime.Current();
        if (config) {
            values = config->sunPickupValues;
        }
    } catch (const std::exception& exception) {
        pvzmod::LogError(std::string("Sun value config lookup failed: ") + exception.what());
    }
    const int coinType = *reinterpret_cast<const int*>(static_cast<const std::uint8_t*>(coin) + 0x58);
    return pvzmod::SunValueForCoinType(values, coinType);
}

extern "C" void __stdcall PrepareSunScore(void* coin) {
    pvzmod::g_pendingSunScore = {};
    if (coin == nullptr) {
        return;
    }
    auto* bytes = static_cast<std::uint8_t*>(coin);
    const int coinType = *reinterpret_cast<const int*>(bytes + 0x58);
    if (coinType < 4 || coinType > 6) {
        return;
    }
    void* board = *reinterpret_cast<void**>(bytes + 0x04);
    if (board == nullptr) {
        return;
    }
    pvzmod::SunPickupValues values;
    const std::shared_ptr<const pvzmod::GlobalConfig> config = pvzmod::g_sunConfigRuntime.Current();
    if (config) {
        values = config->sunPickupValues;
    }
    int* total = reinterpret_cast<int*>(static_cast<std::uint8_t*>(board) + 0x5560);
    pvzmod::g_pendingSunScore = {total, *total, pvzmod::SunValueForCoinType(values, coinType), true};
}

extern "C" void __stdcall FinishSunScore() {
    const pvzmod::PendingSunScore pending = pvzmod::g_pendingSunScore;
    pvzmod::g_pendingSunScore = {};
    if (pending.active && pending.sunTotal != nullptr) {
        *pending.sunTotal = pvzmod::SunTotalAfterPickup(pending.beforePickup, pending.configuredPickup);
    }
}
