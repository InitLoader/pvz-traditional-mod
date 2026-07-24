#pragma once

#include <string_view>

namespace pvzmod {

// Exact Plants vs. Zombies 1.0.0.1051 body ReanimationType catalog.
[[nodiscard]] int ResolveCarrierReanimationType(std::string_view symbol);
[[nodiscard]] bool IsBodyCarrierReanimationType(int reanimationType);
[[nodiscard]] int ResolvePlantTemplateCarrierReanimationType(int plantType);
[[nodiscard]] int ResolveZombieTypeCarrierReanimationType(int zombieType);

}  // namespace pvzmod
