using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using Newtonsoft.Json;
using Witch;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayStateCapture
{
    internal static MatchReplayCheckpoint Capture(long sequence, int turnIndex)
    {
        var snapshot = CaptureSnapshot(turnIndex);
        var json = AuraSharedJson.SerializeCompact(snapshot);
        return new MatchReplayCheckpoint
        {
            EventSequence = sequence,
            TurnIndex = turnIndex,
            SnapshotJson = json,
            StateHash = Hash(json),
            CanRestore = FightManager.Instance != null && RoleTable.Instance != null
        };
    }

    internal static bool Verify(MatchReplayCheckpoint checkpoint, out string actualHash)
    {
        var snapshot = CaptureSnapshot(checkpoint.TurnIndex);
        actualHash = Hash(AuraSharedJson.SerializeCompact(snapshot));
        return string.Equals(actualHash, checkpoint.StateHash, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool Restore(MatchReplayCheckpoint checkpoint)
    {
        if (!checkpoint.CanRestore || string.IsNullOrWhiteSpace(checkpoint.SnapshotJson) || FightManager.Instance == null)
        {
            return false;
        }

        var snapshot = AuraSharedJson.Deserialize<MatchReplayStateSnapshot>(checkpoint.SnapshotJson);
        if (snapshot == null) return false;
        var manager = FightManager.Instance;
        manager.SumOfEnemyPositive = snapshot.EnemyPositive;
        manager.EnemyHp = snapshot.EnemyHp;
        if (RoleTable.Instance != null && !string.IsNullOrWhiteSpace(snapshot.RoleTableJson))
        {
            var restored = JsonConvert.DeserializeObject<RoleTable>(snapshot.RoleTableJson);
            if (restored != null) RoleTable.Instance.ResetFight(restored);
        }

        foreach (var item in snapshot.Statuses)
        {
            if (!manager.statuses.TryGetValue(item.InstanceId, out var status) || status == null) continue;
            status.maxHp = item.MaxHp;
            status.curHp = item.CurrentHp;
            status.defend = item.Defend;
            if (!ApplyState(status, item.State)) return false;
            status.UpdateStatus();
        }

        return true;
    }

    private static MatchReplayStateSnapshot CaptureSnapshot(int turnIndex)
    {
        var manager = FightManager.Instance;
        var result = new MatchReplayStateSnapshot
        {
            LevelId = manager?.level ?? "",
            TurnIndex = Math.Max(1, turnIndex),
            EnemyPositive = manager?.SumOfEnemyPositive ?? 0f,
            EnemyHp = manager?.EnemyHp ?? 0f,
            RoleTableJson = RoleTable.Instance == null ? "" : AuraSharedJson.Serialize(RoleTable.Instance)
        };
        if (manager == null) return result;
        result.Statuses = manager.statuses
            .Where(item => item.Value != null)
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new MatchReplayStatusState
            {
                InstanceId = item.Key ?? "",
                MaxHp = item.Value.maxHp,
                CurrentHp = item.Value.curHp,
                Defend = item.Value.defend,
                State = item.Value.state.ToString()
            })
            .ToList();
        return result;
    }

    private static bool ApplyState(object status, string stateName)
    {
        if (string.IsNullOrWhiteSpace(stateName)) return true;
        var method = status.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(value => value.Name == "ApplyAuthoritativeState" && value.GetParameters().Length == 2);
        if (method == null) return false;
        var type = method.GetParameters()[0].ParameterType;
        if (!type.IsEnum) return false;
        try
        {
            var state = Enum.Parse(type, stateName, ignoreCase: true);
            var father = status.GetType().GetField("fatherObject", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(status);
            method.Invoke(status, new[] { state, (object)(father is Enemy) });
            return true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("无法恢复检查点中的角色状态：" + stateName, ex);
        }
    }

    private static string Hash(string value)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")).Select(item => item.ToString("x2")));
    }
}
