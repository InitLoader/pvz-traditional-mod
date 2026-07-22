using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using PvZAnimationStudio.Controls;
using PvZAnimationStudio.Models;
using PvZAnimationStudio.Services;
using PvZAnimationStudio.ViewModels;

namespace PvZAnimationStudio;

public partial class MainWindow : Window
{
    private readonly ReanimCodecService _codec = new();
    private readonly ActionCatalogService _actionCatalog = new();
    private readonly OriginalResourceService _resources = new();
    private readonly ProjectFileService _projectFiles;
    private readonly EntityPreviewProfileService _entityProfiles = new();
    private readonly ProjectPackageService _packages;
    private readonly EditorViewModel _viewModel;
    private readonly DispatcherTimer _playTimer;
    private bool _changingWorkspaceSelection;
    private string? _lastFileDirectory;

    public MainWindow()
    {
        InitializeComponent();
        _projectFiles = new ProjectFileService(_resources);
        _packages = new ProjectPackageService(_codec, new JsoncArrayEditor());
        _viewModel = new EditorViewModel(_actionCatalog);
        DataContext = _viewModel;
        WorkspaceHost.Bind(_viewModel, _resources);
        WorkspaceHost.ApplyLayout(_viewModel.Project.WorkspaceLayout);
        WorkspaceHost.ImportImagesRequested += (_, _) => ImportImages();
        WorkspaceHost.ChooseGameRootRequested += (_, _) => ChooseGameRoot();
        WorkspaceHost.LayoutChanged += (_, _) =>
        {
            _viewModel.Project.WorkspaceLayout = WorkspaceHost.ExportLayout();
            SelectWorkspacePreset(WorkspacePreset.Custom);
        };
        _playTimer = new DispatcherTimer();
        _playTimer.Tick += (_, _) => _viewModel.StepPlayback();
        _viewModel.VisualStateChanged += (_, _) =>
        {
            UpdatePlaybackInterval();
            UpdateToolButtons();
        };
        var localGameRoot = FindGameRoot(Environment.CurrentDirectory);
        if (localGameRoot is not null)
        {
            _viewModel.Project.GameRoot = localGameRoot;
            _resources.RebuildIndex(localGameRoot);
        }
        UpdatePlaybackInterval();
        UpdateToolButtons();
        Loaded += (_, _) =>
        {
            var startupArguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
            var startupFile = startupArguments.FirstOrDefault(File.Exists);
            if (startupFile is not null) RunGuarded(() =>
            {
                if (IsProjectFile(startupFile)) LoadProjectFile(startupFile);
                else LoadAnimationFile(startupFile);
                if (startupArguments.Length > 1)
                {
                    if (string.Equals(startupArguments[1], "representative", StringComparison.OrdinalIgnoreCase))
                        _viewModel.CurrentFrame = FindRepresentativeActionFrame();
                    else if (string.Equals(startupArguments[1], "auto", StringComparison.OrdinalIgnoreCase))
                        _viewModel.CurrentFrame = FindRepresentativeFrame(_viewModel.Project.Animation);
                    else if (int.TryParse(startupArguments[1], out var startupFrame))
                        _viewModel.CurrentFrame = Math.Clamp(startupFrame, 0, Math.Max(0, _viewModel.Project.Animation.FrameCount - 1));
                }
                if (startupArguments.Length > 2)
                    _viewModel.SelectedAction = _viewModel.Actions.FirstOrDefault(action =>
                        string.Equals(action.Track, startupArguments[2], StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(action.Id, startupArguments[2], StringComparison.OrdinalIgnoreCase));
                Dispatcher.BeginInvoke(WorkspaceHost.FrameAllViews, DispatcherPriority.Loaded);
            });
        };
        Closed += (_, _) => WorkspaceHost.Unbind();
    }

    private void UpdatePlaybackInterval()
    {
        var fps = Math.Clamp(_viewModel.Project.Animation.Fps, 0.1f, 120f);
        _playTimer.Interval = TimeSpan.FromSeconds(1.0 / fps);
    }

    private void NewPlant_Click(object sender, RoutedEventArgs eventArgs) => NewProject(EntityKind.Plant);
    private void NewZombie_Click(object sender, RoutedEventArgs eventArgs) => NewProject(EntityKind.Zombie);

    private void NewProject(EntityKind kind)
    {
        _viewModel.NewProject(kind);
        var layout = new WorkspaceLayoutPresetService().Create(WorkspacePreset.Animation);
        _viewModel.Project.WorkspaceLayout = layout;
        WorkspaceHost.ApplyLayout(layout);
        SelectWorkspacePreset(WorkspacePreset.Animation);
    }

    private void OpenAnimation_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开 Reanimation",
            Filter = "全部支持的动画|*.reanim.compiled;*.compiled;*.reanim|Compiled Reanimation|*.reanim.compiled;*.compiled|Raw Reanimation|*.reanim|所有文件|*.*",
            CheckFileExists = true,
            RestoreDirectory = true,
            InitialDirectory = Directory.Exists(_lastFileDirectory) ? _lastFileDirectory : null
        };
        if (dialog.ShowDialog(this) != true) return;
        RunGuarded(() => LoadAnimationFile(dialog.FileName));
    }

    public void LoadAnimationFile(string fileName)
    {
        _lastFileDirectory = Path.GetDirectoryName(Path.GetFullPath(fileName));
        var document = _codec.Load(fileName);
        _viewModel.ReplaceAnimation(document, fileName);
        var gameRoot = FindGameRoot(Path.GetDirectoryName(Path.GetFullPath(fileName))!);
        if (gameRoot is not null)
        {
            _viewModel.Project.GameRoot = gameRoot;
            _resources.RebuildIndex(gameRoot);
        }
        _viewModel.Status = $"已读取 {Path.GetFileName(fileName)}：{document.Tracks.Count} 轨 / {document.FrameCount} 帧";
        Dispatcher.BeginInvoke(WorkspaceHost.FrameAllViews, DispatcherPriority.Loaded);
    }

    private void OpenProject_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开动画工程",
            Filter = "PvZ 便携动画工程|*.pvza;*.pvzanimproj|旧版工程 JSON|*.pvza.json;*.json|所有文件|*.*",
            CheckFileExists = true,
            RestoreDirectory = true,
            InitialDirectory = Directory.Exists(_lastFileDirectory) ? _lastFileDirectory : null
        };
        if (dialog.ShowDialog(this) != true) return;
        RunGuarded(() => LoadProjectFile(dialog.FileName));
    }

    private void LoadProjectFile(string fileName)
    {
        _lastFileDirectory = Path.GetDirectoryName(Path.GetFullPath(fileName));
        var project = _projectFiles.Load(fileName);
        _viewModel.ReplaceProject(project);
        _resources.RebuildIndex(project.GameRoot);
        WorkspaceHost.ApplyLayout(project.WorkspaceLayout);
        SelectWorkspacePreset(WorkspacePreset.Custom);
        _viewModel.Status = $"已打开便携工程：{project.DisplayName}";
        Dispatcher.BeginInvoke(WorkspaceHost.FrameAllViews, DispatcherPriority.Loaded);
    }

    private void SaveProject_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.Project.ProjectPath) ||
            _viewModel.Project.ProjectPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            SaveProjectAs_Click(sender, eventArgs);
            return;
        }
        RunGuarded(() =>
        {
            _viewModel.Project.WorkspaceLayout = WorkspaceHost.ExportLayout();
            _projectFiles.Save(_viewModel.Project, _viewModel.Project.ProjectPath!);
            _viewModel.Status = "便携工程已保存（动画、属性和图片均已嵌入）";
        });
    }

    private void SaveProjectAs_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存动画工程",
            Filter = "PvZ 便携动画工程|*.pvza",
            DefaultExt = ".pvza",
            FileName = $"{_viewModel.Project.Id}.pvza",
            AddExtension = true,
            RestoreDirectory = true,
            InitialDirectory = Directory.Exists(_lastFileDirectory) ? _lastFileDirectory : null
        };
        if (dialog.ShowDialog(this) != true) return;
        RunGuarded(() =>
        {
            _viewModel.Project.WorkspaceLayout = WorkspaceHost.ExportLayout();
            _projectFiles.Save(_viewModel.Project, dialog.FileName);
            _lastFileDirectory = Path.GetDirectoryName(Path.GetFullPath(dialog.FileName));
            _viewModel.Status = $"便携工程已保存：{dialog.FileName}";
        });
    }

    private void ExportRaw_Click(object sender, RoutedEventArgs eventArgs) => Export(AnimationOutputFormat.Raw);
    private void ExportCompiled_Click(object sender, RoutedEventArgs eventArgs) => Export(AnimationOutputFormat.Compiled);

    private void Export(AnimationOutputFormat format)
    {
        var compiled = format == AnimationOutputFormat.Compiled;
        var dialog = new SaveFileDialog
        {
            Title = compiled ? "导出 compiled Reanimation" : "导出 Raw Reanimation",
            Filter = compiled ? "Compiled Reanimation|*.reanim.compiled" : "Raw Reanimation|*.reanim",
            DefaultExt = compiled ? ".reanim.compiled" : ".reanim",
            FileName = _viewModel.Project.Id + (compiled ? ".reanim.compiled" : ".reanim"),
            AddExtension = true,
            RestoreDirectory = true,
            InitialDirectory = Directory.Exists(_lastFileDirectory) ? _lastFileDirectory : null
        };
        if (dialog.ShowDialog(this) != true) return;
        RunGuarded(() =>
        {
            var outputPath = _codec.Save(_viewModel.Project.Animation, dialog.FileName, format);
            _lastFileDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            _viewModel.Status = $"已导出，可直接重新打开：{outputPath}";
        });
    }

    private void Package_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new SaveFileDialog
        {
            Title = "打包 PvZ Mod",
            Filter = "ZIP 压缩包|*.zip",
            FileName = $"{_viewModel.Project.Id}-pvzmod.zip",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        RunGuarded(() =>
        {
            _packages.CreatePackage(_viewModel.Project, dialog.FileName);
            _viewModel.Status = $"Mod 包已生成：{dialog.FileName}";
        });
    }

    private void Install_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.Project.GameRoot) ||
            !File.Exists(Path.Combine(_viewModel.Project.GameRoot, "PlantsVsZombies.exe")))
        {
            if (!ChooseGameRoot()) return;
        }
        RunGuarded(() =>
        {
            _packages.InstallToGame(_viewModel.Project, _viewModel.Project.GameRoot!);
            _viewModel.Status = "动画、图片和 JSONC 已一键安装；原配置备份为 .pvzstudio.bak";
            MessageBox.Show(this,
                "安装完成。\n\n动画、图片和配置已写入游戏目录；首次修改的 JSONC 已保存 .pvzstudio.bak。\n请完全退出游戏后重新启动。",
                "安装完成", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private void ChooseGameRoot_Click(object sender, RoutedEventArgs eventArgs) => ChooseGameRoot();

    private bool ChooseGameRoot()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 PlantsVsZombies.exe 所在目录",
            InitialDirectory = Directory.Exists(_viewModel.Project.GameRoot) ? _viewModel.Project.GameRoot : Environment.CurrentDirectory
        };
        if (dialog.ShowDialog(this) != true) return false;
        if (!File.Exists(Path.Combine(dialog.FolderName, "PlantsVsZombies.exe")))
        {
            MessageBox.Show(this, "此目录没有 PlantsVsZombies.exe。", "目录无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _viewModel.ProjectGameRoot = dialog.FolderName;
        _resources.RebuildIndex(dialog.FolderName);
        _viewModel.Status = $"已索引原版资源：{dialog.FolderName}";
        WorkspaceHost.FrameAllViews();
        return true;
    }

    private void ImportImages_Click(object sender, RoutedEventArgs eventArgs) => ImportImages();

    private void ImportImages()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入 PNG / JPG 动画部件",
            Filter = "支持的图片|*.png;*.jpg;*.jpeg|PNG 图片|*.png|JPEG 图片|*.jpg;*.jpeg",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        RunGuarded(() =>
        {
            _viewModel.BeginEditTransaction("导入图片资源");
            string? first = null;
            try
            {
                foreach (var file in dialog.FileNames)
                    first ??= _resources.ImportImage(_viewModel.Project, file);
                if (first is not null && string.IsNullOrWhiteSpace(_viewModel.CurrentImage))
                    _viewModel.CurrentImage = first;
            }
            finally
            {
                _viewModel.EndEditTransaction();
            }
            WorkspaceHost.RefreshImageBindings();
            _viewModel.Status = $"已导入 {dialog.FileNames.Length} 张图片";
        });
    }

    private void AddTrack_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.AddTrack("新部件");
    private void RemoveTrack_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.RemoveSelectedTrack();

    private void RemoveAction_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.RemoveSelectedAction();
    private void InferActions_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.InferActions();
    private void FullTimeline_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.ClearActionView();
    private void Undo_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.Undo();
    private void Redo_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.Redo();
    private void SelectTool_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.ActiveTool = EditorTool.Select;
    private void MoveTool_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.ActiveTool = EditorTool.Move;
    private void RotateTool_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.ActiveTool = EditorTool.Rotate;
    private void ScaleTool_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.ActiveTool = EditorTool.Scale;
    private void SetKey_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.SetKeyframe();
    private void ClearKey_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.ClearKeyframe();
    private void LinearTween_Click(object sender, RoutedEventArgs eventArgs) => RunGuarded(() => _viewModel.CreateTween(TweenCurve.Linear));
    private void SmoothTween_Click(object sender, RoutedEventArgs eventArgs) => RunGuarded(() => _viewModel.CreateTween(TweenCurve.SmoothStep));
    private void InsertFrame_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.InsertFrame();
    private void DeleteFrame_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.DeleteFrame();
    private void FirstFrame_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.CurrentFrame = _viewModel.TimelineFrameStart;
    private void PreviousFrame_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.CurrentFrame--;
    private void NextFrame_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.CurrentFrame++;
    private void LastFrame_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.CurrentFrame = _viewModel.TimelineFrameEnd;
    private void ResetView_Click(object sender, RoutedEventArgs eventArgs) => WorkspaceHost.ResetAllViews();
    private void FrameAll_Click(object sender, RoutedEventArgs eventArgs) => WorkspaceHost.FrameAllViews();

    private void Play_Click(object sender, RoutedEventArgs eventArgs)
    {
        _viewModel.IsPlaying = !_viewModel.IsPlaying;
        if (_viewModel.IsPlaying) _playTimer.Start(); else _playTimer.Stop();
        PlayButton.Content = _viewModel.IsPlaying ? "⏸ 暂停" : "▶ 播放";
    }

    private void Stop_Click(object sender, RoutedEventArgs eventArgs)
    {
        _playTimer.Stop();
        _viewModel.IsPlaying = false;
        _viewModel.CurrentFrame = _viewModel.TimelineFrameStart;
        PlayButton.Content = "▶ 播放";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        var modifiers = Keyboard.Modifiers;
        var isTextEditing = eventArgs.OriginalSource is TextBox or ComboBox;
        if (isTextEditing && !modifiers.HasFlag(ModifierKeys.Control)) return;

        if (eventArgs.Key == Key.Z && modifiers.HasFlag(ModifierKeys.Control) && modifiers.HasFlag(ModifierKeys.Shift))
        { _viewModel.Redo(); eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.Z && modifiers.HasFlag(ModifierKeys.Control))
        { _viewModel.Undo(); eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.Y && modifiers.HasFlag(ModifierKeys.Control))
        { _viewModel.Redo(); eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.S && modifiers.HasFlag(ModifierKeys.Control))
        { SaveProject_Click(sender, new RoutedEventArgs()); eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.O && modifiers.HasFlag(ModifierKeys.Control))
        { OpenAnimation_Click(sender, new RoutedEventArgs()); eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.Space)
        { Play_Click(sender, new RoutedEventArgs()); eventArgs.Handled = true; }
        else if ((eventArgs.OriginalSource is TimelineControl or GraphEditorControl) &&
                 (eventArgs.Key is Key.Delete or Key.Back or Key.Home ||
                  modifiers.HasFlag(ModifierKeys.Shift) && eventArgs.Key is Key.Left or Key.Right))
        { return; }
        else if (eventArgs.Key == Key.K && modifiers.HasFlag(ModifierKeys.Shift))
        { _viewModel.ClearKeyframe(); eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.K)
        { _viewModel.SetKeyframe(); eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.Q)
        { _viewModel.ActiveTool = EditorTool.Select; eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.G)
        { WorkspaceHost.BeginModalTransform(EditorTool.Move); eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.W)
        { _viewModel.ActiveTool = EditorTool.Move; eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.E)
        { _viewModel.ActiveTool = EditorTool.Rotate; eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.R)
        { WorkspaceHost.BeginModalTransform(EditorTool.Rotate); eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.S)
        { WorkspaceHost.BeginModalTransform(EditorTool.Scale); eventArgs.Handled = true; }
        else if (eventArgs.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var amount = modifiers.HasFlag(ModifierKeys.Shift) ? 10f : 1f;
            _viewModel.BeginEditTransaction("方向键移动部件");
            if (eventArgs.Key == Key.Left) _viewModel.MoveSelected(-amount, 0);
            if (eventArgs.Key == Key.Right) _viewModel.MoveSelected(amount, 0);
            if (eventArgs.Key == Key.Up) _viewModel.MoveSelected(0, -amount);
            if (eventArgs.Key == Key.Down) _viewModel.MoveSelected(0, amount);
            _viewModel.EndEditTransaction();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.OemComma)
        { _viewModel.CurrentFrame--; eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.OemPeriod)
        { _viewModel.CurrentFrame++; eventArgs.Handled = true; }
    }

    private void UpdateToolButtons()
    {
        var normal = new SolidColorBrush(Color.FromRgb(52, 64, 76));
        var active = new SolidColorBrush(Color.FromRgb(47, 111, 67));
        SelectToolButton.Background = _viewModel.ActiveTool == EditorTool.Select ? active : normal;
        MoveToolButton.Background = _viewModel.ActiveTool == EditorTool.Move ? active : normal;
        RotateToolButton.Background = _viewModel.ActiveTool == EditorTool.Rotate ? active : normal;
        ScaleToolButton.Background = _viewModel.ActiveTool == EditorTool.Scale ? active : normal;
    }

    private static string? FindGameRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PlantsVsZombies.exe"))) return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }

    private static int FindRepresentativeFrame(AnimationDocument document)
    {
        if (document.FrameCount <= 0) return 0;
        var visiblePartsByFrame = new int[document.FrameCount];
        foreach (var track in document.Tracks.Where(track => !track.IsActionTrack && track.HasRenderableContent))
        {
            var imageFrame = 0f;
            var alpha = 1f;
            string? image = null;
            for (var frameIndex = 0; frameIndex < Math.Min(document.FrameCount, track.Frames.Count); frameIndex++)
            {
                var frame = track.Frames[frameIndex];
                if (frame.Frame.HasValue) imageFrame = frame.Frame.Value;
                if (frame.Alpha.HasValue) alpha = frame.Alpha.Value;
                if (frame.Image is not null) image = frame.Image.Length == 0 ? null : frame.Image;
                if (imageFrame >= 0 && alpha > 0 && !string.IsNullOrWhiteSpace(image))
                    visiblePartsByFrame[frameIndex]++;
            }
        }
        var maximum = visiblePartsByFrame.Max();
        var candidates = Enumerable.Range(0, visiblePartsByFrame.Length)
            .Where(index => visiblePartsByFrame[index] == maximum).ToArray();
        return candidates.Length == 0 ? 0 : candidates[candidates.Length / 2];
    }

    private int FindRepresentativeActionFrame()
    {
        var entityFrame = _entityProfiles.GetRepresentativeFrame(_viewModel.Project);
        if (entityFrame.HasValue) return entityFrame.Value;
        var preferred = _viewModel.Actions.FirstOrDefault(action =>
                            action.Id.Contains("full_idle", StringComparison.OrdinalIgnoreCase))
                        ?? _viewModel.Actions.FirstOrDefault(action =>
                            string.Equals(action.Id, "idle", StringComparison.OrdinalIgnoreCase))
                        ?? _viewModel.Actions.FirstOrDefault(action =>
                            action.Id.Contains("idle", StringComparison.OrdinalIgnoreCase))
                        ?? _viewModel.Actions.FirstOrDefault(action =>
                            action.Id.Contains("walk", StringComparison.OrdinalIgnoreCase))
                        ?? _viewModel.Actions.FirstOrDefault();
        if (preferred is null) return FindRepresentativeFrame(_viewModel.Project.Animation);
        var range = new ActionViewService().GetRange(_viewModel.Project.Animation, preferred);
        var candidate = range.Start + (range.Count - 1) / 2;
        return CountVisibleParts(_viewModel.Project.Animation, candidate) > 0
            ? candidate
            : FindRepresentativeFrame(_viewModel.Project.Animation);
    }

    private static int CountVisibleParts(AnimationDocument document, int frameIndex) =>
        document.Tracks.Count(track =>
        {
            if (track.IsActionTrack || !track.HasRenderableContent) return false;
            var frame = track.ResolveFrame(frameIndex);
            return frame.Frame >= 0 && frame.Alpha > 0 && !string.IsNullOrWhiteSpace(frame.Image);
        });

    private void WorkspacePresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_changingWorkspaceSelection || WorkspaceHost is null ||
            WorkspacePresetCombo.SelectedItem is not ComboBoxItem item ||
            !Enum.TryParse<WorkspacePreset>(item.Tag?.ToString(), out var preset) ||
            preset == WorkspacePreset.Custom) return;
        ApplyWorkspacePreset(preset);
    }

    private void ApplyWorkspacePreset(WorkspacePreset preset)
    {
        WorkspaceHost.ApplyPreset(preset);
        _viewModel.Project.WorkspaceLayout = WorkspaceHost.ExportLayout();
        SelectWorkspacePreset(preset);
        Dispatcher.BeginInvoke(WorkspaceHost.FrameAllViews, DispatcherPriority.Loaded);
    }

    private void SelectWorkspacePreset(WorkspacePreset preset)
    {
        if (WorkspacePresetCombo is null) return;
        _changingWorkspaceSelection = true;
        try
        {
            WorkspacePresetCombo.SelectedItem = WorkspacePresetCombo.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), preset.ToString(), StringComparison.Ordinal));
        }
        finally
        {
            _changingWorkspaceSelection = false;
        }
    }

    private void AnimationWorkspace_Click(object sender, RoutedEventArgs eventArgs) => ApplyWorkspacePreset(WorkspacePreset.Animation);
    private void DualViewWorkspace_Click(object sender, RoutedEventArgs eventArgs) => ApplyWorkspacePreset(WorkspacePreset.DualView);
    private void DualTimelineWorkspace_Click(object sender, RoutedEventArgs eventArgs) => ApplyWorkspacePreset(WorkspacePreset.DualTimeline);
    private void GraphWorkspace_Click(object sender, RoutedEventArgs eventArgs) => ApplyWorkspacePreset(WorkspacePreset.GraphEditing);
    private void FocusWorkspace_Click(object sender, RoutedEventArgs eventArgs) => ApplyWorkspacePreset(WorkspacePreset.Focus);

    private void Window_Drop(object sender, DragEventArgs eventArgs)
    {
        if (!eventArgs.Data.GetDataPresent(DataFormats.FileDrop) ||
            eventArgs.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        var file = files[0];
        RunGuarded(() =>
        {
            if (IsProjectFile(file)) LoadProjectFile(file);
            else LoadAnimationFile(file);
        });
        eventArgs.Handled = true;
    }

    private static bool IsProjectFile(string path) =>
        path.EndsWith(".pvza", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".pvzanimproj", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".pvza.json", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private void Help_Click(object sender, RoutedEventArgs eventArgs)
    {
        MessageBox.Show(this,
            "基本流程：\n1. 选择游戏目录。\n2. 打开任意目录中的 .reanim.compiled，或新建工程。\n3. 拖动区域分隔线调整大小；右上角 ↔/↕ 或斜纹拖拽可拆分区域，↗ 可打开独立窗口。\n4. 每个区域可切换动画视图、时间轴、曲线编辑器、资源或属性；右上角可选曲线动画工作区。\n5. 时间轴或曲线区的空白位置随时按住左键拖动即可框选，Ctrl/Shift 追加；可批量拖动，Delete/Backspace 批量删除。跨过已有关键帧会交换顺序，不会合并吞帧。\n6. 曲线区拖关键点改变帧/值，拖圆形手柄改变 Bezier；滚轮缩放时间，Ctrl+滚轮缩放数值，中键平移。\n7. Q 选择，W/E 切换移动/旋转操纵器；G/R/S 进入鼠标移动/中心旋转/缩放，左键确认，右键或 Esc 取消；K 设置关键帧。\n8. 保存为 .pvza 会把动画、曲线手柄、动作、属性、工作区和所有图片嵌入同一个文件。\n\n撤销/重做：Ctrl+Z / Ctrl+Y，最多保留最近 100 步。方向键微调部件，Shift+方向键加速。",
            "制作流程", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Exit_Click(object sender, RoutedEventArgs eventArgs) => Close();

    private void RunGuarded(Action action)
    {
        try { action(); }
        catch (Exception exception)
        {
            _viewModel.Status = exception.Message;
            MessageBox.Show(this, exception.Message, "操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
