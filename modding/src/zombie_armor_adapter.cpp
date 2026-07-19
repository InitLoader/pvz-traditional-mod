#include "zombie_armor_adapter.h"

#include <stdexcept>

namespace pvzmod {
namespace {

// Specialized visuals use a harmless native health carrier (bucket/door) on
// plain bodies. Their own reanimation is rendered as a filtered attachment.
constexpr ArmorVisualAdapterInfo kCone = {ArmorSlot::Helmet, 1, -1, -1, false, false};
constexpr ArmorVisualAdapterInfo kBucket = {ArmorSlot::Helmet, 2, -1, -1, false, false};
constexpr ArmorVisualAdapterInfo kDoor = {ArmorSlot::Shield, 1, -1, -1, false, false};
constexpr ArmorVisualAdapterInfo kNewspaper = {ArmorSlot::Shield, 1, -1, 30, false, false};
constexpr ArmorVisualAdapterInfo kFootballHelmet = {ArmorSlot::Helmet, 2, -1, 29, false, false};
constexpr ArmorVisualAdapterInfo kBobsled = {ArmorSlot::Helmet, 2, -1, 63, false, false};
constexpr ArmorVisualAdapterInfo kBalloon = {ArmorSlot::Flying, 0, -1, 55, false, false};
constexpr ArmorVisualAdapterInfo kDiggerHelmet = {ArmorSlot::Helmet, 2, -1, 58, false, false};
constexpr ArmorVisualAdapterInfo kLadder = {ArmorSlot::Shield, 1, -1, 68, false, false};
constexpr ArmorVisualAdapterInfo kWallnutHead = {ArmorSlot::Helmet, 8, 27, -1, false, true};
constexpr ArmorVisualAdapterInfo kTallnutHead = {ArmorSlot::Helmet, 9, 31, -1, false, true};

}  // namespace

const ArmorVisualAdapterInfo& GetArmorVisualAdapterInfo(const ArmorVisual visual) {
    switch (visual) {
        case ArmorVisual::Cone: return kCone;
        case ArmorVisual::Bucket: return kBucket;
        case ArmorVisual::Door: return kDoor;
        case ArmorVisual::Newspaper: return kNewspaper;
        case ArmorVisual::FootballHelmet: return kFootballHelmet;
        case ArmorVisual::Bobsled: return kBobsled;
        case ArmorVisual::Balloon: return kBalloon;
        case ArmorVisual::DiggerHelmet: return kDiggerHelmet;
        case ArmorVisual::Ladder: return kLadder;
        case ArmorVisual::WallnutHead: return kWallnutHead;
        case ArmorVisual::TallnutHead: return kTallnutHead;
    }
    throw std::runtime_error("unknown ArmorVisual enum value");
}

const char* ArmorVisualConfigName(const ArmorVisual visual) {
    switch (visual) {
        case ArmorVisual::Cone: return "cone";
        case ArmorVisual::Bucket: return "bucket";
        case ArmorVisual::Door: return "door";
        case ArmorVisual::Newspaper: return "newspaper";
        case ArmorVisual::FootballHelmet: return "footballHelmet";
        case ArmorVisual::Bobsled: return "bobsled";
        case ArmorVisual::Balloon: return "balloon";
        case ArmorVisual::DiggerHelmet: return "diggerHelmet";
        case ArmorVisual::Ladder: return "ladder";
        case ArmorVisual::WallnutHead: return "wallnutHead";
        case ArmorVisual::TallnutHead: return "tallnutHead";
    }
    return "unknown";
}

bool RequiresOriginalZombieInitializer(const ArmorVisual visual) {
    return GetArmorVisualAdapterInfo(visual).initializerZombieType >= 0;
}

bool UsesIndependentArmorOverlay(const ArmorVisual visual) {
    return GetArmorVisualAdapterInfo(visual).overlayReanimationType >= 0;
}

}  // namespace pvzmod
