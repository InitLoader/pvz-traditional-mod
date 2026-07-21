#include "hook_modules.h"

#include "hook_utils.h"
#include "logger.h"
#include "zombie_armor_adapter.h"
#include "zombie_config.h"
#include "zombie_event_bus.h"

#include <array>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <iterator>
#include <limits>
#include <memory>
#include <mutex>
#include <optional>
#include <set>
#include <unordered_map>
#include <vector>

extern "C" void* g_originalZombieInitialize = nullptr;
extern "C" void* g_originalZombieEatPlant = nullptr;
extern "C" void* g_originalZombieDropHead = nullptr;
extern "C" void* g_originalZombieDropShield = nullptr;
extern "C" void* g_originalZombieDropHelm = nullptr;
extern "C" void* g_originalZombieLandFlyer = nullptr;
extern "C" void* g_zombieReanimShowPrefix = nullptr;
extern "C" void* g_zombieAttachShield = nullptr;
extern "C" void* g_zombieAddAttachedReanim = nullptr;
extern "C" void* g_reanimationSetFramesForLayer = nullptr;
extern "C" void* g_reanimationDie = nullptr;
extern "C" void ZombieInitializeDetour();
extern "C" void ZombieDropHeadDetour();
extern "C" void ZombieDropShieldDetour();
extern "C" void ZombieDropHelmDetour();
extern "C" void ZombieLandFlyerDetour();
extern "C" void __fastcall ZombieEatPlantDetour(void* zombie, void* unusedEdx, void* plant);
extern "C" void __stdcall PrepareZombieConfigBeforeInitialize(void* zombie, int* zombieType);
extern "C" void __stdcall ApplyZombieConfigAfterInitialize(void* zombie);
extern "C" void __stdcall NotifyZombieInitializedExtensions(void* zombie);
extern "C" void __stdcall PrepareWallnutDropHead(void* zombie);
extern "C" void __stdcall FinishWallnutDropHead(void* zombie);
extern "C" int __stdcall DropOverlayShield(void* zombie, unsigned int damageFlags);
extern "C" int __stdcall DropOverlayHelmet(void* zombie, unsigned int damageFlags);
extern "C" int __stdcall DropOverlayFlying(void* zombie, unsigned int damageFlags);
extern "C" void* __stdcall CallZombieAddAttachedReanim(
    void* zombie, int offsetX, int offsetY, int reanimationType);
extern "C" void __stdcall CallReanimationSetFramesForLayer(void* reanimation, const char* layer);
extern "C" void __stdcall CallReanimationDie(void* reanimation);

namespace pvzmod {
namespace {

constexpr std::uintptr_t kZombieInitializeRva = 0x00122580;
constexpr std::uintptr_t kZombieEatPlantRva = 0x0012FB40;
constexpr std::uintptr_t kZombieDropHeadRva = 0x00129A30;
constexpr std::uintptr_t kZombieLandFlyerRva = 0x00125B60;
constexpr std::uintptr_t kZombieDropShieldRva = 0x00130A00;
constexpr std::uintptr_t kZombieDropHelmRva = 0x00130E30;
constexpr std::uintptr_t kZombieReanimShowPrefixRva = 0x001331C0;
constexpr std::uintptr_t kZombieAttachShieldRva = 0x00133000;
constexpr std::uintptr_t kZombieAddAttachedReanimRva = 0x001322C0;
constexpr std::uintptr_t kReanimationSetFramesForLayerRva = 0x00073280;
constexpr std::uintptr_t kReanimationDieRva = 0x000733F0;

constexpr std::array<std::uint8_t, 12> kZombieInitializePrologue = {
    0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8, 0x83, 0xEC, 0x14, 0x53, 0x56, 0x8B};
constexpr std::array<std::uint8_t, 12> kZombieEatPlantPrologue = {
    0x6A, 0xFF, 0x68, 0x98, 0xF5, 0x64, 0x00, 0x64, 0xA1, 0x00, 0x00, 0x00};
constexpr std::array<std::uint8_t, 12> kZombieDropHeadPrologue = {
    0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8, 0x83, 0xEC, 0x2C, 0x53, 0x8B, 0x5D};
constexpr std::array<std::uint8_t, 12> kZombieLandFlyerPrologue = {
    0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8, 0x51, 0xF6, 0x45, 0x08, 0x10, 0x57};
constexpr std::array<std::uint8_t, 12> kZombieDropShieldPrologue = {
    0x83, 0xEC, 0x0C, 0x53, 0x55, 0x8B, 0x6C, 0x24, 0x18, 0x8B, 0x85, 0xD8};
constexpr std::array<std::uint8_t, 12> kZombieDropHelmPrologue = {
    0x83, 0xEC, 0x24, 0x53, 0x56, 0x8B, 0xD8, 0x57, 0x8B, 0xBB, 0xC4, 0x00};

constexpr std::ptrdiff_t kBoardOffset = 0x04;
constexpr std::ptrdiff_t kZombieTypeOffset = 0x24;
constexpr std::ptrdiff_t kHelmetTypeOffset = 0xC4;
constexpr std::ptrdiff_t kBodyHealthOffset = 0xC8;
constexpr std::ptrdiff_t kBodyMaxHealthOffset = 0xCC;
constexpr std::ptrdiff_t kHelmetHealthOffset = 0xD0;
constexpr std::ptrdiff_t kHelmetMaxHealthOffset = 0xD4;
constexpr std::ptrdiff_t kShieldTypeOffset = 0xD8;
constexpr std::ptrdiff_t kShieldHealthOffset = 0xDC;
constexpr std::ptrdiff_t kShieldMaxHealthOffset = 0xE0;
constexpr std::ptrdiff_t kFlyingHealthOffset = 0xE4;
constexpr std::ptrdiff_t kFlyingMaxHealthOffset = 0xE8;
constexpr std::ptrdiff_t kSpecialHeadReanimIdOffset = 0x144;
constexpr std::ptrdiff_t kZombieInstanceIdOffset = 0x158;
constexpr std::ptrdiff_t kPlantHealthOffset = 0x40;
constexpr int kOriginalEatDamage = 4;
constexpr int kNormalZombieType = 0;
constexpr int kNullReanimationId = 0;

class ZombieConfigRuntime {
public:
    void Initialize(const std::filesystem::path& path) {
        std::lock_guard lock(mutex_);
        path_ = path;
        ReloadLocked(true);
    }

    std::shared_ptr<const ZombieConfig> Current() {
        std::lock_guard lock(mutex_);
        ReloadLocked(false);
        return config_;
    }

    void WarnUnsupportedVisualOnce(const int zombieType, const int armorId) {
        std::lock_guard lock(mutex_);
        if (unsupportedVisualWarnings_.emplace(zombieType, armorId).second) {
            LogWarning(
                "Skipped armorId " + std::to_string(armorId) + " on zombie id " +
                std::to_string(zombieType) +
                ": its visual adapter is not compatible with this zombie animation family. "
                "Special-body armor adapters currently target normal zombie id 0 or their own native zombie id; "
                "cone, bucket, and door target plain REANIM_ZOMBIE bodies.");
        }
    }

private:
    void ReloadLocked(const bool force) {
        std::error_code error;
        const bool exists = std::filesystem::exists(path_, error);
        if (error || !exists) {
            if (force || !missingWasLogged_) {
                LogWarning("Zombie config not found: " + path_.string() + "; original zombie attributes are used.");
                missingWasLogged_ = true;
            }
            return;
        }
        const auto writeTime = std::filesystem::last_write_time(path_, error);
        if (error) {
            LogWarning("Cannot read zombie config modification time: " + error.message());
            return;
        }
        if (!force && lastWriteTime_.has_value() && writeTime == *lastWriteTime_) {
            return;
        }
        lastWriteTime_ = writeTime;
        missingWasLogged_ = false;
        ZombieConfigLoadResult loaded = LoadZombieConfig(path_);
        if (!loaded.Ok()) {
            LogError(loaded.error + "; keeping the last valid zombie config.");
            return;
        }
        config_ = std::make_shared<ZombieConfig>(std::move(*loaded.config));
        unsupportedVisualWarnings_.clear();
        const std::size_t effectiveOriginalArmorOverrides = static_cast<std::size_t>(std::count_if(
            config_->originalArmorHealth.begin(), config_->originalArmorHealth.end(),
            [](const auto& item) { return item.second.HasEffect(); }));
        LogInfo(
            "Loaded zombie config: " + std::to_string(config_->originalArmorHealth.size()) +
            " original armor value(s), " + std::to_string(effectiveOriginalArmorOverrides) +
            " changed; " + std::to_string(config_->armors.size()) + " random armor definition(s), " +
            std::to_string(config_->zombies.size()) + " sparse zombie override(s).");
    }

    std::mutex mutex_;
    std::filesystem::path path_;
    std::optional<std::filesystem::file_time_type> lastWriteTime_;
    std::shared_ptr<const ZombieConfig> config_;
    std::set<std::pair<int, int>> unsupportedVisualWarnings_;
    bool missingWasLogged_ = false;
};

ZombieConfigRuntime g_zombieConfigRuntime;

struct PreparedArmorInit {
    void* zombie = nullptr;
    int baseType = kNormalZombieType;
    int armorId = 0;
    ArmorVisual visual = ArmorVisual::WallnutHead;
};

struct PreparedPlantHeadDrop {
    void* zombie = nullptr;
    int originalType = kNormalZombieType;
};

thread_local std::vector<PreparedArmorInit> g_preparedArmorInits;
thread_local std::vector<PreparedPlantHeadDrop> g_preparedPlantHeadDrops;
std::mutex g_armorStateMutex;
std::unordered_map<void*, int> g_nativeArmorBaseTypes;
std::unordered_map<void*, int> g_specialPlantHeadTypes;

struct ArmorOverlayEntry {
    void* reanimation = nullptr;
    ArmorVisual visual = ArmorVisual::Ladder;
};

struct ZombieArmorOverlays {
    std::optional<ArmorOverlayEntry> helmet;
    std::optional<ArmorOverlayEntry> shield;
    std::optional<ArmorOverlayEntry> flying;
};

std::unordered_map<void*, ZombieArmorOverlays> g_armorOverlays;

void ForgetArmorState(void* zombie) {
    std::lock_guard lock(g_armorStateMutex);
    g_nativeArmorBaseTypes.erase(zombie);
    g_specialPlantHeadTypes.erase(zombie);
    g_armorOverlays.erase(zombie);
}

void MarkNativeArmorBaseType(void* zombie, const int baseType) {
    std::lock_guard lock(g_armorStateMutex);
    g_nativeArmorBaseTypes[zombie] = baseType;
}

int ResolveConfigZombieType(void* zombie, const int currentType) {
    std::lock_guard lock(g_armorStateMutex);
    const auto found = g_nativeArmorBaseTypes.find(zombie);
    return found == g_nativeArmorBaseTypes.end() ? currentType : found->second;
}

void MarkSpecialPlantHead(void* zombie, const int initializerType) {
    std::lock_guard lock(g_armorStateMutex);
    g_specialPlantHeadTypes[zombie] = initializerType;
}

std::optional<int> FindSpecialPlantHeadType(void* zombie) {
    std::lock_guard lock(g_armorStateMutex);
    const auto found = g_specialPlantHeadTypes.find(zombie);
    return found == g_specialPlantHeadTypes.end() ? std::nullopt : std::optional<int>(found->second);
}

void ForgetSpecialPlantHead(void* zombie) {
    std::lock_guard lock(g_armorStateMutex);
    g_specialPlantHeadTypes.erase(zombie);
}

std::optional<PreparedArmorInit> ConsumePreparedArmorInit(void* zombie) {
    for (auto iterator = g_preparedArmorInits.rbegin(); iterator != g_preparedArmorInits.rend(); ++iterator) {
        if (iterator->zombie != zombie) {
            continue;
        }
        const PreparedArmorInit prepared = *iterator;
        g_preparedArmorInits.erase(std::next(iterator).base());
        return prepared;
    }
    return std::nullopt;
}

bool IsHigherPriorityArmor(const ArmorDefinition* candidate, const ArmorDefinition* selected) {
    return candidate != nullptr &&
        (selected == nullptr || candidate->tier > selected->tier ||
         (candidate->tier == selected->tier && candidate->id < selected->id));
}

bool StartsWithInsensitive(const char* text, const char* prefix) {
    if (text == nullptr || prefix == nullptr) {
        return false;
    }
    const std::size_t prefixLength = std::strlen(prefix);
    return std::strlen(text) >= prefixLength && _strnicmp(text, prefix, prefixLength) == 0;
}

bool OverlayTrackMatches(const ArmorVisual visual, const char* trackName) {
    switch (visual) {
        case ArmorVisual::Newspaper:
            return StartsWithInsensitive(trackName, "Zombie_paper_paper") ||
                StartsWithInsensitive(trackName, "Zombie_paper_hands");
        case ArmorVisual::FootballHelmet:
            return StartsWithInsensitive(trackName, "zombie_football_helmet");
        case ArmorVisual::Bobsled:
            return StartsWithInsensitive(trackName, "Zombie_bobsled") ||
                StartsWithInsensitive(trackName, "Bobsled");
        case ArmorVisual::Balloon:
            return StartsWithInsensitive(trackName, "Zombie_balloon") ||
                StartsWithInsensitive(trackName, "Balloon") ||
                StartsWithInsensitive(trackName, "Propeller");
        case ArmorVisual::DiggerHelmet:
            return StartsWithInsensitive(trackName, "Zombie_digger_hardhat");
        case ArmorVisual::Ladder:
            return StartsWithInsensitive(trackName, "Zombie_ladder_1");
        default:
            return false;
    }
}

const char* OverlayFrameLayer(const ArmorVisual visual) {
    switch (visual) {
        case ArmorVisual::Newspaper:
        case ArmorVisual::FootballHelmet:
        case ArmorVisual::DiggerHelmet:
        case ArmorVisual::Ladder:
            return "anim_walk";
        case ArmorVisual::Bobsled:
            return "anim_push";
        case ArmorVisual::Balloon:
            return "anim_idle";
        default:
            return nullptr;
    }
}

bool FilterArmorOverlayTracks(void* reanimation, const ArmorVisual visual) {
    if (reanimation == nullptr) {
        return false;
    }
    auto* bytes = static_cast<std::uint8_t*>(reanimation);
    auto* definition = *reinterpret_cast<std::uint8_t**>(bytes + 0x0C);
    auto* instances = *reinterpret_cast<std::uint8_t**>(bytes + 0x58);
    if (definition == nullptr || instances == nullptr) {
        return false;
    }
    auto* tracks = *reinterpret_cast<std::uint8_t**>(definition);
    const int trackCount = *reinterpret_cast<int*>(definition + 0x04);
    if (tracks == nullptr || trackCount <= 0 || trackCount > 10000) {
        return false;
    }
    int visibleTrackCount = 0;
    for (int index = 0; index < trackCount; ++index) {
        const char* trackName = *reinterpret_cast<const char**>(tracks + index * 0x0C);
        const bool visible = OverlayTrackMatches(visual, trackName);
        *reinterpret_cast<int*>(instances + index * 0x60 + 0x48) = visible ? 0 : -1;
        visibleTrackCount += visible ? 1 : 0;
    }
    return visibleTrackCount > 0;
}

bool CreateArmorOverlay(void* zombie, const ArmorDefinition& armor) {
    const ArmorVisualAdapterInfo& adapter = GetArmorVisualAdapterInfo(armor.visual);
    if (adapter.overlayReanimationType < 0) {
        return true;
    }
    void* reanimation = CallZombieAddAttachedReanim(zombie, 0, 0, adapter.overlayReanimationType);
    if (reanimation == nullptr) {
        LogWarning("Could not create overlay reanimation for armorId " + std::to_string(armor.id) + ".");
        return false;
    }
    const char* frameLayer = OverlayFrameLayer(armor.visual);
    if (frameLayer != nullptr) {
        CallReanimationSetFramesForLayer(reanimation, frameLayer);
    }
    if (!FilterArmorOverlayTracks(reanimation, armor.visual)) {
        CallReanimationDie(reanimation);
        LogWarning(
            "No matching tracks were found for armorId " + std::to_string(armor.id) +
            " visual " + ArmorVisualConfigName(armor.visual) + ".");
        return false;
    }
    std::lock_guard lock(g_armorStateMutex);
    ZombieArmorOverlays& overlays = g_armorOverlays[zombie];
    ArmorOverlayEntry entry{reanimation, armor.visual};
    if (armor.slot == ArmorSlot::Helmet) {
        overlays.helmet = entry;
    } else if (armor.slot == ArmorSlot::Shield) {
        overlays.shield = entry;
    } else {
        overlays.flying = entry;
    }
    return true;
}

bool RemoveArmorOverlay(void* zombie, const ArmorSlot slot) {
    void* reanimation = nullptr;
    {
        std::lock_guard lock(g_armorStateMutex);
        const auto found = g_armorOverlays.find(zombie);
        if (found == g_armorOverlays.end()) {
            return false;
        }
        std::optional<ArmorOverlayEntry>* entry = slot == ArmorSlot::Helmet
            ? &found->second.helmet
            : (slot == ArmorSlot::Shield ? &found->second.shield : &found->second.flying);
        if (!entry->has_value()) {
            return false;
        }
        reanimation = (*entry)->reanimation;
        entry->reset();
        if (!found->second.helmet.has_value() && !found->second.shield.has_value() &&
            !found->second.flying.has_value()) {
            g_armorOverlays.erase(found);
        }
    }
    if (reanimation != nullptr) {
        CallReanimationDie(reanimation);
    }
    return true;
}

bool HasPlainZombieBody(const int zombieType) {
    switch (zombieType) {
        case 0:
        case 1:
        case 2:
        case 4:
        case 6:
        case 10:
        case 26:
        case 27:
        case 28:
        case 29:
        case 30:
        case 31:
        case 33:
            return true;
        default:
            return false;
    }
}

const ArmorDefinition* PickArmorForSlot(
    const ZombieConfig& config,
    const ZombieAttributeOverride& override,
    const ArmorSlot slot,
    const int zombieType,
    const std::uint32_t instanceId) {
    if (!override.armorRolls.has_value()) {
        return nullptr;
    }
    const ArmorDefinition* selected = nullptr;
    for (const ArmorRoll& roll : *override.armorRolls) {
        const ArmorDefinition* armor = config.FindArmor(roll.armorId);
        if (armor == nullptr || armor->slot != slot ||
            !ArmorRollSucceeds(config.seed, zombieType, instanceId, armor->id, roll.chance)) {
            continue;
        }
        if (selected == nullptr || armor->tier > selected->tier ||
            (armor->tier == selected->tier && armor->id < selected->id)) {
            selected = armor;
        }
    }
    return selected;
}

}  // namespace

bool InstallZombieHooks(std::uint8_t* moduleBase) {
    if (!VerifyHookTarget(moduleBase, kZombieInitializeRva, kZombieInitializePrologue, "Zombie::ZombieInitialize") ||
        !VerifyHookTarget(moduleBase, kZombieEatPlantRva, kZombieEatPlantPrologue, "Zombie::EatPlant") ||
        !VerifyHookTarget(moduleBase, kZombieDropHeadRva, kZombieDropHeadPrologue, "Zombie::DropHead") ||
        !VerifyHookTarget(moduleBase, kZombieLandFlyerRva, kZombieLandFlyerPrologue, "Zombie::LandFlyer") ||
        !VerifyHookTarget(moduleBase, kZombieDropShieldRva, kZombieDropShieldPrologue, "Zombie::DropShield") ||
        !VerifyHookTarget(moduleBase, kZombieDropHelmRva, kZombieDropHelmPrologue, "Zombie::DropHelm")) {
        return false;
    }
    g_zombieReanimShowPrefix = moduleBase + kZombieReanimShowPrefixRva;
    g_zombieAttachShield = moduleBase + kZombieAttachShieldRva;
    g_zombieAddAttachedReanim = moduleBase + kZombieAddAttachedReanimRva;
    g_reanimationSetFramesForLayer = moduleBase + kReanimationSetFramesForLayerRva;
    g_reanimationDie = moduleBase + kReanimationDieRva;
    g_zombieConfigRuntime.Initialize(ModuleDirectory() / L"pvzmod" / L"config" / L"zombies" / L"attributes.jsonc");
    return CreateAndEnableHook(
               moduleBase, kZombieInitializeRva, reinterpret_cast<void*>(&ZombieInitializeDetour),
               &g_originalZombieInitialize, "Zombie::ZombieInitialize") &&
        CreateAndEnableHook(
               moduleBase, kZombieEatPlantRva, reinterpret_cast<void*>(&ZombieEatPlantDetour),
               &g_originalZombieEatPlant, "Zombie::EatPlant") &&
        CreateAndEnableHook(
               moduleBase, kZombieDropHeadRva, reinterpret_cast<void*>(&ZombieDropHeadDetour),
               &g_originalZombieDropHead, "Zombie::DropHead") &&
        CreateAndEnableHook(
               moduleBase, kZombieLandFlyerRva, reinterpret_cast<void*>(&ZombieLandFlyerDetour),
               &g_originalZombieLandFlyer, "Zombie::LandFlyer") &&
        CreateAndEnableHook(
               moduleBase, kZombieDropShieldRva, reinterpret_cast<void*>(&ZombieDropShieldDetour),
               &g_originalZombieDropShield, "Zombie::DropShield") &&
        CreateAndEnableHook(
               moduleBase, kZombieDropHelmRva, reinterpret_cast<void*>(&ZombieDropHelmDetour),
               &g_originalZombieDropHelm, "Zombie::DropHelm");
}

}  // namespace pvzmod

// These bridges pop their C++ arguments with ret N, so callers must use stdcall.
// Declaring them as cdecl would make the caller pop the same arguments again.
extern "C" void __declspec(naked) __stdcall CallZombieReanimShowPrefix(
    void* zombie, const char* prefix, int renderGroup) {
    __asm {
        mov eax, [esp + 4]
        push dword ptr [esp + 12]
        push dword ptr [esp + 12]
        call dword ptr [g_zombieReanimShowPrefix]
        ret 12
    }
}

extern "C" void __declspec(naked) __stdcall CallZombieAttachShield(void* zombie) {
    __asm {
        mov eax, [esp + 4]
        call dword ptr [g_zombieAttachShield]
        ret 4
    }
}

extern "C" __declspec(naked) void* __stdcall CallZombieAddAttachedReanim(
    void* zombie, int offsetX, int offsetY, int reanimationType) {
    __asm {
        push esi
        mov esi, [esp + 8]
        mov edx, [esp + 20]
        push dword ptr [esp + 16]
        push dword ptr [esp + 16]
        call dword ptr [g_zombieAddAttachedReanim]
        pop esi
        ret 16
    }
}

extern "C" void __declspec(naked) __stdcall CallReanimationSetFramesForLayer(
    void* reanimation, const char* layer) {
    __asm {
        mov ecx, [esp + 4]
        push dword ptr [esp + 8]
        call dword ptr [g_reanimationSetFramesForLayer]
        ret 8
    }
}

extern "C" void __declspec(naked) __stdcall CallReanimationDie(void* reanimation) {
    __asm {
        mov ecx, [esp + 4]
        call dword ptr [g_reanimationDie]
        ret 4
    }
}

extern "C" void __stdcall PrepareZombieConfigBeforeInitialize(void* zombie, int* zombieType) {
    if (zombie == nullptr || zombieType == nullptr) {
        return;
    }
    try {
        pvzmod::ForgetArmorState(zombie);
        const int originalType = *zombieType;
        auto* bytes = static_cast<std::uint8_t*>(zombie);
        if (*reinterpret_cast<void**>(bytes + pvzmod::kBoardOffset) == nullptr) {
            return;
        }
        const std::shared_ptr<const pvzmod::ZombieConfig> config = pvzmod::g_zombieConfigRuntime.Current();
        if (!config) {
            return;
        }
        const pvzmod::ZombieAttributeOverride* zombieOverride = config->FindZombie(originalType);
        if (zombieOverride == nullptr) {
            return;
        }
        const std::uint32_t instanceId = *reinterpret_cast<std::uint32_t*>(
            bytes + pvzmod::kZombieInstanceIdOffset);
        const pvzmod::ArmorDefinition* helmet = pvzmod::PickArmorForSlot(
            *config, *zombieOverride, pvzmod::ArmorSlot::Helmet, originalType, instanceId);
        const pvzmod::ArmorDefinition* shield = pvzmod::PickArmorForSlot(
            *config, *zombieOverride, pvzmod::ArmorSlot::Shield, originalType, instanceId);
        const pvzmod::ArmorDefinition* flying = pvzmod::PickArmorForSlot(
            *config, *zombieOverride, pvzmod::ArmorSlot::Flying, originalType, instanceId);

        const pvzmod::ArmorDefinition* initializerArmor = nullptr;
        for (const pvzmod::ArmorDefinition* candidate : {helmet, shield, flying}) {
            if (candidate == nullptr || !pvzmod::RequiresOriginalZombieInitializer(candidate->visual) ||
                !pvzmod::IsHigherPriorityArmor(candidate, initializerArmor)) {
                continue;
            }
            initializerArmor = candidate;
        }
        if (initializerArmor == nullptr) {
            return;
        }
        const pvzmod::ArmorVisualAdapterInfo& adapter =
            pvzmod::GetArmorVisualAdapterInfo(initializerArmor->visual);
        if (originalType != pvzmod::kNormalZombieType && originalType != adapter.initializerZombieType) {
            pvzmod::g_zombieConfigRuntime.WarnUnsupportedVisualOnce(originalType, initializerArmor->id);
            return;
        }
        pvzmod::g_preparedArmorInits.push_back(
            {zombie, originalType, initializerArmor->id, initializerArmor->visual});
        *zombieType = adapter.initializerZombieType;
    } catch (const std::exception& exception) {
        pvzmod::LogError(std::string("Preparing original armor visual failed: ") + exception.what());
    } catch (...) {
        pvzmod::LogError("Preparing original armor visual failed with an unknown exception.");
    }
}

extern "C" void __declspec(naked) ZombieInitializeDetour() {
    __asm {
        pushfd
        pushad
        lea eax, [esp + 44]
        push eax
        push dword ptr [esp + 44]
        call PrepareZombieConfigBeforeInitialize
        popad
        popfd
        push dword ptr [esp + 20]
        push dword ptr [esp + 20]
        push dword ptr [esp + 20]
        push dword ptr [esp + 20]
        push dword ptr [esp + 20]
        call dword ptr [g_originalZombieInitialize]
        pushfd
        pushad
        mov eax, [esp + 40]
        push eax
        call ApplyZombieConfigAfterInitialize
        push dword ptr [esp + 40]
        call NotifyZombieInitializedExtensions
        popad
        popfd
        ret 20
    }
}

extern "C" void __stdcall NotifyZombieInitializedExtensions(void* zombie) {
    pvzmod::DispatchZombieInitialized(zombie);
}

extern "C" void __stdcall ApplyZombieConfigAfterInitialize(void* zombie) {
    if (zombie == nullptr) {
        return;
    }
    try {
        auto* bytes = static_cast<std::uint8_t*>(zombie);
        const std::optional<pvzmod::PreparedArmorInit> prepared = pvzmod::ConsumePreparedArmorInit(zombie);
        int configZombieType = *reinterpret_cast<int*>(bytes + pvzmod::kZombieTypeOffset);
        if (prepared.has_value()) {
            configZombieType = prepared->baseType;
            const pvzmod::ArmorVisualAdapterInfo& adapter =
                pvzmod::GetArmorVisualAdapterInfo(prepared->visual);
            if (adapter.specialPlantHead) {
                *reinterpret_cast<int*>(bytes + pvzmod::kZombieTypeOffset) = prepared->baseType;
                pvzmod::MarkSpecialPlantHead(zombie, adapter.initializerZombieType);
            } else if (prepared->baseType != adapter.initializerZombieType) {
                pvzmod::MarkNativeArmorBaseType(zombie, prepared->baseType);
            }
        }
        if (*reinterpret_cast<void**>(bytes + pvzmod::kBoardOffset) == nullptr) {
            return;
        }
        const std::shared_ptr<const pvzmod::ZombieConfig> config = pvzmod::g_zombieConfigRuntime.Current();
        if (!config) {
            return;
        }
        const int effectiveZombieType = *reinterpret_cast<int*>(bytes + pvzmod::kZombieTypeOffset);
        const pvzmod::ZombieAttributeOverride* override = config->FindZombie(configZombieType);
        const pvzmod::OriginalArmorHealthRule* originalArmor =
            config->FindOriginalArmorHealth(effectiveZombieType);
        if (override == nullptr && originalArmor == nullptr) {
            return;
        }
        if (override != nullptr && override->bodyHealth.has_value()) {
            *reinterpret_cast<int*>(bytes + pvzmod::kBodyHealthOffset) = *override->bodyHealth;
            *reinterpret_cast<int*>(bytes + pvzmod::kBodyMaxHealthOffset) = *override->bodyHealth;
        }

        if (originalArmor != nullptr && originalArmor->HasEffect()) {
            std::ptrdiff_t currentOffset = pvzmod::kHelmetHealthOffset;
            std::ptrdiff_t maximumOffset = pvzmod::kHelmetMaxHealthOffset;
            if (originalArmor->pool == pvzmod::ArmorHealthPool::Shield) {
                currentOffset = pvzmod::kShieldHealthOffset;
                maximumOffset = pvzmod::kShieldMaxHealthOffset;
            } else if (originalArmor->pool == pvzmod::ArmorHealthPool::Flying) {
                currentOffset = pvzmod::kFlyingHealthOffset;
                maximumOffset = pvzmod::kFlyingMaxHealthOffset;
            }
            static_cast<void>(pvzmod::ApplyOriginalArmorHealthOverride(
                *originalArmor,
                *reinterpret_cast<int*>(bytes + currentOffset),
                *reinterpret_cast<int*>(bytes + maximumOffset)));
        }

        if (override == nullptr) {
            return;
        }

        const std::uint32_t instanceId = *reinterpret_cast<std::uint32_t*>(
            bytes + pvzmod::kZombieInstanceIdOffset);
        const pvzmod::ArmorDefinition* helmet = pvzmod::PickArmorForSlot(
            *config, *override, pvzmod::ArmorSlot::Helmet, configZombieType, instanceId);
        const pvzmod::ArmorDefinition* shield = pvzmod::PickArmorForSlot(
            *config, *override, pvzmod::ArmorSlot::Shield, configZombieType, instanceId);
        const pvzmod::ArmorDefinition* flying = pvzmod::PickArmorForSlot(
            *config, *override, pvzmod::ArmorSlot::Flying, configZombieType, instanceId);

        if (prepared.has_value()) {
            const pvzmod::ArmorDefinition* preparedArmor = config->FindArmor(prepared->armorId);
            const pvzmod::ArmorVisualAdapterInfo& adapter =
                pvzmod::GetArmorVisualAdapterInfo(prepared->visual);
            if (adapter.exclusiveBody) {
                helmet = nullptr;
                shield = nullptr;
                flying = nullptr;
            }
            if (preparedArmor != nullptr) {
                if (preparedArmor->slot == pvzmod::ArmorSlot::Helmet) {
                    helmet = preparedArmor;
                } else if (preparedArmor->slot == pvzmod::ArmorSlot::Shield) {
                    shield = preparedArmor;
                } else {
                    flying = preparedArmor;
                }
            }
        }

        auto rejectUnpreparedInitializer = [&](const pvzmod::ArmorDefinition*& armor) {
            if (armor == nullptr || !pvzmod::RequiresOriginalZombieInitializer(armor->visual) ||
                (prepared.has_value() && prepared->armorId == armor->id)) {
                return;
            }
            pvzmod::g_zombieConfigRuntime.WarnUnsupportedVisualOnce(configZombieType, armor->id);
            armor = nullptr;
        };
        rejectUnpreparedInitializer(helmet);
        rejectUnpreparedInitializer(shield);
        rejectUnpreparedInitializer(flying);

        const bool plainBody = pvzmod::HasPlainZombieBody(effectiveZombieType) ||
            (prepared.has_value() &&
             pvzmod::GetArmorVisualAdapterInfo(prepared->visual).specialPlantHead);
        if (!plainBody) {
            for (const pvzmod::ArmorDefinition** armor : {&helmet, &shield}) {
                if (*armor != nullptr && !pvzmod::RequiresOriginalZombieInitializer((*armor)->visual)) {
                    pvzmod::g_zombieConfigRuntime.WarnUnsupportedVisualOnce(configZombieType, (*armor)->id);
                    *armor = nullptr;
                }
            }
        }

        if (helmet != nullptr) {
            const pvzmod::ArmorVisualAdapterInfo& adapter =
                pvzmod::GetArmorVisualAdapterInfo(helmet->visual);
            if (!pvzmod::UsesIndependentArmorOverlay(helmet->visual) ||
                pvzmod::CreateArmorOverlay(zombie, *helmet)) {
                *reinterpret_cast<int*>(bytes + pvzmod::kHelmetTypeOffset) = adapter.gameArmorType;
                *reinterpret_cast<int*>(bytes + pvzmod::kHelmetHealthOffset) = helmet->health;
                *reinterpret_cast<int*>(bytes + pvzmod::kHelmetMaxHealthOffset) = helmet->health;
                if (helmet->visual == pvzmod::ArmorVisual::Cone ||
                    helmet->visual == pvzmod::ArmorVisual::Bucket) {
                    CallZombieReanimShowPrefix(zombie, "anim_cone", -1);
                    CallZombieReanimShowPrefix(zombie, "anim_bucket", -1);
                    CallZombieReanimShowPrefix(
                        zombie, helmet->visual == pvzmod::ArmorVisual::Cone ? "anim_cone" : "anim_bucket", 0);
                    CallZombieReanimShowPrefix(zombie, "anim_hair", -1);
                }
            }
        }
        if (shield != nullptr) {
            const pvzmod::ArmorVisualAdapterInfo& adapter =
                pvzmod::GetArmorVisualAdapterInfo(shield->visual);
            if (!pvzmod::UsesIndependentArmorOverlay(shield->visual) ||
                pvzmod::CreateArmorOverlay(zombie, *shield)) {
                *reinterpret_cast<int*>(bytes + pvzmod::kShieldTypeOffset) = adapter.gameArmorType;
                *reinterpret_cast<int*>(bytes + pvzmod::kShieldHealthOffset) = shield->health;
                *reinterpret_cast<int*>(bytes + pvzmod::kShieldMaxHealthOffset) = shield->health;
                if (shield->visual == pvzmod::ArmorVisual::Door) {
                    CallZombieAttachShield(zombie);
                }
            }
        }
        if (flying != nullptr) {
            if (!pvzmod::UsesIndependentArmorOverlay(flying->visual) ||
                pvzmod::CreateArmorOverlay(zombie, *flying)) {
                *reinterpret_cast<int*>(bytes + pvzmod::kFlyingHealthOffset) = flying->health;
                *reinterpret_cast<int*>(bytes + pvzmod::kFlyingMaxHealthOffset) = flying->health;
            }
        }
    } catch (const std::exception& exception) {
        pvzmod::LogError(std::string("Applying zombie attributes failed: ") + exception.what());
    } catch (...) {
        pvzmod::LogError("Applying zombie attributes failed with an unknown exception.");
    }
}

extern "C" void __stdcall PrepareWallnutDropHead(void* zombie) {
    if (zombie == nullptr) {
        return;
    }
    const std::optional<int> plantHeadType = pvzmod::FindSpecialPlantHeadType(zombie);
    if (!plantHeadType.has_value()) {
        return;
    }
    auto* bytes = static_cast<std::uint8_t*>(zombie);
    int& zombieType = *reinterpret_cast<int*>(bytes + pvzmod::kZombieTypeOffset);
    pvzmod::g_preparedPlantHeadDrops.push_back({zombie, zombieType});
    // DropHead uses the zombie type to decide whether a special plant head must
    // be destroyed. Temporarily expose the original wall-nut-head type only for
    // this call, then restore the configured base zombie type below.
    zombieType = *plantHeadType;
}

extern "C" void __stdcall FinishWallnutDropHead(void* zombie) {
    if (zombie == nullptr) {
        return;
    }
    for (auto iterator = pvzmod::g_preparedPlantHeadDrops.rbegin();
         iterator != pvzmod::g_preparedPlantHeadDrops.rend(); ++iterator) {
        if (iterator->zombie != zombie) {
            continue;
        }
        auto* bytes = static_cast<std::uint8_t*>(zombie);
        *reinterpret_cast<int*>(bytes + pvzmod::kZombieTypeOffset) = iterator->originalType;
        pvzmod::g_preparedPlantHeadDrops.erase(std::next(iterator).base());
        if (*reinterpret_cast<int*>(bytes + pvzmod::kSpecialHeadReanimIdOffset) ==
            pvzmod::kNullReanimationId) {
            pvzmod::ForgetSpecialPlantHead(zombie);
        }
        return;
    }
}

extern "C" void __declspec(naked) ZombieDropHeadDetour() {
    __asm {
        pushfd
        pushad
        push dword ptr [esp + 40]
        call PrepareWallnutDropHead
        popad
        popfd
        push dword ptr [esp + 8]
        push dword ptr [esp + 8]
        call dword ptr [g_originalZombieDropHead]
        pushfd
        pushad
        push dword ptr [esp + 40]
        call FinishWallnutDropHead
        popad
        popfd
        ret 8
    }
}

extern "C" int __stdcall DropOverlayShield(void* zombie, unsigned int) {
    if (zombie == nullptr || !pvzmod::RemoveArmorOverlay(zombie, pvzmod::ArmorSlot::Shield)) {
        return 0;
    }
    auto* bytes = static_cast<std::uint8_t*>(zombie);
    *reinterpret_cast<int*>(bytes + pvzmod::kShieldTypeOffset) = 0;
    *reinterpret_cast<int*>(bytes + pvzmod::kShieldHealthOffset) = 0;
    return 1;
}

extern "C" int __stdcall DropOverlayHelmet(void* zombie, unsigned int) {
    if (zombie == nullptr || !pvzmod::RemoveArmorOverlay(zombie, pvzmod::ArmorSlot::Helmet)) {
        return 0;
    }
    auto* bytes = static_cast<std::uint8_t*>(zombie);
    *reinterpret_cast<int*>(bytes + pvzmod::kHelmetTypeOffset) = 0;
    *reinterpret_cast<int*>(bytes + pvzmod::kHelmetHealthOffset) = 0;
    return 1;
}

extern "C" int __stdcall DropOverlayFlying(void* zombie, unsigned int) {
    if (zombie == nullptr || !pvzmod::RemoveArmorOverlay(zombie, pvzmod::ArmorSlot::Flying)) {
        return 0;
    }
    auto* bytes = static_cast<std::uint8_t*>(zombie);
    *reinterpret_cast<int*>(bytes + pvzmod::kFlyingHealthOffset) = 0;
    return 1;
}

extern "C" void __declspec(naked) ZombieDropShieldDetour() {
    __asm {
        pushfd
        pushad
        push dword ptr [esp + 44]
        push dword ptr [esp + 44]
        call DropOverlayShield
        test eax, eax
        jz drop_shield_original
        popad
        popfd
        ret 8
    drop_shield_original:
        popad
        popfd
        jmp dword ptr [g_originalZombieDropShield]
    }
}

extern "C" void __declspec(naked) ZombieDropHelmDetour() {
    __asm {
        pushfd
        pushad
        push dword ptr [esp + 40]
        push dword ptr [esp + 32]
        call DropOverlayHelmet
        test eax, eax
        jz drop_helm_original
        popad
        popfd
        ret 4
    drop_helm_original:
        popad
        popfd
        jmp dword ptr [g_originalZombieDropHelm]
    }
}

extern "C" void __declspec(naked) ZombieLandFlyerDetour() {
    __asm {
        pushfd
        pushad
        push dword ptr [esp + 40]
        push dword ptr [esp + 32]
        call DropOverlayFlying
        test eax, eax
        jz land_flyer_original
        popad
        popfd
        ret 4
    land_flyer_original:
        popad
        popfd
        jmp dword ptr [g_originalZombieLandFlyer]
    }
}

extern "C" void __fastcall ZombieEatPlantDetour(void* zombie, void*, void* plant) {
    using OriginalFunction = void(__thiscall*)(void*, void*);
    const auto original = reinterpret_cast<OriginalFunction>(g_originalZombieEatPlant);
    if (zombie == nullptr || plant == nullptr) {
        original(zombie, plant);
        return;
    }
    int* configuredHealth = nullptr;
    int healthBefore = 0;
    int fakeHealthBefore = 0;
    int configuredDamage = pvzmod::kOriginalEatDamage;
    try {
        const std::shared_ptr<const pvzmod::ZombieConfig> config = pvzmod::g_zombieConfigRuntime.Current();
        if (config) {
            const int effectiveZombieType = *reinterpret_cast<const int*>(
                static_cast<const std::uint8_t*>(zombie) + pvzmod::kZombieTypeOffset);
            const int zombieType = pvzmod::ResolveConfigZombieType(zombie, effectiveZombieType);
            const pvzmod::ZombieAttributeOverride* override = config->FindZombie(zombieType);
            if (override != nullptr && override->attackDamage.has_value()) {
                configuredDamage = *override->attackDamage;
            }
        }
        configuredDamage = pvzmod::DispatchZombieAttackDamageModifiers(zombie, configuredDamage);
        if (configuredDamage != pvzmod::kOriginalEatDamage) {
            configuredHealth = reinterpret_cast<int*>(
                static_cast<std::uint8_t*>(plant) + pvzmod::kPlantHealthOffset);
            healthBefore = *configuredHealth;
            const long long adjusted = static_cast<long long>(healthBefore) + pvzmod::kOriginalEatDamage -
                configuredDamage;
            fakeHealthBefore = static_cast<int>(std::clamp<long long>(
                adjusted, std::numeric_limits<int>::min(), std::numeric_limits<int>::max()));
            *configuredHealth = fakeHealthBefore;
        }
    } catch (const std::exception& exception) {
        pvzmod::LogError(std::string("Zombie attack damage lookup failed: ") + exception.what());
    } catch (...) {
        pvzmod::LogError("Zombie attack damage lookup failed with an unknown exception.");
    }
    original(zombie, plant);
    if (configuredHealth != nullptr) {
        const long long originalDamageApplied = static_cast<long long>(fakeHealthBefore) - *configuredHealth;
        if (originalDamageApplied <= 0) {
            *configuredHealth = healthBefore;
            return;
        }
        const long long hitCount = std::max<long long>(1, originalDamageApplied / pvzmod::kOriginalEatDamage);
        const long long desiredHealth = static_cast<long long>(healthBefore) - hitCount * configuredDamage;
        *configuredHealth = static_cast<int>(std::clamp<long long>(
            desiredHealth, std::numeric_limits<int>::min(), std::numeric_limits<int>::max()));
    }
}
