# PvZ Traditional Mod

面向《植物大战僵尸》PC 版 `1.0.0.1051` 的传统 DLL/EXE Hook 改版工程。当前支持外部关卡出怪、僵尸权重与波次数量倍率、阳光价值、植物攻击、僵尸属性与防具、自定义植物逻辑卡片和选卡分页。

> 本项目是非官方爱好者 Mod，与 PopCap、Electronic Arts 无隶属或授权关系。仓库不提供游戏本体、原版素材、破解工具或 DRM 绕过。使用者必须自行持有合法游戏副本。

## 当前实现状态

标号说明：`✅` 已实现并完成进游戏验证；`🟡` 已有可用部分，但仍有明确限制；`⬜` 尚未实现，目前只有设计或预留接口。

### ✅ 已实现

- `F01` — `1.0.0.1051` 专用 DLL 加载、EXE 补丁器、版本和补丁点字节校验。
- `F02` — 按关卡稀疏覆盖出怪类型，支持僵尸 ID、相对权重、保底数量和确定性种子。
- `F03` — 全局、单关和单波僵尸数量倍率；未配置关卡继续使用原版规则。
- `F04` — 普通、小型和大型阳光价值全局覆盖。
- `F05` — 原版植物投射物与直接攻击伤害配置；未填写的攻击保持原版数值。
- `F06` — 僵尸本体生命、独立啃食伤害、防具概率和确定性抽取。
- `F07` — 原版 11 类独立防具生命池的数值覆盖，包括路障、铁桶、报纸、铁门、橄榄球头盔、雪橇、气球、矿工帽、梯子、坚果头和高坚果头。
- `F08` — 选卡分页、原版页与自定义页切换、原版式选入/退回动画、手型光标和工具提示。
- `F09` — 配置目录分类、JSON/JSONC 校验、无配置回退原版及运行日志。
- `F10` — 通用外部贴图注册表、受限路径校验、字符串 ID 查询、原版图片加载器与进程缓存；支持按僵尸 Reanimation 部位/轨道替换，贴图缺失时安全跳过。
- `F11` — 独立中文动画制作器：按原版矩阵/注册点/子帧规则预览、Blender 式 `G/R/S` 中心变换、时间轴/曲线空白处随时左键框选与批量移动删除、跨帧交换防吞帧、插入空帧及删除中间关键帧后自动重算连续运动、彩色曲线与可拉 Bezier 手柄、可拆分工作区和独立窗口、100 步 `Ctrl+Z`/`Ctrl+Y`、任意目录 compiled 文件头识别、内嵌图片/曲线/动作/属性/布局的 `.pvza` 单文件工程、JSONC/ZIP 打包和一键安装。
- 动作标记的 `f=0/-1` 使用阶梯插值并随空帧同步移动，修复动作范围已经结束、位移/缩放过渡却只播放一部分的问题；旧版错误生成的 `-0.x` 标记会在加载时自动清理。
- 时间轴支持 `Ctrl+C/Ctrl+V` 复制同一轨道的框选/当前关键帧，以及按钮复制当前动作范围内的整轨关键帧；粘贴保留相对间距、曲线和 Bezier 手柄，并可一次撤销。

### 🟡 部分实现

- `P01` — 自定义植物拥有独立逻辑 ID、名称、介绍、阳光、冷却、生命、射速和投射物属性；`animationId` 已能把原创 Definition 注入主体 Reanimation，并让模板状态机播放外部 `anim_idle/anim_shooting` 等同名轨道；目标选择与行为树仍复用 `templatePlantId`。
- `P02` — 随机附加防具已适配路障、铁桶、铁门和普通僵尸坚果头；梯子、报纸、气球等跨僵尸类型套用时仍缺少对应动作、挂点和视觉适配，不能视为完整支持。
- `P03` — 关卡配置已能改变出怪和数量，但还不是完整关卡编辑器，不能新建地图网格、背景、关卡流程或胜负条件。
- `P04` — UI 已完成选卡分页按钮和自定义卡片交互；通用设置页、主菜单入口、图鉴和完整界面改造尚未完成。
- `T01` — 精英僵尸已完成实例编号与侧挂状态、确定性概率、属性倍率、技能事件、红色视觉标记和外部贴图引用；当前只有 `RAGE/BERSERK` 垂直切片，掉落和更多技能尚未实现。
- `P05` — 外部动作资源已完成 Raw `.reanim` 与原版 PC `.reanim.compiled` 自动读取、安全解析、ABI Definition 构建和自定义植物主体动画注入；多 Reanimation 植物附件、独立眨眼、僵尸动画与通用事件控制器仍未接入。
- `P06` — 动画制作器会动态保留所有 `anim_*` 动作并支持眨眼/特殊动作编辑，已有跨轨道/通道框选、可持久化 Bezier 曲线手柄和 100 步撤销/恢复；当前尚未实现骨骼 IK、曲线修改器和音频轨。

### ⬜ 尚未实现

- `T02` — 每个大关的独立 Boss、多阶段技能和 Boss 波控制器。
- `T03` — 真正的新僵尸类型、原创 AI、动画状态机和资源注册。
- `T04` — 新地图、背景、地图机制、完整新关卡和关卡选择入口。
- `T05` — 自定义僵尸、植物附属 Reanimation、原创音效和资源热加载管线。
- `T06` — Mod 设置界面、可视化配置编辑器和游戏内调试面板。
- `T07` — Mod 独立存档、版本迁移、精英图鉴、Boss 进度和自定义解锁状态。
- `T08` — 自动打包 Release、安装器和多游戏版本适配。

详细设计和实现依据见 [`PVZ传统改版技术路线.md`](PVZ传统改版技术路线.md)。
外部动作制作、配置和真正新增实体的分阶段契约见 [`modding/EXTERNAL_ANIMATION_AND_CUSTOM_ENTITIES.md`](modding/EXTERNAL_ANIMATION_AND_CUSTOM_ENTITIES.md)。
技能与后续自由行为采用三级扩展规划：有限 JSON 处理静态配置和“一事件一效果”的微型功能，Lua 处理有状态/组合玩法，可信 Win32 DLL 从固定插件目录加载并通过版本化 Host API 提供原生能力。JSON、Lua、内置代码和多个 DLL 共用事件、Capability、所有者和配置注册中心，并把“1 小时完成首个技能、10 小时熟悉常用能力、30 小时近乎掌握脚本玩法层”作为硬性验收目标；完整方案见 [`modding/SCRIPTABLE_SKILLS_AND_BEHAVIORS.md`](modding/SCRIPTABLE_SKILLS_AND_BEHAVIORS.md)。该运行时目前仍是设计稿，尚未实现。
动画制作器操作与边界见 [`modding/PvZAnimationStudio/README.md`](modding/PvZAnimationStudio/README.md)。
原版 48 个独立植物动画与 38 个僵尸/僵尸效果动画的逐项审计见 [`modding/PvZAnimationStudio/ORIGINAL_ASSET_AUDIT.md`](modding/PvZAnimationStudio/ORIGINAL_ASSET_AUDIT.md)。
原版植物 `SeedType 0–52`、可复用模板范围和动画资源映射见 [`modding/PvZAnimationStudio/PLANT_TEMPLATE_IDS.md`](modding/PvZAnimationStudio/PLANT_TEMPLATE_IDS.md)。

## 仓库内容

- `modding/src`：按功能拆分的运行时 Hook 模块。
- `modding/patcher`：仅支持已校验 `1.0.0.1051` 文件的补丁加载器。
- `modding/tests`：配置与生成逻辑回归测试。
- `modding/PvZAnimationStudio`：全中文 Reanimation 动画制作器和自动配置/打包器。
- `modding/PvZAnimationStudio.Tests`：Raw/compiled 往返、补间、JSONC 与打包自测。
- `pvzmod/config`：按关卡、植物、僵尸、UI 和全局设置分类的配置示例。
- `PVZ传统改版技术路线.md`：逆向结论、模块边界、配置规则与后续路线。
- `modding/ARCHITECTURE.md`：代码架构和 Hook 接入说明。
- `modding/EXTERNAL_ANIMATION_AND_CUSTOM_ENTITIES.md`：外部 Reanimation 制作、解析、动作事件和独立实体方案。
- `modding/SCRIPTABLE_SKILLS_AND_BEHAVIORS.md`：有限 JSON、Lua、原生 DLL 三级扩展，统一事件/Capability 管理、热重载和插件 ABI。

完整配置说明见 [`modding/README.md`](modding/README.md) 与 [`pvzmod/config/README.md`](pvzmod/config/README.md)。

## 构建

需要 Windows、Visual Studio 2022 C++ 工具链与 CMake 3.24 或更新版本：

```powershell
cmake -S modding -B build-win32 -A Win32
cmake --build build-win32 --config Release
ctest --test-dir build-win32 -C Release --output-on-failure
```

构建会下载固定版本的 nlohmann/json 与 MinHook。生成的二进制不会提交到仓库，应通过经过审核的 GitHub Release 分发。

## 安装概要

1. 准备合法的《植物大战僵尸》PC `1.0.0.1051` 游戏目录。
2. 构建 `pvzmod.dll` 与 `PvZModPatcher.exe`。
3. 将 DLL 和 `pvzmod/config` 复制到游戏目录。
4. 使用补丁器处理精确匹配的 EXE；不匹配的哈希或补丁点会被拒绝。
5. 日志写入 `pvzmod/logs/pvzmod.log`。

具体命令、备份行为和配置热重载规则请阅读 [`modding/README.md`](modding/README.md)。

## 开发流程

- `main` 只接收 Pull Request。
- 每次功能或修复使用独立的 `agent/<主题>` 或 `feature/<主题>` 分支。
- PR 必须同步代码、配置示例、技术文档和 `CHANGELOG.md`。
- PR 必须说明影响范围、逆向依据、崩溃风险以及实际验证步骤。
- 禁止提交游戏 EXE、DAT、DLL、图片、动画、音频、存档、日志或反编译生成物。

详细规则见 [`CONTRIBUTING.md`](CONTRIBUTING.md)。
