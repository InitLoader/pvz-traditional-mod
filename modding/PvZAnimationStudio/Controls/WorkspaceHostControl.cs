using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PvZAnimationStudio.Models;
using PvZAnimationStudio.Services;
using PvZAnimationStudio.ViewModels;

namespace PvZAnimationStudio.Controls;

public sealed class WorkspaceHostControl : Grid
{
    private readonly WorkspaceLayoutPresetService _presets = new();
    private readonly List<AnimationPreviewControl> _previews = [];
    private readonly List<WorkspaceTimelineControl> _timelines = [];
    private readonly List<WorkspaceBrowserControl> _browsers = [];
    private readonly List<Window> _floatingWindows = [];
    private EditorViewModel? _viewModel;
    private OriginalResourceService? _resources;
    private WorkspaceLayoutState _layout;

    public event EventHandler? ImportImagesRequested;
    public event EventHandler? ChooseGameRootRequested;
    public event EventHandler? LayoutChanged;

    public WorkspaceHostControl()
    {
        Background = new SolidColorBrush(Color.FromRgb(20, 25, 31));
        _layout = _presets.Create(WorkspacePreset.Animation);
    }

    public void Bind(EditorViewModel viewModel, OriginalResourceService resources)
    {
        _viewModel = viewModel;
        _resources = resources;
        Rebuild();
    }

    public void ApplyPreset(WorkspacePreset preset)
    {
        if (preset == WorkspacePreset.Custom) return;
        ApplyLayout(_presets.Create(preset));
    }

    public void OpenFloatingWindow(WorkspaceEditorKind editor) => CreateFloatingWindow(editor);

    public void ApplyLayout(WorkspaceLayoutState? layout)
    {
        _layout = (layout ?? _presets.Create(WorkspacePreset.Animation)).Clone();
        Rebuild();
    }

    public WorkspaceLayoutState ExportLayout() => _layout.Clone();
    public void FrameAllViews() { foreach (var preview in _previews) preview.FrameAll(); }
    public void ResetAllViews() { foreach (var preview in _previews) preview.ResetView(); }
    public void RefreshImageBindings() { foreach (var browser in _browsers) browser.RefreshImageBindings(); }

    public void Unbind()
    {
        foreach (var preview in _previews) preview.Unbind();
        foreach (var timeline in _timelines) timeline.Unbind();
        foreach (var window in _floatingWindows.ToArray()) window.Close();
        _previews.Clear();
        _timelines.Clear();
        _browsers.Clear();
        _viewModel = null;
        _resources = null;
        Children.Clear();
    }

    private void Rebuild()
    {
        foreach (var preview in _previews) preview.Unbind();
        foreach (var timeline in _timelines) timeline.Unbind();
        Children.Clear();
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();
        _previews.Clear();
        _timelines.Clear();
        _browsers.Clear();
        if (_viewModel is null || _resources is null) return;
        Children.Add(BuildNode(_layout.Root));
    }

    private FrameworkElement BuildNode(WorkspaceLayoutNode node)
    {
        if (node.IsLeaf)
        {
            var pane = new WorkspacePaneControl(node.Editor, CreateEditorContent);
            pane.EditorChanged += editor =>
            {
                node.Editor = editor;
                Rebuild();
                LayoutChanged?.Invoke(this, EventArgs.Empty);
            };
            pane.SplitRequested += direction => SplitLeaf(node, direction);
            pane.FloatingWindowRequested += OpenFloatingWindow;
            pane.CloseRequested += () => CloseLeaf(node);
            return pane;
        }

        var splitGrid = new Grid();
        var ratio = Math.Clamp(node.Ratio, 0.1, 0.9);
        var splitter = new GridSplitter
        {
            Background = new SolidColorBrush(Color.FromRgb(75, 88, 102)),
            ShowsPreview = false,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ResizeDirection = node.Split == WorkspaceSplitDirection.Horizontal
                ? GridResizeDirection.Columns
                : GridResizeDirection.Rows
        };
        splitter.DragCompleted += (_, _) =>
        {
            if (node.Split == WorkspaceSplitDirection.Horizontal)
            {
                var available = Math.Max(1, splitGrid.ActualWidth - 6);
                node.Ratio = Math.Clamp(splitGrid.ColumnDefinitions[0].ActualWidth / available, 0.1, 0.9);
            }
            else
            {
                var available = Math.Max(1, splitGrid.ActualHeight - 6);
                node.Ratio = Math.Clamp(splitGrid.RowDefinitions[0].ActualHeight / available, 0.1, 0.9);
            }
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        };

        var first = BuildNode(node.First!);
        var second = BuildNode(node.Second!);
        if (node.Split == WorkspaceSplitDirection.Horizontal)
        {
            splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star), MinWidth = 150 });
            splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - ratio, GridUnitType.Star), MinWidth = 150 });
            splitter.Width = 6;
            splitter.Cursor = Cursors.SizeWE;
            Grid.SetColumn(first, 0);
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(second, 2);
        }
        else
        {
            splitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ratio, GridUnitType.Star), MinHeight = 100 });
            splitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
            splitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1 - ratio, GridUnitType.Star), MinHeight = 100 });
            splitter.Height = 6;
            splitter.Cursor = Cursors.SizeNS;
            Grid.SetRow(first, 0);
            Grid.SetRow(splitter, 1);
            Grid.SetRow(second, 2);
        }
        splitGrid.Children.Add(first);
        splitGrid.Children.Add(splitter);
        splitGrid.Children.Add(second);
        return splitGrid;
    }

    private FrameworkElement CreateEditorContent(WorkspaceEditorKind editor)
    {
        if (_viewModel is null || _resources is null) return new Border();
        switch (editor)
        {
            case WorkspaceEditorKind.Timeline:
                var timeline = new WorkspaceTimelineControl();
                timeline.Bind(_viewModel);
                _timelines.Add(timeline);
                return timeline;
            case WorkspaceEditorKind.Browser:
                var browser = new WorkspaceBrowserControl();
                browser.Bind(_viewModel);
                browser.ImportImagesRequested += (_, _) => ImportImagesRequested?.Invoke(this, EventArgs.Empty);
                browser.ChooseGameRootRequested += (_, _) => ChooseGameRootRequested?.Invoke(this, EventArgs.Empty);
                _browsers.Add(browser);
                return browser;
            case WorkspaceEditorKind.Inspector:
                var inspector = new WorkspaceInspectorControl();
                inspector.Bind(_viewModel);
                return inspector;
            default:
                var preview = new AnimationPreviewControl();
                preview.Bind(_viewModel, _resources);
                preview.ExternalImagesDropped += (_, args) => ImportDroppedImages(args);
                _previews.Add(preview);
                return preview;
        }
    }

    private void SplitLeaf(WorkspaceLayoutNode node, WorkspaceSplitDirection direction)
    {
        if (!node.IsLeaf) return;
        var editor = node.Editor;
        node.Split = direction;
        node.Ratio = 0.5;
        node.First = WorkspaceLayoutPresetService.Leaf(editor);
        node.Second = WorkspaceLayoutPresetService.Leaf(editor);
        Rebuild();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ImportDroppedImages(ExternalImagesDroppedEventArgs args)
    {
        if (_viewModel is null || _resources is null || args.Files.Count == 0) return;
        _viewModel.BeginEditTransaction(args.Files.Count == 1 ? "拖入图片到动画" : $"拖入 {args.Files.Count} 张图片到动画");
        try
        {
            for (var index = 0; index < args.Files.Count; index++)
            {
                var file = args.Files[index];
                var symbol = _resources.ImportImage(_viewModel.Project, file);
                var resource = _resources.ResolveImage(_viewModel.Project, symbol)
                               ?? throw new InvalidDataException($"无法读取图片：{file}");
                var offset = index * 24;
                var x = (float)(args.WorldPosition.X - resource.CelWidth / 2 + offset);
                var y = (float)(args.WorldPosition.Y - resource.CelHeight / 2 + offset);
                var trackName = SanitizeTrackName(Path.GetFileNameWithoutExtension(file));
                _viewModel.AddImageTrack(trackName, symbol, x, y);
            }
            RefreshImageBindings();
            _viewModel.Status = $"已拖入 {args.Files.Count} 张图片并创建可动画轨道；按 K 设置关键帧。";
        }
        catch (Exception exception)
        {
            _viewModel.Status = $"拖入图片失败：{exception.Message}";
        }
        finally
        {
            _viewModel.EndEditTransaction();
        }
    }

    private static string SanitizeTrackName(string value)
    {
        var result = new string(value.Select(character =>
            char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray()).Trim('_');
        return string.IsNullOrWhiteSpace(result) ? "图片部件" : result;
    }

    private void CloseLeaf(WorkspaceLayoutNode node)
    {
        if (ReferenceEquals(_layout.Root, node)) return;
        if (!RemoveLeaf(_layout.Root, node)) return;
        Rebuild();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool RemoveLeaf(WorkspaceLayoutNode current, WorkspaceLayoutNode target)
    {
        if (current.IsLeaf) return false;
        if (ReferenceEquals(current.First, target))
        {
            CopyNode(current, current.Second!);
            return true;
        }
        if (ReferenceEquals(current.Second, target))
        {
            CopyNode(current, current.First!);
            return true;
        }
        return RemoveLeaf(current.First!, target) || RemoveLeaf(current.Second!, target);
    }

    private static void CopyNode(WorkspaceLayoutNode target, WorkspaceLayoutNode source)
    {
        var copy = source.Clone();
        target.Editor = copy.Editor;
        target.Split = copy.Split;
        target.Ratio = copy.Ratio;
        target.First = copy.First;
        target.Second = copy.Second;
    }

    private void CreateFloatingWindow(WorkspaceEditorKind editor)
    {
        if (_viewModel is null || _resources is null) return;
        var host = new WorkspaceHostControl();
        host.Bind(_viewModel, _resources);
        host.ApplyLayout(new WorkspaceLayoutState { Root = WorkspaceLayoutPresetService.Leaf(editor) });
        host.ImportImagesRequested += (_, _) => ImportImagesRequested?.Invoke(this, EventArgs.Empty);
        host.ChooseGameRootRequested += (_, _) => ChooseGameRootRequested?.Invoke(this, EventArgs.Empty);
        var window = new Window
        {
            Title = $"PvZ 动画制作器 · {EditorName(editor)} · 独立窗口",
            Width = 1050,
            Height = 720,
            MinWidth = 520,
            MinHeight = 360,
            Owner = Window.GetWindow(this),
            Content = host,
            Background = new SolidColorBrush(Color.FromRgb(20, 25, 31))
        };
        _floatingWindows.Add(window);
        window.Closed += (_, _) =>
        {
            host.Unbind();
            _floatingWindows.Remove(window);
        };
        window.Show();
    }

    private static string EditorName(WorkspaceEditorKind editor) => editor switch
    {
        WorkspaceEditorKind.Timeline => "轨道时间轴",
        WorkspaceEditorKind.Browser => "资源与工程",
        WorkspaceEditorKind.Inspector => "属性检查器",
        _ => "动画视图"
    };
}
