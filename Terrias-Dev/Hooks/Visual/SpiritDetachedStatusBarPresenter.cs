using System;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using TMPro;
using UnityEngine;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks.Visual;

[DefaultExecutionOrder(10000)]
internal sealed class SpiritDetachedStatusBarPresenter : MonoBehaviour
{
    private const float DisplayScale = 0.72f;
    private const float GapAt1080 = 14f;
    private const float BarRotationDegrees = -90f;
    private const float TextCounterRotationDegrees = 90f;
    private const float CounterLineSpacing = -10f;
    private const string RootPrefix = "Terrias_SpiritDetachedStatus:";

    private StatusManager? status;
    private SpriteRenderer? actorRenderer;
    private StatusBarUI? sourceBar;
    private GameObject? sourceStatusRoot;
    private GameObject? sourceEffectRoot;
    private GameObject? displayRoot;
    private GameObject? hpItem;
    private SpriteRenderer? healthFill;
    private SpriteRenderer? healthDelayFill;
    private SpriteRenderer? defendFill;
    private GameObject? defendRoot;
    private TMP_Text? healthText;
    private TMP_Text? defendText;
    private Vector2 healthTextBaseSize;
    private Vector2 defendTextBaseSize;
    private bool sourceStatusWasActive;
    private bool sourceEffectWasActive;
    private bool configured;
    private bool restored;
    private int lastHp = int.MinValue;
    private int lastMaxHp = int.MinValue;
    private int lastDefend = int.MinValue;

    public void Configure(StatusManager? nextStatus, SpriteRenderer renderer)
    {
        status = nextStatus;
        actorRenderer = renderer;
        sourceBar = nextStatus?.statusBarUI
                    ?? nextStatus?.statusBarObj?.GetComponent<StatusBarUI>();
        sourceStatusRoot = nextStatus?.statusBarObj;
        sourceEffectRoot = nextStatus?.effectListObj;
        if (sourceBar?.hpItemObj == null || sourceStatusRoot?.transform.parent == null)
        {
            HideNativePresentation();
            TerriasLog.Warn("[SpiritAttachment] detached status bar unavailable; native presentation suppressed.");
            return;
        }

        sourceStatusWasActive = sourceStatusRoot.activeSelf;
        sourceEffectWasActive = sourceEffectRoot?.activeSelf == true;
        try
        {
            displayRoot = new GameObject(
                RootPrefix + (nextStatus?.InstanceId ?? "unknown"),
                typeof(RectTransform));
            CompanionSceneApi.MoveToOwnerScene(displayRoot, sourceStatusRoot, "SpiritAttachment.DetachedStatusRoot");
            displayRoot.layer = sourceBar.hpItemObj.layer;
            displayRoot.transform.SetParent(sourceStatusRoot.transform.parent, false);
            displayRoot.transform.localScale = sourceStatusRoot.transform.localScale * DisplayScale;
            displayRoot.transform.localRotation = sourceStatusRoot.transform.localRotation;
            displayRoot.transform.SetAsFirstSibling();

            hpItem = UnityEngine.Object.Instantiate(sourceBar.hpItemObj);
            CompanionSceneApi.MoveToOwnerScene(hpItem, displayRoot, "SpiritAttachment.DetachedHpItem");
            hpItem.name = "HpItem";
            hpItem.transform.SetParent(displayRoot.transform, false);
            hpItem.transform.localPosition = sourceBar.hpItemObj.transform.localPosition;
            hpItem.transform.localScale = sourceBar.hpItemObj.transform.localScale;
            hpItem.transform.localRotation = sourceBar.hpItemObj.transform.localRotation
                                             * Quaternion.Euler(0f, 0f, BarRotationDegrees);
            hpItem.SetActive(true);

            healthFill = hpItem.transform.Find("fill")?.GetComponent<SpriteRenderer>();
            healthDelayFill = hpItem.transform.Find("redfill")?.GetComponent<SpriteRenderer>();
            defendFill = hpItem.transform.Find("bluefill")?.GetComponent<SpriteRenderer>();
            defendRoot = hpItem.transform.Find("DefendShow")?.gameObject;
            healthText = hpItem.transform.Find("hpTxt")?.GetComponent<TMP_Text>();
            defendText = hpItem.transform.Find("DefendShow/val")?.GetComponent<TMP_Text>();
            ConfigureCounterText(healthText, out healthTextBaseSize);
            ConfigureCounterText(defendText, out defendTextBaseSize);

            configured = healthText != null && healthFill != null;
            restored = false;
            HideNativePresentation();
            RefreshValues(force: true);
            LogGeometry();
        }
        catch (Exception ex)
        {
            configured = false;
            HideNativePresentation();
            DestroyDisplayRoot();
            TerriasLog.Warn("[SpiritAttachment] detached status bar creation failed: " + ex.Message);
        }
    }

    private void LateUpdate()
    {
        HideNativePresentation();
        if (restored || status == null || actorRenderer == null)
        {
            return;
        }

        var visible = actorRenderer.enabled && actorRenderer.sprite != null;
        GetComponent<ProjectionVisualProxy>()?.RefreshNativeUiAnchors(visible);
        if (!configured || displayRoot == null)
        {
            return;
        }

        if (!visible || Camera.main == null)
        {
            if (displayRoot.activeSelf) displayRoot.SetActive(false);
            return;
        }

        if (!displayRoot.activeSelf) displayRoot.SetActive(true);
        PositionAtActorRight(actorRenderer.bounds, Camera.main);
        RefreshValues(force: false);
    }

    private void PositionAtActorRight(Bounds bounds, Camera worldCamera)
    {
        if (displayRoot?.transform.parent is not RectTransform parentRect)
        {
            return;
        }

        var screenPoint = worldCamera.WorldToScreenPoint(new Vector3(bounds.max.x, bounds.center.y, bounds.center.z));
        if (!IsFinite(screenPoint) || screenPoint.z <= 0f)
        {
            displayRoot.SetActive(false);
            return;
        }

        screenPoint.x = Mathf.Clamp(
            screenPoint.x + Screen.height * GapAt1080 / 1080f,
            20f,
            Mathf.Max(20f, Screen.width - 20f));
        screenPoint.y = Mathf.Clamp(screenPoint.y, 50f, Mathf.Max(50f, Screen.height - 50f));
        var canvas = parentRect.GetComponentInParent<Canvas>();
        var uiCamera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : canvas.worldCamera ?? worldCamera;
        if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(parentRect, screenPoint, uiCamera, out var targetWorld)
            || !IsFinite(targetWorld))
        {
            return;
        }

        displayRoot.transform.position = targetWorld;
        if (sourceStatusRoot != null)
        {
            // Native damage/heal text reads this position even while the source
            // status bar is hidden, so keep its anchor aligned with the clone.
            sourceStatusRoot.transform.localPosition = displayRoot.transform.localPosition;
        }
    }

    private void RefreshValues(bool force)
    {
        if (status == null)
        {
            return;
        }

        var hp = Math.Max(0, status.CurHp);
        var maxHp = Math.Max(0, status.MaxHp);
        var defend = Math.Max(0, status.Defend);
        if (!force && hp == lastHp && maxHp == lastMaxHp && defend == lastDefend)
        {
            return;
        }

        lastHp = hp;
        lastMaxHp = maxHp;
        lastDefend = defend;
        var hpRatio = maxHp <= 0 ? 0f : Mathf.Clamp01((float)hp / maxHp);
        var defendRatio = maxHp <= 0 ? 0f : Mathf.Clamp01((float)defend / maxHp);
        SetFill(healthFill, hpRatio, enabled: true);
        SetFill(healthDelayFill, hpRatio, enabled: true);
        SetFill(defendFill, defendRatio, enabled: defend > 0);
        UpdateCounterText(healthText, hp, healthTextBaseSize);
        UpdateCounterText(defendText, defend, defendTextBaseSize);
        if (defendRoot != null)
        {
            defendRoot.SetActive(true);
            defendRoot.transform.Find("Large")?.gameObject.SetActive(defend >= 100);
            defendRoot.transform.Find("Small")?.gameObject.SetActive(defend < 100);
        }
    }

    private static void ConfigureCounterText(TMP_Text? text, out Vector2 baseSize)
    {
        baseSize = Vector2.zero;
        if (text == null)
        {
            return;
        }

        var rect = text.rectTransform;
        baseSize = rect?.sizeDelta ?? Vector2.zero;
        text.transform.localRotation = text.transform.localRotation
                                       * Quaternion.Euler(0f, 0f, TextCounterRotationDegrees);
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.enableAutoSizing = false;
        text.lineSpacing = CounterLineSpacing;
    }

    private static void UpdateCounterText(TMP_Text? text, int value, Vector2 baseSize)
    {
        if (text == null)
        {
            return;
        }

        var content = SpiritStatusBarText.FormatVerticalDigits(value);
        text.SetText(content);
        var lineCount = Math.Max(1, content.Count(character => character == '\n') + 1);
        if (text.rectTransform != null)
        {
            var lineHeight = baseSize.y > 0.01f
                ? baseSize.y
                : Mathf.Max(1f, text.fontSize * 1.15f);
            text.rectTransform.sizeDelta = new Vector2(
                baseSize.x > 0.01f ? baseSize.x : Mathf.Max(1f, text.fontSize * 1.2f),
                Mathf.Max(lineHeight, lineHeight * lineCount));
        }

        text.ForceMeshUpdate();
    }

    private static void SetFill(SpriteRenderer? renderer, float value, bool enabled)
    {
        if (renderer == null)
        {
            return;
        }

        renderer.enabled = enabled;
        var material = renderer.material;
        if (material != null && material.HasProperty("_FillAmount"))
        {
            material.SetFloat("_FillAmount", Mathf.Clamp01(value));
        }
    }

    private void HideNativePresentation()
    {
        if (sourceStatusRoot?.activeSelf == true) sourceStatusRoot.SetActive(false);
        if (sourceEffectRoot?.activeSelf == true) sourceEffectRoot.SetActive(false);
    }

    private void LogGeometry()
    {
        var canvas = displayRoot?.GetComponentInParent<Canvas>();
        TerriasLog.Info("[SpiritAttachment] detached status bar ready; parent="
                        + (displayRoot?.transform.parent?.name ?? "none")
                        + ", canvas="
                        + (canvas?.renderMode.ToString() ?? "none")
                        + ", rootScale="
                        + VectorText(displayRoot?.transform.localScale ?? Vector3.zero)
                        + ", hpItemScale="
                        + VectorText(hpItem?.transform.localScale ?? Vector3.zero)
                        + ", hpTextScale="
                        + VectorText(healthText?.transform.localScale ?? Vector3.zero)
                        + ", hpFont="
                        + (healthText?.fontSize.ToString("0.##") ?? "none")
                        + ", rotation="
                        + BarRotationDegrees.ToString("0")
                        + ".");
    }

    private static string VectorText(Vector3 value)
    {
        return value.x.ToString("0.###") + "/" + value.y.ToString("0.###") + "/" + value.z.ToString("0.###");
    }

    private void OnEnable()
    {
        HideNativePresentation();
    }

    private void OnDisable()
    {
        if (restored)
        {
            return;
        }

        HideNativePresentation();
        if (displayRoot != null) displayRoot.SetActive(false);
        status?.actionContent?.SetActive(false);
    }

    public void RestorePresentation()
    {
        if (restored)
        {
            return;
        }

        restored = true;
        DestroyDisplayRoot();
        if (sourceStatusRoot != null) sourceStatusRoot.SetActive(sourceStatusWasActive);
        if (sourceEffectRoot != null) sourceEffectRoot.SetActive(sourceEffectWasActive);
    }

    private void OnDestroy()
    {
        DestroyDisplayRoot();
    }

    private void DestroyDisplayRoot()
    {
        if (displayRoot == null)
        {
            return;
        }

        displayRoot.SetActive(false);
        UnityEngine.Object.Destroy(displayRoot);
        displayRoot = null;
        hpItem = null;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
               && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
               && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
