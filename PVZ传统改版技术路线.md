# Plants vs. Zombies 1.0.0.1051 传统改版技术路线

## animation-studio-dev 原版动画全量审计与完整编辑历史

动画制作器的预览已按原版 Reanimation 语义拆分为独立模块：`ReanimationRenderMath` 负责矩阵、左上注册点、图片子帧和透明度，`ActionViewService` 负责只显示选中动作实际影响的轨道/关键帧，`EntityPreviewProfileService` 负责原版实体的附属动作组合和可选装备过滤。原版 JPG 与同名 `_.png` 灰度蒙版由资源服务合成透明图片，避免 Boss 等大图被错误显示成不透明色块。

编辑历史由 `EditHistoryService` 和工程深拷贝服务维护，不再只记一步，也不设 50 步之类的静默截断。画布移动/旋转/缩放、K 帧、补间、插入/删除帧、轨道/动作增删、实体属性、动作参数、FPS 和图片导入全部进入同一会话历史；`Ctrl+Z` 连续撤销，`Ctrl+Y` 或 `Ctrl+Shift+Z` 按原顺序连续恢复，撤销后新编辑会建立新分支。

当前已逐项审计 `compiled/reanim` 中 48 个独立植物动画和 38 个僵尸/僵尸效果动画，全部引用图片均可解析；143 个原版 compiled 文件通过读取、重打包、Raw 导出和再次读取回归。完整清单、特殊组合及 `LawnMoweredZombie` 等预期空资源见 `modding/PvZAnimationStudio/ORIGINAL_ASSET_AUDIT.md`。

编辑器工作区采用可序列化二叉拆分树。每个叶区域可在动画视图、时间轴、曲线编辑器、资源浏览器和属性检查器之间切换，分隔线保存比例并随窗口自适应；区域角落或横/纵拆分按钮可继续分区，独立窗口共享同一工程和撤销历史。内置动画制作、双动画视图、双时间轴、曲线动画和专注动画预设，布局随工程传递。

工程默认保存为 `.pvza` 单文件容器：`project.json` 保存全部轨道、逐帧 Transform、曲线关键点/插值/Bezier 手柄、动作、事件、实体属性和工作区；`assets/` 保存所有引用图片。原版 JPG+遮罩会先合成透明 PNG，图片 `cols/rows` 同步保存。曲线编辑后同步烘焙为原版逐帧 Transform，所以 Raw/compiled 不需要理解编辑器手柄。加载端限制条目数、单图与总大小并阻止路径穿越。旧 `.pvza.json` 可读取，但再次保存应升级为 `.pvza`。compiled 打开由 `DEADFED4` Cookie 优先识别，因此不再依赖文件必须位于 `compiled/reanim` 或必须拥有精确扩展名。

动画视图区直接接受外部 PNG/JPG/JPEG 文件拖放。落点先按视图平移与缩放逆变换成 Reanimation 世界坐标，再根据图片单帧尺寸居中；每张图建立独立轨道并在当前帧写入 `x/y/sx/sy/f/a/i`，多图使用轻微偏移避免完全重叠。资源绑定与轨道创建放在同一编辑事务中，单次 `Ctrl+Z` 即可完整撤销。

## 0.10.1-dev 原版 compiled Reanimation 读取

外部动画入口现在同时接受 Raw `.reanim` 和原版 32 位 PC `.reanim.compiled`。compiled 解码器验证 `0xDEADFED4`、zlib 解压长度、`0xB393B4C0` Schema 以及 16/12/44 字节的 Definition/Track/Transform 缓存结构，忽略文件中的旧指针并重建安全的 DLL 侧动画定义。配置可以直接引用 `compiled/reanim/Blover.reanim.compiled`，自制 compiled 仍必须分类放入 `pvzmod/animations/`。

## 0.10.0-dev 外部动作资源与独立实体基础

新增 `pvzmod/config/resources/animations.jsonc` 和 `pvzmod/animations/` 分类目录。第一阶段已实现 Raw `.reanim` 的受限解析、动作轨道、循环、播放速度、混合帧、帧事件、定位轨道和外部贴图 ID 交叉校验，并在 DLL 启动时建立只读注册表；尚未安装 Reanimation Definition 注入 Hook，因此当前不会替换游戏内动画。

真正新增植物和僵尸采用“原版 Plant/Zombie 对象池载体 + DLL 侧独立逻辑 ID、外部 Definition、行为控制器和 Mod 存档”的路线。载体只用于保持 Board 遍历、碰撞和回收安全，不再提供名称、属性、图片、动作或攻击逻辑；暂不扩大原版固定 SeedType、ZombieType 和 ReanimationType 数组。完整制作流程、JSONC 契约、安全限制和后续 ABI 注入方案见 `modding/EXTERNAL_ANIMATION_AND_CUSTOM_ENTITIES.md`。

## 0.9.0 设计契约：精英僵尸、技能接口与外部贴图

精英僵尸使用数字 `runtimeId` 和字符串 `id` 双重标识，生成后以 `Zombie* + instanceId` 保存在 DLL 侧挂表；寄存器只负责 Hook 边界的临时传递，不能长期保存精英编号。首个 `RAGE` 规则让普通僵尸按确定性概率成为红色狂暴怪，并通过 `BERSERK` 技能增加生命、速度和攻击。

外部贴图统一登记在 `pvzmod/config/resources/textures.jsonc`。ID 支持英文字母、数字与下划线，例如 `KILL`；路径必须位于 `pvzmod/images/`，例如 `pvzmod/images/zi/kill.png`。资源模块通过原版图片加载器建立通用缓存，植物、僵尸、卡片、UI 和以后其他系统均使用同一查询接口。完整字段、安全边界和模块接口见 `modding/ELITE_AND_TEXTURE_DESIGN.md`。

## 0.8.3 已实现：真正的候选卡翻页

自定义页不再采用“先画原版卡，再盖一层背景”的做法。运行时对原版 `SeedChooserScreen::Draw` 设置区域裁剪：只保留顶部已选卡槽和底部按钮，原版候选网格完全不进入该帧；随后从原版选卡背景资源合成干净网格，并绘制当前页的独立逻辑卡片。返回第 0 页时恢复原版卡坐标，并重新使用完整原版绘制和交互路径。

自定义卡的 `name`、`description`、`cost` 等配置同时用于选卡悬停提示和关卡内种子包提示，不再显示模板植物的名称或介绍。

## 0.8.2 已实现：原版式卡片动画与悬停手型

自定义卡选入、撤回和顶部卡片补位已经改成独立的 `ChosenSeed` 等价状态机：选入/撤回持续 25 帧，补位持续 15 帧，并沿用原版 `CURVE_EASE_IN_OUT` 的双重 SmoothStep 曲线。点击过程中 `mSeedsInFlight` 会先升高，落位后归零，不再直接瞬移。

卡槽容量现在从 `Board::mSeedBank->mNumPackets` 读取；此前误读 `SeedChooserScreen::mNumSeedsToChoose`，该字段在普通选卡中可为 0，是“偶尔点击无效”的直接原因。点击入口还会按真实卡片状态收敛飞行动画，清除没有对应飞行卡的陈旧计数。

自定义页会把原版候选卡暂时移出命中区域；0.8.3 起进一步通过绘制区域分流彻底禁止原版候选卡进入自定义页。返回原版页再恢复位置。自定义候选卡、顶部自定义卡与翻页按钮均加入原版每帧手型光标判定。

## 0.8.1 已修复：分页遮罩与自定义卡交互

这一版曾使用不透明遮罩解决残影；0.8.3 已替换为真正的绘制分流，旧遮罩方案不再使用。翻页按钮位于“摇滚”按钮右侧的完整可见区域。

选卡状态改为由 `SeedChooserScreen` 析构 Hook 明确清理。撤下顶部原卡不会再误重置分页；卡槽未满时点击自定义卡会把它作为独立逻辑卡加入顶部，凑满后可以正常开始关卡。默认 `ui/seed_chooser.jsonc` 不写 `slotCount`，因此保留每一关原版卡槽数量，不再默认扩成 10 格。

`FindSeedInBank` 必须整体替代，不能经 MinHook trampoline 回到原函数：1051 版该函数内部会反向跳到入口第 5 个字节，入口被覆盖后会落到 `0xFB` 并触发特权指令异常。本规则作为后续 Hook 内部回跳审查项。

## 0.8.0 已实现：选卡分页、卡槽扩展与独立逻辑植物

配置分为两个文件，继续遵守“按模块分类、根目录不散放 JSON”的规则：

- `pvzmod/config/ui/seed_chooser.jsonc`：可选设置上方实际携带卡槽，合法范围为 6-10；当前模板未启用该字段，保留每关原版数量。
- `pvzmod/config/plants/custom_plants.jsonc`：定义自定义植物。第 0 页显示原版卡，第 1 页起每页 40 张，最多读取 512 张；“一起摇滚吧！”右侧使用原版商店下一页图标循环翻页。

自定义植物 ID 从 1000 开始，与原版 0-48 分离。卡片默认解锁，选择后将 `templatePlantId` 与自定义逻辑 ID 一起写入种子包；前者只复用原版动画、动作和目标选择，后者负责查找自己的价格、冷却、生命、射速、首发延迟、连发数、子弹类型和伤害。因此原版豌豆射手和自定义豌豆套壳可以同时选用，且数值互不覆盖。

当前攻击模式支持 `projectile` 和 `template`。`projectile` 会给该植物发出的每个子弹实例记录独立伤害；`template` 完全沿用模板攻击。真正的新动画、名字字体渲染、特殊攻击和图鉴页属于下一层资源/行为适配，不应通过继续扩大原版枚举或把所有分支堆进一个总 Hook 来实现。

## 1. 文档目标

本项目按照传统 PC 版 PVZ 改版方式实施：保留原版 `PlantsVsZombies.exe` 作为游戏主体，通过资源替换、EXE 二进制补丁和 32 位 DLL Hook 扩展玩法，不改用重制引擎。

主要目标：

- 新增植物、僵尸和卡片。
- 修改植物与僵尸属性。
- 为原版僵尸生成精英变体。
- 支持僵尸倍率、关卡出怪表和难度配置。
- 为每个大关设计 Boss。
- 扩展地图、背景或关卡。
- 修改原版 UI，新增按钮、设置页和界面。
- 增加 Mod 独立设置及扩展存档。

## 2. 目标程序基线

当前目标文件：`H:\pvz\PlantsVsZombies.exe`

| 项目 | 当前值 |
| --- | --- |
| 游戏版本 | 1.0.0.1051 |
| 程序架构 | 32 位 x86 |
| SHA-256 | `678016E417CD55F1F19D7EABB6F2B7E6B87146BE107DB1179A61985078004F7A` |
| PE 时间戳 | `0x49ECF563` |
| 默认映像基址 | `0x00400000` |
| 重定位 | 已移除，1051 版绝对地址可作为版本专用地址使用 |

补丁器必须同时检查版本、SHA-256、PE 时间戳和每个补丁点的原始字节。任何一项不匹配都必须拒绝修改，不能尝试强行套用地址。

当前安装中存在 `PVZ_60 / MaxTime=60` 相关试玩配置。本项目只修改玩法、资源和 UI，不修改 `PlantsVsZombies.dat`，也不处理 DRM、授权或试玩时间限制。

## 3. 总体架构

```text
原版 PlantsVsZombies.exe
        │
        ▼
离线补丁器：校验版本、备份原文件、应用补丁
        │
        ▼
改版 PlantsVsZombies.exe ──启动加载──> pvzmod.dll
        │                                  │
        │                                  ├─ 植物与僵尸逻辑
        │                                  ├─ 精英与 Boss 系统
        │                                  ├─ 关卡与出怪系统
        │                                  ├─ 卡片与 UI Hook
        │                                  ├─ 设置与扩展存档
        │                                  └─ 日志及崩溃定位
        │
        └─ images / reanim / compiled / sounds / LawnStrings 等资源
```

EXE 内只保留：

1. 加载 `pvzmod.dll` 的引导代码。
2. 无法通过运行时 Hook 实现的固定容量或边界补丁。
3. 改版版本标记。

复杂逻辑放在 `pvzmod.dll` 中，以便按配置启用、快速回滚并记录错误。最终发行的仍然是经过传统二进制修改的 `PlantsVsZombies.exe`，不是外部重制程序。

## 4. EXE 补丁原则

### 4.1 离线补丁器

补丁器执行以下步骤：

1. 检查目标必须是受支持的 1051 版。
2. 第一次运行时生成 `PlantsVsZombies.original.exe` 备份。
3. 检查补丁位置的预期字节。
4. 为加载器增加代码段，或在安全代码洞写入启动 Stub。
5. 让启动流程调用 `LoadLibraryA("pvzmod.dll")`。
6. 保存补丁记录和恢复信息。

每个补丁使用清单描述：

```json
{
  "name": "load_pvzmod_dll",
  "rva": "待反汇编确认",
  "expected_bytes": "原始字节",
  "patched_bytes": "修改字节",
  "description": "启动时加载 pvzmod.dll"
}
```

### 4.2 运行时 Hook

DLL 使用 x86 Hook 接管指定游戏函数。每个 Hook 必须确认：

- 函数入口和覆盖指令长度。
- `thiscall`、`stdcall` 或 `cdecl` 调用约定。
- `ECX` 是否为对象指针。
- 原函数返回值的位置。
- 被覆盖指令和寄存器、标志位的恢复方式。
- 不同场景退出时的状态清理。

建议使用 MinHook 管理普通函数入口 Hook；无法直接 Hook 的内部代码分支再使用版本专用裸汇编跳板。

## 5. 1051 版首批地址

以下地址来自 1.0.0.1051 研究资料，正式使用前仍要对当前 EXE 的机器码逐项复核：

```cpp
constexpr uintptr_t LawnAppGlobal   = 0x006A9EC0;
constexpr uintptr_t PutPlant        = 0x0040D120;
constexpr uintptr_t PutZombie       = 0x0042A0F0;
constexpr uintptr_t PutZombieInRow  = 0x0040DDC0;
constexpr uintptr_t PickBackground  = 0x0040A160;
constexpr uintptr_t PickZombieWaves = 0x004092E0;
constexpr uintptr_t PlayMusic       = 0x0045B750;
constexpr uintptr_t SyncProfile     = 0x0044A320;
```

已知 Board 相关偏移可用于定位僵尸数组、植物数组、出怪列表、场景、阳光和游戏时钟。地址统一集中在 `address_1051.h`，业务代码中禁止散落裸地址。

## 6. 精英僵尸系统

### 6.1 核心构想

精英僵尸不是完全独立的新僵尸种类，而是已有僵尸实例在生成时获得一个特殊的精英编号。编号决定该实例获得的属性倍率、技能、视觉效果和奖励。

示例：

```cpp
enum class EliteId : uint16_t {
    Normal       = 0,
    Armored      = 1,
    Berserker    = 2,
    Frost        = 3,
    Commander    = 4,
    Regenerator  = 5
};
```

编号 `0` 永远表示普通僵尸。其他编号由配置表映射到精英模板。

### 6.2 “在寄存器写编号”的实际落地

生成 Hook 中可以使用 `EAX`、`EDX` 等寄存器临时传递精英编号，也可以从原函数的返回寄存器取得新生成僵尸的指针，但寄存器不是僵尸对象的一部分。函数返回后寄存器会立即被其他代码复用，所以不能仅靠寄存器长期保存编号。

正确流程是：

```text
调用原版生成函数
        │
        ├─ 从返回值、对象数组新槽位或构造 Hook 获得 Zombie*（待反汇编确认）
        │
        ├─ 在寄存器/局部变量中计算 EliteId
        │
        └─ 立即把 Zombie* 与 EliteId 写入持久状态表
```

也就是说，寄存器负责“生成瞬间传递编号”，状态表负责“僵尸存活期间保存编号”。这保留了原构想，同时避免编号在后续帧丢失。

### 6.3 推荐存储方案：DLL 侧挂状态

第一阶段不直接扩大原版 `Zombie` 结构体，而是在 DLL 中建立与僵尸实例绑定的状态：

```cpp
struct EliteState {
    EliteId id = EliteId::Normal;
    uint32_t spawnSerial = 0;
    float baseMaxHealth = 0.0f;
    float baseSpeed = 0.0f;
    int skillCooldown = 0;
    int phase = 0;
    uint32_t flags = 0;
};

std::unordered_map<void*, EliteState> g_eliteStates;
```

键是原版 `Zombie*`，值是精英编号和技能状态。

必须处理原版对象池复用：僵尸死亡或关卡结束时删除记录；新僵尸占用相同地址时使用新的 `spawnSerial`，防止继承前一只僵尸的精英状态。

如果后续反汇编确认 `Zombie` 中存在不会被原版使用、保存或清零覆盖的安全字段，可以考虑把 `EliteId` 写入该字段，以减少查表。但在完成所有读写交叉引用检查前，不能随意占用未知字段。

### 6.4 生成流程

```cpp
Zombie* OnZombieCreated(Zombie* zombie, int zombieType, int row) {
    if (zombie == nullptr) {
        return nullptr;
    }

    const EliteId eliteId = EliteSelector::Select({
        .zombieType = zombieType,
        .row = row,
        .wave = GetCurrentWave(),
        .level = GetCurrentLevel(),
        .difficulty = GetDifficulty()
    });

    if (eliteId != EliteId::Normal) {
        EliteState state{};
        state.id = eliteId;
        state.spawnSerial = NextSpawnSerial();
        state.baseMaxHealth = ReadZombieMaxHealth(zombie);
        state.baseSpeed = ReadZombieSpeed(zombie);
        g_eliteStates.insert_or_assign(zombie, state);
        ApplyEliteInitialStats(zombie, state);
        AttachEliteVisual(zombie, state);
    }

    return zombie;
}
```

这里的 `Zombie*` 获取方式和读写函数需要根据 1051 版反汇编结果确定，伪代码不代表可直接编译的对象结构。

### 6.5 精英选择规则

精英编号可由以下来源决定，优先级从高到低：

1. 关卡出怪表显式指定的精英编号。
2. Boss 或特殊事件强制召唤。
3. 当前波次的保底精英规则。
4. 按僵尸类型、关卡和难度计算的随机概率。
5. 普通僵尸，即 `EliteId::Normal`。

配置示例：

```json
{
  "eliteTypes": {
    "1": {
      "name": "重甲",
      "healthMultiplier": 2.5,
      "speedMultiplier": 0.85,
      "damageMultiplier": 1.25,
      "skills": ["damage_reduction"],
      "auraColor": "#D6B15C"
    },
    "2": {
      "name": "狂暴",
      "healthMultiplier": 1.5,
      "speedMultiplier": 1.45,
      "damageMultiplier": 1.8,
      "skills": ["enrage_on_low_health"],
      "auraColor": "#E34B42"
    }
  },
  "spawnRules": [
    {
      "level": "1-10",
      "zombieTypes": [0, 2, 3],
      "eliteIds": [1, 2],
      "chance": 0.08,
      "maxAlive": 2
    }
  ]
}
```

随机数应从关卡种子和生成序号派生，保证同一个关卡存档恢复后的精英结果可重复，而不是直接使用不可复现的系统时间随机数。

### 6.6 属性修改

生成时保存原始基础值，再按精英模板计算最终值：

```text
最终生命 = 基础生命 × 关卡倍率 × 精英生命倍率
最终速度 = 基础速度 × 难度倍率 × 精英速度倍率
最终伤害 = 基础伤害 × 僵尸倍率 × 精英伤害倍率
```

不能在每帧更新中对已经修改过的数值继续相乘，否则数值会指数膨胀。重新计算时始终从 `baseMaxHealth`、`baseSpeed` 等基础快照开始。

### 6.7 精英技能 Hook 点

| 事件 | 用途 |
| --- | --- |
| 僵尸生成/初始化 | 分配编号、属性和特效 |
| Zombie Update | 执行冷却、光环、恢复和阶段技能 |
| 僵尸攻击 | 修改伤害或触发特殊攻击 |
| 僵尸受到伤害 | 护盾、减伤、反伤、低血量狂暴 |
| 移动速度计算 | 减速免疫、冲刺、光环加速 |
| Draw/Reanimation | 精英光圈、颜色、头顶图标 |
| 僵尸死亡 | 掉落、爆炸、召唤及状态表清理 |
| Board 退出/重开 | 清空全部侧挂状态 |

第一批建议只实现三种精英：重甲、狂暴、指挥官。先验证属性、事件和视觉三条链路，再扩展复杂技能。

### 6.8 精英状态查询接口

其他模块不直接访问全局 Map，统一调用接口：

```cpp
EliteState* FindEliteState(Zombie* zombie);
EliteId GetEliteId(Zombie* zombie);
bool IsElite(Zombie* zombie);
bool HasEliteSkill(Zombie* zombie, EliteSkill skill);
void RemoveEliteState(Zombie* zombie);
void ClearEliteStates();
```

这样将来把侧挂表切换成结构内字段时，上层技能代码不需要重写。

## 7. 植物与僵尸属性系统

属性配置分为三层：

1. 原版基础值。
2. 全局或当前难度倍率。
3. 关卡、精英、Boss 或临时 Buff 修正。

简单属性可在构造时修改，动态属性通过 Getter 或具体行为函数 Hook。不要为了方便在多个位置重复写同一个裸地址。

## 8. 关卡与出怪系统

优先 Hook `PickZombieWaves`，在原版生成波次后替换或调整出怪表。关卡配置应支持：

- 每波僵尸类型和数量。
- 指定行或随机行。
- 生成延迟。
- 固定精英编号。
- 精英概率和同时存活上限。
- 僵尸属性倍率。
- 旗帜波、保底波和 Boss 波。
- 场景、背景、音乐和初始阳光。

第一阶段复用原版场景和网格。新增背景、天气及行类型比修改网格拓扑安全。真正改变行列数量、寻路或碰撞系统放到后期。

## 9. 新植物、新僵尸和卡片

分两个阶段：

### 9.1 变体阶段

- 复用已有或可占用的类型槽位。
- 通过 DLL 状态标识新逻辑。
- 使用独立动画、卡片和文本资源。
- Hook 初始化、攻击、更新和绘制行为。

这种方式可以较快完成“一个有独立技能的新植物”和“一个有独立技能的新僵尸”。

### 9.2 真正扩容阶段

增加原生类型总数需要统一检查并修改：

- 类型数量常量。
- 数组长度和内存分配。
- 所有边界判断。
- switch 跳转表。
- 选卡和图鉴。
- 卡片冷却、阳光和提示文本。
- 存档序列化。

在完成全局交叉引用清单前，不直接修改类型上限。

## 10. Boss 系统

每个大关 Boss 由 DLL 内的控制器管理，不要求扩展原版僵尸结构：

```cpp
struct BossController {
    int bossId;
    int phase;
    int health;
    int maxHealth;
    int actionCooldown;
    uint32_t flags;
};
```

控制器负责阶段切换、召唤僵尸、场地技能、血条、音乐、胜利判定和死亡演出。第一版可以复用现有僵尸或僵王演员，再逐步加入独立动画。

## 11. UI、按钮和设置

UI 分成两类：

- 资源层修改：按钮图片、字体、卡片、背景、提示文字和动画。
- 代码层修改：新增 Widget、鼠标事件、翻页、设置窗口和界面状态。

新卡片较多时，为选卡界面增加分页或分类按钮，不应强行把所有卡片挤进原版固定区域。坐标首先保持原版 800×600 逻辑坐标，避免过早改动整个布局系统。

## 12. 设置与存档

原版存档保持不变，扩展数据单独保存：

```text
pvzmod_settings.json
pvzmod_profile_1.dat
pvzmod_progress_1.dat
```

可在原版 `SyncProfile` 周围挂接扩展保存生命周期，但不扩大原版 Profile 结构，也不向未知偏移写入 Mod 数据。

扩展存档至少记录：

- Mod 配置版本。
- 已解锁植物、僵尸图鉴和关卡。
- 难度与倍率。
- Boss 进度。
- 精英图鉴或统计。
- 兼容迁移版本号和校验值。

## 13. 资源管线

| 资源 | 处理方式 |
| --- | --- |
| PNG/JPG/GIF | 直接替换或新增资源定义 |
| `reanim.compiled` | 使用 PopStudio 转换、编辑和重新编译 |
| 粒子、Trail | 转换后编辑，重新编译 |
| OGG 音频 | 保留格式和合适采样参数 |
| 文本 | 修改 `properties/LawnStrings.txt` 并增加 Mod 文本表 |
| PAK | 开发期优先使用当前解包资源；发布期再决定是否重打包 |

## 14. 推荐工程结构

```text
H:\pvz\modding\
├─ patcher\
│  ├─ patch_manifest.json
│  └─ src\
├─ loader\
├─ runtime\
│  ├─ address_1051.h
│  ├─ hooks\
│  ├─ elite\
│  ├─ plants\
│  ├─ zombies\
│  ├─ bosses\
│  ├─ levels\
│  ├─ ui\
│  ├─ save\
│  └─ logging\
├─ content\
│  ├─ config\
│  ├─ plants\
│  ├─ zombies\
│  ├─ levels\
│  └─ ui\
├─ tools\
├─ tests\
└─ dist\
```

## 15. 实施里程碑

### M0：反汇编与基线

- 备份 EXE 和用户存档。
- 建立 Ghidra 数据库和 1051 地址文件。
- 验证社区地址的机器码及调用约定。
- 创建补丁清单和原始字节检查器。

### M1：EXE 加载器

- 补丁后的 EXE 能加载 `pvzmod.dll`。
- DLL 只记录启动、关卡进入和退出日志。
- 验证正常启动、游玩、退出和保存。

### M2：属性与出怪

- 植物、僵尸属性读取配置。
- 实现全局与关卡僵尸倍率。
- 接管一个测试关卡的出怪表。

### M3：精英垂直切片

- Hook 僵尸生成并分配 `EliteId`。
- 完成侧挂状态、对象池复用清理。
- 实现重甲、狂暴、指挥官三种精英。
- 增加精英图标或光圈和调试日志。

### M4：新增单位

- 一个新植物及其卡片、动画和技能。
- 一个新僵尸及其动画和行为。

### M5：UI 与存档

- 新增主菜单或关卡内按钮。
- 新增设置页、卡片分页和 Mod 独立存档。

### M6：Boss 与地图

- 完成一个大关 Boss 的多阶段战斗。
- 新增背景、音乐和场景规则。
- 评估是否需要真正修改地图网格。

## 16. 调试与安全要求

- 所有功能均有配置开关。
- DLL 写入 `logs/pvzmod.log`，记录版本、Hook 安装和异常阶段。
- 每个 Hook 安装前检查入口字节。
- 发生不兼容时不继续写内存。
- 关卡退出时清理精英、Boss、植物和 UI 侧挂状态。
- 不直接覆盖唯一的原始 EXE。
- 不修改原版存档前先创建备份。
- 调试构建显示僵尸指针、生成序号、原类型和 `EliteId`，发行构建关闭调试覆盖层。

## 17. 开源参考

- [pvztools：Plants vs. Zombies 1.0.0.1051 Toolset](https://github.com/lmintlcx/pvztools)
- [pvztoolkit：多版本 PVZ 工具及 1051 地址适配](https://github.com/lmintlcx/pvztoolkit)
- [pvztoolkit data.cpp：版本地址与偏移](https://github.com/lmintlcx/pvztoolkit/blob/master/src/data.cpp)
- [pvztoolkit code.cpp：运行时汇编代码](https://github.com/lmintlcx/pvztoolkit/blob/master/src/code.cpp)
- [pvztoolkit pvz.cpp：游戏调用和内存修改实现](https://github.com/lmintlcx/pvztoolkit/blob/master/src/pvz.cpp)
- [CrackPVZ：1051 版 PAK 与 Hook 参考](https://github.com/Wanxiaace/CrackPVZ)
- [PopStudio：PVZ 动画及资源格式工具](https://github.com/PopGameTool/PopStudio)
- [MinHook：Windows x86/x64 Hook 库](https://github.com/TsudaKageyu/minhook)

引用开源项目代码前需要核对许可证。特别是 pvztoolkit 使用 GPL-3.0；如果直接复制其代码到可发布组件，需要按许可证履行相应义务。地址和行为也应在本项目的目标 EXE 上独立验证并记录来源。

## 18. 当前实现状态：外部出怪权重配置

已完成第一项可运行功能，工程位于 `H:\pvz\modding`：

- `pvzmod/config/levels/spawn.json` 使用 `levels` 对象保存冒险关卡的稀疏覆盖。
- 只有 JSON 中显式出现且 `enabled` 不为 `false` 的关卡会被接管。
- 未出现的关卡完整保留原版出怪逻辑。
- Hook 原版 `PickZombieWaves` 后保留原波数、每波有效槽位数量和节奏，只按外部权重重新选择僵尸类型。
- 只修改游戏当前记录的实际波数，不处理未启用的 20 波缓存区域。
- 每个权重项可选 `minimumCount`，用于保证低出怪量关卡至少出现指定数量；省略时保持纯权重随机。
- 同一关卡和种子生成相同结果，避免选卡预览与实际出怪不一致。
- 默认保留旗帜、雪人、蹦极、小鬼和僵王等原版特殊放置槽位。
- JSON 修改时间发生变化后，会在下一次生成关卡出怪表时自动重载；无效 JSON 不会覆盖上一次有效配置。
- 运行日志写入 `H:\pvz\pvzmod\logs\pvzmod.log`。
- 自动测试覆盖稀疏关卡、确定性权重、波次终止符和特殊槽位保留。

当前根目录 `PlantsVsZombies.exe` 已添加 `.pvzm` 加载段，原版文件备份为 `PlantsVsZombies.original.exe`。加载器只加载同目录的 `pvzmod.dll` 并返回原入口，不修改 DRM 或存档逻辑。

## 19. 外部文件目录规范

游戏根目录只保留必须由 EXE 直接加载的 `pvzmod.dll`。所有 JSON 配置统一放入 `pvzmod/config` 并按模块分类：

```text
pvzmod/
├─ config/
│  ├─ levels/       # 关卡、波次、出怪、地图
│  ├─ plants/       # 植物属性、技能、卡片
│  ├─ zombies/      # 僵尸属性与行为
│  ├─ elites/       # 精英编号、倍率与技能
│  ├─ bosses/       # Boss 阶段与技能
│  ├─ ui/           # UI、按钮和界面
│  ├─ settings/     # 全局设置与难度
│  └─ schemas/      # 配置格式及版本校验
├─ saves/           # Mod 独立存档
└─ logs/            # 运行日志
```

以后新增 JSON 时，必须先确定所属模块并放进对应目录；禁止再次直接放到游戏根目录，也禁止把运行产生的存档、缓存或日志混入 `config`。每个配置文件使用小写英文文件名并带 `schemaVersion`，一个文件只负责一个模块。

## 20. 当前实现状态：波次僵尸数量倍率

已新增 `pvzmod/config/levels/wave_multipliers.json`，用于调整僵尸出怪数量。倍率分为三层并使用乘法合成：

```text
最终波次倍率 = 全局数量倍率 × 当前关卡倍率 × 当前波次倍率
```

实现约束：

- 只修改实际启用波次，不改变波数、刷新时间和旗帜波位置。
- 每波数量四舍五入，原本非空的波次至少保留 1 只，最多 50 只。
- 放大时从该波已有类型中追加，缩小时确定性抽样。
- 默认保护特殊放置僵尸，不复制或删减旗帜、雪人、蹦极、小鬼和僵王。
- 数量调整在僵尸类型权重处理之后执行，因此新增槽位会从最终类型池中复制。
- 调整结束后重建选卡界面的僵尸类型预览。
- 全局配置对所有冒险关卡生效；关卡和波次配置保持稀疏，不出现的条目使用 `1.0`。
- 配置支持修改时间检测，无效 JSON 保留上一次有效配置并记录错误日志。

数量倍率与以后要实现的生命、移动速度、攻击属性倍率是两个独立模块，配置字段不混用。

## 21. 当前实现状态：全局配置和阳光价值

已新增 `pvzmod/config/settings/global.json` 作为全局配置入口。文件按功能域分组，当前 `economy.sunPickupValues` 支持：

- `normal`：普通阳光价值。
- `small`：小型阳光价值。
- `large`：大型阳光价值。

1051 版的 `Coin::GetSunValue` 位于 `0x004329A0`，但真正入账的 `Coin::ScoreCoin`（`0x004309D0`）内联了原版 25/15/50 判断。因此实现同时 Hook 两处：`GetSunValue` 修正正在收集中的预计值，`ScoreCoin` 记录拾取前阳光并在原版逻辑后校正实际总额。Coin 类型位于对象偏移 `+0x58`，类型 `4/5/6` 分别映射普通、小型和大型阳光；阳光总额继续遵守原版 9990 上限。两个 Hook 安装前均验证目标机器码，配置丢失时使用原版 `25/15/50`，无效配置保留上一次有效值。

全局配置扩展约定：经济类设置继续放入 `economy`；其他功能建立新的顶层功能域。关卡出怪、植物个体、僵尸个体、精英和 UI 等已有独立分类的配置不得混入 `global.json`。

## 22. 当前实现状态：植物攻击伤害稀疏覆盖

已新增 `pvzmod/config/plants/attacks.jsonc`。选择 JSONC 而不是普通 JSON，是为了在每个可编辑攻击键旁保留原版默认伤害和对应植物说明。解析使用 nlohmann/json 的忽略注释模式，配置模型只保存用户真正写出的键；未出现的键不产生覆盖。合法伤害范围为 `0–1000000`，未知键会拒绝新配置，运行时继续持有上一次有效快照。

1051 版实现分为三条技术路径：

1. 投射物：原版 `gProjectileDefinition` 位于 `0x0069F1C0`，每项 12 字节，伤害字段为 `表基址 + projectileId × 12 + 8`。模块保存已核对的原版值，并只改植物投射物 ID 0–8、10、12；僵尸投篮和僵尸豌豆不进入植物配置。删除覆盖键时主动写回原版值，因此热重载不会遗留旧覆盖。西瓜、冰瓜和火球的直接/溅射路径都读取这张表。
2. 直接攻击：Hook `Zombie::TakeDamage`（`0x005317C0`），保留原版非标准寄存器调用约定（`ESI=Zombie*`、`EAX=DamageFlags`、栈参数为伤害），由返回地址识别 `DoRowAreaDamage`、窝瓜、大嘴花强敌咬击、寒冰菇和范围爆炸调用点。`DoRowAreaDamage` 的调用帧中 `EBP` 保持 `Plant*`，读取 `Plant+0x24` 的 `SeedType` 区分大喷菇、忧郁菇、地刺和地刺王；其他来源不修改。
3. 燃烧/爆炸：Hook `Board::KillAllZombiesInRadius`（`0x0041D8A0`）并按调用返回地址标记樱桃、毁灭菇、土豆雷、玉米加农炮和爆炸坚果的线程局部上下文；Hook `Zombie::ApplyBurn`（`0x00532B70`）处理燃烧路径和火爆辣椒。配置仍为 1800 时调用原版燃烧逻辑；配置为其他值时通过原始 `TakeDamage` trampoline 施加普通数值伤害，避免再次进入 Hook。

Hook 安装前分别验证三个入口的 1051 原始机器码。攻击识别仅接受已确认的返回地址，未识别的僵尸攻击、割草机、关卡脚本和其他系统伤害全部原样放行。范围 Hook 使用 `thread_local` 上下文，嵌套调用结束后恢复旧值，避免把一次植物爆炸错误传播到后续伤害。

配置键和原版值：

- 投射物：`pea=20`、`snowPea=20`、`cabbage=40`、`melon=80`、`puff=20`、`winterMelon=80`、`fireball=40`、`star=20`、`spike=20`、`kernel=20`、`butter=40`。
- 行/接触攻击：`fumeShroom=20`、`gloomShroom=20`、`spikeweed=20`、`spikerock=20`、`spikeVehicle=1800`。
- 特殊攻击：`squash=1800`、`chomperStrongTarget=40`、`iceShroom=20`。
- 范围攻击：`potatoMine=1800`、`cherryBomb=1800`、`doomShroom=1800`、`jalapeno=1800`、`cobCannon=1800`、`explodeONut=1800`。

大嘴花普通吞食、缠绕海草拖拽、魅惑菇控制和三叶草吹飞是状态转换或直接处决，不经过可替换的数值伤害；它们在用户文档中明确列出，但不伪造一个无效伤害字段。向日葵等非攻击植物同样不创建字段。模仿者直接继承被模仿植物的攻击路径。

自动测试覆盖 JSONC 注释、投射物与直接攻击两组键的稀疏语义、缺省键不覆盖以及未知键拒绝。示例文件中的 `pea` 是当前可直接修改的测试覆盖，其余键均为带默认值的注释模板；文档不固定用户后续自行调整的实际数值。

## 23. 模块解耦重构与僵尸防具体系

### 23.1 Hook 模块边界

运行时已从单体 `pvz_hook.cpp` 拆分。该文件现在只做 PE 版本检查、MinHook 初始化和模块注册，禁止再加入业务配置类或对象偏移。当前模块为：

- `wave_hook.cpp`：出怪权重与波次数量倍率。
- `sun_hook.cpp`：阳光预计值和实际入账。
- `plant_attack_hook.cpp`：植物投射物表、直接伤害和燃烧路径。
- `zombie_hook.cpp`：僵尸初始化、基础属性、防具和啃食伤害。
- `hook_utils.cpp`：公共 1051 PE 校验、目标机器码检查和 Hook 安装。
- `*_config.cpp`：不访问游戏内存的配置解析、校验和确定性算法。

依赖方向固定为 `启动器 → Hook 模块 → 本模块配置/纯算法 → 公共日志与 Hook 工具`。业务模块之间不能直接读取对方的匿名运行时状态。未来精英、Boss、UI 和存档分别新增模块注册接口，不能继续扩大 `zombie_hook.cpp` 或重新形成总控巨型类。完整约束写入 `modding/ARCHITECTURE.md`。

### 23.2 逆向提示核对结果

已读取 `H:\pvz\一些逆向提示` 中的函数表、创建函数表、加强版指针表和扩僵尸提示，并与 1051 原版反汇编/逆向源码交叉核对：

| 字段/函数 | 1051 地址或偏移 | 用途 |
| --- | ---: | --- |
| `Zombie::ZombieInitialize` | `0x00522580` | 原版完成类型专属初始化后应用稀疏配置 |
| `Zombie::EatPlant` | `0x0052FB40` | 按僵尸类型换算独立啃食伤害 |
| 僵尸类型 | `Zombie+0x24` | 查找 `zombies.<id>` |
| 一级原版头盔槽 | `+0xC4` | 原版 `HelmType`，当前适配路障/铁桶 |
| 本体当前/最大生命 | `+0xC8/+0xCC` | `bodyHealth` 同时写入两处 |
| 头盔当前/最大生命 | `+0xD0/+0xD4` | Mod 头盔防具耐久 |
| 二级原版盾牌槽 | `+0xD8` | 原版 `ShieldType`，当前适配铁门 |
| 盾牌当前/最大生命 | `+0xDC/+0xE0` | Mod 盾牌防具耐久 |
| 僵尸实例 ID | `+0x158` | 防具概率的稳定随机输入 |
| 僵尸对象步长 | `0x15C` | 与提示表一致，本功能不扩大原版对象 |

提示资料把原版 `+0xC4` 称为“一类饰品”、`+0xD8` 称为“二类饰品”。改版配置中的 `tier` 不直接等于这两个原版槽位：用户定义的一级铁桶映射到头盔槽，二级铁门映射到盾牌槽。这样“进阶等级”“装备位置”“原版枚举”三者解耦，未来可定义三级原创防具而不篡改原版 ID 语义。

### 23.3 配置模型

`pvzmod/config/zombies/attributes.jsonc` 包含三个部分：

1. `seed`：全局确定性防具种子。
2. `armorDefinitions`：以 Mod 防具 ID 建立名称、`tier`、视觉适配器和耐久。
3. `zombies`：以僵尸类型 ID 稀疏覆盖 `bodyHealth`、`attackDamage` 和 `armorRolls`。

防具概率使用 `seed、zombieType、Zombie+0x158 实例 ID、armorId` 混合后生成 `[0,100)` 值。`chance=0` 在算法入口直接返回 false，`chance=100` 直接返回 true。多个同槽防具抽中时选择最高 `tier`，同级按较小 ID，避免 JSON 顺序改变结果。头盔与盾牌属于不同槽，可同时存在，仍沿用原版盾牌→头盔→本体的受伤顺序。

当前视觉适配器支持普通僵尸动画骨架上的 `cone`、`bucket`、`door`：头盔通过 `Zombie::ReanimShowPrefix(0x005331C0)` 显示轨道，铁门写入盾牌字段后调用 `Zombie::AttachShield(0x00533000)`。0.6.1 又为普通僵尸 ID 0 增加 `wallnutHead`：初始化前先完成确定性头盔选择，抽中后暂时把传给原版 `ZombieInitialize` 的类型替换为 ID 27，让原版创建并附着 `REANIM_WALLNUT`；返回后恢复 ID 0，再覆盖配置耐久、本体生命和攻击伤害。掉头时仅对被标记实例临时采用 ID 27 的特殊头部清理分支，调用完成后恢复基础类型。这样保留原版裂纹动画与生命周期，又不让普通僵尸长期变成植物头僵尸。

`wallnutHead` 只在它赢得头盔槽竞争时触发。多个头盔定义同时成功仍按最高 `tier`、同级较小 Mod ID 决胜。例如铁桶 1001 与坚果头 3001 同为一级时，铁桶抽中就优先；若要坚果头必定装备，应提高 3001 的 `tier` 或关闭其他头盔概率。对舞王、巨人、冰车等其他动画族不盲目复用该初始化器；运行时跳过并只记录一次兼容性警告。以后新增僵尸或原创防具时，为动画族增加适配器，而不是在配置解析器或总启动器中增加类型分支。

### 23.4 独立攻击伤害

原版 `DAMAGE_PER_EAT` 为 4。`Zombie::EatPlant` 在冰冻奇数帧、特殊植物状态等情况下可能提前返回，因此不能无条件预扣配置伤害。实现先把植物临时生命换算到原版 4 点路径，调用原函数，再按原版实际发生的扣血次数换算为 `attackDamage`；若原版本次跳过啃食则恢复原值。这样保留死亡、吞咽、关卡统计和 I-Zombie 特殊逻辑，同时让不同僵尸拥有独立数值。

该字段只修改啃食植物的伤害，不修改僵尸豌豆、投石车篮球、巨人砸击、僵王技能或被魅惑后的僵尸互咬；这些攻击以后按攻击类型建立独立键，禁止复用一个含义模糊的全局攻击倍率。

### 23.5 精英系统预留

基础配置不把精英编号写入原版未知空隙。下一阶段精英 Hook 在僵尸生成后以 `Zombie* + 实例 ID` 建立侧挂状态，保存 `eliteId`、技能冷却和临时倍率；基础生命、攻击和防具仍复用本节的应用接口。对象池复用或僵尸死亡时清理侧挂状态，避免精英编号遗留给下一只僵尸。

### 23.6 0.5.1 防具初始化崩溃修复

进入 1-2 后的崩溃通过 Windows Application Error 定位为 `pvzmod.dll` 内的 `0xc0000409 / FAST_FAIL_STACK_COOKIE_CHECK_FAILURE`。概率对照进一步确认：铁门概率 100 时第一只普通僵尸稳定触发；关闭铁门后，抽中铁桶时仍可能触发。两者共同经过 `ReanimShowPrefix` / `AttachShield` 裸汇编桥接器。

根因是桥接器内部使用 `ret 12` 和 `ret 4` 清栈，但 C++ 侧采用默认 `cdecl`，返回后调用方再次清栈。0.5.1 把两个桥接器声明为 `__stdcall`，保留 `ret N`，并从 Release 对象反汇编确认各调用后没有重复的 `add esp, N`。最终在铁桶 50%、铁门 100%、1-2 数量倍率开启的配置下越过原固定崩溃点，Windows 事件日志没有产生新的 PvZ 崩溃记录。

## 24. 当前实现状态：完整原版防具生命目录

0.6.0 在 `pvzmod/config/zombies/attributes.jsonc` 增加 `originalArmorHealth`。目录覆盖 1051 版所有实际建立独立生命池的防具：路障 370、铁桶 1100、报纸 150、铁门 1100、橄榄球头盔 1400、雪橇防护 300、气球 20、矿工帽 100、梯子 500、坚果头 1100、高坚果头 2200。对应僵尸 ID 分别为 2、4、5、6、7、13、16、17、21、27、31。

解析器把每个键映射到固定的僵尸类型、生命池和已核对默认值。生成 Hook 在原版 `ZombieInitialize` 完成后运行：配置值等于默认值时不写任何字段，继续使用原版或前置 Mod 的初始化结果；配置值改变时，只覆盖对应的 `HelmHealth/HelmMaxHealth`、`ShieldHealth/ShieldMaxHealth` 或 `FlyingHealth/FlyingMaxHealth`。这套目录不要求 `zombies` 中存在对应 ID，所以只改一个防具值即可生效；删除键也保持原版。

`originalArmorHealth` 只改已有原版防具的耐久，不改变其出现类型和视觉。`armorDefinitions + armorRolls` 继续负责给兼容僵尸随机附加 Mod 防具，二者互不覆盖配置职责。未知目录键、非整数或超范围数值会拒绝本次热加载并保留上一份有效配置。原版 `HelmType` 还保留红眼、头带和独立高坚果枚举，但 1051 初始化代码没有给它们建立独立防具生命池，因此不在可修改目录中伪造默认值。
