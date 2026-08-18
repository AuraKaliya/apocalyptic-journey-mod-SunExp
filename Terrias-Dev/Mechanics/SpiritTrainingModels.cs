using System;
using System.Collections.Generic;

namespace Terrias.Dll.Mechanics;

[Serializable]
public sealed class SpiritUnlockNode
{
    public int Stage { get; set; }

    public int RequiredLevel { get; set; }

    public string AbilityKind { get; set; } = "Intent";

    public string AbilityId { get; set; } = "";

    public bool Unlocked { get; set; }

    public SpiritUnlockNode Clone()
    {
        return new SpiritUnlockNode
        {
            Stage = Stage,
            RequiredLevel = RequiredLevel,
            AbilityKind = AbilityKind,
            AbilityId = AbilityId,
            Unlocked = Unlocked
        };
    }
}

[Serializable]
public sealed class SpiritPassiveDefinition
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Description { get; set; } = "";

    public string Pool { get; set; } = "Species";

    public string EffectKind { get; set; } = "";

    public string HandlerId { get; set; } = "";

    public string IntentType { get; set; } = "Attack";

    public int NumericBonusPercent { get; set; }

    public int Threshold { get; set; }

    public int Value { get; set; }

    public int SecondaryValue { get; set; }

    public int MaximumStacks { get; set; }

    public string StateLabel { get; set; } = "";
}

[Serializable]
public sealed class SpiritSpeciesTrainingProfile
{
    public string SpeciesId { get; set; } = "";

    public string ProfileId { get; set; } = "";

    public string InitialPassiveId { get; set; } = "";

    public List<string> DefaultIntentIds { get; set; } = new();
}

[Serializable]
public sealed class SpiritTrainingRegistryDocument
{
    public int SchemaVersion { get; set; } = SpiritSystemContract.TrainingRegistrySchemaVersion;

    public List<CompanionIntentDefinition> CommonIntents { get; set; } = new();

    public List<SpiritPassiveDefinition> Passives { get; set; } = new();

    public List<SpiritSpeciesTrainingProfile> SpeciesProfiles { get; set; } = new();
}

public sealed class SpiritAbilityView
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Description { get; set; } = "";

    public string Kind { get; set; } = "Intent";

    public string Type { get; set; } = "";

    public int Cost { get; set; }

    public int Cooldown { get; set; }

    public bool IsNew { get; set; }
}

public sealed class SpiritTrainingViewSnapshot
{
    public int Speed { get; set; } = 100;

    public int LoadoutRevision { get; set; }

    public string LoadoutHash { get; set; } = "";

    public List<SpiritAbilityView> EquippedIntents { get; set; } = new();

    public SpiritAbilityView? EquippedPassive { get; set; }

    public List<SpiritAbilityView> LearnedIntents { get; set; } = new();

    public List<SpiritAbilityView> LearnedPassives { get; set; } = new();
}
