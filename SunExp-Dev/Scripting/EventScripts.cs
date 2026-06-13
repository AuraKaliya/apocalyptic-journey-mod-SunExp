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
}
