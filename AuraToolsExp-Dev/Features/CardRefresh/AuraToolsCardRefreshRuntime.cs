using System;
using System.Collections.Generic;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using Michsky.MUIP;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.CardRefresh;

public static class AuraToolsCardRefreshRuntime
{
    private const string HandlerId = "CardRefresh";
    private static bool initialized;
    private static IDisposable? selectionSubscription;

    internal static bool Enabled => AuraToolsConfigService.MatchExperience.CardRefresh.Enabled;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        AuraToolsHookRegistry.Before(modConfig, "CardChoiceUI.Start", BeforeCardChoiceStart, "CardRefresh");
        AuraToolsHookRegistry.After(modConfig, "CardChoiceUI.Start", AfterCardChoiceStart, "CardRefresh");
        selectionSubscription = AuraCardLifecycleRouter.Register(
            modConfig,
            AuraToolsIds.ModId,
            HandlerId,
            new AuraCardLifecycleSubscription
            {
                BeforeCardChoiceUiSelect = BeforeCardSelected
            },
            info: AuraToolsLog.Info,
            warn: AuraToolsLog.Warn);
    }

    private static void BeforeCardChoiceStart(ModHookContext context)
    {
        if (!Enabled
            || !CardChoiceRefreshNativeApi.IsBattleRewardContext()
            || context.Target is not CardChoiceUI ui)
        {
            return;
        }

        var controller = ui.GetComponent<AuraToolsCardRefreshController>()
                         ?? ui.gameObject.AddComponent<AuraToolsCardRefreshController>();
        controller.Prepare(ui);
    }

    private static void AfterCardChoiceStart(ModHookContext context)
    {
        if (context.Target is CardChoiceUI ui)
        {
            ui.GetComponent<AuraToolsCardRefreshController>()?.Activate();
        }
    }

    private static void BeforeCardSelected(ModHookContext context)
    {
        if (context.Target is CardChoiceUI ui)
        {
            ui.GetComponent<AuraToolsCardRefreshController>()?.DisableForSelection();
        }
    }
}

internal sealed class AuraToolsCardRefreshController : MonoBehaviour
{
    private const string TemplateHostName = "AuraToolsCardRefreshTemplates";
    private const string RefreshButtonName = "AuraToolsCardRefreshButton";
    private static readonly string[] ItemPaths = { "Content/Item1", "Content/Item2", "Content/Item3" };
    private readonly GameObject[] templates = new GameObject[3];
    private CardChoiceUI? choiceUi;
    private GameObject? templateHost;
    private GameObject? refreshButton;
    private ButtonManager? refreshButtonManager;
    private Dice? refreshDice;
    private bool prepared;
    private bool refreshing;
    private bool selectionStarted;
    private bool loggedFailure;

    public void Prepare(CardChoiceUI ui)
    {
        if (prepared)
        {
            return;
        }

        choiceUi = ui;
        if (!CardChoiceRefreshNativeApi.Compatible || !CaptureCleanTemplates(ui))
        {
            LogFailureOnce("native CardChoiceUI contract is unavailable");
            return;
        }

        prepared = true;
    }

    public void Activate()
    {
        if (!prepared || choiceUi == null || !AuraToolsCardRefreshRuntime.Enabled)
        {
            return;
        }

        refreshDice = CardChoiceRefreshNativeApi.CloneCurrentDice();
        if (refreshDice == null || !EnsureRefreshButton())
        {
            LogFailureOnce(refreshDice == null ? "failed to clone the card-choice dice" : "failed to create the refresh button");
        }
    }

    public void DisableForSelection()
    {
        selectionStarted = true;
        if (refreshButton != null)
        {
            refreshButton.SetActive(false);
        }
    }

    private bool CaptureCleanTemplates(CardChoiceUI ui)
    {
        templateHost = new GameObject(TemplateHostName, typeof(RectTransform));
        templateHost.SetActive(false);
        templateHost.transform.SetParent(ui.transform, false);

        for (var i = 0; i < ItemPaths.Length; i++)
        {
            var source = ui.transform.Find(ItemPaths[i]);
            if (source == null || source.GetComponent<CardChoiceItem>() == null)
            {
                DestroyTemplateHost();
                return false;
            }

            var template = Object.Instantiate(source.gameObject, templateHost.transform, false);
            template.name = "Template" + (i + 1);
            template.SetActive(false);
            templates[i] = template;
        }

        return true;
    }

    private bool EnsureRefreshButton()
    {
        if (choiceUi == null)
        {
            return false;
        }

        if (refreshButton != null)
        {
            refreshButton.SetActive(true);
            return true;
        }

        var nativeButton = choiceUi.transform.Find("Button");
        var templateManager = nativeButton?.GetComponent<ButtonManager>();
        var parent = nativeButton?.parent;
        if (nativeButton == null || templateManager == null || parent == null)
        {
            return false;
        }

        var result = AuraUiNativeButtonCloneAdapter.TryClone(new AuraUiNativeButtonCloneRequest
        {
            Template = templateManager,
            Parent = parent,
            CloneName = RefreshButtonName,
            Label = "刷新",
            OnClick = RefreshChoices
        });
        if (!result.Success || result.Root == null)
        {
            AuraToolsLog.Warn("[CardRefresh] native refresh button clone rejected: " + result.FailureReason);
            return false;
        }

        refreshButton = result.Root;
        refreshButtonManager = result.Manager as ButtonManager;
        PositionBesideNativeButton(nativeButton, refreshButton);
        refreshButton.SetActive(true);
        return true;
    }

    private static void PositionBesideNativeButton(Transform nativeButton, GameObject clone)
    {
        clone.transform.SetSiblingIndex(nativeButton.GetSiblingIndex() + 1);
        if (nativeButton.parent.GetComponent<LayoutGroup>() != null
            || nativeButton is not RectTransform nativeRect
            || clone.transform is not RectTransform cloneRect)
        {
            return;
        }

        var baseline = nativeRect.anchoredPosition;
        var width = Mathf.Max(Mathf.Abs(nativeRect.rect.width), Mathf.Abs(nativeRect.sizeDelta.x));
        var shift = Mathf.Max(72f, width * 0.55f);
        nativeRect.anchoredPosition = baseline + Vector2.left * shift;
        cloneRect.anchoredPosition = baseline + Vector2.right * shift;
    }

    private void RefreshChoices()
    {
        if (refreshing
            || selectionStarted
            || choiceUi == null
            || refreshDice == null
            || !AuraToolsCardRefreshRuntime.Enabled
            || CardChoiceRefreshNativeApi.IsSelected(choiceUi))
        {
            return;
        }

        refreshing = true;
        refreshButtonManager?.Interactable(false);
        var replacementObjects = new List<GameObject>(3);
        GameObject[]? previousItems = null;
        var fieldsUpdated = false;
        try
        {
            if (!CardChoiceRefreshNativeApi.TryGetItems(choiceUi, out previousItems))
            {
                throw new InvalidOperationException("native card-choice items are unavailable");
            }

            var currentIds = CardChoiceRefreshNativeApi.CurrentChoiceIds(previousItems);
            if (!CardChoiceRefreshNativeApi.TryDrawChoices(refreshDice, currentIds, out var cardIds))
            {
                throw new InvalidOperationException("fewer than three eligible cards were drawn");
            }

            for (var i = 0; i < templates.Length; i++)
            {
                if (templates[i] == null || previousItems[i] == null || previousItems[i].transform.parent == null)
                {
                    throw new InvalidOperationException("clean card template is unavailable");
                }

                var replacement = Object.Instantiate(templates[i], previousItems[i].transform.parent, false);
                replacement.name = previousItems[i].name;
                replacement.SetActive(false);
                replacement.transform.SetSiblingIndex(previousItems[i].transform.GetSiblingIndex());
                replacementObjects.Add(replacement);
            }

            if (!CardChoiceRefreshNativeApi.TrySetItems(choiceUi, replacementObjects))
            {
                throw new InvalidOperationException("failed to update native card-choice references");
            }

            fieldsUpdated = true;
            for (var i = 0; i < replacementObjects.Count; i++)
            {
                var replacement = replacementObjects[i];
                replacement.SetActive(true);
                var item = replacement.GetComponent<CardChoiceItem>()
                           ?? throw new InvalidOperationException("replacement has no CardChoiceItem");
                item.FadeIn(i * 0.08f);
                item.Initialize(choiceUi, cardIds[i]);
            }

            foreach (var previous in previousItems)
            {
                previous.SetActive(false);
                Object.Destroy(previous);
            }

            AuraToolsLog.Debug("[CardRefresh] choices refreshed: " + string.Join(",", cardIds));
        }
        catch (Exception ex)
        {
            if (fieldsUpdated && previousItems != null)
            {
                CardChoiceRefreshNativeApi.TrySetItems(choiceUi, previousItems);
            }

            foreach (var replacement in replacementObjects)
            {
                if (replacement != null)
                {
                    replacement.SetActive(false);
                    Object.Destroy(replacement);
                }
            }

            AuraToolsLog.Warn("[CardRefresh] refresh failed; current choices were preserved: " + ex.Message);
        }
        finally
        {
            refreshing = false;
            if (!selectionStarted && refreshButtonManager != null)
            {
                refreshButtonManager.Interactable(true);
            }
        }
    }

    private void DestroyTemplateHost()
    {
        if (templateHost != null)
        {
            Object.Destroy(templateHost);
            templateHost = null;
        }

        Array.Clear(templates, 0, templates.Length);
    }

    private void LogFailureOnce(string reason)
    {
        if (loggedFailure)
        {
            return;
        }

        loggedFailure = true;
        AuraToolsLog.Warn("[CardRefresh] unavailable for this card-choice window: " + reason);
    }
}
