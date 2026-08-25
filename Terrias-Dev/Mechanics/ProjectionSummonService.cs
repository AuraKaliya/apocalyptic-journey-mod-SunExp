using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatAi.Shared.GameApi;
using AuraGameData.Shared.GameApi;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Network;
using UnityEngine;
using Witch.UI;

namespace Terrias.Dll.Mechanics;

public static class ProjectionSummonService
{
    private const double RetryNoticeSeconds = 8d;
    private const double TransactionLifetimeSeconds = 30d;
    private const int MaximumAttempts = 12;
    private const int RetryWakeFrames = 30;
    private const int HostReservationPollFrames = 30;
    private static readonly object NetworkSync = new();
    private static readonly Dictionary<string, ProjectionSummonResultSnapshot> TerminalResults =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ProjectionSummonTransaction> PendingTransactions =
        new(StringComparer.Ordinal);
    private static readonly HashSet<string> AppliedResultTokens = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ProjectionTombstone> Tombstones =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, RoleDeckWaitState> RoleDeckWaitAttempts =
        new(StringComparer.Ordinal);

    public static void ResetBattleSynchronization()
    {
        lock (NetworkSync)
        {
            TerminalResults.Clear();
            PendingTransactions.Clear();
            AppliedResultTokens.Clear();
            Tombstones.Clear();
            RoleDeckWaitAttempts.Clear();
        }
        FriendlyRoleSeatLedger.BeginBattle();
    }

    public static bool TrySummon(ScriptExecutor self, PolymorphRoleSpec role)
    {
        if (self?.Self == null || role == null)
        {
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.projection.summon_failed"));
            return false;
        }

        if (FightManager.Instance == null || FightManager.Instance.fightType == FightType.None)
        {
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.projection.battle_only"));
            return false;
        }

        var token = Guid.NewGuid().ToString("N");
        if (!ProjectionRoleDeckService.TryCaptureLocal(out var localRecipe, out var deckReason)
            || localRecipe == null)
        {
            TerriasLog.Warn("[ProjectionDeck] local role deck unavailable: " + deckReason);
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.projection.deck_not_ready"));
            return false;
        }
        var transaction = new ProjectionSummonTransaction(
            token,
            role.Id,
            self.Self.InstanceId,
            localRecipe.BaseHash,
            Time.unscaledTimeAsDouble);
        lock (NetworkSync)
        {
            PendingTransactions[token] = transaction;
        }
        if (TerriasNetworkRuntime.IsMultiplayerSession() && !TerriasNetworkRuntime.IsServer())
        {
            SendPendingTransaction(transaction, Time.unscaledTimeAsDouble, "Initial");
            SchedulePendingTransaction(transaction.Token);
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.projection.synchronizing"));
            return true;
        }

        var sender = TerriasRpcAuthorityRuntime.CreateLocalServerSender(
            "ProjectionSummonService.TrySummon");
        ResolveNetworkSummon(
            role.Id,
            self.Self.InstanceId,
            token,
            sender,
            CompanionAuthorityService.ProjectionProtocolVersion,
            CompanionAuthorityService.BattleEpoch,
            ProjectionRoleDeckService.CardModelVersion,
            localRecipe.BaseHash);
        return true;
    }

    private static void SchedulePendingTransaction(string token)
    {
        TerriasFrameDispatcher.RunOnceAfterFrames(
            "Projection.SummonRetry." + token,
            RetryWakeFrames,
            () => AdvancePendingTransaction(token));
    }

    private static void AdvancePendingTransaction(string token)
    {
        ProjectionSummonTransaction? transaction;
        lock (NetworkSync)
        {
            PendingTransactions.TryGetValue(token ?? "", out transaction);
        }

        if (transaction == null || transaction.Terminal)
        {
            return;
        }

        var now = Time.unscaledTimeAsDouble;
        var age = now - transaction.CreatedAt;
        if (transaction.ShouldExpire(now, MaximumAttempts, TransactionLifetimeSeconds))
        {
            transaction.SetTerminal();
            var ownerPlayerId = CompanionOwnershipService.ResolveOwnerPlayerId(transaction.OwnerStatusId);
            ApplySummonResult(
                CreateResult(
                    transaction.Token,
                    transaction.RoleId,
                    transaction.OwnerStatusId,
                    ownerPlayerId,
                    ProjectionSummonFailureCode.RoleDeckTimedOut,
                    "projection summon response timed out"),
                "ProjectionSummonService.Timeout");
            return;
        }

        var retryInterval = transaction.Attempts < 3 ? 1.25d : 2.5d;
        if (transaction.IsDue(now, retryInterval))
        {
            SendPendingTransaction(transaction, now, "Retry" + transaction.Attempts);
        }

        if (!transaction.TimeoutReported && age >= RetryNoticeSeconds)
        {
            transaction.TimeoutReported = true;
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.projection.retrying_host"));
        }

        SchedulePendingTransaction(transaction.Token);
    }

    private static void SendPendingTransaction(
        ProjectionSummonTransaction transaction,
        double now,
        string source)
    {
        if (transaction.Terminal)
        {
            return;
        }
        transaction.MarkAttempt(now);
        var status = TerriasNetworkRuntime.TrySend(
            new RpcProjectionSummonRequest(
                transaction.RoleId,
                transaction.OwnerStatusId,
                transaction.Token,
                transaction.DeckRecipeHash),
            "ProjectionSummonService." + source);
        if (status == TerriasNetworkSendStatus.NotAttempted && transaction.Attempts == 1)
        {
            ApplySummonResult(CreateResult(
                transaction.Token,
                transaction.RoleId,
                transaction.OwnerStatusId,
                "",
                ProjectionSummonFailureCode.TransportNotSent,
                "transport did not accept the summon request"),
                "ProjectionSummonService.TransportNotSent");
        }
    }

    public static void ResolveNetworkSummon(
        string roleId,
        string ownerStatusId,
        string token,
        TerriasRpcSender sender,
        int protocolVersion,
        int battleEpoch,
        string cardModelVersion,
        string deckRecipeHash)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }
        ProjectionSummonResultSnapshot? previous;
        lock (NetworkSync)
        {
            TerminalResults.TryGetValue(token, out previous);
        }
        if (previous != null)
        {
            if (ValidateNetworkSender(sender, previous.OwnerStatusId)
                    != ProjectionSummonFailureCode.None
                || !string.Equals(previous.RoleId, roleId ?? "", StringComparison.Ordinal)
                || !string.Equals(previous.OwnerStatusId, ownerStatusId ?? "", StringComparison.Ordinal))
            {
                TerriasLog.Warn("[Projection] ignored terminal-result replay with mismatched sender or request identity.");
                return;
            }
            BroadcastSummonResult(previous, "ProjectionSummonService.ReplayResult");
            if (previous.Accepted)
            {
                var active = ProjectionStateStore.Find(previous.StatusId)?.Projection;
                if (active != null) BroadcastRuntimeState(active, "ReplayAccepted");
            }
            return;
        }

        var role = PolymorphRoleRegistry.Find(roleId);
        var rejection = ValidateNetworkSender(sender, ownerStatusId);
        if (rejection == ProjectionSummonFailureCode.None
            && protocolVersion != CompanionAuthorityService.ProjectionProtocolVersion)
        {
            rejection = ProjectionSummonFailureCode.ProtocolMismatch;
        }
        else if (rejection == ProjectionSummonFailureCode.None
                 && battleEpoch != CompanionAuthorityService.BattleEpoch)
        {
            rejection = ProjectionSummonFailureCode.BattleEpochMismatch;
        }
        else if (rejection == ProjectionSummonFailureCode.None
                 && !string.Equals(
                     cardModelVersion,
                     ProjectionRoleDeckService.CardModelVersion,
                     StringComparison.Ordinal))
        {
            rejection = ProjectionSummonFailureCode.CardModelMismatch;
        }
        if (rejection == ProjectionSummonFailureCode.None && role == null)
        {
            rejection = ProjectionSummonFailureCode.UnknownRole;
        }

        if (rejection != ProjectionSummonFailureCode.None)
        {
            RejectSummon(
                roleId,
                ownerStatusId,
                sender.IsAvailable ? sender.PlayerId : "",
                token,
                rejection,
                "request validation failed",
                "ProjectionSummonService.ResolveNetworkSummon.Reject");
            return;
        }

        var ownerPlayerId = CompanionOwnershipService.ResolveOwnerPlayerId(ownerStatusId, sender.PlayerId);
        if (!RoleDeckWaitRequestMatches(token, roleId, ownerPlayerId, ownerStatusId, deckRecipeHash))
        {
            RejectSummon(
                roleId,
                ownerStatusId,
                ownerPlayerId,
                token,
                ProjectionSummonFailureCode.TokenConflict,
                "same token was reused with a different summon request",
                "ProjectionSummonService.ResolveNetworkSummon.TokenConflict");
            return;
        }
        if (!ProjectionTurnCoordinator.TryReserveAuthoritative(
                token,
                "ProjectionSummonService.ResolveNetworkSummon",
                out var turnTransaction,
                out var turnReason))
        {
            RejectSummon(
                roleId,
                ownerStatusId,
                ownerPlayerId,
                token,
                ProjectionSummonFailureCode.TurnTransactionUnavailable,
                turnReason,
                "ProjectionSummonService.ResolveNetworkSummon.TurnTransactionRejected");
            return;
        }
        ScheduleTurnReservationExpiry(
            token,
            roleId,
            ownerStatusId,
            ownerPlayerId,
            battleEpoch);
        if (!ProjectionRoleDeckService.TryCaptureAuthoritative(
                ownerPlayerId,
                ownerStatusId,
                out var recipe,
                out var deckReason)
            || recipe == null)
        {
            TerriasLog.Warn("[ProjectionDeck] authoritative role deck unavailable: owner="
                + ownerPlayerId
                + "; "
                + deckReason);
            var waitAttempt = RecordRoleDeckWaitAttempt(
                token,
                roleId,
                ownerPlayerId,
                ownerStatusId,
                deckRecipeHash);
            if (waitAttempt >= 6)
            {
                ProjectionTurnCoordinator.TryMarkFailedAuthoritative(
                    token,
                    deckReason,
                    "ProjectionSummonService.ResolveNetworkSummon.RoleDeckTimeout");
                RejectSummon(
                    roleId,
                    ownerStatusId,
                    ownerPlayerId,
                    token,
                    ProjectionSummonFailureCode.RoleDeckTimedOut,
                    deckReason,
                    "ProjectionSummonService.ResolveNetworkSummon.RoleDeckTimeout");
            }
            else
            {
                BroadcastSummonResult(CreateResult(
                    token,
                    roleId,
                    ownerStatusId,
                    ownerPlayerId,
                    ProjectionSummonFailureCode.RoleDeckUnavailable,
                    deckReason),
                    "ProjectionSummonService.ResolveNetworkSummon.MissingRoleDeck");
            }
            return;
        }
        if (string.IsNullOrWhiteSpace(deckRecipeHash)
            || !string.Equals(recipe.BaseHash, deckRecipeHash, StringComparison.OrdinalIgnoreCase))
        {
            TerriasLog.Warn("[ProjectionDeck] role deck hash mismatch: owner="
                + ownerPlayerId
                + ", client="
                + (deckRecipeHash ?? "")
                + ", host="
                + recipe.BaseHash
                + "; authoritative RoleTable recipe accepted.");
            TerriasPerformanceCounters.Record("Projection.DeckHashDiagnosticMismatch");
        }
        if (!FriendlyRoleSeatLedger.TryReserve(
                token,
                ownerPlayerId,
                ownerStatusId,
                battleEpoch,
                out var slotIndex,
                out var seatReason))
        {
            var seatFailure = string.Equals(
                    seatReason,
                    "friendly role seats are full",
                    StringComparison.Ordinal)
                ? ProjectionSummonFailureCode.FriendlySeatsFull
                : ProjectionSummonFailureCode.OwnerAlreadyHasProjection;
            ProjectionTurnCoordinator.TryMarkFailedAuthoritative(
                token,
                seatReason,
                "ProjectionSummonService.ResolveNetworkSummon.SeatReject");
            RejectSummon(
                roleId,
                ownerStatusId,
                ownerPlayerId,
                token,
                seatFailure,
                seatReason,
                "ProjectionSummonService.ResolveNetworkSummon.SeatReject");
            return;
        }

        if (!FriendlyRoleSeatLedger.TryClaim(
                token,
                ownerPlayerId,
                ownerStatusId,
                battleEpoch,
                out slotIndex))
        {
            FriendlyRoleSeatLedger.Release(token);
            ProjectionTurnCoordinator.TryMarkFailedAuthoritative(
                token,
                "projection seat reservation expired",
                "ProjectionSummonService.ResolveNetworkSummon.SeatClaimRejected");
            RejectSummon(
                roleId,
                ownerStatusId,
                ownerPlayerId,
                token,
                ProjectionSummonFailureCode.SeatReservationExpired,
                "projection seat reservation expired",
                "ProjectionSummonService.ResolveNetworkSummon.SeatClaimRejected");
            return;
        }

        if (!TrySummonLocal(
                ownerStatusId,
                role!,
                "ProjectionSummonService.ResolveNetworkSummon.AuthoritativeRoleDeck",
                token: token,
                preferredOwnerPlayerId: ownerPlayerId,
                slotIndex: slotIndex,
                recipe: recipe,
                summonRoundSequence: turnTransaction.RoundSequence,
                summonTurnOrder: turnTransaction.Order))
        {
            ProjectionTurnCoordinator.TryMarkFailedAuthoritative(
                token,
                "projection spawn failed",
                "ProjectionSummonService.ResolveNetworkSummon.SpawnRejected");
            RejectSummon(
                roleId,
                ownerStatusId,
                ownerPlayerId,
                token,
                ProjectionSummonFailureCode.SpawnFailed,
                "projection spawn failed",
                "ProjectionSummonService.ResolveNetworkSummon.SpawnRejected");
            return;
        }

        var state = ProjectionStateStore.FindByOwner(ownerPlayerId, ownerStatusId);
        if (state?.Projection == null
            || !ProjectionTurnCoordinator.TryMarkReadyAuthoritative(
                token,
                state.StatusId,
                state.Replication.Generation,
                "ProjectionSummonService.ResolveNetworkSummon"))
        {
            ProjectionTurnCoordinator.TryMarkFailedAuthoritative(
                token,
                "projection turn transaction could not bind the spawned actor",
                "ProjectionSummonService.ResolveNetworkSummon.ReadyRejected");
            if (state?.Projection?.Status != null)
            {
                ProjectionStateStore.Retire(
                    state.Projection.Status,
                    "ProjectionSummonService.ResolveNetworkSummon.ReadyRejected");
            }
            RejectSummon(
                roleId,
                ownerStatusId,
                ownerPlayerId,
                token,
                ProjectionSummonFailureCode.SpawnFailed,
                "projection turn transaction could not bind the spawned actor",
                "ProjectionSummonService.ResolveNetworkSummon.ReadyRejected");
            return;
        }
        var accepted = new ProjectionSummonResultSnapshot
        {
            ServerProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
            ServerBattleEpoch = CompanionAuthorityService.BattleEpoch,
            ServerCardModelVersion = ProjectionRoleDeckService.CardModelVersion,
            Token = token,
            RoleId = roleId,
            OwnerStatusId = ownerStatusId,
            OwnerPlayerId = ownerPlayerId,
            StatusId = state?.StatusId ?? "",
            Generation = state?.Replication.Generation ?? token,
            Accepted = true,
            Terminal = true
        };
        CacheTerminalResult(accepted);
        BroadcastSummonResult(accepted, "ProjectionSummonService.Accepted");
        if (state?.Projection != null)
        {
            BroadcastRuntimeState(state.Projection, "SpawnAccepted");
        }
    }

    public static void ApplyNetworkState(ProjectionCompanionSnapshot? snapshot, string source)
    {
        if (snapshot == null)
        {
            return;
        }

        if (snapshot.ProtocolVersion != CompanionAuthorityService.ProjectionProtocolVersion
            || snapshot.BattleEpoch != CompanionAuthorityService.BattleEpoch
            || !string.Equals(
                snapshot.CardModelVersion,
                ProjectionRoleDeckService.CardModelVersion,
                StringComparison.Ordinal))
        {
            TerriasLog.Warn("[Projection] ignored incompatible snapshot: protocol=" + snapshot.ProtocolVersion
                + ", epoch=" + snapshot.BattleEpoch + ", localEpoch=" + CompanionAuthorityService.BattleEpoch);
            return;
        }

        ApplyTurnTransaction(snapshot, source);

        var role = PolymorphRoleRegistry.Find(snapshot.RoleId);
        if (role == null || string.IsNullOrWhiteSpace(snapshot.StatusId))
        {
            return;
        }

        var existing = ProjectionStateStore.Find(snapshot.StatusId);
        if (existing != null)
        {
            if (!snapshot.Active
                && string.Equals(existing.Replication.Generation, snapshot.Generation, StringComparison.Ordinal))
            {
                if (!existing.Replication.TryApplyRemote(
                        snapshot.Generation,
                        snapshot.StateRevision,
                        snapshot.ActionSequence,
                        snapshot.CompletedTurnSequence,
                        false))
                {
                    return;
                }
                RememberTombstone(
                    snapshot.StatusId,
                    snapshot.Generation,
                    snapshot.StateRevision);
                ProjectionStateStore.Retire(existing.Projection.Status, source + ".RemoteTombstone");
                return;
            }
            ApplySnapshot(existing.Projection, snapshot, source);
            return;
        }

        if (!snapshot.Active)
        {
            RememberTombstone(
                snapshot.StatusId,
                snapshot.Generation,
                snapshot.StateRevision);
            return;
        }

        lock (NetworkSync)
        {
            if (Tombstones.TryGetValue(snapshot.StatusId, out var tombstone)
                && string.Equals(tombstone.Generation, snapshot.Generation, StringComparison.Ordinal))
            {
                return;
            }
        }

        var ownerExisting = ProjectionStateStore.FindByOwner(snapshot.OwnerPlayerId, snapshot.OwnerStatusId);
        if (ownerExisting != null)
        {
            if (string.Equals(
                    ownerExisting.Replication.Generation,
                    snapshot.Generation,
                    StringComparison.Ordinal))
            {
                ApplySnapshot(ownerExisting.Projection, snapshot, source + ".OwnerAlreadyBound");
                return;
            }
            RememberTombstone(
                ownerExisting.StatusId,
                ownerExisting.Replication.Generation,
                ownerExisting.Replication.StateRevision);
            ProjectionStateStore.Retire(
                ownerExisting.Projection.Status,
                source + ".SupersededGeneration");
        }

        SpawnProjection(role, snapshot.OwnerStatusId, snapshot.SlotIndex, snapshot.StatusId, source, snapshot);
    }

    public static DataConfig CreateProjectionDataConfig(PolymorphRoleSpec role, CompanionStats? stats = null)
    {
        var activeStats = stats ?? CompanionStatsService.ProjectionStats(role);
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Attack"] = activeStats.Attack.ToString(),
            ["Defend"] = activeStats.Armor.ToString(),
            ["Hp"] = activeStats.MaxHp.ToString(),
            ["ActionCount"] = "0",
            ["CardList"] = ""
        };
        var arguments = new Dictionary<string, string>();
        foreach (var locale in TerriasLocale.Supported)
        {
            arguments["name"] = role.DisplayNameFor(locale);
            overrides[TerriasLocale.FieldName("Name", locale)] =
                TerriasTextCatalog.GetForLocale("card.projection.name", locale, arguments);
        }
        var handle = AuraGameDataHostApi.ResolveHandle(DataType.Career, role.Id)
            ?? throw new InvalidOperationException("Projection career definition is not registered: " + role.Id);
        var result = AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest
        {
            Definition = handle,
            DataOverrides = overrides
        });
        return result.Instance as DataConfig
            ?? throw new InvalidOperationException("Projection career materialization failed: " + result.Message);
    }

    public static void RegisterFightState(ProjectionOtherObj projection, string source)
    {
        var status = projection.Status as StatusManager;
        var manager = FightManager.Instance;
        if (status == null || manager == null)
        {
            return;
        }

        manager.statuses[projection.InstanceId] = status;
        if (manager.netIdentity != null && manager.isServer)
        {
            manager.statusData[projection.InstanceId] = new StatusDataTransfer(status);
        }

        CompanionOwnershipService.EnsureNativeStatusRoute(
            projection.InstanceId,
            source + ".RegisterFightState");
        ProjectionTurnCoordinator.RegisterCompanion(projection, source);

        // The internal Status remains available to ScriptExecutor through
        // FightManager.statuses, but is not a formal friendly target or HUD row.
    }

    private static bool TrySummonLocal(
        string ownerStatusId,
        PolymorphRoleSpec role,
        string source,
        string token = "",
        string preferredOwnerPlayerId = "",
        int slotIndex = -1,
        ProjectionDeckRecipe? recipe = null,
        int summonRoundSequence = 0,
        long summonTurnOrder = 0)
    {
        var ownerPlayerId = CompanionOwnershipService.ResolveOwnerPlayerId(ownerStatusId, preferredOwnerPlayerId);
        if (ProjectionStateStore.HasForOwner(ownerPlayerId, ownerStatusId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(ownerStatusId))
        {
            return false;
        }

        if (slotIndex < 0)
        {
            slotIndex = FriendlyRoleSeatLedger.FindOpenSeat() ?? -1;
        }
        if (slotIndex < 0)
        {
            return false;
        }

        var statusId = ProjectionStateStore.NextStatusId();
        var spawned = SpawnProjection(
            role,
            ownerStatusId,
            slotIndex,
            statusId,
            source,
            null,
            ownerPlayerId,
            recipe,
            string.IsNullOrWhiteSpace(token) ? Guid.NewGuid().ToString("N") : token,
            summonRoundSequence,
            summonTurnOrder);
        return spawned;
    }

    private static bool SpawnProjection(
        PolymorphRoleSpec role,
        string ownerStatusId,
        int slotIndex,
        string statusId,
        string source,
        ProjectionCompanionSnapshot? snapshot = null,
        string ownerPlayerId = "",
        ProjectionDeckRecipe? recipe = null,
        string generation = "",
        int authoritativeSummonRoundSequence = 0,
        long authoritativeSummonTurnOrder = 0)
    {
        try
        {
            var prefab = TerriasResourceCache.Load<GameObject>("Model/player", true, "projection");
            if (prefab == null)
            {
                PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.projection.model_failed"));
                return false;
            }

            var gameObject = UnityEngine.Object.Instantiate(prefab);
            if (gameObject == null)
            {
                PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.projection.model_failed"));
                return false;
            }

            var owner = FightManager.Instance?.statuses?.TryGetValue(ownerStatusId, out var ownerStatus) == true
                ? ownerStatus
                : null;
            CompanionSceneApi.MoveToOwnerScene(
                gameObject,
                owner?.transform?.gameObject,
                source + ".ProjectionSpawn");

            var stats = snapshot != null && snapshot.MaxHp > 0
                ? new CompanionStats(snapshot.MaxHp, snapshot.MaxMagic, snapshot.Attack, snapshot.Armor)
                : CompanionStatsService.ProjectionStats(role);
            if (snapshot != null)
            {
                stats.SetCurrentMagic(snapshot.CurrentMagic);
                ownerPlayerId = snapshot.OwnerPlayerId;
            }
            var projection = gameObject.AddComponent<ProjectionOtherObj>();
            if (!projection.InitProjection(role, ownerStatusId, slotIndex, stats, statusId, ownerPlayerId))
            {
                UnityEngine.Object.Destroy(gameObject);
                PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.projection.initialize_failed"));
                return false;
            }

            var summonRoundSequence = snapshot?.SummonRoundSequence
                                      ?? Math.Max(0, authoritativeSummonRoundSequence);
            var summonTurnToken = snapshot != null
                                  && !string.IsNullOrWhiteSpace(snapshot.SummonTurnToken)
                ? snapshot.SummonTurnToken
                : generation;
            var summonTurnOrder = snapshot?.SummonTurnOrder
                                  ?? Math.Max(0L, authoritativeSummonTurnOrder);
            ProjectionStateStore.Register(new ProjectionState(
                projection.InstanceId,
                ownerStatusId,
                role.Id,
                projection,
                slotIndex,
                projection.OwnerPlayerId,
                snapshot?.Generation ?? generation,
                snapshot == null ? 1L : 0L,
                summonRoundSequence,
                summonTurnToken,
                summonTurnOrder));
            if (snapshot == null)
            {
                projection.Status.UpdateStatus(true);
                projection.HydrateOwnerCoreStats(owner);
                if (!projection.InitializeRoleDeck(recipe, source + ".RoleDeck"))
                {
                    ProjectionStateStore.Retire(projection.Status, source + ".RoleDeckRejected");
                    return false;
                }
                projection.ActivateAfterHydration(null, source + ".AuthoritativeInit");
            }
            else
            {
                ApplySnapshot(projection, snapshot, source + ".Hydrate");
                ProjectionCardPresentationService.FlushPending(
                    projection.InstanceId,
                    source + ".Hydrate");
            }
            PlayerApi.ShowCaption(TerriasTextCatalog.Format("caption.projection.joined", "name", role.DisplayName));
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[Projection] summon failed from " + source, ex);
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.projection.summon_failed"));
            return false;
        }
    }

    private static void RejectSummon(
        string roleId,
        string ownerStatusId,
        string ownerPlayerId,
        string token,
        ProjectionSummonFailureCode failureCode,
        string detail,
        string source)
    {
        var result = CreateResult(
            token,
            roleId,
            ownerStatusId,
            ownerPlayerId,
            failureCode,
            detail);
        CacheTerminalResult(result);
        BroadcastSummonResult(result, source);
    }

    private static ProjectionSummonResultSnapshot CreateResult(
        string token,
        string roleId,
        string ownerStatusId,
        string ownerPlayerId,
        ProjectionSummonFailureCode failureCode,
        string detail)
    {
        var failure = ProjectionSummonFailureCatalog.Describe(failureCode);
        return new ProjectionSummonResultSnapshot
        {
            ServerProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
            ServerBattleEpoch = CompanionAuthorityService.BattleEpoch,
            ServerCardModelVersion = ProjectionRoleDeckService.CardModelVersion,
            Token = token ?? "",
            RoleId = roleId ?? "",
            OwnerStatusId = ownerStatusId ?? "",
            OwnerPlayerId = ownerPlayerId ?? "",
            Accepted = failureCode == ProjectionSummonFailureCode.None,
            Terminal = failure.Terminal,
            FailureCode = failure.Code,
            FailureCategory = failure.Category,
            Retryable = failure.Retryable,
            RefundCard = failure.RefundCard,
            Detail = detail ?? ""
        };
    }

    private static void CacheTerminalResult(ProjectionSummonResultSnapshot result)
    {
        if (result == null || !result.Terminal || string.IsNullOrWhiteSpace(result.Token))
        {
            return;
        }
        lock (NetworkSync)
        {
            TerminalResults[result.Token] = result;
            RoleDeckWaitAttempts.Remove(result.Token);
        }
    }

    private static int RecordRoleDeckWaitAttempt(
        string token,
        string roleId,
        string ownerPlayerId,
        string ownerStatusId,
        string deckRecipeHash)
    {
        lock (NetworkSync)
        {
            if (!RoleDeckWaitAttempts.TryGetValue(token ?? "", out var state))
            {
                state = new RoleDeckWaitState(
                    roleId,
                    ownerPlayerId,
                    ownerStatusId,
                    deckRecipeHash);
                RoleDeckWaitAttempts[token ?? ""] = state;
            }
            state.Attempts++;
            return state.Attempts;
        }
    }

    private static bool RoleDeckWaitRequestMatches(
        string token,
        string roleId,
        string ownerPlayerId,
        string ownerStatusId,
        string deckRecipeHash)
    {
        lock (NetworkSync)
        {
            return !RoleDeckWaitAttempts.TryGetValue(token ?? "", out var state)
                   || state.Matches(roleId, ownerPlayerId, ownerStatusId, deckRecipeHash);
        }
    }

    private static void ScheduleTurnReservationExpiry(
        string token,
        string roleId,
        string ownerStatusId,
        string ownerPlayerId,
        int battleEpoch,
        double deadline = double.NaN)
    {
        var normalizedToken = token ?? "";
        var effectiveDeadline = double.IsNaN(deadline)
            ? Time.unscaledTimeAsDouble + TransactionLifetimeSeconds
            : deadline;
        TerriasFrameDispatcher.RunOnceAfterFrames(
            "Projection.SummonTurnExpiry." + normalizedToken,
            HostReservationPollFrames,
            () => ExpireTurnReservation(
                normalizedToken,
                roleId,
                ownerStatusId,
                ownerPlayerId,
                battleEpoch,
                effectiveDeadline));
    }

    private static void ExpireTurnReservation(
        string token,
        string roleId,
        string ownerStatusId,
        string ownerPlayerId,
        int battleEpoch,
        double deadline)
    {
        if (!CompanionAuthorityService.IsAuthoritative()
            || battleEpoch != CompanionAuthorityService.BattleEpoch
            || !ProjectionTurnCoordinator.TryGetTransaction(token, out var transaction)
            || transaction.State != ProjectionSummonTurnTransactionState.Reserved)
        {
            return;
        }
        if (Time.unscaledTimeAsDouble < deadline)
        {
            ScheduleTurnReservationExpiry(
                token,
                roleId,
                ownerStatusId,
                ownerPlayerId,
                battleEpoch,
                deadline);
            return;
        }
        lock (NetworkSync)
        {
            if (TerminalResults.ContainsKey(token)) return;
        }

        const string detail = "projection summon turn reservation expired before actor readiness";
        ProjectionTurnCoordinator.TryMarkFailedAuthoritative(
            token,
            detail,
            "ProjectionSummonService.TurnReservationExpiry");
        RejectSummon(
            roleId,
            ownerStatusId,
            ownerPlayerId,
            token,
            ProjectionSummonFailureCode.RoleDeckTimedOut,
            detail,
            "ProjectionSummonService.TurnReservationExpiry");
    }

    private static void BroadcastSummonResult(ProjectionSummonResultSnapshot result, string source)
    {
        ApplySummonResult(result, source + ".Local");
        if (TerriasNetworkRuntime.IsMultiplayerSession())
        {
            TerriasNetworkRuntime.Send(new RpcProjectionSummonResult(result), source);
        }
    }

    public static void ApplySummonResult(ProjectionSummonResultSnapshot? result, string source)
    {
        if (result == null || string.IsNullOrWhiteSpace(result.Token))
        {
            return;
        }

        ProjectionSummonTransaction? transaction;
        lock (NetworkSync)
        {
            PendingTransactions.TryGetValue(result.Token, out transaction);
            if (result.Terminal && transaction != null)
            {
                transaction.SetTerminal();
            }
            if (result.Terminal && !AppliedResultTokens.Add(result.Token))
            {
                return;
            }
        }

        var isLocalOwner = transaction != null
                           || string.Equals(
                               FightPlayer.Instance?.Status?.InstanceId,
                               result.OwnerStatusId,
                               StringComparison.Ordinal);
        if (!isLocalOwner)
        {
            return;
        }
        if (!result.Accepted)
        {
            TerriasLog.Warn("[ProjectionSummonResult] code="
                + result.FailureCode
                + "; category="
                + result.FailureCategory
                + "; terminal="
                + result.Terminal
                + "; retryable="
                + result.Retryable
                + "; detail="
                + (result.Detail ?? ""));
        }
        if (!result.Terminal)
        {
            if (transaction == null || transaction.Attempts <= 1)
            {
                PlayerApi.ShowCaption(TerriasTextCatalog.Get(ProjectionSummonFailureCatalog.LocalizationKey(result.FailureCode)));
            }
            return;
        }
        if (result.Accepted)
        {
            return;
        }

        var failure = ProjectionSummonFailureCatalog.Describe(result.FailureCode);
        PlayerApi.ShowCaption(TerriasTextCatalog.Get(ProjectionSummonFailureCatalog.LocalizationKey(result.FailureCode)));
        if (result.RefundCard
            && (transaction == null || transaction.TryClaimRefund()))
        {
            RefundProjectionRoleCard(result.RoleId, result.OwnerStatusId, result.Token, source);
        }
    }

    private static bool BroadcastNetworkState(ProjectionCompanionSnapshot snapshot, string source)
    {
        return TerriasNetworkRuntime.Send(new RpcProjectionCompanionState(snapshot), source);
    }

    public static void BroadcastTurnTransaction(
        ProjectionSummonTurnTransaction transaction,
        string source)
    {
        if (transaction == null
            || !CompanionAuthorityService.IsAuthoritative()
            || !TerriasNetworkRuntime.IsMultiplayerSession())
        {
            return;
        }

        TerriasNetworkRuntime.Send(
            new RpcProjectionSummonTurnState(new ProjectionSummonTurnSnapshot
            {
                ProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
                BattleEpoch = CompanionAuthorityService.BattleEpoch,
                Token = transaction.Token,
                RoundSequence = transaction.RoundSequence,
                Order = transaction.Order,
                Revision = transaction.Revision,
                State = transaction.State,
                StatusId = transaction.StatusId,
                Generation = transaction.Generation,
                Detail = transaction.Detail
            }),
            "ProjectionSummonTurn." + source);
    }

    public static void BroadcastRuntimeState(ProjectionOtherObj projection, string source)
    {
        if (projection == null || !TerriasNetworkRuntime.IsMultiplayerSession() || !CompanionAuthorityService.IsAuthoritative())
        {
            return;
        }

        BroadcastNetworkState(BuildSnapshot(projection), "ProjectionRuntime." + source);
    }

    public static void BroadcastTurnCompleted(ProjectionOtherObj projection, string source)
    {
        var projectionState = ProjectionStateStore.Find(projection.InstanceId);
        if (projectionState == null || !CompanionAuthorityService.IsAuthoritative())
        {
            return;
        }
        projectionState.Replication.CompleteTurn();
        projectionState.RemoteTurnGate.Observe(
            projectionState.Replication.CompletedTurnSequence,
            projectionState.Replication.ActionSequence,
            projectionState.Replication.StateRevision,
            Time.unscaledTimeAsDouble);
        BroadcastRuntimeState(projection, source);
    }

    public static void BroadcastExternalStateChange(ProjectionOtherObj projection, string source)
    {
        var state = ProjectionStateStore.Find(projection.InstanceId);
        if (state == null || !CompanionAuthorityService.IsAuthoritative())
        {
            return;
        }
        state.Replication.Touch();
        BroadcastRuntimeState(projection, "ExternalState." + source);
    }

    public static void BroadcastRetired(ProjectionState state, string source)
    {
        if (state == null || !CompanionAuthorityService.IsAuthoritative())
        {
            return;
        }
        state.Replication.Retire();
        RememberTombstone(
            state.StatusId,
            state.Replication.Generation,
            state.Replication.StateRevision);
        if (TerriasNetworkRuntime.IsMultiplayerSession())
        {
            BroadcastNetworkState(BuildSnapshot(state.Projection), "ProjectionRuntime.Retired." + source);
        }
    }

    public static void RequestRuntimeState(ProjectionOtherObj projection, string source)
    {
        var state = ProjectionStateStore.Find(projection?.InstanceId ?? "");
        if (state == null || !TerriasNetworkRuntime.IsMultiplayerSession())
        {
            return;
        }
        TerriasNetworkRuntime.Send(
            new RpcProjectionStateRequest(state.StatusId, state.Replication.Generation),
            "ProjectionRuntime.StateRequest." + source);
    }

    public static void ResolveStateRequest(
        string statusId,
        string generation,
        TerriasRpcSender sender,
        int protocolVersion,
        int battleEpoch)
    {
        if (!CompanionAuthorityService.IsAuthoritative()
            || protocolVersion != CompanionAuthorityService.ProjectionProtocolVersion
            || battleEpoch != CompanionAuthorityService.BattleEpoch
            || !sender.IsAvailable
            || !sender.IsLobbyMember)
        {
            return;
        }
        var state = ProjectionStateStore.Find(statusId);
        if (state == null
            || !string.Equals(state.Replication.Generation, generation ?? "", StringComparison.Ordinal))
        {
            return;
        }
        BroadcastNetworkState(BuildSnapshot(state.Projection), "ProjectionRuntime.StateRequestReply");
    }

    private static ProjectionCompanionSnapshot BuildSnapshot(ProjectionOtherObj projection)
    {
        var state = CompanionBattleStateStore.Find(projection.InstanceId);
        var projectionState = ProjectionStateStore.Find(projection.InstanceId);
        var replication = projectionState?.Replication;
        var turnToken = projectionState?.SummonTurnToken ?? replication?.Generation ?? "";
        var hasTurnTransaction = ProjectionTurnCoordinator.TryGetTransaction(
            turnToken,
            out var turnTransaction);
        return new ProjectionCompanionSnapshot
        {
            ProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
            BattleEpoch = CompanionAuthorityService.BattleEpoch,
            CardModelVersion = ProjectionRoleDeckService.CardModelVersion,
            Generation = replication?.Generation ?? "",
            StateRevision = replication?.StateRevision ?? 0L,
            ActionSequence = replication?.ActionSequence ?? 0L,
            CompletedTurnSequence = replication?.CompletedTurnSequence ?? 0L,
            SummonRoundSequence = projectionState?.SummonRoundSequence ?? 0,
            SummonTurnToken = hasTurnTransaction ? turnTransaction.Token : turnToken,
            SummonTurnOrder = hasTurnTransaction
                ? turnTransaction.Order
                : projectionState?.SummonTurnOrder ?? 0L,
            SummonTurnRevision = hasTurnTransaction ? turnTransaction.Revision : 0L,
            SummonTurnState = hasTurnTransaction
                ? turnTransaction.State
                : default,
            SummonTurnDetail = hasTurnTransaction ? turnTransaction.Detail : "",
            Active = replication?.Active ?? true,
            RoleId = projection.RoleId,
            OwnerPlayerId = projection.OwnerPlayerId,
            OwnerStatusId = projection.OwnerStatusId,
            StatusId = projection.InstanceId,
            SlotIndex = state?.SlotIndex ?? -1,
            MaxHp = projection.MaxHp,
            CurrentHp = projection.CurHp,
            Attack = projection.Attack,
            Armor = projection.Defend,
            MaxMagic = state?.Stats.MaxMagic ?? 1,
            CurrentMagic = state?.Stats.CurrentMagic ?? 0
        };
    }

    private static void ApplyTurnTransaction(
        ProjectionCompanionSnapshot snapshot,
        string source)
    {
        if (CompanionAuthorityService.IsAuthoritative()
            || snapshot == null
            || string.IsNullOrWhiteSpace(snapshot.SummonTurnToken)
            || snapshot.SummonRoundSequence <= 0
            || snapshot.SummonTurnOrder <= 0
            || snapshot.SummonTurnRevision <= 0
            || !Enum.IsDefined(
                typeof(ProjectionSummonTurnTransactionState),
                snapshot.SummonTurnState))
        {
            return;
        }

        ProjectionTurnCoordinator.ApplyAuthoritativeTransaction(
            new ProjectionSummonTurnTransaction
            {
                Token = snapshot.SummonTurnToken,
                RoundSequence = snapshot.SummonRoundSequence,
                Order = snapshot.SummonTurnOrder,
                Revision = snapshot.SummonTurnRevision,
                State = snapshot.SummonTurnState,
                StatusId = snapshot.StatusId,
                Generation = snapshot.Generation,
                Detail = snapshot.SummonTurnDetail
            },
            source + ".CompanionSnapshot");
    }

    private static void ApplySnapshot(ProjectionOtherObj projection, ProjectionCompanionSnapshot snapshot, string source)
    {
        var state = CompanionBattleStateStore.Find(projection.InstanceId);
        var projectionState = ProjectionStateStore.Find(projection.InstanceId);
        if (state == null
            || projectionState == null
            || !projectionState.Replication.TryApplyRemote(
                snapshot.Generation,
                snapshot.StateRevision,
                snapshot.ActionSequence,
                snapshot.CompletedTurnSequence,
                snapshot.Active))
        {
            return;
        }

        state.Stats.SetCurrentMagic(snapshot.CurrentMagic);
        state.ApplyRemoteProgress(
            snapshot.CompletedTurnSequence > int.MaxValue
                ? int.MaxValue
                : (int)snapshot.CompletedTurnSequence,
            snapshot.StateRevision > int.MaxValue
                ? int.MaxValue
                : (int)snapshot.StateRevision);
        projectionState.RemoteTurnGate.Observe(
            snapshot.CompletedTurnSequence,
            snapshot.ActionSequence,
            snapshot.StateRevision,
            Time.unscaledTimeAsDouble);
        if (projection.Status != null)
        {
            projection.MaxHp = Math.Max(1, snapshot.MaxHp);
            projection.CurHp = Math.Max(0, Math.Min(projection.MaxHp, snapshot.CurrentHp));
            projection.Attack = snapshot.Attack;
            projection.Defend = Math.Max(0, snapshot.Armor);
            projection.Status.UpdateStatus(true);
        }
        projection.ActivateAfterHydration(null, source);
    }

    private static void RememberTombstone(
        string statusId,
        string generation,
        long stateRevision)
    {
        if (string.IsNullOrWhiteSpace(statusId) || string.IsNullOrWhiteSpace(generation))
        {
            return;
        }
        lock (NetworkSync)
        {
            if (Tombstones.TryGetValue(statusId, out var existing)
                && string.Equals(existing.Generation, generation, StringComparison.Ordinal)
                && existing.StateRevision >= stateRevision)
            {
                return;
            }
            Tombstones[statusId] = new ProjectionTombstone(generation, stateRevision);
        }
    }

    private static ProjectionSummonFailureCode ValidateNetworkSender(
        TerriasRpcSender sender,
        string ownerStatusId)
    {
        if (!TerriasNetworkRuntime.IsMultiplayerSession())
        {
            return ProjectionSummonFailureCode.None;
        }

        if (!sender.IsAvailable)
        {
            return ProjectionSummonFailureCode.MissingSender;
        }

        if (!sender.IsLobbyMember)
        {
            return ProjectionSummonFailureCode.SenderOutsideLobby;
        }

        return SenderOwnsStatus(sender.PlayerId, ownerStatusId)
            ? ProjectionSummonFailureCode.None
            : ProjectionSummonFailureCode.OwnerMismatch;
    }

    private static bool SenderOwnsStatus(string playerId, string ownerStatusId)
    {
        if (string.IsNullOrWhiteSpace(ownerStatusId))
        {
            return false;
        }

        if (string.Equals(playerId, ownerStatusId, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            var map = Singleton<TempDataManager>.Instance?.RoleStatusMap;
            return map != null
                && map.TryGetValue(playerId, out var statuses)
                && statuses != null
                && statuses.Contains(ownerStatusId);
        }
        catch
        {
            return false;
        }
    }

    private static void RefundProjectionRoleCard(
        string roleId,
        string ownerStatusId,
        string token,
        string source)
    {
        var refundToken = token ?? "";
        TerriasFrameDispatcher.RunOnceAfterFrames(
            "Projection.RefundRoleCard." + refundToken,
            1,
            () => RefundProjectionRoleCardAfterSettlement(
                roleId,
                ownerStatusId,
                refundToken,
                source));
    }

    private static void RefundProjectionRoleCardAfterSettlement(
        string roleId,
        string ownerStatusId,
        string token,
        string source)
    {
        var owner = FightPlayer.Instance?.Status;
        var executor = owner?.MirrorSc as ScriptExecutor;
        if (owner == null
            || executor == null
            || !string.Equals(owner.InstanceId, ownerStatusId, StringComparison.Ordinal))
        {
            TerriasLog.Warn("[Projection] refund failed from " + source + ": owner executor unavailable.");
            return;
        }
        executor.Self = owner;
        ProjectionActivationService.GrantRoleCard(executor, roleId);
    }

    private sealed class ProjectionTombstone
    {
        public ProjectionTombstone(string generation, long stateRevision)
        {
            Generation = generation ?? "";
            StateRevision = Math.Max(0L, stateRevision);
        }

        public string Generation { get; }
        public long StateRevision { get; }
    }

    private sealed class RoleDeckWaitState
    {
        public RoleDeckWaitState(
            string roleId,
            string ownerPlayerId,
            string ownerStatusId,
            string deckRecipeHash)
        {
            Identity = new ProjectionSummonRequestIdentity(
                roleId,
                ownerPlayerId,
                ownerStatusId,
                deckRecipeHash);
        }

        public ProjectionSummonRequestIdentity Identity { get; }
        public int Attempts { get; set; }

        public bool Matches(
            string roleId,
            string ownerPlayerId,
            string ownerStatusId,
            string deckRecipeHash)
        {
            return Identity.Matches(roleId, ownerPlayerId, ownerStatusId, deckRecipeHash);
        }
    }

}
