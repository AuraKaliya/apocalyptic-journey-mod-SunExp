using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatAi.Shared.GameApi;
using AuraGameData.Shared.GameApi;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Network;
using UnityEngine;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.Mechanics;

public static class ProjectionSummonService
{
    private const string RejectProtocolMismatch = "projection protocol mismatch";
    private const string RejectBattleEpochMismatch = "projection battle epoch mismatch";
    private const string RejectIntentRegistryMismatch = "projection card runtime mismatch";
    private const string RejectUnknownRolePrefix = "unknown role:";
    private const string RejectOwnerAlreadyHasProjection = "owner already has projection";
    private const string RejectMissingOwnerStatus = "missing owner status";
    private const string RejectMissingSender = "missing sender";
    private const string RejectSenderOutsideLobby = "sender outside lobby";
    private const string RejectOwnerMismatch = "owner mismatch";
    private const string RejectFriendlySeatsFull = "friendly role seats are full";
    private const string RejectPrivateStateInvalid = "projection private state invalid";
    private const int MaxActiveUploads = 8;
    private const int PrivateStateTimeoutSeconds = 30;
    private const int PrivateStateExpiryCheckFrames = 600;

    private static readonly object NetworkSync = new();
    private static readonly HashSet<string> ResolvedTokens = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LocalPreparedTokens = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PendingProjection> Pending = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PendingProjectionUpload> Uploads = new(StringComparer.Ordinal);

    public static void ResetBattleSynchronization()
    {
        lock (NetworkSync)
        {
            ResolvedTokens.Clear();
            LocalPreparedTokens.Clear();
            Pending.Clear();
            Uploads.Clear();
        }
        FriendlyRoleSeatLedger.BeginBattle();
    }

    public static bool TrySummon(ScriptExecutor self, PolymorphRoleSpec role)
    {
        if (self?.Self == null || role == null)
        {
            PlayerApi.ShowCaption("拜托了：召唤失败。");
            return false;
        }

        if (FightManager.Instance == null || FightManager.Instance.fightType == FightType.None)
        {
            PlayerApi.ShowCaption("拜托了：只能在战斗中召唤。");
            return false;
        }

        var token = Guid.NewGuid().ToString("N");
        if (TerriasNetworkRuntime.IsMultiplayerSession() && !TerriasNetworkRuntime.IsServer())
        {
            TerriasNetworkRuntime.Send(
                new RpcProjectionSummonRequest(role.Id, self.Self.InstanceId, token),
                "ProjectionSummonService.TrySummon");
            PlayerApi.ShowCaption("拜托了：正在同步投影。");
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
            ProjectionCardBattleState.ProtocolIdentity);
        return true;
    }

    public static void ResolveNetworkSummon(
        string roleId,
        string ownerStatusId,
        string token,
        TerriasRpcSender sender,
        int protocolVersion,
        int battleEpoch,
        string registryHash)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }
        lock (NetworkSync)
        {
            if (ResolvedTokens.Contains(token))
            {
                return;
            }
        }

        var role = PolymorphRoleRegistry.Find(roleId);
        var rejection = ValidateNetworkSender(sender, ownerStatusId);
        if (protocolVersion != CompanionAuthorityService.ProjectionProtocolVersion)
        {
            rejection = RejectProtocolMismatch;
        }
        else if (battleEpoch != CompanionAuthorityService.BattleEpoch)
        {
            rejection = RejectBattleEpochMismatch;
        }
        else if (!string.Equals(registryHash, ProjectionCardBattleState.ProtocolIdentity, StringComparison.Ordinal))
        {
            rejection = RejectIntentRegistryMismatch;
        }
        if (role == null)
        {
            rejection = RejectUnknownRolePrefix + " " + roleId;
        }

        if (!string.IsNullOrWhiteSpace(rejection))
        {
            BroadcastPrepareResult(new ProjectionPrepareResult
            {
                ProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
                BattleEpoch = CompanionAuthorityService.BattleEpoch,
                Token = token,
                RoleId = roleId,
                OwnerStatusId = ownerStatusId,
                OwnerPlayerId = sender.IsAvailable ? sender.PlayerId : "",
                Accepted = false,
                RefundCard = string.IsNullOrWhiteSpace(ValidateNetworkSender(sender, ownerStatusId))
                             && role != null,
                RejectionReason = rejection
            }, "ProjectionSummonService.ResolveNetworkSummon.Reject");
            return;
        }

        var ownerPlayerId = CompanionOwnershipService.ResolveOwnerPlayerId(ownerStatusId, sender.PlayerId);
        if (!FriendlyRoleSeatLedger.TryReserve(
                token,
                ownerPlayerId,
                ownerStatusId,
                battleEpoch,
                out var slotIndex,
                out var seatReason))
        {
            BroadcastPrepareResult(new ProjectionPrepareResult
            {
                ProtocolVersion = protocolVersion,
                BattleEpoch = battleEpoch,
                Token = token,
                RoleId = roleId,
                OwnerStatusId = ownerStatusId,
                OwnerPlayerId = ownerPlayerId,
                Accepted = false,
                RefundCard = true,
                RejectionReason = seatReason
            }, "ProjectionSummonService.ResolveNetworkSummon.SeatReject");
            return;
        }

        lock (NetworkSync)
        {
            Pending[token] = new PendingProjection(
                token,
                role!,
                ownerPlayerId,
                ownerStatusId,
                slotIndex,
                DateTime.UtcNow.AddSeconds(PrivateStateTimeoutSeconds));
        }
        SchedulePendingExpiry(token, 0);
        BroadcastPrepareResult(new ProjectionPrepareResult
        {
            ProtocolVersion = protocolVersion,
            BattleEpoch = battleEpoch,
            Token = token,
            RoleId = roleId,
            OwnerStatusId = ownerStatusId,
            OwnerPlayerId = ownerPlayerId,
            SlotIndex = slotIndex,
            Accepted = true
        }, "ProjectionSummonService.ResolveNetworkSummon.Prepared");
    }

    public static void ApplyPrepareResult(ProjectionPrepareResult? result, string source)
    {
        if (result == null
            || result.ProtocolVersion != CompanionAuthorityService.ProjectionProtocolVersion
            || result.BattleEpoch != CompanionAuthorityService.BattleEpoch
            || string.IsNullOrWhiteSpace(result.Token))
        {
            return;
        }
        if (!SenderOwnsStatus(TerriasNetworkRuntime.LocalPlayerId(), result.OwnerStatusId)
            && !string.Equals(FightPlayer.Instance?.Status?.InstanceId, result.OwnerStatusId, StringComparison.Ordinal))
        {
            return;
        }
        if (!result.Accepted)
        {
            ShowRejectionCaption(result.RejectionReason);
            if (result.RefundCard)
            {
                RefundProjectionRoleCard(result.RoleId, result.OwnerStatusId, result.Token, source);
            }
            return;
        }

        lock (NetworkSync)
        {
            if (!LocalPreparedTokens.Add(result.Token))
            {
                return;
            }
        }

        CaptureAndUploadAfterSettlement(result, source, 0);
    }

    public static void AcceptPrivateStateChunk(
        RpcProjectionPrivateStateChunk chunk,
        TerriasRpcSender sender)
    {
        if (chunk == null
            || chunk.ProtocolVersion != CompanionAuthorityService.ProjectionProtocolVersion
            || chunk.BattleEpoch != CompanionAuthorityService.BattleEpoch
            || !sender.IsAvailable
            || !sender.IsLobbyMember
            || chunk.ChunkCount <= 0
            || chunk.ChunkCount > ProjectionCardStateTransport.MaxChunks
            || chunk.ChunkIndex < 0
            || chunk.ChunkIndex >= chunk.ChunkCount
            || chunk.TotalBytes <= 0
            || chunk.TotalBytes > ProjectionCardStateTransport.MaxCompressedBytes
            || chunk.UncompressedBytes <= 0
            || chunk.UncompressedBytes > ProjectionCardStateTransport.MaxUncompressedBytes
            || chunk.Payload == null
            || chunk.Payload.Length == 0
            || chunk.Payload.Length > ProjectionCardStateTransport.ChunkBytes)
        {
            return;
        }

        var token = chunk.Token ?? "";

        PendingProjection? pending;
        PendingProjectionUpload upload;
        lock (NetworkSync)
        {
            PruneNetworkState();
            if (!Pending.TryGetValue(token, out pending)
                || !string.Equals(pending.OwnerPlayerId, sender.PlayerId, StringComparison.Ordinal))
            {
                return;
            }
            if (!Uploads.TryGetValue(token, out upload!))
            {
                if (Uploads.Count >= MaxActiveUploads)
                {
                    RejectPending(token, RejectPrivateStateInvalid, true);
                    return;
                }
                upload = new PendingProjectionUpload(
                    token,
                    chunk.ChunkCount,
                    chunk.TotalBytes,
                    chunk.UncompressedBytes,
                    chunk.Sha256,
                    DateTime.UtcNow.AddSeconds(PrivateStateTimeoutSeconds));
                Uploads[token] = upload;
            }
            if (!upload.Accept(chunk))
            {
                RejectPending(token, RejectPrivateStateInvalid, true);
                return;
            }
            if (!upload.Complete)
            {
                return;
            }
            Uploads.Remove(token);
        }

        var payload = upload.Join();
        if (!ProjectionCardStateTransport.TryDecode(
                payload,
                upload.Sha256,
                upload.UncompressedBytes,
                out var envelope,
                out var reason)
            || envelope == null
            || !string.Equals(envelope.Token, token, StringComparison.Ordinal)
            || !string.Equals(envelope.CardState.OwnerModId, TerriasIds.ModId, StringComparison.OrdinalIgnoreCase))
        {
            RejectPending(token, reason, true);
            return;
        }
        CompletePreparedSummon(pending!, envelope, "ProjectionSummonService.AcceptPrivateStateChunk");
    }

    public static void AbortPrivateStateUpload(
        string token,
        string reason,
        TerriasRpcSender sender,
        int protocolVersion,
        int battleEpoch)
    {
        lock (NetworkSync)
        {
            if (protocolVersion != CompanionAuthorityService.ProjectionProtocolVersion
                || battleEpoch != CompanionAuthorityService.BattleEpoch
                || !Pending.TryGetValue(token ?? "", out var pending)
                || !string.Equals(pending.OwnerPlayerId, sender.PlayerId, StringComparison.Ordinal))
            {
                return;
            }
        }
        RejectPending(token ?? "", reason, true);
    }

    public static void ApplyNetworkState(ProjectionCompanionSnapshot? snapshot, string source)
    {
        if (snapshot == null)
        {
            return;
        }

        if (!snapshot.Accepted)
        {
            if (SenderOwnsStatus(TerriasNetworkRuntime.LocalPlayerId(), snapshot.OwnerStatusId))
            {
                ShowRejectionCaption(snapshot.RejectionReason);
            }

            return;
        }

        var role = PolymorphRoleRegistry.Find(snapshot.RoleId);
        if (role == null || string.IsNullOrWhiteSpace(snapshot.StatusId))
        {
            return;
        }

        if (snapshot.ProtocolVersion != CompanionAuthorityService.ProjectionProtocolVersion
            || snapshot.BattleEpoch != CompanionAuthorityService.BattleEpoch
            || !string.Equals(snapshot.RegistryHash, ProjectionCardBattleState.ProtocolIdentity, StringComparison.Ordinal))
        {
            TerriasLog.Warn("[Projection] ignored incompatible snapshot: protocol=" + snapshot.ProtocolVersion
                + ", epoch=" + snapshot.BattleEpoch + ", localEpoch=" + CompanionAuthorityService.BattleEpoch);
            return;
        }

        var existing = ProjectionStateStore.Find(snapshot.StatusId);
        if (existing != null)
        {
            ApplySnapshot(existing.Projection, snapshot, source);
            return;
        }

        var ownerExisting = ProjectionStateStore.FindByOwner(snapshot.OwnerPlayerId, snapshot.OwnerStatusId);
        if (ownerExisting != null)
        {
            ApplySnapshot(ownerExisting.Projection, snapshot, source + ".OwnerAlreadyBound");
            return;
        }

        SpawnProjection(role, snapshot.OwnerStatusId, snapshot.SlotIndex, snapshot.StatusId, source, snapshot);
    }

    public static DataConfig CreateProjectionDataConfig(PolymorphRoleSpec role, CompanionStats? stats = null)
    {
        var activeStats = stats ?? CompanionStatsService.ProjectionStats(role);
        var name = role.DisplayName + "的投影";
        var handle = AuraGameDataHostApi.ResolveHandle(DataType.Career, role.Id)
            ?? throw new InvalidOperationException("Projection career definition is not registered: " + role.Id);
        var result = AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest
        {
            Definition = handle,
            DataOverrides = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Name"] = name,
                ["Name_zh-Hant"] = role.DisplayName + "的投影",
                ["Name_en"] = role.DisplayName + " Projection",
                ["Name_ja"] = role.DisplayName + "の投影",
                ["Attack"] = activeStats.Attack.ToString(),
                ["Defend"] = activeStats.Armor.ToString(),
                ["Hp"] = activeStats.MaxHp.ToString(),
                ["ActionCount"] = "0",
                ["CardList"] = ""
            }
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

        ProjectionTurnCoordinator.RegisterProjection(projection, source);

        // The internal Status remains available to ScriptExecutor through
        // FightManager.statuses, but is not a formal friendly target or HUD row.
    }

    private static bool TrySummonLocal(
        string ownerStatusId,
        PolymorphRoleSpec role,
        string source,
        bool broadcast,
        string token = "",
        string preferredOwnerPlayerId = "",
        int slotIndex = -1,
        ProjectionPrivateStateEnvelope? privateState = null)
    {
        var ownerPlayerId = CompanionOwnershipService.ResolveOwnerPlayerId(ownerStatusId, preferredOwnerPlayerId);
        if (ProjectionStateStore.HasForOwner(ownerPlayerId, ownerStatusId))
        {
            var sent = BroadcastRejectIfNeeded(
                role.Id,
                ownerStatusId,
                token,
                RejectOwnerAlreadyHasProjection,
                broadcast,
                source);
            ShowLocalRejectionIfNeeded(ownerStatusId, RejectOwnerAlreadyHasProjection, broadcast, sent);
            return false;
        }

        if (string.IsNullOrWhiteSpace(ownerStatusId))
        {
            var sent = BroadcastRejectIfNeeded(
                role.Id,
                ownerStatusId,
                token,
                RejectMissingOwnerStatus,
                broadcast,
                source);
            ShowLocalRejectionIfNeeded(ownerStatusId, RejectMissingOwnerStatus, broadcast, sent);
            return false;
        }

        if (slotIndex < 0)
        {
            slotIndex = FriendlyRoleSeatLedger.FindOpenSeat() ?? -1;
        }
        if (slotIndex < 0)
        {
            BroadcastRejectIfNeeded(role.Id, ownerStatusId, token, RejectFriendlySeatsFull, broadcast, source);
            return false;
        }

        var statusId = ProjectionStateStore.NextStatusId();
        if (privateState?.CardState != null)
        {
            privateState.CardState.ActorId = statusId;
        }
        var spawned = SpawnProjection(
            role,
            ownerStatusId,
            slotIndex,
            statusId,
            source,
            null,
            ownerPlayerId,
            privateState);
        if (spawned && broadcast)
        {
            var projection = ProjectionStateStore.Find(statusId)?.Projection;
            if (projection != null)
            {
                var snapshot = BuildSnapshot(projection);
                snapshot.Token = string.IsNullOrWhiteSpace(token) ? Guid.NewGuid().ToString("N") : token;
                BroadcastNetworkState(snapshot, source);
            }
        }

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
        ProjectionPrivateStateEnvelope? privateState = null)
    {
        try
        {
            var prefab = TerriasResourceCache.Load<GameObject>("Model/player", true, "projection");
            if (prefab == null)
            {
                PlayerApi.ShowCaption("拜托了：投影模型加载失败。");
                return false;
            }

            var gameObject = UnityEngine.Object.Instantiate(prefab);
            if (gameObject == null)
            {
                PlayerApi.ShowCaption("拜托了：投影模型加载失败。");
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
                PlayerApi.ShowCaption("拜托了：投影初始化失败。");
                return false;
            }

            ProjectionStateStore.Register(new ProjectionState(
                projection.InstanceId,
                ownerStatusId,
                role.Id,
                role.DisplayName,
                projection,
                slotIndex,
                projection.OwnerPlayerId));
            if (snapshot == null)
            {
                projection.Status.UpdateStatus(true);
                projection.HydrateOwnerCombatState(privateState?.OwnerCombat);
                projection.HydrateCardState(privateState?.CardState, source + ".PrivateState");
                projection.ActivateAfterHydration(null, source + ".AuthoritativeInit");
            }
            else
            {
                ApplySnapshot(projection, snapshot, source + ".Hydrate");
            }
            PlayerApi.ShowCaption("拜托了：" + role.DisplayName + "的投影加入战斗。");
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[Projection] summon failed from " + source, ex);
            PlayerApi.ShowCaption("拜托了：召唤失败。");
            return false;
        }
    }

    private static bool BroadcastRejectIfNeeded(string roleId, string ownerStatusId, string token, string reason, bool broadcast, string source)
    {
        if (!broadcast)
        {
            return false;
        }

        return BroadcastNetworkState(new ProjectionCompanionSnapshot
        {
            Token = token ?? "",
            RoleId = roleId ?? "",
            OwnerStatusId = ownerStatusId ?? "",
            Accepted = false,
            RejectionReason = reason ?? ""
        }, source + ".Reject");
    }

    private static bool BroadcastNetworkState(ProjectionCompanionSnapshot snapshot, string source)
    {
        return TerriasNetworkRuntime.Send(new RpcProjectionCompanionState(snapshot), source);
    }

    private static void ShowLocalRejectionIfNeeded(string ownerStatusId, string reason, bool broadcast, bool sent)
    {
        if (!broadcast || !sent && SenderOwnsStatus(TerriasNetworkRuntime.LocalPlayerId(), ownerStatusId))
        {
            ShowRejectionCaption(reason);
        }
    }

    private static void ShowRejectionCaption(string reason)
    {
        PlayerApi.ShowCaption("拜托了：" + RejectionMessage(reason));
    }

    private static string RejectionMessage(string reason)
    {
        var normalized = (reason ?? "").Trim();
        if (normalized.StartsWith(RejectUnknownRolePrefix, StringComparison.Ordinal))
        {
            return "投影目标已失效。";
        }

        return normalized switch
        {
            RejectOwnerAlreadyHasProjection => "投影位置已被占用。",
            RejectMissingOwnerStatus => "没有可用的友方站位。",
            RejectProtocolMismatch => "投影协议版本不一致。",
            RejectBattleEpochMismatch => "当前战斗状态已失效，请重新使用。",
            RejectIntentRegistryMismatch => "投影行动配置不一致。",
            RejectMissingSender => "无法确认操作玩家。",
            RejectSenderOutsideLobby => "操作玩家不在当前房间中。",
            RejectOwnerMismatch => "当前角色不属于该玩家。",
            _ => "投影召唤失败，请稍后重试。"
        };
    }

    public static void BroadcastRuntimeState(ProjectionOtherObj projection, string source)
    {
        if (projection == null || !TerriasNetworkRuntime.IsMultiplayerSession() || !CompanionAuthorityService.IsAuthoritative())
        {
            return;
        }

        BroadcastNetworkState(BuildSnapshot(projection), "ProjectionRuntime." + source);
    }

    private static ProjectionCompanionSnapshot BuildSnapshot(ProjectionOtherObj projection)
    {
        var state = CompanionBattleStateStore.Find(projection.InstanceId);
        return new ProjectionCompanionSnapshot
        {
            ProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
            BattleEpoch = CompanionAuthorityService.BattleEpoch,
            RegistryHash = ProjectionCardBattleState.ProtocolIdentity,
            Revision = state?.Revision ?? 0,
            Accepted = true,
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
            CurrentMagic = state?.Stats.CurrentMagic ?? 0,
            TurnIndex = state?.TurnIndex ?? 0
        };
    }

    private static void ApplySnapshot(ProjectionOtherObj projection, ProjectionCompanionSnapshot snapshot, string source)
    {
        var state = CompanionBattleStateStore.Find(projection.InstanceId);
        if (state == null || snapshot.Revision < state.Revision)
        {
            return;
        }

        state.Stats.SetCurrentMagic(snapshot.CurrentMagic);
        state.ApplyRemoteProgress(snapshot.TurnIndex, snapshot.Revision);
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

    private static IStatusManager? StatusById(string statusId)
    {
        return !string.IsNullOrWhiteSpace(statusId)
            && FightManager.Instance?.statuses?.TryGetValue(statusId, out var status) == true
                ? status
                : null;
    }

    private static string ValidateNetworkSender(TerriasRpcSender sender, string ownerStatusId)
    {
        if (!TerriasNetworkRuntime.IsMultiplayerSession())
        {
            return "";
        }

        if (!sender.IsAvailable)
        {
            return RejectMissingSender;
        }

        if (!sender.IsLobbyMember)
        {
            return RejectSenderOutsideLobby;
        }

        return SenderOwnsStatus(sender.PlayerId, ownerStatusId) ? "" : RejectOwnerMismatch;
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

    private static void CaptureAndUploadAfterSettlement(
        ProjectionPrepareResult result,
        string source,
        int attempt)
    {
        if (attempt > 240)
        {
            AbortLocalUpload(result, "projection card settlement timed out");
            return;
        }
        var localOwner = FightPlayer.Instance?.Status;
        if (localOwner == null
            || !string.Equals(localOwner.InstanceId, result.OwnerStatusId, StringComparison.Ordinal))
        {
            AbortLocalUpload(result, "projection summoner local state is unavailable");
            return;
        }
        var fightUi = UIManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi == null
            || WitchCombatRuntime.IsUiBusy(fightUi)
            || (FightUI.WaitCard?.Count ?? 0) > 0)
        {
            TerriasFrameDispatcher.RunOnceAfterFrames(
                "Projection.PrivateStateCapture." + result.Token + "." + attempt,
                1,
                () => CaptureAndUploadAfterSettlement(result, source, attempt + 1));
            return;
        }

        var captured = ProjectionCardBattleState.CaptureFromPlayer(
            "pending:" + result.Token,
            out var captureReason);
        if (captured == null)
        {
            AbortLocalUpload(result, captureReason);
            return;
        }
        var envelope = new ProjectionPrivateStateEnvelope
        {
            ProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
            BattleEpoch = CompanionAuthorityService.BattleEpoch,
            Token = result.Token,
            OwnerCombat = ProjectionOwnerCombatSnapshot.Capture(localOwner),
            CardState = captured.Export(
                TerriasIds.ModId,
                "pending:" + result.Token,
                AuraBattleLifecycleRouter.CurrentBattleSessionId)
        };
        if (!ProjectionCardStateTransport.TryEncode(
                envelope,
                out var compressed,
                out var sha256,
                out var uncompressedBytes,
                out var encodeReason))
        {
            AbortLocalUpload(result, encodeReason);
            return;
        }

        if (!TerriasNetworkRuntime.IsMultiplayerSession())
        {
            PendingProjection? pending;
            lock (NetworkSync)
            {
                Pending.TryGetValue(result.Token, out pending);
            }
            if (pending == null)
            {
                RefundProjectionRoleCard(result.RoleId, result.OwnerStatusId, result.Token, source);
                return;
            }
            CompletePreparedSummon(pending, envelope, source + ".LocalPrivateState");
            return;
        }

        var chunkCount = ProjectionCardStateTransport.ChunkCount(compressed.Length);
        var chunkIndex = 0;
        foreach (var segment in ProjectionCardStateTransport.Chunks(compressed))
        {
            var chunk = new byte[segment.Count];
            Buffer.BlockCopy(segment.Array!, segment.Offset, chunk, 0, segment.Count);
            if (!TerriasNetworkRuntime.Send(new RpcProjectionPrivateStateChunk
                {
                    ProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
                    BattleEpoch = CompanionAuthorityService.BattleEpoch,
                    Token = result.Token,
                    ChunkIndex = chunkIndex++,
                    ChunkCount = chunkCount,
                    TotalBytes = compressed.Length,
                    UncompressedBytes = uncompressedBytes,
                    Sha256 = sha256,
                    Payload = chunk
                }, "Projection.PrivateStateUpload." + source))
            {
                AbortLocalUpload(result, "projection private state upload failed");
                return;
            }
        }
    }

    private static void CompletePreparedSummon(
        PendingProjection pending,
        ProjectionPrivateStateEnvelope envelope,
        string source)
    {
        if (!FriendlyRoleSeatLedger.TryClaim(
                pending.Token,
                pending.OwnerPlayerId,
                pending.OwnerStatusId,
                CompanionAuthorityService.BattleEpoch,
                out var slotIndex))
        {
            RejectPending(pending.Token, "projection seat reservation expired", true);
            return;
        }
        lock (NetworkSync)
        {
            Pending.Remove(pending.Token);
            Uploads.Remove(pending.Token);
            ResolvedTokens.Add(pending.Token);
        }
        if (!TrySummonLocal(
                pending.OwnerStatusId,
                pending.Role,
                source,
                broadcast: TerriasNetworkRuntime.IsMultiplayerSession(),
                token: pending.Token,
                preferredOwnerPlayerId: pending.OwnerPlayerId,
                slotIndex: slotIndex,
                privateState: envelope))
        {
            BroadcastPrepareResult(new ProjectionPrepareResult
            {
                ProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
                BattleEpoch = CompanionAuthorityService.BattleEpoch,
                Token = pending.Token,
                RoleId = pending.Role.Id,
                OwnerStatusId = pending.OwnerStatusId,
                OwnerPlayerId = pending.OwnerPlayerId,
                Accepted = false,
                RefundCard = true,
                RejectionReason = "projection spawn failed"
            }, source + ".SpawnRejected");
        }
    }

    private static void AbortLocalUpload(ProjectionPrepareResult result, string reason)
    {
        if (!TerriasNetworkRuntime.IsMultiplayerSession())
        {
            RejectPending(result.Token, reason, true);
            return;
        }
        TerriasNetworkRuntime.Send(new RpcProjectionPrivateStateAbort
        {
            ProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
            BattleEpoch = CompanionAuthorityService.BattleEpoch,
            Token = result.Token,
            Reason = reason ?? "projection private state unavailable"
        }, "Projection.PrivateStateAbort");
    }

    private static void RejectPending(string token, string reason, bool refundCard)
    {
        PendingProjection? pending;
        lock (NetworkSync)
        {
            Pending.TryGetValue(token ?? "", out pending);
            Pending.Remove(token ?? "");
            Uploads.Remove(token ?? "");
        }
        FriendlyRoleSeatLedger.Release(token);
        if (pending == null)
        {
            return;
        }
        BroadcastPrepareResult(new ProjectionPrepareResult
        {
            ProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
            BattleEpoch = CompanionAuthorityService.BattleEpoch,
            Token = token ?? "",
            RoleId = pending.Role.Id,
            OwnerStatusId = pending.OwnerStatusId,
            OwnerPlayerId = pending.OwnerPlayerId,
            SlotIndex = pending.SlotIndex,
            Accepted = false,
            RefundCard = refundCard,
            RejectionReason = string.IsNullOrWhiteSpace(reason)
                ? RejectPrivateStateInvalid
                : reason
        }, "ProjectionSummonService.RejectPending");
    }

    private static void SchedulePendingExpiry(string token, int attempt)
    {
        TerriasFrameDispatcher.RunOnceAfterFrames(
            "Projection.PrivateStateExpiry." + token + "." + attempt,
            PrivateStateExpiryCheckFrames,
            () =>
            {
                var pending = false;
                var expired = false;
                lock (NetworkSync)
                {
                    if (Pending.TryGetValue(token, out var state))
                    {
                        pending = true;
                        expired = state.ExpiresAtUtc <= DateTime.UtcNow;
                    }
                }

                if (expired)
                {
                    RejectPending(token, "projection private state upload timed out", true);
                }
                else if (pending && attempt < 20)
                {
                    SchedulePendingExpiry(token, attempt + 1);
                }
            });
    }

    private static bool BroadcastPrepareResult(ProjectionPrepareResult result, string source)
    {
        ApplyPrepareResult(result, source + ".Local");
        if (!TerriasNetworkRuntime.IsMultiplayerSession())
        {
            return true;
        }
        return TerriasNetworkRuntime.Send(new RpcProjectionPrepareResult(result), source);
    }

    private static void RefundProjectionRoleCard(
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

    private static void PruneNetworkState()
    {
        var now = DateTime.UtcNow;
        foreach (var token in Uploads
                     .Where(pair => pair.Value.ExpiresAtUtc <= now)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            Uploads.Remove(token);
        }
    }

    private sealed class PendingProjection
    {
        public PendingProjection(
            string token,
            PolymorphRoleSpec role,
            string ownerPlayerId,
            string ownerStatusId,
            int slotIndex,
            DateTime expiresAtUtc)
        {
            Token = token;
            Role = role;
            OwnerPlayerId = ownerPlayerId;
            OwnerStatusId = ownerStatusId;
            SlotIndex = slotIndex;
            ExpiresAtUtc = expiresAtUtc;
        }
        public string Token { get; }
        public PolymorphRoleSpec Role { get; }
        public string OwnerPlayerId { get; }
        public string OwnerStatusId { get; }
        public int SlotIndex { get; }
        public DateTime ExpiresAtUtc { get; }
    }

    private sealed class PendingProjectionUpload
    {
        private readonly byte[][] chunks;
        private int received;

        public PendingProjectionUpload(
            string token,
            int chunkCount,
            int totalBytes,
            int uncompressedBytes,
            string sha256,
            DateTime expiresAtUtc)
        {
            Token = token;
            chunks = new byte[chunkCount][];
            TotalBytes = totalBytes;
            UncompressedBytes = uncompressedBytes;
            Sha256 = sha256 ?? "";
            ExpiresAtUtc = expiresAtUtc;
        }

        public string Token { get; }
        public int TotalBytes { get; }
        public int UncompressedBytes { get; }
        public string Sha256 { get; }
        public DateTime ExpiresAtUtc { get; }
        public bool Complete => received == chunks.Length;

        public bool Accept(RpcProjectionPrivateStateChunk chunk)
        {
            if (chunk.ChunkCount != chunks.Length
                || chunk.TotalBytes != TotalBytes
                || chunk.UncompressedBytes != UncompressedBytes
                || !string.Equals(chunk.Sha256, Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (chunks[chunk.ChunkIndex] != null)
            {
                return chunks[chunk.ChunkIndex].SequenceEqual(chunk.Payload);
            }
            chunks[chunk.ChunkIndex] = chunk.Payload.ToArray();
            received++;
            return chunks.Sum(value => value?.Length ?? 0) <= TotalBytes;
        }

        public byte[] Join()
        {
            var result = new byte[TotalBytes];
            var offset = 0;
            foreach (var chunk in chunks)
            {
                if (chunk == null || offset + chunk.Length > result.Length)
                {
                    return Array.Empty<byte>();
                }
                Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
                offset += chunk.Length;
            }
            return offset == result.Length ? result : Array.Empty<byte>();
        }
    }
}
