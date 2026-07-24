#include "audio_sample_config.h"

#include <algorithm>
#include <cctype>
#include <fstream>
#include <stdexcept>
#include <unordered_set>

#include <nlohmann/json.hpp>

namespace pvzmod {
namespace {

using nlohmann::json;

std::string ValidateSamplePath(const std::string& rawPath) {
    if (rawPath.empty()) throw std::runtime_error("sample path must not be empty");
    if (rawPath.find('\0') != std::string::npos) throw std::runtime_error("sample path contains a null byte");
    std::filesystem::path path = std::filesystem::u8path(rawPath).lexically_normal();
    if (path.is_absolute() || path.has_root_name() || path.has_root_directory()) {
        throw std::runtime_error("sample path must be relative to the game directory");
    }
    for (const auto& component : path) {
        if (component == "..") throw std::runtime_error("sample path must not contain '..'");
    }
    auto component = path.begin();
    if (component == path.end() || NormalizeAudioId(component->string()) != "PVZMOD") {
        throw std::runtime_error("sample path must be inside pvzmod/audio/samples");
    }
    ++component;
    if (component == path.end() || NormalizeAudioId(component->string()) != "AUDIO") {
        throw std::runtime_error("sample path must be inside pvzmod/audio/samples");
    }
    ++component;
    if (component == path.end() || NormalizeAudioId(component->string()) != "SAMPLES") {
        throw std::runtime_error("sample path must be inside pvzmod/audio/samples");
    }
    ++component;
    if (component == path.end()) throw std::runtime_error("sample path must name a file");

    std::string extension = path.extension().string();
    std::transform(extension.begin(), extension.end(), extension.begin(), [](unsigned char character) {
        return static_cast<char>(std::tolower(character));
    });
    static const std::unordered_set<std::string> kExtensions = {".ogg", ".wav", ".au"};
    if (!kExtensions.contains(extension)) {
        throw std::runtime_error("sample extension must be ogg, wav, or au");
    }
    return path.generic_string();
}

}  // namespace

std::string NormalizeAudioId(const std::string_view id) {
    std::string normalized(id);
    std::transform(normalized.begin(), normalized.end(), normalized.begin(), [](unsigned char character) {
        return static_cast<char>(std::toupper(character));
    });
    return normalized;
}

bool IsAudioAssetId(const std::string_view id) {
    if (id.empty() || id.size() > 64) return false;
    return std::all_of(id.begin(), id.end(), [](unsigned char character) {
        return std::isalnum(character) != 0 || character == '_';
    });
}

const AudioSampleDefinition* AudioSampleConfig::Find(const std::string_view id) const {
    const auto found = samples.find(NormalizeAudioId(id));
    return found == samples.end() ? nullptr : &found->second;
}

AudioSampleConfigLoadResult LoadAudioSampleConfig(const std::filesystem::path& path) {
    AudioSampleConfigLoadResult result;
    std::ifstream input(path, std::ios::binary);
    if (!input) {
        result.error = "Cannot open audio sample config: " + path.string();
        return result;
    }
    try {
        const json root = json::parse(input, nullptr, true, true);
        if (!root.is_object()) throw std::runtime_error("root must be an object");
        AudioSampleConfig parsed;
        parsed.schemaVersion = root.value("schemaVersion", 1);
        if (parsed.schemaVersion != 1) throw std::runtime_error("unsupported schemaVersion; expected 1");
        const auto samples = root.find("samples");
        if (samples == root.end() || !samples->is_object()) throw std::runtime_error("samples must be an object");
        if (samples->size() > 64) throw std::runtime_error("at most 64 external samples are supported");

        for (const auto& [rawId, value] : samples->items()) {
            if (!IsAudioAssetId(rawId)) throw std::runtime_error("sample id must match [A-Za-z0-9_]{1,64}");
            if (!value.is_object()) throw std::runtime_error("sample '" + rawId + "' must be an object");
            AudioSampleDefinition sample;
            sample.id = NormalizeAudioId(rawId);
            if (sample.id.starts_with("SOUND_")) {
                throw std::runtime_error("external sample id must not use the reserved SOUND_ prefix");
            }
            const auto pathValue = value.find("path");
            if (pathValue == value.end() || !pathValue->is_string()) {
                throw std::runtime_error("sample '" + rawId + "' path must be a string");
            }
            sample.path = ValidateSamplePath(pathValue->get<std::string>());
            sample.enabled = value.value("enabled", true);
            sample.preload = value.value("preload", true);
            if (!parsed.samples.emplace(sample.id, std::move(sample)).second) {
                throw std::runtime_error("sample ids are case-insensitive and must be unique");
            }
        }
        result.config = std::move(parsed);
    } catch (const std::exception& exception) {
        result.error = std::string("Audio sample config validation failed: ") + exception.what();
    }
    return result;
}

}  // namespace pvzmod
