#pragma once

#include <filesystem>
#include <optional>
#include <string>

namespace pvzmod {

struct SeedUiConfig {
    int schemaVersion = 1;
    std::optional<int> slotCount;
};

struct SeedUiConfigLoadResult {
    std::optional<SeedUiConfig> config;
    std::string error;
    [[nodiscard]] bool Ok() const { return config.has_value(); }
};

[[nodiscard]] SeedUiConfigLoadResult LoadSeedUiConfig(const std::filesystem::path& path);

}  // namespace pvzmod
