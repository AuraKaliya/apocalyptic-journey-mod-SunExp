using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public interface ICombatInteractionChoiceScorer
{
    bool TryScore(
        CombatInteractionHint hint,
        CombatActionObservation choice,
        out double score);
}

public sealed class CombatInteractionHint
{
    public string OwnerModId { get; set; } = "";

    public string Purpose { get; set; } = "";

    public string SourceId { get; set; } = "";

    public CombatPromptKind Kind { get; set; }

    public CombatPromptZone Zone { get; set; }

    public bool Forced { get; set; } = true;

    public bool PreferLowestValue { get; set; }

    public string ParentActionToken { get; set; } = "";

    public string ParentCandidateId { get; set; } = "";

    public CombatInteractionDefinition? Interaction { get; set; }

    public ICombatInteractionChoiceScorer? ChoiceScorer { get; set; }
}

public sealed class CombatInteractionRequest
{
    public long RequestId { get; set; }

    public CombatInteractionHint Hint { get; set; } = new();

    public int RequiredCount { get; set; }

    public int MinSelections { get; set; }

    public int MaxSelections { get; set; }

    public int TargetSelections { get; set; }

    public List<CombatActionObservation> Choices { get; set; } = new();

    public List<string> SelectedCandidateIds { get; set; } = new();

    public CombatInteractionState State { get; set; }

    public string Message { get; set; } = "";

    public DateTime CreatedUtc { get; set; }
}

public static class CombatInteractionBroker
{
    private static readonly object Gate = new();
    private static CombatInteractionHint? nextHint;
    private static CombatInteractionRequest? active;
    private static CombatTrainingInteractionTrace? completedTrace;
    private static long nextRequestId;

    public static void SetNextHint(CombatInteractionHint hint)
    {
        lock (Gate)
        {
            nextHint = hint;
        }
    }

    public static CombatInteractionHint ConsumeNextHint(CombatInteractionHint fallback)
    {
        lock (Gate)
        {
            var result = nextHint ?? fallback;
            nextHint = null;
            return result;
        }
    }

    public static void ClearNextHint()
    {
        lock (Gate)
        {
            nextHint = null;
        }
    }

    public static CombatInteractionRequest Begin(
        CombatInteractionHint fallbackHint,
        int requiredCount,
        IReadOnlyList<CombatActionObservation>? choices)
    {
        lock (Gate)
        {
            completedTrace = null;
            var hint = nextHint ?? fallbackHint ?? new CombatInteractionHint();
            var observedCount = Math.Max(0, requiredCount);
            var interaction = (hint.Interaction ?? new CombatInteractionDefinition
            {
                SourceApi = "native-observation",
                Kind = ToInteractionKind(hint.Kind),
                Zone = ToInteractionZone(hint.Zone),
                MinSelections = observedCount,
                MaxSelections = observedCount,
                EffectsComplete = false
            }).Normalize();
            // The live native argument is authoritative for dynamic counts;
            // compiled semantics supplies mode and callback effects.
            interaction.MaxSelections = observedCount;
            interaction.MinSelections = interaction.CanConfirmEarly
                                        || interaction.CanConfirmEmpty
                ? 0
                : observedCount;
            interaction = interaction.Normalize();
            hint.Interaction = interaction;
            active = new CombatInteractionRequest
            {
                RequestId = ++nextRequestId,
                Hint = hint,
                MinSelections = interaction.MinSelections,
                MaxSelections = interaction.MaxSelections,
                TargetSelections = interaction.MaxSelections,
                RequiredCount = interaction.MaxSelections,
                Choices = choices == null
                    ? new List<CombatActionObservation>()
                    : new List<CombatActionObservation>(choices),
                State = CombatInteractionState.AwaitingUi,
                CreatedUtc = DateTime.UtcNow
            };
            nextHint = null;
            return Clone(active);
        }
    }

    public static CombatInteractionRequest? Snapshot()
    {
        lock (Gate)
        {
            return active == null ? null : Clone(active);
        }
    }

    public static bool Transition(long requestId, CombatInteractionState state, string message = "")
    {
        lock (Gate)
        {
            if (active == null || active.RequestId != requestId)
            {
                return false;
            }

            active.State = state;
            active.Message = message ?? "";
            if (state is CombatInteractionState.Completed
                or CombatInteractionState.Failed
                or CombatInteractionState.HandedToPlayer)
            {
                completedTrace = ToTrace(active, state == CombatInteractionState.Completed);
            }
            return true;
        }
    }

    public static bool PublishVisibleChoices(
        long requestId,
        IReadOnlyList<CombatActionObservation>? choices)
    {
        lock (Gate)
        {
            if (active == null || active.RequestId != requestId)
            {
                return false;
            }
            active.Choices = choices == null
                ? new List<CombatActionObservation>()
                : new List<CombatActionObservation>(choices);
            return true;
        }
    }

    public static bool PublishPlan(
        long requestId,
        int targetSelections,
        IReadOnlyList<string>? selectedCandidateIds)
    {
        lock (Gate)
        {
            if (active == null || active.RequestId != requestId)
            {
                return false;
            }
            active.TargetSelections = Math.Max(
                active.MinSelections,
                Math.Min(active.MaxSelections, targetSelections));
            active.RequiredCount = active.TargetSelections;
            active.SelectedCandidateIds = selectedCandidateIds == null
                ? new List<string>()
                : selectedCandidateIds.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
            return true;
        }
    }

    public static CombatTrainingInteractionTrace? ConsumeCompletedTrace(
        string parentActionToken = "")
    {
        lock (Gate)
        {
            if (completedTrace == null
                || (!string.IsNullOrWhiteSpace(parentActionToken)
                    && !string.Equals(
                        completedTrace.ParentActionToken,
                        parentActionToken,
                        StringComparison.Ordinal)))
            {
                return null;
            }
            var result = completedTrace;
            completedTrace = null;
            return result;
        }
    }

    public static void Clear(long requestId = 0)
    {
        lock (Gate)
        {
            if (requestId == 0 || active?.RequestId == requestId)
            {
                active = null;
            }
            if (requestId == 0)
            {
                completedTrace = null;
            }
        }
    }

    private static CombatInteractionRequest Clone(CombatInteractionRequest source)
    {
        return new CombatInteractionRequest
        {
            RequestId = source.RequestId,
            Hint = source.Hint,
            RequiredCount = source.RequiredCount,
            MinSelections = source.MinSelections,
            MaxSelections = source.MaxSelections,
            TargetSelections = source.TargetSelections,
            Choices = new List<CombatActionObservation>(source.Choices),
            SelectedCandidateIds = new List<string>(source.SelectedCandidateIds),
            State = source.State,
            Message = source.Message,
            CreatedUtc = source.CreatedUtc
        };
    }

    private static CombatTrainingInteractionTrace ToTrace(
        CombatInteractionRequest source,
        bool completed)
    {
        var interaction = source.Hint.Interaction;
        return new CombatTrainingInteractionTrace
        {
            RequestId = source.RequestId,
            ParentActionToken = source.Hint.ParentActionToken,
            ParentCandidateId = source.Hint.ParentCandidateId,
            Kind = interaction?.Kind ?? ToInteractionKind(source.Hint.Kind),
            Zone = interaction?.Zone ?? ToInteractionZone(source.Hint.Zone),
            MinSelections = source.MinSelections,
            MaxSelections = source.MaxSelections,
            CanConfirmEarly = interaction?.CanConfirmEarly == true,
            EffectsComplete = interaction?.EffectsComplete == true,
            EligibleCandidateIds = source.Choices
                .Select(item => item.CandidateId ?? "")
                .Where(item => item.Length > 0)
                .ToList(),
            SelectedCandidateIds = new List<string>(source.SelectedCandidateIds),
            Completed = completed,
            CompletionReason = source.Message
        };
    }

    private static CombatInteractionKind ToInteractionKind(CombatPromptKind kind)
    {
        return kind switch
        {
            CombatPromptKind.BurnCards => CombatInteractionKind.BurnCards,
            CombatPromptKind.DiscardCards => CombatInteractionKind.DiscardCards,
            _ => CombatInteractionKind.ChooseCards
        };
    }

    private static CombatInteractionZone ToInteractionZone(CombatPromptZone zone)
    {
        return zone switch
        {
            CombatPromptZone.Hand => CombatInteractionZone.Hand,
            CombatPromptZone.Deck => CombatInteractionZone.Deck,
            CombatPromptZone.DiscardPile => CombatInteractionZone.DiscardPile,
            _ => CombatInteractionZone.Unknown
        };
    }
}
