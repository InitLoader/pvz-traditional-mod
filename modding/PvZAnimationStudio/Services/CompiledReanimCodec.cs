using System.IO;
using System.IO.Compression;
using System.Text;
using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public sealed class CompiledReanimCodec : IReanimCodec
{
    private const uint OuterMagic = 0xDEADFED4;
    private const uint SchemaHash = 0xB393B4C0;
    private const int DefinitionSize = 16;
    private const int TrackSize = 12;
    private const int TransformSize = 44;
    private const float Missing = -10000f;
    private const uint EmptyStringPointer = 0x00B8AE3C;
    private const int MaxCompressedSize = 16 * 1024 * 1024;
    private const int MaxDecompressedSize = 64 * 1024 * 1024;

    public AnimationDocument Load(string path)
    {
        var file = File.ReadAllBytes(path);
        if (file.Length is < 9 or > MaxCompressedSize)
            throw new InvalidDataException("compiled 文件大小必须为 9 字节到 16 MiB。 ");
        using var outer = new BinaryReader(new MemoryStream(file), Encoding.UTF8, false);
        if (outer.ReadUInt32() != OuterMagic)
            throw new InvalidDataException("compiled Cookie 不是 0xDEADFED4。 ");
        var declaredSize = outer.ReadUInt32();
        if (declaredSize is 0 or > MaxDecompressedSize)
            throw new InvalidDataException("compiled 声明解压大小不安全。 ");

        using var decompressed = new MemoryStream((int)declaredSize);
        using (var zlib = new ZLibStream(outer.BaseStream, CompressionMode.Decompress, true))
            zlib.CopyTo(decompressed);
        if (decompressed.Length != declaredSize)
            throw new InvalidDataException($"compiled 解压长度不匹配：声明 {declaredSize}，实际 {decompressed.Length}。 ");
        if (outer.BaseStream.Position != outer.BaseStream.Length)
            throw new InvalidDataException("compiled zlib 数据后存在尾随字节。 ");

        decompressed.Position = 0;
        using var reader = new BinaryReader(decompressed, Encoding.UTF8, true);
        if (reader.ReadUInt32() != SchemaHash)
            throw new InvalidDataException("不支持的 compiled Schema。 ");
        _ = reader.ReadUInt32();
        var trackCount = reader.ReadInt32();
        var fps = reader.ReadSingle();
        _ = reader.ReadUInt32();
        if (trackCount is < 1 or > 512 || !float.IsFinite(fps) || fps is <= 0 or > 120)
            throw new InvalidDataException("compiled 轨道数量或 FPS 不合法。 ");
        if (reader.ReadInt32() != TrackSize)
            throw new InvalidDataException("compiled ReanimatorTrack 结构不是 12 字节。 ");

        var counts = new int[trackCount];
        var totalTransforms = 0;
        for (var index = 0; index < trackCount; index++)
        {
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            counts[index] = reader.ReadInt32();
            if (counts[index] is < 1 or > 20000)
                throw new InvalidDataException("compiled Transform 数量不合法。 ");
            totalTransforms = checked(totalTransforms + counts[index]);
            if (totalTransforms > 500000)
                throw new InvalidDataException("compiled Transform 总数超过限制。 ");
        }

        var document = new AnimationDocument { Fps = fps };
        var commonFrameCount = counts[0];
        for (var trackIndex = 0; trackIndex < trackCount; trackIndex++)
        {
            var name = ReadString(reader, 128, "轨道名称");
            // 原版少数 Reanimation 会合法地包含同名轨道，不能在编辑器层强制唯一。
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException("compiled 轨道名称不能为空。 ");
            if (reader.ReadInt32() != TransformSize)
                throw new InvalidDataException("compiled ReanimatorTransform 结构不是 44 字节。 ");
            if (counts[trackIndex] != commonFrameCount)
                throw new InvalidDataException("compiled 所有轨道必须具有相同帧数。 ");

            var track = new AnimationTrack { Name = name };
            for (var frameIndex = 0; frameIndex < counts[trackIndex]; frameIndex++)
            {
                var frame = new AnimationFrame
                {
                    X = ReadOptional(reader),
                    Y = ReadOptional(reader),
                    SkewX = ReadOptional(reader),
                    SkewY = ReadOptional(reader),
                    ScaleX = ReadOptional(reader),
                    ScaleY = ReadOptional(reader),
                    Frame = ReadOptional(reader),
                    Alpha = ReadOptional(reader)
                };
                if (frame.Alpha is < 0 or > 1)
                    throw new InvalidDataException($"轨道 {name} 的透明度不合法。 ");
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt32();
                track.Frames.Add(frame);
            }
            foreach (var frame in track.Frames)
            {
                var image = ReadString(reader, 128, "图片符号");
                var font = ReadString(reader, 128, "字体符号");
                var text = ReadString(reader, 1024, "文字");
                frame.Image = image.Length == 0 ? null : image;
                frame.Font = font.Length == 0 ? null : font;
                frame.Text = text.Length == 0 ? null : text;
            }
            document.Tracks.Add(track);
        }
        if (decompressed.Position != decompressed.Length)
            throw new InvalidDataException("compiled 解码后仍有未读取数据。 ");
        return document;
    }

    public void Save(AnimationDocument document, string path)
    {
        RawReanimCodec.ValidateDocument(document);
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, true))
        {
            writer.Write(SchemaHash);
            writer.Write(0u);
            writer.Write(document.Tracks.Count);
            writer.Write(document.Fps);
            writer.Write(0u);
            writer.Write(TrackSize);
            foreach (var track in document.Tracks)
            {
                writer.Write(0u);
                writer.Write(0u);
                writer.Write(track.Frames.Count);
            }
            foreach (var track in document.Tracks)
            {
                WriteString(writer, track.Name);
                writer.Write(TransformSize);
                foreach (var frame in track.Frames)
                {
                    WriteOptional(writer, frame.X);
                    WriteOptional(writer, frame.Y);
                    WriteOptional(writer, frame.SkewX);
                    WriteOptional(writer, frame.SkewY);
                    WriteOptional(writer, frame.ScaleX);
                    WriteOptional(writer, frame.ScaleY);
                    WriteOptional(writer, frame.Frame);
                    WriteOptional(writer, frame.Alpha);
                    writer.Write(0u);
                    writer.Write(0u);
                    writer.Write(EmptyStringPointer);
                }
                foreach (var frame in track.Frames)
                {
                    WriteString(writer, frame.Image ?? string.Empty);
                    WriteString(writer, frame.Font ?? string.Empty);
                    WriteString(writer, frame.Text ?? string.Empty);
                }
            }
        }
        if (payload.Length > MaxDecompressedSize)
            throw new InvalidDataException("动画解压后超过 64 MiB，不能导出 compiled。 ");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var output = File.Create(path);
        using var outer = new BinaryWriter(output, Encoding.UTF8, true);
        outer.Write(OuterMagic);
        outer.Write(checked((uint)payload.Length));
        payload.Position = 0;
        using var zlib = new ZLibStream(output, CompressionLevel.Optimal, true);
        payload.CopyTo(zlib);
    }

    private static float? ReadOptional(BinaryReader reader)
    {
        var value = reader.ReadSingle();
        if (!float.IsFinite(value))
            throw new InvalidDataException("compiled Transform 含非有限浮点数。 ");
        return value <= Missing ? null : value;
    }

    private static void WriteOptional(BinaryWriter writer, float? value) => writer.Write(value ?? Missing);

    private static string ReadString(BinaryReader reader, int maximum, string field)
    {
        var length = reader.ReadInt32();
        if (length is < 0 || length > maximum)
            throw new InvalidDataException($"{field}长度超过限制。 ");
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException($"读取{field}时文件结束。 ");
        if (Array.IndexOf(bytes, (byte)0) >= 0)
            throw new InvalidDataException($"{field}包含空字节。 ");
        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
