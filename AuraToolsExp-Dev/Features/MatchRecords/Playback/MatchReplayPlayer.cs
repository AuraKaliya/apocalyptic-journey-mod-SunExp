using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Infrastructure;
using Fight.ActionCommand;
using Fight.ObjTarget;
using Fight.StatusCommand;
using MemoryPack;
using Mirror;
using Newtonsoft.Json;
using UnityEngine;
using Witch.Core;
using Witch.UI.Window;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayPlayer
{
    private static readonly float[] Speeds = { 0.5f, 1f, 2f, 4f };
    private static MatchRecord? record;
    private static List<MatchReplayEvent> events = new();
    private static List<long> timeline = new();
    private static string roleTableBeforeReplay = "";
    private static int eventIndex;
    private static int waitFrames;
    private static float playbackClock;
    private static int speedIndex = 1;
    private static bool paused;
    private static bool resetting;
    private static bool controlsVisible;
    private static bool externalClock;
    private static CanvasGroup? fightCanvasGroup;
    private static bool previousFightRaycasts;
    private static string playbackHealth = "Compatible";
    private static string playbackIssue = "";
    private static int failedEventCount;

    internal static bool IsActive => record != null;

    internal static bool IsPaused => paused;

    internal static float Speed => Speeds[speedIndex];

    internal static int EventIndex => eventIndex;

    internal static int EventCount => events.Count;

    internal static bool IsFinished => IsActive && eventIndex >= events.Count;

    internal static float Progress => events.Count == 0 ? 0f : Math.Max(0f, Math.Min(1f, eventIndex / (float)events.Count));

    internal static long DurationMilliseconds => timeline.Count == 0 ? 0 : timeline[timeline.Count - 1];

    internal static IReadOnlyList<MatchReplayEvent> Events => events;

    internal static bool IsReadyForExport => IsActive && externalClock && waitFrames <= 0;

    internal static string PlaybackHealth => playbackHealth;

    internal static string PlaybackIssue => playbackIssue;

    internal static int FailedEventCount => failedEventCount;

    internal static bool HasBlockingError => playbackHealth == "Desynced" || playbackHealth == "Failed";

    internal static int CurrentTurn => eventIndex <= 0 || events.Count == 0
        ? 1
        : events[Math.Min(events.Count - 1, eventIndex - 1)].TurnIndex;

    internal static int TurnCount => record?.TurnCount ?? 0;

    internal static bool TryStart(string recordId, out string message)
    {
        return TryStartCore(recordId, true, out message);
    }

    internal static bool TryStartAtSequence(string recordId, long eventSequence, out string message)
    {
        if (!TryStartCore(recordId, true, out message))
        {
            return false;
        }

        var target = events.FindIndex(item => item.Sequence >= eventSequence);
        SeekToIndex(target < 0 ? events.Count : target);
        return true;
    }

    internal static bool TryStartForExport(string recordId, out string message)
    {
        return TryStartCore(recordId, false, out message);
    }

    private static bool TryStartCore(string recordId, bool showControls, out string message)
    {
        message = "";
        if (IsActive)
        {
            message = "已有对局正在回放。";
            return false;
        }

        if (!AuraToolsMatchRecordsRuntime.Enabled)
        {
            message = "对局记录模块尚未开启。";
            return false;
        }

        if (FightManager.Instance == null || FightManager.Instance.fightType != FightType.None)
        {
            message = "请在没有进行中战斗时开始回放。";
            return false;
        }

        if (!NetworkClient.active)
        {
            message = "本地战斗客户端尚未就绪，无法创建回放沙箱。";
            return false;
        }

        try
        {
            var loaded = MatchRecordStorage.Database.Get(recordId);
            if (loaded == null)
            {
                message = "找不到这条对局记录。";
                return false;
            }

            var metadataCompatibility = MatchReplayCompatibility.Evaluate(loaded);
            if (!metadataCompatibility.CanPlay)
            {
                message = metadataCompatibility.Message;
                return false;
            }

            var decoded = MatchReplayChunker.Decode(MatchRecordStorage.Database.LoadChunks(recordId)).ToList();
            if (decoded.Count != loaded.EventCount)
            {
                message = "回放事件数量校验失败，记录可能不完整。";
                return false;
            }

            var compatibility = MatchReplayCompatibility.Evaluate(loaded, decoded);
            if (!compatibility.CanPlay)
            {
                message = compatibility.Message;
                return false;
            }

            roleTableBeforeReplay = RoleTable.Instance == null ? "" : AuraSharedJson.Serialize(RoleTable.Instance);
            record = loaded;
            events = decoded;
            timeline = MatchReplayPresentationSchedule.Build(
                decoded,
                AuraToolsExp.Dll.Config.AuraToolsConfigService.MatchExperience.MatchRecords.Replay.PresentationMode);
            eventIndex = 0;
            playbackClock = 0f;
            paused = false;
            speedIndex = 1;
            controlsVisible = showControls;
            externalClock = !showControls;
            playbackHealth = compatibility.Level;
            playbackIssue = compatibility.Level == MatchReplayCompatibilityLevels.Degraded ? compatibility.Message : "";
            failedEventCount = 0;
            MatchReplaySessionState.IsPlayback = true;
            InitializeBattle();
            if (showControls)
            {
                MatchReplayControlsPresenter.Show();
            }
            message = "开始回放。";
            return true;
        }
        catch (Exception ex)
        {
            message = "无法开始回放：" + ex.Message;
            Stop();
            return false;
        }
    }

    internal static void Tick()
    {
        if (!IsActive || resetting)
        {
            return;
        }

        FreezeBattleRuntime();
        if (waitFrames > 0)
        {
            waitFrames--;
            RefreshControls();
            return;
        }

        if (externalClock)
        {
            return;
        }

        if (paused || eventIndex >= events.Count)
        {
            if (eventIndex >= events.Count)
            {
                paused = true;
            }

            RefreshControls();
            return;
        }

        playbackClock += Time.unscaledDeltaTime * 1000f * Speed;
        var executed = 0;
        while (eventIndex < events.Count
               && playbackClock >= timeline[eventIndex]
               && executed < 24)
        {
            Execute(events[eventIndex]);
            eventIndex++;
            executed++;
            if (HasBlockingError) break;
        }

        RefreshControls();
    }

    internal static void TogglePause()
    {
        paused = !paused;
        RefreshControls();
    }

    internal static void ContinueDegraded()
    {
        if (!HasBlockingError) return;
        playbackHealth = "Degraded";
        playbackIssue = "用户选择跳过失败事件；后续画面可能与原对局不一致。";
        paused = false;
        RefreshControls();
    }

    internal static void CycleSpeed()
    {
        speedIndex = (speedIndex + 1) % Speeds.Length;
        RefreshControls();
    }

    internal static void SeekNormalized(float value)
    {
        if (events.Count == 0)
        {
            return;
        }

        SeekToIndex(Math.Max(0, Math.Min(events.Count, (int)Math.Round(value * events.Count))));
    }

    internal static void SeekTurn(int delta)
    {
        if (events.Count == 0)
        {
            return;
        }

        var targetTurn = Math.Max(1, Math.Min(Math.Max(1, TurnCount), CurrentTurn + delta));
        var targetIndex = events.FindIndex(item => item.TurnIndex >= targetTurn);
        if (targetIndex < 0)
        {
            targetIndex = events.Count;
        }

        SeekToIndex(targetIndex);
    }

    internal static void AdvanceExportClock(float milliseconds)
    {
        if (!IsReadyForExport || paused || eventIndex >= events.Count)
        {
            return;
        }

        playbackClock += Math.Max(0f, milliseconds);
        while (eventIndex < events.Count && playbackClock >= timeline[eventIndex])
        {
            Execute(events[eventIndex]);
            eventIndex++;
            if (HasBlockingError) break;
        }

        if (eventIndex >= events.Count)
        {
            paused = true;
        }
    }

    internal static void Stop()
    {
        if (record == null && !MatchReplaySessionState.IsPlayback)
        {
            MatchReplayControlsPresenter.Close();
            return;
        }

        resetting = true;
        try
        {
            MatchReplayControlsPresenter.Close();
            WitchUiManager.Instance?.CloseUI("FightUI");
            WitchUiManager.Instance?.CloseUI("BattleRewardsUI");
            if (FightManager.Instance != null)
            {
                FightManager.Instance.fightType = FightType.None;
                FightManager.Instance.IsFake = false;
            }

            RestoreRoleTable(roleTableBeforeReplay);
            if (fightCanvasGroup != null)
            {
                fightCanvasGroup.blocksRaycasts = previousFightRaycasts;
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] replay cleanup failed: " + ex.Message);
        }
        finally
        {
            record = null;
            events.Clear();
            timeline.Clear();
            roleTableBeforeReplay = "";
            eventIndex = 0;
            waitFrames = 0;
            playbackClock = 0f;
            fightCanvasGroup = null;
            controlsVisible = false;
            externalClock = false;
            playbackHealth = "Compatible";
            playbackIssue = "";
            failedEventCount = 0;
            MatchReplaySessionState.IsPlayback = false;
            resetting = false;
        }
    }

    private static void SeekToIndex(int targetIndex)
    {
        if (record == null)
        {
            return;
        }

        resetting = true;
        try
        {
            InitializeBattle();
            var normalized = Math.Max(0, Math.Min(events.Count, targetIndex));
            var start = 0;
            for (var i = normalized - 1; i >= 0; i--)
            {
                if (events[i].Kind != MatchReplayEventKinds.Checkpoint) continue;
                var checkpoint = MatchReplayPayload.Decode<MatchReplayCheckpoint>(events[i].Payload);
                if (checkpoint?.CanRestore == true && MatchReplayStateCapture.Restore(checkpoint))
                {
                    start = i + 1;
                    break;
                }
            }

            playbackHealth = "Compatible";
            playbackIssue = "";
            failedEventCount = 0;
            for (var i = start; i < normalized; i++)
            {
                Execute(events[i]);
                if (HasBlockingError) throw new InvalidOperationException(playbackIssue);
            }

            eventIndex = normalized;
            playbackClock = eventIndex <= 0 ? 0f : timeline[eventIndex - 1];
            paused = true;
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] replay seek failed: " + ex.Message);
            Stop();
        }
        finally
        {
            resetting = false;
            RefreshControls();
        }
    }

    private static void RefreshControls()
    {
        if (controlsVisible)
        {
            MatchReplayControlsPresenter.Refresh();
        }
    }

    private static void InitializeBattle()
    {
        if (record == null || FightManager.Instance == null)
        {
            throw new InvalidOperationException("Fight runtime is unavailable.");
        }

        WitchUiManager.Instance?.CloseUI("FightUI");
        WitchUiManager.Instance?.CloseUI("BattleRewardsUI");
        RestoreRoleTable(record.InitialState.RoleTableJson);

        var method = typeof(FightManager).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(candidate => candidate.Name.StartsWith("UserCode_Init__", StringComparison.Ordinal)
                                         && ParametersMatch(candidate.GetParameters()));
        if (method == null)
        {
            throw new MissingMethodException("The compatible local fight initializer was not found.");
        }

        method.Invoke(FightManager.Instance, new object[]
        {
            record.InitialState.LevelId,
            record.InitialState.RoleQueue,
            record.InitialState.TemporaryRoles,
            record.InitialState.EnemyPositive,
            record.InitialState.EnemyHp
        });
        FightManager.Instance.IsFake = true;
        FreezeBattleRuntime();
        waitFrames = 2;
        playbackClock = 0f;
        eventIndex = 0;

        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi != null)
        {
            fightCanvasGroup = fightUi.gameObject.GetComponent<CanvasGroup>()
                               ?? fightUi.gameObject.AddComponent<CanvasGroup>();
            previousFightRaycasts = fightCanvasGroup.blocksRaycasts;
            fightCanvasGroup.alpha = 1f;
            fightCanvasGroup.blocksRaycasts = false;
        }
    }

    private static bool ParametersMatch(ParameterInfo[] parameters)
    {
        return parameters.Length == 5
               && parameters[0].ParameterType == typeof(string)
               && parameters[1].ParameterType == typeof(byte[])
               && parameters[2].ParameterType == typeof(byte[])
               && parameters[3].ParameterType == typeof(float)
               && parameters[4].ParameterType == typeof(float);
    }

    private static void FreezeBattleRuntime()
    {
        if (FightManager.Instance != null)
        {
            FightManager.Instance.fightType = FightType.None;
        }
    }

    private static void RestoreRoleTable(string json)
    {
        if (RoleTable.Instance == null || string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var restored = JsonConvert.DeserializeObject<RoleTable>(json);
        if (restored != null)
        {
            RoleTable.Instance.ResetFight(restored);
        }
    }

    private static bool Execute(MatchReplayEvent item)
    {
        try
        {
            switch (item.Kind)
            {
                case MatchReplayEventKinds.ActionCommand:
                    (ActionCommandBaseReaderWriter.Read(CreateReader(item.Payload))
                     ?? throw new InvalidOperationException("行动指令无法反序列化。")).Execute();
                    break;
                case MatchReplayEventKinds.ClientCommand:
                    (ClientCommandBaseReaderWriter.Read(CreateReader(item.Payload))
                     ?? throw new InvalidOperationException("客户端指令无法反序列化。")).Execute();
                    break;
                case MatchReplayEventKinds.TargetCommand:
                    (ObjTargetBaseReaderWriter.Read(CreateReader(item.Payload))
                     ?? throw new InvalidOperationException("目标指令无法反序列化。")).Execute();
                    break;
                case MatchReplayEventKinds.StatusSnapshot:
                    ApplyStatusSnapshot(AuraSharedJson.Deserialize<StatusDataTransfer>(
                        System.Text.Encoding.UTF8.GetString(item.Payload ?? Array.Empty<byte>())));
                    break;
                case MatchReplayEventKinds.Checkpoint:
                    var checkpoint = MatchReplayPayload.Decode<MatchReplayCheckpoint>(item.Payload)
                                     ?? throw new InvalidOperationException("检查点数据无法读取。");
                    if (!MatchReplayStateCapture.Verify(checkpoint, out var actualHash))
                    {
                        playbackHealth = "Desynced";
                        playbackIssue = "回放在事件 " + item.Sequence + " 失步（检查点摘要 "
                                        + Short(checkpoint.StateHash) + " / " + Short(actualHash) + "）。";
                        paused = true;
                        failedEventCount++;
                        return false;
                    }
                    break;
            }
            return true;
        }
        catch (Exception ex)
        {
            playbackHealth = "Failed";
            playbackIssue = "事件 " + item.Sequence + "（" + item.TypeName + "）执行失败：" + ex.Message;
            paused = true;
            failedEventCount++;
            AuraToolsLog.Warn("[MatchRecords] replay event " + item.Sequence + " failed: " + ex.Message);
            return false;
        }
    }

    private static NetworkReader CreateReader(byte[] payload)
    {
        var writer = new NetworkWriter();
        writer.WriteBytesAndSize(payload ?? Array.Empty<byte>());
        return new NetworkReader(writer.ToArraySegment());
    }

    private static void ApplyStatusSnapshot(StatusDataTransfer? snapshot)
    {
        if (snapshot == null) throw new InvalidOperationException("角色状态快照无法反序列化。");
        if (FightManager.Instance == null) throw new InvalidOperationException("战斗状态管理器不可用。");
        if (!FightManager.Instance.statuses.TryGetValue(snapshot.InstanceId ?? "", out var status) || status == null)
            throw new InvalidOperationException("状态快照目标不存在：" + snapshot.InstanceId);

        status.maxHp = snapshot.maxHp;
        status.curHp = snapshot.curHp;
        status.defend = snapshot.defend;
        status.ApplyAuthoritativeState(snapshot.state, status.fatherObject is Enemy);
        status.UpdateStatus();
    }

    private static string Short(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value.Substring(0, Math.Min(8, value.Length));
    }
}
