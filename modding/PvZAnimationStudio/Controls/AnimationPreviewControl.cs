using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PvZAnimationStudio.Models;
using PvZAnimationStudio.Services;
using PvZAnimationStudio.ViewModels;

namespace PvZAnimationStudio.Controls;

public sealed record ExternalImagesDroppedEventArgs(IReadOnlyList<string> Files, Point WorldPosition);

public sealed class AnimationPreviewControl : FrameworkElement
{
    private enum GizmoHandle { None, FreeMove, MoveX, MoveY, Rotate, ScaleX, ScaleY, ScaleUniform }

    private sealed record RenderedPart(AnimationTrack Track, Matrix Matrix, Rect LocalBounds, Point Pivot);
    private sealed record PreviewFrame(int Frame, bool Selectable);

    private readonly List<RenderedPart> _renderedParts = [];
    private readonly EntityPreviewProfileService _entityProfiles = new();
    private EditorViewModel? _viewModel;
    private OriginalResourceService? _resources;
    private Point _lastMouse;
    private Point _dragStart;
    private Point _selectedPivot;
    private GizmoHandle _activeHandle;
    private bool _panning;
    private Vector _pan;
    private double _zoom = 1;

    public event EventHandler<ExternalImagesDroppedEventArgs>? ExternalImagesDropped;

    public AnimationPreviewControl()
    {
        Focusable = true;
        ClipToBounds = true;
        AllowDrop = true;
        Cursor = Cursors.Arrow;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseWheel += OnMouseWheel;
        DragOver += OnDragOver;
        Drop += OnDrop;
        MouseLeave += (_, _) => { if (_activeHandle == GizmoHandle.None) Cursor = Cursors.Arrow; };
    }

    public void Bind(EditorViewModel viewModel, OriginalResourceService resources)
    {
        if (_viewModel is not null) _viewModel.VisualStateChanged -= OnVisualStateChanged;
        _viewModel = viewModel;
        _resources = resources;
        _viewModel.VisualStateChanged += OnVisualStateChanged;
        InvalidateVisual();
    }

    public void Unbind()
    {
        if (_viewModel is not null) _viewModel.VisualStateChanged -= OnVisualStateChanged;
        _viewModel = null;
        _resources = null;
        _renderedParts.Clear();
    }

    public void ResetView()
    {
        _pan = default;
        _zoom = 1;
        InvalidateVisual();
    }

    public void FrameAll()
    {
        if (_viewModel is null || _resources is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        Rect? bounds = null;
        foreach (var previewFrame in GetPreviewFrames())
        {
            foreach (var track in _viewModel.Project.Animation.Tracks)
            {
                if (track.IsActionTrack || !track.HasRenderableContent ||
                    !_entityProfiles.IsTrackVisible(_viewModel.Project, track, _viewModel.SelectedAction)) continue;
                var frame = track.ResolveFrame(previewFrame.Frame);
                if (frame.Frame < 0 || frame.Alpha <= 0 || string.IsNullOrWhiteSpace(frame.Image)) continue;
                var image = _resources.ResolveImage(_viewModel.Project, frame.Image);
                if (image is null) continue;
                var cel = ReanimationRenderMath.GetCelRect(image, frame.Frame);
                var matrix = ReanimationRenderMath.CreateScreenMatrix(frame, 1, new Point(0, 0));
                var corners = new[]
                {
                    matrix.Transform(new Point(0, 0)),
                    matrix.Transform(new Point(cel.Width, 0)),
                    matrix.Transform(new Point(0, cel.Height)),
                    matrix.Transform(new Point(cel.Width, cel.Height))
                };
                var partBounds = new Rect(corners[0], corners[0]);
                foreach (var corner in corners.Skip(1)) partBounds.Union(corner);
                bounds = bounds is null ? partBounds : Rect.Union(bounds.Value, partBounds);
            }
        }
        if (bounds is null || bounds.Value.Width < 1 || bounds.Value.Height < 1) return;
        var availableWidth = Math.Max(100, ActualWidth - 140);
        var availableHeight = Math.Max(100, ActualHeight - 140);
        _zoom = Math.Clamp(Math.Min(availableWidth / bounds.Value.Width, availableHeight / bounds.Value.Height), 0.1, 4);
        var worldCenter = new Point(bounds.Value.Left + bounds.Value.Width / 2, bounds.Value.Top + bounds.Value.Height / 2);
        _pan = new Vector(-worldCenter.X * _zoom, -worldCenter.Y * _zoom);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 29, 35)), null, new Rect(RenderSize));
        DrawGrid(context);
        _renderedParts.Clear();
        if (_viewModel is null || _resources is null) return;

        var origin = GetOrigin();
        foreach (var previewFrame in GetPreviewFrames())
            DrawAnimationFrame(context, origin, previewFrame);

        var selected = _renderedParts.LastOrDefault(part => ReferenceEquals(part.Track, _viewModel.SelectedTrack));
        if (selected is not null)
        {
            _selectedPivot = selected.Pivot;
            DrawGizmo(context, selected.Pivot, _viewModel.ActiveTool);
        }

        var originPen = new Pen(new SolidColorBrush(Color.FromArgb(190, 105, 230, 110)), 1.5);
        context.DrawLine(originPen, new Point(origin.X - 8, origin.Y), new Point(origin.X + 8, origin.Y));
        context.DrawLine(originPen, new Point(origin.X, origin.Y - 8), new Point(origin.X, origin.Y + 8));
        DrawOverlay(context);
    }

    private void DrawAnimationFrame(DrawingContext context, Point origin, PreviewFrame previewFrame)
    {
        if (_viewModel is null || _resources is null) return;
        foreach (var track in _viewModel.Project.Animation.Tracks)
        {
            if (track.IsActionTrack || track.Frames.Count == 0 ||
                !_entityProfiles.IsTrackVisible(_viewModel.Project, track, _viewModel.SelectedAction)) continue;
            var frame = track.ResolveFrame(previewFrame.Frame);
            if (frame.Frame < 0 || frame.Alpha <= 0 || string.IsNullOrWhiteSpace(frame.Image)) continue;
            var image = _resources.ResolveImage(_viewModel.Project, frame.Image);
            if (image is null)
            {
                if (previewFrame.Selectable) DrawMissingImage(context, origin, track, frame);
                continue;
            }

            var celRect = ReanimationRenderMath.GetCelRect(image, frame.Frame);
            BitmapSource bitmap = image.Bitmap;
            if (celRect.Width != image.Bitmap.PixelWidth || celRect.Height != image.Bitmap.PixelHeight)
            {
                var cropped = new CroppedBitmap(image.Bitmap, celRect);
                cropped.Freeze();
                bitmap = cropped;
            }

            var matrix = ReanimationRenderMath.CreateScreenMatrix(frame, _zoom, origin);
            // PvZ's renderer feeds centered vertices into a +half-width/+half-height
            // pivot matrix before applying the track transform. WPF DrawImage already
            // uses top-left coordinates, so the equivalent local rectangle starts at 0,0.
            // Centering it here again shifts every rotated body part by half its bitmap.
            var localBounds = new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight);
            var pivot = new Point(matrix.OffsetX, matrix.OffsetY);
            if (previewFrame.Selectable)
                _renderedParts.Add(new RenderedPart(track, matrix, localBounds, pivot));

            context.PushOpacity(Math.Clamp(frame.Alpha, 0, 1));
            context.PushTransform(new MatrixTransform(matrix));
            context.DrawImage(bitmap, localBounds);
            if (previewFrame.Selectable && ReferenceEquals(track, _viewModel.SelectedTrack))
                context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(255, 214, 66)), 1.5 / Math.Max(0.1, _zoom)), localBounds);
            context.Pop();
            context.Pop();
        }
    }

    private IReadOnlyList<PreviewFrame> GetPreviewFrames()
    {
        if (_viewModel is null) return [];
        var result = new List<PreviewFrame> { new(_viewModel.CurrentFrame, true) };
        var plan = _entityProfiles.CreatePlan(
            _viewModel.Project, _viewModel.CurrentFrame, _viewModel.SelectedAction);
        if (plan is null) return result;
        result.AddRange(plan.OverlayLayers
            .Where(layer => result.All(existing => existing.Frame != layer.Frame))
            .Select(layer => new PreviewFrame(layer.Frame, false)));
        return result;
    }

    private Point GetOrigin() => new(ActualWidth / 2 + _pan.X, ActualHeight / 2 + _pan.Y);

    private void OnDragOver(object sender, DragEventArgs eventArgs)
    {
        eventArgs.Effects = GetDroppedImages(eventArgs.Data).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        eventArgs.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs eventArgs)
    {
        var files = GetDroppedImages(eventArgs.Data);
        if (files.Count == 0) return;
        var screen = eventArgs.GetPosition(this);
        var origin = GetOrigin();
        var world = new Point((screen.X - origin.X) / _zoom, (screen.Y - origin.Y) / _zoom);
        ExternalImagesDropped?.Invoke(this, new ExternalImagesDroppedEventArgs(files, world));
        eventArgs.Effects = DragDropEffects.Copy;
        eventArgs.Handled = true;
    }

    private static IReadOnlyList<string> GetDroppedImages(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] files)
            return [];
        return files.Where(file => File.Exists(file) && Path.GetExtension(file) is var extension &&
                                   (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                                    extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                    extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void DrawGrid(DrawingContext context)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(42, 180, 190, 200)), 1);
        var majorPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 180, 190, 200)), 1);
        var spacing = Math.Max(12, 32 * _zoom);
        var origin = GetOrigin();
        var offsetX = origin.X % spacing;
        var offsetY = origin.Y % spacing;
        for (var x = offsetX; x < ActualWidth; x += spacing)
            context.DrawLine(Math.Abs(x - origin.X) < 1 ? majorPen : gridPen, new Point(x, 0), new Point(x, ActualHeight));
        for (var y = offsetY; y < ActualHeight; y += spacing)
            context.DrawLine(Math.Abs(y - origin.Y) < 1 ? majorPen : gridPen, new Point(0, y), new Point(ActualWidth, y));
    }

    private static void DrawGizmo(DrawingContext context, Point pivot, EditorTool tool)
    {
        var red = new SolidColorBrush(Color.FromRgb(242, 78, 78));
        var blue = new SolidColorBrush(Color.FromRgb(72, 154, 255));
        var yellow = new SolidColorBrush(Color.FromRgb(255, 211, 55));
        var shadow = new Pen(new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), 4);
        var redPen = new Pen(red, 2.5);
        var bluePen = new Pen(blue, 2.5);
        var yellowPen = new Pen(yellow, 2.5);

        context.DrawEllipse(new SolidColorBrush(Color.FromRgb(35, 40, 46)), new Pen(yellow, 2), pivot, 6, 6);
        if (tool == EditorTool.Select) return;

        if (tool == EditorTool.Rotate)
        {
            context.DrawEllipse(null, shadow, pivot, 58, 58);
            context.DrawEllipse(null, yellowPen, pivot, 58, 58);
            context.DrawEllipse(yellow, null, new Point(pivot.X + 41, pivot.Y - 41), 5, 5);
            return;
        }

        DrawArrow(context, pivot, new Point(pivot.X + 72, pivot.Y), redPen, red);
        DrawArrow(context, pivot, new Point(pivot.X, pivot.Y - 72), bluePen, blue);
        if (tool == EditorTool.Scale)
        {
            context.DrawRectangle(red, new Pen(Brushes.White, 1), new Rect(pivot.X + 66, pivot.Y - 6, 12, 12));
            context.DrawRectangle(blue, new Pen(Brushes.White, 1), new Rect(pivot.X - 6, pivot.Y - 78, 12, 12));
            context.DrawLine(yellowPen, pivot, new Point(pivot.X + 52, pivot.Y - 52));
            context.DrawRectangle(yellow, new Pen(Brushes.White, 1), new Rect(pivot.X + 46, pivot.Y - 58, 12, 12));
        }
    }

    private static void DrawArrow(DrawingContext context, Point start, Point end, Pen pen, Brush fill)
    {
        context.DrawLine(pen, start, end);
        var direction = end - start;
        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        var geometry = new StreamGeometry();
        using (var writer = geometry.Open())
        {
            writer.BeginFigure(end, true, true);
            writer.LineTo(end - direction * 12 + normal * 6, true, false);
            writer.LineTo(end - direction * 12 - normal * 6, true, false);
        }
        geometry.Freeze();
        context.DrawGeometry(fill, null, geometry);
    }

    private void DrawMissingImage(DrawingContext context, Point origin, AnimationTrack track, ResolvedAnimationFrame frame)
    {
        var position = new Point(origin.X + frame.X * _zoom, origin.Y + frame.Y * _zoom);
        var rectangle = new Rect(position.X - 48, position.Y - 24, 96, 48);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(180, 92, 38, 38)), new Pen(Brushes.IndianRed, 1), rectangle);
        var text = MakeText(frame.Image ?? track.Name, 10, Brushes.White);
        text.MaxTextWidth = 90;
        text.MaxTextHeight = 42;
        context.DrawText(text, new Point(rectangle.X + 3, rectangle.Y + 3));
    }

    private void DrawOverlay(DrawingContext context)
    {
        if (_viewModel is null) return;
        var tool = _viewModel.ActiveTool switch
        {
            EditorTool.Select => "选择(Q)",
            EditorTool.Move => "移动(W)",
            EditorTool.Rotate => "旋转(E)",
            _ => "缩放(R)"
        };
        var line1 = MakeText($"{_viewModel.FrameLabel}    视图 {_zoom:P0}    工具：{tool}", 13, Brushes.White, FontWeights.SemiBold);
        var selectedName = _viewModel.SelectedTrack?.Name ?? "未选择部件";
        var plan = _entityProfiles.CreatePlan(_viewModel.Project, _viewModel.CurrentFrame, _viewModel.SelectedAction);
        var previewNote = plan is null ? string.Empty : $"    实体组合预览：{plan.DisplayName}";
        var line2 = MakeText($"当前：{selectedName}{previewNote}    左键选择/拖动操纵器 · 中键平移 · 滚轮缩放 · 方向键微调 · Ctrl+Z 撤销", 12,
            new SolidColorBrush(Color.FromRgb(218, 226, 235)));
        var width = Math.Max(line1.Width, line2.Width) + 20;
        context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(205, 6, 10, 14)), new Pen(new SolidColorBrush(Color.FromArgb(150, 95, 110, 125)), 1),
            new Rect(8, 8, width, line1.Height + line2.Height + 17), 4, 4);
        context.DrawText(line1, new Point(18, 13));
        context.DrawText(line2, new Point(18, 15 + line1.Height));
    }

    private FormattedText MakeText(string text, double size, Brush brush, FontWeight? weight = null) => new(
        text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
        size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void OnMouseDown(object sender, MouseButtonEventArgs eventArgs)
    {
        Focus();
        _lastMouse = _dragStart = eventArgs.GetPosition(this);
        if (eventArgs.ChangedButton == MouseButton.Middle)
        {
            _panning = true;
            Cursor = Cursors.SizeAll;
            CaptureMouse();
            return;
        }
        if (eventArgs.ChangedButton != MouseButton.Left || _viewModel is null) return;

        _activeHandle = HitTestGizmo(_lastMouse);
        if (_activeHandle == GizmoHandle.None)
        {
            var hit = HitTestPart(_lastMouse);
            if (hit is null) return;
            _viewModel.SelectedTrack = hit.Track;
            _selectedPivot = hit.Pivot;
            if (_viewModel.ActiveTool == EditorTool.Move) _activeHandle = GizmoHandle.FreeMove;
        }

        if (_activeHandle != GizmoHandle.None)
        {
            _viewModel.BeginEditTransaction(_viewModel.ActiveTool switch
            {
                EditorTool.Rotate => "旋转部件",
                EditorTool.Scale => "缩放部件",
                _ => "移动部件"
            });
            Cursor = _activeHandle == GizmoHandle.Rotate ? Cursors.Hand : Cursors.SizeAll;
            CaptureMouse();
        }
        InvalidateVisual();
    }

    private void OnMouseMove(object sender, MouseEventArgs eventArgs)
    {
        var current = eventArgs.GetPosition(this);
        if (_panning)
        {
            _pan += current - _lastMouse;
            _lastMouse = current;
            InvalidateVisual();
            return;
        }
        if (_activeHandle != GizmoHandle.None && _viewModel is not null && eventArgs.LeftButton == MouseButtonState.Pressed)
        {
            var delta = current - _lastMouse;
            switch (_activeHandle)
            {
                case GizmoHandle.FreeMove:
                    _viewModel.MoveSelected((float)(delta.X / _zoom), (float)(delta.Y / _zoom));
                    break;
                case GizmoHandle.MoveX:
                    _viewModel.MoveSelected((float)(delta.X / _zoom), 0);
                    break;
                case GizmoHandle.MoveY:
                    _viewModel.MoveSelected(0, (float)(delta.Y / _zoom));
                    break;
                case GizmoHandle.ScaleX:
                    _viewModel.ScaleSelected((float)Math.Max(0.05, 1 + delta.X / 100), 1);
                    break;
                case GizmoHandle.ScaleY:
                    _viewModel.ScaleSelected(1, (float)Math.Max(0.05, 1 - delta.Y / 100));
                    break;
                case GizmoHandle.ScaleUniform:
                    _viewModel.ScaleSelected((float)Math.Max(0.05, 1 + (delta.X - delta.Y) / 180),
                        (float)Math.Max(0.05, 1 + (delta.X - delta.Y) / 180));
                    break;
                case GizmoHandle.Rotate:
                    var before = Math.Atan2(_lastMouse.Y - _selectedPivot.Y, _lastMouse.X - _selectedPivot.X);
                    var after = Math.Atan2(current.Y - _selectedPivot.Y, current.X - _selectedPivot.X);
                    _viewModel.RotateSelected((float)((after - before) * 180 / Math.PI));
                    break;
            }
            _lastMouse = current;
            return;
        }

        Cursor = HitTestGizmo(current) != GizmoHandle.None || HitTestPart(current) is not null ? Cursors.Hand : Cursors.Arrow;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_activeHandle != GizmoHandle.None) _viewModel?.EndEditTransaction();
        _panning = false;
        _activeHandle = GizmoHandle.None;
        Cursor = Cursors.Arrow;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs eventArgs)
    {
        if (_viewModel is not null && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _viewModel.SelectedTrack is not null)
        {
            _viewModel.BeginEditTransaction("缩放部件");
            var factor = eventArgs.Delta > 0 ? 1.05f : 1 / 1.05f;
            _viewModel.ScaleSelected(factor, factor);
            _viewModel.EndEditTransaction();
        }
        else
        {
            _zoom = Math.Clamp(_zoom * (eventArgs.Delta > 0 ? 1.1 : 1 / 1.1), 0.1, 8);
            InvalidateVisual();
        }
        eventArgs.Handled = true;
    }

    private RenderedPart? HitTestPart(Point point)
    {
        for (var index = _renderedParts.Count - 1; index >= 0; index--)
        {
            var part = _renderedParts[index];
            if (!part.Matrix.HasInverse) continue;
            var inverse = part.Matrix;
            inverse.Invert();
            if (part.LocalBounds.Contains(inverse.Transform(point))) return part;
        }
        return null;
    }

    private GizmoHandle HitTestGizmo(Point point)
    {
        if (_viewModel?.SelectedTrack is null || !_renderedParts.Any(part => ReferenceEquals(part.Track, _viewModel.SelectedTrack)))
            return GizmoHandle.None;
        var pivot = _selectedPivot;
        if ((point - pivot).Length <= 10 && _viewModel.ActiveTool != EditorTool.Select) return GizmoHandle.FreeMove;
        return _viewModel.ActiveTool switch
        {
            EditorTool.Move when DistanceToSegment(point, pivot, new Point(pivot.X + 72, pivot.Y)) <= 8 => GizmoHandle.MoveX,
            EditorTool.Move when DistanceToSegment(point, pivot, new Point(pivot.X, pivot.Y - 72)) <= 8 => GizmoHandle.MoveY,
            EditorTool.Scale when (point - new Point(pivot.X + 72, pivot.Y)).Length <= 12 => GizmoHandle.ScaleX,
            EditorTool.Scale when (point - new Point(pivot.X, pivot.Y - 72)).Length <= 12 => GizmoHandle.ScaleY,
            EditorTool.Scale when (point - new Point(pivot.X + 52, pivot.Y - 52)).Length <= 12 => GizmoHandle.ScaleUniform,
            EditorTool.Rotate when Math.Abs((point - pivot).Length - 58) <= 9 => GizmoHandle.Rotate,
            _ => GizmoHandle.None
        };
    }

    private static double DistanceToSegment(Point point, Point start, Point end)
    {
        var lengthSquared = (end - start).LengthSquared;
        if (lengthSquared <= double.Epsilon) return (point - start).Length;
        var t = Math.Clamp(Vector.Multiply(point - start, end - start) / lengthSquared, 0, 1);
        return (point - (start + (end - start) * t)).Length;
    }

    private void OnVisualStateChanged(object? sender, EventArgs eventArgs) => InvalidateVisual();
}
