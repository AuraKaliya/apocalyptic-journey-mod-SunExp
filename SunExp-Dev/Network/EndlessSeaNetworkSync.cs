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
public sealed class EndlessSeaStateSnapshot
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

    public static EndlessSeaStateSnapshot Capture(string source)
    {
        return new EndlessSeaStateSnapshot
        {
            Mode = ReadString(SunExpIds.EndlessSeaModeKey),
            Floor = Math.Max(1, ReadInt(SunExpIds.EndlessSeaFloorKey)),
            GeneratedFloor = Math.Max(0, ReadInt(SunExpIds.EndlessSeaGeneratedFloorKey)),
            FloorPlanJson = ReadString(SunExpIds.EndlessSeaFloorPlanKey),
            RunId = ReadString(SunExpIds.EndlessSeaRunIdKey),
            RunPhase = ReadString(SunExpIds.EndlessSeaRunPhaseKey),
            RunEnded = ReadString(SunExpIds.EndlessSeaRunEndedKey),
            StarterDeckApplied = ReadString(SunExpIds.EndlessSeaStarterDeckAppliedKey),
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

        Set(SunExpIds.EndlessSeaModeKey, "1");
        Set(SunExpIds.EndlessSeaFloorKey, Math.Max(1, Floor).ToString());
        Set(SunExpIds.EndlessSeaGeneratedFloorKey, Math.Max(0, GeneratedFloor).ToString());
        Set(SunExpIds.EndlessSeaFloorPlanKey, FloorPlanJson ?? "");
        Set(SunExpIds.EndlessSeaRunIdKey, RunId ?? "");
        Set(SunExpIds.EndlessSeaRunPhaseKey, RunPhase ?? "");
        Set(SunExpIds.EndlessSeaRunEndedKey, RunEnded ?? "0");
        Set(SunExpIds.EndlessSeaStarterDeckAppliedKey, StarterDeckApplied ?? "");
        if (GazeLevel > 0)
        {
            Set(SunExpIds.EndlessAbyssGazeLevelKey, GazeLevel.ToString());
        }

        Set(SunExpIds.EndlessAbyssLedgerKey, EndlessAbyssRunLedger.MergeRemotePreservingLocalMilestones(LedgerJson ?? ""));
        Set(SunExpIds.EndlessAbyssPendingShockKey, PendingShockJson ?? "");
        SunExpLog.Info("[EndlessSeaSync] snapshot applied from "
            + source
            + "; floor="
            + Math.Max(1, Floor)
            + "; gaze="
            + GazeLevel
            + ".");

        SunExpFrameDispatcher.RunOnceNextFrame(
            "EndlessSeaSync.RefreshMapUi",
            () =>
            {
                RefreshMapUi(source);
                EndlessAbyssMilestonePromptService.Schedule("EndlessSeaSync:" + source);
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
            EndlessSeaMapViewPresenter.SetLayerTitle(mapSelect, floor);
            if (manager != null)
            {
                // Snapshots can arrive while the native map card is being dragged.
                // Keep this refresh fixed-slot only so selectable slots are not rebuilt mid-interaction.
                EndlessSeaMapViewPresenter.ApplySlots(
                    mapSelect,
                    manager,
                    floor,
                    applyAllSlots: false,
                    sync: false,
                    "EndlessSeaSync:" + source);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessSeaSync] map UI refresh failed: " + ex.Message);
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
public sealed class RpcEndlessSeaStateSnapshot : RpcCommandBase
{
    public EndlessSeaStateSnapshot Snapshot { get; set; } = new();

    public string Source { get; set; } = "";

    public RpcEndlessSeaStateSnapshot()
    {
    }

    public RpcEndlessSeaStateSnapshot(EndlessSeaStateSnapshot snapshot, string source)
    {
        Snapshot = snapshot ?? new EndlessSeaStateSnapshot();
        Source = source ?? "";
    }

    public override void RpcExecute()
    {
        Snapshot?.Apply("RpcEndlessSeaStateSnapshot:" + Source);
    }
}

[Serializable]
public sealed class RpcEndlessSeaStateSnapshotRequest : RpcCommandBase, ISunExpServerBoundRpcCommand
{
    private SunExpRpcSender serverSender = SunExpRpcSender.Unbound;

    public string Source { get; set; } = "";

    public EndlessSeaStateSnapshot? Snapshot { get; set; }

    public void BindServerSender(SunExpRpcSender sender)
    {
        serverSender = sender ?? SunExpRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        if (serverSender.IsAvailable && !serverSender.IsLobbyMember)
        {
            SunExpLog.Warn("[EndlessSeaSync] rejected snapshot request from outside lobby: " + serverSender.PlayerId);
            return;
        }

        if (GameSaveManager.GetValue<string>(SunExpIds.EndlessSeaModeKey) != "1")
        {
            return;
        }

        Snapshot = EndlessSeaStateSnapshot.Capture("request:" + Source);
        SunExpLog.Debug("[EndlessSeaSync] prepared snapshot for request from "
            + (serverSender.PlayerId.Length == 0 ? "local" : serverSender.PlayerId)
            + ".");
    }

    public override void RpcExecute()
    {
        Snapshot?.Apply("RpcEndlessSeaStateSnapshotRequest:" + Source);
    }
}

public static class EndlessSeaNetworkSync
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

        var snapshot = EndlessSeaStateSnapshot.Capture(source);
        SunExpNetworkRuntime.Send(new RpcEndlessSeaStateSnapshot(snapshot, source), source);
    }

    public static void RequestSnapshot(string source)
    {
        if (!SunExpNetworkRuntime.HasRemotePlayers()
            || !SunExpNetworkRuntime.IsClientOnly()
            || IsSnapshotRequestThrottled(source ?? ""))
        {
            return;
        }

        SunExpNetworkRuntime.Send(new RpcEndlessSeaStateSnapshotRequest
        {
            Source = source ?? ""
        }, source ?? "");
    }

    private static bool IsSnapshotRequestThrottled(string source)
    {
        var floor = Math.Max(1, DictionaryUtil.ParseInt(ReadString(SunExpIds.EndlessSeaFloorKey), 1));
        var now = DateTime.UtcNow;
        if (floor == lastSnapshotRequestFloor
            && string.Equals(source, lastSnapshotRequestSource, StringComparison.Ordinal)
            && (now - lastSnapshotRequestAtUtc).TotalSeconds < SnapshotRequestThrottleSeconds)
        {
            SunExpLog.Debug("[EndlessSeaSync] snapshot request throttled from " + source + ".");
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
