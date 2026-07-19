#pragma once

#include <cstdint>
#include <filesystem>
#include <optional>
#include <string>
#include <unordered_map>
#include <vector>

namespace pvzmod {

enum class ArmorSlot {
    Helmet,
    Shield,
    Flying,
};

enum class ArmorVisual {
    Cone,
    Bucket,
    Door,
    Newspaper,
    FootballHelmet,
    Bobsled,
    Balloon,
    DiggerHelmet,
    Ladder,
    WallnutHead,
    TallnutHead,
};

enum class ArmorHealthPool {
    Helmet,
    Shield,
    Flying,
};

struct OriginalArmorHealthRule {
    std::string key;
    int zombieId = 0;
    ArmorHealthPool pool = ArmorHealthPool::Helmet;
    int defaultHealth = 0;
    int configuredHealth = 0;

    [[nodiscard]] bool HasEffect() const { return configuredHealth != defaultHealth; }
};

struct ArmorDefinition {
    int id = 0;
    std::string name;
    int tier = 1;
    ArmorSlot slot = ArmorSlot::Helmet;
    ArmorVisual visual = ArmorVisual::Bucket;
    int health = 0;
};

struct ArmorRoll {
    int armorId = 0;
    double chance = 0.0;
};

struct ZombieAttributeOverride {
    std::optional<int> bodyHealth;
    std::optional<int> attackDamage;
    std::optional<std::vector<ArmorRoll>> armorRolls;
};

struct ZombieConfig {
    int schemaVersion = 1;
    std::uint32_t seed = 0x5A17B00B;
    std::unordered_map<int, OriginalArmorHealthRule> originalArmorHealth;
    std::unordered_map<int, ArmorDefinition> armors;
    std::unordered_map<int, ZombieAttributeOverride> zombies;

    [[nodiscard]] const OriginalArmorHealthRule* FindOriginalArmorHealth(int zombieId) const;
    [[nodiscard]] const ArmorDefinition* FindArmor(int armorId) const;
    [[nodiscard]] const ZombieAttributeOverride* FindZombie(int zombieId) const;
};

struct ZombieConfigLoadResult {
    std::optional<ZombieConfig> config;
    std::string error;

    [[nodiscard]] bool Ok() const { return config.has_value(); }
};

[[nodiscard]] ZombieConfigLoadResult LoadZombieConfig(const std::filesystem::path& path);
[[nodiscard]] bool ApplyOriginalArmorHealthOverride(
    const OriginalArmorHealthRule& rule, int& currentHealth, int& maximumHealth);
[[nodiscard]] bool ArmorRollSucceeds(
    std::uint32_t seed, int zombieType, std::uint32_t zombieInstanceId, int armorId, double chance);

}  // namespace pvzmod
