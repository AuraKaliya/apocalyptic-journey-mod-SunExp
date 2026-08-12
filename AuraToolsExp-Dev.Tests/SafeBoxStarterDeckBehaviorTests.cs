using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.DamageMeter.Capture;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.DamageMeter.SettlementCg;
using AuraToolsExp.Dll.Features.CardRefresh;
using AuraToolsExp.Dll.Features.ModSync;
using AuraToolsExp.Dll.Features.SafeBox;
using AuraToolsExp.Dll.Features.StarterDeck;
using AuraToolsExp.Dll.Infrastructure;
using AuraSkin.Shared.Models;
using Newtonsoft.Json;
internal static partial class AuraToolsTestSuite
{
    public static void TestSafeBoxDataCompatibility()
    {
        var sparse = new Dictionary<string, string>
        {
            ["Name"] = "Sparse"
        };
        var vars = new Dictionary<string, string>
        {
            ["Id"] = "custom_card"
        };
    
        Assert(AuraToolsSafeBoxDataCompatibility.TryCreateSafeCardData(
                sparse,
                vars,
                out var safeSparse,
                out var sparseId,
                out var sparseChanged),
            "sparse SafeBox card data is repairable");
        Assert(sparseChanged
               && sparseId == "custom_card"
               && safeSparse["Id"] == "custom_card"
               && safeSparse["Expend"] == AuraToolsSafeBoxDataCompatibility.DefaultExpend
               && safeSparse["Icon"] == AuraToolsSafeBoxDataCompatibility.DefaultIcon
               && safeSparse["Description"] == "",
            "sparse SafeBox card data receives required UI fields");
    
        var complete = new Dictionary<string, string>
        {
            ["Id"] = "card",
            ["Name"] = "Card",
            ["Expend"] = "2",
            ["Tag"] = "",
            ["Icon"] = AuraToolsSafeBoxDataCompatibility.DefaultIcon,
            ["Rarity"] = "1",
            ["Description"] = "done"
        };
    
        Assert(!AuraToolsSafeBoxDataCompatibility.TryCreateSafeCardData(
                complete,
                null,
                out var safeComplete,
                out var completeId,
                out var completeChanged),
            "complete SafeBox card data is left unchanged");
        Assert(!completeChanged && completeId == "card" && safeComplete["Expend"] == "2",
            "complete SafeBox card data keeps original values");
    }
    
    public static void TestStarterDeckCardClassification()
    {
        var careerRows = new List<IDictionary<string, string>>
        {
            new Dictionary<string, string>
            {
                ["Id"] = "career_1",
                ["SkillScript"] = "not_a_card_id",
                ["Skill1"] = "careercard_1",
                ["Skill2"] = "custom_skill_a; custom_skill_b|custom_skill_c"
            }
        };
        var careerSkillCardIds = StarterDeckCardClassification.BuildCareerSkillCardIds(careerRows);
        Assert(careerSkillCardIds.SetEquals(new[]
            {
                "careercard_1",
                "custom_skill_a",
                "custom_skill_b",
                "custom_skill_c"
            }),
            "starter deck career skills come from numbered Career.SkillN references only");
    
        var ordinaryActionSkillCardIds = new[]
        {
            "burningcard_1",
            "burningcard_2",
            "burningcard_3",
            "burningcard_4",
            "card_13",
            "card_15",
            "card_9",
            "healcard_7",
            "perceivecard_6"
        };
        foreach (var cardId in ordinaryActionSkillCardIds)
        {
            var row = new Dictionary<string, string>
            {
                ["Id"] = cardId,
                ["Action"] = "Skill",
                ["Type"] = cardId == "card_13" ? "消耗攻击牌" : "技能牌"
            };
            Assert(!StarterDeckCardClassification.ShouldExcludeFromStarterDeck(cardId, row, careerSkillCardIds),
                cardId + " Action=Skill remains a normal starter-deck card");
            Assert(StarterDeckCardClassification.ResolveEffectivePackId(row)
                   == StarterDeckCardClassification.DefaultCardPackId,
                cardId + " inherits the host default card pack");
        }
    
        var careerSkillRow = new Dictionary<string, string>
        {
            ["Id"] = "careercard_1",
            ["Action"] = "Attack",
            ["Type"] = "职业技能"
        };
        Assert(StarterDeckCardClassification.ShouldExcludeFromStarterDeck(
                "careercard_*1",
                careerSkillRow,
                careerSkillCardIds),
            "Career.SkillN reference excludes a career skill regardless of Action");
    
        var coronationToken = new Dictionary<string, string>
        {
            ["Id"] = "Terrias_wuna_wuna_coronation_token",
            ["Action"] = "Skill",
            ["Type"] = "衍生牌"
        };
        Assert(!StarterDeckCardClassification.IsCareerSkillCard(
                coronationToken["Id"],
                careerSkillCardIds),
            "Radiance Coronation is not mislabeled as a career skill");
        Assert(StarterDeckCardClassification.IsExcludedDerivedCard(coronationToken)
               && StarterDeckCardClassification.ShouldExcludeFromStarterDeck(
                   coronationToken["Id"],
                   coronationToken,
                   careerSkillCardIds),
            "Radiance Coronation is independently excluded as a derived card");
    
        var explicitPack = new Dictionary<string, string> { ["PackBelong"] = " cardpack_7 " };
        Assert(StarterDeckCardClassification.ResolveEffectivePackId(explicitPack) == "cardpack_7",
            "explicit card-pack ownership is preserved");
        Assert(StarterDeckCardClassification.ResolveEffectivePackId(
                   new Dictionary<string, string>(),
                   _ => "cardpack_host") == "cardpack_host",
            "host card-pack resolution takes precedence");
    }
}
