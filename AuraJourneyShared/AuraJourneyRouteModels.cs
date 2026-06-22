using System.Collections.Generic;
using Newtonsoft.Json;

namespace AuraJourney.Shared;

public static class AuraJourneyNodeKinds
{
    public const string Setup = "Setup";
    public const string Event = "Event";
    public const string Fight = "Fight";
    public const string Boss = "Boss";
    public const string Settlement = "Settlement";
    public const string RouteGraph = "RouteGraph";
}

public static class AuraJourneyDicePolicies
{
    public const string TreeDice = "TreeDice";
    public const string Default = "Default";
    public const string None = "None";
}

public static class AuraJourneyReplacementPolicies
{
    public const string Replace = "Replace";
    public const string FillEmpty = "FillEmpty";
    public const string PreserveBreak = "PreserveBreak";
    public const string KeepNative = "KeepNative";
}

public sealed class AuraJourneyRouteGraph
{
    [JsonProperty("graphId")]
    public string GraphId { get; set; } = "";

    [JsonProperty("layers")]
    public List<AuraJourneyRouteLayer> Layers { get; set; } = new();
}

public sealed class AuraJourneyRouteLayer
{
    [JsonProperty("layerIndex")]
    public int LayerIndex { get; set; }

    [JsonProperty("layerId")]
    public string LayerId { get; set; } = "";

    [JsonProperty("levelStart")]
    public int LevelStart { get; set; }

    [JsonProperty("defaultSegmentSize")]
    public int DefaultSegmentSize { get; set; }

    [JsonProperty("selectSegmentSize")]
    public int SelectSegmentSize { get; set; }

    [JsonProperty("defaultSlots")]
    public List<AuraJourneySlotRule> DefaultSlots { get; set; } = new();

    [JsonProperty("selectSlots")]
    public List<AuraJourneySlotRule> SelectSlots { get; set; } = new();
}

public sealed class AuraJourneySlotRule
{
    [JsonProperty("slotIndex")]
    public int SlotIndex { get; set; }

    [JsonProperty("mapSlotIndex")]
    public int MapSlotIndex { get; set; }

    [JsonProperty("replacementPolicy")]
    public string ReplacementPolicy { get; set; } = AuraJourneyReplacementPolicies.Replace;

    [JsonProperty("mapNode")]
    public AuraJourneyMapNodeSpec MapNode { get; set; } = new();

    [JsonProperty("conditions")]
    public List<AuraJourneyCondition> Conditions { get; set; } = new();
}

public sealed class AuraJourneyMapNodeSpec
{
    [JsonProperty("nodeKey")]
    public string NodeKey { get; set; } = "";

    [JsonProperty("mapId")]
    public string MapId { get; set; } = "";

    [JsonProperty("fallbackMapId")]
    public string FallbackMapId { get; set; } = "";

    [JsonProperty("nodeId")]
    public string NodeId { get; set; } = "";

    [JsonProperty("type")]
    public string Type { get; set; } = "";

    [JsonProperty("note")]
    public string Note { get; set; } = "";

    [JsonProperty("level")]
    public string Level { get; set; } = "-1";

    [JsonProperty("dicePolicy")]
    public string DicePolicy { get; set; } = AuraJourneyDicePolicies.TreeDice;

    [JsonProperty("fixedNode")]
    public bool FixedNode { get; set; }

    [JsonProperty("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public sealed class AuraJourneyMapNodeProjection
{
    [JsonProperty("valid")]
    public bool Valid { get; set; }

    [JsonProperty("mapId")]
    public string MapId { get; set; } = "";

    [JsonProperty("nodeId")]
    public string NodeId { get; set; } = "";

    [JsonProperty("type")]
    public string Type { get; set; } = "";

    [JsonProperty("note")]
    public string Note { get; set; } = "";

    [JsonProperty("level")]
    public string Level { get; set; } = "-1";

    [JsonProperty("dicePolicy")]
    public string DicePolicy { get; set; } = AuraJourneyDicePolicies.TreeDice;

    [JsonProperty("data")]
    public Dictionary<string, string> Data { get; set; } = new();
}

public sealed class AuraJourneyRunBinding
{
    [JsonProperty("runId")]
    public string RunId { get; set; } = "";

    [JsonProperty("saveSlotId")]
    public string SaveSlotId { get; set; } = "";

    [JsonProperty("nativeModeKey")]
    public string NativeModeKey { get; set; } = "";

    [JsonProperty("nativeModeValue")]
    public string NativeModeValue { get; set; } = "";

    [JsonProperty("startedUtc")]
    public string StartedUtc { get; set; } = "";
}
