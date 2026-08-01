using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraDecision.Shared;
using Michsky.MUIP;
using UnityEngine;
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

        var request = CombatInteractionBroker.Begin(
            new CombatInteractionHint
            {
                Purpose = "deck-selection",
                Kind = CombatPromptKind.ChooseCards,
                Zone = CombatPromptZone.Deck,
                Forced = true
            },
            count,
            null);
        deckPrompt = new DeckPrompt(deckUi, source, request);
        handPrompt = null;
    }

    public static void ObserveHandPrompt(ModHookContext context)
    {
        ObserveHandPrompt(
            context,
            CombatPromptKind.ChooseHandCards,
            "hand-selection",
            preferLowestValue: true,
            excludesFrozenCards: false);
    }

    public static void ObserveDiscardPrompt(ModHookContext context)
    {
        ObserveHandPrompt(
            context,
            CombatPromptKind.DiscardCards,
            "forced-discard",
            preferLowestValue: true,
            excludesFrozenCards: true);
    }

    public static void ObserveBurnPrompt(ModHookContext context)
    {
        ObserveHandPrompt(
            context,
            CombatPromptKind.BurnCards,
            "forced-burn",
            preferLowestValue: true,
            excludesFrozenCards: true);
    }

    private static void ObserveHandPrompt(
        ModHookContext context,
        CombatPromptKind kind,
        string purpose,
        bool preferLowestValue,
        bool excludesFrozenCards)
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

        var request = CombatInteractionBroker.Begin(
            new CombatInteractionHint
            {
                Purpose = purpose,
                Kind = kind,
                Zone = CombatPromptZone.Hand,
                Forced = true,
                PreferLowestValue = preferLowestValue
            },
            Math.Max(1, count),
            null);
        handPrompt = new HandPrompt(
            fightUi,
            request,
            cards.Count,
            excludesFrozenCards,
            Time.unscaledTime);
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
            CombatInteractionBroker.PublishVisibleChoices(
                prompt.Request.RequestId,
                buttons
                    .Select((button, index) =>
                        button.GetComponentInChildren<DisplayCard>(true)?.dataConfig is { } config
                            ? CreateChoice(config, index)
                            : null)
                    .Where(choice => choice != null)
                    .Cast<CombatActionObservation>()
                    .ToList());
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

        if (!prompt.UiObserved)
        {
            if (FightUI.InIEn)
            {
                prompt.UiObserved = true;
                var selectedCount = FightUI.SelectedCard?.Count ?? 0;
                var nativeRequired = FightUI.SpecialCount + selectedCount;
                if (nativeRequired > 0)
                {
                    prompt.Selection.SetRequiredCount(nativeRequired);
                }
                CombatInteractionBroker.PublishVisibleChoices(
                    prompt.Request.RequestId,
                    EligibleHandCards(prompt)
                        .Select((card, index) => CreateChoice(card.dataConfig, index))
                        .ToList());
                CombatInteractionBroker.Transition(
                    prompt.Request.RequestId,
                    CombatInteractionState.AwaitingChoice,
                    BuildProgressMessage(prompt, selectedCount, EligibleHandCards(prompt).Count));
            }
            else if (Time.unscaledTime - prompt.CreatedAt > 0.15f
                     && CardItem.canUse
                     && (FightUI.cardItemList?.Count ?? 0) <= prompt.InitialHandCount)
            {
                return CompleteHandPrompt(prompt, "native prompt completed without manual selection");
            }

            return WitchInteractionResolveResult.Pending;
        }

        try
        {
            if (!FightUI.InIEn)
            {
                return CompleteHandPrompt(prompt, "native prompt closed");
            }

            var selectedCount = FightUI.SelectedCard?.Count ?? 0;
            var eligibleCards = EligibleHandCards(prompt);
            CombatInteractionBroker.Transition(
                prompt.Request.RequestId,
                CombatInteractionState.Resolving,
                BuildProgressMessage(prompt, selectedCount, eligibleCards.Count));
            var progress = prompt.Selection.Observe(selectedCount, Time.unscaledTime);
            if (progress == CombatSelectionProgress.TimedOut)
            {
                var timeoutReason = prompt.Selection.ConfirmIssued
                    ? "native hand prompt did not close after confirmation"
                    : "card selection produced no progress";
                CombatInteractionBroker.Transition(
                    prompt.Request.RequestId,
                    CombatInteractionState.Failed,
                    timeoutReason);
                handPrompt = null;
                return WitchInteractionResolveResult.Failed;
            }

            if (progress == CombatSelectionProgress.AwaitingNativeClose)
            {
                return WitchInteractionResolveResult.Pending;
            }

            if (progress == CombatSelectionProgress.Complete)
            {
                if (FightUI.SelectedCard.Any(card => card == null || !card.enabled))
                {
                    return WitchInteractionResolveResult.Pending;
                }

                var confirm = prompt.FightUi.ConfirmButton?.GetComponent<ButtonManager>();
                if (confirm == null || !confirm.gameObject.activeInHierarchy)
                {
                    return WitchInteractionResolveResult.Pending;
                }

                if (prompt.Selection.TryIssueConfirm(
                        selectedCount,
                        Time.unscaledTime))
                {
                    confirm.onClick.Invoke();
                    CombatInteractionBroker.Transition(
                        prompt.Request.RequestId,
                        CombatInteractionState.Resolving,
                        "confirm issued once; awaiting native close");
                }

                return WitchInteractionResolveResult.Pending;
            }

            if (progress == CombatSelectionProgress.Pending
                || FightUI.SelectedCard.Any(card => card == null || !card.enabled))
            {
                return WitchInteractionResolveResult.Pending;
            }

            if (eligibleCards.Count == 0)
            {
                if (prompt.NoEligibleSince < 0f)
                {
                    prompt.NoEligibleSince = Time.unscaledTime;
                    return WitchInteractionResolveResult.Pending;
                }
                if (Time.unscaledTime - prompt.NoEligibleSince <= 0.5f)
                {
                    return WitchInteractionResolveResult.Pending;
                }

                CombatInteractionBroker.Transition(
                    prompt.Request.RequestId,
                    CombatInteractionState.Failed,
                    "no eligible hand card can satisfy the prompt");
                handPrompt = null;
                return WitchInteractionResolveResult.Failed;
            }
            prompt.NoEligibleSince = -1f;

            var utilities = eligibleCards
                .Select(card => ToUtility(WitchCombatValueEstimator.Estimate(
                    card.dataConfig,
                    card is AttackCardItem,
                    CombatTargetKind.None)))
                .ToList();
            var selected = MultiSelectPlanner.ChooseIndices(
                utilities,
                1,
                preferLowest: prompt.Request.Hint.PreferLowestValue);
            if (selected.Count == 0)
            {
                CombatInteractionBroker.Transition(
                    prompt.Request.RequestId,
                    CombatInteractionState.Failed,
                    "selection policy returned no eligible hand card");
                handPrompt = null;
                return WitchInteractionResolveResult.Failed;
            }
            if (HandleSelectModeClick == null)
            {
                CombatInteractionBroker.Transition(
                    prompt.Request.RequestId,
                    CombatInteractionState.Failed,
                    "native CardItem.HandleSelectModeClick is unavailable");
                handPrompt = null;
                return WitchInteractionResolveResult.Failed;
            }
            if (EventSystem.current == null)
            {
                CombatInteractionBroker.Transition(
                    prompt.Request.RequestId,
                    CombatInteractionState.Failed,
                    "Unity EventSystem is unavailable for card selection");
                handPrompt = null;
                return WitchInteractionResolveResult.Failed;
            }
            if (!prompt.Selection.TryBeginAttempt(
                    selectedCount,
                    Time.unscaledTime))
            {
                return WitchInteractionResolveResult.Pending;
            }

            var selectedCard = eligibleCards[selected[0]];
            var eventData = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left
            };
            HandleSelectModeClick.Invoke(selectedCard, new object[] { eventData });
            CombatInteractionBroker.Transition(
                prompt.Request.RequestId,
                CombatInteractionState.Resolving,
                "selection attempt source=" + WitchCombatValueEstimator.IdOf(selectedCard.dataConfig)
                + ", selected=" + selectedCount
                + "/" + prompt.Selection.RequiredCount);
            return WitchInteractionResolveResult.Pending;
        }
        catch (Exception ex)
        {
            CombatInteractionBroker.Transition(prompt.Request.RequestId, CombatInteractionState.Failed, ex.Message);
            handPrompt = null;
            return WitchInteractionResolveResult.Failed;
        }
    }

    private static List<CardItem> EligibleHandCards(HandPrompt prompt)
    {
        var cards = FightUI.cardItemList ?? new List<CardItem>();
        var selected = FightUI.SelectedCard ?? new List<CardItem>();
        return cards
            .Where(card => card != null
                           && card.gameObject != null
                           && card.gameObject.activeInHierarchy
                           && card.enabled
                           && !card.hasUse
                           && card.selectContainer != null
                           && !selected.Contains(card)
                           && (!prompt.ExcludesFrozenCards || !card.Tags.Contains("Froze")))
            .ToList();
    }

    private static string BuildProgressMessage(HandPrompt prompt, int selectedCount, int eligibleCount)
    {
        return "prompt=" + prompt.Request.Hint.Kind
               + ", required=" + prompt.Selection.RequiredCount
               + ", selected=" + selectedCount
               + ", eligible=" + eligibleCount;
    }

    private static WitchInteractionResolveResult CompleteHandPrompt(HandPrompt prompt, string message)
    {
        CombatInteractionBroker.Transition(
            prompt.Request.RequestId,
            CombatInteractionState.Completed,
            message);
        CombatInteractionBroker.Clear(prompt.Request.RequestId);
        handPrompt = null;
        return WitchInteractionResolveResult.Completed;
    }

    private static CombatActionObservation CreateChoice(
        IDataConfig config,
        int index)
    {
        return new CombatActionObservation
        {
            ObservationId = "prompt",
            ActionToken = "prompt:" + index,
            CandidateId = "prompt:" + index + ":" + WitchCombatValueEstimator.IdOf(config),
            SourceId = WitchCombatValueEstimator.IdOf(config),
            DisplayName = WitchCombatValueEstimator.NameOf(config),
            Kind = CombatActionKind.ResolvePrompt,
            RuntimeId = index,
            Semantics = WitchCombatValueEstimator.Estimate(config, false, CombatTargetKind.None)
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
        public HandPrompt(
            FightUI fightUi,
            CombatInteractionRequest request,
            int initialHandCount,
            bool excludesFrozenCards,
            float createdAt)
        {
            FightUi = fightUi;
            Request = request;
            InitialHandCount = initialHandCount;
            ExcludesFrozenCards = excludesFrozenCards;
            CreatedAt = createdAt;
            Selection = new CombatPromptSelectionTracker(request.RequiredCount);
            NoEligibleSince = -1f;
        }

        public FightUI FightUi { get; }

        public CombatInteractionRequest Request { get; }

        public int InitialHandCount { get; }

        public bool ExcludesFrozenCards { get; }

        public float CreatedAt { get; }

        public bool UiObserved { get; set; }

        public CombatPromptSelectionTracker Selection { get; }

        public float NoEligibleSince { get; set; }
    }
}
