#include "compiled_reanim.h"

#include <algorithm>
#include <array>
#include <bit>
#include <cctype>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <fstream>
#include <limits>
#include <span>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

#include <zlib.h>

namespace pvzmod {
namespace {

constexpr std::uint32_t kCompiledCookie = 0xDEADFED4U;
constexpr std::uint32_t kReanimSchemaHash = 0xB393B4C0U;
constexpr std::int32_t kDefinitionSize = 16;
constexpr std::int32_t kTrackSize = 12;
constexpr std::int32_t kTransformSize = 44;
constexpr float kMissingField = -10000.0f;
constexpr std::uintmax_t kMaxCompressedSize = 16U * 1024U * 1024U;
constexpr std::size_t kMaxDecompressedSize = 64U * 1024U * 1024U;
constexpr std::size_t kMaxTracks = 512;
constexpr std::size_t kMaxFramesPerTrack = 20000;
constexpr std::size_t kMaxTotalTransforms = 500000;

class BinaryReader {
public:
    explicit BinaryReader(const std::span<const std::uint8_t> bytes) : bytes_(bytes) {}

    [[nodiscard]] std::size_t Remaining() const { return bytes_.size() - position_; }

    std::span<const std::uint8_t> ReadBytes(const std::size_t count, const char* fieldName) {
        if (count > Remaining()) {
            throw std::runtime_error(std::string("unexpected end while reading ") + fieldName);
        }
        const auto result = bytes_.subspan(position_, count);
        position_ += count;
        return result;
    }

    std::uint32_t ReadU32(const char* fieldName) {
        const auto bytes = ReadBytes(sizeof(std::uint32_t), fieldName);
        return static_cast<std::uint32_t>(bytes[0]) |
               (static_cast<std::uint32_t>(bytes[1]) << 8U) |
               (static_cast<std::uint32_t>(bytes[2]) << 16U) |
               (static_cast<std::uint32_t>(bytes[3]) << 24U);
    }

    std::int32_t ReadI32(const char* fieldName) {
        return std::bit_cast<std::int32_t>(ReadU32(fieldName));
    }

    float ReadFloat(const char* fieldName) {
        return std::bit_cast<float>(ReadU32(fieldName));
    }

    std::string ReadString(const char* fieldName, const std::size_t maximum) {
        const std::int32_t signedLength = ReadI32("string length");
        if (signedLength < 0 || static_cast<std::size_t>(signedLength) > maximum) {
            throw std::runtime_error(std::string(fieldName) + " length is outside the safe range");
        }
        const auto bytes = ReadBytes(static_cast<std::size_t>(signedLength), fieldName);
        std::string value(reinterpret_cast<const char*>(bytes.data()), bytes.size());
        if (value.find('\0') != std::string::npos) {
            throw std::runtime_error(std::string(fieldName) + " contains a null byte");
        }
        return value;
    }

private:
    std::span<const std::uint8_t> bytes_;
    std::size_t position_ = 0;
};

std::vector<std::uint8_t> ReadCompiledPayload(const std::filesystem::path& path) {
    std::error_code error;
    const std::uintmax_t fileSize = std::filesystem::file_size(path, error);
    if (error) throw std::runtime_error("cannot read file size: " + error.message());
    if (fileSize < 9 || fileSize > kMaxCompressedSize) {
        throw std::runtime_error("compiled file size must be in [9, 16 MiB]");
    }
    std::ifstream input(path, std::ios::binary);
    if (!input) throw std::runtime_error("cannot open file");
    std::vector<std::uint8_t> file(static_cast<std::size_t>(fileSize));
    input.read(reinterpret_cast<char*>(file.data()), static_cast<std::streamsize>(file.size()));
    if (!input) throw std::runtime_error("cannot read the complete file");

    BinaryReader header(file);
    if (header.ReadU32("compiled cookie") != kCompiledCookie) {
        throw std::runtime_error("compiled cookie is not 0xDEADFED4");
    }
    const std::uint32_t declaredSize = header.ReadU32("uncompressed size");
    if (declaredSize == 0 || declaredSize > kMaxDecompressedSize) {
        throw std::runtime_error("declared uncompressed size must be in [1, 64 MiB]");
    }

    std::vector<std::uint8_t> payload(declaredSize);
    uLongf outputSize = static_cast<uLongf>(payload.size());
    uLong inputSize = static_cast<uLong>(header.Remaining());
    const auto compressed = header.ReadBytes(header.Remaining(), "zlib payload");
    const int status = uncompress2(
        reinterpret_cast<Bytef*>(payload.data()), &outputSize,
        reinterpret_cast<const Bytef*>(compressed.data()), &inputSize);
    if (status != Z_OK) {
        throw std::runtime_error("zlib decompression failed with status " + std::to_string(status));
    }
    if (outputSize != declaredSize || inputSize != compressed.size()) {
        throw std::runtime_error("zlib payload length does not match the compiled header");
    }
    return payload;
}

std::optional<float> ParseOptionalFloat(const float value, const char* fieldName) {
    if (!std::isfinite(value)) {
        throw std::runtime_error(std::string(fieldName) + " must be finite");
    }
    if (value <= kMissingField) return std::nullopt;
    if (value < -100000.0f || value > 100000.0f) {
        throw std::runtime_error(std::string(fieldName) + " is outside the safe range");
    }
    return value;
}

void ResolveInheritance(RawReanimTrack& track) {
    RawReanimTransform inherited;
    inherited.x = 0.0f;
    inherited.y = 0.0f;
    inherited.skewX = 0.0f;
    inherited.skewY = 0.0f;
    inherited.scaleX = 1.0f;
    inherited.scaleY = 1.0f;
    inherited.frame = 0.0f;
    inherited.alpha = 1.0f;
    for (RawReanimTransform& transform : track.transforms) {
#define PVZMOD_INHERIT_COMPILED_FIELD(field) \
        if (transform.field.has_value()) inherited.field = transform.field; \
        else transform.field = inherited.field
        PVZMOD_INHERIT_COMPILED_FIELD(x);
        PVZMOD_INHERIT_COMPILED_FIELD(y);
        PVZMOD_INHERIT_COMPILED_FIELD(skewX);
        PVZMOD_INHERIT_COMPILED_FIELD(skewY);
        PVZMOD_INHERIT_COMPILED_FIELD(scaleX);
        PVZMOD_INHERIT_COMPILED_FIELD(scaleY);
        PVZMOD_INHERIT_COMPILED_FIELD(frame);
        PVZMOD_INHERIT_COMPILED_FIELD(alpha);
        PVZMOD_INHERIT_COMPILED_FIELD(image);
        PVZMOD_INHERIT_COMPILED_FIELD(font);
        PVZMOD_INHERIT_COMPILED_FIELD(text);
#undef PVZMOD_INHERIT_COMPILED_FIELD
    }
}

RawReanimDefinition DecodePayload(const std::span<const std::uint8_t> payload) {
    BinaryReader reader(payload);
    if (reader.ReadU32("schema hash") != kReanimSchemaHash) {
        throw std::runtime_error("unsupported reanimation schema hash");
    }

    const auto definition = reader.ReadBytes(kDefinitionSize, "ReanimatorDefinition");
    BinaryReader definitionReader(definition);
    definitionReader.ReadU32("track pointer");
    const std::int32_t signedTrackCount = definitionReader.ReadI32("track count");
    const float fps = definitionReader.ReadFloat("fps");
    definitionReader.ReadU32("atlas pointer");
    if (signedTrackCount <= 0 || static_cast<std::size_t>(signedTrackCount) > kMaxTracks) {
        throw std::runtime_error("track count must be in [1, 512]");
    }
    if (!std::isfinite(fps) || fps <= 0.0f || fps > 120.0f) {
        throw std::runtime_error("fps must be finite and in (0, 120]");
    }
    if (reader.ReadI32("ReanimatorTrack size") != kTrackSize) {
        throw std::runtime_error("ReanimatorTrack size is not 12 bytes");
    }

    struct TrackHeader {
        std::int32_t transformCount = 0;
    };
    std::vector<TrackHeader> trackHeaders;
    trackHeaders.reserve(static_cast<std::size_t>(signedTrackCount));
    std::size_t totalTransforms = 0;
    for (std::int32_t index = 0; index < signedTrackCount; ++index) {
        reader.ReadU32("track name pointer");
        reader.ReadU32("transform pointer");
        const std::int32_t transformCount = reader.ReadI32("transform count");
        if (transformCount <= 0 || static_cast<std::size_t>(transformCount) > kMaxFramesPerTrack) {
            throw std::runtime_error("transform count must be in [1, 20000]");
        }
        totalTransforms += static_cast<std::size_t>(transformCount);
        if (totalTransforms > kMaxTotalTransforms) {
            throw std::runtime_error("compiled animation has too many total transforms");
        }
        trackHeaders.push_back({transformCount});
    }

    RawReanimDefinition result;
    result.fps = fps;
    result.tracks.reserve(trackHeaders.size());
    std::size_t commonFrameCount = 0;
    for (const TrackHeader& header : trackHeaders) {
        RawReanimTrack track;
        track.name = reader.ReadString("track name", 128);
        if (track.name.empty()) throw std::runtime_error("track name must not be empty");

        if (reader.ReadI32("ReanimatorTransform size") != kTransformSize) {
            throw std::runtime_error("ReanimatorTransform size is not 44 bytes");
        }
        const std::size_t transformCount = static_cast<std::size_t>(header.transformCount);
        if (commonFrameCount == 0) commonFrameCount = transformCount;
        if (transformCount != commonFrameCount) {
            throw std::runtime_error("all tracks must contain the same number of transforms");
        }
        const auto rawTransforms = reader.ReadBytes(transformCount * kTransformSize, "transforms");
        BinaryReader transformReader(rawTransforms);
        track.transforms.reserve(transformCount);
        for (std::size_t index = 0; index < transformCount; ++index) {
            RawReanimTransform transform;
            transform.x = ParseOptionalFloat(transformReader.ReadFloat("x"), "x");
            transform.y = ParseOptionalFloat(transformReader.ReadFloat("y"), "y");
            transform.skewX = ParseOptionalFloat(transformReader.ReadFloat("kx"), "kx");
            transform.skewY = ParseOptionalFloat(transformReader.ReadFloat("ky"), "ky");
            transform.scaleX = ParseOptionalFloat(transformReader.ReadFloat("sx"), "sx");
            transform.scaleY = ParseOptionalFloat(transformReader.ReadFloat("sy"), "sy");
            transform.frame = ParseOptionalFloat(transformReader.ReadFloat("f"), "f");
            transform.alpha = ParseOptionalFloat(transformReader.ReadFloat("a"), "a");
            if (transform.alpha.has_value() && (*transform.alpha < 0.0f || *transform.alpha > 1.0f)) {
                throw std::runtime_error("alpha must be in [0, 1]");
            }
            transformReader.ReadU32("image pointer");
            transformReader.ReadU32("font pointer");
            transformReader.ReadU32("text pointer");
            track.transforms.push_back(std::move(transform));
        }
        for (RawReanimTransform& transform : track.transforms) {
            std::string image = reader.ReadString("image name", 128);
            std::string font = reader.ReadString("font name", 128);
            std::string text = reader.ReadString("text", 1024);
            if (!image.empty()) transform.image = std::move(image);
            if (!font.empty()) transform.font = std::move(font);
            if (!text.empty()) transform.text = std::move(text);
        }
        ResolveInheritance(track);
        result.tracks.push_back(std::move(track));
    }
    if (reader.Remaining() != 0) {
        throw std::runtime_error("compiled payload contains trailing bytes");
    }
    return result;
}

}  // namespace

RawReanimLoadResult LoadCompiledReanim(const std::filesystem::path& path) {
    RawReanimLoadResult result;
    try {
        const std::vector<std::uint8_t> payload = ReadCompiledPayload(path);
        result.definition = DecodePayload(payload);
    } catch (const std::exception& exception) {
        result.error = std::string("Compiled reanim validation failed for ") + path.string() + ": " + exception.what();
    }
    return result;
}

}  // namespace pvzmod
