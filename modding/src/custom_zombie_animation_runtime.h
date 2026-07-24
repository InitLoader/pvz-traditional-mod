#pragma once

#include <cstdint>

namespace pvzmod {

// Registers sparse zombie animation overrides and subscribes to the existing
// post-ZombieInitialize event. It does not own zombie AI or armor policy.
[[nodiscard]] bool InstallCustomZombieAnimationRuntime(std::uint8_t* moduleBase);

}  // namespace pvzmod
