using AuraShared.Core;
using AuraJourney.Shared;
using AuraMode.Shared;
using AuraOnline.Shared;
using AuraDirector.Shared;
using AuraRole.Shared;
using AuraGameData.Shared;
using Newtonsoft.Json.Linq;
internal static partial class CoreTestSuite
{
    public static void TestResourceProtocolV4()
    {
        var root = Path.Combine(tempRoot, "protocol-v4");
        var sources = Path.Combine(sourceRoot, "protocol-v4");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sources);
        File.WriteAllText(Path.Combine(sources, "first.png"), "first");
        File.WriteAllText(Path.Combine(sources, "second.png"), "second");
        using var v4Storage = new AuraSharedStorageCoordinator(root);
        var v4Packages = new AuraSharedPackageCoordinator(v4Storage);
        var v4 = new AuraSharedRegistrationCoordinator(v4Storage, v4Packages, "session-v4");
    
        AuraSharedResourceDeclarationV4 Resource(string id, string source, int priority) => new()
        {
            ModuleId = "CG",
            FeatureId = "Feast",
            ScopeType = "Role",
            ScopeId = "Content_role_a",
            ScopeOwnerModId = "Content",
            ScopeAliases = new List<string> { "role_a" },
            ResourceId = id,
            Source = source,
            FileName = "content.png",
            OriginKind = AuraSharedOriginKinds.ContentRegistered,
            WriterId = "Content",
            DefaultEnabled = true,
            Priority = priority
        };
    
        var manifest = new AuraSharedRegistrationManifestV4
        {
            OwnerModId = "Content",
            ParticipantKind = AuraSharedParticipantKinds.Content,
            PackageSourceKind = AuraSharedPackageSourceKinds.ModPackage,
            PackageId = "Content.Resources.V4",
            Resources = new List<AuraSharedResourceDeclarationV4>
            {
                Resource("first", "first.png", 20),
                Resource("second", "second.png", 10)
            }
        };
        var registered = v4.Register("Content", manifest, sources);
        var firstPath = "CG/Role/Content_role_a/Feast/Content/first/content.png";
        Assert(registered.Success && registered.Items.All(item => item.Success)
               && File.Exists(Path.Combine(root, firstPath.Replace('/', Path.DirectorySeparatorChar))),
            "v4 atomically registers resources into the canonical hierarchy");
        Assert(File.Exists(Path.Combine(root, "aura.shared.json"))
               && File.Exists(Path.Combine(root, "CG", "Role", "Content_role_a", "Feast", "aura.feature.json"))
               && File.Exists(Path.Combine(root, "_Registry", "V4", "Owners", "Content", "Content.Resources.V4.json")),
            "v4 writes layered metadata and a persistent registration");
    
        var longScope = new AuraSharedScopeKey
        {
            ModuleId = "Skin",
            FeatureId = "Skin",
            ScopeType = "Role",
            ScopeId = "Terrias_columbina_columbina"
        };
        var longSkin = new AuraSharedResourceDeclarationV4
        {
            ModuleId = longScope.ModuleId,
            FeatureId = longScope.FeatureId,
            ScopeType = longScope.ScopeType,
            ScopeId = longScope.ScopeId,
            ScopeOwnerModId = "Terrias",
            ResourceId = "Terrias.Terrias_columbina_columbina.restore_colors",
            Source = "first.png",
            FileName = "content.png",
            OriginKind = AuraSharedOriginKinds.ContentRegistered,
            WriterId = "Terrias"
        };
        var logicalSkinPath = AuraSharedResourcePathPolicy.ResourcePath(longScope, "Terrias", longSkin);
        var physicalSkinPath = AuraSharedResourcePathPolicy.StorageResourcePath(longScope, "Terrias", longSkin);
        Assert(logicalSkinPath.Contains("Terrias_columbina_columbina", StringComparison.Ordinal)
               && physicalSkinPath.StartsWith("Skin/_Store/", StringComparison.Ordinal)
               && physicalSkinPath.Length < logicalSkinPath.Length,
            "v4 keeps logical resource identity while compacting long physical paths");
        var deepRoot = Path.Combine(root, new string('d', 100));
        var shortResource = Resource("short", "first.png", 1);
        Assert(AuraSharedResourcePathPolicy.StorageResourcePath(
                   deepRoot, shortResource.Scope, "Content", shortResource)
               .StartsWith("CG/_Store/", StringComparison.Ordinal),
            "v4 includes the client root length when selecting bounded physical storage");
        var longSkinManifest = new AuraSharedRegistrationManifestV4
        {
            OwnerModId = "Terrias",
            ParticipantKind = AuraSharedParticipantKinds.Content,
            PackageSourceKind = AuraSharedPackageSourceKinds.ModPackage,
            PackageId = "Terrias.LongSkin.V4",
            PackageVersion = 1,
            Resources = new List<AuraSharedResourceDeclarationV4> { longSkin }
        };
        var longSkinRegistered = v4.Register("Terrias", longSkinManifest, sources);
        var logicalSkinResolved = v4.Resolve(logicalSkinPath);
        Assert(longSkinRegistered.Success
               && longSkinRegistered.Activated
               && longSkinRegistered.ExpectedItemCount == 1
               && longSkinRegistered.ProcessedItemCount == 1
               && logicalSkinResolved.Success
               && logicalSkinResolved.ResolvedPath.EndsWith(
                   physicalSkinPath.Replace('/', Path.DirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase),
            "v4 resolves stable logical paths to compact physical storage");
    
        using (var restartedStorage = new AuraSharedStorageCoordinator(root))
        {
            var restartedPackages = new AuraSharedPackageCoordinator(restartedStorage);
            var restarted = new AuraSharedRegistrationCoordinator(restartedStorage, restartedPackages, "session-v4-restart");
            var restartedRegistration = restarted.Register("Terrias", longSkinManifest, sources);
            Assert(restartedRegistration.Success
                   && restartedRegistration.Activated
                   && restarted.QueryCatalog(new AuraSharedCatalogQueryV4 { OwnerModId = "Terrias" }).Entries.Count == 1,
                "v4 reactivates a deduplicated compact resource after restart without a stale lease");
        }
    
        var legacySkin = new AuraSharedResourceDeclarationV4
        {
            ModuleId = "Skin",
            FeatureId = "Skin",
            ScopeType = "Role",
            ScopeId = "Legacy_columbina_columbina",
            ScopeOwnerModId = "LegacySkin",
            ResourceId = "LegacySkin.Legacy_columbina_columbina.restore_colors",
            Source = "second.png",
            FileName = "content.png",
            OriginKind = AuraSharedOriginKinds.ContentRegistered,
            WriterId = "LegacySkin"
        };
        legacySkin.Normalize();
        var legacyLogicalPath = AuraSharedResourcePathPolicy.ResourcePath(legacySkin.Scope, "LegacySkin", legacySkin);
        var legacySeed = v4Packages.Install(new AuraSharedInstallRequest
        {
            OwnerModId = "LegacySkin",
            System = "Skin",
            LogicalId = legacySkin.Scope.Key + ":LegacySkin:" + legacySkin.ResourceId,
            PackageId = "LegacySkin.Package",
            PackageVersion = 1,
            Kind = AuraSharedResourceKinds.File,
            SourcePath = Path.Combine(sources, "second.png"),
            DestinationRelativePath = legacyLogicalPath
        });
        var migrated = v4.Register("LegacySkin", new AuraSharedRegistrationManifestV4
        {
            OwnerModId = "LegacySkin",
            ParticipantKind = AuraSharedParticipantKinds.Content,
            PackageSourceKind = AuraSharedPackageSourceKinds.ModPackage,
            PackageId = "LegacySkin.Package",
            PackageVersion = 1,
            Resources = new List<AuraSharedResourceDeclarationV4> { legacySkin }
        }, sources);
        Assert(legacySeed.Success
               && migrated.Success
               && migrated.Items.Single().Status == "Relocated"
               && migrated.Items.Single().CanonicalPath.StartsWith("Skin/_Store/", StringComparison.Ordinal),
            "v4 transactionally relocates a legacy readable resource into compact physical storage");
    
        var overBudgetScopeId = new string('r', 180);
        var overBudgetRegistration = v4.Register("PathBudget", new AuraSharedRegistrationManifestV4
        {
            OwnerModId = "PathBudget",
            ParticipantKind = AuraSharedParticipantKinds.Content,
            PackageSourceKind = AuraSharedPackageSourceKinds.ModPackage,
            PackageId = "PathBudget.Package",
            PackageVersion = 1,
            Resources = new List<AuraSharedResourceDeclarationV4>
            {
                new()
                {
                    ModuleId = "Skin", FeatureId = "Skin", ScopeType = "Role", ScopeId = overBudgetScopeId,
                    ScopeOwnerModId = "PathBudget", ResourceId = "skin", Source = "first.png", FileName = "content.png",
                    OriginKind = AuraSharedOriginKinds.ContentRegistered, WriterId = "PathBudget"
                }
            }
        }, sources);
        Assert(!overBudgetRegistration.Success
               && !overBudgetRegistration.Activated
               && overBudgetRegistration.FailureCode == "PathBudgetExceeded"
               && overBudgetRegistration.FailedPathLength > AuraSharedStorageCoordinator.MaxPortablePathLength
               && v4.QueryCatalog(new AuraSharedCatalogQueryV4 { OwnerModId = "PathBudget" }).Entries.Count == 0,
            "v4 reports an exact path-budget failure and restores the active catalog atomically");
    
        var unregistered = v4.Resolve("CG/legacy.png");
        Assert(!unregistered.Success && unregistered.Outcome == "Unregistered",
            "v4 never resolves an unregistered raw file");
    
        var rejectedV3 = new AuraSharedRegistrationManifestV4
        {
            SchemaVersion = 3,
            OwnerModId = "OldContent",
            PackageId = "Old.V3"
        };
        var rejected = v4.Register("OldContent", rejectedV3, sources);
        Assert(!rejected.Success && rejected.Status == AuraSharedRegistrationStatuses.UnsupportedSchema,
            "v4 rejects v3 manifests without an adapter");
    
        var missingManifest = new AuraSharedRegistrationManifestV4
        {
            OwnerModId = "Missing",
            PackageId = "Missing.Resources.V4",
            Resources = new List<AuraSharedResourceDeclarationV4>
            {
                new()
                {
                    ModuleId = "CG", FeatureId = "Feast", ScopeType = "Role", ScopeId = "Missing_role",
                    ScopeOwnerModId = "Missing", ResourceId = "available", Source = "first.png",
                    OriginKind = AuraSharedOriginKinds.ContentRegistered, WriterId = "Missing"
                },
                new()
                {
                    ModuleId = "CG", FeatureId = "Feast", ScopeType = "Role", ScopeId = "Missing_role",
                    ScopeOwnerModId = "Missing", ResourceId = "missing", Source = "missing.png",
                    OriginKind = AuraSharedOriginKinds.ContentRegistered, WriterId = "Missing"
                }
            }
        };
        var atomicReject = v4.Register("Missing", missingManifest, sources);
        Assert(!atomicReject.Success
               && v4.QueryCatalog(new AuraSharedCatalogQueryV4 { OwnerModId = "Missing" }).Entries.Count == 0,
            "v4 rejects an unavailable package atomically");
    
        var scope = new AuraSharedScopeKey
        {
            ModuleId = "CG", FeatureId = "Feast", ScopeType = "Role", ScopeId = "Content_role_a"
        };
        var all = v4.ResolveEffective(scope, new AuraSharedLocalOverrideV4
        {
            Enabled = true,
            SelectionMode = AuraSharedSelectionModes.All
        });
        Assert(all.Resources.Count == 2 && all.Resources[0].ResourceId == "first",
            "v4 routes multiple enabled resources through the common selection pipeline");
        var disabled = v4.ResolveEffective(scope, new AuraSharedLocalOverrideV4
        {
            Enabled = true,
            SelectionMode = AuraSharedSelectionModes.All,
            ResourceOverrides = new Dictionary<string, bool> { ["Content:first"] = false }
        });
        Assert(disabled.Resources.Count == 1 && disabled.Resources[0].ResourceId == "second",
            "v4 sparse resource overrides control effective candidates");
        var overrideWrite = v4.WriteUserOverride(scope, "LocalUser", new AuraSharedLocalOverrideV4
        {
            Enabled = true,
            SelectionMode = AuraSharedSelectionModes.Sequential,
            ResourceOverrides = new Dictionary<string, bool> { ["Content:first"] = false }
        }, 0);
        var overrideJson = JObject.Parse(File.ReadAllText(Path.Combine(
            root, "CG", "Role", "Content_role_a", "Feast", "aura.user.json")));
        var configuredCatalog = v4.QueryCatalog(new AuraSharedCatalogQueryV4
        {
            ModuleId = "CG", FeatureId = "Feast", ScopeId = "Content_role_a"
        });
        Assert(overrideWrite.Success
               && overrideJson["schemaVersion"]!.Value<int>() == 4
               && overrideJson["selectionMode"]!.Value<string>() == AuraSharedSelectionModes.Sequential
               && overrideJson["override"] == null
               && configuredCatalog.Entries.Any(entry => entry.Resource.ResourceId == "first" && !entry.ConfiguredEnabled),
            "v4 writes a sparse flat aura.user.json and keeps disabled resources in the normal catalog");
    
        var manualSource = Path.Combine(sources, "manual.png");
        File.WriteAllText(manualSource, "manual");
        var manualRequest = new AuraSharedManualResourceRequestV4
        {
            OwnerModId = "Tool",
            WriterId = "LocalUser",
            SourcePath = manualSource,
            Resource = new AuraSharedResourceDeclarationV4
            {
                ModuleId = "CG", FeatureId = "Feast", ScopeType = "Role", ScopeId = "Content_role_a",
                ScopeOwnerModId = "Content", ScopeAliases = new List<string> { "role_a" }, ResourceId = "manual.local",
                Kind = AuraSharedResourceKinds.File, FileName = "content.png", OriginKind = AuraSharedOriginKinds.UserManual,
                WriterId = "LocalUser", Priority = 1000
            }
        };
        var imported = v4.UpsertManualResource("Tool", manualRequest);
        manualRequest.Archive = true;
        manualRequest.SourcePath = "";
        var archived = v4.UpsertManualResource("Tool", manualRequest);
        var history = v4.QueryCatalog(new AuraSharedCatalogQueryV4
        {
            Visibility = AuraSharedCatalogVisibilities.History,
            OwnerModId = "Tool"
        });
        Assert(imported.Success && archived.Success && history.Entries.Single().HistoryReasons.Contains(AuraSharedHistoryReasons.Archived),
            "v4 manual resources share the canonical tree and archive into the history view");
    
        manifest.Resources = new List<AuraSharedResourceDeclarationV4> { Resource("first", "first.png", 20) };
        manifest.PackageVersion++;
        var updated = v4.Register("Content", manifest, sources);
        var retired = v4.QueryCatalog(new AuraSharedCatalogQueryV4
        {
            Visibility = AuraSharedCatalogVisibilities.History,
            OwnerModId = "Content"
        });
        Assert(updated.Success && retired.Entries.Any(entry => entry.Resource.ResourceId == "second"
                && entry.HistoryReasons.Contains(AuraSharedHistoryReasons.Retired)),
            "v4 keeps removed declarations in history as retired resources");
    
        var nextSession = new AuraSharedRegistrationCoordinator(v4Storage, v4Packages, "session-v4-next");
        Assert(nextSession.QueryCatalog(new AuraSharedCatalogQueryV4()).Entries.Count == 0
               && nextSession.QueryCatalog(new AuraSharedCatalogQueryV4 { Visibility = AuraSharedCatalogVisibilities.History })
                   .Entries.All(entry => entry.HistoryReasons.Contains(AuraSharedHistoryReasons.InactiveOwner)),
            "v4 separates persistent history from active session leases");
    }
    
    public static void TestQualifiedResourceIdentityConflicts()
    {
        var root = Path.Combine(tempRoot, "qualified-identity");
        var sources = Path.Combine(sourceRoot, "qualified-identity");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sources);
        File.WriteAllText(Path.Combine(sources, "skin-a.txt"), "a");
        File.WriteAllText(Path.Combine(sources, "skin-b.txt"), "b");
    
        using var identityStorage = new AuraSharedStorageCoordinator(root);
        var identityPackages = new AuraSharedPackageCoordinator(identityStorage);
        var coordinator = new AuraSharedRegistrationCoordinator(identityStorage, identityPackages, "identity-session");
        AuraSharedRegistrationManifestV4 Manifest(string packageId, string source) => new()
        {
            OwnerModId = "ContentA",
            PackageId = packageId,
            Resources = new List<AuraSharedResourceDeclarationV4>
            {
                new()
                {
                    ModuleId = "Skin",
                    ScopeType = "Role",
                    ScopeId = "role-a",
                    FeatureId = "Skin",
                    ResourceId = "summer",
                    Source = source,
                    ScopeOwnerModId = "ContentA",
                    OriginKind = AuraSharedOriginKinds.ContentRegistered,
                    WriterId = "ContentA"
                }
            }
        };
    
        var first = coordinator.Register("ContentA", Manifest("ContentA.Skins", "skin-a.txt"), sources);
        var conflict = coordinator.Register("ContentA", Manifest("ContentA.AlternateSkins", "skin-b.txt"), sources);
        var catalog = coordinator.QueryCatalog(new AuraSharedCatalogQueryV4 { ModuleId = "Skin" });
        Assert(first.Items.Single().Success
               && !conflict.Items.Single().Success
               && conflict.Items.Single().Status == AuraSharedRegistrationStatuses.Invalid
               && catalog.Entries.Count == 1
               && catalog.Entries.Single().QualifiedResourceId == "Skin/Role/role-a/Skin/ContentA/summer",
            "qualified resource identity rejects a second active package from the same owner without hiding the valid entry");
    
        var otherOwner = Manifest("ContentB.Skins", "skin-b.txt");
        otherOwner.OwnerModId = "ContentB";
        var coexist = coordinator.Register("ContentB", otherOwner, sources);
        var candidates = coordinator.QueryCatalog(new AuraSharedCatalogQueryV4 { ModuleId = "Skin" }).Entries;
        Assert(coexist.Items.Single().Success
               && candidates.Count == 2
               && candidates.Select(entry => entry.SemanticResourceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
               && candidates.Select(entry => entry.QualifiedResourceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
            "different owners may contribute distinct qualified candidates to one semantic resource group");
    
        var unsafeIdentity = Manifest("ContentC.Skins", "skin-b.txt");
        unsafeIdentity.OwnerModId = "ContentC";
        unsafeIdentity.Resources[0].ResourceId = "summer/alternate";
        var unsafeResult = coordinator.Register("ContentC", unsafeIdentity, sources);
        Assert(!unsafeResult.Items.Single().Success
               && unsafeResult.Items.Single().Status == AuraSharedRegistrationStatuses.Invalid
               && coordinator.QueryCatalog(new AuraSharedCatalogQueryV4 { OwnerModId = "ContentC" }).Entries.Count == 0,
            "resource registration rejects identity segments that would collapse to the same canonical directory");
    }
    
    public static void TestRoleRegistryContracts()
    {
        var document = new AuraRoleRegistryDocument();
        var runtime = new AuraRoleRegistryContribution
        {
            ContributorModId = "Tool",
            ContributionId = "game-scan",
            SessionId = "session-a",
            Entries = new List<AuraRoleRegistryEntry>
            {
                new() { RoleId = "7", DisplayName = "Koko", Priority = 0 },
                new() { RoleId = "Mod_role", DisplayName = "Runtime Role", Priority = 0 }
            }
        };
        Assert(document.ReplaceContribution(runtime), "role registry accepts a runtime contribution");
        Assert(!document.ReplaceContribution(runtime), "role registry contribution replacement is idempotent");
        Assert(document.BuildActiveEntries("session-b").Count == 0,
            "role registry excludes stale runtime contributions from older sessions");
        var manifest = new AuraRoleRegistryContribution
        {
            ContributorModId = "Content",
            ContributionId = "manifest",
            SessionId = "session-a",
            Persistent = true,
            Entries = new List<AuraRoleRegistryEntry>
            {
                new()
                {
                    RoleId = "Mod_role",
                    OwnerModId = "Content",
                    DisplayName = "Declared Role",
                    Aliases = new List<string> { "role" },
                    Priority = 100
                },
                new()
                {
                    RoleId = "Mod_role",
                    OwnerModId = "Content",
                    Aliases = new List<string> { "role-alternate" },
                    Priority = 90
                }
            }
        };
        Assert(document.ReplaceContribution(manifest), "role registry accepts a persistent declaration");
        var active = document.BuildActiveEntries("session-a");
        Assert(active.Count == 2
               && active.Any(role => role.RoleId == "career_7")
               && active.Single(role => role.RoleId == "Mod_role").DisplayName == "Declared Role"
               && active.Single(role => role.RoleId == "Mod_role").Aliases.Contains("role")
               && active.Single(role => role.RoleId == "Mod_role").Aliases.Contains("role-alternate"),
            "role registry preserves duplicate semantic contributions and merges their metadata by explicit priority");
    
        var effective = AuraEffectiveRoleCatalog.Merge(
            new[]
            {
                new AuraRoleRegistryEntry { RoleId = "career_7", OwnerModId = "BaseGame", DisplayName = "Runtime Koko" },
                new AuraRoleRegistryEntry { RoleId = "Mod_role", OwnerModId = "Mod", DisplayName = "Runtime Role" }
            },
            active.Concat(new[]
            {
                new AuraRoleRegistryEntry
                {
                    RoleId = "DisabledMod_role",
                    OwnerModId = "DisabledMod",
                    DisplayName = "Disabled Role",
                    Priority = 200
                }
            }));
        Assert(effective.Count == 2
               && effective.All(role => role.RoleId != "DisabledMod_role")
               && effective.Single(role => role.RoleId == "Mod_role").DisplayName == "Declared Role"
               && effective.Single(role => role.RoleId == "Mod_role").Aliases.Contains("role-alternate"),
            "effective role catalog enriches only roles present in the current native career snapshot");
    }
}
