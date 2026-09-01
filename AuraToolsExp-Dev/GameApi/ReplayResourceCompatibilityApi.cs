using Witch.UI.Window;

namespace AuraToolsExp.Dll.GameApi;

internal static class ReplayResourceCompatibilityApi
{
    internal static string CurrentGameBuild =>
        (typeof(FightUI).Assembly.GetName().Version?.ToString() ?? "unknown")
        + "+" + typeof(FightUI).Assembly.ManifestModule.ModuleVersionId.ToString("N");
}
