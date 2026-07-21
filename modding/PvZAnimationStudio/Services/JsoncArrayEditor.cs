using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PvZAnimationStudio.Services;

public sealed class JsoncArrayEditor
{
    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public void Upsert(string path, string arrayProperty, string idProperty, JsonObject item)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
            File.WriteAllText(path, $"{{\n  \"schemaVersion\": 1,\n  \"{arrayProperty}\": []\n}}\n", new UTF8Encoding(false));
        var text = File.ReadAllText(path);
        var (start, end) = FindArray(text, arrayProperty);
        var spans = FindTopLevelObjects(text, start + 1, end);
        var wantedId = item[idProperty]?.ToJsonString() ?? throw new InvalidDataException($"生成项缺少 {idProperty}。 ");
        foreach (var span in spans)
        {
            var existingText = text[span.Start..span.End];
            JsonObject? existing;
            try
            {
                existing = JsonNode.Parse(existingText, documentOptions: ReadOptions) as JsonObject;
            }
            catch
            {
                continue;
            }
            if (existing?[idProperty]?.ToJsonString() != wantedId) continue;
            Backup(path);
            var replacement = Indent(item.ToJsonString(WriteOptions), GetLineIndent(text, span.Start));
            File.WriteAllText(path, text[..span.Start] + replacement + text[span.End..], new UTF8Encoding(false));
            return;
        }

        Backup(path);
        var arrayBody = text[(start + 1)..end];
        var needsComma = spans.Count > 0;
        var insertion = $"{(needsComma ? "," : string.Empty)}\n{Indent(item.ToJsonString(WriteOptions), "    ")}\n  ";
        File.WriteAllText(path, text[..end] + insertion + text[end..], new UTF8Encoding(false));
    }

    private static (int Start, int End) FindArray(string text, string property)
    {
        var index = 0;
        while (index < text.Length)
        {
            SkipTrivia(text, ref index);
            if (index >= text.Length) break;
            if (text[index] != '"') { index++; continue; }
            var tokenStart = index;
            var token = ReadStringToken(text, ref index);
            if (!string.Equals(token, property, StringComparison.Ordinal)) continue;
            SkipTrivia(text, ref index);
            if (index >= text.Length || text[index++] != ':') continue;
            SkipTrivia(text, ref index);
            if (index >= text.Length || text[index] != '[') continue;
            var start = index;
            var end = FindMatching(text, start, '[', ']');
            return (start, end);
        }
        throw new InvalidDataException($"JSONC 中找不到数组 {property}。 ");
    }

    private static List<(int Start, int End)> FindTopLevelObjects(string text, int start, int end)
    {
        var result = new List<(int Start, int End)>();
        var index = start;
        while (index < end)
        {
            SkipTrivia(text, ref index);
            if (index >= end) break;
            if (text[index] == ',') { index++; continue; }
            if (text[index] != '{') { index++; continue; }
            var objectEnd = FindMatching(text, index, '{', '}') + 1;
            result.Add((index, objectEnd));
            index = objectEnd;
        }
        return result;
    }

    private static int FindMatching(string text, int start, char open, char close)
    {
        var depth = 0;
        var index = start;
        while (index < text.Length)
        {
            if (text[index] == '"') { _ = ReadStringToken(text, ref index); continue; }
            if (text[index] == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                index += 2;
                while (index < text.Length && text[index] != '\n') index++;
                continue;
            }
            if (text[index] == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                index += 2;
                while (index + 1 < text.Length && !(text[index] == '*' && text[index + 1] == '/')) index++;
                index = Math.Min(text.Length, index + 2);
                continue;
            }
            if (text[index] == open) depth++;
            else if (text[index] == close && --depth == 0) return index;
            index++;
        }
        throw new InvalidDataException("JSONC 括号没有闭合。 ");
    }

    private static string ReadStringToken(string text, ref int index)
    {
        if (text[index] != '"') throw new InvalidDataException("JSONC 字符串起始错误。 ");
        var builder = new StringBuilder();
        index++;
        while (index < text.Length)
        {
            var character = text[index++];
            if (character == '"') return builder.ToString();
            if (character == '\\' && index < text.Length)
            {
                builder.Append(character);
                builder.Append(text[index++]);
            }
            else builder.Append(character);
        }
        throw new InvalidDataException("JSONC 字符串没有闭合。 ");
    }

    private static void SkipTrivia(string text, ref int index)
    {
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index])) { index++; continue; }
            if (text[index] == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                index += 2;
                while (index < text.Length && text[index] != '\n') index++;
                continue;
            }
            if (text[index] == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                index += 2;
                while (index + 1 < text.Length && !(text[index] == '*' && text[index + 1] == '/')) index++;
                index = Math.Min(text.Length, index + 2);
                continue;
            }
            break;
        }
    }

    private static string GetLineIndent(string text, int position)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, position - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var index = lineStart;
        while (index < position && text[index] is ' ' or '\t') index++;
        return text[lineStart..index];
    }

    private static string Indent(string text, string indentation) =>
        string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(line => indentation + line));

    private static void Backup(string path)
    {
        var backup = path + ".pvzstudio.bak";
        if (!File.Exists(backup)) File.Copy(path, backup);
    }
}
