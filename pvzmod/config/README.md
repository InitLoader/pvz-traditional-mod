# PVZ Mod 配置目录规范

## 选卡交互说明（0.8.3）

- 自定义页使用真正的绘制分流：原版候选卡区域不会被绘制，自定义页由原版空白背景资源和当前页卡片组成，不是覆盖层。
- 切回第 0 页会恢复原版卡片坐标、完整绘制和原版交互。
- `name`、`description`、`cost` 会覆盖模板植物的选卡提示与关卡内种子包提示；没有自定义逻辑 ID 的原版卡不受影响。

- 自定义卡使用与原版一致的 25 帧选入/撤回动画，顶部补位为 15 帧；动画期间再次点击会先完成当前移动，再处理新点击。
- 自定义页不保留原版卡的底层绘制或命中，翻回第 0 页后原卡恢复。
- 鼠标移到可选自定义卡、已选自定义卡或翻页按钮时显示原版手型；已经选走后留在网格中的灰色影子不可点击，也不显示手型。
- 卡槽容量来自当前关卡的原版种子栏。槽位已满时仍需先撤下一张卡，配置未写 `slotCount` 时不会自动扩容。

## 精英僵尸与外部贴图（0.9.0）

- `resources/textures.jsonc` 只负责把字符串 ID 映射到图片路径。ID 区分大小写，必须匹配 `[A-Za-z0-9_]+`，长度为 1–64，例如 `KILL`、`RAGE_HEAD_01`。
- 规范字段为小写 `id`、`path`；为兼容手写配置也接受 `ID`、`Path` 和用户示例中的 `Patch`。同一项不能同时写两个别名。
- 图片只能放在游戏目录下的 `pvzmod/images/`，禁止绝对路径、UNC 路径和 `..`；当前接受 PNG、JPG/JPEG、BMP、GIF。
- `elites/zombies.jsonc` 管理精英数字 `runtimeId`、字符串 ID、适用僵尸、生成概率、优先级、视觉和技能。未列出的僵尸继续执行原版行为。
- 默认示例让普通僵尸 ID `0` 有 20% 概率成为 `RAGE`。`BERSERK` 在生成时应用生命、水平速度和啃食伤害倍率；红色 tint 不依赖外部图片。
- `visual.replacements[]` 会查询贴图注册表并真正替换 Reanimation 图片轨道。`scope` 可为 `body` 或 `special`；普通僵尸优先使用 `target` 语义部位，高级配置可直接写 `track`。图片不存在、解码失败、附加动画不存在或轨道不匹配时只跳过该项，不取消精英属性和技能。
- 两份配置都在 DLL 启动时读取；更改后需完全退出并重启游戏。图片首次使用时延迟加载，并在本次进程内缓存，不支持热替换。

示例：

```jsonc
// pvzmod/config/resources/textures.jsonc
{
  "schemaVersion": 1,
  "textures": [
    { "id": "KILL", "path": "pvzmod/images/zi/kill.png" }
  ]
}
```

图片应放在 `pvzmod/images/zi/kill.png`。仓库不附带这张素材；未放入时日志中的一次缺失警告属于安全降级。

## 外部动作资源（0.10.0-dev）

- `resources/animations.jsonc` 注册外部动画 ID、Raw `.reanim` 或原版 PC `.reanim.compiled` 路径、图片符号映射、动作、事件和定位轨道。
- 动画文件必须位于 `pvzmod/animations/`，图片继续位于 `pvzmod/images/`；配置不能引用绝对路径、盘符、UNC 或 `..`。
- 游戏本体已有 compiled 可以只读引用 `compiled/reanim/*.reanim.compiled`，例如 `compiled/reanim/Blover.reanim.compiled`；其他格式或目录仍拒绝。
- `images` 的值必须是 `resources/textures.jsonc` 已注册的贴图 ID。
- `actions` 至少包含一个动作；每个动作通过 `track` 指向 Raw `.reanim` 的 `anim_*` 轨道。
- 事件使用相对动作帧 `frame` 或 `normalizedTime`，二者必须且只能填写一个。
- 自定义植物和 `zombies/attributes.jsonc` 的 `animationId` 已支持把注册动画注入主体 Reanimation；附属 Reanimation 和通用动作事件仍处于分阶段接入，完整边界见 `modding/EXTERNAL_ANIMATION_AND_CUSTOM_ENTITIES.md`。

精英视觉示例：

```jsonc
"visual": {
  "tint": { "red": 255, "green": 48, "blue": 48, "alpha": 255 },
  "replacements": [
    { "scope": "body", "target": "head", "textureId": "KILL" },
    // target 与 track 二选一；下面是高级原版轨道写法。
    // { "scope": "body", "track": "Zombie_tie", "textureId": "RAGE_TIE" }
  ]
}
```

`target: "head"` 对应普通僵尸主动画的 `anim_head1`，图片会跟随头部轨道；它不是固定坐标贴图。完整语义 target、普通僵尸全部 30 个图片轨道和特殊僵尸轨道见 `modding/ZOMBIE_TEXTURE_TRACKS.md`。

## 选卡分页与自定义植物（0.8.1）

- 自定义页由原版空白背景资源重新合成，原版候选卡不会提交到该页；翻页按钮完整显示在“摇滚”按钮右侧。
- 当前关卡槽位已满时需要先撤下一张原卡，再点击自定义卡；这是与原版一致的容量规则，不会自动挤掉已有卡。
- 分页和自定义已选卡在选卡界面析构时清理，撤卡不会重置当前页，下一关也不会继承旧状态。

- `ui/seed_chooser.jsonc`：只管理上方实际携带卡槽数；当前模板不写 `slotCount`，因此沿用原版关卡数。以后主动写入时可设为 6-10。
- `plants/custom_plants.jsonc`：管理独立逻辑植物和卡片。新卡默认解锁，第 0 页是原版卡，第 1 页起每页显示 40 张自定义卡，当前配置上限 512 张。
- 选卡面板“一起摇滚吧！”右侧使用商店下一页图标循环翻页。已选自定义卡的逻辑 ID 会固化到上方种子包，翻页不会把它改成另一张卡。
- `templatePlantId` 只是动画、动作和目标选择的套壳；`cost`、`rechargeTime`、`health`、`launchRate`、首发延迟、连发数、子弹类型和伤害属于新植物自身，不覆盖模板植物。
- `animationId` 可省略；省略时保持模板主体动画，填写时必须引用 `resources/animations.jsonc` 中已通过校验的字符串 ID。外部动画必须提供可用的 `idle` 动作，并保留模板状态机会调用的动作轨道名（香蒲攻击为 `anim_shooting`），只覆盖主体 body，不自动替换独立头部或眨眼实例。
- `templatePlantId` 当前只允许 `0–48`。完整 ID、中文名、载体 Reanimation 与 compiled 对照表见 [`../../modding/PvZAnimationStudio/PLANT_TEMPLATE_IDS.md`](../../modding/PvZAnimationStudio/PLANT_TEMPLATE_IDS.md)；原版 `49–52` 是模式专用植物，不能直接当普通模板。
- 两份配置均在 DLL 启动时读取，修改后要完全退出并重启游戏。配置无效时对应模块回退为原版槽位或不加载新卡，并在 `pvzmod/logs/pvzmod.log` 记录原因。

所有业务配置统一放在 `pvzmod/config` 下，禁止再把 JSON 文件直接放到游戏根目录。规划中的 `pvzmod/plugins/native/<plugin-id>/plugin.jsonc` 只是原生插件加载元数据，是唯一目录例外，不能承载普通玩法配置。

```text
pvzmod/
├─ config/
│  ├─ levels/       # 关卡、波次、出怪权重和地图规则
│  ├─ plants/       # 植物属性、技能和卡片参数
│  ├─ zombies/      # 普通僵尸属性与行为参数
│  ├─ elites/       # 精英编号、倍率、技能和生成规则
│  ├─ resources/    # 通用外部贴图字符串 ID 注册表
│  ├─ bosses/       # 各大关 Boss 阶段和技能
│  ├─ ui/           # UI 布局、按钮、文本和界面开关
│  ├─ settings/     # Mod 全局设置和难度配置
│  ├─ rules/        # 规划中的有限 JSON MicroRule
│  ├─ scripts/      # 规划中由工具生成的 Lua 模块索引
│  ├─ extensions/   # 规划中的统一扩展包索引和依赖
│  └─ schemas/      # JSON Schema 和配置版本定义
├─ saves/           # Mod 独立存档，不放配置模板
├─ images/          # 用户提供的外部图片；按用途继续分子目录
├─ animations/      # Raw .reanim 外部动作；按植物、僵尸和 UI 分类
├─ plugins/native/  # 规划中的可信 Win32 DLL 插件；每个插件独占子目录
└─ logs/            # 运行日志
```

命名规则：

- 文件名使用小写英文和下划线，例如 `spawn.json`、`elite_types.json`。
- 每个 JSON/JSONC 顶层必须包含 `schemaVersion`。需要逐字段默认值注释的模板使用 `.jsonc`，普通数据继续使用 `.json`。
- 一个文件只负责一个模块，避免把关卡、植物和 UI 混在同一文件中。
- 新模块先创建对应分类目录，再增加 JSON 和读取代码。
- 运行产生的存档、缓存和日志不能写进 `config`。

当前已实现配置：

- `levels/spawn.json`：关卡僵尸类型、权重和保底数量。
- `levels/wave_multipliers.json`：全局、关卡和单波的僵尸数量倍率。
- `settings/global.json`：全局经济和通用规则；当前包含普通、小型、大型阳光拾取价值。
- `resources/textures.jsonc`：通用外部贴图 ID、受限相对路径和原版图片加载缓存。
- `resources/animations.jsonc`：外部 Raw/compiled Reanimation、动作、事件、定位轨道和贴图符号映射。
- `elites/zombies.jsonc`：精英编号、概率、视觉、贴图引用和技能绑定。
- `rules/*.jsonc`：规划中的有限 MicroRule；只允许“一个事件 + 简单过滤 + 一个固定效果”，复杂逻辑必须升级为 Lua。
- `scripts/modules.jsonc`：规划中由打包工具生成的 Lua 发布索引、API 版本和能力声明，新手不手写。
- `extensions/packages.jsonc`：规划中由工具生成的统一包索引，管理 JSON、Lua、DLL 的所有者、版本、依赖、启用状态和文件哈希。
- 原生插件只允许位于 `pvzmod/plugins/native/<plugin-id>/`，不放在 `config`；每个目录必须包含 `plugin.jsonc` 和 manifest 精确指定的 Win32 DLL。
- `plants/attacks.jsonc`：植物攻击伤害稀疏覆盖；文件内已列出所有数值攻击的原版默认值。
- `zombies/attributes.jsonc`：完整原版防具生命目录、按僵尸 ID 稀疏覆盖本体生命/啃食伤害/主体 `animationId`，以及带等级和概率的额外防具。

`plants/attacks.jsonc` 中只有实际写出的键会覆盖原版。注释掉的示例只是攻击目录，不会生效；删除已启用键后，投射物会在下次启动或进入关卡时恢复原版，直接攻击会在下一次命中时恢复原版。未知键、非整数或超出 `0–1000000` 的数值会拒绝整份新配置并保留上一次有效配置。

`zombies/attributes.jsonc` 中的 `originalArmorHealth` 完整列出 1051 版 11 个有效防具/额外生命池。文件中的数值等于 DLL 内核对过的原版默认值时不写内存，保留原版初始化；改动某个数值时只覆盖对应防具，且不要求在 `zombies` 中再写该僵尸 ID。删除某个键也表示完全使用原版。`zombies.<id>.animationId` 可引用 `resources/animations.jsonc`，只覆盖该 ID 的主体动画；省略则完全沿用原版。动画载体必须与实际僵尸主体一致，并保留原版 AI 请求的同名 `anim_*` 动作轨道。

复杂技能、Boss 状态机和以后需要自由代码的功能不会继续扩张为大量 JSON 字段。规划采用有限 JSON、Lua、可信 DLL 三级扩展，并让它们共用 `pvzmod.dll` 的事件、Capability、命令、配置和所有者注册中心；以上目录和运行时目前都尚未实现，设计与迁移顺序见 `modding/SCRIPTABLE_SKILLS_AND_BEHAVIORS.md`。

规划中的工具也严格分离：`PvZLuaStudio` 创建、编辑、绑定辅助并验证 Lua；`PvZModManager` 管理本目录、包、Lua 文件元数据、DLL、资源、启用档案、安装和回滚，但不显示、验证或打开 Lua 代码；`PvZAnimationStudio` 只输出动画资产与基础植物骨架。详见 `modding/PVZLUA_STUDIO_DESIGN.md` 和 `modding/PVZMOD_MANAGER_DESIGN.md`。

`armorDefinitions` 和 `armorRolls` 是另一套“给任意兼容僵尸随机附加防具”的系统，使用独立 Mod ID。`chance` 为 0 时永不附加，为 100 时必定附加；同一种子和僵尸实例会得到相同结果。`zombies` 仍只稀疏覆盖本体生命、啃食攻击和随机装备规则，当前示例只覆盖普通僵尸 ID 0。

视觉键当前支持 `cone`、`bucket`、`door`，以及仅适配普通僵尸 ID 0 的 `wallnutHead`。坚果头适配器复用原版 ID 27 的附着动画、裂纹和掉头清理流程。它与铁桶同属头盔槽；同槽多项同时抽中时先比较 `tier`，同级再选择较小的 Mod 防具 ID，因此单项 `chance=100` 不代表它一定压过同槽的更高优先级防具。
