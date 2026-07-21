#pragma once

#include "elite_zombie_config.h"

#include <string>
#include <string_view>

namespace pvzmod {

enum class EliteSkillEvent {
    Spawn,
    BeforeUpdate,
    AfterUpdate,
    BeforeAttack,
    BeforeDraw,
    AfterDraw,
    Remove,
};

struct EliteSkillContext {
    void* zombie = nullptr;
    const EliteZombieDefinition* elite = nullptr;
    const EliteSkillBinding* binding = nullptr;
    double healthMultiplier = 1.0;
    double speedMultiplier = 1.0;
    double attackMultiplier = 1.0;
    int attackDamage = 0;
};

using EliteSkillCallback = void (*)(EliteSkillEvent event, EliteSkillContext& context);

void InitializeBuiltinEliteSkills();
[[nodiscard]] bool RegisterEliteSkill(std::string id, EliteSkillCallback callback);
[[nodiscard]] bool IsEliteSkillRegistered(std::string_view id);
[[nodiscard]] bool DispatchEliteSkill(
    std::string_view id, EliteSkillEvent event, EliteSkillContext& context);
[[nodiscard]] bool ValidateEliteSkillBindings(const EliteZombieConfig& config, std::string& error);

}  // namespace pvzmod
