#pragma once

namespace pvzmod {

using ZombieInitializedListener = void (*)(void* zombie);
using ZombieAttackDamageModifier = int (*)(void* zombie, int currentDamage);

[[nodiscard]] bool RegisterZombieInitializedListener(ZombieInitializedListener listener);
[[nodiscard]] bool RegisterZombieInitializedListenerFirst(ZombieInitializedListener listener);
[[nodiscard]] bool RegisterZombieAttackDamageModifier(ZombieAttackDamageModifier modifier);
void DispatchZombieInitialized(void* zombie);
[[nodiscard]] int DispatchZombieAttackDamageModifiers(void* zombie, int baseDamage);

}  // namespace pvzmod
