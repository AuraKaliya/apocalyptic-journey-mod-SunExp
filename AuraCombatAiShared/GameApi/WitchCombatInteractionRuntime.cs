using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraDecision.Shared;
using Michsky.MUIP;
using UnityEngine.EventSystems;
using Witch.Core;
using Witch.UI.Window;

namespace AuraCombatAi.Shared.GameApi;

public enum WitchInteractionResolveResult
{
    None,
    Pending,
    Completed,
    HandedToPlayer,
    Failed
}

public static class WitchCombatInteractionRuntime
{
    private static readonly BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly MethodInfo? HandleSelectModeClick =
        typeof(CardItem).GetMethod("HandleSelectModeClick", InstanceFlags);
    private static DeckPrompt? deckPrompt;
    private static HandPrompt? handPrompt;

    public static bool HasActivePrompt => deckPrompt != null || handPrompt != null;

    public static void ObserveDeckPrompt(ModHookContext context)
    {
        if (context?.Target is not DeckUI deckUi
            || context.Arguments == null
            || context.Arguments.Length < 3
            || context.Arguments[0] is not int count
            || context.Arguments[2] is not IList source)
        {
            return;
        }

        var choices = new List<CombatActionObservation>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            if (source[i] is not IDataConfig config)
            {
                continue;
            }

            choices.Add(CreateChoice(config, i));
        }

        var request = CombatInteractionBroker.Begin(
            new CombatInteractionHint
            {
                Purpose = "deck-selection",
                Kind = CombatPromptKind.ChooseCards,
                Zone = CombatPromptZone.Deck,
                Forced = true
            },
            count,
            choices);
        deckPrompt = new DeckPrompt(deckUi, source, request);
        handPrompt = null;
    }

    public static void ObserveHandPrompt(ModHookContext context)
    {
        if (context?.Target is not FightUI fightUi
            || context.Arguments == null
            || context.Arguments.Length == 0)
        {
            return;
        }

        var count = 1;
        if (context.Arguments[0] is string raw)
        {
            int.TryParse(raw, out count);
        }

        var cards = FightUI.cardItemList ?? new List<CardItem>();
        var choices = new List<CombatActionObservation>(cards.Count);
        for (var i = 0; i < cards.Count; i++)
        {
            if (cards[i]?.dataConfig != null)
            {
                choices.Add(CreateChoice(cards[i].dataConfig, i, cards[i]));
            }
        }

        var request = CombatInteractionBroker.Begin(
            new CombatInteractionHint
            {
                Purpose = "hand-selection",
                Kind = CombatPromptKind.ChooseHandCards,
                Zone = CombatPromptZone.Hand,
                Forced = true,
                PreferLowestValue = true
            },
            Math.Max(1, count),
            choices);
        handPrompt = new HandPrompt(fightUi, request);
        deckPrompt = null;
    }

    public static WitchInteractionResolveResult TryResolve(bool automationActive)
    {
        if (deckPrompt != null)
        {
            return ResolveDeckPrompt(automationActive);
        }

        if (handPrompt != null)
        {
            return ResolveHandPrompt(automationActive);
        }

        return WitchInteractionResolveResult.None;
    }

    public static void Reset()
    {
        deckPrompt = null;
        handPrompt = null;
        CombatInteractionBroker.Clear();
    }

    private static WitchInteractionResolveResult ResolveDeckPrompt(bool automationActive)
    {
        var prompt = deckPrompt;
        if (prompt == null)
        {
            return WitchInteractionResolveResult.None;
        }

        if (!automationActive)
        {
            CombatInteractionBroker.Transition(
                prompt.Request.RequestId,
                CombatInteractionState.HandedToPlayer,
                "automation disabled while prompt is active");
            deckPrompt = null;
            return WitchInteractionResolveResult.HandedToPlayer;
        }

        if (prompt.DeckUi == null || !prompt.DeckUi.gameObject.activeInHierarchy)
        {
            CombatInteractionBroker.Transition(prompt.Request.RequestId, CombatInteractionState.Completed);
            CombatInteractionBroker.Clear(prompt.Request.RequestId);
            deckPrompt = null;
            return WitchInteractionResolveResult.Completed;
        }

        try
        {
            CombatInteractionBroker.Transition(prompt.Request.RequestId, CombatInteractionState.AwaitingChoice);
            if (prompt.SelectionIssued)
            {
                return WitchInteractionResolveResult.Pending;
            }

            var buttons = prompt.DeckUi.GetComponentsInChildren<ButtonManager>(true)
                .Where(button => button != null
                                 && button.gameObject.activeInHierarchy
                                 && button.GetComponentInChildren<DisplayCard>(true) != null)
                .ToList();
            if (buttons.Count < prompt.Request.RequiredCount)
            {
                return WitchInteractionResolveResult.Pending;
            }

            var utilities = buttons
                .Select(button =>
                {
                    var card = button.GetComponentInChildren<DisplayCard>(true);
                    return ToUtility(WitchCombatValueEstimator.Estimate(
                        card?.dataConfig,
                        false,
                        CombatTargetKind.None));
                })
                .ToList();
            var selected = MultiSelectPlanner.ChooseIndices(
                utilities,
                prompt.Request.RequiredCount,
                preferLowest: prompt.Request.Hint.PreferLowestValue);
            CombatInteractionBroker.Transition(prompt.Request.RequestId, CombatInteractionState.Resolving);
            prompt.SelectionIssued = true;
            for (var i = 0; i < selected.Count; i++)
            {
                buttons[selected[i]].onClick.Invoke();
            }

            return WitchInteractionResolveResult.Pending;
        }
        catch (Exception ex)
        {
            CombatInteractionBroker.Transition(prompt.Request.RequestId, CombatInteractionState.Failed, ex.Message);
            deckPrompt = null;
            return WitchInteractionResolveResult.Failed;
        }
    }

    private static WitchInteractionResolveResult ResolveHandPrompt(bool automationActive)
    {
        var prompt = handPrompt;
        if (prompt == null)
        {
            return WitchInteractionResolveResult.None;
        }

        if (!automationActive)
        {
            CombatInteractionBroker.Transition(
                prompt.Request.RequestId,
                CombatInteractionState.HandedToPlayer,
                "automation disabled while prompt is active");
            handPrompt = null;
            return WitchInteractionResolveResult.HandedToPlayer;
        }

        if (prompt.FightUi == null)
        {
            CombatInteractionBroker.Transition(prompt.Request.RequestId, CombatInteractionState.Failed, "FightUI was destroyed");
            handPrompt = null;
            return WitchInteractionResolveResult.Failed;
        }

        if (!FightUI.InIEn)
        {
            return WitchInteractionResolveResult.Pending;
        }

        try
        {
            if (!prompt.SelectionIssued)
            {
                var cards = FightUI.cardItemList
                    .Where(card => card != null && card.gameObject.activeInHierarchy)
                    .ToList();
                var utilities = cards
                    .Select(card => ToUtility(WitchCombatValueEstimator.Estimate(
                        card.dataConfig,
                        card is AttackCardItem,
                        CombatTargetKind.None)))
                    .ToList();
                var selected = MultiSelectPlanner.ChooseIndices(
                    utilities,
                    prompt.Request.RequiredCount,
                    preferLowest: prompt.Request.Hint.PreferLowestValue);
                if (selected.Count == 0)
                {
                    return WitchInteractionResolveResult.Pending;
                }

                CombatInteractionBroker.Transition(prompt.Request.RequestId, CombatInteractionState.Resolving);
                for (var i = 0; i < selected.Count; i++)
                {
                    var eventData = new PointerEventData(EventSystem.current)
                    {
                        button = PointerEventData.InputButton.Left
                    };
                    HandleSelectModeClick?.Invoke(cards[selected[i]], new object[] { eventData });
                }

                prompt.SelectionIssued = true;
                return WitchInteractionResolveResult.Pending;
            }

            if (FightUI.SelectedCard.Count < prompt.Request.RequiredCount
                || FightUI.SelectedCard.Any(card => card == null || !card.enabled))
            {
                return WitchInteractionResolveResult.Pending;
            }

            var confirm = prompt.FightUi.ConfirmButton?.GetComponent<ButtonManager>();
            if (confirm == null || !confirm.gameObject.activeInHierarchy)
            {
                return WitchInteractionResolveResult.Pending;
            }

            confirm.onClick.Invoke();
            handPrompt = null;
            CombatInteractionBroker.Transition(prompt.Request.RequestId, CombatInteractionState.Completed);
            CombatInteractionBroker.Clear(prompt.Request.RequestId);
            return WitchInteractionResolveResult.Completed;
        }
        catch (Exception ex)
        {
            CombatInteractionBroker.Transition(prompt.Request.RequestId, CombatInteractionState.Failed, ex.Message);
            handPrompt = null;
            return WitchInteractionResolveResult.Failed;
        }
    }

    private static CombatActionObservation CreateChoice(
        IDataConfig config,
        int index,
        object? runtimeHandle = null)
    {
        return new CombatActionObservation
        {
            CandidateId = "prompt:" + index + ":" + WitchCombatValueEstimator.IdOf(config),
            SourceId = WitchCombatValueEstimator.IdOf(config),
            DisplayName = WitchCombatValueEstimator.NameOf(config),
            Kind = CombatActionKind.ResolvePrompt,
            RuntimeId = index,
            Semantics = WitchCombatValueEstimator.Estimate(config, false, CombatTargetKind.None),
            RuntimeHandle = runtimeHandle ?? config
        };
    }

    private static DecisionUtility ToUtility(CombatActionSemantics semantics)
    {
        return new DecisionUtility
        {
            Survival = semantics.Defend + semantics.Heal,
            Lethal = semantics.Damage,
            Tempo = semantics.Damage * 0.5d + semantics.Defend * 0.25d,
            Resource = semantics.EnergyGain,
            DeckEconomy = semantics.DeckValue,
            Scaling = semantics.Scaling,
            Continuation = semantics.Draw,
            Risk = semantics.Risk,
            Uncertainty = semantics.Uncertainty
        };
    }

    private sealed class DeckPrompt
    {
        public DeckPrompt(DeckUI deckUi, IList source, CombatInteractionRequest request)
        {
            DeckUi = deckUi;
            Source = source;
            Request = request;
        }

        public DeckUI DeckUi { get; }

        public IList Source { get; }

        public CombatInteractionRequest Request { get; }

        public bool SelectionIssued { get; set; }
    }

    private sealed class HandPrompt
    {
        public HandPrompt(FightUI fightUi, CombatInteractionRequest request)
        {
            FightUi = fightUi;
            Request = request;
        }

        public FightUI FightUi { get; }

        public CombatInteractionRequest Request { get; }

        public bool SelectionIssued { get; set; }
    }
}
