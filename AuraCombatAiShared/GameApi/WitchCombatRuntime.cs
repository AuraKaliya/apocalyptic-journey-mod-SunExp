using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using AuraDecision.Shared;
using Michsky.MUIP;
using UnityEngine;
using UnityEngine.EventSystems;
using Witch.Core;
using Witch.UI.Window;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraCombatAi.Shared.GameApi;

public sealed class WitchCombatRuntime :
    ICombatObservationProvider,
    IPlayerCombatObservationProvider,
    ICombatActionExecutor
{
    private static readonly BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? SkillItemsField = typeof(FightUI).GetField("skillItems", InstanceFlags);
    private static readonly FieldInfo? AttackHitEnemyField = typeof(AttackCardItem).GetField("hitEnemy", InstanceFlags);
    private static readonly FieldInfo? AttackIsLineField = typeof(AttackCardItem).GetField("<isLine>k__BackingField", InstanceFlags);
    private static readonly FieldInfo? SkillHitEnemyField = typeof(SkillItem).GetField("hitEnemy", InstanceFlags);
    private static readonly object ReflectionGate = new();
    private static readonly Dictionary<string, MemberInfo?> MemberCache = new(StringComparer.Ordinal);
    private static long sequence;
    private CombatExecutionContext executionContext = new();
    private long trackedDeckBattleSessionId;
    private int trackedDrawPileCount = -1;
    private int trackedDiscardPileCount = -1;
    private int shuffleEpoch;
    private long trackedMechanicBattleSessionId;
    private int resurrectionCountBaseline;

    public bool TryCapture(out CombatStateObservation observation, out string reason)
    {
        observation = new CombatStateObservation();
        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi == null || !fightUi.gameObject.activeInHierarchy)
        {
            reason = "FightUI is unavailable";
            return false;
        }

        var player = FightPlayer.Instance;
        if (player?.Status == null)
        {
            reason = "local fight player is unavailable";
            return false;
        }

        observation.BattleSessionId = AuraShared.Core.AuraBattleLifecycleRouter.CurrentBattleSessionId;
        observation.Sequence = ++sequence;
        observation.ObservationId = CombatPlayerObservationBoundary.BuildObservationId(
            observation.BattleSessionId,
            observation.Sequence);
        var capturedExecutionContext = new CombatExecutionContext
        {
            ObservationId = observation.ObservationId
        };
        if (player.Status is not StatusManager playerStatus)
        {
            reason = "local player status type is unsupported";
            return false;
        }

        observation.Player = ObserveUnit(playerStatus, CombatTargetKind.Self, 1);
        capturedExecutionContext.BindActor(observation.Player.RuntimeId, playerStatus);
        observation.CurrentPower = player.CurPowerCount;
        observation.MaxPower = player.MaxPowerCount;
        observation.HandCount = FightUI.cardItemList?.Count ?? 0;
        observation.UiBusy = IsUiBusy(fightUi);
        observation.IsPlayerActionWindow = IsPlayerActionWindow(fightUi);
        AddEnemiesAndNativeThreat(observation, capturedExecutionContext);
        AddCards(observation, fightUi, capturedExecutionContext);
        AddSkills(observation, fightUi, capturedExecutionContext);
        ObserveDeck(observation);
        ObservePublicMechanicState(playerStatus, observation);
        if (CombatAiRegistry.TryResolveThreat(observation, out var providedThreat))
        {
            observation.Threat = providedThreat;
        }
        NormalizeThreat(observation);
        AddBoundAction(
            observation,
            capturedExecutionContext,
            new CombatActionObservation
        {
            CandidateId = "end-turn",
            SourceId = "end-turn",
            DisplayName = "结束回合",
            Kind = CombatActionKind.EndTurn,
            RuntimeId = 9000,
            Legal = observation.IsPlayerActionWindow,
        },
            fightUi.turnButton,
            null);
        observation = CombatPlayerObservationBoundary.Normalize(observation);
        executionContext = capturedExecutionContext;
        reason = "";
        return true;
    }

    public bool TryCapturePlayerObservation(
        out PlayerCombatObservation observation,
        out string reason)
    {
        if (!TryCapture(out var state, out reason))
        {
            observation = new PlayerCombatObservation();
            return false;
        }
        observation = new PlayerCombatObservation
        {
            ObservationId = state.ObservationId,
            State = state
        };
        return true;
    }

    public CombatExecutionResult Execute(CombatActionObservation action)
    {
        if (action == null)
        {
            return CombatExecutionResult.Rejected("action is null");
        }

        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi == null || !IsPlayerActionWindow(fightUi))
        {
            return CombatExecutionResult.Rejected("player action window is not stable");
        }
        if (!executionContext.TryResolve(action, out var binding))
        {
            return CombatExecutionResult.Rejected("action token is stale");
        }

        try
        {
            switch (action.Kind)
            {
                case CombatActionKind.EndTurn:
                    if (fightUi.turnButton == null || !fightUi.turnButton.isInteractable)
                    {
                        return CombatExecutionResult.Rejected("end-turn button is unavailable");
                    }

                    fightUi.turnButton.onClick.Invoke();
                    return CombatExecutionResult.Success("end turn");

                case CombatActionKind.PlayCard:
                    return ExecuteCard(action, binding, fightUi);

                case CombatActionKind.UseSkill:
                    return ExecuteSkill(action, binding);

                default:
                    return CombatExecutionResult.Rejected("unsupported action kind");
            }
        }
        catch (Exception ex)
        {
            return CombatExecutionResult.Rejected("execution failed: " + ex.Message);
        }
    }

    public static bool IsPlayerActionWindow(FightUI fightUi)
    {
        return fightUi != null
               && fightUi.gameObject.activeInHierarchy
               && FightManager.Instance != null
               && FightManager.Instance.fightType == FightType.Player
               && fightUi.turnButton != null
               && fightUi.turnButton.gameObject.activeInHierarchy
               && fightUi.turnButton.isInteractable
               && CardItem.canUse
               && !FightUI.InIEn
               && !fightUi.NowAnimation
               && fightUi.createCardQueue.Count == 0;
    }

    public static bool IsUiBusy(FightUI fightUi)
    {
        return fightUi == null
               || FightUI.InIEn
               || fightUi.NowAnimation
               || fightUi.createCardQueue.Count > 0
               || WitchCombatInteractionRuntime.HasActivePrompt;
    }

    private static CombatExecutionResult ExecuteCard(
        CombatActionObservation action,
        CombatExecutionBinding binding,
        FightUI fightUi)
    {
        if (binding.SourceHandle is not CommonCardItem card
            || card == null
            || card.gameObject == null)
        {
            return CombatExecutionResult.Rejected("card handle is stale");
        }

        if (!TryPreflightCard(card, fightUi, out var reason))
        {
            return CombatExecutionResult.Rejected(reason);
        }

        var target = binding.TargetHandle as StatusManager;
        if (card is AttackCardItem attack)
        {
            if (!IsCurrentTarget(action, target))
            {
                return CombatExecutionResult.Rejected("targeted card target is stale or defeated");
            }

            attack.scriptExecutor.Target = target;
            attack.scriptExecutor.Object.Clear();
            attack.scriptExecutor.Object.Add(target);
            AttackHitEnemyField?.SetValue(attack, target);
            AttackIsLineField?.SetValue(attack, false);
            WitchUiManager.Instance?.GetUI<LineUI>("LineUI")?.Hide();
            Cursor.visible = true;
            attack.TrueUse();
            return CombatExecutionResult.Success("played targeted card");
        }

        if (target != null && card.dataConfig?.scriptExecutor != null)
        {
            card.dataConfig.scriptExecutor.Target = target;
        }

        card.TrueUse();
        return CombatExecutionResult.Success("played card");
    }

    private static CombatExecutionResult ExecuteSkill(
        CombatActionObservation action,
        CombatExecutionBinding binding)
    {
        if (binding.SourceHandle is not SkillItem skill
            || skill == null
            || skill.gameObject == null)
        {
            return CombatExecutionResult.Rejected("skill handle is stale");
        }

        if (!TryPreflightSkill(skill, out var reason))
        {
            return CombatExecutionResult.Rejected(reason);
        }

        var target = binding.TargetHandle as StatusManager;
        if (!IsUntargetedSkill(skill) && target == null)
        {
            return CombatExecutionResult.Rejected("targeted skill has no target");
        }
        if (target != null && !IsCurrentTarget(action, target))
        {
            return CombatExecutionResult.Rejected("skill target is stale or defeated");
        }

        SkillHitEnemyField?.SetValue(skill, target);
        skill.TrueUse();
        return CombatExecutionResult.Success("used skill");
    }

    private static void AddCards(
        CombatStateObservation state,
        FightUI fightUi,
        CombatExecutionContext context)
    {
        var cards = FightUI.cardItemList;
        if (cards == null)
        {
            return;
        }

        var count = Math.Min(cards.Count, 64);
        for (var i = 0; i < count; i++)
        {
            if (cards[i] is not CommonCardItem card)
            {
                continue;
            }

            var legal = TryPreflightCard(card, fightUi, out var reason);
            if (card is AttackCardItem)
            {
                if (state.Enemies.Count == 0)
                {
                    AddCardCandidate(
                        state,
                        context,
                        card,
                        2000 + i,
                        i,
                        null,
                        0,
                        false,
                        "no living enemy target");
                    continue;
                }

                for (var targetIndex = 0; targetIndex < state.Enemies.Count; targetIndex++)
                {
                    var targetId = state.Enemies[targetIndex].RuntimeId;
                    context.TryResolveActor<StatusManager>(targetId, out var target);
                    AddCardCandidate(
                        state,
                        context,
                        card,
                        2000 + i,
                        i,
                        target,
                        targetId,
                        legal,
                        reason);
                }
            }
            else
            {
                AddCardCandidate(
                    state,
                    context,
                    card,
                    2000 + i,
                    i,
                    null,
                    0,
                    legal,
                    reason);
            }
        }
    }

    private static void AddCardCandidate(
        CombatStateObservation state,
        CombatExecutionContext context,
        CommonCardItem card,
        int publicCardId,
        int index,
        StatusManager? target,
        int publicTargetId,
        bool legal,
        string reason)
    {
        var sourceId = WitchCombatValueEstimator.IdOf(card.dataConfig);
        var semantics = WitchCombatValueEstimator.Estimate(
            card.dataConfig,
            card is AttackCardItem,
            target == null ? CombatTargetKind.None : CombatTargetKind.Enemy);
        var features = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["handIndex"] = index,
            ["isCard"] = 1d,
            ["visibleFake"] = IsVisibleFake(card) ? 1d : 0d,
            ["retain"] = HasAnyTag(card, "Retain", "RetainCard") ? 1d : 0d,
            ["inherent"] = HasAnyTag(card, "Inherent") ? 1d : 0d,
            ["recycle"] = HasAnyTag(card, "Recycle") ? 1d : 0d,
            ["ouroboros"] = HasAnyTag(card, "Ouroboros") ? 1d : 0d,
            ["exhaustOnUse"] = HasAnyTag(
                card,
                "Burnout",
                "Fragmented",
                "Exhaust",
                "Consumption")
                ? 1d
                : 0d
        };
        if (card.Vars != null
            && card.Vars.TryGetValue("ThisCount", out var useCountRaw)
            && double.TryParse(
                useCountRaw,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var useCount))
        {
            features["mechanic:card-use-count"] = Math.Max(0d, useCount);
        }
        AddBoundAction(
            state,
            context,
            new CombatActionObservation
        {
            CandidateId = "card:" + sourceId + ":" + publicCardId + ":" + publicTargetId,
            SourceId = sourceId,
            DisplayName = WitchCombatValueEstimator.NameOf(card.dataConfig),
            Kind = CombatActionKind.PlayCard,
            RuntimeId = publicCardId,
            TargetRuntimeId = publicTargetId,
            TargetKind = target == null ? CombatTargetKind.None : CombatTargetKind.Enemy,
            Cost = ComputeCardCost(card),
            Legal = legal,
            RejectionReason = reason,
            Semantics = semantics,
            Features = features
        },
            card,
            target);
    }

    private static bool IsVisibleFake(CommonCardItem card)
    {
        return card?.Vars != null
               && card.Vars.TryGetValue("IsFake", out var value)
               && bool.TryParse(value, out var parsed)
               && parsed;
    }

    private static void AddSkills(
        CombatStateObservation state,
        FightUI fightUi,
        CombatExecutionContext context)
    {
        if (SkillItemsField?.GetValue(fightUi) is not IEnumerable skills)
        {
            return;
        }

        var skillIndex = 0;
        foreach (var value in skills)
        {
            if (value is not SkillItem skill)
            {
                continue;
            }

            var publicSkillId = 3000 + skillIndex++;
            var legal = TryPreflightSkill(skill, out var reason);
            if (IsUntargetedSkill(skill))
            {
                AddSkillCandidate(
                    state,
                    context,
                    skill,
                    publicSkillId,
                    null,
                    0,
                    CombatTargetKind.None,
                    legal,
                    reason);
                continue;
            }

            for (var i = 0; i < state.Enemies.Count; i++)
            {
                var targetId = state.Enemies[i].RuntimeId;
                context.TryResolveActor<StatusManager>(targetId, out var target);
                if (target != null)
                {
                    AddSkillCandidate(
                        state,
                        context,
                        skill,
                        publicSkillId,
                        target,
                        targetId,
                        CombatTargetKind.Enemy,
                        legal,
                        reason);
                }
            }

            if (FightPlayer.Instance?.Status is StatusManager self)
            {
                AddSkillCandidate(
                    state,
                    context,
                    skill,
                    publicSkillId,
                    self,
                    state.Player.RuntimeId,
                    CombatTargetKind.Self,
                    legal,
                    reason);
            }
        }
    }

    private static void AddSkillCandidate(
        CombatStateObservation state,
        CombatExecutionContext context,
        SkillItem skill,
        int publicSkillId,
        StatusManager? target,
        int publicTargetId,
        CombatTargetKind targetKind,
        bool legal,
        string reason)
    {
        var semantics = WitchCombatValueEstimator.Estimate(skill.dataConfig, false, targetKind);
        if (semantics.CooldownTurns <= 0d)
        {
            semantics.CooldownTurns = 1d;
        }
        var sourceId = WitchCombatValueEstimator.IdOf(skill.dataConfig);
        AddBoundAction(
            state,
            context,
            new CombatActionObservation
        {
            CandidateId = "skill:" + sourceId + ":" + publicSkillId + ":" + publicTargetId,
            SourceId = sourceId,
            DisplayName = WitchCombatValueEstimator.NameOf(skill.dataConfig),
            Kind = CombatActionKind.UseSkill,
            RuntimeId = publicSkillId,
            TargetRuntimeId = publicTargetId,
            TargetKind = targetKind,
            Legal = legal,
            RejectionReason = reason,
            Semantics = semantics,
            Features = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["isSkill"] = 1d
            }
        },
            skill,
            target);
    }

    private static void AddBoundAction(
        CombatStateObservation state,
        CombatExecutionContext context,
        CombatActionObservation action,
        object? sourceHandle,
        object? targetHandle)
    {
        action.ObservationId = state.ObservationId;
        action.ActionToken = "a" + state.Actions.Count;
        if (action.Legal
            && !CombatAiRegistry.EvaluateRuntimePreflight(
                state,
                action,
                new CombatRuntimeActionContext
                {
                    SourceHandle = sourceHandle,
                    TargetHandle = targetHandle
                },
                out var reason))
        {
            action.Legal = false;
            action.RejectionReason = reason;
        }
        state.Actions.Add(action);
        context.Bind(action, sourceHandle, targetHandle);
    }

    private static bool TryPreflightCard(CommonCardItem card, FightUI fightUi, out string reason)
    {
        if (!IsPlayerActionWindow(fightUi))
        {
            reason = "not a stable player action window";
            return false;
        }

        if (card == null
            || card.gameObject == null
            || !card.gameObject.activeInHierarchy
            || card.hasUse
            || !card.enabled
            || card.dataConfig == null
            || card.data == null
            || card.status == null)
        {
            reason = "card runtime state is unavailable";
            return false;
        }

        if (card.Tags.Contains("Unusable")
            || (card.Vars.TryGetValue("Usable", out var usable) && usable == "0"))
        {
            reason = "card is marked unusable";
            return false;
        }

        if (!card.data.TryGetValue("Expend", out var rawCost) || !int.TryParse(rawCost, out _))
        {
            reason = "card cost is invalid";
            return false;
        }

        if (ComputeCardCost(card) > FightPlayer.Instance.CurPowerCount)
        {
            reason = "insufficient power";
            return false;
        }

        reason = "";
        return true;
    }

    private static bool TryPreflightSkill(SkillItem skill, out string reason)
    {
        if (skill == null
            || skill.dataConfig == null
            || skill.data == null
            || skill.Vars == null
            || skill.status == null
            || !CardItem.canUse
            || FightManager.Instance == null
            || FightManager.Instance.fightType != FightType.Player)
        {
            reason = "skill runtime state is unavailable";
            return false;
        }

        var id = WitchCombatValueEstimator.IdOf(skill.dataConfig);
        if (string.IsNullOrWhiteSpace(id)
            || RoleTable.Instance?.SkillTime == null
            || !RoleTable.Instance.SkillTime.TryGetValue(id, out var cooldown)
            || cooldown > 0)
        {
            reason = "skill is cooling down";
            return false;
        }

        reason = "";
        return true;
    }

    private static int ComputeCardCost(CommonCardItem card)
    {
        if (card.data == null
            || !card.data.TryGetValue("Expend", out var raw)
            || !int.TryParse(raw, out var baseCost))
        {
            return int.MaxValue;
        }

        var multiplier = 1f;
        var dynamicVariables = FightPlayer.Instance?.Status?.dynamicVariables;
        if (dynamicVariables != null && dynamicVariables.TryGetValue("CardCost", out var configuredMultiplier))
        {
            multiplier = configuredMultiplier;
        }
        var cost = Math.Min((int)(baseCost * multiplier), 4);
        cost += ParseInt(card.Vars, "TotalExCost");
        cost += ParseInt(card.Vars, "ExCost");
        cost += ParseInt(card.Vars, "OnceExCost");
        return Math.Max(0, cost);
    }

    private static int ParseInt(IDictionary<string, string> values, string key)
    {
        return values != null
               && values.TryGetValue(key, out var raw)
               && int.TryParse(raw, out var value)
            ? value
            : 0;
    }

    private static bool IsUntargetedSkill(SkillItem skill)
    {
        return skill.Vars != null
               && skill.Vars.TryGetValue("BaseScript", out var baseScript)
               && string.Equals(baseScript, "CommonCardItem", StringComparison.Ordinal);
    }

    private static void AddEnemiesAndNativeThreat(
        CombatStateObservation state,
        CombatExecutionContext context)
    {
        var enemies = EnemyManager.Instance?.enemyList;
        if (enemies == null)
        {
            return;
        }

        for (var i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            if (enemy?.Status is StatusManager status && status.CurHp > 0)
            {
                var observed = ObserveUnit(
                    status,
                    CombatTargetKind.Enemy,
                    1000 + i);
                observed.DefinitionId = WitchCombatValueEstimator.IdOf(
                    ReadMember(enemy, "dataConfig", "DataConfig") as IDataConfig);
                observed.Attack = ReadNumber(enemy, "Attack");
                state.Enemies.Add(observed);
                context.BindActor(observed.RuntimeId, status);
                AddEnemyThreat(state, enemy, observed);
            }
        }
    }

    private void ObserveDeck(CombatStateObservation state)
    {
        var manager = FightCardManager.Instance;
        if (manager == null)
        {
            return;
        }
        state.DeckCardIds = ReadCardIds(manager.FightcardList);
        state.DiscardPileCardIds = ReadCardIds(manager.usedCardList);
        state.HandCardIds = state.Actions
            .Where(action => action.Kind == CombatActionKind.PlayCard)
            .GroupBy(action => action.RuntimeId)
            .Select(group => group.First())
            .Select(action => action.SourceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        state.RetainedHandCardIds = state.Actions
            .Where(action => action.Kind == CombatActionKind.PlayCard
                             && action.Features.TryGetValue("retain", out var retain)
                             && retain > 0d)
            .GroupBy(action => action.RuntimeId)
            .Select(group => group.First().SourceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        foreach (var sourceId in state.Actions
                     .Where(action => action.Kind == CombatActionKind.PlayCard)
                     .Select(action => action.SourceId)
                     .Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            if (!state.DeckCardIds.Contains(sourceId, StringComparer.OrdinalIgnoreCase))
            {
                state.DeckCardIds.Add(sourceId);
            }
        }
        var drawPileCount = CountItems(manager.cardList);
        var discardPileCount = CountItems(manager.usedCardList);
        if (trackedDeckBattleSessionId != state.BattleSessionId)
        {
            trackedDeckBattleSessionId = state.BattleSessionId;
            trackedDrawPileCount = -1;
            trackedDiscardPileCount = -1;
            shuffleEpoch = 0;
        }
        else if (trackedDrawPileCount >= 0
                 && trackedDiscardPileCount >= 0
                 && drawPileCount > trackedDrawPileCount
                 && discardPileCount < trackedDiscardPileCount)
        {
            shuffleEpoch++;
        }
        trackedDrawPileCount = drawPileCount;
        trackedDiscardPileCount = discardPileCount;
        state.ExhaustPileCardIds.Clear();
        state.DeckKnowledge = new CombatDeckKnowledge
        {
            DrawPileCount = drawPileCount,
            DiscardPileCount = discardPileCount,
            ExhaustPileCount = 0,
            ShuffleEpoch = shuffleEpoch,
            DiscardContentsVisible = true,
            ExhaustContentsVisible = false,
            KnownDeckCardIds = new List<string>(state.DeckCardIds)
        };
        state.Features["deckCount"] = state.DeckCardIds.Count;
        state.Features["drawPileCount"] = drawPileCount;
        state.Features["discardPileCount"] = discardPileCount;
        state.Features["exhaustPileCount"] = 0d;
        foreach (var group in state.DeckCardIds
                     .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                     .Take(128))
        {
            state.Features["deck:" + group.Key] = group.Count();
        }
    }

    private static List<string> ReadCardIds(IEnumerable? values)
    {
        var result = new List<string>();
        if (values == null)
        {
            return result;
        }
        foreach (var value in values)
        {
            var config = value as IDataConfig
                         ?? ReadMember(value, "dataConfig", "DataConfig") as IDataConfig;
            var id = WitchCombatValueEstimator.IdOf(config);
            if (!string.IsNullOrWhiteSpace(id))
            {
                result.Add(id);
            }
        }
        return result;
    }

    private static int CountItems(IEnumerable? values)
    {
        if (values is ICollection collection)
        {
            return collection.Count;
        }
        if (values == null)
        {
            return 0;
        }
        var count = 0;
        foreach (var _ in values)
        {
            count++;
        }
        return count;
    }

    private static bool HasAnyTag(CommonCardItem card, params string[] tags)
    {
        return card?.Tags != null
               && card.Tags.Any(value => tags.Any(tag =>
                   string.Equals(value, tag, StringComparison.OrdinalIgnoreCase)));
    }

    public CombatActionObservation? FindActionForRuntimeHandle(
        CombatStateObservation state,
        object runtimeHandle)
    {
        if (state == null || runtimeHandle == null)
        {
            return null;
        }

        object? targetHandle = null;
        if (runtimeHandle is CommonCardItem card
            && card.dataConfig?.scriptExecutor?.Target is StatusManager cardTarget)
        {
            targetHandle = cardTarget;
        }
        else if (runtimeHandle is SkillItem skill
                 && SkillHitEnemyField?.GetValue(skill) is StatusManager skillTarget)
        {
            targetHandle = skillTarget;
        }

        return executionContext.FindBySourceHandle(
            state,
            runtimeHandle,
            targetHandle)
               ?? executionContext.FindBySourceHandle(state, runtimeHandle);
    }

    public bool TryResolvePresentation(
        CombatActionObservation action,
        out UnityEngine.Component? source,
        out StatusManager? target)
    {
        source = null;
        target = null;
        if (!executionContext.TryResolve(action, out var binding))
        {
            return false;
        }
        source = binding.SourceHandle as UnityEngine.Component;
        target = binding.TargetHandle as StatusManager;
        return source != null;
    }

    private static CombatUnitObservation ObserveUnit(
        StatusManager status,
        CombatTargetKind kind,
        int publicRuntimeId)
    {
        var result = new CombatUnitObservation
        {
            RuntimeId = publicRuntimeId,
            Name = status.gameObject == null ? "" : status.gameObject.name,
            Kind = kind,
            CurrentHp = status.CurHp,
            MaxHp = status.MaxHp,
            Defend = status.Defend
        };
        ObserveEffects(status, result);
        return result;
    }

    private static void AddEnemyThreat(
        CombatStateObservation state,
        object enemy,
        CombatUnitObservation observed)
    {
        var actionCards = ReadMember(enemy, "ActionCards") as IEnumerable;
        var currentCount = 0;
        if (actionCards != null)
        {
            foreach (var rawCard in actionCards)
            {
                if (rawCard == null || currentCount++ >= 16)
                {
                    continue;
                }

                var config = ReadMember(rawCard, "dataConfig", "DataConfig") as IDataConfig;
                var semantics = WitchCombatValueEstimator.Estimate(
                    config,
                    forceAttack: false,
                    CombatTargetKind.Enemy);
                var keywords = ReadStrings(ReadMember(rawCard, "keyWords", "KeyWords"));
                if (semantics.Damage <= 0d
                    && semantics.TrueDamage <= 0d
                    && semantics.DamageOverTime <= 0d
                    && keywords.Any(value => ContainsAny(value, "attack", "damage", "攻击")))
                {
                    semantics.Damage = Math.Max(1d, observed.Attack);
                }

                var knownSemantics = HasKnownSemantics(semantics);
                var fallbackBlockable = !knownSemantics && observed.Attack > 0d
                    ? observed.Attack * 0.35d
                    : 0d;
                var intent = new CombatIntentObservation
                {
                    SourceId = WitchCombatValueEstimator.IdOf(config),
                    DisplayName = WitchCombatValueEstimator.NameOf(config),
                    Kind = ClassifyIntent(semantics),
                    SourceRuntimeId = observed.RuntimeId,
                    Probability = knownSemantics ? 1d : 0.35d,
                    BlockableDamage = Math.Max(
                        fallbackBlockable,
                        semantics.Damage * Math.Max(1d, semantics.HitCount)),
                    UnblockableDamage = Math.Max(0d, semantics.TrueDamage),
                    DamageOverTime = Math.Max(0d, semantics.DamageOverTime),
                    Confidence = knownSemantics ? 0.9d : 0.35d,
                    Current = true
                };
                state.Threat.Intents.Add(intent);
                state.Threat.CurrentIntentKnown = true;
                state.Threat.ExpectedBlockableDamage += intent.BlockableDamage;
                state.Threat.MaximumBlockableDamage += knownSemantics
                    ? intent.BlockableDamage
                    : Math.Max(intent.BlockableDamage, observed.Attack);
                state.Threat.ExpectedUnblockableDamage += intent.UnblockableDamage;
                state.Threat.ExpectedDamageOverTime += intent.DamageOverTime;
            }
        }

        var enemyConfig = ReadMember(enemy, "dataConfig", "DataConfig") as IDataConfig;
        state.Threat.IntentPoolSize += CountIntentPool(enemyConfig);
        if (currentCount == 0 && observed.Attack > 0d)
        {
            var actionCount = Math.Max(1d, ReadNumber(enemy, "ActionCount", "MaxActionCount"));
            var maximum = observed.Attack * actionCount;
            state.Threat.ExpectedBlockableDamage += maximum * 0.35d;
            state.Threat.MaximumBlockableDamage += maximum;
            state.Threat.AttackProbability = Math.Max(state.Threat.AttackProbability, 0.35d);
            state.Threat.Confidence = Math.Max(state.Threat.Confidence, 0.25d);
        }
    }

    private static void NormalizeThreat(CombatStateObservation state)
    {
        var threat = state.Threat ?? new CombatThreatForecast();
        state.Threat = threat;
        if (threat.CurrentIntentKnown)
        {
            var knownAttack = threat.Intents.Any(intent =>
                intent.Kind == CombatIntentKind.Attack
                || intent.Kind == CombatIntentKind.DamageOverTime);
            var unknownProbability = threat.Intents
                .Where(intent => intent.Kind == CombatIntentKind.Unknown)
                .Select(intent => intent.Probability)
                .DefaultIfEmpty(0d)
                .Max();
            threat.AttackProbability = knownAttack
                ? 1d
                : Math.Max(threat.AttackProbability, unknownProbability);
            threat.Confidence = threat.Intents.Count == 0
                ? Math.Max(threat.Confidence, 0.5d)
                : Math.Max(
                    threat.Confidence,
                    threat.Intents.Average(intent => Math.Max(0d, Math.Min(1d, intent.Confidence))));
        }
        threat.MaximumBlockableDamage = Math.Max(
            threat.ExpectedBlockableDamage,
            threat.MaximumBlockableDamage);
        var total = threat.ExpectedBlockableDamage
                    + threat.ExpectedUnblockableDamage
                    + threat.ExpectedDamageOverTime;
        state.ExpectedIncomingDamage = total;
        var effectiveHp = Math.Max(1d, state.Player.CurrentHp + state.Player.Defend);
        threat.LethalProbability = total >= effectiveHp
            ? Math.Max(threat.AttackProbability, threat.Confidence)
            : 0d;
        threat.Summary = "blockable="
                         + threat.ExpectedBlockableDamage.ToString("0.0")
                         + ",unblockable="
                         + threat.ExpectedUnblockableDamage.ToString("0.0")
                         + ",dot="
                         + threat.ExpectedDamageOverTime.ToString("0.0")
                         + ",known="
                         + threat.CurrentIntentKnown;
        state.Features["expectedBlockableDamage"] = threat.ExpectedBlockableDamage;
        state.Features["maximumBlockableDamage"] = threat.MaximumBlockableDamage;
        state.Features["expectedUnblockableDamage"] = threat.ExpectedUnblockableDamage;
        state.Features["expectedDamageOverTime"] = threat.ExpectedDamageOverTime;
        state.Features["attackProbability"] = threat.AttackProbability;
        state.Features["threatConfidence"] = threat.Confidence;
        state.Features["currentIntentKnown"] = threat.CurrentIntentKnown ? 1d : 0d;
    }

    private static void ObserveEffects(StatusManager status, CombatUnitObservation observed)
    {
        IBuffItem[] buffs;
        try
        {
            buffs = status.GetBuffs() ?? Array.Empty<IBuffItem>();
        }
        catch
        {
            buffs = Array.Empty<IBuffItem>();
        }

        var count = 0;
        foreach (var buff in buffs)
        {
            if (buff?.buffConfig == null || count++ >= 64)
            {
                continue;
            }

            var config = buff.buffConfig;
            var id = config.BuffId ?? "";
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var level = Math.Max(0, config.Level);
            observed.Features["status:" + id] = level;
            if (id.IndexOf(
                    "limitdamage",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                observed.Features["damageLimitActive"] = 1d;
                observed.Features["damageLimitLevel"] = level;
            }
            if (id.IndexOf("frenzy", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("keenedge", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("counterattack", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("thorns", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                observed.Features["escalationPressure"] =
                    observed.Features.TryGetValue(
                        "escalationPressure",
                        out var escalation)
                        ? escalation + Math.Max(1, level)
                        : Math.Max(1, level);
            }
            observed.Statuses.Add(new CombatStatusObservation
            {
                StatusId = id,
                DisplayName = config.BuffName ?? "",
                Level = level,
                UpperBound = config.UpperBound,
                ReducePerTurn = config.ReducePerTurn,
                ReducePerUse = config.ReducePerUse,
                ReducePerAttacked = config.ReducePerAttacked,
                Type = config.Type ?? ""
            });
        }
    }

    private void ObservePublicMechanicState(
        StatusManager status,
        CombatStateObservation state)
    {
        try
        {
            var specialVars = ScriptExecutor.PlayerInfo.SpecialVars;
            if (specialVars != null
                && specialVars.TryGetValue(
                    "ResurrectionCount",
                    out var resurrectionRaw)
                && int.TryParse(resurrectionRaw, out var resurrectionCount))
            {
                if (trackedMechanicBattleSessionId != state.BattleSessionId)
                {
                    trackedMechanicBattleSessionId = state.BattleSessionId;
                    resurrectionCountBaseline = Math.Max(0, resurrectionCount);
                }
                if (resurrectionCount < resurrectionCountBaseline)
                {
                    resurrectionCountBaseline = Math.Max(0, resurrectionCount);
                }
                state.Features[CombatArchetypePolicy.ResurrectionCountFeature] =
                    Math.Max(0, resurrectionCount - resurrectionCountBaseline);
            }
        }
        catch
        {
            // The deck and visible buff state remain a safe compatibility fallback.
        }

        IBuffItem[] buffs;
        try
        {
            buffs = status.GetBuffs() ?? Array.Empty<IBuffItem>();
        }
        catch
        {
            return;
        }
        var timeLock = buffs.FirstOrDefault(item =>
            item?.buffConfig != null
            && string.Equals(
                item.buffConfig.BuffId,
                "buff_timelock",
                StringComparison.OrdinalIgnoreCase));
        var effects = ReadMember(timeLock!, "effectList", "EffectList") as IEnumerable;
        if (effects == null)
        {
            return;
        }
        var sequence = 0;
        foreach (var effect in effects)
        {
            if (sequence >= 64)
            {
                break;
            }
            var config = ReadMember(effect!, "Item1", "dataConfig", "DataConfig")
                         as IDataConfig;
            var sourceId = WitchCombatValueEstimator.IdOf(config);
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                continue;
            }
            state.DeferredEffects.Add(new CombatDeferredEffectObservation
            {
                Sequence = sequence++,
                StatusId = "buff_timelock",
                SourceId = sourceId
            });
        }
    }

    private static CombatIntentKind ClassifyIntent(CombatActionSemantics semantics)
    {
        if (semantics.DamageOverTime > 0d)
        {
            return CombatIntentKind.DamageOverTime;
        }
        if (semantics.Damage > 0d || semantics.TrueDamage > 0d)
        {
            return CombatIntentKind.Attack;
        }
        if (semantics.Defend > 0d)
        {
            return CombatIntentKind.Defend;
        }
        if (semantics.Heal > 0d)
        {
            return CombatIntentKind.Heal;
        }
        if (semantics.Debuff > 0d)
        {
            return CombatIntentKind.Debuff;
        }
        if (semantics.Buff > 0d || semantics.Scaling > 0d)
        {
            return CombatIntentKind.Buff;
        }
        return CombatIntentKind.Unknown;
    }

    private static bool HasKnownSemantics(CombatActionSemantics semantics)
    {
        return semantics.Damage > 0d
               || semantics.TrueDamage > 0d
               || semantics.DamageOverTime > 0d
               || semantics.Defend > 0d
               || semantics.Heal > 0d
               || semantics.Buff > 0d
               || semantics.Debuff > 0d;
    }

    private static int CountIntentPool(IDataConfig? config)
    {
        if (config?.data == null
            || !config.data.TryGetValue("CardList", out var raw)
            || string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        return raw.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static IEnumerable<string> ReadStrings(object? value)
    {
        if (value is not IEnumerable values || value is string)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var item in values)
        {
            if (item != null)
            {
                result.Add(Convert.ToString(item) ?? "");
            }
        }
        return result;
    }

    private static double ReadNumber(object target, params string[] names)
    {
        var value = ReadMember(target, names);
        if (value == null)
        {
            return 0d;
        }

        try
        {
            return Finite(Convert.ToDouble(value));
        }
        catch
        {
            return 0d;
        }
    }

    private static object? ReadMember(object target, params string[] names)
    {
        if (target == null)
        {
            return null;
        }

        var type = target.GetType();
        for (var i = 0; i < names.Length; i++)
        {
            var cacheKey = type.AssemblyQualifiedName + "|" + names[i];
            MemberInfo? member;
            lock (ReflectionGate)
            {
                if (!MemberCache.TryGetValue(cacheKey, out member))
                {
                    member = (MemberInfo?)type.GetField(names[i], InstanceFlags)
                             ?? type.GetProperty(names[i], InstanceFlags);
                    MemberCache[cacheKey] = member;
                }
            }

            try
            {
                if (member is FieldInfo field)
                {
                    return field.GetValue(target);
                }
                if (member is PropertyInfo property && property.GetIndexParameters().Length == 0)
                {
                    return property.GetValue(target, null);
                }
            }
            catch
            {
                // Compatibility probing is best-effort; the stable fallback is zero/empty.
            }
        }

        return null;
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
    }

    private static bool IsCurrentTarget(
        CombatActionObservation action,
        StatusManager? target)
    {
        if (target == null
            || target.CurHp <= 0
            || action.TargetRuntimeId == 0)
        {
            return false;
        }

        if (FightPlayer.Instance?.Status is StatusManager self
            && ReferenceEquals(self, target))
        {
            return action.TargetKind == CombatTargetKind.Self;
        }
        var enemies = EnemyManager.Instance?.enemyList;
        return enemies != null
               && enemies.Any(enemy =>
                   enemy?.Status is StatusManager status
                   && status.CurHp > 0
                   && ReferenceEquals(status, target));
    }

}

public static class WitchCombatValueEstimator
{
    private static readonly string[] TrueDamageTokens = { "truedamage", "piercingdamage", "unblockabledamage" };
    private static readonly string[] DamageOverTimeTokens = { "damageovertime", "dotdamage", "poison", "toxin", "burn" };
    private static readonly string[] HitCountTokens = { "hitcount", "attackcount", "times" };
    private static readonly string[] DamageTokens = { "damage", "hurt", "attack" };
    private static readonly string[] DefendTokens = { "defend", "shield", "armor" };
    private static readonly string[] HealTokens = { "heal", "cure", "recoverhp" };
    private static readonly string[] DrawTokens = { "draw", "getcard" };
    private static readonly string[] EnergyTokens = { "power", "energy" };
    private static readonly string[] ScalingTokens = { "strength", "dexterity", "addbuff" };
    private static readonly string[] BuffTokens = { "buff", "strength", "dexterity", "enhance" };
    private static readonly string[] DebuffTokens = { "debuff", "vulnerable", "weak", "frail", "poison" };
    private static readonly string[] CleanseTokens = { "cleanse", "dispel", "removebuff", "clearbuff" };
    private static readonly string[] CostReductionTokens = { "reducecost", "costreduce", "expendreduce", "excostreduce" };
    private static readonly string[] CardGenerationTokens = { "createcard", "addcard", "generatecard", "getcard" };
    private static readonly string[] CooldownTokens = { "cooldown", "skillcd", "cdturn" };

    public static CombatActionSemantics Estimate(
        IDataConfig? config,
        bool forceAttack,
        CombatTargetKind targetKind)
    {
        var result = new CombatActionSemantics();
        if (config == null)
        {
            result.Uncertainty = 3d;
            return result;
        }

        ReadDictionary(config.data, result);
        ReadDictionary(config.Vars, result);
        var script = CombinedScript(config);
        var descriptiveValue = LargestDescriptiveValue(config);
        var constantSelfHpLoss = ConstantSelfHpLoss(
            script,
            targetKind);
        if (constantSelfHpLoss > 0d)
        {
            if (ContainsAny(
                    script,
                    "AddEvent(\"Action\"",
                    "AddEvent(\"EndRound\"",
                    "AddEvent(\"StartRound\""))
            {
                result.EndOfCycleSelfHpLoss += constantSelfHpLoss;
            }
            else
            {
                result.SelfHpLoss += constantSelfHpLoss;
            }
            result.Risk += constantSelfHpLoss;
        }
        if (result.Damage <= 0d && ContainsAny(script, "Damage", "Hit("))
        {
            result.Damage = descriptiveValue;
        }
        if (result.TrueDamage <= 0d && ContainsAny(script, "TrueDamage", "PiercingDamage", "UnblockableDamage"))
        {
            result.TrueDamage = descriptiveValue;
            if (result.Damage == descriptiveValue)
            {
                result.Damage = 0d;
            }
        }
        if (result.DamageOverTime <= 0d && ContainsAny(script, "Poison", "Toxin", "DamageOverTime"))
        {
            result.DamageOverTime = descriptiveValue;
        }

        if (result.Defend <= 0d && ContainsAny(script, "Defend", "Shield", "Armor"))
        {
            result.Defend = descriptiveValue;
        }

        if (result.Heal <= 0d && ContainsAny(script, "Heal", "Cure", "RecoverHp"))
        {
            result.Heal = descriptiveValue;
        }

        if (result.Draw <= 0d && ContainsAny(script, "Draw", "GetCard"))
        {
            result.Draw = Math.Max(1d, descriptiveValue);
        }

        if (result.Cleanse <= 0d && ContainsAny(script, "RemoveBuff", "ClearBuff", "Cleanse", "Dispel"))
        {
            result.Cleanse = Math.Max(1d, descriptiveValue);
        }

        if (result.CostReduction <= 0d && ContainsAny(script, "ReduceCost", "CostReduce", "ExCost", "OnceExCost"))
        {
            result.CostReduction = Math.Max(1d, descriptiveValue);
        }

        if (result.CardGeneration <= 0d && ContainsAny(script, "CreateCard", "AddCard", "GetCard", "GenerateCard"))
        {
            result.CardGeneration = Math.Max(1d, result.Draw > 0d ? result.Draw : descriptiveValue);
        }

        if (ContainsAny(script, "AddTempEvent", "AddEvent", "AddListener", "RoundStart", "TurnStart"))
        {
            result.PersistentValue = Math.Max(1d, descriptiveValue);
        }

        if (ContainsAny(script, "AddBuff"))
        {
            if (targetKind == CombatTargetKind.Enemy)
            {
                result.Debuff = Math.Max(1d, result.Debuff == 0d ? descriptiveValue : result.Debuff);
            }
            else
            {
                result.Buff = Math.Max(1d, result.Buff == 0d ? descriptiveValue : result.Buff);
            }
        }

        result.OpensInteraction = ContainsAny(
            script,
            "SelectCard",
            "PackToDeck",
            "OutFightSelect",
            "DeckUI",
            "ThrowCard",
            "Burning");
        result.RandomOutcome = ContainsAny(script, "Random", "Dice", "RandomRange");
        if (result.RandomOutcome)
        {
            result.Uncertainty += 0.7d;
        }

        if (forceAttack && result.Damage <= 0d)
        {
            result.Damage = 3d;
            result.Uncertainty += 0.8d;
        }

        if (targetKind == CombatTargetKind.Self && result.Damage > 0d)
        {
            result.SelfHpLoss += result.Damage;
            result.Risk += result.Damage;
            result.Damage = 0d;
        }

        if (result.Damage == 0d
            && result.TrueDamage == 0d
            && result.DamageOverTime == 0d
            && result.Defend == 0d
            && result.Heal == 0d
            && result.Draw == 0d
            && result.EnergyGain == 0d
            && result.Scaling == 0d
            && result.DeckValue == 0d
            && result.Buff == 0d
            && result.Debuff == 0d
            && result.Cleanse == 0d
            && result.CostReduction == 0d
            && result.CardGeneration == 0d
            && result.PersistentValue == 0d)
        {
            result.Uncertainty += 1.5d;
        }

        return result;
    }

    private static double ConstantSelfHpLoss(
        string script,
        CombatTargetKind targetKind)
    {
        if (string.IsNullOrWhiteSpace(script)
            || (targetKind == CombatTargetKind.Enemy
                && script.IndexOf(
                    "SetStatus(\"Self\")",
                    StringComparison.OrdinalIgnoreCase) < 0))
        {
            return 0d;
        }
        var total = 0d;
        foreach (Match match in Regex.Matches(
                     script,
                     "ChangeHp\\s*\\(\\s*\"-(\\d+)\"\\s*\\)",
                     RegexOptions.IgnoreCase
                     | RegexOptions.CultureInvariant))
        {
            if (double.TryParse(
                    match.Groups[1].Value,
                    out var amount))
            {
                total += Math.Max(0d, amount);
            }
        }
        return total;
    }

    public static string IdOf(IDataConfig? config)
    {
        return Value(config?.data, "Id");
    }

    public static string NameOf(IDataConfig? config)
    {
        var name = Value(config?.data, "Name");
        return string.IsNullOrWhiteSpace(name) ? IdOf(config) : name;
    }

    private static void ReadDictionary(
        IDictionary<string, string>? values,
        CombatActionSemantics result)
    {
        if (values == null)
        {
            return;
        }

        foreach (var pair in values)
        {
            if (!double.TryParse(pair.Value, out var number))
            {
                continue;
            }

            var key = pair.Key ?? "";
            if (ContainsToken(key, TrueDamageTokens))
            {
                result.TrueDamage = Math.Max(result.TrueDamage, number);
            }
            else if (ContainsToken(key, DamageOverTimeTokens))
            {
                result.DamageOverTime = Math.Max(result.DamageOverTime, number);
            }
            else if (ContainsToken(key, HitCountTokens))
            {
                result.HitCount = Math.Max(result.HitCount, number);
            }
            else if (ContainsToken(key, DamageTokens))
            {
                result.Damage = Math.Max(result.Damage, number);
            }
            else if (ContainsToken(key, DefendTokens))
            {
                result.Defend = Math.Max(result.Defend, number);
            }
            else if (ContainsToken(key, HealTokens))
            {
                result.Heal = Math.Max(result.Heal, number);
            }
            else if (ContainsToken(key, DrawTokens))
            {
                result.Draw = Math.Max(result.Draw, number);
            }
            else if (ContainsToken(key, EnergyTokens) && !key.Contains("Expend"))
            {
                result.EnergyGain = Math.Max(result.EnergyGain, number);
            }
            else if (ContainsToken(key, ScalingTokens))
            {
                result.Scaling = Math.Max(result.Scaling, number);
            }
            else if (ContainsToken(key, DebuffTokens))
            {
                result.Debuff = Math.Max(result.Debuff, number);
            }
            else if (ContainsToken(key, CleanseTokens))
            {
                result.Cleanse = Math.Max(result.Cleanse, number);
            }
            else if (ContainsToken(key, CostReductionTokens))
            {
                result.CostReduction = Math.Max(result.CostReduction, number);
            }
            else if (ContainsToken(key, CardGenerationTokens))
            {
                result.CardGeneration = Math.Max(result.CardGeneration, number);
            }
            else if (ContainsToken(key, CooldownTokens))
            {
                result.CooldownTurns = Math.Max(result.CooldownTurns, number);
            }
            else if (ContainsToken(key, BuffTokens))
            {
                result.Buff = Math.Max(result.Buff, number);
            }
        }
    }

    private static string CombinedScript(IDataConfig config)
    {
        var parts = new List<string>();
        AddScript(parts, config.data);
        AddScript(parts, config.Vars);
        return string.Join(";", parts);
    }

    private static double LargestDescriptiveValue(IDataConfig config)
    {
        var maximum = 0d;
        ReadDescriptiveValues(config.data, ref maximum);
        ReadDescriptiveValues(config.Vars, ref maximum);
        return maximum <= 0d ? 1d : maximum;
    }

    private static void ReadDescriptiveValues(
        IDictionary<string, string>? values,
        ref double maximum)
    {
        if (values == null)
        {
            return;
        }

        foreach (var pair in values)
        {
            var key = pair.Key ?? "";
            if (key.IndexOf("DesVal", StringComparison.OrdinalIgnoreCase) < 0
                && key.IndexOf("Value", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (double.TryParse(pair.Value, out var value))
            {
                maximum = Math.Max(maximum, Math.Abs(value));
            }
        }
    }

    private static void AddScript(List<string> parts, IDictionary<string, string>? values)
    {
        if (values == null)
        {
            return;
        }

        foreach (var pair in values)
        {
            if ((pair.Key ?? "").IndexOf("Script", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                parts.Add(pair.Value ?? "");
            }
        }
    }

    private static bool ContainsToken(string value, IEnumerable<string> tokens)
    {
        return tokens.Any(token => value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string Value(IDictionary<string, string>? values, string key)
    {
        return values != null && values.TryGetValue(key, out var value) ? value ?? "" : "";
    }
}
