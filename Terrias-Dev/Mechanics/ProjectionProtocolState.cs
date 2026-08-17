using System;
using System.Collections.Generic;

namespace Terrias.Dll.Mechanics;

public enum ProjectionSummonFailureCode
{
    None,
    TransportNotSent,
    ProtocolMismatch,
    BattleEpochMismatch,
    CardModelMismatch,
    RoleDeckUnavailable,
    RoleDeckTimedOut,
    UnknownRole,
    MissingSender,
    SenderOutsideLobby,
    OwnerMismatch,
    TokenConflict,
    OwnerAlreadyHasProjection,
    FriendlySeatsFull,
    SeatReservationExpired,
    SpawnFailed,
    Cancelled
}

public enum ProjectionSummonFailureCategory
{
    None,
    Transport,
    Compatibility,
    Synchronization,
    Authorization,
    Capacity,
    Content,
    Runtime,
    Cancelled
}

public sealed class ProjectionSummonFailureDescriptor
{
    public ProjectionSummonFailureDescriptor(
        ProjectionSummonFailureCode code,
        ProjectionSummonFailureCategory category,
        bool terminal,
        bool retryable,
        bool refundCard,
        string message)
    {
        Code = code;
        Category = category;
        Terminal = terminal;
        Retryable = retryable;
        RefundCard = refundCard;
        Message = message ?? "";
    }

    public ProjectionSummonFailureCode Code { get; }
    public ProjectionSummonFailureCategory Category { get; }
    public bool Terminal { get; }
    public bool Retryable { get; }
    public bool RefundCard { get; }
    public string Message { get; }
}

public static class ProjectionSummonFailureCatalog
{
    public static string LocalizationKey(ProjectionSummonFailureCode code)
    {
        var normalized = code != ProjectionSummonFailureCode.None
                         && Enum.IsDefined(typeof(ProjectionSummonFailureCode), code)
            ? code
            : ProjectionSummonFailureCode.SpawnFailed;
        return "caption.projection.failure." + normalized;
    }

    public static ProjectionSummonFailureDescriptor Describe(ProjectionSummonFailureCode code)
    {
        return code switch
        {
            ProjectionSummonFailureCode.None => New(code, ProjectionSummonFailureCategory.None, true, false, false, ""),
            ProjectionSummonFailureCode.TransportNotSent => New(code, ProjectionSummonFailureCategory.Transport, true, false, true, "网络尚未建立，投影卡牌已返还。"),
            ProjectionSummonFailureCode.ProtocolMismatch => New(code, ProjectionSummonFailureCategory.Compatibility, true, false, true, "投影协议版本不一致。"),
            ProjectionSummonFailureCode.BattleEpochMismatch => New(code, ProjectionSummonFailureCategory.Synchronization, true, false, true, "当前战斗状态已失效，请重新使用。"),
            ProjectionSummonFailureCode.CardModelMismatch => New(code, ProjectionSummonFailureCategory.Compatibility, true, false, true, "投影卡牌模型版本不一致。"),
            ProjectionSummonFailureCode.RoleDeckUnavailable => New(code, ProjectionSummonFailureCategory.Synchronization, false, true, false, "主机正在同步你的牌组。"),
            ProjectionSummonFailureCode.RoleDeckTimedOut => New(code, ProjectionSummonFailureCategory.Synchronization, true, false, true, "主机未能取得你的牌组，投影卡牌已返还。"),
            ProjectionSummonFailureCode.UnknownRole => New(code, ProjectionSummonFailureCategory.Content, true, false, true, "投影目标已失效。"),
            ProjectionSummonFailureCode.MissingSender => New(code, ProjectionSummonFailureCategory.Authorization, true, false, false, "无法确认操作玩家。"),
            ProjectionSummonFailureCode.SenderOutsideLobby => New(code, ProjectionSummonFailureCategory.Authorization, true, false, false, "操作玩家不在当前房间中。"),
            ProjectionSummonFailureCode.OwnerMismatch => New(code, ProjectionSummonFailureCategory.Authorization, true, false, false, "当前角色不属于该玩家。"),
            ProjectionSummonFailureCode.TokenConflict => New(code, ProjectionSummonFailureCategory.Authorization, true, false, false, "投影同步标识与原请求不一致。"),
            ProjectionSummonFailureCode.OwnerAlreadyHasProjection => New(code, ProjectionSummonFailureCategory.Capacity, true, false, true, "投影位置已被占用。"),
            ProjectionSummonFailureCode.FriendlySeatsFull => New(code, ProjectionSummonFailureCategory.Capacity, true, false, true, "友方角色位置已达到4人上限。"),
            ProjectionSummonFailureCode.SeatReservationExpired => New(code, ProjectionSummonFailureCategory.Synchronization, true, false, true, "投影位置预约已失效。"),
            ProjectionSummonFailureCode.SpawnFailed => New(code, ProjectionSummonFailureCategory.Runtime, true, false, true, "投影召唤失败，请稍后重试。"),
            ProjectionSummonFailureCode.Cancelled => New(code, ProjectionSummonFailureCategory.Cancelled, true, false, true, "投影同步已取消。"),
            _ => New(code, ProjectionSummonFailureCategory.Runtime, true, false, true, "投影召唤失败，请稍后重试。")
        };
    }

    private static ProjectionSummonFailureDescriptor New(
        ProjectionSummonFailureCode code,
        ProjectionSummonFailureCategory category,
        bool terminal,
        bool retryable,
        bool refund,
        string message)
    {
        return new ProjectionSummonFailureDescriptor(code, category, terminal, retryable, refund, message);
    }
}

public sealed class ProjectionReplicationClock
{
    public ProjectionReplicationClock(string generation, long initialStateRevision = 1L)
    {
        Generation = string.IsNullOrWhiteSpace(generation)
            ? Guid.NewGuid().ToString("N")
            : generation.Trim();
        Active = true;
        StateRevision = Math.Max(0L, initialStateRevision);
    }

    public string Generation { get; }
    public long StateRevision { get; private set; }
    public long ActionSequence { get; private set; }
    public long CompletedTurnSequence { get; private set; }
    public bool Active { get; private set; }

    public bool MatchesActiveGeneration(string generation)
    {
        return Active && string.Equals(Generation, generation ?? "", StringComparison.Ordinal);
    }

    public void CommitAction()
    {
        if (!Active) return;
        ActionSequence++;
        StateRevision++;
    }

    public void CompleteTurn()
    {
        if (!Active) return;
        CompletedTurnSequence++;
        StateRevision++;
    }

    public void Touch()
    {
        if (Active) StateRevision++;
    }

    public void Retire()
    {
        if (!Active) return;
        Active = false;
        StateRevision++;
    }

    public bool TryApplyRemote(
        string generation,
        long stateRevision,
        long actionSequence,
        long completedTurnSequence,
        bool active)
    {
        if (!string.Equals(Generation, generation ?? "", StringComparison.Ordinal)
            || stateRevision <= StateRevision
            || !Active && active)
        {
            return false;
        }

        StateRevision = Math.Max(StateRevision, stateRevision);
        ActionSequence = Math.Max(ActionSequence, actionSequence);
        CompletedTurnSequence = Math.Max(CompletedTurnSequence, completedTurnSequence);
        Active = active;
        return true;
    }
}

public sealed class ProjectionRemoteTurnGate
{
    private long completed;
    private long consumed;
    private double lastProgressAt;
    private double lastQueryAt = double.NegativeInfinity;
    private long lastAction;
    private long lastRevision;

    public long Completed => completed;
    public long Consumed => consumed;
    public double LastProgressAt => lastProgressAt;

    public void Observe(long completedTurnSequence, long actionSequence, long stateRevision, double now)
    {
        var next = Math.Max(0L, completedTurnSequence);
        if (next > completed
            || actionSequence > lastAction
            || stateRevision > lastRevision)
        {
            completed = Math.Max(completed, next);
            lastAction = Math.Max(lastAction, actionSequence);
            lastRevision = Math.Max(lastRevision, stateRevision);
            lastProgressAt = now;
        }
    }

    public long BeginInvocation()
    {
        return consumed + 1L;
    }

    public bool IsSatisfied(long expected)
    {
        return completed >= expected;
    }

    public void Consume(long expected)
    {
        if (completed >= expected)
        {
            consumed = Math.Max(consumed, completed);
        }
    }

    public void Release(long expected)
    {
        consumed = Math.Max(consumed, expected);
    }

    public bool ShouldQuery(double now, double idleSeconds, double minimumIntervalSeconds)
    {
        return now - lastProgressAt >= Math.Max(0.1d, idleSeconds)
               && now - lastQueryAt >= Math.Max(0.1d, minimumIntervalSeconds);
    }

    public void MarkQuery(double now)
    {
        lastQueryAt = now;
    }
}

public sealed class ProjectionSummonTransaction
{
    public ProjectionSummonTransaction(
        string token,
        string roleId,
        string ownerStatusId,
        string deckRecipeHash,
        double now)
    {
        Token = token ?? "";
        RoleId = roleId ?? "";
        OwnerStatusId = ownerStatusId ?? "";
        DeckRecipeHash = deckRecipeHash ?? "";
        CreatedAt = now;
        LastAttemptAt = double.NegativeInfinity;
    }

    public string Token { get; }
    public string RoleId { get; }
    public string OwnerStatusId { get; }
    public string DeckRecipeHash { get; }
    public double CreatedAt { get; }
    public double LastAttemptAt { get; private set; }
    public int Attempts { get; private set; }
    public bool Terminal { get; private set; }
    public bool Refunded { get; private set; }
    public bool TimeoutReported { get; set; }

    public bool IsDue(double now, double retryInterval)
    {
        return !Terminal && now - LastAttemptAt >= retryInterval;
    }

    public void MarkAttempt(double now)
    {
        LastAttemptAt = now;
        Attempts++;
    }

    public void SetTerminal()
    {
        Terminal = true;
    }

    public bool TryClaimRefund()
    {
        if (Refunded) return false;
        Refunded = true;
        return true;
    }
}

public sealed class ProjectionSummonRequestIdentity
{
    public ProjectionSummonRequestIdentity(
        string roleId,
        string ownerPlayerId,
        string ownerStatusId,
        string deckRecipeHash)
    {
        RoleId = roleId ?? "";
        OwnerPlayerId = ownerPlayerId ?? "";
        OwnerStatusId = ownerStatusId ?? "";
        DeckRecipeHash = deckRecipeHash ?? "";
    }

    public string RoleId { get; }
    public string OwnerPlayerId { get; }
    public string OwnerStatusId { get; }
    public string DeckRecipeHash { get; }

    public bool Matches(
        string roleId,
        string ownerPlayerId,
        string ownerStatusId,
        string deckRecipeHash)
    {
        return string.Equals(RoleId, roleId ?? "", StringComparison.Ordinal)
               && string.Equals(OwnerPlayerId, ownerPlayerId ?? "", StringComparison.Ordinal)
               && string.Equals(OwnerStatusId, ownerStatusId ?? "", StringComparison.Ordinal)
               && string.Equals(DeckRecipeHash, deckRecipeHash ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
