#pragma once

#include <cstdint>
#include <filesystem>
#include <optional>
#include <string>
#include <unordered_map>
#include <vector>

namespace pvzmod {

struct LevelWaveMultiplierConfig {
    double countMultiplier = 1.0;
    std::unordered_map<int, double> waveMultipliers;
};

struct WaveMultiplierConfig {
    int schemaVersion = 1;
    double globalCountMultiplier = 1.0;
    bool scaleSpecialZombies = false;
    std::uint32_t seed = 0x57415645u;
    std::unordered_map<int, LevelWaveMultiplierConfig> adventureLevels;

    [[nodiscard]] double EffectiveMultiplier(int adventureLevel, int waveNumber) const;
    [[nodiscard]] bool HasEffectForLevel(int adventureLevel, int activeWaveCount) const;
};

struct WaveMultiplierConfigLoadResult {
    std::optional<WaveMultiplierConfig> config;
    std::vector<std::string> warnings;
    std::string error;

    [[nodiscard]] bool Ok() const { return config.has_value(); }
};

[[nodiscard]] WaveMultiplierConfigLoadResult LoadWaveMultiplierConfig(const std::filesystem::path& path);

}  // namespace pvzmod
