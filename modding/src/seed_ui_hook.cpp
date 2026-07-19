#include "hook_modules.h"

#include "hook_utils.h"
#include "game_tooltip_text.h"
#include "logger.h"
#include "plant_catalog_runtime.h"

#include <Windows.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

extern "C" void* g_originalSeedChooserDraw = nullptr;
extern "C" void* g_originalSeedChooserDestructor = nullptr;
extern "C" void* g_originalSeedChooserUpdate = nullptr;
extern "C" void* g_originalSeedChooserMouseDown = nullptr;
extern "C" void* g_originalFindSeedInBank = nullptr;
extern "C" void* g_originalDrawSeedPacket = nullptr;
extern "C" void* g_originalSetPacketType = nullptr;
extern "C" void* g_originalGetNumSeedsInBank = nullptr;

extern "C" void __fastcall SeedChooserDrawDetour(void* screen, void*, void* graphics);
extern "C" void __fastcall SeedChooserDestructorDetour(void* screen, void*);
extern "C" void __fastcall SeedChooserUpdateDetour(void* screen, void*);
extern "C" void __fastcall SeedChooserMouseDownDetour(void* screen, void*, int x, int y, int clickCount);
extern "C" void FindSeedInBankDetour();
extern "C" void DrawSeedPacketDetour();
extern "C" void SetPacketTypeDetour();
extern "C" void GetNumSeedsInBankDetour();
extern "C" int __stdcall FindSeedInBankReplacement(void* screen, int bankIndex);
extern "C" void __stdcall MapChooserDrawPacket(int* seedType, int* marker);
extern "C" unsigned long long __stdcall MapChooserPacketValues(int seedType, int marker);
extern "C" int __stdcall ResolveSeedSlotCount(int originalCount);

namespace pvzmod {
namespace {

constexpr int kCardsPerPage = 40;
constexpr int kCardWidth = 50;
constexpr int kCardHeight = 70;
constexpr int kPageButtonX = 355;
constexpr int kPageButtonY = 520;
constexpr int kPageButtonWidth = 82;
constexpr int kPageButtonHeight = 52;

constexpr std::uintptr_t kSeedChooserDrawRva = 0x00084690;
constexpr std::uintptr_t kSeedChooserDestructorRva = 0x000844D0;
constexpr std::uintptr_t kSeedChooserUpdateRva = 0x000851A0;
constexpr std::uintptr_t kSeedChooserMouseDownRva = 0x00086770;
constexpr std::uintptr_t kFindSeedInBankRva = 0x00085E20;
constexpr std::uintptr_t kDrawSeedPacketRva = 0x000876F0;
constexpr std::uintptr_t kSetPacketTypeRva = 0x00089B50;
constexpr std::uintptr_t kGetNumSeedsInBankRva = 0x0001BEE0;
constexpr std::uintptr_t kGraphicsDrawImageRva = 0x00187150;
constexpr std::uintptr_t kToolTipWidgetDrawRva = 0x0011AA50;
constexpr std::uintptr_t kGameAppPlaySampleRva = 0x000560C0;
constexpr std::uintptr_t kSexyAppSetCursorRva = 0x001548C0;
constexpr std::uintptr_t kGameStringConstructorRva = 0x00004450;
constexpr std::uintptr_t kGameStringDestructorRva = 0x00004420;
constexpr std::uintptr_t kTodLoadResourcesRva = 0x00113120;
constexpr std::uintptr_t kStoreNextButtonImageRva = 0x002A79B8;
constexpr std::uintptr_t kStoreNextButtonHighlightImageRva = 0x002A78B8;
constexpr std::uintptr_t kSeedPacketSilhouetteImageRva = 0x002A72E8;
constexpr std::uintptr_t kSeedChooserBackgroundImageRva = 0x002A7460;
constexpr std::uintptr_t kTapSoundIdRva = 0x002A7980;

constexpr std::array<std::uint8_t, 12> kSeedChooserDrawPrologue =
    {0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8, 0x6A, 0xFF, 0x68, 0xA3, 0xE8, 0x64};
constexpr std::array<std::uint8_t, 12> kSeedChooserDestructorPrologue =
    {0x6A, 0xFF, 0x68, 0x88, 0x22, 0x64, 0x00, 0x64, 0xA1, 0x00, 0x00, 0x00};
constexpr std::array<std::uint8_t, 12> kSeedChooserUpdatePrologue =
    {0x51, 0x53, 0x55, 0x56, 0x57, 0x8B, 0xE9, 0xE8, 0x84, 0x4B, 0x0B, 0x00};
constexpr std::array<std::uint8_t, 12> kSeedChooserMouseDownPrologue =
    {0x64, 0xA1, 0x00, 0x00, 0x00, 0x00, 0x6A, 0xFF, 0x68, 0xFB, 0xF4, 0x64};
constexpr std::array<std::uint8_t, 12> kFindSeedInBankPrologue =
    {0x53, 0x57, 0x33, 0xDB, 0x83, 0xFB, 0x28, 0x8B, 0xBE, 0x10, 0x0D, 0x00};
constexpr std::array<std::uint8_t, 12> kDrawSeedPacketPrologue =
    {0x6A, 0xFF, 0x68, 0x7F, 0xE8, 0x64, 0x00, 0x64, 0xA1, 0x00, 0x00, 0x00};
constexpr std::array<std::uint8_t, 12> kSetPacketTypePrologue =
    {0x53, 0x55, 0x33, 0xED, 0x83, 0xFF, 0x30, 0x89, 0x7E, 0x34, 0x89, 0x56};
constexpr std::array<std::uint8_t, 12> kGetNumSeedsInBankPrologue =
    {0x56, 0x8B, 0xB7, 0x8C, 0x00, 0x00, 0x00, 0x8B, 0x86, 0xF8, 0x07, 0x00};

enum CustomSeedState {
    kFlyingToBank = 0,
    kInBank = 1,
    kFlyingToChooser = 2,
    kInChooser = 3,
};

struct CustomChosenSeed {
    const CustomPlantDefinition* definition = nullptr;
    int x = 0;
    int y = 0;
    int timeStartMotion = 0;
    int timeEndMotion = 0;
    int startX = 0;
    int startY = 0;
    int endX = 0;
    int endY = 0;
    int state = kInChooser;
    int bankIndex = 0;
};

struct ChooserRuntimeState {
    int page = 0;  // 0 is the original page; custom pages start at 1.
    bool initialized = false;
    std::vector<CustomChosenSeed> cards;
    std::array<int, kOriginalPlantTypeCount> originalX{};
    std::array<int, kOriginalPlantTypeCount> originalY{};
    std::array<bool, kOriginalPlantTypeCount> originalPositionKnown{};
    int lastTooltipLogicalId = -1;
};

struct GameRect {
    int x;
    int y;
    int width;
    int height;
};

std::uint8_t* g_seedUiModuleBase = nullptr;
std::mutex g_chooserMutex;
std::unordered_map<void*, ChooserRuntimeState> g_choosers;
bool g_storeResourcesAttempted = false;
bool g_storeResourcesLoaded = false;

template <typename T>
T& Field(void* object, const std::size_t offset) {
    return *reinterpret_cast<T*>(static_cast<std::uint8_t*>(object) + offset);
}

int PageCount() {
    const int count = CustomChooserPlantCount();
    return (count + kCardsPerPage - 1) / kCardsPerPage;
}

ChooserRuntimeState& StateForLocked(void* screen) {
    ChooserRuntimeState& state = g_choosers[screen];
    if (state.initialized) return state;

    state.initialized = true;
    state.cards.reserve(CustomChooserPlantCount());
    for (int index = 0; index < CustomChooserPlantCount(); ++index) {
        const CustomPlantDefinition* definition = CustomPlantAt(index);
        if (!definition) continue;
        const int slot = index % kCardsPerPage;
        CustomChosenSeed card;
        card.definition = definition;
        card.x = card.startX = card.endX = (slot % 8) * 53 + 22;
        card.y = card.startY = card.endY = (slot / 8) * 73 + 128;
        state.cards.push_back(card);
    }

    auto* chosen = static_cast<std::uint8_t*>(screen) + 0xA4;
    for (int seed = 0; seed < kOriginalPlantTypeCount; ++seed) {
        const auto* record = chosen + seed * 0x3C;
        state.originalX[seed] = *reinterpret_cast<const int*>(record);
        state.originalY[seed] = *reinterpret_cast<const int*>(record + 4);
        state.originalPositionKnown[seed] = true;
    }
    return state;
}

int BankCardX(const int bankIndex, const int packetCount) {
    if (packetCount <= 7) return bankIndex * 59 + 85;
    if (packetCount == 8) return bankIndex * 54 + 81;
    if (packetCount == 9) return bankIndex * 52 + 80;
    return bankIndex * 51 + 79;
}

int SeedPacketCount(void* screen) {
    if (!screen) return 0;
    void* board = Field<void*>(screen, 0xD14);
    if (!board) return 0;
    void* seedBank = Field<void*>(board, 0x144);
    if (!seedBank) return 0;
    const int count = Field<int>(seedBank, 0x24);
    return count >= 0 && count <= 10 ? count : 0;
}

void ChooserCardPosition(const int index, int& x, int& y) {
    x = (index % 8) * 53 + 22;
    y = (index / 8) * 73 + 128;
}

void SetOriginalChooserCardsVisibleLocked(void* screen, ChooserRuntimeState& runtime, const bool visible) {
    auto* chosen = static_cast<std::uint8_t*>(screen) + 0xA4;
    for (int seed = 0; seed < kOriginalPlantTypeCount; ++seed) {
        auto* record = chosen + seed * 0x3C;
        int& x = *reinterpret_cast<int*>(record);
        int& y = *reinterpret_cast<int*>(record + 4);
        const int seedState = *reinterpret_cast<int*>(record + 0x24);
        if (seedState != kInChooser) continue;

        if (visible) {
            if (runtime.originalPositionKnown[seed]) {
                x = runtime.originalX[seed];
                y = runtime.originalY[seed];
            }
        } else {
            if (x > -500) {
                runtime.originalX[seed] = x;
                runtime.originalY[seed] = y;
                runtime.originalPositionKnown[seed] = true;
            }
            x = -1000;
            y = -1000;
        }
    }
}

void PlayTapSound(void* screen) {
    if (!screen || !g_seedUiModuleBase) return;
    void* app = Field<void*>(screen, 0xD10);
    if (!app) return;
    const int soundId = *reinterpret_cast<const int*>(g_seedUiModuleBase + kTapSoundIdRva);
    using Fn = void(__thiscall*)(void*, int);
    reinterpret_cast<Fn>(g_seedUiModuleBase + kGameAppPlaySampleRva)(app, soundId);
}

void SetHandCursor(void* screen) {
    if (!screen || !g_seedUiModuleBase) return;
    void* app = Field<void*>(screen, 0xD10);
    if (!app) return;
    void* target = g_seedUiModuleBase + kSexyAppSetCursorRva;
    __asm {
        mov ecx, app
        mov eax, 1
        call target
    }
}

int AnimateEaseInOut(const int timeStart, const int timeEnd, const int age,
                     const int positionStart, const int positionEnd) {
    if (age <= timeStart) return positionStart;
    if (age >= timeEnd) return positionEnd;
    float t = static_cast<float>(age - timeStart) / static_cast<float>(timeEnd - timeStart);
    const auto smoothStep = [](const float value) { return value * value * (3.0F - 2.0F * value); };
    t = smoothStep(smoothStep(t));
    return static_cast<int>(std::lround(positionStart + (positionEnd - positionStart) * t));
}

void DrawImageRaw(void* graphics, void* image, const int x, const int y) {
    if (!graphics || !image || !g_seedUiModuleBase) return;
    void* target = g_seedUiModuleBase + kGraphicsDrawImageRva;
    __asm {
        mov eax, graphics
        mov ebx, image
        push y
        push x
        call target
    }
}

GameRect IntersectRect(const GameRect& lhs, const GameRect& rhs) {
    const int left = std::max(lhs.x, rhs.x);
    const int top = std::max(lhs.y, rhs.y);
    const int right = std::min(lhs.x + lhs.width, rhs.x + rhs.width);
    const int bottom = std::min(lhs.y + lhs.height, rhs.y + rhs.height);
    return {left, top, std::max(0, right - left), std::max(0, bottom - top)};
}

GameRect GetGraphicsClipRect(void* graphics) {
    // Graphics has a vtable at +0; GraphicsState::mClipRect therefore starts
    // at +0x20 in this 32-bit build.
    return *reinterpret_cast<const GameRect*>(static_cast<const std::uint8_t*>(graphics) + 0x20);
}

void SetGraphicsClipRect(void* graphics, const GameRect& clip) {
    *reinterpret_cast<GameRect*>(static_cast<std::uint8_t*>(graphics) + 0x20) = clip;
}

void DrawOriginalChooserRegion(void* screen, void* graphics, const GameRect& region) {
    if (!screen || !graphics || !g_originalSeedChooserDraw) return;
    const GameRect savedClip = GetGraphicsClipRect(graphics);
    SetGraphicsClipRect(graphics, IntersectRect(savedClip, region));
    using Fn = void(__thiscall*)(void*, void*);
    reinterpret_cast<Fn>(g_originalSeedChooserDraw)(screen, graphics);
    SetGraphicsClipRect(graphics, savedClip);
}

void DrawCleanChooserCatalogBackground(void* graphics) {
    if (!graphics || !g_seedUiModuleBase) return;
    const GameRect savedClip = GetGraphicsClipRect(graphics);
    // The original chooser background begins at y=87. Redraw only the catalog
    // band from that image so this is a clean page, not an opaque overlay on
    // top of original cards.
    SetGraphicsClipRect(graphics, IntersectRect(savedClip, {0, 121, 465, 374}));
    void* background = *reinterpret_cast<void**>(
        g_seedUiModuleBase + kSeedChooserBackgroundImageRva);
    DrawImageRaw(graphics, background, 0, 87);
    SetGraphicsClipRect(graphics, savedClip);
}

void DrawChooserToolTip(void* screen, void* graphics) {
    if (!screen || !graphics || !g_seedUiModuleBase) return;
    void* tooltip = Field<void*>(screen, 0xD28);
    if (!tooltip) return;
    void* target = g_seedUiModuleBase + kToolTipWidgetDrawRva;
    __asm {
        mov ebx, graphics
        push tooltip
        call target
    }
}

void DrawPacketRaw(
    void* graphics, const int x, const int y, const int seedType, const int marker, const int grayness) {
    if (!graphics || !g_originalDrawSeedPacket) return;
    const float drawX = static_cast<float>(x);
    const float drawY = static_cast<float>(y);
    void* target = g_originalDrawSeedPacket;
    __asm {
        push 0
        push 1
        push 0
        push marker
        push seedType
        push drawY
        push drawX
        push graphics
        mov ecx, grayness
        call target
        add esp, 32
    }
}

bool Contains(const int x, const int y, const int left, const int top, const int width, const int height) {
    return x >= left && x < left + width && y >= top && y < top + height;
}

void EnsureStoreButtonResourcesLoaded() {
    if (g_storeResourcesAttempted || !g_seedUiModuleBase) return;
    g_storeResourcesAttempted = true;
    alignas(8) std::array<std::uint8_t, 32> gameString{};
    const char* groupName = "DelayLoad_Store";
    void* constructor = g_seedUiModuleBase + kGameStringConstructorRva;
    void* destructor = g_seedUiModuleBase + kGameStringDestructorRva;
    void* loadResources = g_seedUiModuleBase + kTodLoadResourcesRva;
    int loaded = 0;
    void* stringObject = gameString.data();
    __asm {
        mov ecx, stringObject
        push groupName
        call constructor
        push stringObject
        call loadResources
        add esp, 4
        movzx eax, al
        mov loaded, eax
        mov ecx, stringObject
        call destructor
    }
    g_storeResourcesLoaded = loaded != 0;
    if (g_storeResourcesLoaded) LogInfo("Loaded DelayLoad_Store resources for the seed chooser page button.");
    else LogWarning("Could not load DelayLoad_Store resources; the page button image is unavailable.");
}

void DrawChooserExtension(void* screen, void* graphics) {
    const int customPages = PageCount();
    if (customPages <= 0) return;

    EnsureStoreButtonResourcesLoaded();
    int page = 0;
    std::vector<CustomChosenSeed> cards;
    {
        std::lock_guard lock(g_chooserMutex);
        ChooserRuntimeState& state = StateForLocked(screen);
        page = state.page;
        cards = state.cards;
    }

    if (page > 0) {
        void* silhouette = *reinterpret_cast<void**>(g_seedUiModuleBase + kSeedPacketSilhouetteImageRva);
        DrawCleanChooserCatalogBackground(graphics);
        for (int slot = 0; slot < kCardsPerPage; ++slot) {
            int x = 0, y = 0;
            ChooserCardPosition(slot, x, y);
            DrawImageRaw(graphics, silhouette, x, y);
        }
        const int first = (page - 1) * kCardsPerPage;
        for (int slot = 0; slot < kCardsPerPage; ++slot) {
            const int index = first + slot;
            if (index < 0 || index >= static_cast<int>(cards.size())) break;
            const CustomChosenSeed& card = cards[index];
            if (!card.definition) continue;
            int x = 0, y = 0;
            ChooserCardPosition(slot, x, y);
            if (card.state != kInChooser) {
                DrawPacketRaw(graphics, x, y, card.definition->templatePlantId, card.definition->id, 55);
            } else {
                DrawPacketRaw(graphics, card.x, card.y, card.definition->templatePlantId,
                              card.definition->id, 255);
            }
        }
    }

    for (const CustomChosenSeed& card : cards) {
        if (!card.definition || card.state != kInBank) continue;
        DrawPacketRaw(graphics, card.x, card.y, card.definition->templatePlantId,
                      card.definition->id, 255);
    }
    for (const CustomChosenSeed& card : cards) {
        if (!card.definition || (card.state != kFlyingToBank && card.state != kFlyingToChooser)) continue;
        DrawPacketRaw(graphics, card.x, card.y, card.definition->templatePlantId,
                      card.definition->id, 255);
    }

    if (!g_storeResourcesLoaded) return;
    const int mouseX = Field<int>(screen, 0xD30);
    const int mouseY = Field<int>(screen, 0xD34);
    const bool hover = Contains(mouseX, mouseY, kPageButtonX, kPageButtonY, kPageButtonWidth, kPageButtonHeight);
    const std::uintptr_t imageRva = hover ? kStoreNextButtonHighlightImageRva : kStoreNextButtonImageRva;
    DrawImageRaw(graphics, *reinterpret_cast<void**>(g_seedUiModuleBase + imageRva), kPageButtonX, kPageButtonY);
    if (page > 0) DrawChooserToolTip(screen, graphics);
}

void SetStartButtonDisabled(void* screen, const bool disabled) {
    void* button = Field<void*>(screen, 0x88);
    if (button) Field<std::uint8_t>(button, 0x1A) = disabled ? 1 : 0;
}

void StartCustomMotion(CustomChosenSeed& card, const int state, const int age,
                       const int endX, const int endY, const int duration, const int bankIndex) {
    card.timeStartMotion = age;
    card.timeEndMotion = age + duration;
    card.startX = card.x;
    card.startY = card.y;
    card.endX = endX;
    card.endY = endY;
    card.state = state;
    card.bankIndex = bankIndex;
}

void StartOriginalMotion(void* record, const int state, const int age,
                         const int endX, const int endY, const int duration, const int bankIndex) {
    auto* bytes = static_cast<std::uint8_t*>(record);
    *reinterpret_cast<int*>(bytes + 0x08) = age;
    *reinterpret_cast<int*>(bytes + 0x0C) = age + duration;
    *reinterpret_cast<int*>(bytes + 0x10) = *reinterpret_cast<int*>(bytes);
    *reinterpret_cast<int*>(bytes + 0x14) = *reinterpret_cast<int*>(bytes + 4);
    *reinterpret_cast<int*>(bytes + 0x18) = endX;
    *reinterpret_cast<int*>(bytes + 0x1C) = endY;
    *reinterpret_cast<int*>(bytes + 0x24) = state;
    *reinterpret_cast<int*>(bytes + 0x28) = bankIndex;
}

void LandAllChooserFlights(void* screen) {
    std::lock_guard lock(g_chooserMutex);
    ChooserRuntimeState& runtime = StateForLocked(screen);

    auto* chosen = static_cast<std::uint8_t*>(screen) + 0xA4;
    for (int seed = 0; seed < kOriginalPlantTypeCount; ++seed) {
        auto* record = chosen + seed * 0x3C;
        int& seedState = *reinterpret_cast<int*>(record + 0x24);
        if (seedState != kFlyingToBank && seedState != kFlyingToChooser) continue;
        *reinterpret_cast<int*>(record) = *reinterpret_cast<int*>(record + 0x18);
        *reinterpret_cast<int*>(record + 4) = *reinterpret_cast<int*>(record + 0x1C);
        *reinterpret_cast<int*>(record + 0x08) = 0;
        *reinterpret_cast<int*>(record + 0x0C) = 0;
        seedState = seedState == kFlyingToBank ? kInBank : kInChooser;
    }
    for (CustomChosenSeed& card : runtime.cards) {
        if (card.state != kFlyingToBank && card.state != kFlyingToChooser) continue;
        card.x = card.endX;
        card.y = card.endY;
        card.timeStartMotion = 0;
        card.timeEndMotion = 0;
        card.state = card.state == kFlyingToBank ? kInBank : kInChooser;
    }
    // The original counter is derived state. Reconcile it from the actual
    // records so a stale count can never make all custom cards unclickable.
    Field<int>(screen, 0xD20) = 0;
    SetOriginalChooserCardsVisibleLocked(screen, runtime, runtime.page == 0);
}

void UpdateChooserExtension(void* screen) {
    std::lock_guard lock(g_chooserMutex);
    ChooserRuntimeState& runtime = StateForLocked(screen);
    const int age = Field<int>(screen, 0xD1C);
    int& seedsInFlight = Field<int>(screen, 0xD20);
    for (CustomChosenSeed& card : runtime.cards) {
        if (card.state != kFlyingToBank && card.state != kFlyingToChooser) continue;
        card.x = AnimateEaseInOut(card.timeStartMotion, card.timeEndMotion, age, card.startX, card.endX);
        card.y = AnimateEaseInOut(card.timeStartMotion, card.timeEndMotion, age, card.startY, card.endY);
        if (age < card.timeEndMotion) continue;
        card.x = card.endX;
        card.y = card.endY;
        card.timeStartMotion = 0;
        card.timeEndMotion = 0;
        card.state = card.state == kFlyingToBank ? kInBank : kInChooser;
        if (seedsInFlight > 0) --seedsInFlight;
    }
    SetOriginalChooserCardsVisibleLocked(screen, runtime, runtime.page == 0);

    if (Field<std::uint8_t>(screen, 0x55) == 0 || Field<int>(screen, 0xD38) != 0) return;
    const int mouseX = Field<int>(screen, 0xD30);
    const int mouseY = Field<int>(screen, 0xD34);
    bool showHand = Contains(mouseX, mouseY, kPageButtonX, kPageButtonY, kPageButtonWidth, kPageButtonHeight);
    const CustomPlantDefinition* hoveredPlant = nullptr;
    int hoveredX = 0;
    int hoveredY = 0;
    if (!showHand && runtime.page > 0) {
        const int first = (runtime.page - 1) * kCardsPerPage;
        const int last = std::min(first + kCardsPerPage, static_cast<int>(runtime.cards.size()));
        for (int index = first; index < last; ++index) {
            const CustomChosenSeed& card = runtime.cards[index];
            if (card.state == kInChooser && Contains(mouseX, mouseY, card.x, card.y, kCardWidth, kCardHeight)) {
                showHand = true;
                hoveredPlant = card.definition;
                hoveredX = card.x;
                hoveredY = card.y;
                break;
            }
        }
    }
    if (!showHand) {
        for (const CustomChosenSeed& card : runtime.cards) {
            if (card.state == kInBank && Contains(mouseX, mouseY, card.x, card.y, kCardWidth, kCardHeight)) {
                showHand = true;
                hoveredPlant = card.definition;
                hoveredX = card.x;
                hoveredY = card.y;
                break;
            }
        }
    }
    if (showHand) SetHandCursor(screen);

    void* tooltip = Field<void*>(screen, 0xD28);
    if (hoveredPlant && tooltip) {
        if (runtime.lastTooltipLogicalId != hoveredPlant->id) {
            SetGameTooltipTitleAndLabel(tooltip, hoveredPlant->name, hoveredPlant->description);
            SetGameTooltipWarning(tooltip, "");
            runtime.lastTooltipLogicalId = hoveredPlant->id;
        }
        const int tooltipWidth = Field<int>(tooltip, 0x5C);
        Field<int>(tooltip, 0x54) = std::clamp((kCardWidth - tooltipWidth) / 2 + hoveredX,
                                              0, 800 - tooltipWidth);
        Field<int>(tooltip, 0x58) = hoveredY + kCardHeight;
        Field<bool>(tooltip, 0x64) = true;
        Field<bool>(tooltip, 0x65) = false;
    } else {
        runtime.lastTooltipLogicalId = -1;
    }
}

bool RemoveBankCard(void* screen, const int bankIndex) {
    const int seedsInBank = Field<int>(screen, 0xD24);
    if (bankIndex < 0 || bankIndex >= seedsInBank) return false;
    const int age = Field<int>(screen, 0xD1C);
    const int packetCount = SeedPacketCount(screen);
    int& seedsInFlight = Field<int>(screen, 0xD20);
    bool found = false;
    std::lock_guard lock(g_chooserMutex);
    ChooserRuntimeState& runtime = StateForLocked(screen);
    auto* chosen = static_cast<std::uint8_t*>(screen) + 0xA4;
    for (int seed = 0; seed < kOriginalPlantTypeCount; ++seed) {
        auto* record = chosen + seed * 0x3C;
        const int state = *reinterpret_cast<int*>(record + 0x24);
        int& index = *reinterpret_cast<int*>(record + 0x28);
        if (state == kInBank && index == bankIndex) {
            const int endX = runtime.originalPositionKnown[seed] ? runtime.originalX[seed] : (seed % 8) * 53 + 22;
            const int endY = runtime.originalPositionKnown[seed] ? runtime.originalY[seed] : (seed / 8) * 73 + 128;
            StartOriginalMotion(record, kFlyingToChooser, age, endX, endY, 25, 0);
            ++seedsInFlight;
            found = true;
        } else if (state == kInBank && index > bankIndex) {
            --index;
            StartOriginalMotion(record, kFlyingToBank, age, BankCardX(index, packetCount), 8, 15, index);
            ++seedsInFlight;
        }
    }
    for (CustomChosenSeed& card : runtime.cards) {
        if (card.state == kInBank && card.bankIndex == bankIndex) {
            const int customIndex = static_cast<int>(&card - runtime.cards.data());
            const int slot = customIndex % kCardsPerPage;
            int endX = 0, endY = 0;
            ChooserCardPosition(slot, endX, endY);
            StartCustomMotion(card, kFlyingToChooser, age, endX, endY, 25, 0);
            ++seedsInFlight;
            found = true;
        } else if (card.state == kInBank && card.bankIndex > bankIndex) {
            --card.bankIndex;
            StartCustomMotion(card, kFlyingToBank, age, BankCardX(card.bankIndex, packetCount),
                              8, 15, card.bankIndex);
            ++seedsInFlight;
        }
    }
    if (!found) return false;
    Field<int>(screen, 0xD24) = seedsInBank - 1;
    SetStartButtonDisabled(screen, true);
    PlayTapSound(screen);
    return true;
}

bool HandleChooserExtensionClick(void* screen, const int x, const int y) {
    const int customPages = PageCount();
    if (customPages <= 0) return false;
    if (Contains(x, y, kPageButtonX, kPageButtonY, kPageButtonWidth, kPageButtonHeight)) {
        std::lock_guard lock(g_chooserMutex);
        ChooserRuntimeState& state = StateForLocked(screen);
        state.page = (state.page + 1) % (customPages + 1);
        SetOriginalChooserCardsVisibleLocked(screen, state, state.page == 0);
        PlayTapSound(screen);
        return true;
    }

    const int packetCount = SeedPacketCount(screen);
    for (int bank = 0; bank < Field<int>(screen, 0xD24); ++bank) {
        if (Contains(x, y, BankCardX(bank, packetCount), 8, kCardWidth, kCardHeight)) {
            return RemoveBankCard(screen, bank);
        }
    }

    std::lock_guard lock(g_chooserMutex);
    ChooserRuntimeState& state = StateForLocked(screen);
    const int page = state.page;
    if (page <= 0 || !Contains(x, y, 22, 128, 8 * 53, 5 * 73)) return false;

    const int column = (x - 22) / 53;
    const int row = (y - 128) / 73;
    if (column < 0 || column >= 8 || row < 0 || row >= 5) return true;
    const int slot = row * 8 + column;
    const int cardIndex = (page - 1) * kCardsPerPage + slot;
    if (cardIndex < 0 || cardIndex >= static_cast<int>(state.cards.size())) return true;
    CustomChosenSeed& card = state.cards[cardIndex];
    if (!card.definition || card.state != kInChooser) return true;
    int& seedsInBank = Field<int>(screen, 0xD24);
    if (seedsInBank >= packetCount) return true;
    const int age = Field<int>(screen, 0xD1C);
    StartCustomMotion(card, kFlyingToBank, age, BankCardX(seedsInBank, packetCount), 8, 25, seedsInBank);
    ++seedsInBank;
    ++Field<int>(screen, 0xD20);
    if (seedsInBank == packetCount) SetStartButtonDisabled(screen, false);
    PlayTapSound(screen);
    return true;
}

}  // namespace

bool InstallSeedUiHooks(std::uint8_t* moduleBase) {
    if (!InitializePlantCatalogRuntime()) return false;
    InitializeGameTooltipText(moduleBase);
    if (!VerifyHookTarget(moduleBase, kSeedChooserDrawRva, kSeedChooserDrawPrologue, "SeedChooser::Draw") ||
        !VerifyHookTarget(moduleBase, kSeedChooserDestructorRva, kSeedChooserDestructorPrologue,
                          "SeedChooser::~SeedChooser") ||
        !VerifyHookTarget(moduleBase, kSeedChooserUpdateRva, kSeedChooserUpdatePrologue, "SeedChooser::Update") ||
        !VerifyHookTarget(moduleBase, kSeedChooserMouseDownRva, kSeedChooserMouseDownPrologue, "SeedChooser::MouseDown") ||
        !VerifyHookTarget(moduleBase, kFindSeedInBankRva, kFindSeedInBankPrologue, "SeedChooser::FindSeedInBank") ||
        !VerifyHookTarget(moduleBase, kDrawSeedPacketRva, kDrawSeedPacketPrologue, "DrawSeedPacket") ||
        !VerifyHookTarget(moduleBase, kSetPacketTypeRva, kSetPacketTypePrologue, "SeedPacket::SetPacketType") ||
        !VerifyHookTarget(moduleBase, kGetNumSeedsInBankRva, kGetNumSeedsInBankPrologue, "Board::GetNumSeedsInBank")) {
        return false;
    }
    g_seedUiModuleBase = moduleBase;
    if (!CreateAndEnableHook(moduleBase, kSeedChooserDrawRva, reinterpret_cast<void*>(&SeedChooserDrawDetour),
                             &g_originalSeedChooserDraw, "SeedChooser::Draw") ||
        !CreateAndEnableHook(moduleBase, kSeedChooserDestructorRva,
                             reinterpret_cast<void*>(&SeedChooserDestructorDetour),
                             &g_originalSeedChooserDestructor, "SeedChooser::~SeedChooser") ||
        !CreateAndEnableHook(moduleBase, kSeedChooserUpdateRva, reinterpret_cast<void*>(&SeedChooserUpdateDetour),
                             &g_originalSeedChooserUpdate, "SeedChooser::Update") ||
        !CreateAndEnableHook(moduleBase, kSeedChooserMouseDownRva, reinterpret_cast<void*>(&SeedChooserMouseDownDetour),
                             &g_originalSeedChooserMouseDown, "SeedChooser::MouseDown") ||
        !CreateAndEnableHook(moduleBase, kFindSeedInBankRva, reinterpret_cast<void*>(&FindSeedInBankDetour),
                             &g_originalFindSeedInBank, "SeedChooser::FindSeedInBank") ||
        !CreateAndEnableHook(moduleBase, kDrawSeedPacketRva, reinterpret_cast<void*>(&DrawSeedPacketDetour),
                             &g_originalDrawSeedPacket, "DrawSeedPacket") ||
        !CreateAndEnableHook(moduleBase, kSetPacketTypeRva, reinterpret_cast<void*>(&SetPacketTypeDetour),
                             &g_originalSetPacketType, "SeedPacket::SetPacketType") ||
        !CreateAndEnableHook(moduleBase, kGetNumSeedsInBankRva, reinterpret_cast<void*>(&GetNumSeedsInBankDetour),
                             &g_originalGetNumSeedsInBank, "Board::GetNumSeedsInBank")) {
        return false;
    }
    LogInfo("Installed paged seed chooser extension: 40 custom cards per page, " +
            std::to_string(CustomChooserPlantCount()) + " configured custom card(s).");
    return true;
}

}  // namespace pvzmod

extern "C" void __fastcall SeedChooserDrawDetour(void* screen, void*, void* graphics) {
    int page = 0;
    {
        std::lock_guard lock(pvzmod::g_chooserMutex);
        pvzmod::ChooserRuntimeState& runtime = pvzmod::StateForLocked(screen);
        page = runtime.page;
        pvzmod::SetOriginalChooserCardsVisibleLocked(screen, runtime, runtime.page == 0);
    }
    if (page == 0) {
        using Fn = void(__thiscall*)(void*, void*);
        reinterpret_cast<Fn>(g_originalSeedChooserDraw)(screen, graphics);
    } else {
        // A custom page is composed from original UI regions rather than drawn
        // over a complete original page. The catalog band is never submitted
        // by the original renderer, so original cards cannot remain underneath.
        pvzmod::DrawOriginalChooserRegion(screen, graphics, {0, 0, 800, 121});
        pvzmod::DrawOriginalChooserRegion(screen, graphics, {0, 495, 800, 105});
    }
    pvzmod::DrawChooserExtension(screen, graphics);
}

extern "C" void __fastcall SeedChooserDestructorDetour(void* screen, void*) {
    {
        std::lock_guard lock(pvzmod::g_chooserMutex);
        pvzmod::g_choosers.erase(screen);
    }
    using Fn = void(__thiscall*)(void*);
    reinterpret_cast<Fn>(g_originalSeedChooserDestructor)(screen);
}

extern "C" void __fastcall SeedChooserUpdateDetour(void* screen, void*) {
    using Fn = void(__thiscall*)(void*);
    reinterpret_cast<Fn>(g_originalSeedChooserUpdate)(screen);
    pvzmod::UpdateChooserExtension(screen);
}

extern "C" void __fastcall SeedChooserMouseDownDetour(
    void* screen, void*, const int x, const int y, const int clickCount) {
    if (clickCount == 1) {
        pvzmod::LandAllChooserFlights(screen);
        if (pvzmod::HandleChooserExtensionClick(screen, x, y)) return;
    }
    using Fn = void(__thiscall*)(void*, int, int, int);
    reinterpret_cast<Fn>(g_originalSeedChooserMouseDown)(screen, x, y, clickCount);
}

extern "C" int __stdcall FindSeedInBankReplacement(void* screen, const int bankIndex) {
    auto* chosen = static_cast<std::uint8_t*>(screen) + 0xA4;
    for (int seed = 0; seed < pvzmod::kOriginalPlantTypeCount; ++seed) {
        const auto* record = chosen + seed * 0x3C;
        if (*reinterpret_cast<const int*>(record + 0x24) == 1 &&
            *reinterpret_cast<const int*>(record + 0x28) == bankIndex) {
            return seed;
        }
    }
    const pvzmod::CustomPlantDefinition* definition = nullptr;
    {
        std::lock_guard lock(pvzmod::g_chooserMutex);
        pvzmod::ChooserRuntimeState& state = pvzmod::StateForLocked(screen);
        const auto it = std::find_if(state.cards.begin(), state.cards.end(), [bankIndex](const auto& item) {
            return item.state == pvzmod::kInBank && item.bankIndex == bankIndex;
        });
        if (it != state.cards.end()) definition = it->definition;
    }
    if (!definition) return -1;
    auto* record = static_cast<std::uint8_t*>(screen) + 0xA4 + 49 * 0x3C;
    *reinterpret_cast<int*>(record + 0x20) = 49;
    *reinterpret_cast<int*>(record + 0x24) = 1;
    *reinterpret_cast<int*>(record + 0x28) = bankIndex;
    *reinterpret_cast<int*>(record + 0x34) = definition->id;
    return 49;
}

extern "C" void __stdcall MapChooserDrawPacket(int* seedType, int* marker) {
    if (!seedType || !marker) return;
    if (const pvzmod::CustomPlantDefinition* plant = pvzmod::FindCustomPlantByLogicalId(*marker)) {
        *seedType = plant->templatePlantId;
    }
}

extern "C" unsigned long long __stdcall MapChooserPacketValues(const int seedType, const int marker) {
    if (const pvzmod::CustomPlantDefinition* plant = pvzmod::FindCustomPlantByLogicalId(marker)) {
        return static_cast<unsigned int>(plant->templatePlantId) |
               (static_cast<unsigned long long>(static_cast<unsigned int>(plant->id)) << 32U);
    }
    return static_cast<unsigned int>(seedType) |
           (static_cast<unsigned long long>(static_cast<unsigned int>(marker)) << 32U);
}

extern "C" int __stdcall ResolveSeedSlotCount(const int originalCount) {
    return pvzmod::ConfiguredSeedSlotCount(originalCount);
}

extern "C" void __declspec(naked) FindSeedInBankDetour() {
    __asm {
        push dword ptr [esp + 4]
        push esi
        call FindSeedInBankReplacement
        ret 4
    }
}

extern "C" void __declspec(naked) DrawSeedPacketDetour() {
    __asm {
        push ecx
        lea eax, [esp + 20]
        lea edx, [esp + 24]
        push edx
        push eax
        call MapChooserDrawPacket
        pop ecx
        jmp dword ptr [g_originalDrawSeedPacket]
    }
}

extern "C" void __declspec(naked) SetPacketTypeDetour() {
    __asm {
        push edx
        push edi
        call MapChooserPacketValues
        mov edi, eax
        jmp dword ptr [g_originalSetPacketType]
    }
}

extern "C" void __declspec(naked) GetNumSeedsInBankDetour() {
    __asm {
        call dword ptr [g_originalGetNumSeedsInBank]
        push eax
        call ResolveSeedSlotCount
        ret
    }
}
