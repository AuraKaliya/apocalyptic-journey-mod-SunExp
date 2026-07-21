using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.GameApi;

public static class PlayerPartyApi
{
    public static IReadOnlyList<IStatusManager> Snapshot(bool aliveOnly = true)
    {
        var result = (FightManager.Instance?.statuses?.Values ?? Enumerable.Empty<IStatusManager>())
            .Where(status => status != null
                && string.Equals(status.fatherObject?.GetType().Name, "FightPlayer", StringComparison.Ordinal)
                && (!aliveOnly || StatusApi.IsAlive(status)))
            .GroupBy(status => status.InstanceId ?? status.GetHashCode().ToString(), StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(status => status.InstanceId, StringComparer.Ordinal)
            .ToList();

        var local = FightPlayer.Instance?.Status;
        if (local != null && (!aliveOnly || StatusApi.IsAlive(local))
            && result.All(status => !ReferenceEquals(status, local) && status.InstanceId != local.InstanceId))
        {
            result.Add(local);
        }

        return result;
    }

    public static bool TryGainPower(IStatusManager? target, int amount)
    {
        if (target == null || amount <= 0)
        {
            return false;
        }

        if (ReferenceEquals(target, FightPlayer.Instance?.Status) || target.InstanceId == FightPlayer.Instance?.Status?.InstanceId)
        {
            return PlayerPowerApi.TryGainPower(amount);
        }

        try
        {
            if (target.MirrorSc is ScriptExecutor executor)
            {
                TargetApi.SetStatusForTarget(executor, target, "Self");
                executor.ChangePower(amount.ToString());
                return true;
            }
        }
        catch
        {
            // Remote player power is normally replicated by its owning peer.
        }

        return false;
    }
}
