using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public readonly record struct GroundMotionPoint(int Frame, float X, float Y);

public sealed record GroundMotionSample(
    AnimationTrack Track,
    ActionFrameRange Range,
    int Frame,
    int NextFrame,
    float X,
    float Y,
    float DeltaX,
    float DeltaY,
    double AnimationRate,
    double PixelsPerUpdate,
    double PixelsPerSecond,
    double AveragePixelsPerUpdate,
    double AveragePixelsPerSecond,
    float TotalDeltaX,
    float TotalDeltaY);

/// <summary>
/// Mirrors PvZ Reanimation::GetTrackVelocity for the special _ground track.
/// The original game advances at 100 updates per second, so the value returned
/// to game logic is delta-X * 0.01 * mAnimRate. Multiplying that result by 100
/// gives the more familiar pixels-per-second value shown by the editor.
/// </summary>
public sealed class GroundMotionService
{
    public const double SecondsPerGameUpdate = 0.01;
    private readonly ActionViewService _actionView = new();

    public GroundMotionSample? Sample(
        AnimationDocument document,
        ActionDefinition? action,
        int currentFrame)
    {
        var track = document.FindTrack("_ground");
        if (track is null || track.Frames.Count == 0) return null;

        var range = _actionView.GetRange(document, action);
        var start = Math.Clamp(range.Start, 0, track.Frames.Count - 1);
        var end = Math.Clamp(range.End, start, track.Frames.Count - 1);
        var frame = Math.Clamp(currentFrame, start, end);
        var nextFrame = Math.Min(frame + 1, end);
        var current = track.ResolveFrame(frame);
        var next = track.ResolveFrame(nextFrame);
        var first = track.ResolveFrame(start);
        var last = track.ResolveFrame(end);
        var rate = Math.Clamp(action?.Rate ?? document.Fps, 0.1, 120.0);
        var deltaX = next.X - current.X;
        var deltaY = next.Y - current.Y;
        var segmentCount = Math.Max(1, end - start);
        var averageDeltaX = (last.X - first.X) / segmentCount;

        return new GroundMotionSample(
            track,
            new ActionFrameRange(start, end),
            frame,
            nextFrame,
            current.X,
            current.Y,
            deltaX,
            deltaY,
            rate,
            deltaX * SecondsPerGameUpdate * rate,
            deltaX * rate,
            averageDeltaX * SecondsPerGameUpdate * rate,
            averageDeltaX * rate,
            last.X - first.X,
            last.Y - first.Y);
    }

    public IReadOnlyList<GroundMotionPoint> GetPath(
        AnimationDocument document,
        ActionDefinition? action,
        int maxPoints = 512)
    {
        var track = document.FindTrack("_ground");
        if (track is null || track.Frames.Count == 0) return [];
        var range = _actionView.GetRange(document, action);
        var start = Math.Clamp(range.Start, 0, track.Frames.Count - 1);
        var end = Math.Clamp(range.End, start, track.Frames.Count - 1);
        var frameCount = end - start + 1;
        var stride = Math.Max(1, (int)Math.Ceiling(frameCount / (double)Math.Max(2, maxPoints)));
        var points = new List<GroundMotionPoint>(Math.Min(frameCount, maxPoints + 1));
        for (var frame = start; frame <= end; frame += stride)
        {
            var resolved = track.ResolveFrame(frame);
            points.Add(new GroundMotionPoint(frame, resolved.X, resolved.Y));
        }
        if (points.Count == 0 || points[^1].Frame != end)
        {
            var resolved = track.ResolveFrame(end);
            points.Add(new GroundMotionPoint(end, resolved.X, resolved.Y));
        }
        return points;
    }
}
