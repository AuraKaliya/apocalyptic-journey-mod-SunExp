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
            PlayerApi.ShowCaption("\u767e\u53d8\uff1a\u89d2\u8272\u9009\u62e9\u754c\u9762\u6682\u65f6\u4e0d\u53ef\u7528\u3002");
        }
    }

    public static bool GrantRoleCard(ScriptExecutor self, string roleId)
    {
        var role = PolymorphRoleRegistry.Find(roleId);
        if (role == null)
        {
            PlayerApi.ShowCaption("\u767e\u53d8\uff1a\u672a\u627e\u5230\u76ee\u6807\u89d2\u8272\u3002");
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
            PlayerApi.ShowCaption("\u767e\u53d8\uff1a\u5316\u8eab\u724c\u751f\u6210\u5931\u8d25\u3002");
            return false;
        }

        PlayerApi.ShowCaption("\u767e\u53d8\uff1a\u83b7\u5f97\u3010" + role.DisplayName + "\u3011\u5316\u8eab\u724c\u3002");
        return true;
    }

    public static bool ApplyRoleFromCard(ScriptExecutor self)
    {
        if (self == null)
        {
            PlayerApi.ShowCaption("\u767e\u53d8\uff1a\u5316\u8eab\u5207\u6362\u5931\u8d25\u3002");
            return false;
        }

        var roleId = DictionaryUtil.Get(self.Vars, TerriasIds.PolymorphRoleIdKey);
        var role = PolymorphRoleRegistry.Find(roleId);
        if (role == null)
        {
            PlayerApi.ShowCaption("\u767e\u53d8\uff1a\u5316\u8eab\u76ee\u6807\u5df2\u5931\u6548\u3002");
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
        DictionaryUtil.Set(config.Vars, TerriasIds.PolymorphRoleNameKey, role.DisplayName);
        DictionaryUtil.Set(config.Vars, TerriasIds.PolymorphRoleCardFacePathKey, role.CardFacePath);
        DictionaryUtil.Set(config.Vars, TerriasIds.PolymorphRoleCropXKey, role.CropOffsetX.ToString());
        DictionaryUtil.Set(config.Vars, TerriasIds.PolymorphRoleCropYKey, role.CropOffsetY.ToString());
    }

    private static Dictionary<string, string> BuildRoleCardPresentation(PolymorphRoleSpec role)
    {
        return new Dictionary<string, string>
        {
            ["Icon"] = role.CardFacePath,
            ["Name"] = "\u767e\u53d8\uff1a" + role.DisplayName,
            ["Name_zh-Hant"] = "\u767e\u8b8a\uff1a" + role.DisplayName,
            ["Name_en"] = "Polymorph: " + role.DisplayName,
            ["Name_ja"] = "\u767e\u5909\uff1a" + role.DisplayName,
            ["Description"] = "\u767e\u53d8\uff1a" + role.DisplayName,
            ["Description_zh-Hant"] = "\u767e\u8b8a\uff1a" + role.DisplayName,
            ["Description_en"] = "Polymorph: " + role.DisplayName,
            ["Description_ja"] = "\u767e\u5909\uff1a" + role.DisplayName
        };
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
