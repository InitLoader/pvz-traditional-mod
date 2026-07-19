#include "wave_multiplier.h"

#include "spawn_config.h"

#include <algorithm>
#include <cmath>
#include <numeric>
#include <random>
#include <vector>

namespace pvzmod {

WaveMultiplierApplyStats ApplyWaveCountMultipliers(
    std::array<int, kSpawnListCount>& spawnList,
    const WaveMultiplierConfig& config,
    const int adventureLevel,
    const int activeWaveCount) {
    WaveMultiplierApplyStats stats;
    const std::size_t wavesToProcess = std::clamp(activeWaveCount, 0, static_cast<int>(kWaveCount));
    stats.activeWaves = wavesToProcess;

    for (std::size_t wave = 0; wave < wavesToProcess; ++wave) {
        const std::size_t waveStart = wave * kWaveSlotCount;
        std::vector<int> original;
        original.reserve(kWaveSlotCount);
        for (std::size_t slot = 0; slot < kWaveSlotCount; ++slot) {
            const int zombieId = spawnList[waveStart + slot];
            if (zombieId == -1) {
                break;
            }
            original.push_back(zombieId);
        }

        stats.originalCounts[wave] = original.size();
        stats.finalCounts[wave] = original.size();
        const double multiplier = config.EffectiveMultiplier(adventureLevel, static_cast<int>(wave + 1));
        stats.effectiveMultipliers[wave] = multiplier;
        if (original.empty() || std::abs(multiplier - 1.0) <= 0.000001) {
            continue;
        }

        std::size_t desiredTotal = static_cast<std::size_t>(std::llround(original.size() * multiplier));
        desiredTotal = std::max<std::size_t>(1, desiredTotal);
        if (desiredTotal > kWaveSlotCount) {
            desiredTotal = kWaveSlotCount;
            ++stats.cappedWaves;
        }

        std::vector<std::size_t> scalableIndices;
        std::size_t preservedSpecialCount = 0;
        for (std::size_t index = 0; index < original.size(); ++index) {
            if (!config.scaleSpecialZombies && IsSpecialPlacementZombie(original[index])) {
                ++preservedSpecialCount;
            } else {
                scalableIndices.push_back(index);
            }
        }

        desiredTotal = std::max(desiredTotal, preservedSpecialCount);
        const std::size_t desiredScalableCount = desiredTotal - preservedSpecialCount;
        const std::uint32_t waveSeed = config.seed ^
            (static_cast<std::uint32_t>(adventureLevel) * 0x9E3779B9u) ^
            (static_cast<std::uint32_t>(wave + 1) * 0x85EBCA6Bu);
        std::mt19937 random(waveSeed);
        std::vector<int> result;
        result.reserve(desiredTotal);

        if (desiredScalableCount < scalableIndices.size()) {
            std::vector<std::size_t> shuffled = scalableIndices;
            std::shuffle(shuffled.begin(), shuffled.end(), random);
            shuffled.resize(desiredScalableCount);
            std::sort(shuffled.begin(), shuffled.end());

            std::size_t selectedPosition = 0;
            for (std::size_t index = 0; index < original.size(); ++index) {
                if (!config.scaleSpecialZombies && IsSpecialPlacementZombie(original[index])) {
                    result.push_back(original[index]);
                } else if (selectedPosition < shuffled.size() && shuffled[selectedPosition] == index) {
                    result.push_back(original[index]);
                    ++selectedPosition;
                }
            }
        } else {
            result = original;
            if (!scalableIndices.empty()) {
                std::uniform_int_distribution<std::size_t> choose(0, scalableIndices.size() - 1);
                while (result.size() < desiredTotal) {
                    result.push_back(original[scalableIndices[choose(random)]]);
                }
            }
        }

        if (result.size() == original.size() && result == original) {
            continue;
        }

        for (std::size_t slot = 0; slot < kWaveSlotCount; ++slot) {
            spawnList[waveStart + slot] = -1;
        }
        for (std::size_t slot = 0; slot < result.size(); ++slot) {
            spawnList[waveStart + slot] = result[slot];
        }

        stats.finalCounts[wave] = result.size();
        ++stats.changedWaves;
    }

    return stats;
}

void RebuildAllowedZombieTypes(
    const std::array<int, kSpawnListCount>& spawnList,
    std::array<std::uint8_t, kZombieTypeCount>& allowedZombieTypes,
    const int activeWaveCount) {
    allowedZombieTypes.fill(0);
    const std::size_t wavesToProcess = std::clamp(activeWaveCount, 0, static_cast<int>(kWaveCount));
    for (std::size_t wave = 0; wave < wavesToProcess; ++wave) {
        const std::size_t waveStart = wave * kWaveSlotCount;
        for (std::size_t slot = 0; slot < kWaveSlotCount; ++slot) {
            const int zombieId = spawnList[waveStart + slot];
            if (zombieId == -1) {
                break;
            }
            if (zombieId >= 0 && zombieId < static_cast<int>(kZombieTypeCount)) {
                allowedZombieTypes[static_cast<std::size_t>(zombieId)] = 1;
            }
        }
    }
}

}  // namespace pvzmod
