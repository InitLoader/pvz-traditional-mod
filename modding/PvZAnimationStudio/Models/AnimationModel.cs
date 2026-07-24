using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace PvZAnimationStudio.Models;

public enum EntityKind
{
    Plant,
    Zombie,
    Ui,
    Other
}

public enum EntityIntegrationMode
{
    ReplaceOriginal,
    AddEntity
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
    private float? _x;
    private float? _y;
    private float? _skewX;
    private float? _skewY;
    private float? _scaleX;
    private float? _scaleY;
    private float? _frame;
    private float? _alpha;
    private string? _image;
    private string? _font;
    private string? _text;

    [JsonIgnore]
    internal Action? Changed { get; set; }

    public float? X { get => _x; set => SetField(ref _x, value); }
    public float? Y { get => _y; set => SetField(ref _y, value); }
    public float? SkewX { get => _skewX; set => SetField(ref _skewX, value); }
    public float? SkewY { get => _skewY; set => SetField(ref _skewY, value); }
    public float? ScaleX { get => _scaleX; set => SetField(ref _scaleX, value); }
    public float? ScaleY { get => _scaleY; set => SetField(ref _scaleY, value); }
    public float? Frame { get => _frame; set => SetField(ref _frame, value); }
    public float? Alpha { get => _alpha; set => SetField(ref _alpha, value); }
    public string? Image { get => _image; set => SetField(ref _image, value); }
    public string? Font { get => _font; set => SetField(ref _font, value); }
    public string? Text { get => _text; set => SetField(ref _text, value); }

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

    private void SetField<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed?.Invoke();
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
    private bool _isVisibleInEditor = true;
    private bool _isAlwaysVisibleInEditor;
    private bool _isLockedInEditor;
    private ObservableCollection<AnimationFrame> _frames = [];
    private ResolvedAnimationFrame[]? _resolvedFrameCache;
    private bool? _hasRenderableContentCache;
    private BitmapSource? _editorThumbnail;
    private string? _editorImageSymbol;

    public AnimationTrack()
    {
        _frames.CollectionChanged += OnFramesCollectionChanged;
    }

    public string EditorId { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set
        {
            if (!SetField(ref _name, value)) return;
            RaisePropertyChanged(nameof(IsActionTrack));
            RaisePropertyChanged(nameof(IsGroundTrack));
            RaisePropertyChanged(nameof(EditorDisplayName));
            RaisePropertyChanged(nameof(EditorFallbackGlyph));
        }
    }

    public ObservableCollection<AnimationFrame> Frames
    {
        get => _frames;
        set
        {
            if (ReferenceEquals(_frames, value)) return;
            _frames.CollectionChanged -= OnFramesCollectionChanged;
            foreach (var frame in _frames) frame.Changed = null;
            _frames = value ?? [];
            _frames.CollectionChanged += OnFramesCollectionChanged;
            AttachFrameCallbacks();
            InvalidateFrameCache();
        }
    }

    // Preview-only state. Raw/compiled codecs export only Reanimation fields,
    // so hiding a layer here never changes the in-game animation.
    public bool IsVisibleInEditor
    {
        get => _isVisibleInEditor;
        set
        {
            if (SetField(ref _isVisibleInEditor, value))
                RaisePropertyChanged(nameof(EditorVisibilityGlyph));
        }
    }

    [JsonIgnore]
    public string EditorVisibilityGlyph => IsVisibleInEditor ? "\uE7B3" : "\uED1A";

    // Preview-only override for optional equipment and accessory layers. This
    // bypasses entity-profile filtering without changing raw/compiled output.
    public bool IsAlwaysVisibleInEditor
    {
        get => _isAlwaysVisibleInEditor;
        set
        {
            if (SetField(ref _isAlwaysVisibleInEditor, value))
                RaisePropertyChanged(nameof(EditorAlwaysVisibleGlyph));
        }
    }

    [JsonIgnore]
    public string EditorAlwaysVisibleGlyph => IsAlwaysVisibleInEditor ? "\uE718" : "\uE77A";

    // Editor-only protection. It is persisted in .pvza projects but ignored by
    // raw/compiled Reanimation codecs and therefore never changes game data.
    public bool IsLockedInEditor
    {
        get => _isLockedInEditor;
        set
        {
            if (SetField(ref _isLockedInEditor, value))
                RaisePropertyChanged(nameof(EditorLockGlyph));
        }
    }

    [JsonIgnore]
    public string EditorLockGlyph => IsLockedInEditor ? "\uE72E" : "\uE785";

    [JsonIgnore]
    public BitmapSource? EditorThumbnail
    {
        get => _editorThumbnail;
        set => SetField(ref _editorThumbnail, value);
    }

    [JsonIgnore]
    public string? EditorImageSymbol
    {
        get => _editorImageSymbol;
        set => SetField(ref _editorImageSymbol, value);
    }

    [JsonIgnore]
    public bool HasRenderableContent => _hasRenderableContentCache ??= Frames.Any(frame =>
        !string.IsNullOrEmpty(frame.Image) || !string.IsNullOrEmpty(frame.Font) || !string.IsNullOrEmpty(frame.Text));

    [JsonIgnore]
    public bool IsActionTrack =>
        Name.StartsWith("anim_", StringComparison.OrdinalIgnoreCase) && !HasRenderableContent;

    [JsonIgnore]
    public bool IsGroundTrack => string.Equals(Name, "_ground", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string EditorDisplayName => IsGroundTrack ? "地面位移 / 速度  _ground" : Name;

    [JsonIgnore]
    public string EditorFallbackGlyph => IsGroundTrack ? "↔" : string.Empty;

    public void EnsureFrameCount(int count)
    {
        while (Frames.Count < count)
            Frames.Add(new AnimationFrame());
        while (Frames.Count > count)
            Frames.RemoveAt(Frames.Count - 1);
    }

    public ResolvedAnimationFrame ResolveFrame(int index)
    {
        if (Frames.Count == 0) return new ResolvedAnimationFrame();
        index = Math.Clamp(index, 0, Frames.Count - 1);
        EnsureResolvedFrameCache();
        return _resolvedFrameCache![index];
    }

    public void InvalidateFrameCache()
    {
        _resolvedFrameCache = null;
        _hasRenderableContentCache = null;
    }

    public void WarmFrameCache()
    {
        if (Frames.Count > 0) EnsureResolvedFrameCache();
    }

    private void EnsureResolvedFrameCache()
    {
        if (_resolvedFrameCache is { Length: var length } && length == Frames.Count) return;
        AttachFrameCallbacks();
        var cache = new ResolvedAnimationFrame[Frames.Count];
        var resolved = new ResolvedAnimationFrame();
        for (var frameIndex = 0; frameIndex < Frames.Count; frameIndex++)
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
            cache[frameIndex] = new ResolvedAnimationFrame
            {
                X = resolved.X, Y = resolved.Y, SkewX = resolved.SkewX, SkewY = resolved.SkewY,
                ScaleX = resolved.ScaleX, ScaleY = resolved.ScaleY, Frame = resolved.Frame,
                Alpha = resolved.Alpha, Image = resolved.Image, Font = resolved.Font, Text = resolved.Text
            };
        }
        _resolvedFrameCache = cache;
    }

    private void AttachFrameCallbacks()
    {
        foreach (var frame in Frames) frame.Changed = InvalidateFrameCache;
    }

    private void OnFramesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (eventArgs.Action is NotifyCollectionChangedAction.Replace or NotifyCollectionChangedAction.Reset)
        {
            // Swap operations can temporarily place one frame in two
            // positions. Rebind the complete final collection so neither
            // frame loses its callback. Adds stay O(1), which matters while
            // cloning large projects into the undo history.
            AttachFrameCallbacks();
        }
        else
        {
            if (eventArgs.OldItems is not null)
                foreach (AnimationFrame frame in eventArgs.OldItems) frame.Changed = null;
            if (eventArgs.NewItems is not null)
                foreach (AnimationFrame frame in eventArgs.NewItems) frame.Changed = InvalidateFrameCache;
        }
        InvalidateFrameCache();
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

    public void WarmFrameCaches()
    {
        foreach (var track in Tracks) track.WarmFrameCache();
    }

    public AnimationTrack? FindTrack(string name) =>
        Tracks.FirstOrDefault(track => string.Equals(track.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class AnimationEventDefinition : ObservableObject
{
    private string _id = "FIRE_PROJECTILE";
    private int? _frame;
    private double? _normalizedTime;
    private bool _oncePerLoop = true;
    private string _targetAction = string.Empty;

    public string Id { get => _id; set { if (SetField(ref _id, value)) RaisePropertyChanged(nameof(Summary)); } }
    public int? Frame { get => _frame; set { if (SetField(ref _frame, value)) RaisePropertyChanged(nameof(Summary)); } }
    public double? NormalizedTime { get => _normalizedTime; set { if (SetField(ref _normalizedTime, value)) RaisePropertyChanged(nameof(Summary)); } }
    public bool OncePerLoop { get => _oncePerLoop; set => SetField(ref _oncePerLoop, value); }
    public string TargetAction { get => _targetAction; set { if (SetField(ref _targetAction, value)) RaisePropertyChanged(nameof(Summary)); } }

    [JsonIgnore]
    public string Summary => Frame.HasValue
        ? $"{Id} · 第 {Frame.Value + 1} 帧"
        : $"{Id} · {NormalizedTime.GetValueOrDefault():0.###}";
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
    public ObservableCollection<string> Replaces { get; set; } = [];
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
    private string _initialActionId = "idle";
    private bool _hideTemplateAttachments = true;
    private EntityIntegrationMode _integrationMode = EntityIntegrationMode.AddEntity;

    public int SchemaVersion { get; set; } = 5;
    public string Id { get => _id; set => SetField(ref _id, value); }
    public string DisplayName { get => _displayName; set => SetField(ref _displayName, value); }
    public string Description { get => _description; set => SetField(ref _description, value); }
    public EntityKind Kind { get => _kind; set => SetField(ref _kind, value); }
    public string CarrierReanimation { get => _carrierReanimation; set => SetField(ref _carrierReanimation, value); }
    public AnimationOutputFormat OutputFormat { get => _outputFormat; set => SetField(ref _outputFormat, value); }
    public string? GameRoot { get => _gameRoot; set => SetField(ref _gameRoot, value); }
    public string? ProjectPath { get => _projectPath; set => SetField(ref _projectPath, value); }
    public string? SourceAnimationPath { get => _sourceAnimationPath; set => SetField(ref _sourceAnimationPath, value); }
    public string InitialActionId { get => _initialActionId; set => SetField(ref _initialActionId, value); }
    public bool HideTemplateAttachments { get => _hideTemplateAttachments; set => SetField(ref _hideTemplateAttachments, value); }
    public EntityIntegrationMode IntegrationMode { get => _integrationMode; set => SetField(ref _integrationMode, value); }

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
    public HashSet<string> OriginalImageReferences { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public WorkspaceLayoutState WorkspaceLayout { get; set; } = WorkspaceLayoutState.CreateDefault();
}
