#pragma once

#include "external_animation_config.h"
#include "raw_reanim.h"

#include <cstdint>
#include <deque>
#include <functional>
#include <memory>
#include <string>
#include <string_view>
#include <vector>

namespace pvzmod {

// Binary layouts consumed directly by PvZ 1.0.0.1051 (32-bit).
struct RuntimeReanimatorTransform {
    float transX;
    float transY;
    float skewX;
    float skewY;
    float scaleX;
    float scaleY;
    float frame;
    float alpha;
    void* image;
    void* font;
    const char* text;
};

struct RuntimeReanimatorTrack {
    const char* name;
    RuntimeReanimatorTransform* transforms;
    int transformCount;
};

struct RuntimeReanimatorDefinition {
    RuntimeReanimatorTrack* tracks;
    int trackCount;
    float fps;
    void* atlas;
};

using RuntimeImageResolver = std::function<void*(std::string_view textureId)>;

class RuntimeReanimDefinitionStorage {
public:
    [[nodiscard]] RuntimeReanimatorDefinition* Definition() { return &definition_; }
    [[nodiscard]] const RuntimeReanimatorDefinition* Definition() const { return &definition_; }

private:
    friend struct RuntimeReanimBuildResult;
    friend RuntimeReanimBuildResult BuildRuntimeReanimDefinition(
        const RawReanimDefinition&, const ExternalAnimationDefinition&, const RuntimeImageResolver&);

    std::deque<std::string> strings_;
    std::vector<std::vector<RuntimeReanimatorTransform>> transformBlocks_;
    std::vector<RuntimeReanimatorTrack> tracks_;
    RuntimeReanimatorDefinition definition_{};
};

struct RuntimeReanimBuildResult {
    std::shared_ptr<RuntimeReanimDefinitionStorage> storage;
    std::string error;

    [[nodiscard]] bool Ok() const { return storage != nullptr; }
};

[[nodiscard]] RuntimeReanimBuildResult BuildRuntimeReanimDefinition(
    const RawReanimDefinition& raw,
    const ExternalAnimationDefinition& config,
    const RuntimeImageResolver& imageResolver);

}  // namespace pvzmod
