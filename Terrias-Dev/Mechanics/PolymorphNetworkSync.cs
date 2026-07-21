using System;
using System.Linq;
using AuraGameData.Shared.GameApi;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Network;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public sealed class PolymorphVisualSnapshot
{
    public string OwnerStatusId { get; set; } = "";

    public string CareerId { get; set; } = "";

    public string OriginalCareerId { get; set; } = "";

    public bool Active { get; set; }

    public int Version { get; set; }

    public string Source { get; set; } = "";
}

public static class PolymorphNetworkSync
{
    public static void BroadcastEnter(PolymorphState state, string source)
    {
        var safeSource = source ?? "";
        if (state == null)
        {
            return;
        }

        Broadcast(new PolymorphVisualSnapshot
        {
            OwnerStatusId = state.OwnerStatusId,
            CareerId = state.RoleId,
            OriginalCareerId = state.OriginalCareerId,
            Active = true,
            Version = state.Version,
            Source = safeSource
        }, safeSource);
    }

    public static void BroadcastRestore(PolymorphState state, string source)
    {
        var safeSource = source ?? "";
        if (state == null)
        {
            return;
        }

        Broadcast(new PolymorphVisualSnapshot
        {
            OwnerStatusId = state.OwnerStatusId,
            CareerId = state.OriginalCareerId,
            OriginalCareerId = state.OriginalCareerId,
            Active = false,
            Version = state.Version,
            Source = safeSource
        }, safeSource);
    }

    public static void ApplyVisualSnapshot(PolymorphVisualSnapshot? snapshot, string source)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.OwnerStatusId))
        {
            return;
        }

        var careerId = snapshot.Active ? snapshot.CareerId : snapshot.OriginalCareerId;
        if (string.IsNullOrWhiteSpace(careerId))
        {
            careerId = snapshot.CareerId;
        }

        if (string.IsNullOrWhiteSpace(careerId))
        {
            return;
        }

        try
        {
            ApplyCareerToFightState(snapshot.OwnerStatusId, careerId);
            TerriasLog.Info("[PolymorphSync] visual snapshot applied from "
                + source
                + "; owner="
                + snapshot.OwnerStatusId
                + "; career="
                + careerId
                + "; active="
                + snapshot.Active
                + ".");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[PolymorphSync] visual snapshot failed from "
                + source
                + ": "
                + ex.Message);
        }
    }

    public static void BroadcastNativeCareerChange(string ownerStatusId, string careerId, string source)
    {
        if (string.IsNullOrWhiteSpace(ownerStatusId) || string.IsNullOrWhiteSpace(careerId))
        {
            return;
        }

        try
        {
            FightManager.Instance?.CmdChangeCareer(careerId, ownerStatusId);
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[PolymorphSync] native career change skipped from "
                + source
                + ": "
                + ex.Message);
        }
    }

    private static void Broadcast(PolymorphVisualSnapshot snapshot, string source)
    {
        ApplyVisualSnapshot(snapshot, "local:" + source);
        if (!TerriasNetworkRuntime.IsMultiplayerSession())
        {
            return;
        }

        TerriasNetworkRuntime.Send(new RpcPolymorphVisualState(snapshot), source);
    }

    private static void ApplyCareerToFightState(string ownerStatusId, string careerId)
    {
        var career = CreateCareerConfig(careerId);
        if (career == null)
        {
            return;
        }

        var fightManager = FightManager.Instance;
        var roleData = fightManager?.roleQueue?.FirstOrDefault(role =>
            string.Equals(role?.InstanceId, ownerStatusId, StringComparison.Ordinal));
        if (roleData != null)
        {
            roleData.career = career;
        }

        if (string.Equals(ownerStatusId, PlayerApi.LocalPlayerStatusId(), StringComparison.Ordinal)
            && RoleTable.Instance != null)
        {
            RoleTable.Instance.Career = career;
        }

        if (fightManager?.statuses != null
            && fightManager.statuses.TryGetValue(ownerStatusId, out var status)
            && status != null)
        {
            status.ResetAnimator(false);
        }
    }

    private static DataConfig? CreateCareerConfig(string careerId)
    {
        try
        {
            var type = AuraGameDataHostApi.Resolve(DataType.Enemy, careerId) != null
                ? DataType.Enemy
                : DataType.Career;
            var handle = AuraGameDataHostApi.ResolveHandle(type, careerId);
            return handle == null
                ? null
                : AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest { Definition = handle }).Instance as DataConfig;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[PolymorphSync] career config unavailable: " + careerId + "; " + ex.Message);
            return null;
        }
    }
}
