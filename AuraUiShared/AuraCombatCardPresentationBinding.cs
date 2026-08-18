using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Witch.UI;

namespace AuraUi.Shared;

/// <summary>
/// Caches the native card presentation nodes after the first complete bind so
/// subsequent field deltas do not need to rediscover or rebuild the card UI.
/// </summary>
public sealed class AuraCombatCardPresentationBinding : MonoBehaviour
{
    private TMP_Text? costText;
    private TMP_Text? descriptionText;
    private KeywordDisplay? keywordDisplay;

    public static AuraCombatCardPresentationBinding GetOrCreate(Transform root)
    {
        var binding = root.GetComponent<AuraCombatCardPresentationBinding>()
                      ?? root.gameObject.AddComponent<AuraCombatCardPresentationBinding>();
        binding.ResolveMissingNodes(root);
        return binding;
    }

    public void Rebind(Transform root)
    {
        costText = null;
        descriptionText = null;
        keywordDisplay = null;
        ResolveMissingNodes(root);
    }

    public bool TrySetCost(string value)
    {
        if (costText == null)
        {
            ResolveMissingNodes(transform);
        }

        if (costText == null)
        {
            return false;
        }

        if (!string.Equals(costText.text, value, StringComparison.Ordinal))
        {
            costText.text = value;
        }

        return true;
    }

    public bool TrySetDescription(string value)
    {
        if (descriptionText == null)
        {
            ResolveMissingNodes(transform);
        }

        if (descriptionText == null)
        {
            return false;
        }

        var rendered = keywordDisplay?.keyWords == null
            ? value
            : LocalizeEx.Highlight(value, keywordDisplay.keyWords);
        var previous = descriptionText.text ?? "";
        if (string.Equals(previous, rendered, StringComparison.Ordinal))
        {
            return true;
        }

        descriptionText.text = rendered;
        RefreshKeywordTooltip(previous, rendered);
        return true;
    }

    private void RefreshKeywordTooltip(string previous, string value)
    {
        if (keywordDisplay == null)
        {
            ResolveMissingNodes(transform);
        }

        if (keywordDisplay == null)
        {
            return;
        }

        var tooltipText = keywordDisplay.text ?? "";
        if (previous.Length > 0 && tooltipText.Contains(previous))
        {
            tooltipText = tooltipText.Replace(previous, value);
        }
        else if (tooltipText.Length == 0 || string.Equals(tooltipText, previous, StringComparison.Ordinal))
        {
            tooltipText = value;
        }

        keywordDisplay.SetText(
            keywordDisplay.title,
            tooltipText,
            keywordDisplay.keyWords ?? new List<string>(),
            keywordDisplay.msg,
            keywordDisplay.icon,
            keywordDisplay.type);
    }

    private void ResolveMissingNodes(Transform root)
    {
        if (costText == null)
        {
            costText = root.Find("Front/cost/cost")?.GetComponent<TMP_Text>();
        }

        var texts = root.GetComponentsInChildren<TMP_Text>(true);
        if (descriptionText == null)
        {
            descriptionText = root.Find("Front/字体/msgTxt")?.GetComponent<TMP_Text>()
                              ?? FindDescription(texts);
        }

        if (keywordDisplay == null)
        {
            var displays = root.GetComponentsInChildren<KeywordDisplay>(true);
            foreach (var display in displays)
            {
                if (display == null)
                {
                    continue;
                }

                if (descriptionText != null
                    && !string.IsNullOrEmpty(descriptionText.text)
                    && display.text != null
                    && display.text.Contains(descriptionText.text ?? ""))
                {
                    keywordDisplay = display;
                    break;
                }

                keywordDisplay ??= display;
            }
        }
    }

    private static TMP_Text? FindDescription(IEnumerable<TMP_Text> texts)
    {
        TMP_Text? fallback = null;
        foreach (var text in texts)
        {
            if (text == null)
            {
                continue;
            }

            var nodeName = (text.gameObject.name ?? "").Trim();
            var parentName = (text.transform.parent?.gameObject.name ?? "").Trim();
            if (nodeName.IndexOf("description", StringComparison.OrdinalIgnoreCase) >= 0
                || nodeName.IndexOf("describe", StringComparison.OrdinalIgnoreCase) >= 0
                || parentName.IndexOf("description", StringComparison.OrdinalIgnoreCase) >= 0
                || parentName.IndexOf("describe", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return text;
            }

            if (fallback == null
                && text.text != null
                && text.text.Length >= 8
                && nodeName.IndexOf("name", StringComparison.OrdinalIgnoreCase) < 0)
            {
                fallback = text;
            }
        }

        return fallback;
    }
}
