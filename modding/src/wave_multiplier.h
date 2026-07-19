#pragma once

#include "wave_generator.h"
#include "wave_multiplier_config.h"

#include <array>
#include <cstddef>

namespace pvzmod {

struct WaveMultiplierApplyStats {
    std::size_t activeWaves = 0;
    std::size_t changedWaves = 0;
    std::size_t cappedWaves = 0;
    std::array<std::size_t, kWaveCount> originalCounts{};
    std::array<std::size_t, kWaveCount> finalCounts{};
    std::array<double, kWaveCount> effectiveMultipliers{};
};

[[nodiscard]] WaveMultiplierApplyStats ApplyWaveCountMultipliers(
    std::array<int, kSpawnListCount>& spawnList,
    const WaveMultiplierConfig& config,
    int adventureLevel,
    int activeWaveCount);

void RebuildAllowedZombieTypes(
    const std::array<int, kSpawnListCount>& spawnList,
    std::array<std::uint8_t, kZombieTypeCount>& allowedZombieTypes,
    int activeWaveCount);

}  // namespace pvzmod
