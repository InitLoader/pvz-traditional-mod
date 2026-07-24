#pragma once

#include <string>
#include <unordered_map>

namespace pvzmod {

struct ReanimationTrackInstanceState {
    int renderGroup = 0;
    bool ignoreClipRect = false;
    bool truncateDisappearingFrames = true;
};

using ReanimationTrackInstanceStateMap =
    std::unordered_map<std::string, ReanimationTrackInstanceState>;

// PvZ stores equipment visibility and draw order on each Reanimation instance,
// not in the shared Definition. Preserve those values while replacing a body
// Definition so cone/bucket/door/flag/etc. do not all become visible again.
[[nodiscard]] ReanimationTrackInstanceStateMap CaptureReanimationTrackInstanceState(
    void* reanimation);

void RestoreReanimationTrackInstanceState(
    void* reanimation,
    const ReanimationTrackInstanceStateMap& state);

}  // namespace pvzmod
