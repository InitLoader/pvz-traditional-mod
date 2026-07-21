using System.Windows.Media.Imaging;
using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public sealed class OriginalResourceService
{
    private readonly Dictionary<string, string> _symbolIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource?> _bitmapCache = new(StringComparer.OrdinalIgnoreCase);
    private string? _indexedRoot;

    public void RebuildIndex(string? gameRoot)
    {
        _symbolIndex.Clear();
        _bitmapCache.Clear();
        _indexedRoot = null;
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot)) return;
        var fullRoot = Path.GetFullPath(gameRoot);
        foreach (var directory in new[]
                 {
                     Path.Combine(fullRoot, "reanim"),
                     Path.Combine(fullRoot, "images"),
                     Path.Combine(fullRoot, "pvzmod", "images")
                 })
        {
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.png", SearchOption.AllDirectories))
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                _symbolIndex.TryAdd($"IMAGE_REANIM_{NormalizeSymbol(stem)}", file);
                _symbolIndex.TryAdd($"IMAGE_{NormalizeSymbol(stem)}", file);
                _symbolIndex.TryAdd(NormalizeSymbol(stem), file);
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
        _bitmapCache.Remove(candidate);
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
        return _symbolIndex.TryGetValue(symbol, out var original) && File.Exists(original) ? original : null;
    }

    public BitmapSource? ResolveBitmap(EditorProject project, string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;
        if (_bitmapCache.TryGetValue(symbol, out var cached)) return cached;
        var path = ResolvePath(project, symbol);
        if (path is null) return _bitmapCache[symbol] = null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return _bitmapCache[symbol] = image;
        }
        catch
        {
            return _bitmapCache[symbol] = null;
        }
    }

    private static string NormalizeSymbol(string value)
    {
        var chars = value.ToUpperInvariant().Select(character =>
            char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_').ToArray();
        return new string(chars);
    }
}
