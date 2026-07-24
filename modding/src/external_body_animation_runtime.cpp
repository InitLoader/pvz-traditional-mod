#include "external_body_animation_runtime.h"

#include "external_animation_runtime.h"
#include "hook_utils.h"
#include "logger.h"
#include "reanimation_carrier_catalog.h"
#include "reanimation_track_instance_state.h"

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

struct RegisteredDefinition {
    std::string animationId;
    RuntimeReanimatorDefinition* definition = nullptr;
};

std::uint8_t* g_moduleBase = nullptr;
std::mutex g_runtimeMutex;
std::unordered_map<int, RegisteredDefinition> g_definitionsByCarrier;
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
    return RegisterExternalBodyAnimationForCarrier(animationId, carrier);
}

bool RegisterExternalBodyAnimationForCarrier(
    const std::string_view animationId, const int carrierReanimationType) {
    if (animationId.empty() || g_moduleBase == nullptr) return false;
    if (!IsBodyCarrierReanimationType(carrierReanimationType)) {
        LogOnce("unsupported-carrier-type:" + std::to_string(carrierReanimationType), true,
            "External animation '" + std::string(animationId) +
            "' cannot use unsupported body ReanimationType " +
            std::to_string(carrierReanimationType) + ".");
        return false;
    }
    const std::shared_ptr<const LoadedExternalAnimation> animation = FindExternalAnimation(animationId);
    if (!animation) {
        LogOnce("missing-registration:" + std::string(animationId), true,
            "External body animation '" + std::string(animationId) + "' is not registered.");
        return false;
    }
    {
        std::lock_guard lock(g_runtimeMutex);
        const auto existing = g_definitionsByCarrier.find(carrierReanimationType);
        if (existing != g_definitionsByCarrier.end()) {
            if (existing->second.animationId == animationId) return true;
            LogWarning("External animation '" + std::string(animationId) +
                       "' was disabled because ReanimationType " +
                       std::to_string(carrierReanimationType) + " is already used by '" +
                       existing->second.animationId +
                       "'; saved games require one Definition per carrier.");
            return false;
        }
    }
    void* lawnApp = *reinterpret_cast<void**>(g_moduleBase + kLawnAppGlobalRva);
    RuntimeReanimatorDefinition* definition =
        PrepareExternalReanimationDefinition(animationId, lawnApp);
    if (definition == nullptr) return false;

    std::lock_guard lock(g_runtimeMutex);
    const auto [entry, inserted] = g_definitionsByCarrier.emplace(
        carrierReanimationType,
        RegisteredDefinition{std::string(animationId), definition});
    if (!inserted && entry->second.animationId != animationId) return false;
    entry->second.definition = definition;
    LogInfo("Registered saved-game body Definition '" + std::string(animationId) +
            "' for ReanimationType " + std::to_string(carrierReanimationType) + ".");
    return true;
}

bool ApplyExternalBodyAnimation(
    void* owner,
    const std::ptrdiff_t lawnAppOffset,
    const std::ptrdiff_t bodyReanimationIdOffset,
    const std::string_view animationId,
    const std::string_view ownerLabel) {
    if (owner == nullptr || animationId.empty()) return true;
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
    void* body = ResolveBodyReanimation(owner, lawnAppOffset, bodyReanimationIdOffset);
    if (body == nullptr) {
        LogOnce("prepare:" + std::string(animationId), true,
            "External animation '" + std::string(animationId) +
            "' could not prepare its body Reanimation; the original animation was kept.");
        return false;
    }
    const int expectedCarrier = ResolveCarrierReanimationType(animation->config.carrierReanimation);
    const int actualCarrier = Field<int>(body, 0x00);
    if (expectedCarrier != actualCarrier) {
        LogOnce("carrier-mismatch:" + std::string(ownerLabel) + ":" + std::string(animationId), true,
            "External animation '" + std::string(animationId) + "' declares carrier '" +
            animation->config.carrierReanimation + "', but " + std::string(ownerLabel) +
            " owns ReanimationType " + std::to_string(actualCarrier) +
            "; the actual template carrier is used for save compatibility.");
    }
    if (!RegisterExternalBodyAnimationForCarrier(animationId, actualCarrier)) return false;
    void* lawnApp = Field<void*>(owner, lawnAppOffset);
    RuntimeReanimatorDefinition* runtimeDefinition =
        PrepareExternalReanimationDefinition(animationId, lawnApp);
    if (runtimeDefinition == nullptr) {
        LogOnce("prepare-definition:" + std::string(animationId), true,
            "External animation '" + std::string(animationId) +
            "' could not prepare its Definition; the original body was kept.");
        return false;
    }

    const ReanimationTrackInstanceStateMap previousTrackState =
        CaptureReanimationTrackInstanceState(body);
    DestroyReanimationContents(body);
    InitializeReanimation(body, runtimeDefinition);
    RestoreReanimationTrackInstanceState(body, previousTrackState);
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
    pvzmod::RuntimeReanimatorDefinition* definition = nullptr;
    {
        std::lock_guard lock(pvzmod::g_runtimeMutex);
        const auto found = pvzmod::g_definitionsByCarrier.find(reanimationType);
        if (found != pvzmod::g_definitionsByCarrier.end()) definition = found->second.definition;
    }
    if (definition == nullptr) {
        pvzmod::LogOnce(
            "missing-saved-definition:" + std::to_string(reanimationType),
            true,
            "Saved Reanimation has a null Definition and no external mapping for ReanimationType " +
                std::to_string(reanimationType) + ".");
    }
    return definition;
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
