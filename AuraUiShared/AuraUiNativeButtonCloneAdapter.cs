using System;
using Michsky.MUIP;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AuraUi.Shared;

public sealed class AuraUiNativeButtonCloneRequest
{
    public UnityEngine.Component? Template { get; set; }

    public Transform? Parent { get; set; }

    public string CloneName { get; set; } = "AuraUiNativeButtonClone";

    public string Label { get; set; } = "";

    public float? TextSizeOverride { get; set; }

    public float? MinimumTextSizeOverride { get; set; }

    public UnityAction? OnClick { get; set; }

    public Action<GameObject>? StripOwnerBehaviours { get; set; }
}

public sealed class AuraUiNativeButtonCloneResult
{
    private AuraUiNativeButtonCloneResult(
        bool success,
        GameObject? root,
        UnityEngine.Component? manager,
        string failureReason)
    {
        Success = success;
        Root = root;
        Manager = manager;
        FailureReason = failureReason ?? "";
    }

    public bool Success { get; }

    public GameObject? Root { get; }

    public UnityEngine.Component? Manager { get; }

    public string FailureReason { get; }

    internal static AuraUiNativeButtonCloneResult Succeeded(GameObject root, UnityEngine.Component manager)
    {
        return new AuraUiNativeButtonCloneResult(true, root, manager, "");
    }

    internal static AuraUiNativeButtonCloneResult Failed(string reason)
    {
        return new AuraUiNativeButtonCloneResult(false, null, null, reason);
    }
}

public sealed class AuraUiNativeButtonCloneMarker : MonoBehaviour
{
    public const int CurrentProtocol = 2;

    public int Protocol = CurrentProtocol;

    public int SourceInstanceId;

    public string SourceName = "";
}

public sealed class AuraUiOwnedNativeButtonText : MonoBehaviour
{
}

/// <summary>
/// Owns the content of a cloned native button after the visual shell has been
/// detached from the template's text objects and localization writers.
/// </summary>
public sealed class AuraUiNativeButtonLabelOwner : MonoBehaviour
{
    private ButtonManager? manager;
    private string label = "";
    private float? textSizeOverride;
    private float? minimumTextSizeOverride;

    public void Configure(
        ButtonManager target,
        string value,
        float? textSize,
        float? minimumTextSize)
    {
        manager = target;
        label = value ?? "";
        textSizeOverride = textSize;
        minimumTextSizeOverride = minimumTextSize;
        ApplyNow();
    }

    public bool Owns(ButtonManager target)
    {
        return ReferenceEquals(manager, target)
               && IsOwned(manager.normalText)
               && IsOwned(manager.highlightedText)
               && IsOwned(manager.disabledText);
    }

    public void ApplyNow()
    {
        if (manager == null)
        {
            return;
        }

        if (!TextMatches())
        {
            manager.SetText(label);
        }

        if (!SizingMatches())
        {
            ApplySizing();
        }
    }

    private void OnEnable()
    {
        ApplyNow();
    }

    private void LateUpdate()
    {
        // Native owners may refresh their localized template after our hook.
        // Only write on divergence so normal layout and hover updates stay idle.
        ApplyNow();
    }

    private bool TextMatches()
    {
        return manager != null
               && string.Equals(manager.buttonText ?? "", label, StringComparison.Ordinal)
               && string.Equals(manager.normalText == null ? "" : manager.normalText.text ?? "", label, StringComparison.Ordinal)
               && string.Equals(manager.highlightedText == null ? "" : manager.highlightedText.text ?? "", label, StringComparison.Ordinal)
               && string.Equals(manager.disabledText == null ? "" : manager.disabledText.text ?? "", label, StringComparison.Ordinal);
    }

    private bool SizingMatches()
    {
        if (manager == null || !textSizeOverride.HasValue)
        {
            return true;
        }

        var maximum = textSizeOverride.Value;
        var minimum = minimumTextSizeOverride ?? maximum;
        var autoSize = minimum < maximum;
        return Approximately(manager.textSize, maximum)
               && TextSizingMatches(manager.normalText, minimum, maximum, autoSize)
               && TextSizingMatches(manager.highlightedText, minimum, maximum, autoSize)
               && TextSizingMatches(manager.disabledText, minimum, maximum, autoSize);
    }

    private void ApplySizing()
    {
        if (manager == null || !textSizeOverride.HasValue)
        {
            return;
        }

        var maximum = textSizeOverride.Value;
        var minimum = minimumTextSizeOverride ?? maximum;
        manager.textSize = maximum;
        ApplyTextSizing(manager.normalText, minimum, maximum);
        ApplyTextSizing(manager.highlightedText, minimum, maximum);
        ApplyTextSizing(manager.disabledText, minimum, maximum);
        manager.UpdateUI();
    }

    private static void ApplyTextSizing(TMP_Text? text, float minimum, float maximum)
    {
        if (text == null)
        {
            return;
        }

        text.enableAutoSizing = minimum < maximum;
        text.fontSizeMin = minimum;
        text.fontSizeMax = maximum;
        text.fontSize = maximum;
    }

    private static bool TextSizingMatches(TMP_Text? text, float minimum, float maximum, bool autoSize)
    {
        return text != null
               && text.enableAutoSizing == autoSize
               && Approximately(text.fontSizeMin, minimum)
               && Approximately(text.fontSizeMax, maximum);
    }

    private static bool Approximately(float left, float right)
    {
        return Mathf.Abs(left - right) < 0.01f;
    }

    private static bool IsOwned(TMP_Text? text)
    {
        return text != null && text.GetComponent<AuraUiOwnedNativeButtonText>() != null;
    }
}

/// <summary>
/// Deep-clones one game-native ButtonManager visual shell, replaces all state
/// labels with Aura-owned text nodes, and keeps gameplay meaning outside the
/// shared adapter.
/// </summary>
public static class AuraUiNativeButtonCloneAdapter
{
    public static AuraUiNativeButtonCloneResult TryClone(AuraUiNativeButtonCloneRequest request)
    {
        if (request == null)
        {
            return AuraUiNativeButtonCloneResult.Failed("request is null");
        }

        var template = request.Template as ButtonManager;
        if (template == null)
        {
            return AuraUiNativeButtonCloneResult.Failed("template ButtonManager is missing");
        }

        if (request.Parent == null)
        {
            return AuraUiNativeButtonCloneResult.Failed("clone parent is missing");
        }

        if (!TryValidateTemplate(template, out var failureReason))
        {
            return AuraUiNativeButtonCloneResult.Failed(failureReason);
        }

        GameObject? clone = null;
        try
        {
            var sourceSnapshot = ButtonTextSnapshot.Capture(template);
            clone = Object.Instantiate(template.gameObject, request.Parent, false);
            clone.name = string.IsNullOrWhiteSpace(request.CloneName)
                ? "AuraUiNativeButtonClone"
                : request.CloneName;
            clone.SetActive(false);

            var marker = clone.GetComponent<AuraUiNativeButtonCloneMarker>()
                         ?? clone.AddComponent<AuraUiNativeButtonCloneMarker>();
            marker.Protocol = AuraUiNativeButtonCloneMarker.CurrentProtocol;
            marker.SourceInstanceId = template.GetInstanceID();
            marker.SourceName = template.gameObject.name;

            request.StripOwnerBehaviours?.Invoke(clone);

            var configureResult = TryConfigureClone(
                template,
                clone,
                request.Label,
                request.OnClick,
                request.TextSizeOverride,
                request.MinimumTextSizeOverride,
                sourceSnapshot);
            if (!configureResult.Success)
            {
                DestroyRejectedClone(clone);
                return configureResult;
            }

            return configureResult;
        }
        catch (Exception ex)
        {
            if (clone != null)
            {
                DestroyRejectedClone(clone);
            }

            return AuraUiNativeButtonCloneResult.Failed("native button clone failed: " + ex.Message);
        }
    }

    public static AuraUiNativeButtonCloneResult TryConfigureClone(
        UnityEngine.Component? templateComponent,
        GameObject? cloneRoot,
        string label,
        UnityAction? onClick,
        float? textSizeOverride = null,
        float? minimumTextSizeOverride = null)
    {
        var template = templateComponent as ButtonManager;
        if (template == null)
        {
            return AuraUiNativeButtonCloneResult.Failed("template ButtonManager is missing");
        }

        return TryConfigureClone(
            template,
            cloneRoot,
            label,
            onClick,
            textSizeOverride,
            minimumTextSizeOverride,
            ButtonTextSnapshot.Capture(template));
    }

    public static bool IsOwnedClone(UnityEngine.Component? templateComponent, GameObject? cloneRoot)
    {
        var template = templateComponent as ButtonManager;
        if (template == null || cloneRoot == null)
        {
            return false;
        }

        var marker = cloneRoot.GetComponent<AuraUiNativeButtonCloneMarker>();
        return marker != null
               && marker.Protocol == AuraUiNativeButtonCloneMarker.CurrentProtocol
               && marker.SourceInstanceId == template.GetInstanceID();
    }

    private static AuraUiNativeButtonCloneResult TryConfigureClone(
        ButtonManager template,
        GameObject? cloneRoot,
        string label,
        UnityAction? onClick,
        float? textSizeOverride,
        float? minimumTextSizeOverride,
        ButtonTextSnapshot sourceSnapshot)
    {
        if (cloneRoot == null)
        {
            return AuraUiNativeButtonCloneResult.Failed("clone root is missing");
        }

        if (!TryValidateTemplate(template, out var failureReason))
        {
            return AuraUiNativeButtonCloneResult.Failed(failureReason);
        }

        if (!TryValidateTextSizing(textSizeOverride, minimumTextSizeOverride, out failureReason))
        {
            return AuraUiNativeButtonCloneResult.Failed(failureReason);
        }

        if (!IsOwnedClone(template, cloneRoot))
        {
            return AuraUiNativeButtonCloneResult.Failed("clone marker does not match the selected template");
        }

        var manager = cloneRoot.GetComponent<ButtonManager>();
        if (manager == null)
        {
            return AuraUiNativeButtonCloneResult.Failed("cloned root has no ButtonManager");
        }

        if (!TryEnsureOwnedStateLabels(manager, cloneRoot.transform, out failureReason))
        {
            return AuraUiNativeButtonCloneResult.Failed(failureReason);
        }

        if (!TryValidateOwnedTextReferences(template, manager, cloneRoot.transform, out failureReason))
        {
            return AuraUiNativeButtonCloneResult.Failed(failureReason);
        }

        var labelOwner = cloneRoot.GetComponent<AuraUiNativeButtonLabelOwner>()
                         ?? cloneRoot.AddComponent<AuraUiNativeButtonLabelOwner>();

        manager.onClick.RemoveAllListeners();
        manager.onDoubleClick.RemoveAllListeners();
        manager.onRightClick.RemoveAllListeners();
        manager.onHover.RemoveAllListeners();
        manager.onLeave.RemoveAllListeners();
        manager.enableText = true;
        manager.SetText(label ?? "");
        manager.Interactable(true);
        if (onClick != null)
        {
            manager.onClick.AddListener(onClick);
        }

        manager.UpdateUI();
        labelOwner.Configure(
            manager,
            label ?? "",
            textSizeOverride,
            minimumTextSizeOverride);

        var unityButton = cloneRoot.GetComponent<Button>();
        if (unityButton != null)
        {
            unityButton.onClick.RemoveAllListeners();
        }

        if (!sourceSnapshot.Matches(template))
        {
            return AuraUiNativeButtonCloneResult.Failed("configuring the clone changed the template label");
        }

        if (!CloneTextsMatch(manager, label ?? ""))
        {
            return AuraUiNativeButtonCloneResult.Failed("cloned visual-state labels did not accept the requested text");
        }

        return AuraUiNativeButtonCloneResult.Succeeded(cloneRoot, manager);
    }

    private static bool TryValidateTextSizing(
        float? textSizeOverride,
        float? minimumTextSizeOverride,
        out string failureReason)
    {
        if (!textSizeOverride.HasValue)
        {
            if (minimumTextSizeOverride.HasValue)
            {
                failureReason = "minimum text size requires a text size override";
                return false;
            }

            failureReason = "";
            return true;
        }

        var maximum = textSizeOverride.Value;
        var minimum = minimumTextSizeOverride ?? maximum;
        if (float.IsNaN(maximum)
            || float.IsInfinity(maximum)
            || float.IsNaN(minimum)
            || float.IsInfinity(minimum)
            || maximum <= 0f
            || minimum <= 0f
            || minimum > maximum)
        {
            failureReason = "native button text sizing is invalid";
            return false;
        }

        failureReason = "";
        return true;
    }

    private static bool TryEnsureOwnedStateLabels(
        ButtonManager manager,
        Transform cloneRoot,
        out string failureReason)
    {
        var owner = cloneRoot.GetComponent<AuraUiNativeButtonLabelOwner>();
        if (owner != null && owner.Owns(manager))
        {
            failureReason = "";
            return true;
        }

        if (!TryCreateOwnedLabel(manager.normalText, "Normal", cloneRoot, out var normal, out failureReason)
            || !TryCreateOwnedLabel(manager.highlightedText, "Highlighted", cloneRoot, out var highlighted, out failureReason)
            || !TryCreateOwnedLabel(manager.disabledText, "Disabled", cloneRoot, out var disabled, out failureReason))
        {
            return false;
        }

        manager.normalText = normal;
        manager.highlightedText = highlighted;
        manager.disabledText = disabled;
        failureReason = "";
        return true;
    }

    private static bool TryCreateOwnedLabel(
        TextMeshProUGUI source,
        string stateName,
        Transform cloneRoot,
        out TextMeshProUGUI owned,
        out string failureReason)
    {
        owned = null!;
        if (source == null || source.transform == cloneRoot || source.transform.parent == null)
        {
            failureReason = stateName + " template label cannot be detached from the clone root";
            return false;
        }

        var sourceRect = source.rectTransform;
        var sourceObject = source.gameObject;
        var siblingIndex = source.transform.GetSiblingIndex();
        sourceObject.name = "TemplateText-Disabled-" + stateName;
        sourceObject.SetActive(false);

        var labelObject = new GameObject("AuraText-" + stateName, typeof(RectTransform));
        labelObject.transform.SetParent(source.transform.parent, false);
        labelObject.transform.SetSiblingIndex(siblingIndex + 1);
        var rect = labelObject.GetComponent<RectTransform>();
        CopyRect(sourceRect, rect);

        owned = labelObject.AddComponent<TextMeshProUGUI>();
        CopyTextStyle(source, owned);
        owned.text = source.text ?? "";
        owned.raycastTarget = false;
        labelObject.AddComponent<AuraUiOwnedNativeButtonText>();
        failureReason = "";
        return true;
    }

    private static void CopyRect(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.anchoredPosition = source.anchoredPosition;
        target.sizeDelta = source.sizeDelta;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }

    private static void CopyTextStyle(TMP_Text source, TMP_Text target)
    {
        target.font = source.font;
        target.fontSharedMaterial = source.fontSharedMaterial;
        target.fontSize = source.fontSize;
        target.fontStyle = source.fontStyle;
        target.enableAutoSizing = source.enableAutoSizing;
        target.fontSizeMin = source.fontSizeMin;
        target.fontSizeMax = source.fontSizeMax;
        target.color = source.color;
        target.alignment = source.alignment;
        target.overflowMode = source.overflowMode;
        target.margin = source.margin;
        target.characterSpacing = source.characterSpacing;
        target.wordSpacing = source.wordSpacing;
        target.lineSpacing = source.lineSpacing;
        target.paragraphSpacing = source.paragraphSpacing;
        target.richText = source.richText;
        target.isRightToLeftText = source.isRightToLeftText;
    }

    private static bool TryValidateTemplate(ButtonManager template, out string failureReason)
    {
        var root = template.transform;
        if (!IsOwnedBy(root, template.normalText)
            || !IsOwnedBy(root, template.highlightedText)
            || !IsOwnedBy(root, template.disabledText))
        {
            failureReason = "template state labels are missing or live outside the template root";
            return false;
        }

        if (!AreDistinct(template.normalText, template.highlightedText, template.disabledText))
        {
            failureReason = "template state labels share the same text reference";
            return false;
        }

        if (!IsOwnedBy(root, template.normalCG)
            || !IsOwnedBy(root, template.highlightCG)
            || !IsOwnedBy(root, template.disabledCG)
            || !AreDistinct(template.normalCG, template.highlightCG, template.disabledCG))
        {
            failureReason = "template visual-state roots are missing, shared, or live outside the template root";
            return false;
        }

        failureReason = "";
        return true;
    }

    private static bool TryValidateOwnedTextReferences(
        ButtonManager template,
        ButtonManager clone,
        Transform cloneRoot,
        out string failureReason)
    {
        if (!IsOwnedBy(cloneRoot, clone.normalText)
            || !IsOwnedBy(cloneRoot, clone.highlightedText)
            || !IsOwnedBy(cloneRoot, clone.disabledText))
        {
            failureReason = "cloned state labels are missing or live outside the clone root";
            return false;
        }

        if (!AreDistinct(clone.normalText, clone.highlightedText, clone.disabledText))
        {
            failureReason = "cloned state labels share the same text reference";
            return false;
        }

        if (ReferenceEquals(template.normalText, clone.normalText)
            || ReferenceEquals(template.highlightedText, clone.highlightedText)
            || ReferenceEquals(template.disabledText, clone.disabledText))
        {
            failureReason = "cloned state labels still reference the template";
            return false;
        }

        if (!IsOwnedBy(cloneRoot, clone.normalCG)
            || !IsOwnedBy(cloneRoot, clone.highlightCG)
            || !IsOwnedBy(cloneRoot, clone.disabledCG)
            || !AreDistinct(clone.normalCG, clone.highlightCG, clone.disabledCG))
        {
            failureReason = "cloned visual-state roots are missing, shared, or live outside the clone root";
            return false;
        }

        if (ReferenceEquals(template.normalCG, clone.normalCG)
            || ReferenceEquals(template.highlightCG, clone.highlightCG)
            || ReferenceEquals(template.disabledCG, clone.disabledCG))
        {
            failureReason = "cloned visual-state roots still reference the template";
            return false;
        }

        failureReason = "";
        return true;
    }

    private static bool IsOwnedBy(Transform root, TMP_Text? text)
    {
        return text != null
               && text.transform != null
               && (text.transform == root || text.transform.IsChildOf(root));
    }

    private static bool IsOwnedBy(Transform root, UnityEngine.Component? component)
    {
        return component != null
               && component.transform != null
               && (component.transform == root || component.transform.IsChildOf(root));
    }

    private static bool AreDistinct(TMP_Text first, TMP_Text second, TMP_Text third)
    {
        return !ReferenceEquals(first, second)
               && !ReferenceEquals(first, third)
               && !ReferenceEquals(second, third);
    }

    private static bool AreDistinct(UnityEngine.Component first, UnityEngine.Component second, UnityEngine.Component third)
    {
        return !ReferenceEquals(first, second)
               && !ReferenceEquals(first, third)
               && !ReferenceEquals(second, third);
    }

    private static bool CloneTextsMatch(ButtonManager manager, string label)
    {
        return string.Equals(manager.buttonText, label, StringComparison.Ordinal)
               && string.Equals(manager.normalText.text, label, StringComparison.Ordinal)
               && string.Equals(manager.highlightedText.text, label, StringComparison.Ordinal)
               && string.Equals(manager.disabledText.text, label, StringComparison.Ordinal);
    }

    private static void DestroyRejectedClone(GameObject clone)
    {
        clone.SetActive(false);
        Object.Destroy(clone);
    }

    private sealed class ButtonTextSnapshot
    {
        private ButtonTextSnapshot(string button, string normal, string highlighted, string disabled)
        {
            Button = button;
            Normal = normal;
            Highlighted = highlighted;
            Disabled = disabled;
        }

        private string Button { get; }

        private string Normal { get; }

        private string Highlighted { get; }

        private string Disabled { get; }

        public static ButtonTextSnapshot Capture(ButtonManager manager)
        {
            return new ButtonTextSnapshot(
                manager.buttonText ?? "",
                manager.normalText == null ? "" : manager.normalText.text ?? "",
                manager.highlightedText == null ? "" : manager.highlightedText.text ?? "",
                manager.disabledText == null ? "" : manager.disabledText.text ?? "");
        }

        public bool Matches(ButtonManager manager)
        {
            return string.Equals(manager.buttonText ?? "", Button, StringComparison.Ordinal)
                   && string.Equals(manager.normalText == null ? "" : manager.normalText.text ?? "", Normal, StringComparison.Ordinal)
                   && string.Equals(manager.highlightedText == null ? "" : manager.highlightedText.text ?? "", Highlighted, StringComparison.Ordinal)
                   && string.Equals(manager.disabledText == null ? "" : manager.disabledText.text ?? "", Disabled, StringComparison.Ordinal);
        }
    }
}
