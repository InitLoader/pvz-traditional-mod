#pragma once

#include "custom_plant_config.h"

#include <cstdint>

namespace pvzmod {

// Registers configured plant body animations with the shared, exact-version
// Reanimation runtime. Plant catalog policy remains isolated in this module.
[[nodiscard]] bool InitializeCustomPlantAnimationRuntime(std::uint8_t* moduleBase);

// Replaces the already-created template body Reanimation with the external
// Definition selected by definition.animationId. The original animation is
// retained when validation or resource preparation fails.
[[nodiscard]] bool ApplyCustomPlantAnimation(
    void* plant, const CustomPlantDefinition& definition);

}  // namespace pvzmod
