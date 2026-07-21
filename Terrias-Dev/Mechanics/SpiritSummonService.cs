using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared.GameApi;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Network;
using UnityEngine;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public static class SpiritSummonService
{
    private const int MaxExchangeCount = 999;
    private const int MaxIntentStateEntries = 128;
    private const int MaxIntentTurnIndex = 10000;
    private static readonly object NetworkSync = new();
    private static readonly HashSet<string> ResolvedTokens = new(StringComparer.Ordinal);
    private static readonly HashSet<string> GrantedCardEvents = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PendingCardGrant> PendingCardGrants = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> OwnerGenerations = new(StringComparer.Ordinal);

    public static void ResetBattleSynchronization()
    {
        lock (NetworkSync)
        {
            ResolvedTokens.Clear();
            GrantedCardEvents.Clear();
            PendingCardGrants.Clear();
            OwnerGenerations.Clear();
        }
    }

    public static void FlushPendingCardReturns(string source)
    {
        PendingCardGrant[] pending;
        lock (NetworkSync)
        {
            pending = PendingCardGrants.Values.ToArray();
        }

        foreach (var item in pending)
        {
            TryDeliverCard(item, null, source);
        }
    }

    public static bool TrySummon(ScriptExecutor self)
    {
        if (!CanSummon(self?.dataConfig, self?.Self, out var reason, out var snapshot))
        {
            PlayerApi.ShowCaption("精灵：" + reason);
            return false;
        }

        var ownerStatusId = self!.Self.InstanceId;
        var exchangeCount = SpiritCardFactory.ReadExchangeCount(self.dataConfig);
        var battleState = SpiritCardFactory.ReadBattleState(self.dataConfig);
        var token = Guid.NewGuid().ToString("N");
        if (TerriasNetworkRuntime.IsMultiplayerSession() && TerriasNetworkRuntime.IsClientOnly())
        {
            TerriasNetworkRuntime.Send(
                new RpcSpiritSummonRequest(snapshot!, ownerStatusId, token, exchangeCount, battleState),
                "SpiritSummonService.TrySummon");
            PlayerApi.ShowCaption("精灵：正在同步召唤。");
            return true;
        }

        return TrySummonLocal(
            snapshot!,
            ownerStatusId,
            "SpiritSummonService.TrySummon",
            TerriasNetworkRuntime.IsMultiplayerSession(),
            token,
            "",
            exchangeCount,
            battleState,
            self);
    }

    public static void ResolveNetworkSummon(
        CapturedEnemySnapshot snapshot,
        string ownerStatusId,
        string token,
        int exchangeCount,
        SpiritCardBattleState battleState,
        TerriasRpcSender sender,
        int protocolVersion,
        int battleEpoch,
        string registryHash)
    {
        if (!ClaimToken(token))
        {
            return;
        }

        var rejection = ValidateNetworkRequest(snapshot, ownerStatusId, exchangeCount, battleState, sender, protocolVersion, battleEpoch, registryHash);
        if (rejection.Length > 0)
        {
            Broadcast(CreateRejection(snapshot, ownerStatusId, token, exchangeCount, battleState, rejection), "SpiritSummonService.ResolveNetworkSummon.Reject");
            return;
        }

        TrySummonLocal(
            snapshot,
            ownerStatusId,
            "SpiritSummonService.ResolveNetworkSummon",
            true,
            token,
            sender.PlayerId,
            exchangeCount,
            battleState,
            null);
    }

    public static void ApplyNetworkState(SpiritCompanionSnapshot? snapshot, string source)
    {
        if (snapshot == null)
        {
            return;
        }

        if (snapshot.ProtocolVersion != CompanionAuthorityService.ProjectionProtocolVersion
            || snapshot.BattleEpoch != CompanionAuthorityService.BattleEpoch)
        {
            TerriasLog.Warn("[Spirit] ignored incompatible companion snapshot from " + source + ".");
            return;
        }

        if (!snapshot.Accepted)
        {
            QueueReturnedCard(snapshot, null, source);
            if (SenderOwnsStatus(TerriasNetworkRuntime.LocalPlayerId(), snapshot.OwnerStatusId))
            {
                PlayerApi.ShowCaption("精灵：" + RejectionMessage(snapshot.RejectionReason));
            }
            return;
        }

        if (snapshot.ProtocolVersion != CompanionAuthorityService.ProjectionProtocolVersion
            || snapshot.BattleEpoch != CompanionAuthorityService.BattleEpoch
            || !string.Equals(snapshot.RegistryHash, SpiritIntentRegistry.RegistryHash, StringComparison.Ordinal))
        {
            TerriasLog.Warn("[Spirit] ignored incompatible companion snapshot from " + source + ".");
            return;
        }

        QueueReturnedCard(snapshot, null, source);

        var existing = SpiritStateStore.Find(snapshot.StatusId);
        if (existing != null)
        {
            if (snapshot.Generation < existing.Generation)
            {
                return;
            }

            ObserveGeneration(snapshot.OwnerPlayerId, snapshot.OwnerStatusId, snapshot.Generation);
            ApplySnapshot(existing.Spirit, snapshot, source);
            return;
        }

        var ownerExisting = SpiritStateStore.FindByOwner(snapshot.OwnerPlayerId, snapshot.OwnerStatusId);
        if (ownerExisting != null && snapshot.Generation < ownerExisting.Generation)
        {
            TerriasLog.Debug("[Spirit] ignored stale owner generation from " + source + ": incoming="
                + snapshot.Generation + ", active=" + ownerExisting.Generation + ".");
            return;
        }

        if (ownerExisting != null)
        {
            SpiritStateStore.Withdraw(ownerExisting.StatusId, source + ".OwnerGenerationReplace");
        }

        ObserveGeneration(snapshot.OwnerPlayerId, snapshot.OwnerStatusId, snapshot.Generation);
        Spawn(
            snapshot.CapturedEnemy,
            snapshot.OwnerStatusId,
            snapshot.OwnerPlayerId,
            snapshot.StatusId,
            source,
            snapshot.ExchangeCount,
            snapshot.Generation,
            snapshot);
    }

    public static bool CanSummon(IDataConfig? card, IStatusManager? owner, out string reason)
    {
        return CanSummon(card, owner, out reason, out _);
    }

    private static bool CanSummon(IDataConfig? card, IStatusManager? owner, out string reason, out CapturedEnemySnapshot? snapshot)
    {
        var started = TerriasPerformanceCounters.Timestamp();
        var result = CanSummonCore(card, owner, out reason, out snapshot);
        TerriasPerformanceCounters.RecordHotspot(
            "Spirit.Summon.CanSummon",
            started,
            "owner=" + (owner?.InstanceId ?? "<none>")
            + ", allowed=" + result
            + (reason.Length == 0 ? "" : ", reason=" + reason),
            logFirstSample: true);
        return result;
    }

    private static bool CanSummonCore(IDataConfig? card, IStatusManager? owner, out string reason, out CapturedEnemySnapshot? snapshot)
    {
        snapshot = SpiritCardFactory.Read(card);
        if (owner == null || snapshot == null)
        {
            reason = "召唤信息已经失效。";
            return false;
        }

        if (FightManager.Instance == null || FightManager.Instance.fightType == FightType.None)
        {
            reason = "只能在战斗中召唤。";
            return false;
        }

        if (!HasIdle(snapshot.IdlePath))
        {
            reason = "来源动画暂时不可用。";
            return false;
        }

        var ownerPlayerId = CompanionOwnershipService.ResolveOwnerPlayerId(owner.InstanceId);
        if (ProjectionStateStore.HasForOwner(ownerPlayerId, owner.InstanceId))
        {
            reason = "投影位置已被占用。";
            return false;
        }

        reason = "";
        return true;
    }

    private static bool TrySummonLocal(
        CapturedEnemySnapshot snapshot,
        string ownerStatusId,
        string source,
        bool broadcast,
        string token = "",
        string preferredOwnerPlayerId = "",
        int exchangeCount = 0,
        SpiritCardBattleState? incomingBattleState = null,
        ScriptExecutor? preferredExecutor = null)
    {
        var ownerPlayerId = CompanionOwnershipService.ResolveOwnerPlayerId(ownerStatusId, preferredOwnerPlayerId);
        if (ProjectionStateStore.HasForOwner(ownerPlayerId, ownerStatusId))
        {
            BroadcastRejection(snapshot, ownerStatusId, token, exchangeCount, incomingBattleState, "position-occupied", broadcast, source, preferredExecutor);
            return false;
        }

        var outgoing = SpiritStateStore.FindByOwner(ownerPlayerId, ownerStatusId);
        var generation = NextGeneration(ownerPlayerId, ownerStatusId);
        var statusId = SpiritStateStore.NextStatusId();
        var spawned = Spawn(snapshot, ownerStatusId, ownerPlayerId, statusId, source, exchangeCount, generation, null, incomingBattleState);
        if (!spawned)
        {
            BroadcastRejection(snapshot, ownerStatusId, token, exchangeCount, incomingBattleState, "spawn-failed", broadcast, source, preferredExecutor);
            return false;
        }

        var spirit = SpiritStateStore.Find(statusId)?.Spirit;
        if (spirit == null)
        {
            SpiritStateStore.Withdraw(statusId, source + ".MissingSpawnStateRollback");
            BroadcastRejection(snapshot, ownerStatusId, token, exchangeCount, incomingBattleState, "spawn-state-missing", broadcast, source, preferredExecutor);
            return false;
        }

        var networkState = BuildSnapshot(spirit);
        networkState.Token = string.IsNullOrWhiteSpace(token) ? Guid.NewGuid().ToString("N") : token;
        if (outgoing != null)
        {
            var returnedBattleState = SpiritCardBattleState.From(CompanionBattleStateStore.Find(outgoing.StatusId));
            networkState.ReplacedStatusId = outgoing.StatusId;
            networkState.ReturnedCard = outgoing.Snapshot;
            networkState.ReturnedExchangeCount = Math.Min(MaxExchangeCount, outgoing.ExchangeCount + 1);
            networkState.ReturnedTurnIndex = returnedBattleState.TurnIndex;
            networkState.ReturnedReadyOnTurn = returnedBattleState.ReadyOnTurn;
            networkState.CardGrantEventId = networkState.Token + ":return";
            SpiritStateStore.Withdraw(outgoing.StatusId, source + ".ExchangeOut");
            QueueReturnedCard(networkState, preferredExecutor, source);
        }

        if (broadcast)
        {
            Broadcast(networkState, source);
        }

        return true;
    }

    private static bool Spawn(
        CapturedEnemySnapshot snapshot,
        string ownerStatusId,
        string ownerPlayerId,
        string statusId,
        string source,
        int exchangeCount,
        int generation,
        SpiritCompanionSnapshot? networkState,
        SpiritCardBattleState? initialBattleState = null)
    {
        var started = TerriasPerformanceCounters.Timestamp();
        var succeeded = false;
        GameObject? root = null;
        try
        {
            var prefab = TerriasResourceCache.Load<GameObject>("Model/player", true, "spirit");
            if (prefab == null)
            {
                PlayerApi.ShowCaption("精灵：战斗模型加载失败。");
                return false;
            }

            root = UnityEngine.Object.Instantiate(prefab);
            var owner = FightManager.Instance?.statuses?.TryGetValue(ownerStatusId, out var ownerStatus) == true
                ? ownerStatus
                : null;
            CompanionSceneApi.MoveToOwnerScene(
                root,
                owner?.transform?.gameObject,
                source + ".SpiritSpawn");
            var spirit = root.AddComponent<SpiritOtherObj>();
            var profileResolution = SpiritIntentRegistry.ResolveProfile(snapshot.ProfileKey);
            var profile = profileResolution.Profile;
            var profileMessage = "[SpiritProfile] summon resolve: raw=" + snapshot.ProfileKey
                + ", matched=" + profileResolution.MatchedProfileKey
                + ", kind=" + profileResolution.MatchKind
                + ", pveAttack=" + profile.PveAttackTendency.Count
                + ", pveDefense=" + profile.PveDefenseTendency.Count
                + ", fallbackAttack=" + profile.FallbackAttackTendency.Count
                + ", fallbackDefense=" + profile.FallbackDefenseTendency.Count
                + ", registry=" + SpiritIntentRegistry.RegistryHash
                + ", source=" + source;
            if (profileResolution.UsedGlobalFallback)
            {
                TerriasLog.WarnOnce(
                    "spirit-summon-global:" + profileResolution.RawEnemyId + "#" + profileResolution.RawVariantId,
                    profileMessage);
            }
            else
            {
                TerriasLog.InfoAlways(profileMessage);
            }
            var stats = networkState == null
                ? CompanionStatsService.SpiritStats(profile)
                : new CompanionStats(1, Math.Max(1, networkState.MaxMagic), Math.Max(1, networkState.Attack), Math.Max(1, networkState.Armor));
            if (networkState != null)
            {
                stats.SetCurrentMagic(networkState.CurrentMagic);
            }

            if (!spirit.InitSpirit(snapshot, ownerStatusId, -1, stats, ownerPlayerId, statusId))
            {
                UnityEngine.Object.Destroy(root);
                PlayerApi.ShowCaption("精灵：初始化失败。");
                return false;
            }

            SpiritStateStore.Register(new SpiritState(
                snapshot,
                ownerStatusId,
                spirit.OwnerPlayerId,
                spirit,
                -1,
                exchangeCount,
                generation));
            spirit.Status.UpdateStatus(true);
            if (networkState == null)
            {
                var state = CompanionBattleStateStore.Find(spirit.InstanceId);
                state?.ApplyReadyOnTurn(initialBattleState?.ReadyOnTurn);
                state?.ApplyRemoteProgress(initialBattleState?.TurnIndex ?? 0, 0);
                spirit.Activate(source);
                PlayerApi.ShowCaption("精灵：【" + snapshot.DisplayName + "】加入战斗。");
            }
            else
            {
                ApplySnapshot(spirit, networkState, source);
            }
            succeeded = true;
            root = null;
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[Spirit] summon failed from " + source, ex);
            PlayerApi.ShowCaption("精灵：召唤失败。");
            return false;
        }
        finally
        {
            if (!succeeded)
            {
                FightManager.Instance?.statuses?.Remove(statusId);
                FightManager.Instance?.statusData?.Remove(statusId);
                FightManager.Instance?.ActionQueue?.RemoveAll(item => item == null || item.InstanceId == statusId);
                CompanionBattleStateStore.Remove(statusId);
                if (root != null)
                {
                    UnityEngine.Object.Destroy(root);
                }
            }
            TerriasPerformanceCounters.RecordHotspot(
                "Spirit.Summon.Spawn",
                started,
                "enemy=" + (snapshot?.EnemyId ?? "<none>")
                + ", network=" + (networkState != null)
                + ", success=" + succeeded
                + ", source=" + source,
                logFirstSample: true);
        }
    }

    public static DataConfig CreateSpiritDataConfig(CapturedEnemySnapshot snapshot, CompanionStats stats)
    {
        var handle = AuraGameDataHostApi.ResolveHandle(DataType.Enemy, snapshot.EnemyId)
            ?? throw new InvalidOperationException("Spirit enemy definition is not registered: " + snapshot.EnemyId);
        var result = AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest
        {
            Definition = handle,
            DataOverrides = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Name"] = snapshot.DisplayName,
                ["Animation"] = snapshot.AnimationPath,
                ["Attack"] = stats.Attack.ToString(),
                ["Defend"] = "0",
                ["Hp"] = "1",
                ["ActionCount"] = "1",
                ["CardList"] = string.Join(",", new[]
                {
                    TerriasIds.ProjectionActionStaffTapCardId,
                    TerriasIds.ProjectionActionShieldBlessingCardId,
                    TerriasIds.ProjectionActionStaffComboCardId,
                    TerriasIds.ProjectionActionMagicInterferenceCardId,
                    TerriasIds.ProjectionActionYouAreEnhancedCardId,
                    TerriasIds.ProjectionActionChargeCardId,
                    TerriasIds.ProjectionActionHolyHealCardId
                })
            }
        });
        return result.Instance as DataConfig
            ?? throw new InvalidOperationException("Spirit enemy materialization failed: " + result.Message);
    }

    public static void RegisterFightState(SpiritOtherObj spirit)
    {
        var status = spirit.Status as StatusManager;
        var manager = FightManager.Instance;
        if (status == null || manager == null)
        {
            return;
        }

        manager.statuses[spirit.InstanceId] = status;
        if (manager.netIdentity != null && manager.isServer)
        {
            manager.statusData[spirit.InstanceId] = new StatusDataTransfer(status);
        }

        ProjectionTurnCoordinator.RegisterCompanion(spirit, "SpiritSummonService.RegisterFightState");
    }

    public static void BroadcastRuntimeState(SpiritOtherObj spirit, string source)
    {
        if (spirit == null || !TerriasNetworkRuntime.IsMultiplayerSession() || !CompanionAuthorityService.IsAuthoritative())
        {
            return;
        }

        Broadcast(BuildSnapshot(spirit), "SpiritRuntime." + source);
    }

    private static SpiritCompanionSnapshot BuildSnapshot(SpiritOtherObj spirit)
    {
        var state = CompanionBattleStateStore.Find(spirit.InstanceId);
        var spiritState = SpiritStateStore.Find(spirit.InstanceId);
        return new SpiritCompanionSnapshot
        {
            ProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
            BattleEpoch = CompanionAuthorityService.BattleEpoch,
            RegistryHash = SpiritIntentRegistry.RegistryHash,
            Revision = state?.Revision ?? 0,
            Generation = spiritState?.Generation ?? 1,
            ExchangeCount = spiritState?.ExchangeCount ?? 0,
            Accepted = true,
            CapturedEnemy = spirit.Snapshot,
            OwnerStatusId = spirit.OwnerStatusId,
            OwnerPlayerId = spirit.OwnerPlayerId,
            StatusId = spirit.InstanceId,
            Attack = spirit.Attack,
            Armor = state?.Stats.Armor ?? 1,
            MaxMagic = state?.Stats.MaxMagic ?? 1,
            CurrentMagic = state?.Stats.CurrentMagic ?? 0,
            TurnIndex = state?.TurnIndex ?? 0,
            ReadyOnTurn = state == null
                ? new Dictionary<string, int>()
                : state.ReadyOnTurnSnapshot().ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
            Threat = CompanionThreatService.Export(spirit.InstanceId),
            IntentPlan = state?.CurrentPlan
        };
    }

    private static void ApplySnapshot(SpiritOtherObj spirit, SpiritCompanionSnapshot snapshot, string source)
    {
        var state = CompanionBattleStateStore.Find(spirit.InstanceId);
        if (state == null || snapshot.Revision < state.Revision)
        {
            return;
        }

        state.Stats.SetCurrentMagic(snapshot.CurrentMagic);
        state.ApplyReadyOnTurn(snapshot.ReadyOnTurn);
        state.ApplyRemoteProgress(snapshot.TurnIndex, snapshot.Revision);
        CompanionThreatService.ApplyAuthoritative(snapshot.Threat);
        spirit.ActivateAfterHydration(snapshot.IntentPlan, source);
    }

    private static string ValidateNetworkRequest(
        CapturedEnemySnapshot snapshot,
        string ownerStatusId,
        int exchangeCount,
        SpiritCardBattleState battleState,
        TerriasRpcSender sender,
        int protocolVersion,
        int battleEpoch,
        string registryHash)
    {
        if (protocolVersion != CompanionAuthorityService.ProjectionProtocolVersion)
        {
            return "protocol-mismatch";
        }
        if (battleEpoch != CompanionAuthorityService.BattleEpoch)
        {
            return "battle-epoch-mismatch";
        }
        if (!string.Equals(registryHash, SpiritIntentRegistry.RegistryHash, StringComparison.Ordinal))
        {
            return "registry-mismatch";
        }
        if (!sender.IsAvailable || !sender.IsLobbyMember)
        {
            return "sender-invalid";
        }
        if (!SenderOwnsStatus(sender.PlayerId, ownerStatusId))
        {
            return "owner-mismatch";
        }
        if (exchangeCount < 0 || exchangeCount > MaxExchangeCount)
        {
            return "exchange-count-invalid";
        }
        if (!ValidBattleState(battleState))
        {
            return "intent-state-invalid";
        }
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.EnemyId) || !HasIdle(snapshot.IdlePath))
        {
            return "snapshot-invalid";
        }
        return "";
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

    private static bool ClaimToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        lock (NetworkSync)
        {
            return ResolvedTokens.Add(token);
        }
    }

    private static void BroadcastRejection(
        CapturedEnemySnapshot snapshot,
        string ownerStatusId,
        string token,
        int exchangeCount,
        SpiritCardBattleState? battleState,
        string reason,
        bool broadcast,
        string source,
        ScriptExecutor? preferredExecutor)
    {
        var rejection = CreateRejection(snapshot, ownerStatusId, token, exchangeCount, battleState, reason);
        if (broadcast)
        {
            Broadcast(rejection, source + ".Reject");
        }
        else
        {
            QueueReturnedCard(rejection, preferredExecutor, source + ".Reject");
            PlayerApi.ShowCaption("精灵：" + RejectionMessage(reason));
        }
    }

    private static SpiritCompanionSnapshot CreateRejection(
        CapturedEnemySnapshot snapshot,
        string ownerStatusId,
        string token,
        int exchangeCount,
        SpiritCardBattleState? battleState,
        string reason)
    {
        var normalizedToken = string.IsNullOrWhiteSpace(token) ? Guid.NewGuid().ToString("N") : token;
        var returnBattleState = ValidBattleState(battleState)
            ? battleState!
            : new SpiritCardBattleState();
        return new SpiritCompanionSnapshot
        {
            ProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
            BattleEpoch = CompanionAuthorityService.BattleEpoch,
            Token = normalizedToken,
            CapturedEnemy = snapshot ?? new CapturedEnemySnapshot(),
            OwnerStatusId = ownerStatusId ?? "",
            Accepted = false,
            ReturnedCard = snapshot,
            ReturnedExchangeCount = Math.Max(0, Math.Min(MaxExchangeCount, exchangeCount)),
            ReturnedTurnIndex = returnBattleState.TurnIndex,
            ReturnedReadyOnTurn = new Dictionary<string, int>(returnBattleState.ReadyOnTurn),
            CardGrantEventId = normalizedToken + ":refund",
            RejectionReason = reason ?? ""
        };
    }

    private static void QueueReturnedCard(
        SpiritCompanionSnapshot snapshot,
        ScriptExecutor? preferredExecutor,
        string source)
    {
        if (snapshot.ReturnedCard == null
            || string.IsNullOrWhiteSpace(snapshot.CardGrantEventId)
            || !IsLocalOwner(snapshot.OwnerStatusId))
        {
            return;
        }

        var pending = new PendingCardGrant(
            snapshot.CardGrantEventId,
            snapshot.OwnerStatusId,
            snapshot.ReturnedCard,
            snapshot.ReturnedExchangeCount,
            new SpiritCardBattleState
            {
                TurnIndex = snapshot.ReturnedTurnIndex,
                ReadyOnTurn = snapshot.ReturnedReadyOnTurn ?? new Dictionary<string, int>()
            });
        lock (NetworkSync)
        {
            if (GrantedCardEvents.Contains(pending.EventId))
            {
                return;
            }

            PendingCardGrants[pending.EventId] = pending;
        }

        TryDeliverCard(pending, preferredExecutor, source);
    }

    private static bool TryDeliverCard(PendingCardGrant pending, ScriptExecutor? preferredExecutor, string source)
    {
        lock (NetworkSync)
        {
            if (GrantedCardEvents.Contains(pending.EventId))
            {
                PendingCardGrants.Remove(pending.EventId);
                return true;
            }
        }

        var localStatus = FightPlayer.Instance?.Status;
        if (localStatus == null || !IsLocalOwner(pending.OwnerStatusId))
        {
            return false;
        }

        var executor = preferredExecutor;
        if (executor?.Self == null
            || !string.Equals(executor.Self.InstanceId, pending.OwnerStatusId, StringComparison.Ordinal))
        {
            executor = localStatus.MirrorSc as ScriptExecutor;
        }
        if (executor == null)
        {
            TerriasLog.Debug("[Spirit] returned-card delivery deferred from " + source + ": executor unavailable.");
            return false;
        }

        executor.Self = localStatus;
        var result = SpiritCardFactory.GrantReturnedToHand(
            executor,
            pending.Card,
            pending.ExchangeCount,
            pending.BattleState,
            "spirit-exchange:" + source);
        if (!result.Success)
        {
            TerriasLog.Warn("[Spirit] returned-card delivery deferred from " + source
                + ": step=" + result.FailureStep + ", reason=" + result.FailureReason + ".");
            return false;
        }

        lock (NetworkSync)
        {
            GrantedCardEvents.Add(pending.EventId);
            PendingCardGrants.Remove(pending.EventId);
        }
        PlayerApi.ShowCaption("\u7cbe\u7075\uff1a\u3010" + pending.Card.DisplayName
            + "\u3011\u5df2\u8fd4\u56de\u624b\u724c\uff0c\u8017\u8d39\u63d0\u5347\u81f3"
            + pending.ExchangeCount + "\u3002");
        TerriasPerformanceCounters.Record("Spirit.Card.ReturnedToHand");
        return true;
    }

    private static bool IsLocalOwner(string ownerStatusId)
    {
        return string.Equals(FightPlayer.Instance?.Status?.InstanceId, ownerStatusId, StringComparison.Ordinal)
            || SenderOwnsStatus(TerriasNetworkRuntime.LocalPlayerId(), ownerStatusId);
    }

    private static bool ValidBattleState(SpiritCardBattleState? battleState)
    {
        if (battleState == null
            || battleState.TurnIndex < 0
            || battleState.TurnIndex > MaxIntentTurnIndex
            || battleState.ReadyOnTurn == null
            || battleState.ReadyOnTurn.Count > MaxIntentStateEntries)
        {
            return false;
        }

        return battleState.ReadyOnTurn.All(entry =>
            !string.IsNullOrWhiteSpace(entry.Key)
            && entry.Key.Length <= 160
            && entry.Value >= 0
            && entry.Value <= MaxIntentTurnIndex);
    }

    private static int NextGeneration(string ownerPlayerId, string ownerStatusId)
    {
        var key = OwnerKey(ownerPlayerId, ownerStatusId);
        lock (NetworkSync)
        {
            var next = OwnerGenerations.TryGetValue(key, out var current) ? current + 1 : 1;
            OwnerGenerations[key] = next;
            return next;
        }
    }

    private static void ObserveGeneration(string ownerPlayerId, string ownerStatusId, int generation)
    {
        var key = OwnerKey(ownerPlayerId, ownerStatusId);
        lock (NetworkSync)
        {
            var normalized = Math.Max(1, generation);
            if (!OwnerGenerations.TryGetValue(key, out var current) || normalized > current)
            {
                OwnerGenerations[key] = normalized;
            }
        }
    }

    private static string OwnerKey(string ownerPlayerId, string ownerStatusId)
    {
        return !string.IsNullOrWhiteSpace(ownerPlayerId)
            ? "player:" + ownerPlayerId.Trim()
            : "status:" + (ownerStatusId ?? "").Trim();
    }

    private static bool Broadcast(SpiritCompanionSnapshot snapshot, string source)
    {
        return TerriasNetworkRuntime.Send(new RpcSpiritCompanionState(snapshot), source);
    }

    private static string RejectionMessage(string reason)
    {
        return (reason ?? "") switch
        {
            "position-occupied" => "投影位置已被占用。",
            "protocol-mismatch" => "召唤协议版本不一致。",
            "battle-epoch-mismatch" => "当前战斗状态已失效，请重新使用。",
            "registry-mismatch" => "精灵行动配置不一致。",
            "intent-state-invalid" => "精灵行动冷却记录无效。",
            "sender-invalid" => "无法确认操作玩家。",
            "owner-mismatch" => "当前角色不属于该玩家。",
            "snapshot-invalid" => "捕获记录或来源动画已经失效。",
            _ => "召唤失败，请稍后重试。"
        };
    }

    private static bool HasIdle(string idlePath)
    {
        var started = TerriasPerformanceCounters.Timestamp();
        var found = false;
        try
        {
            found = !string.IsNullOrWhiteSpace(idlePath)
                && TerriasResourceCache.LoadAll<Sprite>(idlePath, "spirit-idle")?.Length > 0;
            return found;
        }
        catch
        {
            return false;
        }
        finally
        {
            TerriasPerformanceCounters.RecordHotspot(
                "Spirit.Summon.IdleProbe",
                started,
                "found=" + found + ", path=" + (idlePath ?? ""),
                logFirstSample: true);
        }
    }

    private sealed class PendingCardGrant
    {
        public PendingCardGrant(
            string eventId,
            string ownerStatusId,
            CapturedEnemySnapshot card,
            int exchangeCount,
            SpiritCardBattleState battleState)
        {
            EventId = eventId ?? "";
            OwnerStatusId = ownerStatusId ?? "";
            Card = card ?? new CapturedEnemySnapshot();
            ExchangeCount = Math.Max(0, Math.Min(MaxExchangeCount, exchangeCount));
            BattleState = battleState ?? new SpiritCardBattleState();
        }

        public string EventId { get; }

        public string OwnerStatusId { get; }

        public CapturedEnemySnapshot Card { get; }

        public int ExchangeCount { get; }

        public SpiritCardBattleState BattleState { get; }
    }
}
