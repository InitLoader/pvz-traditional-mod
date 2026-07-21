using System.Collections.ObjectModel;
using PvZAnimationStudio.Models;
using PvZAnimationStudio.Services;

namespace PvZAnimationStudio.ViewModels;

public sealed class EditorViewModel : ObservableObject
{
    private readonly ActionCatalogService _actionCatalog;
    private readonly TweenService _tweenService = new();
    private readonly ActionViewService _actionView = new();
    private readonly ProjectCloneService _cloneService = new();
    private readonly EditHistoryService _history = new();
    private EditorProject _project;
    private AnimationTrack? _selectedTrack;
    private ActionDefinition? _selectedAction;
    private int _currentFrame;
    private bool _isPlaying;
    private string _status = "就绪";
    private EditorTool _activeTool = EditorTool.Move;
    private bool _editTransactionActive;

    public EditorViewModel(ActionCatalogService actionCatalog)
    {
        _actionCatalog = actionCatalog;
        _project = CreateDefaultProject(EntityKind.Plant);
        _selectedTrack = _project.Animation.Tracks.FirstOrDefault(track => !track.IsActionTrack);
    }

    public event EventHandler? VisualStateChanged;

    public EditorProject Project
    {
        get => _project;
        private set
        {
            if (!SetField(ref _project, value)) return;
            RaiseAll();
        }
    }

    public ObservableCollection<AnimationTrack> Tracks => Project.Animation.Tracks;
    public ObservableCollection<ActionDefinition> Actions => Project.Actions;
    public IReadOnlyList<ActionTemplate> ActionTemplates => _actionCatalog.Templates;
    public IReadOnlyList<AnimationTrack> TimelineTracks => _actionView.GetTimelineTracks(Project.Animation, SelectedAction);
    public ActionFrameRange ActiveRange => _actionView.GetRange(Project.Animation, SelectedAction);
    public int TimelineFrameStart => ActiveRange.Start;
    public int TimelineFrameEnd => ActiveRange.End;
    public int TimelineFrameCount => ActiveRange.Count;
    public bool IsActionView => SelectedAction is not null;
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public string UndoLabel => _history.UndoName is null ? "撤销" : $"撤销：{_history.UndoName}";
    public string RedoLabel => _history.RedoName is null ? "重做" : $"重做：{_history.RedoName}";

    public EntityKind ProjectKind { get => Project.Kind; set => SetProjectValue("修改实体类型", Project.Kind, value, item => Project.Kind = item); }
    public string ProjectId { get => Project.Id; set => SetProjectValue("修改字符串 ID", Project.Id, value, item => Project.Id = item); }
    public string ProjectDisplayName { get => Project.DisplayName; set => SetProjectValue("修改中文名称", Project.DisplayName, value, item => Project.DisplayName = item); }
    public string ProjectDescription { get => Project.Description; set => SetProjectValue("修改介绍", Project.Description, value, item => Project.Description = item); }
    public string ProjectCarrierReanimation { get => Project.CarrierReanimation; set => SetProjectValue("修改载体动画", Project.CarrierReanimation, value, item => Project.CarrierReanimation = item); }
    public AnimationOutputFormat ProjectOutputFormat { get => Project.OutputFormat; set => SetProjectValue("修改导出格式", Project.OutputFormat, value, item => Project.OutputFormat = item); }
    public string? ProjectGameRoot { get => Project.GameRoot; set => SetProjectValue("修改游戏目录", Project.GameRoot, value, item => Project.GameRoot = item); }
    public int ProjectNumericEntityId { get => Project.NumericEntityId; set => SetProjectValue("修改数字 ID", Project.NumericEntityId, value, item => Project.NumericEntityId = item); }
    public int ProjectTemplateEntityId { get => Project.TemplateEntityId; set => SetProjectValue("修改模板 ID", Project.TemplateEntityId, value, item => Project.TemplateEntityId = item); }
    public int ProjectCost { get => Project.Cost; set => SetProjectValue("修改阳光", Project.Cost, value, item => Project.Cost = item); }
    public int ProjectRechargeTime { get => Project.RechargeTime; set => SetProjectValue("修改冷却", Project.RechargeTime, value, item => Project.RechargeTime = item); }
    public int ProjectHealth { get => Project.Health; set => SetProjectValue("修改生命", Project.Health, value, item => Project.Health = item); }
    public int ProjectLaunchRate { get => Project.LaunchRate; set => SetProjectValue("修改攻击间隔", Project.LaunchRate, value, item => Project.LaunchRate = item); }
    public int ProjectProjectileType { get => Project.ProjectileType; set => SetProjectValue("修改子弹类型", Project.ProjectileType, value, item => Project.ProjectileType = item); }
    public int ProjectDamage { get => Project.Damage; set => SetProjectValue("修改伤害", Project.Damage, value, item => Project.Damage = item); }
    public int ProjectShotsPerAttack { get => Project.ShotsPerAttack; set => SetProjectValue("修改每次发射数", Project.ShotsPerAttack, value, item => Project.ShotsPerAttack = item); }
    public float AnimationFps { get => Project.Animation.Fps; set => SetProjectValue("修改 FPS", Project.Animation.Fps, value, item => Project.Animation.Fps = item); }

    public string? SelectedActionId { get => SelectedAction?.Id; set => SetActionValue("修改动作 ID", SelectedAction?.Id, value, item => SelectedAction!.Id = item ?? string.Empty); }
    public string? SelectedActionDisplayName { get => SelectedAction?.DisplayName; set => SetActionValue("修改动作中文名", SelectedAction?.DisplayName, value, item => SelectedAction!.DisplayName = item ?? string.Empty); }
    public string? SelectedActionTrack { get => SelectedAction?.Track; set => SetActionValue("修改动作轨道", SelectedAction?.Track, value, item => SelectedAction!.Track = item ?? string.Empty); }
    public AnimationLoopMode? SelectedActionLoop { get => SelectedAction?.Loop; set => SetActionValue("修改动作循环", SelectedAction?.Loop, value, item => SelectedAction!.Loop = item ?? AnimationLoopMode.Loop); }
    public double? SelectedActionRate { get => SelectedAction?.Rate; set => SetActionValue("修改动作速度", SelectedAction?.Rate, value, item => SelectedAction!.Rate = item ?? Project.Animation.Fps); }
    public int? SelectedActionBlendFrames { get => SelectedAction?.BlendFrames; set => SetActionValue("修改动作混合帧", SelectedAction?.BlendFrames, value, item => SelectedAction!.BlendFrames = item ?? 0); }

    public AnimationTrack? SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            if (!SetField(ref _selectedTrack, value)) return;
            RaiseFrameProperties();
            NotifyVisualChanged();
        }
    }

    public ActionDefinition? SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (!SetField(ref _selectedAction, value)) return;
            var range = ActiveRange;
            _currentFrame = range.Start;
            var visible = TimelineTracks;
            if (_selectedTrack is null || !visible.Contains(_selectedTrack) || _selectedTrack.IsActionTrack)
                _selectedTrack = visible.FirstOrDefault(track => !track.IsActionTrack) ?? visible.FirstOrDefault();
            RaisePropertyChanged(nameof(CurrentFrame));
            RaisePropertyChanged(nameof(SelectedTrack));
            RaiseActionProperties();
            RaiseTimelineProperties();
            RaiseFrameProperties();
            Status = value is null
                ? "已显示完整时间轴"
                : $"动作视图：{value.DisplayName}，原始帧 {range.Start + 1}–{range.End + 1}，仅列出发生变化的轨道";
            NotifyVisualChanged();
        }
    }

    public EditorTool ActiveTool
    {
        get => _activeTool;
        set
        {
            if (!SetField(ref _activeTool, value)) return;
            Status = value switch
            {
                EditorTool.Select => "选择工具 Q：单击部件选择，不修改动画",
                EditorTool.Move => "移动工具 W/G：拖动中心或红/蓝轴；方向键微调，Shift 加速",
                EditorTool.Rotate => "旋转工具 E：拖动黄色圆环",
                _ => "缩放工具 R/S：红色 X、蓝色 Y、黄色对角等比缩放"
            };
            NotifyVisualChanged();
        }
    }

    public int CurrentFrame
    {
        get => _currentFrame;
        set
        {
            var range = ActiveRange;
            value = Math.Clamp(value, range.Start, range.End);
            if (!SetField(ref _currentFrame, value)) return;
            RaiseFrameProperties();
            NotifyVisualChanged();
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetField(ref _isPlaying, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string FrameLabel => IsActionView
        ? $"动作帧 {CurrentFrame - ActiveRange.Start + 1} / {ActiveRange.Count}（原始 {CurrentFrame + 1}）"
        : $"帧 {CurrentFrame + 1} / {Math.Max(1, Project.Animation.FrameCount)}";
    public bool CurrentHasKey => CurrentExplicitFrame?.HasKey ?? false;

    public float? CurrentX { get => CurrentExplicitFrame?.X; set => SetFrameValue("修改 X 位移", frame => frame.X = value); }
    public float? CurrentY { get => CurrentExplicitFrame?.Y; set => SetFrameValue("修改 Y 位移", frame => frame.Y = value); }
    public float? CurrentSkewX { get => CurrentExplicitFrame?.SkewX; set => SetFrameValue("修改 X 旋转", frame => frame.SkewX = value); }
    public float? CurrentSkewY { get => CurrentExplicitFrame?.SkewY; set => SetFrameValue("修改 Y 旋转", frame => frame.SkewY = value); }
    public float? CurrentScaleX { get => CurrentExplicitFrame?.ScaleX; set => SetFrameValue("修改 X 缩放", frame => frame.ScaleX = value); }
    public float? CurrentScaleY { get => CurrentExplicitFrame?.ScaleY; set => SetFrameValue("修改 Y 缩放", frame => frame.ScaleY = value); }
    public float? CurrentVisibilityFrame { get => CurrentExplicitFrame?.Frame; set => SetFrameValue("修改图片子帧", frame => frame.Frame = value); }
    public float? CurrentAlpha { get => CurrentExplicitFrame?.Alpha; set => SetFrameValue("修改透明度", frame => frame.Alpha = value); }
    public string? CurrentImage { get => CurrentExplicitFrame?.Image; set => SetFrameValue("修改图片符号", frame => frame.Image = value); }
    public string? CurrentText { get => CurrentExplicitFrame?.Text; set => SetFrameValue("修改文字", frame => frame.Text = value); }

    public ResolvedAnimationFrame? CurrentResolvedFrame => SelectedTrack?.ResolveFrame(CurrentFrame);

    public void NewProject(EntityKind kind)
    {
        ReplaceProject(CreateDefaultProject(kind));
        Status = kind == EntityKind.Plant ? "已创建新植物工程" : "已创建新僵尸工程";
    }

    public void ReplaceProject(EditorProject project)
    {
        Project = project;
        _currentFrame = 0;
        _selectedTrack = project.Animation.Tracks.FirstOrDefault(track => !track.IsActionTrack)
                         ?? project.Animation.Tracks.FirstOrDefault();
        _selectedAction = null;
        IsPlaying = false;
        _history.Clear();
        RaisePropertyChanged(nameof(CurrentFrame));
        RaisePropertyChanged(nameof(SelectedTrack));
        RaisePropertyChanged(nameof(SelectedAction));
        RaiseAll();
        NotifyVisualChanged();
    }

    public void ReplaceAnimation(AnimationDocument document, string sourcePath)
    {
        Project.Animation = document;
        Project.SourceAnimationPath = sourcePath;
        Project.Actions = _actionCatalog.InferActions(document, Project.Kind);
        _selectedTrack = document.Tracks.FirstOrDefault(track => !track.IsActionTrack)
                         ?? document.Tracks.FirstOrDefault();
        _selectedAction = null;
        _currentFrame = 0;
        _history.Clear();
        RaiseAll();
        NotifyVisualChanged();
    }

    public void InferActions()
    {
        RecordUndo("重新识别动作");
        Project.Actions = _actionCatalog.InferActions(Project.Animation, Project.Kind);
        _selectedAction = null;
        RaisePropertyChanged(nameof(Actions));
        RaisePropertyChanged(nameof(SelectedAction));
        RaiseTimelineProperties();
        Status = $"已识别 {Project.Actions.Count} 个 anim_* 动作；含图片的动作轨道同时保留为可见部件";
        NotifyVisualChanged();
    }

    public void ClearActionView() => SelectedAction = null;

    public void AddAction(ActionTemplate template)
    {
        var action = _actionCatalog.CreateFromTemplate(template, Project.Animation);
        var existing = Project.Actions.FirstOrDefault(item => string.Equals(item.Id, action.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) { SelectedAction = existing; return; }
        RecordUndo("添加动作");
        if (Project.Animation.FindTrack(action.Track) is null)
        {
            var track = new AnimationTrack { Name = action.Track };
            track.EnsureFrameCount(Math.Max(1, Project.Animation.FrameCount));
            track.Frames[0].Frame = 0;
            Project.Animation.Tracks.Add(track);
        }
        Project.Actions.Add(action);
        SelectedAction = action;
        RaisePropertyChanged(nameof(Tracks));
        NotifyVisualChanged();
    }

    public void RemoveSelectedAction()
    {
        if (SelectedAction is null) return;
        RecordUndo("删除动作");
        Project.Actions.Remove(SelectedAction);
        SelectedAction = null;
    }

    public void AddTrack(string name)
    {
        RecordUndo("添加轨道");
        if (string.IsNullOrWhiteSpace(name)) name = "新轨道";
        var baseName = name;
        var suffix = 2;
        while (Project.Animation.FindTrack(name) is not null) name = $"{baseName}_{suffix++}";
        var track = new AnimationTrack { Name = name };
        track.EnsureFrameCount(Math.Max(1, Project.Animation.FrameCount));
        Project.Animation.Tracks.Add(track);
        SelectedTrack = track;
        RaisePropertyChanged(nameof(Tracks));
        RaiseTimelineProperties();
        NotifyVisualChanged();
    }

    public void RemoveSelectedTrack()
    {
        if (SelectedTrack is null || Project.Animation.Tracks.Count <= 1) return;
        RecordUndo("删除轨道");
        var index = Project.Animation.Tracks.IndexOf(SelectedTrack);
        Project.Animation.Tracks.Remove(SelectedTrack);
        SelectedTrack = Project.Animation.Tracks[Math.Clamp(index, 0, Project.Animation.Tracks.Count - 1)];
        RaisePropertyChanged(nameof(Tracks));
        RaiseTimelineProperties();
        NotifyVisualChanged();
    }

    public void SetKeyframe()
    {
        if (SelectedTrack is null) return;
        RecordUndo("设置关键帧");
        var resolved = SelectedTrack.ResolveFrame(CurrentFrame);
        SelectedTrack.Frames[CurrentFrame] = resolved.ToExplicitFrame();
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    public void ClearKeyframe()
    {
        if (CurrentExplicitFrame is null) return;
        RecordUndo("清除关键帧");
        CurrentExplicitFrame.Clear();
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    public void ToggleKeyframe()
    {
        if (CurrentHasKey) ClearKeyframe(); else SetKeyframe();
    }

    public void CreateTween(TweenCurve curve)
    {
        if (SelectedTrack is null) return;
        RecordUndo(curve == TweenCurve.Linear ? "生成线性补间" : "生成平滑补间");
        var count = _tweenService.BakeToNextKeyframe(SelectedTrack, CurrentFrame, curve);
        Status = curve == TweenCurve.Linear
            ? $"已生成 {count} 帧线性补间"
            : $"已生成 {count} 帧平滑缓入缓出补间";
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    public void InsertFrame()
    {
        RecordUndo("插入帧");
        foreach (var track in Project.Animation.Tracks)
            track.Frames.Insert(Math.Min(CurrentFrame, track.Frames.Count), new AnimationFrame());
        RaiseAll();
        NotifyVisualChanged();
    }

    public void DeleteFrame()
    {
        if (Project.Animation.FrameCount <= 1) return;
        RecordUndo("删除帧");
        foreach (var track in Project.Animation.Tracks)
            track.Frames.RemoveAt(Math.Clamp(CurrentFrame, 0, track.Frames.Count - 1));
        _currentFrame = Math.Min(CurrentFrame, Project.Animation.FrameCount - 1);
        RaiseAll();
        NotifyVisualChanged();
    }

    public void BeginEditTransaction(string name)
    {
        if (_editTransactionActive) return;
        RecordUndo(name);
        _editTransactionActive = true;
    }

    public void EndEditTransaction() => _editTransactionActive = false;

    public void MoveSelected(float deltaX, float deltaY)
    {
        if (SelectedTrack is null || SelectedTrack.IsActionTrack) return;
        RecordUndo("移动部件");
        var resolved = SelectedTrack.ResolveFrame(CurrentFrame);
        var frame = EnsureCurrentFrame();
        frame.X = resolved.X + deltaX;
        frame.Y = resolved.Y + deltaY;
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    public void ScaleSelected(float factorX, float factorY)
    {
        if (SelectedTrack is null || SelectedTrack.IsActionTrack) return;
        RecordUndo("缩放部件");
        var resolved = SelectedTrack.ResolveFrame(CurrentFrame);
        var frame = EnsureCurrentFrame();
        frame.ScaleX = Math.Clamp(resolved.ScaleX * factorX, -20f, 20f);
        frame.ScaleY = Math.Clamp(resolved.ScaleY * factorY, -20f, 20f);
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    public void RotateSelected(float deltaDegrees)
    {
        if (SelectedTrack is null || SelectedTrack.IsActionTrack) return;
        RecordUndo("旋转部件");
        var resolved = SelectedTrack.ResolveFrame(CurrentFrame);
        var frame = EnsureCurrentFrame();
        frame.SkewX = resolved.SkewX + deltaDegrees;
        frame.SkewY = resolved.SkewY + deltaDegrees;
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    public void Undo()
    {
        var entry = _history.Undo(CreateSnapshot());
        if (entry is null) return;
        RestoreSnapshot(entry.Value.Snapshot);
        Status = $"已撤销：{entry.Value.Name}";
    }

    public void Redo()
    {
        var entry = _history.Redo(CreateSnapshot());
        if (entry is null) return;
        RestoreSnapshot(entry.Value.Snapshot);
        Status = $"已重做：{entry.Value.Name}";
    }

    public void StepPlayback()
    {
        if (!IsPlaying || Project.Animation.FrameCount == 0) return;
        var range = ActiveRange;
        CurrentFrame = CurrentFrame >= range.End ? range.Start : CurrentFrame + 1;
    }

    public bool IsMeaningfulKey(AnimationTrack track, int frameIndex) =>
        _actionView.HasMeaningfulChange(track, frameIndex, ActiveRange.Start);

    private AnimationFrame? CurrentExplicitFrame =>
        SelectedTrack is null || SelectedTrack.Frames.Count == 0
            ? null
            : SelectedTrack.Frames[Math.Clamp(CurrentFrame, 0, SelectedTrack.Frames.Count - 1)];

    private AnimationFrame EnsureCurrentFrame()
    {
        if (SelectedTrack is null) throw new InvalidOperationException("没有选中的轨道。 ");
        SelectedTrack.EnsureFrameCount(Math.Max(1, Project.Animation.FrameCount));
        return SelectedTrack.Frames[Math.Clamp(CurrentFrame, 0, SelectedTrack.Frames.Count - 1)];
    }

    private void SetFrameValue(string historyName, Action<AnimationFrame> setter)
    {
        if (SelectedTrack is null) return;
        RecordUndo(historyName);
        setter(EnsureCurrentFrame());
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    private void SetProjectValue<T>(string historyName, T current, T value, Action<T> setter)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        RecordUndo(historyName);
        setter(value);
        RaiseProjectEditProperties();
        NotifyVisualChanged();
    }

    private void SetActionValue<T>(string historyName, T current, T value, Action<T> setter)
    {
        if (SelectedAction is null || EqualityComparer<T>.Default.Equals(current, value)) return;
        RecordUndo(historyName);
        setter(value);
        RaiseActionProperties();
        RaisePropertyChanged(nameof(Actions));
        RaiseTimelineProperties();
        NotifyVisualChanged();
    }

    private void RecordUndo(string name)
    {
        if (_editTransactionActive) return;
        _history.Record(name, CreateSnapshot());
        RaiseHistoryProperties();
    }

    private EditorSnapshot CreateSnapshot() => new(
        _cloneService.Clone(Project),
        CurrentFrame,
        SelectedTrack is null ? -1 : Project.Animation.Tracks.IndexOf(SelectedTrack),
        SelectedAction is null ? -1 : Project.Actions.IndexOf(SelectedAction));

    private void RestoreSnapshot(EditorSnapshot snapshot)
    {
        Project = snapshot.Project;
        _selectedTrack = snapshot.SelectedTrackIndex >= 0 && snapshot.SelectedTrackIndex < Project.Animation.Tracks.Count
            ? Project.Animation.Tracks[snapshot.SelectedTrackIndex]
            : Project.Animation.Tracks.FirstOrDefault(track => !track.IsActionTrack);
        _selectedAction = snapshot.SelectedActionIndex >= 0 && snapshot.SelectedActionIndex < Project.Actions.Count
            ? Project.Actions[snapshot.SelectedActionIndex]
            : null;
        var range = ActiveRange;
        _currentFrame = Math.Clamp(snapshot.CurrentFrame, range.Start, range.End);
        _editTransactionActive = false;
        RaisePropertyChanged(nameof(SelectedTrack));
        RaisePropertyChanged(nameof(SelectedAction));
        RaiseActionProperties();
        RaiseAll();
        RaiseHistoryProperties();
        NotifyVisualChanged();
    }

    private void RaiseHistoryProperties()
    {
        RaisePropertyChanged(nameof(CanUndo));
        RaisePropertyChanged(nameof(CanRedo));
        RaisePropertyChanged(nameof(UndoLabel));
        RaisePropertyChanged(nameof(RedoLabel));
    }

    private void RaiseTimelineProperties()
    {
        RaisePropertyChanged(nameof(TimelineTracks));
        RaisePropertyChanged(nameof(ActiveRange));
        RaisePropertyChanged(nameof(TimelineFrameStart));
        RaisePropertyChanged(nameof(TimelineFrameEnd));
        RaisePropertyChanged(nameof(TimelineFrameCount));
        RaisePropertyChanged(nameof(IsActionView));
        RaisePropertyChanged(nameof(FrameLabel));
    }

    private void RaiseFrameProperties()
    {
        foreach (var property in new[]
                 {
                     nameof(FrameLabel), nameof(CurrentHasKey), nameof(CurrentX), nameof(CurrentY),
                     nameof(CurrentSkewX), nameof(CurrentSkewY), nameof(CurrentScaleX), nameof(CurrentScaleY),
                     nameof(CurrentVisibilityFrame), nameof(CurrentAlpha), nameof(CurrentImage), nameof(CurrentText),
                     nameof(CurrentResolvedFrame)
                 })
            RaisePropertyChanged(property);
    }

    private void RaiseProjectEditProperties()
    {
        foreach (var property in new[]
                 {
                     nameof(ProjectKind), nameof(ProjectId), nameof(ProjectDisplayName), nameof(ProjectDescription),
                     nameof(ProjectCarrierReanimation), nameof(ProjectOutputFormat), nameof(ProjectGameRoot),
                     nameof(ProjectNumericEntityId), nameof(ProjectTemplateEntityId), nameof(ProjectCost),
                     nameof(ProjectRechargeTime), nameof(ProjectHealth), nameof(ProjectLaunchRate),
                     nameof(ProjectProjectileType), nameof(ProjectDamage), nameof(ProjectShotsPerAttack),
                     nameof(AnimationFps)
                 })
            RaisePropertyChanged(property);
    }

    private void RaiseActionProperties()
    {
        foreach (var property in new[]
                 {
                     nameof(SelectedActionId), nameof(SelectedActionDisplayName), nameof(SelectedActionTrack),
                     nameof(SelectedActionLoop), nameof(SelectedActionRate), nameof(SelectedActionBlendFrames)
                 })
            RaisePropertyChanged(property);
    }

    private void RaiseAll()
    {
        RaisePropertyChanged(nameof(Project));
        RaisePropertyChanged(nameof(Tracks));
        RaisePropertyChanged(nameof(Actions));
        RaiseProjectEditProperties();
        RaiseActionProperties();
        RaiseTimelineProperties();
        RaiseFrameProperties();
        RaiseHistoryProperties();
    }

    private void NotifyVisualChanged() => VisualStateChanged?.Invoke(this, EventArgs.Empty);

    private EditorProject CreateDefaultProject(EntityKind kind)
    {
        var document = new AnimationDocument { Fps = 12 };
        var idle = new AnimationTrack { Name = "anim_idle" };
        var blink = new AnimationTrack { Name = "anim_blink" };
        var body = new AnimationTrack { Name = "body" };
        idle.EnsureFrameCount(60);
        blink.EnsureFrameCount(60);
        body.EnsureFrameCount(60);
        idle.Frames[0].Frame = 0;
        blink.Frames[0].Frame = -1;
        blink.Frames[48].Frame = 0;
        blink.Frames[54].Frame = -1;
        body.Frames[0].Frame = 0;
        document.Tracks.Add(idle);
        document.Tracks.Add(blink);
        document.Tracks.Add(body);
        var project = new EditorProject
        {
            Kind = kind,
            Id = kind == EntityKind.Zombie ? "NEW_ZOMBIE" : "NEW_PLANT",
            DisplayName = kind == EntityKind.Zombie ? "新僵尸" : "新植物",
            CarrierReanimation = kind == EntityKind.Zombie ? "REANIM_ZOMBIE" : "REANIM_PEASHOOTER",
            Animation = document
        };
        project.Actions = _actionCatalog.InferActions(document, kind);
        return project;
    }
}
