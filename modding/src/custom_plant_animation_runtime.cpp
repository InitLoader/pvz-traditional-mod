#include "custom_plant_animation_runtime.h"

#include "external_animation_runtime.h"
#include "hook_utils.h"
#include "logger.h"
#include "plant_catalog_runtime.h"

#include <array>
#include <cstdint>
#include <limits>
#include <mutex>
#include <string>
#include <unordered_set>

extern "C" void* g_originalReanimationDefinitionSyncTrackRead = nullptr;
extern "C" pvzmod::RuntimeReanimatorDefinition* g_savedExternalReanimationDefinition = nullptr;
extern "C" void ReanimationDefinitionSyncNullGuard();

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

constexpr std::ptrdiff_t kPlantLawnAppOffset = 0x00;
constexpr std::ptrdiff_t kPlantBodyReanimationIdOffset = 0x94;
constexpr std::ptrdiff_t kReanimationRateOffset = 0x08;
constexpr std::ptrdiff_t kReanimationLoopTypeOffset = 0x10;
constexpr std::ptrdiff_t kReanimationLoopCountOffset = 0x5C;

std::uint8_t* g_moduleBase = nullptr;
std::string g_saveRestoreAnimationId;
bool g_saveRestoreAnimationIsAmbiguous = false;
std::mutex g_logMutex;
std::unordered_set<std::string> g_loggedMessages;

template <typename T>
T& Field(void* object, const std::ptrdiff_t offset) {
    return *reinterpret_cast<T*>(static_cast<std::uint8_t*>(object) + offset);
}

void LogOnce(const std::string& key, const bool warning, const std::string& message) {
    {
        std::lock_guard lock(g_logMutex);
        if (!g_loggedMessages.insert(key).second) return;
    }
    if (warning) LogWarning(message);
    else LogInfo(message);
}

void* ResolveBodyReanimation(void* plant) {
    if (plant == nullptr || g_moduleBase == nullptr) return nullptr;
    void* lawnApp = Field<void*>(plant, kPlantLawnAppOffset);
    const std::uint32_t id = Field<std::uint32_t>(plant, kPlantBodyReanimationIdOffset);
    if (lawnApp == nullptr || id == (std::numeric_limits<std::uint32_t>::max)()) return nullptr;

    void* target = g_moduleBase + kLawnAppReanimationTryToGetRva;
    void* reanimation = nullptr;
    // PvZ 1.0.0.1051: EAX=LawnApp*, ECX=ReanimationID.
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
    // The original destructor uses ESI=this and releases the old 0x60-byte
    // TrackInstance array without freeing the holder object itself.
    __asm {
        push esi
        mov esi, reanimation
        call target
        pop esi
    }
}

void InitializeReanimation(void* reanimation, RuntimeReanimatorDefinition* definition) {
    void* target = g_moduleBase + kReanimationInitializeRva;
    // EAX=Definition; stack arguments are (this, x, y), callee pops 12 bytes.
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
    // ECX=this; one callee-cleaned const char* argument.
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

std::string FindUniqueSavedAnimationId() {
    std::string result;
    g_saveRestoreAnimationIsAmbiguous = false;
    for (int index = 0; index < CustomChooserPlantCount(); ++index) {
        const CustomPlantDefinition* plant = CustomPlantAt(index);
        if (plant == nullptr || plant->animationId.empty()) continue;
        if (result.empty()) {
            result = plant->animationId;
            continue;
        }
        if (result != plant->animationId) {
            g_saveRestoreAnimationIsAmbiguous = true;
            return {};
        }
    }
    return result;
}

}  // namespace

bool InitializeCustomPlantAnimationRuntime(std::uint8_t* moduleBase) {
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
    g_saveRestoreAnimationId = FindUniqueSavedAnimationId();
    void* lawnApp = *reinterpret_cast<void**>(moduleBase + kLawnAppGlobalRva);
    g_savedExternalReanimationDefinition = g_saveRestoreAnimationId.empty()
        ? nullptr
        : PrepareExternalReanimationDefinition(g_saveRestoreAnimationId, lawnApp);
    if (g_savedExternalReanimationDefinition == nullptr) {
        if (g_saveRestoreAnimationIsAmbiguous) {
            LogWarning("External body animation injection is disabled because saved games cannot uniquely identify "
                       "more than one custom animation. Configure at most one runtime animation until versioned "
                       "save metadata is implemented.");
        } else if (!g_saveRestoreAnimationId.empty()) {
            LogWarning("External body animation injection is disabled because save-restore Definition '" +
                       g_saveRestoreAnimationId + "' could not be prepared.");
        }
        LogInfo("Custom plant body Reanimation ABI verified; unsafe global action hooks are disabled.");
        return true;
    }
    if (!CreateAndEnableHook(
            moduleBase, kReanimationDefinitionSyncTrackReadRva,
            reinterpret_cast<void*>(&ReanimationDefinitionSyncNullGuard),
            &g_originalReanimationDefinitionSyncTrackRead,
            "Reanimation save-load definition guard")) {
        return false;
    }
    LogInfo("Custom plant body Reanimation injection ABI verified; unsafe global action hooks are disabled.");
    LogInfo("Installed saved-Reanimation Definition restore guard for external animation '" +
            g_saveRestoreAnimationId + "'.");
    return true;
}

bool ApplyCustomPlantAnimation(void* plant, const CustomPlantDefinition& definition) {
    if (plant == nullptr || definition.animationId.empty()) return true;
    if (g_savedExternalReanimationDefinition == nullptr ||
        definition.animationId != g_saveRestoreAnimationId) {
        LogOnce("save-unsafe:" + definition.animationId, true,
            "External animation '" + definition.animationId +
            "' was not injected because it has no unambiguous saved-game restore Definition.");
        return false;
    }
    const std::shared_ptr<const LoadedExternalAnimation> animation =
        FindExternalAnimation(definition.animationId);
    if (!animation) {
        LogOnce("missing:" + definition.animationId, true,
            "Custom plant " + std::to_string(definition.id) + " references unavailable animationId '" +
            definition.animationId + "'; keeping template body animation.");
        return false;
    }

    const ExternalAnimationActionDefinition* initialAction =
        animation->config.FindAction(animation->config.initialAction);
    if (initialAction == nullptr || animation->raw.FindTrack(initialAction->track) == nullptr) {
        LogOnce("initial:" + definition.animationId, true,
            "External animation '" + definition.animationId +
            "' has no usable configured initial action; keeping template body animation.");
        return false;
    }

    void* lawnApp = Field<void*>(plant, kPlantLawnAppOffset);
    RuntimeReanimatorDefinition* runtimeDefinition =
        PrepareExternalReanimationDefinition(definition.animationId, lawnApp);
    void* body = ResolveBodyReanimation(plant);
    if (runtimeDefinition == nullptr || body == nullptr) {
        LogOnce("prepare:" + definition.animationId, true,
            "External animation '" + definition.animationId +
            "' could not prepare its body Reanimation; keeping template animation.");
        return false;
    }

    // All fallible validation and texture loading happens above. Once the old
    // track instances are released, initialization uses only persistent cached
    // storage owned by external_animation_runtime.
    DestroyReanimationContents(body);
    InitializeReanimation(body, runtimeDefinition);
    SetFramesForLayer(body, initialAction->track.c_str());
    Field<float>(body, kReanimationRateOffset) = static_cast<float>(initialAction->rate);
    Field<int>(body, kReanimationLoopTypeOffset) = ToGameLoopType(initialAction->loop);
    Field<int>(body, kReanimationLoopCountOffset) = 0;

    LogOnce("applied:" + definition.animationId, false,
        "Injected external body animation '" + definition.animationId + "' into custom plant " +
        std::to_string(definition.id) + "; configured initial action is active.");
    return true;
}

}  // namespace pvzmod

extern "C" void __declspec(naked) ReanimationDefinitionSyncNullGuard() {
    __asm {
        mov ecx, [esi]
        test ecx, ecx
        jne definition_ready
        mov ecx, dword ptr [g_savedExternalReanimationDefinition]
        mov [esi], ecx
    definition_ready:
        jmp dword ptr [g_originalReanimationDefinitionSyncTrackRead]
    }
}
