using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Resolution;
using AuraToolsExp.Dll.Infrastructure;
using Network.Command;

namespace AuraToolsExp.Dll.Features.DamageMeter.Network;

internal static class DamageMeterNetworkRuntime
{
    private static readonly DamageLedger LedgerInstance = new();
    private static readonly DamageHistoryStore HistoryInstance = new();
    private static readonly Dictionary<string, long> LastReporterSequence =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Queue<long>> ReporterRateWindows =
        new(StringComparer.OrdinalIgnoreCase);
    private static long localReporterSequence;
    private static int hostRoundSignalCount;
    private static bool snapshotRequestPending;

    public static DamageLedger Ledger => LedgerInstance;

    public static DamageHistoryStore History => HistoryInstance;

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
    }

    public static void StartFight(bool sharedEnabled)
    {
        ResetTransient();
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
        HistoryInstance.Clear();
        LedgerInstance.ApplySnapshot(new DamageMeterSnapshot());
        if (IsHost)
        {
            DamageMeterPersistence.Clear();
        }

        NotifyChanged();
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

        if (!IsMultiplayer)
        {
            LedgerInstance.EndFight();
            ArchiveFight(result);
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
                NotifyChanged();
            }

            return;
        }

        Send(new DamageMeterSubmitCommand { Candidate = damage });
    }

    public static bool AcceptOnServer(
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

        LastReporterSequence[confirmed.ReporterPlayerId] = confirmed.ReporterSequence;
        NotifyChanged();
        return true;
    }

    public static void ApplyConfirmed(DamageEvent confirmed)
    {
        snapshotRequestPending = false;
        if (confirmed == null)
        {
            return;
        }

        if (confirmed.ServerSequence > LedgerInstance.ServerSequence + 1)
        {
            RequestSnapshot();
            return;
        }

        if (LedgerInstance.Apply(confirmed))
        {
            NotifyChanged();
        }
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
                ArchiveFight(command.Result);
                break;
            default:
                rejection = "unsupported control";
                return false;
        }

        command.Snapshot = CreateServerSnapshot();
        NotifyChanged();
        return true;
    }

    public static void ApplySnapshot(DamageMeterSnapshot snapshot)
    {
        snapshotRequestPending = false;
        var ledgerChanged = LedgerInstance.ApplySnapshot(snapshot);
        if (snapshot != null && snapshot.ProtocolVersion == DamageMeterProtocol.Version)
        {
            HistoryInstance.ApplySnapshot(snapshot.History);
        }

        if (ledgerChanged)
        {
            NotifyChanged();
        }
    }

    public static DamageMeterSnapshot CreateServerSnapshot()
    {
        var snapshot = LedgerInstance.CreateSnapshot();
        snapshot.History = HistoryInstance.CreateSnapshot();
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

        snapshot = CreateServerSnapshot();
        return true;
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

    private static void ArchiveFight(string result)
    {
        if (!HistoryInstance.Archive(
                LedgerInstance.CreateSnapshot(),
                result,
                DateTime.UtcNow.ToString("O")))
        {
            return;
        }

        if (IsHost)
        {
            DamageMeterPersistence.Save(HistoryInstance);
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

    private static void Send(RpcCommandBase command)
    {
        try
        {
            PlayerManager.Instance?.SendRpcCommand(command);
        }
        catch (Exception ex)
        {
            snapshotRequestPending = false;
            AuraToolsLog.Warn("[DamageMeter] network send failed: " + ex.Message);
        }
    }

    private static void NotifyChanged()
    {
        AuraToolsDamageMeterRuntime.NotifyLedgerChanged();
    }
}
