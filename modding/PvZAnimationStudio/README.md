# PvZ 动画制作器

`PvZAnimationStudio` 是面向 PC 版 `Plants vs. Zombies 1.0.0.1051` 的独立中文 WPF 编辑器。它用于制作、检查和打包分层 Reanimation，不向游戏进程注入编辑器代码。

## 当前可用能力

- 新建植物、僵尸、UI 或其他动画工程；保存为可继续编辑的 `*.pvza.json`。
- 直接读取和重新导出 Raw `*.reanim` 与原版 PC `*.reanim.compiled`。
- 分层画布预览、轨道选择、时间轴、播放、插入/删除帧、双击切换 K 帧。
- 鼠标拖动部件，编辑 `x/y/kx/ky/sx/sy/f/a/i/font/text`。
- 从当前关键帧到右侧下一个关键帧生成线性或平滑补间；补间会烘焙位移、旋转/斜切、缩放和透明度。
- 自动识别所有 `anim_*` 轨道，不把动作限制为内置列表；常用植物、僵尸、Boss、特殊动作和眨眼显示中文分类，未知动作显示为“自定义动作”。
- 导入透明 PNG，并把图片符号绑定到工程；选择游戏目录后可索引已解包的 `reanim/`、`images/` 与 `pvzmod/images/`。
- 生成 `.reanim`、`.reanim.compiled`、Mod ZIP、贴图/动画/实体 JSONC 片段。
- 一键把动画、图片和配置合并到 `pvzmod` 分类目录；首次改写配置前生成 `.pvzstudio.bak`。

原版 `compiled/reanim` 目录的 143 个 compiled 文件已完成“读取 → 重新打包 → 再读取”回归。原版少数动画含同名轨道，工具会按顺序保留，不能擅自去重。

## 运行

本机发布版：

```text
H:\pvz\modding\dist\PvZAnimationStudio\PvZAnimationStudio.exe
```

从源码启动：

```powershell
dotnet run --project H:\pvz\modding\PvZAnimationStudio\PvZAnimationStudio.csproj -c Release
```

需要 Windows 和 .NET 8 Desktop Runtime。编辑器为 64 位独立工具；生成的 Reanimation 仍是游戏所用的 32 位 PC 格式。

## 从零制作一个植物或僵尸

1. 把角色拆成透明 PNG 部件，例如 `body`、`head`、`eye`、`arm_front`、`arm_back`、`leg_front`、`shadow`、`weapon`。
2. 新建植物或僵尸工程，设置字符串 ID、数字 ID、中文名称、说明、生命和攻击参数。
3. 导入 PNG；为每个可独立运动的部件建一条轨道，双击图片资源即可把符号写入当前轨道帧。
4. 在时间轴起点按 `K`，移动到结束帧后拖动部件或修改位置、旋转、缩放，再按 `K`。
5. 回到起点，点击“线性补间”或“平滑补间”。
6. 添加或识别 `anim_idle`、`anim_attack`、`anim_blink`、`anim_die` 等动作。特殊动作只需使用新的 `anim_<动作ID>`，工具会完整保留和分类。
7. 眨眼建议把眼皮/眼睛单独放入轨道，在 `anim_blink` 范围内用 `f=-1/0` 或图片切换控制显示；编辑器不会把眨眼硬编码到身体轨道。
8. 先导出 compiled 并在编辑器重新打开验证，再生成 ZIP 或一键安装。

## 工程和输出目录

```text
<工程>.pvza.json                         # 编辑器工程
pvzmod/animations/plants/<id>/           # 植物动作
pvzmod/animations/zombies/<id>/          # 僵尸动作
pvzmod/images/plants/<id>/               # 植物 PNG
pvzmod/images/zombies/<id>/               # 僵尸 PNG
pvzmod/config/resources/textures.jsonc   # 图片注册
pvzmod/config/resources/animations.jsonc # 动画、动作、事件注册
pvzmod/config/plants/custom_plants.jsonc # 新植物配置
pvzmod/config/zombies/                    # 生成的新僵尸配置
```

ZIP 中使用 `generated/<id>/*.fragment.jsonc`，便于人工审查后合并；“一键安装”使用字符串 ID 更新对应数组，保留文件其他注释和条目。

## Raw 与 compiled

- Raw 是 XML 片段，适合版本管理和人工审阅。
- compiled 使用原版 PC `DEADFED4 + zlib + B393B4C0` 布局；输出保持 Definition/Track/Transform 的 16/12/44 字节协议。
- 文件中的旧指针只作为占位，不会被解引用；导出时重建字符串数据。
- 省略字段表示继承前一帧；工具只在设置 K 帧或生成补间时写入显式数值。

## 当前边界

编辑器与格式打包器已经可用，但“文件可生成”不等于“游戏运行时已经支持真正新实体”。当前 DLL 仍缺少自定义 `ReanimatorDefinition` 注入、自定义僵尸运行时和完整动作事件控制器。因此：

- 可以安全编辑、重打包和登记原创动画资源。
- 自定义植物配置可接入现有模板载体；原创动画最终替换仍取决于后续运行时 Definition 注入。
- 僵尸配置目前生成到独立文件，等待 `custom_zombie_runtime` 接入，避免假装已经能在游戏中生成真正新僵尸。
- 编辑器当前采用“补间烘焙”，还没有 Adobe Animate 的贝塞尔曲线手柄、骨骼 IK、撤销栈、多选和音频时间轴。

## 构建与回归

```powershell
dotnet build H:\pvz\modding\PvZAnimationStudio\PvZAnimationStudio.csproj -c Release
dotnet run --project H:\pvz\modding\PvZAnimationStudio.Tests\PvZAnimationStudio.Tests.csproj -c Release
dotnet run --project H:\pvz\modding\PvZAnimationStudio.Tests\PvZAnimationStudio.Tests.csproj -c Release -- H:\pvz\compiled\reanim
```

第三条命令会验证本地全部原版 compiled。测试只在临时目录写入重打包文件，不覆盖原版资源。
