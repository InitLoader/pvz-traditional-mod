#pragma once

#include <Windows.h>

namespace pvzmod {

[[nodiscard]] bool InstallPvZHooks();
DWORD WINAPI InitializeModThread(void* moduleParameter);

}  // namespace pvzmod
