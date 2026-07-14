using System;
using System.Collections.Generic;
using AuraUi.Shared;
using Data.Save;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class EndlessSeaRunLauncher
{
    private const string NativeMapModeType = SunExpIds.NativeNormalModeType;
    private const string PromptName = "SunExp_EndlessSeaContinuePrompt";
    private static readonly Color PromptBackdrop = new(0f, 0f, 0f, 0.62f);
    private static readonly Color PromptTint = new(0.02f, 0.018f, 0.08f, 0.98f);
    private static readonly Color PromptTitle = new(1f, 0.84f, 0.42f, 1f);
    private static readonly Color PromptText = new(0.9f, 0.88f, 0.78f, 1f);
    private static GameObject? activePrompt;

    public static void Start(ModeChoiceUI modeChoice)
    {
        try
        {
            var saveInfo = EndlessSeaRunStateStore.FindLatestUnfinishedRun();
            if (saveInfo != null)
            {
                ShowContinuePrompt(modeChoice, saveInfo);
                return;
            }

            StartNewRun(modeChoice, deleteExistingRuns: false);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Sea run start failed", ex);
        }
    }

    private static void ShowContinuePrompt(ModeChoiceUI modeChoice, SaveInfo saveInfo)
    {
        CloseContinuePrompt();
        var parent = SunExpModalHost.ModalParent();
        if (parent == null)
        {
            ContinueRun(modeChoice, saveInfo);
            return;
        }

        activePrompt = SunExpModalHost.CreateFullscreenRoot(PromptName, parent, PromptBackdrop);
        var window = SunExpUiBuilder.CreateRect(
            "Window",
            activePrompt.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(540f, 238f));
        SunExpUiBuilder.ApplyPanelImage(
            window.gameObject,
            SunExpUiSprites.Panel("[EndlessSeaRunLauncher]"),
            PromptTint,
            false);

        var layout = window.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 22, 22);
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        AddTextBlock(window.transform, "\u901a\u5929\u4e4b\u5854", 28, FontStyle.Bold, TextAnchor.MiddleCenter, PromptTitle, 42f);
        AddTextBlock(
            window.transform,
            "\u68c0\u6d4b\u5230\u672a\u5b8c\u6210\u7684\u6311\u6218\u3002\u662f\u5426\u7ee7\u7eed\u4e0a\u6b21\u8fdb\u5ea6\uff1f",
            18,
            FontStyle.Normal,
            TextAnchor.MiddleCenter,
            PromptText,
            54f);

        var buttons = CreateLayoutObject("Buttons", window.transform);
        var buttonsElement = buttons.gameObject.AddComponent<LayoutElement>();
        buttonsElement.minHeight = 58f;
        buttonsElement.preferredHeight = 58f;
        var buttonLayout = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
        buttonLayout.spacing = 20f;
        buttonLayout.childAlignment = TextAnchor.MiddleCenter;
        buttonLayout.childControlWidth = true;
        buttonLayout.childControlHeight = true;
        buttonLayout.childForceExpandWidth = false;
        buttonLayout.childForceExpandHeight = true;

        CreatePromptButton(buttons.transform, "\u7ee7\u7eed\u6311\u6218", () =>
        {
            CloseContinuePrompt();
            ContinueRun(modeChoice, saveInfo);
        });
        CreatePromptButton(buttons.transform, "\u91cd\u65b0\u5f00\u59cb", () =>
        {
            CloseContinuePrompt();
            StartNewRun(modeChoice, deleteExistingRuns: true);
        });
    }

    private static void ContinueRun(ModeChoiceUI modeChoice, SaveInfo saveInfo)
    {
        try
        {
            EndlessSeaRunStateStore.RepairSave(saveInfo, "EndlessSeaRunLauncher.Continue");
            SunExpLog.Info("[EndlessSeaRunLauncher] continuing run; save="
                + saveInfo.Name
                + "; floor="
                + saveInfo.GetValue<int>(SunExpIds.EndlessSeaFloorKey));
            LaunchSelectedSave(modeChoice, saveInfo);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Sea continue failed", ex);
        }
    }

    private static void StartNewRun(ModeChoiceUI modeChoice, bool deleteExistingRuns)
    {
        try
        {
            if (deleteExistingRuns)
            {
                EndlessSeaRunStateStore.DeleteUnfinishedRuns("EndlessSeaRunLauncher.NewRun");
                ModeChoiceSaveCacheApi.ForgetSelectedSaveIf(
                    EndlessSeaRunStateStore.IsEndlessSeaSave,
                    "EndlessSeaRunLauncher.NewRun");
                EndlessSeaSaveCacheRuntime.ClearNativeNormalCache("EndlessSeaRunLauncher.NewRun");
            }

            var saveInfo = CreateSave();
            SunExpLog.Info("[EndlessSeaRunLauncher] created new run; save=" + saveInfo.Name + ".");
            LaunchSelectedSave(modeChoice, saveInfo);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless Sea new run failed", ex);
        }
    }

    private static void LaunchSelectedSave(ModeChoiceUI modeChoice, SaveInfo saveInfo)
    {
        CloseContinuePrompt();
        EndlessSeaRunStateStore.RepairSave(saveInfo, "EndlessSeaRunLauncher.Launch");
        GameSaveManager.Select(saveInfo);
        GameEntryUI.selectedSave = saveInfo;
        LobbyManager.Instance?.SetLobbyModeType(NativeMapModeType);
        EndlessSeaSaveCacheRuntime.ClearNativeNormalCache("EndlessSeaRunLauncher.Launch");
        SunExpLog.Info("[EndlessSeaRunLauncher] launching save="
            + saveInfo.Name
            + "; saveMode="
            + saveInfo.modeType
            + "; nativeMode="
            + NativeMapModeType
            + "; floor="
            + saveInfo.GetValue<int>(SunExpIds.EndlessSeaFloorKey)
            + "; runId="
            + saveInfo.GetValue<string>(SunExpIds.EndlessSeaRunIdKey)
            + ".");

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

    private static void CloseContinuePrompt()
    {
        SunExpModalHost.Close(ref activePrompt, "EndlessSeaRunLauncher.ClosePrompt", "[EndlessSeaRunLauncher]");
    }

    private static RectTransform CreateLayoutObject(string name, Transform parent)
    {
        return SunExpUiBuilder.CreateRect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);
    }

    private static Text AddTextBlock(
        Transform parent,
        string value,
        int fontSize,
        FontStyle style,
        TextAnchor alignment,
        Color color,
        float preferredHeight)
    {
        var rect = CreateLayoutObject("Text", parent);
        var element = rect.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = preferredHeight;
        element.minHeight = preferredHeight;

        var text = rect.gameObject.AddComponent<Text>();
        text.text = value;
        text.font = AuraUiNativeBridge.ResolveLegacyFont();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Math.Max(12, fontSize - 6);
        text.resizeTextMaxSize = fontSize;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreatePromptButton(Transform parent, string label, Action action)
    {
        var rect = CreateLayoutObject("Button", parent);
        var element = rect.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = 172f;
        element.minWidth = 172f;
        element.preferredHeight = 50f;
        element.minHeight = 50f;

        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = SunExpUiSprites.Button("[EndlessSeaRunLauncher]");
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = image.sprite != null ? Color.white : new Color(0.08f, 0.065f, 0.16f, 0.98f);

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(new UnityAction(action));
        AddTextBlock(rect.transform, label, 18, FontStyle.Bold, TextAnchor.MiddleCenter, PromptTitle, 50f);
        return button;
    }

    public static SaveInfo CreateSave()
    {
        var random = new System.Random((int)DateTime.Now.Ticks);
        var seed = random.Next(0, (int)Math.Pow(10.0, 16.0) - 1).ToString();
        var saveInfo = new SaveInfo
        {
            CreatedTime = DateTime.Now.ToString("yyyy-MM-dd,HH:mm"),
            Version = GameConfigManager.Version,
            isCheat = false,
            Name = "SunExpEndlessSea" + UnityEngine.Random.Range(0, 100000),
            roleTable = new Dictionary<string, RoleTable>(),
            mapTree = new MapTree(),
            HardTags = SunExpHardTagRuntime.SelectedRuntimeHardTags(),
            startTime = DateTime.Now,
            modeType = NativeMapModeType,
            Seed = seed
        };

        saveInfo.ItemOpers.PlayerId = Singleton<GameConfigManager>.Instance.PlayerId;
        EndlessSeaRunStateStore.InitializeNewRun(saveInfo, seed);
        saveInfo.GameVars[GameVar.ExLockDes.ToString()] = "0";
        saveInfo.GameVars[GameVar.ExDeleteDes.ToString()] = "0";
        saveInfo.GameVars["MapScene1"] = (random.Next(0, 100) < 50 ? SceneType.Courtyard : SceneType.Forest).ToString();
        saveInfo.GameVars["MapScene2"] = SceneType.SlotMachScene.ToString();
        saveInfo.GameVars["MapScene3"] = (random.Next(0, 100) < 50 ? SceneType.Castle : SceneType.Chessboard).ToString();
        return saveInfo;
    }
}
