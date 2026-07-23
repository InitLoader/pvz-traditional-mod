# 三级可扩展技能与自由行为运行时方案（有限 JSON / Lua / DLL）

> 状态：设计稿，尚未实现。目标版本暂定 `0.11.x`。本文只定义架构、接口和验收顺序，不表示当前 DLL 已经能够加载 Lua。
>
> 当前 `0.10.x` 基础在大量植物、僵尸、子弹、UI、资源与订阅者下的具体瓶颈、容量维度和前置改造见 [`EXTENSION_SCALE_ARCHITECTURE_AUDIT.md`](EXTENSION_SCALE_ARCHITECTURE_AUDIT.md)。该审计是本方案的规模化约束，不应只提高现有 JSON 数量上限后绕过。
>
> 工具职责已经拆分：`PvZAnimationStudio` 只负责动画和基础植物资产骨架；独立 [`PvZLuaStudio`](PVZLUA_STUDIO_DESIGN.md) 负责 Lua 创建、编辑、绑定辅助、验证和错误跳转；[`PvZModManager`](PVZMOD_MANAGER_DESIGN.md) 负责包、配置、Lua 资产元数据、DLL、资源、依赖、安装与回滚，但不显示、验证或打开 Lua 代码。

## 1. 结论

项目应从“JSON 描述所有玩法 + `pvzmod.dll` 预注册每一种技能”升级为 **三级扩展 + 一个核心宿主**：

1. **有限 JSON/JSONC 扩展**：静态数值、资源、绑定，以及“一次事件映射到一个固定效果”的微型功能；不必为了改倍率、播放音效或增加一个固定效果就写 Lua/DLL。
2. **Lua 行为扩展**：技能条件、状态机、组合效果、阶段逻辑、计时器和事件响应。
3. **可信原生 DLL 插件**：补充新的通用能力、高频算法、资源解码器或必须使用原生代码的功能；由 `pvzmod.dll` 从固定目录加载，插件也只能通过版本化宿主 API 注册和调用公共能力。

`pvzmod.dll` 是这三级扩展的核心宿主：负责 1051 版 Hook、实体句柄、事件总线、能力注册表、命令缓冲、配置索引、加载顺序、日志和故障隔离。JSON、Lua、内置 C++ 和外部 DLL 不能各自复制一套游戏 API 或事件系统。

核心原则不是“为每个技能做一个 API”，而是“为一类游戏能力做一个可复用原语”。例如三连发、扇形弹幕、受伤反击和阶段召唤都不应各自写 C++；C++ 只提供 `查询目标`、`生成子弹`、`造成伤害`、`计时` 等能力，Lua 自由组合它们。

Lua 的自由范围止于受控游戏 API。脚本不能取得裸指针、读写任意内存、安装 Hook 或调用任意地址。外部 DLL 虽然拥有进程内原生权限，但正常协作必须走同一个 `PvZModHostApi`；真正需要新底层能力时，先在 C++ 中完成逆向验证和安全封装，再把一个通用能力同时开放给 JSON 微规则、Lua 和其他 DLL。

### 1.1 三级扩展的选择规则

| 需求 | 应使用 | 边界 |
| --- | --- | --- |
| 改固定数值、资源、倍率、概率；单一事件触发一个固定效果 | 有限 JSON | 无循环、无自定义函数、无持久局部状态、无事件链 |
| 有条件、分支、计时、阶段、局部状态或多个能力组合 | Lua | 只调用受控公共 API，不接触地址与 Hook |
| 缺少底层能力、极高频算法、原生库/格式或必须新增 Hook | DLL 插件 | Win32 C ABI、精确版本、可信代码、完整原生风险 |

升级规则固定为 `JSON → Lua → DLL`。如果需求已经出现第二个条件、需要记住上一次状态、需要循环/计时或需要组合多个命令，就停止扩张 JSON Schema，转到 Lua。如果 Lua 只是缺少一个通用底层原语，则增加一次原生 Capability，让所有 Lua/DLL/微规则共用，而不是给某个技能做专用入口。

### 1.2 学习曲线是硬性产品指标

脚本系统不仅要“能够实现复杂技能”，还必须让第一次接触项目的人快速得到正反馈。目标学习曲线固定为：

| 投入时间 | 应达到的能力 | 可验收成果 |
| --- | --- | --- |
| 1 小时 | 会复制模板、修改参数、绑定实体、看懂报错和热重载 | 独立完成一个可进游戏运行的原创小技能 |
| 10 小时 | 熟悉常用事件、查询、状态、计时、生成、伤害、动画和组合方式 | 不改 C++ 完成一组相互配合的植物/僵尸技能 |
| 30 小时 | 几乎掌握脚本玩法层，能设计较大状态机、调试性能、处理热重载和存档迁移 | 独立完成一个精英或 Boss 的多阶段玩法包 |

计时从“开发版运行时、示例和工具已经安装完成”开始，不把下载游戏、安装 Visual Studio、编译 DLL 或逆向新 Hook 算入脚本学习时间。目标用户只需要理解变量、条件和函数等最基础编程概念，不要求会 C++、汇编、内存布局或 Lua 元表。

这里的“30 小时几乎精通”特指 **Lua 玩法层**。新增未知原版能力、确认调用约定和编写裸汇编桥仍属于原生开发路线，不能伪装成新手脚本内容。

为实现这个指标，API 必须采用渐进式暴露：

1. **配方层**：新手使用 `ctx:shoot`、`ctx:every`、`ctx:nearest_enemy`、`ctx:explode` 等安全便捷函数，5–15 行即可完成常见技能。
2. **核心能力层**：熟悉后直接组合 `ctx.world`、`ctx.combat`、`ctx.animation`、`ctx.timer`，实现复杂规则。
3. **原生能力层**：只有脚本层确实缺少通用原语时，原生开发者才扩展 C++ Hook 和 Capability；普通技能作者不接触这一层。

配方层不是继续增加“一技能一 API”。它应主要由随运行时发布的 Lua 标准库组合核心能力实现，例如 `ctx:shoot({count=3})` 最终仍拆成若干条 `SpawnProjectile` 命令。高级作者可以查看其源码、复制并改写，也可以完全绕过配方层使用核心能力。

## 2. 当前限制与可复用基础

当前已有两块可以直接演进的基础：

- `zombie_event_bus.*` 已能把僵尸初始化和啃食伤害修改从基础 Hook 发布给扩展模块。
- `elite_skill_registry.*` 已能按 `Spawn`、`BeforeUpdate`、`AfterUpdate`、`BeforeAttack`、`BeforeDraw`、`AfterDraw`、`Remove` 分发 DLL 内注册的 C++ 回调。

但现有模式仍有四个根本限制：

- JSON 只能表达固定字段，无法自然表达条件、循环、局部状态、组合效果和复杂阶段。
- 每增加一种玩法都要修改 DLL、重新编译并增加一个技能 ID，注册表只是把硬编码分支换了位置。
- 植物、僵尸、子弹、动画和关卡事件没有统一语义，技能容易重新依赖具体 Hook 和对象偏移。
- `EliteSkillContext` 暴露的是某个精英模块所需的少数字段，不能自然复用于普通植物、自定义僵尸、Boss 和关卡控制器。

因此不应继续无限扩张 `EliteSkillContext`，也不应把表达式语言塞进 JSON。现有事件总线和技能注册表应作为迁移入口，最终由统一行为运行时承接。

## 3. 为什么选择 Lua

第一阶段采用 **Lua 5.4 系列**，构建时锁定具体源码版本和校验值。

| 方案 | 优点 | 主要问题 | 结论 |
| --- | --- | --- | --- |
| 有限 JSON 微规则 | 改动小、易校验、无需写代码 | 不能承载状态机和复杂组合 | 保留为第一级扩展 |
| Lua | 体积小、32 位嵌入成熟、语法简单、热重载方便 | 动态类型，必须自己限制资源和宿主接口 | 首选 |
| 无限扩展 JSON/表达式 DSL | 表面上不用写代码 | 很快重新发明函数、状态机和调试器 | 不采用 |
| 运行时编译 C++ | 理论自由度最高 | 依赖编译环境，ABI 和崩溃风险极高 | 不采用 |
| 外部 DLL 插件 | 性能高，可增加原生能力并供其他扩展共用 | 任意内存权限，版本和安全成本高 | 作为第三级可信扩展 |
| JavaScript/Python | 生态丰富 | 对当前 32 位注入 DLL 过重 | 不作为首版 |
| WebAssembly | 隔离更强 | 工具链、宿主绑定和调试复杂 | 可作为未来不可信脚本方案 |

Lua 在这里不是操作系统级安全沙箱。它用于运行用户主动安装、视为可信的本地 Mod 代码；能力白名单、内存上限和指令预算主要用于防止误操作、死循环和普通脚本错误，不能把恶意脚本变成绝对安全的内容。

## 4. 总体结构

```text
PlantsVsZombies.exe 1.0.0.1051
        │
        ▼
pvzmod.dll：版本专用 Hook / 裸汇编桥接
        │  只处理寄存器、栈、原始指针和入口字节
        ▼
语义事件适配器
        │  PlantSpawned / BeforeAttack / Damaged / AnimationEvent ...
        ▼
ExtensionHub
        ├─ EventRegistry       一个事件，多订阅者
        ├─ CapabilityRegistry  一个能力，一个明确提供者或显式管线
        ├─ ConfigRegistry      Schema、所有者、命名空间和世代
        └─ PackageRegistry     JSON/Lua/DLL 包、依赖和加载顺序
        │
        ├────────► JSON MicroRules（有限、无状态或单步效果）
        ├────────► ScriptRuntime（多个 Lua 模块/VM）
        ├────────► NativePluginHost（多个可信 Win32 DLL）
        └────────► 内置 C++ 行为（兼容与核心路径）
        │                     │
        │                     └─ DLL 可注册新 Capability，供所有扩展共用
        ▼
统一 Game API / Capability Facade
        │  查询、伤害、生成、动画、音效、计时等受控能力
        ▼
同步返回值 + 延迟命令缓冲区
        │
        ▼
经过校验的原版函数桥接和运行时模块
```

硬边界如下：

- Hook 层知道地址和对象布局，但不知道具体技能。
- Lua 知道实体、事件和能力，但不知道地址、寄存器和原始结构偏移。
- JSON 可以描述有限微规则，但只能编译成已有事件、过滤器和单个命令，不能演变成第二种脚本语言。
- 外部 DLL 通过稳定 C ABI 使用宿主函数表和注册回调；不链接 `pvzmod.dll` 的内部 C++ 类、STL 容器或私有符号。
- 所有扩展都带稳定 `ownerId`、模块 ID、版本、优先级和世代，日志、订阅、命令和配置都能追溯到所有者。
- 动画、贴图、音效等资源继续走现有注册表，脚本只能引用逻辑 ID。
- `pvz_hook.cpp` 仍只负责启动顺序和模块安装，不能成为脚本总控类。

| 来源 | 注册单位 | 故障隔离 | 更新方式 |
| --- | --- | --- | --- |
| JSON MicroRule | 每条规则一个订阅者 | Schema/范围错误拒绝新世代 | 安全点事务热重载 |
| Lua | 每包一个 VM、每模块事件一个处理器 | `pcall`、指令/内存/时间预算 | 安全点脚本世代热重载 |
| 内置 C++ | 核心模块 | 版本校验和模块级失败 | 随 `pvzmod.dll` 更新 |
| 外部 DLL | 每插件一个 owner、多个显式注册项 | 可信原生代码，只能有限记录/禁用 | 替换二进制后重启游戏 |

## 5. 文件与配置布局

建议新增：

```text
pvzmod/
├─ config/
│  ├─ extensions/
│  │  └─ packages.jsonc      # 工具生成的统一包索引、启用状态和依赖
│  ├─ rules/                 # 有限 JSON 微规则，按命名空间拆分
│  ├─ scripts/
│  │  └─ modules.jsonc       # 打包工具生成的可选发布索引，新手不手写
│  ├─ plants/
│  ├─ zombies/
│  ├─ elites/
│  └─ bosses/
├─ scripts/
│  ├─ skills/
│  │  ├─ plants/
│  │  └─ zombies/
│  ├─ controllers/           # Boss、关卡和较大状态机
│  └─ lib/                   # 受限 require 可访问的共享 Lua 模块
├─ plugins/
│  └─ native/                # 唯一允许自动加载原生插件的根目录
│     └─ author.plugin_id/
│        ├─ plugin.jsonc     # ID、版本、ABI、依赖、启用与 DLL 文件
│        └─ plugin.dll       # 必须为 Win32，文件名由 manifest 精确指定
├─ saves/
└─ logs/

modding/script-sdk/
├─ QUICKSTART_60_MINUTES.md  # 严格按分钟推进的首个技能教程
├─ COOKBOOK.md               # 常见玩法配方
├─ API_REFERENCE.md          # 完整事件、字段和失败原因
├─ types/pvz.lua             # LuaLS/编辑器补全和类型提示
├─ templates/                # 植物、僵尸、精英、Boss 模板
└─ examples/                 # 从 00 到 10 逐级增加复杂度的可运行示例

modding/native-sdk/
├─ include/pvzmod_plugin.h   # 只含稳定 C ABI 和 POD 结构
├─ examples/                 # 最小插件、能力提供者、事件订阅者
└─ README.md                 # Win32 构建、加载、调试和风险说明
```

这些目录由 `PvZModManager` 统一管理归属、版本、启用状态、安装和回滚。动画制作器只输出公开 Schema 的动画资产包和可选基础植物骨架；LuaStudio 只创作和验证普通 Lua/module/binding 文件；管理器导入两者产物，但不生成、显示、验证或打开 Lua 代码。动画制作器和 LuaStudio 都不启停 DLL，也不拥有 `packages.jsonc`。

### 5.1 新手路径：一个 Lua 文件加一次绑定

开发模式按规范化路径和稳定字典序自动发现 `pvzmod/scripts/skills/**/*.lua`。技能 ID、事件函数和可选元数据直接由 Lua 文件返回，新手不需要先维护中央 manifest：

```lua
-- pvzmod/scripts/skills/plants/triple_pea.lua
return pvz.skill("TRIPLE_PEA", {
    on_attack = function(ctx)
        ctx:cancel_default_attack()
        ctx:shoot({ count = 3, spread = 0.12, damage = 20 })
    end
})
```

实体配置支持最短绑定写法：

```jsonc
{
  "id": 1000,
  "name": "三连豌豆",
  "templatePlantId": 0,
  "skills": ["TRIPLE_PEA"]
}
```

需要让同一脚本复用不同参数时，再使用完整写法：

```jsonc
{
  "skills": [
    {
      "id": "TRIPLE_PEA",
      "parameters": {
        "count": 3,
        "damage": 20,
        "spread": 0.12
      }
    }
  ]
}
```

独立 `PvZLuaStudio` 应提供“创建并绑定技能”向导：选择目标包和植物/僵尸/子弹/Boss，填写技能 ID，生成 Lua，自动写入绑定，启动语法与能力检查并打开待编辑位置。向导只自动完成机械步骤，生成结果仍是普通文本文件，不形成只能由 LuaStudio 读取的私有格式。`PvZModManager` 通过文件监视把新文件登记为受管资产，只显示路径、所有者、版本、哈希、绑定关系、启用状态和外部报告摘要。

### 5.2 发布路径：工具生成索引

`modules.jsonc` 是打包器生成的可选发布索引，用于固定允许加载的文件、API 版本、能力要求和文件哈希。它不写行为，也不要求入门用户手工维护：

```jsonc
{
  "schemaVersion": 1,
  "modules": [
    {
      "id": "TRIPLE_PEA",
      "kind": "skill",
      "entityKinds": ["plant"],
      "file": "skills/plants/triple_pea.lua",
      "apiVersion": 1,
      "requires": [
        "combat.cancel_default_attack",
        "world.spawn_projectile"
      ]
    },
    {
      "id": "COMMANDER_SUMMON",
      "kind": "skill",
      "entityKinds": ["zombie"],
      "file": "skills/zombies/commander_summon.lua",
      "apiVersion": 1,
      "requires": ["world.spawn_zombie", "fx.play_sound"]
    }
  ]
}
```

兼容规则：

- 开发模式无 manifest 时自动发现脚本；发布包存在 manifest 时只加载索引内文件，禁止偷偷扫描并执行未列出的脚本。
- `skills[].id` 先在统一行为注册表中解析；内置 C++ 和 Lua 模块使用同一 ID 空间。
- 当前 `BERSERK` 可以继续作为内置 C++ 技能存在，原配置无需立即修改。
- 同一个 ID 同时被 C++ 和 Lua 注册时启动失败并明确记录冲突，禁止依赖加载顺序覆盖。
- 未找到的非必需技能只禁用该绑定；绑定设置 `"required": true` 时，缺失会禁用对应实体定义。
- 脚本路径必须是 `pvzmod/scripts/` 内的规范化相对路径，只允许 `.lua`，拒绝绝对路径、`..`、目录联接逃逸和动态库。
- `PvZLuaStudio` 负责生成/更新 Lua module manifest，并检查重复 ID、能力兼容、无用脚本和缺失绑定；`PvZModManager` 只读取 manifest 和哈希，执行路径、归属、依赖、安装结构与启用状态检查，不解析 Lua 语义。

### 5.3 有限 JSON 微规则

非常小的功能不应被迫写 Lua。`pvzmod/config/rules/*.jsonc` 可以描述严格受限的 MicroRule：**一个事件、若干简单等值/范围过滤、一个内置效果**。

```jsonc
{
  "schemaVersion": 1,
  "namespace": "example.small_rules",
  "rules": [
    {
      "id": "rage_spawn_sound",
      "enabled": true,
      "event": "zombie.spawned",
      "priority": 0,
      "when": {
        "eliteId": "RAGE"
      },
      "effect": {
        "type": "play_sound",
        "soundId": "scream"
      }
    },
    {
      "id": "night_melee_bonus",
      "enabled": true,
      "event": "combat.before_damage",
      "priority": 0,
      "when": {
        "sourceTag": "MELEE",
        "levelTag": "NIGHT"
      },
      "effect": {
        "type": "multiply_number",
        "field": "damage",
        "value": 1.25
      }
    }
  ]
}
```

首批允许的过滤器只包括实体逻辑 ID/类型/标签、阵营、行、关卡标签和简单数值范围；首批效果只包括设置/加减/乘一个白名单数值、播放已注册音效/粒子/动画、增加一个固定标签、发送一个固定命令。所有字段都由 JSON Schema 枚举和范围校验。

明确禁止：

- 任意表达式字符串、脚本片段或 `eval`；
- 嵌套 `and/or/not` 条件树、循环、变量和用户函数；
- 自定义持久状态、计时器、协程和跨规则调用；
- 一个规则执行多个效果，或效果触发另一条 MicroRule 形成链；
- 直接指定函数地址、对象偏移、Hook 地址或 DLL 导出名。

一旦需要上述任意能力，就改用 Lua。新增 MicroRule `effect.type` 必须代表可被很多配置复用的稳定原语，不能为单个角色持续增加专用类型。

每条规则编译为统一事件总线中的订阅者，所有者 ID 为 `json:<namespace>:<ruleId>`。它与 Lua、内置 C++ 和 DLL 回调使用同一事件顺序、参数校验、命令缓冲、性能统计和错误日志，不建立第二套 JSON 执行器。

JSON 数量增长时遵循以下管理规则：

- 一个文件只负责一个命名空间和一个功能域，禁止把数百条规则堆进全局巨型文件。
- 完整 ID 为 `<namespace>:<id>`；重复 ID 直接报错，禁止依赖“最后加载者覆盖”。
- 单值配置只能有一个明确所有者；需要叠加的倍率/修正必须进入有顺序的 Modifier Pipeline。
- 开发模式按规范化路径排序发现文件；发布包由 `extensions/packages.jsonc` 固定文件、哈希、依赖和启用状态。
- 新世代配置必须整份解析、交叉校验和编译成功后再原子替换；失败保留上一有效世代。
- 注册表记录来源文件、行/JSON Pointer、所有者、事件、优先级和当前状态，调试面板可按这些字段过滤。

## 6. 最简单的技能代码

脚本通过 `pvz.skill` 返回一个普通行为表。只实现关心的事件即可自动订阅，不需要写类、继承、注册函数、manifest 或 Hook。入门教程先只使用配方层；理解需求后再展开到核心能力层。

### 6.1 三连豌豆

```lua
-- pvzmod/scripts/skills/plants/triple_pea.lua
return pvz.skill("TRIPLE_PEA", {
    on_attack = function(ctx)
        ctx:cancel_default_attack()
        ctx:shoot({
            count = ctx:param("count", 3),
            damage = ctx:param("damage", 20),
            spread = ctx:param("spread", 0.12)
        })
    end
})
```

### 6.2 周期召唤僵尸

```lua
-- pvzmod/scripts/skills/zombies/commander_summon.lua
return pvz.skill("COMMANDER_SUMMON", {
    on_tick = function(ctx)
        local ready = ctx:every("summon", ctx:param("cooldown", 1200), {
            first = ctx:param("firstDelay", 300)
        })
        if not ready then
            return
        end

        ctx:spawn_zombie({
            zombieType = ctx:param("zombieType", 0),
            row = ctx.self.row,
            x = ctx.self.x + 40
        })
        ctx:play_sound("scream")
    end
})
```

`ctx:shoot`、`ctx:every`、`ctx:spawn_zombie` 和 `ctx:play_sound` 是配方层入口。学习者需要精细控制时，可以查看配方源码并逐步替换成 `ctx.world:spawn_projectile`、`ctx.timer` 和 `ctx.fx:play_sound` 等核心调用，不需要重新学习另一套概念。

这类技能的 C++ 不应出现 `TRIPLE_PEA` 或 `COMMANDER_SUMMON` 分支。只要底层已有生成子弹、生成僵尸和播放音效能力，新增或调整技能只改 Lua 和参数。

### 6.3 60 分钟快速入门

官方 Quick Start 必须按真实时钟设计，并在全新开发包上反复试走：

| 时间 | 操作 | 立即反馈 |
| --- | --- | --- |
| 0–10 分钟 | 启动示例关卡，找到 `hello_pea.lua` | 日志和游戏内提示确认脚本已加载 |
| 10–20 分钟 | 把单发改为三发、修改伤害后保存 | 无需重启游戏，下一次攻击立即热重载 |
| 20–35 分钟 | 在 `PvZLuaStudio` 使用“创建并绑定技能” | 自动创建 Lua、绑定实体、验证并跳到待编辑行 |
| 35–50 分钟 | 增加一个条件或计时效果 | 调试面板显示事件、`ctx.state` 和待执行命令 |
| 50–60 分钟 | 故意写错字段并修复 | 中文错误指向文件、行号并给出相近字段建议 |

60 分钟验收不是“读完文档”，而是用户能关闭教程后，从空模板独立完成一个不同的小技能，并在游戏内运行。

### 6.4 10 小时熟悉路线

前 10 小时只学习最常用的 20% API：

1. 第 1 小时：完成 Quick Start。
2. 第 2 小时：理解 `on_spawn`、`on_attack`、`on_damaged`、`on_death`、`on_tick`。
3. 第 3 小时：使用 `ctx.state`、`ctx:every` 和 `ctx:after`。
4. 第 4 小时：使用 `nearest_enemy`、按行/距离查询和安全句柄。
5. 第 5 小时：伤害、治疗、生成植物/僵尸/子弹与取消原版行为。
6. 第 6 小时：动画动作、帧事件、音效和粒子。
7. 第 7 小时：参数化同一技能并复用到多个实体。
8. 第 8 小时：热重载、事件检查器、断言和错误定位。
9. 第 9 小时：完成“受击蓄能后范围反击”的组合练习。
10. 第 10 小时：验证、打包并在干净目录安装自己的技能包。

达到 10 小时目标的人，应能主要依靠补全和 Cookbook 工作，而不是频繁翻阅完整 API 参考。

### 6.5 30 小时近乎精通路线

10–30 小时不再堆砌新函数，重点学习系统化设计：

- 10–15 小时：拆分共享模块、标签、技能间通信和可复用配方。
- 15–20 小时：精英/Boss 多阶段状态机、事件优先级和冲突处理。
- 20–24 小时：状态序列化、版本号、`on_migrate` 与读档恢复。
- 24–27 小时：事件频率、查询范围、命令数量、预算分析和性能优化。
- 27–30 小时：完成一个包含至少三个阶段、动画事件和多个实体协作的结业玩法包。

结业验收要求作者能解释自己的事件顺序、状态归属、失败回退和性能预算，而不仅是“游戏里看起来能跑”。

### 6.6 为学习曲线配套的工具

运行时首版不能只交付 DLL 和 API 文档，还必须同时提供：

- 由易到难且始终可运行的编号示例：改数值、三连发、定时器、查询目标、受击反击、死亡爆炸、阶段 Boss。
- 独立 `PvZLuaStudio`：创建模板、绑定实体、语法检查、热重载、错误列表、事件检查器和状态查看器；管理器不内嵌这些代码功能。
- 独立 Mock 技能实验场：不启动游戏即可手动触发 `spawn/attack/damaged/tick`，查看返回字段和命令缓冲。
- LuaLS 类型文件和编辑器片段：输入 `ctx.` 即显示中文说明、参数、示例和可用事件范围。
- 可搜索 Cookbook：按“我想发射子弹”“我想低血量狂暴”“我想每隔几秒召唤”组织，不按 C++ 模块名组织。
- 中文、可行动的诊断，例如：`TRIPLE_PEA 第 8 行：ctx.slef 不存在，你是否想写 ctx.self？本次攻击已回退到原版行为。`
- 游戏内开发提示：成功热重载、当前脚本世代、已禁用模块和最近一次错误；正式发布模式可关闭。

API 标识符统一使用简短稳定的英文，教程、补全和错误解释使用中文。不要同时维护中英文两套函数别名，否则搜索、示例和第三方脚本会分裂成两套生态。

## 7. 统一事件模型

JSON 微规则、Lua、内置 C++ 和外部 DLL 使用同一事件定义。首版事件分成两类，不能混用。

### 7.1 同步可修改事件

这类事件必须在原函数继续执行前返回，扩展只能修改明确开放的字段：

| 处理器 | 可读取 | 可修改 |
| --- | --- | --- |
| `on_before_attack` / `on_attack` | 攻击者、目标、武器、行列 | `cancel_default`、有限攻击参数 |
| `on_before_damage` | 来源、目标、伤害类型、当前伤害 | `damage`、`cancel_default` |
| `on_before_heal` | 来源、目标、当前治疗量 | `amount`、`cancel_default` |
| `on_before_spawn` | 生成请求和来源 | 受限生成参数或取消 |

同步事件中的 Lua 不得 `yield`，所有扩展都必须遵守更低的耗时预算。世界生成、删除、二次伤害等操作先写入命令缓冲区，等当前原版调用退出后再执行，避免重入对象池或递归进入同一 Hook。

### 7.2 延迟通知事件

这类事件记录快照并在安全点派发：

- `on_spawn`、`on_ready`、`on_tick`、`on_after_attack`
- `on_damaged`、`on_death`、`on_remove`
- `on_animation_event`
- `on_wave_start`、`on_wave_end`、`on_level_start`、`on_level_end`

安全点建议由核对后的 `Board::Update` 后置 Hook 驱动。裸汇编桥接只构造原生事件，不直接执行任意 Lua 或第三方 DLL 回调；Lua VM 和正常插件回调只归游戏主线程所有。插件自己的工作线程只能通过线程安全的 Host API 投递有上限消息，不能直接访问游戏对象。

脚本未实现某个处理器时不订阅该事件。`on_tick` 只对明确实现它的绑定派发，避免所有植物和僵尸每帧都进入 Lua。

### 7.3 顺序与冲突

每个订阅具有：`eventId`、`ownerId`、`handlerId`、`phase`、`priority`、`filter`、`generation` 和不可复用的 `SubscriptionToken`。固定排序键为：

```text
phase 固定顺序 → priority 从高到低 → ownerId 字典序 → handlerId 字典序
```

推荐阶段为 `Validate → Modify → Act → After → Observe`。只有事件定义明确允许的阶段才能修改字段或取消原版行为；`Observe` 永远只读。同步修改值按顺序串行传递，最后统一做范围校验。任何扩展都不能依赖 DLL 加载顺序、脚本扫描顺序或 JSON 文件枚举顺序。

事件分发时使用监听器快照，不在持锁状态调用脚本。事件嵌套深度和每帧事件数量必须有限制；超过限制的后续事件延迟到下一安全点或丢弃并记录错误，不能无限递归。

### 7.4 大量订阅者的管理

事件总线按“一个事件可能被几十到上百个 JSON/Lua/DLL 处理器订阅”设计：

- 注册和热重载时构建按 `eventId + phase` 分组的不可变连续数组；派发热路径不扫描所有模块。
- `eventId`、Capability ID 和常用标签在加载期驻留为数字 ID，热路径不反复创建/哈希字符串。
- `filter` 先由 C++ 对事件快照做廉价匹配；不符合实体类型、标签、关卡或范围的订阅不会进入 Lua/DLL 回调。
- 增删订阅采用 copy-on-write 世代快照；当前派发继续使用旧快照，引用归零后释放，回调期间不持全局锁。
- 同一 `(ownerId, eventId, handlerId)` 重复注册直接拒绝；每次成功注册返回 Token，卸载、禁用和热重载必须按所有者世代一次性撤销。
- 配置每所有者、每事件、全局订阅上限；超限只拒绝新增项并列出占用最多的所有者，不破坏已生效世代。
- 统计每个订阅的调用次数、总耗时、最大耗时、错误数、过滤命中率和命令数；调试器可按事件或所有者排序热点。
- 高频 `tick/update/draw` 事件要求显式订阅和尽可能窄的过滤器；达到预算时 Lua 可延迟/禁用，DLL 只能告警或禁用后续调用，不能安全强制中断正在执行的原生代码。
- 同一个 Lua 技能定义每种事件只注册一个模块处理器，不能因为场上有 500 个实体就生成 500 个全局订阅；`EntityHandle -> BehaviorBinding[]` 由独立 BindingIndex 查找实例参数和 `ctx.state`。
- 公共 API 包装每个 Lua VM 只创建一次，DLL Host API 函数表每个 ABI 版本只创建一次；模块和实体只持轻量句柄，不复制整张函数表。

注册表的数据结构应支持至少每事件 `1024` 个订阅和全局 `10000` 个订阅；这表示管理能力，不表示允许一帧执行一万个昂贵回调。压力测试必须覆盖同一事件 100 个混合 JSON/Lua/DLL 订阅者的稳定顺序、撤销、重载和性能统计。

### 7.5 能力提供者与事件订阅者必须分开

“注册 API”和“订阅事件”不是同一件事：

- **Event** 是一对多通知或修改管线，可以有很多订阅者。
- **Capability** 是可调用服务，例如 `world.spawn_zombie@1`，同一完整 ID/主版本默认只能有一个权威提供者。
- **Modifier Pipeline** 是显式声明为可叠加的特殊 Capability，例如伤害修正；它可以有很多 modifier，但沿用事件的确定顺序和所有者管理。

外部 DLL 若想提供新能力，必须用带命名空间的 ID，例如 `author.weather.spawn_lightning@1`。重复提供同一 ID 时整项注册失败，禁止“后加载 DLL 覆盖前者”。若确实需要多个实现，由调用者选择不同 ID，或由一个明确的聚合 Capability 管理它们。

所有消费者——JSON 微规则、Lua、内置 C++ 和其他 DLL——通过同一 `CapabilityRegistry` 查询版本化函数表，不持有提供 DLL 的内部对象。提供者被禁用时，注册表先阻止新调用，等待活动调用归零，再撤销能力和关联订阅。

## 8. `ctx` 与 API v1

每次回调只收到一个 `ctx`：

| 字段 | 含义 |
| --- | --- |
| `ctx.self` | 当前实体的安全句柄 |
| `ctx.source` / `ctx.target` | 事件有来源或目标时提供的安全句柄 |
| `ctx.state` | “实体实例 + 技能绑定”独享的可序列化状态表 |
| `ctx.params` | JSON 参数的只读视图 |
| `ctx.dt` | 游戏逻辑 tick 差，不使用系统墙钟 |
| `ctx.event` | 事件类型专用的只读/可写字段 |
| `ctx.world` | 查询和生成能力 |
| `ctx.combat` | 伤害、治疗、状态效果能力 |
| `ctx.animation` | 动作、速度、帧事件和挂点能力 |
| `ctx.fx` | 粒子、音效、屏幕效果能力 |
| `ctx.timer` | 基于游戏 tick 的计时能力 |
| `ctx.rng` | 可复现的确定性随机数发生器 |

为了让简单代码保持简短，常用实体只读属性可以写成 `ctx.self.x`、`ctx.self.row`、`ctx.self.health`；修改必须调用受控方法，例如 `ctx.self:set_speed_multiplier(1.5)`，不能直接覆盖结构字段。

配方层首批只提供高频、含义稳定的便捷函数：

| 配方 | 用途 | 底层来源 |
| --- | --- | --- |
| `ctx:shoot(options)` | 从当前植物发射一枚或多枚子弹 | `world.spawn_projectile` |
| `ctx:every(key, ticks, options)` | 用 `ctx.state` 实现周期触发 | `timer` + 状态表 |
| `ctx:after(key, ticks)` | 一次性延迟条件 | `timer` + 状态表 |
| `ctx:nearest_enemy(options)` | 查询最近合法目标 | `world.query` |
| `ctx:explode(options)` | 对受限范围目标造成伤害并播放效果 | `world.query` + `combat.damage` + `fx` |
| `ctx:spawn_plant/zombie(options)` | 以当前实体为默认来源生成实体 | `world.spawn_*` |
| `ctx:play_sound/animation(id)` | 播放已注册资源 | `fx` / `animation` |

所有配方使用命名参数、合理默认值和同一套单位；编辑器悬停必须显示默认值、合法范围和最终拆出的核心命令。配方不引入只有某一个角色才能理解的名字。

首版能力建议按下列顺序实现：

1. `entity.read`：类型、逻辑 ID、行列、坐标、生命、阵营、存活状态。
2. `entity.stats`：受范围限制的生命、速度、攻击间隔和临时倍率。
3. `world.query`：按阵营、行、距离和标签查询；返回句柄数组并限制最大结果数。
4. `combat.damage` / `combat.heal`：统一伤害来源、标志和递归保护。
5. `world.spawn_projectile`、`world.spawn_zombie`、`world.spawn_plant`：参数校验后排队执行。
6. `animation.play` / `animation.emit_event`：只引用已注册动画和动作 ID。
7. `fx.play_sound` / `fx.spawn_particle`：只引用资源注册表 ID。
8. `timer` / `rng`：游戏 tick 计时和确定性随机。
9. `economy`、`level`、`ui`：以后按实际玩法需求添加，不预先暴露大而不稳的接口。

API 必须返回明确结果。无效句柄或不满足条件的操作返回 `false, "reason"`；脚本编程错误抛出 Lua 错误并由宿主捕获。禁止静默写入不明内存。

## 9. 实体句柄、侧挂状态与生命周期

Lua 永远不能持有 `Plant*`、`Zombie*` 或任意地址。宿主提供的句柄至少包含：

```text
EntityHandle = kind + poolSlot + gameInstanceId + hostGeneration
```

每次 API 调用都重新验证对象池范围、实例 ID、Board 所属关系和宿主世代。对象删除或对象池槽复用后，旧句柄立即失效；访问失效句柄只返回错误，不能命中下一只复用同一地址的实体。

`ctx.state` 由运行时侧挂，不写入原版对象未知空隙。键建议使用：

```text
boardGeneration + entityHandle + behaviorId + bindingIndex
```

`on_remove` 派发后清理实体状态。关卡退出时整体清理该 Board 的事件、计时器、命令和句柄，不能把上一关状态带进下一关。

## 10. 命令缓冲与可重入安全

除了少数同步返回字段，Lua 对世界的修改都转换成 `GameCommand`：

```text
SpawnProjectile / SpawnZombie / SpawnPlant
ApplyDamage / ApplyHeal / ApplyStatus
RemoveEntity
PlayAnimation / PlaySound / SpawnParticle
SetTimer / CancelTimer
```

每条命令记录脚本 ID、源实体、产生事件和序号。运行时在当前 Hook 退出后的安全阶段执行，并再次验证所有句柄和参数。这样可以避免：

- 在遍历僵尸数组时插入或删除对象；
- `TakeDamage` 脚本再次调用 `TakeDamage` 形成无穷递归；
- 初始化尚未结束时访问动画或 Board；
- 脚本错误破坏裸汇编桥接的寄存器和栈。

同一帧命令按事件序号和脚本顺序稳定执行。对同一已删除目标的后续命令安全失败，不回滚已经完成的其他游戏操作。

## 11. 能力声明与 API 版本

开发模式中的 Lua 省略元数据时默认使用 `apiVersion: 1`，并在实际调用不存在的能力时给出精确错误。`PvZLuaStudio` 及其 headless validator 根据 Lua 模板、配方依赖和验证结果生成脚本模块索引；`PvZModManager` 把该索引与 JSON 规则、DLL manifest、包依赖和文件哈希合并为安装计划，但不重新验证 Lua。发布包中的每个模块必须拥有明确的 API/ABI 版本和 `requires`，运行时仍在实际加载时独立强制校验。

加载发布包时先检查当前 DLL 是否提供全部声明能力，缺失时给出类似下列日志：

```text
[script] COMMANDER_SUMMON disabled: requires world.spawn_zombie, host provides api v1 without that capability
```

版本规则：

- 增加新能力或给现有调用增加可选字段，保持 `apiVersion: 1`。
- 删除、重命名或改变既有语义时新增 API 大版本，并在过渡期并存适配器。
- 能力名比“DLL 版本大于某值”更重要，脚本应按自己真正需要的能力加载。
- `requires` 是提前诊断兼容性，不是访问控制的替代品；即使声明遗漏，所有实际 API 调用仍必须经过宿主校验。
- JSON、Lua、内置 C++ 和 DLL 共用事件与能力语义；任何实现都不得绕过句柄/命令的关键校验去制造不同玩法规则。

新增底层能力的标准流程：

1. 先证明现有 API 无法组合出目标玩法。
2. 把需求提炼为通用原语，而不是某个技能名的专用入口。
3. 对 1051 版目标函数重新反汇编，确认寄存器、栈清理、入口字节和生命周期。
4. 在 `pvzmod.dll` 核心或可信 DLL Provider 中实现验证、范围限制和失败回退。
5. 增加能力名、Mock 测试和进游戏垂直切片。
6. 明确它是否允许 JSON/Lua 调用，并生成对应绑定后，才允许其他扩展使用。

### 11.1 统一所有者、命名空间与包索引

每个扩展包拥有全局稳定的反向域名式 `ownerId`，例如 `author.commander_pack`。它的 JSON 规则、Lua 模块、DLL、配置 Schema、事件处理器和 Capability 都归属于同一个所有者，不能分别使用无法关联的匿名 ID。

```text
package:     author.commander_pack
lua skill:   author.commander_pack:commander_summon
json rule:   author.commander_pack:night_bonus
handler:     author.commander_pack:on_zombie_spawned
capability:  author.commander_pack.spawn_minion@1
```

核心能力使用保留命名空间 `pvzmod.*`；Lua 中的 `ctx.world:spawn_zombie` 是 `pvzmod.world.spawn_zombie@1` 的易用包装。第三方不能注册 `pvzmod.*`，也不能注册另一个包命名空间下的 ID。

Lua 默认每个扩展包一个独立 VM，而不是每个技能或实体一个 VM。包内多个 Lua 文件可以通过受限 `require` 共用库；包间不共享 Lua 全局表，只通过 Event、Capability 和显式消息通信。这样既避免数百个 VM，又能按 `ownerId` 统计内存、预算、错误和一次性卸载整个包。

`pvzmod/config/extensions/packages.jsonc` 由工具生成，集中记录包版本、启用状态、优先级、依赖、JSON 文件、Lua 模块和原生插件，但不复制各文件的业务内容。禁用一个包会通过 `ownerId + generation` 原子撤销它的全部规则、脚本、订阅、能力和配置视图；依赖它的包同步变为 `dependency_missing`，不能留下半启用状态。

加载顺序固定为：

1. 核心 Schema、事件和 Capability；
2. 解析所有包 manifest 与依赖图；
3. 查询 DLL Descriptor，并把插件声明的 Schema、Capability 和事件需求放入暂存注册表；
4. 使用暂存注册表验证 JSON、编译 Lua，但不派发事件或开放调用；
5. 全局检查 ID、Capability Provider、订阅配额和依赖冲突；
6. 按拓扑顺序一次提交新世代；任一步失败都保留上一完整世代。

## 12. 热重载

JSON 微规则和 Lua 只能在安全点热重载，不能在 Hook、Lua 或插件回调执行到一半时替换订阅。DLL 二进制不热卸载，更新后必须重启游戏。

流程：

1. 发现包索引、JSON/JSONC 或 `.lua` 修改时间变化。
2. 在新 `ExtensionGeneration` 中解析规则、加载 Lua 并校验所有变更模块。
3. 检查 ID、Schema、事件、能力、依赖、配额和确定顺序。
4. 全部成功后原子切换新的订阅/配置/脚本快照。
5. 旧回调结束且引用归零后销毁旧 Lua 环境和注册表世代。
6. 失败时继续运行上一份有效世代，并记录文件、行号或 JSON Pointer、所有者和原因。

默认保留已有实体的 `ctx.state`。脚本可选实现：

```lua
on_migrate = function(old_state, from_version)
    old_state.cooldown = old_state.cooldown or 0
    return old_state
end
```

若迁移失败，只重置该技能绑定的状态，不应卸载整个 Mod 或破坏实体。

## 13. 存档边界

允许进入 Mod 存档的脚本状态只包括：`nil`、布尔、有限整数/浮点数、UTF-8 字符串和无环普通表。禁止保存函数、协程、userdata、句柄、文件对象或带元表对象。

保存时记录：

```text
module id + module state version + entity logical identity + serialized ctx.state
```

原版瞬时对象句柄不能跨读档恢复。读档时由自定义实体存档先重建实体，再把已验证状态交给对应模块的 `on_load`/`on_migrate`。每个绑定和整个存档都要设置大小上限。

## 14. 错误、性能和资源限制

起步默认值应放在 C++ 常量中并通过压力测试调整，而不是让普通 Mod 随意关闭。建议至少包括：

- 每次同步回调的 Lua 指令预算；
- 每个模块每帧总指令预算和全局墙钟预算；
- 每个脚本 VM 的自定义分配器内存上限；
- 每帧事件数、命令数、计时器数和查询结果数上限；
- 单个 `ctx.state` 和单个存档表大小上限；
- 连续错误阈值，达到后只禁用对应模块的当前世代。

Lua 标准库只开放必要部分：基础值、字符串、表和受控数学函数。移除 `io`、`os`、`package.loadlib`、`dofile`、`loadfile`、`debug` 和任意字节码加载；`require` 只能读取 `pvzmod/scripts/lib/` 下经过路径校验的 Lua 源码。

`math.random` 应替换为 `ctx.rng`，使相同关卡种子、实体实例 ID 和事件序列得到可复现结果。计时使用游戏 tick，不使用系统时间。

错误日志至少包含：模块 ID、文件和行号、事件名、实体句柄摘要、错误堆栈、耗时/指令数以及“保留旧世代”或“已禁用当前绑定”的处理结果。相同错误应限频，避免日志洪泛。

## 15. 绘制与高频路径的特殊规则

首版不要把现有 `BeforeDraw` / `AfterDraw` 直接开放给 Lua。绘制频率高、调用环境敏感，脚本异常也可能污染图形状态。

脚本应在 `on_spawn`、`on_tick` 或动画事件中设置受控视觉状态，C++ 绘制适配器只读取结果，例如 tint、贴图 ID、动作 ID、显隐和有限特效参数。必须逐帧变化的视觉可通过动画轨道、粒子系统或预先计算参数完成。

确实需要自定义绘制时，未来只开放“提交绘制命令”的 API，不向 Lua 暴露 `Graphics*`。

## 16. 原生 DLL 插件宿主

Lua 能自由组合的是宿主已经验证过的能力，不能替代所有逆向和原生工作。以下情况使用第三级 DLL 插件：

- 原版从未暴露过的生命周期或伤害路径，需要新增底层适配；
- 必须在极高频路径执行且 Lua 预算不够；
- 需要新的资源解码器、压缩库、算法库或外部原生集成；
- 需要修改固定容量、保存格式或其他全局结构；
- 需要提供一个新的通用 Capability，随后让很多 Lua、JSON 和 DLL 一起调用。

### 16.1 唯一加载目录与插件 manifest

`pvzmod.dll` 只自动扫描：

```text
pvzmod/plugins/native/<plugin-id>/plugin.jsonc
```

DLL 必须位于自己的插件目录，且文件名由 manifest 精确指定。游戏根目录、`pvzmod/` 根目录、`config/`、`scripts/`、当前工作目录和系统 PATH 中的陌生 DLL 一律不作为插件扫描。

```jsonc
{
  "schemaVersion": 1,
  "id": "author.weather",
  "name": "Weather Native Provider",
  "version": "1.2.0",
  "abiVersion": 1,
  "binary": "weather.dll",
  "enabled": true,
  "sha256": "由打包工具写入的文件哈希",
  "dependencies": [
    { "id": "pvzmod.core", "version": ">=0.11.0" }
  ],
  "nativeAccess": "hostOnly"
}
```

加载前必须完成：规范化路径包含性、拒绝重解析点逃逸、PE32/i386、文件哈希、插件 ID、ABI 主版本、依赖版本、依赖环和重复包检查。`nativeAccess` 默认 `hostOnly`，表示插件承诺只通过宿主 API 工作；声明 `unsafeProcess` 表示它会自行访问内存或安装 Hook，只能由用户显式启用，并在日志和管理界面持续显示高风险标记。这个标记是风险披露，不能真正限制原生 DLL 的进程权限。

依赖拓扑排序后以 `ownerId` 作为稳定次序。使用受限 `LoadLibraryExW` 搜索标志，使插件依赖优先从自身目录和 System32 解析，不能退回游戏当前目录进行模糊搜索。绝不能在 `DllMain` 的 loader lock 内加载插件；宿主基础初始化完成、尚未开始派发游戏事件时再加载。

### 16.2 双向调用的稳定 C ABI

插件只导出固定入口，`pvzmod.dll` 主动查询并加载插件：

```cpp
extern "C" __declspec(dllexport)
int32_t __cdecl PvZModPlugin_Query(
    uint32_t hostAbiVersion,
    PvZModPluginDescriptorV1* descriptor);

extern "C" __declspec(dllexport)
int32_t __cdecl PvZModPlugin_Load(
    const PvZModHostApiV1* host,
    PvZModPluginHandle* pluginHandle);

extern "C" __declspec(dllexport)
void __cdecl PvZModPlugin_Shutdown(PvZModPluginHandle pluginHandle);
```

`PvZModPlugin_Load` 收到只读宿主函数表。插件通过它反向调用 `pvzmod`：

```text
log / report_error
subscribe_event / unsubscribe
register_capability / unregister_capability
find_capability / call_capability
enqueue_command
resolve_entity_handle / read_entity_snapshot
register_config_schema / read_owned_config
allocate / deallocate（跨边界内存时必须成对）
```

ABI 规则固定为：

- 所有结构以 `abiVersion + structSize` 开头，新增字段只追加在尾部。
- 只使用定宽整数、浮点、长度明确的 UTF-8 缓冲区、枚举和 opaque handle。
- 明确使用 `__cdecl`，禁止跨边界传递 C++ 类、STL、异常、RTTI、引用或编译器私有布局。
- 谁分配谁释放；确需跨模块释放时只能调用 Host API 中配对的 allocator。
- 插件不能保存事件快照中的临时指针；实体仍使用 `EntityHandle` 和实例世代校验。
- 插件入口返回状态码并通过宿主日志报告详细错误，异常不能穿过 ABI 边界。

这同时满足“DLL 调用 pvzmod”和“pvzmod 加载并调用 DLL”：宿主拥有生命周期和注册事务，插件拥有回调实现，并通过传入的 Host API 请求公共服务。插件不需要链接 `pvzmod.lib` 或导入不稳定的内部符号。

### 16.3 DLL 注册公共能力

插件可通过 `register_capability` 提供新能力。Descriptor 至少包含：

```text
capabilityId + major/minor version + ownerId
input/output schema + flags + invoke callback + userData
```

标记为 `SCRIPT_SAFE` 的能力必须提供可验证的通用参数 Schema、确定的所有权和明确失败码；宿主据此自动建立 Lua 包装和 JSON MicroRule 可用效果。未标记的能力只能由 C/C++ 通过 `find_capability` 查询版本化函数表，适合高性能 DLL-to-DLL 调用。

同一完整 Capability ID/主版本只能有一个提供者。注册发生在事务中：插件先声明全部事件、能力和配置 Schema，宿主统一检查冲突与依赖，全部成功后一次提交；任何一项失败都回滚该插件本次注册，不留下半加载状态。

### 16.4 生命周期、禁用与热更新

原生 DLL 首版不做二进制热卸载。原因是其他插件可能仍持有函数表，后台线程可能尚未退出，系统也无法证明所有回调地址已经离开调用栈。

- 配置禁用时先关闭新调用，撤销该所有者全部订阅/能力/Schema，等待活动 Host 调用归零；模块句柄仍保留到进程退出。
- 更新 Lua/JSON 可以热重载；替换 DLL 文件要求完全退出并重启游戏。
- 正常退出按依赖逆序调用 `Shutdown`，然后释放模块；异常退出不承诺清理游戏状态。
- 插件不得在 `DllMain` 创建线程、加载其他插件、访问游戏对象或注册回调；所有工作放在 `Load/Shutdown`。

### 16.5 原生风险边界

原生 DLL 与游戏处于同一进程，理论上可读写全部内存。宿主可以在回调外层记录耗时并用 SEH 记录部分异常，但访问冲突后进程可能已经被破坏，不能承诺像 Lua 一样安全恢复。插件必须视为完全可信代码，管理界面应显示发布者、版本、哈希、原生权限、依赖和最近错误。

正常插件不要各自安装相同 Hook，而应请求核心增加一次语义事件或注册一个共享 Capability。确实自行 Hook 的 `unsafeProcess` 插件必须声明目标 EXE 版本、Hook 点标识和冲突项；宿主只能提前发现已声明冲突，无法管理未申报的任意内存操作。

仍然禁止向 Lua 或 JSON 增加以下“万能接口”：

- `read_memory(address)` / `write_memory(address, value)`；
- `call_address(address, ...)`；
- `install_hook(address, callback)`；
- 把 `Plant*`、`Zombie*`、`Board*` 转成整数。

这些接口看似省事，实际会让版本校验、对象生命周期、调用约定和崩溃隔离全部失效。需要它们的工作只能进入明确标记风险的原生插件或 `pvzmod.dll` 核心。

## 17. 建议代码模块

保持当前扁平源码风格，可新增：

```text
extension_package_registry.* # packages.jsonc、所有者、依赖图和统一世代
extension_diagnostics.*    # JSON/Lua/DLL 来源、耗时、错误和状态查询
json_micro_rule_config.*   # 有限规则 Schema、解析和静态编译
json_micro_rule_runtime.*  # MicroRule -> Event subscription / GameCommand
script_manifest.*          # modules.jsonc 解析、路径和能力校验
script_runtime.*           # Lua VM、模块世代、预算、错误和热重载
script_context.*           # ctx、参数、状态和事件字段绑定
script_entity_handle.*     # 安全句柄创建、解析和失效
script_command_buffer.*    # 延迟命令、顺序和递归保护
behavior_event_registry.*  # 统一事件、快照索引、Token、过滤和顺序
capability_registry.*      # 版本化 Provider、查询、调用和活动引用
behavior_registry.*        # JSON/Lua/内置 C++/DLL 行为的统一 ID
native_plugin_manifest.*   # plugin.jsonc、PE32、哈希、ABI 和依赖校验
native_plugin_host.*       # 固定目录加载、事务注册、禁用与 Shutdown
pvzmod_plugin_abi.h        # 对外发布的纯 C Win32 ABI
game_api_entity.*          # 实体查询与属性能力
game_api_combat.*          # 伤害、治疗和状态能力
game_api_world.*           # 世界查询与生成能力
game_api_animation.*       # 动画和帧事件能力
game_api_fx.*              # 音效与粒子能力
script_hook.cpp            # 安全点 Pump 和必要的统一 Hook 安装
```

迁移期间 `elite_skill_registry` 可以作为 `behavior_registry` 的适配层，`zombie_event_bus` 可以转发到统一总线。不要一次性重写已经验证的 Hook，也不要先为 Lua、JSON 和 DLL 分别建立临时总线再尝试合并。

## 18. 现有 Hook 的接入判断

| 目标语义 | 当前基础 | 处理方式 |
| --- | --- | --- |
| 僵尸生成完成 | 已有 `DispatchZombieInitialized` | 直接适配为 `on_spawn/on_ready` |
| 僵尸啃食伤害 | 已有伤害 modifier | 适配为同步 `on_before_attack` |
| 僵尸 Update/Delete | 精英模块已有 Hook | 抽成通用生命周期事件，避免由精英模块独占 |
| 植物 Initialize/Update/Fire | 自定义植物模块已有 Hook | 发布植物事件，保留当前逻辑作为内置行为 |
| 子弹 Initialize/伤害来源 | 已有实例侧挂和命中识别 | 抽成子弹生成、命中和移除事件；移除点仍需核对 |
| `Zombie::TakeDamage` | 已有植物伤害 Hook | 扩展成通用伤害上下文，必须保持已确认来源识别和递归保护 |
| 动画帧事件 | 配置和设计已有，通用总线未落地 | 先实现动画事件控制器，再转发脚本 |
| Board 安全 Pump | 尚未建立统一入口 | 重新反汇编并验证 `Board::Update` 后置安全点 |
| 植物删除/死亡 | 尚无统一事件 | 核对真正回收路径后增加，不靠轮询裸指针猜测 |

这里的“复用”是复用已验证地址和桥接思路，不表示可以不检查当前入口机器码。每个新 Hook 仍只支持精确的 `PlantsVsZombies.exe 1.0.0.1051`。

## 19. 分阶段实施

### 阶段 A：统一 ExtensionHub 与有限 JSON

- 先实现 `PackageRegistry`、`EventRegistry`、`CapabilityRegistry`、Owner/Generation、订阅 Token 和命令缓冲。
- 把现有 `zombie_event_bus` 与 `elite_skill_registry` 通过适配器接入，不重写已验证 Hook。
- 实现首批 MicroRule Schema、规则编译、事务热重载和统一诊断。
- 建立 100 个混合模拟订阅者以及全局大量注册/撤销的压力测试。

验收：同一事件至少 100 个订阅者时顺序确定、派发不持注册锁、按所有者完整撤销；重复 ID、重复 Capability Provider、依赖环和无效 JSON 均保留上一有效世代。一个简单固定效果只写 JSON 即可进 Mock 验证。

### 阶段 B：Lua 宿主与新手工具

- 引入锁定版本的 Lua 源码并支持 Win32 Release 构建。
- 完成 manifest、路径限制、模块加载、日志、指令预算和内存上限。
- 使用 Mock `ctx` 在独立测试程序运行 Lua，不安装任何新游戏 Hook。
- 同时交付 60 分钟 Quick Start、LuaLS 类型文件、编号示例、模板和最小 Mock 实验场；不能把教学工具推迟到运行时完成以后。
- 建立独立 `PvZLuaStudio` 的最小编辑、绑定、验证和 Mock 工作区；动画制作器与管理器都不增加 Lua 代码页面。

验收：语法错误、缺能力、死循环、超内存和越界路径均只禁用对应模块，测试进程不崩溃；不了解项目内部结构的测试者能在一小时内修改并运行 Mock 小技能。

### 阶段 C：用 Lua 复刻 `BERSERK`

- 保留当前 C++ `BERSERK` 作为对照和回退。
- 把 `Spawn` 倍率能力接入统一上下文。
- 编写功能等价的 Lua 版本，通过固定种子和字段快照比较结果。

验收：C++ 与 Lua 两种实现分别启用时，生命、速度和啃食伤害结果一致；未配置脚本时行为与当前版本完全相同。

### 阶段 D：植物攻击垂直切片

- 把植物初始化、攻击和子弹生成发布为语义事件。
- 实现 `cancel_default_attack` 和 `spawn_projectile` 命令。
- 以三连豌豆验证“一份 Lua + 一段绑定配置”可完成当前 JSON 无法表达的技能。
- 在 `PvZLuaStudio` 接入“创建并绑定技能”、错误跳转、热重载提示和事件检查器；从动画制作器导入并由管理器登记的植物资产只作为可选绑定目标。

验收：不新增技能专用 C++ 分支即可改变发射数量、散射和条件；脚本错误时恢复模板攻击或安全跳过，不能卡死攻击状态机；从安装好的开发包开始，首次使用者可在 60 分钟内做出不同于教程成品的小技能并进游戏验证。

### 阶段 E：原生 DLL 插件与共享能力

- 发布 `native-sdk/pvzmod_plugin.h`、最小 Win32 插件和 CMake 示例。
- 实现固定目录扫描、manifest/PE/hash/依赖校验、受限 `LoadLibraryExW`、Query/Load/Shutdown 和事务注册。
- 用示例 DLL 注册 `example.counter@1` 与一个事件处理器，并让 Lua、另一个 DLL 和 JSON MicroRule 共同调用/观察它。
- 完成禁用所有者、活动调用计数、依赖逆序 Shutdown 和“DLL 更新必须重启”提示。

验收：`pvzmod.dll` 能加载固定目录中的多个插件，插件能通过 Host API 调用核心并注册共享 Capability；重复 Provider、ABI 不匹配、缺依赖、错误位数和部分注册全部安全拒绝。混合 100 个 JSON/Lua/DLL 订阅者仍保持确定顺序和完整所有者追踪。

### 阶段 F：生命周期、Boss、关卡和存档

- 接入伤害、死亡、移除、动画帧事件、计时器和世界查询。
- 支持低血量阶段、受击反击、周期召唤和死亡爆炸。
- 将同一运行时用于 Boss 控制器、关卡机制和后续确实需要自由代码的系统。
- 增加状态序列化、版本迁移和读档后实体重新绑定。
- 完成 JSON/Lua 世代热重载与 `ctx.state` 迁移；DLL 继续要求重启。

验收：旧句柄不能命中复用对象；JSON/Lua 重载失败保留上一有效世代；动画掉帧不漏发或重复发事件；多阶段 Boss 可以同时复用核心和 DLL Provider 能力并正确保存脚本状态。

## 20. 总体验收标准

- 没有新的规则、脚本或插件包时，当前所有 JSON、Hook 和内置技能行为保持不变。
- 固定数值和“一事件一效果”的小功能能只用有限 JSON 完成；出现状态、计时、复杂条件或多个效果时校验器明确建议改用 Lua。
- 新技能的常规开发只需要一个 `.lua` 文件和一处技能绑定，不需要改 DLL。
- `pvzmod.dll` 只从 `pvzmod/plugins/native/<plugin-id>/` 加载 manifest 指定的 Win32 DLL；插件可通过 `PvZModHostApiV1` 双向调用并提供共享能力。
- JSON、Lua、内置 C++ 和多个 DLL 共用一个事件注册表、Capability 注册表、命令缓冲和所有者/世代系统。
- 同一事件 100 个混合订阅者的排序、过滤、撤销、重载和统计通过压力测试；同一 Capability 的重复 Provider 被事务拒绝。
- 禁用任意包可按 `ownerId` 完整撤销它的 JSON、Lua、DLL 订阅、能力和 Schema，不影响其他所有者。
- `PvZLuaStudio` 可以在一次事务中创建 Lua、写入绑定、验证并打开待编辑位置；`PvZModManager` 只管理脚本资产元数据和启用/安装状态，不能显示、验证或打开代码；动画制作器不接触 Lua 或技能绑定，手工文件路径仍完整可用。
- 1 小时测试者能完成原创小技能；10 小时测试者能组合常用事件与能力；30 小时测试者能独立完成带状态迁移和性能说明的多阶段精英/Boss 包。
- Quick Start、Cookbook、完整 API、LuaLS 类型、模板和编号示例与运行时同版本发布，示例进入自动测试。
- Lua 看不到裸地址、游戏对象指针、MinHook 或原版调用约定。
- 一个脚本的语法错误、运行错误、死循环或内存超限不能导致游戏崩溃，也不能禁用其他模块。
- 所有同步修改字段、生成参数和实体句柄都经过宿主校验与范围限制。
- 相同配置、种子和输入事件序列产生相同逻辑结果。
- JSON/Lua 热重载只在安全点切换；失败保留上一完整扩展世代；DLL 更新要求重启。
- 事件和命令顺序确定，递归、队列和每帧预算均有上限。
- `BERSERK` 和三连豌豆两个垂直切片通过自动测试与进游戏验证。
- 每个新增 Hook 均有 1051 版入口字节校验、调用约定说明和失败回退。

## 21. 最终边界

该方案不是“彻底不用 JSON”，也不是“所有复杂功能都塞进 Lua”。它提供清楚的三级升级路径，并由 `pvzmod.dll` 统一托管。

以后判断功能应该放在哪里时使用一条简单规则：

- **数值、资源、ID、绑定，以及一个事件映射一个固定效果**放有限 JSON；
- **条件、循环、状态机、技能组合**放 Lua；
- **新通用能力、高频原生算法、外部原生库**放可信 DLL 插件；
- **核心 Hook、对象生命周期、原版函数桥接和全局安全边界**由 `pvzmod.dll` 管理。

三层扩展共享同一个版本化 API 和事件中心。这样能力只实现一次即可被几十个 Lua、DLL 和 JSON 规则复用，事件也能容纳上百个可追踪订阅者，而不是让技能数量与专用 API、独立 Hook 或散乱配置数量一起线性增长。
