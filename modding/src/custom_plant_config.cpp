#include "custom_plant_config.h"

#include "external_texture_config.h"

#include <fstream>
#include <stdexcept>
#include <unordered_set>

#include <nlohmann/json.hpp>

namespace pvzmod {
namespace {

using nlohmann::json;

int Integer(const json& object, const char* key, int fallback, int minimum, int maximum) {
    const auto it = object.find(key);
    if (it == object.end()) return fallback;
    if (!it->is_number_integer() && !it->is_number_unsigned()) {
        throw std::runtime_error(std::string(key) + " must be an integer");
    }
    const long long value = it->get<long long>();
    if (value < minimum || value > maximum) {
        throw std::runtime_error(std::string(key) + " must be between " + std::to_string(minimum) +
                                 " and " + std::to_string(maximum));
    }
    return static_cast<int>(value);
}

std::string String(const json& object, const char* key, std::string fallback) {
    const auto it = object.find(key);
    if (it == object.end()) return fallback;
    if (!it->is_string()) throw std::runtime_error(std::string(key) + " must be a string");
    return it->get<std::string>();
}

bool Boolean(const json& object, const char* key, bool fallback) {
    const auto it = object.find(key);
    if (it == object.end()) return fallback;
    if (!it->is_boolean()) throw std::runtime_error(std::string(key) + " must be true or false");
    return it->get<bool>();
}

}  // namespace

CustomPlantConfigLoadResult LoadCustomPlantConfig(const std::filesystem::path& path) {
    CustomPlantConfigLoadResult result;
    std::ifstream input(path, std::ios::binary);
    if (!input) {
        result.error = "Cannot open custom plant config: " + path.string();
        return result;
    }
    try {
        const json root = json::parse(input, nullptr, true, true);
        if (!root.is_object()) throw std::runtime_error("root must be an object");
        CustomPlantCatalog parsed;
        parsed.schemaVersion = root.value("schemaVersion", 1);
        if (parsed.schemaVersion != 1) throw std::runtime_error("unsupported schemaVersion; expected 1");
        const auto plants = root.find("plants");
        if (plants == root.end() || !plants->is_array()) throw std::runtime_error("plants must be an array");
        if (plants->size() > 512) throw std::runtime_error("at most 512 custom plants are supported");

        std::unordered_set<int> ids;
        int ordinal = 0;
        for (const json& item : *plants) {
            if (!item.is_object()) throw std::runtime_error("each plants entry must be an object");
            CustomPlantDefinition plant;
            plant.id = Integer(item, "id", 1000 + ordinal, 1000, 30000);
            if (!ids.insert(plant.id).second) throw std::runtime_error("custom plant id values must be unique");
            plant.name = String(item, "name", plant.name);
            plant.description = String(item, "description", plant.description);
            plant.animationId = String(item, "animationId", {});
            if (!plant.animationId.empty() && !IsExternalResourceId(plant.animationId)) {
                throw std::runtime_error("animationId must match [A-Za-z0-9_]+ and contain 1-64 characters");
            }
            plant.hideTemplateAttachments = Boolean(item, "hideTemplateAttachments", true);
            plant.templatePlantId = Integer(item, "templatePlantId", 0, 0, 48);
            plant.unlocked = Boolean(item, "unlocked", true);
            plant.cost = Integer(item, "cost", 100, 0, 9999);
            plant.rechargeTime = Integer(item, "rechargeTime", 750, 0, 60000);
            plant.health = Integer(item, "health", 300, 1, 1000000);
            plant.launchRate = Integer(item, "launchRate", 150, 1, 60000);

            if (const auto chooser = item.find("chooser"); chooser != item.end()) {
                if (!chooser->is_object()) throw std::runtime_error("chooser must be an object");
                plant.chooserX = Integer(*chooser, "x", 470, 0, 760);
                plant.chooserY = Integer(*chooser, "y", 128 + ordinal * 73, 80, 520);
            } else {
                plant.chooserY = 128 + ordinal * 73;
            }
            if (const auto delay = item.find("initialLaunchDelay"); delay != item.end()) {
                if (!delay->is_object()) throw std::runtime_error("initialLaunchDelay must be an object");
                plant.initialLaunchDelayMin = Integer(*delay, "min", 0, 0, 60000);
                plant.initialLaunchDelayMax = Integer(*delay, "max", 150, 0, 60000);
                if (plant.initialLaunchDelayMin > plant.initialLaunchDelayMax) {
                    throw std::runtime_error("initialLaunchDelay.min must not exceed max");
                }
            }
            if (const auto attack = item.find("attack"); attack != item.end()) {
                if (!attack->is_object()) throw std::runtime_error("attack must be an object");
                plant.attack.mode = String(*attack, "mode", "projectile");
                if (plant.attack.mode != "projectile" && plant.attack.mode != "template") {
                    throw std::runtime_error("attack.mode must be projectile or template");
                }
                plant.attack.projectileType = Integer(*attack, "projectileType", 0, 0, 13);
                plant.attack.damage = Integer(*attack, "damage", 20, 0, 1000000);
                plant.attack.shotsPerAttack = Integer(*attack, "shotsPerAttack", 1, 1, 8);
                plant.attack.damageRangeFlags = Integer(*attack, "damageRangeFlags", -1, -1, 0x7fffffff);
            }
            parsed.plants.push_back(std::move(plant));
            ++ordinal;
        }
        result.config = std::move(parsed);
    } catch (const std::exception& exception) {
        result.error = std::string("Custom plant config validation failed: ") + exception.what();
    }
    return result;
}

}  // namespace pvzmod
