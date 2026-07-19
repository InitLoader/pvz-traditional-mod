#include "wave_multiplier_config.h"

#include "spawn_config.h"

#include <cmath>
#include <fstream>
#include <limits>
#include <regex>
#include <stdexcept>

#include <nlohmann/json.hpp>

namespace pvzmod {
namespace {

using nlohmann::json;

double ReadMultiplier(const json& object, const char* field, const std::string& context, const double fallback) {
    const auto found = object.find(field);
    if (found == object.end()) {
        return fallback;
    }
    if (!found->is_number()) {
        throw std::runtime_error(context + ": " + field + " must be numeric");
    }
    const double value = found->get<double>();
    if (!std::isfinite(value) || value < 0.1 || value > 10.0) {
        throw std::runtime_error(context + ": " + field + " must be between 0.1 and 10.0");
    }
    return value;
}

std::optional<int> ParseWaveKey(const std::string& key) {
    static const std::regex pattern(R"(^([1-9]|1[0-9]|20)$)");
    if (!std::regex_match(key, pattern)) {
        return std::nullopt;
    }
    return std::stoi(key);
}

}  // namespace

double WaveMultiplierConfig::EffectiveMultiplier(const int adventureLevel, const int waveNumber) const {
    double result = globalCountMultiplier;
    const auto level = adventureLevels.find(adventureLevel);
    if (level == adventureLevels.end()) {
        return result;
    }

    result *= level->second.countMultiplier;
    const auto wave = level->second.waveMultipliers.find(waveNumber);
    if (wave != level->second.waveMultipliers.end()) {
        result *= wave->second;
    }
    return result;
}

bool WaveMultiplierConfig::HasEffectForLevel(const int adventureLevel, const int activeWaveCount) const {
    for (int wave = 1; wave <= activeWaveCount; ++wave) {
        if (std::abs(EffectiveMultiplier(adventureLevel, wave) - 1.0) > 0.000001) {
            return true;
        }
    }
    return false;
}

WaveMultiplierConfigLoadResult LoadWaveMultiplierConfig(const std::filesystem::path& path) {
    WaveMultiplierConfigLoadResult result;
    std::ifstream input(path, std::ios::binary);
    if (!input) {
        result.error = "Cannot open wave multiplier config: " + path.string();
        return result;
    }

    json root;
    try {
        input >> root;
    } catch (const std::exception& exception) {
        result.error = std::string("Invalid wave multiplier JSON: ") + exception.what();
        return result;
    }

    try {
        if (!root.is_object()) {
            throw std::runtime_error("root must be an object");
        }

        WaveMultiplierConfig parsed;
        parsed.schemaVersion = root.value("schemaVersion", 1);
        if (parsed.schemaVersion != 1) {
            throw std::runtime_error("unsupported schemaVersion; expected 1");
        }

        const auto global = root.find("global");
        if (global != root.end()) {
            if (!global->is_object()) {
                throw std::runtime_error("global must be an object");
            }
            parsed.globalCountMultiplier = ReadMultiplier(*global, "countMultiplier", "global", 1.0);
            parsed.scaleSpecialZombies = global->value("scaleSpecialZombies", false);

            const auto seed = global->find("seed");
            if (seed != global->end()) {
                if (!seed->is_number_unsigned() && !seed->is_number_integer()) {
                    throw std::runtime_error("global: seed must be an integer");
                }
                const auto value = seed->get<std::int64_t>();
                if (value < 0 || value > std::numeric_limits<std::uint32_t>::max()) {
                    throw std::runtime_error("global: seed must be between 0 and 4294967295");
                }
                parsed.seed = static_cast<std::uint32_t>(value);
            }
        }

        const auto levels = root.find("levels");
        if (levels != root.end()) {
            if (!levels->is_object()) {
                throw std::runtime_error("levels must be an object");
            }

            for (auto level = levels->begin(); level != levels->end(); ++level) {
                const std::string levelKey = level.key();
                const auto adventureLevel = ParseAdventureLevelKey(levelKey);
                if (!adventureLevel.has_value()) {
                    throw std::runtime_error("invalid adventure level key '" + levelKey + "'; expected 1-1 through 5-10");
                }
                if (!level.value().is_object()) {
                    throw std::runtime_error("level '" + levelKey + "' must be an object");
                }

                LevelWaveMultiplierConfig levelConfig;
                levelConfig.countMultiplier =
                    ReadMultiplier(level.value(), "countMultiplier", "level '" + levelKey + "'", 1.0);

                const auto waves = level.value().find("waves");
                if (waves != level.value().end()) {
                    if (!waves->is_object()) {
                        throw std::runtime_error("level '" + levelKey + "': waves must be an object");
                    }
                    for (auto wave = waves->begin(); wave != waves->end(); ++wave) {
                        const auto waveNumber = ParseWaveKey(wave.key());
                        if (!waveNumber.has_value()) {
                            throw std::runtime_error("level '" + levelKey + "': wave key must be 1 through 20");
                        }
                        if (!wave.value().is_number()) {
                            throw std::runtime_error("level '" + levelKey + "': wave multiplier must be numeric");
                        }
                        const double multiplier = wave.value().get<double>();
                        if (!std::isfinite(multiplier) || multiplier < 0.1 || multiplier > 10.0) {
                            throw std::runtime_error("level '" + levelKey + "': wave multiplier must be between 0.1 and 10.0");
                        }
                        levelConfig.waveMultipliers.emplace(*waveNumber, multiplier);
                    }
                }

                parsed.adventureLevels.emplace(*adventureLevel, std::move(levelConfig));
            }
        }

        if (parsed.scaleSpecialZombies) {
            result.warnings.push_back(
                "scaleSpecialZombies=true can duplicate or remove flag, yeti, bungee, imp, and boss slots.");
        }
        result.config = std::move(parsed);
    } catch (const std::exception& exception) {
        result.error = std::string("Wave multiplier config validation failed: ") + exception.what();
    }

    return result;
}

}  // namespace pvzmod
