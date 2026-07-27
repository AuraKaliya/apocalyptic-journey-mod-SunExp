using AuraCombatAi.Shared;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.AutoBattle;

internal sealed class AuraToolsAutoBattlePredictionPresenter : MonoBehaviour
{
    private const string UnitMarkerResource = "UI/SelectedIcon";
    private const float OverlayBorderThickness = 5f;
    private const float OverlayBorderPadding = 8f;
    private const float CardBorderThickness = 1.5f;
    private const float CardBorderPadding = 1.5f;
    private static readonly Color EnemyColor = new(1f, 0.16f, 0.12f, 0.72f);
    private static readonly Color FriendlyColor = new(0.18f, 1f, 0.32f, 0.72f);
    private static readonly Color ActionColor = new(1f, 0.78f, 0.08f, 0.86f);

    private readonly Vector3[] worldCorners = new Vector3[4];
    private readonly Vector2[] localCorners = new Vector2[4];
    private readonly RectTransform?[] actionEdges = new RectTransform?[4];
    private GameObject? unitMarker;
    private SpriteRenderer? unitMarkerRenderer;
    private RectTransform? actionFrame;
    private RectTransform? actionFrameParent;
    private RectTransform? actionTarget;
    private bool cardFrameMode;
    private Vector2 actionFallbackSize;
    private string stateFingerprint = "";
    private string candidateId = "";
    private float holdActionUntil;
    private bool unitLoadFailed;
    private Vector3 unitMarkerLocalPosition;
    private Quaternion unitMarkerLocalRotation = Quaternion.identity;
    private Vector3 unitMarkerLocalScale = Vector3.one;

    public bool IsShowing(string fingerprint, string selectedCandidateId)
    {
        return !string.IsNullOrWhiteSpace(candidateId)
               && string.Equals(stateFingerprint, fingerprint, System.StringComparison.Ordinal)
               && string.Equals(candidateId, selectedCandidateId, System.StringComparison.Ordinal)
               && actionFrame != null
               && actionFrame.gameObject.activeSelf;
    }

    public bool Show(
        FightUI fightUi,
        string fingerprint,
        CombatActionObservation action,
        UnityEngine.Component actionComponent,
        StatusManager? target,
        float actionHoldSeconds = 0f)
    {
        Clear();
        if (fightUi == null
            || action == null
            || actionComponent == null
            || ResolveActionTarget(actionComponent, action.Kind) is not { } actionRect)
        {
            return false;
        }

        stateFingerprint = fingerprint ?? "";
        candidateId = action.CandidateId ?? "";
        actionTarget = actionRect;
        cardFrameMode = action.Kind == CombatActionKind.PlayCard
                        && actionComponent is CardItem;
        actionFallbackSize = FallbackSize(action.Kind);
        holdActionUntil = Time.unscaledTime + Mathf.Max(0f, actionHoldSeconds);
        EnsureActionFrame(fightUi);
        SyncActionFrame();
        ShowUnitMarker(action, target);
        return actionFrame != null && actionFrame.gameObject.activeSelf;
    }

    public void Clear()
    {
        stateFingerprint = "";
        candidateId = "";
        actionTarget = null;
        cardFrameMode = false;
        actionFallbackSize = Vector2.zero;
        holdActionUntil = 0f;
        if (actionFrame != null)
        {
            actionFrame.gameObject.SetActive(false);
        }
        if (unitMarker != null)
        {
            unitMarker.SetActive(false);
        }
    }

    private void LateUpdate()
    {
        SyncActionFrame();
    }

    private void EnsureActionFrame(FightUI fightUi)
    {
        if (fightUi.transform is not RectTransform parent)
        {
            return;
        }

        if (actionFrame == null)
        {
            var root = new GameObject(
                "AuraToolsAutoBattlePredictionFrame",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(LayoutElement));
            actionFrame = root.GetComponent<RectTransform>();
            actionFrame.anchorMin = new Vector2(0.5f, 0.5f);
            actionFrame.anchorMax = new Vector2(0.5f, 0.5f);
            actionFrame.pivot = new Vector2(0.5f, 0.5f);
            actionFrame.anchoredPosition = Vector2.zero;
            actionFrame.sizeDelta = Vector2.zero;
            var group = root.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            root.GetComponent<LayoutElement>().ignoreLayout = true;
            for (var index = 0; index < actionEdges.Length; index++)
            {
                actionEdges[index] = CreateEdge(actionFrame, "Edge" + index);
            }
        }

        var requestedParent = cardFrameMode && actionTarget?.parent is RectTransform cardParent
            ? cardParent
            : parent;
        if (actionFrameParent != requestedParent)
        {
            actionFrame.SetParent(requestedParent, false);
            actionFrameParent = requestedParent;
        }
        if (cardFrameMode && actionTarget != null && actionTarget.parent == actionFrameParent)
        {
            PlaceImmediatelyBehind(actionFrame, actionTarget);
        }
        else
        {
            ResetOverlayTransform(actionFrame);
            actionFrame.SetAsLastSibling();
        }
        actionFrame.gameObject.SetActive(true);
    }

    private static RectTransform CreateEdge(RectTransform parent, string name)
    {
        var edge = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rect = edge.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        var image = edge.GetComponent<Image>();
        image.color = ActionColor;
        image.raycastTarget = false;
        return rect;
    }

    private void SyncActionFrame()
    {
        if (actionFrame == null || !actionFrame.gameObject.activeSelf || actionFrameParent == null)
        {
            return;
        }

        if (actionTarget == null || !actionTarget.gameObject.activeInHierarchy)
        {
            if (Time.unscaledTime >= holdActionUntil)
            {
                actionFrame.gameObject.SetActive(false);
            }
            return;
        }

        if (cardFrameMode && SyncCardFrame())
        {
            return;
        }

        SyncOverlayFrame();
    }

    private bool SyncCardFrame()
    {
        if (actionFrame == null
            || actionTarget == null
            || actionTarget.parent is not RectTransform targetParent)
        {
            return false;
        }

        if (actionFrameParent != targetParent)
        {
            actionFrame.SetParent(targetParent, false);
            actionFrameParent = targetParent;
        }
        PlaceImmediatelyBehind(actionFrame, actionTarget);

        actionFrame.anchorMin = actionTarget.anchorMin;
        actionFrame.anchorMax = actionTarget.anchorMax;
        actionFrame.pivot = actionTarget.pivot;
        actionFrame.anchoredPosition3D = actionTarget.anchoredPosition3D;
        actionFrame.sizeDelta = actionTarget.sizeDelta;
        actionFrame.localRotation = actionTarget.localRotation;
        actionFrame.localScale = actionTarget.localScale;

        var rect = actionTarget.rect;
        localCorners[0] = new Vector2(
            rect.xMin - CardBorderPadding,
            rect.yMin - CardBorderPadding);
        localCorners[1] = new Vector2(
            rect.xMin - CardBorderPadding,
            rect.yMax + CardBorderPadding);
        localCorners[2] = new Vector2(
            rect.xMax + CardBorderPadding,
            rect.yMax + CardBorderPadding);
        localCorners[3] = new Vector2(
            rect.xMax + CardBorderPadding,
            rect.yMin - CardBorderPadding);
        SyncEdges(CardBorderThickness);
        return true;
    }

    private void SyncOverlayFrame()
    {
        if (actionFrame == null || actionTarget == null || actionFrameParent == null)
        {
            return;
        }

        ResetOverlayTransform(actionFrame);
        actionFrame.SetAsLastSibling();
        actionTarget.GetWorldCorners(worldCorners);
        var center = Vector2.zero;
        for (var index = 0; index < worldCorners.Length; index++)
        {
            var local = actionFrameParent.InverseTransformPoint(worldCorners[index]);
            localCorners[index] = new Vector2(local.x, local.y);
            center += localCorners[index];
        }
        center *= 0.25f;

        if ((localCorners[2] - localCorners[1]).sqrMagnitude < 1f
            || (localCorners[1] - localCorners[0]).sqrMagnitude < 1f)
        {
            var half = actionFallbackSize * 0.5f;
            localCorners[0] = center + new Vector2(-half.x, -half.y);
            localCorners[1] = center + new Vector2(-half.x, half.y);
            localCorners[2] = center + new Vector2(half.x, half.y);
            localCorners[3] = center + new Vector2(half.x, -half.y);
        }

        for (var index = 0; index < localCorners.Length; index++)
        {
            var outward = localCorners[index] - center;
            if (outward.sqrMagnitude > 0.001f)
            {
                localCorners[index] += outward.normalized * OverlayBorderPadding * 1.414214f;
            }
        }

        SyncEdges(OverlayBorderThickness);
    }

    private void SyncEdges(float thickness)
    {
        for (var index = 0; index < actionEdges.Length; index++)
        {
            SyncEdge(
                actionEdges[index],
                localCorners[index],
                localCorners[(index + 1) % 4],
                thickness);
        }
    }

    private static void SyncEdge(
        RectTransform? edge,
        Vector2 start,
        Vector2 end,
        float thickness)
    {
        if (edge == null)
        {
            return;
        }

        var delta = end - start;
        edge.anchoredPosition = (start + end) * 0.5f;
        edge.sizeDelta = new Vector2(delta.magnitude + thickness, thickness);
        edge.localRotation = Quaternion.Euler(
            0f,
            0f,
            Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        edge.localScale = Vector3.one;
    }

    private static void PlaceImmediatelyBehind(
        RectTransform frame,
        RectTransform target)
    {
        if (frame.parent != target.parent)
        {
            return;
        }

        var frameIndex = frame.GetSiblingIndex();
        var targetIndex = target.GetSiblingIndex();
        if (frameIndex + 1 == targetIndex)
        {
            return;
        }

        var desiredIndex = targetIndex - (frameIndex < targetIndex ? 1 : 0);
        frame.SetSiblingIndex(Mathf.Max(0, desiredIndex));
    }

    private static void ResetOverlayTransform(RectTransform frame)
    {
        frame.anchorMin = new Vector2(0.5f, 0.5f);
        frame.anchorMax = new Vector2(0.5f, 0.5f);
        frame.pivot = new Vector2(0.5f, 0.5f);
        frame.anchoredPosition3D = Vector3.zero;
        frame.sizeDelta = Vector2.zero;
        frame.localRotation = Quaternion.identity;
        frame.localScale = Vector3.one;
    }

    private static RectTransform? ResolveActionTarget(
        UnityEngine.Component actionComponent,
        CombatActionKind kind)
    {
        if (kind == CombatActionKind.PlayCard
            && actionComponent is CardItem card
            && card.uiElement != null)
        {
            return card.uiElement;
        }

        return actionComponent.transform as RectTransform;
    }

    private void ShowUnitMarker(
        CombatActionObservation action,
        StatusManager? status)
    {
        if (status == null
            || (action.TargetKind != CombatTargetKind.Enemy
                && action.TargetKind != CombatTargetKind.Self
                && action.TargetKind != CombatTargetKind.Friendly))
        {
            return;
        }

        EnsureUnitMarker(status);
        if (unitMarker == null || unitMarkerRenderer == null)
        {
            return;
        }

        unitMarker.transform.SetParent(status.transform, false);
        unitMarker.transform.localPosition = unitMarkerLocalPosition;
        unitMarker.transform.localRotation = unitMarkerLocalRotation;
        unitMarker.transform.localScale = unitMarkerLocalScale;
        unitMarkerRenderer.color = action.TargetKind == CombatTargetKind.Enemy
            ? EnemyColor
            : FriendlyColor;
        unitMarker.SetActive(true);
    }

    private void EnsureUnitMarker(StatusManager owner)
    {
        if (unitMarker != null)
        {
            return;
        }
        if (unitLoadFailed)
        {
            return;
        }

        var prefab = AuraToolsResourceCache.Load<GameObject>(UnitMarkerResource, true);
        if (prefab == null)
        {
            unitLoadFailed = true;
            AuraToolsLog.Warn("[AutoBattle] native prediction target marker is unavailable");
            return;
        }

        unitMarker = Object.Instantiate(prefab, owner.transform);
        unitMarker.name = "AuraToolsAutoBattlePredictionTarget";
        unitMarkerLocalPosition = unitMarker.transform.localPosition;
        unitMarkerLocalRotation = unitMarker.transform.localRotation;
        unitMarkerLocalScale = unitMarker.transform.localScale;
        unitMarkerRenderer = unitMarker.GetComponentInChildren<SpriteRenderer>(true);
        if (unitMarkerRenderer == null)
        {
            Object.Destroy(unitMarker);
            unitMarker = null;
            unitLoadFailed = true;
            AuraToolsLog.Warn("[AutoBattle] native prediction target marker has no SpriteRenderer");
            return;
        }
        foreach (var graphic in unitMarker.GetComponentsInChildren<Graphic>(true))
        {
            graphic.raycastTarget = false;
        }
    }

    private static Vector2 FallbackSize(CombatActionKind kind)
    {
        return kind switch
        {
            CombatActionKind.PlayCard => new Vector2(120f, 170f),
            CombatActionKind.UseSkill => new Vector2(72f, 72f),
            CombatActionKind.EndTurn => new Vector2(120f, 48f),
            _ => new Vector2(72f, 48f)
        };
    }

    private void OnDestroy()
    {
        if (actionFrame != null)
        {
            Object.Destroy(actionFrame.gameObject);
        }
        if (unitMarker != null)
        {
            Object.Destroy(unitMarker);
        }
    }
}
