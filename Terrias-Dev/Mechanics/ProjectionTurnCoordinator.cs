using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using UnityEngine;

namespace Terrias.Dll.Mechanics;

public static class ProjectionTurnCoordinator
{
    private static readonly object SyncRoot = new();
    private static readonly HashSet<string> ExecutedThisRound = new(StringComparer.Ordinal);
    private static ProjectionTurnAnchorObj? anchor;
    private static int roundSequence;

    public static void BeginBattle(string source)
    {
        ClearBattle(source + ".Reset");
        EnsureAnchor(source);
    }

    public static void BeginPlayerRound(string source)
    {
        lock (SyncRoot)
        {
            roundSequence++;
            if (roundSequence <= 0)
            {
                roundSequence = 1;
            }

            ExecutedThisRound.Clear();
        }

        EnsureAnchor(source);
        TerriasPerformanceCounters.Record("ProjectionTurnCoordinator.RoundStarted");
        TerriasLog.Info("[ProjectionTurn] player round started: round=" + roundSequence + ", source=" + source);
    }

    public static void RegisterProjection(ProjectionOtherObj projection, string source)
    {
        RegisterCompanion(projection, source);
    }

    public static void RegisterCompanion(OtherObj projection, string source)
    {
        if (projection == null || !CompanionAuthorityService.IsAuthoritative())
        {
            return;
        }

        var manager = FightManager.Instance;
        if (manager?.ActionQueue == null)
        {
            return;
        }

        EnsureAnchor(source);
        manager.ActionQueue.RemoveAll(item => ReferenceEquals(item, projection));
        if (anchor == null && !manager.ActionQueue.Contains(projection))
        {
            manager.ActionQueue.Add(projection);
            TerriasPerformanceCounters.Record("ProjectionTurnCoordinator.NativeFallbackQueued");
            TerriasLog.Warn("[ProjectionTurn] anchor unavailable; projection queued for native next-round fallback: "
                + projection.InstanceId);
        }
    }

    public static IEnumerator ExecuteCurrentRound()
    {
        if (!CompanionAuthorityService.IsAuthoritative())
        {
            yield break;
        }

        var activeRound = Math.Max(1, roundSequence);
        var projections = ProjectionStateStore.Active()
            .Select(state => new CompanionTurnEntry(state.OwnerPlayerId, state.StatusId, state.SlotIndex, state.Projection))
            .Concat(SpiritStateStore.Active().Select(state => new CompanionTurnEntry(state.OwnerPlayerId, state.StatusId, state.SlotIndex, state.Spirit)))
            .OrderBy(state => state.OwnerPlayerId, StringComparer.Ordinal)
            .ThenBy(state => state.StatusId, StringComparer.Ordinal)
            .ToArray();
        TerriasLog.Info("[ProjectionTurn] anchor executing: round=" + activeRound + ", projections=" + projections.Length);
        TerriasPerformanceCounters.Record("ProjectionTurnCoordinator.AnchorExecuted");

        foreach (var state in projections)
        {
            if (state?.Actor == null || !TryClaim(activeRound, state.OwnerPlayerId, state.SlotIndex, state.StatusId))
            {
                continue;
            }

            TerriasLog.Info("[ProjectionTurn] executing projection: round="
                + activeRound
                + ", status="
                + state.StatusId
                + ", slot="
                + state.SlotIndex);
            TerriasPerformanceCounters.Record("ProjectionTurnCoordinator.ProjectionExecuted");
            var routine = state.Actor.DoAction();
            while (routine.MoveNext())
            {
                yield return routine.Current;
            }
        }
    }

    public static void ClearBattle(string source, bool sweepStaleAnchors = true)
    {
        lock (SyncRoot)
        {
            roundSequence = 0;
            ExecutedThisRound.Clear();
        }

        var manager = FightManager.Instance;
        if (manager?.ActionQueue != null)
        {
            manager.ActionQueue.RemoveAll(item => item == null || item is ProjectionTurnAnchorObj);
        }

        if (anchor != null)
        {
            UnityEngine.Object.Destroy(anchor.gameObject);
            anchor = null;
        }

        if (sweepStaleAnchors)
        {
            CleanupStaleAnchors();
        }

        TerriasPerformanceCounters.Record("ProjectionTurnCoordinator.Cleared");
        TerriasLog.Debug("[ProjectionTurn] coordinator cleared from " + source + ".");
    }

    private static bool TryClaim(int round, string ownerPlayerId, int slotIndex, string statusId)
    {
        var ownerScope = string.IsNullOrWhiteSpace(ownerPlayerId)
            ? "status:" + (statusId ?? "")
            : "owner:" + ownerPlayerId + ":slot:" + slotIndex;
        var token = CompanionAuthorityService.BattleEpoch + ":" + round + ":" + ownerScope;
        lock (SyncRoot)
        {
            return ExecutedThisRound.Add(token);
        }
    }

    private static void EnsureAnchor(string source)
    {
        if (!CompanionAuthorityService.IsAuthoritative())
        {
            return;
        }

        var manager = FightManager.Instance;
        if (manager?.ActionQueue == null)
        {
            return;
        }

        if (anchor != null)
        {
            manager.ActionQueue.RemoveAll(item => item is ProjectionOtherObj || item is SpiritOtherObj);
            if (!manager.ActionQueue.Contains(anchor))
            {
                if (anchor.Status != null && anchor.Status.state == IStatusManager.State.NoAction)
                {
                    anchor.Status.ChangeState(IStatusManager.State.Default);
                }

                anchor.ActionCount = anchor.MaxActionCount;
                manager.ActionQueue.Add(anchor);
                TerriasPerformanceCounters.Record("ProjectionTurnCoordinator.AnchorRequeued");
                TerriasLog.Debug("[ProjectionTurn] anchor requeued from " + source + ".");
            }

            return;
        }

        GameObject? pendingRoot = null;
        try
        {
            var prefab = TerriasResourceCache.Load<GameObject>("Model/player", true, "projection-turn-anchor");
            if (prefab == null)
            {
                TerriasPerformanceCounters.Record("ProjectionTurnCoordinator.AnchorPrefabMissing");
                return;
            }

            pendingRoot = UnityEngine.Object.Instantiate(prefab);
            CompanionSceneApi.MoveToOwnerScene(
                pendingRoot,
                FightPlayer.Instance?.Status?.transform?.gameObject,
                source + ".TurnAnchor");
            pendingRoot.name = "TerriasProjectionTurnAnchor:pending";
            pendingRoot.SetActive(false);
            var created = pendingRoot.AddComponent<ProjectionTurnAnchorObj>();
            var templateData = ResolveAnchorTemplateData();
            if (templateData == null
                || !created.InitializeAnchor(CompanionAuthorityService.BattleEpoch, templateData))
            {
                TerriasPerformanceCounters.Record("ProjectionTurnCoordinator.AnchorInitFailed");
                TerriasLog.Warn("[ProjectionTurn] anchor template is incomplete from " + source + ".");
                return;
            }

            manager.ActionQueue.RemoveAll(item => item == null || item is ProjectionTurnAnchorObj || item is ProjectionOtherObj || item is SpiritOtherObj);
            manager.ActionQueue.Add(created);
            anchor = created;
            pendingRoot = null;
            TerriasPerformanceCounters.Record("ProjectionTurnCoordinator.AnchorRegistered");
            TerriasLog.Info("[ProjectionTurn] anchor registered: epoch="
                + CompanionAuthorityService.BattleEpoch
                + ", source="
                + source);
        }
        catch (Exception ex)
        {
            TerriasPerformanceCounters.Record("ProjectionTurnCoordinator.AnchorInitFailed");
            TerriasLog.Warn("[ProjectionTurn] anchor registration failed from " + source + ": " + ex.Message);
        }
        finally
        {
            if (pendingRoot != null)
            {
                pendingRoot.SetActive(false);
                UnityEngine.Object.Destroy(pendingRoot);
            }
        }
    }

    private static IDictionary<string, string>? ResolveAnchorTemplateData()
    {
        var localCareer = RoleTable.Instance?.Career?.data;
        if (HasAnimation(localCareer))
        {
            return localCareer;
        }

        var roles = FightManager.Instance?.roleQueue;
        if (roles == null)
        {
            return null;
        }

        foreach (var role in roles)
        {
            var data = role?.career?.data;
            if (HasAnimation(data))
            {
                return data;
            }
        }

        return null;
    }

    private static bool HasAnimation(IDictionary<string, string>? data)
    {
        return data != null
            && data.TryGetValue("Animation", out var animation)
            && !string.IsNullOrWhiteSpace(animation);
    }

    private static void CleanupStaleAnchors()
    {
        try
        {
            foreach (var stale in Resources.FindObjectsOfTypeAll<ProjectionTurnAnchorObj>())
            {
                if (stale != null && !ReferenceEquals(stale, anchor) && stale.gameObject != null)
                {
                    stale.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(stale.gameObject);
                }
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[ProjectionTurn] stale anchor cleanup skipped: " + ex.Message);
        }
    }

    private sealed class CompanionTurnEntry
    {
        public CompanionTurnEntry(string ownerPlayerId, string statusId, int slotIndex, OtherObj actor)
        {
            OwnerPlayerId = ownerPlayerId ?? "";
            StatusId = statusId ?? "";
            SlotIndex = slotIndex;
            Actor = actor;
        }

        public string OwnerPlayerId { get; }

        public string StatusId { get; }

        public int SlotIndex { get; }

        public OtherObj Actor { get; }
    }
}
