# PvZ 1.0.0.1051 外部出怪配置模块

该模块让 `PlantsVsZombies.exe` 从 `pvzmod/config/levels/spawn.json` 读取冒险关卡出怪权重。所有 JSON 都必须放在 `pvzmod/config` 下的模块分类目录中，禁止直接堆放在游戏根目录。

## 稀疏覆盖规则

- `levels` 中出现的关卡由 Mod 接管僵尸类型。
- 没有出现的关卡完全执行原版出怪逻辑。
- `enabled: false` 等同于没有覆盖。
- Mod 读取游戏的实际波数，保留原版每波有效槽位数量和波次节奏，只按权重替换实际启用波次中的僵尸类型；未启用的波次缓存不修改。
- 相同关卡和 `seed` 生成相同列表，保证选卡预览和实际出怪一致。
- 修改 JSON 后重新进入关卡即可读取新配置；JSON 无效时继续使用上一次有效配置并写入日志。

例如当前 `pvzmod/config/levels/spawn.json` 只覆盖 `1-1`：

```json
{
  "schemaVersion": 1,
  "levels": {
    "1-1": {
      "seed": 1101,
      "zombies": [
        { "id": 0, "weight": 80 },
        { "id": 2, "weight": 20, "minimumCount": 1 }
      ]
    }
  }
}
```

`id: 0` 是普通僵尸，`id: 2` 是路障僵尸。权重是相对值，因此 `80/20`、`8/2` 和 `4/1` 的概率比例相同。

`minimumCount` 是可选的关卡保底数量。示例中的路障设置为 `1`，表示在该关卡实际启用的所有出怪槽位中至少安排一只路障，其余槽位仍按 80/20 抽取。省略该字段或设为 `0` 时没有保底，少量出怪的关卡可能一次也抽不到低权重僵尸。

要覆盖其他关卡，只需在 `levels` 中增加对应键。例如增加 `1-2` 不会影响 `1-3`：

```json
"1-2": {
  "seed": 1102,
  "zombies": [
    { "id": 0, "weight": 60 },
    { "id": 2, "weight": 30, "minimumCount": 1 },
    { "id": 3, "weight": 10 }
  ]
}
```

## 僵尸 ID

| ID | 僵尸 |
| ---: | --- |
| 0 | 普通僵尸 |
| 1 | 旗帜僵尸（特殊） |
| 2 | 路障僵尸 |
| 3 | 撑杆僵尸 |
| 4 | 铁桶僵尸 |
| 5 | 读报僵尸 |
| 6 | 铁栅门僵尸 |
| 7 | 橄榄球僵尸 |
| 8 | 舞王僵尸 |
| 9 | 伴舞僵尸 |
| 10 | 鸭子救生圈僵尸 |
| 11 | 潜水僵尸 |
| 12 | 冰车僵尸 |
| 13 | 雪橇车僵尸 |
| 14 | 海豚骑士僵尸 |
| 15 | 玩偶匣僵尸 |
| 16 | 气球僵尸 |
| 17 | 矿工僵尸 |
| 18 | 跳跳僵尸 |
| 19 | 雪人僵尸（特殊） |
| 20 | 蹦极僵尸（特殊） |
| 21 | 扶梯僵尸 |
| 22 | 投石车僵尸 |
| 23 | 巨人僵尸 |
| 24 | 小鬼僵尸（特殊） |
| 25 | 僵王博士（特殊） |
| 26 | 豌豆射手僵尸 |
| 27 | 坚果僵尸 |
| 28 | 火爆辣椒僵尸 |
| 29 | 机枪射手僵尸 |
| 30 | 窝瓜僵尸 |
| 31 | 高坚果僵尸 |
| 32 | 红眼巨人僵尸 |

水生、屋顶和我是僵尸模式单位放进不合适的冒险场景时可能没有正确行、动画或行为条件。每次新增一种 ID 都应在对应场景测试。

## 特殊僵尸

旗帜僵尸、雪人、蹦极僵尸、小鬼和僵王属于特殊放置类型。默认不能把它们加入普通权重池，原版已经放在波次中的特殊槽位则默认保留。

确实需要实验时可以设置：

```json
"allowSpecialZombieIds": true,
"preserveOriginalSpecialZombies": false
```

这可能改变旗帜波、召唤或 Boss 行为，应为对应关卡单独测试。

## 构建

```powershell
cmake -S H:\pvz\modding -B H:\pvz\modding\build -A Win32
cmake --build H:\pvz\modding\build --config Release
ctest --test-dir H:\pvz\modding\build -C Release --output-on-failure
```

构建会下载固定版本的 nlohmann/json 3.11.3、MinHook 1.3.4、tinyxml2 10.0.0 和 zlib 1.3.1。

## 安装

1. 将 Release 版 `pvzmod.dll` 放到 `PlantsVsZombies.exe` 同目录。
2. 将整个 `pvzmod` 数据目录放到游戏根目录，出怪配置应位于 `pvzmod/config/levels/spawn.json`。
3. 使用补丁器修改当前精确版本的 EXE：

```powershell
PvZModPatcher.exe --in-place H:\pvz\PlantsVsZombies.exe
```

原地修改前会创建 `PlantsVsZombies.original.exe`。补丁器只接受项目已确认 SHA-256 的 `1.0.0.1051` 文件，并添加 `.pvzm` 加载器段；不修改 DRM 或存档。

运行日志位于 `H:\pvz\pvzmod\logs\pvzmod.log`。

## 僵尸数量倍率

倍率配置位于 `pvzmod/config/levels/wave_multipliers.json`。它只改变每波的僵尸数量，不修改生命、速度或攻击属性。

```json
{
  "schemaVersion": 1,
  "global": {
    "countMultiplier": 1.0,
    "scaleSpecialZombies": false,
    "seed": 1463899717
  },
  "levels": {
    "1-1": {
      "countMultiplier": 1.0,
      "waves": {
        "1": 2.0
      }
    }
  }
}
```

最终倍率使用乘法合成：

```text
最终倍率 = global.countMultiplier × 关卡 countMultiplier × 单波倍率
```

例如全局 `2.0`、`1-1` 关卡 `1.5`、第 1 波 `2.0`，则 `1-1` 第 1 波最终为 `6.0` 倍；`1-1` 其他波为 `3.0` 倍；没有关卡规则的其他关卡为 `2.0` 倍。

规则：

- 倍率范围为 `0.1–10.0`，三层相乘后的实际数量仍受每波 50 只上限限制。
- 数量使用四舍五入，每个原本非空的波次至少保留 1 只。
- 扩大时从该波已有僵尸类型中确定性抽取并追加。
- 缩小时从该波普通僵尸中确定性抽样。
- `scaleSpecialZombies: false` 会保护旗帜、雪人、蹦极、小鬼和僵王，不复制也不删减，建议保持关闭。
- `seed` 控制抽样结果；相同配置和种子会得到相同结果。
- 修改 JSON 后重新进入关卡即可热重载。

当前示例只有 `1-1` 第 1 波为 `2.0` 倍。要启用全局双倍，将 `global.countMultiplier` 改为 `2.0`；要恢复原版数量，将所有倍率改为 `1.0` 或删除对应关卡规则。

## 全局配置与阳光价值

全局配置位于 `pvzmod/config/settings/global.json`。当前实现了三种阳光拾取价值：

```json
{
  "schemaVersion": 1,
  "economy": {
    "sunPickupValues": {
      "normal": 50,
      "small": 25,
      "large": 100
    }
  }
}
```

字段含义：

- `normal`：普通阳光，原版为 25，当前示例改为 50。
- `small`：小型阳光，原版为 15，当前示例改为 25。
- `large`：大型阳光，原版为 50，当前示例改为 100。
- 取值必须是 `0–10000` 的整数。

该设置作用于所有模式和来源。1051 原版在真正入账的 `Coin::ScoreCoin` 中内联了 25/15/50，因此模块同时 Hook `Coin::GetSunValue` 和 `Coin::ScoreCoin`：前者修正正在飞向阳光栏的预计值，后者修正点击拾取后的实际入账值。修改 JSON 后，下一次计算阳光价值时自动重载；已经加入阳光栏的数值不会追溯修改。

`global.json` 按功能域分组。以后初始阳光、阳光上限、金币价值等经济规则继续加入 `economy`；游戏速度或难度等其他全局设置使用新的同级对象，禁止把关卡规则或植物个体属性混入该文件。

## 植物攻击伤害配置

植物攻击配置位于 `pvzmod/config/plants/attacks.jsonc`。使用 JSONC 是为了让每个字段旁边直接保留原版默认值和对应植物说明。它采用稀疏覆盖：只有取消注释并真正写入对象的键才生效；没有写出的键保持原版数值。当前模板只启用了 `projectiles.pea: 40`，所以豌豆从原版 20 改为 40；删除该行即可恢复 20。

```jsonc
{
  "schemaVersion": 1,
  "projectiles": {
    // "snowPea": 20, // 原版 20
    "pea": 40         // 原版 20
  },
  "directAttacks": {
    // "iceShroom": 20, // 原版 20
    // "squash": 1800   // 原版 1800
  }
}
```

规则：

- 伤害必须是 `0–1000000` 的整数，`0` 表示命中但不造成数值伤害。
- 未知键会使整份文件校验失败，继续使用上一次有效配置并写入日志，防止拼写错误被静默忽略。
- 投射物字段使用原版全局投射物定义表；西瓜、冰瓜和火球的溅射会随着基础伤害自动变化。西瓜/冰瓜的单体溅射是基础值的 `1/3`（整数除法）。
- `spikerock` 是每一段 20，地刺王一次攻击动画会打两段；改成 30 后即每段 30。
- 大嘴花普通吞食和缠绕海草属于直接处决，没有可替换的数值伤害；`chomperStrongTarget` 只控制大嘴花咬巨人、红眼巨人和僵王时的 40 点攻击。
- 樱桃炸弹、毁灭菇、辣椒和玉米加农炮原版通过燃烧/范围处决路径实现。配置为 1800 时保留原版燃烧处决；改为其他值时改走普通数值伤害，范围、音效和特效不变。
- 修改后，直接攻击在下一次命中时读取新值；投射物表在 DLL 启动和重新进入关卡时刷新。配置无效时保留上一次有效值。

### 原版植物攻击目录

| 植物 | 攻击方式 | 原版伤害 / 配置键 |
| --- | --- | --- |
| 豌豆射手、双发、三线、裂荚、机枪、左向射手 | 豌豆 | 20 / `pea` |
| 寒冰射手 | 冰豌豆 | 20 / `snowPea` |
| 小喷菇、胆小菇、海蘑菇 | 孢子 | 20 / `puff` |
| 大喷菇 | 穿透烟雾 | 20 / `fumeShroom` |
| 忧郁菇 | 每段范围喷射 | 20 / `gloomShroom` |
| 卷心菜投手 | 卷心菜 | 40 / `cabbage` |
| 玉米投手 | 玉米粒 / 黄油 | 20 / `kernel`；40 / `butter` |
| 西瓜投手 | 本体 / 溅射 | 80 / 26；配置 `melon` 后溅射自动取 1/3 |
| 冰瓜 | 本体 / 溅射 | 80 / 26；配置 `winterMelon` 后溅射自动取 1/3 |
| 火炬树桩 | 火球本体（由豌豆转化） | 40 / `fireball` |
| 杨桃 | 星星 | 20 / `star` |
| 仙人掌、香蒲 | 尖刺投射物 | 20 / `spike` |
| 地刺 | 每次接触攻击 / 车辆特攻 | 20 / `spikeweed`；1800 / `spikeVehicle` |
| 地刺王 | 每段攻击 / 车辆特攻 | 20 / `spikerock`；1800 / `spikeVehicle` |
| 寒冰菇 | 全屏伤害并冻结 | 20 / `iceShroom` |
| 窝瓜 | 范围压击 | 1800 / `squash` |
| 土豆雷 | 小范围爆炸 | 1800 / `potatoMine` |
| 樱桃炸弹 | 3×3 范围爆炸 | 等效 1800 / `cherryBomb` |
| 毁灭菇 | 大范围爆炸 | 等效 1800 / `doomShroom` |
| 火爆辣椒 | 整行燃烧 | 等效 1800 / `jalapeno` |
| 玉米加农炮 | 落点范围爆炸 | 等效 1800 / `cobCannon` |
| 大嘴花 | 普通目标吞食；强敌咬击 | 普通目标直接处决；强敌 40 / `chomperStrongTarget` |
| 缠绕海草 | 拖入水中 | 直接处决，无数值伤害 |
| 魅惑菇 | 魅惑吃掉它的僵尸 | 无数值伤害 |
| 三叶草 | 吹走飞行僵尸 | 无数值伤害 |
| 爆炸坚果（保龄球） | 范围爆炸 | 等效 1800 / `explodeONut` |
| 模仿者 | 继承所模仿植物 | 使用被模仿植物对应键 |

向日葵、阳光菇、双子向日葵、坚果、高坚果、睡莲、花盆、南瓜头、墓碑吞噬者、灯笼草、磁力菇、金盏花、咖啡豆、大蒜、叶子保护伞和吸金磁不对僵尸造成数值攻击，因此没有伤害键。

## JSON 分类约定

```text
pvzmod/config/
├─ levels/       # 关卡、波次、地图和出怪权重
├─ plants/       # 植物属性、技能和卡片
├─ zombies/      # 僵尸属性和行为
├─ elites/       # 精英编号、属性和技能
├─ bosses/       # Boss 阶段和技能
├─ ui/           # UI、按钮和界面
├─ settings/     # 全局设置及难度
└─ schemas/      # 配置格式版本与校验规则
```

Mod 存档放在 `pvzmod/saves`，日志放在 `pvzmod/logs`，两者都不能与静态 JSON 配置混放。完整规范见 `pvzmod/config/README.md`。

## 僵尸属性、防具与攻击配置

配置位于 `pvzmod/config/zombies/attributes.jsonc`。`zombies` 使用僵尸 ID 作为键，保持稀疏覆盖：示例只写了普通僵尸 ID `0`，因此路障、撑杆、铁桶等其他类型仍完全执行原版初始化。

```jsonc
{
  "schemaVersion": 1,
  "seed": 1511505931,
  "originalArmorHealth": {
    "trafficCone": 370,
    "bucket": 1100,
    "newspaper": 150,
    "screenDoor": 1100,
    "footballHelmet": 1400,
    "bobsled": 300,
    "balloon": 20,
    "diggerHelmet": 100,
    "ladder": 500,
    "wallnutHead": 1100,
    "tallnutHead": 2200
  },
  "armorDefinitions": {
    "1001": { "name": "一级铁桶防具", "tier": 1, "visual": "bucket", "health": 1100 },
    "2001": { "name": "二级铁门防具", "tier": 2, "visual": "door", "health": 1100 },
    "3001": { "name": "一级坚果头", "tier": 1, "visual": "wallnutHead", "health": 1100 }
  },
  "zombies": {
    "0": {
      "bodyHealth": 540,
      "attackDamage": 8,
      "armorRolls": [
        { "armorId": 1001, "chance": 50 },
        { "armorId": 2001, "chance": 25 },
        { "armorId": 3001, "chance": 100 }
      ]
    }
  }
}
```

`originalArmorHealth` 是原版防具目录，不需要对应的 `zombies` 条目：

| 配置键 | 僵尸 ID | 原版槽位 | 默认生命 |
|---|---:|---|---:|
| `trafficCone` | 2 | 头盔 | 370 |
| `bucket` | 4 | 头盔 | 1100 |
| `newspaper` | 5 | 盾牌 | 150 |
| `screenDoor` | 6 | 盾牌 | 1100 |
| `footballHelmet` | 7 | 头盔 | 1400 |
| `bobsled` | 13 | 头盔/雪橇防护 | 300 |
| `balloon` | 16 | 飞行生命池 | 20 |
| `diggerHelmet` | 17 | 头盔 | 100 |
| `ladder` | 21 | 盾牌 | 500 |
| `wallnutHead` | 27 | 头盔 | 1100 |
| `tallnutHead` | 31 | 头盔 | 2200 |

表中数值保持默认时运行时不写回对象；例如只把 `footballHelmet` 改为 `2000`，就只在橄榄球僵尸生成后把头盔当前/最大生命覆盖为 2000。删除键与保留默认值都表示沿用原版。未知键会拒绝整份新配置，防止拼写错误被静默忽略。原版还保留 `HelmType 5/6/9`（红眼、头带、独立高坚果）枚举，但 1051 初始化代码没有为它们建立独立生命池，因此目录不伪造无效数值。

- 普通僵尸原版本体生命为 270，示例改为 540；字段同时修改当前值和最大值。
- 原版普通啃食每次有效扣 4，示例改为 8。冰冻帧等原版跳过啃食的情况仍然不扣血。
- `chance` 范围为 `0–100`：0 永不附加，100 必定附加。随机结果由 `seed + 僵尸类型 + 僵尸实例 ID + 防具 ID` 确定，相同条件可复现。
- Mod 防具 ID 与原版内部枚举解耦。`tier` 是改版的进阶等级：示例规定铁桶为一级、铁门为二级。
- 铁桶使用头盔槽，铁门使用盾牌槽，所以两次概率都成功时可以同时出现；伤害顺序仍由原版决定：盾牌 → 头盔 → 本体。
- 同一槽位有多个防具同时抽中，选择 `tier` 较高者；等级相同时选择较小的防具 ID，结果不依赖 JSON 排列顺序。
- 当前视觉适配器支持 `cone`、`bucket`、`door`；`wallnutHead` 适配普通僵尸 ID `0`。坚果头会建立原版 `REANIM_WALLNUT` 附着动画，沿用裂纹受伤效果和掉头清理，不只是写入头盔生命字段。特殊动画族会跳过不兼容防具并写日志。
- `wallnutHead` 与铁桶都占头盔槽。示例中两者同为 `tier=1`，若铁桶的 50% 抽取成功，则同级较小 ID `1001` 胜出；否则装备 `3001`。若要普通僵尸必定使用坚果头，可提高 `3001.tier` 或把其他头盔槽概率设为 0。
- `armorRolls` 省略时不改原版防具；写入概率 0 表示该定义不会额外出现，不会剥掉某类僵尸身份自带的原版装备。

运行时架构和新增模块约束见 [ARCHITECTURE.md](H:/pvz/modding/ARCHITECTURE.md)。

### 0.5.1 防具崩溃修复

0.5.0 的防具视觉裸汇编桥接器与 C++ 调用约定不一致：桥接器使用 `ret N` 清理参数，默认 `cdecl` 调用方又清理一次，抽中铁桶或铁门后会破坏栈。0.5.1 将两个桥接器明确声明为 `__stdcall`，并通过 Release 对象反汇编确认调用后不再重复清栈。若 Windows 事件曾显示 `pvzmod.dll`、异常 `0xc0000409`、快速失败代码 2，应确认根目录 DLL 日志版本为 0.5.1 或更高。

### 0.6.1 坚果头随机防具适配

`visual: "wallnutHead"` 不能用普通僵尸身体上的静态轨道开关实现。0.6.1 在普通僵尸初始化前先完成确定性头盔选择；抽中时暂时调用原版坚果头僵尸 ID 27 的初始化分支，让游戏自己创建、播放并附着 `REANIM_WALLNUT`，初始化后立即恢复原来的僵尸 ID，再应用配置耐久、本体生命和攻击伤害。`Zombie::DropHead` 钩子只在该实例掉头期间临时恢复 ID 27 的清理语义，随后还原 ID 0，避免特殊头部动画残留或污染普通僵尸行为。

## 精英僵尸与外部贴图（0.9.0）

配置分为两个独立模块：

- `pvzmod/config/resources/textures.jsonc` 注册通用图片 ID。植物、僵尸、卡片和 UI 后续都应调用同一个 `ResolveExternalTexture` 接口，禁止各模块各写一套图片加载器。
- `pvzmod/config/elites/zombies.jsonc` 注册精英。默认 `RAGE` 仅匹配普通僵尸 ID `0`，以 20% 的确定性概率生成，并由 `BERSERK` 技能应用生命 `1.5`、速度 `1.35`、攻击 `2.0` 倍率。

精英状态不写入未经确认的原版对象空隙。初始化事件取得僵尸指针后，运行时以 `Zombie* + instanceId` 建立侧挂记录；删除 Hook 和地址复用检查负责清理，避免后续对象继承旧精英编号。基础 `zombie_hook.cpp` 只发布初始化和攻击伤害事件，精英选择、倍率、技能和绘制均在独立模块中完成。

外部图片路径必须位于 `pvzmod/images/`。下面的注册项会在首次绘制命中精英时尝试加载：

```jsonc
{ "id": "KILL", "path": "pvzmod/images/zi/kill.png" }
```

`ID` 支持英文、数字和下划线且区分大小写。加载失败只影响这张替换贴图，红色 tint、属性倍率、技能和原版绘制继续运行；失败 ID 在同一进程内只记录一次。当前已在 `1-10` 验证逐实例红色狂暴僵尸、缺图降级，以及 64×64 `KILL` PNG 成功解码并替换普通僵尸 `anim_head1` 头部轨道。图片沿用原轨道的移动、旋转、缩放和显隐；完整部位目录见 [ZOMBIE_TEXTURE_TRACKS.md](H:/pvz/modding/ZOMBIE_TEXTURE_TRACKS.md)。

技能由注册表按事件分发：`Spawn`、`BeforeUpdate`、`AfterUpdate`、`BeforeAttack`、`BeforeDraw`、`AfterDraw`、`Remove`。JSON 只能引用 DLL 已注册的技能 ID，未知技能会禁用整份精英配置，不能从配置执行任意代码。完整接口和安全边界见 [ELITE_AND_TEXTURE_DESIGN.md](H:/pvz/modding/ELITE_AND_TEXTURE_DESIGN.md)。

## 恢复原版

退出游戏后，将 `PlantsVsZombies.original.exe` 复制回 `PlantsVsZombies.exe`，并移走 `pvzmod.dll` 即可恢复。原版备份不会被补丁器再次覆盖。
