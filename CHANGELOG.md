# Changelog

本项目采用持续更新记录。每个 Pull Request 必须在这里增加面向使用者的变更摘要。

## Unreleased

### Added in 0.10.4-dev

- 增加僵尸主体外部 Reanimation 纵切：`zombies/attributes.jsonc` 可按原版僵尸 ID 稀疏填写 `animationId`，生成后在既有 body Holder 内安全替换 Definition 和 TrackInstance。
- 抽出植物/僵尸共用的 `external_body_animation_runtime`，统一精确版本 ABI、资源预构建、载体校验、初始动作和存档恢复，僵尸配置、动画生命周期和基础属性 Hook 继续分模块维护。
- 存档 Definition 恢复从“全局唯一动画”改为按原版 `carrierReanimation` 分类；植物与僵尸可同时使用不同载体的外部动画，同一载体出现两个 Definition 时拒绝后者并保留原版，避免读档歧义。
- 动画制作器安装僵尸工程时会把 `bodyHealth`、`attackDamage` 和 `animationId` 合并到实际生效的 `zombies/attributes.jsonc`，保留其他僵尸 ID、JSONC 注释并生成 `.pvzstudio.bak`。

### Validation in 0.10.4-dev

- Win32 Release `pvzmod.dll` 构建通过；C++ 配置回归和动画制作器控制台回归通过。
- 新增 `animationId` 合法/非法 ID 测试，以及 JSONC 僵尸对象新增、重复更新、注释保留和稀疏覆盖测试。

### Planned documentation for 0.11.x

- 设计有限 JSON、Lua、可信 Win32 DLL 三级扩展体系：小型“一事件一效果”功能保留 JSON，复杂状态与组合使用 Lua，原生能力由 `pvzmod/plugins/native/<plugin-id>/` 中的版本化插件提供。
- 设计统一 `ExtensionHub`，让 JSON、Lua、内置 C++ 和多个 DLL 共用 Event、Capability、命令缓冲、配置 Schema、所有者/世代和事务加载；区分“一个 Capability Provider”和“一个事件多个订阅者”。
- 为几十到上百个处理器定义不可变订阅快照、稳定阶段/优先级顺序、过滤器、订阅 Token、配额、热点统计及 100 个混合订阅者压力测试。
- 定义 `PvZModHostApiV1` 双向 C ABI、固定插件目录、manifest/PE32/hash/依赖校验、受限 DLL 搜索、共享 Capability 和不支持二进制热卸载的生命周期边界。
- 把 1 小时完成首个技能、10 小时熟悉常用能力、30 小时近乎掌握脚本玩法层写为产品验收目标，并规划配方 API、Mock 实验场、LuaLS 补全、中文诊断和创建绑定向导。
- 增加当前基础架构的大规模扩展审计：核对植物/精英/贴图/动画/选卡的现有限制，分析大量自定义植物、僵尸、子弹、UI 和事件订阅导致的 ID 冲突、热路径乘法、裸指针生命周期、32 位资源耗尽、热重载半世代和 UI Hook 竞争，并定义 `ContentCatalog`、预编译索引、资源预算、统一 `UiHost`、压力矩阵及开放 Lua/DLL 前的 P0/P1 门槛。
- 把工具边界拆成 `PvZAnimationStudio`、独立 `PvZLuaStudio`、纯管理型 `PvZModManager` 和运行时 `pvzmod.dll`：LuaStudio 独占代码创建/编辑/验证/打开，Manager 只管理脚本资产元数据、包、配置、DLL、资源、安装和回滚。
- 本节仅记录设计文档变化；当前 `pvzmod.dll` 尚未实现 JSON MicroRule、Lua 宿主或外部 DLL 插件加载。

### Added in animation-studio-dev

- 增加自定义植物 `animationId` 运行时链路：把 Raw/compiled 转换为 1.0.0.1051 ABI Definition，在原 Holder 内安全替换 body Reanimation，使模板攻击状态机播放外部 `anim_*` 轨道；失败时保留原模板动画。
- 植物模板 ID 修改时自动同步编辑器载体 Reanimation，避免香蒲模板仍导出豌豆载体；文档明确主体、附属头部和眨眼的当前支持边界。
- 增加全中文 `PvZAnimationStudio` 独立桌面工具，提供分层预览、时间轴、K 帧、拖动、旋转、缩放、透明度、线性/平滑补间和播放。
- 支持新建植物/僵尸工程，动态识别所有 `anim_*` 动作，分类常用植物、僵尸、Boss、特殊动作和眨眼。
- 支持 Raw `.reanim` 和原版 PC `.reanim.compiled` 读取、编辑、导出与重新打包，以及 PNG 资源绑定。
- 增加 Mod ZIP、贴图/动画/实体 JSONC 片段生成和带 `.pvzstudio.bak` 的一键配置安装。
- 增加无第三方测试项目，覆盖格式往返、关键帧补间、JSONC 注释保留和 ZIP 内容。
- 增加最近 100 步会话级编辑历史：`Ctrl+Z` 连续撤销，`Ctrl+Y`/`Ctrl+Shift+Z` 按原顺序恢复；画布变换、K 帧、补间、帧/轨道/动作、工程属性、FPS 和图片导入共用同一历史。
- 增加原版精确预览数学、动作局部时间轴和完整实体组合预览，覆盖 GatlingPea、SplitPea、ThreePeater 与普通僵尸可选装备等特殊结构。
- 增加 JPG + 灰度 PNG 透明蒙版读取，兼容原版 Boss、Crazy Dave 等资源配对方式。
- 增加 Blender 式可拆分工作区：区域分隔线可拖动，每区可切换动画视图、时间轴、资源浏览器或属性检查器，并支持双视图、双时间轴、专注预设和共享工程状态的独立窗口。
- 增加 `.pvza` 单文件便携工程，嵌入动画、动作、实体属性、工作区布局、全部引用图片及 `cols/rows` 子帧信息；旧 `.pvza.json` 保持读取兼容。
- compiled 打开改为文件头优先识别并扩大文件选择过滤器；导出明确调用所选格式编码器，修复文件位于图片目录或扩展名异常时无法重新选择的问题。
- 所有工作区、区域类型和动作选项下拉框改为统一深色模板，增加圆角边框、独立箭头区以及绿色悬停/选中反馈，不再显示突兀的白色系统选择框。
- 动画视图支持直接拖入外部 PNG/JPG/JPEG：按落点创建并选中视觉轨道，多图自动错开放置，随后可直接 K 帧和补间；图片注册与全部新轨道合并为一次可撤销编辑。
- 增加全局植物模板目录和完整文档，登记原版 `SeedType 0–52`、中文名、Reanimation 与 compiled 映射；属性检查器可速选当前安全复用的 `0–48`，打包时拒绝未知 ID 和模式专用 `49–52`。

### Validation in animation-studio-dev

- WPF Release 构建零警告通过。
- 本地 `compiled/reanim` 的 143 个原版 compiled 文件全部通过“读取 → compiled 重打包 → Raw 导出 → 两种格式再读取”回归，包括原版合法同名轨道；C++ 运行时校验器也全部接受。
- 全量审计 48 个独立植物动画和 38 个僵尸/僵尸效果动画，图片解析缺失为 0；逐项结果、特殊组合和预期空动画记录在 `ORIGINAL_ASSET_AUDIT.md`。
- `.pvza` 往返测试会先删除原始 PNG，再验证动画、动作、属性、工作区、嵌入图片和子帧布局仍可完整恢复；另覆盖无标准扩展名 compiled 的 Cookie 识别。
- 时间轴关键帧支持直接横向拖动、逐帧左移/右移和独立删除，并与“插入/删除整帧”明确分离；全部操作进入 Ctrl+Z/Ctrl+Y 历史。
- 新增 Blender 风格曲线编辑器与“曲线动画”工作区：八类数值通道使用固定颜色和顶部图例，支持拖动关键点、Bezier 左右手柄、自动/对齐/自由/矢量手柄及 Bezier/线性/常量插值。
- `.pvza` schema 升级到 3，保存曲线关键点、数值、插值和手柄；编辑结果同步烘焙为普通 Reanimation 帧，Raw/compiled 继续保持原版兼容。
- 修复时间轴与曲线关键帧拖过相邻关键帧时沿途合并/吞帧：碰撞改为交换重排，连续跨过多个关键帧仍保留全部关键点和值。
- 时间轴与曲线编辑器增加常驻框选：空白位置直接左键拖动即可跨轨道/通道选择，无需前置快捷键；支持 `Ctrl`/`Shift` 追加选择、批量移动和 `Delete`/`Backspace` 批量删除。
- 动画画布增加 Blender 式 `G/R/S` 鼠标模态变换；左键确认，右键或 `Esc` 取消，旋转根据鼠标方向并围绕图片视觉中心，同时补偿 Reanimation 左上注册点造成的位置漂移。
- 修复在两个关键帧之间插入空帧后运动冻结：插入/删除整帧及设置 K 帧后自动重烘焙位移、旋转、缩放与透明度；旧 compiled 会从显式运动帧补建曲线，但保持图片切换和动作标记离散。
- 修复删除中间关键帧后播放保持在起点、到终点瞬间跳变：单个删除和框选批量删除都会在清空前捕获旧 compiled 的显式运动曲线，再用剩余两端重新烘焙连续补间。
- 修复动作标记 `f=0` 到 `f=-1` 被 Bezier 烘焙成负小数，导致动作提前结束而运动补间只播放一部分：纯 `anim_*` 标记强制常量插值，插入/删除空帧时标记终点与运动终点同步移动，并自动修复旧工程中的 `-0.x` 标记。
- 增加单轨关键帧复制/粘贴：支持框选或当前关键帧 `Ctrl+C/Ctrl+V`、当前动作范围整轨复制、跨视觉轨道粘贴、相对间距/曲线类型/Bezier 手柄/离散图片保留，以及一次性撤销恢复。

### Added in 0.10.1-dev

- 增加原版 PC `.reanim.compiled` 直接读取：校验 `DEADFED4` 外层、zlib 解压、`B393B4C0` Schema 和 16/12/44 字节缓存结构。
- 增加 Raw/compiled 自动格式分流，并允许只读引用 `compiled/reanim/*.reanim.compiled` 或加载 `pvzmod/animations/` 下的自制 compiled。
- 增加 `PvZReanimValidator` 命令行工具和原版 `Blover.reanim.compiled` 配置示例。

### Safety in 0.10.1-dev

- compiled 解码器不使用缓存内旧进程指针，限制压缩/解压大小、轨道、帧、Transform 和字符串数量，并拒绝结构尺寸、Schema、长度或尾随数据不匹配的文件。

### Added in 0.10.0-dev

- 增加 `resources/animations.jsonc` 与 `pvzmod/animations/` 分类外部动作资源目录。
- 增加 Raw `.reanim` 安全解析器，支持 FPS、轨道和 `x/y/kx/ky/sx/sy/f/a/i/font/text` Transform。
- 增加动作循环、速率、混合帧、帧事件、定位轨道和外部贴图 ID 的启动时交叉校验。
- 增加只读外部动画注册表和制作示例；当前阶段不安装 Reanimation ABI 注入 Hook。
- 增加外部动作制作、独立植物/僵尸、存档和后续运行时注入技术文档。

### Safety in 0.10.0-dev

- 动画路径被限制在 `pvzmod/animations/`，拒绝目录穿越、DTD/实体、非有限浮点、重复字段、未知字段、轨道帧数不一致和超限文件。
- 单个动画校验失败时只跳过该动画并写日志，不会把半初始化 Definition 交给原版游戏。

### Added in 0.9.0

- 增加精英僵尸数字编号、`Zombie* + instanceId` 侧挂状态和可注册技能事件接口。
- 增加通用外部贴图注册表，ID 支持英文字母、数字和下划线；路径被限制在 `pvzmod/images` 内，并复用原版图片加载器和进程缓存。
- 增加 `RAGE` 狂暴僵尸垂直切片：普通僵尸按确定性概率转为精英，`BERSERK` 可配置生命、速度和攻击倍率，并支持红色 tint 与 `KILL` 外部贴图引用。
- 增加 `scope/target/track/textureId` 部位替换配置，通过原版 Reanimation 图片轨道替换头、身体、手脚、防具或高级自定义轨道；附带普通僵尸 30 个图片部位与特殊僵尸轨道目录。
- 增加僵尸初始化和攻击伤害事件总线，基础僵尸 Hook 不再直接依赖精英模块。

### Validation

- Release Win32 构建和配置回归测试通过。
- 在 `1-10` 实际关卡验证普通僵尸逐实例精英化和红色视觉效果；未命中的实例保持原版外观。
- 未提供 `pvzmod/images/zi/kill.png` 时，运行时仅记录一次警告并继续绘制、更新和出怪。
- 提供 64×64 `KILL` PNG 后，已验证原版加载器成功解码，并将普通僵尸 `anim_head1` 真正替换为该图片；图片随头部轨道移动、旋转和缩放。

### Fixed in 0.9.0

- 移除不能表达部位语义的固定坐标覆盖绘制，改为 `Reanimation::SetImageOverride` 轨道替换。
- 修复初版轨道替换把优化后的内部函数误当作普通 `__thiscall` 而在 `0x00453CB8` 崩溃的问题；桥接器现按 1051 二进制实际使用的 `EAX/ECX`、`EBX+栈` 和 `ECX/EAX+栈` 约定传参。

### Documentation

- 在仓库首页增加 `✅ / 🟡 / ⬜` 实现状态表，并为已实现、部分实现和尚未实现的功能分配稳定编号。
- 明确自定义植物模板复用、防具跨类型视觉适配和关卡/UI 扩展的当前边界。

## 0.8.3 - 2026-07-20

### Added

- 支持最多 40 张/页的自定义植物逻辑卡片与选卡翻页。
- 自定义植物名称、介绍、阳光、冷却、血量、发射物和攻击配置。
- 自定义卡片使用原版式选入/退回动画、手型光标和工具提示。

### Changed

- 自定义页改为真正的绘制分流，原版候选卡不会在下一图层绘制。
- 返回第 0 页时恢复完整的原版卡片绘制、位置与交互。
- 运行时代码按出怪、倍率、阳光、植物攻击、僵尸、选卡和自定义植物拆分模块。

### Existing systems

- 关卡僵尸权重与稀疏覆盖。
- 全局、关卡和单波僵尸数量倍率。
- 全局阳光价值覆盖。
- 植物攻击伤害覆盖。
- 僵尸基础属性、防具概率与伤害配置。
