#include "spawn_config.h"

#include "wave_generator.h"

#include <cmath>
#include <fstream>
#include <limits>
#include <regex>
#include <set>
#include <sstream>

#include <nlohmann/json.hpp>

namespace pvzmod {
namespace {

using nlohmann::json;

std::string PathForMessage(const std::filesystem::path& path) {
    return path.string();
}

}  // namespace

const LevelSpawnConfig* SpawnConfig::FindAdventureLevel(const int adventureLevel) const {
    const auto found = adventureLevels.find(adventureLevel);
    if (found == adventureLevels.end() || !found->second.enabled) {
        return nullptr;
    }
    return &found->second;
}

std::optional<int> ParseAdventureLevelKey(const std::string& key) {
    static const std::regex pattern(R"(^([1-5])-([1-9]|10)$)");
    std::smatch match;
    if (!std::regex_match(key, match, pattern)) {
        return std::nullopt;
    }

    const int world = std::stoi(match[1].str());
    const int stage = std::stoi(match[2].str());
    return (world - 1) * 10 + stage;
}

std::string FormatAdventureLevelKey(const int adventureLevel) {
    if (adventureLevel < 1 || adventureLevel > 50) {
        return "unknown";
    }
    const int world = ((adventureLevel - 1) / 10) + 1;
    const int stage = ((adventureLevel - 1) % 10) + 1;
    return std::to_string(world) + "-" + std::to_string(stage);
}

bool IsSpecialPlacementZombie(const int zombieId) {
    switch (zombieId) {
        case 1:   // Flag Zombie
        case 19:  // Yeti
        case 20:  // Bungee Zombie
        case 24:  // Imp (normally spawned by Gargantuar)
        case 25:  // Dr. Zomboss
            return true;
        default:
            return false;
    }
}

ConfigLoadResult LoadSpawnConfig(const std::filesystem::path& path) {
    ConfigLoadResult result;

    std::ifstream input(path, std::ios::binary);
    if (!input) {
        result.error = "Cannot open spawn config: " + PathForMessage(path);
        return result;
    }

    json root;
    try {
        input >> root;
    } catch (const std::exception& exception) {
        result.error = std::string("Invalid JSON: ") + exception.what();
        return result;
    }

    try {
        if (!root.is_object()) {
            throw std::runtime_error("root must be an object");
        }

        SpawnConfig parsed;
        parsed.schemaVersion = root.value("schemaVersion", 1);
        if (parsed.schemaVersion != 1) {
            throw std::runtime_error("unsupported schemaVersion; expected 1");
        }

        const auto levelsIt = root.find("levels");
        if (levelsIt == root.end() || !levelsIt->is_object()) {
            throw std::runtime_error("levels must be an object");
        }

        for (auto levelIt = levelsIt->begin(); levelIt != levelsIt->end(); ++levelIt) {
            const std::string levelKey = levelIt.key();
            const auto adventureLevel = ParseAdventureLevelKey(levelKey);
            if (!adventureLevel.has_value()) {
                throw std::runtime_error("invalid adventure level key '" + levelKey + "'; expected 1-1 through 5-10");
            }
            if (!levelIt.value().is_object()) {
                throw std::runtime_error("level '" + levelKey + "' must be an object");
            }

            const json& levelJson = levelIt.value();
            LevelSpawnConfig level;
            level.enabled = levelJson.value("enabled", true);
            level.preserveOriginalSpecialZombies = levelJson.value("preserveOriginalSpecialZombies", true);
            level.allowSpecialZombieIds = levelJson.value("allowSpecialZombieIds", false);

            const auto seedIt = levelJson.find("seed");
            if (seedIt != levelJson.end()) {
                if (!seedIt->is_number_unsigned() && !seedIt->is_number_integer()) {
                    throw std::runtime_error("level '" + levelKey + "': seed must be an integer");
                }
                const auto seed = seedIt->get<std::int64_t>();
                if (seed < 0 || seed > std::numeric_limits<std::uint32_t>::max()) {
                    throw std::runtime_error("level '" + levelKey + "': seed must be between 0 and 4294967295");
                }
                level.seed = static_cast<std::uint32_t>(seed);
            } else {
                level.seed = 0x50565A00u ^ static_cast<std::uint32_t>(*adventureLevel);
            }

            const auto zombiesIt = levelJson.find("zombies");
            if (zombiesIt == levelJson.end() || !zombiesIt->is_array() || zombiesIt->empty()) {
                throw std::runtime_error("level '" + levelKey + "': zombies must be a non-empty array");
            }

            std::set<int> seenIds;
            for (std::size_t index = 0; index < zombiesIt->size(); ++index) {
                const json& item = (*zombiesIt)[index];
                if (!item.is_object() || !item.contains("id") || !item.contains("weight")) {
                    throw std::runtime_error("level '" + levelKey + "': every zombies item needs id and weight");
                }
                if (!item["id"].is_number_integer() || !item["weight"].is_number()) {
                    throw std::runtime_error("level '" + levelKey + "': zombie id must be integer and weight must be numeric");
                }

                const int id = item["id"].get<int>();
                const double weight = item["weight"].get<double>();
                std::size_t minimumCount = 0;
                const auto minimumIt = item.find("minimumCount");
                if (minimumIt != item.end()) {
                    if (!minimumIt->is_number_unsigned() && !minimumIt->is_number_integer()) {
                        throw std::runtime_error("level '" + levelKey + "': minimumCount must be an integer");
                    }
                    const auto parsedMinimum = minimumIt->get<std::int64_t>();
                    if (parsedMinimum < 0 || parsedMinimum > static_cast<std::int64_t>(kSpawnListCount)) {
                        throw std::runtime_error("level '" + levelKey + "': minimumCount must be between 0 and 1000");
                    }
                    minimumCount = static_cast<std::size_t>(parsedMinimum);
                }
                if (id < 0 || id > 32) {
                    throw std::runtime_error("level '" + levelKey + "': zombie id must be between 0 and 32");
                }
                if (!std::isfinite(weight) || weight <= 0.0 || weight > 1.0e9) {
                    throw std::runtime_error("level '" + levelKey + "': weight must be greater than 0 and at most 1e9");
                }
                if (!seenIds.insert(id).second) {
                    throw std::runtime_error("level '" + levelKey + "': duplicate zombie id " + std::to_string(id));
                }
                if (IsSpecialPlacementZombie(id) && !level.allowSpecialZombieIds) {
                    throw std::runtime_error(
                        "level '" + levelKey + "': zombie id " + std::to_string(id) +
                        " requires allowSpecialZombieIds=true");
                }
                level.zombies.push_back({id, weight, minimumCount});
            }

            if (!level.enabled) {
                result.warnings.push_back("Level " + levelKey + " is present but disabled; original spawn logic will be used.");
            }

            parsed.adventureLevels.emplace(*adventureLevel, std::move(level));
        }

        result.config = std::move(parsed);
    } catch (const std::exception& exception) {
        result.error = std::string("Spawn config validation failed: ") + exception.what();
    }

    return result;
}

}  // namespace pvzmod
