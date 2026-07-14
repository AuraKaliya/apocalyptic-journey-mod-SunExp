using AuraUi.Shared;
using UnityEngine;

namespace SunExp.Dll.Hooks.Ui;

public static class SunExpUiTheme
{
    public static AuraUiTheme Current { get; } = AuraUiStyleRegistry.RegisterDerived(
        AuraUiStyleIds.SunExpSolar,
        AuraUiStyleIds.WitchNative,
        theme =>
        {
            theme.Accent = new Color(1f, 0.72f, 0.28f, 1f);
            theme.Text = new Color(1f, 0.96f, 0.86f, 1f);
            theme.MutedText = new Color(0.95f, 0.78f, 0.42f, 1f);
            theme.Panel = new Color(0.08f, 0.07f, 0.05f, 0.96f);
        });
}
