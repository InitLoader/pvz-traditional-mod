using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public sealed class ProjectPackageService
{
    private readonly ReanimCodecService _codec;
    private readonly JsoncArrayEditor _jsoncEditor;

    public ProjectPackageService(ReanimCodecService codec, JsoncArrayEditor jsoncEditor)
    {
        _codec = codec;
        _jsoncEditor = jsoncEditor;
    }

    public void CreatePackage(EditorProject project, string zipPath)
    {
        Validate(project);
        var staging = Path.Combine(Path.GetTempPath(), "PvZAnimationStudio", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            WritePackageTree(project, staging);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(staging, zipPath, CompressionLevel.Optimal, false, Encoding.UTF8);
        }
        finally
        {
            var safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PvZAnimationStudio"));
            var fullStaging = Path.GetFullPath(staging);
            if (fullStaging.StartsWith(safeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(fullStaging))
                Directory.Delete(fullStaging, true);
        }
    }

    public void InstallToGame(EditorProject project, string gameRoot)
    {
        Validate(project);
        gameRoot = Path.GetFullPath(gameRoot);
        if (!File.Exists(Path.Combine(gameRoot, "PlantsVsZombies.exe")))
            throw new InvalidDataException("选择的目录不是 Plants vs. Zombies 游戏根目录。 ");

        var kindDirectory = GetKindDirectory(project.Kind);
        var entityDirectory = SafePathSegment(project.Id).ToLowerInvariant();
        var extension = project.OutputFormat == AnimationOutputFormat.Compiled ? ".reanim.compiled" : ".reanim";
        var relativeAnimation = Path.Combine("pvzmod", "animations", kindDirectory, entityDirectory, project.Id + extension);
        var absoluteAnimation = Path.Combine(gameRoot, relativeAnimation);
        _codec.Save(project.Animation, absoluteAnimation);

        var textureItems = CopyImagesAndCreateTextureItems(project, gameRoot, kindDirectory, entityDirectory);
        var animationItem = CreateAnimationJson(project, relativeAnimation.Replace('\\', '/'));

        var textureConfig = Path.Combine(gameRoot, "pvzmod", "config", "resources", "textures.jsonc");
        foreach (var texture in textureItems)
            _jsoncEditor.Upsert(textureConfig, "textures", "id", texture);
        _jsoncEditor.Upsert(
            Path.Combine(gameRoot, "pvzmod", "config", "resources", "animations.jsonc"),
            "animations", "id", animationItem);

        if (project.Kind == EntityKind.Plant)
        {
            _jsoncEditor.Upsert(
                Path.Combine(gameRoot, "pvzmod", "config", "plants", "custom_plants.jsonc"),
                "plants", "id", CreatePlantJson(project));
        }
        else if (project.Kind == EntityKind.Zombie)
        {
            var generatedPath = Path.Combine(gameRoot, "pvzmod", "config", "zombies", "custom_zombies.generated.jsonc");
            var root = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["note"] = "由 PvZ 动画制作器生成；自定义僵尸运行时模块接入后直接使用。",
                ["zombie"] = CreateZombieJson(project)
            };
            File.WriteAllText(generatedPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }
    }

    private void WritePackageTree(EditorProject project, string staging)
    {
        var kindDirectory = GetKindDirectory(project.Kind);
        var entityDirectory = SafePathSegment(project.Id).ToLowerInvariant();
        var extension = project.OutputFormat == AnimationOutputFormat.Compiled ? ".reanim.compiled" : ".reanim";
        var relativeAnimation = Path.Combine("pvzmod", "animations", kindDirectory, entityDirectory, project.Id + extension);
        _codec.Save(project.Animation, Path.Combine(staging, relativeAnimation));
        var textureItems = CopyImagesAndCreateTextureItems(project, staging, kindDirectory, entityDirectory);

        var generatedRoot = Path.Combine(staging, "pvzmod", "config", "generated", entityDirectory);
        Directory.CreateDirectory(generatedRoot);
        WriteJson(Path.Combine(generatedRoot, "textures.fragment.jsonc"), new JsonObject
        {
            ["schemaVersion"] = 1,
            ["textures"] = new JsonArray(textureItems.Select(item => item.DeepClone()).ToArray())
        });
        WriteJson(Path.Combine(generatedRoot, "animations.fragment.jsonc"), new JsonObject
        {
            ["schemaVersion"] = 1,
            ["animations"] = new JsonArray(CreateAnimationJson(project, relativeAnimation.Replace('\\', '/')))
        });
        WriteJson(Path.Combine(generatedRoot, "entity.fragment.jsonc"), new JsonObject
        {
            ["schemaVersion"] = 1,
            [project.Kind == EntityKind.Plant ? "plant" : "zombie"] =
                project.Kind == EntityKind.Plant ? CreatePlantJson(project) : CreateZombieJson(project)
        });
        File.WriteAllText(Path.Combine(staging, "安装说明.txt"),
            "此包由 PvZ 动画制作器生成。\r\n" +
            "推荐在制作器中选择“安装到游戏”，工具会备份并合并 JSONC。\r\n" +
            "手工安装时复制 pvzmod 目录，并把 generated 下的 fragment 合并到对应配置。\r\n",
            new UTF8Encoding(false));
    }

    private static List<JsonObject> CopyImagesAndCreateTextureItems(
        EditorProject project, string root, string kindDirectory, string entityDirectory)
    {
        var result = new List<JsonObject>();
        foreach (var (symbol, source) in project.ImageBindings.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(source)) throw new FileNotFoundException($"图片不存在：{source}", source);
            var fileName = Path.GetFileName(source);
            var relative = Path.Combine("pvzmod", "images", kindDirectory, entityDirectory, fileName);
            var destination = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
            result.Add(new JsonObject
            {
                ["id"] = SanitizeResourceId(symbol),
                ["path"] = relative.Replace('\\', '/')
            });
        }
        return result;
    }

    private static JsonObject CreateAnimationJson(EditorProject project, string relativeAnimation)
    {
        var images = new JsonObject();
        foreach (var symbol in project.ImageBindings.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            images[symbol] = SanitizeResourceId(symbol);
        var actions = new JsonObject();
        foreach (var action in project.Actions)
        {
            var actionJson = new JsonObject
            {
                ["track"] = action.Track,
                ["loop"] = action.Loop switch
                {
                    AnimationLoopMode.Loop => "loop",
                    AnimationLoopMode.Once => "once",
                    _ => "once_hold"
                },
                ["rate"] = action.Rate,
                ["blendFrames"] = action.BlendFrames
            };
            if (action.Events.Count > 0)
            {
                actionJson["events"] = new JsonArray(action.Events.Select(item => new JsonObject
                {
                    ["id"] = item.Id,
                    ["frame"] = item.Frame,
                    ["normalizedTime"] = item.NormalizedTime,
                    ["oncePerLoop"] = item.OncePerLoop
                }).ToArray());
            }
            actions[action.Id] = actionJson;
        }
        return new JsonObject
        {
            ["id"] = SanitizeResourceId(project.Id),
            ["path"] = relativeAnimation,
            ["carrierReanimation"] = project.CarrierReanimation,
            ["images"] = images,
            ["actions"] = actions
        };
    }

    private static JsonObject CreatePlantJson(EditorProject project) => new()
    {
        ["id"] = project.NumericEntityId,
        ["name"] = project.DisplayName,
        ["description"] = project.Description,
        ["templatePlantId"] = project.TemplateEntityId,
        ["unlocked"] = true,
        ["cost"] = project.Cost,
        ["rechargeTime"] = project.RechargeTime,
        ["health"] = project.Health,
        ["launchRate"] = project.LaunchRate,
        ["initialLaunchDelay"] = new JsonObject { ["min"] = 0, ["max"] = project.LaunchRate },
        ["attack"] = new JsonObject
        {
            ["mode"] = "projectile",
            ["projectileType"] = project.ProjectileType,
            ["damage"] = project.Damage,
            ["shotsPerAttack"] = project.ShotsPerAttack,
            ["damageRangeFlags"] = -1
        },
        ["animationId"] = SanitizeResourceId(project.Id)
    };

    private static JsonObject CreateZombieJson(EditorProject project) => new()
    {
        ["id"] = project.NumericEntityId,
        ["name"] = project.DisplayName,
        ["description"] = project.Description,
        ["templateZombieId"] = project.TemplateEntityId,
        ["health"] = project.Health,
        ["attackDamage"] = project.Damage,
        ["animationId"] = SanitizeResourceId(project.Id)
    };

    private static void WriteJson(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    private static string GetKindDirectory(EntityKind kind) => kind switch
    {
        EntityKind.Plant => "plants",
        EntityKind.Zombie => "zombies",
        EntityKind.Ui => "ui",
        _ => "other"
    };

    private static string SafePathSegment(string value)
    {
        var safe = new string(value.Where(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-').ToArray());
        if (string.IsNullOrWhiteSpace(safe) || safe is "." or "..")
            throw new InvalidDataException("实体 ID 不能作为安全目录名。 ");
        return safe;
    }

    private static string SanitizeResourceId(string value)
    {
        var safe = new string(value.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_').ToArray());
        if (string.IsNullOrWhiteSpace(safe)) throw new InvalidDataException("资源 ID 不能为空。 ");
        return safe;
    }

    private static void Validate(EditorProject project)
    {
        _ = SafePathSegment(project.Id);
        if (project.Kind == EntityKind.Plant && !PlantTemplateCatalog.IsRuntimeTemplate(project.TemplateEntityId))
        {
            var known = PlantTemplateCatalog.Find(project.TemplateEntityId);
            throw new InvalidDataException(known is null
                ? $"未知植物模板 ID {project.TemplateEntityId}；当前支持 0–{PlantTemplateCatalog.LastRuntimeTemplateId}。"
                : $"植物 ID {known.Id}（{known.ChineseName}）是原版模式专用类型，不能作为 templatePlantId；请选择 0–{PlantTemplateCatalog.LastRuntimeTemplateId}。 ");
        }
        if (project.Animation.Tracks.Count == 0) throw new InvalidDataException("动画没有轨道。 ");
        if (project.Actions.Count == 0) throw new InvalidDataException("至少需要定义一个动作。 ");
        foreach (var action in project.Actions)
        {
            if (project.Animation.FindTrack(action.Track) is null)
                throw new InvalidDataException($"动作 {action.Id} 引用了不存在的轨道 {action.Track}。 ");
        }
    }
}
