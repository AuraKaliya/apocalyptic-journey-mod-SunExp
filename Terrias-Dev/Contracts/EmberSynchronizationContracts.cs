using AuraShared.Core;
namespace Terrias.Dll.Contracts;

public sealed class EmberAdventureStateSnapshot
{
    public string AdventureId { get; set; } = "";
    public string OwnerPlayerId { get; set; } = "";
    public string OwnerStatusId { get; set; } = "";
    public int Level { get; set; }
    public long Version { get; set; }
}
public sealed class EmberUpdateRequest
{
    public string AdventureId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string RequestId { get; set; } = "";
    public long ExpectedVersion { get; set; }
    public int Level { get; set; }
    public bool Query { get; set; }
}
public sealed class EmberSyncResult
{
    public string RequestId { get; set; } = "";
    public string Status { get; set; } = "Rejected";
    public string Reason { get; set; } = "";
    public EmberAdventureStateSnapshot Snapshot { get; set; } = new();
}
public sealed class EmberPersistentValue
{
    public int Level { get; set; }
    public AuraVersionedReceipt Receipt { get; set; } = new();
}
public sealed class EmberClientOutbox
{
    public string AdventureId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public EmberAdventureStateSnapshot Confirmed { get; set; } = new();
    public EmberUpdateRequest? Pending { get; set; }
    public bool HasDesired { get; set; }
    public int DesiredLevel { get; set; }
    public string Status { get; set; } = "Pending";
}
