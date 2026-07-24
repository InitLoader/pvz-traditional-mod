using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PvZAnimationStudio.Models;
using PvZAnimationStudio.Services;
using PvZAnimationStudio.ViewModels;

namespace PvZAnimationStudio.Controls;

public partial class WorkspaceBrowserControl : UserControl
{
    private EditorViewModel? _viewModel;

    public event EventHandler? ImportImagesRequested;
    public event EventHandler? ChooseGameRootRequested;

    public WorkspaceBrowserControl()
    {
        InitializeComponent();
        KindCombo.ItemsSource = Enum.GetValues<EntityKind>();
        OutputFormatCombo.ItemsSource = Enum.GetValues<AnimationOutputFormat>();
        Loaded += (_, _) => { if (ActionTemplateCombo.SelectedIndex < 0) ActionTemplateCombo.SelectedIndex = 0; };
    }

    public void Bind(EditorViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public void RefreshImageBindings() => ImageBindingsList.Items.Refresh();

    private void AddTrack_Click(object sender, RoutedEventArgs eventArgs) => _viewModel?.AddTrack("新部件");
    private void RemoveTrack_Click(object sender, RoutedEventArgs eventArgs) => _viewModel?.RemoveSelectedTrack();
    private void TrackVisibility_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel is not null && sender is FrameworkElement { Tag: AnimationTrack track })
            _viewModel.ToggleTrackEditorVisibility(track);
        eventArgs.Handled = true;
    }
    private void TrackLock_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel is not null && sender is FrameworkElement { Tag: AnimationTrack track })
            _viewModel.ToggleTrackEditorLock(track);
        eventArgs.Handled = true;
    }
    private void FullTimeline_Click(object sender, RoutedEventArgs eventArgs) => _viewModel?.ClearActionView();
    private void RemoveAction_Click(object sender, RoutedEventArgs eventArgs) => _viewModel?.RemoveSelectedAction();
    private void InferActions_Click(object sender, RoutedEventArgs eventArgs) => _viewModel?.InferActions();

    private void AddAction_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel is not null && ActionTemplateCombo.SelectedItem is ActionTemplate template)
            _viewModel.AddAction(template);
    }

    private void ImportImages_Click(object sender, RoutedEventArgs eventArgs) =>
        ImportImagesRequested?.Invoke(this, EventArgs.Empty);

    private void ChooseGameRoot_Click(object sender, RoutedEventArgs eventArgs) =>
        ChooseGameRootRequested?.Invoke(this, EventArgs.Empty);

    private void ImageBindingsList_MouseDoubleClick(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_viewModel is not null && ImageBindingsList.SelectedItem is KeyValuePair<string, string> selected)
            _viewModel.CurrentImage = selected.Key;
    }
}
