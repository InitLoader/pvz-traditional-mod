using System.Text.RegularExpressions;
using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public enum PublishOperation
{
    ExportRaw,
    ExportCompiled,
    Package,
    Install
}

public sealed record PublishProjectDraft(
    EntityKind Kind,
    EntityIntegrationMode IntegrationMode,
    string Id,
    string DisplayName,
    string Description,
    int NumericEntityId,
    int TemplateEntityId,
    string InitialActionId,
    int Health,
    int Damage,
    int Cost,
    int RechargeTime,
    int LaunchRate,
    int ProjectileType,
    int ShotsPerAttack)
{
    public static PublishProjectDraft FromProject(EditorProject project) => new(
        project.Kind,
        project.IntegrationMode,
        project.Id,
        project.DisplayName,
        project.Description,
        project.NumericEntityId,
        project.TemplateEntityId,
        project.InitialActionId,
        project.Health,
        project.Damage,
        project.Cost,
        project.RechargeTime,
        project.LaunchRate,
        project.ProjectileType,
        project.ShotsPerAttack);
}

public sealed class PublishConfirmationService
{
    private static readonly Regex ResourceIdPattern = new("^[A-Za-z0-9_]{1,64}$", RegexOptions.CultureInvariant);

    public IReadOnlyList<string> Validate(
        PublishProjectDraft draft,
        EditorProject sourceProject,
        PublishOperation operation)
    {
        var errors = new List<string>();
        if (!ResourceIdPattern.IsMatch(draft.Id))
            errors.Add("字符串 ID 必须为 1–64 位，只能包含英文字母、数字和下划线。");
        if (string.IsNullOrWhiteSpace(draft.DisplayName))
            errors.Add("中文名称不能为空。");
        else if (draft.DisplayName.Trim().Length > 64)
            errors.Add("中文名称不能超过 64 个字符。");
        if (!sourceProject.Actions.Any(action =>
                string.Equals(action.Id, draft.InitialActionId, StringComparison.OrdinalIgnoreCase)))
            errors.Add($"初始动作 {draft.InitialActionId} 不存在。");
        var addsEntity = draft.IntegrationMode == EntityIntegrationMode.AddEntity;
        if (addsEntity && draft.NumericEntityId < 0)
            errors.Add("数字 ID 不能为负数。");
        if (addsEntity && draft.Health is < 1 or > 1_000_000)
            errors.Add("生命必须在 1–1000000 之间。");
        if (addsEntity && draft.Damage is < 0 or > 1_000_000)
            errors.Add("伤害必须在 0–1000000 之间。");

        if (draft.Kind == EntityKind.Plant)
        {
            if (!PlantTemplateCatalog.IsRuntimeTemplate(draft.TemplateEntityId))
                errors.Add($"植物模板 ID 必须在 0–{PlantTemplateCatalog.LastRuntimeTemplateId} 之间。");
            if (addsEntity)
            {
                if (draft.Cost is < 0 or > 100_000) errors.Add("阳光必须在 0–100000 之间。");
                if (draft.RechargeTime is < 0 or > 1_000_000) errors.Add("冷却必须在 0–1000000 之间。");
                if (draft.LaunchRate is < 1 or > 1_000_000) errors.Add("攻击间隔必须在 1–1000000 之间。");
                if (draft.ShotsPerAttack is < 1 or > 1000) errors.Add("每次发射数必须在 1–1000 之间。");
            }
        }
        else if (draft.Kind == EntityKind.Zombie)
        {
            if (ZombieTemplateCatalog.Find(draft.TemplateEntityId) is null)
                errors.Add($"僵尸模板 ID 必须在 {ZombieTemplateCatalog.FirstZombieId}–{ZombieTemplateCatalog.LastZombieId} 之间。");
            if (draft.Id.Contains("PLANT", StringComparison.OrdinalIgnoreCase) ||
                draft.DisplayName.Contains("植物", StringComparison.Ordinal))
                errors.Add("当前选择的是僵尸，但字符串 ID 或名称仍包含“PLANT/植物”；请改成明确的僵尸 ID 和名称。");
            if (addsEntity && operation == PublishOperation.Install)
                errors.Add("真正新增僵尸运行时尚未完成；新增模式只能导出 ZIP 资产骨架，不能一键安装并伪装成原版僵尸替换。");
        }
        else if (operation is PublishOperation.Package or PublishOperation.Install)
        {
            errors.Add("当前 Mod 打包和一键安装只支持植物或僵尸工程。");
        }

        var classification = AnimationEntityClassifier.Classify(sourceProject);
        if (classification.IsHighConfidence && classification.Kind != draft.Kind)
            errors.Add(
                $"动画内容高置信度识别为“{AnimationEntityClassifier.GetKindName(classification.Kind)}”，" +
                $"当前选择的是“{AnimationEntityClassifier.GetKindName(draft.Kind)}”。" +
                $"证据：{string.Join("；", classification.Evidence)}");
        if (draft.Kind == EntityKind.Plant &&
            (draft.Id.Contains("ZOMBIE", StringComparison.OrdinalIgnoreCase) ||
             draft.DisplayName.Contains("僵尸", StringComparison.Ordinal)))
            errors.Add("当前选择的是植物，但字符串 ID 或名称仍包含“ZOMBIE/僵尸”；请重新确认实体类型。");
        return errors;
    }

    public string DescribeTemplate(EntityKind kind, int templateId) => kind switch
    {
        EntityKind.Plant => PlantTemplateCatalog.Find(templateId)?.Summary ?? $"未知植物模板 ID：{templateId}",
        EntityKind.Zombie => ZombieTemplateCatalog.Find(templateId)?.Summary ?? $"未知僵尸模板 ID：{templateId}",
        _ => "UI/其他动画不使用植物或僵尸模板。"
    };

    public string ResolveCarrier(EntityKind kind, int templateId, string fallback) => kind switch
    {
        EntityKind.Plant => PlantTemplateCatalog.Find(templateId)?.CarrierReanimation ?? fallback,
        EntityKind.Zombie => ZombieTemplateCatalog.Find(templateId)?.CarrierReanimation ?? fallback,
        _ => fallback
    };

}
