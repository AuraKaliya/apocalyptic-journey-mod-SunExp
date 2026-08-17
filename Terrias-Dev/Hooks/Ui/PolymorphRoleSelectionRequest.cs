using System;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Hooks.Ui;

public sealed class PolymorphRoleSelectionRequest
{
    private readonly Func<ScriptExecutor, PolymorphRoleSpec, bool> onSelected;
    private readonly string title;
    private readonly string subtitle;
    private readonly string footerHint;
    private readonly string selectionFailureText;

    public PolymorphRoleSelectionRequest(
        string title,
        string subtitle,
        string footerHint,
        string selectionFailureText,
        string logPrefix,
        Func<ScriptExecutor, PolymorphRoleSpec, bool> onSelected)
    {
        this.title = string.IsNullOrWhiteSpace(title) ? "百变" : title;
        this.subtitle = string.IsNullOrWhiteSpace(subtitle) ? "已注册角色：" : subtitle;
        this.footerHint = string.IsNullOrWhiteSpace(footerHint) ? "只在本场战斗内生效。" : footerHint;
        this.selectionFailureText = string.IsNullOrWhiteSpace(selectionFailureText) ? "角色牌生成失败，请选择其他角色。" : selectionFailureText;
        LogPrefix = string.IsNullOrWhiteSpace(logPrefix) ? "RoleSelection" : logPrefix;
        this.onSelected = onSelected ?? ((_, _) => false);
    }

    public string Title => TerriasTextCatalog.ResolveLegacy(title);

    public string Subtitle => TerriasTextCatalog.ResolveLegacy(subtitle);

    public string FooterHint => TerriasTextCatalog.ResolveLegacy(footerHint);

    public string SelectionFailureText => TerriasTextCatalog.ResolveLegacy(selectionFailureText);

    public string LogPrefix { get; }

    public bool Select(ScriptExecutor executor, PolymorphRoleSpec role)
    {
        return onSelected(executor, role);
    }

    public static PolymorphRoleSelectionRequest Polymorph(ScriptExecutor executor)
    {
        return new PolymorphRoleSelectionRequest(
            "百变",
            "选择一个已注册角色，获得对应的一次性化身牌。已注册角色：",
            "化身只在本场战斗内生效，不会改变冒险角色。",
            "化身牌生成失败，请选择其他角色。",
            "PolymorphRoleSelection",
            (self, role) => PolymorphActivationService.GrantRoleCard(self, role.Id));
    }

    public static PolymorphRoleSelectionRequest Projection(ScriptExecutor executor)
    {
        return new PolymorphRoleSelectionRequest(
            "拜托了",
            "选择一个已注册角色，获得对应的一次性投影牌。已注册角色：",
            "投影会作为友方行动单位加入战斗，并计入4人上限。",
            "投影牌生成失败，请选择其他角色。",
            "ProjectionRoleSelection",
            (self, role) => ProjectionActivationService.GrantRoleCard(self, role.Id));
    }
}
