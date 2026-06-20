using System;
using System.Linq;
using SanGuoShaExp.Dll.GameApi;
using SanGuoShaExp.Dll.Infrastructure;

namespace SanGuoShaExp.Dll.Scripting;

public static class SanGuoShaBuffScripts
{
    private const int DodgeFilter = 100;
    private const string LightningHook = "SanGuoShaExpLightningHook";

    public static void Apply(ScriptExecutor self, string id)
    {
        try
        {
            switch (id)
            {
                case "kill_intent":
                    RegisterKillIntentCleanup(self);
                    break;
                case "dodge":
                    AddDamageFilter(self, DodgeFilter);
                    break;
                case "lightning":
                    RegisterLightning(self);
                    break;
            }
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Error("SanGuoSha buff apply failed: " + id, ex);
        }
    }

    public static void Clear(ScriptExecutor self, string id)
    {
        try
        {
            if (id == "dodge")
            {
                AddDamageFilter(self, -DodgeFilter);
            }
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Error("SanGuoSha buff clear failed: " + id, ex);
        }
    }

    private static void RegisterKillIntentCleanup(ScriptExecutor self)
    {
        ExecutorApi.TryAddEvent(self, "EndRound", new Action(() =>
        {
            if (BuffLevel(self.Self, SanGuoShaExpIds.KillIntent) <= 0)
            {
                return;
            }

            self.SetStatus("Self");
            self.RemoveBuff(SanGuoShaExpIds.KillIntent);
        }), "sanguosha_kill_intent");
    }

    private static void RegisterLightning(ScriptExecutor self)
    {
        if (ExecutorApi.GetVar(self, LightningHook, "0") == "1")
        {
            return;
        }

        if (ExecutorApi.TryAddEvent(self, "StartRound", new Action(() => ResolveLightning(self)), "sanguosha_lightning"))
        {
            ExecutorApi.SetVar(self, LightningHook, "1");
        }
    }

    private static void ResolveLightning(ScriptExecutor self)
    {
        if (BuffLevel(self.Self, SanGuoShaExpIds.Lightning) <= 0)
        {
            return;
        }

        if (Check(self, 40))
        {
            self.SetStatus("AllRandomTarget1");
            var target = self.Object?.FirstOrDefault();
            var damage = 1 + ((target?.MaxHp ?? 0) * 20 / 100);
            self.Damage(Math.Max(1, damage).ToString(), "True");

            self.SetStatus("Self");
            self.AddBuff(SanGuoShaExpIds.Fate, "5");
            self.RemoveBuff(SanGuoShaExpIds.Lightning);
            TransferLightningToFriend(self);
            return;
        }

        self.SetStatus("Self");
        var selfDamage = Math.Max(1, (self.Self?.MaxHp ?? 1) * 60 / 100);
        self.Damage(selfDamage.ToString());
        self.RemoveBuff(SanGuoShaExpIds.Lightning);
    }

    private static void TransferLightningToFriend(ScriptExecutor self)
    {
        self.SetStatus("AllFriendsExSelf");
        var friends = self.Object?.Where(target => target != null && target.CurHp > 0).ToList();
        if (friends == null || friends.Count == 0)
        {
            return;
        }

        var friend = friends[UnityEngine.Random.Range(0, friends.Count)];
        self.SetStatusById(friend.InstanceId);
        self.AddBuff(SanGuoShaExpIds.Lightning, "1");
    }

    private static void AddDamageFilter(ScriptExecutor self, int delta)
    {
        if (self.Self?.DamageFilter == null)
        {
            return;
        }

        AddDamageFilter(self.Self, "Normal", delta);
        AddDamageFilter(self.Self, "True", delta);
        AddDamageFilter(self.Self, "Dot", delta);
    }

    private static void AddDamageFilter(IStatusManager target, string key, int delta)
    {
        var current = target.DamageFilter.TryGetValue(key, out var value) ? value : 0f;
        var next = current + delta;
        if (next <= 0.001f)
        {
            target.DamageFilter.Remove(key);
        }
        else
        {
            target.DamageFilter[key] = next;
        }
    }

    private static bool Check(ScriptExecutor self, int threshold)
    {
        try
        {
            return self.CheckDice.Roll().Value > threshold;
        }
        catch
        {
            return false;
        }
    }

    private static int BuffLevel(IStatusManager? target, string buffId)
    {
        return target?.GetBuff(buffId)?.buffConfig?.Level ?? 0;
    }
}
