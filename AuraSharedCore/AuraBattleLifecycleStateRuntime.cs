using System;

namespace AuraShared.Core;

public enum AuraBattleOutcome
{
    Win,
    Escape,
    Loss
}

public enum AuraBattleLifecyclePhase
{
    None,
    Initializing,
    Active,
    OutcomeEntering,
    Settling,
    Ended,
    Finalized,
    Restarting
}

public readonly struct AuraBattleLifecycleSnapshot
{
    internal AuraBattleLifecycleSnapshot(
        long sessionId,
        AuraBattleLifecyclePhase phase,
        AuraBattleOutcome? outcome)
    {
        SessionId = sessionId;
        Phase = phase;
        Outcome = outcome;
    }

    public long SessionId { get; }

    public AuraBattleLifecyclePhase Phase { get; }

    public AuraBattleOutcome? Outcome { get; }

    public bool AcceptsCombatPresentation =>
        SessionId > 0 && Phase == AuraBattleLifecyclePhase.Active;

    public bool HasEnteredOutcome =>
        Phase is AuraBattleLifecyclePhase.OutcomeEntering
            or AuraBattleLifecyclePhase.Settling
            or AuraBattleLifecyclePhase.Ended
            or AuraBattleLifecyclePhase.Finalized;
}

/// <summary>
/// Authoritative, semantic-free state for the native battle lifecycle. Consumers use
/// this state to close transient producers as soon as an outcome starts and to defer
/// terminal snapshots until every BattleEnded cleanup subscriber has completed.
/// </summary>
public static class AuraBattleLifecycleStateRuntime
{
    private static readonly object Gate = new();
    private static long sessionId;
    private static AuraBattleLifecyclePhase phase;
    private static AuraBattleOutcome? outcome;

    public static AuraBattleLifecycleSnapshot Current
    {
        get
        {
            lock (Gate)
            {
                return new AuraBattleLifecycleSnapshot(sessionId, phase, outcome);
            }
        }
    }

    public static AuraBattleLifecyclePhase CurrentPhase => Current.Phase;

    public static bool AcceptsCombatPresentation => Current.AcceptsCombatPresentation;

    public static bool HasEnteredOutcome => Current.HasEnteredOutcome;

    internal static void Begin(long battleSessionId)
    {
        if (battleSessionId <= 0) return;
        lock (Gate)
        {
            sessionId = battleSessionId;
            phase = AuraBattleLifecyclePhase.Initializing;
            outcome = null;
        }
    }

    internal static void Activate(long battleSessionId)
    {
        Transition(battleSessionId, AuraBattleLifecyclePhase.Active, null);
    }

    internal static void EnterOutcome(long battleSessionId, AuraBattleOutcome battleOutcome)
    {
        Transition(battleSessionId, AuraBattleLifecyclePhase.OutcomeEntering, battleOutcome);
    }

    internal static void EnterSettling(long battleSessionId, AuraBattleOutcome battleOutcome)
    {
        Transition(battleSessionId, AuraBattleLifecyclePhase.Settling, battleOutcome);
    }

    internal static void EnterEnded(long battleSessionId, AuraBattleOutcome battleOutcome)
    {
        Transition(battleSessionId, AuraBattleLifecyclePhase.Ended, battleOutcome);
    }

    internal static void EnterFinalized(long battleSessionId, AuraBattleOutcome battleOutcome)
    {
        Transition(battleSessionId, AuraBattleLifecyclePhase.Finalized, battleOutcome);
    }

    internal static void EnterRestarting(long battleSessionId)
    {
        Transition(battleSessionId, AuraBattleLifecyclePhase.Restarting, null);
    }

    internal static void End(long battleSessionId)
    {
        lock (Gate)
        {
            if (sessionId != battleSessionId) return;
            sessionId = 0;
            phase = AuraBattleLifecyclePhase.None;
            outcome = null;
        }
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            sessionId = 0;
            phase = AuraBattleLifecyclePhase.None;
            outcome = null;
        }
    }

    private static void Transition(
        long battleSessionId,
        AuraBattleLifecyclePhase nextPhase,
        AuraBattleOutcome? nextOutcome)
    {
        if (battleSessionId <= 0) return;
        lock (Gate)
        {
            if (sessionId != 0 && sessionId != battleSessionId) return;
            if (!CanTransition(phase, nextPhase)) return;
            sessionId = battleSessionId;
            phase = nextPhase;
            outcome = nextOutcome ?? outcome;
        }
    }

    private static bool CanTransition(
        AuraBattleLifecyclePhase current,
        AuraBattleLifecyclePhase next)
    {
        if (current == next) return true;
        return next switch
        {
            AuraBattleLifecyclePhase.Active => current == AuraBattleLifecyclePhase.Initializing,
            AuraBattleLifecyclePhase.OutcomeEntering =>
                current is AuraBattleLifecyclePhase.Initializing or AuraBattleLifecyclePhase.Active,
            AuraBattleLifecyclePhase.Settling => current == AuraBattleLifecyclePhase.OutcomeEntering,
            AuraBattleLifecyclePhase.Ended =>
                current is AuraBattleLifecyclePhase.OutcomeEntering or AuraBattleLifecyclePhase.Settling,
            AuraBattleLifecyclePhase.Finalized => current == AuraBattleLifecyclePhase.Ended,
            AuraBattleLifecyclePhase.Restarting =>
                current is not AuraBattleLifecyclePhase.None and not AuraBattleLifecyclePhase.Finalized,
            _ => false
        };
    }
}
