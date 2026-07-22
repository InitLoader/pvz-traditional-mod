using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public readonly record struct CurvePoint(float Frame, float Value);
public readonly record struct CurveHandlePair(CurvePoint Left, CurvePoint Right);

public sealed class AnimationCurveService
{
    private static readonly CurveChannel[] Channels = Enum.GetValues<CurveChannel>();
    private static readonly CurveChannel[] AutoInterpolationChannels =
    [
        CurveChannel.X, CurveChannel.Y, CurveChannel.SkewX, CurveChannel.SkewY,
        CurveChannel.ScaleX, CurveChannel.ScaleY, CurveChannel.Alpha
    ];

    public IReadOnlyList<CurveChannel> GetAvailableChannels(EditorProject project, AnimationTrack track) =>
        Channels.Where(channel =>
                project.Curves.Any(curve => curve.TrackId == track.EditorId && curve.Channel == channel) ||
                track.Frames.Any(frame => GetExplicitValue(frame, channel).HasValue))
            .ToArray();

    public AnimationCurveDefinition? FindCurve(EditorProject project, AnimationTrack track, CurveChannel channel) =>
        project.Curves.FirstOrDefault(curve => curve.TrackId == track.EditorId && curve.Channel == channel);

    public AnimationCurveDefinition EnsureCurve(EditorProject project, AnimationTrack track, CurveChannel channel)
    {
        var existing = FindCurve(project, track, channel);
        if (existing is not null)
        {
            if (channel == CurveChannel.Frame && track.IsActionTrack)
                existing.Interpolation = CurveInterpolationMode.Constant;
            return existing;
        }
        var curve = CreateDerivedCurve(track, channel);
        project.Curves.Add(curve);
        return curve;
    }

    public AnimationCurveDefinition GetCurveForDisplay(EditorProject project, AnimationTrack track, CurveChannel channel) =>
        FindCurve(project, track, channel) ?? CreateDerivedCurve(track, channel);

    private static AnimationCurveDefinition CreateDerivedCurve(AnimationTrack track, CurveChannel channel)
    {
        var curve = new AnimationCurveDefinition
        {
            TrackId = track.EditorId,
            Channel = channel,
            // Reanimation f is a discrete image/visibility selector. In
            // particular, anim_* marker tracks use 0 / -1 to delimit an
            // action. Smooth interpolation would hide the action immediately.
            Interpolation = channel == CurveChannel.Frame
                ? CurveInterpolationMode.Constant
                : CurveInterpolationMode.Bezier
        };
        for (var frame = 0; frame < track.Frames.Count; frame++)
        {
            if (GetExplicitValue(track.Frames[frame], channel).HasValue)
                curve.Keys.Add(new CurveKeyDefinition
                {
                    Frame = frame,
                    Value = GetExplicitValue(track.Frames[frame], channel)!.Value
                });
        }
        return curve;
    }

    public CurveKeyDefinition EnsureKey(EditorProject project, AnimationTrack track, CurveChannel channel, int frame)
    {
        var curve = EnsureCurve(project, track, channel);
        var key = curve.Keys.FirstOrDefault(item => item.Frame == frame);
        if (key is not null) return key;
        key = new CurveKeyDefinition { Frame = frame, Value = GetValue(track, channel, frame) };
        curve.Keys.Add(key);
        SortKeys(curve);
        return key;
    }

    public IReadOnlyList<int> GetKeyFrames(EditorProject project, AnimationTrack track, CurveChannel channel)
    {
        var curve = FindCurve(project, track, channel);
        return curve is null
            ? track.Frames.Select((frame, index) => (frame, index))
                .Where(item => GetExplicitValue(item.frame, channel).HasValue)
                .Select(item => item.index).ToArray()
            : curve.Keys.Select(key => key.Frame).OrderBy(frame => frame).ToArray();
    }

    public float GetValue(AnimationTrack track, CurveChannel channel, int frame) =>
        channel switch
        {
            CurveChannel.X => track.ResolveFrame(frame).X,
            CurveChannel.Y => track.ResolveFrame(frame).Y,
            CurveChannel.SkewX => track.ResolveFrame(frame).SkewX,
            CurveChannel.SkewY => track.ResolveFrame(frame).SkewY,
            CurveChannel.ScaleX => track.ResolveFrame(frame).ScaleX,
            CurveChannel.ScaleY => track.ResolveFrame(frame).ScaleY,
            CurveChannel.Frame => track.ResolveFrame(frame).Frame,
            CurveChannel.Alpha => track.ResolveFrame(frame).Alpha,
            _ => 0
        };

    public CurveHandlePair GetHandles(EditorProject project, AnimationTrack track,
        AnimationCurveDefinition curve, CurveKeyDefinition key)
    {
        var keys = curve.Keys.OrderBy(item => item.Frame).ToArray();
        var index = Array.IndexOf(keys, key);
        var value = key.Value;
        if (key.HandleMode is CurveHandleMode.Free or CurveHandleMode.Aligned)
        {
            return new CurveHandlePair(
                new CurvePoint(key.Frame + key.LeftFrameOffset, value + key.LeftValueOffset),
                new CurvePoint(key.Frame + key.RightFrameOffset, value + key.RightValueOffset));
        }

        var previous = index > 0 ? keys[index - 1] : null;
        var next = index + 1 < keys.Length ? keys[index + 1] : null;
        var leftGap = previous is null ? 1f : Math.Max(0.15f, key.Frame - previous.Frame);
        var rightGap = next is null ? 1f : Math.Max(0.15f, next.Frame - key.Frame);
        var leftFrameOffset = -leftGap / 3f;
        var rightFrameOffset = rightGap / 3f;
        float leftValueOffset;
        float rightValueOffset;
        if (key.HandleMode == CurveHandleMode.Vector)
        {
            leftValueOffset = previous is null
                ? 0
                : (previous.Value - value) / 3f;
            rightValueOffset = next is null
                ? 0
                : (next.Value - value) / 3f;
        }
        else
        {
            var slope = CalculateAutoSlope(track, curve.Channel, previous, key, next);
            leftValueOffset = slope * leftFrameOffset;
            rightValueOffset = slope * rightFrameOffset;
        }
        return new CurveHandlePair(
            new CurvePoint(key.Frame + leftFrameOffset, value + leftValueOffset),
            new CurvePoint(key.Frame + rightFrameOffset, value + rightValueOffset));
    }

    public void SetHandle(EditorProject project, AnimationTrack track, CurveChannel channel, int keyFrame,
        bool left, float handleFrame, float handleValue)
    {
        var curve = EnsureCurve(project, track, channel);
        var key = EnsureKey(project, track, channel, keyFrame);
        var keys = curve.Keys.OrderBy(item => item.Frame).ToArray();
        var index = Array.IndexOf(keys, key);
        var value = key.Value;
        if (left)
        {
            var minimum = index > 0 ? keys[index - 1].Frame + 0.05f : keyFrame - Math.Max(1, keyFrame);
            handleFrame = Math.Clamp(handleFrame, minimum, keyFrame - 0.05f);
        }
        else
        {
            var maximum = index + 1 < keys.Length ? keys[index + 1].Frame - 0.05f : keyFrame + 10;
            handleFrame = Math.Clamp(handleFrame, keyFrame + 0.05f, maximum);
        }

        if (key.HandleMode is CurveHandleMode.Auto or CurveHandleMode.Vector)
        {
            var current = GetHandles(project, track, curve, key);
            key.LeftFrameOffset = current.Left.Frame - keyFrame;
            key.LeftValueOffset = current.Left.Value - value;
            key.RightFrameOffset = current.Right.Frame - keyFrame;
            key.RightValueOffset = current.Right.Value - value;
            key.HandleMode = CurveHandleMode.Free;
        }

        var frameOffset = handleFrame - keyFrame;
        var valueOffset = handleValue - value;
        if (left)
        {
            key.LeftFrameOffset = frameOffset;
            key.LeftValueOffset = valueOffset;
        }
        else
        {
            key.RightFrameOffset = frameOffset;
            key.RightValueOffset = valueOffset;
        }

        if (key.HandleMode == CurveHandleMode.Aligned)
            AlignOppositeHandle(key, left);
        BakeCurve(project, track, curve);
    }

    public void SetHandleMode(EditorProject project, AnimationTrack track, CurveChannel channel, int keyFrame,
        CurveHandleMode mode)
    {
        var curve = EnsureCurve(project, track, channel);
        var key = EnsureKey(project, track, channel, keyFrame);
        if (mode is CurveHandleMode.Free or CurveHandleMode.Aligned)
        {
            var value = key.Value;
            var handles = GetHandles(project, track, curve, key);
            key.LeftFrameOffset = handles.Left.Frame - keyFrame;
            key.LeftValueOffset = handles.Left.Value - value;
            key.RightFrameOffset = handles.Right.Frame - keyFrame;
            key.RightValueOffset = handles.Right.Value - value;
        }
        key.HandleMode = mode;
        if (mode == CurveHandleMode.Aligned) AlignOppositeHandle(key, true);
        BakeCurve(project, track, curve);
    }

    public void SetInterpolation(EditorProject project, AnimationTrack track, CurveChannel channel,
        CurveInterpolationMode mode)
    {
        var curve = EnsureCurve(project, track, channel);
        curve.Interpolation = channel == CurveChannel.Frame && track.IsActionTrack
            ? CurveInterpolationMode.Constant
            : mode;
        BakeCurve(project, track, curve);
    }

    public void MoveKey(EditorProject project, AnimationTrack track, CurveChannel channel,
        int sourceFrame, int targetFrame, float value)
    {
        var curve = EnsureCurve(project, track, channel);
        var key = EnsureKey(project, track, channel, sourceFrame);
        targetFrame = Math.Clamp(targetFrame, 0, track.Frames.Count - 1);
        if (sourceFrame != targetFrame)
        {
            var occupied = curve.Keys.FirstOrDefault(item => item.Frame == targetFrame && !ReferenceEquals(item, key));
            // Blender-style reordering: crossing an occupied frame swaps the two
            // keys instead of destructively merging the key being crossed.
            if (occupied is not null) occupied.Frame = sourceFrame;
            SetExplicitValue(track.Frames[sourceFrame], channel, null);
            key.Frame = targetFrame;
            SortKeys(curve);
        }
        key.Value = value;
        BakeCurve(project, track, curve);
    }

    public void DeleteKey(EditorProject project, AnimationTrack track, CurveChannel channel, int frame)
    {
        var curve = FindCurve(project, track, channel);
        var key = curve?.Keys.FirstOrDefault(item => item.Frame == frame);
        if (curve is not null && key is not null) curve.Keys.Remove(key);
        SetExplicitValue(track.Frames[Math.Clamp(frame, 0, track.Frames.Count - 1)], channel, null);
        if (curve is not null) BakeCurve(project, track, curve);
    }

    public void MoveFrameKeys(EditorProject project, AnimationTrack track, int sourceFrame, int targetFrame)
    {
        foreach (var curve in project.Curves.Where(item => item.TrackId == track.EditorId))
        {
            var key = curve.Keys.FirstOrDefault(item => item.Frame == sourceFrame);
            if (key is null) continue;
            var occupied = curve.Keys.FirstOrDefault(item => item.Frame == targetFrame && !ReferenceEquals(item, key));
            if (occupied is not null) occupied.Frame = sourceFrame;
            key.Frame = targetFrame;
            SortKeys(curve);
            BakeCurve(project, track, curve);
        }
    }

    public void DeleteFrameKeys(EditorProject project, AnimationTrack track, int frame)
    {
        foreach (var curve in project.Curves.Where(item => item.TrackId == track.EditorId))
        {
            var key = curve.Keys.FirstOrDefault(item => item.Frame == frame);
            if (key is not null) curve.Keys.Remove(key);
            BakeCurve(project, track, curve);
        }
    }

    public void ShiftForInsertedFrame(EditorProject project, int frame)
    {
        foreach (var key in project.Curves.SelectMany(curve => curve.Keys).Where(key => key.Frame >= frame)) key.Frame++;
    }

    public void CaptureExplicitMotionCurves(EditorProject project)
    {
        foreach (var track in project.Animation.Tracks)
            CaptureExplicitMotionCurves(project, track);
    }

    public void CaptureExplicitMotionCurves(EditorProject project, AnimationTrack track)
    {
        if (track.IsActionTrack) return;
        foreach (var channel in AutoInterpolationChannels)
        {
            if (FindCurve(project, track, channel) is not null) continue;
            if (track.Frames.Any(frame => GetExplicitValue(frame, channel).HasValue))
                EnsureCurve(project, track, channel);
        }
    }

    public void BakeAllCurves(EditorProject project)
    {
        foreach (var curve in project.Curves.ToArray())
        {
            var track = project.Animation.Tracks.FirstOrDefault(item => item.EditorId == curve.TrackId);
            if (track is not null) BakeCurve(project, track, curve);
        }
    }

    public void NormalizeActionMarkerFrames(EditorProject project)
    {
        foreach (var track in project.Animation.Tracks.Where(track => track.IsActionTrack))
        {
            var curve = FindCurve(project, track, CurveChannel.Frame);
            if (curve is not null)
            {
                curve.Interpolation = CurveInterpolationMode.Constant;
                BakeCurve(project, track, curve);
                continue;
            }

            // Repair compiled/Raw files exported by older editor builds where
            // a 0 -> -1 marker transition was baked as fractional negatives.
            // Those values are not meaningful image frames; leaving them in
            // place makes the action range end before the motion tween does.
            foreach (var frame in track.Frames)
            {
                if (frame.Frame is > -1f and < 0f) frame.Frame = null;
            }
        }
    }

    public void ShiftForDeletedFrame(EditorProject project, int frame)
    {
        foreach (var curve in project.Curves)
        {
            foreach (var key in curve.Keys.Where(key => key.Frame == frame).ToArray()) curve.Keys.Remove(key);
            foreach (var key in curve.Keys.Where(key => key.Frame > frame)) key.Frame--;
        }
    }

    public bool HasTimelineKey(EditorProject project, AnimationTrack track, int frame)
    {
        if (frame < 0 || frame >= track.Frames.Count) return false;
        var explicitFrame = track.Frames[frame];
        if (explicitFrame.Image is not null || explicitFrame.Font is not null || explicitFrame.Text is not null) return true;
        var curves = project.Curves.Where(curve => curve.TrackId == track.EditorId).ToArray();
        if (curves.Any(curve => curve.Keys.Any(key => key.Frame == frame))) return true;
        return Channels.Any(channel => curves.All(curve => curve.Channel != channel) &&
                                       GetExplicitValue(explicitFrame, channel).HasValue);
    }

    public void BakeCurve(EditorProject project, AnimationTrack track, AnimationCurveDefinition curve)
    {
        if (curve.Channel == CurveChannel.Frame && track.IsActionTrack)
            curve.Interpolation = CurveInterpolationMode.Constant;
        var keys = curve.Keys.OrderBy(key => key.Frame).ToArray();
        foreach (var frame in track.Frames) SetExplicitValue(frame, curve.Channel, null);
        if (keys.Length == 0) return;
        for (var frame = keys[0].Frame; frame <= keys[^1].Frame; frame++)
            SetExplicitValue(track.Frames[frame], curve.Channel, Evaluate(project, track, curve, frame));
    }

    public float Evaluate(EditorProject project, AnimationTrack track, AnimationCurveDefinition curve, float frame)
    {
        var keys = curve.Keys.OrderBy(key => key.Frame).ToArray();
        if (keys.Length == 0) return GetValue(track, curve.Channel, (int)Math.Clamp(frame, 0, track.Frames.Count - 1));
        if (frame <= keys[0].Frame) return keys[0].Value;
        if (frame >= keys[^1].Frame) return keys[^1].Value;
        var rightIndex = Array.FindIndex(keys, key => key.Frame >= frame);
        if (rightIndex <= 0) return GetValue(track, curve.Channel, keys[0].Frame);
        var left = keys[rightIndex - 1];
        var right = keys[rightIndex];
        var leftValue = left.Value;
        var rightValue = right.Value;
        if (curve.Interpolation == CurveInterpolationMode.Constant) return leftValue;
        var amount = (frame - left.Frame) / Math.Max(0.0001f, right.Frame - left.Frame);
        if (curve.Interpolation == CurveInterpolationMode.Linear)
            return Lerp(leftValue, rightValue, amount);
        var leftHandles = GetHandles(project, track, curve, left);
        var rightHandles = GetHandles(project, track, curve, right);
        var t = SolveBezierTime(frame, left.Frame, leftHandles.Right.Frame, rightHandles.Left.Frame, right.Frame);
        return Cubic(leftValue, leftHandles.Right.Value, rightHandles.Left.Value, rightValue, t);
    }

    public static float? GetExplicitValue(AnimationFrame frame, CurveChannel channel) => channel switch
    {
        CurveChannel.X => frame.X,
        CurveChannel.Y => frame.Y,
        CurveChannel.SkewX => frame.SkewX,
        CurveChannel.SkewY => frame.SkewY,
        CurveChannel.ScaleX => frame.ScaleX,
        CurveChannel.ScaleY => frame.ScaleY,
        CurveChannel.Frame => frame.Frame,
        CurveChannel.Alpha => frame.Alpha,
        _ => null
    };

    public static void SetExplicitValue(AnimationFrame frame, CurveChannel channel, float? value)
    {
        switch (channel)
        {
            case CurveChannel.X: frame.X = value; break;
            case CurveChannel.Y: frame.Y = value; break;
            case CurveChannel.SkewX: frame.SkewX = value; break;
            case CurveChannel.SkewY: frame.SkewY = value; break;
            case CurveChannel.ScaleX: frame.ScaleX = value; break;
            case CurveChannel.ScaleY: frame.ScaleY = value; break;
            case CurveChannel.Frame: frame.Frame = value; break;
            case CurveChannel.Alpha: frame.Alpha = value; break;
        }
    }

    private static float CalculateAutoSlope(AnimationTrack track, CurveChannel channel,
        CurveKeyDefinition? previous, CurveKeyDefinition key, CurveKeyDefinition? next)
    {
        if (previous is not null && next is not null)
            return (next.Value - previous.Value) /
                   Math.Max(0.0001f, next.Frame - previous.Frame);
        if (next is not null)
            return (next.Value - key.Value) /
                   Math.Max(0.0001f, next.Frame - key.Frame);
        if (previous is not null)
            return (key.Value - previous.Value) /
                   Math.Max(0.0001f, key.Frame - previous.Frame);
        return 0;
    }

    private static void AlignOppositeHandle(CurveKeyDefinition key, bool changedLeft)
    {
        var x = changedLeft ? key.LeftFrameOffset : key.RightFrameOffset;
        var y = changedLeft ? key.LeftValueOffset : key.RightValueOffset;
        var changedLength = MathF.Sqrt(x * x + y * y);
        if (changedLength < 0.0001f) return;
        var oppositeX = changedLeft ? key.RightFrameOffset : key.LeftFrameOffset;
        var oppositeY = changedLeft ? key.RightValueOffset : key.LeftValueOffset;
        var oppositeLength = MathF.Max(0.2f, MathF.Sqrt(oppositeX * oppositeX + oppositeY * oppositeY));
        var scale = -oppositeLength / changedLength;
        if (changedLeft)
        {
            key.RightFrameOffset = x * scale;
            key.RightValueOffset = y * scale;
        }
        else
        {
            key.LeftFrameOffset = x * scale;
            key.LeftValueOffset = y * scale;
        }
    }

    private static void SortKeys(AnimationCurveDefinition curve)
    {
        var sorted = curve.Keys.OrderBy(key => key.Frame).ToArray();
        curve.Keys.Clear();
        foreach (var key in sorted) curve.Keys.Add(key);
    }

    private static float SolveBezierTime(float target, float p0, float p1, float p2, float p3)
    {
        var low = 0f;
        var high = 1f;
        for (var iteration = 0; iteration < 18; iteration++)
        {
            var middle = (low + high) * 0.5f;
            if (Cubic(p0, p1, p2, p3, middle) < target) low = middle; else high = middle;
        }
        return (low + high) * 0.5f;
    }

    private static float Cubic(float p0, float p1, float p2, float p3, float t)
    {
        var oneMinus = 1 - t;
        return oneMinus * oneMinus * oneMinus * p0 + 3 * oneMinus * oneMinus * t * p1 +
               3 * oneMinus * t * t * p2 + t * t * t * p3;
    }

    private static float Lerp(float start, float end, float amount) => start + (end - start) * amount;
}
