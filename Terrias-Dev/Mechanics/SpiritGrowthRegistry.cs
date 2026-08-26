using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.Mechanics;

public static class SpiritGrowthRegistry
{
    private static readonly object SyncRoot = new();
    private static SpiritGrowthRegistryDocument document = BuiltInDocument();
    private static Dictionary<string, SpiritSpeciesGrowthProfile> profilesById = new(StringComparer.Ordinal);
    private static string registryHash = "00000000";
    private static string lastLoadDiagnostic = "not-loaded";

    static SpiritGrowthRegistry()
    {
        SetDocument(document);
    }

    public static string RegistryHash
    {
        get
        {
            lock (SyncRoot) return registryHash;
        }
    }

    public static int DefaultMaxLevel
    {
        get
        {
            lock (SyncRoot) return document.Defaults.MaxLevel;
        }
    }

    public static string LastLoadDiagnostic
    {
        get
        {
            lock (SyncRoot) return lastLoadDiagnostic;
        }
    }

    public static void Load(ModConfig modConfig)
    {
        lock (SyncRoot)
        {
            var path = Path.Combine(modConfig.DirectoryName, TerriasIds.SpiritGrowthRegistryFile);
            try
            {
                if (!File.Exists(path))
                {
                    SetDocument(BuiltInDocument());
                    lastLoadDiagnostic = "missing:" + path;
                    TerriasLog.Warn("[SpiritGrowthRegistry] missing registry; using typed defaults and deterministic external fallbacks.");
                    return;
                }

                var loaded = AuraSharedJson.Deserialize<SpiritGrowthRegistryDocument>(File.ReadAllText(path))
                             ?? throw new InvalidDataException("deserialized registry is null");
                if (loaded.SchemaVersion == 1)
                {
                    loaded = MigrateSchema1(loaded);
                    TerriasLog.Warn("[SpiritGrowthRegistry] loaded schema 1 through the in-memory compatibility migration.");
                }
                else if (loaded.SchemaVersion == 2)
                {
                    loaded = MigrateSchema2(loaded);
                    TerriasLog.Warn("[SpiritGrowthRegistry] loaded schema 2 through the in-memory element assignment migration.");
                }
                else if (loaded.SchemaVersion != SpiritSystemContract.GrowthRegistrySchemaVersion)
                {
                    throw new InvalidDataException("unsupported schemaVersion=" + loaded.SchemaVersion
                                                   + "; expected 1, 2, or " + SpiritSystemContract.GrowthRegistrySchemaVersion);
                }

                SetDocument(NormalizeAndValidate(loaded));
                lastLoadDiagnostic = "ready:" + path;
                TerriasLog.Info(
                    "[SpiritGrowthRegistry] registryState=ready schema=" + SpiritSystemContract.GrowthRegistrySchemaVersion
                    + " profiles=" + document.Profiles.Count
                    + ", species=" + document.Profiles.Select(profile => profile.SpeciesId).Distinct(StringComparer.Ordinal).Count()
                    + ", hash=" + registryHash
                    + ", path=" + path);
            }
            catch (Exception ex)
            {
                SetDocument(BuiltInDocument());
                lastLoadDiagnostic = "invalid:" + ex.Message;
                TerriasLog.Warn("[SpiritGrowthRegistry] invalid registry; using typed defaults and deterministic fallbacks: " + ex.Message);
            }
        }
    }

    public static SpiritProfileIdentity ResolveIdentity(CapturedEnemySnapshot snapshot)
    {
        snapshot ??= new CapturedEnemySnapshot();
        lock (SyncRoot)
        {
            var profile = FindByMatch(snapshot);
            return profile == null
                ? FallbackIdentity(snapshot, "", "")
                : new SpiritProfileIdentity
                {
                    SpeciesId = profile.SpeciesId,
                    ProfileId = profile.ProfileId,
                    Profile = profile.Clone()
                };
        }
    }

    public static SpiritSpeciesGrowthProfile Resolve(CapturedEnemySnapshot snapshot)
    {
        return ResolveIdentity(snapshot).Profile;
    }

    public static SpiritSpeciesGrowthProfile Resolve(SpiritInstance instance)
    {
        instance ??= new SpiritInstance();
        lock (SyncRoot)
        {
            if (!string.IsNullOrWhiteSpace(instance.ProfileId)
                && profilesById.TryGetValue(instance.ProfileId.Trim(), out var fixedProfile))
            {
                return fixedProfile.Clone();
            }

            if (!string.IsNullOrWhiteSpace(instance.ProfileId))
            {
                return FallbackIdentity(instance.Snapshot, instance.SpeciesId, instance.ProfileId).Profile;
            }

            var matched = FindByMatch(instance.Snapshot);
            return (matched ?? FallbackIdentity(instance.Snapshot, "", "").Profile).Clone();
        }
    }

    public static bool TryFind(string profileId, out SpiritSpeciesGrowthProfile profile)
    {
        lock (SyncRoot)
        {
            if (profilesById.TryGetValue((profileId ?? "").Trim(), out var found))
            {
                profile = found.Clone();
                return true;
            }

            profile = new SpiritSpeciesGrowthProfile();
            return false;
        }
    }

    public static SpiritSpeciesTier TierFor(CapturedEnemySnapshot snapshot)
    {
        return ParseTier(Resolve(snapshot).Tier, TierFromRarity(snapshot?.Rarity ?? 1));
    }

    public static SpiritSpeciesTier TierFor(SpiritInstance instance)
    {
        return ParseTier(Resolve(instance).Tier, TierFromRarity(instance?.Snapshot?.Rarity ?? 1));
    }

    public static SpiritSpeciesTier TierFromRarity(int rarity)
    {
        return rarity >= 3 ? SpiritSpeciesTier.Boss : rarity == 2 ? SpiritSpeciesTier.Elite : SpiritSpeciesTier.Normal;
    }

    public static string FormLabel(SpiritSpeciesGrowthProfile profile)
    {
        lock (SyncRoot)
        {
            var key = profile?.FormLabelKey ?? "";
            return key.Length > 0 && document.FormLabels.TryGetValue(key, out var label) ? label : "";
        }
    }

    public static SpiritLevelCurveDefinition LevelCurveFor(SpiritSpeciesGrowthProfile profile)
    {
        lock (SyncRoot)
        {
            var id = First(profile?.LevelCurveId, document.Defaults.LevelCurveId);
            return Clone(document.LevelCurves.First(curve => Same(curve.Id, id)));
        }
    }

    public static SpiritAptitudeRollProfile AptitudeRollFor(SpiritSpeciesGrowthProfile profile)
    {
        lock (SyncRoot)
        {
            var id = First(profile?.AptitudeRollProfileId, document.Defaults.AptitudeRollProfileId);
            return Clone(document.AptitudeRollProfiles.First(curve => Same(curve.Id, id)));
        }
    }

    public static SpiritAptitudeCurveDefinition AptitudeCurveFor(SpiritSpeciesGrowthProfile profile)
    {
        lock (SyncRoot)
        {
            var id = First(profile?.AptitudeCurveId, document.Defaults.AptitudeCurveId);
            return Clone(document.AptitudeCurves.First(curve => Same(curve.Id, id)));
        }
    }

    public static SpiritExperienceCurveDefinition ExperienceCurveFor(SpiritSpeciesGrowthProfile profile)
    {
        lock (SyncRoot)
        {
            var id = First(profile?.ExperienceCurveId, document.Defaults.ExperienceCurveId);
            return Clone(document.ExperienceCurves.First(curve => Same(curve.Id, id)));
        }
    }

    public static SpiritBattleConversionDefinition BattleConversionFor(SpiritSpeciesGrowthProfile profile)
    {
        lock (SyncRoot)
        {
            var id = First(profile?.BattleConversionId, document.Defaults.BattleConversionId);
            return Clone(document.BattleConversions.First(curve => Same(curve.Id, id)));
        }
    }

    public static SpiritRadarScaleSet RadarScaleFor(SpiritSpeciesGrowthProfile profile)
    {
        lock (SyncRoot)
        {
            var id = First(profile?.RadarScaleId, document.Defaults.RadarScaleId);
            return Clone(document.RadarScaleSets.First(curve => Same(curve.Id, id)));
        }
    }

    private static SpiritSpeciesGrowthProfile? FindByMatch(CapturedEnemySnapshot snapshot)
    {
        var source = NormalizeSourceModId(snapshot?.SourceModId, snapshot?.EnemyId);
        var enemies = IdentityCandidates(snapshot?.EnemyId ?? "");
        var rawVariant = string.IsNullOrWhiteSpace(snapshot?.VariantId) ? snapshot?.EnemyId ?? "" : snapshot!.VariantId;
        var variants = IdentityCandidates(rawVariant);
        SpiritSpeciesGrowthProfile? best = null;
        var bestScore = int.MaxValue;
        foreach (var profile in document.Profiles)
        {
            var match = profile.Match ?? new SpiritSpeciesGrowthMatch();
            var sourceScore = Same(NormalizeSourceModId(match.SourceModId, match.EnemyId), source) ? 0 : match.SourceModId == "*" ? 100 : -1;
            if (sourceScore < 0) continue;
            var enemyIndex = IndexOf(enemies, match.EnemyId);
            if (enemyIndex < 0) continue;
            var variantIndex = match.VariantId == "*" ? 50 : IndexOf(variants, match.VariantId);
            if (variantIndex < 0) continue;
            var score = sourceScore + enemyIndex * 4 + variantIndex;
            if (score >= bestScore) continue;
            best = profile;
            bestScore = score;
        }

        return best;
    }

    private static SpiritProfileIdentity FallbackIdentity(CapturedEnemySnapshot snapshot, string fixedSpeciesId, string fixedProfileId)
    {
        snapshot ??= new CapturedEnemySnapshot();
        var tier = TierFromRarity(snapshot.Rarity);
        var source = NormalizeSourceModId(snapshot.SourceModId, snapshot.EnemyId);
        var enemy = NormalizeRuntimeId(snapshot.EnemyId);
        var variant = NormalizeRuntimeId(string.IsNullOrWhiteSpace(snapshot.VariantId) ? snapshot.EnemyId : snapshot.VariantId);
        var generatedId = source + "." + SanitizeIdentitySegment(enemy);
        if (!Same(enemy, variant)) generatedId += "." + SanitizeIdentitySegment(variant);
        var profileId = First(fixedProfileId, generatedId);
        var speciesId = First(fixedSpeciesId, profileId);
        var seed = source + ":" + snapshot.ProfileKey;
        var weights = new[]
        {
            1d + Math.Max(0, snapshot.BaseAttack),
            1d + Math.Max(0, snapshot.BaseHp) / 4d,
            1d + (SpiritGrowthService.StableHash(seed + ":luck") % 1000) / 160d,
            1d + Math.Max(0, snapshot.BaseArmor) * 1.5d
        };
        var profile = new SpiritSpeciesGrowthProfile
        {
            SpeciesId = speciesId,
            ProfileId = profileId,
            CaptureElement = SpiritElementService.DeterministicDefault(speciesId),
            FormKey = "default",
            FormLabelKey = "form.default",
            Match = new SpiritSpeciesGrowthMatch
            {
                SourceModId = source,
                EnemyId = enemy,
                VariantId = Same(enemy, variant) ? "*" : variant
            },
            EnemyId = enemy,
            VariantId = Same(enemy, variant) ? "*" : variant,
            Tier = tier.ToString(),
            BaseOrigins = Allocate(DefaultBaseTotal(tier), weights, seed + ":base"),
            GrowthOrigins = Allocate(DefaultGrowthTotal(tier), weights, seed + ":growth"),
            LevelCurveId = document.Defaults.LevelCurveId,
            AptitudeRollProfileId = document.Defaults.AptitudeRollProfileId,
            AptitudeCurveId = document.Defaults.AptitudeCurveId,
            ExperienceCurveId = document.Defaults.ExperienceCurveId,
            BattleConversionId = document.Defaults.BattleConversionId,
            RadarScaleId = document.Defaults.RadarScaleId
        };
        return new SpiritProfileIdentity
        {
            SpeciesId = speciesId,
            ProfileId = profileId,
            Profile = profile,
            UsedFallback = true
        };
    }

    private static SpiritGrowthRegistryDocument MigrateSchema1(SpiritGrowthRegistryDocument source)
    {
        var migrated = BuiltInDocument();
        foreach (var legacy in source.Profiles ?? new List<SpiritSpeciesGrowthProfile>())
        {
            var enemyId = NormalizeRuntimeId(legacy.EnemyId);
            if (enemyId.Length == 0 || enemyId == "*") continue;
            var profileId = "terrias." + SanitizeIdentitySegment(enemyId);
            migrated.Profiles.Add(new SpiritSpeciesGrowthProfile
            {
                SpeciesId = profileId,
                ProfileId = profileId,
                CaptureElement = SpiritElementService.DeterministicDefault(profileId),
                FormKey = "default",
                FormLabelKey = "form.default",
                Match = new SpiritSpeciesGrowthMatch
                {
                    SourceModId = "terrias",
                    EnemyId = enemyId,
                    VariantId = string.IsNullOrWhiteSpace(legacy.VariantId) ? "*" : legacy.VariantId.Trim()
                },
                Tier = legacy.Tier,
                BaseOrigins = legacy.BaseOrigins?.Clone() ?? new SpiritOriginVector(),
                GrowthOrigins = legacy.GrowthOrigins?.Clone() ?? new SpiritOriginVector()
            });
        }

        return migrated;
    }

    private static SpiritGrowthRegistryDocument MigrateSchema2(SpiritGrowthRegistryDocument source)
    {
        foreach (var profile in source.Profiles ?? new List<SpiritSpeciesGrowthProfile>())
        {
            profile.CaptureElement = SpiritElementService.DeterministicDefault(
                string.IsNullOrWhiteSpace(profile.SpeciesId) ? profile.ProfileId : profile.SpeciesId);
        }

        source.SchemaVersion = SpiritSystemContract.GrowthRegistrySchemaVersion;
        return source;
    }

    private static SpiritGrowthRegistryDocument NormalizeAndValidate(SpiritGrowthRegistryDocument source)
    {
        source.Defaults ??= new SpiritGrowthRegistryDefaults();
        source.FormLabels ??= new Dictionary<string, string>(StringComparer.Ordinal);
        source.LevelCurves ??= new List<SpiritLevelCurveDefinition>();
        source.AptitudeRollProfiles ??= new List<SpiritAptitudeRollProfile>();
        source.AptitudeCurves ??= new List<SpiritAptitudeCurveDefinition>();
        source.ExperienceCurves ??= new List<SpiritExperienceCurveDefinition>();
        source.BattleConversions ??= new List<SpiritBattleConversionDefinition>();
        source.RadarScaleSets ??= new List<SpiritRadarScaleSet>();
        source.Profiles ??= new List<SpiritSpeciesGrowthProfile>();
        if (source.Defaults.MaxLevel < 2 || source.Defaults.MaxLevel > 100)
            throw new InvalidDataException("defaults.maxLevel must be within 2..100");

        EnsureUnique(source.LevelCurves.Select(item => item.Id), "level curve");
        EnsureUnique(source.AptitudeRollProfiles.Select(item => item.Id), "aptitude roll profile");
        EnsureUnique(source.AptitudeCurves.Select(item => item.Id), "aptitude curve");
        EnsureUnique(source.ExperienceCurves.Select(item => item.Id), "experience curve");
        EnsureUnique(source.BattleConversions.Select(item => item.Id), "battle conversion");
        EnsureUnique(source.RadarScaleSets.Select(item => item.Id), "radar scale");
        foreach (var curve in source.LevelCurves)
        {
            if (!Same(curve.Type, "normalizedLinear") || curve.MinLevel < 1 || curve.MaxLevel <= curve.MinLevel)
                throw new InvalidDataException("invalid level curve " + curve.Id);
        }
        foreach (var roll in source.AptitudeRollProfiles)
        {
            if (!Same(roll.Type, "truncatedNormal") || roll.StandardDeviation <= 0d || roll.Maximum <= roll.Minimum
                || roll.Mean < roll.Minimum || roll.Mean > roll.Maximum
                || roll.Fallback < roll.Minimum || roll.Fallback > roll.Maximum || roll.MaximumAttempts < 1)
                throw new InvalidDataException("invalid aptitude roll profile " + roll.Id);
        }
        foreach (var curve in source.AptitudeCurves)
        {
            if (!Same(curve.Type, "smoothstep") || curve.InputMax <= curve.InputMin || curve.OutputMin <= 0d || curve.OutputMax < curve.OutputMin)
                throw new InvalidDataException("invalid aptitude curve " + curve.Id);
        }
        foreach (var curve in source.ExperienceCurves)
        {
            if (!Same(curve.Type, "quadraticStep") || curve.Base < 1 || curve.QuadraticDivisor < 1)
                throw new InvalidDataException("invalid experience curve " + curve.Id);
            for (var offset = 0; offset < source.Defaults.MaxLevel; offset++)
            {
                if (curve.Base + curve.Linear * offset + offset * offset / curve.QuadraticDivisor <= 0)
                    throw new InvalidDataException("non-positive experience step in curve " + curve.Id);
            }
        }
        foreach (var conversion in source.BattleConversions)
        {
            var values = new[]
            {
                conversion.HpBase, conversion.HpSpirit, conversion.HpLuck,
                conversion.AttackBase, conversion.AttackMagic, conversion.AttackPerception, conversion.AttackLuck,
                conversion.ArmorBase, conversion.ArmorPerception, conversion.ArmorSpirit, conversion.ArmorLuck,
                conversion.IntentEnergyBase, conversion.IntentEnergyMagic, conversion.IntentEnergyPerception
            };
            if (values.Any(value => double.IsNaN(value) || double.IsInfinity(value) || value < 0d))
                throw new InvalidDataException("invalid battle conversion " + conversion.Id);
        }
        foreach (var scale in source.RadarScaleSets)
        {
            var keys = (scale.Axes ?? new List<SpiritRadarAxisDefinition>()).Select(axis => axis.Key).ToArray();
            if (!Same(scale.Mode, "absoluteCaps") || keys.Length != 4
                || !keys.SequenceEqual(new[] { "magic", "perception", "spirit", "luck" }, StringComparer.Ordinal)
                || scale.Axes.Any(axis => axis.Cap <= 0))
                throw new InvalidDataException("invalid radar scale " + scale.Id);
        }

        RequireReference(source.LevelCurves.Select(item => item.Id), source.Defaults.LevelCurveId, "default level curve");
        RequireReference(source.AptitudeRollProfiles.Select(item => item.Id), source.Defaults.AptitudeRollProfileId, "default aptitude roll profile");
        RequireReference(source.AptitudeCurves.Select(item => item.Id), source.Defaults.AptitudeCurveId, "default aptitude curve");
        RequireReference(source.ExperienceCurves.Select(item => item.Id), source.Defaults.ExperienceCurveId, "default experience curve");
        RequireReference(source.BattleConversions.Select(item => item.Id), source.Defaults.BattleConversionId, "default battle conversion");
        RequireReference(source.RadarScaleSets.Select(item => item.Id), source.Defaults.RadarScaleId, "default radar scale");
        var defaultLevelCurve = source.LevelCurves.First(item => Same(item.Id, source.Defaults.LevelCurveId));
        if (defaultLevelCurve.MaxLevel != source.Defaults.MaxLevel)
            throw new InvalidDataException("defaults.maxLevel must match the default level curve maximum");

        var normalizedProfiles = new List<SpiritSpeciesGrowthProfile>();
        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        var matchKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in source.Profiles)
        {
            var match = raw.Match ?? new SpiritSpeciesGrowthMatch();
            var profileId = (raw.ProfileId ?? "").Trim();
            var speciesId = (raw.SpeciesId ?? "").Trim();
            if (profileId.Length == 0 || speciesId.Length == 0)
                throw new InvalidDataException("profileId and speciesId are required");
            if (!profileIds.Add(profileId)) throw new InvalidDataException("duplicate profileId=" + profileId);
            var sourceModId = NormalizeSourceModId(match.SourceModId, match.EnemyId);
            var enemyId = NormalizeRuntimeId(match.EnemyId);
            var variantId = string.IsNullOrWhiteSpace(match.VariantId) || Same(match.VariantId, "*")
                ? "*"
                : NormalizeRuntimeId(match.VariantId);
            if (enemyId.Length == 0 || enemyId == "*") throw new InvalidDataException("invalid enemy match for " + profileId);
            var matchKey = sourceModId + "#" + enemyId + "#" + variantId;
            if (!matchKeys.Add(matchKey)) throw new InvalidDataException("duplicate profile match=" + matchKey);
            var tier = ParseTier(raw.Tier, (SpiritSpeciesTier)0);
            if (tier == 0) throw new InvalidDataException("invalid tier for " + profileId);
            var captureElement = SpiritElementService.NormalizeId(raw.CaptureElement);
            if (captureElement.Length == 0)
                throw new InvalidDataException("invalid captureElement for " + profileId);
            ValidateOrigins(raw.BaseOrigins, tier, true, profileId);
            ValidateOrigins(raw.GrowthOrigins, tier, false, profileId);
            var profile = new SpiritSpeciesGrowthProfile
            {
                SpeciesId = speciesId,
                ProfileId = profileId,
                CaptureElement = captureElement,
                FormKey = First(raw.FormKey, "default"),
                FormOrder = Math.Max(0, raw.FormOrder),
                FormLabelKey = First(raw.FormLabelKey, "form.default"),
                Match = new SpiritSpeciesGrowthMatch { SourceModId = sourceModId, EnemyId = enemyId, VariantId = variantId },
                EnemyId = enemyId,
                VariantId = variantId,
                Tier = tier.ToString(),
                BaseOrigins = raw.BaseOrigins.Clone(),
                GrowthOrigins = raw.GrowthOrigins.Clone(),
                LevelCurveId = First(raw.LevelCurveId, source.Defaults.LevelCurveId),
                AptitudeRollProfileId = First(raw.AptitudeRollProfileId, source.Defaults.AptitudeRollProfileId),
                AptitudeCurveId = First(raw.AptitudeCurveId, source.Defaults.AptitudeCurveId),
                ExperienceCurveId = First(raw.ExperienceCurveId, source.Defaults.ExperienceCurveId),
                BattleConversionId = First(raw.BattleConversionId, source.Defaults.BattleConversionId),
                RadarScaleId = First(raw.RadarScaleId, source.Defaults.RadarScaleId)
            };
            RequireReference(source.LevelCurves.Select(item => item.Id), profile.LevelCurveId, "profile level curve " + profileId);
            RequireReference(source.AptitudeRollProfiles.Select(item => item.Id), profile.AptitudeRollProfileId, "profile aptitude roll " + profileId);
            RequireReference(source.AptitudeCurves.Select(item => item.Id), profile.AptitudeCurveId, "profile aptitude curve " + profileId);
            RequireReference(source.ExperienceCurves.Select(item => item.Id), profile.ExperienceCurveId, "profile experience curve " + profileId);
            RequireReference(source.BattleConversions.Select(item => item.Id), profile.BattleConversionId, "profile battle conversion " + profileId);
            RequireReference(source.RadarScaleSets.Select(item => item.Id), profile.RadarScaleId, "profile radar scale " + profileId);
            if (!source.FormLabels.ContainsKey(profile.FormLabelKey))
                throw new InvalidDataException("missing form label " + profile.FormLabelKey + " for " + profileId);
            normalizedProfiles.Add(profile);
        }

        foreach (var species in normalizedProfiles.GroupBy(profile => profile.SpeciesId, StringComparer.Ordinal))
        {
            if (species.Select(profile => profile.FormKey).Distinct(StringComparer.Ordinal).Count() != species.Count())
                throw new InvalidDataException("duplicate formKey in species " + species.Key);
            if (species.Select(profile => profile.FormOrder).Distinct().Count() != species.Count())
                throw new InvalidDataException("duplicate formOrder in species " + species.Key);
        }

        source.SchemaVersion = SpiritSystemContract.GrowthRegistrySchemaVersion;
        source.FormLabels = new Dictionary<string, string>(source.FormLabels, StringComparer.Ordinal);
        source.Profiles = normalizedProfiles.OrderBy(profile => profile.ProfileId, StringComparer.Ordinal).ToList();
        ValidateRadarCaps(source);
        return source;
    }

    private static void ValidateRadarCaps(SpiritGrowthRegistryDocument source)
    {
        foreach (var profile in source.Profiles)
        {
            var aptitude = source.AptitudeCurves.First(item => Same(item.Id, profile.AptitudeCurveId));
            var radar = source.RadarScaleSets.First(item => Same(item.Id, profile.RadarScaleId));
            var multiplier = aptitude.OutputMax;
            var values = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["magic"] = profile.BaseOrigins.Magic + Round(profile.GrowthOrigins.Magic * multiplier),
                ["perception"] = profile.BaseOrigins.Perception + Round(profile.GrowthOrigins.Perception * multiplier),
                ["spirit"] = profile.BaseOrigins.Spirit + Round(profile.GrowthOrigins.Spirit * multiplier),
                ["luck"] = profile.BaseOrigins.Luck + Round(profile.GrowthOrigins.Luck * multiplier)
            };
            foreach (var axis in radar.Axes)
            {
                if (values[axis.Key] > axis.Cap)
                    throw new InvalidDataException("radar cap " + radar.Id + "/" + axis.Key + " is below profile " + profile.ProfileId);
            }
        }
    }

    private static void ValidateOrigins(SpiritOriginVector vector, SpiritSpeciesTier tier, bool basis, string profileId)
    {
        if (vector == null) throw new InvalidDataException("missing origin vector for " + profileId);
        var range = BudgetRange(tier, basis);
        if (vector.Total < range.Minimum || vector.Total > range.Maximum)
            throw new InvalidDataException("origin total out of tier range for " + profileId);
        foreach (var value in new[] { vector.Magic, vector.Spirit, vector.Luck, vector.Perception })
        {
            var share = value / (double)vector.Total;
            if (value < 0 || share < 0.10d || share > 0.45d)
                throw new InvalidDataException("origin axis out of 10%-45% range for " + profileId);
        }
    }

    private static (int Minimum, int Maximum) BudgetRange(SpiritSpeciesTier tier, bool basis)
    {
        if (basis)
        {
            return tier switch
            {
                SpiritSpeciesTier.Elite => (32, 40),
                SpiritSpeciesTier.Boss => (40, 48),
                SpiritSpeciesTier.FinalBoss => (48, 60),
                _ => (24, 32)
            };
        }
        return tier switch
        {
            SpiritSpeciesTier.Elite => (72, 88),
            SpiritSpeciesTier.Boss => (88, 108),
            SpiritSpeciesTier.FinalBoss => (108, 132),
            _ => (56, 72)
        };
    }

    private static SpiritGrowthRegistryDocument BuiltInDocument()
    {
        return new SpiritGrowthRegistryDocument
        {
            SchemaVersion = SpiritSystemContract.GrowthRegistrySchemaVersion,
            Defaults = new SpiritGrowthRegistryDefaults(),
            FormLabels = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["form.default"] = "",
                ["form.black"] = "黑色形态",
                ["form.white"] = "白色形态",
                ["form.derived"] = "派生形态",
                ["form.final"] = "最终形态",
                ["form.right"] = "右剑",
                ["form.left"] = "左剑",
                ["form.phase-1"] = "第一形态",
                ["form.phase-2"] = "第二形态",
                ["form.phase-3"] = "第三形态",
                ["form.complete-angel"] = "完全天使"
            },
            LevelCurves = new List<SpiritLevelCurveDefinition>
            {
                new() { Id = "level-linear-1-50", Type = "normalizedLinear", MinLevel = 1, MaxLevel = 50 }
            },
            AptitudeRollProfiles = new List<SpiritAptitudeRollProfile>
            {
                new() { Id = "aptitude-roll-normal-60-15", Type = "truncatedNormal", Mean = 60d, StandardDeviation = 15d, Minimum = 0, Maximum = 100, Fallback = 60, MaximumAttempts = 64 }
            },
            AptitudeCurves = new List<SpiritAptitudeCurveDefinition>
            {
                new() { Id = "aptitude-smoothstep-080-120", Type = "smoothstep", InputMin = 0, InputMax = 100, OutputMin = 0.8d, OutputMax = 1.2d }
            },
            ExperienceCurves = new List<SpiritExperienceCurveDefinition>
            {
                new() { Id = "xp-standard-1-50", Type = "quadraticStep", Base = 20, Linear = 2, QuadraticDivisor = 24 }
            },
            BattleConversions = new List<SpiritBattleConversionDefinition>
            {
                new() { Id = "origins-battle-standard-v1" }
            },
            RadarScaleSets = new List<SpiritRadarScaleSet>
            {
                new()
                {
                    Id = "origins-global-v1",
                    Mode = "absoluteCaps",
                    Axes = new List<SpiritRadarAxisDefinition>
                    {
                        new() { Key = "magic", Cap = 80 },
                        new() { Key = "perception", Cap = 80 },
                        new() { Key = "spirit", Cap = 80 },
                        new() { Key = "luck", Cap = 80 }
                    }
                }
            }
        };
    }

    private static void SetDocument(SpiritGrowthRegistryDocument next)
    {
        document = next ?? BuiltInDocument();
        profilesById = document.Profiles.ToDictionary(profile => profile.ProfileId, profile => profile, StringComparer.Ordinal);
        registryHash = SpiritGrowthService.StableHash(AuraSharedJson.Serialize(document)).ToString("x8");
    }

    private static SpiritOriginVector Allocate(int total, IReadOnlyList<double> rawWeights, string seed)
    {
        total = Math.Max(4, total);
        var weights = rawWeights.Select(value => Math.Max(0.01d, value)).ToArray();
        var minimum = Math.Max(1, (int)Math.Ceiling(total * 0.10d));
        var maximum = Math.Max(minimum, (int)Math.Floor(total * 0.45d));
        var desired = weights.Select(value => value / weights.Sum() * total).ToArray();
        var values = Enumerable.Repeat(minimum, 4).ToArray();
        while (values.Sum() < total)
        {
            var index = Enumerable.Range(0, 4)
                .Where(candidate => values[candidate] < maximum)
                .OrderByDescending(candidate => desired[candidate] - values[candidate])
                .ThenBy(candidate => SpiritGrowthService.StableHash(seed + ":" + candidate))
                .First();
            values[index]++;
        }
        return new SpiritOriginVector { Magic = values[0], Spirit = values[1], Luck = values[2], Perception = values[3] };
    }

    private static int DefaultBaseTotal(SpiritSpeciesTier tier) => tier switch
    {
        SpiritSpeciesTier.Elite => 36,
        SpiritSpeciesTier.Boss => 44,
        SpiritSpeciesTier.FinalBoss => 54,
        _ => 28
    };

    private static int DefaultGrowthTotal(SpiritSpeciesTier tier) => tier switch
    {
        SpiritSpeciesTier.Elite => 80,
        SpiritSpeciesTier.Boss => 100,
        SpiritSpeciesTier.FinalBoss => 120,
        _ => 64
    };

    private static SpiritSpeciesTier ParseTier(string value, SpiritSpeciesTier fallback)
    {
        return Enum.TryParse(value, true, out SpiritSpeciesTier tier) && Enum.IsDefined(typeof(SpiritSpeciesTier), tier) ? tier : fallback;
    }

    private static IReadOnlyList<string> IdentityCandidates(string rawId)
    {
        var result = new List<string>();
        void Add(string value)
        {
            var normalized = NormalizeRuntimeId(value);
            if (normalized.Length > 0 && !result.Contains(normalized, StringComparer.Ordinal)) result.Add(normalized);
        }
        Add(rawId);
        foreach (var candidate in TerriasContentIdCompatibility.LookupCandidates(rawId ?? "", "terrias")) Add(candidate);
        return result;
    }

    private static string NormalizeRuntimeId(string value)
    {
        var result = (value ?? "").Trim().TrimStart('*');
        return result.StartsWith("enemy_", StringComparison.Ordinal) ? result.Substring("enemy_".Length) : result;
    }

    private static string NormalizeSourceModId(string? value, string? enemyId)
    {
        var source = (value ?? "").Trim();
        if (source.Length == 0)
        {
            source = TerriasContentIdCompatibility.HasKnownPrefix(enemyId ?? "") ? "terrias" : "base-game";
        }
        if (Same(source, "BaseGame") || Same(source, "base_game")) return "base-game";
        if (Same(source, "Terrias")) return "terrias";
        if (source == "*") return "*";
        return SanitizeIdentitySegment(source);
    }

    private static string SanitizeIdentitySegment(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in (value ?? "").Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character == '-' || character == '_') builder.Append(character);
            else if (builder.Length == 0 || builder[builder.Length - 1] != '-') builder.Append('-');
        }
        return builder.ToString().Trim('-');
    }

    private static int IndexOf(IReadOnlyList<string> values, string expected)
    {
        for (var index = 0; index < values.Count; index++) if (Same(values[index], expected)) return index;
        return -1;
    }

    private static void EnsureUnique(IEnumerable<string> ids, string kind)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in ids)
        {
            var id = (raw ?? "").Trim();
            if (id.Length == 0 || !seen.Add(id)) throw new InvalidDataException("invalid or duplicate " + kind + " id=" + id);
        }
    }

    private static void RequireReference(IEnumerable<string> ids, string expected, string context)
    {
        if (!ids.Contains(expected ?? "", StringComparer.Ordinal)) throw new InvalidDataException("missing " + context + " id=" + expected);
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
    private static string First(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : (value ?? "").Trim();
    private static bool Same(string left, string right) => string.Equals((left ?? "").Trim(), (right ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    private static SpiritLevelCurveDefinition Clone(SpiritLevelCurveDefinition value) => new() { Id = value.Id, Type = value.Type, MinLevel = value.MinLevel, MaxLevel = value.MaxLevel };
    private static SpiritAptitudeRollProfile Clone(SpiritAptitudeRollProfile value) => new() { Id = value.Id, Type = value.Type, Mean = value.Mean, StandardDeviation = value.StandardDeviation, Minimum = value.Minimum, Maximum = value.Maximum, Fallback = value.Fallback, MaximumAttempts = value.MaximumAttempts };
    private static SpiritAptitudeCurveDefinition Clone(SpiritAptitudeCurveDefinition value) => new() { Id = value.Id, Type = value.Type, InputMin = value.InputMin, InputMax = value.InputMax, OutputMin = value.OutputMin, OutputMax = value.OutputMax };
    private static SpiritExperienceCurveDefinition Clone(SpiritExperienceCurveDefinition value) => new() { Id = value.Id, Type = value.Type, Base = value.Base, Linear = value.Linear, QuadraticDivisor = value.QuadraticDivisor };
    private static SpiritBattleConversionDefinition Clone(SpiritBattleConversionDefinition value) => new()
    {
        Id = value.Id, HpBase = value.HpBase, HpSpirit = value.HpSpirit, HpLuck = value.HpLuck,
        AttackBase = value.AttackBase, AttackMagic = value.AttackMagic, AttackPerception = value.AttackPerception, AttackLuck = value.AttackLuck,
        ArmorBase = value.ArmorBase, ArmorPerception = value.ArmorPerception, ArmorSpirit = value.ArmorSpirit, ArmorLuck = value.ArmorLuck,
        IntentEnergyBase = value.IntentEnergyBase, IntentEnergyMagic = value.IntentEnergyMagic, IntentEnergyPerception = value.IntentEnergyPerception
    };
    private static SpiritRadarScaleSet Clone(SpiritRadarScaleSet value) => new()
    {
        Id = value.Id,
        Mode = value.Mode,
        Axes = (value.Axes ?? new List<SpiritRadarAxisDefinition>()).Select(axis => new SpiritRadarAxisDefinition { Key = axis.Key, Cap = axis.Cap }).ToList()
    };
}
