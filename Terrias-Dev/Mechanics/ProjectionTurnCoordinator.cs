using Terrias.Dll.Contracts;
using System;
using System.Collections;
using System.Linq;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class ProjectionTurnCoordinator
{
    private static readonly ProjectionSummonTurnTransactionLedger Transactions = new();
    private static int roundSequence;
    private static int continuationEpoch;
    private static bool continuationActive;
    private static int playerTurnCompletedRound;
    private static Action<ProjectionSummonTurnTransaction, string>? authoritativePublisher;

    public static int CurrentRoundSequence => roundSequence;

    public static bool TryGetTransaction(
        string token,
        out ProjectionSummonTurnTransaction transaction)
    {
        return Transactions.TryGet(token, out transaction);
    }

    public static void ConfigureAuthoritativePublisher(
        Action<ProjectionSummonTurnTransaction, string> publisher)
    {
        authoritativePublisher = publisher;
    }

    public static void BeginBattle(string source)
    {
        continuationEpoch++;
        continuationActive = false;
        roundSequence = 0;
        playerTurnCompletedRound = 0;
        Transactions.Clear();
        TerriasLog.InfoAlways("[PartnerTurn] authoritative summon-turn transactions reset: source=" + source);
    }

    public static void BeginPlayerRound(string source)
    {
        roundSequence = Math.Max(1, roundSequence + 1);
        playerTurnCompletedRound = 0;
        var stale = Transactions.BeginRound(roundSequence);
        if (stale.Count > 0)
        {
            TerriasLog.Error("[PartnerTurn] stale open summon-turn transactions crossed a round boundary: round="
                             + roundSequence
                             + ", tokens="
                             + string.Join(",", stale.Select(value => value.Token)));
        }
        TerriasPerformanceCounters.Record("ProjectionTurnCoordinator.RoundStarted");
        TerriasLog.InfoAlways("[PartnerTurn] player round opened: round="
                              + roundSequence
                              + ", source="
                              + source);
    }

    public static bool TryReserveAuthoritative(
        string token,
        string source,
        out ProjectionSummonTurnTransaction transaction,
        out string reason)
    {
        transaction = new ProjectionSummonTurnTransaction();
        if (!CompanionAuthorityService.IsAuthoritative())
        {
            reason = "projection summon turn reservation requires host authority";
            LogRejected("reserve", token, source, reason);
            return false;
        }

        var reserved = Transactions.Reserve(token, roundSequence, out reason);
        if (reserved == null)
        {
            LogRejected("reserve", token, source, reason);
            return false;
        }

        transaction = reserved;
        PublishAuthoritative(transaction, source + ".Reserved");
        TryStartContinuation(source + ".Reserved");
        return true;
    }

    public static bool TryMarkReadyAuthoritative(
        string token,
        string statusId,
        string generation,
        string source)
    {
        if (!CompanionAuthorityService.IsAuthoritative())
        {
            LogRejected("ready", token, source, "projection summon turn ready transition requires host authority");
            return false;
        }
        if (!Transactions.TryMarkReady(
                token,
                statusId,
                generation,
                out var snapshot,
                out var reason))
        {
            LogRejected("ready", token, source, reason);
            return false;
        }

        PublishAuthoritative(snapshot, source + ".Ready");
        TryStartContinuation(source + ".Ready");
        return true;
    }

    public static bool TryMarkFailedAuthoritative(
        string token,
        string detail,
        string source)
    {
        if (!CompanionAuthorityService.IsAuthoritative())
        {
            LogRejected("failed", token, source, "projection summon turn failure transition requires host authority");
            return false;
        }
        if (!Transactions.TryMarkFailed(token, detail, out var snapshot, out var reason))
        {
            LogRejected("failed", token, source, reason);
            return false;
        }

        PublishAuthoritative(snapshot, source + ".Failed");
        TryStartContinuation(source + ".Failed");
        return true;
    }

    public static bool ApplyAuthoritativeTransaction(
        ProjectionSummonTurnTransaction transaction,
        string source)
    {
        if (!Transactions.TryApplyAuthoritative(transaction, out var reason))
        {
            LogRejected("apply", transaction?.Token ?? "", source, reason);
            return false;
        }

        LogState(transaction, source + ".Applied");
        TryStartContinuation(source + ".Applied");
        return true;
    }

    public static void CompletePlayerTurnWithPendingProjections(string source)
    {
        playerTurnCompletedRound = roundSequence;
        TerriasLog.InfoAlways("[PartnerTurn] native player-turn completion observed: round="
                              + roundSequence
                              + ", openTransactions="
                              + Transactions.OpenCount(roundSequence)
                              + ", source="
                              + source);
        TryStartContinuation(source);
    }

    public static void RegisterCompanion(OtherObj companion, string source)
    {
        if (companion == null || FightManager.Instance?.ActionQueue == null)
        {
            TerriasLog.Warn("[PartnerTurn] native companion queue rejected: source="
                            + source
                            + ", companion="
                            + (companion?.InstanceId ?? "<missing>"));
            return;
        }

        var queue = FightManager.Instance.ActionQueue;
        queue.RemoveAll(item => item == null || ReferenceEquals(item, companion));
        var enemyIndex = queue.FindIndex(item => item is Enemy);
        if (enemyIndex < 0)
        {
            queue.Add(companion);
        }
        else
        {
            queue.Insert(enemyIndex, companion);
        }
        TerriasPerformanceCounters.Record("ProjectionTurnCoordinator.NativePartnerQueued");
        TerriasLog.InfoAlways("[PartnerTurn] companion queued for future native rounds: status="
                              + companion.InstanceId
                              + ", source="
                              + source);
    }

    public static void ClearBattle(string source)
    {
        continuationEpoch++;
        continuationActive = false;
        Transactions.Clear();
        playerTurnCompletedRound = 0;
        if (FightPlayer.Instance != null)
        {
            FightPlayer.Instance.isEnd = true;
        }
        roundSequence = 0;
        TerriasPerformanceCounters.Record("ProjectionTurnCoordinator.Cleared");
        TerriasLog.InfoAlways("[PartnerTurn] summon-turn transactions cleared: source=" + source);
    }

    private static void TryStartContinuation(string source)
    {
        var player = FightPlayer.Instance;
        var manager = FightManager.Instance;
        if (player == null
            || manager == null
            || !ProjectionSummonTurnBarrierPolicy.ShouldAcquire(
                roundSequence,
                playerTurnCompletedRound,
                Transactions.OpenCount(roundSequence),
                continuationActive,
                IsBattleActive()))
        {
            return;
        }

        var epoch = continuationEpoch;
        continuationActive = true;
        player.isEnd = false;
        TerriasLog.InfoAlways("[PartnerTurn] summon-round continuation acquired native barrier: round="
                              + roundSequence
                              + ", openTransactions="
                              + Transactions.OpenCount(roundSequence)
                              + ", authority="
                              + CompanionAuthorityService.IsAuthoritative()
                              + ", source="
                              + source);
        try
        {
            manager.StartCoroutine(RunSameRoundContinuation(roundSequence, epoch, source));
        }
        catch (Exception ex)
        {
            continuationActive = false;
            player.isEnd = true;
            TerriasLog.Error("[PartnerTurn] summon-round continuation failed to start from " + source, ex);
        }
    }

    private static IEnumerator RunSameRoundContinuation(
        int round,
        int epoch,
        string source)
    {
        try
        {
            while (epoch == continuationEpoch
                   && round == roundSequence
                   && IsBattleActive()
                   && Transactions.OpenCount(round) > 0)
            {
                if (!CompanionAuthorityService.IsAuthoritative())
                {
                    yield return null;
                    continue;
                }

                if (!Transactions.TryClaimReady(round, out var transaction))
                {
                    // An accepted summon is still Reserved. Its synchronous
                    // authoritative spawn path must resolve it to Ready or
                    // Failed before this native barrier is released.
                    yield return null;
                    continue;
                }

                var state = ProjectionStateStore.Find(transaction.StatusId);
                var projection = state?.Projection;
                if (projection == null
                    || projection.Status == null
                    || projection.Status.state == IStatusManager.State.Dead
                    || !string.Equals(
                        state!.Replication.Generation,
                        transaction.Generation,
                        StringComparison.Ordinal))
                {
                    TryMarkFailedAuthoritative(
                        transaction.Token,
                        "projection summon turn lost its ready actor",
                        source + ".ActorUnavailable");
                    continue;
                }

                var actionSequenceBefore = state.Replication.ActionSequence;
                TerriasPerformanceCounters.Record("ProjectionTurnCoordinator.SameRoundStarted");
                TerriasLog.InfoAlways("[PartnerTurn] summon-round projection action started: token="
                                      + transaction.Token
                                      + ", status="
                                      + transaction.StatusId
                                      + ", round="
                                      + round
                                      + ", order="
                                      + transaction.Order
                                      + ", source="
                                      + source);
                var execution = new ProjectionTurnExecutionResult();
                yield return RunProjectionSafely(projection, round, execution);
                var actionSequenceAfter = state.Replication.ActionSequence;
                if (execution.Failure != null || actionSequenceAfter <= actionSequenceBefore)
                {
                    TryMarkFailedAuthoritative(
                        transaction.Token,
                        ProjectionActionFailureDetail(projection, execution.Failure),
                        source + ".ActionFailed");
                    continue;
                }

                if (!Transactions.TryMarkCompleted(
                        transaction.Token,
                        out var completed,
                        out var completionReason))
                {
                    LogRejected("complete", transaction.Token, source, completionReason);
                    TryMarkFailedAuthoritative(
                        transaction.Token,
                        completionReason,
                        source + ".CompletionRejected");
                    continue;
                }
                PublishAuthoritative(completed, source + ".Completed");
                TerriasPerformanceCounters.Record("ProjectionTurnCoordinator.SameRoundCompleted");
            }
        }
        finally
        {
            if (epoch == continuationEpoch)
            {
                continuationActive = false;
                if (FightPlayer.Instance != null)
                {
                    FightPlayer.Instance.isEnd = true;
                }
                TerriasLog.InfoAlways("[PartnerTurn] summon-round continuation released native barrier: round="
                                      + round
                                      + ", remaining="
                                      + Transactions.OpenCount(round)
                                      + ", source="
                                      + source);
            }
        }
    }

    private static IEnumerator RunProjectionSafely(
        ProjectionOtherObj projection,
        int round,
        ProjectionTurnExecutionResult result)
    {
        var routine = projection.DoAction();
        while (true)
        {
            object? current = null;
            var moved = false;
            try
            {
                moved = routine.MoveNext();
                if (moved) current = routine.Current;
            }
            catch (Exception ex)
            {
                result.Failure = ex;
                TerriasLog.Error("[PartnerTurn] summon-round projection action failed: status="
                                 + projection.InstanceId
                                 + ", round="
                                 + round, ex);
                yield break;
            }

            if (!moved)
            {
                yield break;
            }

            yield return current;
        }
    }

    private static void PublishAuthoritative(
        ProjectionSummonTurnTransaction transaction,
        string source)
    {
        LogState(transaction, source);
        try
        {
            authoritativePublisher?.Invoke(transaction.Clone(), source);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[PartnerTurn] authoritative transaction publish failed: token="
                             + transaction.Token
                             + ", source="
                             + source, ex);
        }
    }

    private static void LogState(ProjectionSummonTurnTransaction transaction, string source)
    {
        TerriasLog.InfoAlways("[PartnerTurn] summon-turn transaction: token="
                              + transaction.Token
                              + ", round="
                              + transaction.RoundSequence
                              + ", order="
                              + transaction.Order
                              + ", revision="
                              + transaction.Revision
                              + ", state="
                              + transaction.State
                              + ", status="
                              + (string.IsNullOrWhiteSpace(transaction.StatusId)
                                  ? "<pending>"
                                  : transaction.StatusId)
                              + ", detail="
                              + (string.IsNullOrWhiteSpace(transaction.Detail)
                                  ? "<none>"
                                  : transaction.Detail.Replace('\r', ' ').Replace('\n', ' '))
                              + ", source="
                              + source);
    }

    private static string ProjectionActionFailureDetail(
        ProjectionOtherObj projection,
        Exception? failure)
    {
        if (failure != null)
        {
            return failure.Message;
        }

        var result = projection.LastAutoTurnResult;
        if (result == null)
        {
            return "projection completed without playing a card or producing an autonomous-turn result";
        }

        return "projection autonomous turn ended without a committed card: reason="
               + result.Reason
               + ", forced="
               + result.Forced
               + ", actions="
               + result.CommittedActions
               + ", failures="
               + result.ConsecutiveFailures
               + ", message="
               + (string.IsNullOrWhiteSpace(result.Message)
                   ? "<none>"
                   : result.Message);
    }

    private static void LogRejected(string phase, string token, string source, string reason)
    {
        TerriasLog.Warn("[PartnerTurn] summon-turn transition rejected: phase="
                        + phase
                        + ", token="
                        + (string.IsNullOrWhiteSpace(token) ? "<missing>" : token)
                        + ", round="
                        + roundSequence
                        + ", source="
                        + source
                        + ", reason="
                        + reason);
    }

    private static bool IsBattleActive()
    {
        var manager = FightManager.Instance;
        return manager != null
               && manager.fightType is not (FightType.None
                   or FightType.Win
                   or FightType.Loss
                   or FightType.Escape);
    }

    private sealed class ProjectionTurnExecutionResult
    {
        public Exception? Failure { get; set; }
    }
}
