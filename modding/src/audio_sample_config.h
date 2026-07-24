#pragma once

#include <filesystem>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>

namespace pvzmod {

struct AudioSampleDefinition {
    std::string id;
    std::string path;
    bool enabled = true;
    bool preload = true;
};

struct AudioSampleConfig {
    int schemaVersion = 1;
    std::unordered_map<std::string, AudioSampleDefinition> samples;

    [[nodiscard]] const AudioSampleDefinition* Find(std::string_view id) const;
};

struct AudioSampleConfigLoadResult {
    std::optional<AudioSampleConfig> config;
    std::string error;

    [[nodiscard]] bool Ok() const { return config.has_value(); }
};

[[nodiscard]] std::string NormalizeAudioId(std::string_view id);
[[nodiscard]] bool IsAudioAssetId(std::string_view id);
[[nodiscard]] AudioSampleConfigLoadResult LoadAudioSampleConfig(const std::filesystem::path& path);

}  // namespace pvzmod
