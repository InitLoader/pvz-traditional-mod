using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public sealed class ReanimCodecService
{
    private readonly RawReanimCodec _raw = new();
    private readonly CompiledReanimCodec _compiled = new();

    public AnimationDocument Load(string path) => SelectForLoad(path).Load(path);

    public string Save(AnimationDocument document, string path)
    {
        Select(path).Save(document, path);
        return path;
    }

    public string Save(AnimationDocument document, string path, AnimationOutputFormat format)
    {
        string outputPath;
        if (format == AnimationOutputFormat.Compiled)
        {
            outputPath = path.EndsWith(".compiled", StringComparison.OrdinalIgnoreCase)
                ? path
                : EnsureSuffix(path, ".reanim.compiled");
            _compiled.Save(document, outputPath);
        }
        else
        {
            outputPath = EnsureRawSuffix(path);
            _raw.Save(document, outputPath);
        }
        return outputPath;
    }

    public static bool IsCompiledPath(string path) =>
        path.EndsWith(".reanim.compiled", StringComparison.OrdinalIgnoreCase);

    private IReanimCodec Select(string path) => IsCompiledPath(path) ? _compiled : _raw;

    private IReanimCodec SelectForLoad(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> cookie = stackalloc byte[4];
        var read = stream.Read(cookie);
        return read == 4 && BitConverter.ToUInt32(cookie) == 0xDEADFED4 ? _compiled : Select(path);
    }

    private static string EnsureSuffix(string path, string suffix) =>
        path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? path : path + suffix;

    private static string EnsureRawSuffix(string path) =>
        path.EndsWith(".reanim", StringComparison.OrdinalIgnoreCase) && !IsCompiledPath(path)
            ? path
            : path + ".reanim";
}
