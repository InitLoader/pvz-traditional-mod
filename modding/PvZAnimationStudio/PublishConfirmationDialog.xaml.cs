using System.Windows;
using System.Windows.Controls;
using PvZAnimationStudio.Models;
using PvZAnimationStudio.Services;

namespace PvZAnimationStudio;

public partial class PublishConfirmationDialog : Window
{
    private sealed record KindChoice(EntityKind Kind, string Name);
    private sealed record ModeChoice(EntityIntegrationMode Mode, string Name);

    private readonly EditorProject _project;
    private readonly PublishOperation _operation;
    private readonly PublishConfirmationService _confirmation = new();

    public PublishProjectDraft? Result { get; private set; }

    public PublishConfirmationDialog(EditorProject project, PublishOperation operation, string? targetPath)
    {
        InitializeComponent();
        _project = project;
        _operation = operation;
        OperationText.Text = operation switch
        {
            PublishOperation.ExportRaw => "导出 Raw .reanim",
            PublishOperation.ExportCompiled => "导出原版 .reanim.compiled",
            PublishOperation.Package => "打包 Mod ZIP",
            _ => "一键安装到游戏"
        };
        KindCombo.ItemsSource = new[]
        {
            new KindChoice(EntityKind.Plant, "植物"),
            new KindChoice(EntityKind.Zombie, "僵尸"),
            new KindChoice(EntityKind.Ui, "UI"),
            new KindChoice(EntityKind.Other, "其他")
        };
        KindCombo.SelectedItem = ((IEnumerable<KindChoice>)KindCombo.ItemsSource)
            .First(choice => choice.Kind == project.Kind);
        ModeCombo.ItemsSource = new[]
        {
            new ModeChoice(EntityIntegrationMode.ReplaceOriginal, "替换原版动画"),
            new ModeChoice(EntityIntegrationMode.AddEntity, "新增实体")
        };
        ModeCombo.SelectedItem = ((IEnumerable<ModeChoice>)ModeCombo.ItemsSource)
            .First(choice => choice.Mode == project.IntegrationMode);
        IdBox.Text = project.Id;
        NameBox.Text = project.DisplayName;
        DescriptionBox.Text = project.Description;
        NumericIdBox.Text = project.NumericEntityId.ToString();
        TemplateIdBox.Text = project.TemplateEntityId.ToString();
        InitialActionCombo.ItemsSource = project.Actions.Select(action => action.Id).ToArray();
        InitialActionCombo.SelectedItem = project.InitialActionId;
        HealthBox.Text = project.Health.ToString();
        DamageBox.Text = project.Damage.ToString();
        CostBox.Text = project.Cost.ToString();
        RechargeBox.Text = project.RechargeTime.ToString();
        LaunchRateBox.Text = project.LaunchRate.ToString();
        ProjectileTypeBox.Text = project.ProjectileType.ToString();
        ShotsBox.Text = project.ShotsPerAttack.ToString();
        ProjectSummaryText.Text = $"动画：{project.Animation.Tracks.Count} 轨 / {project.Animation.FrameCount} 帧；图片绑定：{project.ImageBindings.Count}；动作：{project.Actions.Count}";
        TargetPathText.Text = string.IsNullOrWhiteSpace(targetPath) ? "目标位置将在下一步选择。" : $"目标：{targetPath}";
        UpdateTemplatePreview();
    }

    private EntityKind SelectedKind => (KindCombo.SelectedItem as KindChoice)?.Kind ?? _project.Kind;
    private EntityIntegrationMode SelectedMode =>
        (ModeCombo.SelectedItem as ModeChoice)?.Mode ?? _project.IntegrationMode;

    private void Field_Changed(object sender, RoutedEventArgs eventArgs)
    {
        if (!IsInitialized) return;
        UpdateTemplatePreview();
    }

    private void UpdateTemplatePreview()
    {
        var templateId = int.TryParse(TemplateIdBox.Text, out var parsed) ? parsed : -1;
        TemplateSummaryText.Text = _confirmation.DescribeTemplate(SelectedKind, templateId);
        CarrierText.Text = "载体：" + _confirmation.ResolveCarrier(SelectedKind, templateId, _project.CarrierReanimation);
        var addsEntity = SelectedMode == EntityIntegrationMode.AddEntity;
        ModeDescriptionText.Text = addsEntity
            ? "新增实体会生成实体资产骨架。植物当前仍是模板兼容路径；真正新增僵尸只能打包，不能一键安装。"
            : "替换模式只绑定所选原版实体的主体动画，不创建新增实体配置，也不会复制可复用的原版图片。当前一键安装只支持僵尸替换。";
        NumericIdLabel.Visibility = addsEntity ? Visibility.Visible : Visibility.Collapsed;
        NumericIdBox.Visibility = addsEntity ? Visibility.Visible : Visibility.Collapsed;
        TemplateIdLabel.Text = addsEntity ? "模板 ID *" : "目标原版 ID *";
        CommonCombatFields.Visibility = addsEntity ? Visibility.Visible : Visibility.Collapsed;
        PlantFields.Visibility = addsEntity && SelectedKind == EntityKind.Plant
            ? Visibility.Visible
            : Visibility.Collapsed;
        AcknowledgeBox.Content = addsEntity
            ? "我已核对新增实体类型、ID、模板和数值"
            : "我已核对替换目标、动画 ID 和原版模板";

        if (PublishConfirmationService.LooksLikeZombieBody(_project) && SelectedKind != EntityKind.Zombie)
        {
            ValidationBorder.Visibility = Visibility.Visible;
            ValidationText.Text = "检测到僵尸主体轨道。请选择“僵尸”，否则不能继续导出或安装。";
        }
        else
        {
            ValidationBorder.Visibility = Visibility.Collapsed;
            ValidationText.Text = string.Empty;
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs eventArgs)
    {
        var parseErrors = new List<string>();
        var numericId = ParseInt(NumericIdBox, "数字 ID", parseErrors);
        var templateId = ParseInt(TemplateIdBox, "模板 ID", parseErrors);
        var health = ParseInt(HealthBox, "生命", parseErrors);
        var damage = ParseInt(DamageBox, "攻击伤害", parseErrors);
        var cost = ParseInt(CostBox, "阳光", parseErrors);
        var recharge = ParseInt(RechargeBox, "冷却", parseErrors);
        var launchRate = ParseInt(LaunchRateBox, "攻击间隔", parseErrors);
        var projectileType = ParseInt(ProjectileTypeBox, "子弹类型", parseErrors);
        var shots = ParseInt(ShotsBox, "每次发射数", parseErrors);
        var draft = new PublishProjectDraft(
            SelectedKind,
            SelectedMode,
            IdBox.Text.Trim(),
            NameBox.Text.Trim(),
            DescriptionBox.Text.Trim(),
            numericId,
            templateId,
            InitialActionCombo.SelectedItem as string ?? string.Empty,
            health,
            damage,
            cost,
            recharge,
            launchRate,
            projectileType,
            shots);
        parseErrors.AddRange(_confirmation.Validate(draft, _project, _operation));
        if (AcknowledgeBox.IsChecked != true)
            parseErrors.Add("请勾选“我已核对实体类型、ID、模板和数值”。");
        if (parseErrors.Count > 0)
        {
            ValidationText.Text = string.Join("\n", parseErrors.Distinct());
            ValidationBorder.Visibility = Visibility.Visible;
            return;
        }
        Result = draft;
        DialogResult = true;
    }

    private static int ParseInt(TextBox box, string fieldName, ICollection<string> errors)
    {
        if (int.TryParse(box.Text.Trim(), out var result)) return result;
        errors.Add($"{fieldName}必须填写整数。");
        return 0;
    }
}
