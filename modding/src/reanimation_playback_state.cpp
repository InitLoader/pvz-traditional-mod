#include "reanimation_playback_state.h"

#include "runtime_reanim_definition.h"

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <string_view>
#include <utility>

namespace pvzmod {
namespace {

constexpr std::ptrdiff_t kAnimationTimeOffset = 0x04;
constexpr std::ptrdiff_t kAnimationRateOffset = 0x08;
constexpr std::ptrdiff_t kDefinitionOffset = 0x0C;
constexpr std::ptrdiff_t kLoopTypeOffset = 0x10;
constexpr std::ptrdiff_t kFrameStartOffset = 0x18;
constexpr std::ptrdiff_t kFrameCountOffset = 0x1C;
constexpr std::ptrdiff_t kLoopCountOffset = 0x5C;
constexpr float kMissingTransformValue = -9999.0f;
constexpr int kMaximumTrackCount = 10000;
constexpr int kMaximumFrameCount = 20000;

template <typename T>
const T& Field(const void* object, const std::ptrdiff_t offset) {
    return *reinterpret_cast<const T*>(static_cast<const std::uint8_t*>(object) + offset);
}

bool IsActionTrack(const char* name) {
    if (name == nullptr) return false;
    constexpr std::string_view prefix = "anim_";
    const std::string_view value(name);
    return value.size() > prefix.size() &&
        std::equal(prefix.begin(), prefix.end(), value.begin(),
            [](const char left, const char right) {
                return static_cast<unsigned char>(left | 0x20) ==
                    static_cast<unsigned char>(right | 0x20);
            });
}

std::optional<std::pair<int, int>> VisibleFrameRange(const RuntimeReanimatorTrack& track) {
    if (track.transforms == nullptr || track.transformCount <= 0 ||
        track.transformCount > kMaximumFrameCount) return std::nullopt;
    float currentFrame = 0.0f;
    int first = -1;
    int last = -1;
    for (int index = 0; index < track.transformCount; ++index) {
        const float frame = track.transforms[index].frame;
        if (std::isfinite(frame) && frame > kMissingTransformValue) currentFrame = frame;
        if (currentFrame >= 0.0f) {
            if (first < 0) first = index;
            last = index;
        }
    }
    if (first < 0) return std::nullopt;
    return std::pair(first, last - first + 1);
}

}  // namespace

ReanimationPlaybackState CaptureReanimationPlaybackState(const void* reanimation) {
    ReanimationPlaybackState result;
    if (reanimation == nullptr) return result;
    const auto* definition = Field<RuntimeReanimatorDefinition*>(reanimation, kDefinitionOffset);
    const int frameStart = Field<int>(reanimation, kFrameStartOffset);
    const int frameCount = Field<int>(reanimation, kFrameCountOffset);
    if (definition == nullptr || definition->tracks == nullptr ||
        definition->trackCount <= 0 || definition->trackCount > kMaximumTrackCount ||
        frameStart < 0 || frameCount <= 0 || frameCount > kMaximumFrameCount) return result;

    for (int index = 0; index < definition->trackCount; ++index) {
        const RuntimeReanimatorTrack& track = definition->tracks[index];
        if (!IsActionTrack(track.name)) continue;
        const auto range = VisibleFrameRange(track);
        if (!range.has_value() || range->first != frameStart || range->second != frameCount) continue;
        result.actionTrack = track.name;
        break;
    }
    if (!result.HasAction()) return result;

    const float animationTime = Field<float>(reanimation, kAnimationTimeOffset);
    const float animationRate = Field<float>(reanimation, kAnimationRateOffset);
    result.animationTime = std::isfinite(animationTime)
        ? std::clamp(animationTime, 0.0f, 1.0f)
        : 0.0f;
    result.animationRate = std::isfinite(animationRate) ? animationRate : 0.0f;
    result.loopType = Field<int>(reanimation, kLoopTypeOffset);
    result.loopCount = Field<int>(reanimation, kLoopCountOffset);
    return result;
}

}  // namespace pvzmod
