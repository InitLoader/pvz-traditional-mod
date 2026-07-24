#pragma once

#include <cstdint>
#include <string_view>

namespace pvzmod {

inline constexpr std::uintptr_t kDSoundManagerGetSoundInstanceRva = 0x001C7650;

[[nodiscard]] int LoadGameSample(
    std::uint8_t* moduleBase, void* soundManager, std::string_view relativePath);

}  // namespace pvzmod
