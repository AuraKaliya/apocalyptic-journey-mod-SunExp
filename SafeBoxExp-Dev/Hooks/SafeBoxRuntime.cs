using System;
using Michsky.MUIP;
using SafeBoxExp.Dll.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
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

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "TopBarUI.Start", InjectTopBarButton);
        RegisterAfter(modConfig, "TopBarUI.ShowLeftUp", ShowTopBarButton);
        RegisterAfter(modConfig, "TopBarUI.HideLeftUp", HideTopBarButton);

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
            SafeBoxExpLog.Debug("Hook before registered: " + target);
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
            SafeBoxExpLog.Debug("Hook after registered: " + target);
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
            if (context.Target is not TopBarUI topBar)
            {
                return;
            }

            var buttons = topBar.transform.Find("Content/Buttons");
            var cardBack = buttons?.Find("CardBack");
            if (buttons == null || cardBack == null)
            {
                SafeBoxExpLog.Warn("TopBarUI button container or CardBack template missing");
                return;
            }

            var existing = buttons.Find(SafeBoxExpIds.ButtonName);
            if (existing != null)
            {
                existing.gameObject.SetActive(IsSafeBoxButtonVisible());
                return;
            }

            var buttonObject = Object.Instantiate(cardBack.gameObject, buttons);
            buttonObject.name = SafeBoxExpIds.ButtonName;
            buttonObject.transform.SetAsLastSibling();
            buttonObject.SetActive(IsSafeBoxButtonVisible());

            BindButton(buttonObject, OpenSafeBox);
            ApplyButtonText(buttonObject, "保险箱");
            TrySetButtonIcon(buttonObject);
            SafeBoxExpLog.Info("Injected SafeBoxExp top-bar button");
        }
        catch (Exception ex)
        {
            SafeBoxExpLog.Error("Failed to inject SafeBoxExp top-bar button", ex);
        }
    }

    private static void BindButton(GameObject buttonObject, UnityAction action)
    {
        var manager = buttonObject.GetComponent<ButtonManager>();
        if (manager != null)
        {
            manager.onClick.RemoveAllListeners();
            manager.onClick.AddListener(action);
            return;
        }

        var button = buttonObject.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }
    }

    private static void ApplyButtonText(GameObject buttonObject, string text)
    {
        var manager = buttonObject.GetComponent<ButtonManager>();
        if (manager != null)
        {
            manager.SetText(text);
        }

        foreach (var label in buttonObject.GetComponentsInChildren<TMP_Text>(true))
        {
            label.text = text;
        }
    }

    private static void TrySetButtonIcon(GameObject buttonObject)
    {
        var sprite = ResourceLoader.Load<Sprite>("Icon/Tutorial/保险箱", true)
            ?? ResourceLoader.Load<Sprite>("Images/Tutorial/Adventure/保险箱", true)
            ?? ResourceLoader.Load<Sprite>("Icon/Relic/遗物占位", true)
            ?? ResourceLoader.Load<Sprite>("Icon/Card/卡面占位", true);
        if (sprite == null)
        {
            return;
        }

        foreach (var image in buttonObject.GetComponentsInChildren<Image>(true))
        {
            if (image.gameObject.name.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) >= 0
                || image.transform.parent?.name.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                image.sprite = sprite;
                image.preserveAspect = true;
                return;
            }
        }
    }

    private static void ShowTopBarButton(ModHookContext context)
    {
        SetTopBarButtonActive(context, IsSafeBoxButtonVisible());
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
                topBar.transform.Find("Content/Buttons/" + SafeBoxExpIds.ButtonName)?.gameObject.SetActive(active);
            }
        }
        catch (Exception ex)
        {
            SafeBoxExpLog.Warn("Failed to update SafeBoxExp button visibility: " + ex.Message);
        }
    }

    private static bool IsSafeBoxButtonVisible()
    {
        return GameUIManager.Instance != null && RoleTable.Instance != null && !HasBlockingUi();
    }

    private static void OpenSafeBox()
    {
        try
        {
            if (HasBlockingUi())
            {
                GameUIManager.Instance?.ShowTip("当前状态不能打开保险箱");
                return;
            }

            var safeBox = GameUIManager.Instance.ShowUI<SafeBoxUI>("SafeBoxUI", true);
            safeBox.transform.SetAsLastSibling();
            safeBox.ShowBackItem();
            ReplaceCountShowWithUnlimited(safeBox);
            PrimeUnlimitedFlags();
        }
        catch (Exception ex)
        {
            SafeBoxExpLog.Error("Failed to open SafeBoxUI", ex);
        }
    }

    private static bool HasBlockingUi()
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

        return GameUIManager.Instance.WindowObj != null || GameUIManager.Instance.InputObj != null;
    }

    private static bool IsActiveUI<T>(string uiName) where T : UIBase
    {
        var ui = GameUIManager.Instance.GetUI<T>(uiName);
        return ui != null && ui.gameObject.activeInHierarchy;
    }

    private static void CloseSafeBoxForBlockingUi(ModHookContext context)
    {
        CloseSafeBox();
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
