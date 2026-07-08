using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Resolution;
using AuraToolsExp.Dll.Infrastructure;
using Network.Command;

namespace AuraToolsExp.Dll.Features.DamageMeter.Network;

internal static class DamageMeterNetworkRuntime
{
    private const int NetworkSnapshotSoftLimitBytes = AuraToolsRpcPayloadGuard.DefaultSoftLimitBytes;
    private const int NetworkDetailsSoftLimit = 24;
    private const int NetworkRoundsSoftLimit = 32;
    private const int NetworkCombatantsSoftLimit = 16;
    private const int NetworkMinimalCombatantsLimit = 8;
    private static readonly DamageLedger LedgerInstance = new();
    private static readonly DamageRunLedger RunAggregateInstance = new();
    private static readonly DamageHistoryStore HistoryInstance = new();
    private static readonly OutOfRunDamageHistoryStore OutOfRunHistoryInstance = new();
    private static readonly Dictionary<string, long> LastReporterSequence =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Queue<long>> ReporterRateWindows =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<DamageEvent> PendingSubmitBatch = new();
    private static long localReporterSequence;
    private static long nextSubmitBatchFlushAtMs;
    private static int hostRoundSignalCount;
    private static bool snapshotRequestPending;
    private static string currentAdventureId = "";

    public static DamageLedger Ledger => LedgerInstance;

    public static DamageRunLedger RunAggregate => RunAggregateInstance;

    public static DamageHistoryStore History => HistoryInstance;

    public static OutOfRunDamageHistoryStore OutOfRunHistory => OutOfRunHistoryInstance;

    public static string CurrentAdventureId => EnsureAdventureId();

    public static bool IsMultiplayer => PlayerManager.Instance != null;

    public static bool IsHost => !IsMultiplayer || PlayerManager.Instance?.isServer == true;

    public static string LocalPlayerId => PlayerManager.Instance?.PlayerId ?? "single-player";

    public static void ResetTransient()
    {
        localReporterSequence = 0;
        hostRoundSignalCount = 0;
        snapshotRequestPending = false;
        LastReporterSequence.Clear();
        ReporterRateWindows.Clear();
        PendingSubmitBatch.Clear();
        nextSubmitBatchFlushAtMs = 0;
    }

    public static void StartFight(bool sharedEnabled)
    {
        ResetTransient();
        EnsureRunAggregateStarted();
        if (!IsMultiplayer)
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

        Send(new DamageMeterControlCommand
        {
            Kind = DamageMeterControlKind.StartFight,
            IssuerPlayerId = LocalPlayerId,
            SessionId = Guid.NewGuid().ToString("N"),
            SharedEnabled = sharedEnabled
        });
    }

    public static void BeginAdventure()
    {
        ResetTransient();
        currentAdventureId = Guid.NewGuid().ToString("N");
        HistoryInstance.Clear();
        LedgerInstance.ApplySnapshot(new DamageMeterSnapshot());
        RunAggregateInstance.BeginAdventure(currentAdventureId, DateTime.UtcNow.ToString("O"));
        if (IsHost)
        {
            DamageMeterPersistence.Clear();
        }

        NotifyChanged();
    }

    public static void Tick()
    {
        if (!IsMultiplayer || PendingSubmitBatch.Count == 0)
        {
            return;
        }

        var now = NowMs();
        if (nextSubmitBatchFlushAtMs > 0 && now >= nextSubmitBatchFlushAtMs)
        {
            FlushPendingSubmissions();
        }
    }

    public static void RestoreAdventureHistory()
    {
        if (!IsHost || HistoryInstance.Records.Count > 0)
        {
            return;
        }

        HistoryInstance.ApplySnapshot(DamageMeterPersistence.Load());
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
        if (!IsMultiplayer)
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

        FlushPendingSubmissions(immediate: true);

        if (!IsMultiplayer)
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
        if (damage == null || !LedgerInstance.InFight || !LedgerInstance.SharedEnabled)
        {
            return;
        }

        damage.ProtocolVersion = DamageMeterProtocol.Version;
        damage.SessionId = LedgerInstance.SessionId;
        damage.ReporterPlayerId = LocalPlayerId;
        damage.ReporterSequence = ++localReporterSequence;
        damage.RoundIndex = Math.Max(1, LedgerInstance.CurrentRoundIndex);
        damage.ClientTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (!IsMultiplayer)
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
        if (!IsMultiplayer || PendingSubmitBatch.Count == 0)
        {
            PendingSubmitBatch.Clear();
            nextSubmitBatchFlushAtMs = 0;
            return;
        }

        var startedAt = DamageMeterPerformanceCounters.StartSample();
        var eventCount = PendingSubmitBatch.Count;
        nextSubmitBatchFlushAtMs = 0;
        var maximum = Math.Max(1, AuraToolsConfigService.MatchExperience.DamageMeter.MaxEventsPerBatch);
        var commandCount = 0;
        for (var offset = 0; offset < PendingSubmitBatch.Count; offset += maximum)
        {
            var count = Math.Min(maximum, PendingSubmitBatch.Count - offset);
            var candidates = new List<DamageEvent>(count);
            for (var i = 0; i < count; i++)
            {
                candidates.Add(PendingSubmitBatch[offset + i]);
            }

            Send(new DamageMeterSubmitBatchCommand
            {
                Candidates = candidates
            }, deferSubmit: !immediate);
            commandCount++;
        }

        PendingSubmitBatch.Clear();
        DamageMeterPerformanceCounters.RecordBatchFlush(
            eventCount,
            commandCount,
            DamageMeterPerformanceCounters.ElapsedMs(startedAt));
    }

    private static void EnqueueSubmit(DamageEvent damage)
    {
        PendingSubmitBatch.Add(damage.Copy());
        DamageMeterPerformanceCounters.RecordPendingBatch(PendingSubmitBatch.Count);
        var now = NowMs();
        if (nextSubmitBatchFlushAtMs <= 0)
        {
            nextSubmitBatchFlushAtMs = now + SubmitBatchIntervalMs();
        }

        if (PendingSubmitBatch.Count >= Math.Max(1, AuraToolsConfigService.MatchExperience.DamageMeter.MaxEventsPerBatch)
            || now >= nextSubmitBatchFlushAtMs)
        {
            FlushPendingSubmissions();
        }
    }

    public static bool AcceptOnServer(
        DamageEvent candidate,
        AuraToolsRpcSender sender,
        out DamageEvent confirmed,
        out string rejection)
    {
        return AcceptOnServer(candidate, sender, out confirmed, out rejection, true);
    }

    public static bool AcceptBatchOnServer(
        IEnumerable<DamageEvent>? candidates,
        AuraToolsRpcSender sender,
        out List<DamageEvent> confirmed,
        out List<string> rejections)
    {
        confirmed = new List<DamageEvent>();
        rejections = new List<string>();
        var limit = Math.Max(1, AuraToolsConfigService.MatchExperience.DamageMeter.MaxEventsPerBatch);
        var consumed = 0;
        foreach (var candidate in candidates ?? Enumerable.Empty<DamageEvent>())
        {
            if (consumed >= limit)
            {
                rejections.Add("batch limit exceeded");
                break;
            }

            consumed++;
            if (AcceptOnServer(candidate, sender, out var accepted, out var rejection, false))
            {
                confirmed.Add(accepted);
            }
            else if (!string.IsNullOrWhiteSpace(rejection))
            {
                rejections.Add(rejection);
            }
        }

        if (confirmed.Count > 0)
        {
            NotifyChanged();
            return true;
        }

        if (rejections.Count == 0)
        {
            rejections.Add("empty batch");
        }

        return false;
    }

    private static bool AcceptOnServer(
        DamageEvent candidate,
        AuraToolsRpcSender sender,
        out DamageEvent confirmed,
        out string rejection,
        bool notify)
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
            confirmed.SourceDisplayName = CombatantTeamResolver.DisplayName(
                resolvedSource,
                confirmed.SourceInstanceId);
            confirmed.SourceTeam = CombatantTeamResolver.Resolve(
                resolvedSource,
                confirmed.SourceInstanceId);
        }
        else if (string.Equals(confirmed.SourceInstanceId, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            confirmed.SourceDisplayName = "未知来源";
            confirmed.SourceTeam = DamageTeam.Unknown;
        }

        confirmed.ServerSequence = LedgerInstance.NextServerSequence();
        confirmed.RoundIndex = Math.Max(1, LedgerInstance.CurrentRoundIndex);
        if (!LedgerInstance.Apply(confirmed))
        {
            rejection = "ledger rejected event";
            return false;
        }

        RunAggregateInstance.Apply(confirmed);
        LastReporterSequence[confirmed.ReporterPlayerId] = confirmed.ReporterSequence;
        if (notify)
        {
            NotifyChanged();
        }

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
        switch (command.Kind)
        {
            case DamageMeterControlKind.StartFight:
                ResetTransient();
                LedgerInstance.StartFight(command.SessionId, command.SharedEnabled);
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

                LedgerInstance.EndFight();
                ArchiveSnapshot(LedgerInstance.CreateSnapshot(), command.Result);
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
        if (command?.Snapshot == null)
        {
            return;
        }

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
        snapshotRequestPending = false;
        if (snapshot == null)
        {
            return;
        }

        var ledgerChanged = LedgerInstance.ApplySnapshot(snapshot);
        var aggregateChanged = snapshot.RunAggregate != null
                               && RunAggregateInstance.ApplySnapshot(snapshot.RunAggregate);
        if (snapshot.ProtocolVersion == DamageMeterProtocol.Version
            && snapshot.History != null
            && snapshot.History.Count > 0)
        {
            HistoryInstance.ApplySnapshot(snapshot.History);
        }

        if (ledgerChanged || aggregateChanged)
        {
            NotifyChanged();
        }
    }

    public static DamageMeterSnapshot CreateServerSnapshot()
    {
        var snapshot = LedgerInstance.CreateSnapshot();
        snapshot.History = HistoryInstance.CreateSnapshot();
        snapshot.RunAggregate = RunAggregateInstance.CreateSnapshot();
        return snapshot;
    }

    private static DamageMeterSnapshot CreateNetworkSnapshot(string source)
    {
        var startedAt = DamageMeterPerformanceCounters.StartSample();
        var snapshot = LedgerInstance.CreateSnapshot();
        snapshot.History = new List<DamageFightRecord>();
        snapshot.RunAggregate = RunAggregateInstance.CreateSnapshot();
        var beforeBytes = EstimateSnapshotBytes(snapshot);
        CompactNetworkSnapshot(snapshot, source);
        var afterBytes = EstimateSnapshotBytes(snapshot);
        DamageMeterPerformanceCounters.RecordSnapshot(
            DamageMeterPerformanceCounters.ElapsedMs(startedAt),
            beforeBytes,
            afterBytes,
            afterBytes > 0 && beforeBytes > 0 && afterBytes < beforeBytes);
        return snapshot;
    }

    private static void CompactNetworkSnapshot(DamageMeterSnapshot snapshot, string source)
    {
        if (snapshot == null || SnapshotFits(snapshot))
        {
            return;
        }

        var beforeBytes = EstimateSnapshotBytes(snapshot);
        TrimDetailsAndRounds(snapshot, NetworkDetailsSoftLimit, NetworkRoundsSoftLimit);
        if (SnapshotFits(snapshot))
        {
            LogSnapshotCompacted(source, beforeBytes, EstimateSnapshotBytes(snapshot));
            return;
        }

        TrimCombatants(snapshot, NetworkCombatantsSoftLimit);
        if (SnapshotFits(snapshot))
        {
            LogSnapshotCompacted(source, beforeBytes, EstimateSnapshotBytes(snapshot));
            return;
        }

        TrimDetailsAndRounds(snapshot, maxDetails: 8, maxRounds: 12);
        if (SnapshotFits(snapshot))
        {
            LogSnapshotCompacted(source, beforeBytes, EstimateSnapshotBytes(snapshot));
            return;
        }

        MinimizeNetworkSnapshot(snapshot);
        LogSnapshotCompacted(source, beforeBytes, EstimateSnapshotBytes(snapshot));
    }

    private static void MinimizeNetworkSnapshot(DamageMeterSnapshot snapshot)
    {
        snapshot.History = new List<DamageFightRecord>();
        TrimCombatants(snapshot, NetworkMinimalCombatantsLimit);
        TrimDetailsAndRounds(snapshot, maxDetails: 0, maxRounds: 0);
    }

    private static DamageMeterSnapshot CreateStatusOnlySnapshot(DamageMeterSnapshot? source)
    {
        return new DamageMeterSnapshot
        {
            ProtocolVersion = DamageMeterProtocol.Version,
            SessionId = source?.SessionId ?? LedgerInstance.SessionId,
            InFight = source?.InFight ?? LedgerInstance.InFight,
            SharedEnabled = source?.SharedEnabled ?? LedgerInstance.SharedEnabled,
            CurrentRoundIndex = source?.CurrentRoundIndex ?? LedgerInstance.CurrentRoundIndex,
            CompletedRoundCount = source?.CompletedRoundCount ?? LedgerInstance.CompletedRoundCount,
            ServerSequence = source?.ServerSequence ?? LedgerInstance.ServerSequence,
            RunAggregate = CreateStatusOnlyAggregate(source?.RunAggregate),
            Combatants = new List<CombatantDamageStat>(),
            History = new List<DamageFightRecord>()
        };
    }

    private static DamageRunAggregateSnapshot CreateStatusOnlyAggregate(DamageRunAggregateSnapshot? source)
    {
        return new DamageRunAggregateSnapshot
        {
            AdventureId = source?.AdventureId ?? RunAggregateInstance.AdventureId,
            StartedUtc = source?.StartedUtc ?? RunAggregateInstance.StartedUtc,
            UpdatedUtc = source?.UpdatedUtc ?? RunAggregateInstance.UpdatedUtc,
            EncounterCount = source?.EncounterCount ?? RunAggregateInstance.EncounterCount,
            TotalRounds = source?.TotalRounds ?? RunAggregateInstance.TotalRounds,
            ConfirmedEventCount = source?.ConfirmedEventCount ?? RunAggregateInstance.ConfirmedEventCount,
            LastSessionId = source?.LastSessionId ?? RunAggregateInstance.LastSessionId,
            LastServerSequence = source?.LastServerSequence ?? RunAggregateInstance.LastServerSequence,
            BestHit = source?.BestHit?.Copy(),
            Combatants = new List<CombatantDamageStat>()
        };
    }

    private static bool SnapshotFits(DamageMeterSnapshot snapshot)
    {
        return !AuraToolsRpcPayloadGuard.TryMeasureUtf8Json(snapshot, out var bytes, out _)
               || bytes <= NetworkSnapshotSoftLimitBytes;
    }

    private static int EstimateSnapshotBytes(DamageMeterSnapshot snapshot)
    {
        return AuraToolsRpcPayloadGuard.TryMeasureUtf8Json(snapshot, out var bytes, out _)
            ? bytes
            : 0;
    }

    private static void TrimCombatants(DamageMeterSnapshot snapshot, int maximum)
    {
        snapshot.Combatants = (snapshot.Combatants ?? new List<CombatantDamageStat>())
            .Where(stat => stat != null)
            .OrderByDescending(stat => stat.DisplayTotal(true))
            .ThenBy(stat => stat.InstanceId, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, maximum))
            .ToList();
    }

    private static void TrimDetailsAndRounds(DamageMeterSnapshot snapshot, int maxDetails, int maxRounds)
    {
        foreach (var stat in snapshot.Combatants ?? new List<CombatantDamageStat>())
        {
            if (stat == null)
            {
                continue;
            }

            stat.Rounds = maxRounds <= 0
                ? new List<DamageRoundStat>()
                : (stat.Rounds ?? new List<DamageRoundStat>())
                    .Skip(Math.Max(0, (stat.Rounds?.Count ?? 0) - maxRounds))
                    .ToList();

            stat.Details = maxDetails <= 0
                ? new Dictionary<string, DamageDetailStat>(StringComparer.OrdinalIgnoreCase)
                : (stat.Details ?? new Dictionary<string, DamageDetailStat>(StringComparer.OrdinalIgnoreCase))
                    .OrderByDescending(pair => (pair.Value?.HpDamage ?? 0) + (pair.Value?.ShieldDamage ?? 0))
                    .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(maxDetails)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        TrimAggregateDetails(snapshot.RunAggregate, maxDetails);
    }

    private static void TrimAggregateDetails(DamageRunAggregateSnapshot? aggregate, int maxDetails)
    {
        if (aggregate == null)
        {
            return;
        }

        foreach (var stat in aggregate.Combatants ?? new List<CombatantDamageStat>())
        {
            if (stat == null)
            {
                continue;
            }

            stat.Rounds = new List<DamageRoundStat>();
            stat.CurrentRoundHpDamage = 0;
            stat.CurrentRoundShieldDamage = 0;
            stat.Details = maxDetails <= 0
                ? new Dictionary<string, DamageDetailStat>(StringComparer.OrdinalIgnoreCase)
                : (stat.Details ?? new Dictionary<string, DamageDetailStat>(StringComparer.OrdinalIgnoreCase))
                    .OrderByDescending(pair => (pair.Value?.HpDamage ?? 0) + (pair.Value?.ShieldDamage ?? 0))
                    .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(maxDetails)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void LogSnapshotCompacted(string source, int beforeBytes, int afterBytes)
    {
        AuraToolsLog.Warn("[DamageMeter] compacted network snapshot. source="
                          + source
                          + ", bytes="
                          + beforeBytes
                          + "->"
                          + afterBytes
                          + ", softLimit="
                          + NetworkSnapshotSoftLimitBytes);
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
                    MinimizeNetworkSnapshot(command.Snapshot);
                }
            },
            () =>
            {
                command.Snapshot = CreateStatusOnlySnapshot(command.Snapshot);
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
                    MinimizeNetworkSnapshot(command.Snapshot);
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
        if (!IsMultiplayer || snapshotRequestPending)
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
        if (value == null || value.ProtocolVersion != DamageMeterProtocol.Version)
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

        if (value.ReporterSequence <= 0
            || LastReporterSequence.TryGetValue(value.ReporterPlayerId, out var previous)
            && value.ReporterSequence <= previous)
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
        if (string.IsNullOrWhiteSpace(currentAdventureId))
        {
            currentAdventureId = Guid.NewGuid().ToString("N");
        }

        return currentAdventureId;
    }

    private static void EnsureRunAggregateStarted()
    {
        var adventureId = EnsureAdventureId();
        if (!string.IsNullOrWhiteSpace(RunAggregateInstance.AdventureId))
        {
            return;
        }

        RunAggregateInstance.BeginAdventure(adventureId, DateTime.UtcNow.ToString("O"));
    }

    private static bool ArchiveSnapshot(DamageMeterSnapshot snapshot, string result)
    {
        if (IsHost)
        {
            RunAggregateInstance.RecordEncounter(snapshot);
        }

        if (!HistoryInstance.Archive(
                snapshot,
                result,
                DateTime.UtcNow.ToString("O")))
        {
            return false;
        }

        if (IsHost)
        {
            DamageMeterPersistence.Save(HistoryInstance);
        }

        return true;
    }

    private static bool ValidDamage(int value)
    {
        return value >= 0 && value <= DamageMeterProtocol.MaxDamagePerEvent;
    }

    private static bool ValidText(string value)
    {
        return value == null || value.Length <= DamageMeterProtocol.MaxStringLength;
    }

    private static void Send(RpcCommandBase command, bool deferSubmit = true)
    {
        var source = "DamageMeter." + command.GetType().Name;
        var shouldDefer = deferSubmit
                          && (command is DamageMeterSubmitCommand
                              || command is DamageMeterSubmitBatchCommand);
        var sent = shouldDefer
            ? AuraToolsRpcTransport.SendDeferred(PlayerManager.Instance, command, source)
            : AuraToolsRpcTransport.Send(PlayerManager.Instance, command, source);
        if (!sent)
        {
            snapshotRequestPending = false;
        }
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
