using System.Windows;
using System.Windows.Controls;
using PvZAnimationStudio.ViewModels;

namespace PvZAnimationStudio.Controls;

public partial class WorkspaceTimelineControl : UserControl
{
    private EditorViewModel? _viewModel;

    public WorkspaceTimelineControl() => InitializeComponent();

    public void Bind(EditorViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Timeline.Bind(viewModel);
    }

    public void Unbind()
    {
        Timeline.Unbind();
        _viewModel = null;
        DataContext = null;
    }

    private void FirstFrame_Click(object sender, RoutedEventArgs eventArgs) { if (_viewModel is not null) _viewModel.CurrentFrame = _viewModel.TimelineFrameStart; }
    private void PreviousFrame_Click(object sender, RoutedEventArgs eventArgs) { if (_viewModel is not null) _viewModel.CurrentFrame--; }
    private void NextFrame_Click(object sender, RoutedEventArgs eventArgs) { if (_viewModel is not null) _viewModel.CurrentFrame++; }
    private void LastFrame_Click(object sender, RoutedEventArgs eventArgs) { if (_viewModel is not null) _viewModel.CurrentFrame = _viewModel.TimelineFrameEnd; }
    private void InsertFrame_Click(object sender, RoutedEventArgs eventArgs) => _viewModel?.InsertFrame();
    private void DeleteFrame_Click(object sender, RoutedEventArgs eventArgs) => _viewModel?.DeleteFrame();
    private void MoveKeyLeft_Click(object sender, RoutedEventArgs eventArgs) => _viewModel?.NudgeCurrentKeyframe(-1);
    private void MoveKeyRight_Click(object sender, RoutedEventArgs eventArgs) => _viewModel?.NudgeCurrentKeyframe(1);
    private void DeleteKey_Click(object sender, RoutedEventArgs eventArgs) => _viewModel?.DeleteCurrentKeyframe();
    private void FullTimeline_Click(object sender, RoutedEventArgs eventArgs) => _viewModel?.ClearActionView();
}
