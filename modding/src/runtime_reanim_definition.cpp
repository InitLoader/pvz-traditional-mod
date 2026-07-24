#include "runtime_reanim_definition.h"

#include <stdexcept>
#include <unordered_map>

namespace pvzmod {

static_assert(sizeof(void*) == 4, "PvZ runtime Reanimation ABI requires a 32-bit build");
static_assert(sizeof(RuntimeReanimatorTransform) == 44, "unexpected ReanimatorTransform ABI");
static_assert(sizeof(RuntimeReanimatorTrack) == 12, "unexpected ReanimatorTrack ABI");
static_assert(sizeof(RuntimeReanimatorDefinition) == 16, "unexpected ReanimatorDefinition ABI");

RuntimeReanimBuildResult BuildRuntimeReanimDefinition(
    const RawReanimDefinition& raw,
    const ExternalAnimationDefinition& config,
    const RuntimeImageResolver& imageResolver) {
    RuntimeReanimBuildResult result;
    try {
        if (!imageResolver) throw std::runtime_error("image resolver is not available");
        if (raw.tracks.empty()) throw std::runtime_error("animation has no tracks");

        auto storage = std::make_shared<RuntimeReanimDefinitionStorage>();
        storage->transformBlocks_.resize(raw.tracks.size());
        storage->tracks_.resize(raw.tracks.size());
        std::unordered_map<std::string, void*> resolvedImages;
        std::unordered_map<std::string, const char*> resolvedTexts;

        for (std::size_t trackIndex = 0; trackIndex < raw.tracks.size(); ++trackIndex) {
            const RawReanimTrack& sourceTrack = raw.tracks[trackIndex];
            storage->strings_.push_back(sourceTrack.name);
            const char* trackName = storage->strings_.back().c_str();
            std::vector<RuntimeReanimatorTransform>& destination = storage->transformBlocks_[trackIndex];
            destination.reserve(sourceTrack.transforms.size());

            for (const RawReanimTransform& source : sourceTrack.transforms) {
                void* image = nullptr;
                const std::string imageSymbol = source.image.value_or(std::string());
                if (!imageSymbol.empty()) {
                    if (const auto cached = resolvedImages.find(imageSymbol); cached != resolvedImages.end()) {
                        image = cached->second;
                    } else {
                        const auto binding = config.images.find(imageSymbol);
                        const std::string_view resourceId = binding == config.images.end()
                            ? std::string_view(imageSymbol)
                            : std::string_view(binding->second);
                        image = imageResolver(resourceId);
                        if (image == nullptr) {
                            if (binding == config.images.end()) {
                                throw std::runtime_error("image symbol '" + imageSymbol +
                                    "' has no texture binding and no reusable original game image");
                            }
                            throw std::runtime_error("texture '" + binding->second +
                                "' could not be loaded for image symbol '" + imageSymbol + "'");
                        }
                        resolvedImages.emplace(imageSymbol, image);
                    }
                }

                const std::string fontSymbol = source.font.value_or(std::string());
                if (!fontSymbol.empty()) {
                    throw std::runtime_error("external font symbols are not supported by plant injection yet");
                }

                const char* text = nullptr;
                const std::string textValue = source.text.value_or(std::string());
                if (!textValue.empty()) {
                    if (const auto cached = resolvedTexts.find(textValue); cached != resolvedTexts.end()) {
                        text = cached->second;
                    } else {
                        storage->strings_.push_back(textValue);
                        text = storage->strings_.back().c_str();
                        resolvedTexts.emplace(textValue, text);
                    }
                }

                destination.push_back(RuntimeReanimatorTransform{
                    source.x.value_or(0.0f),
                    source.y.value_or(0.0f),
                    source.skewX.value_or(0.0f),
                    source.skewY.value_or(0.0f),
                    source.scaleX.value_or(1.0f),
                    source.scaleY.value_or(1.0f),
                    source.frame.value_or(0.0f),
                    source.alpha.value_or(1.0f),
                    image,
                    nullptr,
                    text
                });
            }

            storage->tracks_[trackIndex] = RuntimeReanimatorTrack{
                trackName,
                destination.data(),
                static_cast<int>(destination.size())
            };
        }

        storage->definition_ = RuntimeReanimatorDefinition{
            storage->tracks_.data(),
            static_cast<int>(storage->tracks_.size()),
            raw.fps,
            nullptr
        };
        result.storage = std::move(storage);
    } catch (const std::exception& exception) {
        result.error = std::string("Runtime Reanimation definition build failed: ") + exception.what();
    }
    return result;
}

}  // namespace pvzmod
