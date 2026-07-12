using System;
using System.Collections;
using System.Reflection;
using SunExp.Dll.Infrastructure;
using Witch.UI.Window;

namespace SunExp.Dll.GameApi;

public static class FightUiDiagnosticsApi
{
    private static readonly FieldInfo? SkillItemsField = typeof(FightUI).GetField(
        "skillItems",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    public static int SkillCount(object? fightUi)
    {
        try
        {
            return fightUi != null && SkillItemsField?.GetValue(fightUi) is ICollection skills
                ? skills.Count
                : -1;
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("FightUI diagnostics skill count unavailable: " + ex.Message);
            return -1;
        }
    }

    public static string CurrentRoleId()
    {
        var data = RoleTable.Instance?.Career?.data;
        return data == null ? "unknown" : DictionaryUtil.Get(data, "Id", "unknown");
    }
}
