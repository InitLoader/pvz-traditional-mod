using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PvZAnimationStudio;
using PvZAnimationStudio.Controls;
using PvZAnimationStudio.Models;
using PvZAnimationStudio.Services;
using PvZAnimationStudio.ViewModels;

if (args.Length > 1 && string.Equals(args[0], "--workspace-screenshot", StringComparison.OrdinalIgnoreCase))
{
    RenderWorkspaceScreenshot(args[1], args.Length > 2 && args[2] != "-" ? args[2] : null,
        args.Length > 3 && Enum.TryParse<WorkspacePreset>(args[3], true, out var preset) ? preset : null,
        args.Length > 4 && string.Equals(args[4], "floating", StringComparison.OrdinalIgnoreCase));
    return;
}

if (args.Length > 1 && string.Equals(args[0], "--audit", StringComparison.OrdinalIgnoreCase))
{
    RunAnimationAudit(args[1]);
    return;
}

if (args.Length > 1 && string.Equals(args[0], "--inspect", StringComparison.OrdinalIgnoreCase))
{
    RunTrackInspection(args[1], args.Skip(2));
    return;
}

var root = Path.Combine(Path.GetTempPath(), "PvZAnimationStudioTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var source = CreateDocument();
    var rawPath = Path.Combine(root, "roundtrip.reanim");
    var compiledPath = Path.Combine(root, "roundtrip.reanim.compiled");
    var raw = new RawReanimCodec();
    var compiled = new CompiledReanimCodec();

    raw.Save(source, rawPath);
    var rawLoaded = raw.Load(rawPath);
    compiled.Save(rawLoaded, compiledPath);
    var compiledLoaded = compiled.Load(compiledPath);
    AssertDocument(source, compiledLoaded);

    var misleadingCompiledPath = Path.Combine(root, "NEW_PLANT.animation-data");
    compiled.Save(source, misleadingCompiledPath);
    AssertDocument(source, new ReanimCodecService().Load(misleadingCompiledPath));
    var normalizedCompiledPath = new ReanimCodecService().Save(
        source, Path.Combine(root, "NEW_PLANT"), AnimationOutputFormat.Compiled);
    Assert(normalizedCompiledPath.EndsWith(".reanim.compiled", StringComparison.OrdinalIgnoreCase) &&
           File.Exists(normalizedCompiledPath), "compiled 导出没有生成可重新打开的完整后缀");

    if (args.Length > 0)
    {
        var sourcePath = Path.GetFullPath(args[0]);
        var originals = Directory.Exists(sourcePath)
            ? Directory.EnumerateFiles(sourcePath, "*.reanim.compiled", SearchOption.AllDirectories).OrderBy(path => path).ToArray()
            : [sourcePath];
        Assert(originals.Length > 0, "指定目录中没有 .reanim.compiled 文件");
        for (var originalIndex = 0; originalIndex < originals.Length; originalIndex++)
        {
            var originalPath = originals[originalIndex];
            var original = compiled.Load(originalPath);
            var repackedPath = Path.Combine(root, $"original-repacked-{Guid.NewGuid():N}.reanim.compiled");
            compiled.Save(original, repackedPath);
            var repacked = compiled.Load(repackedPath);
            AssertDocument(original, repacked);
            if (originalIndex == 0 && args.Length > 1)
            {
                var artifactPath = Path.GetFullPath(args[1]);
                Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
                File.Copy(repackedPath, artifactPath, true);
            }
            var rawOriginalPath = Path.Combine(root, $"original-raw-{Guid.NewGuid():N}.reanim");
            raw.Save(original, rawOriginalPath);
            AssertDocument(original, raw.Load(rawOriginalPath));
        }
        var originalGameRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(originals[0])!, "..", ".."));
        if (File.Exists(Path.Combine(originalGameRoot, "PlantsVsZombies.exe")))
        {
            var originalDocument = compiled.Load(originals[0]);
            var originalResources = new OriginalResourceService();
            originalResources.RebuildIndex(originalGameRoot);
            var originalPortableProject = new EditorProject
            {
                Id = "ORIGINAL_PORTABLE_TEST",
                GameRoot = originalGameRoot,
                SourceAnimationPath = originals[0],
                Animation = originalDocument,
                Actions = new ObservableCollection<ActionDefinition>(
                    new ActionCatalogService().InferActions(originalDocument, EntityKind.Plant))
            };
            var originalPortablePath = Path.Combine(root, "original-assets.pvza");
            new ProjectFileService(originalResources).Save(originalPortableProject, originalPortablePath);
            var loadedOriginalPortable = new ProjectFileService(new OriginalResourceService()).Load(originalPortablePath);
            var usedSymbols = originalDocument.Tracks.SelectMany(track => track.Frames)
                .Select(frame => frame.Image).Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            Assert(loadedOriginalPortable.ImageBindings.Count == usedSymbols &&
                   loadedOriginalPortable.ImageBindings.Values.All(File.Exists),
                "原版 compiled 保存为便携工程时没有嵌入全部实际使用图片");
        }
        Console.WriteLine($"PASS: {originals.Length} 个原版 compiled 文件已全部完成读取、compiled 重打包、Raw 导出和二次读取。");
    }

    var tween = new TweenService();
    var tweenTrack = new AnimationTrack { Name = "body" };
    tweenTrack.EnsureFrameCount(5);
    tweenTrack.Frames[0].X = 0;
    tweenTrack.Frames[0].ScaleX = 1;
    tweenTrack.Frames[4].X = 40;
    tweenTrack.Frames[4].ScaleX = 2;
    Assert(tween.BakeToNextKeyframe(tweenTrack, 0, TweenCurve.Linear) == 3, "补间帧数量错误");
    AssertNear(tweenTrack.Frames[2].X, 20, "线性位移补间错误");
    AssertNear(tweenTrack.Frames[2].ScaleX, 1.5f, "线性缩放补间错误");

    var rotated = new ResolvedAnimationFrame
    {
        SkewX = -26,
        SkewY = -26,
        ScaleX = 1,
        ScaleY = 1
    };
    var matrix = ReanimationRenderMath.CreateScreenMatrix(rotated, 1, new Point(0, 0));
    var xAxisLength = Math.Sqrt(matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12);
    var yAxisLength = Math.Sqrt(matrix.M21 * matrix.M21 + matrix.M22 * matrix.M22);
    var axisDot = matrix.M11 * matrix.M21 + matrix.M12 * matrix.M22;
    AssertNearDouble(xAxisLength, 1, "PVZ kx 轴长度错误");
    AssertNearDouble(yAxisLength, 1, "PVZ ky 轴长度错误");
    AssertNearDouble(axisDot, 0, "PVZ kx/ky 被错误实现成普通斜切");
    var registration = ReanimationRenderMath.CreateScreenMatrix(
        new ResolvedAnimationFrame { X = 25, Y = 40, ScaleX = 1, ScaleY = 1 }, 1, new Point(100, 100));
    var topLeft = registration.Transform(new Point(0, 0));
    AssertNearDouble(topLeft.X, 125, "PVZ 图片左上注册点 X 错误");
    AssertNearDouble(topLeft.Y, 140, "PVZ 图片左上注册点 Y 错误");

    var maskedAssetRoot = Path.Combine(root, "masked-assets");
    var maskedReanimRoot = Path.Combine(maskedAssetRoot, "reanim");
    Directory.CreateDirectory(maskedReanimRoot);
    var colorBitmap = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null,
        new byte[] { 20, 40, 200, 255, 30, 80, 220, 255 }, 8);
    var maskBitmap = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Gray8, null,
        new byte[] { 0, 255 }, 2);
    SaveBitmap(colorBitmap, new JpegBitmapEncoder(), Path.Combine(maskedReanimRoot, "masked.jpg"));
    SaveBitmap(maskBitmap, new PngBitmapEncoder(), Path.Combine(maskedReanimRoot, "masked_.png"));
    var maskedResources = new OriginalResourceService();
    maskedResources.RebuildIndex(maskedAssetRoot);
    var maskedImage = maskedResources.ResolveImage(
        new EditorProject { GameRoot = maskedAssetRoot }, "IMAGE_REANIM_MASKED");
    Assert(maskedImage is not null, "JPG + _PNG 透明遮罩资源没有加载");
    var maskedPixels = new byte[8];
    maskedImage!.Bitmap.CopyPixels(maskedPixels, 8, 0);
    Assert(maskedPixels[3] == 0 && maskedPixels[7] == 255,
        "原版 JPG 颜色图没有正确合并同名 _PNG 灰度透明遮罩");

    var portableImagePath = Path.Combine(root, "portable-body.png");
    var portableBitmap = BitmapSource.Create(4, 2, 96, 96, PixelFormats.Bgra32, null,
        Enumerable.Repeat((byte)255, 4 * 4 * 2).ToArray(), 16);
    SaveBitmap(portableBitmap, new PngBitmapEncoder(), portableImagePath);
    var portableProject = new EditorProject
    {
        Id = "PORTABLE_PLANT",
        DisplayName = "便携植物",
        Description = "动画、属性、布局和图片都必须在同一个文件。",
        Health = 987,
        Damage = 66,
        Animation = source,
        Actions = new ObservableCollection<ActionDefinition>
        {
            new() { Id = "idle", DisplayName = "便携待机", Track = "anim_idle", Rate = 18 }
        },
        ImageBindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["IMAGE_REANIM_TEST_BODY"] = portableImagePath
        },
        ImageLayouts = new Dictionary<string, ImageLayoutDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["IMAGE_REANIM_TEST_BODY"] = new() { Columns = 2, Rows = 1 }
        },
        WorkspaceLayout = new WorkspaceLayoutPresetService().Create(WorkspacePreset.DualView)
    };
    portableProject.Curves.Add(new AnimationCurveDefinition
    {
        TrackId = source.Tracks[0].EditorId,
        Channel = CurveChannel.X,
        Interpolation = CurveInterpolationMode.Bezier,
        Keys = new ObservableCollection<CurveKeyDefinition>
        {
            new() { Frame = 0, Value = 4, HandleMode = CurveHandleMode.Aligned, RightFrameOffset = 2, RightValueOffset = 6 }
        }
    });
    var portablePath = Path.Combine(root, "PORTABLE_PLANT.pvza");
    var portableResources = new OriginalResourceService();
    var portableFiles = new ProjectFileService(portableResources);
    portableFiles.Save(portableProject, portablePath);
    Assert(File.Exists(portablePath), "便携工程没有写入单个 .pvza 文件");
    using (var archive = ZipFile.OpenRead(portablePath))
    {
        Assert(archive.GetEntry("project.json") is not null, "便携工程缺少 project.json");
        Assert(archive.Entries.Count(entry => entry.FullName.StartsWith("assets/", StringComparison.Ordinal)) == 1,
            "便携工程没有只嵌入一次实际使用图片");
    }
    File.Delete(portableImagePath);
    var portableLoaded = portableFiles.Load(portablePath);
    Assert(portableLoaded.DisplayName == "便携植物" && portableLoaded.Health == 987 && portableLoaded.Damage == 66,
        "便携工程没有保留实体属性和信息");
    AssertDocument(source, portableLoaded.Animation);
    Assert(portableLoaded.Actions.Count == 1 && portableLoaded.Actions[0].DisplayName == "便携待机" &&
           Math.Abs(portableLoaded.Actions[0].Rate - 18) < 0.0001,
        "便携工程没有保留动作信息");
    Assert(portableLoaded.ImageBindings.TryGetValue("IMAGE_REANIM_TEST_BODY", out var extractedImage) &&
           File.Exists(extractedImage), "删除原图片后便携工程没有恢复嵌入图片");
    var portableResolved = new OriginalResourceService().ResolveImage(portableLoaded, "IMAGE_REANIM_TEST_BODY");
    Assert(portableResolved is not null && portableResolved.SafeColumns == 2 && portableResolved.SafeRows == 1,
        "便携工程没有保留图片子帧行列信息");
    Assert(CountWorkspaceEditors(portableLoaded.WorkspaceLayout.Root, WorkspaceEditorKind.AnimationView) == 2,
        "便携工程没有保留双视图工作区布局");
    Assert(portableLoaded.Curves.Count == 1 && portableLoaded.Curves[0].Keys.Count == 1 &&
           portableLoaded.Curves[0].Keys[0].HandleMode == CurveHandleMode.Aligned &&
           Math.Abs(portableLoaded.Curves[0].Keys[0].RightValueOffset - 6) < 0.0001f,
        "便携工程没有保留曲线、关键点和 Bezier 手柄");

    var dualTimelineLayout = new WorkspaceLayoutPresetService().Create(WorkspacePreset.DualTimeline);
    Assert(CountWorkspaceEditors(dualTimelineLayout.Root, WorkspaceEditorKind.Timeline) == 2 &&
           CountWorkspaceEditors(dualTimelineLayout.Root, WorkspaceEditorKind.AnimationView) == 1,
        "双时间轴工作区预设结构错误");
    var graphLayout = new WorkspaceLayoutPresetService().Create(WorkspacePreset.GraphEditing);
    Assert(CountWorkspaceEditors(graphLayout.Root, WorkspaceEditorKind.GraphEditor) == 1 &&
           CountWorkspaceEditors(graphLayout.Root, WorkspaceEditorKind.Timeline) == 1 &&
           CountWorkspaceEditors(graphLayout.Root, WorkspaceEditorKind.AnimationView) == 1,
        "曲线动画工作区没有同时包含动画视图、时间轴和曲线编辑器");

    var droppedJpegPath = Path.Combine(root, "external-dropped-part.jpg");
    SaveBitmap(BitmapSource.Create(8, 6, 96, 96, PixelFormats.Bgr24, null,
        Enumerable.Repeat((byte)160, 8 * 6 * 3).ToArray(), 24),
        new JpegBitmapEncoder(), droppedJpegPath);
    var dropEditor = new EditorViewModel(new ActionCatalogService());
    var dropResources = new OriginalResourceService();
    var tracksBeforeDrop = dropEditor.Project.Animation.Tracks.Count;
    dropEditor.BeginEditTransaction("拖入 JPG 到动画");
    var droppedSymbol = dropResources.ImportImage(dropEditor.Project, droppedJpegPath);
    var droppedTrack = dropEditor.AddImageTrack("external_dropped_part", droppedSymbol, 120, 80);
    dropEditor.EndEditTransaction();
    Assert(dropResources.ResolveImage(dropEditor.Project, droppedSymbol) is not null,
        "外部拖入 JPG 没有被图片资源服务读取");
    var droppedFrame = droppedTrack.Frames[dropEditor.CurrentFrame];
    Assert(droppedFrame.Image == droppedSymbol && droppedFrame.X == 120 && droppedFrame.Y == 80 &&
           ReferenceEquals(dropEditor.SelectedTrack, droppedTrack),
        "拖入图片没有在落点创建并选中可动画轨道");
    dropEditor.Undo();
    Assert(dropEditor.Project.Animation.Tracks.Count == tracksBeforeDrop &&
           !dropEditor.Project.ImageBindings.ContainsKey(droppedSymbol),
        "拖入图片和新轨道没有作为同一个步骤撤销");

    var visualAnimTrack = new AnimationTrack { Name = "anim_face" };
    visualAnimTrack.EnsureFrameCount(2);
    visualAnimTrack.Frames[0].Image = "IMAGE_REANIM_FACE";
    Assert(!visualAnimTrack.IsActionTrack, "带图片的 anim_* 轨道不能当成动作标记隐藏");
    var visualActionDocument = new AnimationDocument();
    visualActionDocument.Tracks.Add(visualAnimTrack);
    Assert(new ActionCatalogService().InferActions(visualActionDocument, EntityKind.Plant)
        .Any(action => action.Track == "anim_face"), "带图片的 anim_* 轨道仍必须能被识别为动作片段");

    var actionDocument = new AnimationDocument();
    var markerTrack = new AnimationTrack { Name = "anim_attack" };
    var changedTrack = new AnimationTrack { Name = "arm" };
    var staticTrack = new AnimationTrack { Name = "shadow" };
    markerTrack.EnsureFrameCount(7);
    changedTrack.EnsureFrameCount(7);
    staticTrack.EnsureFrameCount(7);
    markerTrack.Frames[0].Frame = -1;
    markerTrack.Frames[2].Frame = 0;
    markerTrack.Frames[6].Frame = -1;
    changedTrack.Frames[0].X = 10;
    changedTrack.Frames[3].X = 20;
    staticTrack.Frames[0].X = 5;
    actionDocument.Tracks.Add(markerTrack);
    actionDocument.Tracks.Add(changedTrack);
    actionDocument.Tracks.Add(staticTrack);
    var actionDefinition = new ActionDefinition { Id = "attack", Track = "anim_attack" };
    var actionView = new ActionViewService();
    var actionRange = actionView.GetRange(actionDocument, actionDefinition);
    Assert(actionRange.Start == 2 && actionRange.End == 5, "动作范围识别错误");
    var actionTracks = actionView.GetTimelineTracks(actionDocument, actionDefinition);
    Assert(actionTracks.Contains(markerTrack) && actionTracks.Contains(changedTrack), "动作视图漏掉标记或变化轨道");
    Assert(!actionTracks.Contains(staticTrack), "动作视图不应列出动作期间未变化的轨道");

    var editor = new EditorViewModel(new ActionCatalogService());
    var originalX = editor.CurrentResolvedFrame?.X ?? 0;
    editor.MoveSelected(12, 0);
    AssertNear(editor.CurrentResolvedFrame?.X, originalX + 12, "部件移动错误");
    editor.Undo();
    AssertNear(editor.CurrentResolvedFrame?.X, originalX, "Ctrl+Z 历史恢复错误");
    editor.Redo();
    AssertNear(editor.CurrentResolvedFrame?.X, originalX + 12, "Ctrl+Y 历史恢复错误");

    var insertedTweenEditor = new EditorViewModel(new ActionCatalogService());
    insertedTweenEditor.CurrentFrame = 0;
    insertedTweenEditor.CurrentX = 0;
    insertedTweenEditor.CurrentScaleX = 1;
    insertedTweenEditor.SetKeyframe();
    insertedTweenEditor.CurrentFrame = 2;
    insertedTweenEditor.CurrentX = 20;
    insertedTweenEditor.CurrentScaleX = 2;
    insertedTweenEditor.SetKeyframe();
    insertedTweenEditor.CurrentFrame = 1;
    insertedTweenEditor.InsertFrame();
    insertedTweenEditor.InsertFrame();
    AssertNear(insertedTweenEditor.SelectedTrack!.Frames[1].X, 5,
        "在两个关键帧之间插入空帧后没有重新插值位移");
    AssertNear(insertedTweenEditor.SelectedTrack.Frames[2].X, 10,
        "插入多个空帧后中点位移被冻结");
    AssertNear(insertedTweenEditor.SelectedTrack.Frames[3].X, 15,
        "插入多个空帧后结束前位移被冻结");
    AssertNear(insertedTweenEditor.SelectedTrack.Frames[1].ScaleX, 1.25f,
        "在两个关键帧之间插入空帧后没有重新插值缩放");
    AssertNear(insertedTweenEditor.SelectedTrack.Frames[2].ScaleX, 1.5f,
        "插入多个空帧后中点缩放被冻结");
    AssertNear(insertedTweenEditor.SelectedTrack.Frames[3].ScaleX, 1.75f,
        "插入多个空帧后结束前缩放被冻结");
    insertedTweenEditor.Undo();
    AssertNear(insertedTweenEditor.SelectedTrack!.Frames[1].X, 20f / 3f,
        "第一次撤销没有只撤回最后一次插帧");
    insertedTweenEditor.Undo();
    AssertNear(insertedTweenEditor.SelectedTrack!.Frames[1].X, 10,
        "插帧后的自动重插值没有进入撤销历史");

    var legacyInsertEditor = new EditorViewModel(new ActionCatalogService());
    var legacyTrack = legacyInsertEditor.SelectedTrack!;
    foreach (var frame in legacyTrack.Frames) frame.Clear();
    legacyInsertEditor.Project.Curves.Clear();
    legacyTrack.Frames[0].X = 0;
    legacyTrack.Frames[2].X = 30;
    legacyInsertEditor.CurrentFrame = 1;
    legacyInsertEditor.InsertFrame();
    AssertNear(legacyTrack.Frames[1].X, 10,
        "无曲线元数据的旧 compiled 插帧时没有从显式运动帧建立曲线");
    AssertNear(legacyTrack.Frames[2].X, 20,
        "旧 compiled 插入空帧后没有连续重算中间位移");

    var timelineKeyEditor = new EditorViewModel(new ActionCatalogService());
    timelineKeyEditor.CurrentFrame = 10;
    timelineKeyEditor.CurrentX = 40;
    Assert(timelineKeyEditor.CurrentHasKey, "属性编辑没有创建轨道关键帧");
    Assert(timelineKeyEditor.NudgeCurrentKeyframe(1) && timelineKeyEditor.CurrentFrame == 11 &&
           timelineKeyEditor.CurrentHasKey,
        "轨道关键帧没有向右移动一帧");
    timelineKeyEditor.Undo();
    Assert(timelineKeyEditor.CurrentFrame == 10 && timelineKeyEditor.CurrentHasKey,
        "轨道关键帧移动没有完整撤销");
    timelineKeyEditor.Redo();
    Assert(timelineKeyEditor.CurrentFrame == 11 && timelineKeyEditor.CurrentHasKey,
        "轨道关键帧移动没有完整恢复");
    timelineKeyEditor.DeleteCurrentKeyframe();
    Assert(!timelineKeyEditor.CurrentHasKey, "删除关键帧仍保留了轨道关键点");
    timelineKeyEditor.Undo();
    Assert(timelineKeyEditor.CurrentHasKey, "删除关键帧没有进入撤销历史");

    var crossingTimelineEditor = new EditorViewModel(new ActionCatalogService());
    crossingTimelineEditor.CurrentFrame = 5;
    crossingTimelineEditor.CurrentX = 10;
    crossingTimelineEditor.CurrentFrame = 6;
    crossingTimelineEditor.CurrentX = 20;
    crossingTimelineEditor.CurrentFrame = 7;
    crossingTimelineEditor.CurrentX = 30;
    var crossingTrackId = crossingTimelineEditor.SelectedTrack!.EditorId;
    IReadOnlyCollection<TimelineKeySelection> timelineSelection =
        [new TimelineKeySelection(crossingTrackId, 5)];
    crossingTimelineEditor.BeginEditTransaction("连续跨帧");
    timelineSelection = crossingTimelineEditor.MoveTimelineKeys(timelineSelection, 1);
    timelineSelection = crossingTimelineEditor.MoveTimelineKeys(timelineSelection, 1);
    crossingTimelineEditor.EndEditTransaction();
    var crossingTimelineCurve = crossingTimelineEditor.GetCurve(CurveChannel.X)!;
    Assert(crossingTimelineCurve.Keys.Count(key => key.Frame is >= 5 and <= 7) == 3,
        "时间轴关键帧跨过其他关键帧时发生了合并或丢失");
    AssertNear(crossingTimelineCurve.Keys.Single(key => key.Frame == 5).Value, 20,
        "时间轴跨帧后被跨过的第一帧没有保留");
    AssertNear(crossingTimelineCurve.Keys.Single(key => key.Frame == 6).Value, 30,
        "时间轴跨帧后被跨过的第二帧没有保留");
    AssertNear(crossingTimelineCurve.Keys.Single(key => key.Frame == 7).Value, 10,
        "时间轴拖动帧没有按 Blender 式重排到目标位置");
    crossingTimelineEditor.Undo();
    AssertNear(crossingTimelineEditor.GetCurve(CurveChannel.X)!.Keys.Single(key => key.Frame == 5).Value, 10,
        "连续跨帧拖动没有作为一个操作撤销");

    var curveEditor = new EditorViewModel(new ActionCatalogService());
    curveEditor.CurrentFrame = 0;
    curveEditor.CurrentX = 0;
    curveEditor.CurrentFrame = 10;
    curveEditor.CurrentX = 100;
    curveEditor.SetCurveInterpolation(CurveChannel.X, CurveInterpolationMode.Linear);
    AssertNear(curveEditor.SelectedTrack!.Frames[5].X, 50, "线性 F-Curve 没有烘焙到中间帧");
    curveEditor.SetCurveInterpolation(CurveChannel.X, CurveInterpolationMode.Bezier);
    curveEditor.SetCurveHandle(CurveChannel.X, 0, false, 3, 100);
    curveEditor.SetCurveHandle(CurveChannel.X, 10, true, 7, 100);
    Assert((curveEditor.SelectedTrack.Frames[5].X ?? 0) > 70,
        "拖动 Bezier 手柄没有改变曲线和烘焙后的动画值");
    curveEditor.SetCurveHandleMode(CurveChannel.X, 0, CurveHandleMode.Aligned);
    Assert(curveEditor.GetCurve(CurveChannel.X)?.Keys.First(key => key.Frame == 0).HandleMode == CurveHandleMode.Aligned,
        "曲线关键点没有切换为对齐手柄");
    curveEditor.MoveCurveKey(CurveChannel.X, 10, 12, 100);
    Assert(curveEditor.GetCurveKeyFrames(CurveChannel.X).Contains(12) &&
           !curveEditor.GetCurveKeyFrames(CurveChannel.X).Contains(10),
        "曲线关键点没有沿时间轴移动");
    curveEditor.DeleteCurveKey(CurveChannel.X, 12);
    Assert(!curveEditor.GetCurveKeyFrames(CurveChannel.X).Contains(12), "曲线关键点删除失败");
    curveEditor.Undo();
    Assert(curveEditor.GetCurveKeyFrames(CurveChannel.X).Contains(12), "曲线关键点删除没有进入撤销历史");

    var crossingCurveEditor = new EditorViewModel(new ActionCatalogService());
    foreach (var (frame, value) in new[] { (5, 10f), (6, 20f), (7, 30f) })
    {
        crossingCurveEditor.CurrentFrame = frame;
        crossingCurveEditor.CurrentX = value;
    }
    IReadOnlyCollection<CurveKeySelection> curveSelection = [new(CurveChannel.X, 5)];
    crossingCurveEditor.BeginEditTransaction("连续拖动曲线点");
    curveSelection = crossingCurveEditor.MoveCurveKeys(curveSelection, 1, 0);
    curveSelection = crossingCurveEditor.MoveCurveKeys(curveSelection, 1, 5);
    crossingCurveEditor.EndEditTransaction();
    var crossingCurve = crossingCurveEditor.GetCurve(CurveChannel.X)!;
    Assert(crossingCurve.Keys.Count(key => key.Frame is >= 5 and <= 7) == 3,
        "曲线关键点跨过其他点时发生了合并或丢失");
    AssertNear(crossingCurve.Keys.Single(key => key.Frame == 7).Value, 15,
        "批量曲线拖动没有同时保留时间偏移和值偏移");

    var centeredRotationEditor = new EditorViewModel(new ActionCatalogService());
    centeredRotationEditor.CurrentX = 25;
    centeredRotationEditor.CurrentY = 40;
    centeredRotationEditor.CurrentScaleX = 1.2f;
    centeredRotationEditor.CurrentScaleY = 0.8f;
    var localCenter = new Point(50, 25);
    var beforeRotation = ReanimationRenderMath.CreateScreenMatrix(
        centeredRotationEditor.CurrentResolvedFrame!, 1, new Point()).Transform(localCenter);
    centeredRotationEditor.RotateSelectedAround(47, localCenter);
    var afterRotation = ReanimationRenderMath.CreateScreenMatrix(
        centeredRotationEditor.CurrentResolvedFrame!, 1, new Point()).Transform(localCenter);
    AssertNearDouble(afterRotation.X, beforeRotation.X, "旋转后图片中心 X 发生漂移");
    AssertNearDouble(afterRotation.Y, beforeRotation.Y, "旋转后图片中心 Y 发生漂移");

    var longHistoryEditor = new EditorViewModel(new ActionCatalogService());
    var longHistoryStart = longHistoryEditor.CurrentResolvedFrame?.X ?? 0;
    for (var index = 0; index < 75; index++) longHistoryEditor.MoveSelected(1, 0);
    AssertNear(longHistoryEditor.CurrentResolvedFrame?.X, longHistoryStart + 75, "连续编辑结果错误");
    for (var index = 0; index < 75; index++) longHistoryEditor.Undo();
    AssertNear(longHistoryEditor.CurrentResolvedFrame?.X, longHistoryStart, "Ctrl+Z 没有保留全部 75 步历史");
    for (var index = 0; index < 75; index++) longHistoryEditor.Redo();
    AssertNear(longHistoryEditor.CurrentResolvedFrame?.X, longHistoryStart + 75, "Ctrl+Y 没有按顺序恢复全部 75 步历史");
    longHistoryEditor.Undo();
    longHistoryEditor.MoveSelected(3, 0);
    Assert(!longHistoryEditor.CanRedo, "撤销后产生新编辑时必须清空旧的恢复分支");

    var cappedHistoryEditor = new EditorViewModel(new ActionCatalogService());
    var cappedHistoryStart = cappedHistoryEditor.CurrentResolvedFrame?.X ?? 0;
    for (var index = 0; index < 125; index++) cappedHistoryEditor.MoveSelected(1, 0);
    for (var index = 0; index < EditHistoryService.MaximumEntries; index++) cappedHistoryEditor.Undo();
    AssertNear(cappedHistoryEditor.CurrentResolvedFrame?.X, cappedHistoryStart + 25,
        "撤销历史没有严格保留最近 100 步");
    Assert(!cappedHistoryEditor.CanUndo, "撤销历史超过了 100 步上限");
    for (var index = 0; index < EditHistoryService.MaximumEntries; index++) cappedHistoryEditor.Redo();
    AssertNear(cappedHistoryEditor.CurrentResolvedFrame?.X, cappedHistoryStart + 125,
        "100 步恢复历史没有完整保留");

    var fullHistoryEditor = new EditorViewModel(new ActionCatalogService());
    var originalName = fullHistoryEditor.ProjectDisplayName;
    fullHistoryEditor.ProjectDisplayName = "历史测试植物";
    fullHistoryEditor.ProjectHealth = 999;
    fullHistoryEditor.AnimationFps = 24;
    fullHistoryEditor.SelectedAction = fullHistoryEditor.Actions.First();
    fullHistoryEditor.SelectedActionDisplayName = "历史动作";
    fullHistoryEditor.BeginEditTransaction("导入图片资源");
    fullHistoryEditor.Project.ImageBindings["IMAGE_HISTORY"] = "history.png";
    fullHistoryEditor.EndEditTransaction();
    fullHistoryEditor.Undo();
    Assert(!fullHistoryEditor.Project.ImageBindings.ContainsKey("IMAGE_HISTORY"), "图片导入没有进入撤销历史");
    fullHistoryEditor.Undo();
    Assert(fullHistoryEditor.SelectedActionDisplayName != "历史动作", "动作字段没有进入撤销历史");
    fullHistoryEditor.Undo();
    AssertNear(fullHistoryEditor.AnimationFps, 12, "FPS 没有进入撤销历史");
    fullHistoryEditor.Undo();
    Assert(fullHistoryEditor.ProjectHealth == 300, "实体数值没有进入撤销历史");
    fullHistoryEditor.Undo();
    Assert(fullHistoryEditor.ProjectDisplayName == originalName, "工程文本没有进入撤销历史");
    for (var index = 0; index < 5; index++) fullHistoryEditor.Redo();
    Assert(fullHistoryEditor.ProjectDisplayName == "历史测试植物" &&
           fullHistoryEditor.ProjectHealth == 999 &&
           Math.Abs(fullHistoryEditor.AnimationFps - 24) < 0.0001f &&
           fullHistoryEditor.SelectedActionDisplayName == "历史动作" &&
           fullHistoryEditor.Project.ImageBindings.ContainsKey("IMAGE_HISTORY"),
        "Ctrl+Y 没有恢复工程、动作、FPS、数值和图片导入的完整编辑历史");

    var splitPeaProject = new EditorProject
    {
        SourceAnimationPath = Path.Combine("compiled", "reanim", "SplitPea.reanim.compiled"),
        Animation = CreateCompositePreviewDocument()
    };
    var entityPreview = new EntityPreviewProfileService();
    Assert(entityPreview.GetRepresentativeFrame(splitPeaProject) == 4, "双向射手实体预览主体帧错误");
    var splitPlan = entityPreview.CreatePlan(splitPeaProject, 4, null);
    Assert(splitPlan is not null && splitPlan.OverlayLayers.Select(layer => layer.Frame).SequenceEqual([9, 14]),
        "双向射手实体预览没有组合前后两个头部");
    Assert(entityPreview.CreatePlan(splitPeaProject, 4, new ActionDefinition { Track = "anim_idle" }) is null,
        "进入动作编辑视图后不应叠加其他动作实例");
    var zombiePreviewProject = new EditorProject
    {
        SourceAnimationPath = Path.Combine("compiled", "reanim", "Zombie.reanim.compiled"),
        Animation = splitPeaProject.Animation
    };
    var bucketTrack = new AnimationTrack { Name = "anim_bucket" };
    Assert(!entityPreview.IsTrackVisible(zombiePreviewProject, bucketTrack, null),
        "普通僵尸实体预览必须隐藏铁桶等可选装备轨道");
    Assert(entityPreview.IsTrackVisible(zombiePreviewProject, bucketTrack,
            new ActionDefinition { Track = "anim_idle" }),
        "动作编辑视图必须允许检查被实体配置隐藏的装备轨道");

    var jsoncPath = Path.Combine(root, "sample.jsonc");
    File.WriteAllText(jsoncPath, "{\n  // 保留这条注释\n  \"textures\": []\n}\n");
    new JsoncArrayEditor().Upsert(jsoncPath, "textures", "id", new System.Text.Json.Nodes.JsonObject
    {
        ["id"] = "TEST_IMAGE",
        ["path"] = "pvzmod/images/test.png"
    });
    var jsonc = File.ReadAllText(jsoncPath);
    Assert(jsonc.Contains("保留这条注释", StringComparison.Ordinal), "JSONC 注释未保留");
    Assert(jsonc.Contains("TEST_IMAGE", StringComparison.Ordinal), "JSONC 条目未写入");

    var fakePng = Path.Combine(root, "body.png");
    File.WriteAllBytes(fakePng, [137, 80, 78, 71, 13, 10, 26, 10]);
    var project = new EditorProject
    {
        Id = "TEST_PLANT",
        DisplayName = "测试植物",
        Animation = source,
        Actions = new ObservableCollection<ActionDefinition>
        {
            new() { Id = "idle", DisplayName = "待机", Track = "anim_idle" }
        },
        ImageBindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["IMAGE_REANIM_TEST_BODY"] = fakePng
        }
    };
    var zipPath = Path.Combine(root, "package.zip");
    new ProjectPackageService(new ReanimCodecService(), new JsoncArrayEditor()).CreatePackage(project, zipPath);
    using (var zip = ZipFile.OpenRead(zipPath))
    {
        Assert(zip.Entries.Any(entry => entry.FullName.EndsWith("TEST_PLANT.reanim.compiled", StringComparison.Ordinal)), "包内缺少 compiled 动画");
        Assert(zip.Entries.Any(entry => entry.FullName.EndsWith("entity.fragment.jsonc", StringComparison.Ordinal)), "包内缺少实体配置片段");
        Assert(zip.Entries.Any(entry => entry.FullName.EndsWith("body.png", StringComparison.Ordinal)), "包内缺少图片");
    }

    Console.WriteLine("PASS: Raw/compiled 往返、自动补间、JSONC 合并和 ZIP 打包全部通过。");
}
finally
{
    var safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PvZAnimationStudioTests"));
    var fullRoot = Path.GetFullPath(root);
    if (fullRoot.StartsWith(safeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullRoot))
        Directory.Delete(fullRoot, true);
}

static AnimationDocument CreateDocument()
{
    var document = new AnimationDocument { Fps = 12, DoScale = 1 };
    var action = new AnimationTrack { Name = "anim_idle" };
    var body = new AnimationTrack { Name = "body" };
    action.EnsureFrameCount(4);
    body.EnsureFrameCount(4);
    action.Frames[0].Frame = 0;
    body.Frames[0].X = 10;
    body.Frames[0].Y = -5;
    body.Frames[0].ScaleX = 1;
    body.Frames[0].ScaleY = 1;
    body.Frames[0].Alpha = 1;
    body.Frames[0].Image = "IMAGE_REANIM_TEST_BODY";
    body.Frames[2].X = 14.5f;
    body.Frames[2].SkewY = 12;
    body.Frames[3].Text = "中文文本";
    document.Tracks.Add(action);
    document.Tracks.Add(body);
    return document;
}

static void AssertDocument(AnimationDocument expected, AnimationDocument actual)
{
    AssertNear(actual.Fps, expected.Fps, "FPS 往返错误");
    Assert(actual.Tracks.Count == expected.Tracks.Count, "轨道数量往返错误");
    for (var trackIndex = 0; trackIndex < expected.Tracks.Count; trackIndex++)
    {
        var expectedTrack = expected.Tracks[trackIndex];
        var actualTrack = actual.Tracks[trackIndex];
        Assert(actualTrack.Name == expectedTrack.Name, "轨道名称往返错误");
        Assert(actualTrack.Frames.Count == expectedTrack.Frames.Count, "帧数量往返错误");
        for (var frameIndex = 0; frameIndex < expectedTrack.Frames.Count; frameIndex++)
        {
            var left = expectedTrack.Frames[frameIndex];
            var right = actualTrack.Frames[frameIndex];
            AssertNullableNear(right.X, left.X, "X 往返错误");
            AssertNullableNear(right.Y, left.Y, "Y 往返错误");
            AssertNullableNear(right.SkewX, left.SkewX, "SkewX 往返错误");
            AssertNullableNear(right.SkewY, left.SkewY, "SkewY 往返错误");
            AssertNullableNear(right.ScaleX, left.ScaleX, "ScaleX 往返错误");
            AssertNullableNear(right.ScaleY, left.ScaleY, "ScaleY 往返错误");
            AssertNullableNear(right.Frame, left.Frame, "Frame 往返错误");
            AssertNullableNear(right.Alpha, left.Alpha, "Alpha 往返错误");
            Assert(right.Image == left.Image && right.Font == left.Font && right.Text == left.Text, "字符串字段往返错误");
        }
    }
}

static void AssertNullableNear(float? actual, float? expected, string message)
{
    if (!actual.HasValue || !expected.HasValue)
    {
        Assert(actual.HasValue == expected.HasValue, message);
        return;
    }
    AssertNear(actual, expected.Value, message);
}

static void AssertNear(float? actual, float expected, string message) =>
    Assert(actual.HasValue && Math.Abs(actual.Value - expected) < 0.0001f, message);

static void AssertNearDouble(double actual, double expected, string message) =>
    Assert(Math.Abs(actual - expected) < 0.0001, message);

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static int CountWorkspaceEditors(WorkspaceLayoutNode node, WorkspaceEditorKind editor) =>
    node.IsLeaf
        ? node.Editor == editor ? 1 : 0
        : CountWorkspaceEditors(node.First!, editor) + CountWorkspaceEditors(node.Second!, editor);

static void SaveBitmap(BitmapSource bitmap, BitmapEncoder encoder, string path)
{
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = File.Create(path);
    encoder.Save(stream);
}

static void RenderWorkspaceScreenshot(string path, string? animationPath, WorkspacePreset? preset, bool openFloating)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var application = new App();
            application.InitializeComponent();
            var window = new MainWindow
            {
                Width = 1600,
                Height = 900,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                WindowStyle = WindowStyle.None
            };
            if (!string.IsNullOrWhiteSpace(animationPath))
                window.LoadAnimationFile(Path.GetFullPath(animationPath));
            if (window.DataContext is not EditorViewModel screenshotViewModel || screenshotViewModel.SelectedTrack is null)
                throw new InvalidOperationException("加载动画后没有自动选择第一个可编辑轨道。");
            var workspace = window.FindName("WorkspaceHost") as WorkspaceHostControl;
            if (preset.HasValue) workspace?.ApplyPreset(preset.Value);
            window.Show();
            if (openFloating)
                workspace?.OpenFloatingWindow(WorkspaceEditorKind.Inspector);
            window.UpdateLayout();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => window.UpdateLayout(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (openFloating && Application.Current.Windows.Count < 2)
                throw new InvalidOperationException("独立工作区窗口没有创建。 ");
            var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
            var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            SaveBitmap(bitmap, new PngBitmapEncoder(), Path.GetFullPath(path));
            window.Close();
            application.Shutdown();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
    Console.WriteLine($"PASS: 工作区窗口已渲染到 {Path.GetFullPath(path)}");
}

static AnimationDocument CreateCompositePreviewDocument()
{
    var document = new AnimationDocument();
    foreach (var (name, start, end) in new[]
             {
                 ("anim_idle", 2, 6),
                 ("anim_splitpea_idle", 8, 10),
                 ("anim_head_idle", 12, 16)
             })
    {
        var track = new AnimationTrack { Name = name };
        track.EnsureFrameCount(17);
        track.Frames[0].Frame = -1;
        track.Frames[start].Frame = 0;
        if (end + 1 < track.Frames.Count) track.Frames[end + 1].Frame = -1;
        document.Tracks.Add(track);
    }
    return document;
}

static void RunAnimationAudit(string sourceDirectory)
{
    var codec = new CompiledReanimCodec();
    var gameRoot = Path.GetFullPath(Path.Combine(sourceDirectory, "..", ".."));
    var resources = new OriginalResourceService();
    resources.RebuildIndex(gameRoot);
    var project = new EditorProject { GameRoot = gameRoot };
    Console.WriteLine("name\ttracks\tframes\tvisual\tactions\tmarker_actions\tattachers\tmissing_images\tattacher_text\taction_summary");
    foreach (var path in Directory.EnumerateFiles(Path.GetFullPath(sourceDirectory), "*.reanim.compiled")
                 .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
    {
        var document = codec.Load(path);
        var attachers = document.Tracks.Where(track =>
            track.Name.StartsWith("attacher__", StringComparison.OrdinalIgnoreCase)).ToArray();
        var attacherText = attachers.SelectMany(track => track.Frames)
            .Select(frame => frame.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var missingImages = document.Tracks.SelectMany(track => track.Frames)
            .Select(frame => frame.Image)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(symbol => resources.ResolvePath(project, symbol) is null)
            .ToArray();
        var kind = Path.GetFileName(path).StartsWith("Zombie", StringComparison.OrdinalIgnoreCase)
            ? EntityKind.Zombie
            : EntityKind.Plant;
        var actionView = new ActionViewService();
        var inferredActions = new ActionCatalogService().InferActions(document, kind);
        var actionSummary = inferredActions.Select(action =>
        {
            var range = actionView.GetRange(document, action);
            var midpoint = range.Start + (range.Count - 1) / 2;
            var visible = document.Tracks.Count(track =>
            {
                if (track.IsActionTrack || !track.HasRenderableContent) return false;
                var frame = track.ResolveFrame(midpoint);
                return frame.Frame >= 0 && frame.Alpha > 0 && !string.IsNullOrWhiteSpace(frame.Image);
            });
            return $"{action.Track}:{range.Start}-{range.End}:v{visible}";
        });
        Console.WriteLine(string.Join('\t',
            Path.GetFileName(path), document.Tracks.Count, document.FrameCount,
            document.Tracks.Count(track => track.HasRenderableContent && !track.IsActionTrack),
            inferredActions.Count, document.Tracks.Count(track => track.IsActionTrack), attachers.Length,
            string.Join(" | ", missingImages),
            string.Join(" | ", attacherText),
            string.Join(" | ", actionSummary)));
    }
}

static void RunTrackInspection(string sourcePath, IEnumerable<string> frameArguments)
{
    var document = new ReanimCodecService().Load(Path.GetFullPath(sourcePath));
    var requestedFrames = frameArguments
        .Select(value => int.TryParse(value, out var parsed) ? parsed : -1)
        .Where(frame => frame >= 0 && frame < document.FrameCount)
        .Distinct()
        .ToArray();
    if (requestedFrames.Length == 0)
        requestedFrames = Enumerable.Range(0, document.FrameCount).ToArray();

    Console.WriteLine($"{Path.GetFileName(sourcePath)}: {document.Tracks.Count} 轨 / {document.FrameCount} 帧");
    foreach (var frameIndex in requestedFrames)
    {
        Console.WriteLine($"\n[帧 {frameIndex}]");
        foreach (var track in document.Tracks)
        {
            var frame = track.ResolveFrame(frameIndex);
            if (frame.Frame < 0 || frame.Alpha <= 0 ||
                (string.IsNullOrWhiteSpace(frame.Image) && string.IsNullOrWhiteSpace(frame.Text)))
                continue;
            Console.WriteLine(string.Join('\t', track.Name, frame.Image ?? frame.Text ?? string.Empty,
                $"x={frame.X:0.###}", $"y={frame.Y:0.###}",
                $"kx={frame.SkewX:0.###}", $"ky={frame.SkewY:0.###}",
                $"sx={frame.ScaleX:0.###}", $"sy={frame.ScaleY:0.###}",
                $"f={frame.Frame:0.###}", $"a={frame.Alpha:0.###}"));
        }
    }
}
