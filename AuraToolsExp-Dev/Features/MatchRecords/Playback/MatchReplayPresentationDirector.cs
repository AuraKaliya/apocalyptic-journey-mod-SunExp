using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
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
    private const float StatusPresentationMilliseconds = 880f;
    private const float OutcomeStartMilliseconds = 180f;
    private const float OutcomeEndMilliseconds = 500f;
    private static readonly List<ActiveCardVisual> CardVisuals = new();
    private static readonly List<ActiveStatusVisual> StatusVisuals = new();
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
        foreach (var visual in StatusVisuals)
        {
            ResetVisual(visual);
        }

        StatusVisuals.Clear();
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

        foreach (var status in manager.statuses.Values.Where(item => item != null))
        {
            ResetStatus(status);
        }

        manager.ActionQueue?.Clear();
    }

    internal static void Tick(float deltaMilliseconds)
    {
        var elapsed = Math.Max(0f, deltaMilliseconds);
        MatchReplayCardStateCapture.Tick(elapsed);
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

        for (var index = StatusVisuals.Count - 1; index >= 0; index--)
        {
            var visual = StatusVisuals[index];
            visual.ElapsedMilliseconds += elapsed;
            if (visual.Status == null)
            {
                StatusVisuals.RemoveAt(index);
                continue;
            }

            if (visual.HasHitOutcome
                && !visual.OutcomeStateSet
                && visual.ElapsedMilliseconds >= OutcomeStartMilliseconds)
            {
                visual.Status.animatedState = IStatusManager.AnimatedState.Hit;
                visual.OutcomeStateSet = true;
            }

            if (visual.IsActor)
            {
                var progress = Mathf.Clamp01(visual.ElapsedMilliseconds / StatusPresentationMilliseconds);
                if (progress < 0.22f)
                {
                    visual.Status.transform.position = Vector3.LerpUnclamped(
                        visual.StartPosition,
                        visual.PeakPosition,
                        EaseOutCubic(progress / 0.22f));
                }
                else if (progress < 0.58f)
                {
                    visual.Status.transform.position = visual.PeakPosition;
                }
                else
                {
                    visual.Status.transform.position = Vector3.LerpUnclamped(
                        visual.PeakPosition,
                        visual.StartPosition,
                        EaseOutCubic((progress - 0.58f) / 0.42f));
                }
            }
            else if (visual.ElapsedMilliseconds >= OutcomeStartMilliseconds
                     && visual.ElapsedMilliseconds < OutcomeEndMilliseconds)
            {
                var progress = (visual.ElapsedMilliseconds - OutcomeStartMilliseconds)
                               / (OutcomeEndMilliseconds - OutcomeStartMilliseconds);
                var amplitude = (1f - progress) * 0.34f;
                visual.Status.transform.position = visual.StartPosition + new Vector3(
                    Mathf.Sin(visual.ElapsedMilliseconds * 0.085f) * amplitude,
                    Mathf.Sin(visual.ElapsedMilliseconds * 0.137f) * amplitude * 0.18f,
                    0f);
            }
            else
            {
                visual.Status.transform.position = visual.StartPosition;
            }

            visual.Status.UpdateObjPos();
            if (visual.ElapsedMilliseconds >= StatusPresentationMilliseconds)
            {
                ResetVisual(visual);
                StatusVisuals.RemoveAt(index);
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
        PlayCardVisual(fightUi, frame);
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

        var targetIds = frame.Presentation
            .Where(cue => cue.Kind == MatchReplayPresentationCueKinds.Damage)
            .SelectMany(cue => cue.TargetIds ?? new List<string>())
            .Concat(frame.Semantics
                .Where(item => item.Category == MatchSemanticCategories.Damage)
                .Select(item => item.TargetInstanceId))
            .Concat(frame.Semantics
                .Where(item => item.Category == MatchSemanticCategories.Damage)
                .Select(item => item.TargetId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var actionCue = frame.Presentation.FirstOrDefault(item =>
            item.Kind == MatchReplayPresentationCueKinds.ActorAction);
        var actionState = ParseAnimation(actionCue?.AnimationState, frame.Kind);
        var targets = new List<StatusManager>();
        var actorIsTarget = targetIds.Any(targetId =>
            string.Equals(targetId, actor.InstanceId, StringComparison.Ordinal));
        foreach (var targetId in targetIds)
        {
            if (manager.statuses.TryGetValue(targetId, out var target)
                && target != null
                && target != actor
                && !targets.Contains(target))
            {
                targets.Add(target);
            }
        }

        RemoveStatusVisual(actor);
        var actorStart = actor.transform.position;
        var actorDirection = targets.Count == 0
            ? new Vector3(actorStart.x <= 0f ? 1f : -1f, 0f, 0f)
            : targets.Aggregate(Vector3.zero, (sum, target) => sum + target.transform.position) / targets.Count
              - actorStart;
        actorDirection.y *= 0.15f;
        actorDirection.z = 0f;
        if (actorDirection.sqrMagnitude < 0.001f)
        {
            actorDirection = Vector3.right;
        }

        actor.animatedState = actionState;
        StatusVisuals.Add(new ActiveStatusVisual
        {
            Status = actor,
            IsActor = true,
            HasHitOutcome = actorIsTarget,
            StartPosition = actorStart,
            PeakPosition = actorStart + actorDirection.normalized * 0.78f
        });
        foreach (var target in targets)
        {
            RemoveStatusVisual(target);
            StatusVisuals.Add(new ActiveStatusVisual
            {
                Status = target,
                HasHitOutcome = true,
                StartPosition = target.transform.position,
                PeakPosition = target.transform.position
            });
        }
    }

    private static IStatusManager.AnimatedState ParseAnimation(string? value, string actionKind)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Enum.TryParse(value, ignoreCase: true, out IStatusManager.AnimatedState parsed))
        {
            return parsed;
        }

        return string.Equals(actionKind, "SkillUse", StringComparison.Ordinal)
            ? IStatusManager.AnimatedState.Skill
            : IStatusManager.AnimatedState.Attack;
    }

    private static void ResetStatus(StatusManager? status)
    {
        if (status != null)
        {
            status.transform.position = status.initPos;
            status.UpdateObjPos();
            status.animatedState = IStatusManager.AnimatedState.Idle;
        }
    }

    private static void RemoveStatusVisual(StatusManager status)
    {
        for (var index = StatusVisuals.Count - 1; index >= 0; index--)
        {
            if (StatusVisuals[index].Status != status)
            {
                continue;
            }

            ResetVisual(StatusVisuals[index]);
            StatusVisuals.RemoveAt(index);
        }
    }

    private static void ResetVisual(ActiveStatusVisual visual)
    {
        if (visual.Status == null)
        {
            return;
        }

        visual.Status.transform.position = visual.StartPosition;
        visual.Status.UpdateObjPos();
        visual.Status.animatedState = IStatusManager.AnimatedState.Idle;
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

    private sealed class ActiveStatusVisual
    {
        internal StatusManager? Status { get; set; }
        internal bool IsActor { get; set; }
        internal bool HasHitOutcome { get; set; }
        internal bool OutcomeStateSet { get; set; }
        internal Vector3 StartPosition { get; set; }
        internal Vector3 PeakPosition { get; set; }
        internal float ElapsedMilliseconds { get; set; }
    }
}
