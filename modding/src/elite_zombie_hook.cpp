#include "hook_modules.h"

#include "elite_skill_registry.h"
#include "elite_zombie_config.h"
#include "external_texture_runtime.h"
#include "hook_utils.h"
#include "logger.h"
#include "zombie_event_bus.h"

#include <Windows.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <limits>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <unordered_map>
#include <unordered_set>

extern "C" void* g_originalEliteZombieUpdate = nullptr;
extern "C" void* g_originalEliteZombieDraw = nullptr;
extern "C" void* g_originalEliteZombieDelete = nullptr;

extern "C" void EliteZombieUpdateDetour();
extern "C" void EliteZombieDrawDetour();
extern "C" void __fastcall EliteZombieDeleteDetour(void* zombie, void* unusedEdx);
extern "C" void __stdcall EliteZombieUpdateBridge(void* zombie);
extern "C" void __stdcall EliteZombieDrawBridge(void* zombie, void* graphics);

namespace pvzmod {
namespace {

constexpr std::uintptr_t kZombieUpdateRva = 0x0012AE60;
constexpr std::uintptr_t kZombieDrawRva = 0x0012E2E0;
constexpr std::uintptr_t kZombieDeleteRva = 0x001302F0;
constexpr std::uintptr_t kLawnAppReanimationTryToGetRva = 0x00053CB0;
constexpr std::uintptr_t kReanimationTrackExistsRva = 0x000732C0;
constexpr std::uintptr_t kReanimationSetImageOverrideRva = 0x00073490;

constexpr std::array<std::uint8_t, 12> kZombieUpdatePrologue = {
    0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8, 0x51, 0x53, 0x56, 0x8B, 0xF0, 0x83};
constexpr std::array<std::uint8_t, 12> kZombieDrawPrologue = {
    0x64, 0xA1, 0x00, 0x00, 0x00, 0x00, 0x6A, 0xFF, 0x68, 0x38, 0xF0, 0x64};
constexpr std::array<std::uint8_t, 12> kZombieDeletePrologue = {
    0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8, 0x51, 0x56, 0x8B, 0xF1, 0xE8, 0x11};

constexpr std::ptrdiff_t kAppOffset = 0x00;
constexpr std::ptrdiff_t kBoardOffset = 0x04;
constexpr std::ptrdiff_t kZombieTypeOffset = 0x24;
constexpr std::ptrdiff_t kVelocityXOffset = 0x34;
constexpr std::ptrdiff_t kBodyHealthOffset = 0xC8;
constexpr std::ptrdiff_t kBodyMaxHealthOffset = 0xCC;
constexpr std::ptrdiff_t kBodyReanimationIdOffset = 0x118;
constexpr std::ptrdiff_t kSpecialReanimationIdOffset = 0x144;
constexpr std::ptrdiff_t kZombieInstanceIdOffset = 0x158;
constexpr std::ptrdiff_t kGraphicsColorOffset = 0x30;
constexpr std::ptrdiff_t kGraphicsColorizeOffset = 0x48;

struct GameColor {
    int red;
    int green;
    int blue;
    int alpha;
};

struct EliteZombieState {
    std::uint32_t instanceId = 0;
    const EliteZombieDefinition* definition = nullptr;
    double attackMultiplier = 1.0;
};

std::uint8_t* g_eliteModuleBase = nullptr;
std::shared_ptr<const EliteZombieConfig> g_eliteConfig;
std::mutex g_eliteMutex;
std::unordered_map<void*, EliteZombieState> g_eliteStates;
std::mutex g_visualLogMutex;
std::unordered_set<std::string> g_visualMessages;

template <typename T>
T& Field(void* object, const std::ptrdiff_t offset) {
    return *reinterpret_cast<T*>(static_cast<std::uint8_t*>(object) + offset);
}

int ScaleHealth(const int value, const double multiplier) {
    const double scaled = std::round(static_cast<double>(value) * multiplier);
    return static_cast<int>(std::clamp(
        scaled, 1.0, static_cast<double>(std::numeric_limits<int>::max())));
}

std::optional<EliteZombieState> FindEliteState(void* zombie) {
    if (zombie == nullptr) return std::nullopt;
    const std::uint32_t instanceId = Field<std::uint32_t>(zombie, kZombieInstanceIdOffset);
    std::lock_guard lock(g_eliteMutex);
    const auto found = g_eliteStates.find(zombie);
    if (found == g_eliteStates.end()) return std::nullopt;
    if (found->second.instanceId != instanceId) {
        g_eliteStates.erase(found);
        return std::nullopt;
    }
    return found->second;
}

void DispatchStateSkills(
    void* zombie, const EliteZombieState& state, const EliteSkillEvent event,
    EliteSkillContext* sharedContext = nullptr) {
    if (state.definition == nullptr) return;
    EliteSkillContext localContext;
    EliteSkillContext& context = sharedContext == nullptr ? localContext : *sharedContext;
    context.zombie = zombie;
    context.elite = state.definition;
    for (const EliteSkillBinding& binding : state.definition->skills) {
        context.binding = &binding;
        static_cast<void>(DispatchEliteSkill(binding.id, event, context));
    }
    context.binding = nullptr;
}

void OnEliteZombieInitialized(void* zombie) {
    if (zombie == nullptr || g_eliteConfig == nullptr || Field<void*>(zombie, kBoardOffset) == nullptr) return;
    const int zombieType = Field<int>(zombie, kZombieTypeOffset);
    const std::uint32_t instanceId = Field<std::uint32_t>(zombie, kZombieInstanceIdOffset);
    {
        std::lock_guard lock(g_eliteMutex);
        g_eliteStates.erase(zombie);
    }
    const EliteZombieDefinition* definition = PickEliteZombie(*g_eliteConfig, zombieType, instanceId);
    if (definition == nullptr) return;

    EliteSkillContext spawnContext;
    spawnContext.zombie = zombie;
    spawnContext.elite = definition;
    EliteZombieState state{instanceId, definition, 1.0};
    DispatchStateSkills(zombie, state, EliteSkillEvent::Spawn, &spawnContext);
    spawnContext.healthMultiplier = std::clamp(spawnContext.healthMultiplier, 0.1, 100.0);
    spawnContext.speedMultiplier = std::clamp(spawnContext.speedMultiplier, 0.1, 20.0);
    spawnContext.attackMultiplier = std::clamp(spawnContext.attackMultiplier, 0.0, 100.0);

    int& bodyHealth = Field<int>(zombie, kBodyHealthOffset);
    int& bodyMaxHealth = Field<int>(zombie, kBodyMaxHealthOffset);
    bodyHealth = ScaleHealth(bodyHealth, spawnContext.healthMultiplier);
    bodyMaxHealth = ScaleHealth(bodyMaxHealth, spawnContext.healthMultiplier);
    Field<float>(zombie, kVelocityXOffset) *= static_cast<float>(spawnContext.speedMultiplier);
    state.attackMultiplier = spawnContext.attackMultiplier;
    {
        std::lock_guard lock(g_eliteMutex);
        g_eliteStates[zombie] = state;
    }
}

int ModifyEliteZombieAttackDamage(void* zombie, const int currentDamage) {
    const std::optional<EliteZombieState> state = FindEliteState(zombie);
    if (!state.has_value()) return currentDamage;
    EliteSkillContext context;
    context.attackDamage = currentDamage;
    DispatchStateSkills(zombie, *state, EliteSkillEvent::BeforeAttack, &context);
    const double scaled = std::round(
        static_cast<double>(context.attackDamage) * state->attackMultiplier);
    return static_cast<int>(std::clamp(
        scaled, 0.0, static_cast<double>(std::numeric_limits<int>::max())));
}

void RemoveEliteZombieState(void* zombie) {
    const std::optional<EliteZombieState> state = FindEliteState(zombie);
    if (state.has_value()) DispatchStateSkills(zombie, *state, EliteSkillEvent::Remove);
    std::lock_guard lock(g_eliteMutex);
    g_eliteStates.erase(zombie);
}

void CallOriginalZombieUpdate(void* zombie) {
    void* target = g_originalEliteZombieUpdate;
    __asm {
        mov eax, zombie
        call target
    }
}

void CallOriginalZombieDraw(void* zombie, void* graphics) {
    void* target = g_originalEliteZombieDraw;
    __asm {
        mov ebx, zombie
        push graphics
        call target
    }
}

void LogVisualMessageOnce(const std::string& key, const bool warning, const std::string& message) {
    {
        std::lock_guard lock(g_visualLogMutex);
        if (!g_visualMessages.insert(key).second) return;
    }
    if (warning) LogWarning(message);
    else LogInfo(message);
}

void* ResolveZombieReanimation(void* zombie, const EliteTextureScope scope) {
    if (zombie == nullptr || g_eliteModuleBase == nullptr) return nullptr;
    void* lawnApp = Field<void*>(zombie, kAppOffset);
    if (lawnApp == nullptr) return nullptr;
    const std::ptrdiff_t idOffset = scope == EliteTextureScope::Body ?
        kBodyReanimationIdOffset : kSpecialReanimationIdOffset;
    const std::uint32_t reanimationId = Field<std::uint32_t>(zombie, idOffset);
    if (reanimationId == std::numeric_limits<std::uint32_t>::max()) return nullptr;
    // 1.0.0.1051 is optimized with a nonstandard internal convention:
    // EAX=LawnApp*, ECX=ReanimationID, no stack arguments.
    void* target = g_eliteModuleBase + kLawnAppReanimationTryToGetRva;
    void* reanimation = nullptr;
    __asm {
        mov eax, lawnApp
        mov ecx, reanimationId
        call target
        mov reanimation, eax
    }
    return reanimation;
}

bool ReanimationTrackExists(void* reanimation, const char* track) {
    // Reanimation::TrackExists expects EBX=this and one callee-cleaned stack argument.
    void* target = g_eliteModuleBase + kReanimationTrackExistsRva;
    int exists = 0;
    __asm {
        push ebx
        mov ebx, reanimation
        push track
        call target
        movzx eax, al
        mov exists, eax
        pop ebx
    }
    return exists != 0;
}

void SetReanimationImageOverride(void* reanimation, const char* track, void* image) {
    // Reanimation::SetImageOverride expects ECX=this, EAX=track and one
    // callee-cleaned Image* stack argument.
    void* target = g_eliteModuleBase + kReanimationSetImageOverrideRva;
    __asm {
        mov ecx, reanimation
        mov eax, track
        push image
        call target
    }
}

void ApplyEliteTrackReplacements(void* zombie, const EliteZombieState& state) {
    if (zombie == nullptr || state.definition == nullptr) return;
    void* lawnApp = Field<void*>(zombie, kAppOffset);
    for (const EliteTrackReplacement& replacement : state.definition->visual.replacements) {
        const std::string scopeName = replacement.scope == EliteTextureScope::Body ? "body" : "special";
        const std::string messageKey = state.definition->id + ":" + scopeName + ":" + replacement.track;
        void* image = ResolveExternalTexture(replacement.textureId, lawnApp);
        if (image == nullptr) continue;
        void* reanimation = ResolveZombieReanimation(zombie, replacement.scope);
        if (reanimation == nullptr) {
            LogVisualMessageOnce("missing-reanim:" + messageKey, true,
                "Elite '" + state.definition->id + "' cannot replace " + scopeName + " track '" +
                replacement.track + "': the reanimation is not available for this zombie type.");
            continue;
        }
        if (!ReanimationTrackExists(reanimation, replacement.track.c_str())) {
            LogVisualMessageOnce("missing-track:" + messageKey, true,
                "Elite '" + state.definition->id + "' cannot find " + scopeName + " track '" +
                replacement.track + "'; this replacement was skipped.");
            continue;
        }
        SetReanimationImageOverride(reanimation, replacement.track.c_str(), image);
        LogVisualMessageOnce("applied:" + messageKey, false,
            "Elite '" + state.definition->id + "' replaced " + scopeName + " track '" +
            replacement.track + "' with texture '" + replacement.textureId + "'.");
    }
}

}  // namespace

bool InstallEliteZombieHooks(std::uint8_t* moduleBase) {
    if (moduleBase == nullptr) return false;
    InitializeBuiltinEliteSkills();
    const EliteZombieConfigLoadResult loaded = LoadEliteZombieConfig(
        ModuleDirectory() / L"pvzmod" / L"config" / L"elites" / L"zombies.jsonc");
    if (!loaded.Ok()) {
        LogWarning(loaded.error + "; no elite zombies will spawn.");
        g_eliteConfig = std::make_shared<EliteZombieConfig>();
    } else {
        std::string skillError;
        if (!ValidateEliteSkillBindings(*loaded.config, skillError)) {
            LogError(skillError + " No elite zombies will spawn.");
            g_eliteConfig = std::make_shared<EliteZombieConfig>();
        } else {
            g_eliteConfig = std::make_shared<EliteZombieConfig>(*loaded.config);
            for (const EliteZombieDefinition& elite : g_eliteConfig->elites) {
                for (const EliteTrackReplacement& replacement : elite.visual.replacements) {
                    if (!ExternalTextureIsRegistered(replacement.textureId)) {
                        LogWarning("Elite '" + elite.id + "' references unregistered texture '" +
                                   replacement.textureId + "'; track '" + replacement.track +
                                   "' will keep its original image.");
                    }
                }
            }
            LogInfo("Loaded " + std::to_string(g_eliteConfig->elites.size()) +
                    " elite zombie definition(s).");
        }
    }
    if (!VerifyHookTarget(moduleBase, kZombieUpdateRva, kZombieUpdatePrologue, "Zombie::Update") ||
        !VerifyHookTarget(moduleBase, kZombieDrawRva, kZombieDrawPrologue, "Zombie::Draw") ||
        !VerifyHookTarget(moduleBase, kZombieDeleteRva, kZombieDeletePrologue, "Zombie::Delete")) {
        return false;
    }
    g_eliteModuleBase = moduleBase;
    if (!RegisterZombieInitializedListener(&OnEliteZombieInitialized) ||
        !RegisterZombieAttackDamageModifier(&ModifyEliteZombieAttackDamage)) {
        LogError("Elite zombie lifecycle listeners were already registered.");
        return false;
    }
    if (!CreateAndEnableHook(moduleBase, kZombieUpdateRva, reinterpret_cast<void*>(&EliteZombieUpdateDetour),
                             &g_originalEliteZombieUpdate, "Zombie::Update") ||
        !CreateAndEnableHook(moduleBase, kZombieDrawRva, reinterpret_cast<void*>(&EliteZombieDrawDetour),
                             &g_originalEliteZombieDraw, "Zombie::Draw") ||
        !CreateAndEnableHook(moduleBase, kZombieDeleteRva, reinterpret_cast<void*>(&EliteZombieDeleteDetour),
                             &g_originalEliteZombieDelete, "Zombie::Delete")) {
        return false;
    }
    LogInfo("Installed elite zombie lifecycle, skill, tint, and reanimation track-replacement hooks.");
    return true;
}

}  // namespace pvzmod

extern "C" void __stdcall EliteZombieUpdateBridge(void* zombie) {
    const std::optional<pvzmod::EliteZombieState> state = pvzmod::FindEliteState(zombie);
    if (state.has_value()) pvzmod::DispatchStateSkills(zombie, *state, pvzmod::EliteSkillEvent::BeforeUpdate);
    pvzmod::CallOriginalZombieUpdate(zombie);
    if (state.has_value()) pvzmod::DispatchStateSkills(zombie, *state, pvzmod::EliteSkillEvent::AfterUpdate);
}

extern "C" void __declspec(naked) EliteZombieUpdateDetour() {
    __asm {
        push eax
        call EliteZombieUpdateBridge
        ret
    }
}

extern "C" void __stdcall EliteZombieDrawBridge(void* zombie, void* graphics) {
    const std::optional<pvzmod::EliteZombieState> state = pvzmod::FindEliteState(zombie);
    if (!state.has_value() || state->definition == nullptr) {
        pvzmod::CallOriginalZombieDraw(zombie, graphics);
        return;
    }
    pvzmod::DispatchStateSkills(zombie, *state, pvzmod::EliteSkillEvent::BeforeDraw);
    pvzmod::ApplyEliteTrackReplacements(zombie, *state);
    pvzmod::GameColor previousColor{};
    bool previousColorize = false;
    const bool tinted = state->definition->visual.tint.has_value() && graphics != nullptr;
    if (tinted) {
        previousColor = pvzmod::Field<pvzmod::GameColor>(graphics, pvzmod::kGraphicsColorOffset);
        previousColorize = pvzmod::Field<std::uint8_t>(graphics, pvzmod::kGraphicsColorizeOffset) != 0;
        const pvzmod::EliteTint& tint = *state->definition->visual.tint;
        pvzmod::Field<pvzmod::GameColor>(graphics, pvzmod::kGraphicsColorOffset) =
            {tint.red, tint.green, tint.blue, tint.alpha};
        pvzmod::Field<std::uint8_t>(graphics, pvzmod::kGraphicsColorizeOffset) = 1;
    }
    pvzmod::CallOriginalZombieDraw(zombie, graphics);
    if (tinted) {
        pvzmod::Field<pvzmod::GameColor>(graphics, pvzmod::kGraphicsColorOffset) = previousColor;
        pvzmod::Field<std::uint8_t>(graphics, pvzmod::kGraphicsColorizeOffset) =
            previousColorize ? 1 : 0;
    }
    pvzmod::DispatchStateSkills(zombie, *state, pvzmod::EliteSkillEvent::AfterDraw);
}

extern "C" void __declspec(naked) EliteZombieDrawDetour() {
    __asm {
        push dword ptr [esp + 4]
        push ebx
        call EliteZombieDrawBridge
        ret 4
    }
}

extern "C" void __fastcall EliteZombieDeleteDetour(void* zombie, void*) {
    pvzmod::RemoveEliteZombieState(zombie);
    using OriginalFunction = void(__thiscall*)(void*);
    reinterpret_cast<OriginalFunction>(g_originalEliteZombieDelete)(zombie);
}
