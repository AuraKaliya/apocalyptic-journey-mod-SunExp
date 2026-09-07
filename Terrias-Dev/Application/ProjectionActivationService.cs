using Terrias.Dll.Mechanics;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Application;

public static class ProjectionActivationService
{
    public static void OpenRoleSelection(ScriptExecutor self)
    {
        if (!ProjectionUiApi.OpenRoleSelection(self))
        {
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.projection.selection_unavailable"));
        }
    }

    public static bool GrantCurrentRoleCard(ScriptExecutor self)
    {
        var role = PolymorphRoleRegistry.CurrentRole();
        if (role == null || string.IsNullOrWhiteSpace(role.Id))
        {
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.projection.current_role_missing"));
            return false;
        }

        return GrantRoleCard(self, role, fixedAnotherMe: true);
    }

    public static bool GrantRoleCard(ScriptExecutor self, string roleId)
    {
        var role = PolymorphRoleRegistry.Find(roleId);
        if (role == null)
        {
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.projection.role_missing"));
            return false;
        }

        return GrantRoleCard(self, role, fixedAnotherMe: false);
    }

    private static bool GrantRoleCard(ScriptExecutor self, PolymorphRoleSpec role, bool fixedAnotherMe)
    {
        var presentation = BuildRoleCardPresentation(role, fixedAnotherMe);
        var request = CardGrantRequest
            .ToHand(TerriasIds.ProjectionRoleTemplateShortId)
            .WithSource("projection:" + role.Id)
            .WithRuntimeTags("Burnout", "Nihility")
            .WithRuntimePresentation(presentation)
            .RequireMutations()
            .Configure("projection-role-card", config => ConfigureRoleCard(config, role));
        var result = CardApi.GrantCardToHand(self, request);
        if (!result.Success)
        {
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.projection.card_failed"));
            return false;
        }

        PlayerApi.ShowCaption(fixedAnotherMe
            ? TerriasTextCatalog.Get("caption.projection.another_me_granted")
            : TerriasTextCatalog.Format("caption.projection.card_granted", "name", role.DisplayName));
        return true;
    }

    public static bool SummonFromCard(ScriptExecutor self)
    {
        if (self == null)
        {
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.projection.summon_failed"));
            return false;
        }

        var roleId = DictionaryUtil.Get(self.Vars, TerriasIds.ProjectionRoleIdKey);
        var role = PolymorphRoleRegistry.Find(roleId);
        if (role == null)
        {
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.projection.target_expired"));
            return false;
        }

        return ProjectionSummonService.TrySummon(self, role);
    }

    public static void ClearBattle(string source)
    {
        ProjectionStateStore.ClearAll(source);
    }

    private static void ConfigureRoleCard(DataConfig config, PolymorphRoleSpec role)
    {
        DictionaryUtil.Set(config.Vars, "Tag", AppendToken(DictionaryUtil.Get(config.Vars, "Tag"), "Burnout", "Nihility"));
        DictionaryUtil.Set(config.Vars, TerriasIds.RuntimeMarkersKey,
            AppendToken(DictionaryUtil.Get(config.Vars, TerriasIds.RuntimeMarkersKey), TerriasIds.ProjectionRoleCardMarker));
        DictionaryUtil.Set(config.Vars, TerriasIds.ProjectionRoleIdKey, role.Id);
        DictionaryUtil.Set(config.Vars, TerriasIds.ProjectionRoleNameKey, role.DisplayNameFor(TerriasLocale.ZhHans));
        DictionaryUtil.Set(config.Vars, TerriasIds.ProjectionRoleCardFacePathKey, role.CardFacePath);
    }

    private static Dictionary<string, string> BuildRoleCardPresentation(
        PolymorphRoleSpec role,
        bool fixedAnotherMe)
    {
        var result = new Dictionary<string, string> { ["Icon"] = role.CardFacePath };
        var arguments = new Dictionary<string, string>();
        var nameKey = fixedAnotherMe ? "card.projection.another_me.name" : "card.projection.name";
        var descriptionKey = fixedAnotherMe ? "card.projection.another_me.description" : "card.projection.description";
        foreach (var locale in TerriasLocale.Supported)
        {
            arguments["name"] = role.DisplayNameFor(locale);
            result[TerriasLocale.FieldName("Name", locale)] =
                TerriasTextCatalog.GetForLocale(nameKey, locale, arguments);
            result[TerriasLocale.FieldName("Description", locale)] =
                TerriasTextCatalog.GetForLocale(descriptionKey, locale, arguments);
        }

        return result;
    }

    private static string AppendToken(string existing, params string[] tokens)
    {
        var result = existing ?? "";
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token) || DictionaryUtil.ContainsToken(result, token))
            {
                continue;
            }

            result = string.IsNullOrWhiteSpace(result) ? token.Trim() : result + "," + token.Trim();
        }

        return result;
    }
}
