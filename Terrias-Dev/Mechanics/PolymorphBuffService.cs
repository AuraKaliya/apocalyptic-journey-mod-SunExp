using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class PolymorphBuffService
{
    private const string RegistrationId = "Buff.PolymorphTrait";

    public static bool GrantForRole(ScriptExecutor self, PolymorphRoleSpec role)
    {
        if (self?.Self == null || role == null)
        {
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.polymorph.switch_failed"));
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
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.polymorph.switch_failed"));
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
            PlayerApi.ShowCaption(TerriasTextCatalog.Format("caption.polymorph.applied", "name", role.DisplayName));
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
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.polymorph.switch_failed"));
            RemoveTraitBuff(ResolveBuffExecutor(owner, self));
            return false;
        }
    }

    public static void Clear(ScriptExecutor self)
    {
        var owner = self?.Self;
        try
        {
            ScriptEventApi.InvalidateFightScope(self, RegistrationId);
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
        using var scope = ScriptEventApi.BeginFightScope(self, RegistrationId);
        if (scope == null)
        {
            return;
        }

        scope.AddRequired("StartRound", new Action(() =>
        {
            PolymorphCooldownService.TickRound(self, "PolymorphBuffService.StartRound");
        }), "polymorph_trait");

        scope.AddRequired("EndRound", new Action(() =>
        {
            ObserveNativeDurationDecay(self);
        }), "polymorph_trait");
        scope.Commit();
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
        var result = new Dictionary<string, string>();
        var arguments = new Dictionary<string, string>();
        foreach (var locale in TerriasLocale.Supported)
        {
            arguments["name"] = role.DisplayNameFor(locale);
            result[TerriasLocale.FieldName("Description", locale)] =
                TerriasTextCatalog.GetForLocale("buff.polymorph.description", locale, arguments);
        }

        return result;
    }
}
