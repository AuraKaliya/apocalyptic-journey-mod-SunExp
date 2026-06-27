namespace SunExp.Dll.Mechanics;

public sealed class CardVisualSkinSpec
{
    public CardVisualSkinSpec(string id, string framePath, string backgroundPath, string displayName)
    {
        Id = id;
        FramePath = framePath;
        BackgroundPath = backgroundPath;
        DisplayName = displayName;
    }

    public string Id { get; }

    public string FramePath { get; }

    public string BackgroundPath { get; }

    public string DisplayName { get; }
}
