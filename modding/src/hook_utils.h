#pragma once

#include <Windows.h>

#include "logger.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <string>

namespace pvzmod {

[[nodiscard]] bool GetSupportedExecutableBase(std::uint8_t*& moduleBase);
[[nodiscard]] bool InitializeHookEngine();
[[nodiscard]] bool CreateAndEnableHook(
    std::uint8_t* moduleBase,
    std::uintptr_t rva,
    void* detour,
    void** original,
    const char* name);

template <std::size_t Size>
[[nodiscard]] bool VerifyHookTarget(
    const std::uint8_t* moduleBase,
    const std::uintptr_t rva,
    const std::array<std::uint8_t, Size>& expected,
    const char* name) {
    if (std::memcmp(moduleBase + rva, expected.data(), expected.size()) != 0) {
        LogError(std::string(name) + " prologue does not match Plants vs. Zombies 1.0.0.1051.");
        return false;
    }
    return true;
}

}  // namespace pvzmod
