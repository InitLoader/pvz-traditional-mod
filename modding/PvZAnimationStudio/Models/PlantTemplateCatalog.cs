namespace PvZAnimationStudio.Models;

public sealed record PlantTemplateDefinition(
    int Id,
    string SeedConstant,
    string InternalName,
    string ChineseName,
    string CarrierReanimation,
    string CompiledStem,
    bool IsRuntimeTemplate,
    string Notes = "")
{
    public string CompiledFileName => $"{CompiledStem}.reanim.compiled";

    public string DisplayLabel => IsRuntimeTemplate
        ? $"{Id:D2} · {ChineseName}（{SeedConstant}）"
        : $"{Id:D2} · {ChineseName}（模式专用）";

    public string Summary => IsRuntimeTemplate
        ? $"{SeedConstant}；载体 {CarrierReanimation}；资源 {CompiledFileName}"
        : $"{SeedConstant}；原版模式专用，不能作为当前自定义植物的 templatePlantId。{Notes}";
}

/// <summary>
/// PvZ PC 1.0.0.1051 的植物 SeedType 全局目录。
/// 0-48 是当前自定义植物运行时允许复用的模板；49-52 仅供识别和审计。
/// 所有编辑器植物模板下拉框、校验与文档测试都应以此目录为准。
/// </summary>
public static class PlantTemplateCatalog
{
    public const int FirstPlantId = 0;
    public const int LastRuntimeTemplateId = 48;
    public const int LastPlantId = 52;

    public static IReadOnlyList<PlantTemplateDefinition> All { get; } =
    [
        new(0, "SEED_PEASHOOTER", "PEASHOOTER", "豌豆射手", "REANIM_PEASHOOTER", "PeaShooter", true),
        new(1, "SEED_SUNFLOWER", "SUNFLOWER", "向日葵", "REANIM_SUNFLOWER", "SunFlower", true),
        new(2, "SEED_CHERRYBOMB", "CHERRY_BOMB", "樱桃炸弹", "REANIM_CHERRYBOMB", "CherryBomb", true),
        new(3, "SEED_WALLNUT", "WALL_NUT", "坚果", "REANIM_WALLNUT", "Wallnut", true),
        new(4, "SEED_POTATOMINE", "POTATO_MINE", "土豆地雷", "REANIM_POTATOMINE", "PotatoMine", true),
        new(5, "SEED_SNOWPEA", "SNOW_PEA", "寒冰射手", "REANIM_SNOWPEA", "SnowPea", true),
        new(6, "SEED_CHOMPER", "CHOMPER", "大嘴花", "REANIM_CHOMPER", "Chomper", true),
        new(7, "SEED_REPEATER", "REPEATER", "双发射手", "REANIM_REPEATER", "PeaShooter", true,
            "REANIM_REPEATER 在资源表中复用 PeaShooter compiled。"),
        new(8, "SEED_PUFFSHROOM", "PUFF_SHROOM", "小喷菇", "REANIM_PUFFSHROOM", "Puffshroom", true),
        new(9, "SEED_SUNSHROOM", "SUN_SHROOM", "阳光菇", "REANIM_SUNSHROOM", "SunShroom", true),
        new(10, "SEED_FUMESHROOM", "FUME_SHROOM", "大喷菇", "REANIM_FUMESHROOM", "Fumeshroom", true),
        new(11, "SEED_GRAVEBUSTER", "GRAVE_BUSTER", "墓碑吞噬者", "REANIM_GRAVE_BUSTER", "Gravebuster", true),
        new(12, "SEED_HYPNOSHROOM", "HYPNO_SHROOM", "魅惑菇", "REANIM_HYPNOSHROOM", "Hypnoshroom", true),
        new(13, "SEED_SCAREDYSHROOM", "SCAREDY_SHROOM", "胆小菇", "REANIM_SCRAREYSHROOM", "ScaredyShroom", true,
            "原版 Reanimation 枚举常量保留 SCRAREY 的历史拼写。"),
        new(14, "SEED_ICESHROOM", "ICE_SHROOM", "寒冰菇", "REANIM_ICESHROOM", "Iceshroom", true),
        new(15, "SEED_DOOMSHROOM", "DOOM_SHROOM", "毁灭菇", "REANIM_DOOMSHROOM", "DoomShroom", true),
        new(16, "SEED_LILYPAD", "LILY_PAD", "睡莲", "REANIM_LILYPAD", "Lilypad", true),
        new(17, "SEED_SQUASH", "SQUASH", "窝瓜", "REANIM_SQUASH", "Squash", true),
        new(18, "SEED_THREEPEATER", "THREEPEATER", "三线射手", "REANIM_THREEPEATER", "ThreePeater", true),
        new(19, "SEED_TANGLEKELP", "TANGLE_KELP", "缠绕水草", "REANIM_TANGLEKELP", "Tanglekelp", true),
        new(20, "SEED_JALAPENO", "JALAPENO", "火爆辣椒", "REANIM_JALAPENO", "Jalapeno", true),
        new(21, "SEED_SPIKEWEED", "SPIKEWEED", "地刺", "REANIM_SPIKEWEED", "Caltrop", true),
        new(22, "SEED_TORCHWOOD", "TORCHWOOD", "火炬树桩", "REANIM_TORCHWOOD", "Torchwood", true),
        new(23, "SEED_TALLNUT", "TALL_NUT", "高坚果", "REANIM_TALLNUT", "Tallnut", true),
        new(24, "SEED_SEASHROOM", "SEA_SHROOM", "海蘑菇", "REANIM_SEASHROOM", "SeaShroom", true),
        new(25, "SEED_PLANTERN", "PLANTERN", "路灯花", "REANIM_PLANTERN", "Plantern", true),
        new(26, "SEED_CACTUS", "CACTUS", "仙人掌", "REANIM_CACTUS", "Cactus", true),
        new(27, "SEED_BLOVER", "BLOVER", "三叶草", "REANIM_BLOVER", "Blover", true),
        new(28, "SEED_SPLITPEA", "SPLIT_PEA", "裂荚射手", "REANIM_SPLITPEA", "SplitPea", true),
        new(29, "SEED_STARFRUIT", "STARFRUIT", "杨桃", "REANIM_STARFRUIT", "Starfruit", true),
        new(30, "SEED_PUMPKINSHELL", "PUMPKIN", "南瓜头", "REANIM_PUMPKIN", "Pumpkin", true),
        new(31, "SEED_MAGNETSHROOM", "MAGNET_SHROOM", "磁力菇", "REANIM_MAGNETSHROOM", "Magnetshroom", true),
        new(32, "SEED_CABBAGEPULT", "CABBAGE_PULT", "卷心菜投手", "REANIM_CABBAGEPULT", "Cabbagepult", true),
        new(33, "SEED_FLOWERPOT", "FLOWER_POT", "花盆", "REANIM_FLOWER_POT", "Pot", true),
        new(34, "SEED_KERNELPULT", "KERNEL_PULT", "玉米投手", "REANIM_KERNELPULT", "Cornpult", true),
        new(35, "SEED_INSTANT_COFFEE", "COFFEE_BEAN", "咖啡豆", "REANIM_COFFEEBEAN", "Coffeebean", true),
        new(36, "SEED_GARLIC", "GARLIC", "大蒜", "REANIM_GARLIC", "Garlic", true),
        new(37, "SEED_UMBRELLA", "UMBRELLA_LEAF", "叶子保护伞", "REANIM_UMBRELLALEAF", "Umbrellaleaf", true),
        new(38, "SEED_MARIGOLD", "MARIGOLD", "金盏花", "REANIM_MARIGOLD", "Marigold", true),
        new(39, "SEED_MELONPULT", "MELON_PULT", "西瓜投手", "REANIM_MELONPULT", "Melonpult", true),
        new(40, "SEED_GATLINGPEA", "GATLING_PEA", "机枪射手", "REANIM_GATLINGPEA", "GatlingPea", true),
        new(41, "SEED_TWINSUNFLOWER", "TWIN_SUNFLOWER", "双子向日葵", "REANIM_TWIN_SUNFLOWER", "TwinSunFlower", true),
        new(42, "SEED_GLOOMSHROOM", "GLOOM_SHROOM", "忧郁菇", "REANIM_GLOOMSHROOM", "GloomShroom", true),
        new(43, "SEED_CATTAIL", "CATTAIL", "香蒲", "REANIM_CATTAIL", "Cattail", true),
        new(44, "SEED_WINTERMELON", "WINTER_MELON", "冰西瓜", "REANIM_WINTER_MELON", "WinterMelon", true),
        new(45, "SEED_GOLD_MAGNET", "GOLD_MAGNET", "吸金磁", "REANIM_GOLD_MAGNET", "GoldMagnet", true),
        new(46, "SEED_SPIKEROCK", "SPIKEROCK", "地刺王", "REANIM_SPIKEROCK", "SpikeRock", true),
        new(47, "SEED_COBCANNON", "COB_CANNON", "玉米加农炮", "REANIM_COBCANNON", "CobCannon", true),
        new(48, "SEED_IMITATER", "IMITATER", "模仿者", "REANIM_IMITATER", "Imitater", true),
        new(49, "SEED_EXPLODE_O_NUT", "EXPLODE_O_NUT", "爆炸坚果", "REANIM_WALLNUT", "Wallnut", false,
            "仅用于坚果保龄球等特殊规则。"),
        new(50, "SEED_GIANT_WALLNUT", "GIANT_WALLNUT", "巨大坚果", "REANIM_WALLNUT", "Wallnut", false,
            "仅用于坚果保龄球等特殊规则。"),
        new(51, "SEED_SPROUT", "SPROUT", "禅境花园幼苗", "REANIM_ZENGARDEN_SPROUT", "ZenGarden_sprout", false,
            "仅用于禅境花园。"),
        new(52, "SEED_LEFTPEATER", "LEFTPEATER", "反向双发射手", "REANIM_REPEATER", "PeaShooter", false,
            "仅用于我是僵尸等特殊规则。")
    ];

    public static IReadOnlyList<PlantTemplateDefinition> RuntimeTemplates { get; } =
        All.Where(definition => definition.IsRuntimeTemplate).ToArray();

    public static PlantTemplateDefinition? Find(int id) =>
        id >= FirstPlantId && id <= LastPlantId ? All[id] : null;

    public static bool IsRuntimeTemplate(int id) => Find(id)?.IsRuntimeTemplate == true;
}
