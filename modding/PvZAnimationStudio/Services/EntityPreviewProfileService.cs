using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public sealed record EntityPreviewLayer(int Frame, string ActionTrack);

public sealed record EntityPreviewPlan(
    string DisplayName,
    int BaseFrame,
    IReadOnlyList<EntityPreviewLayer> OverlayLayers);

public sealed class EntityPreviewProfileService
{
    private sealed record Profile(
        string SourceName,
        string DisplayName,
        string BaseActionTrack,
        string[] OverlayActionTracks,
        string[]? HiddenTrackPrefixes = null,
        bool UseMostVisibleFrame = false);

    private static readonly Profile[] Profiles =
    [
        new("GatlingPea", "机枪射手：身体 + 独立头部", "anim_idle",
            ["anim_head_idle"]),
        new("SplitPea", "双向射手：身体 + 前后两个头部", "anim_idle",
            ["anim_splitpea_idle", "anim_head_idle"]),
        new("ThreePeater", "三线射手：身体 + 三个独立头部", "anim_head1",
            ["anim_head_idle1", "anim_head_idle2", "anim_head_idle3"]),
        new("Zombie", "普通僵尸：隐藏路障、铁桶、铁门、旗帜和泳圈等可选装备", "anim_idle", [],
            [
                "anim_cone", "anim_bucket", "anim_screendoor", "Zombie_flaghand",
                "Zombie_duckytube", "anim_tongue", "Zombie_mustache",
                "Zombie_outerarm_screendoor", "Zombie_innerarm_screendoor"
            ]),
        new("Zombie_boss", "僵王博士机器人：完整装配检查帧", "anim_death", [],
            UseMostVisibleFrame: true)
    ];

    private readonly ActionViewService _actionView = new();

    public EntityPreviewPlan? CreatePlan(EditorProject project, int currentFrame, ActionDefinition? selectedAction)
    {
        if (selectedAction is not null) return null;
        var profile = FindProfile(project);
        if (profile is null) return null;
        var baseRange = GetRange(project.Animation, profile.BaseActionTrack);
        if (currentFrame < baseRange.Start || currentFrame > baseRange.End) return null;

        var overlays = profile.OverlayActionTracks
            .Select(track => new EntityPreviewLayer(GetMidpoint(project.Animation, track), track))
            .Where(layer => layer.Frame >= 0 && layer.Frame != currentFrame)
            .ToArray();
        return new EntityPreviewPlan(profile.DisplayName, currentFrame, overlays);
    }

    public int? GetRepresentativeFrame(EditorProject project)
    {
        var profile = FindProfile(project);
        if (profile is null) return null;
        return profile.UseMostVisibleFrame
            ? FindMostVisibleFrame(project.Animation)
            : GetMidpoint(project.Animation, profile.BaseActionTrack);
    }

    public bool IsTrackVisible(
        EditorProject project,
        AnimationTrack track,
        ActionDefinition? selectedAction,
        AnimationTrack? selectedTrack = null)
    {
        // Optional equipment is hidden for a normal entity preview even while
        // editing an action. Selecting the equipment track itself temporarily
        // reveals it so it can still be authored.
        if (ReferenceEquals(track, selectedTrack)) return true;
        var hiddenPrefixes = FindProfile(project)?.HiddenTrackPrefixes;
        return hiddenPrefixes is null || hiddenPrefixes.All(prefix =>
            !track.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private ActionFrameRange GetRange(AnimationDocument document, string trackName) =>
        _actionView.GetRange(document, new ActionDefinition { Id = trackName[5..], Track = trackName });

    private int GetMidpoint(AnimationDocument document, string trackName)
    {
        if (document.FindTrack(trackName) is null) return -1;
        var range = GetRange(document, trackName);
        return range.Start + (range.Count - 1) / 2;
    }

    private static Profile? FindProfile(EditorProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.SourceAnimationPath))
        {
            var fileName = Path.GetFileName(project.SourceAnimationPath);
            var reanimIndex = fileName.IndexOf(".reanim", StringComparison.OrdinalIgnoreCase);
            var sourceName = reanimIndex >= 0 ? fileName[..reanimIndex] : Path.GetFileNameWithoutExtension(fileName);
            var named = Profiles.FirstOrDefault(profile =>
                string.Equals(profile.SourceName, sourceName, StringComparison.OrdinalIgnoreCase));
            if (named is not null) return named;
        }
        if (project.Animation.FindTrack("Zombie_body") is not null &&
            project.Animation.FindTrack("anim_cone") is not null &&
            project.Animation.FindTrack("anim_bucket") is not null)
            return Profiles.First(profile => profile.SourceName == "Zombie");
        return null;
    }

    private static int FindMostVisibleFrame(AnimationDocument document)
    {
        if (document.FrameCount <= 0) return 0;
        var visiblePartsByFrame = new int[document.FrameCount];
        foreach (var track in document.Tracks.Where(track => !track.IsActionTrack && track.HasRenderableContent))
        {
            var imageFrame = 0f;
            var alpha = 1f;
            string? image = null;
            for (var frameIndex = 0; frameIndex < Math.Min(document.FrameCount, track.Frames.Count); frameIndex++)
            {
                var frame = track.Frames[frameIndex];
                if (frame.Frame.HasValue) imageFrame = frame.Frame.Value;
                if (frame.Alpha.HasValue) alpha = frame.Alpha.Value;
                if (frame.Image is not null) image = frame.Image.Length == 0 ? null : frame.Image;
                if (imageFrame >= 0 && alpha > 0 && !string.IsNullOrWhiteSpace(image))
                    visiblePartsByFrame[frameIndex]++;
            }
        }
        var maximum = visiblePartsByFrame.Max();
        var candidates = Enumerable.Range(0, visiblePartsByFrame.Length)
            .Where(index => visiblePartsByFrame[index] == maximum).ToArray();
        return candidates.Length == 0 ? 0 : candidates[candidates.Length / 2];
    }
}
