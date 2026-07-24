#pragma once

#include "audio_sample_config.h"

#include <cstdint>
#include <string_view>
#include <unordered_map>

namespace pvzmod {

class AudioAssetRegistry {
public:
    void Reset(AudioSampleConfig config, std::filesystem::path gameDirectory);
    [[nodiscard]] bool LoadAll(std::uint8_t* moduleBase, void* soundManager);
    [[nodiscard]] int FindEngineSampleId(std::string_view id) const;
    [[nodiscard]] std::size_t DefinitionCount() const { return config_.samples.size(); }
    [[nodiscard]] std::size_t LoadedCount() const { return engineIds_.size(); }

private:
    AudioSampleConfig config_;
    std::filesystem::path gameDirectory_;
    std::unordered_map<std::string, int> engineIds_;
};

}  // namespace pvzmod
