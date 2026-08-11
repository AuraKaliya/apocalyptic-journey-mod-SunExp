using System;
using System.Linq;
using System.Collections.Generic;
using AuraShared.Core;
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
    private const int MaximumRetryAttempts = 90;
    private static readonly Dictionary<string, PolymorphVisualSnapshot> PendingSnapshots =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> RetryAttempts = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> LatestVersions = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> OutgoingVersions = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ScheduledOwners = new(StringComparer.Ordinal);
    private static int lifecycleGeneration;

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
            Version = NextOutgoingVersion(state),
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
            Version = NextOutgoingVersion(state),
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

        var normalized = Clone(snapshot);
        var hasLatestVersion = LatestVersions.TryGetValue(normalized.OwnerStatusId, out var currentVersion);
        if (hasLatestVersion && currentVersion > normalized.Version)
        {
            TerriasLog.Debug("[PolymorphSync] stale visual snapshot ignored; owner="
                             + normalized.OwnerStatusId + "; incoming=" + normalized.Version
                             + "; current=" + currentVersion + ".");
            return;
        }

        if (hasLatestVersion && currentVersion == normalized.Version)
        {
            if (PendingSnapshots.ContainsKey(normalized.OwnerStatusId))
            {
                TryApplyPending(normalized.OwnerStatusId, source ?? "");
            }

            return;
        }

        LatestVersions[normalized.OwnerStatusId] = normalized.Version;
        PendingSnapshots[normalized.OwnerStatusId] = normalized;
        RetryAttempts[normalized.OwnerStatusId] = 0;
        TryApplyPending(normalized.OwnerStatusId, source ?? "");
    }

    public static void ClearPending(string source)
    {
        lifecycleGeneration++;
        PendingSnapshots.Clear();
        RetryAttempts.Clear();
        LatestVersions.Clear();
        OutgoingVersions.Clear();
        ScheduledOwners.Clear();
        TerriasLog.Debug("[PolymorphSync] pending visual snapshots cleared from " + source + ".");
    }

    private static void TryApplyPending(string ownerStatusId, string source)
    {
        if (!PendingSnapshots.TryGetValue(ownerStatusId, out var snapshot))
        {
            return;
        }

        var careerId = snapshot.Active ? snapshot.CareerId : snapshot.OriginalCareerId;
        if (string.IsNullOrWhiteSpace(careerId))
        {
            careerId = snapshot.CareerId;
        }

        try
        {
            if (ApplyCareerToFightState(snapshot.OwnerStatusId, careerId))
            {
                PendingSnapshots.Remove(ownerStatusId);
                RetryAttempts.Remove(ownerStatusId);
                ScheduledOwners.Remove(ownerStatusId);
                TerriasLog.Info("[PolymorphSync] visual snapshot applied from "
                    + source
                    + "; owner="
                    + snapshot.OwnerStatusId
                    + "; career="
                    + careerId
                    + "; active="
                    + snapshot.Active
                    + ".");
                return;
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[PolymorphSync] visual snapshot apply deferred from "
                            + source + ": " + ex.Message);
        }

        ScheduleRetry(snapshot, source);
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

    private static void ScheduleRetry(PolymorphVisualSnapshot snapshot, string source)
    {
        var ownerStatusId = snapshot.OwnerStatusId;
        if (ScheduledOwners.Contains(ownerStatusId))
        {
            return;
        }

        var attempt = RetryAttempts.TryGetValue(ownerStatusId, out var currentAttempt)
            ? currentAttempt + 1
            : 1;
        RetryAttempts[ownerStatusId] = attempt;
        if (attempt > MaximumRetryAttempts)
        {
            TerriasLog.Warn("[PolymorphSync] visual snapshot remains pending after bounded retries; owner="
                            + ownerStatusId + ", version=" + snapshot.Version + ".");
            return;
        }

        ScheduledOwners.Add(ownerStatusId);
        var expectedVersion = snapshot.Version;
        var expectedGeneration = lifecycleGeneration;
        AuraSharedFrameScheduler.RunAfterFramesBudgeted(new AuraSharedFrameEnqueueRequest
        {
            OwnerId = TerriasIds.ModId,
            Source = "PolymorphSync.Reconcile:" + ownerStatusId,
            Action = () =>
            {
                ScheduledOwners.Remove(ownerStatusId);
                if (expectedGeneration != lifecycleGeneration
                    || !PendingSnapshots.TryGetValue(ownerStatusId, out var latest))
                {
                    return;
                }

                if (latest.Version != expectedVersion)
                {
                    TryApplyPending(ownerStatusId, source + ":superseded-retry");
                    return;
                }

                TryApplyPending(ownerStatusId, source + ":retry-" + attempt);
            }
        }, Math.Min(12, 1 + attempt / 8));
    }

    private static bool ApplyCareerToFightState(string ownerStatusId, string careerId)
    {
        var career = CreateCareerConfig(careerId);
        if (career == null)
        {
            return false;
        }

        var fightManager = FightManager.Instance;
        if (fightManager == null)
        {
            return false;
        }

        var applied = false;
        var roleData = fightManager?.roleQueue?.FirstOrDefault(role =>
            string.Equals(role?.InstanceId, ownerStatusId, StringComparison.Ordinal));
        if (roleData != null)
        {
            roleData.career = career;
            applied = true;
        }

        if (string.Equals(ownerStatusId, PlayerApi.LocalPlayerStatusId(), StringComparison.Ordinal)
            && FightPlayer.Instance?.Status != null)
        {
            var localCareer = CareerApi.IsCurrent(careerId)
                ? RoleTable.Instance?.Career
                : career;
            applied |= CareerApi.CommitLocalCareer(
                FightPlayer.Instance.Status,
                localCareer,
                "PolymorphNetworkSync.ApplyCareerToFightState");
        }

        if (fightManager?.statuses != null
            && fightManager.statuses.TryGetValue(ownerStatusId, out var status)
            && status != null)
        {
            status.ResetAnimator(false);
            applied = true;
        }

        return applied;
    }

    private static PolymorphVisualSnapshot Clone(PolymorphVisualSnapshot snapshot)
    {
        return new PolymorphVisualSnapshot
        {
            OwnerStatusId = snapshot.OwnerStatusId?.Trim() ?? "",
            CareerId = snapshot.CareerId?.Trim() ?? "",
            OriginalCareerId = snapshot.OriginalCareerId?.Trim() ?? "",
            Active = snapshot.Active,
            Version = snapshot.Version,
            Source = snapshot.Source ?? ""
        };
    }

    private static int NextOutgoingVersion(PolymorphState state)
    {
        var owner = state?.OwnerStatusId ?? "";
        var current = OutgoingVersions.TryGetValue(owner, out var existing) ? existing : 0;
        var next = Math.Max(current + 1, Math.Max(1, state?.Version ?? 0));
        OutgoingVersions[owner] = next;
        return next;
    }

    private static DataConfig? CreateCareerConfig(string careerId)
    {
        return CareerApi.Materialize(careerId);
    }
}
