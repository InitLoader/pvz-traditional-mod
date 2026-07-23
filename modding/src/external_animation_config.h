#pragma once

#include <filesystem>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace pvzmod {

enum class ExternalAnimationLoopMode {
    Loop,
    Once,
    OnceHold,
};

struct ExternalAnimationEventDefinition {
    std::string id;
    std::optional<int> frame;
    std::optional<double> normalizedTime;
    bool oncePerLoop = true;
    std::string targetAction;
};

struct ExternalAnimationActionDefinition {
    std::string id;
    std::string track;
    ExternalAnimationLoopMode loop = ExternalAnimationLoopMode::Loop;
    double rate = 12.0;
    int blendFrames = 0;
    std::vector<std::string> replaces;
    std::vector<ExternalAnimationEventDefinition> events;
};

struct ExternalAnimationDefinition {
    std::string id;
    std::string path;
    std::string carrierReanimation;
    std::string initialAction = "idle";
    std::unordered_map<std::string, std::string> images;
    std::unordered_map<std::string, ExternalAnimationActionDefinition> actions;
    std::unordered_map<std::string, std::string> actionReplacements;
    std::unordered_map<std::string, std::string> locators;

    [[nodiscard]] const ExternalAnimationActionDefinition* FindAction(std::string_view actionId) const;
    [[nodiscard]] const ExternalAnimationActionDefinition* FindActionForTrack(
        std::string_view requestedTrack) const;
};

struct ExternalAnimationConfig {
    int schemaVersion = 1;
    std::unordered_map<std::string, ExternalAnimationDefinition> animations;

    [[nodiscard]] const ExternalAnimationDefinition* Find(std::string_view animationId) const;
};

struct ExternalAnimationConfigLoadResult {
    std::optional<ExternalAnimationConfig> config;
    std::string error;

    [[nodiscard]] bool Ok() const { return config.has_value(); }
};

[[nodiscard]] const char* ToString(ExternalAnimationLoopMode mode);
[[nodiscard]] ExternalAnimationConfigLoadResult LoadExternalAnimationConfig(
    const std::filesystem::path& path);

}  // namespace pvzmod
