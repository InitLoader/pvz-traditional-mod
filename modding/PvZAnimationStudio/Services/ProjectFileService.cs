using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public sealed class ProjectFileService
{
    private const int MaxAssetCount = 2048;
    private const long MaxAssetBytes = 64L * 1024 * 1024;
    private const long MaxTotalAssetBytes = 512L * 1024 * 1024;
    private readonly ProjectCloneService _clone = new();
    private readonly OriginalResourceService _resources;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public ProjectFileService(OriginalResourceService resources) => _resources = resources;

    public EditorProject Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return IsZip(fullPath) ? LoadPortable(fullPath) : LoadLegacyJson(fullPath);
    }

    public void Save(EditorProject project, string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            SaveLegacyJson(project, fullPath);
        else
            SavePortable(project, fullPath);
        project.ProjectPath = fullPath;
    }

    private EditorProject LoadLegacyJson(string path)
    {
        var project = Deserialize(File.ReadAllText(path, Encoding.UTF8));
        PrepareLoadedProject(project, path);
        return project;
    }

    private void SaveLegacyJson(EditorProject project, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var copy = _clone.Clone(project);
        copy.ProjectPath = null;
        File.WriteAllText(path, JsonSerializer.Serialize(copy, Options), new UTF8Encoding(false));
    }

    private void SavePortable(EditorProject project, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        var portable = _clone.Clone(project);
        portable.SchemaVersion = Math.Max(5, portable.SchemaVersion);
        portable.ProjectPath = null;
        portable.ImageBindings.Clear();
        portable.ImageLayouts.Clear();
        portable.OriginalImageReferences.Clear();

        try
        {
            using (var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, true, Encoding.UTF8))
            {
                var usedSymbols = project.Animation.Tracks.SelectMany(track => track.Frames)
                        .Select(frame => frame.Image)
                        .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                        .Select(symbol => symbol!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var symbols = project.ImageBindings.Keys
                    .Where(symbol => !project.OriginalImageReferences.Contains(symbol))
                    .Concat(usedSymbols)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (symbols.Length > MaxAssetCount)
                    throw new InvalidDataException($"工程图片数量超过 {MaxAssetCount}。 ");

                long totalEncodedBytes = 0;
                foreach (var symbol in symbols)
                {
                    var image = _resources.ResolveImage(project, symbol)
                                ?? throw new InvalidDataException($"无法嵌入图片 {symbol}；请先选择正确的游戏目录或重新导入图片。 ");
                    var safeName = AssetFileName(symbol);
                    var entryPath = $"assets/{safeName}";
                    var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
                    using var encoded = new MemoryStream();
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image.Bitmap));
                    encoder.Save(encoded);
                    if (encoded.Length > MaxAssetBytes || (totalEncodedBytes += encoded.Length) > MaxTotalAssetBytes)
                        throw new InvalidDataException($"图片 {symbol} 或工程图片总大小超过安全限制。 ");
                    encoded.Position = 0;
                    using (var output = entry.Open())
                        encoded.CopyTo(output);
                    portable.ImageBindings[symbol] = entryPath;
                    portable.ImageLayouts[symbol] = new ImageLayoutDefinition
                    {
                        Columns = image.SafeColumns,
                        Rows = image.SafeRows
                    };
                    if (project.OriginalImageReferences.Contains(symbol) ||
                        (!project.ImageBindings.ContainsKey(symbol) && _resources.IsOriginalGameSymbol(project, symbol)))
                        portable.OriginalImageReferences.Add(symbol);
                }

                var projectEntry = archive.CreateEntry("project.json", CompressionLevel.Optimal);
                using var projectStream = projectEntry.Open();
                using var writer = new StreamWriter(projectStream, new UTF8Encoding(false));
                writer.Write(JsonSerializer.Serialize(portable, Options));
            }
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private EditorProject LoadPortable(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > MaxAssetCount + 1)
            throw new InvalidDataException("便携工程内文件数量超过安全限制。 ");
        var projectEntry = archive.GetEntry("project.json")
                           ?? throw new InvalidDataException("便携工程缺少 project.json。 ");
        if (projectEntry.Length is <= 0 or > 64L * 1024 * 1024)
            throw new InvalidDataException("project.json 大小不安全。 ");
        string json;
        using (var reader = new StreamReader(projectEntry.Open(), Encoding.UTF8, true))
            json = reader.ReadToEnd();
        var project = Deserialize(json);

        var cacheRoot = GetCacheRoot(path);
        Directory.CreateDirectory(cacheRoot);
        var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var (symbol, entryPath) in project.ImageBindings)
        {
            var normalized = entryPath.Replace('\\', '/');
            if (!normalized.StartsWith("assets/", StringComparison.Ordinal) || normalized.Contains("../", StringComparison.Ordinal))
                throw new InvalidDataException($"工程图片路径不安全：{entryPath}");
            var entry = archive.GetEntry(normalized)
                        ?? throw new InvalidDataException($"工程缺少嵌入图片：{normalized}");
            if (entry.Length is < 0 or > MaxAssetBytes || (totalBytes += entry.Length) > MaxTotalAssetBytes)
                throw new InvalidDataException("工程图片大小超过安全限制。 ");
            var extension = Path.GetExtension(normalized);
            if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)) extension = ".png";
            var outputPath = Path.Combine(cacheRoot, Path.GetFileNameWithoutExtension(AssetFileName(symbol)) + extension);
            using var input = entry.Open();
            using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            input.CopyTo(output);
            extracted[symbol] = outputPath;
        }
        project.ImageBindings = extracted;
        PrepareLoadedProject(project, path);
        return project;
    }

    private static EditorProject Deserialize(string json) =>
        JsonSerializer.Deserialize<EditorProject>(json, Options)
        ?? throw new InvalidDataException("工程文件内容为空。 ");

    private void PrepareLoadedProject(EditorProject project, string path)
    {
        var loadedSchemaVersion = project.SchemaVersion;
        project.ProjectPath = path;
        project.ImageBindings = new Dictionary<string, string>(project.ImageBindings ?? [], StringComparer.OrdinalIgnoreCase);
        project.ImageLayouts = new Dictionary<string, ImageLayoutDefinition>(project.ImageLayouts ?? [], StringComparer.OrdinalIgnoreCase);
        project.OriginalImageReferences = new HashSet<string>(
            project.OriginalImageReferences ?? [], StringComparer.OrdinalIgnoreCase);
        project.Curves ??= [];
        foreach (var track in project.Animation.Tracks)
        {
            if (string.IsNullOrWhiteSpace(track.EditorId)) track.EditorId = Guid.NewGuid().ToString("N");
        }
        foreach (var curve in project.Curves) curve.Keys ??= [];
        project.Actions ??= [];
        foreach (var action in project.Actions)
        {
            action.Replaces ??= [];
            action.Events ??= [];
        }
        if (string.IsNullOrWhiteSpace(project.InitialActionId)) project.InitialActionId = "idle";
        project.WorkspaceLayout ??= new WorkspaceLayoutPresetService().Create(WorkspacePreset.Animation);
        if (loadedSchemaVersion < 5 && !string.IsNullOrWhiteSpace(project.GameRoot))
        {
            _resources.RebuildIndex(project.GameRoot);
            foreach (var symbol in project.Animation.Tracks.SelectMany(track => track.Frames)
                         .Select(frame => frame.Image)
                         .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                         .Select(symbol => symbol!)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (_resources.IsOriginalGameSymbol(project, symbol))
                    project.OriginalImageReferences.Add(symbol);
            }
        }
        project.SchemaVersion = Math.Max(5, project.SchemaVersion);
    }

    private static bool IsZip(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> signature = stackalloc byte[4];
        return stream.Read(signature) == 4 && signature[0] == (byte)'P' && signature[1] == (byte)'K';
    }

    private static string GetCacheRoot(string projectPath)
    {
        var file = new FileInfo(projectPath);
        var identity = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..20];
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PvZAnimationStudio", "ProjectCache", hash);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var characters = value.Select(character => invalid.Contains(character) || character is '/' or '\\' ? '_' : character).ToArray();
        var result = new string(characters).Trim('.', ' ');
        return string.IsNullOrWhiteSpace(result) ? "asset" : result;
    }

    private static string AssetFileName(string symbol)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(symbol)))[..10];
        return $"{SanitizeFileName(symbol)}_{hash}.png";
    }
}
