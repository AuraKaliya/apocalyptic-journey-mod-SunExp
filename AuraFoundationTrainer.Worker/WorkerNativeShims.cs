namespace AuraToolsExp.Dll.Features.AutoBattle;

// The generated official-content program references the game's PowerData DTO
// only for its actor id. The headless worker supplies the protocol-equivalent
// shape without loading Witch or Unity assemblies.
public sealed class PowerData
{
    public string Id { get; set; } = "";
}
