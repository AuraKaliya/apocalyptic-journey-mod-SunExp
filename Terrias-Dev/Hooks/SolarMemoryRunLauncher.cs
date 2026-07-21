using System;
using System.Collections.Generic;
using Data.Save;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public static class SolarMemoryRunLauncher
{
    public static void Start(ModeChoiceUI modeChoice, List<string> selectedPacks)
    {
        try
        {
            var saveInfo = CreateSave(selectedPacks);
            SolarMemoryStarterDeckRuntime.CaptureSelectedPacks(selectedPacks);
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
            TerriasLog.Error("Solar memory run start failed", ex);
        }
    }

    public static SaveInfo CreateSave(List<string> selectedPacks)
    {
        var random = new Random((int)DateTime.Now.Ticks);
        var saveInfo = new SaveInfo
        {
            CreatedTime = DateTime.Now.ToString("yyyy-MM-dd,HH:mm"),
            Version = GameConfigManager.Version,
            isCheat = false,
            Name = "TerriasSolarMemory" + UnityEngine.Random.Range(0, 100000),
            roleTable = new Dictionary<string, RoleTable>(),
            mapTree = new MapTree(),
            HardTags = TerriasHardTagRuntime.SelectedRuntimeHardTags(),
            startTime = DateTime.Now,
            modeType = "Normal",
            Seed = random.Next(0, (int)Math.Pow(10.0, 16.0) - 1).ToString()
        };

        saveInfo.ItemOpers.PlayerId = Singleton<GameConfigManager>.Instance.PlayerId;
        saveInfo.GameVars[TerriasIds.SolarMemoryModeKey] = "1";
        TerriasModeApi.ActivateSolarMemory(saveInfo, "SolarMemoryRunLauncher.CreateSave");
        saveInfo.GameVars[TerriasIds.SolarMemorySelectedPacksKey] = string.Join("|", selectedPacks);
        saveInfo.GameVars[TerriasIds.SolarMemoryOriginPointsKey] = "50";
        saveInfo.GameVars[TerriasIds.SolarMemoryBlessPickCountKey] = "0";
        saveInfo.GameVars[TerriasIds.SolarMemoryBlessSelectedIdsKey] = "";
        saveInfo.GameVars[TerriasIds.SolarMemoryDeckConfiguredKey] = "0";
        saveInfo.GameVars[TerriasIds.SolarMemoryStarterDeckAppliedKey] = "0";
        saveInfo.GameVars[TerriasIds.SolarMemoryStarterDeckModeKey] = "";
        saveInfo.GameVars[TerriasIds.SolarMemoryOriginConfiguredKey] = "0";
        saveInfo.GameVars[TerriasIds.SolarMemoryBlessConfiguredKey] = "0";
        saveInfo.GameVars[TerriasIds.SolarMemorySetupFinishedKey] = "0";
        saveInfo.GameVars[TerriasIds.SolarMemoryPrepStepKey] = SolarMemoryPrepStep.DeckSelection.ToString();
        saveInfo.GameVars[TerriasIds.SolarMemoryPreparedKey] = "0";
        saveInfo.GameVars[TerriasIds.SolarMemoryPostPreparationDialogueSeenKey] = "0";
        saveInfo.GameVars[TerriasIds.SolarMemoryPostPreparationDialoguePendingKey] = "0";
        saveInfo.GameVars[TerriasIds.HardSunsetFightCountKey] = "0";
        saveInfo.GameVars["MapScene1"] = (random.Next(0, 100) < 50 ? SceneType.Courtyard : SceneType.Forest).ToString();
        saveInfo.GameVars["MapScene2"] = SceneType.SlotMachScene.ToString();
        saveInfo.GameVars["MapScene3"] = (random.Next(0, 100) < 50 ? SceneType.Castle : SceneType.Chessboard).ToString();
        return saveInfo;
    }
}
