#include "plant_catalog_runtime.h"

#include "logger.h"

#include <filesystem>
#include <memory>
#include <mutex>
#include <vector>

namespace pvzmod {
namespace {

std::once_flag g_initializeOnce;
SeedUiConfig g_seedUi;
std::vector<CustomPlantDefinition> g_visiblePlants;
bool g_initialized = false;

void LoadRuntime() {
    const std::filesystem::path root = ModuleDirectory() / L"pvzmod" / L"config";
    const SeedUiConfigLoadResult ui = LoadSeedUiConfig(root / L"ui" / L"seed_chooser.jsonc");
    if (ui.Ok()) {
        g_seedUi = *ui.config;
        LogInfo("Loaded seed chooser UI config; slotCount=" +
                (g_seedUi.slotCount ? std::to_string(*g_seedUi.slotCount) : std::string("original")) + '.');
    } else {
        LogWarning(ui.error + "; original seed-bank slot count is used.");
    }

    const CustomPlantConfigLoadResult plants =
        LoadCustomPlantConfig(root / L"plants" / L"custom_plants.jsonc");
    if (plants.Ok()) {
        for (const CustomPlantDefinition& plant : plants.config->plants) {
            if (plant.unlocked) g_visiblePlants.push_back(plant);
        }
        LogInfo("Loaded " + std::to_string(g_visiblePlants.size()) +
                " unlocked custom plant card(s); configuration is applied on the next chooser screen.");
    } else {
        LogWarning(plants.error + "; no custom plant cards are added.");
    }
    g_initialized = true;
}

}  // namespace

bool InitializePlantCatalogRuntime() {
    std::call_once(g_initializeOnce, LoadRuntime);
    return g_initialized;
}

int ConfiguredSeedSlotCount(const int originalCount) {
    if (originalCount < 6 || originalCount > 10 || !g_seedUi.slotCount) return originalCount;
    return *g_seedUi.slotCount;
}

int CustomChooserPlantCount() {
    return static_cast<int>(g_visiblePlants.size());
}

const CustomPlantDefinition* FindCustomPlantByChooserType(const int seedType) {
    const int index = seedType - kOriginalPlantTypeCount;
    return index >= 0 && index < static_cast<int>(g_visiblePlants.size()) ? &g_visiblePlants[index] : nullptr;
}

const CustomPlantDefinition* CustomPlantAt(const int index) {
    return index >= 0 && index < static_cast<int>(g_visiblePlants.size()) ? &g_visiblePlants[index] : nullptr;
}

const CustomPlantDefinition* FindCustomPlantByLogicalId(const int logicalId) {
    for (const CustomPlantDefinition& plant : g_visiblePlants) {
        if (plant.id == logicalId) return &plant;
    }
    return nullptr;
}

const CustomPlantDefinition* ResolveCustomPlant(const int seedType, const int marker) {
    if (const CustomPlantDefinition* plant = FindCustomPlantByLogicalId(marker)) return plant;
    return FindCustomPlantByChooserType(seedType);
}

int CustomPlantChooserType(const CustomPlantDefinition& definition) {
    for (std::size_t i = 0; i < g_visiblePlants.size(); ++i) {
        if (&g_visiblePlants[i] == &definition || g_visiblePlants[i].id == definition.id) {
            return kOriginalPlantTypeCount + static_cast<int>(i);
        }
    }
    return -1;
}

const SeedUiConfig& CurrentSeedUiConfig() {
    return g_seedUi;
}

}  // namespace pvzmod
