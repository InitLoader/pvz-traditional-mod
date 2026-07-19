#pragma once

#include <Windows.h>

#include <filesystem>
#include <string>

namespace pvzmod {

void InitializeLogger(HMODULE module);
void LogInfo(const std::string& message);
void LogWarning(const std::string& message);
void LogError(const std::string& message);
[[nodiscard]] std::filesystem::path ModuleDirectory();

}  // namespace pvzmod
