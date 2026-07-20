# 精英僵尸与外部贴图系统设计

本文是 `0.9.0` 的实现契约。精英系统和资源系统必须保持独立：资源模块不知道植物、僵尸或 UI；精英模块只保存贴图 ID，不直接解析图片文件。

## 1. 外部贴图注册表

配置文件固定为 `pvzmod/config/resources/textures.jsonc`，图片文件统一放在 `pvzmod/images/` 下。禁止把 JSON 或图片直接堆在游戏根目录。

```jsonc
{
  "schemaVersion": 1,
  "textures": [
    {
      "id": "KILL",
      "path": "pvzmod/images/zi/kill.png"
    }
  ]
}
```

规则：

- `id` 必须匹配 `[A-Za-z0-9_]+`，长度为 1-64，大小写敏感。`KILL`、`zombie_head_01` 和 `UI2` 合法；空格、中文、斜杠和点号不合法。
- 标准字段名是小写 `id` 和 `path`。为了兼容手写配置，解析器同时接受 `ID`、`Path` 和常见误写 `Patch`，但文档与自动生成配置只输出标准字段名。
- `path` 必须是相对于游戏根目录的路径，并且规范化后仍位于 `pvzmod/images/` 内。绝对路径、盘符、UNC、`..` 和越界符号链接均拒绝。
- 首批支持 `.png`、`.jpg`、`.jpeg`、`.bmp` 和 `.gif`；推荐带透明通道的 PNG。
- ID 不直接等于游戏内部 `Image*`。运行时建立“字符串 ID → 定义 → Image*”缓存，植物、僵尸、UI 和其他模块只能通过查询接口获取图片。
- 图片第一次被使用时通过原版 `SexyAppBase::GetImage` 加载。加载失败只记录一次日志并返回空指针，调用者必须继续执行原版绘制，不能崩溃。
- 已加载图片在当前进程内保持有效。0.9.0 不热替换已经加载的图片，修改文件后需要完全退出并重启游戏，避免旧 `Image*` 被绘制线程继续引用。

通用查询接口：

```cpp
void* ResolveExternalTexture(std::string_view textureId, void* lawnApp);
```

资源模块只负责验证、加载、缓存和日志。植物、僵尸或 UI 的位置、缩放、动画和命中逻辑不得写入资源模块。

## 2. 精英编号与实例状态

精英配置固定为 `pvzmod/config/elites/zombies.jsonc`。每个精英同时拥有：

- `runtimeId`：1-65535 的稳定数字编号，0 永远表示普通僵尸。
- `id`：匹配 `[A-Za-z0-9_]+` 的字符串键，用于 JSON、日志和技能注册。
- `Zombie* + instanceId`：运行时侧挂键，防止对象池地址复用后继承上一只僵尸的状态。

寄存器只在 Hook 调用边界临时传递新僵尸指针或编号，不能作为长期存储。精英状态保存在 DLL 侧挂表，不写入未经确认的原版对象空隙。

同一只僵尸可能命中多个精英概率时，先选较高 `priority`，相同优先级选较小 `runtimeId`。概率由 `seed + zombieType + instanceId + runtimeId` 确定，同一关卡对象序列可复现。

## 3. 首个精英：狂暴怪

默认示例只允许普通僵尸 ID `0` 以 20% 概率变为 `RAGE`：

- `BERSERK` 生成技能增加生命、移动速度和啃食伤害。
- `visual.tint` 通过原版 `Graphics::SetColor` 与 `SetColorizeImages` 把整只僵尸染红，绘制后恢复原 Graphics 状态，不污染其他单位。
- `visual.overlayTextureId` 可引用 `KILL`，在僵尸坐标基础上按 `offsetX/offsetY` 绘制外部 PNG。图片不存在时仍保留红色精英效果。
- 删除僵尸对象时必须清理侧挂状态；再次使用同一地址的新僵尸必须重新抽取。

## 4. 技能注册接口

精英配置不能直接执行任意代码。JSON 中的 `skills[].id` 必须对应 DLL 中已注册的技能处理器，未知技能使整份精英配置加载失败。

```cpp
enum class EliteSkillEvent {
    Spawn,
    BeforeUpdate,
    AfterUpdate,
    BeforeAttack,
    BeforeDraw,
    AfterDraw,
    Remove
};

using EliteSkillCallback = void (*)(EliteSkillEvent, EliteSkillContext&);

bool RegisterEliteSkill(std::string id, EliteSkillCallback callback);
bool DispatchEliteSkill(std::string_view id, EliteSkillEvent event,
                        EliteSkillContext& context);
```

`EliteSkillContext` 提供当前僵尸、精英实例状态、只读参数和有限的倍率/伤害修改字段。技能不得持有配置对象内部指针，也不得直接访问其他业务模块的全局状态。

首批内置技能 `BERSERK` 在 `Spawn` 阶段读取 `healthMultiplier`、`speedMultiplier` 和 `attackMultiplier`。运行时还会分发生命周期事件，为后续低血量狂暴、周期召唤、受击触发和死亡爆炸保留稳定接口。0.9.0 不把这些未来技能硬编码进 `zombie_hook.cpp`。

## 5. 模块边界

```text
external_texture_config   解析并校验贴图注册表
external_texture_runtime  调用原版图片加载器并缓存 Image*
elite_zombie_config       解析精英、概率、视觉和技能绑定
elite_skill_registry      注册和分发技能事件
elite_zombie_hook         精英抽取、侧挂状态、绘制和清理
zombie_event_bus          原版僵尸 Hook 与扩展模块之间的通用事件桥
zombie_hook               继续只负责基础属性、防具和原版啃食流程
```

未来植物、卡片和 UI 使用外部贴图时，只依赖 `external_texture_runtime`。未来新增精英技能时，只注册技能处理器并增加配置，不继续扩大精英主 Hook 或僵尸基础 Hook。

## 6. 安全与回退

- 任一 Hook 入口字节不匹配 `1.0.0.1051` 时，精英模块拒绝安装并写日志。
- 配置无效时不生成精英，原版僵尸继续正常运行。
- 贴图缺失或解码失败时只跳过该贴图，不跳过精英属性和技能。
- 倍率、概率、坐标和数组数量均有上限；所有整数乘法在写回游戏对象前钳制。
- 外部图片只是数据，不加载 DLL、脚本或可执行内容。
