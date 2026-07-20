#include "reanim_loader.h"

#include <filesystem>
#include <iostream>
#include <string>

int wmain(const int argc, wchar_t** argv) {
    if (argc != 2) {
        std::cerr << "Usage: PvZReanimValidator <file.reanim|file.reanim.compiled>\n";
        return 2;
    }
    const std::filesystem::path path(argv[1]);
    const pvzmod::RawReanimLoadResult loaded = pvzmod::LoadReanimDefinition(path);
    if (!loaded.Ok()) {
        std::cerr << loaded.error << '\n';
        return 1;
    }
    const pvzmod::RawReanimDefinition& definition = *loaded.definition;
    std::cout << "format=" << (pvzmod::IsCompiledReanimPath(path) ? "compiled" : "raw")
              << " fps=" << definition.fps
              << " tracks=" << definition.tracks.size()
              << " frames=" << definition.FrameCount() << '\n';
    for (const pvzmod::RawReanimTrack& track : definition.tracks) {
        std::cout << track.name;
        if (const auto visible = track.VisibleFrameRange(); visible.has_value()) {
            std::cout << " visibleStart=" << visible->first << " visibleCount=" << visible->second;
        } else {
            std::cout << " hidden";
        }
        std::cout << '\n';
    }
    return 0;
}
