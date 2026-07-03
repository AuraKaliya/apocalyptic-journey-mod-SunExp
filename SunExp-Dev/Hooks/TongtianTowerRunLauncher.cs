using System;
using System.Collections.Generic;
using Data.Save;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class TongtianTowerRunLauncher
{
    public static void Start(ModeChoiceUI modeChoice)
    {
        try
        {
            var saveInfo = CreateSave();
            GameSaveManager.Select(saveInfo);
            GameEntryUI.selectedSave = saveInfo;
            LobbyManager.Instance?.SetLobbyModeType("Normal");

            if (PlayerManager.Instance == null)
            {
                GameCompatibilityApi.StartLobby();
            }
            else if (!PlayerManager.Instance.isServer)
            {
                modeChoice.Close();
                UIManager.Instance.ShowUI<GameEntryUI>("GameEntryUI", true).Init();
                UIManager.Instance.GetUI<CaptionUI>("CaptionUI")
                    .ShowCaption("Only the host can start the game".Localize("GameEntryUI"), CaptionStyle.Top, 1f, 1.5f, 3);
                return;
            }

            modeChoice.Close();
            UIManager.Instance.ShowUI<GameEntryUI>("GameEntryUI", true).Init();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Tongtian tower run start failed", ex);
        }
    }

    public static SaveInfo CreateSave()
    {
        var random = new Random((int)DateTime.Now.Ticks);
        var seed = random.Next(0, (int)Math.Pow(10.0, 16.0) - 1).ToString();
        var saveInfo = new SaveInfo
        {
            CreatedTime = DateTime.Now.ToString("yyyy-MM-dd,HH:mm"),
            Version = GameConfigManager.Version,
            isCheat = false,
            Name = "SunExpTongtianTower" + UnityEngine.Random.Range(0, 100000),
            roleTable = new Dictionary<string, RoleTable>(),
            mapTree = new MapTree(),
            HardTags = SunExpHardTagRuntime.SelectedRuntimeHardTags(),
            startTime = DateTime.Now,
            modeType = "Normal",
            Seed = seed
        };

        saveInfo.ItemOpers.PlayerId = Singleton<GameConfigManager>.Instance.PlayerId;
        saveInfo.GameVars[SunExpIds.TongtianTowerModeKey] = "1";
        saveInfo.GameVars[SunExpIds.TongtianTowerFloorKey] = "1";
        saveInfo.GameVars[SunExpIds.TongtianTowerGeneratedFloorKey] = "0";
        saveInfo.GameVars[SunExpIds.TongtianTowerSeedKey] = seed;
        saveInfo.GameVars[SunExpIds.TongtianTowerIntroSeenKey] = "0";
        saveInfo.GameVars[SunExpIds.TongtianTowerStarterDeckAppliedKey] = "0";
        saveInfo.GameVars[SunExpIds.TongtianTowerStarterDeckModeKey] = "";
        saveInfo.GameVars[GameVar.ExLockDes.ToString()] = "4";
        saveInfo.GameVars[GameVar.ExDeleteDes.ToString()] = "0";
        saveInfo.GameVars["MapScene1"] = (random.Next(0, 100) < 50 ? SceneType.Courtyard : SceneType.Forest).ToString();
        saveInfo.GameVars["MapScene2"] = SceneType.SlotMachScene.ToString();
        saveInfo.GameVars["MapScene3"] = (random.Next(0, 100) < 50 ? SceneType.Castle : SceneType.Chessboard).ToString();
        return saveInfo;
    }
}
