#pragma once

#include <cstdint>
#include <string_view>

namespace pvzmod {

[[nodiscard]] bool InstallAudioReplacementRuntime(std::uint8_t* moduleBase);
// Main-thread playback entry for plant, zombie, UI and animation-event adapters.
[[nodiscard]] bool PlayConfiguredAudio(void* gameApp, std::string_view sampleId);

}  // namespace pvzmod
