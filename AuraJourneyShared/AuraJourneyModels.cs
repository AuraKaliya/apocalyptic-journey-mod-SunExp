using System.Collections.Generic;
using Newtonsoft.Json;

namespace AuraJourney.Shared;

public static class AuraJourneyConstants
{
    public const int DefinitionSchemaVersion = 1;
    public const int StateSchemaVersion = 1;
    public const string SystemName = "Journey";
}

public static class AuraJourneyConditionKinds
{
    public const string Always = "Always";
    public const string Flag = "Flag";
    public const string NotFlag = "NotFlag";
    public new const string Equals = "Equals";
    public const string NotEquals = "NotEquals";
    public const string MinCounter = "MinCounter";
    public const string MaxCounter = "MaxCounter";
    public const string AnyRole = "AnyRole";
    public const string AllRoles = "AllRoles";
    public const string PlayerCountAtLeast = "PlayerCountAtLeast";
    public const string PlayerCountAtMost = "PlayerCountAtMost";
}

public sealed class AuraJourneyDefinition
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = AuraJourneyConstants.DefinitionSchemaVersion;

    [JsonProperty("journeyId")]
    public string JourneyId { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("description")]
    public string Description { get; set; } = "";

    [JsonProperty("entryNodeId")]
    public string EntryNodeId { get; set; } = "";

    [JsonProperty("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonProperty("nodes")]
    public List<AuraJourneyNodeDefinition> Nodes { get; set; } = new();

    [JsonProperty("routeGraph")]
    public AuraJourneyRouteGraph RouteGraph { get; set; } = new();
}

public sealed class AuraJourneyNodeDefinition
{
    [JsonProperty("nodeId")]
    public string NodeId { get; set; } = "";

    [JsonProperty("kind")]
    public string Kind { get; set; } = "";

    [JsonProperty("weight")]
    public int Weight { get; set; } = 1;

    [JsonProperty("conditions")]
    public List<AuraJourneyCondition> Conditions { get; set; } = new();

    [JsonProperty("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public sealed class AuraJourneyCondition
{
    [JsonProperty("conditionId")]
    public string ConditionId { get; set; } = "";

    [JsonProperty("kind")]
    public string Kind { get; set; } = AuraJourneyConditionKinds.Always;

    [JsonProperty("key")]
    public string Key { get; set; } = "";

    [JsonProperty("value")]
    public string Value { get; set; } = "";

    [JsonProperty("values")]
    public List<string> Values { get; set; } = new();

    [JsonProperty("number")]
    public int Number { get; set; }
}

public sealed class AuraJourneyConditionContext
{
    [JsonProperty("roleIds")]
    public List<string> RoleIds { get; set; } = new();

    [JsonProperty("flags")]
    public Dictionary<string, bool> Flags { get; set; } = new();

    [JsonProperty("values")]
    public Dictionary<string, string> Values { get; set; } = new();

    [JsonProperty("counters")]
    public Dictionary<string, int> Counters { get; set; } = new();

    [JsonProperty("playerCount")]
    public int PlayerCount { get; set; } = 1;
}

public sealed class AuraJourneyState
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = AuraJourneyConstants.StateSchemaVersion;

    [JsonProperty("journeyId")]
    public string JourneyId { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("version")]
    public long Version { get; set; }

    [JsonProperty("run")]
    public AuraJourneyRunBinding Run { get; set; } = new();

    [JsonProperty("activeNodeId")]
    public string ActiveNodeId { get; set; } = "";

    [JsonProperty("completedNodeIds")]
    public List<string> CompletedNodeIds { get; set; } = new();

    [JsonProperty("selectedRouteIds")]
    public List<string> SelectedRouteIds { get; set; } = new();

    [JsonProperty("flags")]
    public Dictionary<string, bool> Flags { get; set; } = new();

    [JsonProperty("values")]
    public Dictionary<string, string> Values { get; set; } = new();

    [JsonProperty("counters")]
    public Dictionary<string, int> Counters { get; set; } = new();

    [JsonProperty("events")]
    public List<AuraJourneyStateEvent> Events { get; set; } = new();
}

public sealed class AuraJourneyStateEvent
{
    [JsonProperty("version")]
    public long Version { get; set; }

    [JsonProperty("timestampUtc")]
    public string TimestampUtc { get; set; } = "";

    [JsonProperty("actorModId")]
    public string ActorModId { get; set; } = "";

    [JsonProperty("action")]
    public string Action { get; set; } = "";

    [JsonProperty("nodeId")]
    public string NodeId { get; set; } = "";

    [JsonProperty("message")]
    public string Message { get; set; } = "";
}

public sealed class AuraJourneyMutation
{
    [JsonProperty("run")]
    public AuraJourneyRunBinding Run { get; set; } = new();

    [JsonProperty("activeNodeId")]
    public string ActiveNodeId { get; set; } = "";

    [JsonProperty("completeNodeId")]
    public string CompleteNodeId { get; set; } = "";

    [JsonProperty("selectRouteId")]
    public string SelectRouteId { get; set; } = "";

    [JsonProperty("setFlags")]
    public Dictionary<string, bool> SetFlags { get; set; } = new();

    [JsonProperty("setValues")]
    public Dictionary<string, string> SetValues { get; set; } = new();

    [JsonProperty("addCounters")]
    public Dictionary<string, int> AddCounters { get; set; } = new();
}

public sealed class AuraJourneyCommitRequest
{
    [JsonProperty("journeyId")]
    public string JourneyId { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("authorityId")]
    public string AuthorityId { get; set; } = "";

    [JsonProperty("isAuthority")]
    public bool IsAuthority { get; set; }

    [JsonProperty("expectedRevision")]
    public long ExpectedRevision { get; set; } = -1;

    [JsonProperty("action")]
    public string Action { get; set; } = "";

    [JsonProperty("nodeId")]
    public string NodeId { get; set; } = "";

    [JsonProperty("message")]
    public string Message { get; set; } = "";

    [JsonProperty("mutation")]
    public AuraJourneyMutation Mutation { get; set; } = new();
}

public sealed class AuraJourneyCommitResult
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("conflict")]
    public bool Conflict { get; set; }

    [JsonProperty("revision")]
    public long Revision { get; set; }

    [JsonProperty("state")]
    public AuraJourneyState State { get; set; } = new();

    [JsonProperty("message")]
    public string Message { get; set; } = "";
}
