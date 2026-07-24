#pragma once

#include <filesystem>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>

namespace pvzmod {

struct AudioReplacementDefinition {
    std::string originalSound;
    std::string sampleId;
    bool enabled = true;
};

struct AudioReplacementConfig {
    int schemaVersion = 1;
    std::unordered_map<std::string, AudioReplacementDefinition> replacements;

    [[nodiscard]] const AudioReplacementDefinition* Find(std::string_view originalSound) const;
};

struct AudioReplacementConfigLoadResult {
    std::optional<AudioReplacementConfig> config;
    std::string error;

    [[nodiscard]] bool Ok() const { return config.has_value(); }
};

[[nodiscard]] AudioReplacementConfigLoadResult LoadAudioReplacementConfig(
    const std::filesystem::path& path);

}  // namespace pvzmod
