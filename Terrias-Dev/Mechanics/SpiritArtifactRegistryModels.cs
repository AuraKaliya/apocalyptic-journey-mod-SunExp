using System.Collections.Generic;
using Newtonsoft.Json;

namespace Terrias.Dll.Mechanics;

public sealed class SpiritArtifactRegistryDocument
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "Terrias";

    [JsonProperty("inventoryCapacity")]
    public int InventoryCapacity { get; set; } = 1000;

    [JsonProperty("draw")]
    public SpiritArtifactDrawRules Draw { get; set; } = new();

    [JsonProperty("enhancement")]
    public SpiritArtifactEnhancementRules Enhancement { get; set; } = new();

    [JsonProperty("subStatWeights")]
    public List<SpiritArtifactWeightedStat> SubStatWeights { get; set; } = new();

    [JsonProperty("statRanges")]
    public List<SpiritArtifactStatRangeProfile> StatRanges { get; set; } = new();

    [JsonProperty("pools")]
    public List<SpiritArtifactPoolDefinition> Pools { get; set; } = new();

    [JsonProperty("sets")]
    public List<SpiritArtifactSetDefinition> Sets { get; set; } = new();
}

public sealed class SpiritArtifactDrawRules
{
    [JsonProperty("count")]
    public int Count { get; set; } = 10;

    [JsonProperty("truthCost")]
    public int TruthCost { get; set; } = 160;

    [JsonProperty("rarityWeights")]
    public Dictionary<string, int> RarityWeights { get; set; } = new();

    [JsonProperty("minimumTwoStarPerBatch")]
    public int MinimumTwoStarPerBatch { get; set; } = 1;

    [JsonProperty("threeStarHardPity")]
    public int ThreeStarHardPity { get; set; } = 30;

    [JsonProperty("targetSetWeightPercent")]
    public int TargetSetWeightPercent { get; set; } = 50;

    [JsonProperty("guaranteeTargetAfterOffTargetThreeStar")]
    public bool GuaranteeTargetAfterOffTargetThreeStar { get; set; } = true;
}

public sealed class SpiritArtifactEnhancementRules
{
    [JsonProperty("maximumLevel")]
    public int MaximumLevel { get; set; } = 5;

    [JsonProperty("upgradeCosts")]
    public List<int> UpgradeCosts { get; set; } = new();

    [JsonProperty("dismantleBaseEssence")]
    public Dictionary<string, int> DismantleBaseEssence { get; set; } = new();

    [JsonProperty("investedEssenceRefundPercent")]
    public int InvestedEssenceRefundPercent { get; set; } = 70;
}

public sealed class SpiritArtifactWeightedStat
{
    [JsonProperty("statId")]
    public string StatId { get; set; } = "";

    [JsonProperty("weight")]
    public int Weight { get; set; }
}

public sealed class SpiritArtifactIntegerRange
{
    [JsonProperty("minimum")]
    public int Minimum { get; set; }

    [JsonProperty("maximum")]
    public int Maximum { get; set; }

    public SpiritArtifactIntegerRange Clone()
    {
        return new SpiritArtifactIntegerRange { Minimum = Minimum, Maximum = Maximum };
    }
}

public sealed class SpiritArtifactStatRangeProfile
{
    [JsonProperty("statId")]
    public string StatId { get; set; } = "";

    [JsonProperty("main")]
    public Dictionary<string, SpiritArtifactIntegerRange> Main { get; set; } = new();

    [JsonProperty("sub")]
    public Dictionary<string, SpiritArtifactIntegerRange> Sub { get; set; } = new();
}

public sealed class SpiritArtifactPoolDefinition
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("name")]
    public TerriasLocalizedText Name { get; set; } = new();

    [JsonProperty("setIds")]
    public List<string> SetIds { get; set; } = new();
}

public sealed class SpiritArtifactPieceDefinition
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("slotId")]
    public string SlotId { get; set; } = "";

    [JsonProperty("name")]
    public TerriasLocalizedText Name { get; set; } = new();

    [JsonProperty("iconPath")]
    public string IconPath { get; set; } = "";
}

public sealed class SpiritArtifactEffectDefinition
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("handlerId")]
    public string HandlerId { get; set; } = "";

    [JsonProperty("amount")]
    public int Amount { get; set; }

    [JsonProperty("secondaryAmount")]
    public int SecondaryAmount { get; set; }

    [JsonProperty("maximum")]
    public int Maximum { get; set; }
}

public sealed class SpiritArtifactSetBonusDefinition
{
    [JsonProperty("requiredPieces")]
    public int RequiredPieces { get; set; }

    [JsonProperty("description")]
    public TerriasLocalizedText Description { get; set; } = new();

    [JsonProperty("effects")]
    public List<SpiritArtifactEffectDefinition> Effects { get; set; } = new();
}

public sealed class SpiritArtifactSetDefinition
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("name")]
    public TerriasLocalizedText Name { get; set; } = new();

    [JsonProperty("representativePieceId")]
    public string RepresentativePieceId { get; set; } = "";

    [JsonProperty("pieces")]
    public List<SpiritArtifactPieceDefinition> Pieces { get; set; } = new();

    [JsonProperty("bonuses")]
    public List<SpiritArtifactSetBonusDefinition> Bonuses { get; set; } = new();
}
