#include "global_config.h"
#include "custom_plant_config.h"
#include "plant_attack_config.h"
#include "seed_ui_config.h"
#include "spawn_config.h"
#include "wave_generator.h"
#include "wave_multiplier.h"
#include "wave_multiplier_config.h"
#include "zombie_armor_adapter.h"
#include "zombie_config.h"

#include <array>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <utility>

namespace {

int g_failures = 0;

void Expect(const bool condition, const std::string& message) {
    if (!condition) {
        ++g_failures;
        std::cerr << "FAIL: " << message << '\n';
    }
}

std::array<int, pvzmod::kSpawnListCount> FullSpawnList() {
    std::array<int, pvzmod::kSpawnListCount> list{};
    list.fill(0);
    return list;
}

void TestSparseConfigParsing() {
    const std::filesystem::path path = std::filesystem::temp_directory_path() / "pvzmod_spawn_config_test.json";
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({
            "schemaVersion": 1,
            "levels": {
                "1-1": {
                    "seed": 1101,
                    "zombies": [
                        {"id": 0, "weight": 80},
                        {"id": 2, "weight": 20}
                    ]
                }
            }
        })";
    }

    const pvzmod::ConfigLoadResult loaded = pvzmod::LoadSpawnConfig(path);
    std::error_code error;
    std::filesystem::remove(path, error);
    Expect(loaded.Ok(), "valid sparse config should load");
    if (!loaded.Ok()) {
        return;
    }
    Expect(loaded.config->FindAdventureLevel(1) != nullptr, "1-1 should have an override");
    Expect(loaded.config->FindAdventureLevel(2) == nullptr, "1-2 must remain original when omitted");
    Expect(loaded.config->adventureLevels.size() == 1, "only explicitly listed levels should exist");
}

void TestDeterministicWeights() {
    pvzmod::LevelSpawnConfig config;
    config.seed = 1101;
    config.zombies = {{0, 80.0}, {2, 20.0}};

    auto first = FullSpawnList();
    auto second = FullSpawnList();
    std::array<std::uint8_t, pvzmod::kZombieTypeCount> firstAllowed{};
    std::array<std::uint8_t, pvzmod::kZombieTypeCount> secondAllowed{};

    const auto firstStats = pvzmod::ApplyWeightedWaveOverride(first, firstAllowed, config, 1, 20);
    const auto secondStats = pvzmod::ApplyWeightedWaveOverride(second, secondAllowed, config, 1, 20);
    Expect(first == second, "same level and seed should create the same preview and actual list");
    Expect(firstStats.replacedSlots == pvzmod::kSpawnListCount, "all valid slots should be replaced");
    Expect(firstAllowed[0] == 1 && firstAllowed[2] == 1, "weighted pool types should be marked as allowed");

    std::size_t coneCount = 0;
    for (const int id : first) {
        if (id == 2) {
            ++coneCount;
        }
        Expect(id == 0 || id == 2, "generated list must only contain configured IDs");
    }
    const double coneRatio = static_cast<double>(coneCount) / first.size();
    Expect(std::abs(coneRatio - 0.20) < 0.05, "80/20 weights should produce an approximately 20% conehead share");
}

void TestWaveTerminatorsAndSpecialPreservation() {
    std::array<int, pvzmod::kSpawnListCount> list{};
    list.fill(-1);
    list[0] = 0;
    list[1] = 1;
    list[2] = 0;
    list[3] = -1;
    list[4] = 2;  // ignored because it follows the wave terminator

    pvzmod::LevelSpawnConfig config;
    config.seed = 9;
    config.preserveOriginalSpecialZombies = true;
    config.zombies = {{2, 1.0}};
    std::array<std::uint8_t, pvzmod::kZombieTypeCount> allowed{};
    const auto stats = pvzmod::ApplyWeightedWaveOverride(list, allowed, config, 1, 1);

    Expect(list[0] == 2 && list[1] == 1 && list[2] == 2, "normal slots change while flag slot remains");
    Expect(list[4] == 2, "data after a -1 terminator should not be traversed");
    Expect(stats.replacedSlots == 2, "two ordinary valid slots should be replaced");
    Expect(stats.preservedSpecialSlots == 1, "one structural special slot should be preserved");
    Expect(allowed[1] == 1 && allowed[2] == 1, "preserved special and configured types should both be allowed");
}

void TestActiveWaveLimitAndMinimumCount() {
    auto list = FullSpawnList();
    pvzmod::LevelSpawnConfig config;
    config.seed = 1101;
    config.zombies = {{0, 9999.0, 0}, {2, 1.0, 1}};
    std::array<std::uint8_t, pvzmod::kZombieTypeCount> allowed{};

    const auto stats = pvzmod::ApplyWeightedWaveOverride(list, allowed, config, 1, 1);
    std::size_t firstWaveCones = 0;
    for (std::size_t index = 0; index < pvzmod::kWaveSlotCount; ++index) {
        if (list[index] == 2) {
            ++firstWaveCones;
        }
    }

    Expect(stats.activeWaves == 1, "only the game's active wave count should be processed");
    Expect(stats.replacedSlots == pvzmod::kWaveSlotCount, "one active full wave should replace 50 slots");
    Expect(firstWaveCones >= 1, "minimumCount=1 should guarantee at least one conehead in active slots");
    Expect(list[pvzmod::kWaveSlotCount] == 0, "inactive wave cache must remain untouched");
}

void TestWaveMultiplierConfigComposition() {
    const std::filesystem::path path = std::filesystem::temp_directory_path() / "pvzmod_wave_multiplier_test.json";
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({
            "schemaVersion": 1,
            "global": {"countMultiplier": 2.0, "scaleSpecialZombies": false, "seed": 9},
            "levels": {
                "1-1": {
                    "countMultiplier": 1.5,
                    "waves": {"1": 0.5, "2": 2.0}
                }
            }
        })";
    }

    const pvzmod::WaveMultiplierConfigLoadResult loaded = pvzmod::LoadWaveMultiplierConfig(path);
    std::error_code error;
    std::filesystem::remove(path, error);
    Expect(loaded.Ok(), "valid wave multiplier config should load");
    if (!loaded.Ok()) {
        return;
    }

    Expect(std::abs(loaded.config->EffectiveMultiplier(1, 1) - 1.5) < 0.000001,
           "global, level, and wave multipliers should multiply together");
    Expect(std::abs(loaded.config->EffectiveMultiplier(1, 2) - 6.0) < 0.000001,
           "second wave should compose to x6");
    Expect(std::abs(loaded.config->EffectiveMultiplier(2, 1) - 2.0) < 0.000001,
           "missing level should still receive the global multiplier");
}

void TestWaveCountScaling() {
    std::array<int, pvzmod::kSpawnListCount> list{};
    list.fill(-1);
    list[0] = 0;
    list[1] = 2;
    list[2] = 0;
    list[3] = -1;
    list[pvzmod::kWaveSlotCount] = 7;
    list[pvzmod::kWaveSlotCount + 1] = -1;

    pvzmod::WaveMultiplierConfig config;
    config.globalCountMultiplier = 2.0;
    config.seed = 11;
    const auto stats = pvzmod::ApplyWaveCountMultipliers(list, config, 1, 1);

    Expect(stats.changedWaves == 1, "x2 should change one active wave");
    Expect(stats.originalCounts[0] == 3 && stats.finalCounts[0] == 6, "three zombies at x2 should become six");
    Expect(list[5] != -1 && list[6] == -1, "scaled wave should have exactly six contiguous entries");
    Expect(list[pvzmod::kWaveSlotCount] == 7, "inactive wave data must remain untouched by multipliers");
}

void TestWaveCountReductionPreservesSpecials() {
    std::array<int, pvzmod::kSpawnListCount> list{};
    list.fill(-1);
    list[0] = 1;
    list[1] = 0;
    list[2] = 2;
    list[3] = 3;

    pvzmod::WaveMultiplierConfig config;
    config.globalCountMultiplier = 0.5;
    config.scaleSpecialZombies = false;
    const auto stats = pvzmod::ApplyWaveCountMultipliers(list, config, 1, 1);

    Expect(stats.finalCounts[0] == 2, "four zombies at x0.5 should become two");
    Expect(list[0] == 1 || list[1] == 1, "flag zombie should survive count reduction");
}

void TestWaveCountCapAndAllowedTypes() {
    std::array<int, pvzmod::kSpawnListCount> list{};
    list.fill(-1);
    for (std::size_t index = 0; index < 10; ++index) {
        list[index] = (index % 2 == 0) ? 0 : 2;
    }

    pvzmod::WaveMultiplierConfig config;
    config.globalCountMultiplier = 10.0;
    const auto stats = pvzmod::ApplyWaveCountMultipliers(list, config, 1, 1);
    Expect(stats.finalCounts[0] == pvzmod::kWaveSlotCount, "wave count must cap at 50");
    Expect(stats.cappedWaves == 1, "capped wave should be reported");

    std::array<std::uint8_t, pvzmod::kZombieTypeCount> allowed{};
    pvzmod::RebuildAllowedZombieTypes(list, allowed, 1);
    Expect(allowed[0] == 1 && allowed[2] == 1, "allowed type preview should match final scaled list");
    Expect(allowed[4] == 0, "types absent from final list should not be advertised");
}

void TestGlobalSunConfig() {
    const std::filesystem::path path = std::filesystem::temp_directory_path() / "pvzmod_global_config_test.json";
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({
            "schemaVersion": 1,
            "economy": {
                "sunPickupValues": {
                    "normal": 50,
                    "small": 25,
                    "large": 100
                }
            }
        })";
    }

    const pvzmod::GlobalConfigLoadResult loaded = pvzmod::LoadGlobalConfig(path);
    std::error_code error;
    std::filesystem::remove(path, error);
    Expect(loaded.Ok(), "valid global config should load");
    if (!loaded.Ok()) {
        return;
    }
    Expect(loaded.config->sunPickupValues.normal == 50, "normal sun should be configurable to 50");
    Expect(loaded.config->sunPickupValues.small == 25, "small sun should be configurable to 25");
    Expect(loaded.config->sunPickupValues.large == 100, "large sun should be configurable to 100");
    Expect(pvzmod::SunValueForCoinType(loaded.config->sunPickupValues, 4) == 50,
           "coin type 4 should use normal sun value");
    Expect(pvzmod::SunValueForCoinType(loaded.config->sunPickupValues, 5) == 25,
           "coin type 5 should use small sun value");
    Expect(pvzmod::SunValueForCoinType(loaded.config->sunPickupValues, 6) == 100,
           "coin type 6 should use large sun value");
    Expect(pvzmod::SunValueForCoinType(loaded.config->sunPickupValues, 3) == 0,
           "non-sun coin types should return zero sun");
    Expect(pvzmod::SunTotalAfterPickup(100, 50) == 150, "configured pickup should add to prior sun total");
    Expect(pvzmod::SunTotalAfterPickup(9975, 100) == 9990, "sun pickup should preserve the original 9990 cap");
}

void TestPlantAttackJsoncSparseOverrides() {
    const std::filesystem::path path = std::filesystem::temp_directory_path() / "pvzmod_plant_attacks_test.jsonc";
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({
            "schemaVersion": 1,
            "projectiles": {
                // Original pea damage is 20; only this key is overridden.
                "pea": 40
            },
            "directAttacks": {
                // Omitted attacks must keep their original values.
                "iceShroom": 35
            }
        })";
    }

    const pvzmod::PlantAttackConfigLoadResult loaded = pvzmod::LoadPlantAttackConfig(path);
    std::error_code error;
    std::filesystem::remove(path, error);
    Expect(loaded.Ok(), "plant attack JSONC with comments should load");
    if (!loaded.Ok()) {
        return;
    }
    const auto pea = loaded.config->FindProjectile("pea");
    const auto ice = loaded.config->FindDirectAttack("iceShroom");
    Expect(pea == 40, "pea override should be read; actual=" +
           (pea ? std::to_string(*pea) : std::string("missing")) +
           ", mapSize=" + std::to_string(loaded.config->projectileOverrides.size()));
    Expect(!loaded.config->FindProjectile("snowPea").has_value(), "omitted snow pea must remain original");
    Expect(ice == 35, "direct attack override should be read; actual=" +
           (ice ? std::to_string(*ice) : std::string("missing")) +
           ", mapSize=" + std::to_string(loaded.config->directAttackOverrides.size()));
    Expect(!loaded.config->FindDirectAttack("squash").has_value(), "omitted direct attack must remain original");
}

void TestPlantAttackConfigRejectsUnknownKeys() {
    const std::filesystem::path path = std::filesystem::temp_directory_path() / "pvzmod_plant_attacks_bad_test.jsonc";
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({"schemaVersion":1,"projectiles":{"unknownShot":20}})";
    }
    const pvzmod::PlantAttackConfigLoadResult loaded = pvzmod::LoadPlantAttackConfig(path);
    std::error_code error;
    std::filesystem::remove(path, error);
    Expect(!loaded.Ok(), "unknown attack keys should be rejected instead of silently ignored");
}

void TestZombieAttributeConfigAndArmorChances() {
    const std::filesystem::path path = std::filesystem::temp_directory_path() / "pvzmod_zombie_attributes_test.jsonc";
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({
            "schemaVersion": 1,
            "seed": 1234,
            "originalArmorHealth": {
                "trafficCone": 999,
                "bucket": 1100
            },
            "armorDefinitions": {
                "1001": {"name":"bucket_t1","tier":1,"visual":"bucket","health":1100},
                "2001": {"name":"door_t2","tier":2,"visual":"door","health":1100},
                "3001": {"name":"wallnut_t1","tier":1,"visual":"wallnutHead","health":900}
            },
            "zombies": {
                "0": {
                    "bodyHealth": 540,
                    "attackDamage": 8,
                    "armorRolls": [
                        {"armorId":1001,"chance":0},
                        {"armorId":2001,"chance":100}
                    ]
                }
            }
        })";
    }
    const pvzmod::ZombieConfigLoadResult loaded = pvzmod::LoadZombieConfig(path);
    std::error_code error;
    std::filesystem::remove(path, error);
    Expect(loaded.Ok(), "valid zombie attribute JSONC should load");
    if (!loaded.Ok()) {
        return;
    }
    const pvzmod::ZombieAttributeOverride* normal = loaded.config->FindZombie(0);
    Expect(normal != nullptr, "only explicitly configured zombie id 0 should exist");
    Expect(loaded.config->FindZombie(2) == nullptr, "omitted zombie ids must remain original");
    const pvzmod::OriginalArmorHealthRule* cone = loaded.config->FindOriginalArmorHealth(2);
    const pvzmod::OriginalArmorHealthRule* bucket = loaded.config->FindOriginalArmorHealth(4);
    Expect(cone && cone->defaultHealth == 370 && cone->configuredHealth == 999 && cone->HasEffect(),
           "changed traffic cone catalog value should become an effective override without zombie id 2 entry");
    Expect(bucket && bucket->defaultHealth == 1100 && !bucket->HasEffect(),
           "unchanged bucket catalog value should preserve the original initialization path");
    int currentHealth = 777;
    int maximumHealth = 888;
    Expect(bucket && !pvzmod::ApplyOriginalArmorHealthOverride(*bucket, currentHealth, maximumHealth) &&
               currentHealth == 777 && maximumHealth == 888,
           "default catalog values must not overwrite original or earlier mod initialization results");
    currentHealth = 370;
    maximumHealth = 370;
    Expect(cone && pvzmod::ApplyOriginalArmorHealthOverride(*cone, currentHealth, maximumHealth) &&
               currentHealth == 999 && maximumHealth == 999,
           "changed catalog values must replace both current and maximum armor health");
    Expect(loaded.config->FindOriginalArmorHealth(5) == nullptr,
           "omitted original armor catalog values should remain fully sparse");
    Expect(normal && normal->bodyHealth == 540, "body health override should be parsed");
    Expect(normal && normal->attackDamage == 8, "independent attack damage should be parsed");
    const pvzmod::ArmorDefinition* wallnut = loaded.config->FindArmor(3001);
    Expect(wallnut && wallnut->visual == pvzmod::ArmorVisual::WallnutHead &&
               wallnut->slot == pvzmod::ArmorSlot::Helmet && wallnut->health == 900,
           "wallnutHead should parse as a helmet visual adapter");
    Expect(!pvzmod::ArmorRollSucceeds(1234, 0, 7, 1001, 0), "chance 0 must never equip armor");
    Expect(pvzmod::ArmorRollSucceeds(1234, 0, 7, 2001, 100), "chance 100 must always equip armor");
    Expect(pvzmod::ArmorRollSucceeds(1234, 0, 7, 2001, 37.5) ==
               pvzmod::ArmorRollSucceeds(1234, 0, 7, 2001, 37.5),
           "same seed and zombie instance should produce deterministic armor rolls");
}

void TestZombieConfigRejectsMissingArmorDefinition() {
    const std::filesystem::path path = std::filesystem::temp_directory_path() / "pvzmod_zombie_bad_armor_test.jsonc";
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({"schemaVersion":1,"zombies":{"0":{"armorRolls":[{"armorId":999,"chance":50}]}}})";
    }
    const pvzmod::ZombieConfigLoadResult loaded = pvzmod::LoadZombieConfig(path);
    std::error_code error;
    std::filesystem::remove(path, error);
    Expect(!loaded.Ok(), "undefined armor IDs must reject the new zombie config");
}

void TestAllOriginalArmorVisualAdapters() {
    const std::filesystem::path path =
        std::filesystem::temp_directory_path() / "pvzmod_all_armor_visuals_test.jsonc";
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({
            "schemaVersion": 1,
            "armorDefinitions": {
                "1":{"tier":1,"visual":"cone","health":370},
                "2":{"tier":1,"visual":"bucket","health":1100},
                "3":{"tier":1,"visual":"door","health":1100},
                "4":{"tier":1,"visual":"newspaper","health":150},
                "5":{"tier":1,"visual":"footballHelmet","health":1400},
                "6":{"tier":1,"visual":"bobsled","health":300},
                "7":{"tier":1,"visual":"balloon","health":20},
                "8":{"tier":1,"visual":"diggerHelmet","health":100},
                "9":{"tier":1,"visual":"ladder","health":500},
                "10":{"tier":1,"visual":"wallnutHead","health":1100},
                "11":{"tier":1,"visual":"tallnutHead","health":2200}
            }
        })";
    }
    const pvzmod::ZombieConfigLoadResult loaded = pvzmod::LoadZombieConfig(path);
    std::error_code error;
    std::filesystem::remove(path, error);
    Expect(loaded.Ok(), "all 11 original armor visual keys should parse");
    if (!loaded.Ok()) {
        return;
    }
    const std::array expected = {
        std::pair{pvzmod::ArmorVisual::Cone, pvzmod::ArmorSlot::Helmet},
        std::pair{pvzmod::ArmorVisual::Bucket, pvzmod::ArmorSlot::Helmet},
        std::pair{pvzmod::ArmorVisual::Door, pvzmod::ArmorSlot::Shield},
        std::pair{pvzmod::ArmorVisual::Newspaper, pvzmod::ArmorSlot::Shield},
        std::pair{pvzmod::ArmorVisual::FootballHelmet, pvzmod::ArmorSlot::Helmet},
        std::pair{pvzmod::ArmorVisual::Bobsled, pvzmod::ArmorSlot::Helmet},
        std::pair{pvzmod::ArmorVisual::Balloon, pvzmod::ArmorSlot::Flying},
        std::pair{pvzmod::ArmorVisual::DiggerHelmet, pvzmod::ArmorSlot::Helmet},
        std::pair{pvzmod::ArmorVisual::Ladder, pvzmod::ArmorSlot::Shield},
        std::pair{pvzmod::ArmorVisual::WallnutHead, pvzmod::ArmorSlot::Helmet},
        std::pair{pvzmod::ArmorVisual::TallnutHead, pvzmod::ArmorSlot::Helmet},
    };
    for (std::size_t index = 0; index < expected.size(); ++index) {
        const pvzmod::ArmorDefinition* armor = loaded.config->FindArmor(static_cast<int>(index + 1));
        Expect(armor != nullptr && armor->visual == expected[index].first && armor->slot == expected[index].second,
               "armor visual adapter " + std::to_string(index + 1) + " should use its original slot");
        if (armor != nullptr) {
            const pvzmod::ArmorVisualAdapterInfo& adapter =
                pvzmod::GetArmorVisualAdapterInfo(armor->visual);
            Expect(adapter.slot == armor->slot,
                   "adapter metadata and parsed slot should agree for armor " + std::to_string(index + 1));
        }
    }
}

void TestZombieConfigRejectsUnknownOriginalArmor() {
    const std::filesystem::path path =
        std::filesystem::temp_directory_path() / "pvzmod_zombie_bad_original_armor_test.jsonc";
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({"schemaVersion":1,"originalArmorHealth":{"fakeArmor":123}})";
    }
    const pvzmod::ZombieConfigLoadResult loaded = pvzmod::LoadZombieConfig(path);
    std::error_code error;
    std::filesystem::remove(path, error);
    Expect(!loaded.Ok(), "unknown original armor catalog keys should be rejected instead of ignored");
}

void TestSeedUiAndCustomPlantConfig() {
    const std::filesystem::path uiPath =
        std::filesystem::temp_directory_path() / "pvzmod_seed_ui_test.jsonc";
    const std::filesystem::path plantPath =
        std::filesystem::temp_directory_path() / "pvzmod_custom_plant_test.jsonc";
    {
        std::ofstream output(uiPath, std::ios::binary | std::ios::trunc);
        output << R"({"schemaVersion":1,"slotCount":10})";
    }
    {
        std::ofstream output(plantPath, std::ios::binary | std::ios::trunc);
        output << R"({
          "schemaVersion":1,
          "plants":[{
            "id":1000,"name":"test","templatePlantId":0,"unlocked":true,
            "cost":125,"rechargeTime":600,"health":450,"launchRate":90,
            "initialLaunchDelay":{"min":5,"max":20},
            "attack":{"mode":"projectile","projectileType":1,"damage":35,"shotsPerAttack":2}
          }]
        })";
    }
    const auto ui = pvzmod::LoadSeedUiConfig(uiPath);
    const auto plants = pvzmod::LoadCustomPlantConfig(plantPath);
    std::error_code error;
    std::filesystem::remove(uiPath, error);
    std::filesystem::remove(plantPath, error);
    Expect(ui.Ok() && ui.config->slotCount == 10, "seed chooser slot count should parse");
    Expect(plants.Ok() && plants.config->plants.size() == 1, "custom plant should parse");
    if (plants.Ok() && !plants.config->plants.empty()) {
        const auto& plant = plants.config->plants.front();
        Expect(plant.id == 1000 && plant.cost == 125 && plant.health == 450,
               "custom plant identity and independent attributes should be retained");
        Expect(plant.attack.projectileType == 1 && plant.attack.damage == 35 &&
               plant.attack.shotsPerAttack == 2,
               "custom projectile fields should be retained");
    }
}

}  // namespace

int main() {
    TestSparseConfigParsing();
    TestDeterministicWeights();
    TestWaveTerminatorsAndSpecialPreservation();
    TestActiveWaveLimitAndMinimumCount();
    TestWaveMultiplierConfigComposition();
    TestWaveCountScaling();
    TestWaveCountReductionPreservesSpecials();
    TestWaveCountCapAndAllowedTypes();
    TestGlobalSunConfig();
    TestPlantAttackJsoncSparseOverrides();
    TestPlantAttackConfigRejectsUnknownKeys();
    TestZombieAttributeConfigAndArmorChances();
    TestAllOriginalArmorVisualAdapters();
    TestZombieConfigRejectsMissingArmorDefinition();
    TestZombieConfigRejectsUnknownOriginalArmor();
    TestSeedUiAndCustomPlantConfig();

    if (g_failures != 0) {
        std::cerr << g_failures << " test(s) failed.\n";
        return 1;
    }
    std::cout << "All PvZ mod config tests passed.\n";
    return 0;
}
