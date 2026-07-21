using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public enum TweenCurve
{
    Linear,
    SmoothStep
}

public sealed class TweenService
{
    public int BakeToNextKeyframe(AnimationTrack track, int startFrame, TweenCurve curve)
    {
        if (track.Frames.Count < 2)
            throw new InvalidOperationException("当前轨道不足两帧，无法创建补间。");

        startFrame = Math.Clamp(startFrame, 0, track.Frames.Count - 1);
        if (!track.Frames[startFrame].HasKey)
            track.Frames[startFrame] = track.ResolveFrame(startFrame).ToExplicitFrame();

        var endFrame = -1;
        for (var index = startFrame + 1; index < track.Frames.Count; index++)
        {
            if (!track.Frames[index].HasKey) continue;
            endFrame = index;
            break;
        }

        if (endFrame < 0)
            throw new InvalidOperationException("当前关键帧右侧没有结束关键帧。请先在后面的帧设置位置或缩放。");
        if (endFrame == startFrame + 1)
            return 0;

        var start = track.ResolveFrame(startFrame);
        var end = track.ResolveFrame(endFrame);
        for (var index = startFrame + 1; index < endFrame; index++)
        {
            var amount = (float)(index - startFrame) / (endFrame - startFrame);
            if (curve == TweenCurve.SmoothStep)
                amount = amount * amount * (3f - 2f * amount);

            var existing = track.Frames[index];
            existing.X = Lerp(start.X, end.X, amount);
            existing.Y = Lerp(start.Y, end.Y, amount);
            existing.SkewX = LerpAngle(start.SkewX, end.SkewX, amount);
            existing.SkewY = LerpAngle(start.SkewY, end.SkewY, amount);
            existing.ScaleX = Lerp(start.ScaleX, end.ScaleX, amount);
            existing.ScaleY = Lerp(start.ScaleY, end.ScaleY, amount);
            existing.Alpha = Lerp(start.Alpha, end.Alpha, amount);
        }

        return endFrame - startFrame - 1;
    }

    private static float Lerp(float start, float end, float amount) => start + (end - start) * amount;

    private static float LerpAngle(float start, float end, float amount)
    {
        var delta = (end - start) % 360f;
        if (delta > 180f) delta -= 360f;
        if (delta < -180f) delta += 360f;
        return start + delta * amount;
    }
}
