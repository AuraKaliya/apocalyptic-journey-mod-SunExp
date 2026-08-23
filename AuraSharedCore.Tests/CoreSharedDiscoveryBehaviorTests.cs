using AuraShared.Core;

internal static partial class CoreTestSuite
{
    public static void TestSharedDiscoveryProtocol()
    {
        var root = Path.Combine(sourceRoot, "discovery");
        Directory.CreateDirectory(root);
        var absent = AuraSharedDiscoveryLoader.Load(root);
        Assert(!absent.Found && absent.Success,
            "Mod without discovery manifest is not a discovery participant");

        File.WriteAllText(Path.Combine(root, "Content.modproj"), "3741157062");
        var shared = Path.Combine(root, "SharedResources");
        Directory.CreateDirectory(shared);
        File.WriteAllText(Path.Combine(shared, "aura.registration.json"), "{}");
        File.WriteAllText(
            Path.Combine(shared, "aura.discovery.json"),
            AuraSharedJson.Serialize(new AuraSharedDiscoveryManifest
            {
                OwnerModId = "Content",
                Contributions = new List<AuraSharedDiscoveryContribution>
                {
                    new()
                    {
                        Kind = AuraSharedDiscoveryContributionKinds.Resources,
                        Id = "media",
                        Path = "aura.registration.json"
                    }
                }
            }));
        var loaded = AuraSharedDiscoveryLoader.Load(root, forceRefresh: true);
        Assert(loaded.Found
               && loaded.Success
               && loaded.Source?.ModProjectId == "3741157062"
               && loaded.Source.OwnerModId == "Content"
               && loaded.Source.Contributions.Count == 1
               && loaded.Source.Fingerprint.Length == 64,
            "discovery binds semantic owner to numeric modproj identity and fingerprints SharedResources");

        File.WriteAllText(Path.Combine(root, "ModConfig.json"), "{\"PublishedFileId\":\"1\"}");
        var mismatchedPublishedId = AuraSharedDiscoveryLoader.Load(root, forceRefresh: true);
        Assert(mismatchedPublishedId.Found
               && !mismatchedPublishedId.Success
               && mismatchedPublishedId.ErrorCode == "ModProjectIdentity",
            "discovery rejects ModConfig and modproj source identity mismatch");
        File.Delete(Path.Combine(root, "ModConfig.json"));

        File.WriteAllText(Path.Combine(root, "Duplicate.modproj"), "3741157062");
        var duplicate = AuraSharedDiscoveryLoader.Load(root, forceRefresh: true);
        Assert(duplicate.Found
               && !duplicate.Success
               && duplicate.ErrorCode == "ModProjectIdentity",
            "discovery rejects multiple root modproj identities");
        File.Delete(Path.Combine(root, "Duplicate.modproj"));

        File.WriteAllText(
            Path.Combine(shared, "aura.discovery.json"),
            AuraSharedJson.Serialize(new AuraSharedDiscoveryManifest
            {
                OwnerModId = "Content",
                Contributions = new List<AuraSharedDiscoveryContribution>
                {
                    new()
                    {
                        Kind = AuraSharedDiscoveryContributionKinds.Resources,
                        Id = "escape",
                        Path = "../outside.json"
                    }
                }
            }));
        var escaped = AuraSharedDiscoveryLoader.Load(root, forceRefresh: true);
        Assert(escaped.Found && !escaped.Success && escaped.ErrorCode == "ContributionPath",
            "discovery rejects contribution paths outside SharedResources");
    }
}
