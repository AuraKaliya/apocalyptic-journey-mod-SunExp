using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class SolarFinaleStateService
{
    public static void EnsureLedger()
    {
        if (string.IsNullOrWhiteSpace(PlayerApi.GetGameVar(SunExpIds.SolarFinaleSavedNamesKey, "")))
        {
            PlayerApi.SetGameVar(SunExpIds.SolarFinaleSavedNamesKey, SunExpIds.SolarFinaleNameCount.ToString());
        }

        if (string.IsNullOrWhiteSpace(PlayerApi.GetGameVar(SunExpIds.SolarFinaleBurnedNamesKey, "")))
        {
            PlayerApi.SetGameVar(SunExpIds.SolarFinaleBurnedNamesKey, "0");
        }

        if (string.IsNullOrWhiteSpace(PlayerApi.GetGameVar(SunExpIds.SolarFinaleNamelessNamesKey, "")))
        {
            PlayerApi.SetGameVar(SunExpIds.SolarFinaleNamelessNamesKey, "0");
        }
    }

    public static int SavedNames()
    {
        return Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(
            SunExpIds.SolarFinaleSavedNamesKey,
            SunExpIds.SolarFinaleNameCount.ToString())));
    }

    public static int BurnedNames()
    {
        return Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(SunExpIds.SolarFinaleBurnedNamesKey, "0")));
    }

    public static int NamelessNames()
    {
        return Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(SunExpIds.SolarFinaleNamelessNamesKey, "0")));
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

        PlayerApi.SetGameVar(SunExpIds.SolarFinaleSavedNamesKey, (saved - actual).ToString());
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleBurnedNamesKey, (BurnedNames() + actual).ToString());
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

        PlayerApi.SetGameVar(SunExpIds.SolarFinaleSavedNamesKey, (saved - actual).ToString());
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleNamelessNamesKey, (NamelessNames() + actual).ToString());
        return actual;
    }

}
