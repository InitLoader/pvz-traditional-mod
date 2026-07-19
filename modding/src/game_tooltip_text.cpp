#include "game_tooltip_text.h"

#include <Windows.h>

#include <array>
#include <string>

namespace pvzmod {
namespace {

constexpr std::uintptr_t kGameStringConstructorRva = 0x00004450;
constexpr std::uintptr_t kGameStringDestructorRva = 0x00004420;
constexpr std::uintptr_t kTooltipSetLabelRva = 0x0011A8D0;
constexpr std::uintptr_t kTooltipSetTitleRva = 0x0011A950;
constexpr std::uintptr_t kTooltipSetWarningRva = 0x0011A9D0;

std::uint8_t* g_tooltipModuleBase = nullptr;

std::string Utf8ToGameText(const std::string& utf8) {
    if (utf8.empty()) return {};
    const int wideLength = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, utf8.data(),
                                                static_cast<int>(utf8.size()), nullptr, 0);
    if (wideLength <= 0) return utf8;
    std::wstring wide(static_cast<std::size_t>(wideLength), L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, utf8.data(), static_cast<int>(utf8.size()),
                        wide.data(), wideLength);
    const int gameLength = WideCharToMultiByte(936, 0, wide.data(), wideLength, nullptr, 0, nullptr, nullptr);
    if (gameLength <= 0) return utf8;
    std::string gameText(static_cast<std::size_t>(gameLength), '\0');
    WideCharToMultiByte(936, 0, wide.data(), wideLength, gameText.data(), gameLength, nullptr, nullptr);
    return gameText;
}

void SetTooltipString(void* tooltip, const std::uintptr_t setterRva, const std::string& utf8Text) {
    if (!tooltip || !g_tooltipModuleBase) return;
    const std::string gameText = Utf8ToGameText(utf8Text);
    const char* text = gameText.c_str();
    alignas(8) std::array<std::uint8_t, 32> gameString{};
    void* stringObject = gameString.data();
    void* constructor = g_tooltipModuleBase + kGameStringConstructorRva;
    void* destructor = g_tooltipModuleBase + kGameStringDestructorRva;
    void* setter = g_tooltipModuleBase + setterRva;
    __asm {
        mov ecx, stringObject
        push text
        call constructor

        mov esi, tooltip
        mov edx, stringObject
        call setter

        mov ecx, stringObject
        call destructor
    }
}

}  // namespace

void InitializeGameTooltipText(std::uint8_t* moduleBase) {
    g_tooltipModuleBase = moduleBase;
}

void SetGameTooltipTitleAndLabel(void* tooltip, const std::string& utf8Title,
                                 const std::string& utf8Label) {
    SetTooltipString(tooltip, kTooltipSetTitleRva, utf8Title);
    SetTooltipString(tooltip, kTooltipSetLabelRva, utf8Label);
}

void SetGameTooltipWarning(void* tooltip, const std::string& utf8Warning) {
    SetTooltipString(tooltip, kTooltipSetWarningRva, utf8Warning);
}

}  // namespace pvzmod
