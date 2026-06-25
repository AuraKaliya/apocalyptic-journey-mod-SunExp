using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Scripting;

public static class EventScripts
{
    public static void Init(ScriptExecutor self, string id)
    {
        try
        {
            if (id == SunExpIds.WunaEventRepeat)
            {
                BeginRepeatEvent(self);
                return;
            }

            BeginWunaEvent(self, ParseStep(id));
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Event Init failed: " + id, ex);
        }
    }

    public static void RewardCard(int progress, string cardId)
    {
        try
        {
            if (!CanClaim(progress))
            {
                PlayerApi.EndEvent();
                return;
            }

            PlayerApi.AddMoney(100);
            PlayerApi.AddCard(cardId);
            Finish(progress);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Event RewardCard failed: " + cardId, ex);
        }
    }

    public static void RewardRelic(int progress, string relicId)
    {
        try
        {
            if (!CanClaim(progress))
            {
                PlayerApi.EndEvent();
                return;
            }

            PlayerApi.AddMoney(100);
            PlayerApi.AddRelic(relicId);
            Finish(progress);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Event RewardRelic failed: " + relicId, ex);
        }
    }

    public static void RewardBless(int progress, string blessId)
    {
        try
        {
            if (!CanClaim(progress))
            {
                PlayerApi.EndEvent();
                return;
            }

            PlayerApi.AddMoney(100);
            PlayerApi.AddBless(blessId);
            Finish(progress);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Event RewardBless failed: " + blessId, ex);
        }
    }

    public static void RepeatReward()
    {
        try
        {
            PlayerApi.AddBless("blessing_8");
            PlayerApi.EndEvent();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Event RepeatReward failed", ex);
        }
    }

    public static void InitSolarMemoryStart(ScriptExecutor self)
    {
        try
        {
            SolarMemoryFlowApi.EnsureOriginPoints(50);

            SunExpLog.Info("[SolarMemoryEvent] init start node; prepComplete=" + SolarMemoryFlowApi.IsPreparationComplete());
            SetEventChoices(self, "", "", "1", "1");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory start init failed", ex);
        }
    }

    public static void InitSolarMemoryNode(ScriptExecutor self)
    {
        try
        {
            SolarMemoryFlowApi.EnsureOriginPoints(50);

            SetEventChoices(self, "1", "1", "", "");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory node init failed", ex);
        }
    }

    public static void ContinueSolarMemory()
    {
        try
        {
            if (!SolarMemoryFlowApi.IsPreparationComplete())
            {
                SunExpLog.Info("[SolarMemoryEvent] continue requested before preparation complete; resuming preparation.");
                SolarMemoryFlowApi.StartOrResumePreparation();
                return;
            }

            SunExpLog.Info("[SolarMemoryEvent] continue accepted; prepared=1.");
            SolarMemoryFlowApi.MarkPrepared();
            PlayerApi.EndEvent();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory continue failed", ex);
        }
    }

    public static void OpenSolarMemoryOrigin()
    {
        try
        {
            SolarMemoryFlowApi.OpenOriginWindow();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory origin option failed", ex);
        }
    }

    public static void OpenSolarMemoryBless()
    {
        try
        {
            SolarMemoryFlowApi.OpenBlessingWindow();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory blessing option failed", ex);
        }
    }

    public static void OpenSolarMemoryDeck()
    {
        try
        {
            SolarMemoryFlowApi.OpenDeckWindow();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory deck option failed", ex);
        }
    }

    public static void OpenSolarMemoryPreparation()
    {
        try
        {
            SunExpLog.Info("[SolarMemoryEvent] preparation option selected.");
            SolarMemoryFlowApi.StartOrResumePreparation();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory preparation option failed", ex);
        }
    }

    public static void StartSolarMemoryBossRush()
    {
        try
        {
            if (!SolarMemoryFlowApi.IsPreparationComplete())
            {
                SunExpLog.Info("[SolarMemoryEvent] boss rush blocked: preparation incomplete; resuming preparation.");
                SolarMemoryFlowApi.StartOrResumePreparation();
                return;
            }

            SunExpLog.Info("[SolarMemoryEvent] boss rush started; prepared=1.");
            SolarMemoryFlowApi.MarkPrepared();
            PlayerApi.EndEvent();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory start boss rush failed", ex);
        }
    }

    public static void InitSolarFinaleLedger(ScriptExecutor self)
    {
        try
        {
            EnsureSolarFinaleLedger();
            SetEventChoices(self, "1", "1", "1", "");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar finale ledger init failed", ex);
        }
    }

    public static void PreserveSolarFinaleLedger()
    {
        try
        {
            PlayerApi.SetGameVar(SunExpIds.SolarFinaleSavedNamesKey, SunExpIds.SolarFinaleNameCount.ToString());
            PlayerApi.SetGameVar(SunExpIds.SolarFinaleBurnedNamesKey, "0");
            PlayerApi.SetGameVar(SunExpIds.SolarFinaleNamelessNamesKey, "0");
            PlayerApi.ShowCaption("名册已整理：八个名字仍在余烬中发亮。");
            PlayerApi.EndEvent();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar finale preserve ledger failed", ex);
        }
    }

    public static void BurnSolarFinaleName()
    {
        try
        {
            EnsureSolarFinaleLedger();
            BurnFinaleNames(1);
            PlayerApi.ShowCaption("一个名字被烧掉，换来终日前的一次喘息。");
            PlayerApi.EndEvent();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar finale burn name failed", ex);
        }
    }

    public static void NamelessSolarFinaleName()
    {
        try
        {
            EnsureSolarFinaleLedger();
            MoveFinaleNamesToNameless(1);
            PlayerApi.ShowCaption("一个名字被写回白曜名册，字迹完整，却再也无法被呼唤。");
            PlayerApi.EndEvent();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar finale nameless name failed", ex);
        }
    }

    public static void InitSolarFinaleSecondSun(ScriptExecutor self)
    {
        try
        {
            EnsureSolarFinaleLedger();
            SetEventChoices(self, "1", "1", "1", "");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar finale second sun init failed", ex);
        }
    }

    public static void ResolveSolarFinaleSecondSun(string result)
    {
        try
        {
            EnsureSolarFinaleLedger();
            PlayerApi.SetGameVar(SunExpIds.SolarFinaleSecondSunDefeatedKey, "1");

            if (string.Equals(result, "burn_name", StringComparison.OrdinalIgnoreCase))
            {
                BurnFinaleNames(1);
            }
            else if (string.Equals(result, "overload", StringComparison.OrdinalIgnoreCase))
            {
                BurnFinaleNames(3);
                PlayerApi.SetGameVar(SunExpIds.SolarFinaleEndingKey, "witch");
            }

            PlayerApi.EndEvent();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar finale second sun resolve failed: " + result, ex);
        }
    }

    public static void InitSolarFinaleSaint(ScriptExecutor self)
    {
        try
        {
            EnsureSolarFinaleLedger();
            var canReachHiddenBoss = SavedFinaleNames() >= SunExpIds.SolarFinaleHiddenBossNameThreshold
                && BurnedFinaleNames() < SunExpIds.SolarFinaleHiddenBossNameThreshold;
            SetEventChoices(self, canReachHiddenBoss ? "1" : "", "1", "1", "");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar finale saint init failed", ex);
        }
    }

    public static void InitSolarFinaleSaintGate(ScriptExecutor self)
    {
        try
        {
            EnsureSolarFinaleLedger();
            PlayerApi.SetGameVar(SunExpIds.SolarFinaleSecondSunDefeatedKey, "1");
            PlayerApi.SetGameVar(SunExpIds.SolarFinaleSaintGateOpenedKey, "shown");
            var canReachHiddenBoss = CanReachSolarFinaleSaintBattle();
            SetEventChoices(self, canReachHiddenBoss ? "1" : "", "1", "", "");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar finale saint gate init failed", ex);
        }
    }

    public static void EnterSolarFinaleSaintBattle()
    {
        try
        {
            EnsureSolarFinaleLedger();
            if (!CanReachSolarFinaleSaintBattle())
            {
                PlayerApi.ShowCaption("剩余名字不足以呼唤白曜圣女。");
                SkipSolarFinaleSaintBattle();
                return;
            }

            SolarMemoryFlowApi.StartFinaleSaintBattle();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar finale saint battle option failed", ex);
            SkipSolarFinaleSaintBattle();
        }
    }

    public static void SkipSolarFinaleSaintBattle()
    {
        try
        {
            EnsureSolarFinaleLedger();
            PlayerApi.SetGameVar(SunExpIds.SolarFinaleSaintGateResolvedKey, "1");
            PlayerApi.SetGameVar(SunExpIds.SolarFinalePendingSaintBattleKey, "");
            if (string.IsNullOrWhiteSpace(PlayerApi.GetGameVar(SunExpIds.SolarFinaleEndingKey, "")))
            {
                PlayerApi.SetGameVar(SunExpIds.SolarFinaleEndingKey, ResolveSolarFinaleEndingKey());
            }

            SolarMemoryFlowApi.OpenFinaleEndingEvent();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar finale saint skip failed", ex);
        }
    }

    public static void ResolveSolarFinaleSaint(string result)
    {
        try
        {
            EnsureSolarFinaleLedger();
            if (string.Equals(result, "star_echo", StringComparison.OrdinalIgnoreCase)
                && SavedFinaleNames() >= SunExpIds.SolarFinaleHiddenBossNameThreshold)
            {
                PlayerApi.SetGameVar(SunExpIds.SolarFinaleEndingKey, "stars");
            }
            else if (string.Equals(result, "burn_names", StringComparison.OrdinalIgnoreCase))
            {
                BurnFinaleNames(2);
                PlayerApi.SetGameVar(SunExpIds.SolarFinaleEndingKey, BurnedFinaleNames() >= SunExpIds.SolarFinaleHiddenBossNameThreshold ? "witch" : "white_city");
            }
            else
            {
                MoveFinaleNamesToNameless(SavedFinaleNames());
                PlayerApi.SetGameVar(SunExpIds.SolarFinaleEndingKey, "white_city");
            }

            PlayerApi.EndEvent();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar finale saint resolve failed: " + result, ex);
        }
    }

    public static void InitSolarFinaleEnding(ScriptExecutor self)
    {
        try
        {
            EnsureSolarFinaleLedger();
            var ending = PlayerApi.GetGameVar(SunExpIds.SolarFinaleEndingKey, "");
            if (string.IsNullOrWhiteSpace(ending))
            {
                ending = BurnedFinaleNames() >= SunExpIds.SolarFinaleHiddenBossNameThreshold
                    ? "witch"
                    : SavedFinaleNames() >= SunExpIds.SolarFinaleHiddenBossNameThreshold ? "stars" : "white_city";
                PlayerApi.SetGameVar(SunExpIds.SolarFinaleEndingKey, ending);
            }

            SetEventChoices(self,
                string.Equals(ending, "stars", StringComparison.OrdinalIgnoreCase) ? "1" : "",
                string.Equals(ending, "white_city", StringComparison.OrdinalIgnoreCase) ? "1" : "",
                string.Equals(ending, "witch", StringComparison.OrdinalIgnoreCase) ? "1" : "",
                "");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar finale ending init failed", ex);
        }
    }

    public static void FinishSolarFinaleEnding(string ending)
    {
        try
        {
            PlayerApi.SetGameVar(SunExpIds.SolarFinaleEndingKey, ending);
            PlayerApi.SetGameVar(SunExpIds.SolarFinaleCompletedKey, "1");
            SolarMemoryFlowApi.ShowSettlement();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar finale ending failed: " + ending, ex);
        }
    }

    private static bool BeginWunaEvent(ScriptExecutor self, int step)
    {
        var expected = Math.Max(1, Math.Min(SunExpIds.WunaEventMaxProgress, step));
        var current = GetProgress();
        return current == expected - 1
            ? SetEventChoices(self, "1", "1", "", "")
            : BeginRepeatEvent(self);
    }

    private static bool BeginRepeatEvent(ScriptExecutor self)
    {
        return SetEventChoices(self, "1", "", "", "");
    }

    private static bool SetEventChoices(ScriptExecutor self, string choice1, string choice2, string choice3, string choice4)
    {
        if (self?.Vars == null)
        {
            return false;
        }

        ExecutorApi.SetVar(self, "Choice1", string.IsNullOrWhiteSpace(choice1) ? "0" : choice1);
        ExecutorApi.SetVar(self, "Choice2", string.IsNullOrWhiteSpace(choice2) ? "0" : choice2);
        ExecutorApi.SetVar(self, "Choice3", string.IsNullOrWhiteSpace(choice3) ? "0" : choice3);
        ExecutorApi.SetVar(self, "Choice4", string.IsNullOrWhiteSpace(choice4) ? "0" : choice4);
        return true;
    }

    private static void Finish(int progress)
    {
        Advance(progress);
        PlayerApi.EndEvent();
    }

    private static int Advance(int progress)
    {
        PlayerApi.SetGameVar(SunExpIds.WunaEventProgressKey, progress.ToString());
        return progress;
    }

    private static bool CanClaim(int progress)
    {
        return progress >= 1 && progress <= SunExpIds.WunaEventMaxProgress && GetProgress() == progress - 1;
    }

    private static int GetProgress()
    {
        return DictionaryUtil.ParseInt(PlayerApi.GetGameVar(SunExpIds.WunaEventProgressKey, "0"));
    }

    private static int ParseStep(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !id.StartsWith(SunExpIds.WunaEventPrefix, StringComparison.Ordinal) || id.Length < 2)
        {
            return 1;
        }

        return DictionaryUtil.ParseInt(id.Substring(id.Length - 2));
    }

    private static void EnsureSolarFinaleLedger()
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

    private static int SavedFinaleNames()
    {
        return Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(SunExpIds.SolarFinaleSavedNamesKey, SunExpIds.SolarFinaleNameCount.ToString())));
    }

    private static int BurnedFinaleNames()
    {
        return Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(SunExpIds.SolarFinaleBurnedNamesKey, "0")));
    }

    private static bool CanReachSolarFinaleSaintBattle()
    {
        return PlayerApi.GetGameVar(SunExpIds.SolarFinaleSecondSunDefeatedKey, "0") == "1"
            && SavedFinaleNames() >= SunExpIds.SolarFinaleHiddenBossNameThreshold
            && BurnedFinaleNames() < SunExpIds.SolarFinaleHiddenBossNameThreshold;
    }

    private static string ResolveSolarFinaleEndingKey()
    {
        if (BurnedFinaleNames() >= SunExpIds.SolarFinaleHiddenBossNameThreshold)
        {
            return "witch";
        }

        return SavedFinaleNames() >= SunExpIds.SolarFinaleHiddenBossNameThreshold ? "stars" : "white_city";
    }

    private static void BurnFinaleNames(int count)
    {
        var saved = SavedFinaleNames();
        var burned = BurnedFinaleNames();
        var actual = Math.Min(saved, Math.Max(0, count));
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleSavedNamesKey, Math.Max(0, saved - actual).ToString());
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleBurnedNamesKey, (burned + actual).ToString());
    }

    private static void MoveFinaleNamesToNameless(int count)
    {
        var saved = SavedFinaleNames();
        var nameless = Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(SunExpIds.SolarFinaleNamelessNamesKey, "0")));
        var actual = Math.Min(saved, Math.Max(0, count));
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleSavedNamesKey, Math.Max(0, saved - actual).ToString());
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleNamelessNamesKey, (nameless + actual).ToString());
    }
}
