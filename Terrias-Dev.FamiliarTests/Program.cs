using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using SunExp.Dll.Mechanics;

if (args.Length != 1 || !File.Exists(args[0]))
{
    throw new ArgumentException("Expected the shipped familiar.blessing.registry.json path.");
}

var registry = JsonConvert.DeserializeObject<FamiliarBlessingRegistryDocument>(File.ReadAllText(args[0]))
               ?? throw new InvalidDataException("Familiar registry deserialization returned null.");
var documentField = typeof(FamiliarBlessingRegistry).GetField("document", BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new MissingFieldException("FamiliarBlessingRegistry.document");
documentField.SetValue(null, registry);

Assert(registry.SchemaVersion == 3, "registry schema must be 3");
Assert(registry.Blessings.All(item => item.Effects.All(effect => effect.Kind is not "ManifestEnable" and not "SpeciesManifest" and not "CompanionIntentPoolPatch")),
    "registry must not contain familiar combat manifestation effects");
Assert(registry.SpeciesProfiles.Count(profile => profile.FinalBlessingIds.Count == 3) >= 7,
    "all five native familiars plus Dusk and Star Clay Doll must define three species finals");
Assert(registry.Blessings.Where(item => item.Category == FamiliarBlessingCategory.Growth).All(item => item.Pool == "common"),
    "growth blessings must be isolated in the common pool");
Assert(registry.Blessings.Where(item => item.Category == FamiliarBlessingCategory.FinalGeneric).All(item => item.Pool == "final_common"),
    "generic finals must be isolated in the final_common pool");
Assert(registry.Blessings.Single(item => item.Id == "*familiar_law_of_luck").Effects.Single().Amount == 20,
    "Lucky Dice must add 20 to value and check dice");
Assert(registry.Blessings.Single(item => item.Id == "*familiar_bleeding_mark").ExclusiveGroup.Length == 0
       && registry.Blessings.Single(item => item.Id == "*familiar_burn_mark").ExclusiveGroup.Length == 0
       && registry.Blessings.Single(item => item.Id == "*familiar_armor_break").ExclusiveGroup.Length == 0,
    "bleed, burn, and armor-break first-hit blessings must remain mutually compatible");
Assert(registry.Blessings.Single(item => item.Id == "*familiar_dusk_ember_stomach").Name == "不落日魂"
       && registry.Blessings.Single(item => item.Id == "*familiar_star_clay_handed_light").Name == "摘星",
    "Dusk and Star Clay final blessing updates must ship with their final names");
var codexPools = FamiliarBlessingCodexService.Build(
    registry.Blessings,
    Array.Empty<FamiliarSpeciesSpec>(),
    registry.SpeciesProfiles);
Assert(codexPools.Count == 10 && codexPools.Sum(pool => pool.Blessings.Count) == registry.Blessings.Count,
    "the familiar codex must expose every registered blessing exactly once across all pools");
Assert(codexPools.First().Id == "common" && codexPools.First().Name == "通用祝福",
    "the familiar codex must place the common blessing pool first");
Assert(codexPools.Skip(3).Select(pool => pool.Name).SequenceEqual(new[]
    { "报丧偈羽", "匣上黑猫", "噩梦原型", "使魔猫猫", "小克莉斯娜", "黄昏", "星泥人傀" }),
    "species-final codex pools must use registered familiar display names and profile order");
Assert(codexPools.SelectMany(pool => pool.Blessings).Any(item => item.Tier == 1 && item.TierLabel == "Ⅰ阶")
       && codexPools.SelectMany(pool => pool.Blessings).Any(item => item.Tier == 5 && item.TierLabel == "Ⅴ阶"),
    "the familiar codex must render first through fifth tier labels");
foreach (var species in new[] { "10001", "*10002", "10003", "*10004", "*10005", "SunExp_sunexp_dusk", "SunExp_sunexp_star_clay_doll" })
{
    var instance = new FamiliarInstance { FullSpeciesId = species, SpeciesId = species, Aptitude = 70, Level = 8 };
    Assert(FamiliarBlessingRegistry.SpecificFinals(instance).Count == 3,
        species + " must resolve exactly three species-final blessings from its own pool");
}
Assert(FamiliarBlessingRoller.ChoiceSize(69) == 2, "aptitude below 70 must produce two candidates");
Assert(FamiliarBlessingRoller.ChoiceSize(70) == 3, "aptitude 70 or above must produce three candidates");
Assert(FamiliarBlessingRoller.AptitudeFloor(0) == 30, "initial aptitude floor must be 30");
Assert(FamiliarBlessingRoller.AptitudeFloor(8) == 70, "rebirth aptitude floor must cap at 70");
Assert(FamiliarBlessingRoller.RollAptitude("SunExp_sunexp_dusk", 3) == FamiliarBlessingRoller.RollAptitude("SunExp_sunexp_dusk", 3),
    "aptitude rolls must be stable for the same profile and rebirth count");

var dusk = new FamiliarSpeciesSpec(
    "dusk",
    "SunExp_sunexp_dusk",
    "黄昏",
    "",
    "",
    "",
    "",
    "SunExp_sunexp_dusk_afterheat_recovery");
var legacy = new FamiliarRosterDocument
{
    Version = 2,
    Instances = new List<FamiliarInstance>
    {
        new()
        {
            InstanceId = "body-dusk",
            SpeciesId = "dusk",
            Name = "旧本体",
            Level = 10,
            Aptitude = 75,
            LegacyIsBody = true,
            LegacyBlessings = new List<string> { "*familiar_guard_paw", "*familiar_fast_shadow" }
        },
        new()
        {
            InstanceId = "dusk-002",
            SpeciesId = "dusk",
            Name = "旧化身",
            Level = 4,
            Aptitude = 90
        }
    }
};
Assert(FamiliarRosterService.Normalize(legacy, new[] { dusk }), "legacy roster must require migration");
Assert(legacy.Version == FamiliarRosterService.CurrentVersion, "legacy roster must migrate to the current version");
Assert(legacy.Instances.Count == 1, "legacy body and avatars must collapse into one body");
Assert(legacy.Instances[0].InstanceId == dusk.FullSpeciesId, "the one body identity must be the full Partner id");

var profile = legacy.Instances[0];
profile.Level = 10;
profile.Experience = 0;
profile.Aptitude = 80;
profile.GrowthBlessingIds.Clear();
profile.FinalBlessingId = "";
profile.PendingBlessingChoices.Clear();
profile.BlessingRollIndex = 0;
FamiliarRosterService.Normalize(legacy, new[] { dusk });

Assert(profile.PendingBlessingChoices.Single().Level == 2, "Lv.2 must be the first growth milestone");
ChooseFirst(legacy, profile);
Assert(profile.PendingBlessingChoices.Single().Level == 4, "Lv.4 must be the second growth milestone");
ChooseFirst(legacy, profile);
Assert(profile.PendingBlessingChoices.Single().Level == 6, "Lv.6 must be the third growth milestone");
ChooseFirst(legacy, profile);
var finalChoice = profile.PendingBlessingChoices.Single();
Assert(finalChoice.Level == 8 && finalChoice.Kind == FamiliarChoiceKind.Final, "Lv.8 must generate the final blessing choice");
Assert(finalChoice.BlessingIds.Count == 3, "high aptitude final draws must contain three candidates");
Assert(finalChoice.BlessingIds.Any(id => FamiliarBlessingRegistry.Find(id)?.Category == FamiliarBlessingCategory.FinalSpecies),
    "native final draws must guarantee a species candidate");
Assert(finalChoice.BlessingIds.Any(id => FamiliarBlessingRegistry.Find(id)?.Category == FamiliarBlessingCategory.FinalGeneric),
    "final draws must guarantee a generic candidate");
ChooseFirst(legacy, profile);
Assert(profile.GrowthBlessingIds.Count == 3 && profile.FinalBlessingId.Length > 0, "milestone choices must persist three growth blessings and one final blessing");
Assert(FamiliarRosterService.CanRebirth(profile), "Lv.10 with all milestone choices resolved must allow rebirth");

var rebirth = FamiliarRosterService.Rebirth(legacy, profile.InstanceId)
              ?? throw new InvalidDataException("rebirth unexpectedly failed");
Assert(rebirth.Instance.RebirthCount == 1, "rebirth count must increment");
Assert(rebirth.Instance.Level == 1 && rebirth.Instance.Experience == 0, "rebirth must reset level and experience");
Assert(rebirth.Instance.GrowthBlessingIds.Count == 0 && rebirth.Instance.FinalBlessingId.Length == 0, "rebirth must reset all familiar blessings");
Assert(rebirth.Instance.Aptitude >= rebirth.AptitudeFloor, "rebirth aptitude must respect its pity floor");

registry.SpeciesProfiles.Add(new FamiliarSpeciesGrowthProfile
{
    FullSpeciesId = "Example_example_tag_partner",
    SpeciesId = "tag_partner",
    Tags = new List<string> { "shield" }
});
var tagPartner = new FamiliarInstance { FullSpeciesId = "Example_example_tag_partner", SpeciesId = "tag_partner", Aptitude = 50, Level = 8 };
Assert(FamiliarBlessingRegistry.SpecificFinals(tagPartner).Any(item => item.Category == FamiliarBlessingCategory.FinalTag),
    "tag-profile compatibility must provide a tag final pool");
var zeroPartner = new FamiliarInstance { FullSpeciesId = "Example_example_zero_partner", SpeciesId = "zero_partner", Aptitude = 50, Level = 8 };
Assert(FamiliarBlessingRegistry.SpecificFinals(zeroPartner).Count == 0, "zero-config compatibility must not invent a species final pool");
Assert(FamiliarBlessingRegistry.GenericFinals(zeroPartner).Count > 0, "zero-config compatibility must retain the generic final pool");

Console.WriteLine("Familiar growth tests passed: migration, milestones, final slot guarantee, compatibility, and rebirth.");

static void ChooseFirst(FamiliarRosterDocument roster, FamiliarInstance profile)
{
    var choice = profile.PendingBlessingChoices.Single();
    Assert(FamiliarRosterService.ChooseBlessing(roster, profile.InstanceId, choice.ChoiceId, choice.BlessingIds[0]),
        "pending familiar blessing choice must be selectable");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidDataException(message);
    }
}
