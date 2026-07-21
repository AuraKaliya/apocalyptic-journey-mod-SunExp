using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

if (args.Length != 1 || !File.Exists(args[0]))
{
    throw new ArgumentException("Expected the shipped spirit.intent.registry.json path.");
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
Assert(multiPresentation.DisplayText == "5*4", "multi-hit presentation must preserve per-hit value and hit count");

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

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidDataException(message);
    }
}
