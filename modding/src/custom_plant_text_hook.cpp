#include "hook_modules.h"

#include "game_tooltip_text.h"
#include "hook_utils.h"
#include "logger.h"
#include "plant_catalog_runtime.h"

#include <array>
#include <cstdint>

extern "C" void* g_originalBoardUpdateToolTip = nullptr;
extern "C" void __stdcall BoardUpdateToolTipDetour(void* board);

namespace pvzmod {
namespace {

constexpr std::uintptr_t kBoardUpdateToolTipRva = 0x0000EF00;
constexpr std::array<std::uint8_t, 12> kBoardUpdateToolTipPrologue =
    {0x6A, 0xFF, 0x64, 0xA1, 0x00, 0x00, 0x00, 0x00, 0x68, 0xC0, 0x9E, 0x64};

template <typename T>
T& TextField(void* object, const std::size_t offset) {
    return *reinterpret_cast<T*>(static_cast<std::uint8_t*>(object) + offset);
}

void OverrideCustomSeedPacketToolTip(void* board) {
    if (!board) return;
    void* app = TextField<void*>(board, 0x8C);
    void* seedBank = TextField<void*>(board, 0x144);
    void* tooltip = TextField<void*>(board, 0x154);
    if (!app || !seedBank || !tooltip) return;
    void* widgetManager = TextField<void*>(app, 0x320);
    if (!widgetManager) return;
    const int mouseX = TextField<int>(widgetManager, 0xE0);
    const int mouseY = TextField<int>(widgetManager, 0xE4);
    const int bankX = TextField<int>(seedBank, 0x08);
    const int bankY = TextField<int>(seedBank, 0x0C);
    const int packetCount = TextField<int>(seedBank, 0x24);

    for (int index = 0; index < packetCount && index < 10; ++index) {
        auto* packet = static_cast<std::uint8_t*>(seedBank) + 0x28 + index * 0x50;
        if (!*reinterpret_cast<bool*>(packet + 0x18)) continue;
        const int x = bankX + *reinterpret_cast<int*>(packet + 0x08) +
                      *reinterpret_cast<int*>(packet + 0x30);
        const int y = bankY + *reinterpret_cast<int*>(packet + 0x0C);
        const int width = *reinterpret_cast<int*>(packet + 0x10);
        const int height = *reinterpret_cast<int*>(packet + 0x14);
        if (mouseX < x || mouseX >= x + width || mouseY < y || mouseY >= y + height) continue;

        const int marker = *reinterpret_cast<int*>(packet + 0x38);
        const CustomPlantDefinition* definition = FindCustomPlantByLogicalId(marker);
        if (!definition) return;
        SetGameTooltipTitleAndLabel(tooltip, definition->name, definition->description);
        const int tooltipWidth = TextField<int>(tooltip, 0x5C);
        TextField<int>(tooltip, 0x54) = (50 - tooltipWidth) / 2 + x;
        TextField<int>(tooltip, 0x58) = y + 70;
        TextField<bool>(tooltip, 0x64) = true;
        TextField<bool>(tooltip, 0x65) = false;
        return;
    }
}

}  // namespace

bool InstallCustomPlantTextHooks(std::uint8_t* moduleBase) {
    InitializeGameTooltipText(moduleBase);
    if (!VerifyHookTarget(moduleBase, kBoardUpdateToolTipRva, kBoardUpdateToolTipPrologue,
                          "Board::UpdateToolTip")) return false;
    if (!CreateAndEnableHook(moduleBase, kBoardUpdateToolTipRva,
                             reinterpret_cast<void*>(&BoardUpdateToolTipDetour),
                             &g_originalBoardUpdateToolTip, "Board::UpdateToolTip")) return false;
    LogInfo("Installed custom plant chooser and seed-bank tooltip text support.");
    return true;
}

}  // namespace pvzmod

extern "C" void __stdcall BoardUpdateToolTipDetour(void* board) {
    using Fn = void(__stdcall*)(void*);
    reinterpret_cast<Fn>(g_originalBoardUpdateToolTip)(board);
    pvzmod::OverrideCustomSeedPacketToolTip(board);
}
