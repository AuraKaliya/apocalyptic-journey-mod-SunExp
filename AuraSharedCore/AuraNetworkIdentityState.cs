using System;

namespace AuraShared.Core;

// Local lifecycle counters are deliberately not network identities.
public sealed class AuraNetworkIdentityState
{
    public string AdventureId { get; private set; } = "";
    public string BattleId { get; private set; } = "";
    public int BattleEpoch { get; private set; }
    public string NativeFingerprint { get; private set; } = "";
    public bool BattleReady { get; private set; }
    public long RoomGeneration { get; private set; }

    public void ChangeRoom()
    {
        RoomGeneration++;
        ClearAdventure();
    }

    public void ClearAdventure()
    {
        AdventureId = "";
        ClearBattle();
    }

    public bool BindAdventure(string id)
    {
        if (!ValidId(id)) return false;
        if (AdventureId == id) return true;
        AdventureId = id;
        ClearBattle();
        return true;
    }

    public void BindBattle(string adventureId, string battleId, int epoch, string nativeFingerprint)
    {
        if (!ValidId(adventureId) || !ValidId(battleId) || epoch <= 0 || string.IsNullOrWhiteSpace(nativeFingerprint))
            throw new ArgumentException("Incomplete authoritative battle identity.");
        BindAdventure(adventureId);
        BattleId = battleId;
        BattleEpoch = epoch;
        NativeFingerprint = nativeFingerprint;
        BattleReady = true;
    }

    public void EndBattle()
    {
        BattleReady = false;
    }

    private void ClearBattle()
    {
        BattleId = "";
        BattleEpoch = 0;
        NativeFingerprint = "";
        BattleReady = false;
    }

    public bool MatchesBattle(string id) => BattleReady && ValidId(id) && string.Equals(BattleId, id, StringComparison.Ordinal);
    public bool MatchesBattleResult(string id) => ValidId(id) && string.Equals(BattleId, id, StringComparison.Ordinal);
    public static bool ValidId(string? id) => Guid.TryParseExact(id, "N", out _);
}
