using System;
using System.Linq;
using Data.Save;
using Network.Command;
using SunExp.Dll.Hooks;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using Witch;
using Witch.UI.Window;

namespace SunExp.Dll.Network;

[Serializable]
public sealed class TongtianTowerStateSnapshot
{
    public string Mode { get; set; } = "";

    public int Floor { get; set; }

    public int GeneratedFloor { get; set; }

    public string FloorPlanJson { get; set; } = "";

    public string RunId { get; set; } = "";

    public string RunPhase { get; set; } = "";

    public string RunEnded { get; set; } = "";

    public string StarterDeckApplied { get; set; } = "";

    public int GazeLevel { get; set; }

    public string LedgerJson { get; set; } = "";

    public string PendingShockJson { get; set; } = "";

    public string Source { get; set; } = "";

    public static TongtianTowerStateSnapshot Capture(string source)
    {
        return new TongtianTowerStateSnapshot
        {
            Mode = ReadString(SunExpIds.TongtianTowerModeKey),
            Floor = Math.Max(1, ReadInt(SunExpIds.TongtianTowerFloorKey)),
            GeneratedFloor = Math.Max(0, ReadInt(SunExpIds.TongtianTowerGeneratedFloorKey)),
            FloorPlanJson = ReadString(SunExpIds.TongtianTowerFloorPlanKey),
            RunId = ReadString(SunExpIds.TongtianTowerRunIdKey),
            RunPhase = ReadString(SunExpIds.TongtianTowerRunPhaseKey),
            RunEnded = ReadString(SunExpIds.TongtianTowerRunEndedKey),
            StarterDeckApplied = ReadString(SunExpIds.TongtianTowerStarterDeckAppliedKey),
            GazeLevel = Math.Max(0, ReadInt(SunExpIds.EndlessAbyssGazeLevelKey)),
            LedgerJson = ReadString(SunExpIds.EndlessAbyssLedgerKey),
            PendingShockJson = ReadString(SunExpIds.EndlessAbyssPendingShockKey),
            Source = source ?? ""
        };
    }

    public void Apply(string source)
    {
        if (Mode != "1")
        {
            return;
        }

        Set(SunExpIds.TongtianTowerModeKey, "1");
        Set(SunExpIds.TongtianTowerFloorKey, Math.Max(1, Floor).ToString());
        Set(SunExpIds.TongtianTowerGeneratedFloorKey, Math.Max(0, GeneratedFloor).ToString());
        Set(SunExpIds.TongtianTowerFloorPlanKey, FloorPlanJson ?? "");
        Set(SunExpIds.TongtianTowerRunIdKey, RunId ?? "");
        Set(SunExpIds.TongtianTowerRunPhaseKey, RunPhase ?? "");
        Set(SunExpIds.TongtianTowerRunEndedKey, RunEnded ?? "0");
        Set(SunExpIds.TongtianTowerStarterDeckAppliedKey, StarterDeckApplied ?? "");
        if (GazeLevel > 0)
        {
            Set(SunExpIds.EndlessAbyssGazeLevelKey, GazeLevel.ToString());
        }

        Set(SunExpIds.EndlessAbyssLedgerKey, EndlessAbyssRunLedger.MergeRemotePreservingLocalMilestones(LedgerJson ?? ""));
        Set(SunExpIds.EndlessAbyssPendingShockKey, PendingShockJson ?? "");
        SunExpLog.Info("[TongtianTowerSync] snapshot applied from "
            + source
            + "; floor="
            + Math.Max(1, Floor)
            + "; gaze="
            + GazeLevel
            + ".");

        SunExpFrameDispatcher.RunOnceNextFrame(
            "TongtianTowerSync.RefreshMapUi",
            () =>
            {
                RefreshMapUi(source);
                EndlessAbyssMilestonePromptService.Schedule("TongtianTowerSync:" + source);
            });
    }

    private void RefreshMapUi(string source)
    {
        try
        {
            var mapSelect = Resources.FindObjectsOfTypeAll<MapSelectUI>()
                .FirstOrDefault(item => item != null && item.gameObject.scene.IsValid());
            if (mapSelect == null)
            {
                return;
            }

            var floor = Math.Max(1, Floor);
            var manager = MapManager.Instance?.ModeMapManager as NormalMapManager;
            TongtianTowerMapViewPresenter.SetLayerTitle(mapSelect, floor);
            if (manager != null)
            {
                // Snapshots can arrive while the native map card is being dragged.
                // Keep this refresh fixed-slot only so selectable slots are not rebuilt mid-interaction.
                TongtianTowerMapViewPresenter.ApplySlots(
                    mapSelect,
                    manager,
                    floor,
                    applyAllSlots: false,
                    sync: false,
                    "TongtianTowerSync:" + source);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[TongtianTowerSync] map UI refresh failed: " + ex.Message);
        }
    }

    private static string ReadString(string key)
    {
        try
        {
            return GameSaveManager.GetValue<string>(key) ?? "";
        }
        catch
        {
            var save = GameSaveManager.GetNowSave();
            return save?.GameVars != null && save.GameVars.TryGetValue(key, out var value) ? value ?? "" : "";
        }
    }

    private static int ReadInt(string key)
    {
        return DictionaryUtil.ParseInt(ReadString(key));
    }

    private static void Set(string key, string value)
    {
        try
        {
            GameSaveManager.SetValue(key, value ?? "");
        }
        catch
        {
            try
            {
                GameSaveManager.GetNowSave()?.SetValue(key, value ?? "");
            }
            catch
            {
                var save = GameSaveManager.GetNowSave();
                if (save?.GameVars != null)
                {
                    save.GameVars[key] = value ?? "";
                }
            }
        }
    }
}

[Serializable]
public sealed class RpcTongtianTowerStateSnapshot : RpcCommandBase
{
    public TongtianTowerStateSnapshot Snapshot { get; set; } = new();

    public string Source { get; set; } = "";

    public RpcTongtianTowerStateSnapshot()
    {
    }

    public RpcTongtianTowerStateSnapshot(TongtianTowerStateSnapshot snapshot, string source)
    {
        Snapshot = snapshot ?? new TongtianTowerStateSnapshot();
        Source = source ?? "";
    }

    public override void RpcExecute()
    {
        Snapshot?.Apply("RpcTongtianTowerStateSnapshot:" + Source);
    }
}

[Serializable]
public sealed class RpcTongtianTowerStateSnapshotRequest : RpcCommandBase, ISunExpServerBoundRpcCommand
{
    private SunExpRpcSender serverSender = SunExpRpcSender.Unbound;

    public string Source { get; set; } = "";

    public TongtianTowerStateSnapshot? Snapshot { get; set; }

    public void BindServerSender(SunExpRpcSender sender)
    {
        serverSender = sender ?? SunExpRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        if (serverSender.IsAvailable && !serverSender.IsLobbyMember)
        {
            SunExpLog.Warn("[TongtianTowerSync] rejected snapshot request from outside lobby: " + serverSender.PlayerId);
            return;
        }

        if (GameSaveManager.GetValue<string>(SunExpIds.TongtianTowerModeKey) != "1")
        {
            return;
        }

        Snapshot = TongtianTowerStateSnapshot.Capture("request:" + Source);
        SunExpLog.Debug("[TongtianTowerSync] prepared snapshot for request from "
            + (serverSender.PlayerId.Length == 0 ? "local" : serverSender.PlayerId)
            + ".");
    }

    public override void RpcExecute()
    {
        Snapshot?.Apply("RpcTongtianTowerStateSnapshotRequest:" + Source);
    }
}

public static class TongtianTowerNetworkSync
{
    private const double SnapshotRequestThrottleSeconds = 1.5d;
    private static string lastSnapshotRequestSource = "";
    private static int lastSnapshotRequestFloor;
    private static DateTime lastSnapshotRequestAtUtc = DateTime.MinValue;

    public static void BroadcastSnapshot(string source)
    {
        if (!SunExpNetworkRuntime.HasRemotePlayers()
            || !SunExpNetworkRuntime.IsMultiplayerSession()
            || SunExpNetworkRuntime.IsClientOnly())
        {
            return;
        }

        var snapshot = TongtianTowerStateSnapshot.Capture(source);
        SunExpNetworkRuntime.Send(new RpcTongtianTowerStateSnapshot(snapshot, source), source);
    }

    public static void RequestSnapshot(string source)
    {
        if (!SunExpNetworkRuntime.HasRemotePlayers()
            || !SunExpNetworkRuntime.IsClientOnly()
            || IsSnapshotRequestThrottled(source ?? ""))
        {
            return;
        }

        SunExpNetworkRuntime.Send(new RpcTongtianTowerStateSnapshotRequest
        {
            Source = source ?? ""
        }, source ?? "");
    }

    private static bool IsSnapshotRequestThrottled(string source)
    {
        var floor = Math.Max(1, DictionaryUtil.ParseInt(ReadString(SunExpIds.TongtianTowerFloorKey), 1));
        var now = DateTime.UtcNow;
        if (floor == lastSnapshotRequestFloor
            && string.Equals(source, lastSnapshotRequestSource, StringComparison.Ordinal)
            && (now - lastSnapshotRequestAtUtc).TotalSeconds < SnapshotRequestThrottleSeconds)
        {
            SunExpLog.Debug("[TongtianTowerSync] snapshot request throttled from " + source + ".");
            return true;
        }

        lastSnapshotRequestFloor = floor;
        lastSnapshotRequestSource = source;
        lastSnapshotRequestAtUtc = now;
        return false;
    }

    private static string ReadString(string key)
    {
        try
        {
            return GameSaveManager.GetValue<string>(key) ?? "";
        }
        catch
        {
            return "";
        }
    }
}
