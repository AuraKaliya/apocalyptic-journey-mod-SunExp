using AuraUi.Shared;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.Settings;

internal static class AuraToolsUiTheme
{
    private const string StyleId = "AuraToolsExp:arcane";

    public static AuraUiTheme Current { get; } = AuraUiStyleRegistry.RegisterDerived(
        StyleId,
        AuraUiStyleIds.WitchNative,
        theme =>
        {
            theme.Background = new Color(0.043f, 0.039f, 0.063f, 1f);
            theme.Panel = new Color(0.067f, 0.063f, 0.094f, 1f);
            theme.Control = new Color(0.090f, 0.086f, 0.118f, 1f);
            theme.ControlHighlighted = new Color(0.145f, 0.137f, 0.180f, 1f);
            theme.Accent = new Color(0.835f, 0.702f, 0.420f, 1f);
            theme.Text = new Color(0.941f, 0.922f, 0.867f, 1f);
            theme.MutedText = new Color(0.710f, 0.682f, 0.620f, 1f);
            theme.Typography.TitleSize = 22f;
            theme.Typography.SectionSize = 18f;
            theme.Typography.BodySize = 16f;
            theme.Typography.HintSize = 14f;
            theme.Typography.ButtonSize = 15f;
            theme.Typography.MinimumSize = 11f;
            theme.Metrics.SmallSpacing = 6f;
            theme.Metrics.Spacing = 10f;
            theme.Metrics.LargeSpacing = 14f;
            theme.Metrics.ButtonHeight = 42f;
            theme.Metrics.InputHeight = 42f;
            theme.Metrics.CornerPadding = 12f;
        });
}
