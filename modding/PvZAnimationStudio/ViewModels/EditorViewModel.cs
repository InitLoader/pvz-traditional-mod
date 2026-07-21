using System.Collections.ObjectModel;
using PvZAnimationStudio.Models;
using PvZAnimationStudio.Services;

namespace PvZAnimationStudio.ViewModels;

public sealed class EditorViewModel : ObservableObject
{
    private readonly ActionCatalogService _actionCatalog;
    private readonly TweenService _tweenService = new();
    private EditorProject _project;
    private AnimationTrack? _selectedTrack;
    private ActionDefinition? _selectedAction;
    private int _currentFrame;
    private bool _isPlaying;
    private string _status = "就绪";

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
        set => SetField(ref _selectedAction, value);
    }

    public int CurrentFrame
    {
        get => _currentFrame;
        set
        {
            var maximum = Math.Max(0, Project.Animation.FrameCount - 1);
            value = Math.Clamp(value, 0, maximum);
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

    public string FrameLabel => $"帧 {CurrentFrame + 1} / {Math.Max(1, Project.Animation.FrameCount)}";
    public bool CurrentHasKey => CurrentExplicitFrame?.HasKey ?? false;

    public float? CurrentX { get => CurrentExplicitFrame?.X; set => SetFrameValue(frame => frame.X = value); }
    public float? CurrentY { get => CurrentExplicitFrame?.Y; set => SetFrameValue(frame => frame.Y = value); }
    public float? CurrentSkewX { get => CurrentExplicitFrame?.SkewX; set => SetFrameValue(frame => frame.SkewX = value); }
    public float? CurrentSkewY { get => CurrentExplicitFrame?.SkewY; set => SetFrameValue(frame => frame.SkewY = value); }
    public float? CurrentScaleX { get => CurrentExplicitFrame?.ScaleX; set => SetFrameValue(frame => frame.ScaleX = value); }
    public float? CurrentScaleY { get => CurrentExplicitFrame?.ScaleY; set => SetFrameValue(frame => frame.ScaleY = value); }
    public float? CurrentVisibilityFrame { get => CurrentExplicitFrame?.Frame; set => SetFrameValue(frame => frame.Frame = value); }
    public float? CurrentAlpha { get => CurrentExplicitFrame?.Alpha; set => SetFrameValue(frame => frame.Alpha = value); }
    public string? CurrentImage { get => CurrentExplicitFrame?.Image; set => SetFrameValue(frame => frame.Image = value); }
    public string? CurrentText { get => CurrentExplicitFrame?.Text; set => SetFrameValue(frame => frame.Text = value); }

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
        _selectedAction = project.Actions.FirstOrDefault();
        IsPlaying = false;
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
        if (Project.Actions.Count == 0)
            AddAction(_actionCatalog.Templates.First(template => template.Id == "idle"));
        _selectedTrack = document.Tracks.FirstOrDefault(track => !track.IsActionTrack)
                         ?? document.Tracks.FirstOrDefault();
        _selectedAction = Project.Actions.FirstOrDefault();
        _currentFrame = 0;
        RaiseAll();
        NotifyVisualChanged();
    }

    public void InferActions()
    {
        Project.Actions = _actionCatalog.InferActions(Project.Animation, Project.Kind);
        _selectedAction = Project.Actions.FirstOrDefault();
        RaisePropertyChanged(nameof(Actions));
        RaisePropertyChanged(nameof(SelectedAction));
        Status = $"已识别 {Project.Actions.Count} 个 anim_* 动作";
    }

    public void AddAction(ActionTemplate template)
    {
        var action = _actionCatalog.CreateFromTemplate(template, Project.Animation);
        var existing = Project.Actions.FirstOrDefault(item => string.Equals(item.Id, action.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) { SelectedAction = existing; return; }
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
        Project.Actions.Remove(SelectedAction);
        SelectedAction = Project.Actions.FirstOrDefault();
    }

    public void AddTrack(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "新轨道";
        var baseName = name;
        var suffix = 2;
        while (Project.Animation.FindTrack(name) is not null) name = $"{baseName}_{suffix++}";
        var track = new AnimationTrack { Name = name };
        track.EnsureFrameCount(Math.Max(1, Project.Animation.FrameCount));
        Project.Animation.Tracks.Add(track);
        SelectedTrack = track;
        RaisePropertyChanged(nameof(Tracks));
        NotifyVisualChanged();
    }

    public void RemoveSelectedTrack()
    {
        if (SelectedTrack is null || Project.Animation.Tracks.Count <= 1) return;
        var index = Project.Animation.Tracks.IndexOf(SelectedTrack);
        Project.Animation.Tracks.Remove(SelectedTrack);
        SelectedTrack = Project.Animation.Tracks[Math.Clamp(index, 0, Project.Animation.Tracks.Count - 1)];
        RaisePropertyChanged(nameof(Tracks));
        NotifyVisualChanged();
    }

    public void SetKeyframe()
    {
        if (SelectedTrack is null) return;
        var resolved = SelectedTrack.ResolveFrame(CurrentFrame);
        SelectedTrack.Frames[CurrentFrame] = resolved.ToExplicitFrame();
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    public void ClearKeyframe()
    {
        CurrentExplicitFrame?.Clear();
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
        var count = _tweenService.BakeToNextKeyframe(SelectedTrack, CurrentFrame, curve);
        Status = curve == TweenCurve.Linear
            ? $"已生成 {count} 帧线性补间"
            : $"已生成 {count} 帧平滑缓入缓出补间";
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    public void InsertFrame()
    {
        foreach (var track in Project.Animation.Tracks)
            track.Frames.Insert(Math.Min(CurrentFrame, track.Frames.Count), new AnimationFrame());
        RaiseAll();
        NotifyVisualChanged();
    }

    public void DeleteFrame()
    {
        if (Project.Animation.FrameCount <= 1) return;
        foreach (var track in Project.Animation.Tracks)
            track.Frames.RemoveAt(Math.Clamp(CurrentFrame, 0, track.Frames.Count - 1));
        CurrentFrame = Math.Min(CurrentFrame, Project.Animation.FrameCount - 1);
        RaiseAll();
        NotifyVisualChanged();
    }

    public void MoveSelected(float deltaX, float deltaY)
    {
        if (SelectedTrack is null) return;
        var resolved = SelectedTrack.ResolveFrame(CurrentFrame);
        var frame = EnsureCurrentFrame();
        frame.X = resolved.X + deltaX;
        frame.Y = resolved.Y + deltaY;
        RaiseFrameProperties();
        NotifyVisualChanged();
    }

    public void StepPlayback()
    {
        if (!IsPlaying || Project.Animation.FrameCount == 0) return;
        CurrentFrame = (CurrentFrame + 1) % Project.Animation.FrameCount;
    }

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

    private void SetFrameValue(Action<AnimationFrame> setter)
    {
        if (SelectedTrack is null) return;
        setter(EnsureCurrentFrame());
        RaiseFrameProperties();
        NotifyVisualChanged();
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

    private void RaiseAll()
    {
        RaisePropertyChanged(nameof(Project));
        RaisePropertyChanged(nameof(Tracks));
        RaisePropertyChanged(nameof(Actions));
        RaisePropertyChanged(nameof(FrameLabel));
        RaiseFrameProperties();
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
