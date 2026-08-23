using AuraUi.Shared;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.Settings;

internal static class AuraToolsUiTheme
{
    private const string StyleId = "AuraToolsExp:arcane";

    public static AuraUiTheme Current { get; } = AuraUiStyleRegistry.RegisterDerived(
        StyleId,
        AuraUiStyleIds.Default,
        theme =>
        {
            theme.Background = ToolboxVisualSpec.Workspace;
            theme.Panel = ToolboxVisualSpec.Row;
            theme.Control = ToolboxVisualSpec.Control;
            theme.ControlHighlighted = ToolboxVisualSpec.RowHighlighted;
            theme.Accent = ToolboxVisualSpec.Accent;
            theme.Text = ToolboxVisualSpec.Text;
            theme.MutedText = ToolboxVisualSpec.MutedText;
            theme.Typography.TitleSize = 22f;
            theme.Typography.SectionSize = 18f;
            theme.Typography.BodySize = 16f;
            theme.Typography.HintSize = 14f;
            theme.Typography.ButtonSize = 15f;
            theme.Typography.MinimumSize = 11f;
            theme.Metrics.SmallSpacing = 6f;
            theme.Metrics.Spacing = 10f;
            theme.Metrics.LargeSpacing = 14f;
            theme.Metrics.ButtonHeight = 44f;
            theme.Metrics.InputHeight = 44f;
            theme.Metrics.CornerPadding = 12f;
        });
}
