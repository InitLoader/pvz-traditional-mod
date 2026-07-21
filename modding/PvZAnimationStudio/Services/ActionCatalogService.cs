using System.Collections.ObjectModel;
using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public sealed record ActionTemplate(string Id, string ChineseName, string Category, AnimationLoopMode Loop);

public sealed class ActionCatalogService
{
    private static readonly Dictionary<string, string> ChineseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["idle"] = "待机",
        ["idle2"] = "待机变化",
        ["loop"] = "循环",
        ["walk"] = "行走",
        ["attack"] = "攻击",
        ["shoot"] = "发射",
        ["blink"] = "眨眼",
        ["die"] = "死亡",
        ["death"] = "死亡",
        ["hurt"] = "受伤",
        ["eat"] = "啃食",
        ["chew"] = "咀嚼",
        ["swallow"] = "吞咽",
        ["sleep"] = "睡眠",
        ["wake"] = "苏醒",
        ["rise"] = "起身",
        ["jump"] = "跳跃",
        ["land"] = "落地",
        ["swim"] = "游泳",
        ["dive"] = "潜水",
        ["climb"] = "攀爬",
        ["dance"] = "跳舞",
        ["summon"] = "召唤",
        ["throw"] = "投掷",
        ["smash"] = "砸击",
        ["blow"] = "吹风",
        ["explode"] = "爆炸",
        ["deploy"] = "展开",
        ["charge"] = "蓄力",
        ["produce"] = "生产",
        ["head"] = "掉头",
        ["arm"] = "掉手",
        ["stun"] = "眩晕",
        ["enter"] = "入场",
        ["exit"] = "退场"
    };

    public IReadOnlyList<ActionTemplate> Templates { get; } =
    [
        new("idle", "待机", "通用", AnimationLoopMode.Loop),
        new("blink", "眨眼", "通用", AnimationLoopMode.Once),
        new("attack", "攻击", "通用", AnimationLoopMode.Once),
        new("hurt", "受伤", "通用", AnimationLoopMode.Once),
        new("die", "死亡", "通用", AnimationLoopMode.OnceHold),
        new("shoot", "发射", "植物", AnimationLoopMode.Once),
        new("produce", "生产", "植物", AnimationLoopMode.Once),
        new("sleep", "睡眠", "植物", AnimationLoopMode.Loop),
        new("wake", "苏醒", "植物", AnimationLoopMode.Once),
        new("explode", "爆炸", "植物", AnimationLoopMode.OnceHold),
        new("blow", "吹风", "植物", AnimationLoopMode.Once),
        new("walk", "行走", "僵尸", AnimationLoopMode.Loop),
        new("eat", "啃食", "僵尸", AnimationLoopMode.Loop),
        new("rise", "起身", "僵尸", AnimationLoopMode.Once),
        new("jump", "跳跃", "僵尸", AnimationLoopMode.Once),
        new("swim", "游泳", "僵尸", AnimationLoopMode.Loop),
        new("climb", "攀爬", "僵尸", AnimationLoopMode.Once),
        new("dance", "跳舞", "特殊动作", AnimationLoopMode.Loop),
        new("summon", "召唤", "特殊动作", AnimationLoopMode.Once),
        new("throw", "投掷", "特殊动作", AnimationLoopMode.Once),
        new("smash", "砸击", "Boss", AnimationLoopMode.Once),
        new("charge", "蓄力", "Boss", AnimationLoopMode.Once)
    ];

    public ObservableCollection<ActionDefinition> InferActions(AnimationDocument document, EntityKind kind)
    {
        var actions = new ObservableCollection<ActionDefinition>();
        foreach (var track in document.Tracks.Where(track => track.IsActionTrack))
        {
            var id = track.Name[5..];
            var normalized = NormalizeActionId(id);
            actions.Add(new ActionDefinition
            {
                Id = id,
                Track = track.Name,
                DisplayName = GetChineseName(normalized),
                Category = GetCategory(normalized, kind),
                Loop = InferLoop(normalized),
                Rate = document.Fps
            });
        }
        return actions;
    }

    public ActionDefinition CreateFromTemplate(ActionTemplate template, AnimationDocument document) => new()
    {
        Id = template.Id,
        DisplayName = template.ChineseName,
        Category = template.Category,
        Track = $"anim_{template.Id}",
        Loop = template.Loop,
        Rate = document.Fps
    };

    private static string NormalizeActionId(string id)
    {
        var lowered = id.ToLowerInvariant();
        foreach (var known in ChineseNames.Keys.OrderByDescending(value => value.Length))
        {
            if (lowered.Contains(known, StringComparison.Ordinal)) return known;
        }
        return lowered;
    }

    private static string GetChineseName(string id) =>
        ChineseNames.TryGetValue(id, out var value) ? value : $"自定义动作：{id}";

    private static string GetCategory(string id, EntityKind kind)
    {
        if (id is "dance" or "summon" or "throw" or "smash" or "charge") return "特殊动作";
        if (id is "blink" or "idle" or "idle2" or "hurt" or "die" or "death") return "通用";
        return kind switch
        {
            EntityKind.Plant => "植物",
            EntityKind.Zombie => "僵尸",
            EntityKind.Ui => "UI",
            _ => "其他"
        };
    }

    private static AnimationLoopMode InferLoop(string id) => id switch
    {
        "idle" or "idle2" or "loop" or "walk" or "eat" or "chew" or "sleep" or "swim" or "dance" => AnimationLoopMode.Loop,
        "die" or "death" or "explode" => AnimationLoopMode.OnceHold,
        _ => AnimationLoopMode.Once
    };
}
