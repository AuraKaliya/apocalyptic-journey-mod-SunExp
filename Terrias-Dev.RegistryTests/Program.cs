using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using Newtonsoft.Json;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

if (args.Length != 2 || !File.Exists(args[0]) || !File.Exists(args[1]))
{
    throw new ArgumentException("Expected the shipped spirit.intent.registry.json and spirit.artifact.registry.json paths.");
}

var legacyMainPrefix = TerriasContentIdCompatibility.LegacyPrefixFor("terrias");
var legacyWunaPrefix = TerriasContentIdCompatibility.LegacyPrefixFor("wuna");
var legacyLoneerPrefix = TerriasContentIdCompatibility.LegacyPrefixFor("loneer");
var legacyColumbinaPrefix = TerriasContentIdCompatibility.LegacyPrefixFor("columbina");
var legacyCurseCardPrefix = TerriasContentIdCompatibility.LegacyPrefixFor("cursecard");
var legacySolarMemoryPrefix = TerriasContentIdCompatibility.LegacyPrefixFor("solar_memory");

Assert(TerriasContentIdCompatibility.Canonicalize(legacyMainPrefix + "spark") == "Terrias_terrias_spark",
    "legacy main-table ids must canonicalize to Terrias");
Assert(TerriasContentIdCompatibility.Canonicalize(legacyWunaPrefix + "wuna") == "Terrias_wuna_wuna",
    "legacy role-table ids must canonicalize to Terrias");
Assert(TerriasContentIdCompatibility.Canonicalize(legacyLoneerPrefix + "loneer") == "Terrias_loneer_loneer",
    "legacy Loneer ids must canonicalize to Terrias");
Assert(TerriasContentIdCompatibility.Canonicalize(legacyColumbinaPrefix + "columbina") == "Terrias_columbina_columbina",
    "legacy Columbina ids must canonicalize to Terrias");
Assert(TerriasContentIdCompatibility.Canonicalize(legacyCurseCardPrefix + "abyss_deficit") == "Terrias_cursecard_abyss_deficit",
    "legacy curse-card ids must canonicalize to Terrias");
Assert(TerriasContentIdCompatibility.Canonicalize(legacySolarMemoryPrefix + "route") == "Terrias_solar_memory_route",
    "legacy Solar Memory ids must canonicalize to Terrias");
Assert(TerriasContentIdCompatibility.Equivalent(legacyMainPrefix + "spark", "Terrias_terrias_spark"),
    "legacy and current ids must compare as equivalent");
Assert(!TerriasContentIdCompatibility.Equivalent(legacyWunaPrefix + "same", "Terrias_terrias_same"),
    "different table namespaces must not collapse merely because local suffixes match");
var legacyCandidates = TerriasContentIdCompatibility.LookupCandidates(legacyMainPrefix + "spark", "terrias");
Assert(legacyCandidates.Contains(legacyMainPrefix + "spark")
       && legacyCandidates.Contains("Terrias_terrias_spark")
       && legacyCandidates.Contains("spark"),
    "legacy lookup candidates must preserve raw identity and include current/local aliases");
Assert(FamiliarId.NormalizeSpeciesId(legacyMainPrefix + "dusk") == "dusk",
    "familiar identity must accept the legacy main-table prefix");
Assert(RoleActionPresentationCatalog.TargetMode(legacyColumbinaPrefix + "columbina_homesickness") == RoleActionTargetMode.AllOpponents,
    "role action presentation must accept legacy role-card ids");
Assert(TerriasHardTagIds.Normalize(legacyMainPrefix + "solar_memory_scorched_world") == "solar_memory_scorched_world",
    "hard-tag identity must accept the legacy main-table prefix");

var document = AuraSharedJson.Deserialize<SpiritIntentRegistryDocument>(File.ReadAllText(args[0]))
    ?? throw new InvalidDataException("C# SpiritIntentRegistryDocument deserialization returned null.");

Assert(document.SchemaVersion == 3, "schema version must be 3");
Assert(document.Profiles.Count(profile => profile.EnemyId != "*") == 59, "expected 59 explicit profiles");
Assert(document.Intents.Count(intent => intent.Pool == "Pve") == 54, "expected 54 PvE intents");
Assert(document.Intents.Count(intent => intent.Pool == "PvpReserved") == 12, "expected 12 PvP-reserved intents");

var scalarPresentation = CompanionIntentPresentationSnapshot.Resolve(new CompanionResolvedEffect
{
    HandlerId = "block.single",
    Value = 26,
    RepeatCount = 1
}, 1);
Assert(scalarPresentation.DisplayText == "26", "block presentation must preserve the authoritative committed value");

var multiPresentation = CompanionIntentPresentationSnapshot.Resolve(new CompanionResolvedEffect
{
    HandlerId = "damage.multi",
    Value = 5,
    RepeatCount = 4
}, 1);
Assert(multiPresentation.DisplayText == "5\u00d74", "multi-hit presentation must preserve per-hit value and hit count");

var buffPresentation = CompanionIntentPresentationSnapshot.Resolve(new CompanionResolvedEffect
{
    HandlerId = "buff.apply",
    Value = 0,
    BuffStacks = 3
}, 2);
Assert(buffPresentation.DisplayText == "3", "buff presentation must use committed buff stacks");

var composedPresentation = SpiritIntentPresentationDataComposer.Compose(
    new System.Collections.Generic.Dictionary<string, string>
    {
        ["Id"] = "enemycard_defence",
        ["InitScript"] = "native-defence-init",
        ["TargetScript"] = "native-defence-target",
        ["UseScript"] = "native-defence-use",
        ["Description"] = "Grant {0} Shield.",
        ["Description_zh-Hant"] = "獲得{0}點護盾。",
        ["Description1"] = "Secondary {0} presentation.",
        ["Icon"] = "Icon/ActionIcon/Defence"
    },
    new System.Collections.Generic.Dictionary<string, string>
    {
        ["Id"] = "Terrias_terrias_enemycard_spirit_intent_adapter",
        ["InitScript"] = "adapter-init",
        ["TargetScript"] = "adapter-target",
        ["UseScript"] = "adapter-use"
    });
Assert(composedPresentation["Id"] == "Terrias_terrias_enemycard_spirit_intent_adapter", "spirit presentation must use the adapter runtime identity");
Assert(composedPresentation["InitScript"] == "adapter-init", "spirit presentation must use the adapter init script");
Assert(composedPresentation["Description"] == "Grant {0} Shield.", "spirit presentation must preserve the source description");
Assert(composedPresentation["Icon"] == "Icon/ActionIcon/Defence", "spirit presentation must preserve the source icon");
var presentationOverrides = SpiritIntentPresentationDataComposer.PresentationOverrides(composedPresentation);
Assert(presentationOverrides["Description"] == "Grant {0} Shield."
       && presentationOverrides["Description_zh-Hant"] == "獲得{0}點護盾。"
       && presentationOverrides["Description1"] == "Secondary {0} presentation.",
    "spirit materialization overrides must retain every Description presentation field");
Assert(!presentationOverrides.ContainsKey("Id")
       && !presentationOverrides.ContainsKey("InitScript")
       && !presentationOverrides.ContainsKey("TargetScript")
       && !presentationOverrides.ContainsKey("UseScript"),
    "spirit materialization overrides must omit only the adapter-owned identity and executable fields");

foreach (var profile in document.Profiles)
{
    Assert(profile.SourceEnemyCardIds != null, profile.EnemyId + " sourceEnemyCardIds is null");
    Assert(profile.PveAttackTendency != null, profile.EnemyId + " pveAttackTendency is null");
    Assert(profile.PveDefenseTendency != null, profile.EnemyId + " pveDefenseTendency is null");
    Assert(profile.PvpAttackTendency != null, profile.EnemyId + " pvpAttackTendency is null");
    Assert(profile.PvpDefenseTendency != null, profile.EnemyId + " pvpDefenseTendency is null");
    Assert(profile.FallbackAttackTendency != null, profile.EnemyId + " fallbackAttackTendency is null");
    Assert(profile.FallbackDefenseTendency != null, profile.EnemyId + " fallbackDefenseTendency is null");
    Assert(profile.PvpSourceEnemyCardIds != null, profile.EnemyId + " pvpSourceEnemyCardIds is null");
    Assert(profile.FallbackSourceEnemyCardIds != null, profile.EnemyId + " fallbackSourceEnemyCardIds is null");
}

var artifactDocument = JsonConvert.DeserializeObject<SpiritArtifactRegistryDocument>(File.ReadAllText(args[1]))
    ?? throw new InvalidDataException("C# SpiritArtifactRegistryDocument deserialization returned null.");
Assert(artifactDocument.SchemaVersion == 1, "artifact registry schema version must be 1");
Assert(artifactDocument.InventoryCapacity == 1000, "artifact inventory capacity must be 1000");
Assert(artifactDocument.Draw.Count == 10 && artifactDocument.Draw.TruthCost == 160,
    "artifact draw must be a 160-Truth ten-pull");
Assert(artifactDocument.Draw.ThreeStarHardPity == 30
       && artifactDocument.Draw.TargetSetWeightPercent == 50
       && artifactDocument.Draw.MinimumTwoStarPerBatch == 1,
    "artifact pity and target contracts must remain stable");
Assert(artifactDocument.Draw.RarityWeights.Values.Sum() == 10000,
    "artifact rarity weights must total 10000");
Assert(artifactDocument.Enhancement.UpgradeCosts.SequenceEqual(new[] { 10, 20, 30, 40 }),
    "artifact enhancement costs must total 100 through 10/20/30/40");
Assert(artifactDocument.SubStatWeights.Sum(value => value.Weight) == 100,
    "artifact sub-stat weights must total 100 percent");
Assert(artifactDocument.Sets.Count == 12 && artifactDocument.Pools.Count == 3,
    "first artifact release must contain 12 sets in three pools");
Assert(artifactDocument.Pools.All(pool => pool.SetIds.Count == 4),
    "every artifact pool must contain four sets");
Assert(artifactDocument.Sets.SelectMany(set => set.Pieces).Count() == 60,
    "every first-release artifact set must contain five pieces");
Assert(artifactDocument.Sets.All(set => set.Bonuses.Select(bonus => bonus.RequiredPieces).SequenceEqual(new[] { 2, 4 })),
    "first-release artifact sets must expose cumulative 2/4-piece effects");
Assert(artifactDocument.Sets.SelectMany(set => set.Bonuses).SelectMany(bonus => bonus.Effects)
        .All(effect => SpiritArtifactEffectHandlerRegistry.Supports(effect.HandlerId)),
    "every artifact set effect must use a registered handler");
var assignedSetIds = artifactDocument.Pools.SelectMany(pool => pool.SetIds).ToArray();
Assert(assignedSetIds.Distinct(StringComparer.Ordinal).Count() == 12,
    "every artifact set must belong to exactly one draw pool");
var terriasDirectory = Path.GetDirectoryName(args[1]) ?? throw new InvalidDataException("artifact registry directory unavailable");
foreach (var set in artifactDocument.Sets)
{
    Assert(set.Pieces.Select(piece => piece.SlotId).OrderBy(value => value, StringComparer.Ordinal)
            .SequenceEqual(SpiritArtifactSlots.All.OrderBy(value => value, StringComparer.Ordinal)),
        set.Id + " must contain every artifact slot exactly once");
    Assert(set.Pieces.Any(piece => piece.Id == set.RepresentativePieceId && piece.SlotId == SpiritArtifactSlots.Flower),
        set.Id + " representative piece must be its flower");
    foreach (var piece in set.Pieces)
    {
        const string prefix = "Mods/Terrias/";
        Assert(piece.IconPath.StartsWith(prefix, StringComparison.Ordinal), piece.Id + " icon must be Terrias-owned");
        var physical = Path.Combine(terriasDirectory,
            piece.IconPath.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar) + ".png");
        Assert(File.Exists(physical), piece.Id + " icon is missing: " + physical);
    }
}
Assert(File.Exists(Path.Combine(terriasDirectory, "ModResource", "Images", "Artifacts", "splash-background-runtime.png")),
    "artifact result background PNG is missing");
Assert(File.Exists(Path.Combine(terriasDirectory, "ModResource", "Images", "Artifacts", "resultcard-bg-runtime.png")),
    "artifact result-card PNG is missing");
Assert(File.Exists(Path.Combine(terriasDirectory, "ModResource", "Images", "Artifacts", "祈愿动画-runtime.mp4")),
    "artifact runtime wish video is missing");
Assert(SpiritArtifactMath.ApplyDamageMultiplier(125, 2000) == 150,
    "artifact damage must remain in its independent multiplier");

var legacyJson = """
{
  "schemaVersion": 3,
  "intents": [42],
  "profiles": [
    {
      "enemyId": "legacy",
      "variantId": "*",
      "pveAttackTendency": "staff_tap",
      "pveDefenseTendency": null
    },
    {
      "enemyId": "invalid",
      "variantId": "*",
      "pveAttackTendency": { "unexpected": true }
    }
  ]
}
""";
var readMethod = typeof(SpiritIntentRegistry).GetMethod("ReadDocument", BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new MissingMethodException("SpiritIntentRegistry.ReadDocument");
var readResult = readMethod.Invoke(null, new object[] { legacyJson })
    ?? throw new InvalidDataException("legacy registry reader returned null");
var readResultType = readResult.GetType();
var isolatedDocument = (SpiritIntentRegistryDocument)(readResultType.GetProperty("Document")?.GetValue(readResult)
    ?? throw new InvalidDataException("legacy registry reader returned no document"));
var normalizedFields = (int)(readResultType.GetProperty("NormalizedListFields")?.GetValue(readResult) ?? -1);
var rejectedProfiles = (int)(readResultType.GetProperty("RejectedProfiles")?.GetValue(readResult) ?? -1);
var rejectedIntents = (int)(readResultType.GetProperty("RejectedIntents")?.GetValue(readResult) ?? -1);
Assert(normalizedFields == 2, "legacy reader must normalize scalar and null list fields");
Assert(rejectedProfiles == 1, "legacy reader must isolate one malformed profile");
Assert(rejectedIntents == 1, "legacy reader must isolate one malformed intent");
Assert(isolatedDocument.Profiles.Count == 1, "legacy reader must retain the valid profile");
Assert(isolatedDocument.Profiles[0].PveAttackTendency.SequenceEqual(new[] { "staff_tap" }), "legacy scalar must become a one-item list");
Assert(isolatedDocument.Profiles[0].PveDefenseTendency.Count == 0, "legacy null must become an empty list");

Console.WriteLine(
    "Spirit registry C# deserialization passed: profiles=" + (document.Profiles.Count - 1)
    + ", pveIntents=" + document.Intents.Count(intent => intent.Pool == "Pve")
    + ", pvpReservedIntents=" + document.Intents.Count(intent => intent.Pool == "PvpReserved") + ".");
Console.WriteLine("Spirit artifact registry passed: pools=" + artifactDocument.Pools.Count
                  + ", sets=" + artifactDocument.Sets.Count
                  + ", pieces=" + artifactDocument.Sets.SelectMany(set => set.Pieces).Count() + ".");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidDataException(message);
    }
}
