using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui;

public static class EndlessAbyssMilestoneRewardPanel
{
    private static EndlessAbyssChoiceView? view;
    private static EndlessAbyssChoiceState? choices;
    private static IReadOnlyList<EndlessAbyssChoiceOption> options = Array.Empty<EndlessAbyssChoiceOption>();
    private static EndlessAbyssCardGrid? cardGrid;
    private static EndlessAbyssCardOption? selectedCard;
    private static int activeFloor;
    private static bool resolving;
    public static bool IsOpen => view != null && view.Root != null;

    public static bool TryOpenForCurrentFloor(string source)
    {
        try
        {
            if (IsOpen) return true;
            activeFloor = Math.Max(1, GameSaveManager.GetValue<int>(TerriasIds.EndlessSeaFloorKey));
            if (!EndlessAbyssMilestoneRewardService.CanClaim(activeFloor)) return false;
            var parent = TerriasModalHost.ModalParent();
            if (parent == null) return false;
            view = new EndlessAbyssChoiceView("Terrias_EndlessAbyssMilestoneRewardPanel", parent, "深渊里程碑奖励");
            TerriasTransientUiRegistry.Register("EndlessAbyssMilestone", Close);
            view.Back.onClick.AddListener(() => Run(ShowChoices));
            ShowChoices();
            TerriasLog.Info("[EndlessAbyssMilestone] opened from " + source + "; floor=" + activeFloor);
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Endless abyss milestone reward panel failed", ex);
            Close("EndlessAbyssMilestone.OpenFailed");
            return false;
        }
    }

    private static void ReleaseCards()
    {
        if (cardGrid != null) cardGrid.Release();
        cardGrid = null;
        selectedCard = null;
    }

    private static void ShowChoices()
    {
        if (view == null) return;
        ReleaseCards();
        choices = EndlessAbyssMilestoneRewardService.Choices(activeFloor);
        options = EndlessAbyssChoiceCatalog.MilestoneOptions();
        view.SetSubtitle("第 " + activeFloor + " 层 · 选择 1 项奖励");
        view.ShowChoices(false, choices, options, id => Run(() => Select(id)), slot => Run(() => Refresh(slot)));
        view.Confirm.onClick.AddListener(() => Run(ConfirmChoice));
        view.RefreshSelection(choices, options, 1);
    }

    private static void Select(string id)
    {
        if (view == null || !options.Any(option => option.Id == id && option.Available)) return;
        var next = EndlessAbyssMilestoneRewardService.Choices(activeFloor);
        if (!next.Toggle(id, 1)) return;
        EndlessAbyssChoiceStore.Save(EndlessAbyssChoiceStore.Milestone, next);
        choices = next;
        view.RefreshSelection(next, options, 1);
    }

    private static void Refresh(int slot)
    {
        var next = EndlessAbyssMilestoneRewardService.Choices(activeFloor);
        var available = EndlessAbyssChoiceCatalog.MilestoneOptions();
        if (!next.Refresh(slot, EndlessAbyssChoiceCatalog.MilestoneIds,
                id => available.Any(option => option.Id == id && option.Available), EndlessAbyssChoiceStore.PickIndex))
        {
            view?.SetHint("没有可用刷新，次数未消耗。");
            return;
        }
        EndlessAbyssChoiceStore.Save(EndlessAbyssChoiceStore.Milestone, next);
        ShowChoices();
    }

    private static void ConfirmChoice()
    {
        if (view == null || resolving) return;
        var state = EndlessAbyssMilestoneRewardService.Choices(activeFloor);
        var available = EndlessAbyssChoiceCatalog.MilestoneOptions();
        if (!state.IsReady(1, id => available.Any(option => option.Id == id && option.Available)))
        {
            ShowChoices();
            view?.SetHint("所选奖励当前不可用，请重新选择。");
            return;
        }
        switch (state.Selected[0])
        {
            case EndlessAbyssMilestoneRewardKind.Relic: ShowRelicPicker(); break;
            case EndlessAbyssMilestoneRewardKind.RemoveBurnout: ShowCardPicker(true); break;
            case EndlessAbyssMilestoneRewardKind.AddExtinction: ShowCardPicker(false); break;
            case EndlessAbyssMilestoneRewardKind.OtherDimensionCard:
                Resolve(() => {
                    var success = EndlessAbyssMilestoneRewardService.GrantRandomOtherDimensionCard(activeFloor, out var message);
                    return (success, message);
                });
                break;
        }
    }

    private static void ShowRelicPicker()
    {
        if (view == null) return;
        BeginPicker("选择 1 件遗物");
        var list = TerriasUiComponents.CreateVerticalScrollArea(view.Content, "AbyssRelics", 660f, 1f, 10f, 34f,
            new Color(0.04f, 0.05f, 0.08f, 0.95f));
        foreach (var relic in EndlessAbyssMilestoneRewardService.RelicCandidates())
        {
            var row = TerriasUiComponents.CreateTextButton(list.Content, "T" + relic.Tier + "  " + relic.Name,
                new Vector2(1300f, 66f), TerriasUiSprites.Button("[EndlessAbyssMilestone]"), new Color(0.1f, 0.1f, 0.14f),
                EndlessAbyssChoiceView.TextColor, 26, () => Run(() => {
                    view!.SetHint("已选：" + relic.Name);
                    view.Confirm.interactable = true;
                    view.Confirm.onClick.RemoveAllListeners();
                    view.Confirm.onClick.AddListener(() => Resolve(() => {
                        var success = EndlessAbyssMilestoneRewardService.GrantRelic(activeFloor, relic.Id, out var message);
                        return (success, message);
                    }));
                }));
        }
    }

    private static void BeginPicker(string subtitle)
    {
        ReleaseCards();
        view!.ClearContent();
        view.SetSubtitle("第 " + activeFloor + " 层 · " + subtitle);
        view.Back.gameObject.SetActive(true);
        view.Confirm.interactable = false;
        view.SetHint("选中目标后点击确定。");
    }

    private static void ShowCardPicker(bool removeBurnout)
    {
        if (view == null) return;
        BeginPicker(removeBurnout ? "选择 1 张卡牌清除焚毁" : "选择 1 张卡牌附加绝灭");
        var cards = removeBurnout ? EndlessAbyssMilestoneRewardService.BurnoutCards()
            : EndlessAbyssMilestoneRewardService.ExtinctionTargets();
        var gridHost = TerriasUiComponents.CreateFillRect("CardPicker", view.Content);
        cardGrid = gridHost.AddComponent<EndlessAbyssCardGrid>();
        cardGrid.Show((RectTransform)gridHost.transform, cards, option => {
            selectedCard = option;
            cardGrid?.Select(option.InstanceId);
            view!.SetHint("已选：" + option.Name);
            view.Confirm.interactable = true;
        });
        if (cards.Count == 0) view.SetHint("当前没有可选卡牌，请返回选择其他奖励。");
        view.Confirm.onClick.AddListener(() => {
            var card = selectedCard;
            if (card == null) return;
            Resolve(() => {
                string message;
                var success = removeBurnout
                    ? EndlessAbyssMilestoneRewardService.RemoveBurnout(activeFloor, card.Card, out message)
                    : EndlessAbyssMilestoneRewardService.AddExtinction(activeFloor, card.Card, out message);
                if (!success) ShowCardPicker(removeBurnout);
                return (success, message);
            });
        });
    }

    private static void Resolve(Func<(bool Success, string Message)> action)
    {
        if (resolving || view == null) return;
        resolving = true;
        view.Confirm.interactable = false;
        try
        {
            var result = action();
            if (result.Success) Close("EndlessAbyssMilestone.Confirm");
            else
            {
                view?.SetHint(result.Message);
                if (view != null && cardGrid == null) view.Confirm.interactable = true;
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Endless abyss milestone resolution failed", ex);
            view?.SetHint("奖励结算未完成，请重试。");
            if (view != null) view.Confirm.interactable = true;
        }
        finally { resolving = false; }
    }

    private static void Run(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            TerriasLog.Error("Endless abyss milestone selection failed", ex);
            view?.SetHint("操作未完成，可返回后重试。");
        }
    }

    public static void Close(string source)
    {
        ReleaseCards();
        var root = view?.Root;
        view?.ClearContent();
        view = null;
        choices = null;
        options = Array.Empty<EndlessAbyssChoiceOption>();
        activeFloor = 0;
        TerriasModalHost.Close(ref root, source, "[EndlessAbyssMilestone]");
        TerriasTransientUiRegistry.Unregister("EndlessAbyssMilestone");
    }
}
