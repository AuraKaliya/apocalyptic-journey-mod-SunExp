using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Network;

namespace Terrias.Dll.Mechanics;

public static class SpiritWithdrawService
{
    private static readonly object SyncRoot = new();
    private static readonly HashSet<string> ResolvedTokens = new(StringComparer.Ordinal);

    public static void ResetBattle()
    {
        lock (SyncRoot)
        {
            ResolvedTokens.Clear();
        }
    }

    public static bool TryWithdraw(ScriptExecutor self)
    {
        var owner = self?.Self ?? FightPlayer.Instance?.Status;
        if (owner == null)
        {
            PlayerApi.ShowCaption("精灵：没有可换下的精灵。");
            return false;
        }
        var token = Guid.NewGuid().ToString("N");
        if (TerriasNetworkRuntime.IsMultiplayerSession() && TerriasNetworkRuntime.IsClientOnly())
        {
            TerriasNetworkRuntime.Send(
                new RpcSpiritWithdrawRequest(owner.InstanceId, token),
                "SpiritWithdrawService.TryWithdraw");
            return true;
        }

        ResolveNetworkWithdraw(
            owner.InstanceId,
            token,
            TerriasRpcAuthorityRuntime.CreateLocalServerSender("SpiritWithdrawService.TryWithdraw"),
            CompanionAuthorityService.ProjectionProtocolVersion,
            CompanionAuthorityService.BattleEpoch);
        return true;
    }

    public static void ResolveNetworkWithdraw(
        string ownerStatusId,
        string token,
        TerriasRpcSender sender,
        int protocolVersion,
        int battleEpoch)
    {
        lock (SyncRoot)
        {
            if (string.IsNullOrWhiteSpace(token) || !ResolvedTokens.Add(token))
            {
                return;
            }
        }
        if (protocolVersion != CompanionAuthorityService.ProjectionProtocolVersion
            || battleEpoch != CompanionAuthorityService.BattleEpoch
            || TerriasNetworkRuntime.IsMultiplayerSession()
               && (!sender.IsAvailable
                   || !sender.IsLobbyMember
                   || !SenderOwnsStatus(sender.PlayerId, ownerStatusId)))
        {
            return;
        }

        var ownerPlayerId = CompanionOwnershipService.ResolveOwnerPlayerId(ownerStatusId, sender.PlayerId);
        var state = SpiritStateStore.FindByOwner(ownerPlayerId, ownerStatusId);
        var spirit = state?.Spirit;
        var status = spirit?.Status;
        if (state == null
            || spirit == null
            || status == null
            || status.CurHp <= 0
            || status.state == IStatusManager.State.Dead)
        {
            PlayerApi.ShowCaption("精灵：没有可换下的精灵。");
            return;
        }

        var battle = CompanionBattleStateStore.Find(state.StatusId);
        var returnedState = SpiritCardBattleState.From(battle);
        returnedState.MaxHp = Math.Max(1, status.MaxHp);
        returnedState.CurrentHp = Math.Max(1, Math.Min(status.MaxHp, status.CurHp));
        returnedState.CurrentDefend = Math.Max(0, status.Defend);
        returnedState.CurrentMagic = Math.Max(0, battle?.Stats.CurrentMagic ?? 0);
        returnedState.PassiveState = battle == null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : battle.PassiveStateSnapshot()
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

        var snapshot = new SpiritCompanionSnapshot
        {
            ProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
            BattleEpoch = CompanionAuthorityService.BattleEpoch,
            RegistryHash = SpiritIntentRegistry.RegistryHash,
            TrainingRegistryHash = SpiritTrainingRegistry.RegistryHash,
            Token = token,
            CapturedEnemy = state.Snapshot,
            OwnerStatusId = state.OwnerStatusId,
            OwnerPlayerId = state.OwnerPlayerId,
            StatusId = state.StatusId,
            Generation = state.Generation,
            Accepted = true,
            ReturnedCard = state.Snapshot,
            ReturnedExchangeCount = Math.Min(999, state.ExchangeCount + 1),
            ReturnedTurnIndex = returnedState.TurnIndex,
            ReturnedReadyOnTurn = new Dictionary<string, int>(returnedState.ReadyOnTurn),
            ReturnedBattleState = returnedState,
            CardGrantEventId = token + ":withdraw",
            ReturnedCardOnly = true
        };
        SpiritStateStore.Withdraw(state.StatusId, "SpiritWithdrawService.Withdraw");
        SpiritSummonService.ApplyNetworkState(snapshot, "SpiritWithdrawService.LocalReturnedCard");
        if (TerriasNetworkRuntime.IsMultiplayerSession())
        {
            TerriasNetworkRuntime.Send(
                new RpcSpiritCompanionState(snapshot),
                "SpiritWithdrawService.ReturnedCard");
        }
    }

    private static bool SenderOwnsStatus(string playerId, string ownerStatusId)
    {
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
}
