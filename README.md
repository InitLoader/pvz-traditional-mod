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
- `F11` — 独立中文动画制作器：分层画布、时间轴、K 帧、位移/旋转/缩放/透明度补间、Raw/compiled 读取与重打包、动作分类、PNG 绑定、JSONC/ZIP 打包和一键安装。

### 🟡 部分实现

- `P01` — 自定义植物拥有独立逻辑 ID、名称、介绍、阳光、冷却、生命、射速和投射物属性；当前动画、动作与目标选择仍复用 `templatePlantId`，尚未接入原创资源和全新行为树。
- `P02` — 随机附加防具已适配路障、铁桶、铁门和普通僵尸坚果头；梯子、报纸、气球等跨僵尸类型套用时仍缺少对应动作、挂点和视觉适配，不能视为完整支持。
- `P03` — 关卡配置已能改变出怪和数量，但还不是完整关卡编辑器，不能新建地图网格、背景、关卡流程或胜负条件。
- `P04` — UI 已完成选卡分页按钮和自定义卡片交互；通用设置页、主菜单入口、图鉴和完整界面改造尚未完成。
- `T01` — 精英僵尸已完成实例编号与侧挂状态、确定性概率、属性倍率、技能事件、红色视觉标记和外部贴图引用；当前只有 `RAGE/BERSERK` 垂直切片，掉落和更多技能尚未实现。
- `P05` — 外部动作资源已完成 Raw `.reanim` 与原版 PC `.reanim.compiled` 自动读取、安全解析、动作/事件/定位轨道校验和只读注册表；尚未把自定义 Definition 注入原版 `ReanimationHolder`，因此暂时不会改变游戏内植物或僵尸动画。
- `P06` — 动画制作器会动态保留所有 `anim_*` 动作并支持眨眼/特殊动作编辑；当前补间为逐帧烘焙，尚未实现贝塞尔曲线、骨骼 IK、音频轨和完整撤销系统。

### ⬜ 尚未实现

- `T02` — 每个大关的独立 Boss、多阶段技能和 Boss 波控制器。
- `T03` — 真正的新僵尸类型、原创 AI、动画状态机和资源注册。
- `T04` — 新地图、背景、地图机制、完整新关卡和关卡选择入口。
- `T05` — 自定义植物/僵尸的原创动画、贴图、音效和资源热加载管线。
- `T06` — Mod 设置界面、可视化配置编辑器和游戏内调试面板。
- `T07` — Mod 独立存档、版本迁移、精英图鉴、Boss 进度和自定义解锁状态。
- `T08` — 自动打包 Release、安装器和多游戏版本适配。

详细设计和实现依据见 [`PVZ传统改版技术路线.md`](PVZ传统改版技术路线.md)。
外部动作制作、配置和真正新增实体的分阶段契约见 [`modding/EXTERNAL_ANIMATION_AND_CUSTOM_ENTITIES.md`](modding/EXTERNAL_ANIMATION_AND_CUSTOM_ENTITIES.md)。
动画制作器操作与边界见 [`modding/PvZAnimationStudio/README.md`](modding/PvZAnimationStudio/README.md)。

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
