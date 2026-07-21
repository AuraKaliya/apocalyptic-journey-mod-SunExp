using System;
using System.Linq;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using SunExp.Dll.Network;
using UnityEngine;
using Witch.UI;
using Witch.UI.Window;

namespace SunExp.Dll.GameApi;

public static class EnemyCaptureSettlementApi
{
    public static bool Settle(IStatusManager target, CapturedEnemySnapshot snapshot, SpiritCaptureProfile profile)
    {
        if (target?.fatherObject is not Enemy enemy || EnemyManager.Instance == null)
        {
            return false;
        }

        var started = SunExpPerformanceCounters.Timestamp();
        var settled = false;
        using var scope = SpiritCaptureResolutionContext.Begin(snapshot, profile, EnemyManager.Instance.enemyList?.Count ?? 0);
        try
        {
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
                SuppressEnemy(enemy, "captured-target-fallback");
            }

            settled = EnemyManager.Instance.enemyList?.Contains(enemy) != true;
            return settled;
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[SpiritCapture] native settlement failed for " + snapshot.EnemyId, ex);
            SuppressEnemy(enemy, "captured-target-exception");
            settled = EnemyManager.Instance.enemyList?.Contains(enemy) != true;
            return settled;
        }
        finally
        {
            SunExpPerformanceCounters.RecordHotspot(
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
            SuppressEnemy(successor, "captured-successor:" + context.Snapshot.EnemyId);
        }
    }

    private static void SuppressEnemy(Enemy? enemy, string source)
    {
        if (enemy == null)
        {
            return;
        }

        var statusId = enemy.Status?.InstanceId ?? enemy.InstanceId ?? "";
        try
        {
            if (EnemyManager.Instance?.enemyList?.Remove(enemy) == true)
            {
                EnemyManager.enemyCount = Math.Max(0, EnemyManager.enemyCount - 1);
            }

            FightManager.Instance?.statuses?.Remove(statusId);
            FightManager.Instance?.statusData?.Remove(statusId);
            FightManager.Instance?.ActionQueue?.RemoveAll(item => item == null || item.InstanceId == statusId);
            var ui = UIManager.Instance?.GetUI<FightUI>("FightUI");
            ui?.StatusList?.RemoveAll(status => status == null || status.InstanceId == statusId);
            UnityEngine.Object.Destroy(enemy.gameObject);
            if (SunExpNetworkRuntime.IsMultiplayerSession() && SunExpNetworkRuntime.IsServer())
            {
                SunExpNetworkRuntime.Send(
                    new RpcSpiritEnemySuppressed(statusId, DictionaryUtil.Get(enemy.dataConfig?.data, "Id"), source),
                    "EnemyCaptureSettlementApi.SuppressEnemy");
            }
            if (EnemyManager.enemyCount == 0)
            {
                ui?.CanWin();
            }
            SunExpLog.Info("[SpiritCapture] removed enemy without successor settlement: status=" + statusId + ", source=" + source);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[SpiritCapture] forced removal failed from " + source + ": " + ex.Message);
        }
    }

    public static void ApplyNetworkSuppression(string statusId, string enemyId, string source)
    {
        var manager = EnemyManager.Instance;
        var enemy = manager?.enemyList?.FirstOrDefault(candidate => string.Equals(candidate?.Status?.InstanceId, statusId, StringComparison.Ordinal))
            ?? manager?.enemyList?.LastOrDefault(candidate => string.Equals(
                DictionaryUtil.Get(candidate?.dataConfig?.data, "Id").Replace("*", ""),
                (enemyId ?? "").Replace("*", ""),
                StringComparison.Ordinal));
        SuppressEnemy(enemy, "network:" + source);
    }
}

public sealed class SpiritCaptureResolutionContext : IDisposable
{
    [ThreadStatic]
    private static SpiritCaptureResolutionContext? current;

    private readonly SpiritCaptureResolutionContext? previous;

    private SpiritCaptureResolutionContext(CapturedEnemySnapshot snapshot, SpiritCaptureProfile profile, int originalEnemyCount)
    {
        Snapshot = snapshot;
        Profile = profile;
        OriginalEnemyCount = originalEnemyCount;
        previous = current;
        current = this;
    }

    public static SpiritCaptureResolutionContext? Current => current;

    public CapturedEnemySnapshot Snapshot { get; }

    public SpiritCaptureProfile Profile { get; }

    public int OriginalEnemyCount { get; }

    public static SpiritCaptureResolutionContext Begin(CapturedEnemySnapshot snapshot, SpiritCaptureProfile profile, int originalEnemyCount)
    {
        return new SpiritCaptureResolutionContext(snapshot, profile, Math.Max(0, originalEnemyCount));
    }

    public void Dispose()
    {
        current = previous;
    }
}
