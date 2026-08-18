using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.AdventureArchive;

internal sealed class AdventureArchiveRecord
{
    internal string AdventureId { get; set; } = "";
    internal string StartedUtc { get; set; } = "";
    internal string EndedUtc { get; set; } = "";
    internal string Status { get; set; } = "in-progress";
    internal string Result { get; set; } = "";
    internal string ModeId { get; set; } = "";
    internal string RoleId { get; set; } = "";
    internal string GameBuild { get; set; } = "";
    internal string ToolBuild { get; set; } = "";
    internal string ModFingerprint { get; set; } = "";
    internal string LatestStage { get; set; } = "";
    internal int EventCount { get; set; }
    internal int SnapshotCount { get; set; }
    internal int BattleCount { get; set; }
}

internal sealed class AdventureArchiveEvent
{
    internal int Sequence { get; set; }
    internal string OccurredUtc { get; set; } = "";
    internal string Kind { get; set; } = "";
    internal string Title { get; set; } = "";
    internal string Detail { get; set; } = "";
    internal string PayloadJson { get; set; } = "{}";
}

internal sealed class AdventureArchiveSnapshot
{
    internal int Sequence { get; set; }
    internal string OccurredUtc { get; set; } = "";
    internal string Reason { get; set; } = "";
    internal string Stage { get; set; } = "";
    internal string RoleId { get; set; } = "";
    internal string CardsJson { get; set; } = "[]";
    internal string RelicsJson { get; set; } = "[]";
    internal string StateJson { get; set; } = "{}";
}

internal sealed class AdventureArchiveDetails
{
    internal AdventureArchiveRecord Record { get; set; } = new();
    internal List<AdventureArchiveEvent> Events { get; set; } = new();
    internal List<AdventureArchiveSnapshot> Snapshots { get; set; } = new();
    internal List<string> BattleRecordIds { get; set; } = new();
}
