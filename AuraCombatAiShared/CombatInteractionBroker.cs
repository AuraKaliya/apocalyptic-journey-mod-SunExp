using System;
using System.Collections.Generic;

namespace AuraCombatAi.Shared;

public sealed class CombatInteractionHint
{
    public string OwnerModId { get; set; } = "";

    public string Purpose { get; set; } = "";

    public CombatPromptKind Kind { get; set; }

    public CombatPromptZone Zone { get; set; }

    public bool Forced { get; set; } = true;

    public bool PreferLowestValue { get; set; }
}

public sealed class CombatInteractionRequest
{
    public long RequestId { get; set; }

    public CombatInteractionHint Hint { get; set; } = new();

    public int RequiredCount { get; set; }

    public List<CombatActionObservation> Choices { get; set; } = new();

    public CombatInteractionState State { get; set; }

    public string Message { get; set; } = "";

    public DateTime CreatedUtc { get; set; }
}

public static class CombatInteractionBroker
{
    private static readonly object Gate = new();
    private static CombatInteractionHint? nextHint;
    private static CombatInteractionRequest? active;
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
            active = new CombatInteractionRequest
            {
                RequestId = ++nextRequestId,
                Hint = nextHint ?? fallbackHint ?? new CombatInteractionHint(),
                RequiredCount = Math.Max(1, requiredCount),
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
            return true;
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
        }
    }

    private static CombatInteractionRequest Clone(CombatInteractionRequest source)
    {
        return new CombatInteractionRequest
        {
            RequestId = source.RequestId,
            Hint = source.Hint,
            RequiredCount = source.RequiredCount,
            Choices = new List<CombatActionObservation>(source.Choices),
            State = source.State,
            Message = source.Message,
            CreatedUtc = source.CreatedUtc
        };
    }
}
