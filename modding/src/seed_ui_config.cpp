#include "seed_ui_config.h"

#include <fstream>
#include <stdexcept>

#include <nlohmann/json.hpp>

namespace pvzmod {

SeedUiConfigLoadResult LoadSeedUiConfig(const std::filesystem::path& path) {
    SeedUiConfigLoadResult result;
    std::ifstream input(path, std::ios::binary);
    if (!input) {
        result.error = "Cannot open seed chooser UI config: " + path.string();
        return result;
    }
    try {
        const nlohmann::json root = nlohmann::json::parse(input, nullptr, true, true);
        if (!root.is_object()) throw std::runtime_error("root must be an object");
        SeedUiConfig parsed;
        parsed.schemaVersion = root.value("schemaVersion", 1);
        if (parsed.schemaVersion != 1) throw std::runtime_error("unsupported schemaVersion; expected 1");
        if (const auto it = root.find("slotCount"); it != root.end()) {
            if (!it->is_number_integer() && !it->is_number_unsigned()) {
                throw std::runtime_error("slotCount must be an integer");
            }
            const long long value = it->get<long long>();
            if (value < 6 || value > 10) throw std::runtime_error("slotCount must be between 6 and 10");
            parsed.slotCount = static_cast<int>(value);
        }
        result.config = std::move(parsed);
    } catch (const std::exception& exception) {
        result.error = std::string("Seed chooser UI config validation failed: ") + exception.what();
    }
    return result;
}

}  // namespace pvzmod
