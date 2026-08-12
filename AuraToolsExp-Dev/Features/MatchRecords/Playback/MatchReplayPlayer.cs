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

            if (!CheckCompatibility(loaded, out message))
            {
                return false;
            }

            var decoded = MatchReplayChunker.Decode(MatchRecordStorage.Database.LoadChunks(recordId)).ToList();
            if (decoded.Count != loaded.EventCount)
            {
                message = "回放事件数量校验失败，记录可能不完整。";
                return false;
            }

            roleTableBeforeReplay = RoleTable.Instance == null ? "" : AuraSharedJson.Serialize(RoleTable.Instance);
            record = loaded;
            events = decoded;
            timeline = BuildTimeline(decoded);
            eventIndex = 0;
            playbackClock = 0f;
            paused = false;
            speedIndex = 1;
            controlsVisible = showControls;
            externalClock = !showControls;
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
        }

        RefreshControls();
    }

    internal static void TogglePause()
    {
        paused = !paused;
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
            MatchReplaySessionState.IsPlayback = false;
            resetting = false;
        }
    }

    private static bool CheckCompatibility(MatchRecord value, out string message)
    {
        var currentGameBuild = typeof(FightManager).Assembly.GetName().Version?.ToString() ?? "unknown";
        var currentToolBuild = typeof(AuraToolsMatchRecordsRuntime).Assembly.GetName().Version?.ToString() ?? "unknown";
        if (value.ReplayProtocol != MatchReplayProtocol.Version)
        {
            message = "回放协议版本不兼容，但仍可查看该对局的统计摘要。";
            return false;
        }

        if (!string.Equals(value.GameBuild, currentGameBuild, StringComparison.Ordinal)
            || !string.Equals(value.ToolBuild, currentToolBuild, StringComparison.Ordinal)
            || !string.Equals(value.ModFingerprint, MatchReplayRecorder.CurrentRuntimeFingerprint(), StringComparison.Ordinal))
        {
            message = "游戏或工具版本已变化，为避免污染存档，本记录仅允许查看统计摘要。";
            return false;
        }

        message = "";
        return true;
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
            for (var i = 0; i < normalized; i++)
            {
                Execute(events[i]);
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

    private static void Execute(MatchReplayEvent item)
    {
        try
        {
            switch (item.Kind)
            {
                case MatchReplayEventKinds.ActionCommand:
                    ActionCommandBaseReaderWriter.Read(CreateReader(item.Payload))?.Execute();
                    break;
                case MatchReplayEventKinds.ClientCommand:
                    ClientCommandBaseReaderWriter.Read(CreateReader(item.Payload))?.Execute();
                    break;
                case MatchReplayEventKinds.TargetCommand:
                    ObjTargetBaseReaderWriter.Read(CreateReader(item.Payload))?.Execute();
                    break;
                case MatchReplayEventKinds.StatusSnapshot:
                    ApplyStatusSnapshot(AuraSharedJson.Deserialize<StatusDataTransfer>(
                        System.Text.Encoding.UTF8.GetString(item.Payload ?? Array.Empty<byte>())));
                    break;
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] replay event " + item.Sequence + " failed: " + ex.Message);
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
        if (snapshot == null
            || FightManager.Instance == null
            || !FightManager.Instance.statuses.TryGetValue(snapshot.InstanceId ?? "", out var status)
            || status == null)
        {
            return;
        }

        status.maxHp = snapshot.maxHp;
        status.curHp = snapshot.curHp;
        status.defend = snapshot.defend;
        status.ApplyAuthoritativeState(snapshot.state, status.fatherObject is Enemy);
        status.UpdateStatus();
    }

    private static List<long> BuildTimeline(IReadOnlyList<MatchReplayEvent> source)
    {
        var result = new List<long>(source.Count);
        long accumulated = 0;
        long previous = 0;
        foreach (var item in source)
        {
            var rawDelay = Math.Max(0, item.ElapsedMilliseconds - previous);
            accumulated += Math.Max(20, Math.Min(1500, rawDelay));
            result.Add(accumulated);
            previous = item.ElapsedMilliseconds;
        }

        return result;
    }
}
