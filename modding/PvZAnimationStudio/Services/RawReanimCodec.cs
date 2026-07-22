using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public sealed class RawReanimCodec : IReanimCodec
{
    private const int MaxTracks = 512;
    private const int MaxFrames = 20000;

    public AnimationDocument Load(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        if (text.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Raw Reanimation 不允许 DTD 或 XML 实体。 ");

        var wrapped = $"<pvzstudio_root>{StripXmlDeclaration(text)}</pvzstudio_root>";
        var root = XDocument.Parse(wrapped, LoadOptions.PreserveWhitespace).Root
                   ?? throw new InvalidDataException("Raw Reanimation 缺少根内容。 ");
        var document = new AnimationDocument
        {
            Fps = ParseOptionalFloat(root.Element("fps")) ?? 12f,
            DoScale = ParseOptionalInt(root.Element("doScale"))
        };
        foreach (var trackElement in root.Elements("track"))
        {
            if (document.Tracks.Count >= MaxTracks)
                throw new InvalidDataException($"轨道数量不能超过 {MaxTracks}。 ");
            var name = trackElement.Element("name")?.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
                throw new InvalidDataException("轨道名称必须为 1–128 个字符。 ");
            var track = new AnimationTrack { Name = name };
            foreach (var transform in trackElement.Elements("t"))
            {
                if (track.Frames.Count >= MaxFrames)
                    throw new InvalidDataException($"轨道 {name} 超过 {MaxFrames} 帧。 ");
                EnsureKnownFields(transform);
                var frame = new AnimationFrame
                {
                    X = ParseOptionalFloat(transform.Element("x")),
                    Y = ParseOptionalFloat(transform.Element("y")),
                    SkewX = ParseOptionalFloat(transform.Element("kx")),
                    SkewY = ParseOptionalFloat(transform.Element("ky")),
                    ScaleX = ParseOptionalFloat(transform.Element("sx")),
                    ScaleY = ParseOptionalFloat(transform.Element("sy")),
                    Frame = ParseOptionalFloat(transform.Element("f")),
                    Alpha = ParseOptionalFloat(transform.Element("a")),
                    Image = ParseOptionalString(transform.Element("i")),
                    Font = ParseOptionalString(transform.Element("font")),
                    Text = ParseOptionalString(transform.Element("text"))
                };
                ValidateFrame(frame, name, track.Frames.Count);
                track.Frames.Add(frame);
            }
            if (track.Frames.Count == 0)
                throw new InvalidDataException($"轨道 {name} 没有任何帧。 ");
            document.Tracks.Add(track);
        }
        if (document.Tracks.Count == 0)
            throw new InvalidDataException("动画至少需要一条轨道。 ");
        ValidateUniformFrames(document);
        return document;
    }

    public void Save(AnimationDocument document, string path)
    {
        ValidateDocument(document);
        var root = new XElement("pvzstudio_root");
        if (document.DoScale.HasValue)
            root.Add(new XElement("doScale", document.DoScale.Value));
        root.Add(new XElement("fps", FormatFloat(document.Fps)));
        foreach (var track in document.Tracks)
        {
            var trackElement = new XElement("track", new XElement("name", track.Name));
            foreach (var frame in track.Frames)
            {
                var transform = new XElement("t");
                AddFloat(transform, "x", frame.X);
                AddFloat(transform, "y", frame.Y);
                AddFloat(transform, "kx", frame.SkewX);
                AddFloat(transform, "ky", frame.SkewY);
                AddFloat(transform, "sx", frame.ScaleX);
                AddFloat(transform, "sy", frame.ScaleY);
                AddFloat(transform, "f", frame.Frame);
                AddFloat(transform, "a", frame.Alpha);
                AddString(transform, "i", frame.Image);
                AddString(transform, "font", frame.Font);
                AddString(transform, "text", frame.Text);
                trackElement.Add(transform);
            }
            root.Add(trackElement);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            Encoding = new UTF8Encoding(false),
            ConformanceLevel = ConformanceLevel.Fragment
        };
        using var stream = File.Create(path);
        using var writer = XmlWriter.Create(stream, settings);
        foreach (var element in root.Elements())
            element.WriteTo(writer);
    }

    internal static void ValidateDocument(AnimationDocument document)
    {
        if (!float.IsFinite(document.Fps) || document.Fps <= 0 || document.Fps > 120)
            throw new InvalidDataException("FPS 必须在 0–120 之间。 ");
        if (document.Tracks.Count is < 1 or > MaxTracks)
            throw new InvalidDataException($"轨道数量必须在 1–{MaxTracks} 之间。 ");
        ValidateUniformFrames(document);
        foreach (var track in document.Tracks)
        {
            if (string.IsNullOrWhiteSpace(track.Name) || track.Name.Length > 128)
                throw new InvalidDataException($"轨道名称无效：{track.Name}");
            if (track.Frames.Count is < 1 or > MaxFrames)
                throw new InvalidDataException($"轨道 {track.Name} 的帧数不合法。 ");
            for (var index = 0; index < track.Frames.Count; index++)
                ValidateFrame(track.Frames[index], track.Name, index);
        }
    }

    private static void ValidateUniformFrames(AnimationDocument document)
    {
        var frameCount = document.Tracks.FirstOrDefault()?.Frames.Count ?? 0;
        if (document.Tracks.Any(track => track.Frames.Count != frameCount))
            throw new InvalidDataException("所有轨道必须具有相同帧数。 ");
    }

    private static void ValidateFrame(AnimationFrame frame, string track, int index)
    {
        foreach (var value in new[] { frame.X, frame.Y, frame.SkewX, frame.SkewY, frame.ScaleX, frame.ScaleY, frame.Frame, frame.Alpha })
        {
            if (value.HasValue && (!float.IsFinite(value.Value) || value.Value is < -100000 or > 100000))
                throw new InvalidDataException($"轨道 {track} 第 {index} 帧含非法数值。 ");
        }
        if (frame.Alpha is < 0 or > 1)
            throw new InvalidDataException($"轨道 {track} 第 {index} 帧透明度必须在 0–1。 ");
        if ((frame.Image?.Length ?? 0) > 128 || (frame.Font?.Length ?? 0) > 128 || (frame.Text?.Length ?? 0) > 1024)
            throw new InvalidDataException($"轨道 {track} 第 {index} 帧字符串过长。 ");
    }

    private static void EnsureKnownFields(XElement transform)
    {
        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "x", "y", "kx", "ky", "sx", "sy", "f", "a", "i", "font", "text"
        };
        foreach (var child in transform.Elements())
        {
            if (!known.Contains(child.Name.LocalName))
                throw new InvalidDataException($"不支持的 Transform 字段：{child.Name.LocalName}");
        }
    }

    private static float? ParseOptionalFloat(XElement? element)
    {
        if (element is null) return null;
        if (!float.TryParse(element.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
            throw new InvalidDataException($"{element.Name.LocalName} 不是有效浮点数。 ");
        return value;
    }

    private static int? ParseOptionalInt(XElement? element)
    {
        if (element is null) return null;
        if (!int.TryParse(element.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException($"{element.Name.LocalName} 不是有效整数。 ");
        return value;
    }

    private static string? ParseOptionalString(XElement? element) => element is null ? null : element.Value;

    private static void AddFloat(XElement parent, string name, float? value)
    {
        if (value.HasValue) parent.Add(new XElement(name, FormatFloat(value.Value)));
    }

    private static void AddString(XElement parent, string name, string? value)
    {
        if (value is not null) parent.Add(new XElement(name, value));
    }

    private static string FormatFloat(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string StripXmlDeclaration(string text)
    {
        var trimmed = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (!trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)) return trimmed;
        var end = trimmed.IndexOf("?>", StringComparison.Ordinal);
        return end < 0 ? trimmed : trimmed[(end + 2)..];
    }
}
