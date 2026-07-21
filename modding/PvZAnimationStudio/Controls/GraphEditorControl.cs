using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PvZAnimationStudio.Models;
using PvZAnimationStudio.ViewModels;

namespace PvZAnimationStudio.Controls;

public sealed class GraphEditorControl : FrameworkElement
{
    private enum DragTarget { None, Key, LeftHandle, RightHandle, Pan }
    private sealed record ChannelStyle(CurveChannel Channel, string Name, Color Color);
    private sealed record RenderedKey(CurveChannel Channel, int Frame, Point Point);
    private sealed record RenderedHandle(CurveChannel Channel, int Frame, bool Left, Point Point);
    private sealed record RenderedLegend(CurveChannel Channel, Rect Bounds);

    private static readonly ChannelStyle[] Styles =
    [
        new(CurveChannel.X, "X 位移", Color.FromRgb(255, 90, 95)),
        new(CurveChannel.Y, "Y 位移", Color.FromRgb(85, 214, 118)),
        new(CurveChannel.SkewX, "X 旋转", Color.FromRgb(255, 200, 87)),
        new(CurveChannel.SkewY, "Y 旋转", Color.FromRgb(77, 157, 255)),
        new(CurveChannel.ScaleX, "X 缩放", Color.FromRgb(214, 108, 255)),
        new(CurveChannel.ScaleY, "Y 缩放", Color.FromRgb(71, 215, 232)),
        new(CurveChannel.Frame, "图片子帧", Color.FromRgb(255, 138, 61)),
        new(CurveChannel.Alpha, "透明度", Color.FromRgb(232, 237, 242))
    ];

    private readonly List<RenderedKey> _renderedKeys = [];
    private readonly List<RenderedHandle> _renderedHandles = [];
    private readonly List<RenderedLegend> _renderedLegends = [];
    private EditorViewModel? _viewModel;
    private CurveChannel? _selectedChannel;
    private int _selectedFrame;
    private DragTarget _dragTarget;
    private bool _dragTransaction;
    private Point _dragStart;
    private Point _lastPanPoint;
    private int _dragFrame;
    private Rect _plotRect;
    private float _valueMinimum = -1;
    private float _valueMaximum = 1;
    private double _frameZoom = 1;
    private double _valueZoom = 1;
    private double _panX;
    private double _panY;

    public event EventHandler? SelectionChanged;
    public CurveChannel? SelectedChannel => _selectedChannel;
    public int SelectedFrame => _selectedFrame;
    public CurveInterpolationMode? SelectedInterpolation => GetSelectedCurve()?.Interpolation;
    public CurveHandleMode? SelectedHandleMode => GetSelectedKey()?.HandleMode;

    public GraphEditorControl()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseRightButtonDown += OnMouseRightButtonDown;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseWheel += OnMouseWheel;
        KeyDown += OnKeyDown;
    }

    public void Bind(EditorViewModel viewModel)
    {
        if (_viewModel is not null) _viewModel.VisualStateChanged -= OnVisualStateChanged;
        _viewModel = viewModel;
        _viewModel.VisualStateChanged += OnVisualStateChanged;
        EnsureSelection();
        InvalidateVisual();
    }

    public void Unbind()
    {
        if (_viewModel is not null) _viewModel.VisualStateChanged -= OnVisualStateChanged;
        _viewModel = null;
        _renderedKeys.Clear();
        _renderedHandles.Clear();
        _renderedLegends.Clear();
    }

    public void FitAll()
    {
        _frameZoom = 1;
        _valueZoom = 1;
        _panX = 0;
        _panY = 0;
        InvalidateVisual();
    }

    public void NudgeSelected(int offset)
    {
        if (_viewModel is null || !_selectedChannel.HasValue || GetSelectedKey() is null) return;
        var target = Math.Clamp(_selectedFrame + offset, _viewModel.TimelineFrameStart, _viewModel.TimelineFrameEnd);
        if (target == _selectedFrame) return;
        var value = GetSelectedKey()!.Value;
        _viewModel.MoveCurveKey(_selectedChannel.Value, _selectedFrame, target, value);
        _selectedFrame = target;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteSelected()
    {
        if (_viewModel is null || !_selectedChannel.HasValue || GetSelectedKey() is null) return;
        _viewModel.DeleteCurveKey(_selectedChannel.Value, _selectedFrame);
        EnsureSelection();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSelectedInterpolation(CurveInterpolationMode mode)
    {
        if (_viewModel is null || !_selectedChannel.HasValue) return;
        _viewModel.SetCurveInterpolation(_selectedChannel.Value, mode);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSelectedHandleMode(CurveHandleMode mode)
    {
        if (_viewModel is null || !_selectedChannel.HasValue || GetSelectedKey() is null) return;
        _viewModel.SetCurveHandleMode(_selectedChannel.Value, _selectedFrame, mode);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(20, 25, 31)), null, new Rect(RenderSize));
        _renderedKeys.Clear();
        _renderedHandles.Clear();
        if (_viewModel?.SelectedTrack is null)
        {
            DrawMessage(context, "请先在时间轴或动画视图中选择一个轨道");
            return;
        }

        var channels = _viewModel.GetCurveChannels();
        if (channels.Count == 0)
        {
            DrawMessage(context, "当前轨道没有可绘制的数值关键帧；按 K 创建关键帧后即可编辑曲线");
            return;
        }
        EnsureSelection();
        DrawLegend(context, channels);
        _plotRect = new Rect(58, 50, Math.Max(80, ActualWidth - 72), Math.Max(80, ActualHeight - 78));
        CalculateValueRange(channels);
        DrawGrid(context);
        foreach (var style in Styles.Where(style => channels.Contains(style.Channel))) DrawCurve(context, style);
        DrawPlayhead(context);
        DrawSelectionStatus(context);
    }

    private void DrawLegend(DrawingContext context, IReadOnlyList<CurveChannel> channels)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var x = 10d;
        foreach (var style in Styles.Where(style => channels.Contains(style.Channel)))
        {
            var selected = style.Channel == _selectedChannel;
            var text = new FormattedText(style.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), 11, selected ? Brushes.White : Brushes.LightGray, dpi);
            var width = 29 + text.Width;
            if (selected)
                context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(48, 58, 70)), null,
                    new Rect(x - 4, 7, width + 8, 25), 4, 4);
            _renderedLegends.Add(new RenderedLegend(style.Channel, new Rect(x - 4, 7, width + 8, 25)));
            context.DrawLine(new Pen(new SolidColorBrush(style.Color), 3), new Point(x, 20), new Point(x + 20, 20));
            context.DrawText(text, new Point(x + 25, 11));
            x += width + 13;
        }
    }

    private void CalculateValueRange(IReadOnlyList<CurveChannel> channels)
    {
        var values = new List<float>();
        foreach (var channel in channels)
        {
            var curve = _viewModel!.GetCurveForDisplay(channel);
            if (curve is null) continue;
            values.AddRange(curve.Keys.Select(key => key.Value));
            foreach (var key in curve.Keys)
            {
                var handles = _viewModel.GetCurveHandles(channel, curve, key);
                values.Add(handles.Left.Value);
                values.Add(handles.Right.Value);
            }
        }
        if (values.Count == 0) { _valueMinimum = -1; _valueMaximum = 1; return; }
        _valueMinimum = values.Min();
        _valueMaximum = values.Max();
        var padding = Math.Max(0.5f, (_valueMaximum - _valueMinimum) * 0.12f);
        if (Math.Abs(_valueMaximum - _valueMinimum) < 0.001f) padding = Math.Max(1, Math.Abs(_valueMaximum) * 0.2f);
        _valueMinimum -= padding;
        _valueMaximum += padding;
    }

    private void DrawGrid(DrawingContext context)
    {
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 30, 37)),
            new Pen(new SolidColorBrush(Color.FromRgb(72, 83, 96)), 1), _plotRect);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var frameStep = Math.Max(1, (int)Math.Ceiling(_viewModel!.TimelineFrameCount / (10 * _frameZoom)));
        for (var frame = _viewModel.TimelineFrameStart; frame <= _viewModel.TimelineFrameEnd; frame += frameStep)
        {
            var x = FrameToX(frame);
            if (x < _plotRect.Left || x > _plotRect.Right) continue;
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(48, 57, 68)), 1),
                new Point(x, _plotRect.Top), new Point(x, _plotRect.Bottom));
            var label = new FormattedText((frame + 1).ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9,
                new SolidColorBrush(Color.FromRgb(156, 166, 176)), dpi);
            context.DrawText(label, new Point(x + 3, _plotRect.Bottom + 3));
        }
        for (var row = 0; row <= 5; row++)
        {
            var y = _plotRect.Top + row * _plotRect.Height / 5;
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(48, 57, 68)), 1),
                new Point(_plotRect.Left, y), new Point(_plotRect.Right, y));
            var value = YToValue(y);
            var label = new FormattedText(value.ToString("0.##", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture, FlowDirection.RightToLeft, new Typeface("Segoe UI"), 9,
                new SolidColorBrush(Color.FromRgb(156, 166, 176)), dpi)
            { MaxTextWidth = 50 };
            context.DrawText(label, new Point(4, y - 7));
        }
    }

    private void DrawCurve(DrawingContext context, ChannelStyle style)
    {
        var curve = _viewModel!.GetCurveForDisplay(style.Channel);
        if (curve is null || curve.Keys.Count == 0) return;
        var keys = curve.Keys.OrderBy(key => key.Frame).ToArray();
        var geometry = new StreamGeometry();
        using (var writer = geometry.Open())
        {
            writer.BeginFigure(ToPoint(keys[0].Frame, keys[0].Value), false, false);
            for (var index = 1; index < keys.Length; index++)
            {
                var previous = keys[index - 1];
                var current = keys[index];
                if (curve.Interpolation == CurveInterpolationMode.Bezier)
                {
                    var previousHandles = _viewModel.GetCurveHandles(style.Channel, curve, previous);
                    var currentHandles = _viewModel.GetCurveHandles(style.Channel, curve, current);
                    writer.BezierTo(ToPoint(previousHandles.Right.Frame, previousHandles.Right.Value),
                        ToPoint(currentHandles.Left.Frame, currentHandles.Left.Value),
                        ToPoint(current.Frame, current.Value), true, false);
                }
                else if (curve.Interpolation == CurveInterpolationMode.Constant)
                {
                    writer.LineTo(ToPoint(current.Frame, previous.Value), true, false);
                    writer.LineTo(ToPoint(current.Frame, current.Value), true, false);
                }
                else
                {
                    writer.LineTo(ToPoint(current.Frame, current.Value), true, false);
                }
            }
        }
        geometry.Freeze();
        var pen = new Pen(new SolidColorBrush(style.Color), style.Channel == _selectedChannel ? 2.6 : 1.8);
        context.DrawGeometry(null, pen, geometry);
        foreach (var key in keys)
        {
            var point = ToPoint(key.Frame, key.Value);
            _renderedKeys.Add(new RenderedKey(style.Channel, key.Frame, point));
            var selected = style.Channel == _selectedChannel && key.Frame == _selectedFrame;
            context.DrawEllipse(selected ? Brushes.Orange : Brushes.Black,
                new Pen(new SolidColorBrush(style.Color), selected ? 2 : 1.2), point, selected ? 5 : 3.8, selected ? 5 : 3.8);
            if (selected && curve.Interpolation == CurveInterpolationMode.Bezier) DrawHandles(context, style, curve, key);
        }
    }

    private void DrawHandles(DrawingContext context, ChannelStyle style,
        AnimationCurveDefinition curve, CurveKeyDefinition key)
    {
        var handles = _viewModel!.GetCurveHandles(style.Channel, curve, key);
        var keyPoint = ToPoint(key.Frame, key.Value);
        var leftPoint = ToPoint(handles.Left.Frame, handles.Left.Value);
        var rightPoint = ToPoint(handles.Right.Frame, handles.Right.Value);
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(210, style.Color.R, style.Color.G, style.Color.B)), 1.2);
        context.DrawLine(pen, keyPoint, leftPoint);
        context.DrawLine(pen, keyPoint, rightPoint);
        context.DrawEllipse(Brushes.Black, pen, leftPoint, 4.2, 4.2);
        context.DrawEllipse(Brushes.Black, pen, rightPoint, 4.2, 4.2);
        _renderedHandles.Add(new RenderedHandle(style.Channel, key.Frame, true, leftPoint));
        _renderedHandles.Add(new RenderedHandle(style.Channel, key.Frame, false, rightPoint));
    }

    private void DrawPlayhead(DrawingContext context)
    {
        var x = FrameToX(_viewModel!.CurrentFrame);
        if (x >= _plotRect.Left && x <= _plotRect.Right)
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(255, 92, 40)), 1.5),
                new Point(x, _plotRect.Top), new Point(x, _plotRect.Bottom));
    }

    private void DrawSelectionStatus(DrawingContext context)
    {
        var key = GetSelectedKey();
        if (key is null || !_selectedChannel.HasValue) return;
        var style = Styles.First(item => item.Channel == _selectedChannel.Value);
        var curve = GetSelectedCurve()!;
        var text = $"{style.Name}  ·  帧 {key.Frame + 1}  ·  值 {key.Value:0.###}  ·  {InterpolationName(curve.Interpolation)}  ·  {HandleName(key.HandleMode)}";
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), 10, Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(210, 15, 18, 23)), null,
            new Rect(_plotRect.Left + 8, _plotRect.Top + 8, formatted.Width + 16, 24), 4, 4);
        context.DrawText(formatted, new Point(_plotRect.Left + 16, _plotRect.Top + 13));
    }

    private void DrawMessage(DrawingContext context, string message)
    {
        var text = new FormattedText(message, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), 13, Brushes.LightGray,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(text, new Point(Math.Max(15, (ActualWidth - text.Width) / 2), Math.Max(20, ActualHeight / 2)));
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_viewModel is null) return;
        Focus();
        var point = eventArgs.GetPosition(this);
        var legend = _renderedLegends.FirstOrDefault(item => item.Bounds.Contains(point));
        if (legend is not null)
        {
            var keys = _viewModel.GetCurveKeyFrames(legend.Channel);
            if (keys.Count > 0)
                Select(legend.Channel, keys.OrderBy(frame => Math.Abs(frame - _viewModel.CurrentFrame)).First());
            eventArgs.Handled = true;
            return;
        }
        var handle = FindHandle(point);
        if (handle is not null)
        {
            Select(handle.Channel, handle.Frame);
            _dragTarget = handle.Left ? DragTarget.LeftHandle : DragTarget.RightHandle;
        }
        else
        {
            var key = FindKey(point);
            if (key is null) return;
            Select(key.Channel, key.Frame);
            _dragTarget = DragTarget.Key;
        }
        _dragStart = point;
        _dragFrame = _selectedFrame;
        _dragTransaction = false;
        CaptureMouse();
        eventArgs.Handled = true;
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (!_plotRect.Contains(eventArgs.GetPosition(this)) || _viewModel is null) return;
        _viewModel.CurrentFrame = Math.Clamp((int)Math.Round(XToFrame(eventArgs.GetPosition(this).X)),
            _viewModel.TimelineFrameStart, _viewModel.TimelineFrameEnd);
        eventArgs.Handled = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Middle) return;
        Focus();
        _dragTarget = DragTarget.Pan;
        _lastPanPoint = eventArgs.GetPosition(this);
        CaptureMouse();
        eventArgs.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (_viewModel is null || _dragTarget == DragTarget.None) return;
        var point = eventArgs.GetPosition(this);
        if (_dragTarget == DragTarget.Pan && eventArgs.MiddleButton == MouseButtonState.Pressed)
        {
            _panX += (point.X - _lastPanPoint.X) / Math.Max(1, _plotRect.Width);
            _panY += (point.Y - _lastPanPoint.Y) / Math.Max(1, _plotRect.Height);
            _lastPanPoint = point;
            InvalidateVisual();
            return;
        }
        if (eventArgs.LeftButton != MouseButtonState.Pressed || !_selectedChannel.HasValue) return;
        if (!_dragTransaction && (point - _dragStart).Length >= 3)
        {
            _viewModel.BeginEditTransaction(_dragTarget == DragTarget.Key
                ? "拖动曲线关键点" : "拖动 Bezier 曲线手柄");
            _dragTransaction = true;
        }
        if (!_dragTransaction) return;
        if (_dragTarget == DragTarget.Key)
        {
            var targetFrame = Math.Clamp((int)Math.Round(XToFrame(point.X)),
                _viewModel.TimelineFrameStart, _viewModel.TimelineFrameEnd);
            var value = (float)YToValue(point.Y);
            _viewModel.MoveCurveKey(_selectedChannel.Value, _dragFrame, targetFrame, value);
            _dragFrame = targetFrame;
            _selectedFrame = targetFrame;
        }
        else
        {
            _viewModel.SetCurveHandle(_selectedChannel.Value, _selectedFrame,
                _dragTarget == DragTarget.LeftHandle, (float)XToFrame(point.X), (float)YToValue(point.Y));
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_dragTarget == DragTarget.None) return;
        if (_dragTransaction) _viewModel?.EndEditTransaction();
        _dragTarget = DragTarget.None;
        _dragTransaction = false;
        ReleaseMouseCapture();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs eventArgs)
    {
        var factor = eventArgs.Delta > 0 ? 1.18 : 1 / 1.18;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            _valueZoom = Math.Clamp(_valueZoom * factor, 0.25, 16);
        else
            _frameZoom = Math.Clamp(_frameZoom * factor, 0.5, 20);
        InvalidateVisual();
        eventArgs.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key is Key.Delete or Key.Back)
        {
            DeleteSelected();
            eventArgs.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && eventArgs.Key is Key.Left or Key.Right)
        {
            NudgeSelected(eventArgs.Key == Key.Left ? -1 : 1);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Home)
        {
            FitAll();
            eventArgs.Handled = true;
        }
    }

    private void Select(CurveChannel channel, int frame)
    {
        _selectedChannel = channel;
        _selectedFrame = frame;
        if (_viewModel is not null) _viewModel.CurrentFrame = frame;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void EnsureSelection()
    {
        if (_viewModel is null) return;
        var channels = _viewModel.GetCurveChannels();
        if (channels.Count == 0) { _selectedChannel = null; return; }
        if (!_selectedChannel.HasValue || !channels.Contains(_selectedChannel.Value)) _selectedChannel = channels[0];
        var keys = _viewModel.GetCurveKeyFrames(_selectedChannel.Value);
        if (keys.Count == 0) return;
        if (!keys.Contains(_selectedFrame))
            _selectedFrame = keys.OrderBy(frame => Math.Abs(frame - _viewModel.CurrentFrame)).First();
    }

    private AnimationCurveDefinition? GetSelectedCurve() =>
        _viewModel is null || !_selectedChannel.HasValue ? null : _viewModel.GetCurveForDisplay(_selectedChannel.Value);

    private CurveKeyDefinition? GetSelectedKey() =>
        GetSelectedCurve()?.Keys.FirstOrDefault(key => key.Frame == _selectedFrame);

    private RenderedKey? FindKey(Point point) => _renderedKeys
        .Select(key => (key, distance: (key.Point - point).Length))
        .Where(item => item.distance <= 9)
        .OrderBy(item => item.distance).Select(item => item.key).FirstOrDefault();

    private RenderedHandle? FindHandle(Point point) => _renderedHandles
        .Select(handle => (handle, distance: (handle.Point - point).Length))
        .Where(item => item.distance <= 9)
        .OrderBy(item => item.distance).Select(item => item.handle).FirstOrDefault();

    private Point ToPoint(float frame, float value) => new(FrameToX(frame), ValueToY(value));

    private double FrameToX(double frame)
    {
        var start = _viewModel!.TimelineFrameStart;
        var span = Math.Max(1, _viewModel.TimelineFrameEnd - start);
        var normalized = (frame - start) / span;
        return _plotRect.Left + ((normalized - 0.5) * _frameZoom + 0.5 + _panX) * _plotRect.Width;
    }

    private double XToFrame(double x)
    {
        var normalized = ((x - _plotRect.Left) / Math.Max(1, _plotRect.Width) - 0.5 - _panX) / _frameZoom + 0.5;
        return _viewModel!.TimelineFrameStart + normalized * Math.Max(1, _viewModel.TimelineFrameEnd - _viewModel.TimelineFrameStart);
    }

    private double ValueToY(double value)
    {
        var normalized = (value - _valueMinimum) / Math.Max(0.0001, _valueMaximum - _valueMinimum);
        return _plotRect.Bottom - ((normalized - 0.5) * _valueZoom + 0.5 - _panY) * _plotRect.Height;
    }

    private double YToValue(double y)
    {
        var normalized = ((_plotRect.Bottom - y) / Math.Max(1, _plotRect.Height) - 0.5 + _panY) / _valueZoom + 0.5;
        return _valueMinimum + normalized * (_valueMaximum - _valueMinimum);
    }

    private void OnVisualStateChanged(object? sender, EventArgs eventArgs)
    {
        EnsureSelection();
        InvalidateVisual();
    }

    private static string InterpolationName(CurveInterpolationMode mode) => mode switch
    {
        CurveInterpolationMode.Linear => "线性",
        CurveInterpolationMode.Constant => "常量",
        _ => "Bezier"
    };

    private static string HandleName(CurveHandleMode mode) => mode switch
    {
        CurveHandleMode.Aligned => "对齐手柄",
        CurveHandleMode.Free => "自由手柄",
        CurveHandleMode.Vector => "矢量手柄",
        _ => "自动手柄"
    };
}
