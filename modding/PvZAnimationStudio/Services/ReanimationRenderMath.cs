using System.Windows;
using System.Windows.Media;
using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public static class ReanimationRenderMath
{
    public static Matrix CreateScreenMatrix(ResolvedAnimationFrame frame, double zoom, Point origin)
    {
        // PvZ does not use a conventional shear transform. kx and ky independently
        // define the two transformed basis axes (TodLib Reanimation::MatrixFromTransform).
        var radiansX = -frame.SkewX * Math.PI / 180.0;
        var radiansY = -frame.SkewY * Math.PI / 180.0;
        return new Matrix(
            Math.Cos(radiansX) * frame.ScaleX * zoom,
            -Math.Sin(radiansX) * frame.ScaleX * zoom,
            Math.Sin(radiansY) * frame.ScaleY * zoom,
            Math.Cos(radiansY) * frame.ScaleY * zoom,
            origin.X + frame.X * zoom,
            origin.Y + frame.Y * zoom);
    }

    public static Int32Rect GetCelRect(ImageResourceInfo image, float frameValue)
    {
        var columns = image.SafeColumns;
        var rows = image.SafeRows;
        var celCount = columns * rows;
        var index = ((int)Math.Round(frameValue) % celCount + celCount) % celCount;
        var celWidth = image.Bitmap.PixelWidth / columns;
        var celHeight = image.Bitmap.PixelHeight / rows;
        return new Int32Rect((index % columns) * celWidth, (index / columns) * celHeight, celWidth, celHeight);
    }
}
