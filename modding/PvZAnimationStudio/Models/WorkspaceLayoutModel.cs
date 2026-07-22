namespace PvZAnimationStudio.Models;

public enum WorkspaceEditorKind
{
    AnimationView,
    Timeline,
    GraphEditor,
    Browser,
    Inspector
}

public enum WorkspaceSplitDirection
{
    Horizontal,
    Vertical
}

public enum WorkspacePreset
{
    Custom,
    Animation,
    DualView,
    DualTimeline,
    GraphEditing,
    Focus
}

public sealed class WorkspaceLayoutNode
{
    public WorkspaceEditorKind Editor { get; set; } = WorkspaceEditorKind.AnimationView;
    public WorkspaceSplitDirection? Split { get; set; }
    public double Ratio { get; set; } = 0.5;
    public WorkspaceLayoutNode? First { get; set; }
    public WorkspaceLayoutNode? Second { get; set; }

    public bool IsLeaf => Split is null || First is null || Second is null;

    public WorkspaceLayoutNode Clone() => new()
    {
        Editor = Editor,
        Split = Split,
        Ratio = Ratio,
        First = First?.Clone(),
        Second = Second?.Clone()
    };
}

public sealed class WorkspaceLayoutState
{
    public int Version { get; set; } = 1;
    public WorkspaceLayoutNode Root { get; set; } = new();

    public WorkspaceLayoutState Clone() => new() { Version = Version, Root = Root.Clone() };

    public static WorkspaceLayoutState CreateDefault() => new()
    {
        Root = new WorkspaceLayoutNode
        {
            Split = WorkspaceSplitDirection.Vertical,
            Ratio = 0.72,
            First = new WorkspaceLayoutNode
            {
                Split = WorkspaceSplitDirection.Horizontal,
                Ratio = 0.18,
                First = new WorkspaceLayoutNode { Editor = WorkspaceEditorKind.Browser },
                Second = new WorkspaceLayoutNode
                {
                    Split = WorkspaceSplitDirection.Horizontal,
                    Ratio = 0.76,
                    First = new WorkspaceLayoutNode { Editor = WorkspaceEditorKind.AnimationView },
                    Second = new WorkspaceLayoutNode { Editor = WorkspaceEditorKind.Inspector }
                }
            },
            Second = new WorkspaceLayoutNode { Editor = WorkspaceEditorKind.Timeline }
        }
    };
}

public sealed class ImageLayoutDefinition
{
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
}
