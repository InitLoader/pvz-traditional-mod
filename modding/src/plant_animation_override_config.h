#pragma once

#include <filesystem>
#include <map>
#include <optional>
#include <string>

namespace pvzmod {

struct PlantAnimationOverride {
    int plantType = 0;
    std::string animationId;
};

struct PlantAnimationOverrideConfig {
    int schemaVersion = 1;
    std::map<int, PlantAnimationOverride> plants;

    [[nodiscard]] const PlantAnimationOverride* FindPlant(int plantType) const;
};

struct PlantAnimationOverrideConfigLoadResult {
    std::optional<PlantAnimationOverrideConfig> config;
    std::string error;
    [[nodiscard]] bool Ok() const { return config.has_value(); }
};

[[nodiscard]] PlantAnimationOverrideConfigLoadResult LoadPlantAnimationOverrideConfig(
    const std::filesystem::path& path);

}  // namespace pvzmod
