using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;

namespace Terrias.Dll.Hooks.Ui;

public static class EndlessAbyssShockPanel
{
    private static EndlessAbyssChoiceView? view;
    private static EndlessAbyssShockRequest? request;
    private static EndlessAbyssChoiceState? choices;
    private static IReadOnlyList<EndlessAbyssChoiceOption> options = Array.Empty<EndlessAbyssChoiceOption>();
    private static Action? onClosed;
    private static bool resolving;

    public static bool IsOpen => view != null && view.Root != null;

    public static bool TryOpenPending(Action? closed, string source)
    {
        try
        {
            if (IsOpen) return true;
            request = EndlessAbyssShockService.PendingRequest();
            if (request == null) return false;
            var parent = TerriasModalHost.ModalParent();
            if (parent == null) return false;
            choices = EndlessAbyssShockService.Choices(request);
            onClosed = closed;
            view = new EndlessAbyssChoiceView("Terrias_EndlessAbyssShockPanel", parent, "深渊震荡");
            TerriasTransientUiRegistry.Register("EndlessAbyssShock", Close);
            ShowChoices();
            TerriasLog.Info("[EndlessAbyssShock] opened from " + source + "; key=" + request.Key);
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Endless abyss shock panel failed", ex);
            Close("EndlessAbyssShock.OpenFailed");
            return false;
        }
    }

    private static void ShowChoices()
    {
        if (view == null || request == null) return;
        choices = EndlessAbyssShockService.Choices(request);
        options = EndlessAbyssChoiceCatalog.ShockOptions();
        view.SetSubtitle("第 " + request.Floor + " 层 · 深渊注视 " + EndlessAbyssGazeService.CurrentLevel()
            + " · 需选择 " + EndlessAbyssGazeService.RequiredShockChoices() + " 项");
        view.ShowChoices(true, choices, options, id => Run(() => Select(id)), slot => Run(() => Refresh(slot)));
        view.Confirm.onClick.AddListener(() => Run(Confirm));
        view.RefreshSelection(choices, options, EndlessAbyssGazeService.RequiredShockChoices());
    }

    private static void Select(string id)
    {
        if (request == null || view == null) return;
        var next = EndlessAbyssShockService.Choices(request);
        if (!next.Toggle(id, EndlessAbyssGazeService.RequiredShockChoices())) return;
        EndlessAbyssChoiceStore.Save(EndlessAbyssChoiceStore.Shock, next);
        choices = next;
        view.RefreshSelection(next, options, EndlessAbyssGazeService.RequiredShockChoices());
    }

    private static void Refresh(int slot)
    {
        if (request == null) return;
        var next = EndlessAbyssShockService.Choices(request);
        if (!next.Refresh(slot, EndlessAbyssChoiceCatalog.ShockIds, _ => true, EndlessAbyssChoiceStore.PickIndex))
        {
            view?.SetHint("该卡位的刷新次数已用完。");
            return;
        }
        EndlessAbyssChoiceStore.Save(EndlessAbyssChoiceStore.Shock, next);
        ShowChoices();
    }

    private static void Confirm()
    {
        if (resolving || request == null) return;
        resolving = true;
        try
        {
            view!.Confirm.interactable = false;
            var state = EndlessAbyssShockService.Choices(request);
            var result = EndlessAbyssShockService.ApplyPending(state.Selected.ToArray(), "EndlessAbyssShockPanel");
            if (!result.Success)
            {
                ShowChoices();
                view?.SetHint(result.Message);
                return;
            }
            var closed = onClosed;
            Close("EndlessAbyssShock.Confirm");
            closed?.Invoke();
        }
        finally { resolving = false; }
    }

    private static void Run(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            TerriasLog.Error("Endless abyss shock selection failed", ex);
            if (view != null && choices != null)
            {
                view.RefreshSelection(choices, options, EndlessAbyssGazeService.RequiredShockChoices());
                view.SetHint("操作未完成，请重试。");
            }
        }
    }

    public static void Close(string source)
    {
        var root = view?.Root;
        view?.ClearContent();
        view = null;
        request = null;
        choices = null;
        onClosed = null;
        options = Array.Empty<EndlessAbyssChoiceOption>();
        TerriasModalHost.Close(ref root, source, "[EndlessAbyssShock]");
        TerriasTransientUiRegistry.Unregister("EndlessAbyssShock");
    }
}
