#include <Windows.h>
#include <bcrypt.h>

#include <algorithm>
#include <array>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <limits>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

namespace {

constexpr wchar_t kExpectedSha256[] = L"678016E417CD55F1F19D7EABB6F2B7E6B87146BE107DB1179A61985078004F7A";
constexpr DWORD kExpectedTimestamp = 0x49ECF563;
constexpr DWORD kExpectedEntryPointRva = 0x0021EBF2;
constexpr DWORD kExpectedImageBase = 0x00400000;
constexpr DWORD kLoadLibraryAIatVa = 0x006520A4;
constexpr char kSectionName[] = ".pvzm";
constexpr char kDllName[] = "pvzmod.dll";
constexpr std::size_t kDllNameOffset = 0x40;
constexpr std::size_t kMarkerOffset = 0x80;
constexpr char kMarker[] = "PVZMOD-WAVE-1";

DWORD AlignUp(const DWORD value, const DWORD alignment) {
    if (alignment == 0) {
        throw std::runtime_error("PE alignment cannot be zero");
    }
    return (value + alignment - 1) / alignment * alignment;
}

std::vector<std::uint8_t> ReadAllBytes(const std::filesystem::path& path) {
    std::ifstream input(path, std::ios::binary | std::ios::ate);
    if (!input) {
        throw std::runtime_error("cannot open input file");
    }
    const std::streamsize size = input.tellg();
    if (size <= 0) {
        throw std::runtime_error("input file is empty");
    }
    input.seekg(0);
    std::vector<std::uint8_t> bytes(static_cast<std::size_t>(size));
    if (!input.read(reinterpret_cast<char*>(bytes.data()), size)) {
        throw std::runtime_error("cannot read input file");
    }
    return bytes;
}

std::wstring Sha256(const std::vector<std::uint8_t>& bytes) {
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    DWORD objectSize = 0;
    DWORD hashSize = 0;
    DWORD copied = 0;

    auto Check = [](const NTSTATUS status, const char* operation) {
        if (status < 0) {
            throw std::runtime_error(std::string(operation) + " failed");
        }
    };

    Check(BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0), "BCryptOpenAlgorithmProvider");
    try {
        Check(BCryptGetProperty(
                  algorithm,
                  BCRYPT_OBJECT_LENGTH,
                  reinterpret_cast<PUCHAR>(&objectSize),
                  sizeof(objectSize),
                  &copied,
                  0),
              "BCryptGetProperty(object length)");
        Check(BCryptGetProperty(
                  algorithm,
                  BCRYPT_HASH_LENGTH,
                  reinterpret_cast<PUCHAR>(&hashSize),
                  sizeof(hashSize),
                  &copied,
                  0),
              "BCryptGetProperty(hash length)");

        std::vector<std::uint8_t> object(objectSize);
        std::vector<std::uint8_t> digest(hashSize);
        Check(BCryptCreateHash(algorithm, &hash, object.data(), objectSize, nullptr, 0, 0), "BCryptCreateHash");
        Check(BCryptHashData(
                  hash,
                  const_cast<PUCHAR>(bytes.data()),
                  static_cast<ULONG>(bytes.size()),
                  0),
              "BCryptHashData");
        Check(BCryptFinishHash(hash, digest.data(), hashSize, 0), "BCryptFinishHash");

        std::wostringstream output;
        output << std::uppercase << std::hex << std::setfill(L'0');
        for (const std::uint8_t value : digest) {
            output << std::setw(2) << static_cast<unsigned int>(value);
        }

        BCryptDestroyHash(hash);
        BCryptCloseAlgorithmProvider(algorithm, 0);
        return output.str();
    } catch (...) {
        if (hash != nullptr) {
            BCryptDestroyHash(hash);
        }
        BCryptCloseAlgorithmProvider(algorithm, 0);
        throw;
    }
}

struct ParsedPe {
    std::size_t ntOffset = 0;
    std::size_t sectionTableOffset = 0;
    WORD numberOfSections = 0;
    IMAGE_OPTIONAL_HEADER32 optional{};
};

ParsedPe ParsePe(const std::vector<std::uint8_t>& bytes) {
    if (bytes.size() < sizeof(IMAGE_DOS_HEADER)) {
        throw std::runtime_error("file is too small for a DOS header");
    }
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(bytes.data());
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew <= 0) {
        throw std::runtime_error("invalid DOS header");
    }

    const std::size_t ntOffset = static_cast<std::size_t>(dos->e_lfanew);
    if (ntOffset + sizeof(IMAGE_NT_HEADERS32) > bytes.size()) {
        throw std::runtime_error("invalid NT header offset");
    }
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(bytes.data() + ntOffset);
    if (nt->Signature != IMAGE_NT_SIGNATURE ||
        nt->FileHeader.Machine != IMAGE_FILE_MACHINE_I386 ||
        nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC) {
        throw std::runtime_error("target is not a 32-bit x86 PE file");
    }

    ParsedPe parsed;
    parsed.ntOffset = ntOffset;
    parsed.numberOfSections = nt->FileHeader.NumberOfSections;
    parsed.optional = nt->OptionalHeader;
    parsed.sectionTableOffset = ntOffset + sizeof(DWORD) + sizeof(IMAGE_FILE_HEADER) + nt->FileHeader.SizeOfOptionalHeader;

    const std::size_t sectionEnd = parsed.sectionTableOffset +
        static_cast<std::size_t>(parsed.numberOfSections) * sizeof(IMAGE_SECTION_HEADER);
    if (sectionEnd > bytes.size()) {
        throw std::runtime_error("invalid section table");
    }
    return parsed;
}

bool HasModSection(const std::vector<std::uint8_t>& bytes, const ParsedPe& pe) {
    const auto* sections = reinterpret_cast<const IMAGE_SECTION_HEADER*>(bytes.data() + pe.sectionTableOffset);
    for (WORD index = 0; index < pe.numberOfSections; ++index) {
        char name[IMAGE_SIZEOF_SHORT_NAME + 1]{};
        std::memcpy(name, sections[index].Name, IMAGE_SIZEOF_SHORT_NAME);
        if (std::string(name) == kSectionName) {
            return true;
        }
    }
    return false;
}

void AppendU32(std::vector<std::uint8_t>& target, const std::uint32_t value) {
    target.push_back(static_cast<std::uint8_t>(value));
    target.push_back(static_cast<std::uint8_t>(value >> 8));
    target.push_back(static_cast<std::uint8_t>(value >> 16));
    target.push_back(static_cast<std::uint8_t>(value >> 24));
}

std::vector<std::uint8_t> BuildLoaderStub(const DWORD sectionVa, const DWORD originalEntryVa) {
    std::vector<std::uint8_t> stub;
    stub.push_back(0x9C);  // pushfd
    stub.push_back(0x60);  // pushad
    stub.push_back(0x68);  // push dll path
    AppendU32(stub, sectionVa + static_cast<DWORD>(kDllNameOffset));
    stub.push_back(0xFF);  // call dword ptr [LoadLibraryA IAT]
    stub.push_back(0x15);
    AppendU32(stub, kLoadLibraryAIatVa);
    stub.push_back(0x61);  // popad
    stub.push_back(0x9D);  // popfd
    stub.push_back(0xE9);  // jump to original entry point
    const DWORD nextInstruction = sectionVa + static_cast<DWORD>(stub.size()) + sizeof(DWORD);
    AppendU32(stub, originalEntryVa - nextInstruction);

    if (stub.size() > kDllNameOffset) {
        throw std::runtime_error("loader stub overlaps DLL name");
    }
    stub.resize(kDllNameOffset, 0x90);
    stub.insert(stub.end(), std::begin(kDllName), std::end(kDllName));
    if (stub.size() > kMarkerOffset) {
        throw std::runtime_error("DLL name overlaps patch marker");
    }
    stub.resize(kMarkerOffset, 0);
    stub.insert(stub.end(), std::begin(kMarker), std::end(kMarker));
    return stub;
}

std::vector<std::uint8_t> PatchExecutable(std::vector<std::uint8_t> bytes) {
    ParsedPe pe = ParsePe(bytes);
    if (pe.optional.ImageBase != kExpectedImageBase ||
        pe.optional.AddressOfEntryPoint != kExpectedEntryPointRva ||
        pe.optional.FileAlignment == 0 ||
        pe.optional.SectionAlignment == 0) {
        throw std::runtime_error("PE layout does not match the supported 1.0.0.1051 executable");
    }

    const auto* oldSections = reinterpret_cast<const IMAGE_SECTION_HEADER*>(bytes.data() + pe.sectionTableOffset);
    DWORD highestVirtualEnd = 0;
    for (WORD index = 0; index < pe.numberOfSections; ++index) {
        const DWORD extent = std::max(oldSections[index].Misc.VirtualSize, oldSections[index].SizeOfRawData);
        highestVirtualEnd = std::max(highestVirtualEnd, oldSections[index].VirtualAddress + extent);
    }

    const DWORD newSectionRva = AlignUp(highestVirtualEnd, pe.optional.SectionAlignment);
    const DWORD newRawOffset = AlignUp(static_cast<DWORD>(bytes.size()), pe.optional.FileAlignment);
    const DWORD newRawSize = AlignUp(0x1000, pe.optional.FileAlignment);
    const std::size_t newHeaderOffset = pe.sectionTableOffset +
        static_cast<std::size_t>(pe.numberOfSections) * sizeof(IMAGE_SECTION_HEADER);
    DWORD firstSectionRawOffset = std::numeric_limits<DWORD>::max();
    for (WORD index = 0; index < pe.numberOfSections; ++index) {
        if (oldSections[index].PointerToRawData != 0) {
            firstSectionRawOffset = std::min(firstSectionRawOffset, oldSections[index].PointerToRawData);
        }
    }
    const std::size_t headerLimit = std::min<std::size_t>(pe.optional.SizeOfHeaders, firstSectionRawOffset);
    if (newHeaderOffset + sizeof(IMAGE_SECTION_HEADER) > headerLimit) {
        throw std::runtime_error("PE header has no room for the .pvzm section");
    }

    const DWORD sectionVa = pe.optional.ImageBase + newSectionRva;
    const DWORD originalEntryVa = pe.optional.ImageBase + pe.optional.AddressOfEntryPoint;
    const std::vector<std::uint8_t> loader = BuildLoaderStub(sectionVa, originalEntryVa);
    if (loader.size() > newRawSize) {
        throw std::runtime_error("loader does not fit in the new section");
    }

    bytes.resize(static_cast<std::size_t>(newRawOffset) + newRawSize, 0);
    std::copy(loader.begin(), loader.end(), bytes.begin() + newRawOffset);

    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS32*>(bytes.data() + pe.ntOffset);
    auto* sections = reinterpret_cast<IMAGE_SECTION_HEADER*>(bytes.data() + pe.sectionTableOffset);
    IMAGE_SECTION_HEADER& added = sections[pe.numberOfSections];
    std::memset(&added, 0, sizeof(added));
    std::memcpy(added.Name, kSectionName, std::strlen(kSectionName));
    added.Misc.VirtualSize = static_cast<DWORD>(loader.size());
    added.VirtualAddress = newSectionRva;
    added.SizeOfRawData = newRawSize;
    added.PointerToRawData = newRawOffset;
    added.Characteristics = IMAGE_SCN_CNT_CODE | IMAGE_SCN_MEM_EXECUTE | IMAGE_SCN_MEM_READ;

    nt->FileHeader.NumberOfSections = pe.numberOfSections + 1;
    nt->OptionalHeader.AddressOfEntryPoint = newSectionRva;
    nt->OptionalHeader.SizeOfImage = AlignUp(newSectionRva + newRawSize, pe.optional.SectionAlignment);
    nt->OptionalHeader.CheckSum = 0;
    nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_SECURITY].VirtualAddress = 0;
    nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_SECURITY].Size = 0;
    return bytes;
}

void WriteAtomically(const std::filesystem::path& outputPath, const std::vector<std::uint8_t>& bytes) {
    std::error_code error;
    if (!outputPath.parent_path().empty()) {
        std::filesystem::create_directories(outputPath.parent_path(), error);
        if (error) {
            throw std::runtime_error("cannot create output directory");
        }
    }

    const std::filesystem::path temporary = outputPath.wstring() + L".pvzmod.tmp";
    {
        std::ofstream output(temporary, std::ios::binary | std::ios::trunc);
        if (!output || !output.write(reinterpret_cast<const char*>(bytes.data()), bytes.size())) {
            throw std::runtime_error("cannot write temporary patched executable");
        }
    }

    if (!MoveFileExW(
            temporary.c_str(),
            outputPath.c_str(),
            MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        std::filesystem::remove(temporary, error);
        throw std::runtime_error("cannot replace output executable; Windows error " + std::to_string(GetLastError()));
    }
}

void PrintUsage() {
    std::wcout << L"Usage:\n"
               << L"  PvZModPatcher.exe --in-place <PlantsVsZombies.exe>\n"
               << L"  PvZModPatcher.exe <input.exe> <output.exe>\n";
}

}  // namespace

int wmain(const int argc, wchar_t** argv) {
    try {
        bool inPlace = false;
        std::filesystem::path inputPath;
        std::filesystem::path outputPath;

        if (argc == 3 && std::wstring(argv[1]) == L"--in-place") {
            inPlace = true;
            inputPath = argv[2];
            outputPath = inputPath;
        } else if (argc == 3) {
            inputPath = argv[1];
            outputPath = argv[2];
        } else {
            PrintUsage();
            return 2;
        }

        std::vector<std::uint8_t> original = ReadAllBytes(inputPath);
        const ParsedPe initialPe = ParsePe(original);
        if (HasModSection(original, initialPe)) {
            std::wcout << L"Already patched: " << inputPath << L"\n";
            return 0;
        }

        const std::wstring actualHash = Sha256(original);
        if (actualHash != kExpectedSha256) {
            std::wcerr << L"Refusing to patch an unsupported executable.\nExpected SHA-256: "
                       << kExpectedSha256 << L"\nActual SHA-256:   " << actualHash << L"\n";
            return 3;
        }
        if (initialPe.optional.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC) {
            throw std::runtime_error("expected a PE32 optional header");
        }
        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(original.data() + initialPe.ntOffset);
        if (nt->FileHeader.TimeDateStamp != kExpectedTimestamp) {
            throw std::runtime_error("PE timestamp does not match 1.0.0.1051");
        }

        if (inPlace) {
            const std::filesystem::path backupPath = inputPath.parent_path() / L"PlantsVsZombies.original.exe";
            if (std::filesystem::exists(backupPath)) {
                if (Sha256(ReadAllBytes(backupPath)) != kExpectedSha256) {
                    throw std::runtime_error("existing PlantsVsZombies.original.exe is not the expected clean backup");
                }
            } else if (!CopyFileW(inputPath.c_str(), backupPath.c_str(), TRUE)) {
                throw std::runtime_error("cannot create PlantsVsZombies.original.exe backup");
            }
            std::wcout << L"Clean backup: " << backupPath << L"\n";
        }

        std::vector<std::uint8_t> patched = PatchExecutable(std::move(original));
        WriteAtomically(outputPath, patched);
        std::wcout << L"Patched executable: " << outputPath << L"\n"
                   << L"Loader section: .pvzm; DLL: pvzmod.dll\n";
        return 0;
    } catch (const std::exception& exception) {
        std::cerr << "Patch failed: " << exception.what() << '\n';
        return 1;
    }
}
