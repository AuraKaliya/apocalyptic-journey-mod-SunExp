using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public sealed class OlimyaMoneyFrame
{
    public OlimyaMoneyFrame(int before, bool active) { Before = before; Active = active; }
    public int Before { get; }
    public bool Active { get; }
    public bool Notified { get; set; }
}

public static class OlimyaEconomyService
{
    private static readonly Dictionary<RoleTable, Stack<OlimyaMoneyFrame>> Frames = new();
    private static readonly HashSet<RoleTable> Initializing = new();

    public static bool IsActive()
    {
        var local = FightPlayer.Instance?.Status;
        var effective = local == null ? PlayerApi.GetCurrentCareerId() : PolymorphStateStore.EffectiveCombatRoleIdFor(local);
        return OlimyaRules.IsOlimya(effective);
    }

    public static void BeginInitialization(RoleTable role)
    {
        Frames.Remove(role);
        Initializing.Add(role);
    }

    public static void EndInitialization(RoleTable role)
    {
        Frames.Remove(role);
        Initializing.Remove(role);
    }

    public static void BeforeMoneyChange(RoleTable role)
    {
        if (!OlimyaGameApi.IsLocalRoleTable(role)) return;
        if (!Frames.TryGetValue(role, out var stack)) Frames[role] = stack = new Stack<OlimyaMoneyFrame>();
        if (stack.Count >= 64)
        {
            stack.Clear();
            TerriasLog.Warn("[Olimya] discarded abandoned nested money notifications.");
        }
        stack.Push(new OlimyaMoneyFrame(OlimyaGameApi.Money(role), !Initializing.Contains(role) && IsActive()));
    }

    public static void BeforeMoneyNotification(RoleTable role)
    {
        if (!Frames.TryGetValue(role, out var stack) || stack.Count == 0) return;
        var frame = stack.Peek();
        if (frame.Notified) return;
        frame.Notified = true;
        if (!frame.Active || !IsActive() || !OlimyaGameApi.IsLocalRoleTable(role)) return;
        var change = (long)OlimyaGameApi.Money(role) - frame.Before;
        if (change == 0) return;
        var key = change > 0 ? OlimyaIds.IncomeRemainder : OlimyaIds.SpendingRemainder;
        var reward = OlimyaRules.Coins(change, OlimyaGameApi.Remainder(role, key));
        OlimyaGameApi.SetRemainder(role, key, reward.Remainder);
        OlimyaGameApi.ManufactureBeforeMoneyNotification(role, reward.Manufactured);
    }

    public static void AfterMoneyChange(RoleTable role)
    {
        if (!Frames.TryGetValue(role, out var stack)) return;
        if (stack.Count > 0) stack.Pop();
        if (stack.Count == 0) Frames.Remove(role);
    }

    public static void ClearTransient()
    {
        Frames.Clear();
        Initializing.Clear();
    }
}
