using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class LoneerRuntime
{
    [ThreadStatic] private static Stack<MorningPrayerAttempt>? morningPrayerAttempts;

    public static void Initialize(ModConfig modConfig)
    {
        SunExpBattleLifecycleRouter.Register("Loneer", new SunExpBattleLifecycleSubscription
        {
            FightStarted = OnFightStart
        });
        SunExpHookRegistry.Before(modConfig, SunExpHookTargets.SkillItemTrueUse, BeginMorningPrayerAttempt, "Loneer.MorningPrayerAttempt");
        SunExpHookRegistry.After(modConfig, SunExpHookTargets.SkillItemTrueUse, EndMorningPrayerAttempt, "Loneer.MorningPrayerAttempt");
    }

    private static void OnFightStart(ModHookContext context)
    {
        if (!LoneerMiracleService.IsActive())
        {
            LoneerCombatStateStore.ClearAll();
            StarStonePouchStateStore.ClearAll();
            return;
        }
    }

    private static void BeginMorningPrayerAttempt(ModHookContext context)
    {
        if (context.Target is not SkillItem skillItem || !IsMorningPrayer(skillItem.dataConfig))
        {
            return;
        }

        var self = skillItem.scriptExecutor as ScriptExecutor;
        var state = LoneerCombatStateStore.Get(self?.Self);
        morningPrayerAttempts ??= new Stack<MorningPrayerAttempt>();
        morningPrayerAttempts.Push(new MorningPrayerAttempt(
            self,
            SkillUseGateApi.Capture(skillItem),
            state?.PrayerUseCount ?? 0));
    }

    private static void EndMorningPrayerAttempt(ModHookContext context)
    {
        if (context.Target is not SkillItem skillItem
            || !IsMorningPrayer(skillItem.dataConfig)
            || morningPrayerAttempts == null
            || morningPrayerAttempts.Count == 0)
        {
            return;
        }

        var attempt = morningPrayerAttempts.Pop();
        var state = LoneerCombatStateStore.Get(attempt.Executor?.Self);
        var resolved = state != null && state.PrayerUseCount > attempt.PrayerUseCount;
        var reason = resolved
            ? "resolved"
            : !attempt.Gate.NativeAllowed
                ? attempt.Gate.RejectionReason()
                : state?.SelectionPending == true || state?.SelectionScheduled == true
                    ? "guidance-selection-pending"
                    : string.IsNullOrWhiteSpace(state?.GuidanceCardId)
                        ? "guidance-unavailable"
                        : state?.ActionResolving == true
                            ? "action-resolving"
                            : "script-not-committed";

        if (!resolved && !attempt.Gate.NativeAllowed)
        {
            ShowRejectedCaption(reason);
            if (string.Equals(reason, "skill-time-missing", StringComparison.Ordinal))
            {
                LoneerMiracleService.EnsureMorningPrayerSkillState(attempt.Executor, state, "SkillItem.TrueUse:missing-key");
            }
        }

        SunExpLog.InfoAlways("[MorningPrayerAttempt] outcome="
            + reason
            + ", nativeAllowed="
            + attempt.Gate.NativeAllowed
            + ", cardCanUse="
            + attempt.Gate.CardUseAllowed
            + ", status="
            + attempt.Gate.StatusState
            + ", fightType="
            + attempt.Gate.FightType
            + ", skillTimePresent="
            + attempt.Gate.SkillTimePresent
            + ", skillTime="
            + attempt.Gate.SkillTime
            + ", guidance="
            + (state?.GuidanceCardId ?? "")
            + ", selectionPending="
            + (state?.SelectionPending == true)
            + ", selectionScheduled="
            + (state?.SelectionScheduled == true)
            + ", actionResolving="
            + (state?.ActionResolving == true)
            + ".");
    }

    private static bool IsMorningPrayer(IDataConfig? config)
    {
        var id = RoleSkillApi.NormalizeSkillId(CardConfigApi.Id(config));
        return string.Equals(id, RoleSkillApi.NormalizeSkillId(SunExpIds.LoneerMorningPrayerSkillCardId), StringComparison.Ordinal)
            || string.Equals(id, "loneer_morning_star_prayer", StringComparison.Ordinal);
    }

    private static void ShowRejectedCaption(string reason)
    {
        if (string.Equals(reason, "card-ui-busy", StringComparison.Ordinal))
        {
            PlayerApi.ShowCaption("当前动作尚未结束，晨星祈愿未释放。");
        }
        else if (reason.StartsWith("fight-", StringComparison.Ordinal) || reason.StartsWith("status-", StringComparison.Ordinal))
        {
            PlayerApi.ShowCaption("当前尚不可行动，晨星祈愿未释放。");
        }
        else if (string.Equals(reason, "skill-time-missing", StringComparison.Ordinal))
        {
            PlayerApi.ShowCaption("晨星祈愿状态正在同步，请重新使用。");
        }
    }

    private readonly struct MorningPrayerAttempt
    {
        public MorningPrayerAttempt(ScriptExecutor? executor, SkillUseGateSnapshot gate, int prayerUseCount)
        {
            Executor = executor;
            Gate = gate;
            PrayerUseCount = prayerUseCount;
        }

        public ScriptExecutor? Executor { get; }
        public SkillUseGateSnapshot Gate { get; }
        public int PrayerUseCount { get; }
    }
}
