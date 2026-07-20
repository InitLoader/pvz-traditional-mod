# 外部动作、Reanimation 与真正新增植物/僵尸

本文定义 PvZ PC `1.0.0.1051` 外部动作资源管线和真正新增实体的实现契约。目标不是继续给原版轨道换图，而是让 Mod 实体拥有独立逻辑 ID、外部动画、动作状态机、帧事件、属性、攻击、UI 和存档数据。

## 1. 当前实现状态

`0.10.0-dev` 已完成第一阶段：

- `pvzmod/config/resources/animations.jsonc` 外部动画注册表。
- `pvzmod/animations/` 分类资源目录和安全路径限制。
- Raw `.reanim` 解析器，支持 `fps`、`doScale`、轨道和逐帧 Transform。
- 支持的 Transform 字段为 `x/y/kx/ky/sx/sy/f/a/i/font/text`。
- 动作、循环方式、播放速度、混合帧、帧事件和定位轨道配置。
- 动画、动作轨道、事件帧、定位轨道和外部贴图 ID 的启动时交叉校验。
- 只读运行时动画注册表。

当前尚未安装 `ReanimationInitializeType` Detour，也不会把自定义 Definition 写入原版全局数组。日志中的 `Runtime engine injection is not enabled yet` 是明确的阶段提示，不是加载错误。

## 2. 原版动画模型

PvZ 的 Reanimation 是分层变换动画，不是 GIF：

```text
ReanimatorDefinition
├─ FPS
└─ ReanimatorTrack[]
   ├─ name
   └─ ReanimatorTransform[]
      ├─ x/y          平移
      ├─ kx/ky        旋转或倾斜
      ├─ sx/sy        缩放
      ├─ f            显示帧；-1 通常表示隐藏
      ├─ a            透明度
      └─ i            Image*
```

`anim_idle`、`anim_attack`、`anim_die` 等动作也是轨道。原版按动作轨道中首个和最后一个 `f >= 0` 帧确定动作范围；省略的 Transform 字段继承前一帧，首帧默认位置 0、缩放 1、显示帧 0、透明度 1。

`.reanim` 只描述视觉时间轴，不执行伤害、发射、啃咬、召唤和掉落。真正的玩法动作由 DLL 行为控制器在动画事件到达时执行。

## 3. 资源目录

```text
pvzmod/
├─ config/
│  ├─ resources/
│  │  ├─ textures.jsonc
│  │  └─ animations.jsonc
│  ├─ plants/custom_plants.jsonc
│  └─ zombies/custom_zombies.jsonc       # 后续阶段
├─ images/
│  ├─ plants/<entity>/
│  └─ zombies/<entity>/
└─ animations/
   ├─ plants/<entity>/<animation>.reanim
   └─ zombies/<entity>/<animation>.reanim
```

JSON/JSONC 只能放在 `pvzmod/config/` 的所属分类中；Raw 动画只能放在 `pvzmod/animations/`；图片继续只放在 `pvzmod/images/`。配置和运行资源不能散放在游戏根目录。

## 4. 动画注册格式

```jsonc
{
  "schemaVersion": 1,
  "animations": [
    {
      "id": "PLANT_FIRE_PEA",
      "path": "pvzmod/animations/plants/fire_pea/fire_pea.reanim",

      // 以后只作为进入原版 ReanimationHolder 的安全载体。
      "carrierReanimation": "REANIM_PEASHOOTER",

      // Raw .reanim 图片符号 -> textures.jsonc 的字符串 ID。
      "images": {
        "IMAGE_REANIM_FIRE_PEA_BODY": "FIRE_PEA_BODY",
        "IMAGE_REANIM_FIRE_PEA_HEAD": "FIRE_PEA_HEAD"
      },

      "actions": {
        "idle": {
          "track": "anim_idle",
          "loop": "loop",
          "rate": 12,
          "blendFrames": 4
        },
        "attack": {
          "track": "anim_attack",
          "loop": "once_hold",
          "rate": 18,
          "blendFrames": 3,
          "events": [
            { "id": "FIRE_PROJECTILE", "frame": 9, "oncePerLoop": true }
          ]
        }
      },

      "locators": {
        "projectile": "locator_mouth",
        "head": "attacher__head"
      }
    }
  ]
}
```

### 字段规则

- `id`：大小写敏感，匹配 `[A-Za-z0-9_]+`，长度 1–64。
- `path`：必须是 `pvzmod/animations/` 下的相对 `.reanim` 路径，拒绝绝对路径、盘符、UNC 和 `..`。
- `carrierReanimation`：稳定的原版动画符号名，只作为对象池载体，不代表复用其图片或动作。
- `images`：Raw Reanimation 中的图片符号到外部贴图 ID 的映射；贴图 ID 必须已经登记在 `textures.jsonc`。
- `actions`：至少一个动作，动作 ID 匹配 `[A-Za-z0-9_]+`。
- `loop`：`loop`、`once`、`once_hold`。
- `rate`：大于 0 且不超过 120。
- `blendFrames`：0–120。
- `events[].frame`：动作范围内的相对帧。
- `events[].normalizedTime`：0–1；与 `frame` 二选一。
- `locators`：逻辑挂点名到实际轨道名的映射，目标轨道必须存在。

## 5. Raw `.reanim` 安全边界

运行时解析器采用拒绝优先策略：

- 单文件必须在 1 字节至 16 MiB 之间。
- 最多 512 条轨道、每轨最多 20000 帧、总计最多 500000 个 Transform。
- 所有轨道必须具有相同帧数。
- FPS 必须在 `(0, 120]`。
- Alpha 必须在 `[0, 1]`。
- 浮点数必须有限并处于安全范围。
- 轨道名大小写不敏感地保持唯一。
- 拒绝未知 XML 字段、重复 Transform 字段、DTD 和实体声明。
- 单个动画失败只跳过该动画并写日志，不把半初始化 Definition 交给原版游戏。

## 6. 动画制作流程

### 6.1 分层绘制

角色应拆分为带透明通道的 PNG 部件，例如：

```text
body.png
head.png
jaw.png
arm_front.png
arm_back.png
leg_front.png
leg_back.png
weapon.png
eye.png
shadow.png
```

植物根节点放在植株底部中心附近；僵尸根节点放在脚底或两脚之间。每个部件的原点必须保持稳定，图片边缘预留旋转空间。

### 6.2 时间轴和轨道

- 每个视觉部件使用独立轨道。
- `anim_idle`、`anim_attack`、`anim_die` 等轨道标记动作范围。
- `locator_*` 轨道不绘制图片，用作子弹、粒子、音效或碰撞定位。
- `attacher__*` 轨道用于附加头部、帽子、武器或其他子 Reanimation。
- 动作范围外使用 `f=-1`，范围内使用非负 `f`。

### 6.3 工具路线

1. 使用 PopStudio 或 Twinning 解码一个相近的原版 `.reanim.compiled`。
2. 转为 Raw XML、JSON 或 XFL，保留原版坐标比例作为参考。
3. 在 Adobe Animate/XFL 时间轴或兼容编辑流程中替换部件并制作动作。
4. 使用 `FlashReanimExportAsRaw_Xml.jsfl` 或转换工具导出 Raw `.reanim`。
5. 把图片符号写入 `animations.jsonc.images`，把贴图文件登记到 `textures.jsonc`。
6. 完全退出并重新启动游戏，通过 `pvzmod/logs/pvzmod.log` 检查解析和交叉校验。

运行时第一选择是 Raw `.reanim`，不直接依赖原版 compiled 缓存。`.reanim.compiled` 只作为制作端导入/导出格式。

## 7. 真正新增实体架构

推荐使用“原版对象池载体 + DLL 侧独立实体”方案：

```text
Plant*/Zombie* 原版对象池
        │
        ├─ carrierPlantId / carrierZombieId
        │    只保证 Board 遍历、碰撞和回收安全
        │
        └─ CustomEntityState 侧挂记录
             ├─ 独立逻辑 ID
             ├─ 外部 animationId
             ├─ 行为控制器
             ├─ 属性和技能
             ├─ 当前动作和事件状态
             └─ Mod 存档数据
```

载体 ID 不再决定名称、属性、图片、动作和攻击，因此不是原版实体的玩法套壳。暂不扩大原版 SeedType、ZombieType 和 ReanimationType 固定数组。

## 8. 后续运行时注入设计

下一阶段建立独立模块：

```text
custom_reanim_definition    Raw 数据 -> ABI 兼容 Definition
animation_texture_resolver 图片符号 -> Image*
animation_instance_hook     对接 ReanimationInitializeType
custom_animation_runtime    动画实例创建、查找、销毁
animation_controller        动作、混合、速率、循环
animation_event_bus         发射、啃咬、爆炸等帧事件
custom_plant_runtime        独立植物状态机
custom_zombie_runtime       独立僵尸状态机
custom_entity_save          Mod 存档和版本迁移
custom_animation_preview    卡片、选卡、图鉴和预览
```

创建动画时使用线程局部 `PendingCustomDefinition`：调用原版 `AddReanimation` 分配 Holder 实例，Detour `ReanimationInitializeType` 检测待创建定义并初始化自定义 Definition，同时保留安全的原版载体 ReanimationType。这样不扩大 `NUM_REANIMS`，也不先创建再破坏性替换轨道数组。

## 9. 动作事件

动作事件只能引用 DLL 中注册的处理器，JSON 不能执行任意代码。首批事件计划：

- 植物：`FIRE_PROJECTILE`、`GENERATE_SUN`、`EXPLODE`、`SPAWN_CHILD`。
- 僵尸：`BITE_HIT`、`THROW_OBJECT`、`SUMMON_ZOMBIE`、`DROP_ARMOR`。
- 通用：`PLAY_SOUND`、`SPAWN_PARTICLE`、`ENABLE_HITBOX`、`DISABLE_HITBOX`。

控制器需要记录每次循环已触发事件，使用原版等价的跨帧判定，避免掉帧时漏事件或同一帧重复发射。

## 10. 存档和热加载

原版存档不能写入超出固定枚举的动画或实体 ID。Mod 存档保存：

```text
custom entity id
carrier id
health/state/cooldown
current action
normalized animation time
behavior state
schema version
```

读档后重新创建外部动画。热加载采用 Definition 世代：新实例使用新 Definition，旧 Definition 在引用归零前保持存活，禁止原地释放仍被 `Reanimation*` 引用的轨道和图片。

## 11. 验收顺序

1. 外部动画配置、Raw 解析和注册表测试通过。
2. 在测试绘制入口播放 `idle/attack/die`，不绑定植物或僵尸。
3. 自定义植物使用外部动画，并在攻击帧产生真实子弹。
4. 自定义僵尸使用外部动画、移动、啃咬和死亡。
5. 卡片、选卡、关卡、图鉴和读档显示同一动画。
6. 删除载体植物/僵尸的原版图片后仍能显示，证明视觉不再复用模板。

所有新 ABI 入口必须针对 `PlantsVsZombies.exe 1.0.0.1051` 重新反汇编，确认寄存器传参、栈清理和入口字节后才能安装 Hook。
