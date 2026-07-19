#pragma once

#include "zombie_config.h"

namespace pvzmod {

struct ArmorVisualAdapterInfo {
    ArmorSlot slot = ArmorSlot::Helmet;
    int gameArmorType = 0;
    int initializerZombieType = -1;
    int overlayReanimationType = -1;
    bool exclusiveBody = false;
    bool specialPlantHead = false;
};

[[nodiscard]] const ArmorVisualAdapterInfo& GetArmorVisualAdapterInfo(ArmorVisual visual);
[[nodiscard]] const char* ArmorVisualConfigName(ArmorVisual visual);
[[nodiscard]] bool RequiresOriginalZombieInitializer(ArmorVisual visual);
[[nodiscard]] bool UsesIndependentArmorOverlay(ArmorVisual visual);

}  // namespace pvzmod
