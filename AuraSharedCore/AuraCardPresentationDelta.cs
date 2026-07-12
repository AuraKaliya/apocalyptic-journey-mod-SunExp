using System;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace AuraShared.Core;

public static class AuraCardPresentationDelta
{
    private static readonly MethodInfo? NativeSetCardCostVisual = typeof(ICard).GetMethod(
        "SetCardCostVisual",
        BindingFlags.Static | BindingFlags.NonPublic,
        null,
        new[] { typeof(Transform), typeof(string) },
        null);

    public static bool TrySetCost(Transform? cardTransform, string costText)
    {
        if (cardTransform == null)
        {
            return false;
        }

        var normalized = string.IsNullOrWhiteSpace(costText) ? "0" : costText.Trim();
        try
        {
            var costNode = cardTransform.Find("Front/cost/cost");
            var text = costNode == null ? null : costNode.GetComponent<TMP_Text>();
            if (text != null)
            {
                if (!string.Equals(text.text, normalized, StringComparison.Ordinal))
                {
                    text.text = normalized;
                }

                return true;
            }

            if (NativeSetCardCostVisual == null)
            {
                return false;
            }

            NativeSetCardCostVisual.Invoke(null, new object[] { cardTransform, normalized });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
