#include "elite_zombie_config.h"

#include "external_texture_config.h"

#include <algorithm>
#include <cmath>
#include <fstream>
#include <limits>
#include <stdexcept>
#include <unordered_set>

#include <nlohmann/json.hpp>

namespace pvzmod {
namespace {

using nlohmann::json;

int Integer(const json& object, const char* key, int fallback, int minimum, int maximum) {
    const auto found = object.find(key);
    if (found == object.end()) return fallback;
    if (!found->is_number_integer() && !found->is_number_unsigned()) {
        throw std::runtime_error(std::string(key) + " must be an integer");
    }
    const long long value = found->get<long long>();
    if (value < minimum || value > maximum) {
        throw std::runtime_error(std::string(key) + " must be between " + std::to_string(minimum) +
                                 " and " + std::to_string(maximum));
    }
    return static_cast<int>(value);
}

double Number(const json& object, const char* key, double fallback, double minimum, double maximum) {
    const auto found = object.find(key);
    if (found == object.end()) return fallback;
    if (!found->is_number()) throw std::runtime_error(std::string(key) + " must be a number");
    const double value = found->get<double>();
    if (!std::isfinite(value) || value < minimum || value > maximum) {
        throw std::runtime_error(std::string(key) + " must be between " + std::to_string(minimum) +
                                 " and " + std::to_string(maximum));
    }
    return value;
}

std::string String(const json& object, const char* key, std::string fallback = {}) {
    const auto found = object.find(key);
    if (found == object.end()) return fallback;
    if (!found->is_string()) throw std::runtime_error(std::string(key) + " must be a string");
    return found->get<std::string>();
}

std::uint64_t Mix64(std::uint64_t value) {
    value += 0x9E3779B97F4A7C15ULL;
    value = (value ^ (value >> 30U)) * 0xBF58476D1CE4E5B9ULL;
    value = (value ^ (value >> 27U)) * 0x94D049BB133111EBULL;
    return value ^ (value >> 31U);
}

}  // namespace

double EliteSkillBinding::Parameter(const std::string_view name, const double fallback) const {
    const auto found = parameters.find(std::string(name));
    return found == parameters.end() ? fallback : found->second;
}

bool EliteZombieDefinition::AllowsZombie(const int zombieType) const {
    return std::find(eligibleZombieIds.begin(), eligibleZombieIds.end(), zombieType) != eligibleZombieIds.end();
}

EliteZombieConfigLoadResult LoadEliteZombieConfig(const std::filesystem::path& path) {
    EliteZombieConfigLoadResult result;
    std::ifstream input(path, std::ios::binary);
    if (!input) {
        result.error = "Cannot open elite zombie config: " + path.string();
        return result;
    }
    try {
        const json root = json::parse(input, nullptr, true, true);
        if (!root.is_object()) throw std::runtime_error("root must be an object");
        EliteZombieConfig parsed;
        parsed.schemaVersion = root.value("schemaVersion", 1);
        if (parsed.schemaVersion != 1) throw std::runtime_error("unsupported schemaVersion; expected 1");
        const unsigned long long seed = root.value("seed", static_cast<unsigned long long>(parsed.seed));
        if (seed > std::numeric_limits<std::uint32_t>::max()) {
            throw std::runtime_error("seed must be between 0 and 4294967295");
        }
        parsed.seed = static_cast<std::uint32_t>(seed);
        const auto elites = root.find("elites");
        if (elites == root.end() || !elites->is_array()) throw std::runtime_error("elites must be an array");
        if (elites->size() > 128) throw std::runtime_error("at most 128 elite definitions are supported");

        std::unordered_set<int> runtimeIds;
        std::unordered_set<std::string> stringIds;
        for (const json& item : *elites) {
            if (!item.is_object()) throw std::runtime_error("each elites entry must be an object");
            EliteZombieDefinition elite;
            elite.runtimeId = Integer(item, "runtimeId", 0, 1, 65535);
            elite.id = String(item, "id");
            if (!IsExternalResourceId(elite.id)) {
                throw std::runtime_error("elite id must match [A-Za-z0-9_]+ and contain 1-64 characters");
            }
            if (!runtimeIds.insert(elite.runtimeId).second) throw std::runtime_error("runtimeId must be unique");
            if (!stringIds.insert(elite.id).second) throw std::runtime_error("elite id must be unique");
            elite.name = String(item, "name", elite.id);
            if (const auto enabled = item.find("enabled"); enabled != item.end()) {
                if (!enabled->is_boolean()) throw std::runtime_error("enabled must be true or false");
                elite.enabled = enabled->get<bool>();
            }
            elite.priority = Integer(item, "priority", 0, -1000000, 1000000);
            elite.chance = Number(item, "chance", 0.0, 0.0, 100.0);

            const auto eligible = item.find("eligibleZombieIds");
            if (eligible == item.end() || !eligible->is_array() || eligible->empty()) {
                throw std::runtime_error("eligibleZombieIds must be a non-empty array");
            }
            std::unordered_set<int> seenZombieIds;
            for (const json& value : *eligible) {
                if (!value.is_number_integer() && !value.is_number_unsigned()) {
                    throw std::runtime_error("eligibleZombieIds values must be integers");
                }
                const int zombieId = value.get<int>();
                if (zombieId < 0 || zombieId > 32) {
                    throw std::runtime_error("eligibleZombieIds values must be between 0 and 32");
                }
                if (!seenZombieIds.insert(zombieId).second) {
                    throw std::runtime_error("eligibleZombieIds values must be unique within an elite");
                }
                elite.eligibleZombieIds.push_back(zombieId);
            }

            if (const auto skills = item.find("skills"); skills != item.end()) {
                if (!skills->is_array()) throw std::runtime_error("skills must be an array");
                if (skills->size() > 16) throw std::runtime_error("at most 16 skills are allowed per elite");
                for (const json& skillItem : *skills) {
                    if (!skillItem.is_object()) throw std::runtime_error("each skills entry must be an object");
                    EliteSkillBinding skill;
                    skill.id = String(skillItem, "id");
                    if (!IsExternalResourceId(skill.id)) {
                        throw std::runtime_error("skill id must match [A-Za-z0-9_]+ and contain 1-64 characters");
                    }
                    if (const auto parameters = skillItem.find("parameters"); parameters != skillItem.end()) {
                        if (!parameters->is_object()) throw std::runtime_error("skill parameters must be an object");
                        for (auto parameter = parameters->cbegin(); parameter != parameters->cend(); ++parameter) {
                            if (!IsExternalResourceId(parameter.key()) || !parameter.value().is_number()) {
                                throw std::runtime_error("skill parameters require resource-style keys and numbers");
                            }
                            const double value = parameter.value().get<double>();
                            if (!std::isfinite(value) || value < -1000000.0 || value > 1000000.0) {
                                throw std::runtime_error("skill parameter values must be finite and bounded");
                            }
                            skill.parameters.emplace(parameter.key(), value);
                        }
                    }
                    elite.skills.push_back(std::move(skill));
                }
            }

            if (const auto visual = item.find("visual"); visual != item.end()) {
                if (!visual->is_object()) throw std::runtime_error("visual must be an object");
                if (const auto tint = visual->find("tint"); tint != visual->end()) {
                    if (!tint->is_object()) throw std::runtime_error("visual.tint must be an object");
                    elite.visual.tint = EliteTint{
                        Integer(*tint, "red", 255, 0, 255), Integer(*tint, "green", 255, 0, 255),
                        Integer(*tint, "blue", 255, 0, 255), Integer(*tint, "alpha", 255, 0, 255)};
                }
                elite.visual.overlayTextureId = String(*visual, "overlayTextureId");
                if (!elite.visual.overlayTextureId.empty() &&
                    !IsExternalResourceId(elite.visual.overlayTextureId)) {
                    throw std::runtime_error("visual.overlayTextureId must match [A-Za-z0-9_]+");
                }
                elite.visual.offsetX = Integer(*visual, "offsetX", 0, -1000, 1000);
                elite.visual.offsetY = Integer(*visual, "offsetY", 0, -1000, 1000);
            }
            parsed.elites.push_back(std::move(elite));
        }
        result.config = std::move(parsed);
    } catch (const std::exception& exception) {
        result.error = std::string("Elite zombie config validation failed: ") + exception.what();
    }
    return result;
}

bool EliteRollSucceeds(
    const std::uint32_t seed, const int zombieType, const std::uint32_t zombieInstanceId,
    const int runtimeId, const double chance) {
    if (chance <= 0.0) return false;
    if (chance >= 100.0) return true;
    std::uint64_t state = seed;
    state = Mix64(state ^ static_cast<std::uint32_t>(zombieType));
    state = Mix64(state ^ zombieInstanceId);
    state = Mix64(state ^ static_cast<std::uint32_t>(runtimeId));
    const double roll = static_cast<double>(state >> 11U) * (100.0 / 9007199254740992.0);
    return roll < chance;
}

const EliteZombieDefinition* PickEliteZombie(
    const EliteZombieConfig& config, const int zombieType, const std::uint32_t zombieInstanceId) {
    const EliteZombieDefinition* selected = nullptr;
    for (const EliteZombieDefinition& elite : config.elites) {
        if (!elite.enabled || !elite.AllowsZombie(zombieType) ||
            !EliteRollSucceeds(config.seed, zombieType, zombieInstanceId, elite.runtimeId, elite.chance)) {
            continue;
        }
        if (selected == nullptr || elite.priority > selected->priority ||
            (elite.priority == selected->priority && elite.runtimeId < selected->runtimeId)) {
            selected = &elite;
        }
    }
    return selected;
}

}  // namespace pvzmod
