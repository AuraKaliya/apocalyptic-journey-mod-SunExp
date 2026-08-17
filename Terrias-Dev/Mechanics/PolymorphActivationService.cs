using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class PolymorphActivationService
{
    public static void OpenRoleSelection(ScriptExecutor self)
    {
        if (!PolymorphUiApi.OpenRoleSelection(self))
        {
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.polymorph.selection_unavailable"));
        }
    }

    public static bool GrantRoleCard(ScriptExecutor self, string roleId)
    {
        var role = PolymorphRoleRegistry.Find(roleId);
        if (role == null)
        {
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.polymorph.role_missing"));
            return false;
        }

        var presentation = BuildRoleCardPresentation(role);
        var request = CardGrantRequest
            .ToHand(TerriasIds.PolymorphRoleTemplateShortId)
            .WithSource("polymorph:" + role.Id)
            .WithRuntimeTags("Burnout", "Nihility")
            .WithRuntimePresentation(presentation)
            .RequireMutations()
            .Configure("polymorph-role-card", config => ConfigureRoleCard(config, role));
        var result = CardApi.GrantCardToHand(self, request);
        if (!result.Success)
        {
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.polymorph.card_failed"));
            return false;
        }

        PlayerApi.ShowCaption(TerriasTextCatalog.Format("caption.polymorph.card_granted", "name", role.DisplayName));
        return true;
    }

    public static bool ApplyRoleFromCard(ScriptExecutor self)
    {
        if (self == null)
        {
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.polymorph.switch_failed"));
            return false;
        }

        var roleId = DictionaryUtil.Get(self.Vars, TerriasIds.PolymorphRoleIdKey);
        var role = PolymorphRoleRegistry.Find(roleId);
        if (role == null)
        {
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.polymorph.target_expired"));
            return false;
        }

        return PolymorphBuffService.GrantForRole(self, role);
    }

    public static void ClearBattle(string source)
    {
        PolymorphRuntimeService.ClearAll(source);
        PolymorphStateStore.ClearAll(source);
        PolymorphCooldownService.ClearAll();
    }

    private static void ConfigureRoleCard(DataConfig config, PolymorphRoleSpec role)
    {
        DictionaryUtil.Set(config.Vars, "Tag", AppendToken(DictionaryUtil.Get(config.Vars, "Tag"), "Burnout", "Nihility"));
        DictionaryUtil.Set(config.Vars, TerriasIds.RuntimeMarkersKey,
            AppendToken(DictionaryUtil.Get(config.Vars, TerriasIds.RuntimeMarkersKey), TerriasIds.PolymorphRoleCardMarker));
        DictionaryUtil.Set(config.Vars, TerriasIds.PolymorphRoleIdKey, role.Id);
        DictionaryUtil.Set(config.Vars, TerriasIds.PolymorphRoleNameKey, role.DisplayNameFor(TerriasLocale.ZhHans));
        DictionaryUtil.Set(config.Vars, TerriasIds.PolymorphRoleCardFacePathKey, role.CardFacePath);
        DictionaryUtil.Set(config.Vars, TerriasIds.PolymorphRoleCropXKey, role.CropOffsetX.ToString());
        DictionaryUtil.Set(config.Vars, TerriasIds.PolymorphRoleCropYKey, role.CropOffsetY.ToString());
    }

    private static Dictionary<string, string> BuildRoleCardPresentation(PolymorphRoleSpec role)
    {
        var result = new Dictionary<string, string> { ["Icon"] = role.CardFacePath };
        var arguments = new Dictionary<string, string>();
        foreach (var locale in TerriasLocale.Supported)
        {
            arguments["name"] = role.DisplayNameFor(locale);
            result[TerriasLocale.FieldName("Name", locale)] =
                TerriasTextCatalog.GetForLocale("card.polymorph.name", locale, arguments);
            result[TerriasLocale.FieldName("Description", locale)] =
                TerriasTextCatalog.GetForLocale("card.polymorph.description", locale, arguments);
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
