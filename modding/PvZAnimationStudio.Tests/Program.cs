using System.Collections.ObjectModel;
using System.IO.Compression;
using PvZAnimationStudio.Models;
using PvZAnimationStudio.Services;

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

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
