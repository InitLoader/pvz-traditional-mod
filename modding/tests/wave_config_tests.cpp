#include "audio_replacement_config.h"
#include "audio_sample_config.h"
#include "compiled_reanim.h"
#include "global_config.h"
#include "custom_plant_config.h"
#include "elite_zombie_config.h"
#include "external_animation_config.h"
#include "external_texture_config.h"
#include "plant_attack_config.h"
#include "plant_animation_override_config.h"
#include "original_sound_catalog.h"
#include "raw_reanim.h"
#include "reanimation_carrier_catalog.h"
#include "reanimation_playback_state.h"
#include "reanimation_track_instance_state.h"
#include "reanim_loader.h"
#include "runtime_reanim_definition.h"
#include "seed_ui_config.h"
#include "spawn_config.h"
#include "wave_generator.h"
#include "wave_multiplier.h"
#include "wave_multiplier_config.h"
#include "zombie_armor_adapter.h"
#include "zombie_config.h"

#include <array>
#include <bit>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

#include <zlib.h>

namespace {

int g_failures = 0;
void Expect(bool condition, const std::string& message);

void TestAudioConfigs() {
    const std::filesystem::path root = std::filesystem::temp_directory_path();
    const std::filesystem::path samplesPath = root / "pvzmod_audio_samples_test.jsonc";
    const std::filesystem::path replacementsPath = root / "pvzmod_audio_replacements_test.jsonc";
    {
        std::ofstream output(samplesPath, std::ios::binary | std::ios::trunc);
        output << R"({
            "schemaVersion": 1,
            "samples": {
                "rage_roar": {"path": "pvzmod/audio/samples/rage.ogg"},
                "Ui_Click": {"path": "pvzmod/audio/samples/click.wav", "enabled": false}
            }
        })";
    }
    {
        std::ofstream output(replacementsPath, std::ios::binary | std::ios::trunc);
        output << R"({
            "schemaVersion": 1,
            "replaceOriginal": {
                "sound_chomp": {"sampleId": "rage_roar"}
            }
        })";
    }
    const auto samples = pvzmod::LoadAudioSampleConfig(samplesPath);
    const auto replacements = pvzmod::LoadAudioReplacementConfig(replacementsPath);
    std::error_code error;
    std::filesystem::remove(samplesPath, error);
    std::filesystem::remove(replacementsPath, error);
    Expect(samples.Ok(), "valid external audio sample config should load");
    Expect(replacements.Ok(), "valid sparse audio replacement config should load");
    if (samples.Ok()) {
        Expect(samples.config->Find("RAGE_ROAR") != nullptr,
               "audio IDs should be case-insensitive and normalized");
        Expect(samples.config->Find("ui_click") != nullptr && !samples.config->Find("ui_click")->enabled,
               "disabled samples should remain registered but inactive");
    }
    if (replacements.Ok()) {
        const auto* replacement = replacements.config->Find("SOUND_CHOMP");
        Expect(replacement != nullptr && replacement->sampleId == "RAGE_ROAR",
               "replacement should preserve sparse SOUND_* to external ID routing");
    }
    Expect(pvzmod::FindOriginalSoundGlobalRva("sound_chomp") == std::optional<std::uintptr_t>(0x002A74D8),
           "original sound catalog should resolve the verified CHOMP global RVA");
    Expect(pvzmod::IsKnownOriginalSound("SOUND_BUTTONCLICK") &&
           pvzmod::IsKnownOriginalSound("SOUND_ZOMBIE_FALLING_2") &&
           !pvzmod::IsKnownOriginalSound("SOUND_NOT_REAL"),
           "original sound catalog should cover both resource groups and reject unknown symbols");
}

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
                    "animationId": "CUSTOM_NORMAL_ZOMBIE",
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
    Expect(normal && normal->animationId == "CUSTOM_NORMAL_ZOMBIE",
           "sparse zombie animationId should be parsed without affecting omitted zombie ids");
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
            "id":1000,"name":"test","templatePlantId":0,"animationId":"TEST_PLANT_ANIM","unlocked":true,
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
        Expect(plant.animationId == "TEST_PLANT_ANIM",
               "custom plant animationId should be retained for runtime definition injection");
        Expect(plant.attack.projectileType == 1 && plant.attack.damage == 35 &&
               plant.attack.shotsPerAttack == 2,
               "custom projectile fields should be retained");
    }
}

void TestPlantAnimationOverrideConfig() {
    const std::filesystem::path path =
        std::filesystem::temp_directory_path() / "pvzmod_plant_animation_override_test.jsonc";
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({
            "schemaVersion": 1,
            // Only Chomper is replaced; every omitted plant remains original.
            "plants": {
                "6": { "animationId": "MY_CHOMPER" }
            }
        })";
    }
    const auto loaded = pvzmod::LoadPlantAnimationOverrideConfig(path);
    Expect(loaded.Ok(), "valid sparse original-plant animation config should load");
    if (loaded.Ok()) {
        const auto* chomper = loaded.config->FindPlant(6);
        Expect(chomper != nullptr && chomper->animationId == "MY_CHOMPER",
               "configured original plant should expose its animationId");
        Expect(loaded.config->FindPlant(0) == nullptr && loaded.config->plants.size() == 1,
               "omitted original plants must remain untouched");
    }

    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({"schemaVersion":1,"plants":{"49":{"animationId":"BAD"}}})";
    }
    const auto invalidType = pvzmod::LoadPlantAnimationOverrideConfig(path);
    Expect(!invalidType.Ok(), "plant animation override IDs outside 0-48 should be rejected");

    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({"schemaVersion":1,"plants":{"6":{"animationId":"../BAD"}}})";
    }
    const auto invalidAnimation = pvzmod::LoadPlantAnimationOverrideConfig(path);
    Expect(!invalidAnimation.Ok(), "unsafe original-plant animation IDs should be rejected");
    std::error_code error;
    std::filesystem::remove(path, error);
}

void TestZombieConfigRejectsInvalidAnimationId() {
    const std::filesystem::path path =
        std::filesystem::temp_directory_path() / "pvzmod_zombie_bad_animation_id_test.jsonc";
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({"schemaVersion":1,"zombies":{"0":{"animationId":"bad/path"}}})";
    }
    const pvzmod::ZombieConfigLoadResult loaded = pvzmod::LoadZombieConfig(path);
    std::error_code error;
    std::filesystem::remove(path, error);
    Expect(!loaded.Ok(), "zombie animationId must use the shared external resource ID grammar");
}

void AppendU32(std::vector<std::uint8_t>& output, const std::uint32_t value) {
    output.push_back(static_cast<std::uint8_t>(value));
    output.push_back(static_cast<std::uint8_t>(value >> 8U));
    output.push_back(static_cast<std::uint8_t>(value >> 16U));
    output.push_back(static_cast<std::uint8_t>(value >> 24U));
}

void AppendI32(std::vector<std::uint8_t>& output, const std::int32_t value) {
    AppendU32(output, std::bit_cast<std::uint32_t>(value));
}

void AppendFloat(std::vector<std::uint8_t>& output, const float value) {
    AppendU32(output, std::bit_cast<std::uint32_t>(value));
}

void AppendString(std::vector<std::uint8_t>& output, const std::string& value) {
    AppendI32(output, static_cast<std::int32_t>(value.size()));
    output.insert(output.end(), value.begin(), value.end());
}

std::vector<std::uint8_t> BuildCompiledReanimFixture() {
    constexpr float missing = -10000.0f;
    std::vector<std::uint8_t> payload;
    AppendU32(payload, 0xB393B4C0U);
    AppendU32(payload, 0U);
    AppendI32(payload, 2);
    AppendFloat(payload, 12.0f);
    AppendU32(payload, 0U);
    AppendI32(payload, 12);
    for (int track = 0; track < 2; ++track) {
        AppendU32(payload, 0U);
        AppendU32(payload, 0U);
        AppendI32(payload, 4);
    }

    const auto appendTrack = [&](const std::string& name, const bool imageTrack) {
        AppendString(payload, name);
        AppendI32(payload, 44);
        for (int frame = 0; frame < 4; ++frame) {
            const float x = imageTrack && frame == 0 ? 2.5f : missing;
            const float visible = !imageTrack && frame == 0 ? 0.0f :
                                  (!imageTrack && frame == 2 ? -1.0f : missing);
            AppendFloat(payload, x);
            AppendFloat(payload, missing);
            AppendFloat(payload, missing);
            AppendFloat(payload, missing);
            AppendFloat(payload, missing);
            AppendFloat(payload, missing);
            AppendFloat(payload, visible);
            AppendFloat(payload, imageTrack && frame == 0 ? 0.75f : missing);
            AppendU32(payload, 0U);
            AppendU32(payload, 0U);
            AppendU32(payload, 0U);
        }
        for (int frame = 0; frame < 4; ++frame) {
            AppendString(payload, imageTrack && frame == 0 ? "IMAGE_REANIM_TEST_BODY" : "");
            AppendString(payload, "");
            AppendString(payload, "");
        }
    };
    appendTrack("anim_idle", false);
    appendTrack("body", true);

    uLongf compressedSize = compressBound(static_cast<uLong>(payload.size()));
    std::vector<std::uint8_t> compressed(static_cast<std::size_t>(compressedSize));
    const int status = compress2(
        reinterpret_cast<Bytef*>(compressed.data()), &compressedSize,
        reinterpret_cast<const Bytef*>(payload.data()), static_cast<uLong>(payload.size()), Z_BEST_SPEED);
    if (status != Z_OK) throw std::runtime_error("test fixture compression failed");
    compressed.resize(static_cast<std::size_t>(compressedSize));

    std::vector<std::uint8_t> result;
    AppendU32(result, 0xDEADFED4U);
    AppendU32(result, static_cast<std::uint32_t>(payload.size()));
    result.insert(result.end(), compressed.begin(), compressed.end());
    return result;
}

void TestExternalTextureConfigAndAliases() {
    const std::filesystem::path path =
        std::filesystem::temp_directory_path() / "pvzmod_external_textures_test.jsonc";
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({
          "schemaVersion":1,
          "textures":[
            {"id":"KILL","path":"pvzmod/images/zi/kill.png"},
            {"ID":"UI_2","Patch":"pvzmod/images/ui/page_2.jpg"}
          ]
        })";
    }
    const auto loaded = pvzmod::LoadExternalTextureConfig(path);
    std::error_code error;
    std::filesystem::remove(path, error);
    Expect(loaded.Ok() && loaded.config->textures.size() == 2,
           "external texture registry should parse canonical and compatibility aliases");
    if (loaded.Ok()) {
        const auto* kill = loaded.config->Find("KILL");
        const auto* ui = loaded.config->Find("UI_2");
        Expect(kill != nullptr && kill->path == "pvzmod/images/zi/kill.png",
               "KILL should retain its normalized path");
        Expect(ui != nullptr && ui->path == "pvzmod/images/ui/page_2.jpg",
               "ID and Patch aliases should normalize to the standard definition");
    }
    Expect(pvzmod::IsExternalResourceId("zombie_head_01"),
           "letters, digits, and underscores should be valid resource ids");
    Expect(!pvzmod::IsExternalResourceId("zombie-head"),
           "hyphens should be rejected from resource ids");
}

void TestExternalTextureConfigRejectsTraversal() {
    const std::filesystem::path path =
        std::filesystem::temp_directory_path() / "pvzmod_external_texture_traversal_test.jsonc";
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({"schemaVersion":1,"textures":[{"id":"BAD","path":"pvzmod/images/../secret.png"}]})";
    }
    const auto loaded = pvzmod::LoadExternalTextureConfig(path);
    std::error_code error;
    std::filesystem::remove(path, error);
    Expect(!loaded.Ok(), "external texture paths containing traversal must be rejected");
}

void TestExternalAnimationConfigAndRawReanim() {
    const std::filesystem::path configPath =
        std::filesystem::temp_directory_path() / "pvzmod_external_animations_test.jsonc";
    const std::filesystem::path reanimPath =
        std::filesystem::temp_directory_path() / "pvzmod_external_animation_test.reanim";
    {
        std::ofstream output(configPath, std::ios::binary | std::ios::trunc);
        output << R"({
          "schemaVersion":1,
          "animations":[{
            "id":"PLANT_DEMO_01",
            "path":"pvzmod/animations/plants/demo/demo.reanim",
            "carrierReanimation":"REANIM_PEASHOOTER",
            "initialAction":"idle",
            "savedGameRecovery":true,
            "images":{"IMAGE_REANIM_DEMO_BODY":"KILL"},
            "actions":{
              "idle":{"track":"anim_idle","loop":"loop","rate":1.25},
              "attack":{"track":"anim_attack","loop":"once_hold","blendFrames":3,
                "replaces":["anim_shooting","anim_head_shooting"],
                "events":[{"id":"FIRE_PROJECTILE","frame":1},
                          {"id":"PLAY_ACTION","normalizedTime":0.9,"action":"idle"}]}
            },
            "locators":{"projectile":"locator_mouth"}
          }]
        })";
    }
    {
        std::ofstream output(reanimPath, std::ios::binary | std::ios::trunc);
        output << R"(<doScale>1</doScale>
<fps>12</fps>
<track><name>anim_idle</name>
  <t><f>0</f></t><t></t><t><f>-1</f></t><t></t>
</track>
<track><name>anim_attack</name>
  <t><f>-1</f></t><t><f>0</f></t><t></t><t></t>
</track>
<track><name>body</name>
  <t><i>IMAGE_REANIM_DEMO_BODY</i><x>2.5</x><a>0.75</a></t><t></t><t></t><t></t>
</track>
<track><name>locator_mouth</name>
  <t><x>8</x><y>-3</y><f>0</f></t><t></t><t></t><t></t>
</track>)";
    }

    const auto config = pvzmod::LoadExternalAnimationConfig(configPath);
    const auto raw = pvzmod::LoadRawReanim(reanimPath);
    std::error_code error;
    std::filesystem::remove(configPath, error);
    std::filesystem::remove(reanimPath, error);

    Expect(config.Ok() && config.config->animations.size() == 1,
           "external animation metadata should parse");
    if (config.Ok()) {
        const auto* animation = config.config->Find("PLANT_DEMO_01");
        Expect(animation != nullptr && animation->savedGameRecovery &&
                   animation->images.at("IMAGE_REANIM_DEMO_BODY") == "KILL",
               "animation recovery metadata and image symbols should parse");
        const auto* attack = animation == nullptr ? nullptr : animation->FindAction("attack");
        Expect(attack != nullptr && attack->loop == pvzmod::ExternalAnimationLoopMode::OnceHold &&
                   attack->blendFrames == 3 && attack->events.size() == 2 &&
                   attack->events[1].targetAction == "idle" &&
                   animation->FindActionForTrack("ANIM_HEAD_SHOOTING") == attack,
                "animation action playback and event metadata should parse");
    }
    Expect(raw.Ok() && raw.definition->FrameCount() == 4,
           "Raw reanimation tracks with equal frame counts should parse");
    if (raw.Ok()) {
        const auto* idle = raw.definition->FindTrack("anim_idle");
        const auto* body = raw.definition->FindTrack("body");
        const auto range = idle == nullptr ? std::nullopt : idle->VisibleFrameRange();
        Expect(range.has_value() && range->first == 0 && range->second == 2,
               "visibility should inherit until a later explicit hidden frame");
        Expect(body != nullptr && body->transforms[1].image == "IMAGE_REANIM_DEMO_BODY" &&
                   body->transforms[1].x.has_value() &&
                   std::abs(*body->transforms[1].x - 2.5) < 0.0001,
               "transform values should inherit from previous Raw reanimation frames");
    }
}

void TestRuntimeReanimDefinitionBuild() {
    pvzmod::RawReanimDefinition raw;
    raw.fps = 18.0f;
    pvzmod::RawReanimTrack idle;
    idle.name = "anim_idle";
    pvzmod::RawReanimTransform first;
    first.x = 12.0f;
    first.image = "IMAGE_REANIM_TEST_BODY";
    first.text = "runtime text";
    idle.transforms.push_back(first);
    idle.transforms.emplace_back();
    raw.tracks.push_back(std::move(idle));

    pvzmod::ExternalAnimationDefinition config;
    config.id = "TEST_RUNTIME";
    config.images.emplace("IMAGE_REANIM_TEST_BODY", "TEST_TEXTURE");
    void* expectedImage = reinterpret_cast<void*>(static_cast<std::uintptr_t>(0x12345678));
    const auto built = pvzmod::BuildRuntimeReanimDefinition(
        raw, config, [expectedImage](const std::string_view id) {
            return id == "TEST_TEXTURE" ? expectedImage : nullptr;
        });
    Expect(built.Ok(), "Raw animation should build a game-native runtime Definition");
    if (built.Ok()) {
        const auto* definition = built.storage->Definition();
        Expect(definition->trackCount == 1 && std::abs(definition->fps - 18.0f) < 0.0001f,
               "runtime Definition should retain track count and fps");
        Expect(std::string(definition->tracks[0].name) == "anim_idle" &&
                   definition->tracks[0].transformCount == 2,
               "runtime Definition should own stable track names and transforms");
        Expect(definition->tracks[0].transforms[0].image == expectedImage &&
                   std::string(definition->tracks[0].transforms[0].text) == "runtime text",
               "runtime transforms should resolve Image pointers and own text storage");
    }

    pvzmod::ExternalAnimationDefinition missingBinding;
    const auto originalFallback = pvzmod::BuildRuntimeReanimDefinition(
        raw, missingBinding, [expectedImage](const std::string_view id) {
            return id == "IMAGE_REANIM_TEST_BODY" ? expectedImage : nullptr;
        });
    Expect(originalFallback.Ok() &&
               originalFallback.storage->Definition()->tracks[0].transforms[0].image == expectedImage,
           "unbound IMAGE_REANIM symbols should be reusable through the original-image resolver");
    const auto rejected = pvzmod::BuildRuntimeReanimDefinition(
        raw, missingBinding, [](const std::string_view) { return static_cast<void*>(nullptr); });
    Expect(!rejected.Ok(), "runtime Definition build must reject unresolved image symbols");
}

void TestExternalAnimationConfigRejectsUnsafeInput() {
    const std::filesystem::path path =
        std::filesystem::temp_directory_path() / "pvzmod_external_animation_unsafe_test.jsonc";
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({"schemaVersion":1,"animations":[{
          "id":"BAD","path":"pvzmod/animations/../images/secret.reanim",
          "carrierReanimation":"REANIM_PEASHOOTER",
          "actions":{"attack":{"track":"anim_attack","events":[
            {"id":"BAD_EVENT","frame":1,"normalizedTime":0.5}
          ]}}
        }]})";
    }
    const auto loaded = pvzmod::LoadExternalAnimationConfig(path);
    std::error_code error;
    std::filesystem::remove(path, error);
    Expect(!loaded.Ok(), "external animation traversal and ambiguous event times must be rejected");
}

void TestExternalAnimationConfigRejectsConflictingMappings() {
    const std::filesystem::path path =
        std::filesystem::temp_directory_path() / "pvzmod_external_animation_conflict_test.jsonc";
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({"schemaVersion":1,"animations":[{
          "id":"BAD","path":"pvzmod/animations/plants/bad.reanim",
          "carrierReanimation":"REANIM_PEASHOOTER","initialAction":"idle",
          "actions":{
            "idle":{"track":"anim_idle","replaces":["anim_shared"]},
            "attack":{"track":"anim_attack","replaces":["ANIM_SHARED"]}
          }
        }]})";
    }
    const auto loaded = pvzmod::LoadExternalAnimationConfig(path);
    std::error_code error;
    std::filesystem::remove(path, error);
    Expect(!loaded.Ok(), "multiple custom actions must not claim the same template track");
}

void TestRawReanimRejectsUnsafeOrMalformedInput() {
    const std::filesystem::path mismatchedPath =
        std::filesystem::temp_directory_path() / "pvzmod_raw_reanim_mismatch_test.reanim";
    const std::filesystem::path entityPath =
        std::filesystem::temp_directory_path() / "pvzmod_raw_reanim_entity_test.reanim";
    {
        std::ofstream output(mismatchedPath, std::ios::binary | std::ios::trunc);
        output << R"(<fps>12</fps>
<track><name>a</name><t></t><t></t></track>
<track><name>b</name><t></t></track>)";
    }
    {
        std::ofstream output(entityPath, std::ios::binary | std::ios::trunc);
        output << R"(<!DOCTYPE x [<!ENTITY bad "boom">]>
<fps>12</fps><track><name>a</name><t><text>&bad;</text></t></track>)";
    }
    const auto mismatched = pvzmod::LoadRawReanim(mismatchedPath);
    const auto entity = pvzmod::LoadRawReanim(entityPath);
    std::error_code error;
    std::filesystem::remove(mismatchedPath, error);
    std::filesystem::remove(entityPath, error);
    Expect(!mismatched.Ok(), "Raw reanimation tracks with different frame counts must be rejected");
    Expect(!entity.Ok(), "Raw reanimation DTD and entity declarations must be rejected");
}

void TestCompiledReanimDecoding() {
    const std::filesystem::path path =
        std::filesystem::temp_directory_path() / "pvzmod_compiled_reanim_test.reanim.compiled";
    const std::filesystem::path bombPath =
        std::filesystem::temp_directory_path() / "pvzmod_compiled_reanim_bomb_test.reanim.compiled";
    std::vector<std::uint8_t> fixture = BuildCompiledReanimFixture();
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output.write(reinterpret_cast<const char*>(fixture.data()), static_cast<std::streamsize>(fixture.size()));
    }
    fixture[4] = 0xFF;
    fixture[5] = 0xFF;
    fixture[6] = 0xFF;
    fixture[7] = 0x7F;
    {
        std::ofstream output(bombPath, std::ios::binary | std::ios::trunc);
        output.write(reinterpret_cast<const char*>(fixture.data()), static_cast<std::streamsize>(fixture.size()));
    }

    const auto loaded = pvzmod::LoadCompiledReanim(path);
    const auto dispatched = pvzmod::LoadReanimDefinition(path);
    const auto bomb = pvzmod::LoadCompiledReanim(bombPath);
    std::error_code error;
    std::filesystem::remove(path, error);
    std::filesystem::remove(bombPath, error);

    Expect(loaded.Ok() && dispatched.Ok(),
           "original Windows .reanim.compiled data should decode through the direct and automatic loaders");
    if (loaded.Ok()) {
        Expect(loaded.definition->tracks.size() == 2 && loaded.definition->FrameCount() == 4,
               "compiled definition should retain its track and frame counts");
        const auto* idle = loaded.definition->FindTrack("anim_idle");
        const auto* body = loaded.definition->FindTrack("body");
        const auto range = idle == nullptr ? std::nullopt : idle->VisibleFrameRange();
        Expect(range.has_value() && range->first == 0 && range->second == 2,
               "compiled visibility placeholders should normalize to the original visible range");
        Expect(body != nullptr && body->transforms[1].image == "IMAGE_REANIM_TEST_BODY" &&
                   body->transforms[1].x == 2.5f && body->transforms[1].alpha == 0.75f,
               "compiled placeholder values should inherit image and transform data");
    }
    Expect(!bomb.Ok(), "compiled caches declaring an unsafe decompressed size must be rejected");
}

void TestExternalAnimationConfigAcceptsCompiledPaths() {
    const std::filesystem::path path =
        std::filesystem::temp_directory_path() / "pvzmod_compiled_animation_paths_test.jsonc";
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({"schemaVersion":1,"animations":[
          {"id":"ORIGINAL_BLOVER","path":"compiled/reanim/Blover.reanim.compiled",
           "carrierReanimation":"REANIM_BLOVER","actions":{"idle":{"track":"anim_idle"}}},
          {"id":"MOD_COMPILED","path":"pvzmod/animations/plants/test/test.reanim.compiled",
           "carrierReanimation":"REANIM_PEASHOOTER","actions":{"idle":{"track":"anim_idle"}}}
        ]})";
    }
    const auto loaded = pvzmod::LoadExternalAnimationConfig(path);
    std::error_code error;
    std::filesystem::remove(path, error);
    Expect(loaded.Ok() && loaded.config->animations.size() == 2,
           "animation config should accept original and mod-owned .reanim.compiled paths");
}

void TestRepositoryExternalAnimationExample() {
    const std::filesystem::path root = std::filesystem::path(PVZMOD_REPOSITORY_ROOT).lexically_normal();
    const auto textures = pvzmod::LoadExternalTextureConfig(
        root / "pvzmod/config/resources/textures.jsonc");
    const auto animations = pvzmod::LoadExternalAnimationConfig(
        root / "pvzmod/config/resources/animations.jsonc");
    Expect(textures.Ok(), "repository external texture example should parse");
    Expect(animations.Ok(), "repository external animation example should parse");
    if (!textures.Ok() || !animations.Ok()) return;

    for (const auto& [animationId, animation] : animations.config->animations) {
        const std::filesystem::path animationPath = root / std::filesystem::u8path(animation.path);
        if (!std::filesystem::exists(animationPath) && pvzmod::IsCompiledReanimPath(animationPath)) {
            // Original compiled assets are intentionally not committed; the synthetic cache test
            // validates the decoder in clean CI, while an installed game validates the real file.
            continue;
        }
        const auto raw = pvzmod::LoadReanimDefinition(animationPath);
        Expect(raw.Ok(), "repository reanimation example should parse: " + animationId);
        if (!raw.Ok()) continue;
        for (const auto& [symbol, textureId] : animation.images) {
            (void)symbol;
            Expect(textures.config->Find(textureId) != nullptr,
                   "repository animation image binding should reference a registered texture");
        }
        for (const auto& [actionId, action] : animation.actions) {
            const auto* track = raw.definition->FindTrack(action.track);
            Expect(track != nullptr, "repository action should reference an existing track: " + actionId);
            if (track == nullptr) continue;
            const auto range = track->VisibleFrameRange();
            Expect(range.has_value(), "repository action track should have a visible range: " + actionId);
            if (!range.has_value()) continue;
            for (const auto& event : action.events) {
                Expect(!event.frame.has_value() || *event.frame < range->second,
                       "repository event frame should be relative to and inside its action");
            }
        }
        for (const auto& [logicalName, trackName] : animation.locators) {
            (void)logicalName;
            Expect(raw.definition->FindTrack(trackName) != nullptr,
                   "repository locator should reference an existing Raw track");
        }
    }
}

void TestEliteZombieConfigAndPriority() {
    const std::filesystem::path path =
        std::filesystem::temp_directory_path() / "pvzmod_elite_zombies_test.jsonc";
    {
        std::ofstream output(path, std::ios::binary | std::ios::trunc);
        output << R"({
          "schemaVersion":1,
          "seed":123,
          "elites":[
            {
              "runtimeId":2,"id":"LOW","priority":10,"eligibleZombieIds":[0],"chance":100,
              "skills":[],"visual":{"tint":{"red":255,"green":80,"blue":80,"alpha":255}}
            },
            {
              "runtimeId":1,"id":"RAGE","name":"rage","priority":20,
              "eligibleZombieIds":[0],"chance":100,
              "skills":[{"id":"BERSERK","parameters":{"healthMultiplier":1.5}}],
              "visual":{"replacements":[
                {"scope":"body","target":"head","textureId":"KILL"},
                {"scope":"body","track":"Zombie_tie","textureId":"UI_2"}
              ]}
            }
          ]
        })";
    }
    const auto loaded = pvzmod::LoadEliteZombieConfig(path);
    std::error_code error;
    std::filesystem::remove(path, error);
    Expect(loaded.Ok() && loaded.config->elites.size() == 2,
           "elite zombie definitions should parse");
    if (!loaded.Ok()) return;
    const pvzmod::EliteZombieDefinition* selected = pvzmod::PickEliteZombie(*loaded.config, 0, 77);
    Expect(selected != nullptr && selected->id == "RAGE",
           "higher priority matching elite should win deterministic selection");
    Expect(pvzmod::PickEliteZombie(*loaded.config, 2, 77) == nullptr,
           "ineligible zombie types should remain ordinary");
    const auto& berserk = loaded.config->elites[1].skills[0];
    Expect(std::abs(berserk.Parameter("healthMultiplier", 1.0) - 1.5) < 0.0001,
           "elite skill numeric parameters should be retained");
    const bool first = pvzmod::EliteRollSucceeds(123, 0, 77, 1, 33.0);
    const bool second = pvzmod::EliteRollSucceeds(123, 0, 77, 1, 33.0);
    Expect(first == second, "elite probability must be deterministic for the same instance key");
    const auto& replacements = loaded.config->elites[1].visual.replacements;
    Expect(replacements.size() == 2 && replacements[0].track == "anim_head1" &&
           replacements[0].target == "head" && replacements[1].track == "Zombie_tie",
           "semantic texture targets and explicit tracks should parse to real reanimation tracks");
    Expect(pvzmod::ZombieTextureTrackForTarget("body") == std::optional<std::string_view>("Zombie_body"),
           "ordinary zombie body target should resolve to Zombie_body");
    Expect(pvzmod::IsReanimationTrackName("Layer 47") &&
           pvzmod::IsReanimationTrackName("Boss-outerarm_finger4") &&
           !pvzmod::IsReanimationTrackName("../bad"),
           "advanced track validation should accept original names and reject traversal-like input");
}

void TestReanimationCarrierCatalog() {
    Expect(pvzmod::ResolveCarrierReanimationType("REANIM_CATTAIL") == 81,
           "Cattail carrier symbol should resolve to ReanimationType 81");
    Expect(pvzmod::ResolvePlantTemplateCarrierReanimationType(43) == 81,
           "plant template 43 must use the Cattail carrier for saved games");
    Expect(pvzmod::ResolveZombieTypeCarrierReanimationType(0) == 21 &&
           pvzmod::ResolveZombieTypeCarrierReanimationType(3) == 54 &&
           pvzmod::ResolveZombieTypeCarrierReanimationType(32) == 56,
           "zombie template carriers should cover ordinary, pole-vaulter and red-eye bodies");
    Expect(pvzmod::ResolvePlantTemplateCarrierReanimationType(49) == -1 &&
           pvzmod::ResolveZombieTypeCarrierReanimationType(33) == -1,
           "unsupported template IDs must not silently select a carrier");
}

void TestReanimationTrackInstanceStateTransfer() {
    struct FakeDefinition {
        pvzmod::RuntimeReanimatorTrack* tracks;
        int trackCount;
        float fps;
        void* atlas;
    };
    std::array<pvzmod::RuntimeReanimatorTrack, 2> tracks = {{
        {"anim_bucket", nullptr, 0},
        {"Zombie_body", nullptr, 0}
    }};
    FakeDefinition definition{tracks.data(), static_cast<int>(tracks.size()), 12.0f, nullptr};
    alignas(8) std::array<std::byte, 0x60 * 2> instances{};
    alignas(8) std::array<std::byte, 0x60> reanimation{};
    *reinterpret_cast<void**>(reanimation.data() + 0x0C) = &definition;
    *reinterpret_cast<void**>(reanimation.data() + 0x58) = instances.data();
    *reinterpret_cast<int*>(instances.data() + 0x48) = -1;
    *reinterpret_cast<bool*>(instances.data() + 0x5C) = true;
    *reinterpret_cast<bool*>(instances.data() + 0x5D) = false;
    *reinterpret_cast<int*>(instances.data() + 0x60 + 0x48) = 3;

    const auto captured = pvzmod::CaptureReanimationTrackInstanceState(reanimation.data());
    *reinterpret_cast<int*>(instances.data() + 0x48) = 0;
    *reinterpret_cast<bool*>(instances.data() + 0x5C) = false;
    *reinterpret_cast<bool*>(instances.data() + 0x5D) = true;
    *reinterpret_cast<int*>(instances.data() + 0x60 + 0x48) = 0;
    pvzmod::RestoreReanimationTrackInstanceState(reanimation.data(), captured);

    Expect(*reinterpret_cast<int*>(instances.data() + 0x48) == -1 &&
           *reinterpret_cast<bool*>(instances.data() + 0x5C) &&
           !*reinterpret_cast<bool*>(instances.data() + 0x5D),
           "body replacement should preserve hidden equipment and per-track clipping state");
    Expect(*reinterpret_cast<int*>(instances.data() + 0x60 + 0x48) == 3,
           "body replacement should preserve original shield draw order");
}

void TestReanimationPlaybackStateCapture() {
    std::array<pvzmod::RuntimeReanimatorTransform, 6> idleTransforms{};
    std::array<pvzmod::RuntimeReanimatorTransform, 6> walkTransforms{};
    for (auto& transform : idleTransforms) transform.frame = -10000.0f;
    for (auto& transform : walkTransforms) transform.frame = -10000.0f;
    idleTransforms[0].frame = 0.0f;
    idleTransforms[2].frame = -1.0f;
    walkTransforms[0].frame = -1.0f;
    walkTransforms[2].frame = 0.0f;
    walkTransforms[5].frame = -1.0f;
    std::array<pvzmod::RuntimeReanimatorTrack, 2> tracks = {{
        {"anim_idle", idleTransforms.data(), static_cast<int>(idleTransforms.size())},
        {"anim_walk", walkTransforms.data(), static_cast<int>(walkTransforms.size())}
    }};
    pvzmod::RuntimeReanimatorDefinition definition{
        tracks.data(), static_cast<int>(tracks.size()), 12.0f, nullptr};
    alignas(8) std::array<std::byte, 0x60> reanimation{};
    *reinterpret_cast<float*>(reanimation.data() + 0x04) = 0.35f;
    *reinterpret_cast<float*>(reanimation.data() + 0x08) = 11.5f;
    *reinterpret_cast<void**>(reanimation.data() + 0x0C) = &definition;
    *reinterpret_cast<int*>(reanimation.data() + 0x10) = 0;
    *reinterpret_cast<int*>(reanimation.data() + 0x18) = 2;
    *reinterpret_cast<int*>(reanimation.data() + 0x1C) = 3;
    *reinterpret_cast<int*>(reanimation.data() + 0x5C) = 4;

    const auto captured = pvzmod::CaptureReanimationPlaybackState(reanimation.data());
    Expect(captured.actionTrack == "anim_walk" &&
           std::abs(captured.animationTime - 0.35f) < 0.001f &&
           std::abs(captured.animationRate - 11.5f) < 0.001f &&
           captured.loopType == 0 && captured.loopCount == 4,
           "body replacement should identify and preserve the active walking action");

    *reinterpret_cast<int*>(reanimation.data() + 0x18) = 5;
    *reinterpret_cast<int*>(reanimation.data() + 0x1C) = 1;
    Expect(!pvzmod::CaptureReanimationPlaybackState(reanimation.data()).HasAction(),
           "unknown frame ranges should fall back to the configured initial action");
}

}  // namespace

int main() {
    TestAudioConfigs();
    TestSparseConfigParsing();
    TestPlantAnimationOverrideConfig();
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
    TestZombieConfigRejectsInvalidAnimationId();
    TestZombieConfigRejectsUnknownOriginalArmor();
    TestSeedUiAndCustomPlantConfig();
    TestExternalTextureConfigAndAliases();
    TestExternalTextureConfigRejectsTraversal();
    TestExternalAnimationConfigAndRawReanim();
    TestRuntimeReanimDefinitionBuild();
    TestExternalAnimationConfigRejectsUnsafeInput();
    TestExternalAnimationConfigRejectsConflictingMappings();
    TestRawReanimRejectsUnsafeOrMalformedInput();
    TestCompiledReanimDecoding();
    TestExternalAnimationConfigAcceptsCompiledPaths();
    TestRepositoryExternalAnimationExample();
    TestEliteZombieConfigAndPriority();
    TestReanimationCarrierCatalog();
    TestReanimationTrackInstanceStateTransfer();
    TestReanimationPlaybackStateCapture();

    if (g_failures != 0) {
        std::cerr << g_failures << " test(s) failed.\n";
        return 1;
    }
    std::cout << "All PvZ mod config tests passed.\n";
    return 0;
}
