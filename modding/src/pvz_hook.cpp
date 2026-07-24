#include "pvz_hook.h"

#include "hook_modules.h"
#include "hook_utils.h"
#include "logger.h"
#include "external_animation_runtime.h"
#include "external_body_animation_runtime.h"
#include "external_texture_runtime.h"
#include "plant_catalog_runtime.h"

#include <MinHook.h>

#include <cstdint>

namespace pvzmod {

bool InstallPvZHooks() {
    std::uint8_t* moduleBase = nullptr;
    if (!GetSupportedExecutableBase(moduleBase) || !InitializeHookEngine()) {
        return false;
    }

    bool success = true;
    if (!InitializeExternalTextureRuntime(moduleBase)) {
        LogError("External texture runtime failed to initialize.");
        success = false;
    }
    if (!InitializeExternalAnimationRuntime(moduleBase)) {
        LogError("One or more external animations failed validation; animation injection remains inactive.");
        success = false;
    }
    if (!InitializeExternalBodyAnimationRuntime(moduleBase)) {
        LogError("External body animation runtime failed to initialize.");
        success = false;
    }
    if (!InstallEliteZombieHooks(moduleBase)) {
        LogError("Elite zombie hook module failed to install.");
        success = false;
    }
    if (!InstallWaveHooks(moduleBase)) {
        LogError("Wave hook module failed to install.");
        success = false;
    }
    if (!InstallSunHooks(moduleBase)) {
        LogError("Sun hook module failed to install.");
        success = false;
    }
    if (!InstallPlantAttackHooks(moduleBase)) {
        LogError("Plant attack hook module failed to install.");
        success = false;
    }
    if (!InstallZombieHooks(moduleBase)) {
        LogError("Zombie attribute hook module failed to install.");
        success = false;
    }
    if (!InstallCustomZombieAnimationRuntime(moduleBase)) {
        LogError("Custom zombie animation runtime failed to install.");
        success = false;
    }
    if (!InstallSeedUiHooks(moduleBase)) {
        LogError("Seed chooser UI hook module failed to install.");
        success = false;
    }
    if (CustomChooserPlantCount() > 0) {
        if (!InstallCustomPlantHooks(moduleBase)) {
            LogError("Custom plant hook module failed to install.");
            success = false;
        }
        if (!InstallCustomPlantTextHooks(moduleBase)) {
            LogError("Custom plant text hook module failed to install.");
            success = false;
        }
    } else {
        LogInfo("Custom plant catalog is empty; plant instance, fire, projectile, and text hooks are disabled.");
    }

    if (success) {
        LogInfo("Installed wave, sun, plant attack, zombie, elite, external texture/body-animation registry, and seed chooser modules for PvZ 1.0.0.1051.");
    }
    return success;
}

DWORD WINAPI InitializeModThread(void* moduleParameter) {
    InitializeLogger(static_cast<HMODULE>(moduleParameter));
    LogInfo("pvzmod.dll loaded; mod runtime version 0.10.4-dev.");
    if (!InstallPvZHooks()) {
        LogError("One or more isolated hook modules are inactive; see earlier log entries.");
    }
    return 0;
}

}  // namespace pvzmod
