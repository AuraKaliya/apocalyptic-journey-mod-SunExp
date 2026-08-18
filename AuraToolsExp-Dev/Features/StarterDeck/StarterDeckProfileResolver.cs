using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.StarterDeck;

internal sealed class CustomStartResolvedLoadout
{
    internal string RoleId { get; set; } = "";

    internal List<string> CardIds { get; set; } = new();

    internal List<string> RelicIds { get; set; } = new();

    internal string CardSource { get; set; } = "global";

    internal string RelicSource { get; set; } = "global";
}

internal static class StarterDeckProfileResolver
{
    internal static bool IsGlobalModeEnabled()
    {
        var settings = AuraToolsConfigService.MatchExperience.StarterDeck;
        settings.Normalize();
        return settings.Mode == StarterDeckModes.Global;
    }

    internal static CustomStartResolvedLoadout ResolveEffectiveLoadout(string roleId)
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var settings = AuraToolsConfigService.MatchExperience.StarterDeck;
        var effective = settings.ResolveEffective(normalizedRole);
        var result = new CustomStartResolvedLoadout
        {
            RoleId = normalizedRole,
            CardIds = effective.CardIds,
            RelicIds = effective.RelicIds,
            CardSource = effective.CardSource,
            RelicSource = effective.RelicSource
        };
        return result;
    }

    internal static StarterDeckLocalProfileSettings EffectiveSettingsForExport(string roleId)
    {
        var resolved = ResolveEffectiveLoadout(roleId);
        return new StarterDeckLocalProfileSettings
        {
            RoleId = resolved.RoleId,
            DisplayName = string.IsNullOrWhiteSpace(resolved.RoleId)
                ? "全局自定义开局"
                : RoleCatalog.GetDisplayName(resolved.RoleId) + " 自定义开局",
            InheritCards = false,
            InheritRelics = false,
            CardIds = resolved.CardIds,
            RelicIds = resolved.RelicIds
        };
    }
}
