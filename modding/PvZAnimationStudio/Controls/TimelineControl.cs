using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PvZAnimationStudio.ViewModels;

namespace PvZAnimationStudio.Controls;

public sealed class TimelineControl : FrameworkElement
{
    private const double HeaderWidth = 180;
    private const double CellWidth = 14;
    private const double RowHeight = 26;
    private EditorViewModel? _viewModel;

    public TimelineControl()
    {
        MouseDown += OnMouseDown;
    }

    public void Bind(EditorViewModel viewModel)
    {
        if (_viewModel is not null) _viewModel.VisualStateChanged -= OnVisualStateChanged;
        _viewModel = viewModel;
        _viewModel.VisualStateChanged += OnVisualStateChanged;
        UpdateExtent();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 28, 34)), null, new Rect(RenderSize));
        if (_viewModel is null) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(56, 64, 75)), 1);
        for (var row = 0; row < _viewModel.Tracks.Count; row++)
        {
            var track = _viewModel.Tracks[row];
            var y = row * RowHeight;
            var selected = ReferenceEquals(track, _viewModel.SelectedTrack);
            var background = selected
                ? new SolidColorBrush(Color.FromRgb(53, 70, 60))
                : track.IsActionTrack
                    ? new SolidColorBrush(Color.FromRgb(43, 39, 55))
                    : new SolidColorBrush(Color.FromRgb(30, 35, 42));
            context.DrawRectangle(background, null, new Rect(0, y, ActualWidth, RowHeight));
            var label = new FormattedText(
                track.IsActionTrack ? $"动作  {track.Name}" : track.Name,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), 11,
                track.IsActionTrack ? Brushes.Plum : Brushes.White, dpi)
            { MaxTextWidth = HeaderWidth - 10, Trimming = TextTrimming.CharacterEllipsis };
            context.DrawText(label, new Point(6, y + 5));
            context.DrawLine(gridPen, new Point(0, y + RowHeight), new Point(ActualWidth, y + RowHeight));
            for (var frame = 0; frame < track.Frames.Count; frame++)
            {
                var x = HeaderWidth + frame * CellWidth;
                if (frame % 5 == 0)
                    context.DrawLine(gridPen, new Point(x, y), new Point(x, y + RowHeight));
                if (!track.Frames[frame].HasKey) continue;
                var center = new Point(x + CellWidth / 2, y + RowHeight / 2);
                var geometry = new StreamGeometry();
                using (var geometryContext = geometry.Open())
                {
                    geometryContext.BeginFigure(new Point(center.X, center.Y - 5), true, true);
                    geometryContext.LineTo(new Point(center.X + 5, center.Y), true, false);
                    geometryContext.LineTo(new Point(center.X, center.Y + 5), true, false);
                    geometryContext.LineTo(new Point(center.X - 5, center.Y), true, false);
                }
                geometry.Freeze();
                context.DrawGeometry(selected ? Brushes.Gold : Brushes.LightGreen, null, geometry);
            }
        }

        var playheadX = HeaderWidth + _viewModel.CurrentFrame * CellWidth + CellWidth / 2;
        context.DrawLine(new Pen(Brushes.OrangeRed, 2), new Point(playheadX, 0), new Point(playheadX, ActualHeight));
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_viewModel is null) return;
        var point = eventArgs.GetPosition(this);
        var row = (int)(point.Y / RowHeight);
        if (row < 0 || row >= _viewModel.Tracks.Count) return;
        _viewModel.SelectedTrack = _viewModel.Tracks[row];
        if (point.X >= HeaderWidth)
        {
            var frame = (int)((point.X - HeaderWidth) / CellWidth);
            _viewModel.CurrentFrame = Math.Clamp(frame, 0, Math.Max(0, _viewModel.Project.Animation.FrameCount - 1));
            if (eventArgs.ClickCount >= 2) _viewModel.ToggleKeyframe();
        }
        InvalidateVisual();
    }

    private void UpdateExtent()
    {
        if (_viewModel is null) return;
        Width = Math.Max(500, HeaderWidth + Math.Max(1, _viewModel.Project.Animation.FrameCount) * CellWidth);
        Height = Math.Max(120, _viewModel.Tracks.Count * RowHeight);
        InvalidateVisual();
    }

    private void OnVisualStateChanged(object? sender, EventArgs eventArgs) => UpdateExtent();
}
