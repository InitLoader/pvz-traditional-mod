# 植物模板 ID 全局目录

本表对应 PC 版《植物大战僵尸》`1.0.0.1051` 的植物 `SeedType`。动画制作器以 [`PlantTemplateCatalog.cs`](Models/PlantTemplateCatalog.cs) 为唯一代码目录；属性检查器中的“植物模板速选”、打包校验和回归测试均读取该目录。

需要区分两种 ID：

- `templatePlantId`：复用原版植物动画、动作和目标选择的模板 ID。当前 DLL 只接受 `0–48`。
- 自定义植物“数字 ID”：Mod 自己的独立逻辑 ID，从 `1000` 开始，不能与 `templatePlantId` 混用。
- `49–52` 仍是原版植物相关 `SeedType`，编辑器能识别并显示说明，但它们依赖坚果保龄球、禅境花园或我是僵尸等模式逻辑，当前禁止作为普通自定义植物模板导出。

## 可用于 `templatePlantId` 的标准植物（0–48）

| ID | 原版常量 | 中文名 | 载体 Reanimation | 原版 compiled 文件 |
|---:|---|---|---|---|
| 0 | `SEED_PEASHOOTER` | 豌豆射手 | `REANIM_PEASHOOTER` | `PeaShooter.reanim.compiled` |
| 1 | `SEED_SUNFLOWER` | 向日葵 | `REANIM_SUNFLOWER` | `SunFlower.reanim.compiled` |
| 2 | `SEED_CHERRYBOMB` | 樱桃炸弹 | `REANIM_CHERRYBOMB` | `CherryBomb.reanim.compiled` |
| 3 | `SEED_WALLNUT` | 坚果 | `REANIM_WALLNUT` | `Wallnut.reanim.compiled` |
| 4 | `SEED_POTATOMINE` | 土豆地雷 | `REANIM_POTATOMINE` | `PotatoMine.reanim.compiled` |
| 5 | `SEED_SNOWPEA` | 寒冰射手 | `REANIM_SNOWPEA` | `SnowPea.reanim.compiled` |
| 6 | `SEED_CHOMPER` | 大嘴花 | `REANIM_CHOMPER` | `Chomper.reanim.compiled` |
| 7 | `SEED_REPEATER` | 双发射手 | `REANIM_REPEATER` | `PeaShooter.reanim.compiled`（共享） |
| 8 | `SEED_PUFFSHROOM` | 小喷菇 | `REANIM_PUFFSHROOM` | `Puffshroom.reanim.compiled` |
| 9 | `SEED_SUNSHROOM` | 阳光菇 | `REANIM_SUNSHROOM` | `SunShroom.reanim.compiled` |
| 10 | `SEED_FUMESHROOM` | 大喷菇 | `REANIM_FUMESHROOM` | `Fumeshroom.reanim.compiled` |
| 11 | `SEED_GRAVEBUSTER` | 墓碑吞噬者 | `REANIM_GRAVE_BUSTER` | `Gravebuster.reanim.compiled` |
| 12 | `SEED_HYPNOSHROOM` | 魅惑菇 | `REANIM_HYPNOSHROOM` | `Hypnoshroom.reanim.compiled` |
| 13 | `SEED_SCAREDYSHROOM` | 胆小菇 | `REANIM_SCRAREYSHROOM` | `ScaredyShroom.reanim.compiled` |
| 14 | `SEED_ICESHROOM` | 寒冰菇 | `REANIM_ICESHROOM` | `Iceshroom.reanim.compiled` |
| 15 | `SEED_DOOMSHROOM` | 毁灭菇 | `REANIM_DOOMSHROOM` | `DoomShroom.reanim.compiled` |
| 16 | `SEED_LILYPAD` | 睡莲 | `REANIM_LILYPAD` | `Lilypad.reanim.compiled` |
| 17 | `SEED_SQUASH` | 窝瓜 | `REANIM_SQUASH` | `Squash.reanim.compiled` |
| 18 | `SEED_THREEPEATER` | 三线射手 | `REANIM_THREEPEATER` | `ThreePeater.reanim.compiled` |
| 19 | `SEED_TANGLEKELP` | 缠绕水草 | `REANIM_TANGLEKELP` | `Tanglekelp.reanim.compiled` |
| 20 | `SEED_JALAPENO` | 火爆辣椒 | `REANIM_JALAPENO` | `Jalapeno.reanim.compiled` |
| 21 | `SEED_SPIKEWEED` | 地刺 | `REANIM_SPIKEWEED` | `Caltrop.reanim.compiled` |
| 22 | `SEED_TORCHWOOD` | 火炬树桩 | `REANIM_TORCHWOOD` | `Torchwood.reanim.compiled` |
| 23 | `SEED_TALLNUT` | 高坚果 | `REANIM_TALLNUT` | `Tallnut.reanim.compiled` |
| 24 | `SEED_SEASHROOM` | 海蘑菇 | `REANIM_SEASHROOM` | `SeaShroom.reanim.compiled` |
| 25 | `SEED_PLANTERN` | 路灯花 | `REANIM_PLANTERN` | `Plantern.reanim.compiled` |
| 26 | `SEED_CACTUS` | 仙人掌 | `REANIM_CACTUS` | `Cactus.reanim.compiled` |
| 27 | `SEED_BLOVER` | 三叶草 | `REANIM_BLOVER` | `Blover.reanim.compiled` |
| 28 | `SEED_SPLITPEA` | 裂荚射手 | `REANIM_SPLITPEA` | `SplitPea.reanim.compiled` |
| 29 | `SEED_STARFRUIT` | 杨桃 | `REANIM_STARFRUIT` | `Starfruit.reanim.compiled` |
| 30 | `SEED_PUMPKINSHELL` | 南瓜头 | `REANIM_PUMPKIN` | `Pumpkin.reanim.compiled` |
| 31 | `SEED_MAGNETSHROOM` | 磁力菇 | `REANIM_MAGNETSHROOM` | `Magnetshroom.reanim.compiled` |
| 32 | `SEED_CABBAGEPULT` | 卷心菜投手 | `REANIM_CABBAGEPULT` | `Cabbagepult.reanim.compiled` |
| 33 | `SEED_FLOWERPOT` | 花盆 | `REANIM_FLOWER_POT` | `Pot.reanim.compiled` |
| 34 | `SEED_KERNELPULT` | 玉米投手 | `REANIM_KERNELPULT` | `Cornpult.reanim.compiled` |
| 35 | `SEED_INSTANT_COFFEE` | 咖啡豆 | `REANIM_COFFEEBEAN` | `Coffeebean.reanim.compiled` |
| 36 | `SEED_GARLIC` | 大蒜 | `REANIM_GARLIC` | `Garlic.reanim.compiled` |
| 37 | `SEED_UMBRELLA` | 叶子保护伞 | `REANIM_UMBRELLALEAF` | `Umbrellaleaf.reanim.compiled` |
| 38 | `SEED_MARIGOLD` | 金盏花 | `REANIM_MARIGOLD` | `Marigold.reanim.compiled` |
| 39 | `SEED_MELONPULT` | 西瓜投手 | `REANIM_MELONPULT` | `Melonpult.reanim.compiled` |
| 40 | `SEED_GATLINGPEA` | 机枪射手 | `REANIM_GATLINGPEA` | `GatlingPea.reanim.compiled` |
| 41 | `SEED_TWINSUNFLOWER` | 双子向日葵 | `REANIM_TWIN_SUNFLOWER` | `TwinSunFlower.reanim.compiled` |
| 42 | `SEED_GLOOMSHROOM` | 忧郁菇 | `REANIM_GLOOMSHROOM` | `GloomShroom.reanim.compiled` |
| 43 | `SEED_CATTAIL` | 香蒲 | `REANIM_CATTAIL` | `Cattail.reanim.compiled` |
| 44 | `SEED_WINTERMELON` | 冰西瓜 | `REANIM_WINTER_MELON` | `WinterMelon.reanim.compiled` |
| 45 | `SEED_GOLD_MAGNET` | 吸金磁 | `REANIM_GOLD_MAGNET` | `GoldMagnet.reanim.compiled` |
| 46 | `SEED_SPIKEROCK` | 地刺王 | `REANIM_SPIKEROCK` | `SpikeRock.reanim.compiled` |
| 47 | `SEED_COBCANNON` | 玉米加农炮 | `REANIM_COBCANNON` | `CobCannon.reanim.compiled` |
| 48 | `SEED_IMITATER` | 模仿者 | `REANIM_IMITATER` | `Imitater.reanim.compiled` |

## 能识别但不能作为普通模板的特殊植物（49–52）

| ID | 原版常量 | 中文名 | 载体 / 文件 | 限制 |
|---:|---|---|---|---|
| 49 | `SEED_EXPLODE_O_NUT` | 爆炸坚果 | `REANIM_WALLNUT` / `Wallnut.reanim.compiled` | 坚果保龄球专用；额外依赖爆炸与滚动规则 |
| 50 | `SEED_GIANT_WALLNUT` | 巨大坚果 | `REANIM_WALLNUT` / `Wallnut.reanim.compiled` | 坚果保龄球专用；额外依赖尺寸与滚动规则 |
| 51 | `SEED_SPROUT` | 禅境花园幼苗 | `REANIM_ZENGARDEN_SPROUT` / `ZenGarden_sprout.reanim.compiled` | 禅境花园专用 |
| 52 | `SEED_LEFTPEATER` | 反向双发射手 | `REANIM_REPEATER` / `PeaShooter.reanim.compiled` | 特殊模式专用；额外依赖反向索敌与发射规则 |

## 管理规则

1. 新代码禁止再次手写另一份 `0–48` 名称表；统一调用 `PlantTemplateCatalog.All`、`Find(id)` 或 `RuntimeTemplates`。
2. 动画制作器允许手工查看 `0–52` 的解释，但下拉速选只提供能安全导出的 `0–48`。
3. 打包植物工程时再次校验 `templatePlantId`；未知 ID 或 `49–52` 会直接报错，不生成表面成功、进游戏失效的配置。
4. `SeedType`、`ReanimationType` 和 compiled 文件名是三个不同命名空间。例如 ID 21 的常量是 `SEED_SPIKEWEED`，载体是 `REANIM_SPIKEWEED`，文件却叫 `Caltrop.reanim.compiled`。
5. 双发射手虽然有独立的 `REANIM_REPEATER` 类型，但本地原版资源表复用 `PeaShooter.reanim.compiled`；因此 49 张标准植物卡只有 48 个独立植物 compiled 文件。

枚举顺序和特殊 ID 交叉核对自开源反编译工程的 [`ConstEnums.h`](https://github.com/Patoke/re-plants-vs-zombies/blob/main/ConstEnums.h) 与 [`Plant.cpp`](https://github.com/Patoke/re-plants-vs-zombies/blob/main/Lawn/Plant.cpp)；最终适配边界仍以本仓库锁定的 `PlantsVsZombies.exe 1.0.0.1051` 和本地资源审计为准。
