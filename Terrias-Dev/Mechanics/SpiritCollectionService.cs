using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class SpiritCollectionService
{
    public const int CurrentVersion = SpiritSystemContract.CollectionVersion;
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
            var loaded = store.Load();
            var requiresMigrationSave = loaded.Version < CurrentVersion;
            document = Normalize(loaded);
            if (requiresMigrationSave)
            {
                store.Save(CloneDocument(document));
                TerriasLog.Info("[SpiritCollection] migrated collection to version " + CurrentVersion + ".");
            }
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
            normalizedSnapshot.SpiritGuiyuanValue = 0;
            normalizedSnapshot.SpiritStarRank = 0;
            normalizedSnapshot.GuiyuanAllocationMagic = 0;
            normalizedSnapshot.GuiyuanAllocationSpirit = 0;
            normalizedSnapshot.GuiyuanAllocationLuck = 0;
            normalizedSnapshot.GuiyuanAllocationPerception = 0;
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
                Presentation = SpiritPresentationResolver.Capture(normalizedSnapshot),
                Level = 1,
                Experience = 0,
                Aptitude = Math.Max(aptitudeRoll.Minimum, Math.Min(aptitudeRoll.Maximum,
                    fixedAptitude ?? SpiritGrowthService.RollAptitude(identity.Profile, token + ":" + normalizedSnapshot.SpiritUid))),
                CapturedAt = string.IsNullOrWhiteSpace(normalizedSnapshot.CapturedAt)
                    ? DateTimeOffset.UtcNow.ToString("O")
                    : normalizedSnapshot.CapturedAt
            };
            SpiritTrainingService.InitializeCaptured(instance);
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

    public static bool EquipIntent(string spiritUid, int slotIndex, string intentId)
    {
        lock (SyncRoot)
        {
            var candidate = CloneDocument(document);
            var instance = candidate.Instances.FirstOrDefault(item => Same(item.SpiritUid, spiritUid));
            if (instance == null || !SpiritTrainingService.EquipIntent(instance, slotIndex, intentId)) return false;
            SaveUnlocked(candidate);
            document = candidate;
            return true;
        }
    }

    public static bool EquipPassive(string spiritUid, string passiveId)
    {
        lock (SyncRoot)
        {
            var candidate = CloneDocument(document);
            var instance = candidate.Instances.FirstOrDefault(item => Same(item.SpiritUid, spiritUid));
            if (instance == null || !SpiritTrainingService.EquipPassive(instance, passiveId)) return false;
            SaveUnlocked(candidate);
            document = candidate;
            return true;
        }
    }

    public static bool SetGuiyuanAllocations(string spiritUid, SpiritOriginVector allocations)
    {
        lock (SyncRoot)
        {
            var candidate = CloneDocument(document);
            var instance = candidate.Instances.FirstOrDefault(item => Same(item.SpiritUid, spiritUid));
            if (instance == null || !SpiritAscensionService.IsValidAllocation(allocations, instance.GuiyuanValue)) return false;
            instance.GuiyuanAllocations = allocations.Clone();
            SaveUnlocked(candidate);
            document = candidate;
            return true;
        }
    }

    public static SpiritGuiyuanResult Guiyuan(
        string targetUid,
        IReadOnlyList<string> donorUids,
        IReadOnlyCollection<string> forbiddenPartyUids)
    {
        lock (SyncRoot)
        {
            var candidate = CloneDocument(document);
            var target = candidate.Instances.FirstOrDefault(item => Same(item.SpiritUid, targetUid));
            if (target == null) return GuiyuanFailure("目标精灵不存在。");
            if (target.GuiyuanValue >= SpiritAscensionService.MaximumGuiyuanValue)
            {
                return GuiyuanFailure("该精灵已经达到五星。");
            }

            var forbidden = new HashSet<string>(forbiddenPartyUids ?? Array.Empty<string>(), StringComparer.Ordinal);
            var distinctUids = (donorUids ?? Array.Empty<string>())
                .Where(uid => !string.IsNullOrWhiteSpace(uid))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (distinctUids.Length == 0) return GuiyuanFailure("尚未选择归元材料。");
            if (distinctUids.Any(uid => Same(uid, target.SpiritUid))) return GuiyuanFailure("目标精灵不能作为归元材料。");

            var byUid = candidate.Instances.ToDictionary(item => item.SpiritUid, StringComparer.Ordinal);
            var donors = new List<SpiritInstance>();
            foreach (var uid in distinctUids)
            {
                if (!byUid.TryGetValue(uid, out var donor)) return GuiyuanFailure("所选材料已经不存在，请重新选择。");
                if (!Same(donor.SpeciesId, target.SpeciesId)) return GuiyuanFailure("只能选择 SpeciesId 相同的精灵作为材料。");
                if (donor.Locked) return GuiyuanFailure("已锁定的精灵不能作为归元材料。");
                if (forbidden.Contains(uid)) return GuiyuanFailure("编队中的精灵不能作为归元材料，请先放回仓库。");
                donors.Add(donor);
            }

            var preview = SpiritAscensionService.Preview(target, donors);
            if (preview.AppliedValue <= 0) return GuiyuanFailure("此次归元不会增加有效归元值。");
            target.GuiyuanValue = preview.ResultValue;
            target.GuiyuanAllocations = SpiritAscensionService.NormalizeAllocations(
                target.GuiyuanAllocations,
                target.GuiyuanValue);
            target.LoadoutHash = SpiritTrainingService.LoadoutHash(target);
            var donorSet = new HashSet<string>(distinctUids, StringComparer.Ordinal);
            candidate.Instances.RemoveAll(item => donorSet.Contains(item.SpiritUid));
            SaveUnlocked(candidate);
            document = candidate;
            return new SpiritGuiyuanResult
            {
                Success = true,
                Preview = preview,
                Target = target.Clone()
            };
        }
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
                snapshot.SpiritGuiyuanValue = 0;
                snapshot.SpiritStarRank = 0;
                snapshot.GuiyuanAllocationMagic = 0;
                snapshot.GuiyuanAllocationSpirit = 0;
                snapshot.GuiyuanAllocationLuck = 0;
                snapshot.GuiyuanAllocationPerception = 0;
                var identity = SpiritGrowthRegistry.ResolveIdentity(snapshot);
                var imported = new SpiritInstance
                {
                    SpiritUid = snapshot.SpiritUid,
                    SpeciesId = identity.SpeciesId,
                    ProfileId = identity.ProfileId,
                    Snapshot = snapshot,
                    Presentation = SpiritPresentationResolver.Capture(snapshot),
                    Level = 1,
                    Aptitude = SpiritGrowthService.LegacyAptitude,
                    CapturedAt = snapshot.CapturedAt
                };
                SpiritTrainingService.Normalize(imported, legacy: true);
                candidate.Instances.Add(imported);
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
        var sourceVersion = source.Version;
        var legacyTraining = sourceVersion < 5;
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
                item.Presentation ??= new SpiritLocalizedPresentation();
                if (sourceVersion < 6 || !HasPresentation(item.Presentation))
                {
                    item.Presentation = SpiritPresentationResolver.Capture(item.Snapshot);
                }
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
                item.GuiyuanValue = Math.Max(0, Math.Min(SpiritAscensionService.MaximumGuiyuanValue, item.GuiyuanValue));
                item.GuiyuanAllocations = SpiritAscensionService.NormalizeAllocations(
                    item.GuiyuanAllocations,
                    item.GuiyuanValue);
                SpiritTrainingService.Normalize(item, legacyTraining);
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

    private static SpiritGuiyuanResult GuiyuanFailure(string reason)
    {
        return new SpiritGuiyuanResult { Reason = reason ?? "归元失败。" };
    }

    private static bool HasPresentation(SpiritLocalizedPresentation presentation)
    {
        return presentation?.Name != null
               && (TerriasLocale.Supported.Any(presentation.Name.HasExact)
                   || !string.IsNullOrWhiteSpace(presentation.Name.LegacyFallback));
    }

    private static bool IsOwnedSource(string sourceModId)
    {
        return string.Equals(sourceModId, "BaseGame", StringComparison.OrdinalIgnoreCase)
               || string.Equals(sourceModId, "base-game", StringComparison.OrdinalIgnoreCase)
               || string.Equals(sourceModId, "Terrias", StringComparison.OrdinalIgnoreCase);
    }
}
