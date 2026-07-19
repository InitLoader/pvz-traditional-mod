#include "hook_utils.h"

#include "logger.h"

#include <MinHook.h>

#include <sstream>

namespace pvzmod {
namespace {

constexpr DWORD kSupportedTimestamp = 0x49ECF563;

}  // namespace

bool GetSupportedExecutableBase(std::uint8_t*& moduleBase) {
    moduleBase = reinterpret_cast<std::uint8_t*>(GetModuleHandleW(nullptr));
    if (moduleBase == nullptr) {
        LogError("GetModuleHandleW(nullptr) failed.");
        return false;
    }

    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(moduleBase);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) {
        LogError("The main module has no valid DOS header.");
        return false;
    }
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(moduleBase + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC) {
        LogError("The main module is not a valid 32-bit PE image.");
        return false;
    }
    if (nt->FileHeader.TimeDateStamp != kSupportedTimestamp) {
        std::ostringstream message;
        message << "Unsupported PlantsVsZombies.exe timestamp 0x" << std::hex << nt->FileHeader.TimeDateStamp
                << "; expected 0x" << kSupportedTimestamp << '.';
        LogError(message.str());
        return false;
    }
    return true;
}

bool InitializeHookEngine() {
    const MH_STATUS status = MH_Initialize();
    if (status != MH_OK && status != MH_ERROR_ALREADY_INITIALIZED) {
        LogError("MH_Initialize failed with status " + std::to_string(status) + '.');
        return false;
    }
    return true;
}

bool CreateAndEnableHook(
    std::uint8_t* moduleBase,
    const std::uintptr_t rva,
    void* detour,
    void** original,
    const char* name) {
    void* target = moduleBase + rva;
    const MH_STATUS createStatus = MH_CreateHook(target, detour, original);
    if (createStatus != MH_OK) {
        LogError(std::string("MH_CreateHook(") + name + ") failed with status " +
                 std::to_string(createStatus) + '.');
        return false;
    }
    const MH_STATUS enableStatus = MH_EnableHook(target);
    if (enableStatus != MH_OK) {
        LogError(std::string("MH_EnableHook(") + name + ") failed with status " +
                 std::to_string(enableStatus) + '.');
        return false;
    }
    return true;
}

}  // namespace pvzmod
