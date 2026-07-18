using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal static class DamageMeterUiAssets
{
    private const string ButtonSpritePath = "Mods/AuraToolsExp/ModResource/Images/UI/button-九宫格.png";
    private const string PanelSpritePath = "Mods/AuraToolsExp/ModResource/Images/UI/background-九宫格.png";
    private static Sprite? buttonSprite;
    private static Sprite? panelSprite;
    private static bool buttonSpriteLoadAttempted;
    private static bool panelSpriteLoadAttempted;

    internal static Sprite? GetButtonSprite()
    {
        if (buttonSprite != null)
        {
            return buttonSprite;
        }

        if (buttonSpriteLoadAttempted)
        {
            return null;
        }

        buttonSpriteLoadAttempted = true;
        buttonSprite = TryLoadNineSliceSprite(
            ButtonSpritePath,
            new Vector4(14f, 14f, 14f, 14f),
            new Rect(17f, 16f, 135f, 49f));
        return buttonSprite;
    }

    internal static Sprite? GetPanelSprite()
    {
        if (panelSprite != null)
        {
            return panelSprite;
        }

        if (panelSpriteLoadAttempted)
        {
            return null;
        }

        panelSpriteLoadAttempted = true;
        panelSprite = TryLoadNineSliceSprite(PanelSpritePath, new Vector4(4f, 4f, 4f, 4f), null);
        return panelSprite;
    }

    private static Sprite? TryLoadNineSliceSprite(string path, Vector4 fallbackBorder, Rect? sourceCrop)
    {
        try
        {
            var source = AuraToolsResourceCache.Load<Sprite>(path, true);
            if (source == null || source.texture == null)
            {
                return null;
            }

            var texture = source.texture;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            var rect = sourceCrop.HasValue ? ResolveSpriteRect(source, sourceCrop.Value) : source.rect;
            var border = source.border.sqrMagnitude > 0.01f ? source.border : fallbackBorder;
            return Sprite.Create(
                texture,
                rect,
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                border);
        }
        catch
        {
            return null;
        }
    }

    private static Rect ResolveSpriteRect(Sprite source, Rect crop)
    {
        var x = Mathf.Clamp(source.rect.x + crop.x, source.rect.x, source.rect.xMax);
        var y = Mathf.Clamp(source.rect.y + crop.y, source.rect.y, source.rect.yMax);
        var width = Mathf.Clamp(crop.width, 1f, source.rect.xMax - x);
        var height = Mathf.Clamp(crop.height, 1f, source.rect.yMax - y);
        return new Rect(x, y, width, height);
    }
}
