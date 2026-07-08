using System;
using System.Reflection;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class BattleRewardCardPresentationRuntime
{
    private static readonly FieldInfo? CardChoiceItemDataConfigField = typeof(CardChoiceItem).GetField(
        "dataConfig",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "BattleRewardsUI.Entry", context => QueueRewardScan(context.Target as BattleRewardsUI, "BattleRewardsUI.Entry"));
        RegisterAfter(modConfig, "BattleRewardsUI.ModeSetReward", context => QueueRewardScan(context.Target as BattleRewardsUI, "BattleRewardsUI.ModeSetReward"));
        RegisterAfter(modConfig, "CardChoiceItem.Initialize", ApplyChoiceItemInitialize);
        SunExpLog.InfoAlways("Battle reward card presentation diagnostics initialized");
    }

    private static void ApplyChoiceItemInitialize(ModHookContext context)
    {
        SunExpPerformanceCounters.Record("RewardCardPresentation.CardChoiceItem.Initialize.Observed");
        var item = context.Target as CardChoiceItem;
        if (!ApplyChoiceItem(item, "CardChoiceItem.Initialize:direct"))
        {
            SunExpPerformanceCounters.Record("RewardCardPresentation.CardChoiceItem.Initialize.Miss");
            SunExpLog.InfoOnceAlways(
                "RewardCardPresentation.CardChoiceItem.Initialize.Miss",
                "CardChoiceItem.Initialize hook observed but no reward card config was extracted: target="
                + TargetName(context.Target)
                + ", args="
                + ArgumentShape(context.Arguments));
        }
    }

    private static void QueueRewardScan(BattleRewardsUI? rewardUi, string source)
    {
        SunExpPerformanceCounters.Record("RewardCardPresentation." + CounterKey(source) + ".Observed");
        if (rewardUi == null)
        {
            SunExpPerformanceCounters.Record("RewardCardPresentation.RewardUiMiss");
            return;
        }

        ApplyRewardChoices(rewardUi, source);
        ScheduleRewardScan(rewardUi, source + ":next", 1);
        ScheduleRewardScan(rewardUi, source + ":settled", 2);
    }

    private static void ScheduleRewardScan(BattleRewardsUI rewardUi, string source, int delayFrames)
    {
        SunExpFrameDispatcher.RunOnceAfterFrames(
            "RewardCardPresentation.Scan." + rewardUi.GetHashCode() + "." + delayFrames,
            Math.Max(1, delayFrames),
            () => ApplyRewardChoices(rewardUi, source));
    }

    private static void ApplyRewardChoices(BattleRewardsUI? rewardUi, string source)
    {
        if (rewardUi == null)
        {
            return;
        }

        try
        {
            var choices = rewardUi.GetComponentsInChildren<CardChoiceItem>(includeInactive: true);
            SunExpPerformanceCounters.Record("RewardCardPresentation.Scan");
            if (choices == null || choices.Length == 0)
            {
                SunExpPerformanceCounters.Record("RewardCardPresentation.Scan.Empty");
                SunExpLog.InfoOnceAlways(
                    "RewardCardPresentation.Scan.Empty." + source,
                    "Battle reward card scan found no CardChoiceItem children: source=" + source);
                return;
            }

            var applied = 0;
            foreach (var choice in choices)
            {
                if (ApplyChoiceItem(choice, source))
                {
                    applied++;
                }
            }

            SunExpLog.Debug("Battle reward card scan applied: source="
                + source
                + ", choices="
                + choices.Length
                + ", applied="
                + applied);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Battle reward card presentation scan failed: source=" + source + ", error=" + ex.Message);
        }
    }

    private static bool ApplyChoiceItem(CardChoiceItem? item, string source)
    {
        if (item == null)
        {
            return false;
        }

        var config = CardChoiceItemDataConfigField?.GetValue(item) as IDataConfig;
        if (config == null)
        {
            return false;
        }

        SunExpPerformanceCounters.Record("RewardCardPresentation.ChoiceConfigHit");
        SunExpCardPresentationRouter.RequestApply(
            item.transform,
            config,
            source,
            SunExpCardPresentationSurface.RewardChoice);
        SunExpLog.InfoOnceAlways(
            "RewardCardPresentation.ChoiceConfigHit." + CardConfigApi.Id(config),
            "Battle reward card presentation route hit: source="
            + source
            + ", cardId="
            + CardConfigApi.Id(config)
            + ", root="
            + (item.transform == null ? "<null>" : item.transform.name));
        return true;
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "BattleRewardCardPresentation");
    }

    private static string CounterKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        var chars = value.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '.' && chars[i] != '_' && chars[i] != '-')
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    private static string TargetName(object? target)
    {
        return target == null ? "<null>" : target.GetType().FullName ?? target.GetType().Name;
    }

    private static string ArgumentShape(object[]? args)
    {
        if (args == null || args.Length == 0)
        {
            return "none";
        }

        var parts = new string[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            parts[i] = arg == null ? "null" : arg.GetType().FullName ?? arg.GetType().Name;
        }

        return string.Join("|", parts);
    }
}
