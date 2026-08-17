using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.GameApi;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;
using Witch.UI.Window;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Maps recorded presentation cues to visual-only APIs. It never creates or executes a
/// combat, network, script, or target command.
/// </summary>
internal static class MatchReplayPresentationDirector
{
    private const float CardPresentationMilliseconds = 640f;
    private static readonly List<ActiveCardVisual> CardVisuals = new();
    private static string lastActiveActorId = "";

    internal static void Reset()
    {
        foreach (var visual in CardVisuals)
        {
            if (visual.Card != null)
            {
                UnityEngine.Object.Destroy(visual.Card.gameObject);
            }
        }

        CardVisuals.Clear();
        MatchReplaySkillPresenter.ResetAnimation();
        MatchReplayEnemyIntentPresenter.Reset();
        lastActiveActorId = "";
        MatchReplayCardStateCapture.Reset();
        MatchReplayPassiveBuffPresenter.Reset();
        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi != null)
        {
            fightUi.animationQueue?.Clear();
            fightUi.NowAnimation = false;
        }

        var manager = FightManager.Instance;
        if (manager == null)
        {
            return;
        }

        MatchReplayNativePresentationApi.Reset(manager.statuses.Values.Where(item => item != null));

        manager.ActionQueue?.Clear();
    }

    internal static void Tick(float deltaMilliseconds)
    {
        var elapsed = Math.Max(0f, deltaMilliseconds);
        MatchReplayNativePresentationApi.Tick(elapsed);
        MatchReplaySkillPresenter.Tick(elapsed);
        MatchReplayEnemyIntentPresenter.Tick(elapsed);
        for (var index = CardVisuals.Count - 1; index >= 0; index--)
        {
            var visual = CardVisuals[index];
            if (visual.Card == null)
            {
                CardVisuals.RemoveAt(index);
                continue;
            }

            visual.ElapsedMilliseconds += elapsed;
            var progress = Mathf.Clamp01(visual.ElapsedMilliseconds / CardPresentationMilliseconds);
            if (progress < 0.28f)
            {
                var phase = EaseOutCubic(progress / 0.28f);
                visual.Card.transform.position = Vector3.LerpUnclamped(visual.StartPosition, visual.CenterPosition, phase);
                visual.Card.transform.localScale = Vector3.LerpUnclamped(
                    visual.StartScale,
                    Vector3.one * 0.78f,
                    phase);
                visual.Card.transform.rotation = Quaternion.SlerpUnclamped(
                    visual.StartRotation,
                    Quaternion.identity,
                    phase);
            }
            else if (progress < 0.58f)
            {
                visual.Card.transform.position = visual.CenterPosition;
                visual.Card.transform.localScale = Vector3.one * 0.78f;
                visual.Card.transform.rotation = Quaternion.identity;
            }
            else
            {
                var phase = EaseInCubic((progress - 0.58f) / 0.42f);
                visual.Card.transform.position = Vector3.LerpUnclamped(visual.CenterPosition, visual.TargetPosition, phase);
                visual.Card.transform.localScale = Vector3.LerpUnclamped(
                    Vector3.one * 0.78f,
                    Vector3.one * 0.12f,
                    phase);
            }

            if (progress >= 1f)
            {
                UnityEngine.Object.Destroy(visual.Card.gameObject);
                CardVisuals.RemoveAt(index);
            }
        }

    }

    internal static void ShowTurn(int turnIndex, string activeActorId)
    {
        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        var manager = FightManager.Instance;
        if (fightUi == null || manager == null)
        {
            return;
        }

        StatusManager? status = null;
        if (!string.IsNullOrWhiteSpace(activeActorId))
        {
            manager.statuses.TryGetValue(activeActorId, out status);
        }

        status ??= FightPlayer.Instance?.Status as StatusManager;
        if (status?.fatherObject != null)
        {
            if (string.Equals(lastActiveActorId, status.InstanceId, StringComparison.Ordinal))
            {
                return;
            }

            lastActiveActorId = status.InstanceId ?? "";
            fightUi.SetTurn(status.fatherObject, 0, 1);
        }
    }

    internal static void PlayAction(MatchReplayActionFrame frame)
    {
        if (frame == null)
        {
            return;
        }

        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi == null)
        {
            return;
        }

        ShowTurn(frame.TurnIndex, frame.ActorId);
        var cues = frame.Presentation ?? new List<MatchReplayPresentationCue>();
        if (string.Equals(frame.Kind, MatchReplayActionKinds.EnemyIntentUse, StringComparison.Ordinal)
            || cues.Any(cue => cue.Kind == MatchReplayPresentationCueKinds.EnemyIntent))
        {
            MatchReplayEnemyIntentPresenter.Play(frame);
        }
        else if (string.Equals(frame.Kind, MatchReplayActionKinds.SkillUse, StringComparison.Ordinal)
            || cues.Any(cue => cue.Kind == MatchReplayPresentationCueKinds.SkillUse))
        {
            MatchReplaySkillPresenter.Play(frame);
        }
        else if (string.Equals(frame.Kind, MatchReplayActionKinds.CardUse, StringComparison.Ordinal)
                 || cues.Any(cue => cue.Kind == MatchReplayPresentationCueKinds.CardUse))
        {
            PlayCardVisual(fightUi, frame);
        }

        PlayStatusAnimation(frame);
    }

    private static void PlayCardVisual(FightUI fightUi, MatchReplayActionFrame frame)
    {
        if (frame.SourcePresentation == null)
        {
            return;
        }

        try
        {
            var card = MatchReplayCardStateCapture.TakeSourceForPresentation(frame);
            if (card == null)
            {
                return;
            }

            var center = fightUi.transform.Find("CenterCardContainer") ?? fightUi.transform;
            var startPosition = card.transform.position;
            var startRotation = card.transform.rotation;
            var startScale = card.transform.localScale;
            card.transform.SetParent(center, worldPositionStays: true);
            var transition = (frame.CardTransitions ?? new List<MatchReplayCardTransition>()).FirstOrDefault(item =>
                string.Equals(item.ReplayCardId, frame.SourceInstanceId, StringComparison.Ordinal));
            var target = ResolveCardTarget(fightUi, transition);
            CardVisuals.Add(new ActiveCardVisual
            {
                Card = card,
                StartPosition = startPosition,
                StartRotation = startRotation,
                StartScale = startScale,
                CenterPosition = center.position,
                TargetPosition = target.position
            });
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] replay card presentation skipped: " + ex.Message);
        }
    }

    private static Transform ResolveCardTarget(FightUI fightUi, MatchReplayCardTransition? transition)
    {
        if (transition != null
            && string.Equals(transition.Disposition, MatchReplayCardDispositionKinds.Discard, StringComparison.Ordinal)
            && fightUi.UsedCardList != null)
        {
            return fightUi.UsedCardList;
        }

        if (transition != null
            && string.Equals(transition.Disposition, MatchReplayCardDispositionKinds.Burn, StringComparison.Ordinal))
        {
            return fightUi.transform.Find("ClockBoard/牌库") ?? fightUi.transform.Find("Left/Card") ?? fightUi.transform;
        }

        return fightUi.transform.Find("Left/Card") ?? fightUi.UsedCardList ?? fightUi.transform;
    }

    private static void PlayStatusAnimation(MatchReplayActionFrame frame)
    {
        var manager = FightManager.Instance;
        if (manager == null || string.IsNullOrWhiteSpace(frame.ActorId)
                            || !manager.statuses.TryGetValue(frame.ActorId, out var actor)
                            || actor == null)
        {
            return;
        }

        var actionCue = (frame.Presentation ?? new List<MatchReplayPresentationCue>()).FirstOrDefault(item =>
            item.Kind == MatchReplayPresentationCueKinds.ActorAction);
        var actionState = ParseAnimation(FirstNonBlank(
            frame.NativePresentation?.ActorAnimationState,
            actionCue?.AnimationState,
            frame.IntentPresentation?.ActionState,
            Value(frame.SourcePresentation?.Data, "Action")));
        var targetStates = ResolveTargetStates(frame);
        var targets = new List<MatchReplayNativeAnimationTarget>();
        foreach (var targetState in targetStates)
        {
            if (manager.statuses.TryGetValue(targetState.TargetId, out var target)
                && target != null
                && target != actor)
            {
                targets.Add(new MatchReplayNativeAnimationTarget
                {
                    Status = target,
                    AnimationState = ParseAnimation(targetState.AnimationState)
                });
            }
        }

        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi == null)
        {
            return;
        }

        if (!MatchReplayNativePresentationApi.TryPlay(
                fightUi,
                actor,
                actionState,
                targets,
                FirstNonBlank(
                    frame.NativePresentation?.EffectName,
                    frame.IntentPresentation?.EffectName,
                    Value(frame.SourcePresentation?.Data, "Effects")),
                frame.NativePresentation?.EffectDelayMilliseconds ?? 50,
                out var detail))
        {
            actor.animatedState = actionState;
            foreach (var target in targets)
            {
                target.Status.animatedState = target.AnimationState;
            }

            AuraToolsLog.Warn("[MatchRecords] native action presentation degraded: " + detail);
        }
    }

    private static List<MatchReplayTargetPresentationState> ResolveTargetStates(MatchReplayActionFrame frame)
    {
        if (frame.NativePresentation != null)
        {
            return (frame.NativePresentation.Targets ?? new List<MatchReplayTargetPresentationState>())
                .Where(item => !string.IsNullOrWhiteSpace(item.TargetId))
                .GroupBy(item => item.TargetId, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToList();
        }

        var reactions = new Dictionary<string, IStatusManager.AnimatedState>(StringComparer.Ordinal);
        foreach (var cue in frame.Presentation ?? new List<MatchReplayPresentationCue>())
        {
            var reaction = cue.Kind == MatchReplayPresentationCueKinds.Defend
                ? IStatusManager.AnimatedState.Defend
                : cue.Kind == MatchReplayPresentationCueKinds.Damage
                    ? IStatusManager.AnimatedState.Hit
                    : IStatusManager.AnimatedState.Idle;
            foreach (var targetId in cue.TargetIds ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(targetId))
                {
                    continue;
                }

                if (!reactions.TryGetValue(targetId, out var current)
                    || current == IStatusManager.AnimatedState.Idle
                    || reaction == IStatusManager.AnimatedState.Defend)
                {
                    reactions[targetId] = reaction;
                }
            }
        }

        return reactions.Select(item => new MatchReplayTargetPresentationState
        {
            TargetId = item.Key,
            AnimationState = item.Value.ToString()
        }).ToList();
    }

    private static IStatusManager.AnimatedState ParseAnimation(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Enum.TryParse(value, ignoreCase: true, out IStatusManager.AnimatedState parsed))
        {
            return parsed;
        }

        return IStatusManager.AnimatedState.Idle;
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private static string Value(IEnumerable<MatchReplayStringValue>? values, string key)
    {
        return values?.LastOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal))?.Value ?? "";
    }

    private static float EaseOutCubic(float value)
    {
        value = Mathf.Clamp01(value);
        return 1f - Mathf.Pow(1f - value, 3f);
    }

    private static float EaseInCubic(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * value;
    }

    private sealed class ActiveCardVisual
    {
        internal CardItem? Card { get; set; }
        internal Vector3 StartPosition { get; set; }
        internal Quaternion StartRotation { get; set; }
        internal Vector3 StartScale { get; set; }
        internal Vector3 CenterPosition { get; set; }
        internal Vector3 TargetPosition { get; set; }
        internal float ElapsedMilliseconds { get; set; }
    }

}
