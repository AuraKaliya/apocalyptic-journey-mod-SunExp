using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public sealed class PolymorphState
{
    public PolymorphState(
        string ownerStatusId,
        string roleId,
        string displayName,
        string originalCareerId,
        DataConfig? originalCareer,
        IReadOnlyDictionary<string, int>? originalCooldowns,
        int sessionId,
        int version)
    {
        OwnerStatusId = ownerStatusId ?? "";
        RoleId = roleId ?? "";
        DisplayName = displayName ?? "";
        OriginalCareerId = originalCareerId ?? "";
        OriginalCareer = originalCareer;
        OriginalCooldowns = PolymorphCooldownSnapshotPolicy.Normalize(originalCooldowns);
        SessionId = sessionId;
        Version = version;
    }

    public string OwnerStatusId { get; }

    public string RoleId { get; }

    public string DisplayName { get; }

    public string OriginalCareerId { get; }

    public DataConfig? OriginalCareer { get; }

    public IReadOnlyDictionary<string, int> OriginalCooldowns { get; }

    public int SessionId { get; }

    public int Version { get; }
}

public static class PolymorphStateStore
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, PolymorphState> ActiveStates = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PolymorphRoleSpec> PendingRoles = new(StringComparer.Ordinal);
    private static int version;

    public static PolymorphState? ActiveLocal()
    {
        var owner = PlayerApi.LocalPlayerStatusId();
        if (string.IsNullOrWhiteSpace(owner))
        {
            return null;
        }

        lock (SyncRoot)
        {
            return ActiveStates.TryGetValue(owner, out var state) ? state : null;
        }
    }

    public static bool IsLocalRoleSuppressed(string roleId)
    {
        var active = ActiveLocal();
        return active != null && !RoleMatches(active.RoleId, roleId);
    }

    public static bool IsRoleSuppressedFor(IStatusManager? ownerStatus, string roleId)
    {
        var active = ActiveFor(ownerStatus);
        return active != null && !RoleMatches(active.RoleId, roleId);
    }

    public static bool IsRoleActiveFor(IStatusManager? ownerStatus, string roleId)
    {
        var active = ActiveFor(ownerStatus);
        return active != null && RoleMatches(active.RoleId, roleId);
    }

    public static bool IsCurrentSession(IStatusManager? ownerStatus, int sessionId)
    {
        var active = ActiveFor(ownerStatus);
        return active != null && active.SessionId == sessionId;
    }

    public static string EffectiveCombatRoleIdFor(IStatusManager? ownerStatus)
    {
        if (ownerStatus == null)
        {
            return "";
        }

        var active = ActiveFor(ownerStatus);
        if (active != null && !string.IsNullOrWhiteSpace(active.RoleId))
        {
            return active.RoleId;
        }

        var local = FightPlayer.Instance?.Status;
        if (local != null
            && (ReferenceEquals(ownerStatus, local)
                || string.Equals(ownerStatus.InstanceId, local.InstanceId, StringComparison.Ordinal)))
        {
            var currentCareerId = PlayerApi.GetCurrentCareerId();
            if (!string.IsNullOrWhiteSpace(currentCareerId))
            {
                return currentCareerId;
            }
        }

        return StatusApi.RoleId(ownerStatus);
    }

    public static bool IsEffectiveCombatRoleFor(IStatusManager? ownerStatus, string roleId)
    {
        return ownerStatus != null && RoleMatches(EffectiveCombatRoleIdFor(ownerStatus), roleId);
    }

    public static bool IsLocalEffectiveCombatRole(string roleId)
    {
        return IsEffectiveCombatRoleFor(FightPlayer.Instance?.Status, roleId);
    }

    public static PolymorphState? ActiveFor(IStatusManager? ownerStatus)
    {
        var owner = OwnerKey(ownerStatus);
        lock (SyncRoot)
        {
            return ActiveStates.TryGetValue(owner, out var state) ? state : null;
        }
    }

    public static void SetPending(PolymorphRoleSpec role, IStatusManager? ownerStatus = null)
    {
        if (role == null)
        {
            return;
        }

        var owner = OwnerKey(ownerStatus);
        lock (SyncRoot)
        {
            PendingRoles[owner] = role;
        }
    }

    public static PolymorphRoleSpec? PendingFor(IStatusManager? ownerStatus = null)
    {
        var owner = OwnerKey(ownerStatus);
        lock (SyncRoot)
        {
            return PendingRoles.TryGetValue(owner, out var role) ? role : null;
        }
    }

    public static void ClearPending(IStatusManager? ownerStatus = null)
    {
        var owner = OwnerKey(ownerStatus);
        lock (SyncRoot)
        {
            PendingRoles.Remove(owner);
        }
    }

    public static PolymorphState SetLocal(PolymorphRoleSpec role, IStatusManager? ownerStatus = null)
    {
        var owner = OwnerKey(ownerStatus);

        lock (SyncRoot)
        {
            var originalCareer = SnapshotOriginalCareer(owner);
            var originalCareerId = DictionaryUtil.Get(originalCareer?.data, "Id");
            IReadOnlyDictionary<string, int> originalCooldowns = RoleSkillApi.SnapshotCurrentCareerSkillTimes();
            var nextVersion = ++version;
            var sessionId = nextVersion;
            if (ActiveStates.TryGetValue(owner, out var existing) && existing.OriginalCareer != null)
            {
                originalCareer = existing.OriginalCareer;
                originalCareerId = existing.OriginalCareerId;
                originalCooldowns = existing.OriginalCooldowns;
                sessionId = existing.SessionId;
            }

            var state = new PolymorphState(
                owner,
                role.Id,
                role.DisplayName,
                originalCareerId,
                originalCareer,
                originalCooldowns,
                sessionId,
                nextVersion);
            ActiveStates[owner] = state;
            TerriasPerformanceCounters.Record("Polymorph.StateSet");
            return state;
        }
    }

    public static void ClearOwner(IStatusManager? ownerStatus, string source)
    {
        PolymorphState? state = null;
        var owner = OwnerKey(ownerStatus);
        lock (SyncRoot)
        {
            PendingRoles.Remove(owner);
            if (ActiveStates.TryGetValue(owner, out state))
            {
                ActiveStates.Remove(owner);
            }
        }

        if (state == null)
        {
            return;
        }

        RestoreOriginalCareer(state, source);
        TerriasLog.Debug("[Polymorph] cleared owner state from " + source + ": " + owner + ".");
        TerriasPerformanceCounters.Record("Polymorph.StateCleared");
    }

    public static void ClearAll(string source)
    {
        PolymorphState[] states;
        lock (SyncRoot)
        {
            if (ActiveStates.Count == 0 && PendingRoles.Count == 0)
            {
                return;
            }

            states = new PolymorphState[ActiveStates.Count];
            ActiveStates.Values.CopyTo(states, 0);
            ActiveStates.Clear();
            PendingRoles.Clear();
        }

        foreach (var state in states)
        {
            RestoreOriginalCareer(state, source);
        }

        TerriasLog.Debug("[Polymorph] cleared battle states from " + source + ".");
        TerriasPerformanceCounters.Record("Polymorph.StateCleared");
    }

    private static string OwnerKey(IStatusManager? ownerStatus = null)
    {
        var owner = ownerStatus?.InstanceId ?? PlayerApi.LocalPlayerStatusId();
        return string.IsNullOrWhiteSpace(owner) ? "local" : owner;
    }

    private static DataConfig? SnapshotOriginalCareer(string owner)
    {
        try
        {
            return string.Equals(owner, PlayerApi.LocalPlayerStatusId(), StringComparison.Ordinal)
                ? RoleTable.Instance?.Career
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void RestoreOriginalCareer(PolymorphState state, string source)
    {
        if (state.OriginalCareer == null)
        {
            return;
        }

        try
        {
            if (RoleTable.Instance == null)
            {
                return;
            }

            var owner = FightPlayer.Instance?.Status;
            if (!CareerApi.CommitLocalCareer(
                    owner,
                    state.OriginalCareer,
                    "PolymorphStateStore.RestoreOriginalCareer:" + source))
            {
                TerriasLog.Warn("[Polymorph] failed to commit original career from " + source + ".");
                return;
            }

            PolymorphNetworkSync.BroadcastNativeCareerChange(
                state.OwnerStatusId,
                state.OriginalCareerId,
                "PolymorphStateStore.RestoreOriginalCareer:" + source);
            PolymorphNetworkSync.BroadcastRestore(state, "PolymorphStateStore.RestoreOriginalCareer:" + source);
            PolymorphRuntimeService.RestoreOriginalCareerRuntime(
                state,
                "PolymorphStateStore.RestoreOriginalCareer:" + source);
            RoleSkillApi.ApplyCurrentCareerSkillTimes(state.OriginalCooldowns);
            RoleSkillApi.RefreshFightSkills("PolymorphStateStore.RestoreOriginalCareer:" + source);
            RoleSkillApi.LogCurrentSkillDiagnostics("PolymorphStateStore.RestoreOriginalCareer:" + source);
            TerriasLog.Info("[Polymorph] restored career from " + source + ": "
                + state.RoleId + " -> " + state.OriginalCareerId);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[Polymorph] failed to restore career from " + source + ": " + ex.Message);
        }
    }

    private static bool RoleMatches(string activeRoleId, string roleId)
    {
        var active = NormalizeRoleId(activeRoleId);
        var expected = NormalizeRoleId(roleId);
        if (string.IsNullOrWhiteSpace(active) || string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        return string.Equals(active, expected, StringComparison.OrdinalIgnoreCase)
            || active.EndsWith("_" + expected, StringComparison.OrdinalIgnoreCase)
            || expected.EndsWith("_" + active, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRoleId(string roleId)
    {
        var value = (roleId ?? "").Trim().TrimStart('*');
        const string terriasPrefix = "Terrias_";
        if (value.StartsWith(terriasPrefix, StringComparison.Ordinal))
        {
            var parts = value.Split('_');
            return parts.Length > 0 ? parts[parts.Length - 1] : value;
        }

        return value;
    }
}
