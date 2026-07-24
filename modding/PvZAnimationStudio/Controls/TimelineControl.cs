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
    private readonly HashSet<TimelineKeySelection> _selectedKeys = [];
    private bool _pendingKeyDrag;
    private bool _keyDragActive;
    private int _dragFrame;
    private Point _dragOrigin;
    private bool _boxSelecting;
    private bool _boxAdditive;
    private Point _boxStart;
    private Rect _boxRect;

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

    public void CopySelectedKeyframes()
    {
        _viewModel?.CopyTimelineKeys(_selectedKeys);
    }

    public void CopyCurrentTrackKeyframes()
    {
        _viewModel?.CopyCurrentTrackKeyframes();
    }

    public void PasteCopiedKeyframes()
    {
        if (_viewModel is null) return;
        var pasted = _viewModel.PasteTimelineKeys();
        if (pasted.Count == 0) return;
        _selectedKeys.Clear();
        foreach (var key in pasted) _selectedKeys.Add(key);
        InvalidateVisual();
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
            DrawVisibilityIcon(context, new Point(16, y + RowHeight / 2), track.IsVisibleInEditor);
            var label = new FormattedText(
                track.IsActionTrack ? $"动作范围  {track.Name}" : track.Name,
                CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), 11,
                track.IsActionTrack ? Brushes.Plum : Brushes.White, dpi)
            { MaxTextWidth = HeaderWidth - 40, Trimming = TextTrimming.CharacterEllipsis };
            context.DrawText(label, new Point(33, y + 5));
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
                var keySelected = _selectedKeys.Contains(new TimelineKeySelection(track.EditorId, absoluteFrame));
                DrawDiamond(context, new Point(x + CellWidth / 2, y + RowHeight / 2 + 2),
                    keySelected ? Brushes.Orange : selected ? Brushes.Gold : track.IsActionTrack ? Brushes.Plum : Brushes.LightGreen);
            }
        }

        var currentLocalFrame = _viewModel.CurrentFrame - rangeStart;
        var playheadX = HeaderWidth + currentLocalFrame * CellWidth + CellWidth / 2;
        context.DrawLine(new Pen(Brushes.OrangeRed, 2), new Point(playheadX, 0), new Point(playheadX, ActualHeight));
        if (_boxSelecting)
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(42, 73, 151, 255)),
                new Pen(new SolidColorBrush(Color.FromRgb(95, 176, 255)), 1), _boxRect);
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

    private static void DrawVisibilityIcon(DrawingContext context, Point center, bool visible)
    {
        var color = visible ? Color.FromRgb(208, 220, 232) : Color.FromRgb(105, 115, 126);
        var pen = new Pen(new SolidColorBrush(color), 1.4);
        var geometry = new StreamGeometry();
        using (var writer = geometry.Open())
        {
            writer.BeginFigure(new Point(center.X - 8, center.Y), false, false);
            writer.BezierTo(new Point(center.X - 4, center.Y - 6), new Point(center.X + 4, center.Y - 6),
                new Point(center.X + 8, center.Y), true, false);
            writer.BezierTo(new Point(center.X + 4, center.Y + 6), new Point(center.X - 4, center.Y + 6),
                new Point(center.X - 8, center.Y), true, false);
        }
        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);
        if (visible)
            context.DrawEllipse(new SolidColorBrush(color), null, center, 2.5, 2.5);
        else
            context.DrawLine(pen, new Point(center.X - 7, center.Y + 7), new Point(center.X + 7, center.Y - 7));
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
        if (eventArgs.ChangedButton == MouseButton.Left && point.X < 31)
        {
            _viewModel.ToggleTrackEditorVisibility(tracks[row]);
            eventArgs.Handled = true;
            return;
        }
        if (point.X >= HeaderWidth)
        {
            var localFrame = (int)((point.X - HeaderWidth) / CellWidth);
            _viewModel.CurrentFrame = Math.Clamp(_viewModel.TimelineFrameStart + localFrame,
                _viewModel.TimelineFrameStart, _viewModel.TimelineFrameEnd);
            var selection = new TimelineKeySelection(tracks[row].EditorId, _viewModel.CurrentFrame);
            if (eventArgs.ClickCount >= 2)
            {
                _viewModel.ToggleKeyframe();
                _selectedKeys.Clear();
                if (_viewModel.IsMeaningfulKey(tracks[row], _viewModel.CurrentFrame)) _selectedKeys.Add(selection);
            }
            else if (_viewModel.IsMeaningfulKey(_viewModel.SelectedTrack, _viewModel.CurrentFrame))
            {
                var additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
                               Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                if (additive)
                {
                    if (!_selectedKeys.Add(selection)) _selectedKeys.Remove(selection);
                }
                else if (!_selectedKeys.Contains(selection))
                {
                    _selectedKeys.Clear();
                    _selectedKeys.Add(selection);
                }
                if (!_selectedKeys.Contains(selection)) { InvalidateVisual(); return; }
                _pendingKeyDrag = true;
                _keyDragActive = false;
                _dragFrame = _viewModel.CurrentFrame;
                _dragOrigin = point;
                CaptureMouse();
            }
            else if (eventArgs.ChangedButton == MouseButton.Left)
            {
                _boxSelecting = true;
                _boxAdditive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
                               Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                _boxStart = point;
                _boxRect = new Rect(point, point);
                CaptureMouse();
                eventArgs.Handled = true;
            }
        }
        InvalidateVisual();
    }

    private void OnMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (_viewModel is null) return;
        var point = eventArgs.GetPosition(this);
        if (_boxSelecting)
        {
            if (eventArgs.LeftButton != MouseButtonState.Pressed) return;
            _boxRect = new Rect(_boxStart, point);
            InvalidateVisual();
            return;
        }
        if (!_pendingKeyDrag || eventArgs.LeftButton != MouseButtonState.Pressed) return;
        var target = Math.Clamp(_viewModel.TimelineFrameStart +
                                (int)Math.Floor((point.X - HeaderWidth) / CellWidth),
            _viewModel.TimelineFrameStart, _viewModel.TimelineFrameEnd);
        if (!_keyDragActive && Math.Abs(point.X - _dragOrigin.X) >= 4)
        {
            _viewModel.BeginEditTransaction("拖动轨道关键帧");
            _keyDragActive = true;
        }
        if (!_keyDragActive || target == _dragFrame) return;
        var moved = _viewModel.MoveTimelineKeys(_selectedKeys, target - _dragFrame);
        _selectedKeys.Clear();
        foreach (var key in moved) _selectedKeys.Add(key);
        _dragFrame = target;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_boxSelecting)
        {
            ApplyBoxSelection();
            _boxSelecting = false;
            Cursor = Cursors.Arrow;
            if (IsMouseCaptured) ReleaseMouseCapture();
            InvalidateVisual();
            return;
        }
        if (!_pendingKeyDrag) return;
        if (_keyDragActive) _viewModel?.EndEditTransaction();
        _pendingKeyDrag = false;
        _keyDragActive = false;
        ReleaseMouseCapture();
    }

    private void OnKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (_viewModel is null) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && eventArgs.Key == Key.C)
        {
            CopySelectedKeyframes();
            eventArgs.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && eventArgs.Key == Key.V)
        {
            PasteCopiedKeyframes();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key is Key.Delete or Key.Back)
        {
            if (_selectedKeys.Count > 0)
            {
                _viewModel.DeleteTimelineKeys(_selectedKeys);
                _selectedKeys.Clear();
            }
            else _viewModel.DeleteCurrentKeyframe();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Escape && _boxSelecting)
        {
            _boxSelecting = false;
            Cursor = Cursors.Arrow;
            if (IsMouseCaptured) ReleaseMouseCapture();
            InvalidateVisual();
            eventArgs.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && eventArgs.Key is Key.Left or Key.Right)
        {
            var offset = eventArgs.Key == Key.Left ? -1 : 1;
            if (_selectedKeys.Count > 0)
            {
                var moved = _viewModel.MoveTimelineKeys(_selectedKeys, offset);
                _selectedKeys.Clear();
                foreach (var key in moved) _selectedKeys.Add(key);
            }
            else _viewModel.NudgeCurrentKeyframe(offset);
            eventArgs.Handled = true;
        }
    }

    private void ApplyBoxSelection()
    {
        if (_viewModel is null) return;
        if (!_boxAdditive) _selectedKeys.Clear();
        var tracks = _viewModel.TimelineTracks;
        for (var row = 0; row < tracks.Count; row++)
        {
            var track = tracks[row];
            for (var frame = _viewModel.TimelineFrameStart; frame <= _viewModel.TimelineFrameEnd; frame++)
            {
                if (!_viewModel.IsMeaningfulKey(track, frame)) continue;
                var localFrame = frame - _viewModel.TimelineFrameStart;
                var center = new Point(HeaderWidth + localFrame * CellWidth + CellWidth / 2,
                    row * RowHeight + RowHeight / 2 + 2);
                if (_boxRect.Contains(center)) _selectedKeys.Add(new TimelineKeySelection(track.EditorId, frame));
            }
        }
        var first = _selectedKeys.FirstOrDefault();
        var selectedTrack = tracks.FirstOrDefault(track => track.EditorId == first.TrackId);
        if (selectedTrack is not null)
        {
            _viewModel.SelectedTrack = selectedTrack;
            _viewModel.CurrentFrame = first.Frame;
        }
    }

    private void UpdateExtent()
    {
        if (_viewModel is null) return;
        Width = Math.Max(500, HeaderWidth + Math.Max(1, _viewModel.TimelineFrameCount) * CellWidth);
        Height = Math.Max(120, _viewModel.TimelineTracks.Count * RowHeight);
        InvalidateVisual();
    }

    private void OnVisualStateChanged(object? sender, EventArgs eventArgs)
    {
        if (_viewModel is not null)
            _selectedKeys.RemoveWhere(key =>
            {
                var track = _viewModel.Project.Animation.Tracks.FirstOrDefault(item => item.EditorId == key.TrackId);
                return track is null || !_viewModel.IsMeaningfulKey(track, key.Frame);
            });
        UpdateExtent();
    }
}
