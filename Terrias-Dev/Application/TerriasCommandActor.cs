namespace Terrias.Dll.Application;

/// <summary>
/// Authenticated command identity after a Network adapter has bound the real
/// sender. Application handlers consume this value and never authorize from
/// payload-provided player fields.
/// </summary>
public readonly struct TerriasCommandActor
{
    public TerriasCommandActor(
        string playerId,
        bool isAvailable,
        bool isLobbyMember,
        bool isLobbyHost,
        string source)
    {
        PlayerId = playerId ?? "";
        IsAvailable = isAvailable;
        IsLobbyMember = isLobbyMember;
        IsLobbyHost = isLobbyHost;
        Source = source ?? "";
    }

    public string PlayerId { get; }

    public bool IsAvailable { get; }

    public bool IsLobbyMember { get; }

    public bool IsLobbyHost { get; }

    public string Source { get; }
}
