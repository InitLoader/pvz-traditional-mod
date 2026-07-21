using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public sealed class WorkspaceLayoutPresetService
{
    public WorkspaceLayoutState Create(WorkspacePreset preset)
    {
        if (preset == WorkspacePreset.Animation || preset == WorkspacePreset.Custom)
            return WorkspaceLayoutState.CreateDefault();
        return new WorkspaceLayoutState
        {
            Root = preset switch
        {
            WorkspacePreset.DualView => SplitVertical(
                SplitHorizontal(Leaf(WorkspaceEditorKind.AnimationView), Leaf(WorkspaceEditorKind.AnimationView), 0.5),
                Leaf(WorkspaceEditorKind.Timeline), 0.72),
            WorkspacePreset.DualTimeline => SplitVertical(
                Leaf(WorkspaceEditorKind.AnimationView),
                SplitHorizontal(Leaf(WorkspaceEditorKind.Timeline), Leaf(WorkspaceEditorKind.Timeline), 0.5), 0.58),
            WorkspacePreset.Focus => SplitHorizontal(
                Leaf(WorkspaceEditorKind.AnimationView), Leaf(WorkspaceEditorKind.Inspector), 0.78),
            _ => WorkspaceLayoutState.CreateDefault().Root
        }
        };
    }

    public static WorkspaceLayoutNode Leaf(WorkspaceEditorKind editor) => new() { Editor = editor };

    public static WorkspaceLayoutNode SplitHorizontal(
        WorkspaceLayoutNode first, WorkspaceLayoutNode second, double ratio) => new()
    {
        Split = WorkspaceSplitDirection.Horizontal,
        Ratio = Math.Clamp(ratio, 0.1, 0.9),
        First = first,
        Second = second
    };

    public static WorkspaceLayoutNode SplitVertical(
        WorkspaceLayoutNode first, WorkspaceLayoutNode second, double ratio) => new()
    {
        Split = WorkspaceSplitDirection.Vertical,
        Ratio = Math.Clamp(ratio, 0.1, 0.9),
        First = first,
        Second = second
    };
}
