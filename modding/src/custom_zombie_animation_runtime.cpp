#include "custom_zombie_animation_runtime.h"

#include "external_body_animation_runtime.h"
#include "logger.h"
#include "reanimation_carrier_catalog.h"
#include "zombie_config_runtime_access.h"
#include "zombie_event_bus.h"

#include <cstddef>
#include <cstdint>
#include <memory>
#include <algorithm>
#include <string>
#include <utility>
#include <vector>

namespace pvzmod {
namespace {

constexpr std::ptrdiff_t kZombieLawnAppOffset = 0x00;
constexpr std::ptrdiff_t kZombieTypeOffset = 0x24;
constexpr std::ptrdiff_t kZombieBodyReanimationIdOffset = 0x118;

void OnZombieInitialized(void* zombie) {
    if (zombie == nullptr) return;
    const std::shared_ptr<const ZombieConfig> config = CurrentZombieConfigSnapshot();
    if (!config) return;
    const int zombieType = *reinterpret_cast<const int*>(
        static_cast<const std::uint8_t*>(zombie) + kZombieTypeOffset);
    const ZombieAttributeOverride* override = config->FindZombie(zombieType);
    if (override == nullptr || !override->animationId.has_value()) return;
    static_cast<void>(ApplyExternalBodyAnimation(
        zombie,
        kZombieLawnAppOffset,
        kZombieBodyReanimationIdOffset,
        *override->animationId,
        "zombie type " + std::to_string(zombieType)));
}

}  // namespace

bool InstallCustomZombieAnimationRuntime(std::uint8_t*) {
    const std::shared_ptr<const ZombieConfig> config = CurrentZombieConfigSnapshot();
    if (config) {
        std::vector<std::pair<int, const ZombieAttributeOverride*>> overrides;
        overrides.reserve(config->zombies.size());
        for (const auto& [zombieType, override] : config->zombies) {
            overrides.emplace_back(zombieType, &override);
        }
        std::sort(overrides.begin(), overrides.end(), [](const auto& left, const auto& right) {
            return left.first < right.first;
        });
        for (const auto& [zombieType, override] : overrides) {
            if (!override->animationId.has_value()) continue;
            const int carrier = ResolveZombieTypeCarrierReanimationType(zombieType);
            if (carrier < 0 ||
                !RegisterExternalBodyAnimationForCarrier(*override->animationId, carrier)) {
                LogWarning("Zombie type " + std::to_string(zombieType) + " animationId '" +
                           *override->animationId + "' is inactive; its original body animation will be kept.");
            }
        }
    }
    // Run before elite listeners so tint/image overrides target the final body
    // Definition, without changing the proven native Hook installation order.
    if (!RegisterZombieInitializedListenerFirst(&OnZombieInitialized)) {
        LogError("Custom zombie animation listener registration failed.");
        return false;
    }
    LogInfo("Installed isolated custom-zombie body animation listener.");
    return true;
}

}  // namespace pvzmod
