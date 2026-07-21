using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Controls;

public sealed class WorkspacePaneControl : Border
{
    private sealed record EditorChoice(WorkspaceEditorKind Kind, string Name)
    {
        public override string ToString() => Name;
    }

    private static readonly EditorChoice[] Choices =
    [
        new(WorkspaceEditorKind.AnimationView, "动画视图"),
        new(WorkspaceEditorKind.Timeline, "轨道时间轴"),
        new(WorkspaceEditorKind.Browser, "资源与工程"),
        new(WorkspaceEditorKind.Inspector, "属性检查器")
    ];

    private readonly ContentControl _content = new();
    private readonly ComboBox _editorSelector;
    private readonly Func<WorkspaceEditorKind, FrameworkElement> _contentFactory;
    private bool _initializing = true;
    private Vector _splitDrag;

    public WorkspaceEditorKind EditorKind { get; private set; }
    public event Action<WorkspaceEditorKind>? EditorChanged;
    public event Action<WorkspaceSplitDirection>? SplitRequested;
    public event Action<WorkspaceEditorKind>? FloatingWindowRequested;
    public event Action? CloseRequested;

    public WorkspacePaneControl(WorkspaceEditorKind editorKind, Func<WorkspaceEditorKind, FrameworkElement> contentFactory)
    {
        _contentFactory = contentFactory;
        EditorKind = editorKind;
        Background = new SolidColorBrush(Color.FromRgb(24, 29, 36));
        BorderBrush = new SolidColorBrush(Color.FromRgb(65, 76, 88));
        BorderThickness = new Thickness(1);
        MinWidth = 150;
        MinHeight = 100;

        var root = new DockPanel();
        var header = new DockPanel
        {
            Height = 31,
            Background = new SolidColorBrush(Color.FromRgb(38, 46, 56)),
            LastChildFill = true
        };
        DockPanel.SetDock(header, Dock.Top);

        var commands = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(commands, Dock.Right);
        commands.Children.Add(CreateHeaderButton("↔", "左右拆分区域", Cursors.SizeWE,
            () => SplitRequested?.Invoke(WorkspaceSplitDirection.Horizontal)));
        commands.Children.Add(CreateHeaderButton("↕", "上下拆分区域", Cursors.SizeNS,
            () => SplitRequested?.Invoke(WorkspaceSplitDirection.Vertical)));
        commands.Children.Add(CreateHeaderButton("↗", "在独立窗口打开同类区域", Cursors.Arrow,
            () => FloatingWindowRequested?.Invoke(EditorKind)));
        commands.Children.Add(CreateHeaderButton("×", "关闭此区域", Cursors.Arrow,
            () => CloseRequested?.Invoke()));

        var cornerGrip = new WorkspaceSplitGrip
        {
            Width = 15,
            Height = 25,
            Margin = new Thickness(2, 2, 3, 2),
            Cursor = Cursors.SizeNWSE,
            ToolTip = "拖动角落拆分：横向移动生成左右区域，纵向移动生成上下区域"
        };
        cornerGrip.DragStarted += (_, _) => _splitDrag = default;
        cornerGrip.DragDelta += (_, args) => _splitDrag += new Vector(args.HorizontalChange, args.VerticalChange);
        cornerGrip.DragCompleted += (_, _) =>
        {
            if (_splitDrag.Length < 6) return;
            SplitRequested?.Invoke(Math.Abs(_splitDrag.X) >= Math.Abs(_splitDrag.Y)
                ? WorkspaceSplitDirection.Horizontal
                : WorkspaceSplitDirection.Vertical);
        };
        commands.Children.Add(cornerGrip);

        _editorSelector = new ComboBox
        {
            MinWidth = 135,
            HorizontalAlignment = HorizontalAlignment.Left,
            Foreground = new SolidColorBrush(Color.FromRgb(238, 243, 246)),
            Background = new SolidColorBrush(Color.FromRgb(41, 51, 62)),
            ToolTip = "切换此区域的编辑器类型"
        };
        foreach (var choice in Choices)
        {
            _editorSelector.Items.Add(new ComboBoxItem
            {
                Tag = choice.Kind,
                Content = new TextBlock
                {
                    Text = choice.Name,
                    Foreground = new SolidColorBrush(Color.FromRgb(238, 243, 246))
                }
            });
        }
        _editorSelector.SelectedItem = _editorSelector.Items.OfType<ComboBoxItem>()
            .First(item => item.Tag is WorkspaceEditorKind kind && kind == editorKind);
        _editorSelector.SelectionChanged += (_, _) =>
        {
            if (_initializing || _editorSelector.SelectedItem is not ComboBoxItem item ||
                item.Tag is not WorkspaceEditorKind selected) return;
            SetEditor(selected, true);
        };

        header.Children.Add(commands);
        header.Children.Add(_editorSelector);
        root.Children.Add(header);
        root.Children.Add(_content);
        Child = root;
        SetEditor(editorKind, false);
        _initializing = false;
    }

    private void SetEditor(WorkspaceEditorKind editorKind, bool notify)
    {
        EditorKind = editorKind;
        _content.Content = _contentFactory(editorKind);
        if (notify) EditorChanged?.Invoke(editorKind);
    }

    private static Button CreateHeaderButton(string text, string toolTip, Cursor cursor, Action action)
    {
        var button = new Button
        {
            Content = text,
            Width = 29,
            Height = 25,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 2, 1, 2),
            Cursor = cursor,
            ToolTip = toolTip,
            FontSize = 13
        };
        button.Click += (_, _) => action();
        return button;
    }
}

public sealed class WorkspaceSplitGrip : Thumb
{
    private static readonly Brush GripBackground = new SolidColorBrush(Color.FromRgb(37, 47, 57));
    private static readonly Pen GripPen = new(new SolidColorBrush(Color.FromRgb(91, 190, 139)), 1.3);

    public WorkspaceSplitGrip()
    {
        Focusable = false;
        Template = new ControlTemplate(typeof(Thumb));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRoundedRectangle(GripBackground, null, new Rect(0, 0, ActualWidth, ActualHeight), 3, 3);
        for (var offset = 0; offset < 3; offset++)
        {
            var shift = offset * 4;
            drawingContext.DrawLine(GripPen,
                new Point(Math.Max(2, ActualWidth - 11 + shift), ActualHeight - 3),
                new Point(ActualWidth - 3, Math.Max(3, ActualHeight - 11 + shift)));
        }
    }
}
