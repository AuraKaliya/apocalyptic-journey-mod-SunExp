using System;
using System.Collections.Generic;

namespace AuraUi.Shared;

public static class AuraUiStyleRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, AuraUiTheme> Themes = new(StringComparer.OrdinalIgnoreCase);

    static AuraUiStyleRegistry()
    {
        var defaultTheme = new AuraUiTheme(AuraUiStyleIds.Default);
        Themes[defaultTheme.Key] = defaultTheme;

        var native = defaultTheme.CloneAs(AuraUiStyleIds.WitchNative);
        native.Capabilities |= AuraUiCapabilities.GameFont | AuraUiCapabilities.NativeModalHost;
        native.Typography.TitleSize = 28f;
        native.Typography.BodySize = 18f;
        native.Typography.ButtonSize = 18f;
        native.Metrics.ButtonHeight = 48f;
        Themes[native.Key] = native;
    }

    public static AuraUiTheme Register(AuraUiTheme theme, bool replace = true)
    {
        if (theme == null)
        {
            throw new ArgumentNullException(nameof(theme));
        }

        lock (Sync)
        {
            if (!replace && Themes.ContainsKey(theme.Key))
            {
                return Themes[theme.Key];
            }

            Themes[theme.Key] = theme;
            return theme;
        }
    }

    public static AuraUiTheme RegisterDerived(string key, string baseStyleKey, Action<AuraUiTheme>? configure = null)
    {
        var theme = Resolve(baseStyleKey).CloneAs(key);
        configure?.Invoke(theme);
        return Register(theme);
    }

    public static AuraUiTheme Resolve(string? key)
    {
        var normalized = string.IsNullOrWhiteSpace(key) ? AuraUiStyleIds.Default : AuraUiTheme.NormalizeKey(key!);
        lock (Sync)
        {
            return Themes.TryGetValue(normalized, out var theme)
                ? theme
                : Themes[AuraUiStyleIds.Default];
        }
    }
}
