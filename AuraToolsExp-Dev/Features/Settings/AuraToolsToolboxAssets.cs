using System;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.Settings;

internal enum ToolboxCheckboxVisualState
{
    Off,
    On,
    HoverOff,
    HoverOn,
    Disabled
}

internal enum ToolboxIconButtonVisualState
{
    Normal,
    Hover,
    Pressed,
    Disabled
}

internal static class AuraToolsToolboxAssets
{
    private const string Root = "Mods/AuraToolsExp/ModResource/Images/UI/ToolboxV2/";
    private static Sprite? surface;
    private static Sprite? control;
    private static Sprite? categorySelected;
    private static Sprite[]? checkboxStates;
    private static Sprite[]? iconButtonStates;

    internal static Sprite? Surface => surface ??= LoadNineSlice(
        "toolbox-surface-9slice.png",
        new Vector4(16f, 16f, 16f, 16f));

    internal static Sprite? Control => control ??= LoadNineSlice(
        "toolbox-control-9slice.png",
        new Vector4(8f, 8f, 8f, 8f));

    internal static Sprite? CategorySelected => categorySelected ??= LoadNineSlice(
        "toolbox-category-selected-9slice.png",
        new Vector4(8f, 8f, 8f, 8f));

    internal static Sprite? Checkbox(ToolboxCheckboxVisualState state)
    {
        checkboxStates ??= LoadAtlas(
            "toolbox-checkbox-atlas.png",
            48,
            48,
            1,
            5,
            index => 4 - index,
            "ToolboxCheckbox");
        var index = Mathf.Clamp((int)state, 0, checkboxStates.Length - 1);
        return checkboxStates[index];
    }

    internal static Sprite? IconButton(ToolboxIconButtonVisualState state)
    {
        iconButtonStates ??= LoadAtlas(
            "toolbox-icon-button-atlas.png",
            48,
            48,
            4,
            1,
            index => index,
            "ToolboxIconButton");
        var index = Mathf.Clamp((int)state, 0, iconButtonStates.Length - 1);
        return iconButtonStates[index];
    }

    private static Sprite? LoadNineSlice(string file, Vector4 border)
    {
        var source = AuraToolsResourceCache.Load<Sprite>(Root + file, true);
        if (source == null || source.texture == null)
        {
            return null;
        }
        PrepareTexture(source.texture);
        return Sprite.Create(
            source.texture,
            source.rect,
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            border);
    }

    private static Sprite[] LoadAtlas(
        string file,
        int cellWidth,
        int cellHeight,
        int columns,
        int rows,
        Func<int, int> sourceIndex,
        string namePrefix)
    {
        var source = AuraToolsResourceCache.Load<Sprite>(Root + file, true);
        var count = columns * rows;
        var sprites = new Sprite[count];
        if (source == null || source.texture == null)
        {
            return sprites;
        }
        PrepareTexture(source.texture);
        for (var index = 0; index < count; index++)
        {
            var sourceCell = sourceIndex(index);
            var column = sourceCell % columns;
            var row = sourceCell / columns;
            var rect = new Rect(
                source.rect.x + column * cellWidth,
                source.rect.y + row * cellHeight,
                cellWidth,
                cellHeight);
            sprites[index] = Sprite.Create(
                source.texture,
                rect,
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            sprites[index].name = namePrefix + "-" + index;
        }
        return sprites;
    }

    private static void PrepareTexture(Texture texture)
    {
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
    }
}
