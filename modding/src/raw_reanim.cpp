#include "raw_reanim.h"

#include <algorithm>
#include <cctype>
#include <cstdint>
#include <cmath>
#include <cstring>
#include <fstream>
#include <limits>
#include <stdexcept>
#include <unordered_set>

#include <tinyxml2.h>

namespace pvzmod {
namespace {

constexpr std::uintmax_t kMaxFileSize = 16U * 1024U * 1024U;
constexpr std::size_t kMaxTracks = 512;
constexpr std::size_t kMaxFramesPerTrack = 20000;
constexpr std::size_t kMaxTotalTransforms = 500000;

std::string ReadTextFile(const std::filesystem::path& path) {
    std::error_code error;
    const std::uintmax_t size = std::filesystem::file_size(path, error);
    if (error) throw std::runtime_error("cannot read file size: " + error.message());
    if (size == 0 || size > kMaxFileSize) throw std::runtime_error("file size must be in [1, 16 MiB]");
    std::ifstream input(path, std::ios::binary);
    if (!input) throw std::runtime_error("cannot open file");
    std::string content(static_cast<std::size_t>(size), '\0');
    input.read(content.data(), static_cast<std::streamsize>(content.size()));
    if (!input) throw std::runtime_error("cannot read the complete file");
    if (content.starts_with("\xEF\xBB\xBF")) content.erase(0, 3);
    if (content.find("<!DOCTYPE") != std::string::npos ||
        content.find("<!ENTITY") != std::string::npos) {
        throw std::runtime_error("DTD and XML entities are not allowed");
    }
    return content;
}

float ParseFiniteFloat(const tinyxml2::XMLElement& element, const char* fieldName) {
    float value = 0.0f;
    if (element.QueryFloatText(&value) != tinyxml2::XML_SUCCESS || !std::isfinite(value)) {
        throw std::runtime_error(std::string(fieldName) + " must be a finite float");
    }
    if (value < -100000.0f || value > 100000.0f) {
        throw std::runtime_error(std::string(fieldName) + " is outside the safe range");
    }
    return value;
}

std::string ParseText(const tinyxml2::XMLElement& element, const char* fieldName, const std::size_t maximum) {
    const char* text = element.GetText();
    const std::string value = text == nullptr ? std::string() : std::string(text);
    if (value.size() > maximum || value.find('\0') != std::string::npos) {
        throw std::runtime_error(std::string(fieldName) + " is too long or contains a null byte");
    }
    return value;
}

template <typename T>
void SetOnce(std::optional<T>& destination, T value, const char* fieldName) {
    if (destination.has_value()) {
        throw std::runtime_error(std::string("duplicate transform field: ") + fieldName);
    }
    destination = std::move(value);
}

RawReanimTransform ParseTransform(const tinyxml2::XMLElement& transformElement) {
    RawReanimTransform transform;
    for (const tinyxml2::XMLElement* field = transformElement.FirstChildElement(); field != nullptr;
         field = field->NextSiblingElement()) {
        const std::string_view name(field->Name());
        if (name == "x") SetOnce(transform.x, ParseFiniteFloat(*field, "x"), "x");
        else if (name == "y") SetOnce(transform.y, ParseFiniteFloat(*field, "y"), "y");
        else if (name == "kx") SetOnce(transform.skewX, ParseFiniteFloat(*field, "kx"), "kx");
        else if (name == "ky") SetOnce(transform.skewY, ParseFiniteFloat(*field, "ky"), "ky");
        else if (name == "sx") SetOnce(transform.scaleX, ParseFiniteFloat(*field, "sx"), "sx");
        else if (name == "sy") SetOnce(transform.scaleY, ParseFiniteFloat(*field, "sy"), "sy");
        else if (name == "f") SetOnce(transform.frame, ParseFiniteFloat(*field, "f"), "f");
        else if (name == "a") {
            const float alpha = ParseFiniteFloat(*field, "a");
            if (alpha < 0.0f || alpha > 1.0f) throw std::runtime_error("alpha must be in [0, 1]");
            SetOnce(transform.alpha, alpha, "a");
        } else if (name == "i") SetOnce(transform.image, ParseText(*field, "i", 128), "i");
        else if (name == "font") SetOnce(transform.font, ParseText(*field, "font", 128), "font");
        else if (name == "text") SetOnce(transform.text, ParseText(*field, "text", 1024), "text");
        else throw std::runtime_error("unsupported transform field: " + std::string(name));
    }
    return transform;
}

RawReanimTrack ParseTrack(const tinyxml2::XMLElement& trackElement) {
    RawReanimTrack track;
    bool sawName = false;
    for (const tinyxml2::XMLElement* child = trackElement.FirstChildElement(); child != nullptr;
         child = child->NextSiblingElement()) {
        const std::string_view name(child->Name());
        if (name == "name") {
            if (sawName) throw std::runtime_error("track contains multiple name fields");
            track.name = ParseText(*child, "track name", 128);
            sawName = true;
        } else if (name == "t") {
            if (track.transforms.size() >= kMaxFramesPerTrack) {
                throw std::runtime_error("track exceeds 20000 frames");
            }
            track.transforms.push_back(ParseTransform(*child));
        } else {
            throw std::runtime_error("unsupported track field: " + std::string(name));
        }
    }
    if (!sawName || track.name.empty()) throw std::runtime_error("track name must not be empty");
    if (track.transforms.empty()) throw std::runtime_error("track must contain at least one t element");

    RawReanimTransform inherited;
    inherited.x = 0.0f;
    inherited.y = 0.0f;
    inherited.skewX = 0.0f;
    inherited.skewY = 0.0f;
    inherited.scaleX = 1.0f;
    inherited.scaleY = 1.0f;
    inherited.frame = 0.0f;
    inherited.alpha = 1.0f;
    for (RawReanimTransform& transform : track.transforms) {
#define PVZMOD_INHERIT_REANIM_FIELD(field) \
        if (transform.field.has_value()) inherited.field = transform.field; \
        else transform.field = inherited.field
        PVZMOD_INHERIT_REANIM_FIELD(x);
        PVZMOD_INHERIT_REANIM_FIELD(y);
        PVZMOD_INHERIT_REANIM_FIELD(skewX);
        PVZMOD_INHERIT_REANIM_FIELD(skewY);
        PVZMOD_INHERIT_REANIM_FIELD(scaleX);
        PVZMOD_INHERIT_REANIM_FIELD(scaleY);
        PVZMOD_INHERIT_REANIM_FIELD(frame);
        PVZMOD_INHERIT_REANIM_FIELD(alpha);
        PVZMOD_INHERIT_REANIM_FIELD(image);
        PVZMOD_INHERIT_REANIM_FIELD(font);
        PVZMOD_INHERIT_REANIM_FIELD(text);
#undef PVZMOD_INHERIT_REANIM_FIELD
    }
    return track;
}

}  // namespace

std::optional<std::pair<int, int>> RawReanimTrack::VisibleFrameRange() const {
    float currentFrame = 0.0f;
    int first = -1;
    int last = -1;
    for (std::size_t index = 0; index < transforms.size(); ++index) {
        if (transforms[index].frame.has_value()) currentFrame = *transforms[index].frame;
        if (currentFrame >= 0.0f) {
            if (first < 0) first = static_cast<int>(index);
            last = static_cast<int>(index);
        }
    }
    if (first < 0) return std::nullopt;
    return std::pair(first, last - first + 1);
}

const RawReanimTrack* RawReanimDefinition::FindTrack(const std::string_view name) const {
    const std::string ownedName(name);
    const auto found = std::find_if(tracks.begin(), tracks.end(), [&ownedName](const RawReanimTrack& track) {
        return _stricmp(track.name.c_str(), ownedName.c_str()) == 0;
    });
    return found == tracks.end() ? nullptr : &*found;
}

std::size_t RawReanimDefinition::FrameCount() const {
    return tracks.empty() ? 0 : tracks.front().transforms.size();
}

RawReanimLoadResult LoadRawReanim(const std::filesystem::path& path) {
    RawReanimLoadResult result;
    try {
        std::string content = ReadTextFile(path);
        content.insert(0, "<pvzmod_reanim_root>");
        content.append("</pvzmod_reanim_root>");

        tinyxml2::XMLDocument document(false, tinyxml2::PRESERVE_WHITESPACE);
        const tinyxml2::XMLError parseResult = document.Parse(content.data(), content.size());
        if (parseResult != tinyxml2::XML_SUCCESS) {
            throw std::runtime_error(std::string("XML parse failed: ") + document.ErrorStr());
        }
        const tinyxml2::XMLElement* root = document.FirstChildElement("pvzmod_reanim_root");
        if (root == nullptr) throw std::runtime_error("internal XML wrapper was not created");

        RawReanimDefinition definition;
        bool sawFps = false;
        std::unordered_set<std::string> trackNames;
        std::size_t totalTransforms = 0;
        for (const tinyxml2::XMLElement* child = root->FirstChildElement(); child != nullptr;
             child = child->NextSiblingElement()) {
            const std::string_view name(child->Name());
            if (name == "doScale") {
                if (definition.doScale.has_value()) throw std::runtime_error("duplicate doScale");
                int value = 0;
                if (child->QueryIntText(&value) != tinyxml2::XML_SUCCESS || (value != 0 && value != 1)) {
                    throw std::runtime_error("doScale must be 0 or 1");
                }
                definition.doScale = value;
            } else if (name == "fps") {
                if (sawFps) throw std::runtime_error("duplicate fps");
                definition.fps = ParseFiniteFloat(*child, "fps");
                if (definition.fps <= 0.0f || definition.fps > 120.0f) {
                    throw std::runtime_error("fps must be in (0, 120]");
                }
                sawFps = true;
            } else if (name == "track") {
                if (definition.tracks.size() >= kMaxTracks) throw std::runtime_error("too many tracks");
                RawReanimTrack track = ParseTrack(*child);
                std::string folded = track.name;
                std::transform(folded.begin(), folded.end(), folded.begin(), [](const unsigned char c) {
                    return static_cast<char>(std::tolower(c));
                });
                if (!trackNames.insert(folded).second) throw std::runtime_error("track names must be unique");
                totalTransforms += track.transforms.size();
                if (totalTransforms > kMaxTotalTransforms) throw std::runtime_error("too many total transforms");
                definition.tracks.push_back(std::move(track));
            } else {
                throw std::runtime_error("unsupported root element: " + std::string(name));
            }
        }
        if (!sawFps) throw std::runtime_error("fps is required");
        if (definition.tracks.empty()) throw std::runtime_error("at least one track is required");
        const std::size_t frameCount = definition.FrameCount();
        for (const RawReanimTrack& track : definition.tracks) {
            if (track.transforms.size() != frameCount) {
                throw std::runtime_error("all tracks must contain the same number of frames");
            }
        }
        result.definition = std::move(definition);
    } catch (const std::exception& exception) {
        result.error = std::string("Raw reanim validation failed for ") + path.string() + ": " + exception.what();
    }
    return result;
}

}  // namespace pvzmod
