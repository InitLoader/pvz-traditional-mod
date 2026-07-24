using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PvZAnimationStudio.ViewModels;

namespace PvZAnimationStudio.Controls;

public sealed class TimelineControl : FrameworkElement
{
    private const double HeaderWidth = 260;
    private const double CellWidth = 14;
    private const double RowHeight = 26;
    private const double RulerHeight = 28;
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
    private int _lastTrackCount = -1;
    private int _lastFrameCount = -1;
    private ScrollViewer? _scrollViewer;

    public TimelineControl()
    {
        Focusable = true;
        Cursor = Cursors.Arrow;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        KeyDown += OnKeyDown;
        Loaded += (_, _) => AttachScrollViewer();
        Unloaded += (_, _) => DetachScrollViewer();
    }

    public void Bind(EditorViewModel viewModel)
    {
        if (_viewModel is not null) _viewModel.VisualStateChanged -= OnVisualStateChanged;
        _viewModel = viewModel;
        _viewModel.VisualStateChanged += OnVisualStateChanged;
        if (IsLoaded) AttachScrollViewer();
        UpdateExtent();
    }

    public void Unbind()
    {
        if (_viewModel is not null) _viewModel.VisualStateChanged -= OnVisualStateChanged;
        DetachScrollViewer();
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
        var viewport = GetVisibleBounds();
        var firstRow = Math.Clamp((int)Math.Floor((viewport.Top - RulerHeight) / RowHeight), 0,
            Math.Max(0, tracks.Count - 1));
        var lastRow = Math.Clamp((int)Math.Ceiling((viewport.Bottom - RulerHeight) / RowHeight), 0,
            Math.Max(0, tracks.Count - 1));
        var firstLocalFrame = Math.Clamp((int)Math.Floor((viewport.Left - HeaderWidth) / CellWidth) - 1, 0,
            Math.Max(0, rangeEnd - rangeStart));
        var lastLocalFrame = Math.Clamp((int)Math.Ceiling((viewport.Right - HeaderWidth) / CellWidth) + 1, 0,
            Math.Max(0, rangeEnd - rangeStart));

        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(38, 44, 52)), null,
            new Rect(viewport.Left, 0, Math.Max(0, viewport.Width), RulerHeight));
        if (viewport.Left < HeaderWidth)
        {
            var rulerTitle = new FormattedText("显示  锁定  图片 / 轨道",
                CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 10,
                new SolidColorBrush(Color.FromRgb(180, 193, 205)), dpi);
            context.DrawText(rulerTitle, new Point(8, 7));
        }
        var fps = _viewModel.EffectivePlaybackRate;
        for (var localFrame = firstLocalFrame; localFrame <= lastLocalFrame; localFrame++)
        {
            var x = HeaderWidth + localFrame * CellWidth;
            var seconds = localFrame / fps;
            var roundedSecond = Math.Round(seconds);
            var major = Math.Abs(seconds - roundedSecond) <= 0.5 / fps;
            var minorStep = Math.Max(1, (int)Math.Round(fps / 4f));
            if (!major && localFrame % minorStep != 0) continue;
            var pen = major
                ? new Pen(new SolidColorBrush(Color.FromRgb(101, 117, 133)), 1)
                : gridPen;
            context.DrawLine(pen, new Point(x, major ? 13 : 20),
                new Point(x, RulerHeight + tracks.Count * RowHeight));
            if (!major) continue;
            var secondLabel = new FormattedText($"{roundedSecond:0}s",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10,
                new SolidColorBrush(Color.FromRgb(205, 216, 226)), dpi);
            context.DrawText(secondLabel, new Point(x + 2, 2));
        }
        context.DrawLine(gridPen, new Point(viewport.Left, RulerHeight), new Point(viewport.Right, RulerHeight));
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(77, 88, 101)), 1),
            new Point(HeaderWidth, 0), new Point(HeaderWidth, ActualHeight));

        if (tracks.Count == 0) return;
        for (var row = firstRow; row <= lastRow; row++)
        {
            var track = tracks[row];
            var y = RulerHeight + row * RowHeight;
            var selected = ReferenceEquals(track, _viewModel.SelectedTrack);
            var background = selected
                ? new SolidColorBrush(Color.FromRgb(50, 74, 62))
                : track.IsActionTrack
                    ? new SolidColorBrush(Color.FromRgb(48, 40, 62))
                    : new SolidColorBrush(row % 2 == 0 ? Color.FromRgb(30, 35, 42) : Color.FromRgb(27, 32, 39));
            context.DrawRectangle(background, null, new Rect(viewport.Left, y, viewport.Width, RowHeight));
            DrawVisibilityIcon(context, new Point(16, y + RowHeight / 2), track.IsVisibleInEditor);
            DrawLockIcon(context, new Point(45, y + RowHeight / 2), track.IsLockedInEditor);
            if (track.EditorThumbnail is not null)
                context.DrawImage(track.EditorThumbnail, new Rect(62, y + 2, 22, 22));
            else if (track.IsGroundTrack)
                DrawGroundIcon(context, new Point(72, y + RowHeight / 2));
            else
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(45, 52, 61)), gridPen,
                    new Rect(62, y + 3, 20, 20));
            var label = new FormattedText(
                track.IsActionTrack ? $"动作范围  {track.Name}" : track.EditorDisplayName,
                CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), 11,
                track.IsActionTrack ? Brushes.Plum : track.IsGroundTrack ? Brushes.DeepSkyBlue : Brushes.White, dpi)
            { MaxTextWidth = HeaderWidth - 96, Trimming = TextTrimming.CharacterEllipsis };
            context.DrawText(label, new Point(91, y + 5));
            context.DrawLine(gridPen, new Point(viewport.Left, y + RowHeight), new Point(viewport.Right, y + RowHeight));

            foreach (var absoluteFrame in _viewModel.GetMeaningfulKeyFrames(track))
            {
                if (absoluteFrame < rangeStart + firstLocalFrame || absoluteFrame > rangeStart + lastLocalFrame) continue;
                var localFrame = absoluteFrame - rangeStart;
                var x = HeaderWidth + localFrame * CellWidth;
                var keySelected = _selectedKeys.Contains(new TimelineKeySelection(track.EditorId, absoluteFrame));
                DrawDiamond(context, new Point(x + CellWidth / 2, y + RowHeight / 2 + 2),
                    keySelected ? Brushes.Orange : track.IsLockedInEditor ? Brushes.Gray :
                    selected ? Brushes.Gold : track.IsActionTrack ? Brushes.Plum : Brushes.LightGreen);
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

    private static void DrawGroundIcon(DrawingContext context, Point center)
    {
        var brush = new SolidColorBrush(Color.FromRgb(77, 200, 255));
        var pen = new Pen(brush, 1.8);
        context.DrawLine(pen, new Point(center.X - 9, center.Y), new Point(center.X + 9, center.Y));
        context.DrawLine(pen, new Point(center.X - 9, center.Y), new Point(center.X - 5, center.Y - 4));
        context.DrawLine(pen, new Point(center.X - 9, center.Y), new Point(center.X - 5, center.Y + 4));
        context.DrawLine(pen, new Point(center.X + 9, center.Y), new Point(center.X + 5, center.Y - 4));
        context.DrawLine(pen, new Point(center.X + 9, center.Y), new Point(center.X + 5, center.Y + 4));
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

    private static void DrawLockIcon(DrawingContext context, Point center, bool locked)
    {
        var color = locked ? Color.FromRgb(245, 194, 86) : Color.FromRgb(112, 124, 137);
        var pen = new Pen(new SolidColorBrush(color), 1.4);
        context.DrawRoundedRectangle(locked ? new SolidColorBrush(Color.FromRgb(95, 73, 35)) : null,
            pen, new Rect(center.X - 6, center.Y - 1, 12, 9), 1.5, 1.5);
        var arc = new StreamGeometry();
        using (var writer = arc.Open())
        {
            writer.BeginFigure(new Point(center.X - 4, center.Y - 1), false, false);
            writer.BezierTo(new Point(center.X - 4, center.Y - 8), new Point(center.X + 4, center.Y - 8),
                new Point(center.X + 4, center.Y - 1), true, false);
        }
        arc.Freeze();
        context.DrawGeometry(null, pen, arc);
        if (!locked)
            context.DrawLine(pen, new Point(center.X + 4, center.Y - 1), new Point(center.X + 7, center.Y - 4));
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_viewModel is null) return;
        Focus();
        var tracks = _viewModel.TimelineTracks;
        var point = eventArgs.GetPosition(this);
        if (point.Y < RulerHeight && point.X >= HeaderWidth)
        {
            var rulerFrame = (int)((point.X - HeaderWidth) / CellWidth);
            _viewModel.CurrentFrame = Math.Clamp(_viewModel.TimelineFrameStart + rulerFrame,
                _viewModel.TimelineFrameStart, _viewModel.TimelineFrameEnd);
            eventArgs.Handled = true;
            return;
        }
        var row = (int)((point.Y - RulerHeight) / RowHeight);
        if (row < 0 || row >= tracks.Count) return;
        _viewModel.SelectedTrack = tracks[row];
        if (eventArgs.ChangedButton == MouseButton.Left && point.X < 30)
        {
            _viewModel.ToggleTrackEditorVisibility(tracks[row]);
            eventArgs.Handled = true;
            return;
        }
        if (eventArgs.ChangedButton == MouseButton.Left && point.X < 59)
        {
            _viewModel.ToggleTrackEditorLock(tracks[row]);
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
            var hasEditableKey = _selectedKeys.Any(key => _viewModel.Project.Animation.Tracks
                .FirstOrDefault(track => track.EditorId == key.TrackId) is { IsLockedInEditor: false });
            if (!hasEditableKey)
            {
                _pendingKeyDrag = false;
                if (IsMouseCaptured) ReleaseMouseCapture();
                return;
            }
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
        var firstRow = Math.Clamp((int)Math.Floor((_boxRect.Top - RulerHeight) / RowHeight), 0,
            Math.Max(0, tracks.Count - 1));
        var lastRow = Math.Clamp((int)Math.Floor((_boxRect.Bottom - RulerHeight) / RowHeight), 0,
            Math.Max(0, tracks.Count - 1));
        for (var row = firstRow; row <= lastRow; row++)
        {
            var track = tracks[row];
            foreach (var frame in _viewModel.GetMeaningfulKeyFrames(track))
            {
                if (frame < _viewModel.TimelineFrameStart || frame > _viewModel.TimelineFrameEnd) continue;
                var localFrame = frame - _viewModel.TimelineFrameStart;
                var center = new Point(HeaderWidth + localFrame * CellWidth + CellWidth / 2,
                    RulerHeight + row * RowHeight + RowHeight / 2 + 2);
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
        var trackCount = _viewModel.TimelineTracks.Count;
        var frameCount = _viewModel.TimelineFrameCount;
        if (_lastTrackCount == trackCount && _lastFrameCount == frameCount)
        {
            InvalidateVisual();
            return;
        }
        _lastTrackCount = trackCount;
        _lastFrameCount = frameCount;
        Width = Math.Max(500, HeaderWidth + Math.Max(1, frameCount) * CellWidth);
        Height = Math.Max(120, RulerHeight + trackCount * RowHeight);
        InvalidateVisual();
    }

    private Rect GetVisibleBounds()
    {
        var viewer = _scrollViewer ?? FindScrollViewer();
        if (viewer is not null)
            return new Rect(viewer.HorizontalOffset, viewer.VerticalOffset,
                Math.Max(1, viewer.ViewportWidth), Math.Max(1, viewer.ViewportHeight));
        return new Rect(RenderSize);
    }

    private void AttachScrollViewer()
    {
        var viewer = FindScrollViewer();
        if (ReferenceEquals(_scrollViewer, viewer)) return;
        DetachScrollViewer();
        _scrollViewer = viewer;
        if (_scrollViewer is not null) _scrollViewer.ScrollChanged += OnScrollChanged;
    }

    private void DetachScrollViewer()
    {
        if (_scrollViewer is not null) _scrollViewer.ScrollChanged -= OnScrollChanged;
        _scrollViewer = null;
    }

    private ScrollViewer? FindScrollViewer()
    {
        DependencyObject? current = this;
        while ((current = VisualTreeHelper.GetParent(current)) is not null)
            if (current is ScrollViewer viewer) return viewer;
        return null;
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs eventArgs)
    {
        // The timeline deliberately renders only the visible rows/frames. A
        // ScrollViewer changes its offsets without changing this element's
        // model, so explicitly redraw the newly exposed area immediately.
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
