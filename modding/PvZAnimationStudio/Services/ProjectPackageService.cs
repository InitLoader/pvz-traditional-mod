using System.IO.Compression;
using System.Security.Cryptography;
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
        gameRoot = Path.GetFullPath(gameRoot);
        if (!File.Exists(Path.Combine(gameRoot, "PlantsVsZombies.exe")))
            throw new InvalidDataException("选择的目录不是 Plants vs. Zombies 游戏根目录。 ");
        ValidateForPublish(project, gameRoot);

        var kindDirectory = GetKindDirectory(project.Kind);
        var entityDirectory = SafePathSegment(project.Id).ToLowerInvariant();
        var extension = project.OutputFormat == AnimationOutputFormat.Compiled ? ".reanim.compiled" : ".reanim";
        var relativeAnimation = Path.Combine("pvzmod", "animations", kindDirectory, entityDirectory, project.Id + extension);
        var absoluteAnimation = Path.Combine(gameRoot, relativeAnimation);
        var textureConfig = Path.Combine(gameRoot, "pvzmod", "config", "resources", "textures.jsonc");
        var animationConfig = Path.Combine(gameRoot, "pvzmod", "config", "resources", "animations.jsonc");
        var entityConfig = project.Kind == EntityKind.Plant
            ? Path.Combine(gameRoot, "pvzmod", "config", "plants", "custom_plants.jsonc")
            : Path.Combine(gameRoot, "pvzmod", "config", "zombies", "attributes.jsonc");
        var generatedPath = Path.Combine(gameRoot, "pvzmod", "config", "zombies", "custom_zombies.generated.jsonc");
        var transactionTargets = new List<string> { absoluteAnimation, textureConfig, animationConfig, entityConfig };
        if (project.Kind == EntityKind.Zombie) transactionTargets.Add(generatedPath);
        transactionTargets.AddRange(project.ImageBindings.Values.Select(source =>
            Path.Combine(gameRoot, "pvzmod", "images", kindDirectory, entityDirectory, Path.GetFileName(source))));
        var transaction = new InstallFileTransaction(transactionTargets);
        try
        {
            _codec.Save(project.Animation, absoluteAnimation);
            var textureItems = CopyImagesAndCreateTextureItems(project, gameRoot, kindDirectory, entityDirectory);
            var animationItem = CreateAnimationJson(project, relativeAnimation.Replace('\\', '/'));
            foreach (var texture in textureItems)
                _jsoncEditor.Upsert(textureConfig, "textures", "id", texture);
            _jsoncEditor.Upsert(animationConfig, "animations", "id", animationItem);

            if (project.Kind == EntityKind.Plant)
            {
                _jsoncEditor.Upsert(entityConfig, "plants", "id", CreatePlantJson(project));
            }
            else
            {
                _jsoncEditor.UpsertObjectProperty(
                    entityConfig,
                    "zombies",
                    project.TemplateEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    CreateZombieRuntimeOverrideJson(project));
                var root = new JsonObject
                {
                    ["schemaVersion"] = 1,
                    ["note"] = "由 PvZ 动画制作器生成；自定义僵尸运行时模块接入后直接使用。",
                    ["zombie"] = CreateZombieJson(project)
                };
                File.WriteAllText(generatedPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            }
            ValidateInstalledResourceConfigs(textureConfig, animationConfig);
            transaction.Commit();
        }
        catch (Exception exception)
        {
            transaction.Rollback();
            throw new InvalidDataException($"安装失败，已恢复安装前的文件：{exception.Message}", exception);
        }
    }

    public void ValidateForPublish(EditorProject project, string? gameRoot = null)
    {
        Validate(project);
        if (!string.IsNullOrWhiteSpace(gameRoot)) ValidateInstallCollisions(project, Path.GetFullPath(gameRoot));
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
        WriteJson(Path.Combine(generatedRoot, "entity.fragment.jsonc"), CreateEntityFragment(project));
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
                ["id"] = NormalizeResourceId(symbol),
                ["path"] = relative.Replace('\\', '/')
            });
        }
        return result;
    }

    private static JsonObject CreateAnimationJson(EditorProject project, string relativeAnimation)
    {
        var images = new JsonObject();
        foreach (var symbol in project.ImageBindings.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            images[symbol] = NormalizeResourceId(symbol);
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
            if (action.Replaces.Count > 0)
                actionJson["replaces"] = new JsonArray(
                    action.Replaces.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
            if (action.Events.Count > 0)
            {
                actionJson["events"] = new JsonArray(action.Events.Select(item =>
                {
                    var eventJson = new JsonObject
                    {
                        ["id"] = item.Id,
                        ["oncePerLoop"] = item.OncePerLoop
                    };
                    if (item.Frame.HasValue) eventJson["frame"] = item.Frame.Value;
                    else eventJson["normalizedTime"] = item.NormalizedTime;
                    if (!string.IsNullOrWhiteSpace(item.TargetAction)) eventJson["action"] = item.TargetAction;
                    return eventJson;
                }).ToArray());
            }
            actions[action.Id] = actionJson;
        }
        return new JsonObject
        {
            ["id"] = NormalizeResourceId(project.Id),
            ["path"] = relativeAnimation,
            ["carrierReanimation"] = ResolveCarrierReanimation(project),
            ["initialAction"] = project.InitialActionId,
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
        ["hideTemplateAttachments"] = project.HideTemplateAttachments,
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
        ["animationId"] = NormalizeResourceId(project.Id)
    };

    private static JsonObject CreateZombieJson(EditorProject project) => new()
    {
        ["id"] = project.NumericEntityId,
        ["name"] = project.DisplayName,
        ["description"] = project.Description,
        ["templateZombieId"] = project.TemplateEntityId,
        ["health"] = project.Health,
        ["attackDamage"] = project.Damage,
        ["animationId"] = NormalizeResourceId(project.Id)
    };

    private static JsonObject CreateZombieRuntimeOverrideJson(EditorProject project) => new()
    {
        ["bodyHealth"] = project.Health,
        ["attackDamage"] = project.Damage,
        ["animationId"] = NormalizeResourceId(project.Id)
    };

    private static JsonObject CreateEntityFragment(EditorProject project)
    {
        if (project.Kind == EntityKind.Plant)
        {
            return new JsonObject
            {
                ["schemaVersion"] = 1,
                ["plant"] = CreatePlantJson(project)
            };
        }
        var templateId = project.TemplateEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["zombie"] = CreateZombieJson(project),
            ["zombies"] = new JsonObject
            {
                [templateId] = CreateZombieRuntimeOverrideJson(project)
            }
        };
    }

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

    private static string ResolveCarrierReanimation(EditorProject project) => project.Kind switch
    {
        EntityKind.Plant => PlantTemplateCatalog.Find(project.TemplateEntityId)?.CarrierReanimation
                            ?? project.CarrierReanimation,
        EntityKind.Zombie => ZombieTemplateCatalog.Find(project.TemplateEntityId)?.CarrierReanimation
                             ?? project.CarrierReanimation,
        _ => project.CarrierReanimation
    };

    private static string SafePathSegment(string value)
    {
        var safe = new string(value.Where(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-').ToArray());
        if (string.IsNullOrWhiteSpace(safe) || safe is "." or "..")
            throw new InvalidDataException("实体 ID 不能作为安全目录名。 ");
        return safe;
    }

    public static string NormalizeResourceId(string value)
    {
        var safe = new string(value.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_').ToArray());
        if (string.IsNullOrWhiteSpace(safe)) throw new InvalidDataException("资源 ID 不能为空。 ");
        if (safe == value && safe.Length <= 64) return safe;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..10];
        var prefixLength = 64 - hash.Length - 1;
        if (safe.Length > prefixLength) safe = safe[..prefixLength];
        return $"{safe}_{hash}";
    }

    private static void ValidateInstallCollisions(EditorProject project, string gameRoot)
    {
        var expectedCarrier = ResolveCarrierReanimation(project);
        var animationConfig = ReadJsoncObject(
            Path.Combine(gameRoot, "pvzmod", "config", "resources", "animations.jsonc"));
        if (animationConfig?["animations"] is JsonArray animations)
        {
            foreach (var existing in animations.OfType<JsonObject>())
            {
                if (!string.Equals(existing["id"]?.GetValue<string>(), project.Id,
                        StringComparison.OrdinalIgnoreCase)) continue;
                var existingCarrier = existing["carrierReanimation"]?.GetValue<string>() ?? string.Empty;
                if (!string.Equals(existingCarrier, expectedCarrier, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"动画 ID {project.Id} 已被载体 {existingCarrier} 使用；当前{KindName(project.Kind)}需要 {expectedCarrier}。" +
                        "请在确认窗口改用新的字符串 ID，禁止跨植物/僵尸覆盖同名动画。");
            }
        }

        if (project.Kind == EntityKind.Zombie)
        {
            var plants = ReadJsoncObject(
                Path.Combine(gameRoot, "pvzmod", "config", "plants", "custom_plants.jsonc"));
            if (plants?["plants"] is JsonArray plantArray && plantArray.OfType<JsonObject>().Any(item =>
                    string.Equals(item["animationId"]?.GetValue<string>(), project.Id,
                        StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(
                    $"字符串 ID {project.Id} 已由自定义植物使用；请给僵尸设置独立 ID，不能覆盖植物存档映射。");
        }
        else if (project.Kind == EntityKind.Plant)
        {
            var zombieConfig = ReadJsoncObject(
                Path.Combine(gameRoot, "pvzmod", "config", "zombies", "attributes.jsonc"));
            if (zombieConfig?["zombies"] is JsonObject zombies && zombies.Any(pair =>
                    pair.Value is JsonObject item &&
                    string.Equals(item["animationId"]?.GetValue<string>(), project.Id,
                        StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(
                    $"字符串 ID {project.Id} 已由自定义僵尸使用；请给植物设置独立 ID，不能覆盖僵尸存档映射。");
        }
    }

    private static void ValidateInstalledResourceConfigs(string textureConfigPath, string animationConfigPath)
    {
        var textures = ReadJsoncObject(textureConfigPath)?["textures"] as JsonArray
                       ?? throw new InvalidDataException("textures.jsonc 缺少 textures 数组。");
        var textureIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var texture in textures.OfType<JsonObject>())
        {
            var id = texture["id"]?.GetValue<string>() ?? string.Empty;
            if (!IsResourceId(id)) throw new InvalidDataException($"贴图 ID {id} 不符合 1–64 位资源 ID 规则。");
            if (!textureIds.Add(id)) throw new InvalidDataException($"贴图 ID {id} 重复。");
        }

        var animations = ReadJsoncObject(animationConfigPath)?["animations"] as JsonArray
                         ?? throw new InvalidDataException("animations.jsonc 缺少 animations 数组。");
        var animationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var animation in animations.OfType<JsonObject>())
        {
            var id = animation["id"]?.GetValue<string>() ?? string.Empty;
            if (!IsResourceId(id)) throw new InvalidDataException($"动画 ID {id} 不符合 1–64 位资源 ID 规则。");
            if (!animationIds.Add(id)) throw new InvalidDataException($"动画 ID {id} 重复。");
            if (animation["images"] is not JsonObject images) continue;
            foreach (var image in images)
            {
                var textureId = image.Value?.GetValue<string>() ?? string.Empty;
                if (!IsResourceId(textureId))
                    throw new InvalidDataException($"动画 {id} 的贴图映射 {image.Key} 使用了非法 ID {textureId}。");
            }
        }
    }

    private static JsonObject? ReadJsoncObject(string path)
    {
        if (!File.Exists(path)) return null;
        return JsonNode.Parse(
            File.ReadAllText(path, Encoding.UTF8),
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            }) as JsonObject;
    }

    private static bool IsResourceId(string value) =>
        value.Length is >= 1 and <= 64 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character == '_');

    private static string KindName(EntityKind kind) => kind switch
    {
        EntityKind.Plant => "植物",
        EntityKind.Zombie => "僵尸",
        EntityKind.Ui => "UI",
        _ => "其他实体"
    };

    private static void Validate(EditorProject project)
    {
        _ = SafePathSegment(project.Id);
        if (NormalizeResourceId(project.Id) != project.Id)
            throw new InvalidDataException("字符串 ID 必须为 1–64 位，只能包含英文字母、数字和下划线。");
        if (string.IsNullOrWhiteSpace(project.DisplayName))
            throw new InvalidDataException("中文名称不能为空。");
        if (PublishConfirmationService.LooksLikeZombieBody(project) && project.Kind != EntityKind.Zombie)
            throw new InvalidDataException("检测到僵尸主体轨道，实体类型必须选择“僵尸”。");
        if (project.Kind == EntityKind.Zombie &&
            (project.Id.Contains("PLANT", StringComparison.OrdinalIgnoreCase) ||
             project.DisplayName.Contains("植物", StringComparison.Ordinal)))
            throw new InvalidDataException("僵尸工程的字符串 ID 或名称仍包含“PLANT/植物”，请在确认窗口修正。");
        if (project.Kind == EntityKind.Plant &&
            (project.Id.Contains("ZOMBIE", StringComparison.OrdinalIgnoreCase) ||
             project.DisplayName.Contains("僵尸", StringComparison.Ordinal)))
            throw new InvalidDataException("植物工程的字符串 ID 或名称仍包含“ZOMBIE/僵尸”，请在确认窗口修正。");
        if (project.Kind == EntityKind.Plant && !PlantTemplateCatalog.IsRuntimeTemplate(project.TemplateEntityId))
        {
            var known = PlantTemplateCatalog.Find(project.TemplateEntityId);
            throw new InvalidDataException(known is null
                ? $"未知植物模板 ID {project.TemplateEntityId}；当前支持 0–{PlantTemplateCatalog.LastRuntimeTemplateId}。"
                : $"植物 ID {known.Id}（{known.ChineseName}）是原版模式专用类型，不能作为 templatePlantId；请选择 0–{PlantTemplateCatalog.LastRuntimeTemplateId}。 ");
        }
        if (project.Kind == EntityKind.Zombie && ZombieTemplateCatalog.Find(project.TemplateEntityId) is null)
            throw new InvalidDataException(
                $"未知僵尸模板 ID {project.TemplateEntityId}；当前支持 {ZombieTemplateCatalog.FirstZombieId}–{ZombieTemplateCatalog.LastZombieId}。");
        if (project.Animation.Tracks.Count == 0) throw new InvalidDataException("动画没有轨道。 ");
        if (project.Actions.Count == 0) throw new InvalidDataException("至少需要定义一个动作。 ");
        if (!project.Actions.Any(action => string.Equals(action.Id, project.InitialActionId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"初始动作 {project.InitialActionId} 不存在。 ");
        var claimedTracks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in project.Actions)
        {
            if (project.Animation.FindTrack(action.Track) is null)
                throw new InvalidDataException($"动作 {action.Id} 引用了不存在的轨道 {action.Track}。 ");
            foreach (var track in action.Replaces.Prepend(action.Track))
            {
                if (string.IsNullOrWhiteSpace(track) || !claimedTracks.Add(track))
                    throw new InvalidDataException($"原版动作轨道 {track} 被重复映射或为空。 ");
            }
            foreach (var animationEvent in action.Events)
            {
                if (animationEvent.Frame.HasValue == animationEvent.NormalizedTime.HasValue)
                    throw new InvalidDataException($"动作 {action.Id} 的事件 {animationEvent.Id} 必须只设置帧或归一化时间之一。 ");
                if (animationEvent.Id == "PLAY_ACTION" &&
                    !project.Actions.Any(candidate => string.Equals(candidate.Id, animationEvent.TargetAction, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException($"PLAY_ACTION 事件引用了不存在的动作 {animationEvent.TargetAction}。 ");
            }
        }
    }

    private sealed class InstallFileTransaction
    {
        private sealed record Snapshot(string Path, bool Existed, byte[]? Content);

        private readonly IReadOnlyList<Snapshot> _snapshots;
        private bool _committed;

        public InstallFileTransaction(IEnumerable<string> paths)
        {
            _snapshots = paths
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => new Snapshot(path, File.Exists(path), File.Exists(path) ? File.ReadAllBytes(path) : null))
                .ToArray();
        }

        public void Commit() => _committed = true;

        public void Rollback()
        {
            if (_committed) return;
            foreach (var snapshot in _snapshots.Reverse())
            {
                try
                {
                    if (snapshot.Existed)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(snapshot.Path)!);
                        File.WriteAllBytes(snapshot.Path, snapshot.Content!);
                    }
                    else if (File.Exists(snapshot.Path))
                    {
                        File.Delete(snapshot.Path);
                    }
                }
                catch
                {
                    // Preserve the original install failure. Files that can be restored are still rolled back.
                }
            }
        }
    }
}
