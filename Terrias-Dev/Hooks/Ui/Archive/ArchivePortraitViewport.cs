using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui.Archive;

public sealed class ArchivePortraitViewport : MonoBehaviour
{
    private Image? portrait;
    private RectTransform? portraitRect;

    public static ArchivePortraitViewport Create(Transform parent)
    {
        var root = ArchiveUiFactory.CreateFromRect(
            "PortraitViewport",
            parent,
            ArchiveLayoutMetrics.PortraitViewport);
        root.gameObject.AddComponent<RectMask2D>();
        var portraitRect = TerriasUiBuilder.CreateRect(
            "Portrait",
            root,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            Vector2.zero);
        var portrait = portraitRect.gameObject.AddComponent<Image>();
        portrait.color = Color.white;
        portrait.preserveAspect = false;
        portrait.raycastTarget = false;
        var view = root.gameObject.AddComponent<ArchivePortraitViewport>();
        view.portrait = portrait;
        view.portraitRect = portraitRect;
        return view;
    }

    public void Bind(WitchArchiveDisplayEntry entry)
    {
        var sprite = TerriasResourceCache.Load<Sprite>(
            entry.PortraitPath,
            true,
            TerriasIds.WitchArchiveResourceCategory);
        if (portrait == null || portraitRect == null)
        {
            return;
        }

        portrait.sprite = sprite;
        portrait.gameObject.SetActive(sprite != null);
        portraitRect.localScale = Vector3.one;
        portraitRect.localRotation = Quaternion.identity;
        if (sprite != null)
        {
            portrait.SetNativeSize();
            portraitRect.localScale = Vector3.one;
            // Positive Y trims transparent padding above the visible artwork while
            // keeping the native image top-aligned to the fixed portrait viewport.
            portraitRect.anchoredPosition = new Vector2(entry.PortraitOffsetX, entry.PortraitOffsetY);
        }
    }
}
