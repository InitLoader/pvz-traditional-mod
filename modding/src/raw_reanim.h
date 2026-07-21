#pragma once

#include <filesystem>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace pvzmod {

struct RawReanimTransform {
    std::optional<float> x;
    std::optional<float> y;
    std::optional<float> skewX;
    std::optional<float> skewY;
    std::optional<float> scaleX;
    std::optional<float> scaleY;
    std::optional<float> frame;
    std::optional<float> alpha;
    std::optional<std::string> image;
    std::optional<std::string> font;
    std::optional<std::string> text;
};

struct RawReanimTrack {
    std::string name;
    std::vector<RawReanimTransform> transforms;

    [[nodiscard]] std::optional<std::pair<int, int>> VisibleFrameRange() const;
};

struct RawReanimDefinition {
    std::optional<int> doScale;
    float fps = 12.0f;
    std::vector<RawReanimTrack> tracks;

    [[nodiscard]] const RawReanimTrack* FindTrack(std::string_view name) const;
    [[nodiscard]] std::size_t FrameCount() const;
};

struct RawReanimLoadResult {
    std::optional<RawReanimDefinition> definition;
    std::string error;

    [[nodiscard]] bool Ok() const { return definition.has_value(); }
};

[[nodiscard]] RawReanimLoadResult LoadRawReanim(const std::filesystem::path& path);

}  // namespace pvzmod
