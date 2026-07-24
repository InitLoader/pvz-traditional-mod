#include "custom_plant_animation_runtime.h"

#include "external_body_animation_runtime.h"
#include "logger.h"
#include "plant_catalog_runtime.h"

#include <cstddef>
#include <cstdint>
#include <string>

namespace pvzmod {
namespace {

constexpr std::ptrdiff_t kPlantLawnAppOffset = 0x00;
constexpr std::ptrdiff_t kPlantBodyReanimationIdOffset = 0x94;

}  // namespace

bool InitializeCustomPlantAnimationRuntime(std::uint8_t*) {
    bool success = true;
    for (int index = 0; index < CustomChooserPlantCount(); ++index) {
        const CustomPlantDefinition* plant = CustomPlantAt(index);
        if (plant == nullptr || plant->animationId.empty()) continue;
        if (!RegisterExternalBodyAnimation(plant->animationId)) success = false;
    }
    if (success) {
        LogInfo("Registered custom-plant body animations without global action hooks.");
    }
    return true;
}

bool ApplyCustomPlantAnimation(void* plant, const CustomPlantDefinition& definition) {
    return ApplyExternalBodyAnimation(
        plant,
        kPlantLawnAppOffset,
        kPlantBodyReanimationIdOffset,
        definition.animationId,
        "custom plant " + std::to_string(definition.id));
}

}  // namespace pvzmod
