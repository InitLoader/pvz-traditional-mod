#include "reanimation_track_instance_state.h"

#include <cctype>
#include <cstddef>
#include <cstdint>
#include <string_view>

namespace pvzmod {
namespace {

constexpr std::ptrdiff_t kDefinitionOffset = 0x0C;
constexpr std::ptrdiff_t kTrackInstancesOffset = 0x58;
constexpr std::ptrdiff_t kDefinitionTracksOffset = 0x00;
constexpr std::ptrdiff_t kDefinitionTrackCountOffset = 0x04;
constexpr std::size_t kTrackSize = 0x0C;
constexpr std::size_t kTrackInstanceSize = 0x60;
constexpr std::ptrdiff_t kRenderGroupOffset = 0x48;
constexpr std::ptrdiff_t kIgnoreClipRectOffset = 0x5C;
constexpr std::ptrdiff_t kTruncateDisappearingFramesOffset = 0x5D;
constexpr int kMaximumTrackCount = 10000;
constexpr std::size_t kMaximumTrackNameLength = 128;

template <typename T>
T& Field(void* object, const std::ptrdiff_t offset) {
    return *reinterpret_cast<T*>(static_cast<std::uint8_t*>(object) + offset);
}

std::string NormalizeTrackName(const char* name) {
    if (name == nullptr) return {};
    std::size_t length = 0;
    while (length < kMaximumTrackNameLength && name[length] != '\0') ++length;
    if (length == 0 || length == kMaximumTrackNameLength) return {};
    std::string normalized(name, length);
    for (char& character : normalized) {
        character = static_cast<char>(std::tolower(static_cast<unsigned char>(character)));
    }
    return normalized;
}

struct TrackStorageView {
    std::uint8_t* tracks = nullptr;
    std::uint8_t* instances = nullptr;
    int count = 0;

    [[nodiscard]] bool Valid() const {
        return tracks != nullptr && instances != nullptr && count > 0 && count <= kMaximumTrackCount;
    }
};

TrackStorageView GetTrackStorage(void* reanimation) {
    if (reanimation == nullptr) return {};
    void* definition = Field<void*>(reanimation, kDefinitionOffset);
    void* instances = Field<void*>(reanimation, kTrackInstancesOffset);
    if (definition == nullptr || instances == nullptr) return {};
    return {
        Field<std::uint8_t*>(definition, kDefinitionTracksOffset),
        static_cast<std::uint8_t*>(instances),
        Field<int>(definition, kDefinitionTrackCountOffset)
    };
}

}  // namespace

ReanimationTrackInstanceStateMap CaptureReanimationTrackInstanceState(void* reanimation) {
    ReanimationTrackInstanceStateMap result;
    const TrackStorageView storage = GetTrackStorage(reanimation);
    if (!storage.Valid()) return result;
    result.reserve(static_cast<std::size_t>(storage.count));
    for (int index = 0; index < storage.count; ++index) {
        const auto* track = storage.tracks + static_cast<std::size_t>(index) * kTrackSize;
        const char* name = *reinterpret_cast<const char* const*>(track);
        const std::string normalized = NormalizeTrackName(name);
        if (normalized.empty()) continue;
        auto* instance = storage.instances + static_cast<std::size_t>(index) * kTrackInstanceSize;
        result.insert_or_assign(normalized, ReanimationTrackInstanceState{
            Field<int>(instance, kRenderGroupOffset),
            Field<bool>(instance, kIgnoreClipRectOffset),
            Field<bool>(instance, kTruncateDisappearingFramesOffset)
        });
    }
    return result;
}

void RestoreReanimationTrackInstanceState(
    void* reanimation,
    const ReanimationTrackInstanceStateMap& state) {
    if (state.empty()) return;
    const TrackStorageView storage = GetTrackStorage(reanimation);
    if (!storage.Valid()) return;
    for (int index = 0; index < storage.count; ++index) {
        const auto* track = storage.tracks + static_cast<std::size_t>(index) * kTrackSize;
        const char* name = *reinterpret_cast<const char* const*>(track);
        const auto found = state.find(NormalizeTrackName(name));
        if (found == state.end()) continue;
        auto* instance = storage.instances + static_cast<std::size_t>(index) * kTrackInstanceSize;
        Field<int>(instance, kRenderGroupOffset) = found->second.renderGroup;
        Field<bool>(instance, kIgnoreClipRectOffset) = found->second.ignoreClipRect;
        Field<bool>(instance, kTruncateDisappearingFramesOffset) =
            found->second.truncateDisappearingFrames;
    }
}

}  // namespace pvzmod
