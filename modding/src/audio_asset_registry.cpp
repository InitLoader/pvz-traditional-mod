#include "audio_asset_registry.h"

#include "game_sound_bridge.h"
#include "logger.h"

#include <filesystem>

namespace pvzmod {

void AudioAssetRegistry::Reset(AudioSampleConfig config, std::filesystem::path gameDirectory) {
    config_ = std::move(config);
    gameDirectory_ = std::move(gameDirectory);
    engineIds_.clear();
}

bool AudioAssetRegistry::LoadAll(std::uint8_t* moduleBase, void* soundManager) {
    bool success = true;
    for (const auto& [id, sample] : config_.samples) {
        if (!sample.enabled) continue;
        if (engineIds_.contains(id)) continue;
        const std::filesystem::path absolutePath = gameDirectory_ / std::filesystem::u8path(sample.path);
        std::error_code fileError;
        if (!std::filesystem::is_regular_file(absolutePath, fileError)) {
            LogWarning("External audio sample '" + id + "' is missing: " + absolutePath.string() +
                       "; original audio will be kept.");
            success = false;
            continue;
        }
        const int engineId = LoadGameSample(moduleBase, soundManager, sample.path);
        if (engineId < 0 || engineId > 255) {
            LogWarning("Original SoundManager could not load external audio sample '" + id +
                       "'; original audio will be kept.");
            success = false;
            continue;
        }
        engineIds_.emplace(id, engineId);
        LogInfo("Registered external audio sample '" + id + "' as engine sample " +
                std::to_string(engineId) + ".");
    }
    return success;
}

int AudioAssetRegistry::FindEngineSampleId(const std::string_view id) const {
    const auto found = engineIds_.find(NormalizeAudioId(id));
    return found == engineIds_.end() ? -1 : found->second;
}

}  // namespace pvzmod
