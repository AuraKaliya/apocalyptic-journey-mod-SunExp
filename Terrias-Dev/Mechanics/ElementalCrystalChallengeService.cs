using Terrias.Dll.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Network;

namespace Terrias.Dll.Mechanics;

[Serializable]
public sealed class ElementalCrystalEventSnapshot
{
    public int ProtocolVersion { get; set; } = ElementalCrystalChallengeService.ProtocolVersion;

    public int BattleEpoch { get; set; }

    public string EventId { get; set; } = "";

    public string SourceStatusId { get; set; } = "";

    public string TriggerTargetStatusId { get; set; } = "";

    public string TimeoutBeneficiaryStatusId { get; set; } = "";

    public long CreatedAtUnixMilliseconds { get; set; }

    public long ExpiresAtUnixMilliseconds { get; set; }
}

[Serializable]
public sealed class ElementalCrystalResolutionSnapshot
{
    public int ProtocolVersion { get; set; } = ElementalCrystalChallengeService.ProtocolVersion;

    public int BattleEpoch { get; set; }

    public string EventId { get; set; } = "";

    public string BeneficiaryStatusId { get; set; } = "";

    public int Shield { get; set; }

    public bool Claimed { get; set; }
}

public static class ElementalCrystalChallengeService
{
    public const int ProtocolVersion = 1;
    public const double LifetimeSeconds = 4.0;

    private const int PendingCap = 8;
    private static readonly Dictionary<string, ElementalCrystalEventSnapshot> Pending = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Observed = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> CreateRequestTokens = new(StringComparer.Ordinal);
    private static int battleEpoch;
    private static long nextEventSequence;

    public static event Action<ElementalCrystalEventSnapshot>? Spawned;

    public static event Action<ElementalCrystalResolutionSnapshot>? Resolved;

    public static int BattleEpoch => battleEpoch;

    public static long NowUnixMilliseconds => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static void BeginBattle()
    {
        battleEpoch = battleEpoch == int.MaxValue ? 1 : battleEpoch + 1;
        nextEventSequence = 0;
        Pending.Clear();
        Observed.Clear();
        CreateRequestTokens.Clear();
    }

    public static void EndBattle(string source)
    {
        Pending.Clear();
        Observed.Clear();
        CreateRequestTokens.Clear();
        Resolved?.Invoke(new ElementalCrystalResolutionSnapshot
        {
            BattleEpoch = battleEpoch,
            EventId = "*"
        });
        TerriasLog.Debug("[ElementalCrystal] cleared battle state from " + source + ".");
    }

    public static bool Create(IStatusManager? source, IStatusManager? triggerTarget, string origin)
    {
        if (source == null || triggerTarget == null || TerriasNetworkQueries.IsClientOnly())
        {
            return false;
        }

        var timeoutBeneficiary = triggerTarget.fatherObject is Enemy
            ? triggerTarget
            : source.fatherObject is Enemy
                ? source
                : triggerTarget;
        if (!StatusApi.IsAlive(timeoutBeneficiary))
        {
            return false;
        }

        ExpireOldestIfNeeded();
        var now = NowUnixMilliseconds;
        var snapshot = new ElementalCrystalEventSnapshot
        {
            BattleEpoch = battleEpoch,
            EventId = "elemental-crystal:"
                + battleEpoch
                + ":"
                + (++nextEventSequence)
                + ":"
                + Guid.NewGuid().ToString("N"),
            SourceStatusId = source.InstanceId ?? "",
            TriggerTargetStatusId = triggerTarget.InstanceId ?? "",
            TimeoutBeneficiaryStatusId = timeoutBeneficiary.InstanceId ?? "",
            CreatedAtUnixMilliseconds = now,
            ExpiresAtUnixMilliseconds = now + (long)(LifetimeSeconds * 1000d)
        };
        Pending[snapshot.EventId] = snapshot;
        PublishSpawn(snapshot, origin);
        return true;
    }

    public static bool RequestCreate(IStatusManager? source, IStatusManager? triggerTarget, string origin)
    {
        if (source == null || triggerTarget == null)
        {
            return false;
        }

        if (!TerriasNetworkQueries.IsClientOnly())
        {
            return Create(source, triggerTarget, origin);
        }

        var token = "elemental-crystal-create:"
            + battleEpoch
            + ":"
            + (source.InstanceId ?? "")
            + ":"
            + (triggerTarget.InstanceId ?? "")
            + ":"
            + Guid.NewGuid().ToString("N");
        return TerriasNetworkRuntime.Send(
            new RpcElementalCrystalCreateRequest(
                source.InstanceId ?? "",
                triggerTarget.InstanceId ?? "",
                battleEpoch,
                token),
            origin + ":create-request");
    }

    public static void ResolveCreateRequest(
        string sourceStatusId,
        string triggerTargetStatusId,
        string token,
        TerriasRpcSender sender,
        int requestBattleEpoch)
    {
        if (TerriasNetworkQueries.IsClientOnly()
            || requestBattleEpoch != battleEpoch
            || string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var now = NowUnixMilliseconds;
        PruneCreateRequestTokens(now);
        if (CreateRequestTokens.ContainsKey(token))
        {
            return;
        }

        if (!CreateRequestOwnerIsValid(sender, sourceStatusId))
        {
            TerriasLog.Warn("[ElementalCrystal] rejected create request; sender="
                + (sender?.PlayerId ?? "")
                + ", sourceStatus="
                + sourceStatusId
                + ", targetStatus="
                + triggerTargetStatusId
                + ".");
            return;
        }

        var source = StatusApi.FindById(sourceStatusId);
        var target = StatusApi.FindById(triggerTargetStatusId);
        if (!StatusApi.IsAlive(source) || !StatusApi.IsAlive(target))
        {
            return;
        }

        CreateRequestTokens[token] = now;
        Create(source, target, "ElementalCrystalChallengeService.ResolveCreateRequest");
    }

    public static void Tick()
    {
        if (TerriasNetworkQueries.IsClientOnly() || Pending.Count == 0)
        {
            return;
        }

        var now = NowUnixMilliseconds;
        var expired = Pending.Values
            .Where(snapshot => snapshot.ExpiresAtUnixMilliseconds <= now)
            .OrderBy(snapshot => snapshot.ExpiresAtUnixMilliseconds)
            .ToList();
        foreach (var snapshot in expired)
        {
            ResolveTimeout(snapshot, "ElementalCrystalChallengeService.Tick");
        }
    }

    public static void RequestLocalClaim(string eventId, string ownerStatusId)
    {
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(ownerStatusId))
        {
            return;
        }

        if (TerriasNetworkQueries.IsClientOnly())
        {
            TerriasNetworkRuntime.Send(
                new RpcElementalCrystalClaim(eventId, ownerStatusId, battleEpoch),
                "ElementalCrystalChallengeService.RequestLocalClaim");
            return;
        }

        ResolveClaim(
            eventId,
            ownerStatusId,
            TerriasRpcAuthorityRuntime.CreateLocalServerSender("ElementalCrystalChallengeService.RequestLocalClaim"),
            battleEpoch);
    }

    public static void ResolveClaim(
        string eventId,
        string requestedOwnerStatusId,
        TerriasRpcSender sender,
        int requestBattleEpoch)
    {
        if (TerriasNetworkQueries.IsClientOnly()
            || requestBattleEpoch != battleEpoch
            || !Pending.TryGetValue(eventId ?? "", out var snapshot))
        {
            return;
        }

        if (snapshot.ExpiresAtUnixMilliseconds <= NowUnixMilliseconds)
        {
            ResolveTimeout(snapshot, "ElementalCrystalChallengeService.ResolveClaim:late");
            return;
        }

        if (!ClaimOwnerIsValid(sender, requestedOwnerStatusId))
        {
            TerriasLog.Warn("[ElementalCrystal] rejected claim; event="
                + eventId
                + ", sender="
                + (sender?.PlayerId ?? "")
                + ", status="
                + requestedOwnerStatusId
                + ".");
            return;
        }

        Complete(snapshot, requestedOwnerStatusId, claimed: true, "claim");
    }

    public static void ApplyNetworkSpawn(ElementalCrystalEventSnapshot? snapshot, string source)
    {
        if (snapshot == null
            || snapshot.ProtocolVersion != ProtocolVersion
            || snapshot.BattleEpoch != battleEpoch
            || string.IsNullOrWhiteSpace(snapshot.EventId)
            || snapshot.ExpiresAtUnixMilliseconds <= NowUnixMilliseconds
            || !Observed.Add(snapshot.EventId))
        {
            return;
        }

        Spawned?.Invoke(snapshot);
        TerriasLog.Debug("[ElementalCrystal] observed spawn; event=" + snapshot.EventId + ", source=" + source + ".");
    }

    public static void ApplyNetworkResolution(ElementalCrystalResolutionSnapshot? resolution, string source)
    {
        if (resolution == null
            || resolution.ProtocolVersion != ProtocolVersion
            || resolution.BattleEpoch != battleEpoch)
        {
            return;
        }

        if (resolution.EventId == "*")
        {
            Observed.Clear();
        }
        else
        {
            Observed.Remove(resolution.EventId);
        }

        Resolved?.Invoke(resolution);
        TerriasLog.Debug("[ElementalCrystal] observed resolution; event="
            + resolution.EventId
            + ", source="
            + source
            + ".");
    }

    private static void PublishSpawn(ElementalCrystalEventSnapshot snapshot, string source)
    {
        Observed.Add(snapshot.EventId);
        Spawned?.Invoke(snapshot);
        if (TerriasNetworkQueries.IsServer() && TerriasNetworkQueries.HasRemotePlayers())
        {
            TerriasNetworkRuntime.Send(new RpcElementalCrystalSpawn(snapshot), source + ":spawn");
        }
    }

    private static void ResolveTimeout(ElementalCrystalEventSnapshot snapshot, string source)
    {
        Complete(snapshot, snapshot.TimeoutBeneficiaryStatusId, claimed: false, source);
    }

    private static void Complete(
        ElementalCrystalEventSnapshot snapshot,
        string beneficiaryStatusId,
        bool claimed,
        string source)
    {
        if (!Pending.Remove(snapshot.EventId))
        {
            return;
        }

        var beneficiary = StatusApi.FindById(beneficiaryStatusId);
        var shield = StatusApi.IsAlive(beneficiary)
            ? Math.Max(1, StatusApi.MaxHp(beneficiary) * 20 / 100)
            : 0;
        if (shield > 0)
        {
            StatusApi.TryAddShield(beneficiary, shield);
        }

        var resolution = new ElementalCrystalResolutionSnapshot
        {
            BattleEpoch = battleEpoch,
            EventId = snapshot.EventId,
            BeneficiaryStatusId = beneficiaryStatusId ?? "",
            Shield = shield,
            Claimed = claimed
        };
        ApplyNetworkResolution(resolution, "local:" + source);
        if (TerriasNetworkQueries.IsServer() && TerriasNetworkQueries.HasRemotePlayers())
        {
            TerriasNetworkRuntime.Send(new RpcElementalCrystalResolution(resolution), source + ":resolution");
        }

        PlayerApi.ShowCaption(claimed
            ? "结晶成功：获得 " + shield + " 点护盾"
            : "结晶消散：敌人获得 " + shield + " 点护盾");
    }

    private static bool ClaimOwnerIsValid(TerriasRpcSender sender, string requestedOwnerStatusId)
    {
        var status = StatusApi.FindById(requestedOwnerStatusId);
        if (!StatusApi.IsAlive(status))
        {
            return false;
        }

        if (!TerriasNetworkQueries.NetworkActive())
        {
            return string.Equals(FightPlayer.Instance?.Status?.InstanceId, requestedOwnerStatusId, StringComparison.Ordinal);
        }

        if (sender == null || !sender.IsAvailable || !sender.IsLobbyMember)
        {
            return false;
        }

        var map = Singleton<TempDataManager>.Instance?.RoleStatusMap;
        return map != null
            && map.TryGetValue(sender.PlayerId, out var statuses)
            && statuses != null
            && statuses.Contains(requestedOwnerStatusId);
    }

    private static bool CreateRequestOwnerIsValid(TerriasRpcSender sender, string sourceStatusId)
    {
        if (sender == null || !sender.IsAvailable || !sender.IsLobbyMember || string.IsNullOrWhiteSpace(sourceStatusId))
        {
            return false;
        }

        var map = Singleton<TempDataManager>.Instance?.RoleStatusMap;
        if (map != null
            && map.TryGetValue(sender.PlayerId, out var statuses)
            && statuses != null
            && statuses.Contains(sourceStatusId))
        {
            return true;
        }

        var companion = CompanionOwnershipService.Find(sourceStatusId);
        return companion != null
            && string.Equals(companion.SemanticOwnerPlayerId, sender.PlayerId, StringComparison.Ordinal);
    }

    private static void ExpireOldestIfNeeded()
    {
        if (Pending.Count < PendingCap)
        {
            return;
        }

        var oldest = Pending.Values.OrderBy(snapshot => snapshot.CreatedAtUnixMilliseconds).FirstOrDefault();
        if (oldest != null)
        {
            ResolveTimeout(oldest, "pending-cap");
        }
    }

    private static void PruneCreateRequestTokens(long now)
    {
        foreach (var token in CreateRequestTokens
                     .Where(pair => now - pair.Value > 15000L)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            CreateRequestTokens.Remove(token);
        }

        while (CreateRequestTokens.Count >= 64)
        {
            var oldest = CreateRequestTokens.OrderBy(pair => pair.Value).First();
            CreateRequestTokens.Remove(oldest.Key);
        }
    }
}
