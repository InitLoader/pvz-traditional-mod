#include "external_texture_runtime.h"

#include "external_texture_config.h"
#include "logger.h"

#include <Windows.h>

#include <array>
#include <filesystem>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>
#include <unordered_set>

namespace pvzmod {
namespace {

constexpr std::uintptr_t kGameStringConstructorRva = 0x00004450;
constexpr std::uintptr_t kGameStringDestructorRva = 0x00004420;
constexpr std::uintptr_t kSexyAppGetImageRva = 0x001548E0;

std::mutex g_textureMutex;
std::uint8_t* g_textureModuleBase = nullptr;
std::filesystem::path g_gameRoot;
std::shared_ptr<const ExternalTextureConfig> g_textureConfig;
std::unordered_map<std::string, void*> g_loadedTextures;
std::unordered_set<std::string> g_failedTextures;

std::string Utf8ToAnsi(const std::string& text) {
    if (text.empty()) return {};
    const int wideLength = MultiByteToWideChar(
        CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), nullptr, 0);
    if (wideLength <= 0) return text;
    std::wstring wide(static_cast<std::size_t>(wideLength), L'\0');
    MultiByteToWideChar(
        CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()),
        wide.data(), wideLength);
    const int ansiLength = WideCharToMultiByte(
        CP_ACP, 0, wide.data(), wideLength, nullptr, 0, nullptr, nullptr);
    if (ansiLength <= 0) return text;
    std::string ansi(static_cast<std::size_t>(ansiLength), '\0');
    WideCharToMultiByte(
        CP_ACP, 0, wide.data(), wideLength, ansi.data(), ansiLength, nullptr, nullptr);
    return ansi;
}

bool IsUnderDirectory(const std::filesystem::path& child, const std::filesystem::path& parent) {
    auto childPart = child.begin();
    auto parentPart = parent.begin();
    for (; parentPart != parent.end(); ++parentPart, ++childPart) {
        if (childPart == child.end()) return false;
        if (_wcsicmp(childPart->c_str(), parentPart->c_str()) != 0) return false;
    }
    return true;
}

void* LoadGameImage(void* lawnApp, const std::filesystem::path& absolutePath) {
    const std::u8string utf8Path = absolutePath.u8string();
    const std::string ansiPath = Utf8ToAnsi(std::string(
        reinterpret_cast<const char*>(utf8Path.data()), utf8Path.size()));
    alignas(8) std::array<std::uint8_t, 32> gameString{};
    void* stringObject = gameString.data();
    void* constructor = g_textureModuleBase + kGameStringConstructorRva;
    void* destructor = g_textureModuleBase + kGameStringDestructorRva;
    const char* source = ansiPath.c_str();
    __asm {
        mov ecx, stringObject
        push source
        call constructor
    }
    using GetImageFn = void*(__thiscall*)(void*, void*, bool);
    void* image = reinterpret_cast<GetImageFn>(
        g_textureModuleBase + kSexyAppGetImageRva)(lawnApp, stringObject, true);
    __asm {
        mov ecx, stringObject
        call destructor
    }
    return image;
}

}  // namespace

bool InitializeExternalTextureRuntime(std::uint8_t* moduleBase) {
    if (moduleBase == nullptr) return false;
    const std::filesystem::path root = ModuleDirectory();
    const ExternalTextureConfigLoadResult loaded = LoadExternalTextureConfig(
        root / L"pvzmod" / L"config" / L"resources" / L"textures.jsonc");
    if (!loaded.Ok()) {
        LogWarning(loaded.error + "; external texture lookups are disabled.");
        std::lock_guard lock(g_textureMutex);
        g_textureModuleBase = moduleBase;
        g_gameRoot = root;
        g_textureConfig.reset();
        return true;
    }
    {
        std::lock_guard lock(g_textureMutex);
        g_textureModuleBase = moduleBase;
        g_gameRoot = root;
        g_textureConfig = std::make_shared<ExternalTextureConfig>(*loaded.config);
        g_loadedTextures.clear();
        g_failedTextures.clear();
    }
    LogInfo("Loaded external texture registry with " +
            std::to_string(loaded.config->textures.size()) + " texture id(s).");
    return true;
}

bool ExternalTextureIsRegistered(const std::string_view textureId) {
    std::lock_guard lock(g_textureMutex);
    return g_textureConfig != nullptr && g_textureConfig->Find(textureId) != nullptr;
}

void* ResolveExternalTexture(const std::string_view textureId, void* lawnApp) {
    if (textureId.empty() || lawnApp == nullptr) return nullptr;
    std::lock_guard lock(g_textureMutex);
    if (g_textureModuleBase == nullptr || g_textureConfig == nullptr) return nullptr;
    const std::string id(textureId);
    if (const auto loaded = g_loadedTextures.find(id); loaded != g_loadedTextures.end()) {
        return loaded->second;
    }
    if (g_failedTextures.contains(id)) return nullptr;
    const ExternalTextureDefinition* definition = g_textureConfig->Find(id);
    if (definition == nullptr) {
        g_failedTextures.insert(id);
        LogWarning("External texture id '" + id + "' is not registered.");
        return nullptr;
    }

    std::error_code error;
    const std::filesystem::path imagesRoot = std::filesystem::weakly_canonical(
        g_gameRoot / L"pvzmod" / L"images", error);
    if (error) {
        g_failedTextures.insert(id);
        LogWarning("Cannot resolve pvzmod/images for texture '" + id + "': " + error.message());
        return nullptr;
    }
    const std::filesystem::path absolutePath = std::filesystem::weakly_canonical(
        g_gameRoot / std::filesystem::u8path(definition->path), error);
    if (error || !IsUnderDirectory(absolutePath, imagesRoot)) {
        g_failedTextures.insert(id);
        LogWarning("Rejected external texture '" + id + "': path escaped pvzmod/images.");
        return nullptr;
    }
    if (!std::filesystem::is_regular_file(absolutePath, error) || error) {
        g_failedTextures.insert(id);
        LogWarning("External texture '" + id + "' was not found: " + definition->path);
        return nullptr;
    }
    void* image = LoadGameImage(lawnApp, absolutePath);
    if (image == nullptr) {
        g_failedTextures.insert(id);
        LogWarning("The game image decoder could not load external texture '" + id +
                   "' from " + definition->path + ".");
        return nullptr;
    }
    g_loadedTextures.emplace(id, image);
    LogInfo("Loaded external texture '" + id + "' from " + definition->path + ".");
    return image;
}

}  // namespace pvzmod
