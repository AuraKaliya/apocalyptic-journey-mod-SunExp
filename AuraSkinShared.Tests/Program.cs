using AuraShared.Core;
using AuraSkin.Shared.Infrastructure;
using AuraSkin.Shared.Models;
using AuraSkin.Shared.Services;
using Newtonsoft.Json;

var assertions = 0;
var root = Path.Combine(Path.GetTempPath(), "aura-skin-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    AuraSharedPaths.SkinDirectory = Path.Combine(root, "skins");
    AuraSharedPaths.RegistriesRootDirectory = Path.Combine(root, "registry");
    AuraSharedPaths.ConfigDirectory = Path.Combine(root, "config");
    Directory.CreateDirectory(AuraSharedPaths.SkinDirectory);
    Directory.CreateDirectory(AuraSharedPaths.RegistriesRootDirectory);
    Directory.CreateDirectory(AuraSharedPaths.ConfigDirectory);

    var ownerAPath = CreateSkin("owner-a", "OwnerA", "career_1", "summer", "A Summer");
    var ownerBPath = CreateSkin("owner-b", "OwnerB", "career_1", "summer", "B Summer");
    AuraSharedResourceProtocol.Paths["skin/owner-a"] = ownerAPath;
    AuraSharedResourceProtocol.Paths["skin/owner-b"] = ownerBPath;
    SkinPackageInstaller.ActiveResources.Add(Resource("OwnerA", "skin/owner-a", 10));
    SkinPackageInstaller.ActiveResources.Add(Resource("OwnerB", "skin/owner-b", 20));

    SkinRegistry.Reload();
    var candidates = SkinRegistry.GetCandidates("1", "summer");
    Assert(candidates.Count == 2, "semantic duplicates from different owners are retained");
    Assert(candidates[0].OwnerModId == "OwnerB", "candidate priority is deterministic");
    Assert(SkinRegistry.Find("career_1", "summer")?.OwnerModId == "OwnerB", "bare references use candidate order");
    Assert(SkinRegistry.FindQualified("OwnerA:career_1:summer")?.OwnerModId == "OwnerA", "qualified identity resolves exactly");
    Assert(SkinRegistry.ResolveReference("career_1", "OwnerA:summer")?.OwnerModId == "OwnerA", "legacy owner-qualified reference stays deterministic");

    SkinRegistry.ConfigureCandidateEnablement(true, new[] { "OwnerA:career_1:summer" });
    Assert(SkinRegistry.GetForCareer("career_1").Single().OwnerModId == "OwnerA", "tool-managed candidate enablement filters effective candidates");
    Assert(SkinRegistry.GetAllForCareer("career_1").Count == 2, "candidate enablement does not delete registrations");
    SkinRegistry.ConfigureCandidateEnablement(false, null);
    Assert(SkinRegistry.GetForCareer("career_1").Count == 2, "unconfigured selection keeps content candidates enabled");

    SkinRegistry.ConfigureCandidateOverrides(new[]
    {
        new KeyValuePair<string, bool>("OwnerB:career_1:summer", false)
    });
    Assert(SkinRegistry.Find("career_1", "summer")?.OwnerModId == "OwnerA", "explicit candidate override changes effective resolution only");
    SkinRegistry.ConfigureCandidateEnablement(false, null);

    var manifestPath = Path.Combine(ownerAPath, "skin.json");
    Assert(SkinPaths.ResolveManifestAsset(manifestPath, "Character", false).EndsWith("Character.png", StringComparison.OrdinalIgnoreCase),
        "manifest assets resolve supported implicit extensions");
    File.WriteAllText(Path.Combine(root, "outside.png"), "outside");
    Assert(SkinPaths.ResolveManifestAsset(manifestPath, "../outside.png", false) == "", "manifest assets cannot escape their skin directory");

    AuraSharedConfigStore.Reset();
    SkinSelectionStore.Load();
    SkinSelectionStore.Set("1", " OwnerA:career_1:summer ");
    Assert(SkinSelectionStore.Get("career_1") == "OwnerA:career_1:summer", "selections normalize career ids and trim qualified identities");
    Assert(SkinSelectionStore.TryRemapSelection("career_1", "summer", "OwnerB:career_1:summer"), "explicit remapping accepts a matching legacy suffix");
    Assert(SkinSelectionStore.Get("career_1") == "OwnerB:career_1:summer", "selection remapping persists the new qualified identity");
    SkinSelectionStore.Set("career_1", "");
    Assert(SkinSelectionStore.Get("career_1") == "", "empty selection restores the native default");

    Assert(SkinProtocolCompatibility.IsCompatible(7, 7, 7, 7), "equal skin protocol ranges are compatible");
    Assert(SkinProtocolCompatibility.IsCompatible(9, 7, 10, 8), "overlapping skin protocol ranges are compatible");
    Assert(!SkinProtocolCompatibility.IsCompatible(9, 7, 6, 5), "remote protocols below the local minimum are rejected");
    Assert(!SkinProtocolCompatibility.IsCompatible(9, 7, 12, 10), "remote minimums above the local current version are rejected");
    Assert(!SkinProtocolCompatibility.IsCompatible(9, 7, 7, 8), "invalid remote protocol ranges are rejected");
    Assert(!SkinProtocolCompatibility.IsCompatible(0, 0, 7, 7), "invalid local protocol ranges are rejected");

    Assert(!SkinPackageValidationPolicy.TryValidateManifest(null, out _), "missing package manifests are rejected");
    Assert(!SkinPackageValidationPolicy.TryValidateManifest(Package(schemaVersion: 2), out _), "unsupported package schemas are rejected");
    Assert(!SkinPackageValidationPolicy.TryValidateManifest(Package(packageId: " "), out _), "empty package identities are rejected");
    Assert(!SkinPackageValidationPolicy.TryValidateManifest(Package(packageVersion: 0), out _), "non-positive package versions are rejected");
    Assert(!SkinPackageValidationPolicy.TryValidateManifest(Package(includeResource: false), out _), "packages without resources are rejected");
    Assert(SkinPackageValidationPolicy.TryValidateManifest(Package(), out var packageError) && packageError == "",
        "well-formed package manifests pass preflight validation");

    var packageDirectory = Path.Combine(root, "package");
    var sourceDirectory = Path.Combine(packageDirectory, "character", "summer");
    Directory.CreateDirectory(sourceDirectory);
    Assert(SkinPackageValidationPolicy.TryResolveSourceDirectory(
            packageDirectory,
            "character/summer",
            out var relativeSource,
            out var resolvedSource,
            out var sourceError)
           && relativeSource == "character/summer"
           && Path.GetFullPath(resolvedSource) == Path.GetFullPath(sourceDirectory)
           && sourceError == "",
        "existing relative package sources pass preflight validation");
    Assert(!SkinPackageValidationPolicy.TryResolveSourceDirectory(packageDirectory, "", out _, out _, out _),
        "empty package sources are rejected");
    Assert(!SkinPackageValidationPolicy.TryResolveSourceDirectory(packageDirectory, Path.GetFullPath(sourceDirectory), out _, out _, out _),
        "rooted package sources are rejected");
    Assert(!SkinPackageValidationPolicy.TryResolveSourceDirectory(packageDirectory, "../outside", out _, out _, out _),
        "package source traversal is rejected");
    Assert(!SkinPackageValidationPolicy.TryResolveSourceDirectory(packageDirectory, "character/missing", out _, out _, out _),
        "missing package sources are rejected");

    Console.WriteLine($"AuraSkinShared tests passed: {assertions} assertions.");
}
finally
{
    SkinPackageInstaller.ActiveResources.Clear();
    AuraSharedResourceProtocol.Paths.Clear();
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

string CreateSkin(string directoryName, string owner, string careerId, string skinId, string name)
{
    var directory = Path.Combine(root, directoryName);
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "Character.png"), owner);
    File.WriteAllText(Path.Combine(directory, "skin.json"), JsonConvert.SerializeObject(new
    {
        schemaVersion = 2,
        enabled = true,
        targetCareerId = careerId,
        skinId,
        name,
        preview = "Character.png",
        assets = new { CareerImage = "Character.png" }
    }));
    return directory;
}

SkinPackageInstaller.RegisteredSkinResource Resource(string owner, string canonicalPath, int priority)
{
    return new SkinPackageInstaller.RegisteredSkinResource
    {
        OwnerModId = owner,
        PackageId = owner + ".Skins",
        PackageVersion = 1,
        Priority = priority,
        TargetCareerId = "career_1",
        SkinId = "summer",
        CanonicalRelativePath = canonicalPath
    };
}

SkinPackageManifest Package(
    int schemaVersion = 1,
    string packageId = "Owner.Skins",
    int packageVersion = 1,
    bool includeResource = true)
{
    return new SkinPackageManifest
    {
        SchemaVersion = schemaVersion,
        PackageId = packageId,
        PackageVersion = packageVersion,
        Resources = includeResource
            ? new List<SkinPackageResource> { new() { Source = "character/summer" } }
            : new List<SkinPackageResource>()
    };
}

void Assert(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed: " + name);
    }
    assertions++;
}
