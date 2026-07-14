using AuraUi.Shared;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.Settings;

internal static class AuraToolsUiTheme
{
    public static AuraUiTheme Current { get; } = AuraUiStyleRegistry.RegisterDerived(
        AuraUiStyleIds.AuraToolsArcane,
        AuraUiStyleIds.WitchNative,
        theme =>
        {
            theme.Accent = new Color(0.85f, 0.70f, 0.42f, 1f);
            theme.Text = new Color(0.93f, 0.90f, 0.78f, 1f);
            theme.MutedText = new Color(0.66f, 0.62f, 0.50f, 1f);
            theme.Panel = new Color(0.07f, 0.06f, 0.11f, 0.96f);
            theme.Control = new Color(0.12f, 0.10f, 0.18f, 0.98f);
        });
}
