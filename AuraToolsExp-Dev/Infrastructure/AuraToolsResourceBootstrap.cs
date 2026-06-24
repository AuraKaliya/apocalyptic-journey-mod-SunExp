using AuraShared.Core;
using Witch.Mod;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsResourceBootstrap
{
    public static AuraSharedBootstrapResult? LastResult { get; private set; }

    public static void Initialize(ModConfig modConfig)
    {
        LastResult = AuraSharedResourceBootstrapper.Bootstrap(
            modConfig,
            AuraToolsIds.ModId);

        if (LastResult.Success)
        {
            AuraToolsLog.Info("[Resources] bundled resources ready: " + LastResult.Summary + ".");
            return;
        }

        AuraToolsLog.Warn("[Resources] bundled resource bootstrap completed with issues: "
                          + LastResult.Summary + ".");
        foreach (var response in LastResult.Responses)
        {
            if (!response.Success)
            {
                AuraToolsLog.Warn("[Resources] " + response.Status + ": " + response.Message);
            }
        }
    }
}
