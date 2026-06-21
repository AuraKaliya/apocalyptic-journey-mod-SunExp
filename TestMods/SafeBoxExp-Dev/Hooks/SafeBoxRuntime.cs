using System;
using System.Collections;
using Michsky.MUIP;
using SafeBoxExp.Dll.Infrastructure;
using TMPro;
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

namespace SafeBoxExp.Dll.Hooks;

public static class SafeBoxRuntime
{
    private const int UnlimitedMoneyActionCount = 999999;
    private const int RelaxedCardBottomCount = 0;
    private const int RelaxedCardTopCount = 999999;
    private const int RelaxedMaxReserveCardCount = 999999;
    private const int MinimumSafeBoxLevel = 2;
    private static LimitSnapshot? activeSnapshot;
    private static bool? lastLoggedButtonVisible;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "TopBarUI.Awake", RefreshTopBarButton);
        RegisterAfter(modConfig, "TopBarUI.Start", InjectTopBarButton);
        RegisterAfter(modConfig, "TopBarUI.ShowLeftUp", ShowTopBarButton);
        RegisterAfter(modConfig, "TopBarUI.HideLeftUp", HideTopBarButton);
        RegisterAfter(modConfig, "MapSelectUI.Start", RefreshTopBarButtonForAdventureUi);
        RegisterAfter(modConfig, "MapSelectUI.ReadyToSelect", RefreshTopBarButtonForAdventureUi);
        RegisterAfter(modConfig, "MapSelectUI.ShowMap", RefreshTopBarButtonForAdventureUi);
        RegisterAfter(modConfig, "MapSelectUI.MapAnimation", RefreshTopBarButtonForAdventureUi);

        RegisterBefore(modConfig, "SafeBoxUI.PutIntoStore", PrepareUnlimitedSafeBox);
        RegisterAfter(modConfig, "SafeBoxUI.PutIntoStore", FinishUnlimitedSafeBox);
        RegisterBefore(modConfig, "SafeBoxUI.PutItBack", PrepareUnlimitedSafeBox);
        RegisterAfter(modConfig, "SafeBoxUI.PutItBack", FinishUnlimitedSafeBox);
        RegisterBefore(modConfig, "SafeBoxUI.RetainMoney", PrepareUnlimitedSafeBox);
        RegisterAfter(modConfig, "SafeBoxUI.RetainMoney", FinishUnlimitedSafeBox);
        RegisterBefore(modConfig, "SafeBoxUI.ChangeMoney", PrepareUnlimitedSafeBox);
        RegisterAfter(modConfig, "SafeBoxUI.ChangeMoney", FinishUnlimitedSafeBox);
        RegisterAfter(modConfig, "SafeBoxUI.ChangeCountShow", ReplaceCountShowWithUnlimited);
        RegisterAfter(modConfig, "SafeBoxUI.SafeboxSave", SaveRuntimeData);

        RegisterAfter(modConfig, "FightInit.Init", CloseSafeBoxForBlockingUi);
        RegisterAfter(modConfig, "FightUI.FadeIn", CloseSafeBoxForBlockingUi);
        RegisterAfter(modConfig, "EventUI.FadeIn", CloseSafeBoxForBlockingUi);
        RegisterAfter(modConfig, "EventUI.Init", CloseSafeBoxForBlockingUi);
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookBefore(target, action);
            SafeBoxExpLog.Info("Hook before registered: " + target);
        }
        catch (Exception ex)
        {
            SafeBoxExpLog.Warn("Hook before failed: " + target + " -> " + ex.Message);
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookAfter(target, action);
            SafeBoxExpLog.Info("Hook after registered: " + target);
        }
        catch (Exception ex)
        {
            SafeBoxExpLog.Warn("Hook after failed: " + target + " -> " + ex.Message);
        }
    }

    private static void InjectTopBarButton(ModHookContext context)
    {
        try
        {
            if (context.Target is TopBarUI topBar && EnsureTopBarButton(topBar))
            {
                SetTopBarButtonActive(topBar, ShouldShowSafeBoxButton());
            }
        }
        catch (Exception ex)
        {
            SafeBoxExpLog.Error("Failed to inject SafeBoxExp top-bar button", ex);
        }
    }

    private static bool EnsureTopBarButton(TopBarUI topBar)
    {
        var buttons = topBar.transform.Find("Content/Buttons");
        var cardBack = buttons?.Find("CardBack");
        if (buttons == null || cardBack == null)
        {
            SafeBoxExpLog.Warn("TopBarUI button container or CardBack template missing");
            return false;
        }

        var existing = buttons.Find(SafeBoxExpIds.ButtonName);
        if (existing != null)
        {
            BindButton(existing.gameObject);
            ConfigureButtonPresentation(existing.gameObject);
            return true;
        }

        var buttonObject = Object.Instantiate(cardBack.gameObject, buttons);
        buttonObject.name = SafeBoxExpIds.ButtonName;
        buttonObject.transform.SetAsLastSibling();
        buttonObject.SetActive(false);

        BindButton(buttonObject);
        ConfigureButtonPresentation(buttonObject);
        SafeBoxExpLog.Info("Injected SafeBoxExp top-bar button");
        return true;
    }

    private static void RefreshTopBarButton(ModHookContext context)
    {
        try
        {
            if (context.Target is TopBarUI topBar)
            {
                RefreshTopBarButton(topBar);
                ScheduleTopBarButtonRefresh();
            }
        }
        catch (Exception ex)
        {
            SafeBoxExpLog.Warn("Failed to refresh SafeBoxExp button: " + ex.Message);
        }
    }

    private static void RefreshTopBarButtonForAdventureUi(ModHookContext context)
    {
        RefreshTopBarButton();
        ScheduleTopBarButtonRefresh();
        ScheduleTopBarButtonRefresh(3);
    }

    private static void RefreshTopBarButton()
    {
        try
        {
            var topBar = GameUIManager.Instance?.GetUI<TopBarUI>("TopBarUI");
            if (topBar != null)
            {
                RefreshTopBarButton(topBar);
            }
        }
        catch (Exception ex)
        {
            SafeBoxExpLog.Warn("Failed to refresh SafeBoxExp button: " + ex.Message);
        }
    }

    private static void RefreshTopBarButton(TopBarUI topBar)
    {
        if (EnsureTopBarButton(topBar))
        {
            SetTopBarButtonActive(topBar, ShouldShowSafeBoxButton());
        }
    }

    private static void ScheduleTopBarButtonRefresh(int delayFrames = 1)
    {
        try
        {
            var uiManager = GameUIManager.Instance;
            if (uiManager != null)
            {
                uiManager.StartCoroutine(RefreshTopBarButtonAfterFrames(delayFrames));
            }
        }
        catch (Exception ex)
        {
            SafeBoxExpLog.Debug("Failed to schedule SafeBoxExp button refresh: " + ex.Message);
        }
    }

    private static IEnumerator RefreshTopBarButtonAfterFrames(int delayFrames)
    {
        for (var i = 0; i < delayFrames; i++)
        {
            yield return null;
        }

        RefreshTopBarButton();
    }

    private static void BindButton(GameObject buttonObject)
    {
        var manager = buttonObject.GetComponent<ButtonManager>();
        if (manager != null)
        {
            manager.onClick.RemoveAllListeners();
            manager.onDoubleClick.RemoveAllListeners();
            manager.onRightClick.RemoveAllListeners();
            manager.Interactable(true);
        }

        var button = buttonObject.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
        }

        var relay = buttonObject.GetComponent<SafeBoxButtonClickRelay>();
        if (relay == null)
        {
            relay = buttonObject.AddComponent<SafeBoxButtonClickRelay>();
        }
    }

    private static void ConfigureButtonPresentation(GameObject buttonObject)
    {
        const string displayName = "保险箱";
        var sprite = LoadSafeBoxSprite();
        var manager = buttonObject.GetComponent<ButtonManager>();
        if (manager != null)
        {
            manager.enableIcon = sprite != null;
            manager.enableText = false;
            manager.iconScale = 1f;
            manager.buttonText = displayName;
            if (sprite != null)
            {
                manager.buttonIcon = sprite;
                manager.normalImage?.gameObject.transform.parent?.gameObject.SetActive(true);
                manager.highlightImage?.gameObject.transform.parent?.gameObject.SetActive(true);
                manager.disabledImage?.gameObject.transform.parent?.gameObject.SetActive(true);
                SetButtonManagerImage(manager.normalImage, sprite);
                SetButtonManagerImage(manager.highlightImage, sprite);
                SetButtonManagerImage(manager.disabledImage, sprite);
                manager.SetIcon(sprite);
            }

            manager.SetText(displayName);
            manager.UpdateUI();
            SetButtonManagerTextActive(manager, false);
        }

        foreach (var keyItem in buttonObject.GetComponents<KeyItem>())
        {
            Object.Destroy(keyItem);
        }

        var tooltip = buttonObject.GetComponent<SafeBoxButtonTooltip>();
        if (tooltip == null)
        {
            tooltip = buttonObject.AddComponent<SafeBoxButtonTooltip>();
        }

        buttonObject.name = SafeBoxExpIds.ButtonName;
    }

    private static Sprite? LoadSafeBoxSprite()
    {
        return ResourceLoader.Load<Sprite>("Icon/Tutorial/保险箱", true)
            ?? ResourceLoader.Load<Sprite>("Images/Tutorial/Adventure/保险箱", true)
            ?? ResourceLoader.Load<Sprite>("Icon/Relic/遗物占位", true)
            ?? ResourceLoader.Load<Sprite>("Icon/Card/卡面占位", true);
    }

    private static void SetButtonManagerImage(Image? image, Sprite sprite)
    {
        if (image == null)
        {
            return;
        }

        image.sprite = sprite;
        image.preserveAspect = true;
        image.color = Color.white;
    }

    private static void SetButtonManagerTextActive(ButtonManager manager, bool active)
    {
        if (manager.normalText != null)
        {
            manager.normalText.gameObject.SetActive(active);
        }

        if (manager.highlightedText != null)
        {
            manager.highlightedText.gameObject.SetActive(active);
        }

        if (manager.disabledText != null)
        {
            manager.disabledText.gameObject.SetActive(active);
        }
    }

    private static void ShowTopBarButton(ModHookContext context)
    {
        if (context.Target is TopBarUI topBar)
        {
            RefreshTopBarButton(topBar);
        }
    }

    private static void HideTopBarButton(ModHookContext context)
    {
        SetTopBarButtonActive(context, false);
    }

    private static void SetTopBarButtonActive(ModHookContext context, bool active)
    {
        try
        {
            if (context.Target is TopBarUI topBar)
            {
                SetTopBarButtonActive(topBar, active);
            }
        }
        catch (Exception ex)
        {
            SafeBoxExpLog.Warn("Failed to update SafeBoxExp button visibility: " + ex.Message);
        }
    }

    private static void SetTopBarButtonActive(TopBarUI topBar, bool active)
    {
        var button = topBar.transform.Find("Content/Buttons/" + SafeBoxExpIds.ButtonName)?.gameObject;
        if (button == null)
        {
            return;
        }

        button.SetActive(active);
        if (lastLoggedButtonVisible != active)
        {
            lastLoggedButtonVisible = active;
            SafeBoxExpLog.Info("Top-bar SafeBox button visible=" + active + "; state=" + GetOpenBlockReason());
        }
    }

    private static bool ShouldShowSafeBoxButton()
    {
        if (GameUIManager.Instance == null || RoleTable.Instance == null)
        {
            return false;
        }

        if (FightManager.Instance != null && FightManager.Instance.fightType != FightType.None)
        {
            return false;
        }

        return !IsActiveUI<FightUI>("FightUI");
    }

    internal static void HandleButtonClicked()
    {
        SafeBoxExpLog.Info("Top-bar SafeBox button clicked; state=" + GetOpenBlockReason());
        OpenSafeBox();
    }

    private static void OpenSafeBox()
    {
        try
        {
            var blockReason = GetOpenBlockReason();
            if (blockReason != "ok")
            {
                GameUIManager.Instance?.ShowTip("当前状态不能打开保险箱");
                SafeBoxExpLog.Warn("Open SafeBox blocked: " + blockReason);
                return;
            }

            var safeBox = GameUIManager.Instance.ShowUI<SafeBoxUI>("SafeBoxUI", true);
            safeBox.transform.SetAsLastSibling();
            safeBox.ShowBackItem();
            ReplaceCountShowWithUnlimited(safeBox);
            PrimeUnlimitedFlags();
            SafeBoxExpLog.Info("Opened official SafeBoxUI");
        }
        catch (Exception ex)
        {
            SafeBoxExpLog.Error("Failed to open SafeBoxUI", ex);
        }
    }

    private static bool CanOpenSafeBox()
    {
        if (GameUIManager.Instance == null || RoleTable.Instance == null || MapManager.Instance?.ModeMapManager == null)
        {
            return false;
        }

        if (HasBlockingFlowUi())
        {
            return false;
        }

        return GameUIManager.Instance.WindowObj == null && GameUIManager.Instance.InputObj == null;
    }

    private static string GetOpenBlockReason()
    {
        if (GameUIManager.Instance == null)
        {
            return "UIManager missing";
        }

        if (RoleTable.Instance == null)
        {
            return "RoleTable missing";
        }

        if (MapManager.Instance?.ModeMapManager == null)
        {
            return "MapManager/ModeMapManager missing";
        }

        if (FightManager.Instance != null && FightManager.Instance.fightType != FightType.None)
        {
            return "fight active: " + FightManager.Instance.fightType;
        }

        if (IsActiveUI<FightUI>("FightUI"))
        {
            return "FightUI active";
        }

        if (IsActiveUI<EventUI>("EventUI"))
        {
            return "EventUI active";
        }

        if (IsActiveUI<DialogueUI>("DialogueUI"))
        {
            return "DialogueUI active";
        }

        if (IsActiveUI<OptionsUI>("OptionsUI"))
        {
            return "OptionsUI active";
        }

        if (IsActiveUI<InkTurnUI>("InkTurnUI"))
        {
            return "InkTurnUI active";
        }

        if (IsActiveUI<CurtainTurnUI>("CurtainTurnUI"))
        {
            return "CurtainTurnUI active";
        }

        if (IsActiveUI<SceneTurnUI>("SceneTurnUI"))
        {
            return "SceneTurnUI active";
        }

        if (GameUIManager.Instance.WindowObj != null)
        {
            return "ModalWindow active";
        }

        if (GameUIManager.Instance.InputObj != null)
        {
            return "InputWindow active";
        }

        return "ok";
    }

    private static bool HasBlockingFlowUi()
    {
        if (GameUIManager.Instance == null)
        {
            return true;
        }

        if (FightManager.Instance != null && FightManager.Instance.fightType != FightType.None)
        {
            return true;
        }

        if (IsActiveUI<FightUI>("FightUI")
            || IsActiveUI<EventUI>("EventUI")
            || IsActiveUI<DialogueUI>("DialogueUI")
            || IsActiveUI<OptionsUI>("OptionsUI")
            || IsActiveUI<InkTurnUI>("InkTurnUI")
            || IsActiveUI<CurtainTurnUI>("CurtainTurnUI")
            || IsActiveUI<SceneTurnUI>("SceneTurnUI"))
        {
            return true;
        }

        return false;
    }

    private static bool IsActiveUI<T>(string uiName) where T : UIBase
    {
        var ui = GameUIManager.Instance.GetUI<T>(uiName);
        return ui != null && ui.gameObject.activeInHierarchy;
    }

    private static void CloseSafeBoxForBlockingUi(ModHookContext context)
    {
        CloseSafeBox();
        RefreshTopBarButton();
    }

    private static void CloseSafeBox()
    {
        try
        {
            var uiManager = GameUIManager.Instance;
            var safeBox = uiManager?.GetUI<SafeBoxUI>("SafeBoxUI");
            if (safeBox != null)
            {
                uiManager!.CloseUI("SafeBoxUI");
            }
        }
        catch (Exception ex)
        {
            SafeBoxExpLog.Warn("Failed to close SafeBoxUI: " + ex.Message);
        }
    }

    private static void PrepareUnlimitedSafeBox(ModHookContext context)
    {
        try
        {
            if (activeSnapshot == null)
            {
                activeSnapshot = LimitSnapshot.Capture();
            }

            ApplyUnlimitedEnvironment();
            SafeBoxExpLog.Info("Prepared unlimited SafeBox environment; target=" + HookTargetName(context));
        }
        catch (Exception ex)
        {
            SafeBoxExpLog.Error("Failed to prepare unlimited SafeBox environment", ex);
        }
    }

    private static void FinishUnlimitedSafeBox(ModHookContext context)
    {
        SafeBoxUI? safeBox = null;
        try
        {
            safeBox = context.Target as SafeBoxUI ?? GameUIManager.Instance?.GetUI<SafeBoxUI>("SafeBoxUI");
            activeSnapshot?.Restore();
            activeSnapshot = null;
            PrimeUnlimitedFlags();

            if (safeBox != null)
            {
                safeBox.ChangeMoneyShow();
                safeBox.ChangeCountShow();
                safeBox.UpdateCardShow();
            }

            SafeBoxUI.SafeboxSave();
            SaveRuntimeData(context);
            RefreshTopBar();
            RefreshTopBarButton();
            SafeBoxExpLog.Info("Finished unlimited SafeBox operation; target=" + HookTargetName(context));
        }
        catch (Exception ex)
        {
            activeSnapshot?.Restore();
            activeSnapshot = null;
            SafeBoxExpLog.Error("Failed to finish unlimited SafeBox operation", ex);
        }
    }

    private static void ApplyUnlimitedEnvironment()
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return;
        }

        role.SafeBoxCardCount = 0;
        role.SafeBoxRelicCount = 0;
        role.SafeBoxSaveMoneyCount = UnlimitedMoneyActionCount;
        role.SafeBoxGetMoneyCount = UnlimitedMoneyActionCount;
        role.GetCardInBack = false;
        role.GetRelic = false;
        role.CardBottomCount = RelaxedCardBottomCount;
        role.CardTopCount = RelaxedCardTopCount;
        role.MaxAlCardCount = RelaxedMaxReserveCardCount;

        var mode = MapManager.Instance?.ModeMapManager;
        if (mode != null && mode.Level < MinimumSafeBoxLevel)
        {
            mode.Level = MinimumSafeBoxLevel;
        }
    }

    private static void PrimeUnlimitedFlags()
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return;
        }

        role.SafeBoxCardCount = 0;
        role.SafeBoxRelicCount = 0;
        role.SafeBoxSaveMoneyCount = UnlimitedMoneyActionCount;
        role.SafeBoxGetMoneyCount = UnlimitedMoneyActionCount;
        role.GetCardInBack = false;
        role.GetRelic = false;
    }

    private static void ReplaceCountShowWithUnlimited(ModHookContext context)
    {
        if (context.Target is SafeBoxUI safeBox)
        {
            ReplaceCountShowWithUnlimited(safeBox);
        }
    }

    private static void ReplaceCountShowWithUnlimited(SafeBoxUI safeBox)
    {
        try
        {
            SetText(safeBox.transform, "Content/Backpack/Windows/卡牌/Right/SafeCount/Title", "Cards can be stored".Localize("GameEntryUI") + ": 不限");
            SetText(safeBox.transform, "Content/Backpack/Windows/遗物/Right/SafeCount/Title", "Relics can be stored".Localize("GameEntryUI") + ": 不限");
            SetText(safeBox.transform, "Content/Backpack/Windows/卡牌/Left/CanOut/text", "Can bring out".Localize("GameEntryUI") + ": 不限");
            SetText(safeBox.transform, "Content/Backpack/Windows/遗物/Left/CanOut/text", "Can bring out".Localize("GameEntryUI") + ": 不限");
        }
        catch (Exception ex)
        {
            SafeBoxExpLog.Debug("Failed to replace SafeBox count text: " + ex.Message);
        }
    }

    private static void SetText(Transform root, string path, string value)
    {
        var target = root.Find(path);
        var text = target?.GetComponent<TMP_Text>();
        if (text != null)
        {
            text.text = value;
        }
    }

    private static void SaveRuntimeData(ModHookContext context)
    {
        SaveRuntimeData();
    }

    private static void SaveRuntimeData()
    {
        try
        {
            Singleton<GameRuntimeData>.Instance.Save();
        }
        catch (Exception ex)
        {
            SafeBoxExpLog.Warn("Failed to save GameRuntimeData: " + ex.Message);
        }
    }

    private static void RefreshTopBar()
    {
        try
        {
            var topBar = GameUIManager.Instance?.GetUI<TopBarUI>("TopBarUI");
            if (topBar == null)
            {
                return;
            }

            topBar.UpdateRelics();
            topBar.ChangeMoney();
        }
        catch (Exception ex)
        {
            SafeBoxExpLog.Debug("Failed to refresh TopBarUI: " + ex.Message);
        }
    }

    private static string HookTargetName(ModHookContext context)
    {
        return context.Target?.GetType().Name ?? "static";
    }

    private sealed class LimitSnapshot
    {
        private readonly RoleTable? role;
        private readonly IModeManager? mode;
        private readonly int cardBottomCount;
        private readonly int cardTopCount;
        private readonly int maxAlCardCount;
        private readonly int level;

        private LimitSnapshot(RoleTable? role, IModeManager? mode)
        {
            this.role = role;
            this.mode = mode;
            if (role != null)
            {
                cardBottomCount = role.CardBottomCount;
                cardTopCount = role.CardTopCount;
                maxAlCardCount = role.MaxAlCardCount;
            }

            if (mode != null)
            {
                level = mode.Level;
            }
        }

        public static LimitSnapshot Capture()
        {
            return new LimitSnapshot(RoleTable.Instance, MapManager.Instance?.ModeMapManager);
        }

        public void Restore()
        {
            if (role != null)
            {
                role.CardBottomCount = cardBottomCount;
                role.CardTopCount = cardTopCount;
                role.MaxAlCardCount = maxAlCardCount;
            }

            if (mode != null)
            {
                mode.Level = level;
            }
        }
    }
}

internal sealed class SafeBoxButtonClickRelay : MonoBehaviour, IPointerClickHandler, ISubmitHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            SafeBoxRuntime.HandleButtonClicked();
        }
    }

    public void OnSubmit(BaseEventData eventData)
    {
        SafeBoxRuntime.HandleButtonClicked();
    }
}

internal sealed class SafeBoxButtonTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private GameObject? tooltipObject;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (tooltipObject == null)
        {
            tooltipObject = Object.Instantiate(ResourceLoader.Load<GameObject>("UI/SelectedMessage"), transform);
            tooltipObject.name = "SafeBoxExpTooltip";
            tooltipObject.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -100f);
        }
        else
        {
            tooltipObject.SetActive(true);
        }

        var text = tooltipObject.transform.Find("text")?.GetComponent<TMP_Text>();
        if (text != null)
        {
            text.gameObject.SetActive(true);
            text.text = "保险箱";
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (tooltipObject != null)
        {
            tooltipObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (tooltipObject != null)
        {
            Object.Destroy(tooltipObject);
            tooltipObject = null;
        }
    }
}
