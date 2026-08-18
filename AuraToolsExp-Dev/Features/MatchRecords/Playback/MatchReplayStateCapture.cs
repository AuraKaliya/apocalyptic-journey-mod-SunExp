using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.GameApi;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using Witch;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayStateCapture
{
    internal static MatchReplayCheckpoint Capture(
        long sequence,
        int turnIndex,
        string actionId,
        int actionIndex)
    {
        var snapshot = CaptureSnapshot(turnIndex, includeRoleTable: true);
        var json = AuraSharedJson.SerializeCompact(snapshot);
        var logicalHash = HashLogical(snapshot);
        return new MatchReplayCheckpoint
        {
            EventSequence = sequence,
            TurnIndex = turnIndex,
            ActionId = actionId ?? "",
            ActionIndex = Math.Max(0, actionIndex),
            SnapshotJson = json,
            StateHash = logicalHash,
            LogicalStateHash = logicalHash,
            CanRestore = FightManager.Instance != null && RoleTable.Instance != null
        };
    }

    internal static bool Verify(
        MatchReplayCheckpoint checkpoint,
        out string actualHash,
        out MatchReplayStateDiff diff)
    {
        var expected = Deserialize(checkpoint);
        var actual = CaptureSnapshot(checkpoint.TurnIndex, includeRoleTable: false);
        actualHash = HashLogical(actual);
        diff = expected == null
            ? new MatchReplayStateDiff()
            : MatchReplayStateComparer.Compare(expected, actual);
        var expectedHash = string.IsNullOrWhiteSpace(checkpoint.LogicalStateHash)
            ? checkpoint.StateHash
            : checkpoint.LogicalStateHash;
        return expected != null
               && diff.IsMatch
               && string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool Restore(
        MatchReplayCheckpoint checkpoint,
        bool restoreCards,
        bool restoreRoleTable)
    {
        RestoredExpectedHandCount = 0;
        if (!checkpoint.CanRestore || FightManager.Instance == null)
        {
            return false;
        }

        var snapshot = Deserialize(checkpoint);
        if (snapshot == null)
        {
            return false;
        }

        return Project(snapshot, restoreCards, restoreRoleTable);
    }

    internal static MatchReplayStateSnapshot CaptureProjectionSnapshot(int turnIndex)
    {
        return CaptureSnapshot(turnIndex, includeRoleTable: false);
    }

    internal static MatchReplayRevisionProbe CaptureRevisionProbe(int turnIndex, long version)
    {
        var hash = new RevisionHash64();
        var pendingWriters = 0;
        var manager = FightManager.Instance;
        hash.Add(turnIndex);
        hash.Add(manager?.level ?? "");
        hash.Add(manager?.SumOfEnemyPositive ?? 0f);
        hash.Add(manager?.EnemyHp ?? 0f);
        hash.Add(FightPlayer.Instance?.CurPowerCount ?? 0);
        hash.Add(FightPlayer.Instance?.MaxPowerCount ?? 0);
        if (manager != null)
        {
            foreach (var pair in manager.statuses)
            {
                var status = pair.Value;
                if (status == null)
                {
                    continue;
                }

                hash.Add(pair.Key ?? "");
                hash.Add(status.maxHp);
                hash.Add(status.curHp);
                hash.Add(status.defend);
                hash.Add(status.state.ToString());
                foreach (var variable in status.dynamicVariables)
                {
                    hash.Add(variable.Key ?? "");
                    hash.Add(variable.Value);
                }

                foreach (var buff in status.GetBuffs() ?? Array.Empty<IBuffItem>())
                {
                    if (buff?.buffConfig == null)
                    {
                        continue;
                    }

                    hash.Add(buff.buffConfig.BuffId ?? "");
                    hash.Add(buff.buffConfig.Level);
                }
            }

            AddCardZoneFingerprint(ref hash, "draw", FightCardManager.Instance?.cardList);
            AddCardZoneFingerprint(ref hash, "discard", FightCardManager.Instance?.usedCardList);
            AddCardZoneFingerprint(ref hash, "nascent", FightCardManager.Instance?.nascentList);
            hash.Add("hand");
            foreach (var card in FightUI.cardItemList ?? new List<CardItem>())
            {
                AddCardFingerprint(ref hash, card?.dataConfig);
            }

            var fightUi = Witch.UI.UIManager.Instance?.GetUI<FightUI>("FightUI");
            foreach (var config in fightUi?.createCardQueue ?? Enumerable.Empty<DataConfig>())
            {
                AddCardFingerprint(ref hash, config);
            }

            pendingWriters = fightUi?.createCardQueue?.Count ?? 0;
            hash.Add(fightUi?.CardTopCount ?? 0);
            hash.Add(MatchReplayEnemyIntentApi.CaptureRevisionFingerprint());
        }

        return new MatchReplayRevisionProbe(version, hash.Value, pendingWriters);
    }

    private static void AddCardZoneFingerprint(
        ref RevisionHash64 hash,
        string zone,
        IEnumerable<DataConfig>? cards)
    {
        hash.Add(zone);
        if (cards == null)
        {
            return;
        }

        foreach (var card in cards)
        {
            AddCardFingerprint(ref hash, card);
        }
    }

    private static void AddCardFingerprint(ref RevisionHash64 hash, IDataConfig? card)
    {
        if (card == null)
        {
            return;
        }

        hash.Add(card.InstanceID ?? "");
        foreach (var value in card.data)
        {
            hash.Add(value.Key ?? "");
            hash.Add(value.Value ?? "");
        }

        foreach (var value in card.Vars)
        {
            hash.Add(value.Key ?? "");
            hash.Add(value.Value ?? "");
        }
    }

    internal static bool Project(
        MatchReplayStateSnapshot? snapshot,
        bool restoreCards,
        bool restoreRoleTable,
        IReadOnlyCollection<string>? changedStatusIds = null)
    {
        RestoredExpectedHandCount = 0;
        if (snapshot == null || FightManager.Instance == null)
        {
            return false;
        }

        var manager = FightManager.Instance;
        manager.SumOfEnemyPositive = snapshot.EnemyPositive;
        manager.EnemyHp = snapshot.EnemyHp;
        if (FightPlayer.Instance != null)
        {
            ProjectPlayerPower(
                FightPlayer.Instance,
                Math.Max(0, snapshot.PlayerMaxPower),
                Math.Max(0, snapshot.PlayerPower));
        }

        if (restoreRoleTable
            && RoleTable.Instance != null
            && !string.IsNullOrWhiteSpace(snapshot.RoleTableJson))
        {
            var restored = JsonConvert.DeserializeObject<RoleTable>(snapshot.RoleTableJson);
            if (restored != null)
            {
                RoleTable.Instance.ResetFight(restored);
            }
        }

        var expectedStatusIds = new HashSet<string>(
            snapshot.Statuses.Select(item => item.InstanceId),
            StringComparer.Ordinal);
        foreach (var pair in manager.statuses)
        {
            var gameObject = pair.Value?.fatherObject?.gameObject;
            if (gameObject != null)
            {
                gameObject.SetActive(expectedStatusIds.Contains(pair.Key));
            }
        }

        var changed = changedStatusIds == null
            ? null
            : new HashSet<string>(changedStatusIds, StringComparer.Ordinal);
        foreach (var item in snapshot.Statuses)
        {
            if (changed != null && !changed.Contains(item.InstanceId))
            {
                continue;
            }

            if (!manager.statuses.TryGetValue(item.InstanceId, out var status) || status == null)
            {
                continue;
            }

            if (status.fatherObject?.gameObject != null)
            {
                status.fatherObject.gameObject.SetActive(true);
            }

            MatchReplayPassiveBuffPresenter.Project(status, item.Buffs);
            status.dynamicVariables.Clear();
            foreach (var variable in item.DynamicVariables)
            {
                status.dynamicVariables[variable.Key] = variable.Value;
            }

            status.maxHp = item.MaxHp;
            status.curHp = item.CurrentHp;
            status.defend = item.Defend;
            if (!ApplyPassiveState(status, item.State))
            {
                return false;
            }

            ProjectStatusHud(status, item);
            if (manager.statusData.TryGetValue(item.InstanceId, out var statusData))
            {
                statusData.maxHp = item.MaxHp;
                statusData.curHp = item.CurrentHp;
                statusData.defend = item.Defend;
                statusData.state = status.state;
                statusData.Version = Math.Max(statusData.Version, status.LastStatusDataVersion);
                manager.statusData.Remove(item.InstanceId);
                manager.statusData.Add(item.InstanceId, statusData);
            }
        }

        if (restoreCards)
        {
            RestoredExpectedHandCount = MatchReplayCardStateCapture.Restore(
                snapshot.Cards,
                snapshot.CardTopCount,
                rebuild: restoreRoleTable);
        }

        MatchReplayEnemyIntentPresenter.Project(snapshot.EnemyIntents);

        // UI-only BuffBar operations can enqueue native synchronization commands. The replay
        // view never consumes them; clear the queue after projection so state application
        // cannot turn back into combat simulation.
        manager.ActionQueue?.Clear();

        return true;
    }

    internal static int RestoredExpectedHandCount { get; private set; }

    internal static void ResetRestoreState()
    {
        RestoredExpectedHandCount = 0;
    }

    private static MatchReplayStateSnapshot? Deserialize(MatchReplayCheckpoint checkpoint)
    {
        return string.IsNullOrWhiteSpace(checkpoint.SnapshotJson)
            ? null
            : AuraSharedJson.Deserialize<MatchReplayStateSnapshot>(checkpoint.SnapshotJson);
    }

    private static MatchReplayStateSnapshot CaptureSnapshot(int turnIndex, bool includeRoleTable)
    {
        var manager = FightManager.Instance;
        var result = new MatchReplayStateSnapshot
        {
            LevelId = manager?.level ?? "",
            TurnIndex = Math.Max(1, turnIndex),
            EnemyPositive = manager?.SumOfEnemyPositive ?? 0f,
            EnemyHp = manager?.EnemyHp ?? 0f,
            PlayerPower = FightPlayer.Instance?.CurPowerCount ?? 0,
            PlayerMaxPower = FightPlayer.Instance?.MaxPowerCount ?? 0,
            // RoleTable is restore-only context. It is retained for deterministic seeking but
            // excluded from the logical hash/diff contract below.
            RoleTableJson = !includeRoleTable || RoleTable.Instance == null
                ? ""
                : AuraSharedJson.Serialize(RoleTable.Instance)
        };
        if (manager == null)
        {
            return result;
        }

        result.Statuses = manager.statuses
            .Where(item => item.Value != null)
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new MatchReplayStatusState
            {
                InstanceId = item.Key ?? "",
                MaxHp = item.Value.maxHp,
                CurrentHp = item.Value.curHp,
                Defend = item.Value.defend,
                State = item.Value.state.ToString(),
                DynamicVariables = item.Value.dynamicVariables
                    .OrderBy(value => value.Key, StringComparer.Ordinal)
                    .Select(value => new MatchReplayFloatValue
                    {
                        Key = value.Key ?? "",
                        Value = value.Value
                    })
                    .ToList(),
                Buffs = CaptureBuffs(item.Value)
            })
            .ToList();
        result.Cards = MatchReplayCardStateCapture.Capture(out var cardTopCount);
        result.CardTopCount = cardTopCount;
        result.EnemyIntents = MatchReplayEnemyIntentApi.CapturePlans();
        return result;
    }

    private static List<MatchReplayBuffState> CaptureBuffs(StatusManager status)
    {
        return (status.GetBuffs() ?? Array.Empty<IBuffItem>())
            .Where(item => item?.buffConfig != null)
            .OrderBy(item => item.buffConfig.BuffId, StringComparer.Ordinal)
            .Select(item => new MatchReplayBuffState
            {
                BuffId = item.buffConfig.BuffId ?? "",
                Level = item.buffConfig.Level,
                UpperBound = item.buffConfig.UpperBound,
                ReducePerTurn = item.buffConfig.ReducePerTurn,
                ReducePerUse = item.buffConfig.ReducePerUse,
                ReducePerAttacked = item.buffConfig.ReducePerAttacked,
                Vars = CaptureValues(item.buffConfig.dataConfig?.Vars)
            })
            .ToList();
    }

    private static List<MatchReplayStringValue> CaptureValues(IDictionary<string, string>? values)
    {
        return (values ?? new Dictionary<string, string>())
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new MatchReplayStringValue
            {
                Key = item.Key ?? "",
                Value = item.Value ?? ""
            })
            .ToList();
    }

    private static bool ApplyPassiveState(object status, string stateName)
    {
        if (string.IsNullOrWhiteSpace(stateName))
        {
            return true;
        }

        var field = status.GetType().GetField(
            "_state",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null || !field.FieldType.IsEnum)
        {
            return false;
        }

        try
        {
            field.SetValue(status, Enum.Parse(field.FieldType, stateName, ignoreCase: true));
            return true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("无法恢复检查点中的角色状态：" + stateName, ex);
        }
    }

    private static void ProjectStatusHud(StatusManager status, MatchReplayStatusState state)
    {
        var ui = status.statusBarUI;
        if (ui == null)
        {
            return;
        }

        var hp = Math.Max(0, state.CurrentHp);
        var maxHp = Math.Max(0, state.MaxHp);
        var hpRatio = maxHp <= 0 ? 0f : Mathf.Clamp01(hp / (float)maxHp);
        if (ui.hpTxt != null)
        {
            ui.hpTxt.gameObject.SetActive(true);
            ui.hpTxt.SetText(hp.ToString());
            ui.hpTxt.ForceMeshUpdate();
        }

        if (ui.hpRedImg?.material != null)
        {
            ui.hpRedImg.material.SetFloat("_FillAmount", hpRatio);
        }

        if (ui.hpImg?.material != null)
        {
            ui.hpImg.material.SetFloat("_FillAmount", hpRatio);
        }

        var defendRatio = maxHp <= 0 ? 0f : Mathf.Clamp01(state.Defend / (float)maxHp);
        if (ui.defendImg != null)
        {
            ui.defendImg.enabled = state.Defend > 0;
            if (ui.defendImg.material != null)
            {
                ui.defendImg.material.SetFloat("_FillAmount", defendRatio);
            }
        }

        if (ui.DefendObj != null)
        {
            var large = ui.DefendObj.transform.Find("Large");
            var small = ui.DefendObj.transform.Find("Small");
            if (large != null) large.gameObject.SetActive(state.Defend >= 100);
            if (small != null) small.gameObject.SetActive(state.Defend < 100);
            var value = ui.DefendObj.transform.Find("val")?.GetComponent<TMP_Text>();
            if (value != null) value.SetText(state.Defend.ToString());
        }

        status.UpdateDisplay();
    }

    private static void ProjectPlayerPower(FightPlayer player, int maximum, int current)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var maximumField = player.GetType().GetField("maxPowerCount", flags);
        var currentField = player.GetType().GetField("curPowerCount", flags);
        if (maximumField == null || currentField == null)
        {
            throw new InvalidOperationException("当前游戏版本缺少只读能量投影字段。");
        }

        maximumField.SetValue(player, maximum);
        currentField.SetValue(player, current);
        Witch.UI.UIManager.Instance?.GetUI<Witch.UI.Window.FightUI>("FightUI")?.UpdatePower();
    }

    private static string HashLogical(MatchReplayStateSnapshot snapshot)
    {
        return MatchReplayProjectionState.Hash(snapshot);
    }

    private struct RevisionHash64
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private ulong value;
        private bool initialized;

        internal ulong Value => initialized ? value : Offset;

        internal void Add(string value)
        {
            EnsureInitialized();
            foreach (var character in value ?? "")
            {
                this.value ^= character;
                this.value *= Prime;
            }

            this.value ^= 0xff;
            this.value *= Prime;
        }

        internal void Add(int value)
        {
            Add(unchecked((uint)value));
        }

        internal void Add(float value)
        {
            Add(unchecked((uint)value.GetHashCode()));
        }

        private void Add(uint number)
        {
            EnsureInitialized();
            for (var shift = 0; shift < 32; shift += 8)
            {
                value ^= (byte)(number >> shift);
                value *= Prime;
            }
        }

        private void EnsureInitialized()
        {
            if (!initialized)
            {
                value = Offset;
                initialized = true;
            }
        }
    }
}
