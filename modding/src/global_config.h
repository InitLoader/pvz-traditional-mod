#pragma once

#include <filesystem>
#include <optional>
#include <string>

namespace pvzmod {

struct SunPickupValues {
    int normal = 25;
    int small = 15;
    int large = 50;
};

struct GlobalConfig {
    int schemaVersion = 1;
    SunPickupValues sunPickupValues;
};

struct GlobalConfigLoadResult {
    std::optional<GlobalConfig> config;
    std::string error;

    [[nodiscard]] bool Ok() const { return config.has_value(); }
};

[[nodiscard]] GlobalConfigLoadResult LoadGlobalConfig(const std::filesystem::path& path);
[[nodiscard]] int SunValueForCoinType(const SunPickupValues& values, int coinType);
[[nodiscard]] int SunTotalAfterPickup(int beforePickup, int pickupValue);

}  // namespace pvzmod
