#pragma once

#include <cstdint>
#include <filesystem>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace pvzmod {

struct EliteTint {
    int red = 255;
    int green = 255;
    int blue = 255;
    int alpha = 255;
};

struct EliteVisualDefinition {
    std::optional<EliteTint> tint;
    std::string overlayTextureId;
    int offsetX = 0;
    int offsetY = 0;
};

struct EliteSkillBinding {
    std::string id;
    std::unordered_map<std::string, double> parameters;

    [[nodiscard]] double Parameter(std::string_view name, double fallback) const;
};

struct EliteZombieDefinition {
    int runtimeId = 0;
    std::string id;
    std::string name;
    bool enabled = true;
    int priority = 0;
    std::vector<int> eligibleZombieIds;
    double chance = 0.0;
    std::vector<EliteSkillBinding> skills;
    EliteVisualDefinition visual;

    [[nodiscard]] bool AllowsZombie(int zombieType) const;
};

struct EliteZombieConfig {
    int schemaVersion = 1;
    std::uint32_t seed = 0x52414745U;
    std::vector<EliteZombieDefinition> elites;
};

struct EliteZombieConfigLoadResult {
    std::optional<EliteZombieConfig> config;
    std::string error;

    [[nodiscard]] bool Ok() const { return config.has_value(); }
};

[[nodiscard]] EliteZombieConfigLoadResult LoadEliteZombieConfig(const std::filesystem::path& path);
[[nodiscard]] bool EliteRollSucceeds(
    std::uint32_t seed, int zombieType, std::uint32_t zombieInstanceId, int runtimeId, double chance);
[[nodiscard]] const EliteZombieDefinition* PickEliteZombie(
    const EliteZombieConfig& config, int zombieType, std::uint32_t zombieInstanceId);

}  // namespace pvzmod
