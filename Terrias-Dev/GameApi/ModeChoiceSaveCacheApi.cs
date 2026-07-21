using System;
using Data.Save;
using Terrias.Dll.Infrastructure;
using Witch.UI.Window;

namespace Terrias.Dll.GameApi;

public static class ModeChoiceSaveCacheApi
{
    public static void ClearCachedSaveIf(string modeType, Func<SaveInfo, bool> predicate, string source)
    {
        if (string.IsNullOrWhiteSpace(modeType) || predicate == null)
        {
            return;
        }

        try
        {
            if (!ModeChoiceUI.beforeSave.TryGetValue(modeType, out var saveInfo)
                || saveInfo == null
                || !predicate(saveInfo))
            {
                return;
            }

            ModeChoiceUI.beforeSave[modeType] = null;
            TerriasLog.Debug("[ModeChoiceSaveCache] cleared cached "
                + modeType
                + " save from "
                + source
                + "; save="
                + saveInfo.Name
                + ".");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[ModeChoiceSaveCache] clear cached save failed from "
                + source
                + ": "
                + ex.Message);
        }
    }

    public static void ForgetSelectedSaveIf(Func<SaveInfo, bool> predicate, string source)
    {
        if (predicate == null)
        {
            return;
        }

        try
        {
            var saveInfo = GameEntryUI.selectedSave;
            if (saveInfo == null || !predicate(saveInfo))
            {
                return;
            }

            GameEntryUI.selectedSave = null;
            TerriasLog.Debug("[ModeChoiceSaveCache] cleared selected save from "
                + source
                + "; save="
                + saveInfo.Name
                + ".");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[ModeChoiceSaveCache] clear selected save failed from "
                + source
                + ": "
                + ex.Message);
        }
    }
}
