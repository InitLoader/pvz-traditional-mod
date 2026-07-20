#pragma once

#include "raw_reanim.h"

#include <filesystem>

namespace pvzmod {

// Decodes the original 32-bit Windows .reanim.compiled cache format into the
// same normalized definition used by the Raw XML loader.
[[nodiscard]] RawReanimLoadResult LoadCompiledReanim(const std::filesystem::path& path);

}  // namespace pvzmod
