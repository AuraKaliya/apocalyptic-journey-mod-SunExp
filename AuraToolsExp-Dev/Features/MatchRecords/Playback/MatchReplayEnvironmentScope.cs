using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using Newtonsoft.Json;
using UnityEngine;
using Witch;
using Witch.UI;
using Witch.UI.Window;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayEnvironmentScope
{
    private static RoleTable? previousRoleTable;
    private static GameObject? previousBackground;
    private static bool previousBackgroundActive;
    private static GameObject? replayBackground;
    private static bool previousHouseActive;
    private static int previousPlayerCount;
    private static Dictionary<string, List<string>>? previousRoleStatusMap;
    private static float[]? previousRandomSeeds;
    private static float[]? replayRandomSeeds;
    private static MapManager? replayMapManager;
    private static IModeManager? previousModeMapManager;
    private static string previousMapMode = "";
    private static MatchReplayModeContext? replayModeContext;
    private static bool captured;

    internal static void CaptureAndInstallRoleTable(MatchReplayInitialState initialState)
    {
        if (captured)
        {
            throw new InvalidOperationException("Replay environment is already active.");
        }

        var runtimeData = Singleton<GameRuntimeData>.Instance;
        previousRoleTable = runtimeData.roleTable;
        previousBackground = GameApp.Instance?.NowBackground;
        previousBackgroundActive = previousBackground != null && previousBackground.activeSelf;
        previousHouseActive = GameApp.Instance?.HouseItem != null && GameApp.Instance.HouseItem.activeSelf;
        previousPlayerCount = GameEntryUI.playerCount;
        previousRandomSeeds = TempDataManager.seeds;
        previousRoleStatusMap = Singleton<TempDataManager>.Instance.RoleStatusMap.ToDictionary(
            item => item.Key,
            item => item.Value == null ? new List<string>() : new List<string>(item.Value));

        var replayRole = JsonConvert.DeserializeObject<RoleTable>(initialState.RoleTableJson)
                         ?? throw new InvalidOperationException("回放缺少可恢复的角色初始状态。");
        replayModeContext = MatchReplayModeContext.Create(initialState);
        runtimeData.roleTable = replayRole;
        GameEntryUI.playerCount = 1;
        if (TempDataManager.seeds == null || TempDataManager.seeds.Length == 0)
        {
            replayRandomSeeds = MatchReplayBootstrapRandomPool.Create(
                (initialState.LevelId ?? "")
                + "|" + (initialState.DiceJson ?? "")
                + "|" + (initialState.RoleTableJson ?? ""));
            TempDataManager.seeds = replayRandomSeeds;
        }
        captured = true;
    }

    internal static bool TryInstallMapContext(MapManager? map)
    {
        if (!captured || map == null || replayModeContext == null)
        {
            return false;
        }

        if (ReferenceEquals(replayMapManager, map)
            && ReferenceEquals(map.ModeMapManager, replayModeContext))
        {
            return true;
        }

        if (replayMapManager != null && !ReferenceEquals(replayMapManager, map))
        {
            throw new InvalidOperationException("Replay MapManager changed while the local host was starting.");
        }

        replayMapManager = map;
        previousModeMapManager = map.ModeMapManager;
        previousMapMode = map.CurrentMode ?? "";
        map.ModeMapManager = replayModeContext;
        map.CurrentMode = replayModeContext.MapMode;
        return true;
    }

    internal static bool IsMapContextInstalled(MapManager? map)
    {
        return map != null
               && replayModeContext != null
               && ReferenceEquals(replayMapManager, map)
               && ReferenceEquals(map.ModeMapManager, replayModeContext);
    }

    internal static string DescribeMapContext()
    {
        return replayModeContext == null
            ? "unavailable"
            : "mode=" + replayModeContext.MapMode
              + ", level=" + replayModeContext.Level
              + ", dice=" + replayModeContext.DiceSource
              + ", randomPool=" + DescribeRandomPool();
    }

    internal static bool IsRandomPoolReady =>
        TempDataManager.seeds != null && TempDataManager.seeds.Length > 0;

    internal static bool UsesCompatibilityDice =>
        replayModeContext != null
        && !replayModeContext.HasRecordedDiceMetadata;

    internal static string InstallPresentationScene(string requestedScene)
    {
        if (!captured || GameApp.Instance == null)
        {
            throw new InvalidOperationException("Replay environment was not captured.");
        }

        if (replayBackground != null)
        {
            return replayBackground.name;
        }

        if (previousBackground != null)
        {
            previousBackground.SetActive(false);
        }

        if (GameApp.Instance.HouseItem != null)
        {
            GameApp.Instance.HouseItem.SetActive(false);
        }

        var candidates = new List<string>();
        var normalized = NormalizeSceneName(requestedScene);
        if (!string.IsNullOrWhiteSpace(normalized)) candidates.Add(normalized);
        candidates.Add(SceneType.Forest.ToString());

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var prefab = ResourceLoader.Load("UI/Scene/" + candidate);
            var instance = prefab == null ? null : Object.Instantiate(prefab) as GameObject;
            if (instance == null)
            {
                continue;
            }

            if (instance.transform.Find("com")?.GetComponent<SceneInfo>() == null)
            {
                Object.Destroy(instance);
                continue;
            }

            instance.name = candidate;
            replayBackground = instance;
            GameApp.Instance.NowBackground = instance;
            return candidate;
        }

        throw new InvalidOperationException("无法加载回放战斗背景资源。");
    }

    internal static void RestoreInitialRoleTable(string json)
    {
        if (RoleTable.Instance == null || string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Replay role table is unavailable.");
        }

        var restored = JsonConvert.DeserializeObject<RoleTable>(json)
                       ?? throw new InvalidOperationException("Replay role table cannot be deserialized.");
        RoleTable.Instance.ResetFight(restored);
    }

    internal static void Restore()
    {
        if (!captured)
        {
            return;
        }

        Exception? failure = null;
        try
        {
            try
            {
                if (replayMapManager != null
                    && replayModeContext != null
                    && ReferenceEquals(replayMapManager.ModeMapManager, replayModeContext))
                {
                    replayMapManager.ModeMapManager = previousModeMapManager;
                    replayMapManager.CurrentMode = previousMapMode;
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            try
            {
                if (replayBackground != null)
                {
                    Object.Destroy(replayBackground);
                }

                if (GameApp.Instance != null)
                {
                    GameApp.Instance.NowBackground = previousBackground;
                    if (previousBackground != null)
                    {
                        previousBackground.SetActive(previousBackgroundActive);
                    }

                    if (GameApp.Instance.HouseItem != null)
                    {
                        GameApp.Instance.HouseItem.SetActive(previousHouseActive);
                    }
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            try
            {
                if (previousHouseActive)
                {
                    AudioManager.Instance?.PlayBGMList("HouseBGM");
                }
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }

            try
            {
                Singleton<GameRuntimeData>.Instance.roleTable = previousRoleTable;
                GameEntryUI.playerCount = previousPlayerCount;
                TempDataManager.seeds = previousRandomSeeds;
                Singleton<TempDataManager>.Instance.RoleStatusMap = previousRoleStatusMap
                    ?? new Dictionary<string, List<string>>();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }
        finally
        {
            previousRoleTable = null;
            previousBackground = null;
            replayBackground = null;
            previousBackgroundActive = false;
            previousHouseActive = false;
            previousPlayerCount = 0;
            previousRoleStatusMap = null;
            previousRandomSeeds = null;
            replayRandomSeeds = null;
            replayMapManager = null;
            previousModeMapManager = null;
            previousMapMode = "";
            replayModeContext = null;
            captured = false;
        }

        if (failure != null)
        {
            throw new InvalidOperationException("Replay environment restoration was incomplete.", failure);
        }
    }

    private static string NormalizeSceneName(string value)
    {
        var normalized = (value ?? "").Trim();
        const string cloneSuffix = "(Clone)";
        if (normalized.EndsWith(cloneSuffix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(0, normalized.Length - cloneSuffix.Length).Trim();
        }

        return Enum.TryParse(normalized, true, out SceneType parsed) ? parsed.ToString() : "";
    }

    private static string DescribeRandomPool()
    {
        var seeds = TempDataManager.seeds;
        if (seeds == null || seeds.Length == 0)
        {
            return "missing";
        }

        return (ReferenceEquals(seeds, replayRandomSeeds) ? "replay-bootstrap:" : "ambient:")
               + seeds.Length;
    }
}
