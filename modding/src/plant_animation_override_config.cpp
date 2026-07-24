#include "plant_animation_override_config.h"

#include "external_texture_config.h"

#include <fstream>
#include <stdexcept>

#include <nlohmann/json.hpp>

namespace pvzmod {

const PlantAnimationOverride* PlantAnimationOverrideConfig::FindPlant(const int plantType) const {
    const auto found = plants.find(plantType);
    return found == plants.end() ? nullptr : &found->second;
}

PlantAnimationOverrideConfigLoadResult LoadPlantAnimationOverrideConfig(
    const std::filesystem::path& path) {
    PlantAnimationOverrideConfigLoadResult result;
    std::ifstream input(path, std::ios::binary);
    if (!input) {
        result.error = "Cannot open plant animation override config: " + path.string();
        return result;
    }
    try {
        using nlohmann::json;
        const json root = json::parse(input, nullptr, true, true);
        if (!root.is_object()) throw std::runtime_error("root must be an object");

        PlantAnimationOverrideConfig parsed;
        parsed.schemaVersion = root.value("schemaVersion", 1);
        if (parsed.schemaVersion != 1) {
            throw std::runtime_error("unsupported schemaVersion; expected 1");
        }
        const auto plants = root.find("plants");
        if (plants == root.end() || !plants->is_object()) {
            throw std::runtime_error("plants must be an object keyed by original plant ID");
        }
        if (plants->size() > 49) throw std::runtime_error("at most 49 original plant overrides are supported");

        for (const auto& [key, item] : plants->items()) {
            std::size_t consumed = 0;
            const int plantType = std::stoi(key, &consumed);
            if (consumed != key.size() || plantType < 0 || plantType > 48) {
                throw std::runtime_error("plant keys must be integer IDs between 0 and 48");
            }
            if (!item.is_object()) throw std::runtime_error("each plants entry must be an object");
            const auto animation = item.find("animationId");
            if (animation == item.end() || !animation->is_string()) {
                throw std::runtime_error("plants." + key + ".animationId must be a string");
            }
            PlantAnimationOverride override;
            override.plantType = plantType;
            override.animationId = animation->get<std::string>();
            if (!IsExternalResourceId(override.animationId)) {
                throw std::runtime_error(
                    "plants." + key + ".animationId must match [A-Za-z0-9_]+ and contain 1-64 characters");
            }
            if (!parsed.plants.emplace(plantType, std::move(override)).second) {
                throw std::runtime_error("plant IDs must be unique after numeric normalization");
            }
        }
        result.config = std::move(parsed);
    } catch (const std::exception& exception) {
        result.error = std::string("Plant animation override config validation failed: ") + exception.what();
    }
    return result;
}

}  // namespace pvzmod
