#pragma once

#include <string>

namespace pvzmod {

struct ReanimationPlaybackState {
    std::string actionTrack;
    float animationTime = 0.0f;
    float animationRate = 0.0f;
    int loopType = 0;
    int loopCount = 0;

    [[nodiscard]] bool HasAction() const { return !actionTrack.empty(); }
};

// Reanimation stores only the selected frame range, not the action-track name.
// Match that range against anim_* marker tracks before replacing a Definition so
// movement-driving actions such as anim_walk can be restored afterwards.
[[nodiscard]] ReanimationPlaybackState CaptureReanimationPlaybackState(
    const void* reanimation);

}  // namespace pvzmod
