#pragma once

#include "custom_plant_config.h"
#include "seed_ui_config.h"

#include <cstddef>
#include <cstdint>
#include <optional>

namespace pvzmod {

constexpr int kOriginalPlantTypeCount = 49;
constexpr int kCustomPlantCardsPerPage = 48;
constexpr int kMaximumCustomPlantCount = 512;

[[nodiscard]] bool InitializePlantCatalogRuntime();
[[nodiscard]] int ConfiguredSeedSlotCount(int originalCount);
[[nodiscard]] int CustomChooserPlantCount();
[[nodiscard]] const CustomPlantDefinition* CustomPlantAt(int index);
[[nodiscard]] const CustomPlantDefinition* FindCustomPlantByChooserType(int seedType);
[[nodiscard]] const CustomPlantDefinition* FindCustomPlantByLogicalId(int logicalId);
[[nodiscard]] const CustomPlantDefinition* ResolveCustomPlant(int seedType, int marker);
[[nodiscard]] int CustomPlantChooserType(const CustomPlantDefinition& definition);
[[nodiscard]] const SeedUiConfig& CurrentSeedUiConfig();

}  // namespace pvzmod
