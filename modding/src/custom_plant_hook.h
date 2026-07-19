#pragma once

#include <cstdint>
#include <optional>

namespace pvzmod {

[[nodiscard]] std::optional<int> ResolveCustomProjectileDamage(
    void* projectile, std::uintptr_t damageReturnRva, int originalDamage);

}  // namespace pvzmod
