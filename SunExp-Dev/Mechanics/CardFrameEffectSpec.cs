namespace SunExp.Dll.Mechanics;

public sealed class CardFrameEffectSpec
{
    public CardFrameEffectSpec(
        string ownerModId,
        string id,
        string skinId,
        string visualEffectId,
        string displayName,
        int priority)
    {
        OwnerModId = ownerModId;
        Id = id;
        SkinId = skinId;
        VisualEffectId = visualEffectId;
        DisplayName = displayName;
        Priority = priority;
    }

    public string OwnerModId { get; }

    public string Id { get; }

    public string SkinId { get; }

    public string VisualEffectId { get; }

    public string DisplayName { get; }

    public int Priority { get; }
}
