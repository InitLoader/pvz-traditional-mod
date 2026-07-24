using System.Text.RegularExpressions;
using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public enum EntityClassificationConfidence
{
    Unknown,
    Low,
    Medium,
    High
}

public sealed record AnimationEntityClassification(
    EntityKind Kind,
    EntityClassificationConfidence Confidence,
    int Score,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> ActionIds)
{
    public bool IsHighConfidence => Confidence == EntityClassificationConfidence.High;
    public bool CanAutoSelect => Confidence is EntityClassificationConfidence.Medium or EntityClassificationConfidence.High;

    public string Summary
    {
        get
        {
            var confidence = Confidence switch
            {
                EntityClassificationConfidence.High => "高",
                EntityClassificationConfidence.Medium => "中",
                EntityClassificationConfidence.Low => "低",
                _ => "未知"
            };
            return $"{AnimationEntityClassifier.GetKindName(Kind)}（{confidence}置信度）";
        }
    }
}

/// <summary>
/// 根据整套动画的来源目录、图片命名空间占比和主体结构进行分类。
/// anim_* 只用于动作发现，不参与实体类型打分，避免 bite/walk/idle 等通用动作造成误判。
/// </summary>
public static class AnimationEntityClassifier
{
    private const string ImagePrefix = "IMAGE_REANIM_";

    private static readonly Regex IdentifierBoundary = new(
        "(?<=[a-z0-9])(?=[A-Z])|[^A-Za-z0-9]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> PlantImageOwners = PlantTemplateCatalog.All
        .SelectMany(definition => new[] { definition.CompiledStem, definition.InternalName })
        .Select(NormalizeIdentifier)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> UiVocabulary = new(StringComparer.OrdinalIgnoreCase)
    {
        "UI", "HUD", "MENU", "SCREEN", "SELECTOR", "BUTTON", "PANEL", "DIALOG",
        "LOADBAR", "PROGRESS", "METER", "SLOT", "TEXT", "LABEL", "CURSOR"
    };

    public static AnimationEntityClassification Classify(EditorProject project)
    {
        var scores = Enum.GetValues<EntityKind>().ToDictionary(kind => kind, _ => 0);
        var evidence = Enum.GetValues<EntityKind>().ToDictionary(kind => kind, _ => new List<string>());
        var sourceStem = GetSourceStem(project.SourceAnimationPath);
        var sourceTokens = Tokenize(sourceStem).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (PlantTemplateCatalog.All.Any(definition =>
                string.Equals(definition.CompiledStem, sourceStem, StringComparison.OrdinalIgnoreCase)))
            AddScore(EntityKind.Plant, 100, "来源文件匹配全局植物模板目录", scores, evidence);

        if (sourceTokens.Contains("ZOMBIE") || sourceTokens.Contains("ZOMBIES"))
            AddScore(EntityKind.Zombie, 35, "来源名称使用 Zombie 命名空间", scores, evidence);

        var allIdentifiers = new List<string>();
        allIdentifiers.Add(sourceStem);
        allIdentifiers.AddRange(project.Animation.Tracks.Select(track => track.Name));

        var visualTrackCount = 0;
        var plantOwnedTracks = 0;
        var zombieOwnedTracks = 0;
        foreach (var track in project.Animation.Tracks)
        {
            var owners = track.Frames
                .Select(frame => ExtractImageOwner(frame.Image))
                .Where(owner => owner is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (owners.Length == 0) continue;

            visualTrackCount++;
            if (owners.Any(owner => PlantImageOwners.Contains(NormalizeIdentifier(owner))))
                plantOwnedTracks++;
            if (owners.Any(IsZombieOwner))
                zombieOwnedTracks++;
            allIdentifiers.AddRange(track.Frames
                .Select(frame => frame.Image)
                .Where(image => !string.IsNullOrWhiteSpace(image))
                .Cast<string>());
        }

        if (visualTrackCount >= 3)
        {
            var plantRatio = plantOwnedTracks / (double)visualTrackCount;
            var zombieRatio = zombieOwnedTracks / (double)visualTrackCount;
            if (plantOwnedTracks >= 3 && plantRatio >= 0.55)
                AddScore(EntityKind.Plant, 85,
                    $"{plantOwnedTracks}/{visualTrackCount} 条可见轨道使用植物图片命名空间",
                    scores, evidence);
            if (zombieOwnedTracks >= 3 && zombieRatio >= 0.55)
                AddScore(EntityKind.Zombie, 85,
                    $"{zombieOwnedTracks}/{visualTrackCount} 条可见轨道使用僵尸图片命名空间",
                    scores, evidence);
        }

        var zombieIdentifiers = allIdentifiers.Where(ContainsZombieNamespace).ToArray();
        var hasZombieCore = zombieIdentifiers.Any(identifier =>
            ContainsSemanticPart(identifier, "BODY") ||
            ContainsSemanticPart(identifier, "HEAD") ||
            ContainsSemanticPart(identifier, "NECK"));
        var hasZombieLimb = zombieIdentifiers.Any(identifier =>
            ContainsSemanticPart(identifier, "ARM") ||
            ContainsSemanticPart(identifier, "LEG") ||
            ContainsSemanticPart(identifier, "HAND") ||
            ContainsSemanticPart(identifier, "FOOT"));
        if (hasZombieCore && hasZombieLimb)
            AddScore(EntityKind.Zombie, 90, "检测到同一僵尸命名空间中的核心身体与肢体结构", scores, evidence);
        else if (zombieIdentifiers.Length > 0)
            AddScore(EntityKind.Zombie, 10, "仅检测到局部僵尸素材，不足以证明是僵尸主体", scores, evidence);

        var uiCues = allIdentifiers
            .SelectMany(Tokenize)
            .Where(token => UiVocabulary.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (uiCues.Length >= 2)
            AddScore(EntityKind.Ui, 85, $"检测到多个界面语义：{string.Join("、", uiCues.Take(4))}", scores, evidence);
        else if (uiCues.Length == 1)
            AddScore(EntityKind.Ui, 25, $"检测到界面语义：{uiCues[0]}", scores, evidence);

        var ranked = scores.OrderByDescending(pair => pair.Value).ToArray();
        var best = ranked[0];
        var second = ranked[1];
        var actionIds = project.Animation.Tracks
            .Where(track => track.Name.StartsWith("anim_", StringComparison.OrdinalIgnoreCase))
            .Select(track => track.Name[5..])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (best.Value == 0)
            return new AnimationEntityClassification(
                EntityKind.Other,
                string.IsNullOrWhiteSpace(sourceStem)
                    ? EntityClassificationConfidence.Unknown
                    : EntityClassificationConfidence.Medium,
                0,
                ["未发现植物、僵尸或 UI 的可靠主体证据，按其他动画处理"],
                actionIds);

        var confidence = best.Value switch
        {
            >= 75 => EntityClassificationConfidence.High,
            >= 40 => EntityClassificationConfidence.Medium,
            _ => EntityClassificationConfidence.Low
        };
        if (best.Value - second.Value < 20)
        {
            confidence = EntityClassificationConfidence.Low;
            return new AnimationEntityClassification(
                EntityKind.Other,
                confidence,
                best.Value,
                [.. evidence[best.Key], "存在接近的混合类型证据，保留为其他动画并交由用户确认"],
                actionIds);
        }

        return new AnimationEntityClassification(best.Key, confidence, best.Value, evidence[best.Key], actionIds);
    }

    public static string GetKindName(EntityKind kind) => kind switch
    {
        EntityKind.Plant => "植物",
        EntityKind.Zombie => "僵尸",
        EntityKind.Ui => "UI",
        _ => "其他"
    };

    private static void AddScore(
        EntityKind kind,
        int score,
        string reason,
        IDictionary<EntityKind, int> scores,
        IDictionary<EntityKind, List<string>> evidence)
    {
        scores[kind] += score;
        evidence[kind].Add(reason);
    }

    private static bool IsZombieOwner(string owner) =>
        NormalizeIdentifier(owner).StartsWith("ZOMBIE", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsZombieNamespace(string identifier) =>
        Tokenize(identifier).Any(token =>
            token.Equals("ZOMBIE", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("ZOMBIES", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsSemanticPart(string identifier, string part)
    {
        var normalized = NormalizeIdentifier(identifier);
        return normalized.Contains(part, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractImageOwner(string? image)
    {
        if (string.IsNullOrWhiteSpace(image) ||
            !image.StartsWith(ImagePrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var remainder = image[ImagePrefix.Length..];
        var separator = remainder.IndexOf('_');
        return separator < 0 ? remainder : remainder[..separator];
    }

    private static string GetSourceStem(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var fileName = Path.GetFileName(path);
        var reanimIndex = fileName.IndexOf(".reanim", StringComparison.OrdinalIgnoreCase);
        return reanimIndex >= 0 ? fileName[..reanimIndex] : Path.GetFileNameWithoutExtension(fileName);
    }

    private static IEnumerable<string> Tokenize(string identifier) =>
        IdentifierBoundary.Split(identifier ?? string.Empty)
            .Select(token => token.Trim())
            .Where(token => token.Length > 0)
            .Select(token => token.ToUpperInvariant());

    private static string NormalizeIdentifier(string identifier) =>
        string.Concat((identifier ?? string.Empty).Where(char.IsLetterOrDigit)).ToUpperInvariant();
}
