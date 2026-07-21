#pragma once

#include <cstdint>
#include <string_view>

namespace pvzmod {

[[nodiscard]] bool InitializeExternalTextureRuntime(std::uint8_t* moduleBase);
[[nodiscard]] void* ResolveExternalTexture(std::string_view textureId, void* lawnApp);
[[nodiscard]] bool ExternalTextureIsRegistered(std::string_view textureId);

}  // namespace pvzmod
