namespace SunExp.Dll.Mechanics;

public sealed class SpiritState
{
    public SpiritState(CapturedEnemySnapshot snapshot, string ownerStatusId, string ownerPlayerId, SpiritOtherObj spirit, int slotIndex)
    {
        Snapshot = snapshot;
        OwnerStatusId = ownerStatusId ?? "";
        OwnerPlayerId = ownerPlayerId ?? "";
        Spirit = spirit;
        SlotIndex = slotIndex;
    }

    public CapturedEnemySnapshot Snapshot { get; }

    public string StatusId => Spirit?.InstanceId ?? "";

    public string OwnerStatusId { get; }

    public string OwnerPlayerId { get; }

    public SpiritOtherObj Spirit { get; }

    public int SlotIndex { get; }
}
