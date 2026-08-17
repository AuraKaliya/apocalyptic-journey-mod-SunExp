using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraToolsExp.Dll.Features.MatchRecords.Model;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

/// <summary>
/// Captures the visual inputs immediately before the native FightUI animation consumes
/// pending hit reactions. No script, target selection, or combat command is executed here.
/// </summary>
internal static class MatchReplayActionPresentationCapture
{
    private const BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? HasPendingHitReaction =
        typeof(StatusManager).GetField("hasPendingHitReaction", InstanceFields);
    private static readonly FieldInfo? PendingHitReactionFullyDefended =
        typeof(StatusManager).GetField("pendingHitReactionFullyDefended", InstanceFields);

    internal static MatchReplayActionPresentationState? Capture(IScriptExecutor? executor)
    {
        if (executor?.Self is not StatusManager actor)
        {
            return null;
        }

        var result = new MatchReplayActionPresentationState
        {
            ActorAnimationState = Read(executor.dataConfig?.data, "Action"),
            EffectName = Read(executor.dataConfig?.data, "Effects"),
            EffectDelayMilliseconds = 50,
            PresentationDurationMilliseconds = 1040
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in (executor.Object ?? new List<IStatusManager>())
                     .OfType<StatusManager>())
        {
            var targetId = target.InstanceId ?? "";
            if (string.IsNullOrWhiteSpace(targetId)
                || string.Equals(targetId, actor.InstanceId, StringComparison.Ordinal)
                || !seen.Add(targetId))
            {
                continue;
            }

            var mayReact = actor.fatherObject is not FightPlayer
                           || target.fatherObject is not OtherPlayer;
            result.Targets.Add(new MatchReplayTargetPresentationState
            {
                TargetId = targetId,
                AnimationState = mayReact
                    ? PeekHitReaction(target).ToString()
                    : IStatusManager.AnimatedState.Idle.ToString()
            });
        }

        return result;
    }

    private static IStatusManager.AnimatedState PeekHitReaction(StatusManager target)
    {
        try
        {
            if (HasPendingHitReaction?.GetValue(target) is not true)
            {
                return IStatusManager.AnimatedState.Idle;
            }

            return PendingHitReactionFullyDefended?.GetValue(target) is true
                ? IStatusManager.AnimatedState.Defend
                : IStatusManager.AnimatedState.Hit;
        }
        catch
        {
            return target.animatedState is IStatusManager.AnimatedState.Hit
                or IStatusManager.AnimatedState.Defend
                ? target.animatedState
                : IStatusManager.AnimatedState.Idle;
        }
    }

    private static string Read(IDictionary<string, string>? values, string key)
    {
        return values != null && values.TryGetValue(key, out var value) ? value ?? "" : "";
    }
}
