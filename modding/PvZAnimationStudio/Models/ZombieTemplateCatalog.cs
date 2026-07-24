namespace PvZAnimationStudio.Models;

public sealed record ZombieTemplateDefinition(
    int Id,
    string InternalName,
    string ChineseName,
    string CarrierReanimation)
{
    public string Summary => $"{InternalName}；载体 {CarrierReanimation}";
}

/// <summary>
/// PvZ PC 1.0.0.1051 ZombieType 0-32 的身体 Reanimation 载体目录。
/// 鸭子、I/Z 模式等类型仍复用普通僵尸身体；原版附件由游戏继续管理。
/// </summary>
public static class ZombieTemplateCatalog
{
    public const int FirstZombieId = 0;
    public const int LastZombieId = 32;

    public static IReadOnlyList<ZombieTemplateDefinition> All { get; } =
    [
        new(0, "ZOMBIE_NORMAL", "普通僵尸", "REANIM_ZOMBIE"),
        new(1, "ZOMBIE_FLAG", "旗帜僵尸", "REANIM_ZOMBIE"),
        new(2, "ZOMBIE_TRAFFIC_CONE", "路障僵尸", "REANIM_ZOMBIE"),
        new(3, "ZOMBIE_POLEVAULTER", "撑杆僵尸", "REANIM_POLEVAULTER"),
        new(4, "ZOMBIE_PAIL", "铁桶僵尸", "REANIM_ZOMBIE"),
        new(5, "ZOMBIE_NEWSPAPER", "读报僵尸", "REANIM_ZOMBIE_NEWSPAPER"),
        new(6, "ZOMBIE_DOOR", "铁门僵尸", "REANIM_ZOMBIE"),
        new(7, "ZOMBIE_FOOTBALL", "橄榄球僵尸", "REANIM_ZOMBIE_FOOTBALL"),
        new(8, "ZOMBIE_DANCER", "舞王僵尸", "REANIM_DANCER"),
        new(9, "ZOMBIE_BACKUP_DANCER", "伴舞僵尸", "REANIM_BACKUP_DANCER"),
        new(10, "ZOMBIE_DUCKY_TUBE", "鸭子救生圈僵尸", "REANIM_ZOMBIE"),
        new(11, "ZOMBIE_SNORKEL", "潜水僵尸", "REANIM_SNORKEL"),
        new(12, "ZOMBIE_ZAMBONI", "冰车僵尸", "REANIM_ZOMBIE_ZAMBONI"),
        new(13, "ZOMBIE_BOBSLED", "雪橇僵尸", "REANIM_BOBSLED"),
        new(14, "ZOMBIE_DOLPHIN_RIDER", "海豚骑士僵尸", "REANIM_ZOMBIE_DOLPHINRIDER"),
        new(15, "ZOMBIE_JACK_IN_THE_BOX", "小丑僵尸", "REANIM_JACKINTHEBOX"),
        new(16, "ZOMBIE_BALLOON", "气球僵尸", "REANIM_BALLOON"),
        new(17, "ZOMBIE_DIGGER", "矿工僵尸", "REANIM_DIGGER"),
        new(18, "ZOMBIE_POGO", "跳跳僵尸", "REANIM_POGO"),
        new(19, "ZOMBIE_YETI", "雪人僵尸", "REANIM_YETI"),
        new(20, "ZOMBIE_BUNGEE", "蹦极僵尸", "REANIM_BUNGEE"),
        new(21, "ZOMBIE_LADDER", "梯子僵尸", "REANIM_LADDER"),
        new(22, "ZOMBIE_CATAPULT", "投石车僵尸", "REANIM_CATAPULT"),
        new(23, "ZOMBIE_GARGANTUAR", "伽刚特尔", "REANIM_GARGANTUAR"),
        new(24, "ZOMBIE_IMP", "小鬼僵尸", "REANIM_IMP"),
        new(25, "ZOMBIE_BOSS", "僵王博士", "REANIM_BOSS"),
        new(26, "ZOMBIE_PEA_HEAD", "豌豆僵尸", "REANIM_ZOMBIE"),
        new(27, "ZOMBIE_WALLNUT_HEAD", "坚果僵尸", "REANIM_ZOMBIE"),
        new(28, "ZOMBIE_JALAPENO_HEAD", "辣椒僵尸", "REANIM_ZOMBIE"),
        new(29, "ZOMBIE_GATLING_HEAD", "机枪僵尸", "REANIM_ZOMBIE"),
        new(30, "ZOMBIE_SQUASH_HEAD", "窝瓜僵尸", "REANIM_ZOMBIE"),
        new(31, "ZOMBIE_TALLNUT_HEAD", "高坚果僵尸", "REANIM_ZOMBIE"),
        new(32, "ZOMBIE_REDEYE_GARGANTUAR", "红眼伽刚特尔", "REANIM_GARGANTUAR")
    ];

    public static ZombieTemplateDefinition? Find(int id) =>
        id >= FirstZombieId && id <= LastZombieId ? All[id] : null;
}
