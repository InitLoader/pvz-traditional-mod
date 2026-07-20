#include "zombie_event_bus.h"

#include <algorithm>
#include <limits>
#include <mutex>
#include <vector>

namespace pvzmod {
namespace {

std::mutex g_eventMutex;
std::vector<ZombieInitializedListener> g_initializedListeners;
std::vector<ZombieAttackDamageModifier> g_attackModifiers;

template <typename T>
bool RegisterUnique(std::vector<T>& listeners, T listener) {
    if (listener == nullptr || std::find(listeners.begin(), listeners.end(), listener) != listeners.end()) {
        return false;
    }
    listeners.push_back(listener);
    return true;
}

}  // namespace

bool RegisterZombieInitializedListener(const ZombieInitializedListener listener) {
    std::lock_guard lock(g_eventMutex);
    return RegisterUnique(g_initializedListeners, listener);
}

bool RegisterZombieAttackDamageModifier(const ZombieAttackDamageModifier modifier) {
    std::lock_guard lock(g_eventMutex);
    return RegisterUnique(g_attackModifiers, modifier);
}

void DispatchZombieInitialized(void* zombie) {
    std::vector<ZombieInitializedListener> listeners;
    {
        std::lock_guard lock(g_eventMutex);
        listeners = g_initializedListeners;
    }
    for (const ZombieInitializedListener listener : listeners) listener(zombie);
}

int DispatchZombieAttackDamageModifiers(void* zombie, int baseDamage) {
    std::vector<ZombieAttackDamageModifier> modifiers;
    {
        std::lock_guard lock(g_eventMutex);
        modifiers = g_attackModifiers;
    }
    int damage = baseDamage;
    for (const ZombieAttackDamageModifier modifier : modifiers) {
        damage = std::clamp(modifier(zombie, damage), 0, std::numeric_limits<int>::max());
    }
    return damage;
}

}  // namespace pvzmod
