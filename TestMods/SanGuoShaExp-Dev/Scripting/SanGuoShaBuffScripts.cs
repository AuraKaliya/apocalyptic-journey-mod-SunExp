using System;
using System.Linq;
using SanGuoShaExp.Dll.GameApi;
using SanGuoShaExp.Dll.Hooks;
using SanGuoShaExp.Dll.Infrastructure;

namespace SanGuoShaExp.Dll.Scripting;

public static class SanGuoShaBuffScripts
{
    private const string DodgeCleanupHook = "SanGuoShaExpDodgeCleanupHook";
    private const string KillIntentCleanupHook = "SanGuoShaExpKillIntentCleanupHook";
    private const string KillIntentCleanupToken = "SanGuoShaExpKillIntentCleanupToken";
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
                    RegisterDodgeCleanup(self);
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
        // Retained as a stable CSV-callable entry point for older cached data.
    }

    private static void RegisterDodgeCleanup(ScriptExecutor self)
    {
        if (ExecutorApi.GetVar(self, DodgeCleanupHook, "0") == "1")
        {
            return;
        }

        if (ExecutorApi.TryAddEvent(self, "StartRound", new Action(() =>
        {
            if (!SanGuoShaCombatRuntime.IsCombatActive)
            {
                return;
            }

            if (BuffLevel(self.Self, SanGuoShaExpIds.Dodge) <= 0)
            {
                return;
            }

            self.SetStatus("Self");
            self.RemoveBuff(SanGuoShaExpIds.Dodge);
        }), "sanguosha_dodge_cleanup"))
        {
            ExecutorApi.SetVar(self, DodgeCleanupHook, "1");
        }
    }

    private static void RegisterKillIntentCleanup(ScriptExecutor self)
    {
        if (ExecutorApi.GetVar(self, KillIntentCleanupHook, "0") == "1")
        {
            return;
        }

        var token = (DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, KillIntentCleanupToken, "0")) + 1).ToString();
        if (ExecutorApi.TryAddEvent(self, "EndRound", new Action(() =>
        {
            if (!SanGuoShaCombatRuntime.IsCombatActive ||
                !ExecutorApi.IsHookTokenActive(self, KillIntentCleanupToken, token))
            {
                return;
            }

            if (BuffLevel(self.Self, SanGuoShaExpIds.KillIntent) <= 0)
            {
                ExecutorApi.ClearHook(self, KillIntentCleanupHook, KillIntentCleanupToken);
                return;
            }

            self.SetStatus("Self");
            self.RemoveBuff(SanGuoShaExpIds.KillIntent);
            ExecutorApi.ClearHook(self, KillIntentCleanupHook, KillIntentCleanupToken);
        }), "sanguosha_kill_intent"))
        {
            ExecutorApi.SetVar(self, KillIntentCleanupHook, "1");
            ExecutorApi.SetVar(self, KillIntentCleanupToken, token);
        }
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
        if (!SanGuoShaCombatRuntime.IsCombatActive)
        {
            return;
        }

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
