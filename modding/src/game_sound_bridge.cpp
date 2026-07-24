#include "game_sound_bridge.h"

#include <array>
#include <cstdint>
#include <string>

namespace pvzmod {
namespace {

constexpr std::uintptr_t kGameStringConstructorRva = 0x00004450;
constexpr std::uintptr_t kGameStringDestructorRva = 0x00004420;
constexpr std::size_t kLoadSoundByNameVtableOffset = 0x08;

}  // namespace

int LoadGameSample(
    std::uint8_t* moduleBase, void* soundManager, const std::string_view relativePath) {
    if (moduleBase == nullptr || soundManager == nullptr || relativePath.empty()) return -1;

    alignas(8) std::array<std::uint8_t, 32> gameString{};
    std::string path(relativePath);
    using StringConstructor = void(__thiscall*)(void*, const char*);
    using StringDestructor = void(__thiscall*)(void*);
    using LoadSoundByName = int(__thiscall*)(void*, const void*);

    const auto constructor = reinterpret_cast<StringConstructor>(moduleBase + kGameStringConstructorRva);
    const auto destructor = reinterpret_cast<StringDestructor>(moduleBase + kGameStringDestructorRva);
    constructor(gameString.data(), path.c_str());

    int sampleId = -1;
    void** vtable = *reinterpret_cast<void***>(soundManager);
    if (vtable != nullptr) {
        const auto loadSound = reinterpret_cast<LoadSoundByName>(
            *reinterpret_cast<void**>(reinterpret_cast<std::uint8_t*>(vtable) + kLoadSoundByNameVtableOffset));
        if (loadSound != nullptr) sampleId = loadSound(soundManager, gameString.data());
    }
    destructor(gameString.data());
    return sampleId;
}

}  // namespace pvzmod
