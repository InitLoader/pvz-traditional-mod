#pragma once

#include <filesystem>
#include <optional>
#include <string>
#include <unordered_map>

namespace pvzmod {

struct PlantAttackConfig {
    int schemaVersion = 1;
    std::unordered_map<std::string, int> projectileOverrides;
    std::unordered_map<std::string, int> directAttackOverrides;

    [[nodiscard]] std::optional<int> FindProjectile(const std::string& key) const;
    [[nodiscard]] std::optional<int> FindDirectAttack(const std::string& key) const;
};

struct PlantAttackConfigLoadResult {
    std::optional<PlantAttackConfig> config;
    std::string error;

    [[nodiscard]] bool Ok() const { return config.has_value(); }
};

[[nodiscard]] PlantAttackConfigLoadResult LoadPlantAttackConfig(const std::filesystem::path& path);

}  // namespace pvzmod
