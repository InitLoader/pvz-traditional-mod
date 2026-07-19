#include "zombie_config.h"

#include "zombie_armor_adapter.h"

#include <array>
#include <charconv>
#include <cmath>
#include <fstream>
#include <limits>
#include <stdexcept>

#include <nlohmann/json.hpp>

namespace pvzmod {
namespace {

using nlohmann::json;

struct OriginalArmorCatalogEntry {
    const char* key;
    int zombieId;
    ArmorHealthPool pool;
    int defaultHealth;
};

// PvZ 1.0.0.1051 active armor/secondary-health profiles. Some HelmType enum
// values are reserved but have no independent health assignment in this build.
constexpr std::array<OriginalArmorCatalogEntry, 11> kOriginalArmorCatalog = {{
    {"trafficCone", 2, ArmorHealthPool::Helmet, 370},
    {"bucket", 4, ArmorHealthPool::Helmet, 1100},
    {"newspaper", 5, ArmorHealthPool::Shield, 150},
    {"screenDoor", 6, ArmorHealthPool::Shield, 1100},
    {"footballHelmet", 7, ArmorHealthPool::Helmet, 1400},
    {"bobsled", 13, ArmorHealthPool::Helmet, 300},
    {"balloon", 16, ArmorHealthPool::Flying, 20},
    {"diggerHelmet", 17, ArmorHealthPool::Helmet, 100},
    {"ladder", 21, ArmorHealthPool::Shield, 500},
    {"wallnutHead", 27, ArmorHealthPool::Helmet, 1100},
    {"tallnutHead", 31, ArmorHealthPool::Helmet, 2200},
}};

const OriginalArmorCatalogEntry* FindOriginalArmorCatalogEntry(const std::string& key) {
    for (const OriginalArmorCatalogEntry& entry : kOriginalArmorCatalog) {
        if (key == entry.key) {
            return &entry;
        }
    }
    return nullptr;
}

int ParseNumericKey(const std::string& key, const char* path, const int minimum, const int maximum) {
    int value = 0;
    const auto [end, error] = std::from_chars(key.data(), key.data() + key.size(), value);
    if (error != std::errc{} || end != key.data() + key.size() || value < minimum || value > maximum) {
        throw std::runtime_error(std::string(path) + " key '" + key + "' must be an integer between " +
                                 std::to_string(minimum) + " and " + std::to_string(maximum));
    }
    return value;
}

int ReadInteger(const json& object, const char* field, const int minimum, const int maximum) {
    const auto found = object.find(field);
    if (found == object.end() || (!found->is_number_integer() && !found->is_number_unsigned())) {
        throw std::runtime_error(std::string(field) + " must be an integer");
    }
    const long long value = found->get<long long>();
    if (value < minimum || value > maximum) {
        throw std::runtime_error(std::string(field) + " must be between " + std::to_string(minimum) +
                                 " and " + std::to_string(maximum));
    }
    return static_cast<int>(value);
}

std::optional<int> ReadOptionalInteger(
    const json& object, const char* field, const int minimum, const int maximum, const std::string& path) {
    const auto found = object.find(field);
    if (found == object.end()) {
        return std::nullopt;
    }
    if (!found->is_number_integer() && !found->is_number_unsigned()) {
        throw std::runtime_error(path + "." + field + " must be an integer");
    }
    const long long value = found->get<long long>();
    if (value < minimum || value > maximum) {
        throw std::runtime_error(path + "." + field + " must be between " + std::to_string(minimum) +
                                 " and " + std::to_string(maximum));
    }
    return static_cast<int>(value);
}

ArmorVisual ParseVisual(const std::string& visual) {
    if (visual == "cone") {
        return ArmorVisual::Cone;
    }
    if (visual == "bucket") {
        return ArmorVisual::Bucket;
    }
    if (visual == "door") {
        return ArmorVisual::Door;
    }
    if (visual == "newspaper") {
        return ArmorVisual::Newspaper;
    }
    if (visual == "footballHelmet") {
        return ArmorVisual::FootballHelmet;
    }
    if (visual == "bobsled") {
        return ArmorVisual::Bobsled;
    }
    if (visual == "balloon") {
        return ArmorVisual::Balloon;
    }
    if (visual == "diggerHelmet") {
        return ArmorVisual::DiggerHelmet;
    }
    if (visual == "ladder") {
        return ArmorVisual::Ladder;
    }
    if (visual == "wallnutHead") {
        return ArmorVisual::WallnutHead;
    }
    if (visual == "tallnutHead") {
        return ArmorVisual::TallnutHead;
    }
    throw std::runtime_error(
        "visual must be cone, bucket, door, newspaper, footballHelmet, bobsled, balloon, "
        "diggerHelmet, ladder, wallnutHead, or tallnutHead");
}

ArmorSlot SlotForVisual(const ArmorVisual visual) {
    return GetArmorVisualAdapterInfo(visual).slot;
}

std::uint64_t Mix64(std::uint64_t value) {
    value += 0x9E3779B97F4A7C15ULL;
    value = (value ^ (value >> 30U)) * 0xBF58476D1CE4E5B9ULL;
    value = (value ^ (value >> 27U)) * 0x94D049BB133111EBULL;
    return value ^ (value >> 31U);
}

}  // namespace

const OriginalArmorHealthRule* ZombieConfig::FindOriginalArmorHealth(const int zombieId) const {
    const auto found = originalArmorHealth.find(zombieId);
    return found == originalArmorHealth.end() ? nullptr : &found->second;
}

const ArmorDefinition* ZombieConfig::FindArmor(const int armorId) const {
    const auto found = armors.find(armorId);
    return found == armors.end() ? nullptr : &found->second;
}

const ZombieAttributeOverride* ZombieConfig::FindZombie(const int zombieId) const {
    const auto found = zombies.find(zombieId);
    return found == zombies.end() ? nullptr : &found->second;
}

ZombieConfigLoadResult LoadZombieConfig(const std::filesystem::path& path) {
    ZombieConfigLoadResult result;
    std::ifstream input(path, std::ios::binary);
    if (!input) {
        result.error = "Cannot open zombie config: " + path.string();
        return result;
    }

    json root;
    try {
        root = json::parse(input, nullptr, true, true);
    } catch (const std::exception& exception) {
        result.error = std::string("Invalid zombie JSONC: ") + exception.what();
        return result;
    }

    try {
        if (!root.is_object()) {
            throw std::runtime_error("root must be an object");
        }
        ZombieConfig parsed;
        parsed.schemaVersion = root.value("schemaVersion", 1);
        if (parsed.schemaVersion != 1) {
            throw std::runtime_error("unsupported schemaVersion; expected 1");
        }
        const long long seed = root.value("seed", static_cast<long long>(parsed.seed));
        if (seed < 0 || seed > std::numeric_limits<std::uint32_t>::max()) {
            throw std::runtime_error("seed must be between 0 and 4294967295");
        }
        parsed.seed = static_cast<std::uint32_t>(seed);

        const auto originalArmorObject = root.find("originalArmorHealth");
        if (originalArmorObject != root.end()) {
            if (!originalArmorObject->is_object()) {
                throw std::runtime_error("originalArmorHealth must be an object");
            }
            for (auto item = originalArmorObject->cbegin(); item != originalArmorObject->cend(); ++item) {
                const OriginalArmorCatalogEntry* catalog = FindOriginalArmorCatalogEntry(item.key());
                if (catalog == nullptr) {
                    throw std::runtime_error("unknown originalArmorHealth key '" + item.key() + "'");
                }
                if (!item.value().is_number_integer() && !item.value().is_number_unsigned()) {
                    throw std::runtime_error("originalArmorHealth." + item.key() + " must be an integer");
                }
                const long long health = item.value().get<long long>();
                if (health < 1 || health > 1000000) {
                    throw std::runtime_error(
                        "originalArmorHealth." + item.key() + " must be between 1 and 1000000");
                }
                OriginalArmorHealthRule rule;
                rule.key = catalog->key;
                rule.zombieId = catalog->zombieId;
                rule.pool = catalog->pool;
                rule.defaultHealth = catalog->defaultHealth;
                rule.configuredHealth = static_cast<int>(health);
                parsed.originalArmorHealth.emplace(rule.zombieId, std::move(rule));
            }
        }

        const auto armorObject = root.find("armorDefinitions");
        if (armorObject != root.end()) {
            if (!armorObject->is_object()) {
                throw std::runtime_error("armorDefinitions must be an object");
            }
            for (auto item = armorObject->cbegin(); item != armorObject->cend(); ++item) {
                const int id = ParseNumericKey(item.key(), "armorDefinitions", 1, 999999);
                if (!item.value().is_object()) {
                    throw std::runtime_error("armorDefinitions." + item.key() + " must be an object");
                }
                ArmorDefinition armor;
                armor.id = id;
                armor.name = item.value().value("name", "armor_" + item.key());
                armor.tier = ReadInteger(item.value(), "tier", 1, 100);
                armor.visual = ParseVisual(item.value().value("visual", std::string{}));
                armor.slot = SlotForVisual(armor.visual);
                armor.health = ReadInteger(item.value(), "health", 1, 1000000);
                parsed.armors.emplace(id, std::move(armor));
            }
        }

        const auto zombieObject = root.find("zombies");
        if (zombieObject != root.end()) {
            if (!zombieObject->is_object()) {
                throw std::runtime_error("zombies must be an object");
            }
            for (auto item = zombieObject->cbegin(); item != zombieObject->cend(); ++item) {
                const int zombieId = ParseNumericKey(item.key(), "zombies", 0, 1023);
                if (!item.value().is_object()) {
                    throw std::runtime_error("zombies." + item.key() + " must be an object");
                }
                const std::string entryPath = "zombies." + item.key();
                ZombieAttributeOverride override;
                override.bodyHealth = ReadOptionalInteger(item.value(), "bodyHealth", 1, 1000000, entryPath);
                override.attackDamage = ReadOptionalInteger(item.value(), "attackDamage", 0, 1000000, entryPath);

                const auto rolls = item.value().find("armorRolls");
                if (rolls != item.value().end()) {
                    if (!rolls->is_array()) {
                        throw std::runtime_error(entryPath + ".armorRolls must be an array");
                    }
                    override.armorRolls.emplace();
                    for (std::size_t index = 0; index < rolls->size(); ++index) {
                        const json& rollObject = (*rolls)[index];
                        if (!rollObject.is_object()) {
                            throw std::runtime_error(entryPath + ".armorRolls entries must be objects");
                        }
                        ArmorRoll roll;
                        roll.armorId = ReadInteger(rollObject, "armorId", 1, 999999);
                        const auto chance = rollObject.find("chance");
                        if (chance == rollObject.end() || !chance->is_number()) {
                            throw std::runtime_error(entryPath + ".armorRolls.chance must be a number");
                        }
                        roll.chance = chance->get<double>();
                        if (!std::isfinite(roll.chance) || roll.chance < 0.0 || roll.chance > 100.0) {
                            throw std::runtime_error(entryPath + ".armorRolls.chance must be between 0 and 100");
                        }
                        if (!parsed.armors.contains(roll.armorId)) {
                            throw std::runtime_error(entryPath + ".armorRolls references undefined armorId " +
                                                     std::to_string(roll.armorId));
                        }
                        override.armorRolls->push_back(roll);
                    }
                }
                parsed.zombies.emplace(zombieId, std::move(override));
            }
        }
        result.config = std::move(parsed);
    } catch (const std::exception& exception) {
        result.error = std::string("Zombie config validation failed: ") + exception.what();
    }
    return result;
}

bool ApplyOriginalArmorHealthOverride(
    const OriginalArmorHealthRule& rule, int& currentHealth, int& maximumHealth) {
    if (!rule.HasEffect()) {
        return false;
    }
    currentHealth = rule.configuredHealth;
    maximumHealth = rule.configuredHealth;
    return true;
}

bool ArmorRollSucceeds(
    const std::uint32_t seed,
    const int zombieType,
    const std::uint32_t zombieInstanceId,
    const int armorId,
    const double chance) {
    if (chance <= 0.0) {
        return false;
    }
    if (chance >= 100.0) {
        return true;
    }
    std::uint64_t value = seed;
    value ^= static_cast<std::uint64_t>(static_cast<std::uint32_t>(zombieType)) << 32U;
    value ^= zombieInstanceId;
    value ^= static_cast<std::uint64_t>(static_cast<std::uint32_t>(armorId)) * 0xD6E8FEB86659FD93ULL;
    const double roll = static_cast<double>(Mix64(value) % 1000000ULL) / 10000.0;
    return roll < chance;
}

}  // namespace pvzmod
