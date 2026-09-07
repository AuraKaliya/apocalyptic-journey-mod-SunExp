using Terrias.Dll.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Network;

namespace Terrias.Dll.Mechanics;

[Serializable]
public sealed class ConstellationStateSnapshot
{
    public const int CurrentProtocolVersion = 3;

    public int ProtocolVersion { get; set; } = CurrentProtocolVersion;
    public string BattleSessionId { get; set; } = "";
    public string OwnerPlayerId { get; set; } = "";
    public string OwnerStatusId { get; set; } = "";
    public string RoleId { get; set; } = "";
    public int Level { get; set; }
    public int Sequence { get; set; }
    public string FateStarResolution { get; set; } = "";
    public OriginCapState OriginCaps { get; set; } = new();
    public string Source { get; set; } = "";
}

[Serializable]
public sealed class ConstellationRoundRewardEvent
{
    public int ProtocolVersion { get; set; } = ConstellationStateSnapshot.CurrentProtocolVersion;
    public string BattleSessionId { get; set; } = "";
    public int RoundSequence { get; set; }
    public string EventId { get; set; } = "";
    public string SourceOwnerPlayerId { get; set; } = "";
}

public static class ConstellationService
{
    private const string SyncDomainId = "ConstellationState";
    private const string ConstellationResolution = "Constellation";
    private const string OriginCapsResolution = "OriginCaps";
    private const int MaximumRoundRewardClaims = 256;
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, int> LastSequences = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> AdventureRoles = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ConstellationStateSnapshot> KnownStates = new(StringComparer.Ordinal);
    private static readonly HashSet<string> AppliedRoundRewardIds = new(StringComparer.Ordinal);
    private static readonly Queue<string> AppliedRoundRewardOrder = new();
    private static readonly AuraAuthoritativeSyncDomain SyncDomain =
        AuraAuthoritativeSyncRuntime.RegisterDomain(new AuraAuthoritativeSyncDomainOptions
        {
            OwnerModId = TerriasIds.ModId,
            DomainId = SyncDomainId,
            SnapshotRequestThrottleSeconds = 0.5d,
            MaxResolvedTokens = 256
        });

    private static object? activeBattleIdentity;
    private static bool battleActive;
    private static string hostBattleSessionId = "";
    private static string acceptedBattleSessionId = "";
    private static int hostRoundSequence;

    public static int CurrentBattleSerial => SyncDomain.CurrentSession;

    public static string CurrentBattleSessionId
    {
        get
        {
            lock (SyncRoot)
            {
                return acceptedBattleSessionId;
            }
        }
    }

    public static int Level(IStatusManager? status)
    {
        return ConstellationPoolCatalog.Clamp(BuffApi.Level(status, TerriasIds.Constellation));
    }

    public static bool MatchesAdventureRole(IStatusManager? status, string? roleId)
    {
        if (status == null || string.IsNullOrWhiteSpace(roleId))
        {
            return false;
        }

        return string.Equals(
            ResolveAdventureRole(status),
            NormalizeRole(roleId),
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool BeginBattle()
    {
        var battleIdentity = (object?)FightManager.Instance;
        lock (SyncRoot)
        {
            if (battleActive && ReferenceEquals(activeBattleIdentity, battleIdentity))
            {
                TerriasLog.Debug("[ConstellationSync] duplicate battle start ignored.");
                return false;
            }

            battleActive = true;
            activeBattleIdentity = battleIdentity;
            AdventureRoles.Clear();
            KnownStates.Clear();
            LastSequences.Clear();
            AppliedRoundRewardIds.Clear();
            AppliedRoundRewardOrder.Clear();
            hostRoundSequence = 0;
            hostBattleSessionId = TerriasNetworkQueries.IsClientOnly() ? "" : Guid.NewGuid().ToString("N");
            acceptedBattleSessionId = hostBattleSessionId;
        }

        SyncDomain.ResetSession();
        foreach (var status in PlayerPartyApi.Snapshot())
        {
            var statusId = status.InstanceId ?? "";
            var roleId = "";
            if (TerriasStatusOwnershipPolicy.TryResolveOwningPlayerId(statusId, out var ownerPlayerId))
            {
                roleId = NormalizeRole(PlayerApi.GetCareerIdForPlayer(ownerPlayerId));
            }

            if (string.IsNullOrWhiteSpace(roleId))
            {
                roleId = ResolveUnboundAdventureRole(status);
            }

            BindAdventureRole(status, roleId, overwrite: false);
        }

        if (!TerriasNetworkQueries.IsClientOnly())
        {
            SeedAuthoritativeParty("BeginBattle");
        }

        TerriasLog.InfoAlways("[ConstellationSync] battle state initialized; session="
            + CurrentBattleSessionId
            + "; authority="
            + !TerriasNetworkQueries.IsClientOnly()
            + ".");
        return true;
    }

    public static void EndBattle()
    {
        lock (SyncRoot)
        {
            if (!battleActive)
            {
                return;
            }

            battleActive = false;
            activeBattleIdentity = null;
            AdventureRoles.Clear();
            KnownStates.Clear();
            LastSequences.Clear();
            AppliedRoundRewardIds.Clear();
            AppliedRoundRewardOrder.Clear();
            hostBattleSessionId = "";
            acceptedBattleSessionId = "";
            hostRoundSequence = 0;
        }
    }

    public static void SynchronizeBattleState(string source)
    {
        if (!TerriasNetworkQueries.HasRemotePlayers())
        {
            return;
        }

        if (TerriasNetworkQueries.IsClientOnly())
        {
            RpcConstellationRosterSnapshot.Request(
                FightPlayer.Instance?.Status,
                ResolveAdventureRole(FightPlayer.Instance?.Status),
                source);
            return;
        }

        RpcConstellationRosterSnapshot.Broadcast(source);
    }

    public static int LightUp(ScriptExecutor? executor)
    {
        var status = executor?.Self ?? FightPlayer.Instance?.Status;
        if (status == null)
        {
            return 0;
        }

        var roleId = ResolveAdventureRole(status);
        if (!TerriasNetworkQueries.NetworkActive())
        {
            var before = GetStored(status, roleId);
            var next = ConstellationPoolCatalog.Clamp(before + 1);
            if (next == before)
            {
                if (OriginCapService.TryIncreaseCurrent(
                        OriginCapService.FateStarIncrease,
                        "FateStar.SinglePlayer",
                        out var originCaps))
                {
                    OriginCapService.ShowIncreaseCaption(originCaps);
                }
                return before;
            }

            PersistLevel(status.InstanceId ?? "", roleId, next);
            var applied = ApplyToStatus(status, roleId, next, before, "FateStar.SinglePlayer");
            RegisterProvisionalState(status, roleId, next, "FateStar.SinglePlayer");
            LogLightUp(roleId, next, applied, "single-player");
            PlayerApi.ShowCaption("命之座 · 第" + next + "层");
            return next;
        }

        if (TerriasNetworkQueries.IsClientOnly())
        {
            var sent = TerriasNetworkRuntime.Send(new RpcConstellationStateCommit
            {
                ProtocolVersion = ConstellationStateSnapshot.CurrentProtocolVersion,
                Token = SyncDomain.NextToken(),
                BattleSessionId = CurrentBattleSessionId,
                OwnerStatusId = status.InstanceId ?? "",
                RoleId = roleId
            }, "FateStar.LightUpRequest");
            if (!sent)
            {
                TerriasLog.Warn("[ConstellationSync] failed to submit light-up request for owner=" + (status.InstanceId ?? "") + ".");
            }

            return Level(status);
        }

        var sender = TerriasRpcAuthorityRuntime.CreateLocalServerSender("FateStar.Host");
        if (!TryResolveLightUpRequest(
                ConstellationStateSnapshot.CurrentProtocolVersion,
                SyncDomain.NextToken(),
                CurrentBattleSessionId,
                status.InstanceId ?? "",
                roleId,
                sender,
                out var snapshot,
                out var rejection))
        {
            if (string.Equals(rejection, "constellation already complete", StringComparison.Ordinal))
            {
                PlayerApi.ShowCaption("命之座已全部点亮。");
            }
            else
            {
                TerriasLog.Warn("[ConstellationSync] host light-up rejected: " + rejection);
            }

            return Level(status);
        }

        NotifyLightUpApplied(snapshot, "FateStar.Host");
        RpcConstellationRosterSnapshot.Broadcast("FateStar.HostAccepted");
        return snapshot.Level;
    }

    public static int RestoreLocalForBattle(string source)
    {
        var status = FightPlayer.Instance?.Status;
        if (status == null)
        {
            return 0;
        }

        var roleId = ResolveAdventureRole(status);
        if (TerriasNetworkQueries.IsClientOnly())
        {
            BindAdventureRole(status, roleId, overwrite: false);
            RegisterProvisionalState(status, roleId, 0, source + ".AwaitingAuthority");
            TerriasLog.InfoAlways("[ConstellationSync] client battle restore is awaiting the host roster; role="
                + roleId
                + "; status="
                + (status.InstanceId ?? "")
                + "; source="
                + source
                + ".");
            return 0;
        }

        var level = GetStored(status, roleId);
        var applied = ApplyToStatus(status, roleId, level, 0, source);
        RegisterProvisionalState(status, roleId, level, source);
        TerriasLog.InfoAlways("[Constellation] restored for battle; role="
            + roleId
            + ", pool="
            + ConstellationPoolCatalog.PoolForRole(roleId).Id
            + ", stored="
            + level
            + ", buff="
            + applied
            + ", source="
            + source
            + ".");
        return level;
    }

    public static void ResolveLocalRoundStart()
    {
        var status = FightPlayer.Instance?.Status;
        if (!StatusApi.IsAlive(status))
        {
            return;
        }

        var roleId = ResolveAdventureRole(status);
        var isTraveler = string.Equals(
            ConstellationPoolCatalog.PoolForRole(roleId).Id,
            ConstellationPoolCatalog.TravelerPoolId,
            StringComparison.OrdinalIgnoreCase);
        var level = Level(status);
        if (isTraveler && level >= 2)
        {
            StatusApi.TryAddShield(status, Math.Max(1, StatusApi.MaxHp(status) / 10));
        }

        if (isTraveler && level >= 4)
        {
            PlayerPartyApi.TryGainPower(status, 1);
        }

        if (!TerriasNetworkQueries.NetworkActive())
        {
            if (isTraveler && level >= 6)
            {
                var round = NextHostRoundSequence();
                ApplyRoundReward(CreateRoundReward(
                    TerriasNetworkQueries.LocalPlayerId(),
                    round), "RoundStart.SinglePlayer");
            }

            return;
        }

        if (TerriasNetworkQueries.IsClientOnly())
        {
            return;
        }

        PublishAuthoritativeRoundRewards();
    }

    public static void ResolveColumbinaLunarReaction(IStatusManager? source)
    {
        if (!IsColumbinaWithLevel(source, 1))
        {
            return;
        }

        foreach (var member in PlayerPartyApi.Snapshot())
        {
            PlayerPartyApi.TryGainPower(member, 1);
            var maxHpGain = Math.Max(1, ColumbinaBattleStateService.StartingMaxHpFor(member) / 100);
            if (StatusApi.TryIncreaseMaxHp(member, maxHpGain))
            {
                StatusApi.TryHeal(member, maxHpGain);
            }
        }
    }

    public static bool IsColumbinaWithLevel(IStatusManager? status, int level)
    {
        return status != null
            && string.Equals(
                ConstellationPoolCatalog.PoolForRole(ResolveAdventureRole(status)).Id,
                ConstellationPoolCatalog.ColumbinaPoolId,
                StringComparison.OrdinalIgnoreCase)
            && Level(status) >= level;
    }

    public static void ResolveInterferenceTriggered(IStatusManager? source)
    {
        if (IsColumbinaWithLevel(source, 4))
        {
            StatusApi.TryIncreaseMaxHp(source, 50);
        }
    }

    public static int EligibleColumbinaC6Count()
    {
        lock (SyncRoot)
        {
            var synchronizedCount = KnownStates.Values.Count(snapshot =>
                snapshot.Level >= 6
                && IsCurrentPartyOwner(snapshot.OwnerPlayerId)
                && string.Equals(
                    ConstellationPoolCatalog.PoolForRole(snapshot.RoleId).Id,
                    ConstellationPoolCatalog.ColumbinaPoolId,
                    StringComparison.OrdinalIgnoreCase));
            if (synchronizedCount > 0)
            {
                return synchronizedCount;
            }
        }

        return PlayerPartyApi.Snapshot().Count(status => IsColumbinaWithLevel(status, 6));
    }

    public static bool TryResolveLightUpRequest(
        int protocolVersion,
        int token,
        string battleSessionId,
        string ownerStatusId,
        string roleId,
        TerriasRpcSender sender,
        out ConstellationStateSnapshot snapshot,
        out string rejection)
    {
        snapshot = new ConstellationStateSnapshot();
        rejection = "";
        roleId = NormalizeRole(roleId);
        if (protocolVersion != ConstellationStateSnapshot.CurrentProtocolVersion)
        {
            rejection = "protocol mismatch";
            return false;
        }

        if (!sender.IsAvailable || !sender.IsLobbyMember)
        {
            rejection = "sender unavailable or outside lobby";
            return false;
        }

        if (string.IsNullOrWhiteSpace(roleId) || roleId.Length > 96)
        {
            rejection = "invalid adventure role";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(battleSessionId)
            && !string.Equals(battleSessionId, hostBattleSessionId, StringComparison.Ordinal))
        {
            rejection = "stale battle session";
            return false;
        }

        if (!TerriasStatusOwnershipPolicy.SenderOwnsStatus(sender.PlayerId, ownerStatusId, out var ownershipDetail))
        {
            rejection = "sender does not own status; sender="
                + sender.PlayerId
                + "; ownerStatus="
                + ownerStatusId
                + "; authority="
                + ownershipDetail;
            return false;
        }

        if (!SyncDomain.TryClaimToken(sender.PlayerId, token))
        {
            rejection = "duplicate command token";
            return false;
        }

        var status = StatusApi.FindById(ownerStatusId);
        var authoritativeRoleId = ResolveAuthoritativeOwnerRole(sender.PlayerId, status);
        if (!string.IsNullOrWhiteSpace(authoritativeRoleId)
            ? !string.Equals(authoritativeRoleId, roleId, StringComparison.OrdinalIgnoreCase)
            : status != null && !MatchesAdventureRole(status, roleId))
        {
            rejection = "role does not match adventure owner";
            return false;
        }

        var current = EnsureAuthoritativeOwner(sender.PlayerId, ownerStatusId, roleId, status, "LightUpRequest");
        if (!string.Equals(
                ConstellationPoolCatalog.PoolForRole(current.RoleId).Id,
                ConstellationPoolCatalog.PoolForRole(roleId).Id,
                StringComparison.OrdinalIgnoreCase))
        {
            rejection = "constellation pool mismatch";
            return false;
        }

        var next = ConstellationPoolCatalog.Clamp(current.Level + 1);
        if (next == current.Level)
        {
            var authoritativeRole = OriginCapService.ResolveAuthoritativeRole(sender.PlayerId);
            if (!OriginCapService.TryIncrease(
                    authoritativeRole,
                    OriginCapService.FateStarIncrease,
                    "FateStar.Server:" + sender.PlayerId,
                    out var increasedCaps))
            {
                rejection = "authoritative origin caps unavailable";
                return false;
            }

            snapshot = new ConstellationStateSnapshot
            {
                BattleSessionId = hostBattleSessionId,
                OwnerPlayerId = sender.PlayerId,
                OwnerStatusId = ownerStatusId,
                RoleId = current.RoleId,
                Level = current.Level,
                Sequence = Math.Max(1, current.Sequence + 1),
                FateStarResolution = OriginCapsResolution,
                OriginCaps = increasedCaps,
                Source = "FateStar"
            };
            ApplySnapshot(snapshot, "server:FateStarOriginCaps");
            TerriasLog.InfoAlways("[ConstellationSync] Fate Star origin cap increase accepted; owner="
                + sender.PlayerId
                + "; status="
                + ownerStatusId
                + "; main="
                + increasedCaps.Main
                + "; secondary="
                + increasedCaps.Secondary
                + "; other="
                + increasedCaps.Other
                + "; sequence="
                + snapshot.Sequence
                + "; token="
                + token
                + ".");
            return true;
        }

        snapshot = new ConstellationStateSnapshot
        {
            BattleSessionId = hostBattleSessionId,
            OwnerPlayerId = sender.PlayerId,
            OwnerStatusId = ownerStatusId,
            RoleId = current.RoleId,
            Level = next,
            Sequence = Math.Max(1, current.Sequence + 1),
            FateStarResolution = ConstellationResolution,
            OriginCaps = CaptureOriginCaps(sender.PlayerId),
            Source = "FateStar"
        };
        PersistLevel(ownerStatusId, snapshot.RoleId, snapshot.Level);
        ApplySnapshot(snapshot, "server:FateStar");
        TerriasLog.InfoAlways("[ConstellationSync] light-up accepted; owner="
            + sender.PlayerId
            + "; status="
            + ownerStatusId
            + "; role="
            + snapshot.RoleId
            + "; level="
            + snapshot.Level
            + "; sequence="
            + snapshot.Sequence
            + "; token="
            + token
            + ".");
        return true;
    }

    public static bool TryCaptureAuthoritativeRoster(
        string requestOwnerStatusId,
        string requestRoleId,
        TerriasRpcSender sender,
        out List<ConstellationStateSnapshot> snapshots,
        out string battleSessionId,
        out string rejection)
    {
        snapshots = new List<ConstellationStateSnapshot>();
        battleSessionId = "";
        rejection = "";
        if (!sender.IsAvailable || !sender.IsLobbyMember)
        {
            rejection = "sender unavailable or outside lobby";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requestOwnerStatusId))
        {
            if (!TerriasStatusOwnershipPolicy.SenderOwnsStatus(sender.PlayerId, requestOwnerStatusId, out var detail))
            {
                rejection = "sender does not own requested status: " + detail;
                return false;
            }

            var status = StatusApi.FindById(requestOwnerStatusId);
            var normalizedRole = NormalizeRole(requestRoleId);
            var authoritativeRoleId = ResolveAuthoritativeOwnerRole(sender.PlayerId, status);
            if (!string.IsNullOrWhiteSpace(authoritativeRoleId)
                ? !string.Equals(authoritativeRoleId, normalizedRole, StringComparison.OrdinalIgnoreCase)
                : status != null && !MatchesAdventureRole(status, normalizedRole))
            {
                rejection = "requested role does not match adventure owner";
                return false;
            }

            EnsureAuthoritativeOwner(sender.PlayerId, requestOwnerStatusId, normalizedRole, status, "RosterRequest");
        }
        else if (!sender.IsLobbyHost)
        {
            rejection = "non-host roster request requires owner identity";
            return false;
        }

        SeedAuthoritativeParty("RosterCapture");
        lock (SyncRoot)
        {
            battleSessionId = hostBattleSessionId;
            snapshots = KnownStates.Values
                .Select(CloneSnapshot)
                .OrderBy(value => value.OwnerPlayerId, StringComparer.Ordinal)
                .ThenBy(value => ConstellationPoolCatalog.PoolForRole(value.RoleId).Id, StringComparer.Ordinal)
                .ToList();
        }

        TerriasLog.InfoAlways("[ConstellationSync] authoritative roster captured; requester="
            + sender.PlayerId
            + "; entries="
            + snapshots.Count
            + "; session="
            + battleSessionId
            + ".");
        return true;
    }

    public static bool ApplyRoster(
        string battleSessionId,
        IReadOnlyList<ConstellationStateSnapshot>? snapshots,
        string source)
    {
        if (!AcceptBattleSession(battleSessionId, allowReplace: true, source))
        {
            return false;
        }

        var applied = 0;
        foreach (var snapshot in snapshots ?? Array.Empty<ConstellationStateSnapshot>())
        {
            if (ApplySnapshot(snapshot, source))
            {
                applied++;
            }
        }

        TerriasLog.InfoAlways("[ConstellationSync] roster applied; source="
            + source
            + "; session="
            + battleSessionId
            + "; entries="
            + (snapshots?.Count ?? 0)
            + "; changed="
            + applied
            + ".");
        return true;
    }

    public static bool ApplySnapshot(ConstellationStateSnapshot? snapshot, string source)
    {
        if (snapshot == null
            || snapshot.ProtocolVersion != ConstellationStateSnapshot.CurrentProtocolVersion
            || string.IsNullOrWhiteSpace(snapshot.RoleId)
            || string.IsNullOrWhiteSpace(snapshot.OwnerPlayerId)
            || string.IsNullOrWhiteSpace(snapshot.OwnerStatusId)
            || !AcceptBattleSession(snapshot.BattleSessionId, allowReplace: false, source))
        {
            return false;
        }

        snapshot.Level = ConstellationPoolCatalog.Clamp(snapshot.Level);
        var ownerRoleId = NormalizeRole(snapshot.RoleId);
        var poolId = ConstellationPoolCatalog.PoolForRole(ownerRoleId).Id;
        var stateKey = StateKey(snapshot.OwnerPlayerId, poolId);
        var previousLevel = 0;
        lock (SyncRoot)
        {
            if (snapshot.Sequence > 0
                && LastSequences.TryGetValue(stateKey, out var previousSequence)
                && snapshot.Sequence <= previousSequence)
            {
                return false;
            }

            if (KnownStates.TryGetValue(stateKey, out var previous))
            {
                previousLevel = previous.Level;
            }

            if (snapshot.Sequence > 0)
            {
                LastSequences[stateKey] = snapshot.Sequence;
            }

            KnownStates[stateKey] = CloneSnapshot(snapshot);
        }

        var status = StatusApi.FindById(snapshot.OwnerStatusId);
        if (status == null && IsLocalOwner(snapshot))
        {
            status = FightPlayer.Instance?.Status;
        }

        var appliedLevel = 0;
        var localOwner = IsLocalOwner(snapshot);
        if (localOwner)
        {
            OriginCapService.ApplyAuthoritativeCurrent(snapshot.OriginCaps, source);
        }
        if (status != null)
        {
            BindAdventureRole(status, ownerRoleId, overwrite: true);
            if (localOwner)
            {
                appliedLevel = ApplyToStatus(status, ownerRoleId, snapshot.Level, previousLevel, source);
            }
            else
            {
                appliedLevel = BuffApi.SetExactLevelWithNativeRefresh(status, TerriasIds.Constellation, snapshot.Level);
                ApplyPresentation(status, ownerRoleId, source);
            }
        }

        TerriasLog.InfoAlways("[ConstellationSync] snapshot applied; source="
            + source
            + "; owner="
            + snapshot.OwnerPlayerId
            + "; status="
            + snapshot.OwnerStatusId
            + "; role="
            + ownerRoleId
            + "; pool="
            + poolId
            + "; level="
            + snapshot.Level
            + "; sequence="
            + snapshot.Sequence
            + "; localOwner="
            + localOwner
            + "; buff="
            + appliedLevel
            + ".");
        return true;
    }

    public static void NotifyLightUpApplied(ConstellationStateSnapshot? snapshot, string source)
    {
        if (snapshot == null || !IsLocalOwner(snapshot))
        {
            return;
        }

        if (string.Equals(snapshot.FateStarResolution, OriginCapsResolution, StringComparison.Ordinal))
        {
            OriginCapService.ShowIncreaseCaption(snapshot.OriginCaps);
            TerriasLog.InfoAlways("[Constellation] Fate Star increased origin caps; source=" + source + ".");
            return;
        }

        PlayerApi.ShowCaption("命之座 · 第" + snapshot.Level + "层");
        LogLightUp(snapshot.RoleId, snapshot.Level, Level(FightPlayer.Instance?.Status), source);
    }

    public static bool ValidateRoundRewardOnServer(
        ConstellationRoundRewardEvent? reward,
        TerriasRpcSender sender,
        out string rejection)
    {
        rejection = "";
        if (reward == null || reward.ProtocolVersion != ConstellationStateSnapshot.CurrentProtocolVersion)
        {
            rejection = "invalid reward protocol";
            return false;
        }

        if (!sender.IsAvailable || !sender.IsLobbyMember || !sender.IsLobbyHost)
        {
            rejection = "round reward publisher is not the lobby host";
            return false;
        }

        if (!string.Equals(reward.BattleSessionId, hostBattleSessionId, StringComparison.Ordinal)
            || reward.RoundSequence <= 0)
        {
            rejection = "stale round reward session";
            return false;
        }

        var expectedEventId = RoundRewardEventId(
            reward.BattleSessionId,
            reward.RoundSequence,
            reward.SourceOwnerPlayerId);
        if (!string.Equals(reward.EventId, expectedEventId, StringComparison.Ordinal))
        {
            rejection = "round reward event identity mismatch";
            return false;
        }

        lock (SyncRoot)
        {
            var eligible = KnownStates.Values.Any(snapshot =>
                string.Equals(snapshot.OwnerPlayerId, reward.SourceOwnerPlayerId, StringComparison.Ordinal)
                && snapshot.Level >= 6
                && IsCurrentPartyOwner(snapshot.OwnerPlayerId)
                && string.Equals(
                    ConstellationPoolCatalog.PoolForRole(snapshot.RoleId).Id,
                    ConstellationPoolCatalog.TravelerPoolId,
                    StringComparison.OrdinalIgnoreCase));
            if (!eligible)
            {
                rejection = "round reward source does not own traveler constellation six";
                return false;
            }
        }

        return true;
    }

    public static bool ApplyRoundReward(ConstellationRoundRewardEvent? reward, string source)
    {
        if (reward == null
            || reward.ProtocolVersion != ConstellationStateSnapshot.CurrentProtocolVersion
            || string.IsNullOrWhiteSpace(reward.EventId)
            || !AcceptBattleSession(reward.BattleSessionId, allowReplace: false, source))
        {
            return false;
        }

        lock (SyncRoot)
        {
            if (!AppliedRoundRewardIds.Add(reward.EventId))
            {
                TerriasLog.Debug("[ConstellationSync] duplicate round reward ignored; event=" + reward.EventId + ".");
                return false;
            }

            AppliedRoundRewardOrder.Enqueue(reward.EventId);
            while (AppliedRoundRewardOrder.Count > MaximumRoundRewardClaims)
            {
                AppliedRoundRewardIds.Remove(AppliedRoundRewardOrder.Dequeue());
            }
        }

        var local = FightPlayer.Instance?.Status;
        if (!StatusApi.IsAlive(local))
        {
            return false;
        }

        local!.AddBuff(TerriasIds.Extraordinary, 300);
        var shield = Math.Max(1, StatusApi.MaxHp(local) / 5);
        StatusApi.TryAddShield(local, shield);
        PlayerPartyApi.TryGainPower(local, 2);
        TerriasLog.InfoAlways("[ConstellationSync] traveler C6 reward applied locally; event="
            + reward.EventId
            + "; sourceOwner="
            + reward.SourceOwnerPlayerId
            + "; round="
            + reward.RoundSequence
            + "; extraordinary=300; shield="
            + shield
            + "; power=2; localStatus="
            + (local.InstanceId ?? "")
            + ".");
        return true;
    }

    private static void PublishAuthoritativeRoundRewards()
    {
        var roundSequence = NextHostRoundSequence();
        List<ConstellationStateSnapshot> sources;
        lock (SyncRoot)
        {
            sources = KnownStates.Values
                .Where(snapshot => snapshot.Level >= 6
                    && IsCurrentPartyOwner(snapshot.OwnerPlayerId)
                    && string.Equals(
                        ConstellationPoolCatalog.PoolForRole(snapshot.RoleId).Id,
                        ConstellationPoolCatalog.TravelerPoolId,
                        StringComparison.OrdinalIgnoreCase))
                .Select(CloneSnapshot)
                .ToList();
        }

        foreach (var source in sources)
        {
            var reward = CreateRoundReward(source.OwnerPlayerId, roundSequence);
            var command = new RpcConstellationRoundReward(reward);
            command.BindServerSender(TerriasRpcAuthorityRuntime.CreateLocalServerSender("Constellation.RoundStart"));
            if (!TerriasNetworkRuntime.Send(command, "Constellation.RoundStart:" + source.OwnerPlayerId))
            {
                TerriasLog.Warn("[ConstellationSync] failed to publish traveler C6 reward; event=" + reward.EventId + ".");
            }
        }

        TerriasLog.InfoAlways("[ConstellationSync] round rewards published; round="
            + roundSequence
            + "; eligibleSources="
            + sources.Count
            + "; session="
            + CurrentBattleSessionId
            + ".");
    }

    private static ConstellationRoundRewardEvent CreateRoundReward(string sourceOwnerPlayerId, int roundSequence)
    {
        var session = CurrentBattleSessionId;
        return new ConstellationRoundRewardEvent
        {
            BattleSessionId = session,
            RoundSequence = roundSequence,
            SourceOwnerPlayerId = sourceOwnerPlayerId ?? "",
            EventId = RoundRewardEventId(session, roundSequence, sourceOwnerPlayerId)
        };
    }

    private static int NextHostRoundSequence()
    {
        lock (SyncRoot)
        {
            hostRoundSequence = hostRoundSequence == int.MaxValue ? 1 : hostRoundSequence + 1;
            return hostRoundSequence;
        }
    }

    private static string RoundRewardEventId(string battleSessionId, int roundSequence, string? sourceOwnerPlayerId)
    {
        return (battleSessionId ?? "")
            + ":round:"
            + roundSequence
            + ":traveler-c6:"
            + (sourceOwnerPlayerId ?? "");
    }

    private static void SeedAuthoritativeParty(string source)
    {
        foreach (var status in PlayerPartyApi.Snapshot(aliveOnly: false))
        {
            var statusId = status.InstanceId ?? "";
            if (!TerriasStatusOwnershipPolicy.TryResolveOwningPlayerId(statusId, out var ownerPlayerId))
            {
                if (string.Equals(statusId, PlayerApi.LocalPlayerStatusId(), StringComparison.Ordinal))
                {
                    ownerPlayerId = TerriasNetworkQueries.LocalPlayerId();
                }
                else
                {
                    continue;
                }
            }

            var roleId = ResolveAuthoritativeOwnerRole(ownerPlayerId, status);
            EnsureAuthoritativeOwner(ownerPlayerId, statusId, roleId, status, source);
        }
    }

    private static ConstellationStateSnapshot EnsureAuthoritativeOwner(
        string ownerPlayerId,
        string ownerStatusId,
        string roleId,
        IStatusManager? status,
        string source)
    {
        roleId = NormalizeRole(roleId);
        var poolId = ConstellationPoolCatalog.PoolForRole(roleId).Id;
        var stateKey = StateKey(ownerPlayerId, poolId);
        lock (SyncRoot)
        {
            if (KnownStates.TryGetValue(stateKey, out var existing))
            {
                return CloneSnapshot(existing);
            }
        }

        var persisted = GetStoredForScope(ownerStatusId, roleId);
        var liveLevel = status == null ? 0 : Level(status);
        var initial = new ConstellationStateSnapshot
        {
            BattleSessionId = hostBattleSessionId,
            OwnerPlayerId = ownerPlayerId ?? "",
            OwnerStatusId = ownerStatusId ?? "",
            RoleId = roleId,
            Level = Math.Max(persisted, liveLevel),
            Sequence = 1,
            FateStarResolution = ConstellationResolution,
            OriginCaps = CaptureOriginCaps(ownerPlayerId ?? ""),
            Source = source ?? ""
        };
        lock (SyncRoot)
        {
            KnownStates[stateKey] = CloneSnapshot(initial);
            LastSequences[stateKey] = initial.Sequence;
        }

        if (status != null)
        {
            BindAdventureRole(status, roleId, overwrite: true);
        }

        TerriasLog.InfoAlways("[ConstellationSync] authoritative owner registered; owner="
            + ownerPlayerId
            + "; status="
            + ownerStatusId
            + "; role="
            + roleId
            + "; level="
            + initial.Level
            + "; source="
            + source
            + ".");
        return initial;
    }

    private static void RegisterProvisionalState(IStatusManager status, string roleId, int level, string source)
    {
        var ownerPlayerId = TerriasNetworkQueries.LocalPlayerId();
        if (string.IsNullOrWhiteSpace(ownerPlayerId))
        {
            ownerPlayerId = status.InstanceId ?? "local";
        }

        var snapshot = new ConstellationStateSnapshot
        {
            BattleSessionId = CurrentBattleSessionId,
            OwnerPlayerId = ownerPlayerId,
            OwnerStatusId = status.InstanceId ?? "",
            RoleId = roleId,
            Level = ConstellationPoolCatalog.Clamp(level),
            Sequence = 0,
            FateStarResolution = ConstellationResolution,
            OriginCaps = OriginCapService.Capture(RoleTable.Instance),
            Source = source ?? ""
        };
        lock (SyncRoot)
        {
            var stateKey = StateKey(ownerPlayerId, ConstellationPoolCatalog.PoolForRole(roleId).Id);
            if (!KnownStates.ContainsKey(stateKey))
            {
                KnownStates[stateKey] = snapshot;
            }
        }
    }

    private static bool AcceptBattleSession(string? battleSessionId, bool allowReplace, string source)
    {
        if (string.IsNullOrWhiteSpace(battleSessionId))
        {
            return !TerriasNetworkQueries.NetworkActive();
        }

        var incomingSessionId = battleSessionId!;

        lock (SyncRoot)
        {
            if (string.IsNullOrWhiteSpace(acceptedBattleSessionId))
            {
                acceptedBattleSessionId = incomingSessionId;
                return true;
            }

            if (string.Equals(acceptedBattleSessionId, incomingSessionId, StringComparison.Ordinal))
            {
                return true;
            }

            if (!allowReplace)
            {
                TerriasLog.Warn("[ConstellationSync] stale session rejected; source="
                    + source
                    + "; expected="
                    + acceptedBattleSessionId
                    + "; received="
                    + incomingSessionId
                    + ".");
                return false;
            }

            acceptedBattleSessionId = incomingSessionId;
            KnownStates.Clear();
            LastSequences.Clear();
            AppliedRoundRewardIds.Clear();
            AppliedRoundRewardOrder.Clear();
            return true;
        }
    }

    private static bool IsLocalOwner(ConstellationStateSnapshot snapshot)
    {
        return TerriasNetworkQueries.IsLocalPlayer(snapshot.OwnerPlayerId)
            || string.Equals(snapshot.OwnerStatusId, PlayerApi.LocalPlayerStatusId(), StringComparison.Ordinal);
    }

    private static bool IsCurrentPartyOwner(string ownerPlayerId)
    {
        if (!TerriasNetworkQueries.NetworkActive())
        {
            return true;
        }

        var lobbyPlayerIds = TerriasNetworkQueries.LobbyPlayerIds();
        return lobbyPlayerIds.Count == 0
            || lobbyPlayerIds.Any(playerId => string.Equals(playerId, ownerPlayerId, StringComparison.Ordinal));
    }

    private static string ResolveAuthoritativeOwnerRole(string ownerPlayerId, IStatusManager? status)
    {
        var statusId = status?.InstanceId ?? "";
        lock (SyncRoot)
        {
            if (!string.IsNullOrWhiteSpace(statusId)
                && AdventureRoles.TryGetValue(statusId, out var boundRoleId)
                && !string.IsNullOrWhiteSpace(boundRoleId))
            {
                return boundRoleId;
            }
        }

        var savedRoleId = NormalizeRole(PlayerApi.GetCareerIdForPlayer(ownerPlayerId));
        if (!string.IsNullOrWhiteSpace(savedRoleId))
        {
            return savedRoleId;
        }

        return ResolveAdventureRole(status);
    }

    private static int ApplyToStatus(IStatusManager status, string roleId, int level, int previousLevel, string source)
    {
        var safeLevel = ConstellationPoolCatalog.Clamp(level);
        BindAdventureRole(status, roleId, overwrite: false);
        var appliedLevel = BuffApi.SetExactLevelWithNativeRefresh(status, TerriasIds.Constellation, safeLevel);
        ApplyPresentation(status, roleId, source);
        for (var tier = Math.Max(1, previousLevel + 1); tier <= safeLevel; tier++)
        {
            ApplyOneTimeTier(status, roleId, tier, source);
        }

        return appliedLevel;
    }

    private static void ApplyOneTimeTier(IStatusManager status, string roleId, int tier, string source)
    {
        var pool = ConstellationPoolCatalog.PoolForRole(roleId);
        var key = "Terrias.Constellation.Once."
            + (status.InstanceId ?? "local")
            + "."
            + Sanitize(pool.Id)
            + "."
            + tier;
        if (CombatVarApi.GetInt(key) != 0)
        {
            return;
        }

        var extraordinary = pool.Tier(tier)?.OneTimeExtraordinary ?? 0;
        if (extraordinary > 0)
        {
            status.AddBuff(TerriasIds.Extraordinary, extraordinary);
        }

        CombatVarApi.SetInt(key, 1);
        TerriasLog.Debug("[Constellation] one-time tier applied; role=" + roleId + "; tier=" + tier + "; source=" + source + ".");
    }

    private static int GetStored(IStatusManager status, string roleId)
    {
        return GetStoredForScope(status.InstanceId ?? "", roleId);
    }

    private static int GetStoredForScope(string ownerStatusId, string roleId)
    {
        var poolId = ConstellationPoolCatalog.PoolForRole(roleId).Id;
        var current = PlayerApi.GetScopedGameVarForScope(StorageKeyForPool(poolId), ownerStatusId, "");
        if (!string.IsNullOrWhiteSpace(current))
        {
            return ConstellationPoolCatalog.Clamp(DictionaryUtil.ParseInt(current));
        }

        var legacy = ConstellationPoolCatalog.Clamp(DictionaryUtil.ParseInt(
            PlayerApi.GetScopedGameVarForScope(LegacyStorageKeyForRole(roleId), ownerStatusId, "0")));
        if (legacy > 0 && !TerriasNetworkQueries.IsClientOnly())
        {
            PlayerApi.SetScopedGameVarForScope(StorageKeyForPool(poolId), ownerStatusId, legacy.ToString());
            TerriasLog.Info("[Constellation] migrated legacy role progress; role="
                + roleId
                + ", pool="
                + poolId
                + ", level="
                + legacy
                + ".");
        }

        return legacy;
    }

    private static void PersistLevel(string ownerStatusId, string roleId, int level)
    {
        var poolId = ConstellationPoolCatalog.PoolForRole(roleId).Id;
        PlayerApi.SetScopedGameVarForScope(
            StorageKeyForPool(poolId),
            ownerStatusId,
            ConstellationPoolCatalog.Clamp(level).ToString());
    }

    private static string StorageKeyForPool(string poolId)
    {
        return TerriasIds.ConstellationStorage + "_Pool_" + Sanitize(poolId);
    }

    private static string LegacyStorageKeyForRole(string roleId)
    {
        return TerriasIds.ConstellationStorage + "_Role_" + Sanitize(roleId);
    }

    private static string StateKey(string ownerPlayerId, string poolId)
    {
        return (ownerPlayerId ?? "").Trim() + ":" + (poolId ?? "").Trim();
    }

    private static ConstellationStateSnapshot CloneSnapshot(ConstellationStateSnapshot snapshot)
    {
        return new ConstellationStateSnapshot
        {
            ProtocolVersion = snapshot.ProtocolVersion,
            BattleSessionId = snapshot.BattleSessionId ?? "",
            OwnerPlayerId = snapshot.OwnerPlayerId ?? "",
            OwnerStatusId = snapshot.OwnerStatusId ?? "",
            RoleId = snapshot.RoleId ?? "",
            Level = snapshot.Level,
            Sequence = snapshot.Sequence,
            FateStarResolution = snapshot.FateStarResolution ?? "",
            OriginCaps = new OriginCapState
            {
                Main = snapshot.OriginCaps?.Main ?? 0,
                Secondary = snapshot.OriginCaps?.Secondary ?? 0,
                Other = snapshot.OriginCaps?.Other ?? 0
            },
            Source = snapshot.Source ?? ""
        };
    }

    private static OriginCapState CaptureOriginCaps(string ownerPlayerId)
    {
        return OriginCapService.Capture(OriginCapService.ResolveAuthoritativeRole(ownerPlayerId));
    }

    private static void LogLightUp(string roleId, int level, int applied, string source)
    {
        TerriasLog.InfoAlways("[Constellation] light up; role="
            + roleId
            + ", pool="
            + ConstellationPoolCatalog.PoolForRole(roleId).Id
            + ", stored="
            + level
            + ", buff="
            + applied
            + ", source="
            + source
            + ".");
    }

    private static string NormalizeRole(string? roleId)
    {
        var value = (roleId ?? "").Trim();
        return ConstellationPoolCatalog.IsColumbina(value) ? "columbina" : value;
    }

    public static bool PreparePresentation(
        IBuffItemConfig? buffConfig,
        IStatusManager? status,
        string source)
    {
        if (buffConfig == null
            || !string.Equals(buffConfig.BuffId, TerriasIds.Constellation, StringComparison.Ordinal))
        {
            return false;
        }

        var roleId = ResolveAdventureRole(status ?? buffConfig.status);
        var pool = ConstellationPoolCatalog.PoolForRole(roleId);
        var prepared = BuffApi.PrepareRuntimePresentation(buffConfig, pool.PresentationFields);
        if (prepared)
        {
            TerriasLog.Debug("[Constellation] presentation prepared; role="
                + roleId
                + "; pool="
                + pool.Id
                + "; source="
                + source
                + ".");
        }

        return prepared;
    }

    public static bool RefreshPresentation(IBuffItem? buff, string source)
    {
        if (buff?.buffConfig == null
            || !string.Equals(buff.buffConfig.BuffId, TerriasIds.Constellation, StringComparison.Ordinal))
        {
            return false;
        }

        var status = buff.buffConfig.status;
        var roleId = ResolveAdventureRole(status);
        var changed = BuffApi.ApplyRuntimePresentation(
            buff,
            ConstellationPoolCatalog.PoolForRole(roleId).PresentationFields);
        if (changed)
        {
            TerriasLog.Debug("[Constellation] presentation refreshed; role="
                + roleId
                + "; pool="
                + ConstellationPoolCatalog.PoolForRole(roleId).Id
                + "; source="
                + source
                + ".");
        }

        return changed;
    }

    private static void ApplyPresentation(IStatusManager status, string roleId, string source)
    {
        var pool = ConstellationPoolCatalog.PoolForRole(roleId);
        if (BuffApi.ApplyRuntimePresentation(status, TerriasIds.Constellation, pool.PresentationFields))
        {
            TerriasLog.Debug("[Constellation] presentation applied; role="
                + roleId
                + "; pool="
                + pool.Id
                + "; source="
                + source
                + ".");
        }
    }

    private static void BindAdventureRole(IStatusManager? status, string roleId, bool overwrite)
    {
        var statusId = status?.InstanceId ?? "";
        if (string.IsNullOrWhiteSpace(statusId) || string.IsNullOrWhiteSpace(roleId))
        {
            return;
        }

        lock (SyncRoot)
        {
            if (overwrite || !AdventureRoles.ContainsKey(statusId))
            {
                AdventureRoles[statusId] = NormalizeRole(roleId);
            }
        }
    }

    private static string ResolveAdventureRole(IStatusManager? status)
    {
        if (status == null)
        {
            return "";
        }

        var statusId = status.InstanceId ?? "";
        lock (SyncRoot)
        {
            if (!string.IsNullOrWhiteSpace(statusId)
                && AdventureRoles.TryGetValue(statusId, out var remembered))
            {
                return remembered;
            }
        }

        var roleId = ResolveUnboundAdventureRole(status);
        if (!string.IsNullOrWhiteSpace(roleId))
        {
            BindAdventureRole(status, roleId, overwrite: false);
        }

        return NormalizeRole(roleId);
    }

    private static string ResolveUnboundAdventureRole(IStatusManager? status)
    {
        if (status == null)
        {
            return "";
        }

        var activePolymorph = PolymorphStateStore.ActiveFor(status);
        var currentRoleId = StatusApi.RoleId(status);
        var statusId = status.InstanceId ?? "";
        if (string.IsNullOrWhiteSpace(currentRoleId)
            && (ReferenceEquals(status, FightPlayer.Instance?.Status)
                || string.Equals(statusId, PlayerApi.LocalPlayerStatusId(), StringComparison.Ordinal)))
        {
            currentRoleId = PlayerApi.GetCurrentCareerId();
        }

        return NormalizeRole(ConstellationIdentityRules.ResolveAdventureRole(
            "",
            activePolymorph?.OriginalCareerId,
            currentRoleId));
    }

    private static string Sanitize(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-')
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }
}
