using AuraToolsExp.Dll.Config;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsPerformanceSettings
{
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
}
