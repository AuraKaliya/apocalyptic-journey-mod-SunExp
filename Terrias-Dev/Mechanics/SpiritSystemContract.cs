namespace Terrias.Dll.Mechanics;

public static class SpiritSystemContract
{
    public const int CollectionVersion = 9;
    public const int InitialRosterGrantVersion = 1;
    public const int InitialRosterProfileCount = 58;
    public const string InitialRosterConfigurationKey = "GrantAllSpiritsOnFirstLoad";
    public const string InitialRosterCaptureOrigin = "initial-full-roster-v1";
    public const int InherentAbilityPlanVersion = 1;
    public const int TrainingPlanVersion = 2;
    public const int TrainingRegistrySchemaVersion = 2;
    public const int IntentRegistrySchemaVersion = 3;
    public const int GrowthRegistrySchemaVersion = 3;
    public const int CaptureRegistrySchemaVersion = 1;
    public const int MaximumVisibleStatuses = 24;

    public const string CompatibilityPassiveId = "spirit.passive.compatibility.adaptive-core";
    public const string CompatibilityAttackIntentId = "spirit.common.basic.probing-strike.intent";
    public const string CompatibilityDefenseIntentId = "spirit.common.basic.temporary-ward.intent";
}
