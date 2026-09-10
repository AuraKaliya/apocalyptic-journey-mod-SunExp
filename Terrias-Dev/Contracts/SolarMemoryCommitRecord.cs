using AuraShared.Core;
namespace Terrias.Dll.Contracts;

public sealed class SolarMemoryCommitRecord
{
    public const string ConfirmedTokenKey = "TerriasSolarMemoryConfirmedCommit";
    public const string ConfirmedAdventureKey = "TerriasSolarMemoryConfirmedAdventure";
    public int Version { get; set; } = 1;
    public string AdventureId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string Token { get; set; } = "";
    public string Payload { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string Diagnostic { get; set; } = "";
    public bool IsValid => Version == 1 && AuraNetworkIdentityState.ValidId(AdventureId)
        && AuraNetworkIdentityState.ValidId(Token) && !string.IsNullOrWhiteSpace(PlayerId)
        && PayloadHash.Length == 64 && Payload.Length > 0 && Payload.Length <= 40000;
}


public enum SolarMemoryRoleCommitSubmission { Rejected, Pending, Accepted }
