using System;
using System.Collections;
using AuraUi.Shared;
using Data.Save;
using Michsky.MUIP;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;
using Object = UnityEngine.Object;
using GameUIManager = Witch.UI.UIManager;

namespace Terrias.Dll.Hooks.Ui;

public static class EndlessAbyssEvacuationButtonRuntime
{
    private const string ButtonName = "Terrias_EndlessAbyssEvacuationButton";
    private const string IconPath = "Mods/Terrias/ModResource/Images/UI/\u65e0\u5c3d\u4e4b\u6e0a-\u9000\u51fa.png";
    private const string HoverHint = "\u7ed3\u7b97\u9000\u51fa";

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "TopBarUI.Awake", RefreshFromHook);
        RegisterAfter(modConfig, "TopBarUI.Start", RefreshFromHook);
        RegisterAfter(modConfig, "TopBarUI.ShowLeftUp", RefreshFromHook);
        RegisterAfter(modConfig, "TopBarUI.HideLeftUp", HideFromHook);
        RegisterAfter(modConfig, "MapSelectUI.Start", RefreshForAdventureUi);
        RegisterAfter(modConfig, "MapSelectUI.ReadyToSelect", RefreshForAdventureUi);
        RegisterAfter(modConfig, "MapSelectUI.ShowMap", RefreshForAdventureUi);
        RegisterAfter(modConfig, "MapSelectUI.MapAnimation", RefreshForAdventureUi);
    }

    internal static void HandleButtonClicked()
    {
        TerriasLog.Info("[EndlessAbyssEvacuation] toolbar click received.");
        EndlessAbyssEvacuationRuntime.RequestFromToolbar();
    }

    public static void Refresh()
    {
        try
        {
            var topBar = GameUIManager.Instance?.GetUI<TopBarUI>("TopBarUI");
            if (topBar != null)
            {
                Refresh(topBar);
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[EndlessAbyssEvacuationButton] refresh failed: " + ex.Message);
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "EndlessAbyssEvacuationButton");
    }

    private static void RefreshFromHook(ModHookContext context)
    {
        if (context.Target is TopBarUI topBar)
        {
            Refresh(topBar);
            ScheduleRefresh();
        }
    }

    private static void RefreshForAdventureUi(ModHookContext context)
    {
        Refresh();
        ScheduleRefresh(2);
    }

    private static void HideFromHook(ModHookContext context)
    {
        if (context.Target is TopBarUI topBar)
        {
            SetActive(topBar, false);
        }
    }

    private static void Refresh(TopBarUI topBar)
    {
        if (EnsureButton(topBar))
        {
            SetActive(topBar, ShouldShow());
        }
    }

    private static bool EnsureButton(TopBarUI topBar)
    {
        var buttons = topBar.transform.Find("Content/Buttons");
        var template = buttons?.Find("CardBack");
        if (buttons == null || template == null)
        {
            return false;
        }

        var existing = buttons.Find(ButtonName);
        if (existing != null)
        {
            Bind(existing.gameObject);
            Configure(existing.gameObject, logDiagnostics: false);
            return true;
        }

        var buttonObject = Object.Instantiate(template.gameObject, buttons);
        buttonObject.name = ButtonName;
        buttonObject.transform.SetSiblingIndex(Math.Min(buttons.childCount - 1, template.GetSiblingIndex() + 1));
        buttonObject.SetActive(false);
        Bind(buttonObject);
        Configure(buttonObject, logDiagnostics: true);
        return true;
    }

    private static void Bind(GameObject buttonObject)
    {
        var manager = buttonObject.GetComponent<ButtonManager>();
        if (manager != null)
        {
            manager.onClick.RemoveAllListeners();
            manager.onDoubleClick.RemoveAllListeners();
            manager.onRightClick.RemoveAllListeners();
            manager.Interactable(true);
        }

        buttonObject.GetComponent<Button>()?.onClick.RemoveAllListeners();
        foreach (var component in buttonObject.GetComponents<UnityEngine.Component>())
        {
            if (component != null && component.GetType().Name == "KeyItem")
            {
                if (component is Behaviour behaviour)
                {
                    behaviour.enabled = false;
                }

                Object.Destroy(component);
            }
        }

        AuraUiNativeHoverHint.Attach(buttonObject, HoverHint);

        if (buttonObject.GetComponent<EndlessAbyssEvacuationButtonRelay>() == null)
        {
            buttonObject.AddComponent<EndlessAbyssEvacuationButtonRelay>();
        }
    }

    private static void Configure(GameObject buttonObject, bool logDiagnostics)
    {
        const string label = "\u64a4\u79bb";
        var manager = buttonObject.GetComponent<ButtonManager>();
        if (manager != null)
        {
            var icon = TerriasResourceCache.Load<Sprite>(IconPath, true, "ui.endless-abyss-evacuation");
            manager.enableIcon = icon != null;
            manager.enableText = icon == null;
            manager.buttonText = label;
            if (icon != null)
            {
                var result = AuraUiNativeButtonIconOwner.Apply(manager, icon);
                if (logDiagnostics || !result.Success)
                {
                    var message = "[EndlessAbyssEvacuationButton] icon apply: success="
                                  + result.Success
                                  + ", ownedStates="
                                  + result.OwnedStateCount
                                  + ", customContent="
                                  + result.UsedCustomContent
                                  + ", resource="
                                  + IconPath
                                  + (result.Success ? "." : ", reason=" + result.FailureReason + ".");
                    if (result.Success)
                    {
                        TerriasLog.Info(message);
                    }
                    else
                    {
                        TerriasLog.Warn(message);
                    }
                }
            }
            else
            {
                manager.SetText(label);
                if (logDiagnostics)
                {
                    TerriasLog.Warn("[EndlessAbyssEvacuationButton] icon resource missing: " + IconPath + ".");
                }
            }

            manager.UpdateUI();
        }

        if (manager == null || manager.enableText)
        {
            var text = buttonObject.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                text.text = label;
            }
        }
    }

    private static bool ShouldShow()
    {
        return GameUIManager.Instance != null
               && RoleTable.Instance != null
               && EndlessSeaRunStateStore.IsEndlessSeaSave(GameSaveManager.GetNowSave())
               && string.Equals(EndlessSeaRunStateStore.CurrentPhase(), EndlessSeaRunPhase.MapPlanning, StringComparison.Ordinal)
               && (FightManager.Instance == null || FightManager.Instance.fightType == FightType.None)
               && IsActiveUi<MapSelectUI>("MapSelectUI")
               && !IsActiveUi<GameExitUI>("GameExitUI");
    }

    private static bool IsActiveUi<T>(string name) where T : UIBase
    {
        try
        {
            var ui = GameUIManager.Instance?.GetUI<T>(name);
            return ui != null && ui.gameObject.activeInHierarchy;
        }
        catch
        {
            return false;
        }
    }

    private static void SetActive(TopBarUI topBar, bool active)
    {
        var button = topBar.transform.Find("Content/Buttons/" + ButtonName)?.gameObject;
        if (button != null)
        {
            button.SetActive(active);
        }
    }

    private static void ScheduleRefresh(int frames = 1)
    {
        try
        {
            GameUIManager.Instance?.StartCoroutine(RefreshAfterFrames(frames));
        }
        catch
        {
        }
    }

    private static IEnumerator RefreshAfterFrames(int frames)
    {
        for (var i = 0; i < Math.Max(1, frames); i++)
        {
            yield return null;
        }

        Refresh();
    }
}

internal sealed class EndlessAbyssEvacuationButtonRelay : MonoBehaviour, IPointerClickHandler, ISubmitHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            EndlessAbyssEvacuationButtonRuntime.HandleButtonClicked();
        }
    }

    public void OnSubmit(BaseEventData eventData)
    {
        EndlessAbyssEvacuationButtonRuntime.HandleButtonClicked();
    }
}
