#include "plant_attack_config.h"

#include <fstream>
#include <stdexcept>
#include <unordered_set>

#include <nlohmann/json.hpp>

namespace pvzmod {
namespace {

using nlohmann::json;

const std::unordered_set<std::string> kProjectileKeys = {
    "pea", "snowPea", "cabbage", "melon", "puff", "winterMelon",
    "fireball", "star", "spike", "kernel", "butter"};

const std::unordered_set<std::string> kDirectAttackKeys = {
    "fumeShroom", "gloomShroom", "spikeweed", "spikerock", "spikeVehicle",
    "squash", "chomperStrongTarget", "iceShroom", "potatoMine", "cherryBomb",
    "doomShroom", "jalapeno", "cobCannon", "explodeONut"};

void ReadOverrides(
    const json& root,
    const char* sectionName,
    const std::unordered_set<std::string>& allowedKeys,
    std::unordered_map<std::string, int>& output) {
    const auto section = root.find(sectionName);
    if (section == root.end()) {
        return;
    }
    if (!section->is_object()) {
        throw std::runtime_error(std::string(sectionName) + " must be an object");
    }

    for (auto item = section->cbegin(); item != section->cend(); ++item) {
        const std::string key = item.key();
        const json& value = item.value();
        if (!allowedKeys.contains(key)) {
            throw std::runtime_error(std::string(sectionName) + "." + key + " is not a supported attack key");
        }
        if (!value.is_number_integer() && !value.is_number_unsigned()) {
            throw std::runtime_error(std::string(sectionName) + "." + key + " must be an integer");
        }
        const long long damage = value.get<long long>();
        if (damage < 0 || damage > 1000000) {
            throw std::runtime_error(std::string(sectionName) + "." + key + " must be between 0 and 1000000");
        }
        output.insert_or_assign(key, static_cast<int>(damage));
    }
}

}  // namespace

std::optional<int> PlantAttackConfig::FindProjectile(const std::string& key) const {
    const auto found = projectileOverrides.find(key);
    return found == projectileOverrides.end() ? std::nullopt : std::optional<int>(found->second);
}

std::optional<int> PlantAttackConfig::FindDirectAttack(const std::string& key) const {
    const auto found = directAttackOverrides.find(key);
    return found == directAttackOverrides.end() ? std::nullopt : std::optional<int>(found->second);
}

PlantAttackConfigLoadResult LoadPlantAttackConfig(const std::filesystem::path& path) {
    PlantAttackConfigLoadResult result;
    std::ifstream input(path, std::ios::binary);
    if (!input) {
        result.error = "Cannot open plant attack config: " + path.string();
        return result;
    }

    json root;
    try {
        root = json::parse(input, nullptr, true, true);
    } catch (const std::exception& exception) {
        result.error = std::string("Invalid plant attack JSONC: ") + exception.what();
        return result;
    }

    try {
        if (!root.is_object()) {
            throw std::runtime_error("root must be an object");
        }

        PlantAttackConfig parsed;
        parsed.schemaVersion = root.value("schemaVersion", 1);
        if (parsed.schemaVersion != 1) {
            throw std::runtime_error("unsupported schemaVersion; expected 1");
        }
        ReadOverrides(root, "projectiles", kProjectileKeys, parsed.projectileOverrides);
        ReadOverrides(root, "directAttacks", kDirectAttackKeys, parsed.directAttackOverrides);
        result.config = std::move(parsed);
    } catch (const std::exception& exception) {
        result.error = std::string("Plant attack config validation failed: ") + exception.what();
    }
    return result;
}

}  // namespace pvzmod
