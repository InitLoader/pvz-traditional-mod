#include "custom_plant_hook.h"

#include "custom_plant_animation_runtime.h"
#include "hook_modules.h"
#include "hook_utils.h"
#include "logger.h"
#include "original_plant_animation_runtime.h"
#include "plant_catalog_runtime.h"

#include <Windows.h>

#include <algorithm>
#include <array>
#include <cstdint>
#include <limits>
#include <mutex>
#include <string>
#include <unordered_map>

extern "C" void* g_originalPlantGetCost = nullptr;
extern "C" void* g_originalPlantGetRefreshTime = nullptr;
extern "C" void* g_originalPlantInitialize = nullptr;
extern "C" void* g_originalPlantUpdate = nullptr;
extern "C" void* g_originalPlantFire = nullptr;
extern "C" void* g_originalProjectileInitialize = nullptr;

extern "C" void PlantGetCostDetour();
extern "C" void PlantGetRefreshTimeDetour();
extern "C" void PlantInitializeDetour();
extern "C" void PlantUpdateDetour();
extern "C" void ProjectileInitializeDetour();
extern "C" void __stdcall PlantFireDetour(void* plant, void* target, int row, int weapon);

extern "C" int __stdcall ResolveCustomPlantCost(int seedType, int marker);
extern "C" int __stdcall ResolveCustomPlantRefresh(int seedType, int marker);
extern "C" unsigned long long __stdcall PrepareCustomPlant(void* plant, int seedType, int marker);
extern "C" void __stdcall ApplyPendingCustomPlant(void* plant);
extern "C" int __stdcall PrepareCustomProjectile(void* projectile, int projectileType);

namespace pvzmod {
namespace {

constexpr int kNoCustomValue = (std::numeric_limits<int>::min)();
constexpr std::uintptr_t kPlantGetCostRva = 0x00067B00;
constexpr std::uintptr_t kPlantGetRefreshTimeRva = 0x00067E30;
constexpr std::uintptr_t kPlantInitializeRva = 0x0005DB60;
constexpr std::uintptr_t kPlantUpdateRva = 0x00063E40;
constexpr std::uintptr_t kPlantFireRva = 0x00066E00;
constexpr std::uintptr_t kProjectileInitializeRva = 0x0006C730;
constexpr std::uintptr_t kProjectileDirectDamageReturnRva = 0x0006E080;
constexpr std::uintptr_t kProjectileSplashDamageReturnRva = 0x0006D46D;

constexpr std::array<std::uint8_t, 12> kPlantGetCostPrologue =
    {0x8B, 0x0D, 0xC0, 0x9E, 0x6A, 0x00, 0x8B, 0x89, 0xF8, 0x07, 0x00, 0x00};
constexpr std::array<std::uint8_t, 12> kPlantGetRefreshTimePrologue =
    {0x8B, 0xC1, 0xE8, 0xF9, 0x36, 0xFC, 0xFF, 0x84, 0xC0, 0x74, 0x03, 0x33};
constexpr std::array<std::uint8_t, 12> kPlantInitializePrologue =
    {0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8, 0x83, 0xEC, 0x1C, 0x53, 0x8B, 0x5D};
constexpr std::array<std::uint8_t, 12> kPlantUpdatePrologue =
    {0x53, 0x8B, 0xD8, 0x8A, 0x93, 0x44, 0x01, 0x00, 0x00, 0x84, 0xD2, 0x74};
constexpr std::array<std::uint8_t, 12> kPlantFirePrologue =
    {0x83, 0xEC, 0x1C, 0x53, 0x8B, 0x5C, 0x24, 0x30, 0x55, 0x8B, 0x6C, 0x24};
constexpr std::array<std::uint8_t, 12> kProjectileInitializePrologue =
    {0x53, 0x55, 0x8B, 0x6C, 0x24, 0x0C, 0x8B, 0x5D, 0x04, 0x56, 0x57, 0x8B};

struct PlantRuntimeState {
    const CustomPlantDefinition* definition = nullptr;
    const PlantAnimationOverride* originalAnimation = nullptr;
    bool pending = true;
};

struct ProjectileRuntimeState {
    int logicalPlantId = -1;
    int damage = 20;
};

std::mutex g_stateMutex;
std::unordered_map<void*, PlantRuntimeState> g_plants;
std::unordered_map<void*, ProjectileRuntimeState> g_projectiles;
thread_local const CustomPlantDefinition* g_firingPlant = nullptr;

const CustomPlantDefinition* PlantDefinitionFor(void* plant) {
    std::lock_guard lock(g_stateMutex);
    const auto it = g_plants.find(plant);
    return it == g_plants.end() ? nullptr : it->second.definition;
}

}  // namespace

bool InstallCustomPlantHooks(std::uint8_t* moduleBase) {
    if (!InitializePlantCatalogRuntime()) return false;
    if (!InitializeCustomPlantAnimationRuntime(moduleBase)) return false;
    if (!InitializeOriginalPlantAnimationRuntime(moduleBase)) return false;
    const bool hasCustomPlants = CustomChooserPlantCount() > 0;
    const bool hasOriginalOverrides = OriginalPlantAnimationOverrideCount() > 0;
    if (!hasCustomPlants && !hasOriginalOverrides) {
        LogInfo("Custom plant catalog and original-plant animation override table are empty; plant hooks are disabled.");
        return true;
    }

    if (!VerifyHookTarget(moduleBase, kPlantInitializeRva, kPlantInitializePrologue, "Plant::Initialize") ||
        !VerifyHookTarget(moduleBase, kPlantUpdateRva, kPlantUpdatePrologue, "Plant::Update")) {
        return false;
    }
    if (hasCustomPlants &&
        (!VerifyHookTarget(moduleBase, kPlantGetCostRva, kPlantGetCostPrologue, "Plant::GetCost") ||
         !VerifyHookTarget(moduleBase, kPlantGetRefreshTimeRva, kPlantGetRefreshTimePrologue, "Plant::GetRefreshTime") ||
         !VerifyHookTarget(moduleBase, kPlantFireRva, kPlantFirePrologue, "Plant::Fire") ||
         !VerifyHookTarget(moduleBase, kProjectileInitializeRva, kProjectileInitializePrologue, "Projectile::Initialize"))) {
        return false;
    }
    if (!CreateAndEnableHook(moduleBase, kPlantInitializeRva, reinterpret_cast<void*>(&PlantInitializeDetour),
                             &g_originalPlantInitialize, "Plant::Initialize") ||
        !CreateAndEnableHook(moduleBase, kPlantUpdateRva, reinterpret_cast<void*>(&PlantUpdateDetour),
                             &g_originalPlantUpdate, "Plant::Update")) {
        return false;
    }
    if (hasCustomPlants &&
        (!CreateAndEnableHook(moduleBase, kPlantGetCostRva, reinterpret_cast<void*>(&PlantGetCostDetour),
                              &g_originalPlantGetCost, "Plant::GetCost") ||
         !CreateAndEnableHook(moduleBase, kPlantGetRefreshTimeRva, reinterpret_cast<void*>(&PlantGetRefreshTimeDetour),
                              &g_originalPlantGetRefreshTime, "Plant::GetRefreshTime") ||
         !CreateAndEnableHook(moduleBase, kPlantFireRva, reinterpret_cast<void*>(&PlantFireDetour),
                              &g_originalPlantFire, "Plant::Fire") ||
         !CreateAndEnableHook(moduleBase, kProjectileInitializeRva, reinterpret_cast<void*>(&ProjectileInitializeDetour),
                              &g_originalProjectileInitialize, "Projectile::Initialize"))) {
        return false;
    }
    LogInfo("Installed plant instance hooks for " +
            std::to_string(OriginalPlantAnimationOverrideCount()) +
            " original animation override(s) and " +
            std::to_string(CustomChooserPlantCount()) + " custom plant(s).");
    return true;
}

std::optional<int> ResolveCustomProjectileDamage(
    void* projectile, const std::uintptr_t damageReturnRva, const int originalDamage) {
    if (damageReturnRva != kProjectileDirectDamageReturnRva &&
        damageReturnRva != kProjectileSplashDamageReturnRva) return std::nullopt;
    std::lock_guard lock(g_stateMutex);
    const auto it = g_projectiles.find(projectile);
    if (it == g_projectiles.end()) return std::nullopt;
    if (damageReturnRva == kProjectileDirectDamageReturnRva) return it->second.damage;
    return originalDamage > 0 ? std::max(1, it->second.damage / 3) : 0;
}

}  // namespace pvzmod

extern "C" int __stdcall ResolveCustomPlantCost(const int seedType, const int marker) {
    const pvzmod::CustomPlantDefinition* plant = pvzmod::ResolveCustomPlant(seedType, marker);
    return plant ? plant->cost : pvzmod::kNoCustomValue;
}

extern "C" int __stdcall ResolveCustomPlantRefresh(const int seedType, const int marker) {
    const pvzmod::CustomPlantDefinition* plant = pvzmod::ResolveCustomPlant(seedType, marker);
    return plant ? plant->rechargeTime : pvzmod::kNoCustomValue;
}

extern "C" unsigned long long __stdcall PrepareCustomPlant(void* plant, const int seedType, const int marker) {
    const pvzmod::CustomPlantDefinition* definition = pvzmod::ResolveCustomPlant(seedType, marker);
    const pvzmod::PlantAnimationOverride* originalAnimation = definition == nullptr
        ? pvzmod::FindOriginalPlantAnimationOverride(seedType)
        : nullptr;
    {
        std::lock_guard lock(pvzmod::g_stateMutex);
        pvzmod::g_plants.erase(plant);
        if (definition || originalAnimation) {
            pvzmod::g_plants.emplace(
                plant, pvzmod::PlantRuntimeState{definition, originalAnimation, true});
        }
    }
    const int mappedSeed = definition ? definition->templatePlantId : seedType;
    const int mappedMarker = definition ? -1 : marker;
    return static_cast<unsigned int>(mappedSeed) |
           (static_cast<unsigned long long>(static_cast<unsigned int>(mappedMarker)) << 32U);
}

extern "C" void __stdcall ApplyPendingCustomPlant(void* plant) {
    const pvzmod::CustomPlantDefinition* definition = nullptr;
    const pvzmod::PlantAnimationOverride* originalAnimation = nullptr;
    {
        std::lock_guard lock(pvzmod::g_stateMutex);
        const auto it = pvzmod::g_plants.find(plant);
        if (it == pvzmod::g_plants.end() || !it->second.pending) return;
        definition = it->second.definition;
        originalAnimation = it->second.originalAnimation;
        it->second.pending = false;
    }
    if (!plant) return;
    if (originalAnimation != nullptr) {
        static_cast<void>(pvzmod::ApplyOriginalPlantAnimation(plant, *originalAnimation));
        return;
    }
    if (!definition) return;
    auto* bytes = static_cast<std::uint8_t*>(plant);
    *reinterpret_cast<int*>(bytes + 0x40) = definition->health;
    *reinterpret_cast<int*>(bytes + 0x44) = definition->health;
    *reinterpret_cast<int*>(bytes + 0x5C) = definition->launchRate;
    const unsigned int span = static_cast<unsigned int>(
        definition->initialLaunchDelayMax - definition->initialLaunchDelayMin + 1);
    const unsigned int salt = GetTickCount() ^ static_cast<unsigned int>(reinterpret_cast<std::uintptr_t>(plant));
    *reinterpret_cast<int*>(bytes + 0x58) = definition->initialLaunchDelayMin + static_cast<int>(salt % span);
    static_cast<void>(pvzmod::ApplyCustomPlantAnimation(plant, *definition));
}

extern "C" int __stdcall PrepareCustomProjectile(void* projectile, const int projectileType) {
    std::lock_guard lock(pvzmod::g_stateMutex);
    pvzmod::g_projectiles.erase(projectile);
    if (!pvzmod::g_firingPlant || pvzmod::g_firingPlant->attack.mode != "projectile") return projectileType;
    pvzmod::g_projectiles.emplace(projectile, pvzmod::ProjectileRuntimeState{
        pvzmod::g_firingPlant->id, pvzmod::g_firingPlant->attack.damage});
    return pvzmod::g_firingPlant->attack.projectileType;
}

extern "C" void __stdcall PlantFireDetour(void* plant, void* target, const int row, const int weapon) {
    using Fn = void(__stdcall*)(void*, void*, int, int);
    const auto original = reinterpret_cast<Fn>(g_originalPlantFire);
    const pvzmod::CustomPlantDefinition* definition = pvzmod::PlantDefinitionFor(plant);
    if (!definition) {
        original(plant, target, row, weapon);
        return;
    }
    const pvzmod::CustomPlantDefinition* previous = pvzmod::g_firingPlant;
    pvzmod::g_firingPlant = definition;
    const int shots = definition->attack.mode == "projectile" ? definition->attack.shotsPerAttack : 1;
    for (int shot = 0; shot < shots; ++shot) original(plant, target, row, weapon);
    pvzmod::g_firingPlant = previous;
}

extern "C" void __declspec(naked) PlantGetCostDetour() {
    __asm {
        push edx
        push eax
        push edx
        push eax
        call ResolveCustomPlantCost
        cmp eax, 80000000h
        je original_cost
        add esp, 8
        ret
    original_cost:
        pop eax
        pop edx
        jmp dword ptr [g_originalPlantGetCost]
    }
}

extern "C" void __declspec(naked) PlantGetRefreshTimeDetour() {
    __asm {
        push edx
        push ecx
        push edx
        push ecx
        call ResolveCustomPlantRefresh
        cmp eax, 80000000h
        je original_refresh
        add esp, 8
        ret
    original_refresh:
        pop ecx
        pop edx
        jmp dword ptr [g_originalPlantGetRefreshTime]
    }
}

extern "C" void __declspec(naked) PlantInitializeDetour() {
    __asm {
        push ecx
        push eax
        push dword ptr [esp + 20]
        push dword ptr [esp + 20]
        push dword ptr [esp + 20]
        call PrepareCustomPlant
        mov [esp + 16], eax
        mov [esp + 20], edx
        pop eax
        pop ecx
        jmp dword ptr [g_originalPlantInitialize]
    }
}

extern "C" void __declspec(naked) PlantUpdateDetour() {
    __asm {
        push eax
        push eax
        call ApplyPendingCustomPlant
        pop eax
        jmp dword ptr [g_originalPlantUpdate]
    }
}

extern "C" void __declspec(naked) ProjectileInitializeDetour() {
    __asm {
        push eax
        mov ecx, [esp + 8]
        mov edx, [esp + 28]
        push edx
        push ecx
        call PrepareCustomProjectile
        mov [esp + 28], eax
        pop eax
        jmp dword ptr [g_originalProjectileInitialize]
    }
}
