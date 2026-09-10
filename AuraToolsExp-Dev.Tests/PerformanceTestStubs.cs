namespace AuraToolsExp.Dll.Config;

public static class AuraToolsConfigService
{
    public static DamageNetworkTestSettings MatchExperience { get; set; } = new();
    public static AuraToolsLoggingSettings Logging { get; set; } = new();
}
public sealed class DamageNetworkTestSettings { public DamageMeterSettings DamageMeter { get; set; } = new(); }
