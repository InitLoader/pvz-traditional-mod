#include "audio_replacement_hook.h"

#include "audio_asset_registry.h"
#include "audio_replacement_config.h"
#include "audio_sample_config.h"
#include "game_sound_bridge.h"
#include "hook_utils.h"
#include "logger.h"
#include "original_sound_catalog.h"

#include <algorithm>
#include <array>
#include <cstdint>
#include <filesystem>
#include <mutex>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

namespace pvzmod {
namespace {

using GetSoundInstanceFn = void*(__thiscall*)(void*, unsigned int);

constexpr std::uintptr_t kSoundCatalogReadySentinelRva = 0x002A790C;
constexpr std::uintptr_t kGameAppPlaySampleRva = 0x000560C0;
constexpr std::array<std::uint8_t, 12> kGetSoundInstancePrologue = {
    0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xC0, 0x6A, 0xFF, 0x68, 0xD6, 0xF9, 0x63};

struct PendingReplacement {
    std::string originalSymbol;
    std::uintptr_t globalRva = 0;
    std::string sampleId;
};

std::uint8_t* g_moduleBase = nullptr;
GetSoundInstanceFn g_originalGetSoundInstance = nullptr;
AudioAssetRegistry g_audioAssets;
std::vector<PendingReplacement> g_pendingReplacements;
std::unordered_map<unsigned int, unsigned int> g_engineReplacements;
std::mutex g_loadMutex;
bool g_runtimeReady = false;
thread_local bool g_insideLoader = false;

bool OriginalCatalogIsReady() {
    if (g_moduleBase == nullptr) return false;
    const int sentinel = *reinterpret_cast<const int*>(g_moduleBase + kSoundCatalogReadySentinelRva);
    return sentinel > 0 && sentinel <= 255;
}

void EnsureAudioLoaded(void* soundManager) {
    if (g_runtimeReady || g_insideLoader || !OriginalCatalogIsReady()) return;
    std::lock_guard lock(g_loadMutex);
    if (g_runtimeReady) return;
    g_insideLoader = true;
    const bool allSamplesLoaded = g_audioAssets.LoadAll(g_moduleBase, soundManager);
    if (!allSamplesLoaded) {
        LogWarning("One or more external audio samples were unavailable; their routes remain on original audio.");
    }
    for (const PendingReplacement& replacement : g_pendingReplacements) {
        const int originalId = *reinterpret_cast<const int*>(g_moduleBase + replacement.globalRva);
        const int replacementId = g_audioAssets.FindEngineSampleId(replacement.sampleId);
        if (originalId < 0 || originalId > 255 || replacementId < 0) {
            LogWarning("Audio replacement " + replacement.originalSymbol + " -> " +
                       replacement.sampleId + " is inactive; original audio will be kept.");
            continue;
        }
        g_engineReplacements[static_cast<unsigned int>(originalId)] =
            static_cast<unsigned int>(replacementId);
        LogInfo("Activated audio replacement " + replacement.originalSymbol + " -> " +
                replacement.sampleId + ".");
    }
    g_runtimeReady = true;
    g_insideLoader = false;
    LogInfo("Audio runtime ready: " + std::to_string(g_audioAssets.LoadedCount()) +
            " external sample(s), " + std::to_string(g_engineReplacements.size()) +
            " original replacement(s).");
}

void* __fastcall GetSoundInstanceDetour(void* soundManager, void*, unsigned int sampleId) {
    EnsureAudioLoaded(soundManager);
    if (!g_insideLoader) {
        const auto replacement = g_engineReplacements.find(sampleId);
        if (replacement != g_engineReplacements.end()) sampleId = replacement->second;
    }
    return g_originalGetSoundInstance(soundManager, sampleId);
}

}  // namespace

bool InstallAudioReplacementRuntime(std::uint8_t* moduleBase) {
    g_moduleBase = moduleBase;
    const std::filesystem::path configDirectory = ModuleDirectory() / L"pvzmod" / L"config" / L"audio";
    const AudioSampleConfigLoadResult samples = LoadAudioSampleConfig(configDirectory / L"samples.jsonc");
    if (!samples.Ok()) {
        LogWarning(samples.error + "; external audio is disabled.");
        return true;
    }
    const AudioReplacementConfigLoadResult replacements =
        LoadAudioReplacementConfig(configDirectory / L"replacements.jsonc");
    if (!replacements.Ok()) {
        LogWarning(replacements.error + "; external audio is disabled.");
        return true;
    }

    g_audioAssets.Reset(*samples.config, ModuleDirectory());
    g_pendingReplacements.clear();
    for (const auto& [symbol, replacement] : replacements.config->replacements) {
        if (!replacement.enabled) continue;
        const auto globalRva = FindOriginalSoundGlobalRva(symbol);
        if (!globalRva.has_value()) {
            LogWarning("Unknown original sound symbol '" + symbol + "'; replacement ignored.");
            continue;
        }
        const AudioSampleDefinition* sample = samples.config->Find(replacement.sampleId);
        if (sample == nullptr || !sample->enabled) {
            LogWarning("Audio replacement '" + symbol + "' references missing or disabled sample '" +
                       replacement.sampleId + "'; replacement ignored.");
            continue;
        }
        g_pendingReplacements.push_back({symbol, *globalRva, replacement.sampleId});
    }

    const bool hasEnabledSamples = std::any_of(
        samples.config->samples.begin(), samples.config->samples.end(),
        [](const auto& item) { return item.second.enabled; });
    if (!hasEnabledSamples) {
        LogInfo("Audio config contains no enabled external samples; audio hook was not installed.");
        return true;
    }
    if (!VerifyHookTarget(moduleBase, kDSoundManagerGetSoundInstanceRva,
                          kGetSoundInstancePrologue, "DSoundManager::GetSoundInstance")) {
        return false;
    }
    if (!CreateAndEnableHook(
            moduleBase, kDSoundManagerGetSoundInstanceRva,
            reinterpret_cast<void*>(&GetSoundInstanceDetour),
            reinterpret_cast<void**>(&g_originalGetSoundInstance),
            "DSoundManager::GetSoundInstance")) {
        return false;
    }
    LogInfo("Audio runtime installed; samples will be loaded after the original sound catalog is ready.");
    return true;
}

bool PlayConfiguredAudio(void* gameApp, const std::string_view sampleId) {
    if (gameApp == nullptr || g_moduleBase == nullptr) return false;
    void* soundManager = *reinterpret_cast<void**>(reinterpret_cast<std::uint8_t*>(gameApp) + 0x4B4);
    if (soundManager == nullptr) return false;
    EnsureAudioLoaded(soundManager);
    const int engineId = g_audioAssets.FindEngineSampleId(sampleId);
    if (engineId < 0) return false;
    using PlaySampleFn = void(__thiscall*)(void*, int);
    reinterpret_cast<PlaySampleFn>(g_moduleBase + kGameAppPlaySampleRva)(gameApp, engineId);
    return true;
}

}  // namespace pvzmod
