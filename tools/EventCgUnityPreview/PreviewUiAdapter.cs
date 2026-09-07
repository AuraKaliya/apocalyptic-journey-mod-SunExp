using UnityEngine;
using UnityEngine.UI;

namespace AuraUi.Shared
{
    // Only the game's font lookup is adapted. Layout and drawing use production CG sources.
    internal static class AuraUiComponents
    {
        private static Font font;
        internal static Text ConfigureText(GameObject obj, string value, int size, int minimum,
            TextAnchor alignment, Color color, bool resizeForBestFit)
        {
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 32);
            var text = obj.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.resizeTextForBestFit = resizeForBestFit;
            text.resizeTextMinSize = minimum;
            text.resizeTextMaxSize = size;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }
    }
}
