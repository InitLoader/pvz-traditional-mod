#include "external_animation_config.h"

#include "external_texture_config.h"

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstring>
#include <fstream>
#include <stdexcept>

#include <nlohmann/json.hpp>

namespace pvzmod {
namespace {

using nlohmann::json;

constexpr std::size_t kMaxAnimations = 256;
constexpr std::size_t kMaxImageBindings = 512;
constexpr std::size_t kMaxActions = 64;
constexpr std::size_t kMaxEventsPerAction = 64;
constexpr std::size_t kMaxLocators = 64;

std::string RequiredString(const json& object, const char* key) {
    const auto found = object.find(key);
    if (found == object.end() || !found->is_string()) {
        throw std::runtime_error(std::string(key) + " must be a string");
    }
    return found->get<std::string>();
}

std::string ValidateAnimationPath(const std::string& rawPath) {
    if (rawPath.empty()) throw std::runtime_error("path must not be empty");
    if (rawPath.find('\0') != std::string::npos) throw std::runtime_error("path contains a null byte");
    std::filesystem::path path = std::filesystem::u8path(rawPath);
    if (path.is_absolute() || path.has_root_name() || path.has_root_directory()) {
        throw std::runtime_error("path must be relative to the game directory");
    }
    for (const auto& component : path) {
        if (component == "..") throw std::runtime_error("path must not contain '..'");
    }
    path = path.lexically_normal();
    auto component = path.begin();
    if (component == path.end()) throw std::runtime_error("path must not be empty");
    const bool modAnimation = _stricmp(component->string().c_str(), "pvzmod") == 0;
    const bool originalCompiled = _stricmp(component->string().c_str(), "compiled") == 0;
    ++component;
    if (component == path.end()) throw std::runtime_error("path must name an animation file");
    if (modAnimation && _stricmp(component->string().c_str(), "animations") != 0) {
        throw std::runtime_error("mod animation paths must be inside pvzmod/animations");
    }
    if (originalCompiled && _stricmp(component->string().c_str(), "reanim") != 0) {
        throw std::runtime_error("original compiled paths must be inside compiled/reanim");
    }
    if (!modAnimation && !originalCompiled) {
        throw std::runtime_error("path must be inside pvzmod/animations or compiled/reanim");
    }
    ++component;
    if (component == path.end()) throw std::runtime_error("path must name an animation file");

    std::string fileName = path.filename().string();
    std::transform(fileName.begin(), fileName.end(), fileName.begin(), [](const unsigned char character) {
        return static_cast<char>(std::tolower(character));
    });
    const bool raw = fileName.ends_with(".reanim") && !fileName.ends_with(".reanim.compiled");
    const bool compiled = fileName.ends_with(".reanim.compiled");
    if (!raw && !compiled) {
        throw std::runtime_error("animation path must end with .reanim or .reanim.compiled");
    }
    if (originalCompiled && !compiled) {
        throw std::runtime_error("compiled/reanim only accepts original .reanim.compiled files");
    }
    return path.generic_string();
}

ExternalAnimationLoopMode ParseLoopMode(const std::string& value) {
    if (value == "loop") return ExternalAnimationLoopMode::Loop;
    if (value == "once") return ExternalAnimationLoopMode::Once;
    if (value == "once_hold") return ExternalAnimationLoopMode::OnceHold;
    throw std::runtime_error("loop must be loop, once, or once_hold");
}

void ValidateTrackName(const std::string_view value, const char* fieldName) {
    if (value.empty() || value.size() > 128) {
        throw std::runtime_error(std::string(fieldName) + " must contain 1-128 characters");
    }
    if (value.find('\0') != std::string_view::npos) {
        throw std::runtime_error(std::string(fieldName) + " contains a null byte");
    }
}

void ParseStringMap(
    const json& object, const char* fieldName, const std::size_t maximum,
    std::unordered_map<std::string, std::string>& output, const bool valuesAreTextureIds) {
    const auto found = object.find(fieldName);
    if (found == object.end()) return;
    if (!found->is_object()) throw std::runtime_error(std::string(fieldName) + " must be an object");
    if (found->size() > maximum) throw std::runtime_error(std::string(fieldName) + " has too many entries");
    for (const auto& [key, value] : found->items()) {
        ValidateTrackName(key, fieldName);
        if (!value.is_string()) {
            throw std::runtime_error(std::string(fieldName) + " values must be strings");
        }
        const std::string parsedValue = value.get<std::string>();
        if (valuesAreTextureIds) {
            if (!IsExternalResourceId(parsedValue)) {
                throw std::runtime_error(std::string(fieldName) + " texture ids must match [A-Za-z0-9_]+");
            }
        } else {
            ValidateTrackName(parsedValue, fieldName);
        }
        output.emplace(key, parsedValue);
    }
}

ExternalAnimationActionDefinition ParseAction(const std::string& id, const json& value) {
    if (!IsExternalResourceId(id)) throw std::runtime_error("action ids must match [A-Za-z0-9_]+");
    if (!value.is_object()) throw std::runtime_error("each action must be an object");
    ExternalAnimationActionDefinition action;
    action.id = id;
    action.track = RequiredString(value, "track");
    ValidateTrackName(action.track, "action track");
    action.loop = ParseLoopMode(value.value("loop", "loop"));
    action.rate = value.value("rate", 12.0);
    if (!std::isfinite(action.rate) || action.rate <= 0.0 || action.rate > 120.0) {
        throw std::runtime_error("action rate must be finite and in (0, 120]");
    }
    action.blendFrames = value.value("blendFrames", 0);
    if (action.blendFrames < 0 || action.blendFrames > 120) {
        throw std::runtime_error("blendFrames must be in [0, 120]");
    }
    const auto events = value.find("events");
    if (events == value.end()) return action;
    if (!events->is_array()) throw std::runtime_error("action events must be an array");
    if (events->size() > kMaxEventsPerAction) throw std::runtime_error("an action has too many events");
    for (const json& eventValue : *events) {
        if (!eventValue.is_object()) throw std::runtime_error("each action event must be an object");
        ExternalAnimationEventDefinition event;
        event.id = RequiredString(eventValue, "id");
        if (!IsExternalResourceId(event.id)) {
            throw std::runtime_error("event ids must match [A-Za-z0-9_]+");
        }
        const auto frame = eventValue.find("frame");
        const auto normalized = eventValue.find("normalizedTime");
        if ((frame == eventValue.end()) == (normalized == eventValue.end())) {
            throw std::runtime_error("an event must define exactly one of frame or normalizedTime");
        }
        if (frame != eventValue.end()) {
            if (!frame->is_number_integer()) throw std::runtime_error("event frame must be an integer");
            event.frame = frame->get<int>();
            if (*event.frame < 0 || *event.frame > 100000) {
                throw std::runtime_error("event frame must be in [0, 100000]");
            }
        } else {
            if (!normalized->is_number()) throw std::runtime_error("normalizedTime must be a number");
            event.normalizedTime = normalized->get<double>();
            if (!std::isfinite(*event.normalizedTime) || *event.normalizedTime < 0.0 ||
                *event.normalizedTime > 1.0) {
                throw std::runtime_error("normalizedTime must be finite and in [0, 1]");
            }
        }
        event.oncePerLoop = eventValue.value("oncePerLoop", true);
        action.events.push_back(std::move(event));
    }
    return action;
}

}  // namespace

const ExternalAnimationActionDefinition* ExternalAnimationDefinition::FindAction(
    const std::string_view actionId) const {
    const auto found = actions.find(std::string(actionId));
    return found == actions.end() ? nullptr : &found->second;
}

const ExternalAnimationDefinition* ExternalAnimationConfig::Find(
    const std::string_view animationId) const {
    const auto found = animations.find(std::string(animationId));
    return found == animations.end() ? nullptr : &found->second;
}

const char* ToString(const ExternalAnimationLoopMode mode) {
    switch (mode) {
    case ExternalAnimationLoopMode::Loop: return "loop";
    case ExternalAnimationLoopMode::Once: return "once";
    case ExternalAnimationLoopMode::OnceHold: return "once_hold";
    }
    return "unknown";
}

ExternalAnimationConfigLoadResult LoadExternalAnimationConfig(const std::filesystem::path& path) {
    ExternalAnimationConfigLoadResult result;
    std::ifstream input(path, std::ios::binary);
    if (!input) {
        result.error = "Cannot open external animation config: " + path.string();
        return result;
    }
    try {
        const json root = json::parse(input, nullptr, true, true);
        if (!root.is_object()) throw std::runtime_error("root must be an object");
        ExternalAnimationConfig parsed;
        parsed.schemaVersion = root.value("schemaVersion", 1);
        if (parsed.schemaVersion != 1) throw std::runtime_error("unsupported schemaVersion; expected 1");
        const auto animations = root.find("animations");
        if (animations == root.end() || !animations->is_array()) {
            throw std::runtime_error("animations must be an array");
        }
        if (animations->size() > kMaxAnimations) throw std::runtime_error("at most 256 animations are supported");
        for (const json& value : *animations) {
            if (!value.is_object()) throw std::runtime_error("each animation must be an object");
            ExternalAnimationDefinition animation;
            animation.id = RequiredString(value, "id");
            if (!IsExternalResourceId(animation.id)) {
                throw std::runtime_error("animation id must match [A-Za-z0-9_]+ and contain 1-64 characters");
            }
            animation.path = ValidateAnimationPath(RequiredString(value, "path"));
            animation.carrierReanimation = RequiredString(value, "carrierReanimation");
            if (!IsExternalResourceId(animation.carrierReanimation)) {
                throw std::runtime_error("carrierReanimation must match [A-Za-z0-9_]+");
            }
            ParseStringMap(value, "images", kMaxImageBindings, animation.images, true);
            ParseStringMap(value, "locators", kMaxLocators, animation.locators, false);

            const auto actions = value.find("actions");
            if (actions == value.end() || !actions->is_object() || actions->empty()) {
                throw std::runtime_error("actions must be a non-empty object");
            }
            if (actions->size() > kMaxActions) throw std::runtime_error("an animation has too many actions");
            for (const auto& [actionId, actionValue] : actions->items()) {
                ExternalAnimationActionDefinition action = ParseAction(actionId, actionValue);
                animation.actions.emplace(action.id, std::move(action));
            }
            if (!parsed.animations.emplace(animation.id, std::move(animation)).second) {
                throw std::runtime_error("animation id values must be unique");
            }
        }
        result.config = std::move(parsed);
    } catch (const std::exception& exception) {
        result.error = std::string("External animation config validation failed: ") + exception.what();
    }
    return result;
}

}  // namespace pvzmod
