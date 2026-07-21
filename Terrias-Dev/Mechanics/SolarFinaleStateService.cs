using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class SolarFinaleStateService
{
    public static void EnsureLedger()
    {
        if (string.IsNullOrWhiteSpace(PlayerApi.GetGameVar(TerriasIds.SolarFinaleSavedNamesKey, "")))
        {
            PlayerApi.SetGameVar(TerriasIds.SolarFinaleSavedNamesKey, TerriasIds.SolarFinaleNameCount.ToString());
        }

        if (string.IsNullOrWhiteSpace(PlayerApi.GetGameVar(TerriasIds.SolarFinaleBurnedNamesKey, "")))
        {
            PlayerApi.SetGameVar(TerriasIds.SolarFinaleBurnedNamesKey, "0");
        }

        if (string.IsNullOrWhiteSpace(PlayerApi.GetGameVar(TerriasIds.SolarFinaleNamelessNamesKey, "")))
        {
            PlayerApi.SetGameVar(TerriasIds.SolarFinaleNamelessNamesKey, "0");
        }
    }

    public static int SavedNames()
    {
        return Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(
            TerriasIds.SolarFinaleSavedNamesKey,
            TerriasIds.SolarFinaleNameCount.ToString())));
    }

    public static int BurnedNames()
    {
        return Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(TerriasIds.SolarFinaleBurnedNamesKey, "0")));
    }

    public static int NamelessNames()
    {
        return Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(TerriasIds.SolarFinaleNamelessNamesKey, "0")));
    }

    public static int BurnNames(int count)
    {
        EnsureLedger();
        var saved = SavedNames();
        var actual = Math.Min(saved, Math.Max(0, count));
        if (actual <= 0)
        {
            return 0;
        }

        PlayerApi.SetGameVar(TerriasIds.SolarFinaleSavedNamesKey, (saved - actual).ToString());
        PlayerApi.SetGameVar(TerriasIds.SolarFinaleBurnedNamesKey, (BurnedNames() + actual).ToString());
        return actual;
    }

    public static int MakeNameless(int count)
    {
        EnsureLedger();
        var saved = SavedNames();
        var actual = Math.Min(saved, Math.Max(0, count));
        if (actual <= 0)
        {
            return 0;
        }

        PlayerApi.SetGameVar(TerriasIds.SolarFinaleSavedNamesKey, (saved - actual).ToString());
        PlayerApi.SetGameVar(TerriasIds.SolarFinaleNamelessNamesKey, (NamelessNames() + actual).ToString());
        return actual;
    }

}
