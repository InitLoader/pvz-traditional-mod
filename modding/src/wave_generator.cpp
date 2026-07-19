#include "wave_generator.h"

#include <algorithm>
#include <random>
#include <vector>

namespace pvzmod {

WaveApplyStats ApplyWeightedWaveOverride(
    std::array<int, kSpawnListCount>& spawnList,
    std::array<std::uint8_t, kZombieTypeCount>& allowedZombieTypes,
    const LevelSpawnConfig& config,
    const int adventureLevel,
    const int activeWaveCount) {
    WaveApplyStats stats;
    allowedZombieTypes.fill(0);

    const std::size_t wavesToProcess = std::clamp(activeWaveCount, 0, static_cast<int>(kWaveCount));
    stats.activeWaves = wavesToProcess;

    std::vector<double> weights;
    weights.reserve(config.zombies.size());
    for (const ZombieWeight& zombie : config.zombies) {
        weights.push_back(zombie.weight);
        allowedZombieTypes[static_cast<std::size_t>(zombie.id)] = 1;
    }

    const std::uint32_t mixedSeed =
        config.seed ^ (static_cast<std::uint32_t>(adventureLevel) * 0x9E3779B9u) ^ 0xA511E9B3u;
    std::mt19937 random(mixedSeed);
    std::discrete_distribution<std::size_t> choose(weights.begin(), weights.end());

    std::vector<std::size_t> replaceableSlots;
    replaceableSlots.reserve(wavesToProcess * kWaveSlotCount);

    for (std::size_t wave = 0; wave < wavesToProcess; ++wave) {
        const std::size_t waveStart = wave * kWaveSlotCount;
        for (std::size_t slot = 0; slot < kWaveSlotCount; ++slot) {
            int& zombieId = spawnList[waveStart + slot];
            if (zombieId == -1) {
                break;
            }

            if (config.preserveOriginalSpecialZombies && IsSpecialPlacementZombie(zombieId)) {
                if (zombieId >= 0 && zombieId < static_cast<int>(kZombieTypeCount)) {
                    allowedZombieTypes[static_cast<std::size_t>(zombieId)] = 1;
                    ++stats.generatedTypeCounts[static_cast<std::size_t>(zombieId)];
                }
                ++stats.preservedSpecialSlots;
                continue;
            }

            replaceableSlots.push_back(waveStart + slot);
        }
    }

    std::shuffle(replaceableSlots.begin(), replaceableSlots.end(), random);
    std::size_t forcedSlotCount = 0;
    for (const ZombieWeight& zombie : config.zombies) {
        for (std::size_t count = 0;
             count < zombie.minimumCount && forcedSlotCount < replaceableSlots.size();
             ++count, ++forcedSlotCount) {
            const std::size_t slotIndex = replaceableSlots[forcedSlotCount];
            spawnList[slotIndex] = zombie.id;
            ++stats.generatedTypeCounts[static_cast<std::size_t>(zombie.id)];
        }
    }

    for (std::size_t index = forcedSlotCount; index < replaceableSlots.size(); ++index) {
        const int zombieId = config.zombies[choose(random)].id;
        spawnList[replaceableSlots[index]] = zombieId;
        ++stats.generatedTypeCounts[static_cast<std::size_t>(zombieId)];
    }
    stats.replacedSlots = replaceableSlots.size();

    return stats;
}

}  // namespace pvzmod
