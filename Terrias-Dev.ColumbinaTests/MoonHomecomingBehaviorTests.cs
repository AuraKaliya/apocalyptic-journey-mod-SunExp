using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

internal static class MoonHomecomingBehaviorTests
{
    public static void Run(Action<bool, string> assert)
    {
        var hand = new List<string>
        {
            MoonHomecomingIds.ChronicleI, MoonHomecomingIds.ChronicleI,
            MoonHomecomingIds.ChronicleIII, MoonHomecomingIds.HomecomingNight,
            "OtherMod_terrias_moon_chronicle_ii"
        };
        var snapshot = MoonHomecomingRules.ReadChronicles(hand);
        var reward = new MoonHomecomingReward(snapshot);
        hand.Add(MoonHomecomingIds.ChronicleII);
        assert(reward.Power == 2 && reward.Draw == 1 && reward.Ripples == 0 && reward.ExtraUses == 1,
            "Homecoming rewards distinct owned chapters; duplicates, other mods and a later draw do not alter the captured reward");
        var nextUse = new MoonHomecomingReward(MoonHomecomingRules.ReadChronicles(hand));
        assert(nextUse.Ripples == 5 && nextUse.Power == 2 && nextUse.ExtraUses == 1,
            "a subsequent Homecoming action observes the newly acquired chapter");
        assert(hand.Count == 6, "evaluating Homecoming does not consume the held Chronicles");

        for (var mask = 0; mask < 8; mask++)
        {
            var current = new MoonHomecomingReward((MoonChronicles)mask);
            assert(current.Power == ((mask & 1) == 0 ? 0 : 2)
                && current.Draw == ((mask & 1) == 0 ? 0 : 1)
                && current.Ripples == ((mask & 2) == 0 ? 0 : 5)
                && current.ExtraUses == ((mask & 4) == 0 ? 0 : 1),
                "all Chronicle combinations retain their independently specified rewards: " + mask);
        }

        var maxHp = 199;
        var reactions = new[]
        {
            ElementalReactionType.Bloom, ElementalReactionType.Bloom,
            ElementalReactionType.Hyperbloom, ElementalReactionType.Crystallize,
            ElementalReactionType.ElectroCharged
        };
        foreach (var reaction in reactions)
        {
            if (MoonHomecomingRules.IsMarrowReaction(reaction))
                maxHp = MoonHomecomingRules.AddMaximumHp(maxHp, MoonHomecomingRules.MarrowGrowth(maxHp));
        }
        assert(maxHp == 210,
            "Marrow growth compounds across a 1% threshold and excludes Hyperbloom follow-up damage");
        assert(MoonHomecomingRules.Shield(maxHp, 20) == 42,
            "Flower Sea can calculate its shield from Max HP after its reactions resolve");
        assert(MoonHomecomingRules.AddMaximumHp(maxHp, 5) == 215,
            "Chronicle II adds its fixed growth on top of accumulated adventure growth");
        assert(MoonHomecomingRules.MarrowGrowth(95) == 1
            && MoonHomecomingRules.MarrowGrowth(100) == 2
            && MoonHomecomingRules.MarrowGrowth(0) == 0,
            "Marrow uses 1 plus a floored 1% of current Max HP, with no growth for an invalid maximum");
        assert(MoonHomecomingRules.AddMaximumHp(int.MaxValue - 2, 5) == int.MaxValue
            && MoonHomecomingRules.Shield(int.MaxValue, 20) == 429496729,
            "adventure growth and percentage shields do not overflow at large HP values");

        assert(MoonHomecomingRules.OfferingRecovery(250, 100, 2) == 50
            && MoonHomecomingRules.OfferingRecovery(250, 100, 0) == 0
            && MoonHomecomingRules.OfferingRecovery(250, 230, 4) == 20,
            "Offering respects current cost, zero-cost sacrifices, and missing HP");
        assert(MoonHomecomingRules.OfferingRecovery(int.MaxValue, 0, int.MaxValue) == int.MaxValue,
            "a very large current card cost cannot overflow Offering recovery");

        var generated = Enumerable.Range(0, 3).Select(MoonHomecomingRules.RandomChronicleId).ToArray();
        assert(generated.Distinct().Count() == 3
            && generated.All(id => MoonHomecomingRules.Chronicle(id) != MoonChronicles.None),
            "Luonnotar's three equiprobable slots contain exactly the three visible Chronicle cards");
        assert(FieldActivationIntentCatalog.TryResolve(TerriasFieldId.MoonDomain,
                FieldActivationIntentCatalog.FrostmoonNewGodIntent, out var intent)
            && intent.FixedAmount == 1 && intent.AmountPolicy == FieldActivationAmountPolicy.Fixed,
            "non-host Frostmoon New God has a server-resolved one-layer Moon Domain activation");
    }
}
