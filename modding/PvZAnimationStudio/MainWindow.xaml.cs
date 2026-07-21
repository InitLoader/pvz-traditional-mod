using System.Windows;
using System.Windows.Input;
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
        _viewModel.VisualStateChanged += (_, _) => UpdatePlaybackInterval();
        UpdatePlaybackInterval();
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
        RunGuarded(() =>
        {
            var document = _codec.Load(dialog.FileName);
            _viewModel.ReplaceAnimation(document, dialog.FileName);
            _viewModel.Status = $"已读取 {Path.GetFileName(dialog.FileName)}：{document.Tracks.Count} 轨 / {document.FrameCount} 帧";
        });
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
        _viewModel.Project.GameRoot = dialog.FolderName;
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
            string? first = null;
            foreach (var file in dialog.FileNames)
                first ??= _resources.ImportImage(_viewModel.Project, file);
            if (first is not null && string.IsNullOrWhiteSpace(_viewModel.CurrentImage))
                _viewModel.CurrentImage = first;
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
    private void SetKey_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.SetKeyframe();
    private void ClearKey_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.ClearKeyframe();
    private void LinearTween_Click(object sender, RoutedEventArgs eventArgs) => RunGuarded(() => _viewModel.CreateTween(TweenCurve.Linear));
    private void SmoothTween_Click(object sender, RoutedEventArgs eventArgs) => RunGuarded(() => _viewModel.CreateTween(TweenCurve.SmoothStep));
    private void InsertFrame_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.InsertFrame();
    private void DeleteFrame_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.DeleteFrame();
    private void FirstFrame_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.CurrentFrame = 0;
    private void PreviousFrame_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.CurrentFrame--;
    private void NextFrame_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.CurrentFrame++;
    private void LastFrame_Click(object sender, RoutedEventArgs eventArgs) => _viewModel.CurrentFrame = _viewModel.Project.Animation.FrameCount - 1;
    private void ResetView_Click(object sender, RoutedEventArgs eventArgs) => PreviewControl.ResetView();

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
        _viewModel.CurrentFrame = 0;
        PlayButton.Content = "▶ 播放";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Space) { Play_Click(sender, new RoutedEventArgs()); eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { _viewModel.ClearKeyframe(); eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.K) { _viewModel.SetKeyframe(); eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.Left) { _viewModel.CurrentFrame--; eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.Right) { _viewModel.CurrentFrame++; eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { SaveProject_Click(sender, new RoutedEventArgs()); eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { OpenAnimation_Click(sender, new RoutedEventArgs()); eventArgs.Handled = true; }
    }

    private void Help_Click(object sender, RoutedEventArgs eventArgs)
    {
        MessageBox.Show(this,
            "基本流程：\n1. 选择游戏目录。\n2. 打开原版 .reanim.compiled 或新建工程。\n3. 导入 PNG，双击资源绑定到轨道。\n4. 在时间轴双击设置 K 帧，画布拖动位置，右侧修改缩放/旋转/透明度。\n5. 自动识别 anim_* 动作，补充攻击事件。\n6. 导出 Raw/compiled，或一键安装 JSON、图片和动画。\n\n快捷键：空格播放，K 设置关键帧，Shift+K 清除，左右键换帧。",
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
