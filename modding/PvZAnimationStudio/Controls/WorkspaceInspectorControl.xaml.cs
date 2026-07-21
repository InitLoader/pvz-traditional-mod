using System.Windows;
using System.Windows.Controls;
using PvZAnimationStudio.Models;
using PvZAnimationStudio.ViewModels;

namespace PvZAnimationStudio.Controls;

public partial class WorkspaceInspectorControl : UserControl
{
    private EditorViewModel? _viewModel;

    public WorkspaceInspectorControl()
    {
        InitializeComponent();
        LoopCombo.ItemsSource = Enum.GetValues<AnimationLoopMode>();
    }

    public void Bind(EditorViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void SetKey_Click(object sender, RoutedEventArgs eventArgs) => _viewModel?.SetKeyframe();
    private void ClearKey_Click(object sender, RoutedEventArgs eventArgs) => _viewModel?.ClearKeyframe();
}
