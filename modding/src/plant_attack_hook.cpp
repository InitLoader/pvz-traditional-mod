#include "hook_modules.h"

#include "hook_utils.h"
#include "logger.h"
#include "plant_attack_config.h"
#include "custom_plant_hook.h"

#include <Windows.h>
#include <intrin.h>

#include <array>
#include <cstdint>
#include <filesystem>
#include <memory>
#include <mutex>
#include <optional>
#include <string>

extern "C" void* g_originalPlantKillRadius = nullptr;
extern "C" void* g_originalPlantZombieTakeDamage = nullptr;
extern "C" void* g_originalPlantZombieApplyBurn = nullptr;
extern "C" void PlantZombieTakeDamageDetour();
extern "C" int __stdcall PlantKillRadiusDetour(
    void* board, int row, int x, int y, int radius, int rowRange, int burn, int flags);
extern "C" void __fastcall PlantZombieApplyBurnDetour(void* zombie, void* unusedEdx);
extern "C" int __stdcall ResolvePlantDamage(
    std::uintptr_t returnAddress, void* callerFrame, void* callerEdi, int originalDamage);

namespace pvzmod {
namespace {

constexpr std::uintptr_t kKillRadiusRva = 0x0001D8A0;
constexpr std::uintptr_t kTakeDamageRva = 0x001317C0;
constexpr std::uintptr_t kApplyBurnRva = 0x00132B70;
constexpr std::uintptr_t kProjectileTableRva = 0x0029F1C0;
constexpr std::array<std::uint8_t, 12> kKillRadiusPrologue = {
    0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8, 0x83, 0xEC, 0x1C, 0x53, 0x8B, 0x5D};
constexpr std::array<std::uint8_t, 12> kTakeDamagePrologue = {
    0x51, 0x8B, 0x4E, 0x28, 0x83, 0xF9, 0x10, 0x53, 0x55, 0x8B, 0x6C, 0x24};
constexpr std::array<std::uint8_t, 12> kApplyBurnPrologue = {
    0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8, 0x83, 0xEC, 0x0C, 0x53, 0x56, 0x8B};

constexpr std::uintptr_t kRowDamageReturn = 0x0005EE27;
constexpr std::uintptr_t kSquashReturn = 0x000607B7;
constexpr std::uintptr_t kChomperReturn = 0x000614E5;
constexpr std::uintptr_t kIceShroomReturn = 0x0013249E;
constexpr std::uintptr_t kRadiusDamageReturn = 0x0001D93F;
constexpr std::uintptr_t kRadiusBurnReturn = 0x0001D92E;
constexpr std::uintptr_t kJalapenoBurnReturn = 0x0006652D;

std::uint8_t* g_plantModuleBase = nullptr;

class PlantAttackRuntime {
public:
    void Initialize(const std::filesystem::path& path) {
        std::lock_guard lock(mutex_);
        path_ = path;
        ReloadLocked(true);
    }
    std::shared_ptr<const PlantAttackConfig> Current() {
        std::lock_guard lock(mutex_);
        ReloadLocked(false);
        return config_;
    }

private:
    void ReloadLocked(const bool force) {
        std::error_code error;
        if (!std::filesystem::exists(path_, error)) {
            if (force || !missingWasLogged_) {
                LogWarning("Plant attack config not found: " + path_.string() + "; original damage is used.");
                missingWasLogged_ = true;
            }
            return;
        }
        const auto time = std::filesystem::last_write_time(path_, error);
        if (error || (!force && lastWriteTime_.has_value() && time == *lastWriteTime_)) {
            return;
        }
        lastWriteTime_ = time;
        missingWasLogged_ = false;
        PlantAttackConfigLoadResult loaded = LoadPlantAttackConfig(path_);
        if (!loaded.Ok()) {
            LogError(loaded.error + "; keeping the last valid plant attack config.");
            return;
        }
        config_ = std::make_shared<PlantAttackConfig>(std::move(*loaded.config));
        LogInfo("Loaded plant attack config: " + std::to_string(config_->projectileOverrides.size()) +
                " projectile override(s), " + std::to_string(config_->directAttackOverrides.size()) +
                " direct attack override(s).");
    }
    std::mutex mutex_;
    std::filesystem::path path_;
    std::optional<std::filesystem::file_time_type> lastWriteTime_;
    std::shared_ptr<const PlantAttackConfig> config_;
    bool missingWasLogged_ = false;
};

PlantAttackRuntime g_plantRuntime;

enum class RadiusAttack { None, ExplodeONut, CherryBomb, DoomShroom, PotatoMine, CobCannon };
thread_local RadiusAttack g_radiusAttack = RadiusAttack::None;

struct ProjectileEntry { int id; const char* key; int original; };
constexpr std::array<ProjectileEntry, 11> kProjectileEntries = {{
    {0, "pea", 20}, {1, "snowPea", 20}, {2, "cabbage", 40}, {3, "melon", 80},
    {4, "puff", 20}, {5, "winterMelon", 80}, {6, "fireball", 40}, {7, "star", 20},
    {8, "spike", 20}, {10, "kernel", 20}, {12, "butter", 40},
}};

void RefreshProjectileTable() {
    if (g_plantModuleBase == nullptr) {
        return;
    }
    const std::shared_ptr<const PlantAttackConfig> config = g_plantRuntime.Current();
    auto* table = g_plantModuleBase + kProjectileTableRva;
    bool changed = false;
    for (const ProjectileEntry& entry : kProjectileEntries) {
        const int desired = config ? config->FindProjectile(entry.key).value_or(entry.original) : entry.original;
        if (*reinterpret_cast<int*>(table + entry.id * 12 + 8) != desired) {
            changed = true;
            break;
        }
    }
    if (!changed) {
        return;
    }
    DWORD oldProtection = 0;
    if (!VirtualProtect(table, 14 * 12, PAGE_READWRITE, &oldProtection)) {
        LogError("VirtualProtect failed for projectile damage table.");
        return;
    }
    std::string summary;
    for (const ProjectileEntry& entry : kProjectileEntries) {
        const int desired = config ? config->FindProjectile(entry.key).value_or(entry.original) : entry.original;
        int* field = reinterpret_cast<int*>(table + entry.id * 12 + 8);
        if (*field != desired) {
            *field = desired;
            if (!summary.empty()) summary += ", ";
            summary += std::string(entry.key) + "=" + std::to_string(desired);
        }
    }
    DWORD ignored = 0;
    VirtualProtect(table, 14 * 12, oldProtection, &ignored);
    LogInfo("Applied plant projectile damage values: " + summary + '.');
}

std::optional<int> Direct(const char* key) {
    const std::shared_ptr<const PlantAttackConfig> config = g_plantRuntime.Current();
    return config ? config->FindDirectAttack(key) : std::nullopt;
}

RadiusAttack RadiusFromReturn(const std::uintptr_t address) {
    const std::uintptr_t rva = address - reinterpret_cast<std::uintptr_t>(g_plantModuleBase);
    switch (rva) {
        case 0x00062E88: return RadiusAttack::ExplodeONut;
        case 0x000667E5: return RadiusAttack::CherryBomb;
        case 0x00066846: return RadiusAttack::DoomShroom;
        case 0x00066A6F: return RadiusAttack::PotatoMine;
        case 0x0006D860: return RadiusAttack::CobCannon;
        default: return RadiusAttack::None;
    }
}

const char* RadiusKey(const RadiusAttack attack) {
    switch (attack) {
        case RadiusAttack::ExplodeONut: return "explodeONut";
        case RadiusAttack::CherryBomb: return "cherryBomb";
        case RadiusAttack::DoomShroom: return "doomShroom";
        case RadiusAttack::PotatoMine: return "potatoMine";
        case RadiusAttack::CobCannon: return "cobCannon";
        default: return nullptr;
    }
}

}  // namespace

bool InstallPlantAttackHooks(std::uint8_t* moduleBase) {
    if (!VerifyHookTarget(moduleBase, kKillRadiusRva, kKillRadiusPrologue, "Board::KillAllZombiesInRadius") ||
        !VerifyHookTarget(moduleBase, kTakeDamageRva, kTakeDamagePrologue, "Zombie::TakeDamage") ||
        !VerifyHookTarget(moduleBase, kApplyBurnRva, kApplyBurnPrologue, "Zombie::ApplyBurn")) {
        return false;
    }
    g_plantModuleBase = moduleBase;
    g_plantRuntime.Initialize(ModuleDirectory() / L"pvzmod" / L"config" / L"plants" / L"attacks.jsonc");
    if (!CreateAndEnableHook(moduleBase, kKillRadiusRva, reinterpret_cast<void*>(&PlantKillRadiusDetour),
                             &g_originalPlantKillRadius, "Board::KillAllZombiesInRadius") ||
        !CreateAndEnableHook(moduleBase, kTakeDamageRva, reinterpret_cast<void*>(&PlantZombieTakeDamageDetour),
                             &g_originalPlantZombieTakeDamage, "Zombie::TakeDamage") ||
        !CreateAndEnableHook(moduleBase, kApplyBurnRva, reinterpret_cast<void*>(&PlantZombieApplyBurnDetour),
                             &g_originalPlantZombieApplyBurn, "Zombie::ApplyBurn")) {
        return false;
    }
    RefreshProjectileTable();
    return true;
}

}  // namespace pvzmod

extern "C" void __declspec(naked) PlantZombieTakeDamageDetour() {
    __asm {
        pushfd
        pushad
        mov eax, [esp + 36]
        mov ecx, [esp + 8]
        mov edx, [esp]
        mov esi, [esp + 40]
        push esi
        push edx
        push ecx
        push eax
        call ResolvePlantDamage
        mov [esp + 40], eax
        popad
        popfd
        jmp dword ptr [g_originalPlantZombieTakeDamage]
    }
}

extern "C" void __declspec(naked) CallOriginalPlantZombieTakeDamage(void* zombie, int damage, unsigned int flags) {
    __asm {
        push ebp
        mov ebp, esp
        push esi
        mov esi, [ebp + 8]
        mov eax, [ebp + 16]
        push dword ptr [ebp + 12]
        call dword ptr [g_originalPlantZombieTakeDamage]
        pop esi
        mov esp, ebp
        pop ebp
        ret 12
    }
}

extern "C" int __stdcall PlantKillRadiusDetour(
    void* board, int row, int x, int y, int radius, int rowRange, int burn, int flags) {
    using Fn = int(__stdcall*)(void*, int, int, int, int, int, int, int);
    const auto original = reinterpret_cast<Fn>(g_originalPlantKillRadius);
    const pvzmod::RadiusAttack previous = pvzmod::g_radiusAttack;
    pvzmod::g_radiusAttack = pvzmod::RadiusFromReturn(reinterpret_cast<std::uintptr_t>(_ReturnAddress()));
    const int result = original(board, row, x, y, radius, rowRange, burn, flags);
    pvzmod::g_radiusAttack = previous;
    return result;
}

extern "C" int __stdcall ResolvePlantDamage(
    const std::uintptr_t returnAddress, void* callerFrame, void* callerEdi, const int originalDamage) {
    try {
        pvzmod::RefreshProjectileTable();
        const std::uintptr_t rva = returnAddress - reinterpret_cast<std::uintptr_t>(pvzmod::g_plantModuleBase);
        if (const std::optional<int> custom =
                pvzmod::ResolveCustomProjectileDamage(callerEdi, rva, originalDamage)) {
            return *custom;
        }
        const char* key = nullptr;
        if (rva == pvzmod::kRowDamageReturn && callerFrame) {
            const int seedType = *reinterpret_cast<const int*>(
                static_cast<const std::uint8_t*>(callerFrame) + 0x24);
            if ((seedType == 21 || seedType == 46) && originalDamage == 1800) key = "spikeVehicle";
            else if (seedType == 10) key = "fumeShroom";
            else if (seedType == 42) key = "gloomShroom";
            else if (seedType == 21) key = "spikeweed";
            else if (seedType == 46) key = "spikerock";
        } else if (rva == pvzmod::kSquashReturn) key = "squash";
        else if (rva == pvzmod::kChomperReturn) key = "chomperStrongTarget";
        else if (rva == pvzmod::kIceShroomReturn) key = "iceShroom";
        else if (rva == pvzmod::kRadiusDamageReturn) key = pvzmod::RadiusKey(pvzmod::g_radiusAttack);
        if (key) {
            const std::optional<int> value = pvzmod::Direct(key);
            if (value) return *value;
        }
    } catch (const std::exception& exception) {
        pvzmod::LogError(std::string("Plant damage lookup failed: ") + exception.what());
    }
    return originalDamage;
}

extern "C" void __fastcall PlantZombieApplyBurnDetour(void* zombie, void*) {
    using Fn = void(__thiscall*)(void*);
    const auto original = reinterpret_cast<Fn>(g_originalPlantZombieApplyBurn);
    try {
        const std::uintptr_t rva = reinterpret_cast<std::uintptr_t>(_ReturnAddress()) -
            reinterpret_cast<std::uintptr_t>(pvzmod::g_plantModuleBase);
        const char* key = rva == pvzmod::kRadiusBurnReturn ? pvzmod::RadiusKey(pvzmod::g_radiusAttack) :
            (rva == pvzmod::kJalapenoBurnReturn ? "jalapeno" : nullptr);
        if (key) {
            const std::optional<int> value = pvzmod::Direct(key);
            if (value && *value != 1800) {
                CallOriginalPlantZombieTakeDamage(zombie, *value, 18U);
                return;
            }
        }
    } catch (const std::exception& exception) {
        pvzmod::LogError(std::string("Plant burn damage lookup failed: ") + exception.what());
    }
    original(zombie);
}
