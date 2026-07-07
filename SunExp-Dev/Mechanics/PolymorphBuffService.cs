using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class PolymorphBuffService
{
    private const string HookKey = "SunExpPolymorphTraitHook";
    private const string TokenKey = "SunExpPolymorphTraitToken";

    public static bool GrantForRole(ScriptExecutor self, PolymorphRoleSpec role)
    {
        if (self?.Self == null || role == null)
        {
            PlayerApi.ShowCaption("\u767e\u53d8\uff1a\u5316\u8eab\u5207\u6362\u5931\u8d25\u3002");
            return false;
        }

        PolymorphStateStore.SetPending(role, self.Self);
        if (BuffApi.Has(self.Self, SunExpIds.PolymorphTraitBuffId))
        {
            return Apply(self);
        }

        try
        {
            self.SetStatus("Self");
            self.AddBuff(SunExpIds.PolymorphTraitBuffId, "1");
            return true;
        }
        catch (Exception ex)
        {
            PolymorphStateStore.ClearPending(self.Self);
            SunExpLog.Warn("[Polymorph] failed to grant trait buff: " + ex.Message);
            PlayerApi.ShowCaption("\u767e\u53d8\uff1a\u5316\u8eab\u5207\u6362\u5931\u8d25\u3002");
            return false;
        }
    }

    public static bool Apply(ScriptExecutor self)
    {
        if (self?.Self == null)
        {
            return false;
        }

        var role = PolymorphStateStore.PendingFor(self.Self);
        if (role == null)
        {
            SunExpLog.Warn("[Polymorph] trait buff applied without a pending role; removing inert buff.");
            RemoveTraitBuff(self);
            return false;
        }

        try
        {
            UpdateTraitBuffDescription(self, role);
            RegisterRoundTick(self);

            self.SetStatus("Self");
            var state = PolymorphStateStore.SetLocal(role, self.Self);
            self.ChangeCareer(role.Id);
            PolymorphRuntimeService.Enter(self, role, state);
            PolymorphNetworkSync.BroadcastEnter(state, "PolymorphBuffService.Apply");
            PolymorphCooldownService.ApplyToCurrentRole(self, "PolymorphBuffService.Apply:" + role.Id);
            PlayerApi.ShowCaption("\u767e\u53d8\uff1a\u5316\u8eab\u4e3a\u3010" + state.DisplayName + "\u3011\u3002");
            SunExpPerformanceCounters.Record("Polymorph.BuffApplied");
            return true;
        }
        catch (Exception ex)
        {
            var active = PolymorphStateStore.ActiveFor(self.Self);
            if (active != null)
            {
                PolymorphRuntimeService.ClearOwner(active.OwnerStatusId, "PolymorphBuffService.ApplyFailed");
            }

            PolymorphStateStore.ClearOwner(self.Self, "PolymorphBuffService.ApplyFailed");
            PolymorphCooldownService.Clear(self.Self);
            SunExpLog.Warn("[Polymorph] trait apply failed: " + ex.Message);
            PlayerApi.ShowCaption("\u767e\u53d8\uff1a\u5316\u8eab\u5207\u6362\u5931\u8d25\u3002");
            return false;
        }
    }

    public static void Clear(ScriptExecutor self)
    {
        var owner = self?.Self;
        try
        {
            var active = PolymorphStateStore.ActiveFor(owner);
            if (active != null)
            {
                PolymorphRuntimeService.ClearOwner(active.OwnerStatusId, "PolymorphBuffService.Clear");
            }

            PolymorphStateStore.ClearOwner(owner, "PolymorphBuffService.Clear");
            PolymorphCooldownService.Clear(owner);
            SunExpPerformanceCounters.Record("Polymorph.BuffCleared");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[Polymorph] trait clear failed: " + ex.Message);
        }
    }

    public static void UpdateTraitBuffDescription(ScriptExecutor self, PolymorphRoleSpec role)
    {
        if (self == null || role == null)
        {
            return;
        }

        var description = "\u53d8\u8eab\u6210\u4e3a" + role.DisplayName;
        SetDescription(self.dataConfig, description);

        var buff = self.Self?.GetBuff(SunExpIds.PolymorphTraitBuffId);
        SetDescription(buff?.buffConfig?.dataConfig, description);
        try
        {
            buff?.UpdateMsg();
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("[Polymorph] trait buff message refresh skipped: " + ex.Message);
        }
    }

    private static void RegisterRoundTick(ScriptExecutor self)
    {
        var token = ExecutorApi.RegisterHook(self, HookKey, TokenKey);
        if (token == null)
        {
            return;
        }

        ExecutorApi.TryAddTokenedEvent(self, "StartRound", TokenKey, token, new Action(() =>
        {
            if (ExecutorApi.IsHookTokenActive(self, TokenKey, token))
            {
                PolymorphCooldownService.TickRound(self, "PolymorphBuffService.StartRound");
            }
        }), "polymorph_trait");
    }

    private static void RemoveTraitBuff(ScriptExecutor self)
    {
        try
        {
            self.SetStatus("Self");
            self.RemoveBuff(SunExpIds.PolymorphTraitBuffId);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[Polymorph] inert trait buff remove failed: " + ex.Message);
        }
    }

    private static void SetDescription(IDataConfig? config, string description)
    {
        if (config?.Vars == null)
        {
            return;
        }

        DictionaryUtil.Set(config.Vars, "Description", description);
        DictionaryUtil.Set(config.Vars, "Description_zh-Hant", description);
        DictionaryUtil.Set(config.Vars, "Description_ja", description);
        DictionaryUtil.Set(config.Vars, "Description_en", "Polymorphed into " + description.Substring("\u53d8\u8eab\u6210\u4e3a".Length) + ".");
    }
}
