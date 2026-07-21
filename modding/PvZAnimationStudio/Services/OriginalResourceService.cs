using System.Xml;
using System.Xml.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public sealed record ImageResourceInfo(BitmapSource Bitmap, int Columns = 1, int Rows = 1)
{
    public int SafeColumns => Math.Max(1, Columns);
    public int SafeRows => Math.Max(1, Rows);
    public double CelWidth => Bitmap.PixelWidth / (double)SafeColumns;
    public double CelHeight => Bitmap.PixelHeight / (double)SafeRows;
}

public sealed class OriginalResourceService
{
    private readonly Dictionary<string, string> _symbolIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (int Columns, int Rows)> _resourceLayout = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageResourceInfo?> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private string? _indexedRoot;

    public void RebuildIndex(string? gameRoot)
    {
        _symbolIndex.Clear();
        _resourceLayout.Clear();
        _imageCache.Clear();
        _indexedRoot = null;
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot)) return;

        var fullRoot = Path.GetFullPath(gameRoot);
        ReadResourceManifest(Path.Combine(fullRoot, "properties", "resources.xml"));
        foreach (var directory in new[]
                 {
                     Path.Combine(fullRoot, "reanim"),
                     Path.Combine(fullRoot, "images"),
                     Path.Combine(fullRoot, "pvzmod", "images")
                 })
        {
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                         .Where(file => Path.GetExtension(file) is ".png" or ".jpg" or ".jpeg" ||
                                        Path.GetExtension(file).Equals(".PNG", StringComparison.OrdinalIgnoreCase) ||
                                        Path.GetExtension(file).Equals(".JPG", StringComparison.OrdinalIgnoreCase) ||
                                        Path.GetExtension(file).Equals(".JPEG", StringComparison.OrdinalIgnoreCase)))
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                AddSymbol($"IMAGE_REANIM_{NormalizeSymbol(stem)}", file);
                AddSymbol($"IMAGE_{NormalizeSymbol(stem)}", file);
                AddSymbol(NormalizeSymbol(stem), file);
            }
        }
        _indexedRoot = fullRoot;
    }

    public string ImportImage(EditorProject project, string file)
    {
        var stem = NormalizeSymbol(Path.GetFileNameWithoutExtension(file));
        var symbol = stem.StartsWith("IMAGE_REANIM_", StringComparison.OrdinalIgnoreCase)
            ? stem
            : $"IMAGE_REANIM_{stem}";
        var candidate = symbol;
        var suffix = 2;
        while (project.ImageBindings.ContainsKey(candidate))
            candidate = $"{symbol}_{suffix++}";
        project.ImageBindings[candidate] = Path.GetFullPath(file);
        _imageCache.Remove(candidate);
        return candidate;
    }

    public string? ResolvePath(EditorProject project, string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;
        if (project.ImageBindings.TryGetValue(symbol, out var bound))
        {
            var absolute = Path.IsPathRooted(bound)
                ? bound
                : Path.Combine(Path.GetDirectoryName(project.ProjectPath) ?? Environment.CurrentDirectory, bound);
            if (File.Exists(absolute)) return absolute;
        }
        if (!string.Equals(_indexedRoot, project.GameRoot, StringComparison.OrdinalIgnoreCase))
            RebuildIndex(project.GameRoot);
        var normalized = NormalizeSymbol(symbol);
        return _symbolIndex.TryGetValue(normalized, out var original) && File.Exists(original) ? original : null;
    }

    public ImageResourceInfo? ResolveImage(EditorProject project, string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;
        if (_imageCache.TryGetValue(symbol, out var cached)) return cached;
        var path = ResolvePath(project, symbol);
        if (path is null) return _imageCache[symbol] = null;
        try
        {
            var image = LoadBitmap(path);
            image = ApplyLegacyAlphaMask(path, image);
            var normalized = NormalizeSymbol(symbol);
            var layout = _resourceLayout.TryGetValue(normalized, out var value) ? value : (1, 1);
            return _imageCache[symbol] = new ImageResourceInfo(image, layout.Item1, layout.Item2);
        }
        catch
        {
            return _imageCache[symbol] = null;
        }
    }

    public BitmapSource? ResolveBitmap(EditorProject project, string? symbol) =>
        ResolveImage(project, symbol)?.Bitmap;

    private void ReadResourceManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath)) return;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(manifestPath, settings);
            var document = XDocument.Load(reader, LoadOptions.None);
            foreach (var group in document.Descendants("Resources"))
            {
                var defaultPath = string.Empty;
                var defaultPrefix = string.Empty;
                foreach (var element in group.Elements())
                {
                    if (element.Name.LocalName == "SetDefaults")
                    {
                        defaultPath = (string?)element.Attribute("path") ?? defaultPath;
                        defaultPrefix = (string?)element.Attribute("idprefix") ?? defaultPrefix;
                        continue;
                    }
                    if (element.Name.LocalName != "Image") continue;
                    var id = (string?)element.Attribute("id");
                    var path = (string?)element.Attribute("path");
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(path)) continue;
                    var symbol = NormalizeSymbol(id.StartsWith(defaultPrefix, StringComparison.OrdinalIgnoreCase)
                        ? id
                        : defaultPrefix + id);
                    var columns = ParsePositiveInt((string?)element.Attribute("cols"));
                    var rows = ParsePositiveInt((string?)element.Attribute("rows"));
                    _resourceLayout[symbol] = (columns, rows);

                    var assetBase = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(manifestPath))!, defaultPath, path);
                    var absolute = new[] { assetBase + ".png", assetBase + ".jpg", assetBase + ".jpeg" }
                        .FirstOrDefault(File.Exists);
                    if (absolute is not null) AddSymbol(symbol, absolute);
                }
            }
        }
        catch
        {
            // A damaged optional manifest must not prevent users from opening a project.
        }
    }

    private void AddSymbol(string symbol, string path) => _symbolIndex.TryAdd(NormalizeSymbol(symbol), path);

    private static BitmapSource LoadBitmap(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static BitmapSource ApplyLegacyAlphaMask(string colorPath, BitmapSource color)
    {
        if (!Path.GetExtension(colorPath).Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetExtension(colorPath).Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            return color;
        var maskPath = Path.Combine(Path.GetDirectoryName(colorPath)!,
            Path.GetFileNameWithoutExtension(colorPath) + "_.png");
        if (!File.Exists(maskPath)) return color;

        var mask = LoadBitmap(maskPath);
        if (mask.PixelWidth != color.PixelWidth || mask.PixelHeight != color.PixelHeight) return color;
        var colorBgra = new FormatConvertedBitmap(color, PixelFormats.Bgra32, null, 0);
        var maskGray = new FormatConvertedBitmap(mask, PixelFormats.Gray8, null, 0);
        colorBgra.Freeze();
        maskGray.Freeze();
        var colorStride = color.PixelWidth * 4;
        var maskStride = mask.PixelWidth;
        var pixels = new byte[colorStride * color.PixelHeight];
        var alpha = new byte[maskStride * mask.PixelHeight];
        colorBgra.CopyPixels(pixels, colorStride, 0);
        maskGray.CopyPixels(alpha, maskStride, 0);
        for (var index = 0; index < alpha.Length; index++) pixels[index * 4 + 3] = alpha[index];
        var combined = BitmapSource.Create(color.PixelWidth, color.PixelHeight,
            color.DpiX, color.DpiY, PixelFormats.Bgra32, null, pixels, colorStride);
        combined.Freeze();
        return combined;
    }

    private static int ParsePositiveInt(string? value) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : 1;

    private static string NormalizeSymbol(string value)
    {
        var chars = value.ToUpperInvariant().Select(character =>
            char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_').ToArray();
        return new string(chars);
    }
}
