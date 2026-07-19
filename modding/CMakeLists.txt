cmake_minimum_required(VERSION 3.24)

project(PvZWaveMod VERSION 0.1.0 LANGUAGES CXX C)

if(NOT WIN32 OR NOT MSVC)
    message(FATAL_ERROR "PvZWaveMod currently requires 32-bit MSVC on Windows.")
endif()

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
    message(FATAL_ERROR "Configure with -A Win32. Plants vs. Zombies 1.0.0.1051 is a 32-bit process.")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

include(FetchContent)

set(JSON_BuildTests OFF CACHE INTERNAL "")
set(JSON_Install OFF CACHE INTERNAL "")
FetchContent_Declare(
    nlohmann_json
    URL https://github.com/nlohmann/json/releases/download/v3.11.3/json.tar.xz
    DOWNLOAD_EXTRACT_TIMESTAMP TRUE
)

set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
FetchContent_Declare(
    minhook
    URL https://github.com/TsudaKageyu/minhook/archive/refs/tags/v1.3.4.tar.gz
    DOWNLOAD_EXTRACT_TIMESTAMP TRUE
)

FetchContent_MakeAvailable(nlohmann_json minhook)

add_library(pvz_wave_core STATIC
    src/custom_plant_config.cpp
    src/global_config.cpp
    src/plant_attack_config.cpp
    src/seed_ui_config.cpp
    src/spawn_config.cpp
    src/wave_generator.cpp
    src/wave_multiplier.cpp
    src/wave_multiplier_config.cpp
    src/zombie_armor_adapter.cpp
    src/zombie_config.cpp
)
target_include_directories(pvz_wave_core PUBLIC src)
target_link_libraries(pvz_wave_core PUBLIC nlohmann_json::nlohmann_json)
target_compile_definitions(pvz_wave_core PUBLIC NOMINMAX WIN32_LEAN_AND_MEAN)

add_library(pvzmod SHARED
    src/custom_plant_hook.cpp
    src/custom_plant_text_hook.cpp
    src/dllmain.cpp
    src/game_tooltip_text.cpp
    src/hook_utils.cpp
    src/logger.cpp
    src/plant_catalog_runtime.cpp
    src/plant_attack_hook.cpp
    src/pvz_hook.cpp
    src/seed_ui_hook.cpp
    src/sun_hook.cpp
    src/wave_hook.cpp
    src/zombie_hook.cpp
)
target_include_directories(pvzmod PRIVATE src)
target_link_libraries(pvzmod PRIVATE pvz_wave_core minhook)
target_compile_definitions(pvzmod PRIVATE NOMINMAX WIN32_LEAN_AND_MEAN)
set_target_properties(pvzmod PROPERTIES
    OUTPUT_NAME "pvzmod"
    PREFIX ""
)

add_executable(PvZModPatcher
    patcher/main.cpp
)
target_compile_definitions(PvZModPatcher PRIVATE NOMINMAX WIN32_LEAN_AND_MEAN UNICODE _UNICODE)
target_link_libraries(PvZModPatcher PRIVATE bcrypt)

add_executable(PvZWaveConfigTests
    tests/wave_config_tests.cpp
)
target_link_libraries(PvZWaveConfigTests PRIVATE pvz_wave_core)
target_compile_definitions(PvZWaveConfigTests PRIVATE NOMINMAX WIN32_LEAN_AND_MEAN)

enable_testing()
add_test(NAME wave_config_tests COMMAND PvZWaveConfigTests)
