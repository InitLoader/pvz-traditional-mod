using System.Text.Json;
using System.Text.Json.Serialization;
using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public sealed class ProjectFileService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public EditorProject Load(string path)
    {
        var project = JsonSerializer.Deserialize<EditorProject>(File.ReadAllText(path), Options)
                      ?? throw new InvalidDataException("工程文件内容为空。 ");
        project.ProjectPath = Path.GetFullPath(path);
        project.ImageBindings = new Dictionary<string, string>(project.ImageBindings, StringComparer.OrdinalIgnoreCase);
        return project;
    }

    public void Save(EditorProject project, string path)
    {
        project.ProjectPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(project.ProjectPath)!);
        File.WriteAllText(project.ProjectPath, JsonSerializer.Serialize(project, Options));
    }
}
