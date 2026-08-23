using AuraToolsExp.Dll.Features.Settings;

internal static partial class AuraToolsTestSuite
{
    public static void TestPlayerContentIdentity()
    {
        var qualified = AuraToolsContentIdentity.Parse(
            "Terrias:Terrias_terrias_afterglow_omen_card");
        var native = AuraToolsContentIdentity.Parse("card_1");
        Assert(qualified.IsQualified
               && qualified.OwnerModId == "Terrias"
               && qualified.ContentId == "Terrias_terrias_afterglow_omen_card"
               && !native.IsQualified
               && native.ContentId == "card_1",
            "player display identities separate owner-qualified storage keys from native content ids");
    }
}
