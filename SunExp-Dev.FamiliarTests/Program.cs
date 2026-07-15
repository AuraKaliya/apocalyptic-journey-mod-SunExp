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

Assert(registry.SchemaVersion == 2, "registry schema must be 2");
Assert(registry.Blessings.All(item => item.Effects.All(effect => effect.Kind is not "ManifestEnable" and not "SpeciesManifest" and not "CompanionIntentPoolPatch")),
    "registry must not contain familiar combat manifestation effects");
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
