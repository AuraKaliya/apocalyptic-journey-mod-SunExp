using UnityEngine;

namespace AuraToolsExp.Dll.Features.Settings;

internal static class ToolboxVisualSpec
{
    internal const float CategoryWidth = 168f;
    internal const float CategoryHeight = 48f;
    internal const float HeaderHeight = 60f;
    internal const float ModuleRowHeight = 78f;
    internal const float ModuleIconSize = 46f;
    internal const float IconButtonSize = 42f;
    internal const float CheckboxSize = 32f;
    internal const float SearchWidth = 252f;
    internal const float Spacing = 8f;

    internal const float TitleSize = 20f;
    internal const float StatusSize = 16f;
    internal const float DescriptionSize = 14f;
    internal const float CategorySize = 16f;
    internal const float CountSize = 14f;

    internal static readonly Color Workspace = new(0.031f, 0.016f, 0.227f, 1f);
    internal static readonly Color Row = new(0.063f, 0.078f, 0.227f, 1f);
    internal static readonly Color RowHighlighted = new(0.098f, 0.118f, 0.302f, 1f);
    internal static readonly Color Control = new(0.106f, 0.122f, 0.275f, 1f);
    internal static readonly Color Text = new(0.933f, 0.902f, 0.741f, 1f);
    internal static readonly Color MutedText = new(0.710f, 0.682f, 0.565f, 1f);
    internal static readonly Color Accent = new(0.761f, 0.643f, 0.384f, 1f);
    internal static readonly Color Success = new(0.396f, 0.827f, 0.639f, 1f);
    internal static readonly Color Warning = new(0.906f, 0.686f, 0.337f, 1f);
    internal static readonly Color Error = new(0.875f, 0.435f, 0.435f, 1f);
    internal static readonly Color Disabled = new(0.447f, 0.435f, 0.400f, 1f);
}
