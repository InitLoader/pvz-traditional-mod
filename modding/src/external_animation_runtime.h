#pragma once

#include "external_animation_config.h"
#include "raw_reanim.h"
#include "runtime_reanim_definition.h"

#include <cstddef>
#include <cstdint>
#include <memory>
#include <string_view>

namespace pvzmod {

struct LoadedExternalAnimation {
    ExternalAnimationDefinition config;
    RawReanimDefinition raw;
};

[[nodiscard]] bool InitializeExternalAnimationRuntime(std::uint8_t* moduleBase);
[[nodiscard]] std::shared_ptr<const LoadedExternalAnimation> FindExternalAnimation(
    std::string_view animationId);
[[nodiscard]] RuntimeReanimatorDefinition* PrepareExternalReanimationDefinition(
    std::string_view animationId, void* lawnApp);
[[nodiscard]] std::size_t ExternalAnimationCount();

}  // namespace pvzmod
