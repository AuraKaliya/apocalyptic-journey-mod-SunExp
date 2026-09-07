using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.GameApi;

public static class MoonHomecomingCardApi
{
    public static bool IsLocalPlayer(IStatusManager? status)
    {
        var local = FightPlayer.Instance?.Status;
        return status != null && local != null
            && (ReferenceEquals(status, local)
                || !string.IsNullOrWhiteSpace(status.InstanceId)
                && string.Equals(status.InstanceId, local.InstanceId, StringComparison.Ordinal));
    }

    public static IReadOnlyList<IDataConfig> HandCards(ScriptExecutor? self)
    {
        if (!IsLocalPlayer(self?.Self))
            return Array.Empty<IDataConfig>();

        return AuraCombatCardZoneSnapshot.Capture(self, new AuraCombatCardZoneSnapshotOptions
        {
            IncludeFightUiActive = true,
            IncludeFightUiWait = true,
            IncludeExecutorHand = true,
            IncludeExecutorWait = true
        }).Cards.Select(reference => reference.Config).OfType<IDataConfig>().ToArray();
    }

    public static bool SameCard(IDataConfig? left, IDataConfig? right)
    {
        return ReferenceEquals(left, right)
            || left != null && right != null && !string.IsNullOrWhiteSpace(left.InstanceID)
            && string.Equals(left.InstanceID, right.InstanceID, StringComparison.Ordinal);
    }

    public static bool TryBurnHandCard(ScriptExecutor self, IDataConfig card)
    {
        if (!HandCards(self).Any(candidate => SameCard(candidate, card))) return false;
        try
        {
            self.BurnCardByData(card);
            return !HandCards(self).Any(candidate => SameCard(candidate, card));
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[MoonHomecoming] offering could not be burned", ex);
            return false;
        }
    }
}
