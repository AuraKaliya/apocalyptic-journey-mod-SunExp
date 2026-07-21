using System;
using Terrias.Dll.Network;
using UnityEngine;
using UnityEngine.UI;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks.Ui;

public sealed class EndlessAbyssSettlementBarrierView : MonoBehaviour
{
    private static readonly Color PanelColor = new(0.025f, 0.035f, 0.075f, 0.94f);
    private static readonly Color ButtonColor = new(0.12f, 0.42f, 0.62f, 0.96f);
    private static readonly Color TextColor = new(0.92f, 0.96f, 1f, 1f);

    private GameExitUI? settlementUi;
    private string settlementToken = "";
    private bool detailsShown;
    private bool localCommitStarted;
    private float nextRefreshAt;
    private Button? actionButton;
    private Text? actionText;
    private Text? statusText;

    public void Bind(GameExitUI ui, string token)
    {
        if (ui == null || string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        settlementUi = ui;
        settlementToken = token;
        if (actionButton == null)
        {
            BuildUi();
        }

        transform.SetAsLastSibling();
        Refresh();
    }

    public void ForceCommit()
    {
        if (!TerriasNetworkRuntime.IsClientOnly() || localCommitStarted || settlementUi == null)
        {
            return;
        }

        localCommitStarted = true;
        Refresh();
        EndlessAbyssSettlementBarrierRuntime.CommitLocalPlayer(settlementUi, "barrier-force");
    }

    public void Refresh()
    {
        if (actionButton == null || actionText == null || statusText == null)
        {
            return;
        }

        var isClient = TerriasNetworkRuntime.IsClientOnly();
        var hostReady = EndlessAbyssSettlementBarrierRuntime.HostReady;
        var closing = EndlessAbyssSettlementBarrierRuntime.Closing;
        if (!detailsShown)
        {
            actionButton.interactable = true;
            actionText.text = "查看结算详情";
            statusText.text = isClient
                ? "结算由房主统一结束；完成本地结算后会安全返回。"
                : "所有玩家完成本地结算后，房主将最后关闭联机房间。";
            return;
        }

        if (isClient)
        {
            actionButton.interactable = !localCommitStarted && !closing;
            actionText.text = localCommitStarted ? "正在保存并返回…" : "完成结算";
            statusText.text = hostReady
                ? "房主已准备结束房间，请完成本地结算。"
                : "可以先完成本地结算；系统会向房主确认保存完成。";
            return;
        }

        actionButton.interactable = !hostReady && !closing;
        actionText.text = closing ? "正在安全结束…" : hostReady ? "等待其他玩家…" : "结束本次挑战";
        if (!hostReady)
        {
            statusText.text = "确认后将等待其他玩家保存，再由房主最后结束房间。";
            return;
        }

        var expected = EndlessAbyssSettlementBarrierRuntime.ExpectedRemoteCount;
        var committed = EndlessAbyssSettlementBarrierRuntime.CommittedRemoteCount;
        var deadline = EndlessAbyssSettlementBarrierRuntime.DeadlineUtcTicks;
        var seconds = deadline > 0L
            ? Math.Max(0, (int)Math.Ceiling(TimeSpan.FromTicks(deadline - DateTime.UtcNow.Ticks).TotalSeconds))
            : 0;
        statusText.text = closing
            ? "玩家结算已确认，房主正在最后关闭房间。"
            : "等待玩家保存：" + committed + "/" + expected + "（最长 " + seconds + " 秒）";
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
        {
            return;
        }

        nextRefreshAt = Time.unscaledTime + 0.2f;
        EndlessAbyssSettlementBarrierRuntime.Tick();
        Refresh();
    }

    private void OnDestroy()
    {
        EndlessAbyssSettlementBarrierRuntime.Detach(this);
    }

    private void BuildUi()
    {
        var shield = TerriasUiComponents.CreateFillRect("EndlessAbyssSettlementBarrier", transform);
        shield.transform.SetAsLastSibling();
        var shieldImage = shield.AddComponent<Image>();
        shieldImage.color = new Color(0f, 0f, 0f, 0.01f);
        shieldImage.raycastTarget = true;

        var panel = TerriasUiComponents.CreateRect(
            "SettlementBarrierPanel",
            shield.transform,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(660f, 104f));
        var panelRect = (RectTransform)panel.transform;
        panelRect.anchoredPosition = new Vector2(0f, 34f);
        var panelImage = panel.AddComponent<Image>();
        panelImage.color = PanelColor;
        panelImage.raycastTarget = true;

        statusText = TerriasUiComponents.ConfigureText(
            TerriasUiComponents.CreateRect(
                "Status",
                panel.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero),
            "",
            17,
            TextAnchor.MiddleLeft,
            TextColor);
        var statusRect = (RectTransform)statusText.transform;
        statusRect.offsetMin = new Vector2(22f, 12f);
        statusRect.offsetMax = new Vector2(-218f, -12f);

        actionButton = TerriasUiComponents.CreateTextButton(
            panel.transform,
            "结算",
            new Vector2(180f, 56f),
            null,
            ButtonColor,
            Color.white,
            20,
            OnActionClicked);
        var buttonRect = (RectTransform)actionButton.transform;
        buttonRect.anchorMin = new Vector2(1f, 0.5f);
        buttonRect.anchorMax = new Vector2(1f, 0.5f);
        buttonRect.pivot = new Vector2(1f, 0.5f);
        buttonRect.anchoredPosition = new Vector2(-20f, 0f);
        buttonRect.sizeDelta = new Vector2(180f, 56f);
        actionText = actionButton.GetComponentInChildren<Text>(true);
    }

    private void OnActionClicked()
    {
        if (settlementUi == null || string.IsNullOrWhiteSpace(settlementToken))
        {
            return;
        }

        if (!detailsShown)
        {
            detailsShown = true;
            settlementUi.NextShow();
            Refresh();
            return;
        }

        if (TerriasNetworkRuntime.IsClientOnly())
        {
            ForceCommit();
            return;
        }

        EndlessAbyssSettlementBarrierRuntime.MarkHostReady();
    }
}
