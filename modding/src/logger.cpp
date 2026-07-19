#include "logger.h"

#include <array>
#include <fstream>
#include <iomanip>
#include <mutex>
#include <sstream>

namespace pvzmod {
namespace {

std::mutex g_logMutex;
std::filesystem::path g_moduleDirectory;
std::filesystem::path g_logPath;

void WriteLog(const char* level, const std::string& message) {
    std::lock_guard lock(g_logMutex);
    if (g_logPath.empty()) {
        return;
    }

    SYSTEMTIME time{};
    GetLocalTime(&time);

    std::ofstream output(g_logPath, std::ios::binary | std::ios::app);
    if (!output) {
        return;
    }

    output << std::setfill('0')
           << '[' << std::setw(4) << time.wYear << '-'
           << std::setw(2) << time.wMonth << '-'
           << std::setw(2) << time.wDay << ' '
           << std::setw(2) << time.wHour << ':'
           << std::setw(2) << time.wMinute << ':'
           << std::setw(2) << time.wSecond << "] "
           << '[' << level << "] " << message << "\r\n";
}

}  // namespace

void InitializeLogger(HMODULE module) {
    std::array<wchar_t, 32768> modulePath{};
    const DWORD length = GetModuleFileNameW(module, modulePath.data(), static_cast<DWORD>(modulePath.size()));
    if (length == 0 || length >= modulePath.size()) {
        return;
    }

    g_moduleDirectory = std::filesystem::path(modulePath.data()).parent_path();
    const std::filesystem::path logDirectory = g_moduleDirectory / L"pvzmod" / L"logs";
    std::error_code error;
    std::filesystem::create_directories(logDirectory, error);
    g_logPath = logDirectory / L"pvzmod.log";
}

void LogInfo(const std::string& message) {
    WriteLog("INFO", message);
}

void LogWarning(const std::string& message) {
    WriteLog("WARN", message);
}

void LogError(const std::string& message) {
    WriteLog("ERROR", message);
}

std::filesystem::path ModuleDirectory() {
    return g_moduleDirectory;
}

}  // namespace pvzmod
