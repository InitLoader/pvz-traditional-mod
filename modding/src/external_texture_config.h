#pragma once

#include <filesystem>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>

namespace pvzmod {

struct ExternalTextureDefinition {
    std::string id;
    std::string path;
};

struct ExternalTextureConfig {
    int schemaVersion = 1;
    std::unordered_map<std::string, ExternalTextureDefinition> textures;

    [[nodiscard]] const ExternalTextureDefinition* Find(std::string_view id) const;
};

struct ExternalTextureConfigLoadResult {
    std::optional<ExternalTextureConfig> config;
    std::string error;

    [[nodiscard]] bool Ok() const { return config.has_value(); }
};

[[nodiscard]] bool IsExternalResourceId(std::string_view id);
[[nodiscard]] ExternalTextureConfigLoadResult LoadExternalTextureConfig(const std::filesystem::path& path);

}  // namespace pvzmod
