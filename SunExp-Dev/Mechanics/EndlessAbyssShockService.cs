using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using Newtonsoft.Json;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Network;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class EndlessAbyssShockOptionIds
{
    public const string DestroyRelic = "destroy-relic";
    public const string AnnihilateCards = "annihilate-cards";
    public const string IncreaseGaze = "increase-gaze";
}

public sealed class EndlessAbyssShockRequest
{
    public string Key { get; set; } = "";

    public string Trigger { get; set; } = "";

    public int Floor { get; set; }

    public int NativeLevel { get; set; }

    public string NodeId { get; set; } = "";

    public string NodeKind { get; set; } = "";

    public int GazeLevelAtEnqueue { get; set; }

    public string Source { get; set; } = "";
}

public sealed class EndlessAbyssShockResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public List<string> AppliedOptions { get; } = new();
}

public static class EndlessAbyssShockService
{
    private const string StealthTrigger = "stealth-floor";
    private const string EndlessBattleTrigger = "endless-battle";
    private const int ProcessedResolutionTokenLimit = 128;
    private static readonly object ResolutionTokenSync = new();
    private static readonly HashSet<string> ProcessedResolutionTokens = new(StringComparer.Ordinal);
    private static readonly Queue<string> ProcessedResolutionTokenOrder = new();

    public static bool TryEnqueueStealthFloorShock(int floor, string source)
    {
        floor = Math.Max(1, floor);
        var config = EndlessAbyssConfigStore.Current;
        if (TongtianTowerRewardPlan.IsEndless(floor) || floor < config.Shock.StealthMinFloor)
        {
            return false;
        }

        return TryEnqueue(new EndlessAbyssShockRequest
        {
            Key = "shock:" + StealthTrigger + ":floor:" + floor,
            Trigger = StealthTrigger,
            Floor = floor,
            NativeLevel = MapManager.Instance?.Level ?? 0,
            NodeKind = SunExpIds.EndlessAbyssStealthModeName,
            GazeLevelAtEnqueue = EndlessAbyssGazeService.CurrentLevel(),
            Source = source
        }, source);
    }

    public static bool TryEnqueueEndlessBattleShock(int floor, TongtianTowerNodeKind nodeKind, string source)
    {
        floor = Math.Max(1, floor);
        if (!TongtianTowerRewardPlan.IsEndless(floor))
        {
            return false;
        }

        EndlessAbyssGazeService.EnsureAtLeast(EndlessAbyssConfigStore.Current.Gaze.EndlessMinLevel, source + ":endless-entry");

        var data = MapManager.Instance?.MapTree?.currentNode?.data;
        var slot = DictionaryUtil.Get(data, SunExpIds.TongtianTowerNodeSlotKey, (MapManager.Instance?.Level ?? 0).ToString());
        var nodeId = DictionaryUtil.Get(data, "NodeId", DictionaryUtil.Get(data, "Id", "unknown"));
        return TryEnqueue(new EndlessAbyssShockRequest
        {
            Key = "shock:" + EndlessBattleTrigger + ":floor:" + floor + ":slot:" + slot + ":node:" + nodeId,
            Trigger = EndlessBattleTrigger,
            Floor = floor,
            NativeLevel = MapManager.Instance?.Level ?? 0,
            NodeId = nodeId,
            NodeKind = nodeKind.ToString(),
            GazeLevelAtEnqueue = EndlessAbyssGazeService.CurrentLevel(),
            Source = source
        }, source);
    }

    public static EndlessAbyssShockRequest? PendingRequest()
    {
        try
        {
            var json = GameSaveManager.GetValue<string>(SunExpIds.EndlessAbyssPendingShockKey);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var request = JsonConvert.DeserializeObject<EndlessAbyssShockRequest>(json);
            if (request == null || string.IsNullOrWhiteSpace(request.Key))
            {
                ClearPending("invalid");
                return null;
            }

            if (EndlessAbyssRunLedger.Contains(request.Key))
            {
                ClearPending("already-claimed");
                return null;
            }

            return request;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssShock] pending load failed: " + ex.Message);
            ClearPending("load-failed");
            return null;
        }
    }

    public static EndlessAbyssShockResult ApplyPending(IEnumerable<string> optionIds, string source)
    {
        var request = PendingRequest();
        if (request == null)
        {
            return new EndlessAbyssShockResult
            {
                Success = false,
                Message = "\u6ca1\u6709\u5f85\u5904\u7406\u7684\u6df1\u6e0a\u9707\u8361\u3002"
            };
        }

        return Apply(request, optionIds, source, broadcast: true, validateRequired: true);
    }

    public static EndlessAbyssShockResult ApplyNetworkResolution(EndlessAbyssShockResolution? resolution, string source)
    {
        if (resolution?.Request == null)
        {
            return new EndlessAbyssShockResult
            {
                Success = false,
                Message = "\u6df1\u6e0a\u9707\u8361\u7f51\u7edc\u7ed3\u7b97\u7f3a\u5c11\u8bf7\u6c42\u3002"
            };
        }

        if (!TryClaimResolutionToken(resolution.Token, source))
        {
            return new EndlessAbyssShockResult
            {
                Success = true,
                Message = "\u672c\u6b21\u6df1\u6e0a\u9707\u8361\u5df2\u7ed3\u7b97\u3002"
            };
        }

        return Apply(
            resolution.Request,
            resolution.Options,
            source,
            broadcast: false,
            validateRequired: false);
    }

    private static bool TryEnqueue(EndlessAbyssShockRequest request, string source)
    {
        if (string.IsNullOrWhiteSpace(request.Key) || EndlessAbyssRunLedger.Contains(request.Key))
        {
            return false;
        }

        var pending = PendingRequest();
        if (pending != null)
        {
            return string.Equals(pending.Key, request.Key, StringComparison.Ordinal);
        }

        SetPending(request);
        SunExpLog.Info("[EndlessAbyssShock] enqueued "
            + request.Trigger
            + "; floor="
            + request.Floor
            + "; key="
            + request.Key
            + "; from="
            + source
            + ".");
        return true;
    }

    private static EndlessAbyssShockResult Apply(
        EndlessAbyssShockRequest request,
        IEnumerable<string> optionIds,
        string source,
        bool broadcast,
        bool validateRequired)
    {
        var selected = (optionIds ?? Array.Empty<string>())
            .Where(IsKnownOption)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var required = EndlessAbyssGazeService.RequiredShockChoices();
        if (validateRequired && selected.Count != required)
        {
            return new EndlessAbyssShockResult
            {
                Success = false,
                Message = "\u9700\u8981\u9009\u62e9 " + required + " \u4e2a\u6df1\u6e0a\u9707\u8361\u51b3\u7b56\u3002"
            };
        }

        if (EndlessAbyssRunLedger.Contains(request.Key))
        {
            ClearPending("already-applied");
            return new EndlessAbyssShockResult
            {
                Success = true,
                Message = "\u672c\u6b21\u6df1\u6e0a\u9707\u8361\u5df2\u7ed3\u7b97\u3002"
            };
        }

        var result = new EndlessAbyssShockResult { Success = true };
        var seedBase = request.Key + ":" + source + ":" + SunExpNetworkRuntime.LocalPlayerId();
        foreach (var option in selected)
        {
            ApplyOption(option, result, source, seedBase);
        }

        var passive = EndlessAbyssConfigStore.Current.Gaze.EndlessPassiveIncreasePerShock;
        if (string.Equals(request.Trigger, EndlessBattleTrigger, StringComparison.Ordinal) && passive > 0)
        {
            EndlessAbyssGazeService.Increase(passive, source + ":endless-passive");
        }

        EndlessAbyssRunLedger.TryClaim(request.Key, source);
        ClearPending("applied");
        result.Message = "\u6df1\u6e0a\u9707\u8361\u5df2\u7ed3\u7b97\uff0c\u5f53\u524d"
            + SunExpIds.EndlessAbyssGazeName
            + "\uff1a"
            + EndlessAbyssGazeService.CurrentLevel();
        PlayerApi.ShowCaption(result.Message);
        if (broadcast)
        {
            BroadcastResolution(request, selected, source);
        }

        return result;
    }

    private static void ApplyOption(string option, EndlessAbyssShockResult result, string source, string seedBase)
    {
        switch (option)
        {
            case EndlessAbyssShockOptionIds.DestroyRelic:
                if (!TongtianTowerPressureService.DestroyRandomEquippedRelic(
                        source + ":shock",
                        seedBase + ":" + option))
                {
                    EndlessAbyssGazeService.Increase(1, source + ":relic-fallback");
                }

                result.AppliedOptions.Add(option);
                break;
            case EndlessAbyssShockOptionIds.AnnihilateCards:
                var changed = TongtianTowerPressureService.AddAnnihilationToRandomDeckCards(
                    EndlessAbyssConfigStore.Current.Shock.AnnihilationCardCount,
                    source + ":shock",
                    seedBase + ":" + option);
                if (changed <= 0)
                {
                    EndlessAbyssGazeService.Increase(1, source + ":annihilation-fallback");
                }

                result.AppliedOptions.Add(option);
                break;
            case EndlessAbyssShockOptionIds.IncreaseGaze:
                EndlessAbyssGazeService.Increase(1, source + ":selected");
                result.AppliedOptions.Add(option);
                break;
        }
    }

    private static void BroadcastResolution(EndlessAbyssShockRequest request, IReadOnlyList<string> selected, string source)
    {
        if (!SunExpNetworkRuntime.IsMultiplayerSession() || SunExpNetworkRuntime.IsClientOnly())
        {
            return;
        }

        var safeSource = source ?? "";
        var resolution = new EndlessAbyssShockResolution
        {
            Request = request,
            Options = selected.ToList(),
            Source = safeSource,
            Token = Guid.NewGuid().ToString("N")
        };
        var snapshot = TongtianTowerStateSnapshot.Capture(safeSource + ":shock-resolution");
        SunExpNetworkRuntime.Send(
            new RpcEndlessAbyssShockResolution(resolution, snapshot, safeSource),
            safeSource);
    }

    private static bool IsKnownOption(string option)
    {
        return string.Equals(option, EndlessAbyssShockOptionIds.DestroyRelic, StringComparison.Ordinal)
            || string.Equals(option, EndlessAbyssShockOptionIds.AnnihilateCards, StringComparison.Ordinal)
            || string.Equals(option, EndlessAbyssShockOptionIds.IncreaseGaze, StringComparison.Ordinal);
    }

    private static bool TryClaimResolutionToken(string? token, string source)
    {
        var safeToken = (token ?? "").Trim();
        if (safeToken.Length == 0)
        {
            return true;
        }

        lock (ResolutionTokenSync)
        {
            if (!ProcessedResolutionTokens.Add(safeToken))
            {
                SunExpLog.Debug("[EndlessAbyssShock] duplicate resolution ignored from "
                    + source
                    + "; token="
                    + safeToken);
                return false;
            }

            ProcessedResolutionTokenOrder.Enqueue(safeToken);
            while (ProcessedResolutionTokenOrder.Count > ProcessedResolutionTokenLimit)
            {
                ProcessedResolutionTokens.Remove(ProcessedResolutionTokenOrder.Dequeue());
            }
        }

        return true;
    }

    private static void SetPending(EndlessAbyssShockRequest request)
    {
        SetValue(SunExpIds.EndlessAbyssPendingShockKey, JsonConvert.SerializeObject(request));
    }

    private static void ClearPending(string source)
    {
        SetValue(SunExpIds.EndlessAbyssPendingShockKey, "");
        SunExpLog.Debug("[EndlessAbyssShock] cleared pending from " + source + ".");
    }

    private static void SetValue(string key, string value)
    {
        try
        {
            GameSaveManager.SetValue(key, value);
        }
        catch
        {
            try
            {
                GameSaveManager.GetNowSave()?.SetValue(key, value);
            }
            catch
            {
                var save = GameSaveManager.GetNowSave();
                if (save?.GameVars != null)
                {
                    save.GameVars[key] = value;
                }
            }
        }
    }
}
