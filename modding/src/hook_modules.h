#pragma once

#include <cstdint>

namespace pvzmod {

[[nodiscard]] bool InstallWaveHooks(std::uint8_t* moduleBase);
[[nodiscard]] bool InstallSunHooks(std::uint8_t* moduleBase);
[[nodiscard]] bool InstallPlantAttackHooks(std::uint8_t* moduleBase);
[[nodiscard]] bool InstallZombieHooks(std::uint8_t* moduleBase);
[[nodiscard]] bool InstallEliteZombieHooks(std::uint8_t* moduleBase);
[[nodiscard]] bool InstallSeedUiHooks(std::uint8_t* moduleBase);
[[nodiscard]] bool InstallCustomPlantHooks(std::uint8_t* moduleBase);
[[nodiscard]] bool InstallCustomPlantTextHooks(std::uint8_t* moduleBase);

}  // namespace pvzmod
