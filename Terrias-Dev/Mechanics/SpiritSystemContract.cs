namespace Terrias.Dll.Mechanics;

public static class SpiritSystemContract
{
    public const int CollectionVersion = 7;
    public const int InherentAbilityPlanVersion = 1;
    public const int TrainingPlanVersion = 2;
    public const int TrainingRegistrySchemaVersion = 2;
    public const int IntentRegistrySchemaVersion = 3;
    public const int GrowthRegistrySchemaVersion = 2;
    public const int CaptureRegistrySchemaVersion = 1;
    public const int MaximumVisibleStatuses = 24;

    public const string CompatibilityPassiveId = "spirit.passive.compatibility.adaptive-core";
    public const string CompatibilityAttackIntentId = "spirit.common.basic.probing-strike.intent";
    public const string CompatibilityDefenseIntentId = "spirit.common.basic.temporary-ward.intent";
}
