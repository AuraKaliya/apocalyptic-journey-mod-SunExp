using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class MorningStarRelicFormula
{
    public const string CareerPouchChannel = "career";
    public const string RelicPouchChannel = "relic";

    public static bool IsTimelessClockCandidate(IDataConfig? config)
    {
        return config != null
            && CardConfigApi.CurrentCost(config) > 0
            && !CardMutationService.HasRuntimeMarker(config, TerriasIds.TimelessClockZeroCostMarker);
    }

    public static bool MakeTimelessClockFree(IDataConfig? config)
    {
        if (!IsTimelessClockCandidate(config))
        {
            return false;
        }

        DictionaryUtil.Set(config!.Vars, "TotalExCost", "-999");
        CardMutationService.SetRuntimeMarkers(config, TerriasIds.TimelessClockZeroCostMarker);
        return true;
    }

    public static bool ShouldCountNegativeBuffApplication(
        string ownerStatusId,
        string sourceStatusId,
        bool targetIsEnemy,
        bool isNegativeBuff)
    {
        return !string.IsNullOrWhiteSpace(ownerStatusId)
            && string.Equals(ownerStatusId, sourceStatusId, StringComparison.Ordinal)
            && targetIsEnemy
            && isNegativeBuff;
    }

    public static bool RelicPouchRecycles(bool loneerActiveAtCombatStart)
    {
        return loneerActiveAtCombatStart;
    }

    public static string PouchStateKey(string ownerStatusId, string channelId)
    {
        if (string.IsNullOrWhiteSpace(ownerStatusId))
        {
            return "";
        }

        var channel = string.IsNullOrWhiteSpace(channelId)
            ? CareerPouchChannel
            : channelId.Trim();
        return ownerStatusId + "\u001f" + channel;
    }
}
