#pragma once

#include <filesystem>
#include <optional>
#include <string>
#include <vector>

namespace pvzmod {

struct CustomPlantAttack {
    std::string mode = "projectile";
    int projectileType = 0;
    int damage = 20;
    int shotsPerAttack = 1;
    int damageRangeFlags = -1;
};

struct CustomPlantDefinition {
    int id = 1000;
    std::string name = "Custom Peashooter";
    std::string description;
    int templatePlantId = 0;
    bool unlocked = true;
    int chooserX = 470;
    int chooserY = 128;
    int cost = 100;
    int rechargeTime = 750;
    int health = 300;
    int launchRate = 150;
    int initialLaunchDelayMin = 0;
    int initialLaunchDelayMax = 150;
    CustomPlantAttack attack;
};

struct CustomPlantCatalog {
    int schemaVersion = 1;
    std::vector<CustomPlantDefinition> plants;
};

struct CustomPlantConfigLoadResult {
    std::optional<CustomPlantCatalog> config;
    std::string error;
    [[nodiscard]] bool Ok() const { return config.has_value(); }
};

[[nodiscard]] CustomPlantConfigLoadResult LoadCustomPlantConfig(const std::filesystem::path& path);

}  // namespace pvzmod
