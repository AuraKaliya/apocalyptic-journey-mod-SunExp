using System;
using System.Collections.Generic;
using System.Linq;
using Michsky.MUIP;
using UnityEngine;
using UnityEngine.UI;

namespace AuraUi.Shared;

public sealed class AuraUiOwnedNativeButtonIcon : MonoBehaviour
{
}

public readonly struct AuraUiNativeButtonIconApplyResult
{
    public AuraUiNativeButtonIconApplyResult(bool success, int ownedStateCount, bool usedCustomContent, string failureReason)
    {
        Success = success;
        OwnedStateCount = ownedStateCount;
        UsedCustomContent = usedCustomContent;
        FailureReason = failureReason ?? "";
    }

    public bool Success { get; }

    public int OwnedStateCount { get; }

    public bool UsedCustomContent { get; }

    public string FailureReason { get; }
}

/// <summary>
/// Replaces a cloned native button's serialized state images with consumer-owned
/// image nodes. When a native template does not populate ButtonManager's state
/// image references, the owner adopts the visible Image components that still use
/// the manager's serialized buttonIcon. Both paths bypass template-specific icon
/// wiring while preserving the native raycast and hover state lifecycle.
/// </summary>
public sealed class AuraUiNativeButtonIconOwner : MonoBehaviour
{
    private ButtonManager? manager;
    private Sprite? icon;
    private Image? normal;
    private Image? highlighted;
    private Image? disabled;
    private Image? originalNormal;
    private Image? originalHighlighted;
    private Image? originalDisabled;
    private readonly List<Image> adoptedImages = new();

    public static AuraUiNativeButtonIconApplyResult Apply(ButtonManager? target, Sprite? sprite)
    {
        if (target == null)
        {
            return new AuraUiNativeButtonIconApplyResult(false, 0, false, "native ButtonManager is missing");
        }

        if (sprite == null)
        {
            return new AuraUiNativeButtonIconApplyResult(false, 0, target.useCustomContent, "button icon sprite is missing");
        }

        var owner = target.GetComponent<AuraUiNativeButtonIconOwner>()
                    ?? target.gameObject.AddComponent<AuraUiNativeButtonIconOwner>();
        return owner.Configure(target, sprite);
    }

    private AuraUiNativeButtonIconApplyResult Configure(ButtonManager target, Sprite sprite)
    {
        manager = target;
        icon = sprite;

        if (!Owns(target) && !OwnsAdoptedImages(target))
        {
            originalNormal = target.normalImage;
            originalHighlighted = target.highlightImage;
            originalDisabled = target.disabledImage;

            if (HasCompleteStateImages(target))
            {
                if (!TryCreateOwnedImage(originalNormal, "Normal", out normal, out var failureReason)
                    || !TryCreateOwnedImage(originalHighlighted, "Highlighted", out highlighted, out failureReason)
                    || !TryCreateOwnedImage(originalDisabled, "Disabled", out disabled, out failureReason))
                {
                    return new AuraUiNativeButtonIconApplyResult(false, CountOwnedStates(), target.useCustomContent, failureReason);
                }

                target.normalImage = normal;
                target.highlightImage = highlighted;
                target.disabledImage = disabled;
            }
            else if (!TryAdoptSerializedIconImages(target, out var failureReason))
            {
                return new AuraUiNativeButtonIconApplyResult(false, CountOwnedStates(), target.useCustomContent, failureReason);
            }
        }

        target.enableIcon = true;
        target.enableText = false;
        target.buttonIcon = sprite;
        target.UpdateUI();
        ApplyNow();
        return new AuraUiNativeButtonIconApplyResult(true, CountOwnedStates(), target.useCustomContent, "");
    }

    private bool Owns(ButtonManager target)
    {
        normal = Owned(target.normalImage);
        highlighted = Owned(target.highlightImage);
        disabled = Owned(target.disabledImage);
        return normal != null && highlighted != null && disabled != null;
    }

    private void OnEnable()
    {
        ApplyNow();
    }

    private void LateUpdate()
    {
        ApplyNow();
    }

    private void ApplyNow()
    {
        if (manager == null || icon == null)
        {
            return;
        }

        manager.enableIcon = true;
        manager.enableText = false;
        manager.buttonIcon = icon;
        ApplyImage(normal, icon);
        ApplyImage(highlighted, icon);
        ApplyImage(disabled, icon);
        foreach (var image in adoptedImages)
        {
            ApplyImage(image, icon);
        }
        DisableOriginal(originalNormal);
        DisableOriginal(originalHighlighted);
        DisableOriginal(originalDisabled);
        DisableText(manager.normalText);
        DisableText(manager.highlightedText);
        DisableText(manager.disabledText);
    }

    private static bool TryCreateOwnedImage(
        Image? source,
        string stateName,
        out Image? owned,
        out string failureReason)
    {
        owned = null;
        if (source == null || source.transform.parent == null)
        {
            failureReason = stateName + " template image is missing or detached";
            return false;
        }

        var sourceRect = source.rectTransform;
        var imageObject = new GameObject("AuraIcon-" + stateName, typeof(RectTransform));
        imageObject.transform.SetParent(source.transform.parent, false);
        imageObject.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
        CopyRect(sourceRect, imageObject.GetComponent<RectTransform>());

        owned = imageObject.AddComponent<Image>();
        owned.color = source.color;
        owned.material = source.material;
        owned.type = source.type;
        owned.preserveAspect = true;
        owned.raycastTarget = false;
        owned.maskable = source.maskable;
        imageObject.AddComponent<AuraUiOwnedNativeButtonIcon>();
        source.enabled = false;
        imageObject.SetActive(true);
        failureReason = "";
        return true;
    }

    private bool TryAdoptSerializedIconImages(ButtonManager target, out string failureReason)
    {
        adoptedImages.Clear();
        var serializedIcon = target.buttonIcon;
        var images = target.GetComponentsInChildren<Image>(true)
            .Where(image => image != null && image.GetComponent<AuraUiOwnedNativeButtonIcon>() == null)
            .ToArray();
        if (serializedIcon != null)
        {
            adoptedImages.AddRange(images.Where(image => ReferenceEquals(image.sprite, serializedIcon)));
        }

        if (adoptedImages.Count == 0)
        {
            var namedCandidates = images
                .Where(image => image.sprite != null
                                && (ContainsIconName(image.gameObject.name)
                                    || ContainsIconName(image.sprite.name)))
                .ToArray();
            if (namedCandidates.Length == 1)
            {
                adoptedImages.Add(namedCandidates[0]);
            }
        }

        if (adoptedImages.Count == 0)
        {
            var rootImage = target.GetComponent<Image>();
            if (rootImage != null && rootImage.sprite != null && rootImage.color.a > 0.01f)
            {
                adoptedImages.Add(rootImage);
            }
        }

        if (adoptedImages.Count == 0)
        {
            var visibleCandidates = images
                .Where(image => image.sprite != null && image.color.a > 0.01f)
                .ToArray();
            if (visibleCandidates.Length == 1)
            {
                adoptedImages.Add(visibleCandidates[0]);
            }
        }

        if (adoptedImages.Count == 0)
        {
            failureReason = "serialized button icon has no matching Image; candidates=" + DescribeImages(images);
            return false;
        }

        foreach (var image in adoptedImages)
        {
            image.gameObject.AddComponent<AuraUiOwnedNativeButtonIcon>();
        }

        failureReason = "";
        return true;
    }

    private bool OwnsAdoptedImages(ButtonManager target)
    {
        adoptedImages.Clear();
        adoptedImages.AddRange(target.GetComponentsInChildren<Image>(true)
            .Where(image => image != null && image.GetComponent<AuraUiOwnedNativeButtonIcon>() != null));
        return adoptedImages.Count > 0;
    }

    private static bool HasCompleteStateImages(ButtonManager target)
    {
        return target.normalImage != null
               && target.highlightImage != null
               && target.disabledImage != null;
    }

    private static bool ContainsIconName(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && (value ?? "").IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string DescribeImages(IEnumerable<Image> images)
    {
        var descriptions = images
            .Take(12)
            .Select(image => image.gameObject.name
                             + "(sprite="
                             + (image.sprite == null ? "null" : image.sprite.name)
                             + ", alpha="
                             + image.color.a.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                             + ")")
            .ToArray();
        return descriptions.Length == 0 ? "none" : string.Join("|", descriptions);
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

    private static Image? Owned(Image? image)
    {
        return image != null && image.GetComponent<AuraUiOwnedNativeButtonIcon>() != null ? image : null;
    }

    private static void ApplyImage(Image? image, Sprite sprite)
    {
        if (image == null)
        {
            return;
        }

        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.enabled = true;
        image.gameObject.SetActive(true);
        if (image.transform.parent != null)
        {
            image.transform.parent.gameObject.SetActive(true);
        }
    }

    private static void DisableOriginal(Image? image)
    {
        if (image != null && image.GetComponent<AuraUiOwnedNativeButtonIcon>() == null)
        {
            image.enabled = false;
        }
    }

    private static void DisableText(TMPro.TMP_Text? text)
    {
        if (text != null)
        {
            text.gameObject.SetActive(false);
        }
    }

    private int CountOwnedStates()
    {
        var count = 0;
        count += normal == null ? 0 : 1;
        count += highlighted == null ? 0 : 1;
        count += disabled == null ? 0 : 1;
        count += adoptedImages.Count;
        return count;
    }
}
