#include "external_texture_config.h"

#include <algorithm>
#include <cctype>
#include <fstream>
#include <stdexcept>
#include <unordered_set>

#include <nlohmann/json.hpp>

namespace pvzmod {
namespace {

using nlohmann::json;

const json& RequiredAlias(
    const json& object, const std::initializer_list<const char*> aliases, const char* logicalName) {
    const json* value = nullptr;
    for (const char* alias : aliases) {
        const auto found = object.find(alias);
        if (found == object.end()) continue;
        if (value != nullptr) {
            throw std::runtime_error(std::string(logicalName) + " must not use multiple alias fields");
        }
        value = &*found;
    }
    if (value == nullptr) throw std::runtime_error(std::string(logicalName) + " is required");
    return *value;
}

std::string RequiredStringAlias(
    const json& object, const std::initializer_list<const char*> aliases, const char* logicalName) {
    const json& value = RequiredAlias(object, aliases, logicalName);
    if (!value.is_string()) throw std::runtime_error(std::string(logicalName) + " must be a string");
    return value.get<std::string>();
}

std::string LowerAscii(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(), [](const unsigned char character) {
        return static_cast<char>(std::tolower(character));
    });
    return value;
}

std::string ValidateTexturePath(const std::string& rawPath) {
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
    if (component == path.end() || LowerAscii(component->string()) != "pvzmod") {
        throw std::runtime_error("path must be inside pvzmod/images");
    }
    ++component;
    if (component == path.end() || LowerAscii(component->string()) != "images") {
        throw std::runtime_error("path must be inside pvzmod/images");
    }
    ++component;
    if (component == path.end()) throw std::runtime_error("path must name an image below pvzmod/images");

    const std::string extension = LowerAscii(path.extension().string());
    static const std::unordered_set<std::string> kExtensions = {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif"};
    if (!kExtensions.contains(extension)) {
        throw std::runtime_error("path extension must be png, jpg, jpeg, bmp, or gif");
    }
    return path.generic_string();
}

}  // namespace

const ExternalTextureDefinition* ExternalTextureConfig::Find(const std::string_view id) const {
    const auto found = textures.find(std::string(id));
    return found == textures.end() ? nullptr : &found->second;
}

bool IsExternalResourceId(const std::string_view id) {
    if (id.empty() || id.size() > 64) return false;
    return std::all_of(id.begin(), id.end(), [](const unsigned char character) {
        return std::isalnum(character) != 0 || character == '_';
    });
}

ExternalTextureConfigLoadResult LoadExternalTextureConfig(const std::filesystem::path& path) {
    ExternalTextureConfigLoadResult result;
    std::ifstream input(path, std::ios::binary);
    if (!input) {
        result.error = "Cannot open external texture config: " + path.string();
        return result;
    }
    try {
        const json root = json::parse(input, nullptr, true, true);
        if (!root.is_object()) throw std::runtime_error("root must be an object");
        ExternalTextureConfig parsed;
        parsed.schemaVersion = root.value("schemaVersion", 1);
        if (parsed.schemaVersion != 1) throw std::runtime_error("unsupported schemaVersion; expected 1");
        const auto textures = root.find("textures");
        if (textures == root.end() || !textures->is_array()) {
            throw std::runtime_error("textures must be an array");
        }
        if (textures->size() > 256) throw std::runtime_error("at most 256 external textures are supported");
        for (const json& item : *textures) {
            if (!item.is_object()) throw std::runtime_error("each textures entry must be an object");
            ExternalTextureDefinition texture;
            texture.id = RequiredStringAlias(item, {"id", "ID"}, "id");
            if (!IsExternalResourceId(texture.id)) {
                throw std::runtime_error("texture id must match [A-Za-z0-9_]+ and contain 1-64 characters");
            }
            texture.path = ValidateTexturePath(
                RequiredStringAlias(item, {"path", "Path", "Patch"}, "path"));
            if (!parsed.textures.emplace(texture.id, std::move(texture)).second) {
                throw std::runtime_error("texture id values must be unique");
            }
        }
        result.config = std::move(parsed);
    } catch (const std::exception& exception) {
        result.error = std::string("External texture config validation failed: ") + exception.what();
    }
    return result;
}

}  // namespace pvzmod
