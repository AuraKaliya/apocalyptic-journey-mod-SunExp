namespace SunExp.Dll.Mechanics;

public sealed class SpiritState
{
    public SpiritState(
        CapturedEnemySnapshot snapshot,
        string ownerStatusId,
        string ownerPlayerId,
        SpiritOtherObj spirit,
        int slotIndex,
        int exchangeCount,
        int generation)
    {
        Snapshot = snapshot;
        OwnerStatusId = ownerStatusId ?? "";
        OwnerPlayerId = ownerPlayerId ?? "";
        Spirit = spirit;
        SlotIndex = slotIndex;
        ExchangeCount = System.Math.Max(0, exchangeCount);
        Generation = System.Math.Max(1, generation);
    }

    public CapturedEnemySnapshot Snapshot { get; }

    public string StatusId => Spirit?.InstanceId ?? "";

    public string OwnerStatusId { get; }

    public string OwnerPlayerId { get; }

    public SpiritOtherObj Spirit { get; }

    public int SlotIndex { get; }

    public int ExchangeCount { get; }

    public int Generation { get; }
}
