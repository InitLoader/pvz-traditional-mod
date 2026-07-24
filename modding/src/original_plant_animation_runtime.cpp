#include "original_plant_animation_runtime.h"

#include "external_body_animation_runtime.h"
#include "logger.h"
#include "reanimation_carrier_catalog.h"

#include <cstddef>
#include <filesystem>
#include <string>

namespace pvzmod {
namespace {

constexpr std::ptrdiff_t kPlantLawnAppOffset = 0x00;
constexpr std::ptrdiff_t kPlantBodyReanimationIdOffset = 0x94;

PlantAnimationOverrideConfig g_activeOverrides;

}  // namespace

bool InitializeOriginalPlantAnimationRuntime(std::uint8_t*) {
    const std::filesystem::path path =
        ModuleDirectory() / L"pvzmod" / L"config" / L"plants" / L"attributes.jsonc";
    const PlantAnimationOverrideConfigLoadResult loaded = LoadPlantAnimationOverrideConfig(path);
    if (!loaded.Ok()) {
        LogWarning(loaded.error + "; original plant body animations remain unchanged.");
        return true;
    }

    for (const auto& [plantType, override] : loaded.config->plants) {
        const int carrier = ResolvePlantTemplateCarrierReanimationType(plantType);
        if (carrier < 0 ||
            !RegisterExternalBodyAnimationForCarrier(override.animationId, carrier, false)) {
            LogWarning("Original plant type " + std::to_string(plantType) + " animationId '" +
                       override.animationId + "' is inactive; its original body animation will be kept.");
            continue;
        }
        g_activeOverrides.plants.emplace(plantType, override);
    }
    LogInfo("Registered " + std::to_string(g_activeOverrides.plants.size()) +
            " sparse original-plant body animation override(s).");
    return true;
}

int OriginalPlantAnimationOverrideCount() {
    return static_cast<int>(g_activeOverrides.plants.size());
}

const PlantAnimationOverride* FindOriginalPlantAnimationOverride(const int plantType) {
    return g_activeOverrides.FindPlant(plantType);
}

bool ApplyOriginalPlantAnimation(void* plant, const PlantAnimationOverride& definition) {
    return ApplyExternalBodyAnimation(
        plant,
        kPlantLawnAppOffset,
        kPlantBodyReanimationIdOffset,
        definition.animationId,
        "original plant type " + std::to_string(definition.plantType));
}

}  // namespace pvzmod
