using AuraShared.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Resolution;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Infrastructure;
using Network.Command;

namespace AuraToolsExp.Dll.Features.DamageMeter.Network;

internal static class DamageMeterNetworkRuntime
{
    private static readonly DamageLedger LedgerInstance = new();
    private static readonly DamageRunLedger RunAggregateInstance = new();
    private static readonly DamageHistoryStore HistoryInstance = new();
    private static readonly Dictionary<string, DamageReceiveStream> ReporterStreams = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, DamageMeterSnapshot> FinalizedSnapshots = new(StringComparer.Ordinal);
    private static readonly DamageSubmissionOutbox SubmitOutbox = new();
    private static bool localEnding;
    private static bool finalMarkerAcknowledged;
    private static DateTime finalMarkerRetryAt;
    private static HashSet<string>? closingReporters;
    private static DateTime closingDeadline;
    private static string closingResult = "";
    private static readonly Dictionary<string, Queue<long>> ReporterRateWindows =
        new(StringComparer.OrdinalIgnoreCase);
    private static long localReporterSequence;
    private static long nextSubmitBatchFlushAtMs;
    private static int hostRoundSignalCount;
    private static bool snapshotRequestPending;
    private static string currentAdventureId = "";
    private static bool adventureStartPending;
    private static long activeRoomGeneration = -1;
    private static long activeLocalBattle = -1;
    private static readonly Dictionary<string, Dictionary<string, long>> FinalizedAcknowledgements = new(StringComparer.Ordinal);

    public static DamageLedger Ledger => LedgerInstance;

    public static DamageRunLedger RunAggregate => RunAggregateInstance;

    public static DamageHistoryStore History => HistoryInstance;

    public static string CurrentAdventureId => EnsureAdventureId();

    public static bool NetworkActive => GameApi.AuraToolsNetworkSession.NetworkActive;
    public static bool IsHost => GameApi.AuraToolsNetworkSession.IsAuthority;

    public static string LocalPlayerId => GameApi.AuraToolsNetworkSession.LocalPlayerId;

    public static void ResetTransient()
    {
        localReporterSequence = 0;
        hostRoundSignalCount = 0;
        snapshotRequestPending = false;
        ReporterStreams.Clear();
        localEnding = false;
        finalMarkerAcknowledged = false;
        closingReporters = null;
        ReporterRateWindows.Clear();
        SubmitOutbox.Clear();
        nextSubmitBatchFlushAtMs = 0;
    }

    public static void StartFight(bool sharedEnabled)
    {
        var localBattle = AuraBattleLifecycleRouter.CurrentBattleSessionId;
        if (LedgerInstance.InFight && activeLocalBattle == localBattle && activeRoomGeneration == AuraNetworkIdentityRuntime.RoomGeneration) return;
        if (IsHost && closingReporters != null) FinishClosing("下一场开始时仍有未确认事件。");
        else if (LedgerInstance.InFight) ArchiveInterruptedFight("新战斗开始前，上一场统计未完整结束。");
        ResetTransient();
        activeRoomGeneration = AuraNetworkIdentityRuntime.RoomGeneration;
        activeLocalBattle = localBattle;
        EnsureRunAggregateStarted();
        if (!NetworkActive)
        {
            LedgerInstance.StartFight(Guid.NewGuid().ToString("N"), sharedEnabled);
            NotifyChanged();
            return;
        }

        if (!IsHost)
        {
            LedgerInstance.ApplySnapshot(new DamageMeterSnapshot());
            RequestSnapshot();
            return;
        }

        var newSession = Guid.NewGuid().ToString("N");
        LedgerInstance.StartFight(newSession, sharedEnabled);
        Send(new DamageMeterControlCommand
        {
            Kind = DamageMeterControlKind.StartFight,
            IssuerPlayerId = LocalPlayerId,
            SessionId = newSession,
            SharedEnabled = sharedEnabled
        });
    }

    public static void BeginAdventure()
    {
        if (LedgerInstance.InFight) ArchiveInterruptedFight("冒险已切换，上一场统计未完整结束。");
        ResetTransient();
        AuraNetworkIdentityRuntime.EnsureCurrentAdventure();
        currentAdventureId = AuraNetworkIdentityRuntime.AdventureId;
        adventureStartPending = currentAdventureId.Length == 0;
        if (adventureStartPending) return;
        HistoryInstance.Clear();
        LedgerInstance.ApplySnapshot(new DamageMeterSnapshot());
        RunAggregateInstance.BeginAdventure(currentAdventureId, DateTime.UtcNow.ToString("O"));
        try
        {
            var restored = DamageHistoryStorage.Database.LoadRunState(currentAdventureId);
            if (restored != null) RunAggregateInstance.ApplySnapshot(restored);
            else DamageHistoryStorage.Database.SaveRunState(currentAdventureId, RunAggregateInstance.CreateSnapshot());
            DamageHistoryStorage.EnsureLegacyMigrations();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] adventure history initialization failed: " + ex.Message);
        }

        NotifyChanged();
    }

    public static void Tick()
    {
        if (LedgerInstance.InFight && activeRoomGeneration >= 0 && activeRoomGeneration != AuraNetworkIdentityRuntime.RoomGeneration)
        {
            ArchiveInterruptedFight("网络会话已变化，统计未完整结束。");
            ResetTransient();
            activeRoomGeneration = -1;
            return;
        }
        if (adventureStartPending && AuraNetworkIdentityRuntime.AdventureId.Length > 0) BeginAdventure();
        var now = NowMs();
        if (NetworkActive && SubmitOutbox.Count > 0 && now >= nextSubmitBatchFlushAtMs) FlushPendingSubmissions();
        if (NetworkActive && localEnding && !finalMarkerAcknowledged && DateTime.UtcNow >= finalMarkerRetryAt)
        {
            finalMarkerRetryAt = DateTime.UtcNow.AddSeconds(1);
            Send(new DamageMeterSubmitBatchCommand
            {
                ProtocolVersion = DamageMeterProtocol.Version, SessionId = LedgerInstance.SessionId,
                IsFinalMarker = true, FinalReporterSequence = localReporterSequence
            });
        }
        if (IsHost && closingReporters != null)
        {
            if (closingReporters.All(id => ReporterStreams.TryGetValue(id, out var stream) && stream.IsComplete)) FinishClosing("");
            else if (DateTime.UtcNow >= closingDeadline) FinishClosing("结算时仍有玩家的伤害事件未确认。");
        }
    }

    public static void RestoreAdventureHistory()
    {
        if (!IsHost || HistoryInstance.TotalCount > 0)
        {
            return;
        }

        try
        {
            var adventureId = EnsureAdventureId();
            if (adventureId.Length == 0) return;
            DamageHistoryStorage.EnsureLegacyMigrations();

            var page = DamageHistoryStorage.Database.LoadFightPage(
                adventureId,
                pageSize: DamageHistoryDatabase.DefaultPageSize);
            HistoryInstance.ApplyRecent(page.Items, page.TotalCount);
            var runState = DamageHistoryStorage.Database.LoadRunState(adventureId);
            if (runState != null)
            {
                RunAggregateInstance.ApplySnapshot(runState);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] adventure history restore failed: " + ex.Message);
        }

        NotifyChanged();
    }

    public static void StartRound()
    {
        if (!LedgerInstance.InFight)
        {
            return;
        }

        FlushPendingSubmissions(immediate: true);

        if (!IsHost)
        {
            return;
        }

        var desiredRound = ++hostRoundSignalCount;
        if (!NetworkActive)
        {
            LedgerInstance.StartRound(desiredRound);
            NotifyChanged();
            return;
        }

        Send(new DamageMeterControlCommand
        {
            Kind = DamageMeterControlKind.StartRound,
            IssuerPlayerId = LocalPlayerId,
            SessionId = LedgerInstance.SessionId,
            RoundIndex = desiredRound
        });
    }

    public static void EndFight(string result)
    {
        if (!LedgerInstance.InFight)
        {
            return;
        }

        localEnding = true;
        finalMarkerRetryAt = DateTime.MinValue;
        FlushPendingSubmissions(immediate: true);
        Tick();

        if (!NetworkActive)
        {
            LedgerInstance.EndFight();
            ArchiveSnapshot(LedgerInstance.CreateSnapshot(), result);
            NotifyChanged();
            return;
        }

        if (IsHost)
        {
            Send(new DamageMeterControlCommand
            {
                Kind = DamageMeterControlKind.EndFight,
                IssuerPlayerId = LocalPlayerId,
                SessionId = LedgerInstance.SessionId,
                Result = result
            });
        }
    }

    public static void Submit(DamageEvent damage)
    {
        if (damage == null || localEnding || !LedgerInstance.InFight || !LedgerInstance.SharedEnabled)
        {
            return;
        }

        damage.ProtocolVersion = DamageMeterProtocol.Version;
        damage.SessionId = LedgerInstance.SessionId;
        damage.ReporterPlayerId = LocalPlayerId;
        damage.ReporterSequence = ++localReporterSequence;
        damage.RoundIndex = Math.Max(1, LedgerInstance.CurrentRoundIndex);
        damage.ClientTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (!NetworkActive)
        {
            damage.ServerSequence = LedgerInstance.NextServerSequence();
            if (LedgerInstance.Apply(damage))
            {
                RunAggregateInstance.Apply(damage);
                DamageMeterPerformanceCounters.RecordSubmitted(localApplied: true);
                NotifyChanged();
            }

            return;
        }

        DamageMeterPerformanceCounters.RecordSubmitted(localApplied: false);
        EnqueueSubmit(damage);
    }

    public static void FlushPendingSubmissions(bool immediate = false)
    {
        if (!NetworkActive || SubmitOutbox.Count == 0) return;
        nextSubmitBatchFlushAtMs = NowMs() + Math.Max(250, SubmitBatchIntervalMs());
        var candidates = SubmitOutbox.Batch(AuraToolsConfigService.MatchExperience.DamageMeter.MaxEventsPerBatch,
            values => AuraSharedPayloadBudget.TryMeasureNativeRpc(new DamageMeterSubmitBatchCommand
            { ProtocolVersion = DamageMeterProtocol.Version, SessionId = LedgerInstance.SessionId, Candidates = values }, out var bytes)
            && bytes <= DamageSubmissionProtocol.MaximumBatchBytes);
        if (candidates.Count == 0)
        {
            LedgerInstance.MarkIncomplete("单条伤害事件超过传输预算。");
            return;
        }
        Send(new DamageMeterSubmitBatchCommand
        { ProtocolVersion = DamageMeterProtocol.Version, SessionId = LedgerInstance.SessionId, Candidates = candidates });
    }

    private static void EnqueueSubmit(DamageEvent damage)
    {
        if (!SubmitOutbox.Add(damage))
        {
            LedgerInstance.MarkIncomplete("未确认伤害事件超过缓存上限。");
            return;
        }
        if (nextSubmitBatchFlushAtMs == 0) nextSubmitBatchFlushAtMs = NowMs() + SubmitBatchIntervalMs();
    }

    internal static void ReceiveAcknowledgement(DamageMeterSubmitBatchCommand reply)
    {
        if (reply.ReplyPlayerId != LocalPlayerId || reply.SessionId != LedgerInstance.SessionId) return;
        if (reply.AcknowledgementProtocol != DamageMeterProtocol.Version)
        {
            LedgerInstance.MarkIncomplete("房主 DPT 协议不支持可靠确认，已暂停本机提交。");
            SubmitOutbox.Clear(); localEnding = true; finalMarkerAcknowledged = true;
            return;
        }
        if (!string.IsNullOrWhiteSpace(reply.BatchRejection))
        {
            LedgerInstance.MarkIncomplete(reply.BatchRejection);
            if (reply.BatchRejection == "protocol mismatch") { SubmitOutbox.Clear(); localEnding = true; finalMarkerAcknowledged = true; }
            return;
        }
        SubmitOutbox.Acknowledge(reply.AcknowledgedThrough);
        if (reply.FinalMarkerAccepted) finalMarkerAcknowledged = true;
        if (reply.RejectionReasons.Count > 0) LedgerInstance.MarkIncomplete(reply.RejectionReasons[0]);
        if (LedgerInstance.ServerSequence < reply.AcknowledgedServerSequence) RequestSnapshot();
    }

    internal static void ResolveSubmission(DamageMeterSubmitBatchCommand command, AuraToolsRpcSender sender)
    {
        command.ReplyPlayerId = sender.PlayerId;
        command.AcknowledgementProtocol = DamageMeterProtocol.Version;
        command.Confirmed = new List<DamageEvent>(); command.RejectionReasons = new List<string>();
        command.AcknowledgedThrough = 0; command.AcknowledgedServerSequence = 0; command.FinalMarkerAccepted = false;
        if (!DamageMeterAuthorityPolicy.RequireLobbyMember(sender, out var reason) || !IsHost)
        { command.BatchRejection = reason.Length > 0 ? reason : "not host"; return; }
        if (command.ProtocolVersion != DamageMeterProtocol.Version) { command.BatchRejection = "protocol mismatch"; return; }
        if (!SessionMatches(command.SessionId) || !LedgerInstance.InFight)
        {
            if (FinalizedSnapshots.TryGetValue(command.SessionId, out var closed)
                && FinalizedAcknowledgements.TryGetValue(command.SessionId, out var receipts)
                && receipts.TryGetValue(sender.PlayerId, out var through))
            {
                command.BatchRejection = "";
                command.AcknowledgedThrough = through;
                command.AcknowledgedServerSequence = closed.ServerSequence;
                command.FinalMarkerAccepted = command.IsFinalMarker && command.FinalReporterSequence <= through;
            }
            else command.BatchRejection = "inactive or mismatched session";
            return;
        }
        command.BatchRejection = "";
        if (!ReporterStreams.TryGetValue(sender.PlayerId, out var stream))
            ReporterStreams[sender.PlayerId] = stream = new DamageReceiveStream();
        if (command.IsFinalMarker)
        {
            command.FinalMarkerAccepted = stream.Complete(command.FinalReporterSequence);
            if (!command.FinalMarkerAccepted) command.BatchRejection = "invalid final sequence";
        }
        else
        {
            AcceptBatchOnServer(command.Candidates, sender, out var confirmed, out var rejections);
            command.Confirmed = confirmed; command.RejectionReasons = rejections;
        }
        command.AcknowledgedThrough = stream.Through;
        command.AcknowledgedServerSequence = LedgerInstance.ServerSequence;
    }

    public static bool AcceptBatchOnServer(IEnumerable<DamageEvent>? candidates, AuraToolsRpcSender sender,
        out List<DamageEvent> confirmed, out List<string> rejections)
    {
        confirmed = new List<DamageEvent>(); rejections = new List<string>();
        if (!DamageMeterAuthorityPolicy.RequireLobbyMember(sender, out var rejected) || !IsHost)
        { rejections.Add(rejected); return false; }
        var values = (candidates ?? Enumerable.Empty<DamageEvent>()).Take(DamageSubmissionProtocol.MaximumBatchEvents + 1).ToList();
        if (values.Count > DamageSubmissionProtocol.MaximumBatchEvents)
        { rejections.Add("batch limit exceeded"); return false; }
        if (!ReporterStreams.TryGetValue(sender.PlayerId, out var stream)) ReporterStreams[sender.PlayerId] = stream = new DamageReceiveStream();
        foreach (var value in values)
        {
            if (value == null || !SessionMatches(value.SessionId) || !stream.Add(value))
            { rejections.Add("invalid or out-of-window sequence"); LedgerInstance.MarkIncomplete(rejections.Last()); }
        }
        while (stream.Next is DamageEvent candidate && confirmed.Count < DamageSubmissionProtocol.MaximumBatchEvents)
        {
            if (AcceptOnServer(candidate, sender, out var accepted, out var reason))
            {
                confirmed.Add(accepted);
                stream.ConfirmNext();
            }
            else if (reason == "rate limited") break;
            else
            {
                stream.ConfirmNext();
                rejections.Add("sequence " + candidate.ReporterSequence + ": " + reason);
                LedgerInstance.MarkIncomplete(rejections.Last());
            }
        }
        if (confirmed.Count > 0) NotifyChanged();
        return confirmed.Count > 0 || rejections.Count == 0;
    }

    private static bool AcceptOnServer(
        DamageEvent candidate,
        AuraToolsRpcSender sender,
        out DamageEvent confirmed,
        out string rejection)
    {
        confirmed = new DamageEvent();
        rejection = "";
        if (!IsHost)
        {
            rejection = "not host";
            return false;
        }

        if (!DamageMeterAuthorityPolicy.TryBindReporter(candidate, sender, out var boundCandidate, out rejection))
        {
            return false;
        }

        if (!ValidateCandidate(boundCandidate, out rejection))
        {
            return false;
        }

        confirmed = boundCandidate.Copy();
        var resolvedSource = CombatantTeamResolver.ResolveStatus(confirmed.SourceInstanceId);
        if (resolvedSource != null)
        {
            var attribution = CombatantTeamResolver.ResolveAttribution(
                resolvedSource,
                confirmed.SourceInstanceId,
                confirmed.SourceDisplayName);
            confirmed.SourceInstanceId = attribution.InstanceId;
            confirmed.SourceDisplayName = attribution.DisplayName;
            confirmed.SourceTeam = attribution.Team;
        }
        else if (string.Equals(confirmed.SourceInstanceId, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            confirmed.SourceDisplayName = "未知来源";
            confirmed.SourceTeam = DamageTeam.Unknown;
        }

        confirmed.ServerSequence = LedgerInstance.NextServerSequence();
        confirmed.RoundIndex = Math.Max(1, Math.Min(confirmed.RoundIndex, Math.Max(1, LedgerInstance.CurrentRoundIndex)));
        if (!LedgerInstance.Apply(confirmed))
        {
            rejection = "ledger rejected event";
            return false;
        }

        RunAggregateInstance.Apply(confirmed);
        return true;
    }

    public static void ApplyConfirmed(DamageEvent confirmed)
    {
        ApplyConfirmedCore(confirmed, true);
    }

    public static void ApplyConfirmedBatch(IEnumerable<DamageEvent>? confirmed)
    {
        snapshotRequestPending = false;
        var changed = false;
        foreach (var damage in confirmed ?? Enumerable.Empty<DamageEvent>())
        {
            var result = ApplyConfirmedCore(damage, false);
            changed = changed || result == ApplyConfirmedResult.Applied;
            if (result == ApplyConfirmedResult.SnapshotRequested)
            {
                if (changed)
                {
                    NotifyChanged();
                }

                return;
            }
        }

        if (changed)
        {
            NotifyChanged();
        }
    }

    private static ApplyConfirmedResult ApplyConfirmedCore(DamageEvent confirmed, bool notify)
    {
        snapshotRequestPending = false;
        if (confirmed == null)
        {
            return ApplyConfirmedResult.Ignored;
        }

        if (confirmed.ServerSequence > LedgerInstance.ServerSequence + 1)
        {
            RequestSnapshot();
            return ApplyConfirmedResult.SnapshotRequested;
        }

        if (!LedgerInstance.Apply(confirmed))
        {
            return ApplyConfirmedResult.Ignored;
        }

        if (!RunAggregateInstance.Apply(confirmed))
        {
            RequestSnapshot();
            return ApplyConfirmedResult.SnapshotRequested;
        }

        if (notify)
        {
            NotifyChanged();
        }

        return ApplyConfirmedResult.Applied;
    }

    public static bool ApplyControlOnServer(
        DamageMeterControlCommand command,
        AuraToolsRpcSender sender,
        out string rejection)
    {
        rejection = "";
        if (!IsHost)
        {
            rejection = "not host";
            return false;
        }

        if (!DamageMeterAuthorityPolicy.RequireHostControl(sender, out rejection))
        {
            return false;
        }

        command.IssuerPlayerId = sender.PlayerId;
        if (command.ProtocolVersion != DamageMeterProtocol.Version) { rejection = "protocol mismatch"; return false; }
        if (command.FinalizeOnly)
        {
            if (!FinalizedSnapshots.TryGetValue(command.SessionId, out var closed))
            { rejection = "closed session not available"; return false; }
            command.Snapshot = AuraSharedJson.Deserialize<DamageMeterSnapshot>(AuraSharedJson.Serialize(closed));
            return true;
        }
        switch (command.Kind)
        {
            case DamageMeterControlKind.StartFight:
                if (!SessionMatches(command.SessionId))
                {
                    ResetTransient();
                    LedgerInstance.StartFight(command.SessionId, command.SharedEnabled);
                }
                break;
            case DamageMeterControlKind.StartRound:
                if (!SessionMatches(command.SessionId))
                {
                    rejection = "round session mismatch";
                    return false;
                }

                LedgerInstance.StartRound(Math.Max(1, command.RoundIndex));
                break;
            case DamageMeterControlKind.EndFight:
                if (!SessionMatches(command.SessionId))
                {
                    rejection = "end session mismatch";
                    return false;
                }

                closingReporters ??= new HashSet<string>(GameApi.AuraToolsNetworkSession.PlayerIds, StringComparer.OrdinalIgnoreCase);
                closingResult = command.Result;
                closingDeadline = DateTime.UtcNow.AddSeconds(8);
                break;
            default:
                rejection = "unsupported control";
                return false;
        }

        command.Snapshot = CreateNetworkSnapshot("control:" + command.Kind);
        NotifyChanged();
        return true;
    }

    public static void ApplyControlSnapshot(DamageMeterControlCommand command)
    {
        if (IsHost) return; // The authoritative path already committed this control.
        if (command?.Snapshot == null)
        {
            return;
        }

        if (command.Kind != DamageMeterControlKind.EndFight || SessionMatches(command.Snapshot.SessionId))
            ApplySnapshot(command.Snapshot);
        if (string.Equals(command.Kind, DamageMeterControlKind.EndFight, StringComparison.Ordinal)
            && !command.Snapshot.InFight
            && ArchiveSnapshot(command.Snapshot, command.Result))
        {
            NotifyChanged();
        }
    }

    public static void ApplySnapshot(DamageMeterSnapshot snapshot)
    {
        if (IsHost) return;
        snapshotRequestPending = false;
        if (snapshot == null)
        {
            return;
        }

        var ledgerChanged = LedgerInstance.ApplySnapshot(snapshot);
        var aggregateChanged = snapshot.RunAggregate != null
                               && RunAggregateInstance.ApplySnapshot(snapshot.RunAggregate);
        if (ledgerChanged || aggregateChanged)
        {
            NotifyChanged();
        }
    }

    private static DamageMeterSnapshot CreateNetworkSnapshot(string source)
    {
        var startedAt = DamageMeterPerformanceCounters.StartSample();
        var snapshot = LedgerInstance.CreateSnapshot();
        snapshot.MinimumProtocolVersion = DamageMeterProtocol.MinimumSupportedVersion;
        snapshot.RunAggregate = RunAggregateInstance.CreateSnapshot();
        var beforeBytes = DamageMeterSnapshotCompactor.EstimateSnapshotBytes(snapshot);
        DamageMeterSnapshotCompactor.CompactNetworkSnapshot(snapshot, source);
        var afterBytes = DamageMeterSnapshotCompactor.EstimateSnapshotBytes(snapshot);
        DamageMeterPerformanceCounters.RecordSnapshot(
            DamageMeterPerformanceCounters.ElapsedMs(startedAt),
            beforeBytes,
            afterBytes,
            afterBytes > 0 && beforeBytes > 0 && afterBytes < beforeBytes);
        return snapshot;
    }

    public static bool TryCreateServerSnapshot(
        AuraToolsRpcSender sender,
        out DamageMeterSnapshot? snapshot,
        out string rejection)
    {
        snapshot = null;
        rejection = "";
        if (!IsHost)
        {
            rejection = "not host";
            return false;
        }

        if (!DamageMeterAuthorityPolicy.RequireLobbyMember(sender, out rejection))
        {
            return false;
        }

        snapshot = CreateNetworkSnapshot("snapshot-request");
        return true;
    }

    public static void EnsureControlResponseFits(DamageMeterControlCommand command)
    {
        EnsureResponseFits(
            command,
            () =>
            {
                if (command.Snapshot != null)
                {
                    DamageMeterSnapshotCompactor.MinimizeNetworkSnapshot(command.Snapshot);
                }
            },
            () =>
            {
                command.Snapshot = DamageMeterSnapshotCompactor.CreateStatusOnlySnapshot(command.Snapshot);
                command.RejectionReason = "snapshot compacted: payload too large";
            },
            "control:" + command.Kind);
    }

    public static void EnsureSnapshotResponseFits(DamageMeterSnapshotCommand command)
    {
        EnsureResponseFits(
            command,
            () =>
            {
                if (command.Snapshot != null)
                {
                    DamageMeterSnapshotCompactor.MinimizeNetworkSnapshot(command.Snapshot);
                }
            },
            () =>
            {
                command.Snapshot = null;
                command.RejectionReason = "snapshot omitted: payload too large";
            },
            "snapshot-response");
    }

    private static void EnsureResponseFits(
        RpcCommandBase command,
        Action compactSnapshot,
        Action omitSnapshot,
        string source)
    {
        if (AuraToolsRpcPayloadGuard.FitsSoftLimit(
                command,
                AuraToolsRpcPayloadGuard.DefaultSoftLimitBytes,
                out var bytes,
                out _))
        {
            return;
        }

        AuraToolsLog.Warn("[DamageMeter] compacting oversized RPC response. source="
                          + source
                          + ", bytes="
                          + bytes
                          + ", softLimit="
                          + AuraToolsRpcPayloadGuard.DefaultSoftLimitBytes);
        compactSnapshot();
        if (AuraToolsRpcPayloadGuard.FitsSoftLimit(
                command,
                AuraToolsRpcPayloadGuard.DefaultSoftLimitBytes,
                out bytes,
                out _))
        {
            return;
        }

        omitSnapshot();
        AuraToolsLog.Warn("[DamageMeter] reduced oversized RPC snapshot. source="
                          + source
                          + ", bytes="
                          + bytes
                          + ", softLimit="
                          + AuraToolsRpcPayloadGuard.DefaultSoftLimitBytes);
    }

    public static void RequestSnapshot()
    {
        if (!NetworkActive || snapshotRequestPending)
        {
            return;
        }

        snapshotRequestPending = true;
        Send(new DamageMeterSnapshotCommand
        {
            RequesterPlayerId = LocalPlayerId,
            ProtocolVersion = DamageMeterProtocol.Version
        });
    }

    private static bool ValidateCandidate(DamageEvent value, out string rejection)
    {
        rejection = "";
        if (value == null
            || !DamageMeterProtocol.IsCompatible(
                value.ProtocolVersion,
                value.MinimumProtocolVersion,
                value.RequiredCapabilities))
        {
            rejection = "protocol mismatch";
            return false;
        }

        if (!LedgerInstance.InFight
            || !LedgerInstance.SharedEnabled
            || !SessionMatches(value.SessionId))
        {
            rejection = "inactive or mismatched session";
            return false;
        }

        if (value.ReporterSequence <= 0)
        {
            rejection = "duplicate reporter sequence";
            return false;
        }

        if (!ValidDamage(value.HpDamage)
            || !ValidDamage(value.ShieldDamage)
            || !ValidDamage(value.FinalDamage)
            || value.HpDamage <= 0 && value.ShieldDamage <= 0)
        {
            rejection = "invalid damage amount";
            return false;
        }

        if (!ValidText(value.SourceInstanceId)
            || !ValidText(value.TargetInstanceId)
            || !ValidText(value.SourceDataId)
            || !ValidText(value.DetailLabel)
            || !ValidText(value.DamageType)
            || !ValidText(value.SourceDisplayName))
        {
            rejection = "invalid text field";
            return false;
        }

        if (string.IsNullOrWhiteSpace(value.TargetInstanceId))
        {
            rejection = "target is empty";
            return false;
        }

        if (!AllowRate(value.ReporterPlayerId))
        {
            rejection = "rate limited";
            return false;
        }

        return true;
    }

    private static bool AllowRate(string reporter)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!ReporterRateWindows.TryGetValue(reporter, out var window))
        {
            window = new Queue<long>();
            ReporterRateWindows[reporter] = window;
        }

        while (window.Count > 0 && now - window.Peek() > 1000)
        {
            window.Dequeue();
        }

        if (window.Count >= 240)
        {
            return false;
        }

        window.Enqueue(now);
        return true;
    }

    private static bool SessionMatches(string sessionId)
    {
        return string.Equals(LedgerInstance.SessionId, sessionId, StringComparison.Ordinal);
    }

    private static int SubmitBatchIntervalMs()
    {
        return Math.Max(50, AuraToolsConfigService.MatchExperience.DamageMeter.SubmitBatchIntervalMs);
    }

    private static long NowMs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private static string EnsureAdventureId()
    {
        if (AuraNetworkIdentityRuntime.EnsureCurrentAdventure())
            currentAdventureId = AuraNetworkIdentityRuntime.AdventureId;
        else currentAdventureId = "";
        return currentAdventureId;
    }

    private static void EnsureRunAggregateStarted()
    {
        var adventureId = EnsureAdventureId();
        if (string.IsNullOrWhiteSpace(adventureId) || RunAggregateInstance.AdventureId == adventureId) return;

        RunAggregateInstance.BeginAdventure(adventureId, DateTime.UtcNow.ToString("O"));
    }

    private static bool ArchiveSnapshot(DamageMeterSnapshot snapshot, string result)
    {
        if (snapshot == null)
        {
            return false;
        }

        if (IsHost)
        {
            RunAggregateInstance.RecordEncounter(snapshot);
        }

        var record = new DamageFightRecord
        {
            SessionId = snapshot.SessionId ?? "",
            Result = string.IsNullOrWhiteSpace(result) ? "Unknown" : result.Trim(),
            EndedUtc = DateTime.UtcNow.ToString("O"),
            Snapshot = snapshot
        };

        try
        {
            var adventureId = !string.IsNullOrWhiteSpace(snapshot.RunAggregate?.AdventureId)
                ? snapshot.RunAggregate!.AdventureId : EnsureAdventureId();
            if (adventureId.Length == 0) return false;
            var stored = DamageHistoryStorage.Database.AppendFight(adventureId, record);
            if (stored == null)
            {
                return false;
            }

            if (IsHost)
            {
                DamageHistoryStorage.Database.SaveRunState(adventureId, RunAggregateInstance.CreateSnapshot());
            }

            HistoryInstance.ArchiveRecent(stored, DamageHistoryStorage.Database.CountFights(adventureId));
            return true;
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] SQLite fight archive failed: " + ex.Message);
            return HistoryInstance.Archive(snapshot, result, record.EndedUtc);
        }
    }

    private static bool ValidDamage(int value)
    {
        return value >= 0 && value <= DamageMeterProtocol.MaxDamagePerEvent;
    }

    private static bool ValidText(string value)
    {
        return value == null || value.Length <= DamageMeterProtocol.MaxStringLength;
    }

    private static bool Send(RpcCommandBase command, bool deferSubmit = true)
    {
        if (command is DamageMeterControlCommand control) control.ProtocolVersion = DamageMeterProtocol.Version;
        var sent = AuraToolsRpcTransport.Send(PlayerManager.Instance, command, "DamageMeter." + command.GetType().Name);
        if (!sent) snapshotRequestPending = false;
        return sent;
    }

    private static void FinishClosing(string incomplete)
    {
        if (closingReporters == null) return;
        if (incomplete.Length > 0) LedgerInstance.MarkIncomplete(incomplete);
        var result = closingResult;
        closingReporters = null;
        LedgerInstance.EndFight();
        var snapshot = LedgerInstance.CreateSnapshot();
        RunAggregateInstance.RecordEncounter(snapshot);
        snapshot.RunAggregate = RunAggregateInstance.CreateSnapshot();
        ArchiveSnapshot(snapshot, result);
        FinalizedSnapshots[snapshot.SessionId] = snapshot;
        FinalizedAcknowledgements[snapshot.SessionId] = ReporterStreams.ToDictionary(pair => pair.Key, pair => pair.Value.Through);
        while (FinalizedSnapshots.Count > 16)
        {
            var oldest = FinalizedSnapshots.Keys.First();
            FinalizedSnapshots.Remove(oldest); FinalizedAcknowledgements.Remove(oldest);
        }
        Send(new DamageMeterControlCommand { Kind = DamageMeterControlKind.EndFight, FinalizeOnly = true,
            SessionId = snapshot.SessionId, Result = result, IssuerPlayerId = LocalPlayerId });
        NotifyChanged();
    }

    private static void ArchiveInterruptedFight(string reason)
    {
        LedgerInstance.MarkIncomplete(reason);
        LedgerInstance.EndFight();
        var snapshot = LedgerInstance.CreateSnapshot();
        RunAggregateInstance.RecordEncounter(snapshot);
        snapshot.RunAggregate = RunAggregateInstance.CreateSnapshot();
        ArchiveSnapshot(snapshot, "Interrupted");
    }

    private static void NotifyChanged()
    {
        AuraToolsDamageMeterRuntime.NotifyLedgerChanged();
    }

    private enum ApplyConfirmedResult
    {
        Ignored,
        Applied,
        SnapshotRequested
    }
}
