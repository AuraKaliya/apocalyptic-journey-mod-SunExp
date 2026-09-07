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

    public static string CurrentAdventureId => EnsureAdventureId();

    public static bool NetworkActive => GameApi.AuraToolsNetworkSession.NetworkActive;
    public static bool IsHost => GameApi.AuraToolsNetworkSession.IsAuthority;

    public static string LocalPlayerId => GameApi.AuraToolsNetworkSession.LocalPlayerId;

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
        try
        {
            DamageMeterPersistence.SaveAdventureId(currentAdventureId);
            DamageHistoryStorage.Database.SaveRunState(currentAdventureId, RunAggregateInstance.CreateSnapshot());
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
        if (!NetworkActive || PendingSubmitBatch.Count == 0)
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
        if (!IsHost || HistoryInstance.TotalCount > 0)
        {
            return;
        }

        try
        {
            var savedAdventureId = DamageMeterPersistence.LoadAdventureId();
            if (!string.IsNullOrWhiteSpace(savedAdventureId))
            {
                currentAdventureId = savedAdventureId.Trim();
            }

            var adventureId = EnsureAdventureId();
            DamageHistoryStorage.EnsureLegacyMigrations();
            if (DamageHistoryStorage.Database.CountFights(adventureId) == 0)
            {
                var legacy = DamageMeterPersistence.LoadLegacyHistory();
                if (legacy.Count > 0)
                {
                    DamageHistoryStorage.Database.ImportFights(adventureId, legacy);
                    DamageMeterPersistence.ClearLegacyHistory();
                }
            }

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

        FlushPendingSubmissions(immediate: true);

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
        if (!NetworkActive || PendingSubmitBatch.Count == 0)
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
            if (AcceptOnServer(candidate, sender, out var accepted, out var rejection))
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
        confirmed.RoundIndex = Math.Max(1, LedgerInstance.CurrentRoundIndex);
        if (!LedgerInstance.Apply(confirmed))
        {
            rejection = "ledger rejected event";
            return false;
        }

        RunAggregateInstance.Apply(confirmed);
        LastReporterSequence[confirmed.ReporterPlayerId] = confirmed.ReporterSequence;
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
        if (ledgerChanged || aggregateChanged)
        {
            NotifyChanged();
        }
    }

    private static DamageMeterSnapshot CreateNetworkSnapshot(string source)
    {
        var startedAt = DamageMeterPerformanceCounters.StartSample();
        var snapshot = LedgerInstance.CreateSnapshot();
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
            var adventureId = string.IsNullOrWhiteSpace(RunAggregateInstance.AdventureId)
                ? EnsureAdventureId()
                : RunAggregateInstance.AdventureId;
            currentAdventureId = adventureId;
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

    private static void Send(RpcCommandBase command, bool deferSubmit = true)
    {
        var source = "DamageMeter." + command.GetType().Name;
        var shouldDefer = deferSubmit
                          && command is DamageMeterSubmitBatchCommand;
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
