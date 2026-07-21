using System;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;

namespace SunExp.Dll.Hooks.Ui;

internal readonly struct EndlessAbyssFramedTextCardImages
{
    public EndlessAbyssFramedTextCardImages(Image buttonTarget, Image tintTarget)
    {
        ButtonTarget = buttonTarget;
        TintTarget = tintTarget;
    }

    public Image ButtonTarget { get; }

    public Image TintTarget { get; }
}

internal static class EndlessAbyssFramedTextCard
{
    public static EndlessAbyssFramedTextCardImages Create(
        GameObject frame,
        string logScope,
        Color frameTint,
        string title,
        string body,
        Color titleColor,
        Color bodyColor)
    {
        var image = SunExpUiBuilder.ApplyLabelImage(frame, SunExpUiSprites.Label(logScope), frameTint, true);
        var tint = image;

        var content = SunExpUiBuilder.CreateRect(
            "Content",
            frame.transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        content.offsetMin = new Vector2(22f, 14f);
        content.offsetMax = new Vector2(-22f, -14f);

        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        SunExpUiComponents.AddTextBlock(content.transform, title, 18, TextAnchor.MiddleLeft, titleColor, 30f);
        SunExpUiComponents.AddTextBlock(content.transform, body, 14, TextAnchor.MiddleLeft, bodyColor, 34f);

        return new EndlessAbyssFramedTextCardImages(image, tint);
    }
}
