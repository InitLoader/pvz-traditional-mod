#include "reanim_loader.h"

#include "compiled_reanim.h"

#include <algorithm>
#include <cctype>
#include <string>

namespace pvzmod {

bool IsCompiledReanimPath(const std::filesystem::path& path) {
    std::string name = path.filename().string();
    std::transform(name.begin(), name.end(), name.begin(), [](const unsigned char character) {
        return static_cast<char>(std::tolower(character));
    });
    return name.ends_with(".reanim.compiled");
}

RawReanimLoadResult LoadReanimDefinition(const std::filesystem::path& path) {
    if (IsCompiledReanimPath(path)) return LoadCompiledReanim(path);
    return LoadRawReanim(path);
}

}  // namespace pvzmod
