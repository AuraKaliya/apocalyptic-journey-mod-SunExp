using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class ProjectionActivationService
{
    public static void OpenRoleSelection(ScriptExecutor self)
    {
        if (!ProjectionUiApi.OpenRoleSelection(self))
        {
            PlayerApi.ShowCaption("拜托了：角色选择界面暂时不可用。");
        }
    }

    public static bool GrantCurrentRoleCard(ScriptExecutor self)
    {
        var role = PolymorphRoleRegistry.CurrentRole();
        if (role == null || string.IsNullOrWhiteSpace(role.Id))
        {
            PlayerApi.ShowCaption("拜托了：未找到当前角色。");
            return false;
        }

        return GrantRoleCard(self, role, fixedAnotherMe: true);
    }

    public static bool GrantRoleCard(ScriptExecutor self, string roleId)
    {
        var role = PolymorphRoleRegistry.Find(roleId);
        if (role == null)
        {
            PlayerApi.ShowCaption("拜托了：未找到目标角色。");
            return false;
        }

        return GrantRoleCard(self, role, fixedAnotherMe: false);
    }

    private static bool GrantRoleCard(ScriptExecutor self, PolymorphRoleSpec role, bool fixedAnotherMe)
    {
        var request = CardGrantRequest
            .ToHand(TerriasIds.ProjectionRoleTemplateShortId)
            .WithSource("projection:" + role.Id)
            .WithRuntimeTags("Burnout", "Nihility")
            .RequireMutations()
            .Configure("projection-role-card", config => ConfigureRoleCard(config, role, fixedAnotherMe));
        var result = CardApi.GrantCardToHand(self, request);
        if (!result.Success)
        {
            PlayerApi.ShowCaption("拜托了：投影牌生成失败。");
            return false;
        }

        PlayerApi.ShowCaption(fixedAnotherMe
            ? "拜托了：获得【另一个我】。"
            : "拜托了：获得【" + role.DisplayName + "的投影】。");
        return true;
    }

    public static bool SummonFromCard(ScriptExecutor self)
    {
        if (self == null)
        {
            PlayerApi.ShowCaption("拜托了：召唤失败。");
            return false;
        }

        var roleId = DictionaryUtil.Get(self.Vars, TerriasIds.ProjectionRoleIdKey);
        var role = PolymorphRoleRegistry.Find(roleId);
        if (role == null)
        {
            PlayerApi.ShowCaption("拜托了：投影目标已失效。");
            return false;
        }

        return ProjectionSummonService.TrySummon(self, role);
    }

    public static void ClearBattle(string source)
    {
        ProjectionStateStore.ClearAll(source);
    }

    private static void ConfigureRoleCard(DataConfig config, PolymorphRoleSpec role, bool fixedAnotherMe)
    {
        var displayName = fixedAnotherMe ? "另一个我" : role.DisplayName + "的投影";
        DictionaryUtil.Set(config.Vars, "Tag", AppendToken(DictionaryUtil.Get(config.Vars, "Tag"), "Burnout", "Nihility"));
        DictionaryUtil.Set(config.Vars, "Icon", role.CardFacePath);
        DictionaryUtil.Set(config.Vars, "Name", displayName);
        DictionaryUtil.Set(config.Vars, "Name_zh-Hant", fixedAnotherMe ? "另一個我" : role.DisplayName + "的投影");
        DictionaryUtil.Set(config.Vars, "Name_en", fixedAnotherMe ? "Another Me" : role.DisplayName + " Projection");
        DictionaryUtil.Set(config.Vars, "Name_ja", fixedAnotherMe ? "もう一人の私" : role.DisplayName + "の投影");
        DictionaryUtil.Set(config.Vars, "Description", fixedAnotherMe ? "召唤另一个我。" : "召唤" + displayName + "。");
        DictionaryUtil.Set(config.Vars, "Description_zh-Hant", fixedAnotherMe ? "召喚另一個我。" : "召喚" + role.DisplayName + "的投影。");
        DictionaryUtil.Set(config.Vars, "Description_en", fixedAnotherMe ? "Summon another you." : "Summon " + role.DisplayName + "'s projection.");
        DictionaryUtil.Set(config.Vars, "Description_ja", fixedAnotherMe ? "もう一人の自分を召喚する。" : role.DisplayName + "の投影を召喚する。");
        DictionaryUtil.Set(config.Vars, TerriasIds.RuntimeMarkersKey,
            AppendToken(DictionaryUtil.Get(config.Vars, TerriasIds.RuntimeMarkersKey), TerriasIds.ProjectionRoleCardMarker));
        DictionaryUtil.Set(config.Vars, TerriasIds.ProjectionRoleIdKey, role.Id);
        DictionaryUtil.Set(config.Vars, TerriasIds.ProjectionRoleNameKey, role.DisplayName);
        DictionaryUtil.Set(config.Vars, TerriasIds.ProjectionRoleCardFacePathKey, role.CardFacePath);
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
