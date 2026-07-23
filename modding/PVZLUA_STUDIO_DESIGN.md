# PvZLuaStudio 独立 Lua 行为编辑器方案

> 状态：设计稿，尚未实现。`PvZLuaStudio` 是独立桌面应用，不内嵌在 `PvZModManager` 或 `PvZAnimationStudio` 中。

## 1. 产品职责

`PvZLuaStudio` 专门负责玩法 Lua 的作者体验：

- 新建、打开、编辑和保存普通 `.lua` 文件；
- 创建技能/控制器模板和最小绑定描述；
- Lua 语法、API、Capability、事件字段和模块结构校验；
- LuaLS 补全、悬浮说明、定义/引用跳转和中文诊断；
- 隔离 Mock 运行、事件输入、`ctx.state`、命令缓冲和预算分析；
- 连接 `pvzmod.dll` 查看脚本世代、错误、回调与耗时，并请求安全点热重载；
- 根据运行时错误的文件、行、列打开准确代码位置。

它不负责：

- Reanimation 时间轴、曲线、图片部件和 compiled 编解码；这些属于 `PvZAnimationStudio`；
- 包安装、启用集、DLL 部署、全部 JSONC、资源库、依赖冲突和回滚；这些属于 `PvZModManager`；
- Hook、对象偏移、裸指针、原版调用约定和插件加载；这些属于 `pvzmod.dll`；
- 取代最终运行时安全校验。编辑器通过不代表脚本在版本不匹配或缺能力的运行时中必须加载。

## 2. 与其他组件的关系

```text
PvZAnimationStudio
  └─ animation-asset.jsonc / action / locator / event 摘要
                         │
                         ▼ 只读补全上下文
PvZLuaStudio ──输出──> Lua + module manifest + binding fragment + validation report
                         │
                         ▼
PvZModManager ──只管理──> 包归属 / 版本 / 哈希 / 启用 / 安装 / 回滚
                         │
                         ▼
pvzmod.dll ──再次校验──> 加载 / 事件 / Capability / 预算 / 安全点重载
```

三个工具共享轻量 `PvZMod.Contracts`，但不共享 UI/ViewModel：

- LuaStudio 可读取管理器生成的只读内容摘要，知道有哪些植物、僵尸、子弹、动画动作和 Capability 可绑定；
- LuaStudio 写普通源文件、窄范围绑定片段和带源码哈希的校验报告；
- Manager 只盘点这些文件和摘要，不解析 Lua AST，不显示或打开 Lua；
- Runtime 不信任外部报告，实际加载时重新执行路径、API、能力和预算校验。

## 3. 源文件和交换文件

推荐包内布局：

```text
author.package/
├─ package.jsonc
├─ scripts/
│  ├─ skills/
│  │  ├─ plants/
│  │  ├─ zombies/
│  │  └─ projectiles/
│  ├─ controllers/
│  └─ lib/
├─ bindings/
│  └─ behaviors.jsonc          # 窄范围行为绑定；不是完整 Mod 配置中心
├─ script-modules.jsonc        # 模块 ID、文件、API、requires 和哈希
├─ reports/
│  └─ lua-validation.json      # 可删除、可重建，不是运行时事实来源
└─ tests/
   └─ mock-scenarios.jsonc
```

Lua、JSONC 和报告 Schema 全部公开。删除 `reports/` 后，LuaStudio/CI 可以从源码重建；没有任何脚本被塞进私有数据库。

`bindings/behaviors.jsonc` 只允许表达：

```text
targetContentId
behaviorId
parameters
enabled
required
```

它不编辑植物数值、贴图、DLL、关卡或包安装状态。最终合并、冲突判断和安装由 Manager 完成。

## 4. 编辑器界面

```text
┌────────────────────────────────────────────────────────────────────┐
│ 工作区 / API 版本 / Mock / 连接游戏 / 安全点重载 / 当前世代       │
├───────────────┬────────────────────────────────┬───────────────────┤
│ Lua 工程树     │ 多标签代码编辑器                │ API / 事件 / 绑定 │
│               │                                │                   │
│ Skills        │ 普通 Lua 源码                   │ ctx 字段          │
│ Controllers   │ 行号 / 折叠 / Minimap           │ Capability        │
│ Lib           │ 补全 / 悬浮 / 跳转              │ Target Content    │
│ Bindings      │ 错误波浪线 / Quick Fix          │ 参数 Schema       │
│ Tests         │                                │                   │
├───────────────┴────────────────────────────────┴───────────────────┤
│ Problems / Mock Events / State / Commands / Runtime / Performance  │
└────────────────────────────────────────────────────────────────────┘
```

核心页面：

- **工程树**：只显示 Lua、绑定片段、Mock 场景和模块描述；
- **代码编辑器**：Lua 语法、补全、格式化、搜索替换、重命名和跳转；
- **API 浏览器**：按“查询目标、生成子弹、伤害、动画、计时、UI”等用途组织；
- **事件检查器**：事件阶段、同步/延迟、字段可写性、频率和预算；
- **绑定检查器**：从只读 Content 摘要选择目标，生成窄范围 binding fragment；
- **Mock 面板**：事件、实体快照、随机种子、状态、命令和断言；
- **运行时面板**：脚本世代、热重载、错误、调用次数、P95/P99 和最近命令。

## 5. “创建并绑定技能”属于 LuaStudio

一次操作流程：

1. 选择目标包工作区；
2. 选择行为类型：植物、僵尸、子弹、精英、Boss、关卡或 UI 控制器；
3. 从 Manager 生成的只读 Content 摘要选择目标，或创建暂未绑定模块；
4. 填写 `behaviorId`、模板和首批参数；
5. 预览将创建的 Lua、module manifest 和 binding fragment；
6. 一个文件事务写入全部普通文本文件；
7. 运行快速语法/API 校验；
8. 打开 Lua 中第一个明确的 TODO 待编辑位置。

如果绑定目标不存在、属于未满足依赖或当前运行时不支持所需事件，向导必须在写文件前说明；允许用户选择“只创建未绑定模块”，不能悄悄生成失效绑定。

管理器稍后通过文件监视发现新模块和 binding fragment，把它们当受管资产纳入包关系图。管理器不重复创建或打开代码。

## 6. 分层验证

LuaStudio 负责的验证分四层：

### 6.1 语法

- 使用与运行时锁定的同一 Lua 5.4 小版本；
- 错误包含文件、行、列、诊断代码和恢复建议；
- 未保存缓冲区也能增量解析，不要求先写入磁盘。

### 6.2 模块与绑定结构

- `pvz.skill`/控制器返回结构、事件名、ID 和参数 Schema；
- 重复行为 ID、循环 `require`、越界路径、缺文件和未使用模块；
- binding target、参数类型、required/optional 和包依赖。

### 6.3 API 与 Capability

- `apiVersion`、事件允许的 `ctx` 字段、同步可写字段和命令限制；
- `requires` 与已选择运行时/插件档案的能力集合；
- 拼写建议，例如 `ctx.slef` 建议为 `ctx.self`；
- 高频事件中明显危险的全局查询、无界循环和日志调用给出静态警告。

### 6.4 Mock 动态验证

- 在独立受限进程使用运行时同版 Lua；
- 指令、时间、内存、命令数、递归、日志和随机种子有硬上限；
- 捕获事件输入、状态变化、命令输出和断言；
- 测试超限只终止本场景，不崩溃编辑器；
- 结果可保存为普通 `mock-scenarios.jsonc` 并进入 CI。

静态校验不能证明脚本安全，Mock 也不能证明所有游戏状态。`pvzmod.dll` 在加载和每次调用时仍执行自己的句柄、范围、能力和预算检查。

## 7. Headless 工具

GUI 与 CI 共用两个独立程序：

```text
PvZLuaValidator.exe   # 语法、模块、绑定、API 和 Capability
PvZScriptLab.exe      # 隔离 Lua VM、Mock、预算和确定性测试
```

LuaStudio 调用它们并解析结构化 JSON 结果。Manager 不调用它们来打开或验证代码；它只读取可选报告摘要：

```jsonc
{
  "schemaVersion": 1,
  "tool": "PvZLuaValidator",
  "toolVersion": "0.11.0",
  "sourceTreeHash": "sha256:...",
  "apiVersion": 1,
  "result": "passed",
  "errors": 0,
  "warnings": 2
}
```

源码哈希不匹配时，Manager 只能显示“外部报告已过期”，不能自行重新验证或仍标成通过。

## 8. 补全与 API 文档

LuaStudio 随运行时版本加载：

- LuaLS 类型文件；
- 事件/字段机器可读 Schema；
- Capability 描述、参数、返回值、失败码和是否脚本安全；
- Cookbook 与编号示例；
- 已启用 DLL 提供的 `SCRIPT_SAFE` 能力描述；
- 动画资产导出的 action、event 和 locator ID 摘要。

补全来源带版本和 owner。卸载插件或切换运行时档案后，相关能力保留为错误/缺失提示，不能自动把代码改写为另一个 API。

## 9. 运行时开发连接

LuaStudio 使用版本化命名管道连接游戏，不读取进程内存：

- 查询运行时/API 版本和当前脚本世代；
- 请求某个 Lua owner 在 Board 安全点热重载；
- 接收结构化语法/运行错误、文件、行、列和堆栈；
- 查看事件、状态摘要、命令缓冲、回调次数和耗时；
- 在运行错误上双击跳转到准确代码；
- 关闭开发模式后停止传输详细状态和源码路径。

LuaStudio 不安装 DLL、不替换 `pvzmod.dll`、不写原版内存、不暴露任意 Hook 或地址调用。运行时拒绝重载时，编辑器保留文件并清楚显示上一有效世代仍在运行。

## 10. 错误和诊断

诊断格式至少包含：

```text
severity / code / message
file / line / column / length
ownerId / behaviorId / generation
event / capability / mock scenario
suggestions / related locations
```

例子：

```text
PVZLUA1203  triple_pea.lua:8:9
ctx.slef 不存在；是否想写 ctx.self？
本次 Mock 攻击已停止，未提交任何游戏命令。
```

同一根因不能刷出数千条重复问题。Problems 按源码位置聚合，Runtime 按 owner/event/message key 限速，并显示被折叠数量。

## 11. 与 Manager 的严格边界

Manager 可以管理：

- Lua 文件是否属于包、安装到哪里、哈希和版本；
- 模块 ID、manifest 声明、绑定关系和启用状态；
- 外部报告是否存在、是否与源码哈希匹配、通过/失败摘要；
- 安装、卸载、启用、禁用、依赖影响和回滚。

Manager 不能：

- 显示、编辑、格式化或搜索 Lua 源码；
- 调用 Lua 语言服务给出语法/API/类型结论；
- 运行 Mock、打开错误行或承担代码跳转；
- 因为报告显示通过就绕过 Runtime 的加载校验。

LuaStudio 也不能反向接管 Manager 的全部配置、资源、DLL、依赖或安装档案。它只写 Lua、模块描述、窄范围 binding fragment、测试和报告。

## 12. 学习曲线

### 60 分钟

| 时间 | 操作 | 反馈 |
| --- | --- | --- |
| 0–10 分钟 | 打开示例工作区和 `hello_pea.lua` | API/事件/Mock 全部可见 |
| 10–20 分钟 | 修改发射数量和伤害 | 保存即完成增量校验，Mock 命令变化 |
| 20–35 分钟 | 使用“创建并绑定技能” | Lua、module、binding 一次生成并打开 TODO |
| 35–50 分钟 | 增加条件和计时 | State/Events/Commands 面板显示过程 |
| 50–60 分钟 | 故意制造错误并修复 | 中文诊断跳到行列，最终报告通过 |

60 分钟验收仍是关闭教程后独立完成不同于示例的小技能。安装和启用包可随后在 Manager 完成，不把学习 Manager 全部功能算进 Lua 入门时间。

### 10 小时与 30 小时

10 小时目标覆盖常用事件、状态、查询、伤害、生成、动画、参数化、测试和性能提示。30 小时目标覆盖多阶段状态机、模块复用、事件顺序、状态迁移、确定性和性能预算。DLL ABI、逆向和 Hook 不属于 LuaStudio 学习目标。

## 13. 安全和性能

- 打开工作区不执行 Lua；只有明确点击 Validate/Mock/Reload 才进入受限进程或运行时；
- `require` 限制在当前包声明目录，拒绝绝对路径、`..`、reparse escape 和动态库；
- 大工作区增量解析，未打开文件按摘要索引；
- Problems、符号、引用和搜索使用可取消后台任务；
- Runtime/Mock 输出有队列、速率和大小上限；
- 自动保存保留恢复文件，但不把未保存代码发送到游戏，除非用户明确启用临时 Mock；
- 所有生成/绑定操作先显示文件计划，并以单个可回滚事务写入。

## 14. 分阶段实施

### L0：语言核心

- 锁定 Lua 版本，建立 parser、模块 Schema、API 描述和结构化诊断；
- 完成 `PvZLuaValidator` CLI 与合法/非法示例。

### L1：编辑器和创建向导

- 多文件编辑、LuaLS、补全、跳转、Problems；
- 创建 Lua/module/binding 的事务向导；
- 读取 Manager 的只读 Content 摘要。

### L2：Mock 实验场

- 隔离进程、事件输入、状态、命令、断言、预算和确定性测试；
- 示例与 CI 使用相同场景文件。

### L3：运行时连接

- 世代、热重载、错误跳转、事件摘要、耗时和 owner 诊断；
- 运行时失败保持上一有效世代。

### L4：大型工作区和发布报告

- 增量索引、跨文件引用、重命名、性能检查和报告签名/哈希；
- 与 Manager 的资产清单和外部报告摘要契约稳定化。

## 15. 验收标准

- LuaStudio 是独立可执行程序，Manager 和 AnimationStudio 都不内嵌它；
- 一次操作可以创建 Lua、module manifest、binding fragment，验证并打开待编辑位置；
- Lua 源码始终是普通文本，任意编辑器可修改；
- GUI、CLI、CI 和 Runtime 使用同一 Lua/API 版本与测试夹具；
- Mock 死循环、超内存、越界路径和错误命令不能崩溃编辑器；
- 运行错误能准确跳到文件、行、列，并显示运行世代；
- Manager 只能看资产元数据和报告摘要，不能显示、验证或打开代码；
- 外部报告哈希过期时明确失效，Runtime 永远重新校验；
- 1 小时、10 小时、30 小时目标均由 LuaStudio 体验和可重复用户测试验收。

## 16. 最终边界

动画制作、Lua 编程、Mod 管理和游戏运行时是四种不同工作。拆成独立工具以后，每个工具可以做深而不互相污染：AnimationStudio 不变成 IDE，LuaStudio 不变成安装器，Manager 不变成代码编辑器，Runtime 不变成桌面工具。
