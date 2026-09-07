using System;
using System.Collections.Generic;
using AuraShared.Core;
using TMPro;
using UnityEngine;
using Witch.UI.Component;

namespace AuraToolsExp.Dll.GameApi;

/// <summary>
/// Narrow, script-free adapter over the native card view functions. It builds a
/// read-only presentation DataConfig, applies native frame/art/cost semantics,
/// then routes the frozen replay visual snapshot through the shared card
/// presentation lifecycle. No card script or CardItem.Init path is executed.
/// </summary>
internal static class ReplayNativeCardPresentationApi
{
    private static readonly string[] BurnRendererPaths =
    {
        "Front/icon", "Back/background", "Front/background", "Front/FrontBack", "Front/Icons/Ench/Item"
    };

    internal static DataConfig Apply(
        Transform root,
        string instanceId,
        string stableCardId,
        string name,
        string description,
        string iconPath,
        string rarity,
        string tag,
        int displayedCost,
        string themeId,
        string skinId,
        string effectId,
        string effectParametersJson,
        string enchantIconResourcePath)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Id"] = stableCardId ?? "",
            ["Name"] = name ?? "",
            ["Description"] = description ?? "",
            ["Icon"] = iconPath ?? "",
            ["Rarity"] = string.IsNullOrWhiteSpace(rarity) ? "1" : rarity,
            ["Tag"] = tag ?? "",
            ["Expend"] = Math.Max(0, displayedCost).ToString(),
            ["BaseScript"] = "CardItem"
        };
        var vars = new Dictionary<string, string>(data, StringComparer.Ordinal)
        {
            ["InstanceID"] = "aura-replay-view:" + Guid.NewGuid().ToString("N"),
            ["AuraReplay.CardVisual.ThemeId"] = themeId ?? "",
            ["AuraReplay.CardVisual.SkinId"] = skinId ?? "",
            ["AuraReplay.CardVisual.EffectId"] = effectId ?? "",
            ["AuraReplay.CardVisual.EffectParameters"] = string.IsNullOrWhiteSpace(effectParametersJson)
                ? "{}"
                : effectParametersJson
        };
        var config = new DataConfig
        {
            data = data,
            Vars = vars
        };

        ICard.SetCardStyle(root, config);
        ICard.SetPureMsg(root, config);
        SetText(root.Find("Front/字体/nameTxt"), name);
        SetText(root.Find("Front/字体/msgTxt"), description);
        var cost = root.Find("Front/cost/cost")?.GetComponent<TMP_Text>();
        cost?.SetCardCostText(Math.Max(0, displayedCost).ToString());
        ApplyEnchant(root, enchantIconResourcePath);
        AuraCardPresentationRuntime.RequestApply(new AuraCardPresentationContext
        {
            Root = root,
            Config = config,
            Source = "MatchReplay.NativeCardPresentation",
            Surface = AuraCardPresentationSurface.CardStyle
        });
        return config;
    }

    private static void ApplyEnchant(Transform root, string resourcePath)
    {
        var enchantRoot = root.Find("Front/Icons/Ench");
        var item = root.Find("Front/Icons/Ench/Item");
        if (enchantRoot == null || item == null) return;
        var visible = !string.IsNullOrWhiteSpace(resourcePath);
        enchantRoot.gameObject.SetActive(visible);
        if (!visible) return;
        var renderer = item.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            var texture = ResourceLoader.Load<Texture>(resourcePath);
            if (texture == null)
                throw new InvalidOperationException("Replay enchant texture is missing: " + resourcePath);
            renderer.material.mainTexture = texture;
            return;
        }
        var image = item.GetComponent<UnityEngine.UI.Image>();
        if (image != null)
        {
            image.sprite = ResourceLoader.Load<Sprite>(resourcePath)
                           ?? throw new InvalidOperationException("Replay enchant sprite is missing: " + resourcePath);
        }
    }

    internal static void PrepareBurn(Transform root)
    {
        if (root == null) return;
        AuraCardPresentationRuntime.PrepareNativeExit(new AuraCardPresentationContext
        {
            Root = root, Source = "Replay.NativeBurn", Surface = AuraCardPresentationSurface.CardStyle
        });
        var template = ResourceLoader.Load<Material>("Material/CardBurn")
                       ?? throw new InvalidOperationException("Native replay CardBurn material is missing.");
        var owner = root.GetComponent<ReplayNativeCardOwnedMaterials>()
                    ?? root.gameObject.AddComponent<ReplayNativeCardOwnedMaterials>();
        foreach (var path in BurnRendererPaths)
        {
            var renderer = root.Find(path)?.GetComponent<MeshRenderer>();
            if (renderer == null) continue;
            var texture = renderer.sharedMaterial?.mainTexture;
            var material = new Material(template) { mainTexture = texture };
            material.SetFloat("_Fade", 50f);
            renderer.sharedMaterial = material;
            owner.Track(material);
        }
    }

    internal static void SetBurnFade(Transform root, float value)
    {
        if (root == null) return;
        foreach (var path in BurnRendererPaths)
        {
            var material = root.Find(path)?.GetComponent<MeshRenderer>()?.sharedMaterial;
            if (material?.HasProperty("_Fade") == true) material.SetFloat("_Fade", value);
        }
    }

    private static void SetText(Transform? node, string? value)
    {
        var text = node?.GetComponent<TMP_Text>();
        if (text != null) text.text = value ?? "";
    }
}

internal sealed class ReplayNativeCardOwnedMaterials : MonoBehaviour
{
    private readonly List<Material> values = new();

    internal void Track(Material value)
    {
        if (value != null) values.Add(value);
    }

    private void OnDestroy()
    {
        foreach (var value in values)
            if (value != null) UnityEngine.Object.Destroy(value);
        values.Clear();
    }
}
