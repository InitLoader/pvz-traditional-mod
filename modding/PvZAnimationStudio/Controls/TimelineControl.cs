using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PvZAnimationStudio.ViewModels;

namespace PvZAnimationStudio.Controls;

public sealed class TimelineControl : FrameworkElement
{
    private const double HeaderWidth = 210;
    private const double CellWidth = 14;
    private const double RowHeight = 26;
    private EditorViewModel? _viewModel;
    private bool _pendingKeyDrag;
    private bool _keyDragActive;
    private int _dragFrame;
    private Point _dragOrigin;

    public TimelineControl()
    {
        Focusable = true;
        Cursor = Cursors.Arrow;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        KeyDown += OnKeyDown;
    }

    public void Bind(EditorViewModel viewModel)
    {
        if (_viewModel is not null) _viewModel.VisualStateChanged -= OnVisualStateChanged;
        _viewModel = viewModel;
        _viewModel.VisualStateChanged += OnVisualStateChanged;
        UpdateExtent();
    }

    public void Unbind()
    {
        if (_viewModel is not null) _viewModel.VisualStateChanged -= OnVisualStateChanged;
        _viewModel = null;
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 28, 34)), null, new Rect(RenderSize));
        if (_viewModel is null) return;
        var tracks = _viewModel.TimelineTracks;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(56, 64, 75)), 1);
        var rangeStart = _viewModel.TimelineFrameStart;
        var rangeEnd = _viewModel.TimelineFrameEnd;

        for (var row = 0; row < tracks.Count; row++)
        {
            var track = tracks[row];
            var y = row * RowHeight;
            var selected = ReferenceEquals(track, _viewModel.SelectedTrack);
            var background = selected
                ? new SolidColorBrush(Color.FromRgb(50, 74, 62))
                : track.IsActionTrack
                    ? new SolidColorBrush(Color.FromRgb(48, 40, 62))
                    : new SolidColorBrush(row % 2 == 0 ? Color.FromRgb(30, 35, 42) : Color.FromRgb(27, 32, 39));
            context.DrawRectangle(background, null, new Rect(0, y, ActualWidth, RowHeight));
            var label = new FormattedText(
                track.IsActionTrack ? $"动作范围  {track.Name}" : track.Name,
                CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), 11,
                track.IsActionTrack ? Brushes.Plum : Brushes.White, dpi)
            { MaxTextWidth = HeaderWidth - 12, Trimming = TextTrimming.CharacterEllipsis };
            context.DrawText(label, new Point(7, y + 5));
            context.DrawLine(gridPen, new Point(0, y + RowHeight), new Point(ActualWidth, y + RowHeight));

            for (var absoluteFrame = rangeStart; absoluteFrame <= rangeEnd; absoluteFrame++)
            {
                var localFrame = absoluteFrame - rangeStart;
                var x = HeaderWidth + localFrame * CellWidth;
                if (localFrame % 5 == 0)
                {
                    context.DrawLine(gridPen, new Point(x, y), new Point(x, y + RowHeight));
                    if (row == 0)
                    {
                        var frameNumber = new FormattedText((localFrame + 1).ToString(CultureInfo.InvariantCulture),
                            CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 8,
                            new SolidColorBrush(Color.FromRgb(150, 160, 170)), dpi);
                        context.DrawText(frameNumber, new Point(x + 1, y + 1));
                    }
                }
                if (!_viewModel.IsMeaningfulKey(track, absoluteFrame)) continue;
                DrawDiamond(context, new Point(x + CellWidth / 2, y + RowHeight / 2 + 2),
                    selected ? Brushes.Gold : track.IsActionTrack ? Brushes.Plum : Brushes.LightGreen);
            }
        }

        var currentLocalFrame = _viewModel.CurrentFrame - rangeStart;
        var playheadX = HeaderWidth + currentLocalFrame * CellWidth + CellWidth / 2;
        context.DrawLine(new Pen(Brushes.OrangeRed, 2), new Point(playheadX, 0), new Point(playheadX, ActualHeight));
    }

    private static void DrawDiamond(DrawingContext context, Point center, Brush fill)
    {
        var geometry = new StreamGeometry();
        using (var writer = geometry.Open())
        {
            writer.BeginFigure(new Point(center.X, center.Y - 5), true, true);
            writer.LineTo(new Point(center.X + 5, center.Y), true, false);
            writer.LineTo(new Point(center.X, center.Y + 5), true, false);
            writer.LineTo(new Point(center.X - 5, center.Y), true, false);
        }
        geometry.Freeze();
        context.DrawGeometry(fill, null, geometry);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_viewModel is null) return;
        Focus();
        var tracks = _viewModel.TimelineTracks;
        var point = eventArgs.GetPosition(this);
        var row = (int)(point.Y / RowHeight);
        if (row < 0 || row >= tracks.Count) return;
        _viewModel.SelectedTrack = tracks[row];
        if (point.X >= HeaderWidth)
        {
            var localFrame = (int)((point.X - HeaderWidth) / CellWidth);
            _viewModel.CurrentFrame = Math.Clamp(_viewModel.TimelineFrameStart + localFrame,
                _viewModel.TimelineFrameStart, _viewModel.TimelineFrameEnd);
            if (eventArgs.ClickCount >= 2)
            {
                _viewModel.ToggleKeyframe();
            }
            else if (_viewModel.IsMeaningfulKey(_viewModel.SelectedTrack, _viewModel.CurrentFrame))
            {
                _pendingKeyDrag = true;
                _keyDragActive = false;
                _dragFrame = _viewModel.CurrentFrame;
                _dragOrigin = point;
                CaptureMouse();
            }
        }
        InvalidateVisual();
    }

    private void OnMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (_viewModel is null || !_pendingKeyDrag || eventArgs.LeftButton != MouseButtonState.Pressed) return;
        var point = eventArgs.GetPosition(this);
        var target = Math.Clamp(_viewModel.TimelineFrameStart +
                                (int)Math.Floor((point.X - HeaderWidth) / CellWidth),
            _viewModel.TimelineFrameStart, _viewModel.TimelineFrameEnd);
        if (!_keyDragActive && Math.Abs(point.X - _dragOrigin.X) >= 4)
        {
            _viewModel.BeginEditTransaction("拖动轨道关键帧");
            _keyDragActive = true;
        }
        if (!_keyDragActive || target == _dragFrame) return;
        if (_viewModel.MoveCurrentKeyframe(target)) _dragFrame = target;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (!_pendingKeyDrag) return;
        if (_keyDragActive) _viewModel?.EndEditTransaction();
        _pendingKeyDrag = false;
        _keyDragActive = false;
        ReleaseMouseCapture();
    }

    private void OnKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (_viewModel is null) return;
        if (eventArgs.Key is Key.Delete or Key.Back)
        {
            _viewModel.DeleteCurrentKeyframe();
            eventArgs.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && eventArgs.Key is Key.Left or Key.Right)
        {
            _viewModel.NudgeCurrentKeyframe(eventArgs.Key == Key.Left ? -1 : 1);
            eventArgs.Handled = true;
        }
    }

    private void UpdateExtent()
    {
        if (_viewModel is null) return;
        Width = Math.Max(500, HeaderWidth + Math.Max(1, _viewModel.TimelineFrameCount) * CellWidth);
        Height = Math.Max(120, _viewModel.TimelineTracks.Count * RowHeight);
        InvalidateVisual();
    }

    private void OnVisualStateChanged(object? sender, EventArgs eventArgs) => UpdateExtent();
}
