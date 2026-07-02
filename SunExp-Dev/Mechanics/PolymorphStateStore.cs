using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public sealed class PolymorphState
{
    public PolymorphState(
        string ownerStatusId,
        string roleId,
        string displayName,
        string originalCareerId,
        DataConfig? originalCareer,
        int version)
    {
        OwnerStatusId = ownerStatusId ?? "";
        RoleId = roleId ?? "";
        DisplayName = displayName ?? "";
        OriginalCareerId = originalCareerId ?? "";
        OriginalCareer = originalCareer;
        Version = version;
    }

    public string OwnerStatusId { get; }

    public string RoleId { get; }

    public string DisplayName { get; }

    public string OriginalCareerId { get; }

    public DataConfig? OriginalCareer { get; }

    public int Version { get; }
}

public static class PolymorphStateStore
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, PolymorphState> ActiveStates = new(StringComparer.Ordinal);
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

    public static PolymorphState SetLocal(PolymorphRoleSpec role, IStatusManager? ownerStatus = null)
    {
        var owner = ownerStatus?.InstanceId ?? PlayerApi.LocalPlayerStatusId();
        if (string.IsNullOrWhiteSpace(owner))
        {
            owner = "local";
        }

        lock (SyncRoot)
        {
            var originalCareer = SnapshotOriginalCareer(owner);
            var originalCareerId = DictionaryUtil.Get(originalCareer?.data, "Id");
            if (ActiveStates.TryGetValue(owner, out var existing) && existing.OriginalCareer != null)
            {
                originalCareer = existing.OriginalCareer;
                originalCareerId = existing.OriginalCareerId;
            }

            var state = new PolymorphState(owner, role.Id, role.DisplayName, originalCareerId, originalCareer, ++version);
            ActiveStates[owner] = state;
            SunExpPerformanceCounters.Record("Polymorph.StateSet");
            return state;
        }
    }

    public static void ClearAll(string source)
    {
        PolymorphState[] states;
        lock (SyncRoot)
        {
            if (ActiveStates.Count == 0)
            {
                return;
            }

            states = new PolymorphState[ActiveStates.Count];
            ActiveStates.Values.CopyTo(states, 0);
            ActiveStates.Clear();
        }

        foreach (var state in states)
        {
            RestoreOriginalCareer(state, source);
        }

        SunExpLog.Debug("[Polymorph] cleared battle states from " + source + ".");
        SunExpPerformanceCounters.Record("Polymorph.StateCleared");
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

            RoleTable.Instance.Career = state.OriginalCareer;
            FightPlayer.Instance?.Status?.ResetAnimator(false);
            SunExpLog.Info("[Polymorph] restored career from " + source + ": "
                + state.RoleId + " -> " + state.OriginalCareerId);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[Polymorph] failed to restore career from " + source + ": " + ex.Message);
        }
    }
}
