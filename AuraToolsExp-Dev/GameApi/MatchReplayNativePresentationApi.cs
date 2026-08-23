using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using DG.Tweening;
using Witch.Core;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.GameApi;

internal sealed class MatchReplayNativeAnimationTarget
{
    internal StatusManager Status { get; set; } = null!;

    internal IStatusManager.AnimatedState AnimationState { get; set; }
}

/// <summary>
/// Reuses the native visual animation queue without invoking ScriptExecutor, RPC,
/// FightManager event enqueueing, target selection, or combat commands.
/// </summary>
internal static class MatchReplayNativePresentationApi
{
    private static readonly List<PendingVisualEffect> PendingEffects = new();

    internal static bool TryPlay(
        FightUI fightUi,
        StatusManager actor,
        IStatusManager.AnimatedState actorState,
        IReadOnlyList<MatchReplayNativeAnimationTarget> targets,
        string effectName,
        int effectDelayMilliseconds,
        out string detail)
    {
        detail = "";
        if (fightUi == null || actor == null)
        {
            detail = "native fight presentation is unavailable";
            return false;
        }

        try
        {
            var orderedTargets = (targets ?? Array.Empty<MatchReplayNativeAnimationTarget>())
                .Where(item => item?.Status != null && item.Status != actor)
                .GroupBy(item => item.Status.InstanceId ?? "", StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            var statuses = new List<StatusManager> { actor };
            statuses.AddRange(orderedTargets.Select(item => item.Status));
            var states = new List<IStatusManager.AnimatedState> { actorState };
            states.AddRange(orderedTargets.Select(item => item.AnimationState));

            var delay = Math.Max(0, Math.Min(500, effectDelayMilliseconds));
            if (delay == 0)
            {
                PlayVisualEffects(actor, orderedTargets, actorState, effectName);
            }
            else
            {
                PendingEffects.Add(new PendingVisualEffect
                {
                    Actor = actor,
                    Targets = orderedTargets,
                    ActorState = actorState,
                    EffectName = effectName ?? "",
                    RemainingMilliseconds = delay
                });
            }

            fightUi.animationQueue.Enqueue(new FightUI.AnimationData
            {
                status = statuses.ToArray(),
                animationState = states.ToArray(),
                effectName = effectName ?? ""
            });
            fightUi.DOActionAnimation();
            return true;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    internal static void Reset(IEnumerable<StatusManager>? statuses)
    {
        PendingEffects.Clear();
        foreach (var status in statuses ?? Enumerable.Empty<StatusManager>())
        {
            if (status == null)
            {
                continue;
            }

            try
            {
                DOTween.Kill(status.transform);
                var body = status.transform.Find("body");
                if (body != null)
                {
                    DOTween.Kill(body);
                }
                status.transform.position = status.initPos;
                status.UpdateObjPos();
                status.animatedState = IStatusManager.AnimatedState.Idle;
            }
            catch (Exception ex)
            {
                AuraToolsLog.Debug("[MatchRecords] native action reset degraded: " + ex.Message);
            }
        }
    }

    internal static void Tick(float deltaMilliseconds)
    {
        var elapsed = Math.Max(0f, deltaMilliseconds);
        for (var index = PendingEffects.Count - 1; index >= 0; index--)
        {
            var pending = PendingEffects[index];
            pending.RemainingMilliseconds -= elapsed;
            if (pending.RemainingMilliseconds > 0f)
            {
                continue;
            }

            PendingEffects.RemoveAt(index);
            if (pending.Actor == null)
            {
                continue;
            }

            try
            {
                PlayVisualEffects(
                    pending.Actor,
                    pending.Targets.Where(item => item?.Status != null).ToList(),
                    pending.ActorState,
                    pending.EffectName);
            }
            catch (Exception ex)
            {
                AuraToolsLog.Debug("[MatchRecords] delayed native effect degraded: " + ex.Message);
            }
        }
    }

    private static void PlayVisualEffects(
        StatusManager actor,
        IReadOnlyList<MatchReplayNativeAnimationTarget> targets,
        IStatusManager.AnimatedState actorState,
        string effectName)
    {
        var effectManager = ISingleton<IEffectManager>.Instance;
        if (effectManager == null)
        {
            return;
        }

        var targetStatuses = targets
            .Select(item => (IStatusManager)item.Status)
            .ToList();
        if (!string.IsNullOrWhiteSpace(effectName))
        {
            effectManager.InternalPlayEffect(actor, targetStatuses, effectName);
            return;
        }

        if (actorState is not (IStatusManager.AnimatedState.Attack or IStatusManager.AnimatedState.Skill)
            || actor.fatherObject == null)
        {
            return;
        }

        effectManager.InternalPlayEffect(
            actor,
            targetStatuses,
            actor.fatherObject.GetRoleEffectName(actorState));
        effectManager.InternalPlayEffect(
            actor,
            targetStatuses,
            actor.fatherObject.GetRoleEffectName(IStatusManager.AnimatedState.Hit));
    }

    private sealed class PendingVisualEffect
    {
        internal StatusManager Actor { get; set; } = null!;

        internal List<MatchReplayNativeAnimationTarget> Targets { get; set; } = new();

        internal IStatusManager.AnimatedState ActorState { get; set; }

        internal string EffectName { get; set; } = "";

        internal float RemainingMilliseconds { get; set; }
    }
}
