#include "external_body_animation_runtime.h"

#include "external_animation_runtime.h"
#include "hook_utils.h"
#include "logger.h"

#include <array>
#include <cstdint>
#include <limits>
#include <mutex>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <utility>

extern "C" void* g_originalReanimationDefinitionSyncTrackRead = nullptr;
extern "C" void ReanimationDefinitionSyncNullGuard();
extern "C" pvzmod::RuntimeReanimatorDefinition* __stdcall
ResolveSavedExternalReanimationDefinition(int reanimationType);

namespace pvzmod {
namespace {

constexpr std::uintptr_t kLawnAppReanimationTryToGetRva = 0x00053CB0;
constexpr std::uintptr_t kReanimationDestructorRva = 0x00071A20;
constexpr std::uintptr_t kReanimationInitializeRva = 0x00071B00;
constexpr std::uintptr_t kReanimationSetFramesForLayerRva = 0x00073280;
constexpr std::uintptr_t kReanimationDefinitionSyncTrackReadRva = 0x00081923;
constexpr std::uintptr_t kLawnAppGlobalRva = 0x002A9EC0;

constexpr std::array<std::uint8_t, 8> kTryToGetPrologue =
    {0x85, 0xC9, 0x8B, 0x90, 0x20, 0x08, 0x00, 0x00};
constexpr std::array<std::uint8_t, 8> kDestructorPrologue =
    {0x8B, 0xCE, 0xE8, 0xC9, 0x19, 0x00, 0x00, 0x83};
constexpr std::array<std::uint8_t, 8> kInitializePrologue =
    {0x53, 0x55, 0x8B, 0x6C, 0x24, 0x0C, 0x56, 0x8B};
constexpr std::array<std::uint8_t, 8> kSetFramesPrologue =
    {0xD9, 0xEE, 0xD8, 0x51, 0x08, 0xDF, 0xE0, 0xF6};
constexpr std::array<std::uint8_t, 5> kDefinitionSyncTrackRead =
    {0x8B, 0x0E, 0x8B, 0x79, 0x04};

constexpr std::ptrdiff_t kReanimationRateOffset = 0x08;
constexpr std::ptrdiff_t kReanimationLoopTypeOffset = 0x10;
constexpr std::ptrdiff_t kReanimationLoopCountOffset = 0x5C;

// Exact 1.0.0.1051 ReanimationType values used by playable plant and zombie
// carriers. UI/effect-only definitions are intentionally not accepted as body
// carriers until their object families have a dedicated runtime path.
constexpr std::pair<std::string_view, int> kBodyCarrierTypes[] = {
    {"REANIM_PEASHOOTER", 4}, {"REANIM_WALLNUT", 5}, {"REANIM_LILYPAD", 6},
    {"REANIM_SUNFLOWER", 7}, {"REANIM_CHERRYBOMB", 10}, {"REANIM_SQUASH", 11},
    {"REANIM_DOOMSHROOM", 12}, {"REANIM_SNOWPEA", 13}, {"REANIM_REPEATER", 14},
    {"REANIM_SUNSHROOM", 15}, {"REANIM_TALLNUT", 16}, {"REANIM_FUMESHROOM", 17},
    {"REANIM_PUFFSHROOM", 18}, {"REANIM_HYPNOSHROOM", 19}, {"REANIM_CHOMPER", 20},
    {"REANIM_ZOMBIE", 21}, {"REANIM_POTATOMINE", 23}, {"REANIM_SPIKEWEED", 24},
    {"REANIM_SPIKEROCK", 25}, {"REANIM_THREEPEATER", 26}, {"REANIM_MARIGOLD", 27},
    {"REANIM_ICESHROOM", 28}, {"REANIM_ZOMBIE_FOOTBALL", 29},
    {"REANIM_ZOMBIE_NEWSPAPER", 30}, {"REANIM_ZOMBIE_ZAMBONI", 31},
    {"REANIM_JALAPENO", 33}, {"REANIM_SCRAREYSHROOM", 42}, {"REANIM_PUMPKIN", 43},
    {"REANIM_PLANTERN", 44}, {"REANIM_TORCHWOOD", 45}, {"REANIM_SPLITPEA", 46},
    {"REANIM_SEASHROOM", 47}, {"REANIM_BLOVER", 48}, {"REANIM_FLOWER_POT", 49},
    {"REANIM_CACTUS", 50}, {"REANIM_TANGLEKELP", 51}, {"REANIM_STARFRUIT", 52},
    {"REANIM_POLEVAULTER", 53}, {"REANIM_BALLOON", 54}, {"REANIM_GARGANTUAR", 55},
    {"REANIM_IMP", 56}, {"REANIM_DIGGER", 57}, {"REANIM_ZOMBIE_DOLPHINRIDER", 59},
    {"REANIM_POGO", 60}, {"REANIM_BOBSLED", 61}, {"REANIM_JACKINTHEBOX", 62},
    {"REANIM_SNORKEL", 63}, {"REANIM_BUNGEE", 64}, {"REANIM_CATAPULT", 65},
    {"REANIM_LADDER", 66}, {"REANIM_GRAVE_BUSTER", 69}, {"REANIM_MAGNETSHROOM", 71},
    {"REANIM_BOSS", 72}, {"REANIM_CABBAGEPULT", 73}, {"REANIM_KERNELPULT", 74},
    {"REANIM_MELONPULT", 75}, {"REANIM_COFFEEBEAN", 76}, {"REANIM_UMBRELLALEAF", 77},
    {"REANIM_GATLINGPEA", 78}, {"REANIM_CATTAIL", 79}, {"REANIM_GLOOMSHROOM", 80},
    {"REANIM_COBCANNON", 83}, {"REANIM_GARLIC", 84}, {"REANIM_GOLD_MAGNET", 85},
    {"REANIM_WINTER_MELON", 86}, {"REANIM_TWIN_SUNFLOWER", 87},
    {"REANIM_IMITATER", 91}, {"REANIM_YETI", 92}, {"REANIM_DANCER", 141},
    {"REANIM_BACKUP_DANCER", 142},
};

struct RegisteredDefinition {
    std::string animationId;
    RuntimeReanimatorDefinition* definition = nullptr;
};

std::uint8_t* g_moduleBase = nullptr;
std::mutex g_runtimeMutex;
std::unordered_map<int, RegisteredDefinition> g_definitionsByCarrier;
std::unordered_set<std::string> g_registeredAnimationIds;
std::unordered_set<std::string> g_loggedMessages;

template <typename T>
T& Field(void* object, const std::ptrdiff_t offset) {
    return *reinterpret_cast<T*>(static_cast<std::uint8_t*>(object) + offset);
}

void LogOnce(const std::string& key, const bool warning, const std::string& message) {
    {
        std::lock_guard lock(g_runtimeMutex);
        if (!g_loggedMessages.insert(key).second) return;
    }
    if (warning) LogWarning(message);
    else LogInfo(message);
}

void* ResolveBodyReanimation(
    void* owner, const std::ptrdiff_t lawnAppOffset, const std::ptrdiff_t bodyIdOffset) {
    if (owner == nullptr || g_moduleBase == nullptr) return nullptr;
    void* lawnApp = Field<void*>(owner, lawnAppOffset);
    const std::uint32_t id = Field<std::uint32_t>(owner, bodyIdOffset);
    if (lawnApp == nullptr || id == (std::numeric_limits<std::uint32_t>::max)()) return nullptr;
    void* target = g_moduleBase + kLawnAppReanimationTryToGetRva;
    void* reanimation = nullptr;
    __asm {
        mov eax, lawnApp
        mov ecx, id
        call target
        mov reanimation, eax
    }
    return reanimation;
}

void DestroyReanimationContents(void* reanimation) {
    void* target = g_moduleBase + kReanimationDestructorRva;
    __asm {
        push esi
        mov esi, reanimation
        call target
        pop esi
    }
}

void InitializeReanimation(void* reanimation, RuntimeReanimatorDefinition* definition) {
    void* target = g_moduleBase + kReanimationInitializeRva;
    __asm {
        mov eax, definition
        push 0
        push 0
        push reanimation
        call target
    }
}

void SetFramesForLayer(void* reanimation, const char* track) {
    void* target = g_moduleBase + kReanimationSetFramesForLayerRva;
    __asm {
        mov ecx, reanimation
        push track
        call target
    }
}

int ToGameLoopType(const ExternalAnimationLoopMode loop) {
    switch (loop) {
        case ExternalAnimationLoopMode::Loop: return 0;
        case ExternalAnimationLoopMode::Once: return 2;
        case ExternalAnimationLoopMode::OnceHold: return 3;
    }
    return 0;
}

}  // namespace

int ResolveCarrierReanimationType(const std::string_view symbol) {
    for (const auto& [name, value] : kBodyCarrierTypes) {
        if (name == symbol) return value;
    }
    return -1;
}

bool InitializeExternalBodyAnimationRuntime(std::uint8_t* moduleBase) {
    if (moduleBase == nullptr) return false;
    if (!VerifyHookTarget(moduleBase, kLawnAppReanimationTryToGetRva,
                          kTryToGetPrologue, "LawnApp::ReanimationTryToGet") ||
        !VerifyHookTarget(moduleBase, kReanimationDestructorRva,
                          kDestructorPrologue, "Reanimation::~Reanimation") ||
        !VerifyHookTarget(moduleBase, kReanimationInitializeRva,
                          kInitializePrologue, "Reanimation::Initialize") ||
        !VerifyHookTarget(moduleBase, kReanimationSetFramesForLayerRva,
                          kSetFramesPrologue, "Reanimation::SetFramesForLayer") ||
        !VerifyHookTarget(moduleBase, kReanimationDefinitionSyncTrackReadRva,
                          kDefinitionSyncTrackRead, "Reanimation save-load definition guard")) {
        return false;
    }
    g_moduleBase = moduleBase;
    if (!CreateAndEnableHook(
            moduleBase, kReanimationDefinitionSyncTrackReadRva,
            reinterpret_cast<void*>(&ReanimationDefinitionSyncNullGuard),
            &g_originalReanimationDefinitionSyncTrackRead,
            "Reanimation save-load definition guard")) {
        return false;
    }
    LogInfo("External body Reanimation ABI verified; carrier-aware saved Definition restore is active.");
    return true;
}

bool RegisterExternalBodyAnimation(const std::string_view animationId) {
    if (animationId.empty() || g_moduleBase == nullptr) return false;
    {
        std::lock_guard lock(g_runtimeMutex);
        if (g_registeredAnimationIds.contains(std::string(animationId))) return true;
    }
    const std::shared_ptr<const LoadedExternalAnimation> animation = FindExternalAnimation(animationId);
    if (!animation) {
        LogOnce("missing-registration:" + std::string(animationId), true,
            "External body animation '" + std::string(animationId) + "' is not registered.");
        return false;
    }
    const int carrier = ResolveCarrierReanimationType(animation->config.carrierReanimation);
    if (carrier < 0) {
        LogOnce("unsupported-carrier:" + animation->config.carrierReanimation, true,
            "External animation '" + std::string(animationId) + "' uses unsupported body carrier '" +
            animation->config.carrierReanimation + "'.");
        return false;
    }
    void* lawnApp = *reinterpret_cast<void**>(g_moduleBase + kLawnAppGlobalRva);
    RuntimeReanimatorDefinition* definition =
        PrepareExternalReanimationDefinition(animationId, lawnApp);
    if (definition == nullptr) return false;

    std::lock_guard lock(g_runtimeMutex);
    const auto existing = g_definitionsByCarrier.find(carrier);
    if (existing != g_definitionsByCarrier.end() && existing->second.animationId != animationId) {
        LogWarning("External animation '" + std::string(animationId) + "' was disabled because carrier '" +
                   animation->config.carrierReanimation + "' is already used by '" +
                   existing->second.animationId + "'; saved games require one Definition per carrier.");
        return false;
    }
    g_definitionsByCarrier[carrier] = RegisteredDefinition{std::string(animationId), definition};
    g_registeredAnimationIds.emplace(animationId);
    LogInfo("Registered saved-game body Definition '" + std::string(animationId) + "' for carrier '" +
            animation->config.carrierReanimation + "'.");
    return true;
}

bool ApplyExternalBodyAnimation(
    void* owner,
    const std::ptrdiff_t lawnAppOffset,
    const std::ptrdiff_t bodyReanimationIdOffset,
    const std::string_view animationId,
    const std::string_view ownerLabel) {
    if (owner == nullptr || animationId.empty()) return true;
    if (!RegisterExternalBodyAnimation(animationId)) return false;
    const std::shared_ptr<const LoadedExternalAnimation> animation = FindExternalAnimation(animationId);
    if (!animation) return false;
    const ExternalAnimationActionDefinition* initialAction =
        animation->config.FindAction(animation->config.initialAction);
    if (initialAction == nullptr || animation->raw.FindTrack(initialAction->track) == nullptr) {
        LogOnce("initial:" + std::string(animationId), true,
            "External animation '" + std::string(animationId) +
            "' has no usable initial action; the original body animation was kept.");
        return false;
    }
    void* lawnApp = Field<void*>(owner, lawnAppOffset);
    RuntimeReanimatorDefinition* runtimeDefinition =
        PrepareExternalReanimationDefinition(animationId, lawnApp);
    void* body = ResolveBodyReanimation(owner, lawnAppOffset, bodyReanimationIdOffset);
    if (runtimeDefinition == nullptr || body == nullptr) {
        LogOnce("prepare:" + std::string(animationId), true,
            "External animation '" + std::string(animationId) +
            "' could not prepare its body Reanimation; the original animation was kept.");
        return false;
    }
    const int expectedCarrier = ResolveCarrierReanimationType(animation->config.carrierReanimation);
    const int actualCarrier = Field<int>(body, 0x00);
    if (expectedCarrier < 0 || actualCarrier != expectedCarrier) {
        LogOnce("carrier-mismatch:" + std::string(ownerLabel) + ":" + std::string(animationId), true,
            "External animation '" + std::string(animationId) + "' expects carrier '" +
            animation->config.carrierReanimation + "' but " + std::string(ownerLabel) +
            " owns ReanimationType " + std::to_string(actualCarrier) + "; the original body was kept.");
        return false;
    }

    DestroyReanimationContents(body);
    InitializeReanimation(body, runtimeDefinition);
    SetFramesForLayer(body, initialAction->track.c_str());
    Field<float>(body, kReanimationRateOffset) = static_cast<float>(initialAction->rate);
    Field<int>(body, kReanimationLoopTypeOffset) = ToGameLoopType(initialAction->loop);
    Field<int>(body, kReanimationLoopCountOffset) = 0;
    LogOnce("applied:" + std::string(ownerLabel) + ":" + std::string(animationId), false,
        "Injected external body animation '" + std::string(animationId) + "' into " +
        std::string(ownerLabel) + ".");
    return true;
}

}  // namespace pvzmod

extern "C" pvzmod::RuntimeReanimatorDefinition* __stdcall
ResolveSavedExternalReanimationDefinition(const int reanimationType) {
    std::lock_guard lock(pvzmod::g_runtimeMutex);
    const auto found = pvzmod::g_definitionsByCarrier.find(reanimationType);
    return found == pvzmod::g_definitionsByCarrier.end() ? nullptr : found->second.definition;
}

extern "C" void __declspec(naked) ReanimationDefinitionSyncNullGuard() {
    __asm {
        mov ecx, [esi]
        test ecx, ecx
        jne definition_ready
        pushfd
        push eax
        push edx
        push dword ptr [esi - 0Ch]
        call ResolveSavedExternalReanimationDefinition
        mov ecx, eax
        pop edx
        pop eax
        popfd
        test ecx, ecx
        je definition_ready
        mov [esi], ecx
    definition_ready:
        jmp dword ptr [g_originalReanimationDefinitionSyncTrackRead]
    }
}
