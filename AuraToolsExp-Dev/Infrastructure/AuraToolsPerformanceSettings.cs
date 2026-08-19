using AuraToolsExp.Dll.Config;
using AuraShared.Core;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsPerformanceSettings
{
    private const string SharedDiagnosticsOwnerId = "AuraShared";
    private const string SharedDiagnosticsFeatureId = "Diagnostics.Performance";

    public static bool DiagnosticsEnabled
    {
        get
        {
            try
            {
                return AuraToolsConfigService.Logging.PerformanceDiagnostics;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void PublishSharedOverride()
    {
        AuraFeatureSwitchRuntime.RegisterFeature(
            SharedDiagnosticsOwnerId,
            SharedDiagnosticsFeatureId,
            defaultEnabled: false,
            "AuraShared diagnostics default");
        AuraFeatureSwitchRuntime.SetLocalOverride(
            AuraToolsIds.ModId,
            SharedDiagnosticsOwnerId,
            SharedDiagnosticsFeatureId,
            DiagnosticsEnabled);
    }
}
