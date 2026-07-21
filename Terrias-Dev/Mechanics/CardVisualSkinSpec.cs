namespace Terrias.Dll.Mechanics;

public sealed class CardVisualSkinSpec
{
    public CardVisualSkinSpec(string id, string framePath, string backgroundPath, string displayName)
        : this("Terrias", id, framePath, backgroundPath, displayName, 0)
    {
    }

    public CardVisualSkinSpec(string ownerModId, string id, string framePath, string backgroundPath, string displayName, int priority)
    {
        OwnerModId = ownerModId;
        Id = id;
        FramePath = framePath;
        BackgroundPath = backgroundPath;
        DisplayName = displayName;
        Priority = priority;
    }

    public string OwnerModId { get; }

    public string Id { get; }

    public string FramePath { get; }

    public string BackgroundPath { get; }

    public string DisplayName { get; }

    public int Priority { get; }
}
