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

    public static void PreserveLedger()
    {
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleSavedNamesKey, SunExpIds.SolarFinaleNameCount.ToString());
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleBurnedNamesKey, "0");
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleNamelessNamesKey, "0");
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

    public static void MarkSecondSunDefeated(bool defeated)
    {
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleSecondSunDefeatedKey, defeated ? "1" : "0");
    }

    public static void MarkSaintGateOpened()
    {
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleSaintGateOpenedKey, "shown");
    }

    public static void MarkSaintGateResolved()
    {
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleSaintGateResolvedKey, "1");
        PlayerApi.SetGameVar(SunExpIds.SolarFinalePendingSaintBattleKey, "");
    }

    public static bool CanReachSaintBattle()
    {
        return PlayerApi.GetGameVar(SunExpIds.SolarFinaleSecondSunDefeatedKey, "0") == "1"
            && SavedNames() >= SunExpIds.SolarFinaleHiddenBossNameThreshold
            && BurnedNames() < SunExpIds.SolarFinaleHiddenBossNameThreshold;
    }

    public static string EndingKey()
    {
        return PlayerApi.GetGameVar(SunExpIds.SolarFinaleEndingKey, "");
    }

    public static void SetEnding(string ending)
    {
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleEndingKey, ending ?? "");
    }

    public static string EnsureEnding()
    {
        var ending = EndingKey();
        if (!string.IsNullOrWhiteSpace(ending))
        {
            return ending;
        }

        ending = ResolveEndingKey();
        SetEnding(ending);
        return ending;
    }

    public static string ResolveEndingKey()
    {
        if (BurnedNames() >= SunExpIds.SolarFinaleHiddenBossNameThreshold)
        {
            return "witch";
        }

        return SavedNames() >= SunExpIds.SolarFinaleHiddenBossNameThreshold ? "stars" : "white_city";
    }

    public static void MarkCompleted()
    {
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleCompletedKey, "1");
    }
}
