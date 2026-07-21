#include "elite_skill_registry.h"

#include "external_texture_config.h"

#include <mutex>
#include <unordered_map>

namespace pvzmod {
namespace {

std::mutex g_skillMutex;
std::unordered_map<std::string, EliteSkillCallback> g_skills;
bool g_builtinsInitialized = false;

void BerserkSkill(const EliteSkillEvent event, EliteSkillContext& context) {
    if (event != EliteSkillEvent::Spawn || context.binding == nullptr) return;
    context.healthMultiplier *= context.binding->Parameter("healthMultiplier", 1.0);
    context.speedMultiplier *= context.binding->Parameter("speedMultiplier", 1.0);
    context.attackMultiplier *= context.binding->Parameter("attackMultiplier", 1.0);
}

}  // namespace

void InitializeBuiltinEliteSkills() {
    std::lock_guard lock(g_skillMutex);
    if (g_builtinsInitialized) return;
    g_skills.emplace("BERSERK", &BerserkSkill);
    g_builtinsInitialized = true;
}

bool RegisterEliteSkill(std::string id, const EliteSkillCallback callback) {
    if (!IsExternalResourceId(id) || callback == nullptr) return false;
    std::lock_guard lock(g_skillMutex);
    return g_skills.emplace(std::move(id), callback).second;
}

bool IsEliteSkillRegistered(const std::string_view id) {
    std::lock_guard lock(g_skillMutex);
    return g_skills.contains(std::string(id));
}

bool DispatchEliteSkill(
    const std::string_view id, const EliteSkillEvent event, EliteSkillContext& context) {
    EliteSkillCallback callback = nullptr;
    {
        std::lock_guard lock(g_skillMutex);
        const auto found = g_skills.find(std::string(id));
        if (found == g_skills.end()) return false;
        callback = found->second;
    }
    callback(event, context);
    return true;
}

bool ValidateEliteSkillBindings(const EliteZombieConfig& config, std::string& error) {
    for (const EliteZombieDefinition& elite : config.elites) {
        for (const EliteSkillBinding& skill : elite.skills) {
            if (!IsEliteSkillRegistered(skill.id)) {
                error = "Elite '" + elite.id + "' references unknown skill '" + skill.id + "'.";
                return false;
            }
        }
    }
    return true;
}

}  // namespace pvzmod
