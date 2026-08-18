using System;
using System.Reflection;
using AuraUi.Shared;
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
            if (AuraCombatCardPresentationBinding.GetOrCreate(cardTransform).TrySetCost(normalized))
            {
                return true;
            }

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

    public static bool TrySetDescription(Transform? cardTransform, string descriptionText)
    {
        if (cardTransform == null)
        {
            return false;
        }

        try
        {
            return AuraCombatCardPresentationBinding
                .GetOrCreate(cardTransform)
                .TrySetDescription(descriptionText ?? "");
        }
        catch
        {
            return false;
        }
    }

    public static void Rebind(Transform? cardTransform)
    {
        if (cardTransform == null)
        {
            return;
        }

        try
        {
            AuraCombatCardPresentationBinding.GetOrCreate(cardTransform).Rebind(cardTransform);
        }
        catch
        {
        }
    }
}
