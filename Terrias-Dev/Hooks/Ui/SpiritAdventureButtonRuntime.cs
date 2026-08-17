using System;
using System.Collections;
using AuraUi.Shared;
using Michsky.MUIP;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;
using Object = UnityEngine.Object;
using GameUIManager = Witch.UI.UIManager;

namespace Terrias.Dll.Hooks.Ui;

public static class SpiritAdventureButtonRuntime
{
    private const string ButtonName = "Terrias_SpiritAdventureButton";

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "TopBarUI.Awake", RefreshFromHook);
        RegisterAfter(modConfig, "TopBarUI.Start", RefreshFromHook);
        RegisterAfter(modConfig, "TopBarUI.ShowLeftUp", RefreshFromHook);
        RegisterAfter(modConfig, "TopBarUI.HideLeftUp", HideFromHook);
        RegisterAfter(modConfig, "MapSelectUI.Start", _ => ScheduleRefresh());
        RegisterAfter(modConfig, "MapSelectUI.ReadyToSelect", _ => ScheduleRefresh());
        RegisterAfter(modConfig, "MapSelectUI.ShowMap", _ => ScheduleRefresh());
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "SpiritAdventureButton");
    }

    private static void RefreshFromHook(ModHookContext context)
    {
        if (context.Target is TopBarUI topBar) Refresh(topBar);
        ScheduleRefresh();
    }

    private static void HideFromHook(ModHookContext context)
    {
        if (context.Target is TopBarUI topBar) SetActive(topBar, false);
    }

    private static void Refresh()
    {
        try
        {
            var topBar = GameUIManager.Instance?.GetUI<TopBarUI>("TopBarUI");
            if (topBar != null) Refresh(topBar);
        }
        catch (Exception ex) { TerriasLog.Warn("[SpiritAdventureButton] refresh failed: " + ex.Message); }
    }

    private static void Refresh(TopBarUI topBar)
    {
        if (!EnsureButton(topBar)) return;
        var show = RoleTable.Instance != null
                   && (FightManager.Instance == null || FightManager.Instance.fightType == FightType.None)
                   && GameUIManager.Instance?.GetUI<MapSelectUI>("MapSelectUI")?.gameObject.activeInHierarchy == true;
        SetActive(topBar, show);
    }

    private static bool EnsureButton(TopBarUI topBar)
    {
        var buttons = topBar.transform.Find("Content/Buttons");
        var template = buttons?.Find("CardBack");
        if (buttons == null || template == null) return false;
        var target = buttons.Find(ButtonName)?.gameObject;
        if (target == null)
        {
            target = Object.Instantiate(template.gameObject, buttons);
            target.name = ButtonName;
            target.transform.SetSiblingIndex(Math.Min(buttons.childCount - 1, template.GetSiblingIndex() + 1));
        }
        if (target.GetComponent<TerriasLocalizationScope>() == null)
        {
            var localization = TerriasLocalizationScope.Attach(target);
            localization.RegisterRefresh(() => Configure(target));
        }
        Bind(target);
        Configure(target);
        return true;
    }

    private static void Bind(GameObject target)
    {
        var manager = target.GetComponent<ButtonManager>();
        if (manager != null)
        {
            manager.onClick.RemoveAllListeners();
            manager.onDoubleClick.RemoveAllListeners();
            manager.onRightClick.RemoveAllListeners();
            manager.Interactable(true);
        }
        target.GetComponent<Button>()?.onClick.RemoveAllListeners();
        foreach (var component in target.GetComponents<UnityEngine.Component>())
        {
            if (component == null || component.GetType().Name != "KeyItem") continue;
            if (component is Behaviour behaviour) behaviour.enabled = false;
            Object.Destroy(component);
        }
        if (target.GetComponent<SpiritAdventureButtonRelay>() == null) target.AddComponent<SpiritAdventureButtonRelay>();
    }

    private static void Configure(GameObject target)
    {
        var label = TerriasTextCatalog.Get("ui.spirit.short_label");
        AuraUiNativeHoverHint.Attach(target, TerriasTextCatalog.Get("ui.spirit.adventure_title"));
        var manager = target.GetComponent<ButtonManager>();
        if (manager != null)
        {
            var icon = TerriasResourceCache.Load<Sprite>(TerriasIds.SpiritBallIconPath, true, "ui.spirit-adventure");
            manager.enableIcon = icon != null;
            manager.enableText = icon == null;
            manager.buttonText = label;
            if (icon != null) AuraUiNativeButtonIconOwner.Apply(manager, icon);
            else manager.SetText(label);
            manager.UpdateUI();
        }
        var text = target.GetComponentInChildren<Text>(true);
        if (text != null && (manager == null || manager.enableText)) text.text = label;
    }

    private static void SetActive(TopBarUI topBar, bool active)
    {
        var target = topBar.transform.Find("Content/Buttons/" + ButtonName)?.gameObject;
        if (target != null) target.SetActive(active);
    }

    private static void ScheduleRefresh()
    {
        try { GameUIManager.Instance?.StartCoroutine(RefreshNextFrame()); } catch { }
    }

    private static IEnumerator RefreshNextFrame()
    {
        yield return null;
        Refresh();
    }
}

internal sealed class SpiritAdventureButtonRelay : MonoBehaviour, IPointerClickHandler, ISubmitHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) SpiritManagementPanel.OpenAdventure();
    }

    public void OnSubmit(BaseEventData eventData) => SpiritManagementPanel.OpenAdventure();
}
