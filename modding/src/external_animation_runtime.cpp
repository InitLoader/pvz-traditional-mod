#include "external_animation_runtime.h"

#include "external_texture_runtime.h"
#include "logger.h"
#include "reanim_loader.h"

#include <filesystem>
#include <cwchar>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>

namespace pvzmod {
namespace {

std::mutex g_animationMutex;
std::unordered_map<std::string, std::shared_ptr<const LoadedExternalAnimation>> g_animations;

bool IsUnderDirectory(const std::filesystem::path& child, const std::filesystem::path& parent) {
    auto childPart = child.begin();
    auto parentPart = parent.begin();
    for (; parentPart != parent.end(); ++parentPart, ++childPart) {
        if (childPart == child.end()) return false;
        if (_wcsicmp(childPart->c_str(), parentPart->c_str()) != 0) return false;
    }
    return true;
}

std::string ValidateAnimationDefinition(
    const ExternalAnimationDefinition& config, const RawReanimDefinition& raw) {
    for (const auto& [symbol, textureId] : config.images) {
        if (!ExternalTextureIsRegistered(textureId)) {
            return "image symbol '" + symbol + "' references unregistered texture id '" + textureId + "'";
        }
    }
    for (const auto& [actionId, action] : config.actions) {
        const RawReanimTrack* track = raw.FindTrack(action.track);
        if (track == nullptr) {
            return "action '" + actionId + "' references missing track '" + action.track + "'";
        }
        const auto visibleRange = track->VisibleFrameRange();
        if (!visibleRange.has_value()) {
            return "action '" + actionId + "' track has no visible frames";
        }
        for (const ExternalAnimationEventDefinition& event : action.events) {
            if (event.frame.has_value() && *event.frame >= visibleRange->second) {
                return "event '" + event.id + "' frame is outside action '" + actionId + "'";
            }
        }
    }
    for (const auto& [logicalName, trackName] : config.locators) {
        if (raw.FindTrack(trackName) == nullptr) {
            return "locator '" + logicalName + "' references missing track '" + trackName + "'";
        }
    }
    return {};
}

}  // namespace

bool InitializeExternalAnimationRuntime(std::uint8_t* moduleBase) {
    if (moduleBase == nullptr) return false;
    const std::filesystem::path root = ModuleDirectory();
    const std::filesystem::path configPath =
        root / L"pvzmod" / L"config" / L"resources" / L"animations.jsonc";
    if (!std::filesystem::exists(configPath)) {
        LogInfo("External animation registry is absent; external animations are disabled.");
        std::lock_guard lock(g_animationMutex);
        g_animations.clear();
        return true;
    }

    const ExternalAnimationConfigLoadResult loadedConfig = LoadExternalAnimationConfig(configPath);
    if (!loadedConfig.Ok()) {
        LogWarning(loadedConfig.error + "; external animations are disabled.");
        std::lock_guard lock(g_animationMutex);
        g_animations.clear();
        return false;
    }

    std::error_code error;
    const std::filesystem::path modAnimationRoot = std::filesystem::weakly_canonical(
        root / L"pvzmod" / L"animations", error);
    if (error) {
        LogWarning("Cannot resolve pvzmod/animations: " + error.message());
        return false;
    }
    error.clear();
    const std::filesystem::path originalAnimationRoot = std::filesystem::weakly_canonical(
        root / L"compiled" / L"reanim", error);
    if (error) {
        LogWarning("Cannot resolve compiled/reanim: " + error.message());
        return false;
    }

    std::unordered_map<std::string, std::shared_ptr<const LoadedExternalAnimation>> next;
    for (const auto& [animationId, config] : loadedConfig.config->animations) {
        error.clear();
        const std::filesystem::path absolutePath = std::filesystem::weakly_canonical(
            root / std::filesystem::u8path(config.path), error);
        const bool allowedPath = !error &&
            (IsUnderDirectory(absolutePath, modAnimationRoot) ||
             (IsCompiledReanimPath(absolutePath) && IsUnderDirectory(absolutePath, originalAnimationRoot)));
        if (!allowedPath) {
            LogWarning("External animation '" + animationId +
                       "' escaped pvzmod/animations or compiled/reanim and was skipped.");
            error.clear();
            continue;
        }
        error.clear();
        if (!std::filesystem::is_regular_file(absolutePath, error) || error) {
            LogWarning("External animation '" + animationId + "' file was not found: " + config.path);
            error.clear();
            continue;
        }
        RawReanimLoadResult loadedRaw = LoadReanimDefinition(absolutePath);
        if (!loadedRaw.Ok()) {
            LogWarning(loadedRaw.error + "; animation '" + animationId + "' was skipped.");
            continue;
        }
        const std::string validationError = ValidateAnimationDefinition(config, *loadedRaw.definition);
        if (!validationError.empty()) {
            LogWarning("External animation '" + animationId + "' was skipped: " + validationError + ".");
            continue;
        }
        auto animation = std::make_shared<LoadedExternalAnimation>();
        animation->config = config;
        animation->raw = std::move(*loadedRaw.definition);
        next.emplace(animationId, std::move(animation));
    }

    const std::size_t configured = loadedConfig.config->animations.size();
    const std::size_t accepted = next.size();
    {
        std::lock_guard lock(g_animationMutex);
        g_animations = std::move(next);
    }
    LogInfo("Loaded external animation registry: " + std::to_string(accepted) + "/" +
            std::to_string(configured) + " animation(s) validated. Runtime engine injection is not enabled yet.");
    return accepted == configured;
}

std::shared_ptr<const LoadedExternalAnimation> FindExternalAnimation(const std::string_view animationId) {
    std::lock_guard lock(g_animationMutex);
    const auto found = g_animations.find(std::string(animationId));
    return found == g_animations.end() ? nullptr : found->second;
}

std::size_t ExternalAnimationCount() {
    std::lock_guard lock(g_animationMutex);
    return g_animations.size();
}

}  // namespace pvzmod
