using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PvZAnimationStudio.Models;
using PvZAnimationStudio.Services;
using PvZAnimationStudio.ViewModels;

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

static void SaveBitmap(BitmapSource bitmap, BitmapEncoder encoder, string path)
{
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = File.Create(path);
    encoder.Save(stream);
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
