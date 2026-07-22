#pragma once

#include "custom_plant_config.h"

#include <cstdint>

namespace pvzmod {

// Verifies the PvZ 1.0.0.1051 Reanimation ABI entry points used by custom
// plants. This module intentionally owns animation-object manipulation instead
// of coupling it to the seed, projectile, or catalog hooks.
[[nodiscard]] bool InitializeCustomPlantAnimationRuntime(std::uint8_t* moduleBase);

// Replaces the already-created template body Reanimation with the external
// Definition selected by definition.animationId. The original animation is
// retained when validation or resource preparation fails.
[[nodiscard]] bool ApplyCustomPlantAnimation(
    void* plant, const CustomPlantDefinition& definition);

}  // namespace pvzmod
