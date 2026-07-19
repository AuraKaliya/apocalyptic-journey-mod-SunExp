using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using SunExp.Dll.Network;

var priorities = ElementalAttachmentRegistry.PriorityOrder.Select(definition => definition.Priority).ToArray();
Assert(priorities.SequenceEqual(new[] { 700, 600, 500, 400, 300, 200, 100 }), "attachment priority order changed");
Assert(ElementalAttachmentRegistry.Definition(ElementalAttachmentType.DendroCore).UpperBound == 5, "Dendro Core cap must be 5");
Assert(ElementalAttachmentRegistry.Definition(ElementalAttachmentType.Frozen).UpperBound == 1, "Frozen cap must be 1");
Assert(ElementalAttachmentRegistry.PriorityOrder.Select(definition => definition.BuffId).Distinct(StringComparer.Ordinal).Count() == 7, "attachment buff ids must be unique");

Expect(ElementalAttachmentType.Cryo, ElementalType.Pyro, ElementalReactionType.Melt);
Expect(ElementalAttachmentType.Pyro, ElementalType.Cryo, ElementalReactionType.Melt);
Expect(ElementalAttachmentType.Hydro, ElementalType.Pyro, ElementalReactionType.Vaporize);
Expect(ElementalAttachmentType.Electro, ElementalType.Pyro, ElementalReactionType.Overloaded);
Expect(ElementalAttachmentType.Electro, ElementalType.Cryo, ElementalReactionType.Superconduct);
Expect(ElementalAttachmentType.Electro, ElementalType.Hydro, ElementalReactionType.ElectroCharged);
Expect(ElementalAttachmentType.Cryo, ElementalType.Hydro, ElementalReactionType.Freeze);
Expect(ElementalAttachmentType.Pyro, ElementalType.Anemo, ElementalReactionType.Swirl);
Expect(ElementalAttachmentType.Hydro, ElementalType.Geo, ElementalReactionType.Crystallize);
Expect(ElementalAttachmentType.Dendro, ElementalType.Pyro, ElementalReactionType.Burning);
Expect(ElementalAttachmentType.Dendro, ElementalType.Hydro, ElementalReactionType.Bloom);
Expect(ElementalAttachmentType.Dendro, ElementalType.Electro, ElementalReactionType.Quicken);
Expect(ElementalAttachmentType.DendroCore, ElementalType.Pyro, ElementalReactionType.Burgeon);
Expect(ElementalAttachmentType.DendroCore, ElementalType.Electro, ElementalReactionType.Hyperbloom);
Expect(ElementalAttachmentType.Frozen, ElementalType.Pyro, ElementalReactionType.Melt);
Expect(ElementalAttachmentType.Frozen, ElementalType.Electro, ElementalReactionType.Superconduct);

ExpectPriority(
    new[] { ElementalAttachmentType.DendroCore, ElementalAttachmentType.Hydro },
    ElementalType.Pyro,
    ElementalReactionType.Vaporize,
    ElementalAttachmentType.Hydro);
ExpectPriority(
    new[] { ElementalAttachmentType.Frozen, ElementalAttachmentType.Cryo },
    ElementalType.Electro,
    ElementalReactionType.Superconduct,
    ElementalAttachmentType.Cryo);
ExpectPriority(
    new[] { ElementalAttachmentType.DendroCore, ElementalAttachmentType.Dendro },
    ElementalType.Pyro,
    ElementalReactionType.Burning,
    ElementalAttachmentType.Dendro);

Assert(ElementalTypeParser.TryParse("火", out var pyro) && pyro == ElementalType.Pyro, "Chinese Pyro alias failed");
Assert(ElementalTypeParser.TryParse("Anemo", out var anemo) && anemo == ElementalType.Anemo, "Anemo alias failed");
Assert(!ElementalTypeParser.TryParse("invalid", out _), "invalid element must be rejected");

ExpectRange(1, 1, 10);
ExpectRange(2, 8, 20);
ExpectRange(3, 15, 40);
ExpectRange(99, 1, 10);

Assert(ElementalReactionService.ShouldAttachIncomingElement(hasReaction: false),
    "a non-reacting elemental hit must attach even when the target is no longer alive after damage");
Assert(!ElementalReactionService.ShouldAttachIncomingElement(hasReaction: true),
    "a reacting elemental hit must not attach its incoming element");

Assert(ConstellationIdentityRules.ResolveAdventureRole(
        "career_1",
        "career_1",
        "SunExp_columbina_columbina") == "career_1",
    "a bound adventure role must win over the active polymorph form");
Assert(ConstellationIdentityRules.ResolveAdventureRole(
        "",
        "career_1",
        "SunExp_columbina_columbina") == "career_1",
    "the polymorph original role must recover an unbound adventure identity");
Assert(ConstellationIdentityRules.ResolveAdventureRole(
        "",
        "",
        "SunExp_columbina_columbina") == "SunExp_columbina_columbina",
    "the current role must remain the fallback outside polymorph");

var columbinaPool = ConstellationPoolCatalog.PoolForRole("SunExp_columbina_columbina");
var travelerPool = ConstellationPoolCatalog.PoolForRole("SunExp_wuna_wuna");
Assert(columbinaPool.Id == ConstellationPoolCatalog.ColumbinaPoolId, "Columbina must use the exclusive constellation pool");
Assert(travelerPool.Id == ConstellationPoolCatalog.TravelerPoolId, "roles without an exclusive pool must use the Traveler pool");
Assert(columbinaPool.Name.ZhHans == "御月鸽座" && travelerPool.Name.ZhHans == "旅人座", "constellation pools must expose their localized names");
Assert(columbinaPool.PresentationFields["Description"].Split('\n').Length == 6
       && travelerPool.PresentationFields["Description"].Split('\n').Length == 6,
    "constellation descriptions must render one line per tier");
Assert(columbinaPool.Tier(3)?.OneTimeExtraordinary == 100
       && travelerPool.Tier(1)?.OneTimeExtraordinary == 50,
    "constellation one-time numeric rewards must come from the pool table");
Assert(ConstellationPoolCatalog.Clamp(99) == 6, "constellation cap must be 6");
Assert(LunarReactionRules.ElectroChargedDamage(4) == 8, "Lunar Electro-Charged damage must be twice the personal count");
Assert(LunarReactionRules.AddCrystallizeCounts(2, 2, out var lunarCrystalTriggers) == 1 && lunarCrystalTriggers == 1,
    "Columbina Lunar Crystallize must add two counts and carry the remainder");
Assert(LunarReactionRules.Crossed(49, 55, 50), "gravity threshold crossing failed");
Assert(!LunarReactionRules.Crossed(50, 55, 50), "gravity threshold must not trigger twice");

Assert(FieldActivationIntentCatalog.TryResolve(
        SunExpFieldId.MoonDomain,
        FieldActivationIntentCatalog.ColumbinaHomesicknessIntent,
        out var homesicknessFieldIntent),
    "Columbina Homesickness must be authorized to request Moon Domain");
Assert(homesicknessFieldIntent.AmountPolicy == FieldActivationAmountPolicy.Fixed
       && homesicknessFieldIntent.FixedAmount == 1,
    "Moon Domain activation must resolve to one host-authored stack");
Assert(!FieldActivationIntentCatalog.TryResolve(
        SunExpFieldId.ScorchingCanopy,
        FieldActivationIntentCatalog.ColumbinaHomesicknessIntent,
        out _),
    "a field intent must not authorize a different field");
Assert(FieldActivationIntentCatalog.TryResolve(
        SunExpFieldId.ScorchingCanopy,
        FieldActivationIntentCatalog.CanopyReturnCardIntent,
        out var canopyReturnIntent)
       && canopyReturnIntent.FixedAmount == 2,
    "Canopy Return must preserve its two-stack authoritative resolution");
Assert(FieldActivationIntentCatalog.TryResolve(
        SunExpFieldId.ScorchingCanopy,
        FieldActivationIntentCatalog.ScorchingCanopyCarrierIntent,
        out var carrierIntent)
       && carrierIntent.AmountPolicy == FieldActivationAmountPolicy.AuthoritativeScorchingCanopyCarrierStacks,
    "legacy Scorching Canopy carriers must keep server-resolved stack counts");
Assert(!FieldActivationIntentCatalog.TryResolve(
        SunExpFieldId.MoonDomain,
        "Columbina.InvalidSkill",
        out _),
    "an undeclared Moon Domain intent must be rejected");
Assert(SunExpStatusOwnershipPolicy.SenderOwnsStatus("player-1", "player-1", out _),
    "the bound sender must own its native player status");
Assert(!SunExpStatusOwnershipPolicy.SenderOwnsStatus("player-1", "spoofed-status", out _),
    "a sender must not authorize an unrelated status id");

Console.WriteLine("Elemental mechanics catalog tests passed.");

static void Expect(ElementalAttachmentType existing, ElementalType incoming, ElementalReactionType expected)
{
    Assert(ElementalReactionRegistry.TryResolve(existing, incoming, out var definition), existing + "+" + incoming + " was not registered");
    Assert(definition.Reaction == expected, existing + "+" + incoming + " expected " + expected + " but got " + definition.Reaction);
}

static void ExpectPriority(
    IReadOnlyList<ElementalAttachmentType> attachments,
    ElementalType incoming,
    ElementalReactionType expectedReaction,
    ElementalAttachmentType expectedConsumed)
{
    Assert(ElementalReactionRegistry.TryResolve(attachments, incoming, out var definition), "priority reaction was not resolved");
    Assert(definition.Reaction == expectedReaction, "priority selected wrong reaction");
    Assert(definition.Existing == expectedConsumed, "priority consumed wrong attachment");
}

static void ExpectRange(int rarity, int minimum, int maximum)
{
    var range = ElementalMagicService.RangeForRarity(rarity);
    Assert(range.Minimum == minimum && range.Maximum == maximum, "rarity " + rarity + " range mismatch");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidDataException(message);
    }
}
