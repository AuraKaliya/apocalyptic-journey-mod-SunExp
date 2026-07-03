using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class ProjectionActivationService
{
    public static void OpenRoleSelection(ScriptExecutor self)
    {
        if (!ProjectionUiApi.OpenRoleSelection(self))
        {
            PlayerApi.ShowCaption("魔女投影：角色选择界面暂时不可用。");
        }
    }

    public static bool GrantRoleCard(ScriptExecutor self, string roleId)
    {
        var role = PolymorphRoleRegistry.Find(roleId);
        if (role == null)
        {
            PlayerApi.ShowCaption("魔女投影：未找到目标角色。");
            return false;
        }

        var request = CardGrantRequest
            .ToHand(SunExpIds.ProjectionRoleTemplateShortId)
            .WithSource("projection:" + role.Id)
            .WithRuntimeTags("Burnout", "Nihility")
            .RequireMutations()
            .Configure("projection-role-card", config => ConfigureRoleCard(config, role));
        var result = CardApi.GrantCardToHand(self, request);
        if (!result.Success)
        {
            PlayerApi.ShowCaption("魔女投影：投影牌生成失败。");
            return false;
        }

        PlayerApi.ShowCaption("魔女投影：获得【" + role.DisplayName + "的投影】。");
        return true;
    }

    public static bool SummonFromCard(ScriptExecutor self)
    {
        if (self == null)
        {
            PlayerApi.ShowCaption("魔女投影：召唤失败。");
            return false;
        }

        var roleId = DictionaryUtil.Get(self.Vars, SunExpIds.ProjectionRoleIdKey);
        var role = PolymorphRoleRegistry.Find(roleId);
        if (role == null)
        {
            PlayerApi.ShowCaption("魔女投影：投影目标已失效。");
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
        var displayName = role.DisplayName + "的投影";
        DictionaryUtil.Set(config.Vars, "Tag", AppendToken(DictionaryUtil.Get(config.Vars, "Tag"), "Burnout", "Nihility"));
        DictionaryUtil.Set(config.Vars, "Icon", role.CardFacePath);
        DictionaryUtil.Set(config.Vars, "Name", displayName);
        DictionaryUtil.Set(config.Vars, "Name_zh-Hant", role.DisplayName + "的投影");
        DictionaryUtil.Set(config.Vars, "Name_en", role.DisplayName + " Projection");
        DictionaryUtil.Set(config.Vars, "Name_ja", role.DisplayName + "の投影");
        DictionaryUtil.Set(config.Vars, "Description", "召唤" + displayName + "。");
        DictionaryUtil.Set(config.Vars, "Description_zh-Hant", "召喚" + role.DisplayName + "的投影。");
        DictionaryUtil.Set(config.Vars, "Description_en", "Summon " + role.DisplayName + "'s projection.");
        DictionaryUtil.Set(config.Vars, "Description_ja", role.DisplayName + "の投影を召喚する。");
        DictionaryUtil.Set(config.Vars, SunExpIds.RuntimeMarkersKey,
            AppendToken(DictionaryUtil.Get(config.Vars, SunExpIds.RuntimeMarkersKey), SunExpIds.ProjectionRoleCardMarker));
        DictionaryUtil.Set(config.Vars, SunExpIds.ProjectionRoleIdKey, role.Id);
        DictionaryUtil.Set(config.Vars, SunExpIds.ProjectionRoleNameKey, role.DisplayName);
        DictionaryUtil.Set(config.Vars, SunExpIds.ProjectionRoleCardFacePathKey, role.CardFacePath);
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
