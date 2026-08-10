using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class SpiritCollectionService
{
    public const int CurrentVersion = 3;
    public const int PartyCapacity = 6;
    public const int LegacyCardMigrationVersion = 1;
    private const int OperationHistoryLimit = 512;
    private static readonly object SyncRoot = new();
    private static ISpiritCollectionStore? store;
    private static SpiritCollectionDocument document = Normalize(new SpiritCollectionDocument());

    public static void Configure(ISpiritCollectionStore profileStore)
    {
        lock (SyncRoot)
        {
            store = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
            document = Normalize(store.Load());
            SaveUnlocked(document);
        }
    }

    public static SpiritCollectionDocument Snapshot()
    {
        lock (SyncRoot)
        {
            return CloneDocument(document);
        }
    }

    public static SpiritInstance? Find(string spiritUid)
    {
        lock (SyncRoot)
        {
            return document.Instances.FirstOrDefault(item => Same(item.SpiritUid, spiritUid))?.Clone();
        }
    }

    public static SpiritCaptureRecordResult Capture(
        CapturedEnemySnapshot snapshot,
        string operationToken,
        SpiritAdventureParty party,
        int? fixedAptitude = null)
    {
        lock (SyncRoot)
        {
            var token = (operationToken ?? "").Trim();
            if (token.Length > 0 && document.ProcessedCaptureTokens.TryGetValue(token, out var existingUid))
            {
                return new SpiritCaptureRecordResult
                {
                    Success = true,
                    DuplicateOperation = true,
                    AddedToParty = (party?.PartySlots ?? new List<string>()).Contains(existingUid, StringComparer.Ordinal),
                    Instance = document.Instances.FirstOrDefault(item => Same(item.SpiritUid, existingUid))?.Clone()
                };
            }

            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.EnemyId))
            {
                return new SpiritCaptureRecordResult { Reason = "捕获快照无效。" };
            }

            var candidate = CloneDocument(document);
            var normalizedSnapshot = SpiritModelCloner.CloneSnapshot(snapshot);
            normalizedSnapshot.SpiritUid = UniqueUid(candidate, normalizedSnapshot.SpiritUid);
            normalizedSnapshot.InstanceId = "";
            normalizedSnapshot.SpiritLevel = 0;
            normalizedSnapshot.SpiritAptitude = 0;
            normalizedSnapshot.OriginMagic = 0;
            normalizedSnapshot.OriginSpirit = 0;
            normalizedSnapshot.OriginLuck = 0;
            normalizedSnapshot.OriginPerception = 0;
            normalizedSnapshot.DeploymentToken = "";
            normalizedSnapshot.SpeciesId = "";
            normalizedSnapshot.ProfileId = "";
            var identity = SpiritGrowthRegistry.ResolveIdentity(normalizedSnapshot);
            var aptitudeRoll = SpiritGrowthRegistry.AptitudeRollFor(identity.Profile);
            var instance = new SpiritInstance
            {
                SpiritUid = normalizedSnapshot.SpiritUid,
                SpeciesId = identity.SpeciesId,
                ProfileId = identity.ProfileId,
                Snapshot = normalizedSnapshot,
                Level = 1,
                Experience = 0,
                Aptitude = Math.Max(aptitudeRoll.Minimum, Math.Min(aptitudeRoll.Maximum,
                    fixedAptitude ?? SpiritGrowthService.RollAptitude(identity.Profile, token + ":" + normalizedSnapshot.SpiritUid))),
                CapturedAt = string.IsNullOrWhiteSpace(normalizedSnapshot.CapturedAt)
                    ? DateTimeOffset.UtcNow.ToString("O")
                    : normalizedSnapshot.CapturedAt
            };
            if (identity.UsedFallback && IsOwnedSource(normalizedSnapshot.SourceModId))
            {
                TerriasLog.Warn("[SpiritGrowthRegistry] owned capture used fallback profileId=" + identity.ProfileId
                                + ", sourceModId=" + normalizedSnapshot.SourceModId
                                + ", enemyId=" + normalizedSnapshot.EnemyId);
            }
            candidate.Instances.Add(instance);
            if (token.Length > 0)
            {
                candidate.ProcessedCaptureTokens[token] = instance.SpiritUid;
                TrimCaptureHistory(candidate);
            }

            var normalizedParty = NormalizeParty(party, candidate);
            var addedToParty = AddToFirstEmpty(normalizedParty.PartySlots, instance.SpiritUid);
            AddToFirstEmpty(candidate.DefaultPartySlots, instance.SpiritUid);
            SaveUnlocked(candidate);
            document = candidate;
            CopyParty(normalizedParty, party);
            return new SpiritCaptureRecordResult
            {
                Success = true,
                AddedToParty = addedToParty,
                Instance = instance.Clone()
            };
        }
    }

    public static bool SetDefaultParty(IReadOnlyList<string> slots, string activeSpiritUid)
    {
        lock (SyncRoot)
        {
            var candidate = CloneDocument(document);
            candidate.DefaultPartySlots = NormalizeSlots(slots, candidate);
            var normalizedActiveUid = activeSpiritUid ?? "";
            candidate.DefaultActiveSpiritUid = candidate.DefaultPartySlots.Contains(normalizedActiveUid, StringComparer.Ordinal)
                ? normalizedActiveUid
                : "";
            SaveUnlocked(candidate);
            document = candidate;
            return true;
        }
    }

    public static SpiritAdventureParty DefaultParty()
    {
        lock (SyncRoot)
        {
            return NormalizeParty(new SpiritAdventureParty
            {
                PartySlots = new List<string>(document.DefaultPartySlots),
                ActiveSpiritUid = document.DefaultActiveSpiritUid
            }, document);
        }
    }

    public static bool ToggleFavorite(string spiritUid)
    {
        return ToggleFlag(spiritUid, true);
    }

    public static bool ToggleLocked(string spiritUid)
    {
        return ToggleFlag(spiritUid, false);
    }

    public static SpiritExperienceResult? GrantExperience(string spiritUid, int amount, string battleToken)
    {
        lock (SyncRoot)
        {
            if (amount <= 0 || document.ProcessedBattleTokens.Contains(battleToken ?? "", StringComparer.Ordinal))
            {
                return null;
            }

            var candidate = CloneDocument(document);
            var instance = candidate.Instances.FirstOrDefault(item => Same(item.SpiritUid, spiritUid));
            if (instance == null)
            {
                return null;
            }

            var result = SpiritGrowthService.GrantExperience(instance, amount);
            SaveUnlocked(candidate);
            document = candidate;
            return result;
        }
    }

    public static IReadOnlyList<SpiritExperienceResult> GrantBattleExperience(
        IReadOnlyList<string> partyUids,
        string activeSpiritUid,
        int baseExperience,
        string battleToken)
    {
        lock (SyncRoot)
        {
            if (baseExperience <= 0 || string.IsNullOrWhiteSpace(battleToken)
                || document.ProcessedBattleTokens.Contains(battleToken, StringComparer.Ordinal))
            {
                return Array.Empty<SpiritExperienceResult>();
            }

            var candidate = CloneDocument(document);
            var results = new List<SpiritExperienceResult>();
            foreach (var uid in (partyUids ?? Array.Empty<string>()).Where(uid => !string.IsNullOrWhiteSpace(uid)).Distinct(StringComparer.Ordinal))
            {
                var instance = candidate.Instances.FirstOrDefault(item => Same(item.SpiritUid, uid));
                if (instance == null)
                {
                    continue;
                }

                var amount = Same(uid, activeSpiritUid) ? baseExperience : Math.Max(1, baseExperience / 4);
                results.Add(SpiritGrowthService.GrantExperience(instance, amount));
            }

            candidate.ProcessedBattleTokens.Add(battleToken);
            if (candidate.ProcessedBattleTokens.Count > OperationHistoryLimit)
            {
                candidate.ProcessedBattleTokens.RemoveRange(0, candidate.ProcessedBattleTokens.Count - OperationHistoryLimit);
            }
            SaveUnlocked(candidate);
            document = candidate;
            return results;
        }
    }

    public static bool ImportLegacy(IReadOnlyList<CapturedEnemySnapshot> snapshots)
    {
        lock (SyncRoot)
        {
            if (document.LegacyCardMigrationVersion >= LegacyCardMigrationVersion)
            {
                return false;
            }

            var candidate = CloneDocument(document);
            foreach (var source in snapshots ?? Array.Empty<CapturedEnemySnapshot>())
            {
                if (source == null || string.IsNullOrWhiteSpace(source.EnemyId))
                {
                    continue;
                }

                var snapshot = SpiritModelCloner.CloneSnapshot(source);
                snapshot.SpiritUid = UniqueUid(candidate, snapshot.SpiritUid);
                snapshot.SpeciesId = "";
                snapshot.ProfileId = "";
                var identity = SpiritGrowthRegistry.ResolveIdentity(snapshot);
                candidate.Instances.Add(new SpiritInstance
                {
                    SpiritUid = snapshot.SpiritUid,
                    SpeciesId = identity.SpeciesId,
                    ProfileId = identity.ProfileId,
                    Snapshot = snapshot,
                    Level = 1,
                    Aptitude = SpiritGrowthService.LegacyAptitude,
                    CapturedAt = snapshot.CapturedAt
                });
                AddToFirstEmpty(candidate.DefaultPartySlots, snapshot.SpiritUid);
            }

            candidate.LegacyCardMigrationVersion = LegacyCardMigrationVersion;
            SaveUnlocked(candidate);
            document = candidate;
            return true;
        }
    }

    public static SpiritAdventureParty NormalizeParty(SpiritAdventureParty? party)
    {
        lock (SyncRoot)
        {
            return NormalizeParty(party, document);
        }
    }

    private static SpiritCollectionDocument Normalize(SpiritCollectionDocument? source)
    {
        source ??= new SpiritCollectionDocument();
        source.Version = CurrentVersion;
        source.Instances ??= new List<SpiritInstance>();
        source.ProcessedCaptureTokens ??= new Dictionary<string, string>(StringComparer.Ordinal);
        source.ProcessedBattleTokens ??= new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        source.Instances = source.Instances.Where(item => item?.Snapshot != null && !string.IsNullOrWhiteSpace(item.Snapshot.EnemyId))
            .Select(item =>
            {
                item.SpiritUid = string.IsNullOrWhiteSpace(item.SpiritUid) ? Guid.NewGuid().ToString("N") : item.SpiritUid.Trim();
                while (!seen.Add(item.SpiritUid)) item.SpiritUid = Guid.NewGuid().ToString("N");
                item.Snapshot.SpiritUid = item.SpiritUid;
                if (string.IsNullOrWhiteSpace(item.SpeciesId) || string.IsNullOrWhiteSpace(item.ProfileId))
                {
                    var identity = SpiritGrowthRegistry.ResolveIdentity(item.Snapshot);
                    item.SpeciesId = identity.SpeciesId;
                    item.ProfileId = identity.ProfileId;
                }
                var profile = SpiritGrowthRegistry.Resolve(item);
                var maxLevel = SpiritGrowthService.MaxLevelFor(profile);
                var roll = SpiritGrowthRegistry.AptitudeRollFor(profile);
                item.Level = Math.Max(1, Math.Min(maxLevel, item.Level));
                item.Experience = item.Level >= maxLevel
                    ? 0
                    : Math.Max(0, Math.Min(SpiritGrowthService.ExperienceToNextLevel(profile, item.Level) - 1, item.Experience));
                item.Aptitude = Math.Max(roll.Minimum, Math.Min(roll.Maximum, item.Aptitude));
                return item;
            }).ToList();
        source.DefaultPartySlots = NormalizeSlots(source.DefaultPartySlots, source);
        var normalizedDefaultActive = source.DefaultActiveSpiritUid ?? "";
        source.DefaultActiveSpiritUid = source.DefaultPartySlots.Contains(normalizedDefaultActive, StringComparer.Ordinal)
            ? normalizedDefaultActive
            : "";
        return source;
    }

    private static SpiritAdventureParty NormalizeParty(SpiritAdventureParty? party, SpiritCollectionDocument owner)
    {
        party ??= new SpiritAdventureParty();
        party.Version = 1;
        party.PartySlots = NormalizeSlots(party.PartySlots, owner);
        var normalizedActive = party.ActiveSpiritUid ?? "";
        party.ActiveSpiritUid = party.PartySlots.Contains(normalizedActive, StringComparer.Ordinal)
            ? normalizedActive
            : "";
        return party;
    }

    private static List<string> NormalizeSlots(IReadOnlyList<string>? slots, SpiritCollectionDocument owner)
    {
        var known = new HashSet<string>(owner.Instances.Select(item => item.SpiritUid), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = (slots ?? Array.Empty<string>()).Take(PartyCapacity)
            .Select(uid => (uid ?? "").Trim())
            .Select(uid => uid.Length > 0 && known.Contains(uid) && seen.Add(uid) ? uid : "")
            .ToList();
        while (result.Count < PartyCapacity) result.Add("");
        return result;
    }

    private static bool AddToFirstEmpty(IList<string> slots, string uid)
    {
        while (slots.Count < PartyCapacity) slots.Add("");
        if (slots.Contains(uid)) return false;
        for (var index = 0; index < Math.Min(PartyCapacity, slots.Count); index++)
        {
            if (string.IsNullOrWhiteSpace(slots[index]))
            {
                slots[index] = uid;
                return true;
            }
        }
        return false;
    }

    private static void CopyParty(SpiritAdventureParty source, SpiritAdventureParty target)
    {
        target.Version = source.Version;
        target.PartySlots = new List<string>(source.PartySlots);
        target.ActiveSpiritUid = source.ActiveSpiritUid;
    }

    private static string UniqueUid(SpiritCollectionDocument owner, string requested)
    {
        var candidate = (requested ?? "").Trim();
        if (candidate.Length == 0 || owner.Instances.Any(item => Same(item.SpiritUid, candidate)))
        {
            do candidate = Guid.NewGuid().ToString("N");
            while (owner.Instances.Any(item => Same(item.SpiritUid, candidate)));
        }
        return candidate;
    }

    private static void TrimCaptureHistory(SpiritCollectionDocument owner)
    {
        if (owner.ProcessedCaptureTokens.Count <= OperationHistoryLimit) return;
        foreach (var key in owner.ProcessedCaptureTokens.Keys.Take(owner.ProcessedCaptureTokens.Count - OperationHistoryLimit).ToArray())
        {
            owner.ProcessedCaptureTokens.Remove(key);
        }
    }

    private static void SaveUnlocked(SpiritCollectionDocument candidate)
    {
        if (store == null)
        {
            throw new InvalidOperationException("Spirit collection store has not been configured.");
        }
        store.Save(candidate);
    }

    private static bool ToggleFlag(string spiritUid, bool favorite)
    {
        lock (SyncRoot)
        {
            var candidate = CloneDocument(document);
            var instance = candidate.Instances.FirstOrDefault(item => Same(item.SpiritUid, spiritUid));
            if (instance == null) return false;
            if (favorite) instance.Favorite = !instance.Favorite;
            else instance.Locked = !instance.Locked;
            SaveUnlocked(candidate);
            document = candidate;
            return favorite ? instance.Favorite : instance.Locked;
        }
    }

    private static SpiritCollectionDocument CloneDocument(SpiritCollectionDocument source)
    {
        return new SpiritCollectionDocument
        {
            Version = source.Version,
            LegacyCardMigrationVersion = source.LegacyCardMigrationVersion,
            Instances = source.Instances.Select(item => item.Clone()).ToList(),
            DefaultPartySlots = new List<string>(source.DefaultPartySlots),
            DefaultActiveSpiritUid = source.DefaultActiveSpiritUid,
            ProcessedCaptureTokens = new Dictionary<string, string>(source.ProcessedCaptureTokens, StringComparer.Ordinal),
            ProcessedBattleTokens = new List<string>(source.ProcessedBattleTokens)
        };
    }

    private static bool Same(string left, string right) => string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);

    private static bool IsOwnedSource(string sourceModId)
    {
        return string.Equals(sourceModId, "BaseGame", StringComparison.OrdinalIgnoreCase)
               || string.Equals(sourceModId, "base-game", StringComparison.OrdinalIgnoreCase)
               || string.Equals(sourceModId, "Terrias", StringComparison.OrdinalIgnoreCase);
    }
}
