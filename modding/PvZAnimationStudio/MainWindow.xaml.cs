using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using PvZAnimationStudio.Models;
using PvZAnimationStudio.Services;
using PvZAnimationStudio.ViewModels;

namespace PvZAnimationStudio;

public partial class MainWindow : Window
{
    private readonly ReanimCodecService _codec = new();
    private readonly ProjectFileService _projectFiles = new();
    private readonly ActionCatalogService _actionCatalog = new();
    private readonly OriginalResourceService _resources = new();
    private readonly EntityPreviewProfileService _entityProfiles = new();
    private readonly ProjectPackageService _packages;
    private readonly EditorViewModel _viewModel;
    private readonly DispatcherTimer _playTimer;

    public MainWindow()
    {
        InitializeComponent();
        _packages = new ProjectPackageService(_codec, new JsoncArrayEditor());
        _viewModel = new EditorViewModel(_actionCatalog);
        DataContext = _viewModel;
        PreviewControl.Bind(_viewModel, _resources);
        TimelineControl.Bind(_viewModel);
        KindCombo.ItemsSource = Enum.GetValues<EntityKind>();
        OutputFormatCombo.ItemsSource = Enum.GetValues<AnimationOutputFormat>();
        LoopCombo.ItemsSource = Enum.GetValues<AnimationLoopMode>();
        ActionTemplateCombo.SelectedIndex = 0;
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
                LoadAnimationFile(startupFile);
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
                Dispatcher.BeginInvoke(PreviewControl.FrameAll, DispatcherPriority.Loaded);
            });
        };
    }

    private void UpdatePlaybackInterval()
    {
        var fps = Math.Clamp(_viewModel.Project.Animation.Fps, 0.1f, 120f);
        _playTimer.Interval = TimeSpan.FromSeconds(1.0 / fps);
    }

    private void NewPlant_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.NewProject(EntityKind.Plant);
    private void NewZombie_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.NewProject(EntityKind.Zombie);

    private void OpenAnimation_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开 Reanimation",
            Filter = "Reanimation|*.reanim;*.reanim.compiled|Raw Reanimation|*.reanim|Compiled Reanimation|*.reanim.compiled|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        RunGuarded(() => LoadAnimationFile(dialog.FileName));
    }

    public void LoadAnimationFile(string fileName)
    {
        var document = _codec.Load(fileName);
        _viewModel.ReplaceAnimation(document, fileName);
        var gameRoot = FindGameRoot(Path.GetDirectoryName(Path.GetFullPath(fileName))!);
        if (gameRoot is not null)
        {
            _viewModel.Project.GameRoot = gameRoot;
            _resources.RebuildIndex(gameRoot);
        }
        _viewModel.Status = $"已读取 {Path.GetFileName(fileName)}：{document.Tracks.Count} 轨 / {document.FrameCount} 帧";
        Dispatcher.BeginInvoke(PreviewControl.FrameAll, DispatcherPriority.Loaded);
    }

    private void OpenProject_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog { Title = "打开动画工程", Filter = "PvZ 动画工程|*.pvza.json|JSON|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        RunGuarded(() =>
        {
            var project = _projectFiles.Load(dialog.FileName);
            _viewModel.ReplaceProject(project);
            _resources.RebuildIndex(project.GameRoot);
            _viewModel.Status = $"已打开工程：{project.DisplayName}";
        });
    }

    private void SaveProject_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.Project.ProjectPath))
        {
            SaveProjectAs_Click(sender, eventArgs);
            return;
        }
        RunGuarded(() =>
        {
            _projectFiles.Save(_viewModel.Project, _viewModel.Project.ProjectPath!);
            _viewModel.Status = "工程已保存";
        });
    }

    private void SaveProjectAs_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存动画工程",
            Filter = "PvZ 动画工程|*.pvza.json",
            FileName = $"{_viewModel.Project.Id}.pvza.json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        RunGuarded(() =>
        {
            _projectFiles.Save(_viewModel.Project, dialog.FileName);
            _viewModel.Status = $"工程已保存：{dialog.FileName}";
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
            FileName = _viewModel.Project.Id + (compiled ? ".reanim.compiled" : ".reanim"),
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        RunGuarded(() =>
        {
            _codec.Save(_viewModel.Project.Animation, dialog.FileName);
            _viewModel.Status = $"已导出：{dialog.FileName}";
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
        PreviewControl.InvalidateVisual();
        return true;
    }

    private void ImportImages_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入透明 PNG 部件",
            Filter = "PNG 图片|*.png",
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
            ImageBindingsList.Items.Refresh();
            _viewModel.Status = $"已导入 {dialog.FileNames.Length} 张图片";
        });
    }

    private void ImageBindingsList_MouseDoubleClick(object sender, MouseButtonEventArgs eventArgs)
    {
        if (ImageBindingsList.SelectedItem is KeyValuePair<string, string> selected)
            _viewModel.CurrentImage = selected.Key;
    }

    private void AddTrack_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.AddTrack("新部件");
    private void RemoveTrack_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.RemoveSelectedTrack();

    private void AddAction_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (ActionTemplateCombo.SelectedItem is ActionTemplate template) _viewModel.AddAction(template);
    }

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
    private void ResetView_Click(object sender, RoutedEventArgs eventArgs) => PreviewControl.ResetView();
    private void FrameAll_Click(object sender, RoutedEventArgs eventArgs) => PreviewControl.FrameAll();

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
        else if (eventArgs.Key == Key.K && modifiers.HasFlag(ModifierKeys.Shift))
        { _viewModel.ClearKeyframe(); eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.K)
        { _viewModel.SetKeyframe(); eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.Q)
        { _viewModel.ActiveTool = EditorTool.Select; eventArgs.Handled = true; }
        else if (eventArgs.Key is Key.W or Key.G)
        { _viewModel.ActiveTool = EditorTool.Move; eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.E)
        { _viewModel.ActiveTool = EditorTool.Rotate; eventArgs.Handled = true; }
        else if (eventArgs.Key is Key.R or Key.S)
        { _viewModel.ActiveTool = EditorTool.Scale; eventArgs.Handled = true; }
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

    private void Help_Click(object sender, RoutedEventArgs eventArgs)
    {
        MessageBox.Show(this,
            "基本流程：\n1. 选择游戏目录。\n2. 打开原版 .reanim.compiled 或新建工程。\n3. 在动作页点击动作，时间轴会只显示该动作范围和发生变化的轨道。\n4. Q 选择，W 移动，E 旋转，R 缩放；拖动画布操纵器制作关键帧。\n5. K 设置关键帧，Shift+K 清除，逗号/句号换帧。\n6. 导出 Raw/compiled，或一键安装。\n\n撤销/重做：Ctrl+Z / Ctrl+Y。方向键微调部件，Shift+方向键加速。",
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
