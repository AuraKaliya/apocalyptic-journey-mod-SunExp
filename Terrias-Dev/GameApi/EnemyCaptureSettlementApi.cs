using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Terrias.Dll.Network;
using UnityEngine;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.GameApi;

public static class EnemyCaptureSettlementApi
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, RpcSpiritEnemySuppressed> PendingSuppressions = new(StringComparer.Ordinal);

    public static void ResetBattleSynchronization()
    {
        lock (SyncRoot)
        {
            PendingSuppressions.Clear();
        }
    }

    public static bool Settle(
        IStatusManager target,
        CapturedEnemySnapshot snapshot,
        SpiritCaptureProfile profile,
        string token = "")
    {
        if (target?.fatherObject is not Enemy enemy || EnemyManager.Instance == null)
        {
            return false;
        }

        var started = TerriasPerformanceCounters.Timestamp();
        var settled = false;
        var suppression = CreateSuppression(enemy, token, "captured-target:" + snapshot.EnemyId);
        using var scope = SpiritCaptureResolutionContext.Begin(
            snapshot,
            profile,
            EnemyManager.Instance.enemyList?.Count ?? 0,
            token);
        try
        {
            AnnounceSuppression(suppression);
            if (profile.RunNativeDeath)
            {
                target.CurHp = 0;
                if (target is StatusManager statusManager)
                {
                    statusManager.CheckDead(true);
                }
                else
                {
                    target.CheckDead();
                }
            }

            if (EnemyManager.Instance.enemyList?.Contains(enemy) == true)
            {
                target.state = IStatusManager.State.Dead;
                target.EnemyDead(0f);
            }

            if (EnemyManager.Instance.enemyList?.Contains(enemy) == true)
            {
                RemoveEnemy(enemy, "captured-target-fallback", triggerAuthoritativeWin: true);
            }

            settled = EnemyManager.Instance.enemyList?.Contains(enemy) != true;
            return settled;
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[SpiritCapture] native settlement failed for " + snapshot.EnemyId, ex);
            RemoveEnemy(enemy, "captured-target-exception", triggerAuthoritativeWin: true);
            settled = EnemyManager.Instance.enemyList?.Contains(enemy) != true;
            return settled;
        }
        finally
        {
            TerriasPerformanceCounters.RecordHotspot(
                "Spirit.Capture.Settlement",
                started,
                "enemy=" + snapshot.EnemyId
                + ", mode=" + profile.ResolutionMode
                + ", settled=" + settled,
                logFirstSample: true);
        }
    }

    public static void ObserveEnemyAdded(string enemyId)
    {
        ApplyPendingSuppressions("EnemyManager.AddEnemy");
        var context = SpiritCaptureResolutionContext.Current;
        var manager = EnemyManager.Instance;
        if (context == null || manager?.enemyList == null || manager.enemyList.Count == 0)
        {
            return;
        }

        var normalized = (enemyId ?? "").Replace("*", "").Trim();
        var explicitlySuppressed = context.Profile.SuppressedSuccessorIds.Any(id => string.Equals(id, normalized, StringComparison.Ordinal));
        var guardedSingleReplacement = string.Equals(context.Profile.ResolutionMode, "GuardedTerminal", StringComparison.Ordinal)
            && context.OriginalEnemyCount <= 1
            && manager.enemyList.Count == 1;
        var adaptedReplacement = string.Equals(context.Profile.ResolutionMode, "AdaptedTerminal", StringComparison.Ordinal);
        if (!explicitlySuppressed && !guardedSingleReplacement && !adaptedReplacement)
        {
            return;
        }

        var successor = manager.enemyList.LastOrDefault(enemy => string.Equals(
            DictionaryUtil.Get(enemy?.dataConfig?.data, "Id").Replace("*", ""), normalized, StringComparison.Ordinal));
        if (successor != null)
        {
            SuppressEnemy(
                successor,
                context.Token + ":successor:" + (successor.InstanceId ?? ""),
                "captured-successor:" + context.Snapshot.EnemyId);
        }
    }

    private static void SuppressEnemy(Enemy? enemy, string token, string source)
    {
        if (enemy == null)
        {
            return;
        }

        AnnounceSuppression(CreateSuppression(enemy, token, source));
        RemoveEnemy(enemy, source, triggerAuthoritativeWin: true);
    }

    private static void RemoveEnemy(Enemy? enemy, string source, bool triggerAuthoritativeWin)
    {
        if (enemy == null)
        {
            return;
        }

        var statusId = enemy.Status?.InstanceId ?? enemy.InstanceId ?? "";
        try
        {
            var enemies = EnemyManager.Instance?.enemyList;
            if (enemies?.Remove(enemy) == true)
            {
                EnemyManager.enemyCount = Math.Max(0, EnemyManager.enemyCount - 1);
            }

            enemies?.RemoveAll(item => item == null);
            if (enemies != null)
            {
                EnemyManager.enemyCount = enemies.Count(item => item != null);
            }

            FightManager.Instance?.statuses?.Remove(statusId);
            FightManager.Instance?.statusData?.Remove(statusId);
            FightManager.Instance?.ActionQueue?.RemoveAll(item => item == null || item.InstanceId == statusId);
            var ui = UIManager.Instance?.GetUI<FightUI>("FightUI");
            ui?.StatusList?.RemoveAll(status => status == null || status.InstanceId == statusId);
            if (enemy.gameObject != null)
            {
                enemy.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(enemy.gameObject);
            }
            if (triggerAuthoritativeWin
                && CompanionAuthorityService.IsAuthoritative()
                && EnemyManager.enemyCount == 0
                && EnemyManager.Instance?.TrySpawnNextWheelEnemy() != true)
            {
                ui?.CanWin();
            }
            TerriasLog.Info("[SpiritCapture] removed enemy without successor settlement: status=" + statusId + ", source=" + source);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[SpiritCapture] forced removal failed from " + source + ": " + ex.Message);
        }
    }

    public static void ApplyNetworkSuppression(RpcSpiritEnemySuppressed? suppression)
    {
        if (suppression == null
            || suppression.ProtocolVersion != CompanionAuthorityService.ProjectionProtocolVersion
            || suppression.BattleEpoch != CompanionAuthorityService.BattleEpoch
            || TerriasNetworkRuntime.IsServer())
        {
            return;
        }

        var key = SuppressionKey(suppression);
        lock (SyncRoot)
        {
            PendingSuppressions[key] = suppression;
        }
        ApplyPendingSuppressions("network:" + suppression.Source);
    }

    private static void ApplyPendingSuppressions(string source)
    {
        KeyValuePair<string, RpcSpiritEnemySuppressed>[] pending;
        lock (SyncRoot)
        {
            pending = PendingSuppressions.ToArray();
        }

        foreach (var entry in pending)
        {
            var enemy = FindEnemy(entry.Value);
            if (enemy == null)
            {
                continue;
            }

            RemoveEnemy(enemy, source, triggerAuthoritativeWin: false);
            lock (SyncRoot)
            {
                PendingSuppressions.Remove(entry.Key);
            }
        }
    }

    private static Enemy? FindEnemy(RpcSpiritEnemySuppressed suppression)
    {
        var enemies = EnemyManager.Instance?.enemyList;
        if (enemies == null)
        {
            return null;
        }

        var exact = enemies.FirstOrDefault(candidate => candidate != null
            && string.Equals(candidate.Status?.InstanceId, suppression.StatusId, StringComparison.Ordinal));
        if (exact != null)
        {
            return exact;
        }

        exact = enemies.FirstOrDefault(candidate => candidate != null
            && string.Equals(candidate.InstanceId, suppression.EnemyRuntimeId, StringComparison.Ordinal));
        if (exact != null)
        {
            return exact;
        }

        var normalizedEnemyId = NormalizeEnemyId(suppression.EnemyId);
        var matches = enemies.Where(candidate => candidate != null
                && string.Equals(
                    NormalizeEnemyId(DictionaryUtil.Get(candidate.dataConfig?.data, "Id")),
                    normalizedEnemyId,
                    StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static RpcSpiritEnemySuppressed CreateSuppression(Enemy enemy, string token, string source)
    {
        return new RpcSpiritEnemySuppressed(
            enemy.Status?.InstanceId ?? enemy.InstanceId ?? "",
            enemy.InstanceId ?? "",
            DictionaryUtil.Get(enemy.dataConfig?.data, "Id"),
            token,
            source);
    }

    private static void AnnounceSuppression(RpcSpiritEnemySuppressed suppression)
    {
        if (!TerriasNetworkRuntime.IsMultiplayerSession() || !TerriasNetworkRuntime.IsServer())
        {
            return;
        }

        TerriasNetworkRuntime.Send(suppression, "EnemyCaptureSettlementApi.AnnounceSuppression");
    }

    private static string SuppressionKey(RpcSpiritEnemySuppressed suppression)
    {
        return !string.IsNullOrWhiteSpace(suppression.Token)
            ? suppression.BattleEpoch + ":" + suppression.Token
            : suppression.BattleEpoch + ":" + suppression.StatusId + ":" + suppression.EnemyRuntimeId;
    }

    private static string NormalizeEnemyId(string enemyId)
    {
        return (enemyId ?? "").Replace("*", "").Trim();
    }
}

public sealed class SpiritCaptureResolutionContext : IDisposable
{
    [ThreadStatic]
    private static SpiritCaptureResolutionContext? current;

    private readonly SpiritCaptureResolutionContext? previous;

    private SpiritCaptureResolutionContext(
        CapturedEnemySnapshot snapshot,
        SpiritCaptureProfile profile,
        int originalEnemyCount,
        string token)
    {
        Snapshot = snapshot;
        Profile = profile;
        OriginalEnemyCount = originalEnemyCount;
        Token = token ?? "";
        previous = current;
        current = this;
    }

    public static SpiritCaptureResolutionContext? Current => current;

    public CapturedEnemySnapshot Snapshot { get; }

    public SpiritCaptureProfile Profile { get; }

    public int OriginalEnemyCount { get; }

    public string Token { get; }

    public static SpiritCaptureResolutionContext Begin(
        CapturedEnemySnapshot snapshot,
        SpiritCaptureProfile profile,
        int originalEnemyCount,
        string token)
    {
        return new SpiritCaptureResolutionContext(snapshot, profile, Math.Max(0, originalEnemyCount), token);
    }

    public void Dispose()
    {
        current = previous;
    }
}
