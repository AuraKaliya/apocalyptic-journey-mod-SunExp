using System;

namespace AuraToolsExp.Dll.Config;

internal static class AuraToolsVoiceNativeStateBindingMigration
{
    internal static bool Migrate(
        AuraToolsVoiceBindingSettings settings,
        string providerKind,
        string providerVocalState)
    {
        if (settings == null
            || !string.Equals(providerKind, "VocalState", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(providerVocalState, "Dying", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var changed = !string.Equals(settings.Signal, "VocalState", StringComparison.OrdinalIgnoreCase)
                      || !string.Equals(settings.Stage, "Observed", StringComparison.OrdinalIgnoreCase)
                      || !string.Equals(settings.ActionId, "Dying", StringComparison.OrdinalIgnoreCase);
        settings.Signal = "VocalState";
        settings.Stage = "Observed";
        settings.ActionId = "Dying";
        return changed;
    }
}
