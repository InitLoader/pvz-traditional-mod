#include "audio_replacement_config.h"

#include "audio_sample_config.h"

#include <fstream>
#include <stdexcept>

#include <nlohmann/json.hpp>

namespace pvzmod {

const AudioReplacementDefinition* AudioReplacementConfig::Find(const std::string_view originalSound) const {
    const auto found = replacements.find(NormalizeAudioId(originalSound));
    return found == replacements.end() ? nullptr : &found->second;
}

AudioReplacementConfigLoadResult LoadAudioReplacementConfig(const std::filesystem::path& path) {
    AudioReplacementConfigLoadResult result;
    std::ifstream input(path, std::ios::binary);
    if (!input) {
        result.error = "Cannot open audio replacement config: " + path.string();
        return result;
    }
    try {
        const nlohmann::json root = nlohmann::json::parse(input, nullptr, true, true);
        if (!root.is_object()) throw std::runtime_error("root must be an object");
        AudioReplacementConfig parsed;
        parsed.schemaVersion = root.value("schemaVersion", 1);
        if (parsed.schemaVersion != 1) throw std::runtime_error("unsupported schemaVersion; expected 1");
        const auto replacements = root.find("replaceOriginal");
        if (replacements == root.end() || !replacements->is_object()) {
            throw std::runtime_error("replaceOriginal must be an object");
        }
        if (replacements->size() > 167) throw std::runtime_error("at most 167 replacements are supported");
        for (const auto& [rawOriginal, value] : replacements->items()) {
            const std::string original = NormalizeAudioId(rawOriginal);
            if (!IsAudioAssetId(original) || !original.starts_with("SOUND_")) {
                throw std::runtime_error("replacement key must be a SOUND_* symbol");
            }
            if (!value.is_object()) throw std::runtime_error("replacement '" + rawOriginal + "' must be an object");
            const auto sampleValue = value.find("sampleId");
            if (sampleValue == value.end() || !sampleValue->is_string()) {
                throw std::runtime_error("replacement '" + rawOriginal + "' sampleId must be a string");
            }
            AudioReplacementDefinition replacement;
            replacement.originalSound = original;
            replacement.sampleId = NormalizeAudioId(sampleValue->get<std::string>());
            replacement.enabled = value.value("enabled", true);
            if (!IsAudioAssetId(replacement.sampleId) || replacement.sampleId.starts_with("SOUND_")) {
                throw std::runtime_error("replacement sampleId must name an external sample without SOUND_ prefix");
            }
            if (!parsed.replacements.emplace(original, std::move(replacement)).second) {
                throw std::runtime_error("replacement SOUND_* keys are case-insensitive and must be unique");
            }
        }
        result.config = std::move(parsed);
    } catch (const std::exception& exception) {
        result.error = std::string("Audio replacement config validation failed: ") + exception.what();
    }
    return result;
}

}  // namespace pvzmod
