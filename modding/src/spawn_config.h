#pragma once

#include <cstdint>
#include <cstddef>
#include <filesystem>
#include <optional>
#include <string>
#include <unordered_map>
#include <vector>

namespace pvzmod {

struct ZombieWeight {
    int id = 0;
    double weight = 0.0;
    std::size_t minimumCount = 0;
};

struct LevelSpawnConfig {
    bool enabled = true;
    bool preserveOriginalSpecialZombies = true;
    bool allowSpecialZombieIds = false;
    std::uint32_t seed = 0;
    std::vector<ZombieWeight> zombies;
};

struct SpawnConfig {
    int schemaVersion = 1;
    std::unordered_map<int, LevelSpawnConfig> adventureLevels;

    [[nodiscard]] const LevelSpawnConfig* FindAdventureLevel(int adventureLevel) const;
};

struct ConfigLoadResult {
    std::optional<SpawnConfig> config;
    std::vector<std::string> warnings;
    std::string error;

    [[nodiscard]] bool Ok() const { return config.has_value(); }
};

[[nodiscard]] ConfigLoadResult LoadSpawnConfig(const std::filesystem::path& path);
[[nodiscard]] std::optional<int> ParseAdventureLevelKey(const std::string& key);
[[nodiscard]] std::string FormatAdventureLevelKey(int adventureLevel);
[[nodiscard]] bool IsSpecialPlacementZombie(int zombieId);

}  // namespace pvzmod
