#pragma once

#include "zombie_config.h"

#include <memory>

namespace pvzmod {

// Read-only bridge for isolated zombie extension modules. The owning runtime
// remains in zombie_hook.cpp so armor/attribute hot reload has one authority.
[[nodiscard]] std::shared_ptr<const ZombieConfig> CurrentZombieConfigSnapshot();

}  // namespace pvzmod
