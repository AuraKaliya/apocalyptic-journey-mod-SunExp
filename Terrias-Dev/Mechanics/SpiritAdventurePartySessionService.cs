using System;

namespace Terrias.Dll.Mechanics;

public interface ISpiritAdventurePartySessionStore
{
    SpiritAdventurePartySessionDocument Load();

    void Save(SpiritAdventurePartySessionDocument document);
}

public sealed class SpiritAdventurePartySessionDocument
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public string JourneyId { get; set; } = "";

    public string PlayerId { get; set; } = "";

    public SpiritAdventureParty Party { get; set; } = new();

    public string UpdatedAt { get; set; } = "";

    public SpiritAdventurePartySessionDocument Clone()
    {
        return new SpiritAdventurePartySessionDocument
        {
            Version = Version,
            JourneyId = JourneyId ?? "",
            PlayerId = PlayerId ?? "",
            Party = Party?.Clone() ?? new SpiritAdventureParty(),
            UpdatedAt = UpdatedAt ?? ""
        };
    }
}

public static class SpiritAdventurePartySessionService
{
    private static readonly object SyncRoot = new();
    private static ISpiritAdventurePartySessionStore? store;
    private static SpiritAdventurePartySessionDocument document = new();
    private static bool loaded;

    public static void Configure(ISpiritAdventurePartySessionStore sessionStore)
    {
        lock (SyncRoot)
        {
            store = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
            document = new SpiritAdventurePartySessionDocument();
            loaded = false;
        }
    }

    public static SpiritAdventureParty EnterJourney(
        string journeyId,
        string playerId,
        SpiritAdventureParty defaultParty)
    {
        lock (SyncRoot)
        {
            EnsureLoaded();
            var normalizedJourneyId = NormalizeIdentity(journeyId, "pending-journey");
            var normalizedPlayerId = NormalizeIdentity(playerId, "local-player");
            if (Matches(document, normalizedJourneyId, normalizedPlayerId))
            {
                return document.Party.Clone();
            }

            var candidate = NewDocument(normalizedJourneyId, normalizedPlayerId, defaultParty);
            PersistUnlocked(candidate);
            return document.Party.Clone();
        }
    }

    public static SpiritAdventureParty CurrentOrBegin(
        string journeyId,
        string playerId,
        SpiritAdventureParty defaultParty)
    {
        return EnterJourney(journeyId, playerId, defaultParty);
    }

    public static void SaveParty(
        string journeyId,
        string playerId,
        SpiritAdventureParty party)
    {
        lock (SyncRoot)
        {
            EnsureLoaded();
            var normalizedJourneyId = NormalizeIdentity(journeyId, "pending-journey");
            var normalizedPlayerId = NormalizeIdentity(playerId, "local-player");
            SpiritAdventurePartySessionDocument candidate;
            if (!Matches(document, normalizedJourneyId, normalizedPlayerId))
            {
                candidate = NewDocument(normalizedJourneyId, normalizedPlayerId, party);
            }
            else
            {
                var nextParty = party?.Clone() ?? new SpiritAdventureParty();
                if (SameParty(document.Party, nextParty))
                {
                    return;
                }

                candidate = document.Clone();
                candidate.Party = nextParty;
                candidate.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
            }

            PersistUnlocked(candidate);
        }
    }

    public static SpiritAdventurePartySessionDocument Snapshot()
    {
        lock (SyncRoot)
        {
            EnsureLoaded();
            return document.Clone();
        }
    }

    private static void EnsureLoaded()
    {
        if (loaded)
        {
            return;
        }

        document = store?.Load()?.Clone() ?? new SpiritAdventurePartySessionDocument();
        document.Party ??= new SpiritAdventureParty();
        loaded = true;
    }

    private static SpiritAdventurePartySessionDocument NewDocument(
        string journeyId,
        string playerId,
        SpiritAdventureParty party)
    {
        return new SpiritAdventurePartySessionDocument
        {
            Version = SpiritAdventurePartySessionDocument.CurrentVersion,
            JourneyId = journeyId,
            PlayerId = playerId,
            Party = party?.Clone() ?? new SpiritAdventureParty(),
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
        };
    }

    private static bool Matches(
        SpiritAdventurePartySessionDocument candidate,
        string journeyId,
        string playerId)
    {
        return candidate.Version == SpiritAdventurePartySessionDocument.CurrentVersion
               && string.Equals(candidate.JourneyId ?? "", journeyId, StringComparison.Ordinal)
               && string.Equals(candidate.PlayerId ?? "", playerId, StringComparison.Ordinal);
    }

    private static string NormalizeIdentity(string value, string fallback)
    {
        var normalized = (value ?? "").Trim();
        return normalized.Length == 0 ? fallback : normalized;
    }

    private static bool SameParty(SpiritAdventureParty left, SpiritAdventureParty right)
    {
        if (!string.Equals(left?.ActiveSpiritUid ?? "", right?.ActiveSpiritUid ?? "", StringComparison.Ordinal))
        {
            return false;
        }

        var leftSlots = left?.PartySlots;
        var rightSlots = right?.PartySlots;
        if (leftSlots == null || rightSlots == null || leftSlots.Count != rightSlots.Count)
        {
            return leftSlots == null && rightSlots == null;
        }

        for (var index = 0; index < leftSlots.Count; index++)
        {
            if (!string.Equals(leftSlots[index] ?? "", rightSlots[index] ?? "", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void PersistUnlocked(SpiritAdventurePartySessionDocument candidate)
    {
        var committed = candidate.Clone();
        store?.Save(committed.Clone());
        document = committed;
    }
}
