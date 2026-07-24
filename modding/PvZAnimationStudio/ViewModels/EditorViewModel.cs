using System.Collections.ObjectModel;
using PvZAnimationStudio.Models;
using PvZAnimationStudio.Services;

namespace PvZAnimationStudio.ViewModels;

public readonly record struct TimelineKeySelection(string TrackId, int Frame);
public readonly record struct CurveKeySelection(CurveChannel Channel, int Frame);

public sealed class EditorViewModel : ObservableObject
{
    private sealed record CopiedCurveKey(
        CurveChannel Channel, CurveInterpolationMode Interpolation, CurveKeyDefinition Key);

    private sealed record CopiedTimelineKey(
        int Offset, string? Image, string? Font, string? Text, IReadOnlyList<CopiedCurveKey> CurveKeys);

    private sealed record TimelineKeyClipboard(
        bool IsActionTrack, IReadOnlyList<CopiedTimelineKey> Keys);

    private readonly ActionCatalogService _actionCatalog;
    private readonly TweenService _tweenService = new();
    private readonly ActionViewService _actionView = new();
    private readonly ProjectCloneService _cloneService = new();
    private readonly EditHistoryService _history = new();
    private readonly AnimationCurveService _curveService = new();
    private EditorProject _project;
    private AnimationTrack? _selectedTrack;
    private ActionDefinition? _selectedAction;
    private AnimationEventDefinition? _selectedEvent;
    private int _currentFrame;
    private bool _isPlaying;
    private string _status = "就绪";
    private EditorTool _activeTool = EditorTool.Move;
    private bool _editTransactionActive;
    private TimelineKeyClipboard? _timelineKeyClipboard;

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
    public IReadOnlyList<PlantTemplateDefinition> PlantTemplates => PlantTemplateCatalog.RuntimeTemplates;
    public IReadOnlyList<AnimationTrack> TimelineTracks => _actionView.GetTimelineTracks(Project.Animation, SelectedAction);
    public ActionFrameRange ActiveRange => _actionView.GetRange(Project.Animation, SelectedAction);
    public int TimelineFrameStart => ActiveRange.Start;
    public int TimelineFrameEnd => ActiveRange.End;
    public int TimelineFrameCount => ActiveRange.Count;
    public bool IsActionView => SelectedAction is not null;
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public bool IsPlantProject => Project.Kind == EntityKind.Plant;
    public string UndoLabel => _history.UndoName is null ? "撤销" : $"撤销：{_history.UndoName}";
    public string RedoLabel => _history.RedoName is null ? "重做" : $"重做：{_history.RedoName}";

    public EntityKind ProjectKind
    {
        get => Project.Kind;
        set
        {
            if (Project.Kind == value) return;
            RecordUndo("修改实体类型");
            Project.Kind = value;
            SynchronizeTemplateCarrier();
            RaiseProjectEditProperties();
            NotifyVisualChanged();
        }
    }
    public string ProjectId { get => Project.Id; set => SetProjectValue("修改字符串 ID", Project.Id, value, item => Project.Id = item); }
    public string ProjectDisplayName { get => Project.DisplayName; set => SetProjectValue("修改中文名称", Project.DisplayName, value, item => Project.DisplayName = item); }
    public string ProjectDescription { get => Project.Description; set => SetProjectValue("修改介绍", Project.Description, value, item => Project.Description = item); }
    public string ProjectCarrierReanimation { get => Project.CarrierReanimation; set => SetProjectValue("修改载体动画", Project.CarrierReanimation, value, item => Project.CarrierReanimation = item); }
    public string ProjectInitialActionId { get => Project.InitialActionId; set => SetProjectValue("修改初始动作", Project.InitialActionId, value, item => Project.InitialActionId = item); }
    public bool ProjectHideTemplateAttachments { get => Project.HideTemplateAttachments; set => SetProjectValue("修改模板附件显示", Project.HideTemplateAttachments, value, item => Project.HideTemplateAttachments = item); }
    public AnimationOutputFormat ProjectOutputFormat { get => Project.OutputFormat; set => SetProjectValue("修改导出格式", Project.OutputFormat, value, item => Project.OutputFormat = item); }
    public string? ProjectGameRoot { get => Project.GameRoot; set => SetProjectValue("修改游戏目录", Project.GameRoot, value, item => Project.GameRoot = item); }
    public int ProjectNumericEntityId { get => Project.NumericEntityId; set => SetProjectValue("修改数字 ID", Project.NumericEntityId, value, item => Project.NumericEntityId = item); }
    public int ProjectTemplateEntityId
    {
        get => Project.TemplateEntityId;
        set
        {
            if (Project.TemplateEntityId == value) return;
            RecordUndo("修改模板 ID");
            Project.TemplateEntityId = value;
            SynchronizeTemplateCarrier();
            RaiseProjectEditProperties();
            NotifyVisualChanged();
        }
    }
    public string ProjectTemplateSummary
    {
        get
        {
            if (Project.Kind == EntityKind.Plant)
            {
                var plant = PlantTemplateCatalog.Find(ProjectTemplateEntityId);
                return plant?.Summary ?? $"未知植物模板 ID：{ProjectTemplateEntityId}。";
            }
            if (Project.Kind == EntityKind.Zombie)
            {
                var zombie = ZombieTemplateCatalog.Find(ProjectTemplateEntityId);
                return zombie?.Summary ?? $"未知僵尸模板 ID：{ProjectTemplateEntityId}。";
            }
            return "UI 和其他工程不使用实体模板目录。";
        }
    }
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
    public string SelectedActionReplacesCsv
    {
        get => SelectedAction is null ? string.Empty : string.Join(", ", SelectedAction.Replaces);
        set
        {
            if (SelectedAction is null) return;
            var replacements = (value ?? string.Empty)
                .Split([',', ';', '\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var normalized = string.Join(", ", replacements);
            if (string.Equals(SelectedActionReplacesCsv, normalized, StringComparison.OrdinalIgnoreCase)) return;
            RecordUndo("修改原版动作映射");
            SelectedAction.Replaces = new ObservableCollection<string>(replacements);
            RaiseActionProperties();
        }
    }

    public ObservableCollection<AnimationEventDefinition>? SelectedActionEvents => SelectedAction?.Events;

    public AnimationEventDefinition? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            if (!SetField(ref _selectedEvent, value)) return;
            RaiseEventProperties();
        }
    }

    public string? SelectedEventId { get => SelectedEvent?.Id; set => SetEventValue("修改事件 ID", SelectedEvent?.Id, value, item => SelectedEvent!.Id = item ?? string.Empty); }
    public int? SelectedEventFrame
    {
        get => SelectedEvent?.Frame;
        set
        {
            if (SelectedEvent is null || SelectedEvent.Frame == value) return;
            RecordUndo("修改事件帧");
            SelectedEvent.Frame = value;
            if (value.HasValue) SelectedEvent.NormalizedTime = null;
            RaiseEventProperties();
            RaisePropertyChanged(nameof(SelectedActionEvents));
        }
    }
    public double? SelectedEventNormalizedTime
    {
        get => SelectedEvent?.NormalizedTime;
        set
        {
            if (SelectedEvent is null || SelectedEvent.NormalizedTime == value) return;
            RecordUndo("修改事件时间");
            SelectedEvent.NormalizedTime = value;
            if (value.HasValue) SelectedEvent.Frame = null;
            RaiseEventProperties();
            RaisePropertyChanged(nameof(SelectedActionEvents));
        }
    }
    public bool? SelectedEventOncePerLoop { get => SelectedEvent?.OncePerLoop; set => SetEventValue("修改事件循环设置", SelectedEvent?.OncePerLoop, value, item => SelectedEvent!.OncePerLoop = item ?? true); }
    public string? SelectedEventTargetAction { get => SelectedEvent?.TargetAction; set => SetEventValue("修改事件目标动作", SelectedEvent?.TargetAction, value, item => SelectedEvent!.TargetAction = item ?? string.Empty); }

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
            _selectedEvent = value?.Events.FirstOrDefault();
            var range = ActiveRange;
            _currentFrame = range.Start;
            var visible = TimelineTracks;
            if (_selectedTrack is null || !visible.Contains(_selectedTrack) || _selectedTrack.IsActionTrack)
                _selectedTrack = visible.FirstOrDefault(track => !track.IsActionTrack) ?? visible.FirstOrDefault();
            RaisePropertyChanged(nameof(CurrentFrame));
            RaisePropertyChanged(nameof(SelectedTrack));
            RaiseActionProperties();
            RaiseEventProperties();
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
                EditorTool.Move => "移动工具 W/G：W 切换操纵器，G 进入鼠标移动；方向键微调",
                EditorTool.Rotate => "旋转工具 E/R：E 切换操纵器，R 围绕图片中心跟随鼠标旋转",
                _ => "缩放工具 S：按 S 进入鼠标等比缩放，或拖红色 X、蓝色 Y、黄色对角手柄"
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
    public bool CurrentHasKey => SelectedTrack is not null &&
                                 _curveService.HasTimelineKey(Project, SelectedTrack, CurrentFrame);

    public float? CurrentX { get => CurrentExplicitFrame?.X; set => SetFrameValue("修改 X 位移", CurveChannel.X, frame => frame.X = value); }
    public float? CurrentY { get => CurrentExplicitFrame?.Y; set => SetFrameValue("修改 Y 位移", CurveChannel.Y, frame => frame.Y = value); }
    public float? CurrentSkewX { get => CurrentExplicitFrame?.SkewX; set => SetFrameValue("修改 X 旋转", CurveChannel.SkewX, frame => frame.SkewX = value); }
    public float? CurrentSkewY { get => CurrentExplicitFrame?.SkewY; set => SetFrameValue("修改 Y 旋转", CurveChannel.SkewY, frame => frame.SkewY = value); }
    public float? CurrentScaleX { get => CurrentExplicitFrame?.ScaleX; set => SetFrameValue("修改 X 缩放", CurveChannel.ScaleX, frame => frame.ScaleX = value); }
    public float? CurrentScaleY { get => CurrentExplicitFrame?.ScaleY; set => SetFrameValue("修改 Y 缩放", CurveChannel.ScaleY, frame => frame.ScaleY = value); }
    public float? CurrentVisibilityFrame { get => CurrentExplicitFrame?.Frame; set => SetFrameValue("修改图片子帧", CurveChannel.Frame, frame => frame.Frame = value); }
    public float? CurrentAlpha { get => CurrentExplicitFrame?.Alpha; set => SetFrameValue("修改透明度", CurveChannel.Alpha, frame => frame.Alpha = value); }
    public string? CurrentImage { get => CurrentExplicitFrame?.Image; set => SetFrameValue("修改图片符号", null, frame => frame.Image = value); }
    public string? CurrentText { get => CurrentExplicitFrame?.Text; set => SetFrameValue("修改文字", null, frame => frame.Text = value); }

    public ResolvedAnimationFrame? CurrentResolvedFrame => SelectedTrack?.ResolveFrame(CurrentFrame);

    public void NewProject(EntityKind kind)
    {
        ReplaceProject(CreateDefaultProject(kind));
        Status = kind == EntityKind.Plant ? "已创建新植物工程" : "已创建新僵尸工程";
    }

    public void ReplaceProject(EditorProject project)
    {
        _curveService.NormalizeActionMarkerFrames(project);
        Project = project;
        _currentFrame = 0;
        _selectedTrack = project.Animation.Tracks.FirstOrDefault(track => !track.IsActionTrack)
                         ?? project.Animation.Tracks.FirstOrDefault();
        _selectedAction = null;
        _selectedEvent = null;
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
        Project.Curves.Clear();
        _curveService.NormalizeActionMarkerFrames(Project);
        Project.SourceAnimationPath = sourcePath;
        Project.Actions = _actionCatalog.InferActions(document, Project.Kind);
        _selectedTrack = document.Tracks.FirstOrDefault(track => !track.IsActionTrack)
                         ?? document.Tracks.FirstOrDefault();
        _selectedAction = null;
        _selectedEvent = null;
        _currentFrame = 0;
        _history.Clear();
        RaisePropertyChanged(nameof(SelectedTrack));
        RaisePropertyChanged(nameof(SelectedAction));
        RaisePropertyChanged(nameof(CurrentFrame));
        RaiseAll();
        NotifyVisualChanged();
    }

    public void InferActions()
    {
        RecordUndo("重新识别动作");
        Project.Actions = _actionCatalog.InferActions(Project.Animation, Project.Kind);
        _selectedAction = null;
        _selectedEvent = null;
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

    public void AddAnimationEvent()
    {
        if (SelectedAction is null) return;
        RecordUndo("添加动作事件");
        var animationEvent = new AnimationEventDefinition
        {
            Id = "FIRE_PROJECTILE",
            Frame = Math.Max(0, CurrentFrame - ActiveRange.Start),
            OncePerLoop = true
        };
        SelectedAction.Events.Add(animationEvent);
        SelectedEvent = animationEvent;
        RaisePropertyChanged(nameof(SelectedActionEvents));
    }

    public void RemoveSelectedAnimationEvent()
    {
        if (SelectedAction is null || SelectedEvent is null) return;
        RecordUndo("删除动作事件");
        var index = SelectedAction.Events.IndexOf(SelectedEvent);
        SelectedAction.Events.Remove(SelectedEvent);
        SelectedEvent = SelectedAction.Events.Count == 0
            ? null
            : SelectedAction.Events[Math.Clamp(index, 0, SelectedAction.Events.Count - 1)];
        RaisePropertyChanged(nameof(SelectedActionEvents));
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

    public AnimationTrack AddImageTrack(string name, string imageSymbol, float x, float y)
    {
        RecordUndo("拖入图片轨道");
        if (string.IsNullOrWhiteSpace(name)) name = "图片部件";
        var baseName = name;
        var suffix = 2;
        while (Project.Animation.FindTrack(name) is not null) name = $"{baseName}_{suffix++}";
        var track = new AnimationTrack { Name = name };
        track.EnsureFrameCount(Math.Max(1, Project.Animation.FrameCount));
        var frame = track.Frames[Math.Clamp(CurrentFrame, 0, track.Frames.Count - 1)];
        frame.X = x;
        frame.Y = y;
        frame.ScaleX = 1;
        frame.ScaleY = 1;
        frame.Frame = 0;
        frame.Alpha = 1;
        frame.Image = imageSymbol;
        Project.Animation.Tracks.Add(track);
        SelectedTrack = track;
        RaisePropertyChanged(nameof(Tracks));
        RaiseTimelineProperties();
        RaiseFrameProperties();
        NotifyVisualChanged();
        return track;
    }

    public void RemoveSelectedTrack()
    {
        if (SelectedTrack is null || Project.Animation.Tracks.Count <= 1) return;
        RecordUndo("删除轨道");
        var index = Project.Animation.Tracks.IndexOf(SelectedTrack);
        foreach (var curve in Project.Curves.Where(curve => curve.TrackId == SelectedTrack.EditorId).ToArray())
            Project.Curves.Remove(curve);
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
        var curves = new List<AnimationCurveDefinition>();
        foreach (var channel in Enum.GetValues<CurveChannel>())
        {
            var key = _curveService.EnsureKey(Project, SelectedTrack, channel, CurrentFrame);
            key.Value = _curveService.GetValue(SelectedTrack, channel, CurrentFrame);
            curves.Add(_curveService.EnsureCurve(Project, SelectedTrack, channel));
        }
        foreach (var curve in curves) _curveService.BakeCurve(Project, SelectedTrack, curve);
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    public void ClearKeyframe()
    {
        DeleteCurrentKeyframe();
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
        _curveService.CaptureExplicitMotionCurves(Project);
        foreach (var track in Project.Animation.Tracks)
            track.Frames.Insert(Math.Min(CurrentFrame, track.Frames.Count), new AnimationFrame());
        _curveService.ShiftForInsertedFrame(Project, CurrentFrame);
        _curveService.BakeAllCurves(Project);
        RaiseAll();
        NotifyVisualChanged();
    }

    public void DeleteFrame()
    {
        if (Project.Animation.FrameCount <= 1) return;
        RecordUndo("删除帧");
        _curveService.CaptureExplicitMotionCurves(Project);
        foreach (var track in Project.Animation.Tracks)
            track.Frames.RemoveAt(Math.Clamp(CurrentFrame, 0, track.Frames.Count - 1));
        _curveService.ShiftForDeletedFrame(Project, CurrentFrame);
        _curveService.BakeAllCurves(Project);
        _currentFrame = Math.Min(CurrentFrame, Project.Animation.FrameCount - 1);
        RaiseAll();
        NotifyVisualChanged();
    }

    public bool MoveCurrentKeyframe(int targetFrame)
    {
        if (SelectedTrack is null || !CurrentHasKey) return false;
        var sourceFrame = CurrentFrame;
        targetFrame = Math.Clamp(targetFrame, TimelineFrameStart, TimelineFrameEnd);
        if (sourceFrame == targetFrame) return false;
        RecordUndo("移动轨道关键帧");
        _curveService.CaptureExplicitMotionCurves(Project, SelectedTrack);
        SwapTimelineFrames(SelectedTrack, sourceFrame, targetFrame);
        _curveService.MoveFrameKeys(Project, SelectedTrack, sourceFrame, targetFrame);
        _currentFrame = targetFrame;
        RaisePropertyChanged(nameof(CurrentFrame));
        RaiseFrameProperties();
        Status = $"已将 {SelectedTrack.Name} 的关键帧移动到第 {targetFrame + 1} 帧";
        NotifyVisualChanged();
        return true;
    }

    public IReadOnlyCollection<TimelineKeySelection> MoveTimelineKeys(
        IReadOnlyCollection<TimelineKeySelection> selection, int offset)
    {
        if (selection.Count == 0 || offset == 0) return selection;
        RecordUndo("批量移动轨道关键帧");
        foreach (var trackId in selection.Select(item => item.TrackId).Distinct())
        {
            var selectedTrack = Project.Animation.Tracks.FirstOrDefault(item => item.EditorId == trackId);
            if (selectedTrack is not null) _curveService.CaptureExplicitMotionCurves(Project, selectedTrack);
        }
        var result = selection.ToHashSet();
        var direction = Math.Sign(offset);
        for (var step = 0; step < Math.Abs(offset); step++)
        {
            foreach (var group in result.GroupBy(item => item.TrackId).ToArray())
            {
                var track = Project.Animation.Tracks.FirstOrDefault(item => item.EditorId == group.Key);
                if (track is null) continue;
                var frames = group.Select(item => item.Frame)
                    .OrderBy(frame => direction > 0 ? -frame : frame).ToArray();
                foreach (var source in frames)
                {
                    var target = source + direction;
                    if (target < TimelineFrameStart || target > TimelineFrameEnd) continue;
                    SwapTimelineFrames(track, source, target);
                    _curveService.MoveFrameKeys(Project, track, source, target);
                    result.Remove(new TimelineKeySelection(track.EditorId, source));
                    result.Add(new TimelineKeySelection(track.EditorId, target));
                }
            }
        }
        var primary = result.FirstOrDefault();
        var primaryTrack = Project.Animation.Tracks.FirstOrDefault(item => item.EditorId == primary.TrackId);
        if (primaryTrack is not null)
        {
            _selectedTrack = primaryTrack;
            _currentFrame = primary.Frame;
            RaisePropertyChanged(nameof(SelectedTrack));
            RaisePropertyChanged(nameof(CurrentFrame));
        }
        RaiseFrameProperties();
        NotifyVisualChanged();
        return result;
    }

    public void DeleteTimelineKeys(IReadOnlyCollection<TimelineKeySelection> selection)
    {
        if (selection.Count == 0) return;
        RecordUndo("批量删除轨道关键帧");
        // A compiled animation or an older project can contain explicit motion
        // keys without .pvza curve metadata. Capture those keys before clearing
        // any selected frame so the surviving neighbours can be re-tweened.
        foreach (var group in selection.GroupBy(item => item.TrackId))
        {
            var track = Project.Animation.Tracks.FirstOrDefault(item => item.EditorId == group.Key);
            if (track is null) continue;
            _curveService.CaptureExplicitMotionCurves(Project, track);
            foreach (var frame in group.Select(item => item.Frame).Distinct())
            {
                if (frame < 0 || frame >= track.Frames.Count) continue;
                track.Frames[frame].Clear();
                _curveService.DeleteFrameKeys(Project, track, frame);
            }
        }
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    public bool NudgeCurrentKeyframe(int offset) => MoveCurrentKeyframe(CurrentFrame + offset);

    public void DeleteCurrentKeyframe()
    {
        if (SelectedTrack is null || !CurrentHasKey) return;
        RecordUndo("删除轨道关键帧");
        _curveService.CaptureExplicitMotionCurves(Project, SelectedTrack);
        SelectedTrack.Frames[CurrentFrame].Clear();
        _curveService.DeleteFrameKeys(Project, SelectedTrack, CurrentFrame);
        Status = $"已删除 {SelectedTrack.Name} 第 {CurrentFrame + 1} 帧的关键帧";
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    public int CopyTimelineKeys(IReadOnlyCollection<TimelineKeySelection> selection)
    {
        if (SelectedTrack is null) return 0;
        var requested = selection.Count > 0
            ? selection.ToArray()
            : [new TimelineKeySelection(SelectedTrack.EditorId, CurrentFrame)];
        var trackIds = requested.Select(item => item.TrackId).Distinct().ToArray();
        if (trackIds.Length != 1)
        {
            Status = "一次只能复制一个轨道的关键帧；请只框选同一轨道。";
            return 0;
        }

        var sourceTrack = Project.Animation.Tracks.FirstOrDefault(track => track.EditorId == trackIds[0]);
        if (sourceTrack is null) return 0;
        var frames = requested.Select(item => item.Frame).Distinct().OrderBy(frame => frame)
            .Where(frame => frame >= 0 && frame < sourceTrack.Frames.Count &&
                            IsMeaningfulKey(sourceTrack, frame))
            .ToArray();
        if (frames.Length == 0)
        {
            Status = "当前轨道和帧没有可复制的关键帧。";
            return 0;
        }

        var origin = frames[0];
        var copied = new List<CopiedTimelineKey>(frames.Length);
        foreach (var frame in frames)
        {
            var explicitFrame = sourceTrack.Frames[frame];
            var curveKeys = new List<CopiedCurveKey>();
            foreach (var channel in Enum.GetValues<CurveChannel>())
            {
                var curve = _curveService.FindCurve(Project, sourceTrack, channel);
                var existing = curve?.Keys.FirstOrDefault(key => key.Frame == frame);
                if (existing is not null)
                {
                    curveKeys.Add(new CopiedCurveKey(channel, curve!.Interpolation, existing.Clone()));
                    continue;
                }

                // With no editor curve metadata, an explicit Reanimation value
                // is an authored key. Once a curve exists, its baked samples are
                // deliberately not copied as additional keyframes.
                if (curve is not null) continue;
                var value = AnimationCurveService.GetExplicitValue(explicitFrame, channel);
                if (!value.HasValue) continue;
                curveKeys.Add(new CopiedCurveKey(channel,
                    channel == CurveChannel.Frame
                        ? CurveInterpolationMode.Constant
                        : CurveInterpolationMode.Bezier,
                    new CurveKeyDefinition { Frame = frame, Value = value.Value }));
            }
            copied.Add(new CopiedTimelineKey(frame - origin, explicitFrame.Image,
                explicitFrame.Font, explicitFrame.Text, curveKeys));
        }

        _timelineKeyClipboard = new TimelineKeyClipboard(sourceTrack.IsActionTrack, copied);
        Status = $"已复制轨道 {sourceTrack.Name} 的 {copied.Count} 个关键帧；在目标轨道按 Ctrl+V 粘贴。";
        return copied.Count;
    }

    public int CopyCurrentTrackKeyframes()
    {
        if (SelectedTrack is null) return 0;
        var selection = Enumerable.Range(TimelineFrameStart, TimelineFrameCount)
            .Where(frame => IsMeaningfulKey(SelectedTrack, frame))
            .Select(frame => new TimelineKeySelection(SelectedTrack.EditorId, frame))
            .ToArray();
        return CopyTimelineKeys(selection);
    }

    public IReadOnlyCollection<TimelineKeySelection> PasteTimelineKeys()
    {
        if (SelectedTrack is null || _timelineKeyClipboard is null)
        {
            Status = "还没有复制关键帧。";
            return [];
        }
        if (SelectedTrack.IsActionTrack != _timelineKeyClipboard.IsActionTrack)
        {
            Status = "动作标记轨道与普通视觉轨道不能互相粘贴关键帧。";
            return [];
        }

        var destinationStart = CurrentFrame;
        var destinationEnd = destinationStart + _timelineKeyClipboard.Keys.Max(key => key.Offset);
        if (destinationEnd >= 20000)
        {
            Status = "粘贴结果超过 20000 帧限制。";
            return [];
        }
        if (SelectedAction is not null && destinationEnd > TimelineFrameEnd)
        {
            Status = "目标动作剩余帧数不足；请先插入空帧，或切换到完整时间轴后粘贴。";
            return [];
        }

        RecordUndo("粘贴轨道关键帧");
        if (destinationEnd >= Project.Animation.FrameCount)
            Project.Animation.EnsureUniformFrameCount(destinationEnd + 1);
        var targetTrack = SelectedTrack;
        var targetFrames = _timelineKeyClipboard.Keys
            .Select(key => destinationStart + key.Offset).Distinct().ToArray();
        _curveService.RemoveFrameKeysWithoutBake(Project, targetTrack, targetFrames);
        foreach (var frame in targetFrames) targetTrack.Frames[frame].Clear();

        foreach (var copied in _timelineKeyClipboard.Keys)
        {
            var targetFrame = destinationStart + copied.Offset;
            foreach (var curveKey in copied.CurveKeys)
                _curveService.PutCopiedKey(Project, targetTrack, curveKey.Channel,
                    targetFrame, curveKey.Key, curveKey.Interpolation);
        }
        _curveService.BakeTrackCurves(Project, targetTrack);

        // Strings are discrete and are restored after numeric curve baking.
        foreach (var copied in _timelineKeyClipboard.Keys)
        {
            var frame = targetTrack.Frames[destinationStart + copied.Offset];
            if (copied.Image is not null) frame.Image = copied.Image;
            if (copied.Font is not null) frame.Font = copied.Font;
            if (copied.Text is not null) frame.Text = copied.Text;
        }
        _curveService.NormalizeActionMarkerFrames(Project);

        _currentFrame = destinationStart;
        RaisePropertyChanged(nameof(CurrentFrame));
        RaiseAll();
        Status = $"已将 {_timelineKeyClipboard.Keys.Count} 个关键帧粘贴到轨道 {targetTrack.Name}，起始帧 {destinationStart + 1}。";
        NotifyVisualChanged();
        return targetFrames.Select(frame => new TimelineKeySelection(targetTrack.EditorId, frame)).ToArray();
    }

    public IReadOnlyList<CurveChannel> GetCurveChannels() => SelectedTrack is null
        ? []
        : _curveService.GetAvailableChannels(Project, SelectedTrack);

    public AnimationCurveDefinition? GetCurve(CurveChannel channel, bool create = false) => SelectedTrack is null
        ? null
        : create ? _curveService.EnsureCurve(Project, SelectedTrack, channel)
                 : _curveService.FindCurve(Project, SelectedTrack, channel);

    public AnimationCurveDefinition? GetCurveForDisplay(CurveChannel channel) => SelectedTrack is null
        ? null
        : _curveService.GetCurveForDisplay(Project, SelectedTrack, channel);

    public IReadOnlyList<int> GetCurveKeyFrames(CurveChannel channel) => SelectedTrack is null
        ? []
        : _curveService.GetKeyFrames(Project, SelectedTrack, channel);

    public float GetCurveValue(CurveChannel channel, int frame) => SelectedTrack is null
        ? 0
        : _curveService.GetValue(SelectedTrack, channel, frame);

    public CurveHandlePair GetCurveHandles(CurveChannel channel, AnimationCurveDefinition curve, CurveKeyDefinition key)
    {
        if (SelectedTrack is null) return default;
        return _curveService.GetHandles(Project, SelectedTrack, curve, key);
    }

    public void MoveCurveKey(CurveChannel channel, int sourceFrame, int targetFrame, float value)
    {
        if (SelectedTrack is null) return;
        RecordUndo("移动曲线关键点");
        _curveService.MoveKey(Project, SelectedTrack, channel, sourceFrame, targetFrame, value);
        _currentFrame = targetFrame;
        RaisePropertyChanged(nameof(CurrentFrame));
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    public IReadOnlyCollection<CurveKeySelection> MoveCurveKeys(
        IReadOnlyCollection<CurveKeySelection> selection, int frameOffset, float valueOffset)
    {
        if (SelectedTrack is null || selection.Count == 0 || frameOffset == 0 && Math.Abs(valueOffset) < 0.000001f)
            return selection;
        RecordUndo("批量移动曲线关键点");
        var result = selection.ToHashSet();
        var direction = Math.Sign(frameOffset);
        for (var step = 0; step < Math.Abs(frameOffset); step++)
        {
            foreach (var group in result.GroupBy(item => item.Channel).ToArray())
            {
                var frames = group.Select(item => item.Frame)
                    .OrderBy(frame => direction > 0 ? -frame : frame).ToArray();
                foreach (var source in frames)
                {
                    var target = source + direction;
                    if (target < TimelineFrameStart || target > TimelineFrameEnd) continue;
                    var curve = _curveService.GetCurveForDisplay(Project, SelectedTrack, group.Key);
                    var key = curve.Keys.FirstOrDefault(item => item.Frame == source);
                    if (key is null) continue;
                    _curveService.MoveKey(Project, SelectedTrack, group.Key, source, target, key.Value);
                    result.Remove(new CurveKeySelection(group.Key, source));
                    result.Add(new CurveKeySelection(group.Key, target));
                }
            }
        }
        if (Math.Abs(valueOffset) >= 0.000001f)
        {
            foreach (var item in result)
            {
                var curve = _curveService.GetCurveForDisplay(Project, SelectedTrack, item.Channel);
                var key = curve.Keys.FirstOrDefault(candidate => candidate.Frame == item.Frame);
                if (key is not null)
                    _curveService.MoveKey(Project, SelectedTrack, item.Channel, item.Frame, item.Frame,
                        key.Value + valueOffset);
            }
        }
        var primary = result.First();
        _currentFrame = primary.Frame;
        RaisePropertyChanged(nameof(CurrentFrame));
        RaiseFrameProperties();
        NotifyVisualChanged();
        return result;
    }

    public void DeleteCurveKeys(IReadOnlyCollection<CurveKeySelection> selection)
    {
        if (SelectedTrack is null || selection.Count == 0) return;
        RecordUndo("批量删除曲线关键点");
        foreach (var item in selection)
            _curveService.DeleteKey(Project, SelectedTrack, item.Channel, item.Frame);
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    public void DeleteCurveKey(CurveChannel channel, int frame)
    {
        if (SelectedTrack is null) return;
        RecordUndo("删除曲线关键点");
        _curveService.DeleteKey(Project, SelectedTrack, channel, frame);
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    public void SetCurveHandle(CurveChannel channel, int frame, bool left, float handleFrame, float handleValue)
    {
        if (SelectedTrack is null) return;
        RecordUndo("调整 Bezier 曲线手柄");
        _curveService.SetHandle(Project, SelectedTrack, channel, frame, left, handleFrame, handleValue);
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    public void SetCurveHandleMode(CurveChannel channel, int frame, CurveHandleMode mode)
    {
        if (SelectedTrack is null) return;
        RecordUndo("修改曲线手柄类型");
        _curveService.SetHandleMode(Project, SelectedTrack, channel, frame, mode);
        NotifyVisualChanged();
    }

    public void SetCurveInterpolation(CurveChannel channel, CurveInterpolationMode mode)
    {
        if (SelectedTrack is null) return;
        RecordUndo("修改曲线插值");
        _curveService.SetInterpolation(Project, SelectedTrack, channel, mode);
        RaiseFrameProperties();
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
        SyncCurveKey(CurveChannel.X);
        SyncCurveKey(CurveChannel.Y);
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
        SyncCurveKey(CurveChannel.ScaleX);
        SyncCurveKey(CurveChannel.ScaleY);
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    public void RotateSelected(float deltaDegrees)
        => RotateSelectedAround(deltaDegrees, new System.Windows.Point(0, 0));

    public void RotateSelectedAround(float deltaDegrees, System.Windows.Point localCenter)
    {
        if (SelectedTrack is null || SelectedTrack.IsActionTrack) return;
        RecordUndo("旋转部件");
        var resolved = SelectedTrack.ResolveFrame(CurrentFrame);
        var oldMatrix = ReanimationRenderMath.CreateScreenMatrix(resolved, 1, new System.Windows.Point());
        var fixedCenter = oldMatrix.Transform(localCenter);
        var frame = EnsureCurrentFrame();
        frame.SkewX = resolved.SkewX + deltaDegrees;
        frame.SkewY = resolved.SkewY + deltaDegrees;
        var rotated = SelectedTrack.ResolveFrame(CurrentFrame);
        var newMatrix = ReanimationRenderMath.CreateScreenMatrix(rotated, 1, new System.Windows.Point());
        var movedCenter = newMatrix.Transform(localCenter);
        frame.X = rotated.X + (float)(fixedCenter.X - movedCenter.X);
        frame.Y = rotated.Y + (float)(fixedCenter.Y - movedCenter.Y);
        SyncCurveKey(CurveChannel.X);
        SyncCurveKey(CurveChannel.Y);
        SyncCurveKey(CurveChannel.SkewX);
        SyncCurveKey(CurveChannel.SkewY);
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
        _curveService.HasTimelineKey(Project, track, frameIndex);

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

    private void SetFrameValue(string historyName, CurveChannel? channel, Action<AnimationFrame> setter)
    {
        if (SelectedTrack is null) return;
        RecordUndo(historyName);
        var frame = EnsureCurrentFrame();
        setter(frame);
        if (channel.HasValue && AnimationCurveService.GetExplicitValue(frame, channel.Value).HasValue)
        {
            var key = _curveService.EnsureKey(Project, SelectedTrack, channel.Value, CurrentFrame);
            key.Value = AnimationCurveService.GetExplicitValue(frame, channel.Value)!.Value;
            var curve = _curveService.FindCurve(Project, SelectedTrack, channel.Value);
            if (curve is not null) _curveService.BakeCurve(Project, SelectedTrack, curve);
        }
        else if (channel.HasValue)
        {
            _curveService.DeleteKey(Project, SelectedTrack, channel.Value, CurrentFrame);
        }
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    private void SyncCurveKey(CurveChannel channel)
    {
        if (SelectedTrack is null) return;
        var key = _curveService.EnsureKey(Project, SelectedTrack, channel, CurrentFrame);
        key.Value = _curveService.GetValue(SelectedTrack, channel, CurrentFrame);
        var curve = _curveService.FindCurve(Project, SelectedTrack, channel);
        if (curve is not null) _curveService.BakeCurve(Project, SelectedTrack, curve);
    }

    private static void SwapTimelineFrames(AnimationTrack track, int first, int second)
    {
        var value = track.Frames[first];
        track.Frames[first] = track.Frames[second];
        track.Frames[second] = value;
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

    private void SetEventValue<T>(string historyName, T current, T value, Action<T> setter)
    {
        if (SelectedEvent is null || EqualityComparer<T>.Default.Equals(current, value)) return;
        RecordUndo(historyName);
        setter(value);
        RaiseEventProperties();
        RaisePropertyChanged(nameof(SelectedActionEvents));
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
        _selectedEvent = _selectedAction?.Events.FirstOrDefault();
        var range = ActiveRange;
        _currentFrame = Math.Clamp(snapshot.CurrentFrame, range.Start, range.End);
        _editTransactionActive = false;
        RaisePropertyChanged(nameof(SelectedTrack));
        RaisePropertyChanged(nameof(SelectedAction));
        RaiseActionProperties();
        RaiseEventProperties();
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
                     nameof(ProjectCarrierReanimation), nameof(ProjectInitialActionId), nameof(ProjectHideTemplateAttachments),
                     nameof(ProjectOutputFormat), nameof(ProjectGameRoot),
                     nameof(ProjectNumericEntityId), nameof(ProjectTemplateEntityId), nameof(ProjectTemplateSummary),
                     nameof(IsPlantProject), nameof(PlantTemplates), nameof(ProjectCost),
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
                     nameof(SelectedActionLoop), nameof(SelectedActionRate), nameof(SelectedActionBlendFrames),
                     nameof(SelectedActionReplacesCsv), nameof(SelectedActionEvents)
                 })
            RaisePropertyChanged(property);
    }

    private void RaiseEventProperties()
    {
        foreach (var property in new[]
                 {
                     nameof(SelectedEvent), nameof(SelectedEventId), nameof(SelectedEventFrame),
                     nameof(SelectedEventNormalizedTime), nameof(SelectedEventOncePerLoop),
                     nameof(SelectedEventTargetAction)
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
        RaiseEventProperties();
        RaiseTimelineProperties();
        RaiseFrameProperties();
        RaiseHistoryProperties();
    }

    private void NotifyVisualChanged() => VisualStateChanged?.Invoke(this, EventArgs.Empty);

    private void SynchronizeTemplateCarrier()
    {
        if (Project.Kind == EntityKind.Plant &&
            PlantTemplateCatalog.Find(Project.TemplateEntityId) is { IsRuntimeTemplate: true } plant)
            Project.CarrierReanimation = plant.CarrierReanimation;
        else if (Project.Kind == EntityKind.Zombie &&
                 ZombieTemplateCatalog.Find(Project.TemplateEntityId) is { } zombie)
            Project.CarrierReanimation = zombie.CarrierReanimation;
    }

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
