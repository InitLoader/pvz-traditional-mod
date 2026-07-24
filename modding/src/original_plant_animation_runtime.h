#pragma once

#include "plant_animation_override_config.h"

#include <cstdint>

namespace pvzmod {

[[nodiscard]] bool InitializeOriginalPlantAnimationRuntime(std::uint8_t* moduleBase);
[[nodiscard]] int OriginalPlantAnimationOverrideCount();
[[nodiscard]] const PlantAnimationOverride* FindOriginalPlantAnimationOverride(int plantType);
[[nodiscard]] bool ApplyOriginalPlantAnimation(
    void* plant, const PlantAnimationOverride& definition);

}  // namespace pvzmod
