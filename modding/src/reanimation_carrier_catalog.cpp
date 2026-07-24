#include "reanimation_carrier_catalog.h"

#include <array>
#include <utility>

namespace pvzmod {
namespace {

constexpr std::pair<std::string_view, int> kBodyCarrierTypes[] = {
    {"REANIM_PEASHOOTER", 4}, {"REANIM_WALLNUT", 5}, {"REANIM_LILYPAD", 6},
    {"REANIM_SUNFLOWER", 7}, {"REANIM_CHERRYBOMB", 10}, {"REANIM_SQUASH", 11},
    {"REANIM_DOOMSHROOM", 12}, {"REANIM_SNOWPEA", 13}, {"REANIM_REPEATER", 14},
    {"REANIM_SUNSHROOM", 15}, {"REANIM_TALLNUT", 16}, {"REANIM_FUMESHROOM", 17},
    {"REANIM_PUFFSHROOM", 18}, {"REANIM_HYPNOSHROOM", 19}, {"REANIM_CHOMPER", 20},
    {"REANIM_ZOMBIE", 21}, {"REANIM_POTATOMINE", 23}, {"REANIM_SPIKEWEED", 24},
    {"REANIM_SPIKEROCK", 25}, {"REANIM_THREEPEATER", 26}, {"REANIM_MARIGOLD", 27},
    {"REANIM_ICESHROOM", 28}, {"REANIM_ZOMBIE_FOOTBALL", 29},
    {"REANIM_ZOMBIE_NEWSPAPER", 30}, {"REANIM_ZOMBIE_ZAMBONI", 31},
    {"REANIM_JALAPENO", 33}, {"REANIM_SCRAREYSHROOM", 42}, {"REANIM_PUMPKIN", 43},
    {"REANIM_PLANTERN", 44}, {"REANIM_TORCHWOOD", 45}, {"REANIM_SPLITPEA", 46},
    {"REANIM_SEASHROOM", 47}, {"REANIM_BLOVER", 48}, {"REANIM_FLOWER_POT", 49},
    {"REANIM_CACTUS", 50}, {"REANIM_DANCER", 51}, {"REANIM_TANGLEKELP", 52},
    {"REANIM_STARFRUIT", 53}, {"REANIM_POLEVAULTER", 54}, {"REANIM_BALLOON", 55},
    {"REANIM_GARGANTUAR", 56}, {"REANIM_IMP", 57}, {"REANIM_DIGGER", 58},
    {"REANIM_ZOMBIE_DOLPHINRIDER", 60}, {"REANIM_POGO", 61},
    {"REANIM_BACKUP_DANCER", 62}, {"REANIM_BOBSLED", 63},
    {"REANIM_JACKINTHEBOX", 64}, {"REANIM_SNORKEL", 65}, {"REANIM_BUNGEE", 66},
    {"REANIM_CATAPULT", 67}, {"REANIM_LADDER", 68}, {"REANIM_GRAVE_BUSTER", 71},
    {"REANIM_MAGNETSHROOM", 73}, {"REANIM_BOSS", 74}, {"REANIM_CABBAGEPULT", 75},
    {"REANIM_KERNELPULT", 76}, {"REANIM_MELONPULT", 77}, {"REANIM_COFFEEBEAN", 78},
    {"REANIM_UMBRELLALEAF", 79}, {"REANIM_GATLINGPEA", 80}, {"REANIM_CATTAIL", 81},
    {"REANIM_GLOOMSHROOM", 82}, {"REANIM_COBCANNON", 85}, {"REANIM_GARLIC", 86},
    {"REANIM_GOLD_MAGNET", 87}, {"REANIM_WINTER_MELON", 88},
    {"REANIM_TWIN_SUNFLOWER", 89}, {"REANIM_IMITATER", 93}, {"REANIM_YETI", 94},
};

constexpr std::array<int, 49> kPlantTemplateCarriers = {
    4, 7, 10, 5, 23, 13, 20, 14, 18, 15, 17, 69, 19, 42, 28, 12, 6,
    11, 26, 52, 33, 24, 45, 16, 47, 44, 50, 48, 46, 53, 43, 73, 75, 49,
    76, 78, 86, 79, 27, 77, 80, 89, 82, 81, 88, 87, 25, 85, 93,
};

// ZombieType 0-32. Ducky/I/Z variants reuse the ordinary zombie body;
// accessories remain owned by the original game and are not replaced here.
constexpr std::array<int, 33> kZombieTypeCarriers = {
    21, 21, 21, 54, 21, 30, 21, 29, 51, 62, 21,
    65, 31, 63, 60, 64, 55, 58, 61, 94, 66, 68,
    67, 56, 57, 74, 21, 21, 21, 21, 21, 21, 56,
};

}  // namespace

int ResolveCarrierReanimationType(const std::string_view symbol) {
    for (const auto& [name, value] : kBodyCarrierTypes) {
        if (name == symbol) return value;
    }
    return -1;
}

bool IsBodyCarrierReanimationType(const int reanimationType) {
    for (const auto& [name, value] : kBodyCarrierTypes) {
        (void)name;
        if (value == reanimationType) return true;
    }
    return false;
}

int ResolvePlantTemplateCarrierReanimationType(const int plantType) {
    if (plantType < 0 || plantType >= static_cast<int>(kPlantTemplateCarriers.size())) return -1;
    return kPlantTemplateCarriers[static_cast<std::size_t>(plantType)];
}

int ResolveZombieTypeCarrierReanimationType(const int zombieType) {
    if (zombieType < 0 || zombieType >= static_cast<int>(kZombieTypeCarriers.size())) return -1;
    return kZombieTypeCarriers[static_cast<std::size_t>(zombieType)];
}

}  // namespace pvzmod
