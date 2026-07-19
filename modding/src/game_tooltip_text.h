#pragma once

#include <cstdint>
#include <string>

namespace pvzmod {

void InitializeGameTooltipText(std::uint8_t* moduleBase);
void SetGameTooltipTitleAndLabel(void* tooltip, const std::string& utf8Title,
                                 const std::string& utf8Label);
void SetGameTooltipWarning(void* tooltip, const std::string& utf8Warning);

}  // namespace pvzmod
