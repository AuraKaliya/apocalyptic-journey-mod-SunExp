using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.GameApi;

public static class OlimyaGameApi
{
    public static bool IsLocalPlayer(IStatusManager? status)
    {
        var local = FightPlayer.Instance?.Status;
        return status != null && local != null
            && (ReferenceEquals(status, local) || !string.IsNullOrWhiteSpace(status.InstanceId) && status.InstanceId == local.InstanceId);
    }

    public static bool IsLocalRoleTable(RoleTable? role) => role != null && ReferenceEquals(role, RoleTable.Instance);

    public static int Money(RoleTable role) => Math.Max(0, (int)role.Money);

    public static int Remainder(RoleTable role, string key)
    {
        return role.SpecialVarMap != null && role.SpecialVarMap.TryGetValue(key, out var value) && value == "1" ? 1 : 0;
    }

    public static void SetRemainder(RoleTable role, string key, int value)
    {
        role.SpecialVarMap ??= new Dictionary<string, string>(StringComparer.Ordinal);
        role.SpecialVarMap[key] = value == 1 ? "1" : "0";
    }

    public static int ManufactureBeforeMoneyNotification(RoleTable role, int amount)
    {
        if (!IsLocalRoleTable(role) || amount <= 0) return 0;
        var before = Money(role);
        var next = (int)Math.Min(2147483646L, (long)before + amount);
        // Called immediately before the native Money property notification.
        // That notification publishes the final balance. Writing the checked
        // native backing field avoids a second income multiplier and event.
        role.money = next;
        return next - before;
    }

    public static void ClearLocalShield()
    {
        var status = FightPlayer.Instance?.Status;
        if (status != null) status.Defend = 0;
    }

    public static bool IsFreshPlayerTurn() => FightPlayer.Instance?.Status != null && !FightUI.IsReset;

    public static bool IsHostileEnemy(IStatusManager? status)
    {
        return status?.fatherObject is Enemy && StatusApi.IsAlive(status);
    }

    public static bool SetGoldenization(IStatusManager target, bool active)
    {
        if (active)
        {
            target.AddBuff(OlimyaIds.Goldenized, 1);
            return BuffApi.Level(target, OlimyaIds.Goldenized) > 0;
        }
        if (BuffApi.Level(target, OlimyaIds.Goldenized) > 0) target.RemoveBuff(OlimyaIds.Goldenized);
        return true;
    }

    public static bool AwardAttackGold(IStatusManager? recipient, int amount)
    {
        if (recipient == null || amount <= 0) return false;
        var executor = DamageApi.CreateCardSourceExecutor(recipient, OlimyaIds.GoldenTouch, "Olimya.GoldenizedIncome");
        if (executor == null) return false;
        // A fresh executor preserves native target ownership routing. The remote
        // receiver also enforces that its destination is a player with a wallet.
        executor.SetStatus("Self");
        executor.ChangeMoney(amount.ToString());
        return true;
    }
}
