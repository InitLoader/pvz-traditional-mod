using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public readonly record struct ActionFrameRange(int Start, int End)
{
    public int Count => Math.Max(1, End - Start + 1);
}

public sealed class ActionViewService
{
    public ActionFrameRange GetRange(AnimationDocument document, ActionDefinition? action)
    {
        if (document.FrameCount <= 0) return new ActionFrameRange(0, 0);
        if (action is null) return new ActionFrameRange(0, document.FrameCount - 1);
        var marker = document.FindTrack(action.Track);
        if (marker is null) return new ActionFrameRange(0, document.FrameCount - 1);

        var start = -1;
        var end = -1;
        for (var index = 0; index < marker.Frames.Count; index++)
        {
            if (marker.ResolveFrame(index).Frame < 0) continue;
            if (start < 0) start = index;
            end = index;
        }
        return start < 0
            ? new ActionFrameRange(0, document.FrameCount - 1)
            : new ActionFrameRange(start, Math.Max(start, end));
    }

    public IReadOnlyList<AnimationTrack> GetTimelineTracks(
        AnimationDocument document, ActionDefinition? action)
    {
        if (action is null) return document.Tracks.ToArray();
        var range = GetRange(document, action);
        var marker = document.FindTrack(action.Track);
        var result = new List<AnimationTrack>();
        if (marker is not null) result.Add(marker);
        result.AddRange(document.Tracks.Where(track =>
            !ReferenceEquals(track, marker) && !track.IsActionTrack && TrackChangesInRange(track, range)));
        return result;
    }

    public bool TrackChangesInRange(AnimationTrack track, ActionFrameRange range)
    {
        if (track.Frames.Count == 0) return false;
        var start = Math.Clamp(range.Start, 0, track.Frames.Count - 1);
        var end = Math.Clamp(range.End, start, track.Frames.Count - 1);
        for (var index = start; index <= end; index++)
        {
            if (HasMeaningfulChange(track, index, start)) return true;
        }
        return false;
    }

    public bool HasMeaningfulChange(AnimationTrack track, int frameIndex, int rangeStart = 0)
    {
        if (track.Frames.Count == 0) return false;
        frameIndex = Math.Clamp(frameIndex, 0, track.Frames.Count - 1);
        if (track.IsActionTrack)
        {
            if (frameIndex == rangeStart) return true;
            return !NearlyEqual(track.ResolveFrame(frameIndex).Frame, track.ResolveFrame(frameIndex - 1).Frame);
        }
        if (frameIndex == 0) return track.Frames[0].HasKey;
        var current = track.ResolveFrame(frameIndex);
        var previous = track.ResolveFrame(frameIndex - 1);
        return !NearlyEqual(current.X, previous.X) || !NearlyEqual(current.Y, previous.Y) ||
               !NearlyEqual(current.SkewX, previous.SkewX) || !NearlyEqual(current.SkewY, previous.SkewY) ||
               !NearlyEqual(current.ScaleX, previous.ScaleX) || !NearlyEqual(current.ScaleY, previous.ScaleY) ||
               !NearlyEqual(current.Frame, previous.Frame) || !NearlyEqual(current.Alpha, previous.Alpha) ||
               !string.Equals(current.Image, previous.Image, StringComparison.Ordinal) ||
               !string.Equals(current.Font, previous.Font, StringComparison.Ordinal) ||
               !string.Equals(current.Text, previous.Text, StringComparison.Ordinal);
    }

    private static bool NearlyEqual(float left, float right) => Math.Abs(left - right) < 0.0001f;
}
