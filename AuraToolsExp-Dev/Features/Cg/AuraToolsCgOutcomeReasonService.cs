using System;
using System.Globalization;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.Cg;

internal static class AuraToolsCgOutcomeReasonService
{
    private const string MidasRelicId = "relic_29";
    private const string RitualBuffId = "buff_ritualsublimation";
    private const string CurseBuffId = "buff_ProfaneButterflyHymn";
    private static string pendingReason = "";

    public static void ObserveGiveWin(ModHookContext _)
    {
        var detected = AuraToolsCgOutcomeReasonPolicy.ResolveWin(
            RitualConditionMet(),
            CurseConditionMet());
        if (string.Equals(detected, AuraToolsEventCgOutcomeReasons.StandardVictory, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(pendingReason, AuraToolsEventCgOutcomeReasons.RitualVictory, StringComparison.OrdinalIgnoreCase)
                || string.Equals(pendingReason, AuraToolsEventCgOutcomeReasons.CurseVictory, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        pendingReason = detected;
    }

    public static void ObserveEscape(ModHookContext context)
    {
        var executor = context?.Target as ScriptExecutor;
        var sourceId = ReadId(executor?.dataConfig);
        pendingReason = AuraToolsCgOutcomeReasonPolicy.ResolveEscape(
            string.Equals(sourceId, MidasRelicId, StringComparison.OrdinalIgnoreCase));
    }

    public static string Consume(AuraBattleOutcome outcome)
    {
        var reason = pendingReason;
        pendingReason = "";
        if (outcome == AuraBattleOutcome.Win)
        {
            return string.IsNullOrWhiteSpace(reason)
                ? AuraToolsEventCgOutcomeReasons.StandardVictory
                : reason;
        }

        if (outcome == AuraBattleOutcome.Escape)
        {
            return string.Equals(reason, AuraToolsEventCgOutcomeReasons.MidasEscape, StringComparison.OrdinalIgnoreCase)
                ? reason
                : AuraToolsEventCgOutcomeReasons.Escape;
        }

        return AuraToolsEventCgOutcomeReasons.Defeat;
    }

    public static void Reset()
    {
        pendingReason = "";
    }

    private static bool HasBuff(string buffId)
    {
        try
        {
            return FightPlayer.Instance?.Status?.GetBuff(buffId) != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool CurseConditionMet()
    {
        try
        {
            if (!HasBuff(CurseBuffId))
            {
                return false;
            }

            return (Witch.UI.Window.FightUI.cardItemList ?? new System.Collections.Generic.List<CardItem>())
                .Where(card => card?.dataConfig?.data != null
                               && card.dataConfig.data.TryGetValue("Tag", out var tag)
                               && (tag ?? "").IndexOf("Curse", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(card => card.dataConfig.data.TryGetValue("Name", out var name)
                    ? name ?? ReadId(card.dataConfig)
                    : ReadId(card.dataConfig))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() >= 7;
        }
        catch
        {
            return false;
        }
    }

    private static bool RitualConditionMet()
    {
        try
        {
            var ritual = FightPlayer.Instance?.Status?.GetBuff(RitualBuffId);
            if (ritual?.buffConfig == null)
            {
                return false;
            }

            if (ritual.buffConfig.Level >= 13)
            {
                return true;
            }

            var variables = ritual.buffConfig.dataConfig?.Vars;
            return variables != null
                   && variables.TryGetValue("ThisCount", out var raw)
                   && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                   && count >= 13;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadId(IDataConfig? data)
    {
        try
        {
            return data?.data != null && data.data.TryGetValue("Id", out var value)
                ? value ?? ""
                : "";
        }
        catch
        {
            return "";
        }
    }
}
