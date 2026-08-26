using System;
using System.Collections.Generic;
using System.Threading;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Network;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public static class SpiritCaptureService
{
    private static readonly object NetworkSync = new();
    private static readonly HashSet<string> ResolvedTokens = new(StringComparer.Ordinal);
    private static int attemptSequence;

    public static void ResetBattleSynchronization()
    {
        lock (NetworkSync)
        {
            ResolvedTokens.Clear();
        }
        Interlocked.Exchange(ref attemptSequence, 0);
    }

    public static bool TryCapture(ScriptExecutor self)
    {
        if (self?.Self == null)
        {
            return false;
        }

        var target = ExecutorApi.PrimaryTarget(self);
        var inspected = EnemyCatalogApi.Inspect(target, "battle:" + CompanionAuthorityService.BattleEpoch);
        if (!inspected.Eligible || inspected.Snapshot == null)
        {
            PlayerApi.ShowCaption(TerriasTextCatalog.Format("caption.spirit_capture.reason",
                "reason", TerriasTextCatalog.ResolveLegacy(inspected.Reason)));
            return false;
        }

        if (TerriasNetworkRuntime.IsMultiplayerSession() && TerriasNetworkRuntime.IsClientOnly())
        {
            var token = Guid.NewGuid().ToString("N");
            TerriasNetworkRuntime.Send(
                new RpcSpiritCaptureRequest(self.Self.InstanceId, target!.InstanceId, token),
                "SpiritCaptureService.TryCapture");
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.spirit_capture.waiting_host"));
            return true;
        }

        var sequence = Interlocked.Increment(ref attemptSequence);
        var cardInstance = self.dataConfig?.InstanceID ?? DictionaryUtil.Get(self.Vars, "InstanceID");
        var seed = CompanionAuthorityService.BattleEpoch + ":" + inspected.Snapshot.InstanceId + ":" + cardInstance + ":" + sequence;
        return ResolveLocal(self, target!, inspected.Snapshot, seed);
    }

    public static void ResolveNetworkCapture(
        string ownerStatusId,
        string targetStatusId,
        string token,
        TerriasRpcSender sender,
        int protocolVersion,
        int battleEpoch)
    {
        if (!ClaimToken(token))
        {
            return;
        }

        var state = new SpiritCaptureNetworkState
        {
            ProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
            BattleEpoch = CompanionAuthorityService.BattleEpoch,
            Token = token ?? "",
            OwnerStatusId = ownerStatusId ?? "",
            TargetStatusId = targetStatusId ?? "",
            Resolved = true
        };

        var normalizedOwnerStatusId = ownerStatusId ?? "";
        var normalizedTargetStatusId = targetStatusId ?? "";
        var rejection = ValidateNetworkRequest(normalizedOwnerStatusId, sender, protocolVersion, battleEpoch);
        var target = StatusById(normalizedTargetStatusId);
        var inspected = rejection.Length == 0
            ? EnemyCatalogApi.Inspect(target, "network-battle:" + CompanionAuthorityService.BattleEpoch, requireDictionaryVisible: false)
            : SpiritEligibilityResult.Reject(rejection);
        if (!inspected.Eligible || inspected.Snapshot == null || target == null)
        {
            state.Reason = inspected.Reason;
            Broadcast(state, "SpiritCaptureService.ResolveNetworkCapture.Reject");
            return;
        }

        var snapshot = inspected.Snapshot;
        state.CapturedEnemy = snapshot;
        state.Success = SpiritCaptureRollService.Succeeds(
            target.CurHp,
            target.MaxHp,
            CompanionAuthorityService.BattleEpoch + ":" + targetStatusId + ":" + token,
            out var chance,
            out var roll);
        state.ChanceBasisPoints = chance;
        state.RollBasisPoints = roll;
        if (state.Success)
        {
            var resolution = SpiritCaptureRegistry.ResolveProfile(snapshot.EnemyId, snapshot.VariantId);
            LogProfileResolution(snapshot, resolution, "network");
            state.Success = EnemyCaptureSettlementApi.Settle(target, snapshot, resolution.Profile, token ?? "");
            if (!state.Success)
            {
                state.Reason = "敌人离场结算失败。";
            }
        }

        Broadcast(state, "SpiritCaptureService.ResolveNetworkCapture");
    }

    public static void ApplyNetworkState(SpiritCaptureNetworkState? state, string source)
    {
        if (state == null
            || state.ProtocolVersion != CompanionAuthorityService.ProjectionProtocolVersion
            || state.BattleEpoch != CompanionAuthorityService.BattleEpoch
            || !IsLocalOwner(state.OwnerStatusId))
        {
            return;
        }

        if (!state.Success || state.CapturedEnemy == null)
        {
            var reason = string.IsNullOrWhiteSpace(state.Reason)
                ? TerriasTextCatalog.Format("caption.spirit_capture.failed_chance",
                    "chance", (state.ChanceBasisPoints / 100).ToString())
                : TerriasTextCatalog.ResolveLegacy(state.Reason);
            PlayerApi.ShowCaption(TerriasTextCatalog.Format("caption.spirit_capture.reason", "reason", reason));
            return;
        }

        var recorded = SpiritCollectionApi.RecordCapture(state.CapturedEnemy, state.Token);
        if (!recorded.Success)
        {
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.spirit_capture.archive_sync_failed"));
            TerriasLog.Warn("[SpiritCapture] network collection write failed: " + recorded.Reason);
            return;
        }

        PlayerApi.ShowCaption(TerriasTextCatalog.Format(
            recorded.AddedToParty ? "caption.spirit_capture.captured_party" : "caption.spirit_capture.captured_warehouse",
            "name", SpiritPresentationResolver.Name(state.CapturedEnemy),
            "element", SpiritElementService.DisplayName(recorded.Instance?.ElementId)));
    }

    private static bool ResolveLocal(
        ScriptExecutor self,
        IStatusManager target,
        CapturedEnemySnapshot snapshot,
        string seed)
    {
        var success = SpiritCaptureRollService.Succeeds(target.CurHp, target.MaxHp, seed, out var chance, out var roll);
        TerriasLog.Info("[SpiritCapture] roll enemy=" + snapshot.EnemyId + ", chance=" + chance + ", roll=" + roll + ", success=" + success);
        if (!success)
        {
            PlayerApi.ShowCaption(TerriasTextCatalog.Format("caption.spirit_capture.reason", "reason",
                TerriasTextCatalog.Format("caption.spirit_capture.failed_chance",
                    "chance", (chance / 100).ToString())));
            return false;
        }

        var recorded = SpiritCollectionApi.RecordCapture(snapshot, seed);
        if (!recorded.Success)
        {
            PlayerApi.ShowCaption(TerriasTextCatalog.Get("caption.spirit_capture.archive_write_failed"));
            TerriasLog.Warn("[SpiritCapture] collection write failed: " + recorded.Reason);
            return false;
        }

        var resolution = SpiritCaptureRegistry.ResolveProfile(snapshot.EnemyId, snapshot.VariantId);
        LogProfileResolution(snapshot, resolution, "local");
        var settled = EnemyCaptureSettlementApi.Settle(target, snapshot, resolution.Profile, seed);
        PlayerApi.ShowCaption(settled
            ? TerriasTextCatalog.Format(
                recorded.AddedToParty ? "caption.spirit_capture.captured_party" : "caption.spirit_capture.captured_warehouse",
                "name", SpiritPresentationResolver.Name(snapshot),
                "element", SpiritElementService.DisplayName(recorded.Instance?.ElementId))
            : TerriasTextCatalog.Get("caption.spirit_capture.settlement_fallback"));
        return true;
    }

    private static void LogProfileResolution(
        CapturedEnemySnapshot snapshot,
        SpiritProfileResolution<SpiritCaptureProfile> resolution,
        string source)
    {
        var message = "[SpiritProfile] capture resolve: raw=" + snapshot.ProfileKey
            + ", matched=" + resolution.MatchedProfileKey
            + ", kind=" + resolution.MatchKind
            + ", mode=" + resolution.Profile.ResolutionMode
            + ", source=" + source;
        if (resolution.UsedGlobalFallback)
        {
            TerriasLog.WarnOnce(
                "spirit-capture-global:" + resolution.RawEnemyId + "#" + resolution.RawVariantId,
                message);
            return;
        }

        TerriasLog.InfoAlways(message);
    }

    private static string ValidateNetworkRequest(string ownerStatusId, TerriasRpcSender sender, int protocolVersion, int battleEpoch)
    {
        if (protocolVersion != CompanionAuthorityService.ProjectionProtocolVersion)
        {
            return "捕获协议版本不一致。";
        }
        if (battleEpoch != CompanionAuthorityService.BattleEpoch)
        {
            return "当前战斗状态已失效。";
        }
        if (!sender.IsAvailable || !sender.IsLobbyMember)
        {
            return "无法确认操作玩家。";
        }
        return SenderOwnsStatus(sender.PlayerId, ownerStatusId) ? "" : "当前角色不属于该玩家。";
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

    private static bool IsLocalOwner(string ownerStatusId)
    {
        return string.Equals(FightPlayer.Instance?.Status?.InstanceId, ownerStatusId, StringComparison.Ordinal)
            || SenderOwnsStatus(TerriasNetworkRuntime.LocalPlayerId(), ownerStatusId);
    }

    private static IStatusManager? StatusById(string statusId)
    {
        return !string.IsNullOrWhiteSpace(statusId)
            && FightManager.Instance?.statuses?.TryGetValue(statusId, out var status) == true
                ? status
                : null;
    }

    private static bool ClaimToken(string token)
    {
        lock (NetworkSync)
        {
            return string.IsNullOrWhiteSpace(token) || ResolvedTokens.Add(token);
        }
    }

    private static void Broadcast(SpiritCaptureNetworkState state, string source)
    {
        TerriasNetworkRuntime.Send(new RpcSpiritCaptureState(state), source);
    }
}
