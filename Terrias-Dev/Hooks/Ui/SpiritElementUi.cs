using Terrias.Dll.GameApi;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui;

internal static class SpiritElementUi
{
    public static (GameObject Root, Image Icon, Text Label) CreateBadge(
        Transform parent,
        string name,
        float width,
        float height,
        bool ignoreLayout = false)
    {
        return SpiritElementUiApi.CreateBadge(parent, name, width, height, ignoreLayout);
    }

    public static (GameObject Root, Image Icon) CreateIcon(
        Transform parent,
        string name,
        SpiritElementDisplaySurface surface)
    {
        return SpiritElementUiApi.CreateIcon(parent, name, SpiritElementDisplayPolicy.IconSize(surface));
    }

    public static void Bind(Image? icon, Text? label, string elementId)
    {
        var normalized = SpiritElementService.NormalizeId(elementId);
        SpiritElementUiApi.Bind(
            icon,
            label,
            normalized,
            SpiritElementService.DisplayName(normalized),
            SpiritElementService.IconPath(normalized));
    }

    public static void BindIcon(Image? icon, string elementId)
    {
        var normalized = SpiritElementService.NormalizeId(elementId);
        SpiritElementUiApi.BindIcon(icon, SpiritElementService.IconPath(normalized));
    }

    public static Color Tint(string elementId)
    {
        return SpiritElementUiApi.Tint(SpiritElementService.NormalizeId(elementId));
    }
}
