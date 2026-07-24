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
    private readonly HashSet<string> _originalSymbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (int Columns, int Rows)> _resourceLayout = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageResourceInfo?> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource> _bitmapPathCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource?> _thumbnailCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource?> _celCache = new(StringComparer.OrdinalIgnoreCase);
    private string? _indexedRoot;

    public void RebuildIndex(string? gameRoot)
    {
        _symbolIndex.Clear();
        _originalSymbols.Clear();
        _resourceLayout.Clear();
        _imageCache.Clear();
        _bitmapPathCache.Clear();
        _thumbnailCache.Clear();
        _celCache.Clear();
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
                var isOriginal = !directory.Contains(
                    Path.Combine("pvzmod", "images"), StringComparison.OrdinalIgnoreCase);
                AddSymbol($"IMAGE_REANIM_{NormalizeSymbol(stem)}", file, isOriginal);
                AddSymbol($"IMAGE_{NormalizeSymbol(stem)}", file, isOriginal);
                AddSymbol(NormalizeSymbol(stem), file, isOriginal);
            }
        }
        _indexedRoot = fullRoot;
    }

    public string ImportImage(EditorProject project, string file)
    {
        ValidateImportImage(file);
        var stem = NormalizeSymbol(Path.GetFileNameWithoutExtension(file));
        var symbol = stem.StartsWith("IMAGE_REANIM_", StringComparison.OrdinalIgnoreCase)
            ? stem
            : $"IMAGE_REANIM_{stem}";
        var candidate = symbol;
        var suffix = 2;
        while (project.ImageBindings.ContainsKey(candidate))
            candidate = $"{symbol}_{suffix++}";
        project.ImageBindings[candidate] = Path.GetFullPath(file);
        project.OriginalImageReferences.Remove(candidate);
        project.ImageLayouts[candidate] = new ImageLayoutDefinition();
        _imageCache.Remove(candidate);
        _thumbnailCache.Remove(candidate);
        return candidate;
    }

    public void ValidateImportImage(string file)
    {
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
            throw new FileNotFoundException("找不到要导入的图片。", file);
        var extension = Path.GetExtension(file);
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("只支持真正的 PNG、JPG 或 JPEG 图片。不能只修改文件扩展名。 ");
        try
        {
            _ = LoadBitmap(Path.GetFullPath(file));
        }
        catch (Exception exception)
        {
            throw new InvalidDataException(
                "图片内容无法解码。请用画图或图像软件真正另存为 PNG/JPG，不能把 AVIF、WebP 等文件直接改扩展名。",
                exception);
        }
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

    public bool IsOriginalGameSymbol(EditorProject project, string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return false;
        if (!string.Equals(_indexedRoot, project.GameRoot, StringComparison.OrdinalIgnoreCase))
            RebuildIndex(project.GameRoot);
        return _originalSymbols.Contains(NormalizeSymbol(symbol));
    }

    public void MarkOriginalReferences(EditorProject project, bool preferOriginalResources = false)
    {
        foreach (var symbol in project.Animation.Tracks.SelectMany(track => track.Frames)
                     .Select(frame => frame.Image)
                     .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                     .Select(symbol => symbol!)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsOriginalGameSymbol(project, symbol)) continue;
            if (preferOriginalResources)
            {
                project.ImageBindings.Remove(symbol);
                project.ImageLayouts.Remove(symbol);
            }
            if (!project.ImageBindings.ContainsKey(symbol)) project.OriginalImageReferences.Add(symbol);
        }
    }

    public ImageResourceInfo? ResolveImage(EditorProject project, string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;
        if (_imageCache.TryGetValue(symbol, out var cached)) return cached;
        var path = ResolvePath(project, symbol);
        if (path is null) return _imageCache[symbol] = null;
        try
        {
            if (!_bitmapPathCache.TryGetValue(path, out var image))
            {
                image = ApplyLegacyAlphaMask(path, LoadBitmap(path));
                _bitmapPathCache[path] = image;
            }
            var normalized = NormalizeSymbol(symbol);
            var layout = project.ImageLayouts.TryGetValue(symbol, out var embeddedLayout)
                ? (Math.Max(1, embeddedLayout.Columns), Math.Max(1, embeddedLayout.Rows))
                : _resourceLayout.TryGetValue(normalized, out var value) ? value : (1, 1);
            return _imageCache[symbol] = new ImageResourceInfo(image, layout.Item1, layout.Item2);
        }
        catch
        {
            return _imageCache[symbol] = null;
        }
    }

    public BitmapSource? ResolveBitmap(EditorProject project, string? symbol) =>
        ResolveImage(project, symbol)?.Bitmap;

    public BitmapSource? ResolveThumbnail(EditorProject project, string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;
        if (_thumbnailCache.TryGetValue(symbol, out var cached)) return cached;
        var resource = ResolveImage(project, symbol);
        if (resource is null) return _thumbnailCache[symbol] = null;
        try
        {
            var width = Math.Max(1, (int)Math.Floor(resource.CelWidth));
            var height = Math.Max(1, (int)Math.Floor(resource.CelHeight));
            var crop = new CroppedBitmap(resource.Bitmap,
                new System.Windows.Int32Rect(0, 0,
                    Math.Min(width, resource.Bitmap.PixelWidth), Math.Min(height, resource.Bitmap.PixelHeight)));
            crop.Freeze();
            return _thumbnailCache[symbol] = crop;
        }
        catch
        {
            return _thumbnailCache[symbol] = resource.Bitmap;
        }
    }

    public BitmapSource? ResolveCelBitmap(EditorProject project, string? symbol, float frame)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;
        var resource = ResolveImage(project, symbol);
        if (resource is null) return null;
        var rect = ReanimationRenderMath.GetCelRect(resource, frame);
        if (rect.Width == resource.Bitmap.PixelWidth && rect.Height == resource.Bitmap.PixelHeight)
            return resource.Bitmap;
        var key = $"{symbol}|{rect.X}|{rect.Y}|{rect.Width}|{rect.Height}";
        if (_celCache.TryGetValue(key, out var cached)) return cached;
        try
        {
            var cropped = new CroppedBitmap(resource.Bitmap, rect);
            cropped.Freeze();
            return _celCache[key] = cropped;
        }
        catch
        {
            return _celCache[key] = null;
        }
    }

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
                    if (absolute is not null) AddSymbol(symbol, absolute, true);
                }
            }
        }
        catch
        {
            // A damaged optional manifest must not prevent users from opening a project.
        }
    }

    private void AddSymbol(string symbol, string path, bool isOriginal)
    {
        var normalized = NormalizeSymbol(symbol);
        _symbolIndex.TryAdd(normalized, path);
        if (isOriginal) _originalSymbols.Add(normalized);
    }

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
