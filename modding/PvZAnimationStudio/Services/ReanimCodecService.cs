using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public sealed class ReanimCodecService
{
    private readonly RawReanimCodec _raw = new();
    private readonly CompiledReanimCodec _compiled = new();

    public AnimationDocument Load(string path) => Select(path).Load(path);

    public void Save(AnimationDocument document, string path) => Select(path).Save(document, path);

    public void Save(AnimationDocument document, string path, AnimationOutputFormat format)
    {
        if (format == AnimationOutputFormat.Compiled)
            _compiled.Save(document, EnsureSuffix(path, ".reanim.compiled"));
        else
            _raw.Save(document, EnsureRawSuffix(path));
    }

    public static bool IsCompiledPath(string path) =>
        path.EndsWith(".reanim.compiled", StringComparison.OrdinalIgnoreCase);

    private IReanimCodec Select(string path) => IsCompiledPath(path) ? _compiled : _raw;

    private static string EnsureSuffix(string path, string suffix) =>
        path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? path : path + suffix;

    private static string EnsureRawSuffix(string path) =>
        path.EndsWith(".reanim", StringComparison.OrdinalIgnoreCase) && !IsCompiledPath(path)
            ? path
            : path + ".reanim";
}
