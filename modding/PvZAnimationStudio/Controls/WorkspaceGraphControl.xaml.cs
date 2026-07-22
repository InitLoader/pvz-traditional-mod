using System.Windows;
using System.Windows.Controls;
using PvZAnimationStudio.Models;
using PvZAnimationStudio.ViewModels;

namespace PvZAnimationStudio.Controls;

public partial class WorkspaceGraphControl : UserControl
{
    private bool _updating;

    public WorkspaceGraphControl()
    {
        InitializeComponent();
        Graph.SelectionChanged += (_, _) => UpdateSelectors();
    }

    public void Bind(EditorViewModel viewModel)
    {
        DataContext = viewModel;
        Graph.Bind(viewModel);
        UpdateSelectors();
    }

    public void Unbind()
    {
        Graph.Unbind();
        DataContext = null;
    }

    private void UpdateSelectors()
    {
        _updating = true;
        SelectTag(InterpolationCombo, Graph.SelectedInterpolation?.ToString());
        SelectTag(HandleCombo, Graph.SelectedHandleMode?.ToString());
        _updating = false;
    }

    private static void SelectTag(ComboBox comboBox, string? tag)
    {
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
    }

    private void MoveLeft_Click(object sender, RoutedEventArgs eventArgs) => Graph.NudgeSelected(-1);
    private void MoveRight_Click(object sender, RoutedEventArgs eventArgs) => Graph.NudgeSelected(1);
    private void Delete_Click(object sender, RoutedEventArgs eventArgs) => Graph.DeleteSelected();
    private void FitAll_Click(object sender, RoutedEventArgs eventArgs) => Graph.FitAll();

    private void InterpolationCombo_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_updating || InterpolationCombo.SelectedItem is not ComboBoxItem item ||
            !Enum.TryParse<CurveInterpolationMode>(item.Tag?.ToString(), out var mode)) return;
        Graph.SetSelectedInterpolation(mode);
    }

    private void HandleCombo_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_updating || HandleCombo.SelectedItem is not ComboBoxItem item ||
            !Enum.TryParse<CurveHandleMode>(item.Tag?.ToString(), out var mode)) return;
        Graph.SetSelectedHandleMode(mode);
    }
}
