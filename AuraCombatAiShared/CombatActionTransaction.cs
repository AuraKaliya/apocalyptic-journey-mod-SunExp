using System;

namespace AuraCombatAi.Shared;

public enum CombatActionTransactionState
{
    Idle,
    ExecutingRoot,
    AwaitingPrompt,
    Selecting,
    AwaitingSettlement,
    Completed,
    HandedOff,
    Failed,
    TimedOut,
    Cancelled
}

public sealed class CombatActionTransaction
{
    private long nextTransactionId;

    public long TransactionId { get; private set; }

    public long BattleSessionId { get; private set; }

    public string CandidateId { get; private set; } = "";

    public double StartedAt { get; private set; }

    public double Deadline { get; private set; }

    public int SubmitCount { get; private set; }

    public CombatActionTransactionState State { get; private set; }

    public string TerminalReason { get; private set; } = "";

    public bool IsActive =>
        State == CombatActionTransactionState.ExecutingRoot
        || State == CombatActionTransactionState.AwaitingPrompt
        || State == CombatActionTransactionState.Selecting
        || State == CombatActionTransactionState.AwaitingSettlement;

    public bool IsTerminal =>
        State == CombatActionTransactionState.Completed
        || State == CombatActionTransactionState.HandedOff
        || State == CombatActionTransactionState.Failed
        || State == CombatActionTransactionState.TimedOut
        || State == CombatActionTransactionState.Cancelled;

    public bool TryBegin(
        long battleSessionId,
        string candidateId,
        double now,
        double timeoutSeconds)
    {
        if (IsActive)
        {
            return false;
        }

        TransactionId = ++nextTransactionId;
        BattleSessionId = battleSessionId;
        CandidateId = candidateId ?? "";
        StartedAt = now;
        Deadline = now + Math.Max(0.001d, timeoutSeconds);
        SubmitCount = 1;
        State = CombatActionTransactionState.ExecutingRoot;
        TerminalReason = "";
        return true;
    }

    public bool CheckDeadline(double now)
    {
        if (!IsActive || now <= Deadline)
        {
            return false;
        }

        TransitionTerminal(CombatActionTransactionState.TimedOut, "action transaction timed out");
        return true;
    }

    public void AwaitPrompt()
    {
        if (IsActive)
        {
            State = CombatActionTransactionState.AwaitingPrompt;
        }
    }

    public void Selecting()
    {
        if (IsActive)
        {
            State = CombatActionTransactionState.Selecting;
        }
    }

    public void AwaitSettlement()
    {
        if (IsActive)
        {
            State = CombatActionTransactionState.AwaitingSettlement;
        }
    }

    public void Complete(string reason = "settled")
    {
        TransitionTerminal(CombatActionTransactionState.Completed, reason);
    }

    public void Fail(string reason)
    {
        TransitionTerminal(CombatActionTransactionState.Failed, reason);
    }

    public void HandOff(string reason)
    {
        TransitionTerminal(CombatActionTransactionState.HandedOff, reason);
    }

    public void Cancel(string reason)
    {
        TransitionTerminal(CombatActionTransactionState.Cancelled, reason);
    }

    public void Reset()
    {
        TransactionId = 0;
        BattleSessionId = 0;
        CandidateId = "";
        StartedAt = 0d;
        Deadline = 0d;
        SubmitCount = 0;
        State = CombatActionTransactionState.Idle;
        TerminalReason = "";
    }

    private void TransitionTerminal(CombatActionTransactionState state, string reason)
    {
        if (!IsActive && State != CombatActionTransactionState.ExecutingRoot)
        {
            return;
        }

        State = state;
        TerminalReason = reason ?? "";
    }
}

public enum CombatSelectionProgress
{
    Ready,
    Pending,
    Advanced,
    Complete,
    AwaitingNativeClose,
    TimedOut
}

public sealed class CombatPromptSelectionTracker
{
    private readonly double attemptTimeoutSeconds;
    private readonly double nativeCloseTimeoutSeconds;
    private int selectedBeforeAttempt;
    private double attemptStartedAt;
    private double confirmIssuedAt;

    public CombatPromptSelectionTracker(
        int requiredCount,
        double attemptTimeoutSeconds = 0.8d,
        double nativeCloseTimeoutSeconds = 2d)
    {
        RequiredCount = Math.Max(1, requiredCount);
        this.attemptTimeoutSeconds = Math.Max(0.05d, attemptTimeoutSeconds);
        this.nativeCloseTimeoutSeconds = Math.Max(
            0.1d,
            nativeCloseTimeoutSeconds);
    }

    public int RequiredCount { get; private set; }

    public bool AttemptInFlight { get; private set; }

    public bool ConfirmIssued { get; private set; }

    public void SetRequiredCount(int requiredCount)
    {
        RequiredCount = Math.Max(1, requiredCount);
    }

    public CombatSelectionProgress Observe(int selectedCount, double now)
    {
        if (ConfirmIssued)
        {
            return now - confirmIssuedAt > nativeCloseTimeoutSeconds
                ? CombatSelectionProgress.TimedOut
                : CombatSelectionProgress.AwaitingNativeClose;
        }

        if (selectedCount >= RequiredCount)
        {
            AttemptInFlight = false;
            return CombatSelectionProgress.Complete;
        }

        if (!AttemptInFlight)
        {
            return CombatSelectionProgress.Ready;
        }

        if (selectedCount > selectedBeforeAttempt)
        {
            AttemptInFlight = false;
            return CombatSelectionProgress.Advanced;
        }

        return now - attemptStartedAt > attemptTimeoutSeconds
            ? CombatSelectionProgress.TimedOut
            : CombatSelectionProgress.Pending;
    }

    public bool TryBeginAttempt(int selectedCount, double now)
    {
        if (AttemptInFlight || ConfirmIssued || selectedCount >= RequiredCount)
        {
            return false;
        }

        selectedBeforeAttempt = selectedCount;
        attemptStartedAt = now;
        AttemptInFlight = true;
        return true;
    }

    public bool TryIssueConfirm(int selectedCount, double now = 0d)
    {
        if (ConfirmIssued || AttemptInFlight || selectedCount < RequiredCount)
        {
            return false;
        }

        ConfirmIssued = true;
        confirmIssuedAt = now;
        return true;
    }
}
