using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace PvZAnimationStudio.Models;

public enum EntityKind
{
    Plant,
    Zombie,
    Ui,
    Other
}

public enum AnimationOutputFormat
{
    Raw,
    Compiled
}

public enum AnimationLoopMode
{
    Loop,
    Once,
    OnceHold
}

public enum EditorTool
{
    Select,
    Move,
    Rotate,
    Scale
}

public enum CurveChannel
{
    X,
    Y,
    SkewX,
    SkewY,
    ScaleX,
    ScaleY,
    Frame,
    Alpha
}

public enum CurveInterpolationMode
{
    Bezier,
    Linear,
    Constant
}

public enum CurveHandleMode
{
    Auto,
    Aligned,
    Free,
    Vector
}

public sealed class CurveKeyDefinition
{
    public int Frame { get; set; }
    public float Value { get; set; }
    public CurveHandleMode HandleMode { get; set; } = CurveHandleMode.Auto;
    public float LeftFrameOffset { get; set; } = -1;
    public float LeftValueOffset { get; set; }
    public float RightFrameOffset { get; set; } = 1;
    public float RightValueOffset { get; set; }

    public CurveKeyDefinition Clone() => new()
    {
        Frame = Frame,
        Value = Value,
        HandleMode = HandleMode,
        LeftFrameOffset = LeftFrameOffset,
        LeftValueOffset = LeftValueOffset,
        RightFrameOffset = RightFrameOffset,
        RightValueOffset = RightValueOffset
    };
}

public sealed class AnimationCurveDefinition
{
    public string TrackId { get; set; } = string.Empty;
    public CurveChannel Channel { get; set; }
    public CurveInterpolationMode Interpolation { get; set; } = CurveInterpolationMode.Bezier;
    public ObservableCollection<CurveKeyDefinition> Keys { get; set; } = [];

    public AnimationCurveDefinition Clone() => new()
    {
        TrackId = TrackId,
        Channel = Channel,
        Interpolation = Interpolation,
        Keys = new ObservableCollection<CurveKeyDefinition>(Keys.Select(key => key.Clone()))
    };
}

public sealed class AnimationFrame
{
    public float? X { get; set; }
    public float? Y { get; set; }
    public float? SkewX { get; set; }
    public float? SkewY { get; set; }
    public float? ScaleX { get; set; }
    public float? ScaleY { get; set; }
    public float? Frame { get; set; }
    public float? Alpha { get; set; }
    public string? Image { get; set; }
    public string? Font { get; set; }
    public string? Text { get; set; }

    [JsonIgnore]
    public bool HasKey =>
        X.HasValue || Y.HasValue || SkewX.HasValue || SkewY.HasValue ||
        ScaleX.HasValue || ScaleY.HasValue || Frame.HasValue || Alpha.HasValue ||
        Image is not null || Font is not null || Text is not null;

    public AnimationFrame Clone() => new()
    {
        X = X,
        Y = Y,
        SkewX = SkewX,
        SkewY = SkewY,
        ScaleX = ScaleX,
        ScaleY = ScaleY,
        Frame = Frame,
        Alpha = Alpha,
        Image = Image,
        Font = Font,
        Text = Text
    };

    public void Clear()
    {
        X = Y = SkewX = SkewY = ScaleX = ScaleY = Frame = Alpha = null;
        Image = Font = Text = null;
    }
}

public sealed class ResolvedAnimationFrame
{
    public float X { get; set; }
    public float Y { get; set; }
    public float SkewX { get; set; }
    public float SkewY { get; set; }
    public float ScaleX { get; set; } = 1;
    public float ScaleY { get; set; } = 1;
    public float Frame { get; set; }
    public float Alpha { get; set; } = 1;
    public string? Image { get; set; }
    public string? Font { get; set; }
    public string? Text { get; set; }

    public AnimationFrame ToExplicitFrame() => new()
    {
        X = X,
        Y = Y,
        SkewX = SkewX,
        SkewY = SkewY,
        ScaleX = ScaleX,
        ScaleY = ScaleY,
        Frame = Frame,
        Alpha = Alpha,
        Image = Image,
        Font = Font,
        Text = Text
    };
}

public sealed class AnimationTrack : ObservableObject
{
    private string _name = "track";

    public string EditorId { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public ObservableCollection<AnimationFrame> Frames { get; set; } = [];

    [JsonIgnore]
    public bool HasRenderableContent => Frames.Any(frame =>
        !string.IsNullOrEmpty(frame.Image) || !string.IsNullOrEmpty(frame.Font) || !string.IsNullOrEmpty(frame.Text));

    [JsonIgnore]
    public bool IsActionTrack =>
        Name.StartsWith("anim_", StringComparison.OrdinalIgnoreCase) && !HasRenderableContent;

    public void EnsureFrameCount(int count)
    {
        while (Frames.Count < count)
            Frames.Add(new AnimationFrame());
        while (Frames.Count > count)
            Frames.RemoveAt(Frames.Count - 1);
    }

    public ResolvedAnimationFrame ResolveFrame(int index)
    {
        var resolved = new ResolvedAnimationFrame();
        if (Frames.Count == 0)
            return resolved;
        index = Math.Clamp(index, 0, Frames.Count - 1);
        for (var frameIndex = 0; frameIndex <= index; frameIndex++)
        {
            var frame = Frames[frameIndex];
            if (frame.X.HasValue) resolved.X = frame.X.Value;
            if (frame.Y.HasValue) resolved.Y = frame.Y.Value;
            if (frame.SkewX.HasValue) resolved.SkewX = frame.SkewX.Value;
            if (frame.SkewY.HasValue) resolved.SkewY = frame.SkewY.Value;
            if (frame.ScaleX.HasValue) resolved.ScaleX = frame.ScaleX.Value;
            if (frame.ScaleY.HasValue) resolved.ScaleY = frame.ScaleY.Value;
            if (frame.Frame.HasValue) resolved.Frame = frame.Frame.Value;
            if (frame.Alpha.HasValue) resolved.Alpha = frame.Alpha.Value;
            if (frame.Image is not null) resolved.Image = frame.Image.Length == 0 ? null : frame.Image;
            if (frame.Font is not null) resolved.Font = frame.Font.Length == 0 ? null : frame.Font;
            if (frame.Text is not null) resolved.Text = frame.Text.Length == 0 ? null : frame.Text;
        }
        return resolved;
    }
}

public sealed class AnimationDocument : ObservableObject
{
    private float _fps = 12;
    private int? _doScale;

    public float Fps
    {
        get => _fps;
        set => SetField(ref _fps, Math.Clamp(value, 0.1f, 120f));
    }

    public int? DoScale
    {
        get => _doScale;
        set => SetField(ref _doScale, value);
    }

    public ObservableCollection<AnimationTrack> Tracks { get; set; } = [];

    [JsonIgnore]
    public int FrameCount => Tracks.Count == 0 ? 0 : Tracks.Max(track => track.Frames.Count);

    public void EnsureUniformFrameCount(int count)
    {
        count = Math.Clamp(count, 1, 20000);
        foreach (var track in Tracks)
            track.EnsureFrameCount(count);
        RaisePropertyChanged(nameof(FrameCount));
    }

    public AnimationTrack? FindTrack(string name) =>
        Tracks.FirstOrDefault(track => string.Equals(track.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class AnimationEventDefinition
{
    public string Id { get; set; } = "EVENT";
    public int? Frame { get; set; }
    public double? NormalizedTime { get; set; }
    public bool OncePerLoop { get; set; } = true;
}

public sealed class ActionDefinition : ObservableObject
{
    private string _id = "idle";
    private string _displayName = "待机";
    private string _category = "通用";
    private string _track = "anim_idle";
    private AnimationLoopMode _loop = AnimationLoopMode.Loop;
    private double _rate = 12;
    private int _blendFrames;

    public string Id
    {
        get => _id;
        set
        {
            if (SetField(ref _id, value))
                RaisePropertyChanged(nameof(Summary));
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetField(ref _displayName, value))
                RaisePropertyChanged(nameof(Summary));
        }
    }
    public string Category { get => _category; set => SetField(ref _category, value); }
    public string Track
    {
        get => _track;
        set
        {
            if (SetField(ref _track, value))
                RaisePropertyChanged(nameof(Summary));
        }
    }
    public AnimationLoopMode Loop { get => _loop; set => SetField(ref _loop, value); }
    public double Rate { get => _rate; set => SetField(ref _rate, value); }
    public int BlendFrames { get => _blendFrames; set => SetField(ref _blendFrames, value); }
    public ObservableCollection<AnimationEventDefinition> Events { get; set; } = [];

    [JsonIgnore]
    public string Summary => $"{DisplayName}  ·  {Track}";
}

public sealed class EditorProject : ObservableObject
{
    private string _id = "NEW_PLANT";
    private string _displayName = "新植物";
    private string _description = "使用 PvZ 动画制作器创建。";
    private EntityKind _kind = EntityKind.Plant;
    private string _carrierReanimation = "REANIM_PEASHOOTER";
    private AnimationOutputFormat _outputFormat = AnimationOutputFormat.Compiled;
    private string? _gameRoot;
    private string? _projectPath;
    private string? _sourceAnimationPath;

    public int SchemaVersion { get; set; } = 3;
    public string Id { get => _id; set => SetField(ref _id, value); }
    public string DisplayName { get => _displayName; set => SetField(ref _displayName, value); }
    public string Description { get => _description; set => SetField(ref _description, value); }
    public EntityKind Kind { get => _kind; set => SetField(ref _kind, value); }
    public string CarrierReanimation { get => _carrierReanimation; set => SetField(ref _carrierReanimation, value); }
    public AnimationOutputFormat OutputFormat { get => _outputFormat; set => SetField(ref _outputFormat, value); }
    public string? GameRoot { get => _gameRoot; set => SetField(ref _gameRoot, value); }
    public string? ProjectPath { get => _projectPath; set => SetField(ref _projectPath, value); }
    public string? SourceAnimationPath { get => _sourceAnimationPath; set => SetField(ref _sourceAnimationPath, value); }

    public int NumericEntityId { get; set; } = 1000;
    public int TemplateEntityId { get; set; }
    public int Cost { get; set; } = 100;
    public int RechargeTime { get; set; } = 750;
    public int Health { get; set; } = 300;
    public int LaunchRate { get; set; } = 150;
    public int ProjectileType { get; set; }
    public int Damage { get; set; } = 20;
    public int ShotsPerAttack { get; set; } = 1;

    public AnimationDocument Animation { get; set; } = new();
    public ObservableCollection<ActionDefinition> Actions { get; set; } = [];
    public ObservableCollection<AnimationCurveDefinition> Curves { get; set; } = [];
    public Dictionary<string, string> ImageBindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ImageLayoutDefinition> ImageLayouts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public WorkspaceLayoutState WorkspaceLayout { get; set; } = WorkspaceLayoutState.CreateDefault();
}
