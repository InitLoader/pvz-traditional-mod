using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PvZAnimationStudio.Models;
using PvZAnimationStudio.Services;
using PvZAnimationStudio.ViewModels;

namespace PvZAnimationStudio.Controls;

public sealed class AnimationPreviewControl : FrameworkElement
{
    private EditorViewModel? _viewModel;
    private OriginalResourceService? _resources;
    private Point _lastMouse;
    private bool _draggingTrack;
    private bool _panning;
    private Vector _pan;
    private double _zoom = 1;

    public AnimationPreviewControl()
    {
        Focusable = true;
        ClipToBounds = true;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseWheel += OnMouseWheel;
    }

    public void Bind(EditorViewModel viewModel, OriginalResourceService resources)
    {
        if (_viewModel is not null) _viewModel.VisualStateChanged -= OnVisualStateChanged;
        _viewModel = viewModel;
        _resources = resources;
        _viewModel.VisualStateChanged += OnVisualStateChanged;
        InvalidateVisual();
    }

    public void ResetView()
    {
        _pan = default;
        _zoom = 1;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(25, 31, 37)), null, new Rect(RenderSize));
        DrawGrid(context);
        if (_viewModel is null || _resources is null) return;

        var center = new Point(ActualWidth / 2 + _pan.X, ActualHeight / 2 + _pan.Y);
        foreach (var track in _viewModel.Project.Animation.Tracks)
        {
            if (track.IsActionTrack || track.Frames.Count == 0) continue;
            var frame = track.ResolveFrame(_viewModel.CurrentFrame);
            if (frame.Frame < 0 || frame.Alpha <= 0 || string.IsNullOrWhiteSpace(frame.Image)) continue;
            var bitmap = _resources.ResolveBitmap(_viewModel.Project, frame.Image);
            if (bitmap is null)
            {
                DrawMissingImage(context, center, track, frame);
                continue;
            }
            var width = bitmap.Width;
            var height = bitmap.Height;
            var matrix = Matrix.Identity;
            matrix.Translate(-width / 2, -height / 2);
            matrix.Scale(frame.ScaleX * _zoom, frame.ScaleY * _zoom);
            matrix.Skew(frame.SkewX, frame.SkewY);
            matrix.Translate(center.X + frame.X * _zoom, center.Y + frame.Y * _zoom);
            context.PushOpacity(Math.Clamp(frame.Alpha, 0, 1));
            context.PushTransform(new MatrixTransform(matrix));
            context.DrawImage(bitmap, new Rect(0, 0, width, height));
            if (ReferenceEquals(track, _viewModel.SelectedTrack))
                context.DrawRectangle(null, new Pen(Brushes.Gold, 1 / Math.Max(0.1, _zoom)), new Rect(0, 0, width, height));
            context.Pop();
            context.Pop();
        }

        var originPen = new Pen(new SolidColorBrush(Color.FromArgb(190, 105, 230, 110)), 1.5);
        context.DrawLine(originPen, new Point(center.X - 8, center.Y), new Point(center.X + 8, center.Y));
        context.DrawLine(originPen, new Point(center.X, center.Y - 8), new Point(center.X, center.Y + 8));
        DrawOverlay(context);
    }

    private void DrawGrid(DrawingContext context)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 180, 190, 200)), 1);
        var majorPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 180, 190, 200)), 1);
        var spacing = Math.Max(12, 32 * _zoom);
        var offsetX = (ActualWidth / 2 + _pan.X) % spacing;
        var offsetY = (ActualHeight / 2 + _pan.Y) % spacing;
        for (var x = offsetX; x < ActualWidth; x += spacing)
            context.DrawLine(Math.Abs(x - ActualWidth / 2 - _pan.X) < 1 ? majorPen : gridPen, new Point(x, 0), new Point(x, ActualHeight));
        for (var y = offsetY; y < ActualHeight; y += spacing)
            context.DrawLine(Math.Abs(y - ActualHeight / 2 - _pan.Y) < 1 ? majorPen : gridPen, new Point(0, y), new Point(ActualWidth, y));
    }

    private void DrawMissingImage(DrawingContext context, Point center, AnimationTrack track, ResolvedAnimationFrame frame)
    {
        var position = new Point(center.X + frame.X * _zoom, center.Y + frame.Y * _zoom);
        var rectangle = new Rect(position.X - 45, position.Y - 22, 90, 44);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(140, 92, 38, 38)), new Pen(Brushes.IndianRed, 1), rectangle);
        var text = new FormattedText(
            frame.Image ?? track.Name,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), 10, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = 84, MaxTextHeight = 38 };
        context.DrawText(text, new Point(rectangle.X + 3, rectangle.Y + 3));
    }

    private void DrawOverlay(DrawingContext context)
    {
        if (_viewModel is null) return;
        var text = new FormattedText(
            $"{_viewModel.FrameLabel}    缩放 {_zoom:P0}    左键拖动部件 / 中键平移 / 滚轮缩放",
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, new Rect(8, 8, text.Width + 16, text.Height + 8));
        context.DrawText(text, new Point(16, 12));
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs eventArgs)
    {
        Focus();
        _lastMouse = eventArgs.GetPosition(this);
        if (eventArgs.ChangedButton == MouseButton.Middle)
            _panning = true;
        else if (eventArgs.ChangedButton == MouseButton.Left && _viewModel?.SelectedTrack is { IsActionTrack: false })
        {
            _draggingTrack = true;
            _viewModel.SetKeyframe();
        }
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (!_panning && !_draggingTrack) return;
        var current = eventArgs.GetPosition(this);
        var delta = current - _lastMouse;
        _lastMouse = current;
        if (_panning)
        {
            _pan += delta;
            InvalidateVisual();
        }
        else if (_draggingTrack && _viewModel is not null)
        {
            _viewModel.MoveSelected((float)(delta.X / _zoom), (float)(delta.Y / _zoom));
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs eventArgs)
    {
        _panning = false;
        _draggingTrack = false;
        ReleaseMouseCapture();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs eventArgs)
    {
        _zoom = Math.Clamp(_zoom * (eventArgs.Delta > 0 ? 1.1 : 1 / 1.1), 0.1, 8);
        InvalidateVisual();
    }

    private void OnVisualStateChanged(object? sender, EventArgs eventArgs) => InvalidateVisual();
}
