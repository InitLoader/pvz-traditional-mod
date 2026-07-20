#pragma once

#include "raw_reanim.h"

#include <filesystem>

namespace pvzmod {

[[nodiscard]] bool IsCompiledReanimPath(const std::filesystem::path& path);
[[nodiscard]] RawReanimLoadResult LoadReanimDefinition(const std::filesystem::path& path);

}  // namespace pvzmod
