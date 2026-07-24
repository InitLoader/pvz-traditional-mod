#pragma once

#include <cstddef>
#include <cstdint>
#include <optional>
#include <string_view>

namespace pvzmod {

inline constexpr std::size_t kOriginalSoundCount = 167;

[[nodiscard]] std::optional<std::uintptr_t> FindOriginalSoundGlobalRva(std::string_view symbol);
[[nodiscard]] bool IsKnownOriginalSound(std::string_view symbol);

}  // namespace pvzmod
