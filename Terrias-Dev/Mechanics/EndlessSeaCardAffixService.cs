using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class EndlessSeaCardAffixService
{
    private static int starterDeckWriteDepth;

    public static bool RunWithStarterDeckSuppressed(Func<bool> action)
    {
        if (action == null) return false;
        starterDeckWriteDepth++;
        try { return action(); }
        finally { starterDeckWriteDepth--; }
    }

    public static bool AttachOwnedReward(IDataConfig card) =>
        starterDeckWriteDepth == 0 && EndlessSeaBurnoutPolicy.AttachReward(card);

    public static int MarkStarterDeckBaseline(RoleTable? role, string source)
    {
        if (role == null) return 0;
        var changed = OwnedCards(role).Count(EndlessSeaBurnoutPolicy.MarkStarter);
        if (changed > 0) TryPersistRole(role, source);
        return changed;
    }

    public static int NormalizeOwnedCards(string source) => NormalizeOwnedCards(RoleTable.Instance, source);

    public static int NormalizeOwnedCards(RoleTable? role, string source)
    {
        if (role == null || starterDeckWriteDepth > 0
            || GameSaveManager.GetValue<string>(TerriasIds.EndlessSeaModeKey) != "1"
            || GameSaveManager.GetValue<string>(TerriasIds.EndlessSeaStarterDeckAppliedKey) != "1") return 0;

        var save = GameSaveManager.GetNowSave();
        if (save?.GameVars == null) return 0;
        var key = TerriasIds.EndlessSeaAffixMigrationKey + ":" + role.Id;
        var migrating = !save.GameVars.ContainsKey(key);
        var cards = OwnedCards(role);
        var hasStarterEvidence = cards.Any(EndlessSeaBurnoutPolicy.IsStarter);
        // Old receipts recover exact instances already purified. Without a
        // starter baseline, unidentified old cards are preserved.
        var receipts = migrating ? EndlessAbyssRunLedger.Entries() : Array.Empty<string>();
        var changed = 0;
        foreach (var card in cards)
        {
            var purified = migrating && !string.IsNullOrWhiteSpace(card.InstanceID)
                && receipts.Any(entry => entry.StartsWith("milestone:player:", StringComparison.Ordinal)
                    && entry.EndsWith(":result:remove-burnout_" + card.InstanceID, StringComparison.Ordinal));
            if (purified || EndlessSeaBurnoutPolicy.IsPurified(card))
            {
                if (EndlessSeaBurnoutPolicy.RestorePurification(card)) changed++;
            }
            else if (EndlessSeaBurnoutPolicy.IsStarter(card))
            {
                if (EndlessSeaBurnoutPolicy.MarkStarter(card)) changed++;
            }
            else if ((migrating && hasStarterEvidence)
                || CardMutationService.HasRuntimeMarker(card, TerriasIds.EndlessSeaAutoBurnoutMarker))
            {
                if (EndlessSeaBurnoutPolicy.AttachReward(card)) changed++;
            }
        }

        if ((changed > 0 || migrating) && !TryPersistRole(role, source))
            throw new InvalidOperationException("Could not persist the abyss card attachment state.");
        if (migrating) save.SetValue(key, "1");
        return changed;
    }

    public static bool TryPersistCurrentRole(string source) => TryPersistRole(RoleTable.Instance, source);

    public static bool TryPersistRole(RoleTable? role, string source)
    {
        if (role == null || GameSaveManager.GetNowSave() == null) return false;
        try
        {
            GameSaveManager.UpdateRoles(role);
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[EndlessSeaCardAffix] persist failed from " + source + ": " + ex.Message);
            return false;
        }
    }

    private static IReadOnlyList<IDataConfig> OwnedCards(RoleTable role) =>
        EndlessSeaBurnoutPolicy.DistinctInstances(role.cardList.Cast<IDataConfig>().Concat(role.UnCardList));
}
