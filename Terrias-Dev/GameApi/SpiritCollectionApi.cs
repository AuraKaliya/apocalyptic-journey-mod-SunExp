using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AuraGameData.Shared;
using AuraGameData.Shared.GameApi;
using AuraShared.Core;
using Data.Save;
using Newtonsoft.Json;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Terrias.Dll.Network;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.GameApi;

public static class SpiritCollectionApi
{
    private static readonly object SyncRoot = new();
    private static bool initialized;
    private static ModConfig? configuredMod;
    private static string boundProfileKey = "";
    private static readonly SpiritInitialRosterAttemptLedger InitialRosterAttempts = new();
    private static bool initialRosterCatalogListenerRegistered;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        configuredMod = modConfig ?? throw new ArgumentNullException(nameof(modConfig));
        EnsureInitialRosterCatalogListener();
        initialized = true;
        EnsureProfileBound();
    }

    public static SpiritCollectionDocument Collection()
    {
        return EnsureProfileBound()
            ? SpiritCollectionService.Snapshot()
            : new SpiritCollectionDocument();
    }

    public static SpiritInstance? Find(string uid)
    {
        return EnsureProfileBound() ? SpiritCollectionService.Find(uid) : null;
    }

    public static SpiritAdventureParty DefaultParty()
    {
        return EnsureProfileBound() ? SpiritCollectionService.DefaultParty() : EmptyParty();
    }

    public static SpiritAdventureParty CurrentParty()
    {
        if (!EnsureProfileBound())
        {
            return EmptyParty();
        }

        try
        {
            var party = SpiritAdventurePartySessionService.CurrentOrBegin(
                CurrentJourneyId(),
                LocalPlayerId(),
                SpiritCollectionService.DefaultParty());
            var normalized = SpiritCollectionService.NormalizeParty(party);
            SaveCurrentParty(normalized);
            return normalized;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[SpiritCollection] current adventure party recovery failed: " + ex.Message);
            return SpiritCollectionService.DefaultParty();
        }
    }

    public static SpiritAdventureParty BeginAdventure()
    {
        if (!EnsureProfileBound())
        {
            TerriasLog.Warn("[SpiritCollection] adventure party deferred: stable player identity is unavailable.");
            return EmptyParty();
        }

        MigrateLegacyCards();
        var party = SpiritCollectionService.NormalizeParty(
            SpiritAdventurePartySessionService.EnterJourney(
                CurrentJourneyId(),
                LocalPlayerId(),
                SpiritCollectionService.DefaultParty()));
        SaveCurrentParty(party);
        TerriasLog.Info("[SpiritCollection] local adventure party ready; slots="
                        + party.PartySlots.Count(uid => !string.IsNullOrWhiteSpace(uid))
                        + ", active="
                        + (!string.IsNullOrWhiteSpace(party.ActiveSpiritUid))
                        + ".");
        return party;
    }

    public static SpiritCaptureRecordResult RecordCapture(CapturedEnemySnapshot snapshot, string operationToken, int? aptitude = null)
    {
        if (!EnsureProfileBound())
        {
            return new SpiritCaptureRecordResult { Reason = "玩家档案尚未就绪，请稍后重试。" };
        }

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
        if (!EnsureProfileBound()) return false;
        var party = CurrentParty();
        var normalizedUid = uid ?? "";
        party.ActiveSpiritUid = party.PartySlots.Contains(normalizedUid, StringComparer.Ordinal) ? normalizedUid : "";
        SaveCurrentParty(party);
        return string.Equals(party.ActiveSpiritUid, uid ?? "", StringComparison.Ordinal);
    }

    public static bool ConfigureDefaultPartySlot(int slot, string uid)
    {
        if (!EnsureProfileBound()) return false;
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
        if (!EnsureProfileBound()) return false;
        var party = SpiritCollectionService.DefaultParty();
        if (!party.Remove(uid)) return false;
        return SpiritCollectionService.SetDefaultParty(party.PartySlots, party.ActiveSpiritUid);
    }

    public static bool RemoveFromCurrentAdventureParty(string uid)
    {
        if (!EnsureProfileBound()) return false;
        var party = CurrentParty();
        if (!party.Remove(uid))
        {
            return false;
        }

        SaveCurrentParty(party);
        return true;
    }

    public static bool AddToDefaultParty(string uid)
    {
        if (!EnsureProfileBound()) return false;
        var party = SpiritCollectionService.DefaultParty();
        uid = (uid ?? "").Trim();
        if (uid.Length == 0 || SpiritCollectionService.Find(uid) == null) return false;
        if (party.PartySlots.Contains(uid, StringComparer.Ordinal)) return true;
        var slot = party.PartySlots.FindIndex(string.IsNullOrWhiteSpace);
        if (slot < 0) return false;
        party.PartySlots[slot] = uid;
        return SpiritCollectionService.SetDefaultParty(party.PartySlots, party.ActiveSpiritUid);
    }

    public static bool SetDefaultActive(string uid)
    {
        if (!EnsureProfileBound()) return false;
        var party = SpiritCollectionService.DefaultParty();
        var normalizedUid = uid ?? "";
        party.ActiveSpiritUid = party.PartySlots.Contains(normalizedUid, StringComparer.Ordinal) ? normalizedUid : "";
        return SpiritCollectionService.SetDefaultParty(party.PartySlots, party.ActiveSpiritUid ?? "");
    }

    public static SpiritOriginVector Origins(SpiritInstance instance)
    {
        return SpiritArtifactStatService.AddOrigins(
            SpiritAscensionService.EffectiveOrigins(instance),
            ArtifactBattleFor(instance));
    }

    public static CompanionStats Stats(SpiritInstance instance)
    {
        var profile = SpiritGrowthRegistry.Resolve(instance);
        var artifacts = ArtifactBattleFor(instance);
        return SpiritArtifactStatService.ApplyFlatBattleStats(
            SpiritAscensionService.ApplyStarBonus(SpiritGrowthService.BattleStats(
                    profile,
                    SpiritArtifactStatService.AddOrigins(SpiritAscensionService.EffectiveOrigins(instance), artifacts),
                    SpiritIntentRegistry.ProfileForIdentity(instance.ProfileId, instance.Snapshot.ProfileKey),
                    instance.Speed),
                SpiritAscensionService.StarRankFor(instance.GuiyuanValue)),
            artifacts);
    }

    public static SpiritGrowthViewSnapshot GrowthView(SpiritInstance instance)
        => SpiritGrowthQueryService.Build(instance, ArtifactBattleFor(instance));

    public static SpiritTrainingViewSnapshot TrainingView(SpiritInstance instance) => SpiritTrainingService.BuildView(instance);

    private static SpiritArtifactBattleSnapshot ArtifactBattleFor(SpiritInstance instance)
    {
        if (instance == null || !EnsureProfileBound() || !SpiritArtifactRegistry.IsReady)
            return new SpiritArtifactBattleSnapshot();
        var collection = SpiritCollectionService.Snapshot();
        var persisted = collection.Instances.FirstOrDefault(value =>
            string.Equals(value.SpiritUid, instance.SpiritUid, StringComparison.Ordinal)) ?? instance;
        return SpiritArtifactLoadoutResolver.Resolve(collection, persisted).Battle;
    }

    public static bool EquipIntent(string uid, int slotIndex, string intentId)
        => EnsureProfileBound() && SpiritCollectionService.EquipIntent(uid, slotIndex, intentId);

    public static bool EquipPassive(string uid, string passiveId)
        => EnsureProfileBound() && SpiritCollectionService.EquipPassive(uid, passiveId);

    public static bool SetGuiyuanAllocations(string uid, SpiritOriginVector allocations)
        => EnsureProfileBound() && SpiritCollectionService.SetGuiyuanAllocations(uid, allocations);

    public static SpiritGuiyuanResult Guiyuan(string targetUid, IReadOnlyList<string> donorUids)
    {
        if (!EnsureProfileBound()) return new SpiritGuiyuanResult { Reason = "玩家档案尚未就绪，请稍后重试。" };
        var forbidden = new HashSet<string>(StringComparer.Ordinal);
        foreach (var uid in DefaultParty().PartySlots.Where(uid => !string.IsNullOrWhiteSpace(uid))) forbidden.Add(uid);
        foreach (var uid in CurrentParty().PartySlots.Where(uid => !string.IsNullOrWhiteSpace(uid))) forbidden.Add(uid);
        try
        {
            return SpiritCollectionService.Guiyuan(targetUid, donorUids, forbidden);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[SpiritCollection] guiyuan persistence failed: " + ex.Message);
            return new SpiritGuiyuanResult { Reason = "归元档案写入失败，未消耗任何精灵。" };
        }
    }

    public static bool ToggleFavorite(string uid) => EnsureProfileBound() && SpiritCollectionService.ToggleFavorite(uid);

    public static bool ToggleLocked(string uid) => EnsureProfileBound() && SpiritCollectionService.ToggleLocked(uid);

    public static bool SetElement(string uid, string elementId, string source = SpiritElementService.ExplicitOverrideSource)
        => EnsureProfileBound() && SpiritCollectionService.SetElement(uid, elementId, source);

    public static IReadOnlyList<SpiritExperienceResult> GrantBattleExperience(
        IReadOnlyList<string> partyUids,
        string activeUid,
        int baseExperience,
        string battleToken)
    {
        if (!EnsureProfileBound()) return Array.Empty<SpiritExperienceResult>();
        return SpiritCollectionService.GrantBattleExperience(partyUids, activeUid, baseExperience, battleToken);
    }

    public static int MigrateLegacyCards()
    {
        if (!EnsureProfileBound())
        {
            return 0;
        }

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
        if (!EnsureProfileBound())
        {
            return;
        }

        SpiritAdventurePartySessionService.SaveParty(
            CurrentJourneyId(),
            LocalPlayerId(),
            SpiritCollectionService.NormalizeParty(party));
    }

    private static bool EnsureProfileBound()
    {
        lock (SyncRoot)
        {
            if (!initialized || configuredMod == null)
            {
                return false;
            }

            var stableKey = StableProfileKey();
            if (string.IsNullOrWhiteSpace(stableKey))
            {
                return boundProfileKey.Length > 0;
            }

            if (string.Equals(boundProfileKey, stableKey, StringComparison.Ordinal))
            {
                TryGrantInitialRoster(stableKey);
                return true;
            }

            try
            {
                var legacyKeys = SpiritProfileBindingPolicy.LegacyProfileKeys(
                    RuntimeMember(Singleton<GameRuntimeData>.Instance, "savePath"));
                SpiritCollectionService.Configure(new SpiritSidecarProfileStore(
                    configuredMod.DirectoryName,
                    stableKey,
                    legacyKeys));
                SpiritAdventurePartySessionService.Configure(new SpiritAdventurePartySidecarStore(
                    configuredMod.DirectoryName,
                    stableKey));
                if (!string.IsNullOrWhiteSpace(boundProfileKey)
                    && !string.Equals(boundProfileKey, stableKey, StringComparison.Ordinal)
                    && !InitialRosterAttempts.IsTerminal(boundProfileKey))
                {
                    InitialRosterAttempts.MarkBlocked(
                        boundProfileKey,
                        "profile binding was superseded for the current process lifecycle");
                }
                boundProfileKey = stableKey;
                TerriasLog.Info("[SpiritCollection] profile bound to stable player=" + stableKey + ".");
            }
            catch (Exception ex)
            {
                TerriasLog.Warn("[SpiritCollection] stable profile bind failed for " + stableKey + ": " + ex.Message);
                return false;
            }

            TryGrantInitialRoster(stableKey);
            return true;
        }
    }

    private static void TryGrantInitialRoster(string stableProfileKey)
    {
        lock (SyncRoot)
        {
            if (configuredMod == null
                || string.IsNullOrWhiteSpace(stableProfileKey)
                || !string.Equals(boundProfileKey, stableProfileKey, StringComparison.Ordinal))
            {
                return;
            }

            if (SpiritCollectionService.AppliedInitialRosterGrantVersion()
                >= SpiritSystemContract.InitialRosterGrantVersion)
            {
                InitialRosterAttempts.MarkCompleted(stableProfileKey, "already-applied");
                return;
            }
            if (InitialRosterAttempts.IsTerminal(stableProfileKey))
            {
                return;
            }

            var catalog = AuraGameDataHostApi.AcquireSnapshot();
            var catalogEpoch = catalog.Version.Epoch;
            if (!catalog.Version.NativeReady)
            {
                SetInitialRosterPending(
                    stableProfileKey,
                    catalogEpoch,
                    "native game-data catalog is not ready");
                return;
            }
            if (!InitialRosterAttempts.TryBeginReadyAttempt(stableProfileKey, catalogEpoch))
            {
                return;
            }

            if (!TerriasModConfigurationApi.TryGetBoolean(
                    configuredMod,
                    SpiritSystemContract.InitialRosterConfigurationKey,
                    out var enabled,
                    out var diagnostic))
            {
                SetInitialRosterPending(stableProfileKey, catalogEpoch, diagnostic);
                return;
            }
            if (!enabled)
            {
                InitialRosterAttempts.MarkDisabled(stableProfileKey, "configuration disabled");
                TerriasLog.Info("[SpiritInitialRoster] disabled for profile=" + stableProfileKey + ".");
                return;
            }

            try
            {
                var profiles = SpiritGrowthRegistry.RegisteredProfiles();
                if (profiles.Count != SpiritSystemContract.InitialRosterProfileCount)
                {
                    SetInitialRosterPending(
                        stableProfileKey,
                        catalogEpoch,
                        "expected " + SpiritSystemContract.InitialRosterProfileCount
                        + " profiles but registry exposed " + profiles.Count
                        + "; " + SpiritGrowthRegistry.LastLoadDiagnostic);
                    return;
                }

                var seeds = new List<SpiritInitialRosterSeed>(profiles.Count);
                foreach (var profile in profiles)
                {
                    var match = profile.Match ?? new SpiritSpeciesGrowthMatch();
                    var inspection = EnemyCatalogApi.InspectConfiguredProfile(
                        match.SourceModId,
                        match.EnemyId,
                        match.VariantId,
                        SpiritSystemContract.InitialRosterCaptureOrigin);
                    if (!inspection.Eligible || inspection.Snapshot == null)
                    {
                        SetInitialRosterPending(
                            stableProfileKey,
                            catalogEpoch,
                            "profile " + profile.ProfileId + ": " + inspection.Reason);
                        return;
                    }

                    seeds.Add(new SpiritInitialRosterSeed
                    {
                        ProfileId = profile.ProfileId,
                        Snapshot = inspection.Snapshot
                    });
                }

                var result = SpiritCollectionService.GrantInitialRoster(seeds);
                if (!result.Success)
                {
                    if (result.Reason.IndexOf("损坏", StringComparison.Ordinal) >= 0)
                    {
                        InitialRosterAttempts.MarkBlocked(stableProfileKey, result.Reason);
                    }
                    else
                    {
                        InitialRosterAttempts.MarkPending(stableProfileKey, result.Reason);
                    }
                    TerriasLog.Warn("[SpiritInitialRoster] grant failed for profile="
                                    + stableProfileKey + ": " + result.Reason + ".");
                    return;
                }

                var grantState = result.AlreadyGranted ? "already-applied" : "committed";
                InitialRosterAttempts.MarkCompleted(stableProfileKey, grantState);
                TerriasLog.Info("[SpiritInitialRoster] grantState="
                                + grantState
                                + ", profile=" + stableProfileKey
                                + ", granted=" + result.GrantedCount
                                + ", version=" + SpiritSystemContract.InitialRosterGrantVersion
                                + ", catalogEpoch=" + catalogEpoch
                                + ".");
            }
            catch (Exception ex)
            {
                SetInitialRosterPending(stableProfileKey, catalogEpoch, ex.Message);
            }
        }
    }

    internal static bool EnsureProfileBoundForArtifact() => EnsureProfileBound();

    private static void EnsureInitialRosterCatalogListener()
    {
        if (initialRosterCatalogListenerRegistered)
        {
            return;
        }

        AuraGameDataCatalogRuntime.SnapshotChanged += OnGameDataCatalogChanged;
        initialRosterCatalogListenerRegistered = true;
    }

    private static void OnGameDataCatalogChanged(AuraGameDataCatalogVersion version)
    {
        if (!version.NativeReady)
        {
            return;
        }

        string[] pending;
        lock (SyncRoot)
        {
            pending = InitialRosterAttempts.PendingProfileKeys().ToArray();
        }

        foreach (var profileKey in pending)
        {
            var capturedKey = profileKey;
            var scheduled = AuraSharedFrameScheduler.RunOnceNextFrame(new AuraSharedFrameActionRequest
            {
                OwnerId = TerriasIds.ModId,
                Key = "SpiritInitialRoster." + capturedKey + "." + version.Epoch,
                Source = "SpiritInitialRoster.GameDataReady",
                Action = () => TryGrantInitialRoster(capturedKey)
            });
            if (!scheduled)
            {
                TryGrantInitialRoster(capturedKey);
            }
        }
    }

    private static void SetInitialRosterPending(
        string profileKey,
        long catalogEpoch,
        string reason)
    {
        var changed = InitialRosterAttempts.MarkPending(profileKey, reason);
        if (changed)
        {
            TerriasLog.Warn("[SpiritInitialRoster] pending owner=SpiritCollectionApi, profile="
                            + profileKey
                            + ", catalogEpoch=" + catalogEpoch
                            + ", drain=next-native-catalog-generation-or-process-start"
                            + ": " + InitialRosterAttempts.Snapshot(profileKey).Reason + ".");
        }
    }

    private static SpiritAdventureParty EmptyParty()
    {
        return new SpiritAdventureParty
        {
            PartySlots = Enumerable.Repeat("", SpiritCollectionService.PartyCapacity).ToList(),
            ActiveSpiritUid = ""
        };
    }

    private static string CurrentJourneyId()
    {
        try
        {
            var save = GameSaveManager.GetNowSave();
            if (save != null)
            {
                return "spirit-journey-v1|"
                       + (save.modeType ?? "") + "|"
                       + (save.Seed ?? "") + "|"
                       + (save.CreatedTime ?? "") + "|"
                       + (save.Name ?? "");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[SpiritCollection] journey identity fallback: " + ex.Message);
        }

        return "spirit-journey-pending|" + (boundProfileKey.Length > 0 ? boundProfileKey : "unbound");
    }

    private static string LocalPlayerId()
    {
        var networkId = TerriasNetworkRuntime.LocalPlayerId();
        if (!string.IsNullOrWhiteSpace(networkId))
        {
            return networkId;
        }

        var runtimeId = RuntimeMember(Singleton<GameRuntimeData>.Instance, "PlayerId");
        return !string.IsNullOrWhiteSpace(runtimeId) ? runtimeId : boundProfileKey;
    }

    private sealed class SpiritSidecarProfileStore : ISpiritCollectionStore, ISpiritInitialRosterGrantGuard
    {
        private readonly string modDirectory;
        private readonly string profileKey;
        private readonly IReadOnlyList<string> legacyProfileKeys;
        private ProfileLoadState loadState;

        public SpiritSidecarProfileStore(
            string modDirectory,
            string profileKey,
            IReadOnlyList<string> legacyProfileKeys)
        {
            this.modDirectory = modDirectory;
            this.profileKey = profileKey;
            this.legacyProfileKeys = legacyProfileKeys ?? Array.Empty<string>();
        }

        public bool CanGrantInitialRoster => loadState != ProfileLoadState.Invalid;

        public string InitialRosterGrantBlockReason => loadState == ProfileLoadState.Invalid
            ? "精灵档案损坏且无法从备份恢复，已阻止自动初始发放以避免覆盖原文件。"
            : "";

        public SpiritCollectionDocument Load()
        {
            var path = ProfilePath();
            if (!File.Exists(path))
            {
                var recovered = TryRecoverLegacy(path);
                loadState = recovered == null ? ProfileLoadState.New : ProfileLoadState.LegacyRecovered;
                return recovered ?? new SpiritCollectionDocument();
            }
            try
            {
                var loaded = JsonConvert.DeserializeObject<SpiritCollectionDocument>(File.ReadAllText(path))
                             ?? new SpiritCollectionDocument();
                loadState = ProfileLoadState.Existing;
                return loaded;
            }
            catch (Exception ex)
            {
                var recovered = TryLoadBackup(path);
                if (recovered != null)
                {
                    loadState = ProfileLoadState.BackupRecovered;
                    TerriasLog.Warn("[SpiritCollection] recovered profile from backup " + path + ": " + ex.Message);
                    return recovered;
                }
                try
                {
                    var invalidBackup = path + ".invalid.bak";
                    if (!File.Exists(invalidBackup)) File.Copy(path, invalidBackup, overwrite: false);
                }
                catch { }
                loadState = ProfileLoadState.Invalid;
                TerriasLog.Warn("[SpiritCollection] preserved but ignored invalid profile " + path + ": " + ex.Message);
                return new SpiritCollectionDocument();
            }
        }

        public void Save(SpiritCollectionDocument document)
        {
            var path = ProfilePath();
            AuraSharedFileStore.WriteAllText(
                TerriasIds.ModId,
                path,
                JsonConvert.SerializeObject(document, Formatting.Indented),
                createBackup: true);
            loadState = ProfileLoadState.Existing;
        }

        private string ProfilePath()
        {
            return Path.Combine(
                OwnerDataDirectory(modDirectory, TerriasIds.SpiritProfileDirectory),
                profileKey + ".json");
        }

        private SpiritCollectionDocument? TryRecoverLegacy(string stablePath)
        {
            var directory = Path.GetDirectoryName(stablePath) ?? "";
            foreach (var legacyKey in legacyProfileKeys
                         .Where(key => !string.IsNullOrWhiteSpace(key))
                         .Distinct(StringComparer.Ordinal)
                         .Where(key => !string.Equals(key, profileKey, StringComparison.Ordinal)))
            {
                var legacyPath = Path.Combine(directory, legacyKey + ".json");
                if (!File.Exists(legacyPath))
                {
                    continue;
                }

                SpiritCollectionDocument? legacy;
                try
                {
                    legacy = JsonConvert.DeserializeObject<SpiritCollectionDocument>(File.ReadAllText(legacyPath));
                }
                catch (Exception ex)
                {
                    TerriasLog.Warn("[SpiritCollection] ignored invalid legacy profile " + legacyPath + ": " + ex.Message);
                    continue;
                }

                if (!SpiritProfileBindingPolicy.ShouldRecoverLegacy(File.Exists(stablePath), legacy))
                {
                    continue;
                }

                Save(legacy!);
                TerriasLog.Info("[SpiritCollection] recovered legacy profile "
                                + legacyKey
                                + " into stable player="
                                + profileKey
                                + "; legacy file retained.");
                return legacy;
            }

            return null;
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

        private enum ProfileLoadState
        {
            Unknown,
            New,
            Existing,
            LegacyRecovered,
            BackupRecovered,
            Invalid
        }
    }

    private sealed class SpiritAdventurePartySidecarStore : ISpiritAdventurePartySessionStore
    {
        private readonly string modDirectory;
        private readonly string profileKey;

        public SpiritAdventurePartySidecarStore(string modDirectory, string profileKey)
        {
            this.modDirectory = modDirectory;
            this.profileKey = profileKey;
        }

        public SpiritAdventurePartySessionDocument Load()
        {
            var path = SessionPath();
            if (!File.Exists(path))
            {
                return new SpiritAdventurePartySessionDocument();
            }

            try
            {
                return JsonConvert.DeserializeObject<SpiritAdventurePartySessionDocument>(File.ReadAllText(path))
                       ?? new SpiritAdventurePartySessionDocument();
            }
            catch (Exception ex)
            {
                var recovered = TryLoadBackup(path);
                if (recovered != null)
                {
                    TerriasLog.Warn("[SpiritCollection] recovered adventure party from backup " + path + ": " + ex.Message);
                    return recovered;
                }

                try
                {
                    var invalidBackup = path + ".invalid.bak";
                    if (!File.Exists(invalidBackup)) File.Copy(path, invalidBackup, overwrite: false);
                }
                catch
                {
                }

                TerriasLog.Warn("[SpiritCollection] ignored invalid adventure party " + path + ": " + ex.Message);
                return new SpiritAdventurePartySessionDocument();
            }
        }

        public void Save(SpiritAdventurePartySessionDocument document)
        {
            var path = SessionPath();
            AuraSharedFileStore.WriteAllText(
                TerriasIds.ModId,
                path,
                JsonConvert.SerializeObject(document, Formatting.Indented),
                createBackup: true);
        }

        private string SessionPath()
        {
            return Path.Combine(OwnerDataDirectory(modDirectory, TerriasIds.SpiritAdventureSessionDirectory), profileKey + ".json");
        }

        private static SpiritAdventurePartySessionDocument? TryLoadBackup(string path)
        {
            try
            {
                var backup = path + ".bak";
                return File.Exists(backup)
                    ? JsonConvert.DeserializeObject<SpiritAdventurePartySessionDocument>(File.ReadAllText(backup))
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }

    private static string OwnerDataDirectory(string modDirectory, string systemName)
    {
        var modsDirectory = Path.GetDirectoryName(modDirectory) ?? modDirectory;
        var gameDirectory = Path.GetDirectoryName(modsDirectory) ?? modsDirectory;
        var dataRoot = string.IsNullOrWhiteSpace(AuraSharedPaths.ModsDataDirectory)
            ? Path.Combine(gameDirectory, AuraSharedPaths.DefaultDataRootDirectoryName)
            : AuraSharedPaths.ModsDataDirectory;
        var sharedRoot = string.IsNullOrWhiteSpace(AuraSharedPaths.RootDirectory)
            ? Path.Combine(dataRoot, AuraSharedPaths.DefaultSharedDirectoryName)
            : AuraSharedPaths.RootDirectory;
        return string.IsNullOrWhiteSpace(AuraSharedPaths.RootDirectory)
            ? Path.Combine(sharedRoot, "Data", "Owners", TerriasIds.ModId, systemName)
            : AuraSharedPaths.OwnerSystemDataDirectory(TerriasIds.ModId, systemName);
    }

    private static string StableProfileKey()
    {
        return SpiritProfileBindingPolicy.ResolveStableProfileKey(
            TerriasNetworkRuntime.LocalPlayerId(),
            RuntimeMember(Singleton<GameRuntimeData>.Instance, "PlayerId"));
    }

    private static string RuntimeMember(object? target, string name)
    {
        if (target == null) return "";
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var type = target.GetType();
        return Convert.ToString(type.GetProperty(name, flags)?.GetValue(target)
                                ?? type.GetField(name, flags)?.GetValue(target)) ?? "";
    }
}
