#pragma once

#include "spawn_config.h"

#include <array>
#include <cstddef>
#include <cstdint>

namespace pvzmod {

constexpr std::size_t kZombieTypeCount = 33;
constexpr std::size_t kWaveCount = 20;
constexpr std::size_t kWaveSlotCount = 50;
constexpr std::size_t kSpawnListCount = kWaveCount * kWaveSlotCount;

struct WaveApplyStats {
    std::size_t replacedSlots = 0;
    std::size_t preservedSpecialSlots = 0;
    std::size_t activeWaves = 0;
    std::array<std::size_t, kZombieTypeCount> generatedTypeCounts{};
};

[[nodiscard]] WaveApplyStats ApplyWeightedWaveOverride(
    std::array<int, kSpawnListCount>& spawnList,
    std::array<std::uint8_t, kZombieTypeCount>& allowedZombieTypes,
    const LevelSpawnConfig& config,
    int adventureLevel,
    int activeWaveCount);

}  // namespace pvzmod
