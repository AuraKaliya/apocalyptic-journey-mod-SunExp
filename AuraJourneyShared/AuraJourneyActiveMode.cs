namespace AuraJourney.Shared;

public sealed class AuraJourneyActiveMode
{
    public int SchemaVersion { get; set; } = 1;

    public string OwnerModId { get; set; } = "";

    public string JourneyId { get; set; } = "";

    public string ModeId { get; set; } = "";

    public bool IsActive { get; set; }

    public string Source { get; set; } = "";

    public string UpdatedUtc { get; set; } = "";
}
