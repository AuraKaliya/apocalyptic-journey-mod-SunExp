using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using Newtonsoft.Json;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.GameApi;

public static class SpiritCollectionApi
{
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        SpiritCollectionService.Configure(new SpiritSidecarProfileStore(modConfig));
        initialized = true;
    }

    public static SpiritCollectionDocument Collection() => SpiritCollectionService.Snapshot();

    public static SpiritInstance? Find(string uid) => SpiritCollectionService.Find(uid);

    public static SpiritAdventureParty CurrentParty()
    {
        var json = PlayerApi.GetGameVar(TerriasIds.SpiritAdventurePartyKey, "");
        if (string.IsNullOrWhiteSpace(json))
        {
            return SpiritCollectionService.DefaultParty();
        }

        try
        {
            return SpiritCollectionService.NormalizeParty(
                AuraSharedJson.Deserialize<SpiritAdventureParty>(json) ?? SpiritCollectionService.DefaultParty());
        }
        catch
        {
            return SpiritCollectionService.DefaultParty();
        }
    }

    public static SpiritAdventureParty BeginAdventure()
    {
        MigrateLegacyCards();
        var party = SpiritCollectionService.DefaultParty();
        SaveCurrentParty(party);
        return party;
    }

    public static SpiritCaptureRecordResult RecordCapture(CapturedEnemySnapshot snapshot, string operationToken, int? aptitude = null)
    {
        var party = CurrentParty();
        try
        {
            var result = SpiritCollectionService.Capture(snapshot, operationToken, party, aptitude);
            if (result.Success)
            {
                SaveCurrentParty(party);
            }
            return result;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[SpiritCollection] capture persistence failed: " + ex.Message);
            return new SpiritCaptureRecordResult { Reason = "精灵档案写入失败。" };
        }
    }

    public static bool SetActiveForAdventure(string uid)
    {
        var party = CurrentParty();
        var normalizedUid = uid ?? "";
        party.ActiveSpiritUid = party.PartySlots.Contains(normalizedUid, StringComparer.Ordinal) ? normalizedUid : "";
        SaveCurrentParty(party);
        return string.Equals(party.ActiveSpiritUid, uid ?? "", StringComparison.Ordinal);
    }

    public static bool ConfigureDefaultPartySlot(int slot, string uid)
    {
        if (slot < 0 || slot >= SpiritCollectionService.PartyCapacity)
        {
            return false;
        }

        var party = SpiritCollectionService.DefaultParty();
        uid = (uid ?? "").Trim();
        if (uid.Length > 0 && SpiritCollectionService.Find(uid) == null)
        {
            return false;
        }

        var previousIndex = party.PartySlots.FindIndex(value => string.Equals(value, uid, StringComparison.Ordinal));
        if (previousIndex >= 0 && previousIndex != slot)
        {
            party.PartySlots[previousIndex] = party.PartySlots[slot];
        }
        party.PartySlots[slot] = uid;
        if (!party.PartySlots.Contains(party.ActiveSpiritUid, StringComparer.Ordinal))
        {
            party.ActiveSpiritUid = "";
        }
        return SpiritCollectionService.SetDefaultParty(party.PartySlots, party.ActiveSpiritUid);
    }

    public static bool RemoveFromDefaultParty(string uid)
    {
        var party = SpiritCollectionService.DefaultParty();
        var changed = false;
        for (var index = 0; index < party.PartySlots.Count; index++)
        {
            if (string.Equals(party.PartySlots[index], uid, StringComparison.Ordinal))
            {
                party.PartySlots[index] = "";
                changed = true;
            }
        }
        if (!changed) return false;
        if (string.Equals(party.ActiveSpiritUid, uid, StringComparison.Ordinal)) party.ActiveSpiritUid = "";
        return SpiritCollectionService.SetDefaultParty(party.PartySlots, party.ActiveSpiritUid);
    }

    public static bool SetDefaultActive(string uid)
    {
        var party = SpiritCollectionService.DefaultParty();
        var normalizedUid = uid ?? "";
        party.ActiveSpiritUid = party.PartySlots.Contains(normalizedUid, StringComparer.Ordinal) ? normalizedUid : "";
        return SpiritCollectionService.SetDefaultParty(party.PartySlots, party.ActiveSpiritUid ?? "");
    }

    public static SpiritOriginVector Origins(SpiritInstance instance)
    {
        return SpiritGrowthService.OriginsAt(SpiritGrowthRegistry.Resolve(instance.Snapshot), instance.Level, instance.Aptitude);
    }

    public static CompanionStats Stats(SpiritInstance instance)
    {
        return SpiritGrowthService.BattleStats(Origins(instance), SpiritIntentRegistry.ProfileFor(instance.Snapshot.ProfileKey));
    }

    public static IReadOnlyList<SpiritExperienceResult> GrantBattleExperience(
        IReadOnlyList<string> partyUids,
        string activeUid,
        int baseExperience,
        string battleToken)
    {
        return SpiritCollectionService.GrantBattleExperience(partyUids, activeUid, baseExperience, battleToken);
    }

    public static int MigrateLegacyCards()
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return 0;
        }

        var cards = role.cardList.Concat(role.UnCardList)
            .Where(card => SpiritCardFactory.IsSpiritCard(card))
            .GroupBy(card => string.IsNullOrWhiteSpace(SpiritCardFactory.Read(card)?.SpiritUid)
                ? card.InstanceID ?? Guid.NewGuid().ToString("N")
                : SpiritCardFactory.Read(card)!.SpiritUid, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        var snapshots = cards.Select(SpiritCardFactory.Read).Where(item => item != null).Cast<CapturedEnemySnapshot>().ToList();
        SpiritCollectionService.ImportLegacy(snapshots);
        var removed = RemoveLegacyCards(role.cardList);
        removed += RemoveLegacyCards(role.UnCardList);
        if (removed > 0)
        {
            TerriasLog.Info("[SpiritCollection] migrated and removed legacy spirit cards=" + removed + ".");
        }
        return removed;
    }

    private static int RemoveLegacyCards(System.Collections.ObjectModel.ObservableCollection<DataConfig> cards)
    {
        var removed = 0;
        foreach (var card in cards.Where(SpiritCardFactory.IsSpiritCard).ToArray())
        {
            if (cards.Remove(card)) removed++;
        }
        return removed;
    }

    private static void SaveCurrentParty(SpiritAdventureParty party)
    {
        PlayerApi.SetGameVar(TerriasIds.SpiritAdventurePartyKey, AuraSharedJson.Serialize(SpiritCollectionService.NormalizeParty(party)));
    }

    private sealed class SpiritSidecarProfileStore : ISpiritCollectionStore
    {
        private readonly string modDirectory;

        public SpiritSidecarProfileStore(ModConfig modConfig)
        {
            modDirectory = modConfig.DirectoryName;
        }

        public SpiritCollectionDocument Load()
        {
            var path = ProfilePath();
            if (!File.Exists(path)) return new SpiritCollectionDocument();
            try
            {
                return JsonConvert.DeserializeObject<SpiritCollectionDocument>(File.ReadAllText(path))
                       ?? new SpiritCollectionDocument();
            }
            catch (Exception ex)
            {
                var recovered = TryLoadBackup(path);
                if (recovered != null)
                {
                    TerriasLog.Warn("[SpiritCollection] recovered profile from backup " + path + ": " + ex.Message);
                    return recovered;
                }
                try
                {
                    var invalidBackup = path + ".invalid.bak";
                    if (!File.Exists(invalidBackup)) File.Copy(path, invalidBackup, overwrite: false);
                }
                catch { }
                TerriasLog.Warn("[SpiritCollection] preserved but ignored invalid profile " + path + ": " + ex.Message);
                return new SpiritCollectionDocument();
            }
        }

        public void Save(SpiritCollectionDocument document)
        {
            var path = ProfilePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var temporary = path + ".tmp";
            var backup = path + ".bak";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(document, Formatting.Indented));
            if (File.Exists(path)) File.Replace(temporary, path, backup, ignoreMetadataErrors: true);
            else File.Move(temporary, path);
        }

        private string ProfilePath()
        {
            var modsDirectory = Path.GetDirectoryName(modDirectory) ?? modDirectory;
            var gameDirectory = Path.GetDirectoryName(modsDirectory) ?? modsDirectory;
            var dataRoot = string.IsNullOrWhiteSpace(AuraSharedPaths.ModsDataDirectory)
                ? Path.Combine(gameDirectory, AuraSharedPaths.DefaultDataRootDirectoryName)
                : AuraSharedPaths.ModsDataDirectory;
            var sharedRoot = string.IsNullOrWhiteSpace(AuraSharedPaths.RootDirectory)
                ? Path.Combine(dataRoot, AuraSharedPaths.DefaultSharedDirectoryName)
                : AuraSharedPaths.RootDirectory;
            var profileDirectory = string.IsNullOrWhiteSpace(AuraSharedPaths.RootDirectory)
                ? Path.Combine(sharedRoot, "Data", "Owners", TerriasIds.ModId, TerriasIds.SpiritProfileDirectory)
                : AuraSharedPaths.OwnerSystemDataDirectory(TerriasIds.ModId, TerriasIds.SpiritProfileDirectory);
            return Path.Combine(profileDirectory, ProfileKey() + ".json");
        }

        private static SpiritCollectionDocument? TryLoadBackup(string path)
        {
            try
            {
                var backup = path + ".bak";
                return File.Exists(backup)
                    ? JsonConvert.DeserializeObject<SpiritCollectionDocument>(File.ReadAllText(backup))
                    : null;
            }
            catch { return null; }
        }

        private static string ProfileKey()
        {
            var playerId = RuntimeMember(Singleton<GameRuntimeData>.Instance, "PlayerId");
            if (!string.IsNullOrWhiteSpace(playerId)) return FamiliarId.Sanitize(playerId);
            var savePath = RuntimeMember(Singleton<GameRuntimeData>.Instance, "savePath");
            return string.IsNullOrWhiteSpace(savePath) ? "local" : FamiliarId.Sanitize(Path.GetFileNameWithoutExtension(savePath));
        }

        private static string RuntimeMember(object? target, string name)
        {
            if (target == null) return "";
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = target.GetType();
            return Convert.ToString(type.GetProperty(name, flags)?.GetValue(target)
                                    ?? type.GetField(name, flags)?.GetValue(target)) ?? "";
        }
    }
}
