using System;
using UnityEngine;

namespace AuraUi.Shared;

[Flags]
public enum AuraUiCapabilities
{
    None = 0,
    GameFont = 1 << 0,
    TextMeshPro = 1 << 1,
    NineSliceSprites = 1 << 2,
    NativeModalHost = 1 << 3
}

public enum AuraUiTextRole
{
    Title,
    Section,
    Body,
    Hint,
    Button,
    Counter
}

public static class AuraUiStyleIds
{
    public const string Default = "Aura.Shared:default";
    public const string WitchNative = "Aura.Shared:witch.native";
}

public sealed class AuraUiTypography
{
    public float TitleSize { get; set; } = 26f;
    public float SectionSize { get; set; } = 21f;
    public float BodySize { get; set; } = 17f;
    public float HintSize { get; set; } = 14f;
    public float ButtonSize { get; set; } = 17f;
    public float CounterSize { get; set; } = 18f;
    public float MinimumSize { get; set; } = 11f;

    public float For(AuraUiTextRole role)
    {
        return role switch
        {
            AuraUiTextRole.Title => TitleSize,
            AuraUiTextRole.Section => SectionSize,
            AuraUiTextRole.Hint => HintSize,
            AuraUiTextRole.Button => ButtonSize,
            AuraUiTextRole.Counter => CounterSize,
            _ => BodySize
        };
    }

    public AuraUiTypography Clone()
    {
        return (AuraUiTypography)MemberwiseClone();
    }
}

public sealed class AuraUiMetrics
{
    public float SmallSpacing { get; set; } = 6f;
    public float Spacing { get; set; } = 10f;
    public float LargeSpacing { get; set; } = 16f;
    public float ButtonHeight { get; set; } = 44f;
    public float InputHeight { get; set; } = 44f;
    public float CornerPadding { get; set; } = 12f;

    public AuraUiMetrics Clone()
    {
        return (AuraUiMetrics)MemberwiseClone();
    }
}

public sealed class AuraUiTheme
{
    public AuraUiTheme(string key)
    {
        Key = NormalizeKey(key);
    }

    public string Key { get; }
    public string BaseStyleKey { get; set; } = "";
    public AuraUiCapabilities Capabilities { get; set; } = AuraUiCapabilities.TextMeshPro;
    public AuraUiTypography Typography { get; set; } = new();
    public AuraUiMetrics Metrics { get; set; } = new();
    public Color Background { get; set; } = new(0.035f, 0.03f, 0.05f, 0.96f);
    public Color Panel { get; set; } = new(0.08f, 0.07f, 0.11f, 0.96f);
    public Color Control { get; set; } = new(0.12f, 0.105f, 0.16f, 0.98f);
    public Color ControlHighlighted { get; set; } = new(0.19f, 0.16f, 0.24f, 1f);
    public Color Accent { get; set; } = new(0.86f, 0.70f, 0.42f, 1f);
    public Color Text { get; set; } = new(0.96f, 0.93f, 0.84f, 1f);
    public Color MutedText { get; set; } = new(0.70f, 0.66f, 0.57f, 1f);
    public Sprite? PanelSprite { get; set; }
    public Sprite? ControlSprite { get; set; }

    public AuraUiTheme CloneAs(string key)
    {
        return new AuraUiTheme(key)
        {
            BaseStyleKey = Key,
            Capabilities = Capabilities,
            Typography = Typography.Clone(),
            Metrics = Metrics.Clone(),
            Background = Background,
            Panel = Panel,
            Control = Control,
            ControlHighlighted = ControlHighlighted,
            Accent = Accent,
            Text = Text,
            MutedText = MutedText,
            PanelSprite = PanelSprite,
            ControlSprite = ControlSprite
        };
    }

    public static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Aura UI style key cannot be empty.", nameof(key));
        }

        var normalized = key.Trim();
        return normalized.Contains(":") ? normalized : "Aura.Shared:" + normalized;
    }
}
