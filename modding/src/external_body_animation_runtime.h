#pragma once

#include <cstddef>
#include <cstdint>
#include <string_view>

namespace pvzmod {

// Owns the exact-version Reanimation ABI used by both plants and zombies.
// Registrations are keyed by the original carrier ReanimationType so the
// vanilla save loader can restore the correct persistent external Definition.
[[nodiscard]] bool InitializeExternalBodyAnimationRuntime(std::uint8_t* moduleBase);
[[nodiscard]] bool RegisterExternalBodyAnimation(std::string_view animationId);
[[nodiscard]] bool RegisterExternalBodyAnimationForCarrier(
    std::string_view animationId,
    int carrierReanimationType);

// Replaces one already-created body Reanimation in place. All validation,
// texture resolution and save-restore registration must succeed before the
// original TrackInstance array is released.
[[nodiscard]] bool ApplyExternalBodyAnimation(
    void* owner,
    std::ptrdiff_t lawnAppOffset,
    std::ptrdiff_t bodyReanimationIdOffset,
    std::string_view animationId,
    std::string_view ownerLabel);

}  // namespace pvzmod
