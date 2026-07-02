using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class PolymorphActivationService
{
    public static void OpenRoleSelection(ScriptExecutor self)
    {
        if (!PolymorphUiApi.OpenRoleSelection(self))
        {
            PlayerApi.ShowCaption("百变：角色选择界面暂时不可用。");
        }
    }

    public static bool GrantRoleCard(ScriptExecutor self, string roleId)
    {
        var role = PolymorphRoleRegistry.Find(roleId);
        if (role == null)
        {
            PlayerApi.ShowCaption("百变：未找到目标角色。");
            return false;
        }

        var request = CardGrantRequest
            .ToHand(SunExpIds.PolymorphRoleTemplateShortId)
            .WithSource("polymorph:" + role.Id)
            .WithRuntimeTags("Burnout", "Nihility")
            .RequireMutations()
            .Configure("polymorph-role-card", config => ConfigureRoleCard(config, role));
        var result = CardApi.GrantCardToHand(self, request);
        if (!result.Success)
        {
            PlayerApi.ShowCaption("百变：化身牌生成失败。");
            return false;
        }

        PlayerApi.ShowCaption("百变：获得【" + role.DisplayName + "】化身牌。");
        return true;
    }

    public static bool ApplyRoleFromCard(ScriptExecutor self)
    {
        if (self == null)
        {
            PlayerApi.ShowCaption("百变：化身切换失败。");
            return false;
        }

        var roleId = DictionaryUtil.Get(self.Vars, SunExpIds.PolymorphRoleIdKey);
        var role = PolymorphRoleRegistry.Find(roleId);
        if (role == null)
        {
            PlayerApi.ShowCaption("百变：化身目标已失效。");
            return false;
        }

        var state = PolymorphStateStore.SetLocal(role, self.Self);
        try
        {
            self.SetStatus("Self");
            self.ChangeCareer(role.Id);
        }
        catch (Exception ex)
        {
            PolymorphStateStore.ClearAll("PolymorphActivationService.ApplyFailed");
            SunExpLog.Warn("百变化身切换失败: " + ex.Message);
            PlayerApi.ShowCaption("百变：化身切换失败。");
            return false;
        }

        PlayerApi.ShowCaption("百变：本场战斗化身为【" + state.DisplayName + "】。");
        return true;
    }

    public static void ClearBattle(string source)
    {
        PolymorphStateStore.ClearAll(source);
    }

    private static void ConfigureRoleCard(DataConfig config, PolymorphRoleSpec role)
    {
        DictionaryUtil.Set(config.data, "Icon", role.CardFacePath);
        DictionaryUtil.Set(config.data, "Name", "百变：" + role.DisplayName);
        DictionaryUtil.Set(config.data, "Name_zh-Hant", "百變：" + role.DisplayName);
        DictionaryUtil.Set(config.data, "Name_en", "Polymorph: " + role.DisplayName);
        DictionaryUtil.Set(config.data, "Name_ja", "百変：" + role.DisplayName);
        DictionaryUtil.Set(config.data, "Description", "使用后，本场战斗化身为【" + role.DisplayName + "】。");
        DictionaryUtil.Set(config.data, "Description_zh-Hant", "使用後，本場戰鬥化身為【" + role.DisplayName + "】。");
        DictionaryUtil.Set(config.data, "Description_en", "Use to polymorph into " + role.DisplayName + " for this combat.");
        DictionaryUtil.Set(config.data, "Description_ja", "使用すると、この戦闘中【" + role.DisplayName + "】に化身する。");
        DictionaryUtil.Set(config.data, "Tag", AppendToken(DictionaryUtil.Get(config.data, "Tag"), "Burnout", "Nihility"));
        DictionaryUtil.Set(config.Vars, "Tag", AppendToken(DictionaryUtil.Get(config.Vars, "Tag"), "Burnout", "Nihility"));
        DictionaryUtil.Set(config.Vars, SunExpIds.RuntimeMarkersKey,
            AppendToken(DictionaryUtil.Get(config.Vars, SunExpIds.RuntimeMarkersKey), SunExpIds.PolymorphRoleCardMarker));
        DictionaryUtil.Set(config.Vars, SunExpIds.PolymorphRoleIdKey, role.Id);
        DictionaryUtil.Set(config.Vars, SunExpIds.PolymorphRoleNameKey, role.DisplayName);
        DictionaryUtil.Set(config.Vars, SunExpIds.PolymorphRoleCardFacePathKey, role.CardFacePath);
        DictionaryUtil.Set(config.Vars, SunExpIds.PolymorphRoleCropXKey, role.CropOffsetX.ToString());
        DictionaryUtil.Set(config.Vars, SunExpIds.PolymorphRoleCropYKey, role.CropOffsetY.ToString());
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
