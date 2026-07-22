# PvZ Mod 运行时模块架构

## 动画制作器边界

`PvZAnimationStudio` 是独立 WPF 工具，不链接或注入 `pvzmod.dll`。`Models` 保存可序列化工程；`RawReanimCodec`/`CompiledReanimCodec` 只做格式往返；`ReanimationRenderMath` 复现原版矩阵、左上注册点和资源子帧语义；`ActionCatalogService` 与 `ActionViewService` 分别负责动作识别和动作局部视图；`AnimationCurveService` 维护曲线关键点、Bezier 手柄、插值求值和向 PVZ 普通帧的烘焙；`EntityPreviewProfileService` 负责原版多 Reanimation 组合与普通僵尸可选装备过滤；`OriginalResourceService` 只索引图片并合成原版 JPG + 灰度透明蒙版；`ProjectFileService` 负责旧 JSON 和安全受限的 `.pvza` 单文件工程，保存时嵌入全部引用图片及子帧布局；`WorkspaceHostControl` 只维护可拆分区域树、比例和独立窗口，动画视图、时间轴、曲线编辑器、资源浏览器和属性检查器仍是独立控件；`EditHistoryService`/`ProjectCloneService` 保存会话级完整撤销与恢复；`TweenService` 只烘焙数值补间；`ProjectPackageService` 只生成分类资源、配置片段和安装合并。UI、工作区树、曲线数学、渲染数学、编解码、历史、补间、资源索引与打包禁止并入一个类。

`.pvza` 是 ZIP 容器，但只能包含根目录 `project.json` 和受限的 `assets/` 图片。读取端限制文件数量、单图大小、总大小并拒绝 `..` 和非 `assets/` 路径；图片解压到按工程路径、长度和修改时间散列出的本地缓存。保存端把原版 JPG+灰度遮罩先合成为带 Alpha 的 PNG，连同 `cols/rows` 一起写入工程。曲线关键点、属性值、插值类型及左右手柄保存在 schema 3 的 `project.json`；Raw/compiled 本身没有 Bezier 元数据，因此导出前使用已烘焙的逐帧值。工作区是可序列化的二叉拆分树；GridSplitter 只更新比例，区域类型、拆分、关闭和独立窗口不侵入动画模型。

插入整帧前，`AnimationCurveService.CaptureExplicitMotionCurves` 只从非动作轨道的 `x/y/kx/ky/sx/sy/a` 显式值补建缺失曲线；禁止自动为图片子帧 `f`、图片符号或动作标记建立平滑曲线。帧索引移动完成后，`BakeAllCurves` 重算所有已有曲线，使插入的空帧获得连续运动值；删除整帧后同样重算。设置 K 帧也必须立即烘焙关联曲线，不能只保存编辑器元数据而让预览继续继承上一帧。

预览必须区分“完整实体审计”和“单动作编辑”。完整视图可按配置叠加豌豆头、三线射手三头等原版附属动作，并隐藏普通僵尸骨架上未启用的路障、铁桶、铁门等可选装备；选中动作后只显示该动作影响的轨道与关键帧。所有会改变工程内容的入口——画布变换、K 帧、补间、帧/轨道/动作增删、动作参数、实体属性、FPS 和图片绑定——必须进入同一最多 100 步的会话历史，`Ctrl+Z` 逐项撤销，`Ctrl+Y`/`Ctrl+Shift+Z` 按原顺序恢复；撤销后发生新编辑时丢弃旧恢复分支。

时间轴多选使用 `(trackId, frame)`，曲线多选使用 `(channel, frame)`，选择状态只属于编辑控件，不写入工程文件。`EditorViewModel` 的批量移动接口按移动方向逐格交换源/目标关键帧，禁止在跨越目标帧时删除已占用关键点；这等价于关闭 Blender Graph Editor 的 Auto-Merge Keyframes。框选是时间轴和曲线区的常驻命中分支：左键命中关键点时选择/拖动，左键命中空白区域时直接开始框选，不依赖前置快捷键；框选可覆盖多轨道或多通道。批量移动/删除必须由一个编辑事务包裹，只产生一个撤销记录。画布 `G/R/S` 是鼠标模态变换，旋转时 `AnimationPreviewControl` 传入图片局部中心，`EditorViewModel.RotateSelectedAround` 同步补偿 `x/y`，确保视觉中心在旋转前后不漂移。

编辑器输出必须经过独立运行时管线才能进入游戏。它不能绕过 `custom_reanim_definition`、`animation_instance_hook`、动作事件总线或自定义实体侧挂状态，也不能因 UI 中存在“新僵尸”表单就宣称游戏端已经支持新僵尸。

## 0.10.1 原版 compiled 资源读取

`compiled_reanim` 独立负责 PC `.reanim.compiled` 的 Cookie、zlib、Schema 和固定结构解码；编辑器打开文件时优先嗅探 `DEADFED4` Cookie，再按完整后缀分流，运行时 `reanim_loader` 仍执行路径与后缀白名单。两种输入最终都转换为同一个规范化 `RawReanimDefinition`，因此动作、事件、挂点和后续 Definition 注入不需要维护两套逻辑。缓存中的指针字段只按布局跳过，禁止解引用。

## 0.10.0 外部动作资源边界

`external_animation_config` 只解析 `animations.jsonc` 元数据，`raw_reanim` 只负责受限 Raw XML、逐帧字段继承和结构校验，`external_animation_runtime` 只做启动时路径解析、贴图 ID/动作/事件/定位轨道交叉校验和只读注册。三者不得并回 `pvz_hook.cpp`；启动器仍只按顺序初始化贴图注册表和动画注册表。

当前阶段故意不安装 `ReanimationInitializeType` Detour，也不扩大原版 `ReanimationType` 数组。后续注入必须新增 `custom_reanim_definition`、`animation_instance_hook`、`animation_controller` 和 `animation_event_bus`，并先核对 1.0.0.1051 的 Definition/Track/Transform ABI。完整格式、制作流程和真正新增实体的侧挂模型见 `EXTERNAL_ANIMATION_AND_CUSTOM_ENTITIES.md`。

## 0.9.0 精英与通用外部贴图边界

`external_texture_config/runtime` 建立通用字符串贴图 ID 注册表，只负责路径安全、原版 `SexyAppBase::GetImage` 加载和进程内缓存。精英、植物和 UI 只能按 ID 查询 `Image*`，不得各自复制图片解析器。

`elite_zombie_config/hook` 保存数字 `runtimeId`、字符串精英 ID、确定性概率、`Zombie* + instanceId` 侧挂状态和 Reanimation 轨道替换；`elite_skill_registry` 注册技能回调；`zombie_event_bus` 是基础僵尸 Hook 向扩展模块发事件的唯一桥。完整约定和首个 `RAGE + BERSERK` 示例见 `ELITE_AND_TEXTURE_DESIGN.md`，普通与特殊僵尸图片轨道见 `ZOMBIE_TEXTURE_TRACKS.md`。

## 0.8.3 选卡页绘制分流

- 第 0 页保持调用完整的原版 `SeedChooserScreen::Draw`，原版卡片位置、绘制和交互全部恢复。
- 第 1 页起不再先画完整原版页面。原版绘制只允许进入顶部卡槽区和底部按钮区；候选卡网格从原版 `IMAGE_SEEDCHOOSER_BACKGROUND` 资源重新合成空白页，再绘制本页自定义卡。
- 这不是在原版卡片上盖色块：自定义页的候选区域没有提交任何原版卡片、锁定剪影或悬停层，因此不存在下一图层仍可看见或命中的原版卡。
- 自定义卡片的名称、介绍和阳光消耗由配置提供，选卡界面与进入关卡后的种子包提示均按逻辑 ID 显示。

## 0.8.2 原生选卡状态机接入

- 自定义卡不再瞬移。侧挂卡记录使用与原版 `ChosenSeed` 相同的四个状态：`IN_CHOOSER`、`FLYING_TO_BANK`、`IN_BANK`、`FLYING_TO_CHOOSER`；选入和撤回为 25 帧双重 SmoothStep 缓入缓出，银行后续卡左移为 15 帧。
- 卡槽容量必须读取 `SeedChooserScreen + 0xD14 -> Board + 0x144 -> SeedBank + 0x24`。`SeedChooserScreen + 0xD18` 是 `mNumSeedsToChoose`，不能当容量；其值在普通选卡中可为 0，误用会令所有新卡点击被拒绝。
- 每次点击前从原版及侧挂卡的真实飞行状态收敛动画，并重算 `mSeedsInFlight`，避免计数与记录失配后永久吞掉点击。
- 自定义页激活时，仅把状态为 `IN_CHOOSER` 的原版卡坐标暂移到屏幕外；顶部已选原版卡保持正常。返回第 0 页时恢复保存的原坐标。0.8.3 起原版候选区还会在绘制入口被裁掉，原卡不再参与底层绘制、悬停或点击。
- 自定义卡、顶部自定义卡和翻页按钮进入每帧光标判定，只有可交互项目命中时调用原版 `SexyAppBase::SetCursor(CURSOR_HAND)`。

## 0.8.1 选卡分页稳定性修复

- 自定义页不能直接用带透明通道的原版空卡轮廓覆盖第 0 页。0.8.3 已改为区域裁剪并从原版背景资源合成干净候选区；空卡轮廓和自定义卡只绘制在这张新页面上。
- 分页状态和自定义已选卡只绑定 `SeedChooserScreen*`。禁止用会随选卡变化的对象字段判断生命周期；选卡界面析构 Hook 会明确删除侧挂状态，既不会在撤卡时误回第 0 页，也不会把上一关状态带进下一关。
- 当前关卡卡槽已满时，自定义卡与原版卡一样不能继续加入；需要先从顶部撤下一张卡。撤卡后可选入自定义卡，凑满后原版“摇滚”按钮正常启用。
- `FindSeedInBank` 使用完整替代实现，不跳回带有内部反向分支的原函数入口，避免跳入 MinHook 覆盖区造成 `0x00485E25 / 0xC0000096` 崩溃。
- 商店翻页图像使用前显式加载 `DelayLoad_Store` 资源组；按钮位于选卡面板右下方的完整可见区域。

## 0.8.0 选卡与自定义植物模块边界

选卡扩展没有继续塞进 `pvz_hook.cpp`。当前拆分如下：

```text
seed_ui_config.*          # 上方卡槽数配置和 6-10 校验
custom_plant_config.*     # 新植物完整定义和字段校验
plant_catalog_runtime.*   # 启动时目录读取、逻辑 ID 查询
seed_ui_hook.cpp          # 40 张/页、商店翻页按钮、种子栏桥接
custom_plant_hook.cpp     # 植物实例、生命、射击和子弹实例旁路状态
plant_attack_hook.cpp     # 原版攻击覆盖；只通过公开接口查询自定义子弹伤害
```

原版 `SeedChooserScreen::mChosenSeeds` 后只有 4 个安全空记录，不能把它当成新植物总表。0.8.0 改为侧挂分页和已选卡列表：第 0 页保留原版，每个自定义页 40 张；关闭选卡时才借用一个空记录把“模板 ID + 自定义逻辑 ID”送入原版 `SeedPacket`。因此配置总数不再受 4 限制，也不扩大原版对象结构。

新植物实例仍让原版保存 `templatePlantId`，以复用稳定的动画和行为；自定义 ID 由侧挂表关联到具体 `Plant*` 和 `Projectile*`。对象复用入口先清除旧关联，首次更新时一次性覆盖生命、射速和首发延迟。以后新增特殊攻击必须建立独立行为适配器，不应继续向 `custom_plant_hook.cpp` 堆积模板特判。

## 目标

`pvz_hook.cpp` 只负责启动和模块注册，不允许继续加入某一玩法的配置类、对象偏移、概率算法或裸 Hook。每个功能域拥有独立的配置模型、运行时缓存和 Hook 安装函数，修改僵尸系统不应要求修改植物、阳光或波次实现。

```text
pvz_hook.cpp                 # 启动、版本校验、按模块注册
hook_utils.*                 # 公共的 1051 校验与 MinHook 安装工具
hook_modules.h               # 各模块唯一的安装接口
wave_hook.cpp                # 关卡出怪和数量倍率
sun_hook.cpp                 # 阳光价值
plant_attack_hook.cpp        # 植物投射物和直接攻击
zombie_hook.cpp              # 僵尸生成、属性、防具和啃食伤害
zombie_event_bus.cpp         # 僵尸生命周期与伤害扩展事件
elite_zombie_config.cpp      # 精英概率、视觉和技能绑定配置
elite_skill_registry.cpp     # 技能 ID 注册与事件分发
elite_zombie_hook.cpp        # 精英侧挂状态、红色绘制、贴图和清理
external_texture_config.cpp  # 通用外部贴图注册表解析
external_texture_runtime.cpp # 原版图片加载与 Image* 缓存
external_animation_config.* # 外部动画元数据、动作和事件配置
raw_reanim.*                # Raw .reanim 安全解析与帧继承
compiled_reanim.*           # 原版 PC compiled/zlib 缓存安全解码
reanim_loader.*             # Raw 与 compiled 自动格式分流
external_animation_runtime.* # 资源交叉校验和只读动画注册表

*_config.h / *_config.cpp    # 纯配置解析和校验，可由单元测试直接调用
wave_generator.*             # 不访问游戏内存的纯生成算法
wave_multiplier.*            # 不访问游戏内存的纯倍率算法
```

## 依赖规则

1. `pvz_hook.cpp` 可以依赖各 Hook 模块；业务模块禁止反向依赖 `pvz_hook.cpp`。
2. Hook 模块可以依赖自己的配置模型、`hook_utils` 和 `logger`，禁止直接读取其他业务模块的私有运行时对象。
3. 配置解析和概率算法放在 `*_config` 或纯算法文件中，禁止和裸汇编 Detour 写在同一个类里。
4. 原版地址、对象偏移和调用约定归属于使用它们的模块；只有 PE 版本校验和 MinHook 安装流程放入公共工具。
5. 新增模块时，在 `hook_modules.h` 增加一个 `InstallXxxHooks(moduleBase)` 接口，再由启动器注册。一个模块安装失败只记录该模块错误，不把业务代码塞回启动器。
6. 跨模块协作优先使用稳定的数据接口或事件接口；禁止直接访问另一个 `.cpp` 文件的匿名命名空间状态。

## 僵尸与精英扩展边界

`zombie_config` 定义基础本体血量、独立攻击伤害、防具定义和防具概率；`zombie_hook` 只把有效配置应用到原版对象。0.9.0 的 `elite_zombie_config` 和 `elite_zombie_hook` 使用独立文件，并通过僵尸实例 ID/侧挂状态引用精英编号，不把精英技能分支继续堆入 `zombie_hook.cpp`。

防具视觉采用适配器思路：`cone`、`bucket` 和 `door` 使用普通僵尸动画轨道；`wallnutHead` 为普通僵尸 ID 0 使用原版 ID 27 初始化器建立独立附着动画，并以实例侧挂状态处理掉头清理。其他动画族以后新增自己的视觉适配器。配置中的 Mod 防具 ID 和进阶等级不直接复用原版 `HelmType/ShieldType`，从而允许后续新增原创防具而不破坏原版枚举。

原版防具数值走独立的 `originalArmorHealth` 目录：解析器负责键名、默认值和范围校验，Hook 只在配置值偏离已核对的原版默认值时写当前/最大生命。该目录不依赖 `zombies` 稀疏覆盖，也不负责随机视觉挂载；新增原创随机防具仍使用 `armorDefinitions + armorRolls`，避免原版数值覆盖和装备生成逻辑耦合。

## 裸汇编桥接规则

原版函数经常使用 EAX、ESI 等非标准寄存器传递 `this`，因此 C++ 不能直接调用，必须经过独立的裸汇编桥接器。桥接器和 C++ 声明必须使用完全一致的清栈约定：若桥接器以 `ret N` 返回，声明必须是 `__stdcall`；若声明是 `__cdecl`，桥接器只能使用 `ret`，由调用方清栈。禁止两端同时清栈。

每次新增桥接器都要检查 Release 对象文件反汇编：调用指令之后不得出现与 `ret N` 重复的 `add esp, N`。0.5.1 修复了防具视觉桥接器曾以默认 `cdecl` 声明、同时又执行 `ret 12/ret 4` 所造成的双重清栈；该问题会在第一只抽中铁桶或铁门的僵尸初始化后触发 `FAST_FAIL_STACK_COOKIE_CHECK_FAILURE`。
