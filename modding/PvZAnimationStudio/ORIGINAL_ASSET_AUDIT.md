# 原版植物与僵尸动画全量审计

审计对象为当前项目配套的 PC `PlantsVsZombies.exe 1.0.0.1051` 解包资源。此次检查既验证二进制格式，也验证代表帧的图片解析、子帧、变换矩阵、透明遮罩、动作范围和游戏运行时组合语义。

## 结论

- 原版 `compiled/reanim` 共 143 个 `.reanim.compiled`，全部通过“读取 → compiled 重打包 → 再读取 → Raw 导出 → 再读取”。
- 48 个独立植物动画全部检查，覆盖原版 49 张植物卡；双发射手与豌豆射手共用 `PeaShooter` 定义，因此独立文件少 1 个。
- 38 个僵尸、僵尸变体或僵尸效果动画全部检查；它们覆盖 33 个原版 `ZombieType` 定义以及焦黑、旗杆、手、Boss 火球/冰球、胜利文字和制作人员表等辅助动画。
- 上述 86 个植物/僵尸相关文件的外部图片符号缺失数为 0。
- 预览矩阵使用原版 `MatrixFromTransform` 的双轴角度语义，图片使用原版左上注册点和资源清单中的 `cols/rows` 子帧。

## 已检查植物文件（48）

```text
Blover, Cabbagepult, Cactus, Caltrop, Cattail, CherryBomb
Chomper, CobCannon, Coffeebean, Cornpult, DoomShroom, Fumeshroom
Garlic, GatlingPea, GloomShroom, GoldMagnet, Gravebuster, Hypnoshroom
Iceshroom, Imitater, Jalapeno, Lilypad, Magnetshroom, Marigold
Melonpult, PeaShooter, Plantern, Pot, PotatoMine, Puffshroom
Pumpkin, ScaredyShroom, SeaShroom, SnowPea, SpikeRock, SplitPea
Squash, Starfruit, SunFlower, SunShroom, Tallnut, Tanglekelp
ThreePeater, Torchwood, TwinSunFlower, Umbrellaleaf, Wallnut, WinterMelon
```

## 已检查僵尸与相关文件（38）

```text
Zombie, Zombie_balloon, Zombie_bobsled, Zombie_boss, Zombie_Boss_driver
Zombie_boss_fireball, Zombie_boss_iceball, Zombie_bungi, Zombie_catapult
Zombie_charred, Zombie_charred_catapult, Zombie_charred_digger
Zombie_charred_gargantuar, Zombie_charred_imp, Zombie_charred_zamboni
Zombie_Credits_Conehead, Zombie_credits_dance, Zombie_Credits_Screendoor
Zombie_dancer, Zombie_digger, Zombie_dolphinrider, Zombie_FlagPole
Zombie_football, Zombie_gargantuar, Zombie_hand, Zombie_imp
Zombie_jackbox, Zombie_Jackson, Zombie_ladder, Zombie_paper
Zombie_pogo, Zombie_polevaulter, Zombie_snorkle, Zombie_surprise
Zombie_yeti, Zombie_zamboni, LawnMoweredZombie, ZombiesWon
```

`LoadBar_Zombiehead` 也作为僵尸 UI 效果额外完成可视检查，但不计入上述按文件名分类的 38 个文件。

## 原版特殊结构及处理

| 资源 | 原版结构 | 编辑器处理 |
|---|---|---|
| `GatlingPea` | 身体 `anim_idle` 与头部 `anim_head_idle` 是两个实例 | 完整时间轴自动组合身体和头部；进入动作视图后只显示当前动作 |
| `SplitPea` | 身体加前、后两个独立头部实例 | 按 `anim_idle + anim_head_idle + anim_splitpea_idle` 组合 |
| `ThreePeater` | 身体加三个独立头部实例 | 按 `anim_head1/2/3` 挂点和三个头部待机段组合 |
| `Lilypad` | `anim_idle` 本身就是可见主体轨道，不是纯标记 | 所有 `anim_*` 都参与动作识别；可见动作轨道仍正常绘制 |
| `Zombie` | 同一文件保存路障、铁桶、铁门、旗帜、泳圈等可选装备 | 默认实体预览按原版普通僵尸规则隐藏可选装备；动作视图保留全部轨道 |
| `Zombie_boss` | 大型分阶段机器人，颜色 JPG 与 `_.png` 灰度遮罩成对使用 | 代表帧选择完整装配帧；JPG 与同名 `_.png` 合成为透明贴图 |

原版目录中共有 25 组“JPG 颜色图 + 同名 `_.png` 灰度透明遮罩”，现统一由 `OriginalResourceService` 合成，不再出现黑色矩形背景。

## 不是故障的空白或无动作标记文件

- `LawnMoweredZombie` 只有 1 条轨道、8 帧且没有图片，是原版占位/效果定义，预览为空符合文件内容。
- `Zombie_charred_catapult`、`Zombie_charred_gargantuar`、`Zombie_charred_imp`、`Zombie_FlagPole`、`Zombie_hand`、`Zombie_surprise` 没有 `anim_*` 动作段，但都有可见图片；它们是静态或辅助效果，不是解析失败。
- `Zombie_boss` 与 `Zombie_Boss_driver` 是两个独立定义；本次分别验证。跨文件的完整游戏实体组合仍应由后续实体场景预览器负责，不能修改原始动画来伪造单文件定义。

## 回归命令

```powershell
dotnet run --project H:\pvz\modding\PvZAnimationStudio.Tests\PvZAnimationStudio.Tests.csproj -c Release
dotnet run --project H:\pvz\modding\PvZAnimationStudio.Tests\PvZAnimationStudio.Tests.csproj -c Release -- H:\pvz\compiled\reanim
dotnet run --project H:\pvz\modding\PvZAnimationStudio.Tests\PvZAnimationStudio.Tests.csproj -c Release -- --audit H:\pvz\compiled\reanim
dotnet run --project H:\pvz\modding\PvZAnimationStudio.Tests\PvZAnimationStudio.Tests.csproj -c Release -- --inspect H:\pvz\compiled\reanim\ThreePeater.reanim.compiled 16 57 98 136
```
