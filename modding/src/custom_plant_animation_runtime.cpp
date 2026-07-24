#include "custom_plant_animation_runtime.h"

#include "external_body_animation_runtime.h"
#include "logger.h"
#include "plant_catalog_runtime.h"
#include "reanimation_carrier_catalog.h"

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
        const int carrier = ResolvePlantTemplateCarrierReanimationType(plant->templatePlantId);
        if (carrier < 0 ||
            !RegisterExternalBodyAnimationForCarrier(plant->animationId, carrier)) success = false;
        else {
            // Older editor builds could write a different metadata carrier and
            // saves made with those builds may contain either value. Keep that
            // declaration as a best-effort migration alias while the actual
            // template carrier remains authoritative for new instances.
            static_cast<void>(RegisterExternalBodyAnimation(plant->animationId));
        }
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
