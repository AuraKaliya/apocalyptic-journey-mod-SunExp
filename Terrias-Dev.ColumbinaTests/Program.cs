using System;
using System.Linq;
using Terrias.Dll.Mechanics;
using Terrias.Dll.Network;

var assertions = 0;

var columbina = ConstellationPoolCatalog.PoolForRole("Terrias_columbina_columbina");
var traveler = ConstellationPoolCatalog.PoolForRole("career_not_columbina_variant");
Assert(columbina.Id == ConstellationPoolCatalog.ColumbinaPoolId,
    "full Columbina role identity resolves its dedicated constellation pool");
Assert(traveler.Id == ConstellationPoolCatalog.TravelerPoolId,
    "unregistered role identities use the traveler pool instead of substring matching");
Assert(ConstellationPoolCatalog.IsColumbina("columbina")
       && !ConstellationPoolCatalog.IsColumbina("fake_columbina_role"),
    "Columbina identity uses canonical role lookup");
Assert(ConstellationPoolCatalog.Clamp(-1) == 0
       && ConstellationPoolCatalog.Clamp(3) == 3
       && ConstellationPoolCatalog.Clamp(99) == ConstellationPoolCatalog.MaxLevel,
    "constellation levels are clamped to the published range");
Assert(columbina.Tiers.Count == ConstellationPoolCatalog.MaxLevel
       && columbina.Tiers.Select(tier => tier.Level).SequenceEqual(Enumerable.Range(1, 6)),
    "Columbina publishes one ordered definition for every constellation level");
Assert(columbina.PresentationFields.Count == 8
       && columbina.PresentationFields.Values.All(value => !string.IsNullOrWhiteSpace(value)),
    "constellation presentation covers every shipped locale and field");
Assert(columbina.PresentationFields["Description_en"].Split(Environment.NewLine).Length == 6,
    "constellation descriptions render one line per level");
Assert(columbina.Tier(2)?.Description.English.Contains("Gravity Value", StringComparison.Ordinal) == true,
    "Columbina level-two presentation describes its gravity threshold behavior");

Assert(ConstellationIdentityRules.ResolveAdventureRole(" bound-role ", "old-role", "combat-role") == "bound-role",
    "bound adventure role wins constellation identity resolution");
Assert(ConstellationIdentityRules.ResolveAdventureRole("", " old-role ", "combat-role") == "old-role",
    "polymorph origin is the identity fallback");
Assert(ConstellationIdentityRules.ResolveAdventureRole("", "", " combat-role ") == "combat-role",
    "current combat role is the final identity fallback");

Assert(RoleActionPresentationCatalog.SupportsRole("Terrias_columbina_columbina", ""),
    "role action presentation recognizes Columbina");
Assert(RoleActionPresentationCatalog.TargetMode("Terrias_columbina_columbina_homesickness")
       == RoleActionTargetMode.AllOpponents,
    "Homesickness restores the all-opponent target mode");
Assert(RoleActionPresentationCatalog.TargetMode("*Terrias_columbina_columbina_eternal_tide")
       == RoleActionTargetMode.SelfOnly,
    "Eternal Tide restores the self-only target mode after generated-card normalization");
Assert(RoleActionPresentationCatalog.TargetMode("unrelated-card") == RoleActionTargetMode.Default,
    "unregistered cards preserve native target selection");

Assert(!TerriasStatusOwnershipPolicy.SenderOwnsStatus("", "status", out var missingDetail)
       && missingDetail == "missing player or status id",
    "status ownership rejects an incomplete sender scope");
Assert(TerriasStatusOwnershipPolicy.SenderOwnsStatus("player-a", "player-a", out var directDetail)
       && directDetail == "direct player-status identity",
    "status ownership accepts the server-bound player status identity directly");

MoonHomecomingBehaviorTests.Run(Assert);

Console.WriteLine($"Terrias Columbina behavior tests passed: {assertions} assertions.");

void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }

    assertions++;
}
