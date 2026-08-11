using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class PolymorphBuffService
{
    private const string HookKey = "TerriasPolymorphTraitHook";
    private const string TokenKey = "TerriasPolymorphTraitToken";

    public static bool GrantForRole(ScriptExecutor self, PolymorphRoleSpec role)
    {
        if (self?.Self == null || role == null)
        {
            PlayerApi.ShowCaption("\u767e\u53d8\uff1a\u5316\u8eab\u5207\u6362\u5931\u8d25\u3002");
            return false;
        }

        PolymorphStateStore.SetPending(role, self.Self);
        if (BuffApi.Has(self.Self, TerriasIds.PolymorphTraitBuffId))
        {
            return Apply(ResolveBuffExecutor(self.Self, self));
        }

        try
        {
            self.SetStatus("Self");
            self.AddBuff(TerriasIds.PolymorphTraitBuffId, "1");
            return true;
        }
        catch (Exception ex)
        {
            PolymorphStateStore.ClearPending(self.Self);
            TerriasLog.Warn("[Polymorph] failed to grant trait buff: " + ex.Message);
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

        var owner = self.Self;
        var role = PolymorphStateStore.PendingFor(owner);
        if (role == null)
        {
            TerriasLog.Warn("[Polymorph] trait buff applied without a pending role; removing inert buff.");
            RemoveTraitBuff(self);
            return false;
        }

        try
        {
            UpdateTraitBuffDescription(self, role);
            var execution = ResolveBuffExecutor(owner, self);
            execution.SetStatus("Self");
            RegisterLifecycle(execution);

            var previous = PolymorphStateStore.ActiveFor(owner);
            if (previous != null && PolymorphStateStore.IsRoleActiveFor(owner, role.Id))
            {
                PolymorphStateStore.ClearPending(owner);
                TerriasLog.Debug("[Polymorph] repeated request for the active form ignored: " + role.Id + ".");
                return true;
            }

            var targetCareer = CareerApi.Materialize(role.Id)
                ?? throw new InvalidOperationException("Target career config is unavailable: " + role.Id);

            if (previous != null)
            {
                PolymorphCooldownService.CaptureCurrentRole(owner, "PolymorphBuffService.BeforeChangeCareer");
            }

            var state = PolymorphStateStore.SetLocal(role, owner);
            if (previous == null)
            {
                PolymorphCooldownService.BeginSession(owner, state.SessionId);
                PolymorphRuntimeService.SuspendOriginalCareer(state, "PolymorphBuffService.Apply");
            }

            execution.ChangeCareer(role.Id);
            if (!CareerApi.CommitLocalCareer(owner, targetCareer, "PolymorphBuffService.Apply")
                || !CareerApi.IsCurrent(role.Id))
            {
                throw new InvalidOperationException("Target career did not commit locally: " + role.Id);
            }

            if (!PolymorphRuntimeService.Enter(execution, role, state, targetCareer))
            {
                throw new InvalidOperationException("Target career runtime failed to initialize: " + role.Id);
            }

            PolymorphNetworkSync.BroadcastEnter(state, "PolymorphBuffService.Apply");
            PolymorphStateStore.ClearPending(owner);
            PlayerApi.ShowCaption("\u767e\u53d8\uff1a\u5316\u8eab\u4e3a\u3010" + state.DisplayName + "\u3011\u3002");
            TerriasPerformanceCounters.Record("Polymorph.BuffApplied");
            return true;
        }
        catch (Exception ex)
        {
            var active = PolymorphStateStore.ActiveFor(owner);
            if (active != null)
            {
                PolymorphRuntimeService.ClearOwner(active.OwnerStatusId, "PolymorphBuffService.ApplyFailed");
            }

            PolymorphStateStore.ClearOwner(owner, "PolymorphBuffService.ApplyFailed");
            PolymorphCooldownService.Clear(owner);
            PolymorphStateStore.ClearPending(owner);
            TerriasLog.Warn("[Polymorph] trait apply failed: " + ex.Message);
            PlayerApi.ShowCaption("\u767e\u53d8\uff1a\u5316\u8eab\u5207\u6362\u5931\u8d25\u3002");
            RemoveTraitBuff(ResolveBuffExecutor(owner, self));
            return false;
        }
    }

    public static void Clear(ScriptExecutor self)
    {
        var owner = self?.Self;
        try
        {
            ExecutorApi.ClearHook(self, HookKey, TokenKey);
            var active = PolymorphStateStore.ActiveFor(owner);
            if (active != null)
            {
                PolymorphRuntimeService.ClearOwner(active.OwnerStatusId, "PolymorphBuffService.Clear");
            }

            PolymorphStateStore.ClearOwner(owner, "PolymorphBuffService.Clear");
            PolymorphCooldownService.Clear(owner);
            PolymorphStateStore.ClearPending(owner);
            TerriasPerformanceCounters.Record("Polymorph.BuffCleared");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[Polymorph] trait clear failed: " + ex.Message);
        }
    }

    public static void UpdateTraitBuffDescription(ScriptExecutor self, PolymorphRoleSpec role)
    {
        if (self == null || role == null)
        {
            return;
        }

        var presentation = BuildTraitPresentation(role);
        foreach (var field in presentation)
        {
            DictionaryUtil.Set(self.Vars, field.Key, field.Value);
        }

        var buff = self.Self?.GetBuff(TerriasIds.PolymorphTraitBuffId);
        if (buff != null)
        {
            BuffApi.ApplyRuntimePresentation(buff, presentation);
        }
    }

    private static void RegisterLifecycle(ScriptExecutor self)
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

        ExecutorApi.TryAddTokenedEvent(self, "EndRound", TokenKey, token, new Action(() =>
        {
            if (ExecutorApi.IsHookTokenActive(self, TokenKey, token))
            {
                ObserveNativeDurationDecay(self);
            }
        }), "polymorph_trait");
    }

    private static void ObserveNativeDurationDecay(ScriptExecutor self)
    {
        var owner = self?.Self;
        var state = PolymorphStateStore.ActiveFor(owner);
        var before = BuffApi.Level(owner, TerriasIds.PolymorphTraitBuffId);
        var reduce = BuffApi.ReducePerTurn(owner, TerriasIds.PolymorphTraitBuffId);
        var observedBuff = owner?.GetBuff(TerriasIds.PolymorphTraitBuffId);
        if (owner == null || state == null || observedBuff == null || before <= 0 || reduce <= 0)
        {
            return;
        }

        var sessionId = state.SessionId;
        var key = "Polymorph.DurationFallback." + state.OwnerStatusId + "." + sessionId;
        TerriasFrameDispatcher.RunOnceNextFrame(key, () =>
        {
            if (!PolymorphStateStore.IsCurrentSession(owner, sessionId))
            {
                return;
            }

            var currentBuff = owner.GetBuff(TerriasIds.PolymorphTraitBuffId);
            if (currentBuff == null || !ReferenceEquals(currentBuff, observedBuff))
            {
                return;
            }

            var after = BuffApi.Level(owner, TerriasIds.PolymorphTraitBuffId);
            if (after < before)
            {
                return;
            }

            BuffApi.SetExactLevel(owner, TerriasIds.PolymorphTraitBuffId, before - reduce);
            TerriasLog.Warn("[Polymorph] native turn decay was not observed; applied fallback: before="
                + before + ", reduce=" + reduce + ", after="
                + BuffApi.Level(owner, TerriasIds.PolymorphTraitBuffId) + ".");
        });
    }

    private static ScriptExecutor ResolveBuffExecutor(IStatusManager? owner, ScriptExecutor fallback)
    {
        var execution = BuffApi.Executor(owner, TerriasIds.PolymorphTraitBuffId) ?? fallback;
        execution.Self = owner;
        return execution;
    }

    private static void RemoveTraitBuff(ScriptExecutor self)
    {
        try
        {
            self.SetStatus("Self");
            self.RemoveBuff(TerriasIds.PolymorphTraitBuffId);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[Polymorph] inert trait buff remove failed: " + ex.Message);
        }
    }

    private static Dictionary<string, string> BuildTraitPresentation(PolymorphRoleSpec role)
    {
        var description = "\u53d8\u8eab\u6210\u4e3a\u3010" + role.DisplayName
            + "\u3011\u3002\u767e\u53d8\u7ed3\u675f\u65f6\u6062\u590d\u539f\u89d2\u8272\uff1b\u539f\u89d2\u8272\u7684\u804c\u4e1a\u72b6\u6001\u4e0e\u6280\u80fd\u51b7\u5374\u5728\u6b64\u671f\u95f4\u51bb\u7ed3\u3002";
        var traditionalDescription = "\u8b8a\u8eab\u6210\u70ba\u3010" + role.DisplayName
            + "\u3011\u3002\u767e\u8b8a\u7d50\u675f\u6642\u6062\u5fa9\u539f\u89d2\u8272\uff1b\u539f\u89d2\u8272\u7684\u8077\u696d\u72c0\u614b\u8207\u6280\u80fd\u51b7\u537b\u5728\u6b64\u671f\u9593\u51cd\u7d50\u3002";
        return new Dictionary<string, string>
        {
            ["Description"] = description,
            ["Description_zh-Hant"] = traditionalDescription,
            ["Description_ja"] = "\u3010" + role.DisplayName
                + "\u3011\u306b\u5909\u8eab\u3059\u308b\u3002\u767e\u5909\u306e\u7d42\u4e86\u6642\u306b\u5143\u306e\u5f79\u5272\u3078\u623b\u308a\u3001\u305d\u306e\u9593\u306f\u5143\u306e\u5f79\u5272\u306e\u72b6\u614b\u3068\u30b9\u30ad\u30eb\u518d\u4f7f\u7528\u6642\u9593\u304c\u51cd\u7d50\u3055\u308c\u308b\u3002",
            ["Description_en"] = "Polymorphed into " + role.DisplayName
                + ". Restore the original role when Polymorph ends; its career state and skill cooldowns stay frozen meanwhile."
        };
    }
}
