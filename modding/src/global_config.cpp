#include "global_config.h"

#include <fstream>
#include <stdexcept>

#include <nlohmann/json.hpp>

namespace pvzmod {
namespace {

using nlohmann::json;

int ReadSunValue(const json& object, const char* field, const int fallback) {
    const auto found = object.find(field);
    if (found == object.end()) {
        return fallback;
    }
    if (!found->is_number_integer() && !found->is_number_unsigned()) {
        throw std::runtime_error(std::string("economy.sunPickupValues.") + field + " must be an integer");
    }
    const auto value = found->get<long long>();
    if (value < 0 || value > 10000) {
        throw std::runtime_error(std::string("economy.sunPickupValues.") + field + " must be between 0 and 10000");
    }
    return static_cast<int>(value);
}

}  // namespace

GlobalConfigLoadResult LoadGlobalConfig(const std::filesystem::path& path) {
    GlobalConfigLoadResult result;
    std::ifstream input(path, std::ios::binary);
    if (!input) {
        result.error = "Cannot open global config: " + path.string();
        return result;
    }

    json root;
    try {
        input >> root;
    } catch (const std::exception& exception) {
        result.error = std::string("Invalid global config JSON: ") + exception.what();
        return result;
    }

    try {
        if (!root.is_object()) {
            throw std::runtime_error("root must be an object");
        }

        GlobalConfig parsed;
        parsed.schemaVersion = root.value("schemaVersion", 1);
        if (parsed.schemaVersion != 1) {
            throw std::runtime_error("unsupported schemaVersion; expected 1");
        }

        const auto economy = root.find("economy");
        if (economy != root.end()) {
            if (!economy->is_object()) {
                throw std::runtime_error("economy must be an object");
            }
            const auto sunValues = economy->find("sunPickupValues");
            if (sunValues != economy->end()) {
                if (!sunValues->is_object()) {
                    throw std::runtime_error("economy.sunPickupValues must be an object");
                }
                parsed.sunPickupValues.normal = ReadSunValue(*sunValues, "normal", parsed.sunPickupValues.normal);
                parsed.sunPickupValues.small = ReadSunValue(*sunValues, "small", parsed.sunPickupValues.small);
                parsed.sunPickupValues.large = ReadSunValue(*sunValues, "large", parsed.sunPickupValues.large);
            }
        }

        result.config = parsed;
    } catch (const std::exception& exception) {
        result.error = std::string("Global config validation failed: ") + exception.what();
    }
    return result;
}

int SunValueForCoinType(const SunPickupValues& values, const int coinType) {
    switch (coinType) {
        case 4:
            return values.normal;
        case 5:
            return values.small;
        case 6:
            return values.large;
        default:
            return 0;
    }
}

int SunTotalAfterPickup(const int beforePickup, const int pickupValue) {
    const long long total = static_cast<long long>(beforePickup) + pickupValue;
    if (total < 0) {
        return 0;
    }
    if (total > 9990) {
        return 9990;
    }
    return static_cast<int>(total);
}

}  // namespace pvzmod
