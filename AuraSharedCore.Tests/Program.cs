using AuraShared.Core;
using AuraJourney.Shared;
using AuraMode.Shared;
using AuraOnline.Shared;
using AuraDirector.Shared;
using AuraRole.Shared;
using Newtonsoft.Json.Linq;

var assertions = 0;
var tempRoot = Path.Combine(Path.GetTempPath(), "AuraSharedCore.Tests", Guid.NewGuid().ToString("N"));
var sourceRoot = Path.Combine(Path.GetTempPath(), "AuraSharedCore.Tests.Sources", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
Directory.CreateDirectory(sourceRoot);

try
{
    using var storage = new AuraSharedStorageCoordinator(tempRoot);
    var packages = new AuraSharedPackageCoordinator(storage);
    var editable = new AuraSharedEditableResourceCoordinator(tempRoot);

    var first = storage.Write(new AuraSharedStorageRequest
    {
        Scope = AuraSharedStorageScopes.Shared,
        System = "Test",
        FileName = "shared.json",
        WriterId = "TestAuthority",
        AuthorityId = "TestAuthority",
        ExpectedRevision = 0,
        PayloadJson = "{\"value\":1}"
    });
    Assert(first.Success && first.Revision == 1, "first shared write");

    var snapshot = storage.Read(new AuraSharedStorageRequest
    {
        Scope = AuraSharedStorageScopes.Shared,
        System = "Test",
        FileName = "shared.json"
    });
    Assert(snapshot.Success && snapshot.Found && JObject.Parse(snapshot.PayloadJson)["value"]!.Value<int>() == 1, "shared snapshot read");
    Assert(storage.StorageLockKey(new AuraSharedStorageRequest
    {
        Scope = AuraSharedStorageScopes.Shared,
        System = "Test",
        FileName = "shared.json"
    }).Contains("Config/Shared"), "storage document lock key");

    var unauthorized = storage.Write(new AuraSharedStorageRequest
    {
        Scope = AuraSharedStorageScopes.Shared,
        System = "Test",
        FileName = "shared.json",
        WriterId = "OtherWriter",
        ExpectedRevision = 1,
        PayloadJson = "{\"value\":2}"
    });
    Assert(!unauthorized.Success && unauthorized.Conflict, "shared authority rejection");

    var stale = storage.Write(new AuraSharedStorageRequest
    {
        Scope = AuraSharedStorageScopes.Shared,
        System = "Test",
        FileName = "shared.json",
        WriterId = "TestAuthority",
        ExpectedRevision = 0,
        PayloadJson = "{\"value\":2}"
    });
    Assert(!stale.Success && stale.Conflict && stale.Revision == 1, "revision conflict");

    var wrongOwner = storage.Write(new AuraSharedStorageRequest
    {
        Scope = AuraSharedStorageScopes.Owner,
        System = "Test",
        OwnerModId = "OwnerA",
        FileName = "owner.json",
        WriterId = "OwnerB",
        ExpectedRevision = 0,
        PayloadJson = "{}"
    });
    Assert(!wrongOwner.Success && wrongOwner.Conflict, "owner writer isolation");

    var invalidDocumentPath = Path.Combine(tempRoot, "Config", "Shared", "Invalid", "raw.json");
    Directory.CreateDirectory(Path.GetDirectoryName(invalidDocumentPath)!);
    File.WriteAllText(invalidDocumentPath, "{\"oldRawFormat\":true}");
    var replacedInvalid = storage.Write(new AuraSharedStorageRequest
    {
        Scope = AuraSharedStorageScopes.Shared,
        System = "Invalid",
        FileName = "raw.json",
        WriterId = "RepairAuthority",
        ExpectedRevision = 0,
        PayloadJson = "{\"valid\":true}"
    });
    Assert(replacedInvalid.Success
           && Directory.EnumerateFiles(Path.Combine(tempRoot, "Backups", "Storage", "Invalid"), "*.invalid", SearchOption.AllDirectories).Any(),
        "invalid document quarantine");

    var sourceFile = Path.Combine(sourceRoot, "audio.wav");
    File.WriteAllText(sourceFile, "v1");
    var install = packages.Install(Request("OwnerA", "Audio", "voice", "PackA", 1, sourceFile, "Audio/Test/voice.wav"));
    Assert(install.Success && install.Changed && install.Status == "Installed", "file resource install");
    Assert(OperationLogContains(tempRoot, "InstallResource", "ContentCommitted")
           && OperationLogContains(tempRoot, "InstallResource", "RegistryCommitted")
           && OperationLogContains(tempRoot, "InstallResource", "Completed"),
        "install operation log phases");

    var duplicate = packages.Install(Request("OwnerA", "Audio", "voice", "PackA", 1, sourceFile, "Audio/Test/voice.wav"));
    Assert(duplicate.Success && !duplicate.Changed && duplicate.Status == "Deduplicated", "same owner deduplication");

    var secondOwner = packages.Install(Request("OwnerB", "Audio", "voice", "PackB", 1, sourceFile, "Audio/Test/voice.wav"));
    Assert(secondOwner.Success && !secondOwner.Changed, "equal content cross-owner deduplication");

    var bootstrapSummary = AuraSharedBootstrapResult.FromResponses(new[]
    {
        install,
        duplicate,
        new AuraSharedInstallResponse { Success = true, Changed = true, Status = "Repaired" },
        new AuraSharedInstallResponse { Success = false, Conflict = true, Status = "Conflict" }
    });
    Assert(!bootstrapSummary.Success
           && bootstrapSummary.Changed
           && bootstrapSummary.Installed == 1
           && bootstrapSummary.Repaired == 1
           && bootstrapSummary.Deduplicated == 1
           && bootstrapSummary.Conflicts == 1,
        "resource bootstrap aggregates multi-Mod install outcomes");

    File.WriteAllText(sourceFile, "different owner content");
    var crossOwnerConflict = packages.Install(Request("OwnerC", "Audio", "voice", "PackC", 2, sourceFile, "Audio/Test/voice.wav"));
    Assert(!crossOwnerConflict.Success && crossOwnerConflict.Conflict, "cross-owner content conflict");

    var updateSource = Path.Combine(sourceRoot, "update.wav");
    File.WriteAllText(updateSource, "one");
    Assert(packages.Install(Request("OwnerA", "Audio", "update", "PackUpdate", 1, updateSource, "Audio/Test/update.wav")).Success, "update baseline");
    File.WriteAllText(updateSource, "two");
    var sameVersionConflict = packages.Install(Request("OwnerA", "Audio", "update", "PackUpdate", 1, updateSource, "Audio/Test/update.wav"));
    Assert(!sameVersionConflict.Success && sameVersionConflict.Conflict, "same version content conflict");
    var updated = packages.Install(Request("OwnerA", "Audio", "update", "PackUpdate", 2, updateSource, "Audio/Test/update.wav"));
    Assert(updated.Success && updated.Changed && updated.Status == "Updated", "higher version update");

    var editableSeed = Path.Combine(sourceRoot, "editable-default.png");
    File.WriteAllText(editableSeed, "seed-v1");
    var editableRequest = new AuraSharedEditableResourceRequest
    {
        OwnerModId = "Tool",
        System = "CG",
        LogicalId = "feast.role-a",
        SourcePath = editableSeed,
        DestinationRelativePath = "CG/Overrides/Tool/role-a/feast_cg.png"
    };
    var editableCreated = editable.Seed(editableRequest);
    Assert(editableCreated.Success && editableCreated.Changed && !editableCreated.Customized
           && editableCreated.Status == AuraSharedEditableResourceStatuses.Created,
        "editable resource creates a missing working copy");
    var editableExisting = editable.Seed(editableRequest);
    Assert(editableExisting.Success && !editableExisting.Changed && !editableExisting.Customized
           && editableExisting.Status == AuraSharedEditableResourceStatuses.ExistingDefault,
        "editable resource seed is idempotent");
    var editablePath = Path.Combine(tempRoot, "CG", "Overrides", "Tool", "role-a", "feast_cg.png");
    File.WriteAllText(editablePath, "user-customized");
    editableRequest.PreviousSeedHash = editableCreated.SeedHash;
    var editableCustomized = editable.Seed(editableRequest);
    Assert(editableCustomized.Success && !editableCustomized.Changed && editableCustomized.Customized
           && File.ReadAllText(editablePath) == "user-customized",
        "editable resource preserves a user replacement");
    File.WriteAllText(editableSeed, "seed-v2");
    var editableStillCustomized = editable.Seed(editableRequest);
    Assert(editableStillCustomized.Customized && File.ReadAllText(editablePath) == "user-customized",
        "editable resource template updates do not replace custom content");
    editableRequest.ForceReset = true;
    var editableReset = editable.Seed(editableRequest);
    Assert(editableReset.Success && editableReset.Changed && !editableReset.Customized
           && editableReset.Status == AuraSharedEditableResourceStatuses.Reset
           && File.ReadAllText(editablePath) == "seed-v2"
           && File.Exists(editableReset.BackupPath),
        "editable resource reset is explicit and recoverable");

    var untouchedRequest = new AuraSharedEditableResourceRequest
    {
        OwnerModId = "Tool",
        System = "CG",
        LogicalId = "feast.role-b",
        SourcePath = editableSeed,
        DestinationRelativePath = "CG/Overrides/Tool/role-b/feast_cg.png"
    };
    var untouchedCreated = editable.Seed(untouchedRequest);
    File.WriteAllText(editableSeed, "seed-v3");
    untouchedRequest.PreviousSeedHash = untouchedCreated.SeedHash;
    var untouchedUpdated = editable.Seed(untouchedRequest);
    Assert(untouchedUpdated.Success && untouchedUpdated.Changed && !untouchedUpdated.Customized
           && untouchedUpdated.Status == AuraSharedEditableResourceStatuses.UpdatedDefault
           && File.ReadAllText(untouchedUpdated.InstalledPath) == "seed-v3",
        "editable resource updates an untouched older seed");

    var stagedEditable = editable.StageTemporary("Tool", "feast-import", "png", new byte[] { 1, 2, 3 });
    Assert(File.Exists(stagedEditable)
           && stagedEditable.StartsWith(Path.Combine(tempRoot, "Cache", "Editable", "External"), StringComparison.OrdinalIgnoreCase),
        "editable resource stages generated bytes inside the shared cache");
    editable.ReleaseTemporary(stagedEditable);
    Assert(!File.Exists(stagedEditable), "editable resource releases only staged temporary files");

    var directorySource = Path.Combine(sourceRoot, "skin");
    Directory.CreateDirectory(Path.Combine(directorySource, "Idle"));
    File.WriteAllText(Path.Combine(directorySource, "skin.json"), "{}");
    File.WriteAllText(Path.Combine(directorySource, "Idle", "Idle_00.png"), "frame");
    var directoryInstall = packages.Install(new AuraSharedInstallRequest
    {
        OwnerModId = "OwnerA",
        System = "Skin",
        LogicalId = "career::skin",
        PackageId = "SkinPack",
        PackageVersion = 1,
        Kind = AuraSharedResourceKinds.Directory,
        SourcePath = directorySource,
        DestinationRelativePath = "Skins/career/skin"
    });
    Assert(directoryInstall.Success && File.Exists(Path.Combine(tempRoot, "Skins", "career", "skin", "Idle", "Idle_00.png")), "directory install");

    using var storage2 = new AuraSharedStorageCoordinator(tempRoot);
    await Task.WhenAll(Enumerable.Range(0, 20).Select(index => Task.Run(() => Increment(index % 2 == 0 ? storage2 : storage))));
    var counter = storage.Read(new AuraSharedStorageRequest
    {
        Scope = AuraSharedStorageScopes.Shared,
        System = "Concurrency",
        FileName = "counter.json"
    });
    Assert(counter.Success && JObject.Parse(counter.PayloadJson)["value"]!.Value<int>() == 20, "two coordinator CAS concurrency");

    TestRecovery(storage, packages);
    Assert(true, "transaction recovery");

    TestIdentityContracts();
    TestResourceProtocolV3();
    TestRoleRegistryContracts();
    TestSecureEnvelopeContracts();
    TestLifecycleContracts();
    TestJourneyContracts();
    TestOnlineChatContracts();
    TestAuthoritativeSyncContracts();
    TestObjectPoolContracts();
    TestModeContracts();
    TestDirectorContracts();

    Console.WriteLine($"AuraSharedCore tests passed: {assertions} assertions.");
}
finally
{
    TryDelete(tempRoot);
    TryDelete(sourceRoot);
}

return;

void TestResourceProtocolV3()
{
    var root = Path.Combine(tempRoot, "protocol-v3");
    var sources = Path.Combine(sourceRoot, "protocol-v3");
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(sources, "CG"));
    var source = Path.Combine(sources, "CG", "default.png");
    File.WriteAllText(source, "packaged-default");
    var legacyRelative = "CG/Tool/Templates/Feast/Roles/role_a/feast_cg.png";
    var legacy = Path.Combine(root, legacyRelative.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
    File.WriteAllText(legacy, "user-customized");

    using var v3Storage = new AuraSharedStorageCoordinator(root);
    var v3Packages = new AuraSharedPackageCoordinator(v3Storage);
    var v3 = new AuraSharedRegistrationCoordinator(v3Storage, v3Packages, "session-a");
    var content = new AuraSharedRegistrationManifestV3
    {
        OwnerModId = "Content",
        ParticipantKind = AuraSharedParticipantKinds.Content,
        PackageId = "Content.Resources",
        PackageVersion = 1,
        Resources = new List<AuraSharedResourceDeclarationV3>
        {
            new()
            {
                ModuleId = "CG",
                FeatureId = "Feast",
                ScopeType = "Role",
                ScopeId = "role_a",
                ResourceId = "role-a.feast",
                Source = "CG/default.png",
                FileName = "content.png",
                LegacyPaths = new List<string> { legacyRelative },
                Priority = 20,
                EffectMode = AuraSharedEffectModes.Additive,
                MissingPolicy = AuraSharedMissingPolicies.Skip
            }
        },
        Defaults = new List<AuraSharedDefaultProfileV3>
        {
            new()
            {
                ModuleId = "CG",
                FeatureId = "Feast",
                ScopeType = "Role",
                ScopeId = "role_a",
                ProfileId = "content-default",
                ResourceOwnerModId = "Content",
                ResourceId = "role-a.feast"
            }
        }
    };
    var registered = v3.Register("Content", content, sources);
    var canonicalRelative = "CG/Role/role_a/Feast/Content/role-a.feast/content.png";
    var canonical = Path.Combine(root, canonicalRelative.Replace('/', Path.DirectorySeparatorChar));
    Assert(registered.Success
           && registered.Items.Single().Status == "PreservedLocal"
           && File.ReadAllText(canonical) == "user-customized",
        "v3 registration migrates and preserves a legacy user customization");
    Assert(File.Exists(Path.Combine(root, "CG", "aura.module.json"))
           && File.Exists(Path.Combine(root, "CG", "Role", "role_a", "Feast", "aura.feature.json"))
           && File.Exists(Path.Combine(root, "CG", "Role", "role_a", "Feast", "Content", "aura.defaults.json"))
           && File.Exists(Path.Combine(root, "CG", "Role", "role_a", "Feast", "Content", "role-a.feast", "aura.resource.json"))
           && File.Exists(Path.Combine(root, "CG", "Role", "role_a", "Feast", "Content", "role-a.feast", "aura.state.json")),
        "v3 registration writes layered module feature provider and resource metadata");
    Assert(File.Exists(Path.Combine(root, "_Runtime", "Leases", "session-a", "Content", "Content.Resources.json"))
           && File.Exists(Path.Combine(root, "_Migration", "V2ToV3", "Content", "journal.json")),
        "v3 registration records an active lease and migration journal");
    var compatibility = v3.Resolve(legacyRelative);
    Assert(compatibility.Success && compatibility.Active && !compatibility.UsedLegacyPath
           && string.Equals(compatibility.ResolvedPath, canonical, StringComparison.OrdinalIgnoreCase),
        "v3 resolver maps a legacy path to the canonical active resource");
    var stableRevision = registered.Revision;
    var repeated = v3.Register("Content", content, sources);
    Assert(repeated.Success && repeated.Revision == stableRevision && repeated.ChangedScopeKeys.Count == 0,
        "v3 registration is idempotent for an unchanged owner package");

    var siblingPackage = new AuraSharedRegistrationManifestV3
    {
        OwnerModId = "Content",
        ParticipantKind = AuraSharedParticipantKinds.Content,
        PackageId = "Content.Skins",
        Resources = new List<AuraSharedResourceDeclarationV3>()
    };
    var siblingRegistered = v3.Register("Content", siblingPackage, sources);
    Assert(siblingRegistered.Success && v3.GetActiveLeases().Length == 2,
        "v3 keeps multiple packages from the same owner active in one session");

    var tool = new AuraSharedRegistrationManifestV3
    {
        OwnerModId = "Tool",
        ParticipantKind = AuraSharedParticipantKinds.Tool,
        PackageId = "Tool.Defaults",
        PackageVersion = 1,
        Resources = new List<AuraSharedResourceDeclarationV3>
        {
            new()
            {
                ModuleId = "CG",
                FeatureId = "Feast",
                ScopeType = "Role",
                ScopeId = "role_a",
                ResourceId = "role-a.feast",
                Source = "CG/default.png",
                FileName = "content.png",
                Priority = 5,
                EffectMode = AuraSharedEffectModes.Additive,
                MissingPolicy = AuraSharedMissingPolicies.Skip
            }
        },
        Defaults = new List<AuraSharedDefaultProfileV3>
        {
            new()
            {
                ModuleId = "CG",
                FeatureId = "Feast",
                ScopeType = "Role",
                ScopeId = "role_a",
                ProfileId = "tool-default",
                Priority = 100,
                ResourceOwnerModId = "Content",
                ResourceId = "role-a.feast"
            }
        }
    };
    var lateTool = v3.Register("Tool", tool, sources);
    Assert(lateTool.Success && lateTool.ChangedScopeKeys.Single() == "CG:Feast:Role:role_a"
           && v3.GetActiveLeases().Length == 3
           && File.Exists(Path.Combine(root, "CG", "Role", "role_a", "Feast", "Tool", "role-a.feast", "content.png"))
           && lateTool.Revision > stableRevision,
        "late tool registration keeps sibling leases and allows the same resource id under another owner");
    var effective = v3.ResolveEffective(new AuraSharedScopeKey
    {
        ModuleId = "CG",
        FeatureId = "Feast",
        ScopeType = "Role",
        ScopeId = "role_a"
    });
    Assert(effective.ConfigSource == "ToolDefault"
           && effective.ConfigOwnerModId == "Tool"
           && effective.ResourceOwnerModId == "Content"
           && effective.ResourceId == "role-a.feast",
        "tool default config priority is independent from formal resource ownership priority");
    var localDisabled = v3.ResolveEffective(new AuraSharedScopeKey
    {
        ModuleId = "CG",
        FeatureId = "Feast",
        ScopeType = "Role",
        ScopeId = "role_a"
    }, new AuraSharedLocalOverrideV3 { Enabled = false });
    Assert(localDisabled.ConfigSource == "LocalUser" && !localDisabled.Enabled && localDisabled.Outcome == "Disabled",
        "sparse local user override wins over tool and content defaults");
    var scope = new AuraSharedScopeKey
    {
        ModuleId = "CG",
        FeatureId = "Feast",
        ScopeType = "Role",
        ScopeId = "role_a"
    };
    var persistedOverride = v3.WriteUserOverride(
        scope,
        "Tool",
        new AuraSharedLocalOverrideV3 { Enabled = false },
        expectedRevision: 0);
    var overrideConflict = v3.WriteUserOverride(
        scope,
        "Tool",
        new AuraSharedLocalOverrideV3 { Enabled = true },
        expectedRevision: 0);
    var persistedEffective = v3.ResolveEffective(scope);
    Assert(persistedOverride.Success && persistedOverride.Revision == 1
           && overrideConflict.Conflict && overrideConflict.Revision == 1
           && File.Exists(Path.Combine(root, "CG", "Role", "role_a", "Feast", "aura.user.json"))
           && persistedEffective.ConfigSource == "LocalUser" && !persistedEffective.Enabled,
        "v3 sparse user override is persisted at the feature scope with CAS protection");

    Directory.CreateDirectory(Path.Combine(sources, "Audio", "RoleA"));
    File.WriteAllText(Path.Combine(sources, "Audio", "RoleA", "voice.ogg"), "ogg-data");
    var legacyAudioDirectory = Path.Combine(root, "Audio", "AudioContent", "RoleA");
    Directory.CreateDirectory(legacyAudioDirectory);
    File.WriteAllText(Path.Combine(legacyAudioDirectory, "voice.ogg"), "custom-legacy-ogg");
    var audioOwner = new AuraSharedRegistrationManifestV3
    {
        OwnerModId = "AudioContent",
        ParticipantKind = AuraSharedParticipantKinds.Content,
        PackageId = "AudioContent.Resources",
        Resources = new List<AuraSharedResourceDeclarationV3>
        {
            new()
            {
                ModuleId = "Audio",
                FeatureId = "Voice",
                ScopeType = "Role",
                ScopeId = "role_a",
                ResourceId = "voice-pack",
                Kind = AuraSharedResourceKinds.Directory,
                Source = "Audio/RoleA",
                LegacyPaths = new List<string> { "Audio/AudioContent/RoleA" },
                EffectMode = AuraSharedEffectModes.Replacement,
                MissingPolicy = AuraSharedMissingPolicies.NativeFallback
            }
        }
    };
    Assert(v3.Register("AudioContent", audioOwner, sources).Success, "v3 directory resource registration");
    var legacyChild = v3.Resolve("Audio/AudioContent/RoleA/voice.ogg");
    Assert(legacyChild.Success && legacyChild.Active && legacyChild.UsedLegacyPath
           && string.Equals(legacyChild.ResolvedPath, Path.Combine(legacyAudioDirectory, "voice.ogg"), StringComparison.OrdinalIgnoreCase),
        "v3 resolver preserves and prefers a customized registered legacy directory");
    var canonicalAudioDirectory = Path.Combine(root, "Audio", "Role", "role_a", "Voice", "AudioContent", "voice-pack", "content");
    File.WriteAllText(Path.Combine(canonicalAudioDirectory, "voice.ogg"), "custom-v3-ogg");
    Assert(v3.Register("AudioContent", audioOwner, sources).Success, "v3 directory resource re-registration");
    var canonicalCustomizedChild = v3.Resolve("Audio/AudioContent/RoleA/voice.ogg");
    Assert(canonicalCustomizedChild.Success && !canonicalCustomizedChild.UsedLegacyPath
           && string.Equals(canonicalCustomizedChild.ResolvedPath, Path.Combine(canonicalAudioDirectory, "voice.ogg"), StringComparison.OrdinalIgnoreCase),
        "v3 canonical directory customization wins over an older legacy directory");

    var nextSession = new AuraSharedRegistrationCoordinator(v3Storage, v3Packages, "session-b");
    var residual = nextSession.Resolve(canonicalRelative);
    Assert(residual.Success && !residual.Active && residual.Outcome == "LegacyUnregistered",
        "persistent residual data is inactive without a current-session lease");

    var missing = new AuraSharedRegistrationManifestV3
    {
        OwnerModId = "MissingContent",
        ParticipantKind = AuraSharedParticipantKinds.Content,
        PackageId = "Missing.Resources",
        Resources = new List<AuraSharedResourceDeclarationV3>
        {
            new()
            {
                ModuleId = "Audio",
                FeatureId = "BattleBgm",
                ScopeType = "Role",
                ScopeId = "role_a",
                ResourceId = "missing-bgm",
                Source = "missing.ogg",
                EffectMode = AuraSharedEffectModes.Replacement,
                MissingPolicy = AuraSharedMissingPolicies.NativeFallback
            }
        }
    };
    var missingResult = v3.Register("MissingContent", missing, sources);
    var missingResolution = v3.Resolve("Audio/Role/role_a/BattleBgm/MissingContent/missing-bgm/content.ogg");
    Assert(missingResult.Success
           && missingResult.Items.Single().Status == AuraSharedRegistrationStatuses.Unavailable
           && !missingResolution.Success
           && missingResolution.Fallback == AuraSharedMissingPolicies.NativeFallback,
        "missing replacement resource is isolated and resolves to native fallback");
}

void TestRoleRegistryContracts()
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
            }
        }
    };
    Assert(document.ReplaceContribution(manifest), "role registry accepts a persistent declaration");
    var active = document.BuildActiveEntries("session-a");
    Assert(active.Count == 2
           && active.Any(role => role.RoleId == "career_7")
           && active.Single(role => role.RoleId == "Mod_role").DisplayName == "Declared Role"
           && active.Single(role => role.RoleId == "Mod_role").Aliases.Contains("role"),
        "role registry merges live discovery with higher-priority declared metadata");
}

void TestObjectPoolContracts()
{
    var pool = new AuraSharedObjectPool<string, PoolValue>(2, value => value.IsValid);
    var first = new PoolValue("first");
    var second = new PoolValue("second");
    var overflow = new PoolValue("overflow");
    Assert(pool.Release("common", first), "object pool accepts first value");
    Assert(!pool.Release("common", first), "object pool rejects duplicate idle instances");
    Assert(pool.Release("common", second), "object pool accepts value up to capacity");
    Assert(!pool.Release("common", overflow), "object pool rejects values over per-key capacity");
    Assert(pool.Count("common") == 2, "object pool reports per-key count");
    Assert(pool.TryAcquire("common", out var acquired) && ReferenceEquals(acquired, second), "object pool acquires in LIFO order");

    acquired!.IsValid = false;
    Assert(pool.Release("attack", acquired) == false, "object pool rejects invalid values");
    first.IsValid = false;
    Assert(!pool.TryAcquire("common", out _), "object pool discards invalid idle values");

    var disposable = new PoolValue("dispose");
    Assert(pool.Release("attack", disposable), "object pool keeps keys isolated");
    var disposed = new List<string>();
    pool.Clear(value => disposed.Add(value.Name));
    Assert(disposed.SequenceEqual(new[] { "dispose" }) && pool.Count("attack") == 0, "object pool clear disposes idle values and removes buckets");
}

void TestModeContracts()
{
    Assert(AuraModePolicyEvaluator.EvaluateStarterDeckMutation(null, "Tool").Allowed,
        "mode policy inherits host behavior when no semantic mode is active");

    var snapshot = new AuraActiveModeSnapshot
    {
        Status = AuraModeStates.Active,
        ModeId = "Content:challenge",
        OwnerModId = "Content",
        ResolvedPolicies = new AuraModePolicies
        {
            StarterDeck = new AuraModeStarterDeckPolicy
            {
                MutationAuthority = AuraModeStarterDeckAuthorities.ModeOwnerExclusive,
                ProviderId = "Content"
            }
        }
    };
    Assert(AuraModePolicyEvaluator.EvaluateStarterDeckMutation(snapshot, "Content").Allowed
           && !AuraModePolicyEvaluator.EvaluateStarterDeckMutation(snapshot, "Tool").Allowed,
        "mode-owner-exclusive starter deck policy is evaluated without content semantics");

    snapshot.ResolvedPolicies.StarterDeck.MutationAuthority = AuraModeStarterDeckAuthorities.OfficialOnly;
    Assert(!AuraModePolicyEvaluator.EvaluateStarterDeckMutation(snapshot, "Content").Allowed,
        "official-only starter deck policy rejects every external provider");

    Assert(AuraModeOutcomeRuntime.Publish(new AuraModeOutcomeSnapshot
        {
            OwnerModId = "Content",
            ModeId = "Content:challenge",
            RunId = "run-a",
            OutcomeId = "outcome-a",
            Status = AuraModeOutcomeStates.Completed,
            Source = "authoritative settlement"
        }),
        "mode outcome accepts a complete authoritative handoff");
    Assert(AuraModeOutcomeRuntime.TryReadRecent(
               "Content:challenge",
               "run-a",
               TimeSpan.FromSeconds(30),
               out var completedOutcome)
           && completedOutcome.IsCompleted
           && completedOutcome.Sequence > 0,
        "mode outcome resolves a matching recent run");
    Assert(!AuraModeOutcomeRuntime.TryReadRecent(
            "Content:challenge",
            "run-b",
            TimeSpan.FromSeconds(30),
            out _),
        "mode outcome rejects a different run id");
    Assert(AuraModeOutcomeRuntime.Clear("Content", "Content:challenge", "run-a")
           && !AuraModeOutcomeRuntime.TryReadRecent(
               "Content:challenge",
               "run-a",
               TimeSpan.FromSeconds(30),
               out _),
        "mode outcome conditional clear removes the handoff");
}

void TestDirectorContracts()
{
    var request = DirectorRequest(2);
    var first = AuraDirectorPlanCompiler.Compile(request);
    var second = AuraDirectorPlanCompiler.Compile(DirectorRequest(2));
    Assert(first.Success && first.Descriptor != null && first.Cues.Count == 8, "director compiles four cues per actor");
    Assert(first.Descriptor!.Actors.Select(actor => actor.ActorKey).SequenceEqual(new[] { "player-a", "e0" }),
        "director side strategy groups friendly actors before hostile actors");
    var portraits = first.Cues.Where(cue => cue.CueKind == AuraDirectorCueKind.PortraitSlide).ToArray();
    Assert(Math.Abs(portraits[0].StartSeconds - AuraDirectorPlanCompiler.OpeningDelaySeconds) < 0.001d
           && Math.Abs(first.Descriptor.DurationSeconds - 2.8d) < 0.001d,
        "director side strategy delays the opening by 0.3 seconds and includes it in plan duration");
    Assert(portraits[0].Direction == AuraDirectorDirection.RightToLeft
           && Math.Abs(portraits[0].StartXRatio - 1.15d) < 0.001d
           && Math.Abs(portraits[0].FocusXRatio - 1d / 3d) < 0.001d
           && Math.Abs(portraits[0].EndXRatio + 0.15d) < 0.001d,
        "director sends friendly portraits from screen right through the left third");
    Assert(portraits[1].Direction == AuraDirectorDirection.LeftToRight
           && Math.Abs(portraits[1].StartXRatio + 0.15d) < 0.001d
           && Math.Abs(portraits[1].FocusXRatio - 2d / 3d) < 0.001d
           && Math.Abs(portraits[1].EndXRatio - 1.15d) < 0.001d,
        "director mirrors hostile portraits through the right third");
    var enemyCast = AuraDirectorPlanCompiler.Compile(DirectorRequest(4));
    Assert(enemyCast.Success
           && enemyCast.Cues
               .Where(cue => cue.CueKind == AuraDirectorCueKind.PortraitSlide)
               .Skip(1)
               .All(cue => cue.Direction == AuraDirectorDirection.LeftToRight
                           && Math.Abs(cue.FocusXRatio - 2d / 3d) < 0.001d),
        "director gives every hostile actor the same mirrored route");
    var mixedCast = DirectorRequest(4);
    mixedCast.Actors[2].Side = AuraDirectorActorSide.Friendly;
    var groupedMixedCast = AuraDirectorPlanCompiler.Compile(mixedCast);
    Assert(groupedMixedCast.Success
           && groupedMixedCast.Descriptor!.Actors.Select(actor => actor.ActorKey)
               .SequenceEqual(new[] { "player-a", "e1", "e0", "e2" }),
        "director preserves source order within stable friendly and hostile groups");
    Assert(first.Descriptor.PlanHash == second.Descriptor!.PlanHash,
        "director plan hash is deterministic");
    Assert(first.Envelope != null
           && first.Envelope.ContractId == AuraDirectorProtocol.ContractId
           && first.Envelope.SchemaVersion == AuraDirectorProtocol.CurrentSchemaVersion
           && first.Envelope.Cues.Count == first.Cues.Count,
        "director emits a self-contained versioned plan envelope");

    var legacy = DirectorRequest(2);
    legacy.SchemaVersion = AuraDirectorProtocol.MinimumSupportedSchemaVersion;
    legacy.MinimumReaderSchemaVersion = AuraDirectorProtocol.MinimumSupportedSchemaVersion;
    Assert(AuraDirectorPlanCompiler.Compile(legacy).Success,
        "director accepts the supported legacy schema");

    var future = DirectorRequest(2);
    future.SchemaVersion = AuraDirectorProtocol.CurrentSchemaVersion + 1;
    Assert(AuraDirectorPlanCompiler.Compile(future).RejectionCode == "schema-version-unsupported",
        "director rejects future schemas it cannot interpret");

    var extensionA = DirectorRequest(2);
    extensionA.Extensions["z"] = "last";
    extensionA.Extensions["a"] = "first";
    var extensionB = DirectorRequest(2);
    extensionB.Extensions["a"] = "first";
    extensionB.Extensions["z"] = "last";
    Assert(AuraDirectorPlanCompiler.Compile(extensionA).Descriptor!.PlanHash
           == AuraDirectorPlanCompiler.Compile(extensionB).Descriptor!.PlanHash,
        "director hashes bounded extensions in deterministic key order");

    var oversizedExtensions = DirectorRequest(2);
    for (var i = 0; i <= AuraDirectorPlanCompiler.MaximumExtensionCount; i++)
    {
        oversizedExtensions.Extensions["key-" + i] = "value";
    }
    Assert(AuraDirectorPlanCompiler.Compile(oversizedExtensions).RejectionCode == "extensions-invalid",
        "director rejects oversized extension maps");

    var reversedSides = DirectorRequest(2);
    reversedSides.Actors.Reverse();
    var regrouped = AuraDirectorPlanCompiler.Compile(reversedSides);
    Assert(regrouped.Success
           && regrouped.Descriptor!.Actors.Select(actor => actor.ActorKey).SequenceEqual(new[] { "player-a", "e0" })
           && regrouped.Descriptor.PlanHash == first.Descriptor.PlanHash,
        "director side grouping canonicalizes cross-side caller order");

    var originalEnemyOrder = AuraDirectorPlanCompiler.Compile(DirectorRequest(3));
    var changedEnemyOrder = DirectorRequest(3);
    (changedEnemyOrder.Actors[1], changedEnemyOrder.Actors[2]) =
        (changedEnemyOrder.Actors[2], changedEnemyOrder.Actors[1]);
    var changed = AuraDirectorPlanCompiler.Compile(changedEnemyOrder);
    Assert(changed.Success && changed.Descriptor!.PlanHash != originalEnemyOrder.Descriptor!.PlanHash,
        "director preserves and hashes caller order within one side");

    var alternating = DirectorRequest(2);
    alternating.Actors.Reverse();
    alternating.Strategy = new AuraDirectorStrategyRef
    {
        StrategyId = AuraDirectorPlanCompiler.AlternatingPortraitStrategyId,
        StrategyVersion = AuraDirectorPlanCompiler.AlternatingPortraitStrategyVersion,
        ProfileId = AuraDirectorPlanCompiler.DefaultOpeningProfileId
    };
    var alternatingPlan = AuraDirectorPlanCompiler.Compile(alternating);
    var alternatingPortraits = alternatingPlan.Cues
        .Where(cue => cue.CueKind == AuraDirectorCueKind.PortraitSlide)
        .ToArray();
    Assert(alternatingPlan.Success
           && alternatingPlan.Descriptor!.Actors.Select(actor => actor.ActorKey).SequenceEqual(new[] { "e0", "player-a" })
           && alternatingPortraits[0].StartSeconds == 0d
           && alternatingPortraits[0].Direction == AuraDirectorDirection.RightToLeft
           && alternatingPortraits[1].Direction == AuraDirectorDirection.LeftToRight
           && alternatingPortraits.All(cue => Math.Abs(cue.FocusXRatio - 0.5d) < 0.001d),
        "director retains the explicit alternating portrait v1 strategy");

    var compact = AuraDirectorPlanCompiler.Compile(DirectorRequest(9));
    var compactPortrait = compact.Cues.First(cue => cue.CueKind == AuraDirectorCueKind.PortraitSlide);
    Assert(compact.Success && compactPortrait.EnterSeconds == 0.25d && compactPortrait.HoldSeconds == 0.15d,
        "director uses compact timing beyond eight actors");

    var duplicate = DirectorRequest(2);
    duplicate.Actors[1].ActorKey = duplicate.Actors[0].ActorKey;
    Assert(AuraDirectorPlanCompiler.Compile(duplicate).RejectionCode == "actor-key-duplicate",
        "director rejects duplicate battle actor identities");

    var overLimit = DirectorRequest(AuraDirectorPlanCompiler.MaximumActorCount + 1);
    Assert(AuraDirectorPlanCompiler.Compile(overLimit).RejectionCode == "actors-over-limit",
        "director fails open instead of truncating oversized casts");

    var state = new AuraDirectorSessionStateMachine();
    Assert(state.TryAdvance(AuraDirectorSessionState.Preparing)
           && !state.TryAdvance(AuraDirectorSessionState.Playing)
           && state.TryBeginRelease("test-abort")
           && !state.TryBeginRelease("duplicate")
           && state.TryMarkReleased()
           && state.IsReleased
           && state.ReleaseReason == "test-abort",
        "director session release is ordered and idempotent");
    Assert(typeof(IAuraDirectorNativeStartHold).GetProperty(nameof(IAuraDirectorNativeStartHold.NativeTarget)) != null
           && typeof(IAuraDirectorNativeStartHoldSink).GetMethod(nameof(IAuraDirectorNativeStartHoldSink.TryAccept)) != null,
        "director exposes a backend-independent native start hold contract");
    Assert(typeof(IAuraDirectorStartGateProvider).GetMethod(nameof(IAuraDirectorStartGateProvider.Install)) != null
           && typeof(IAuraDirectorRequestSource).GetMethod(nameof(IAuraDirectorRequestSource.BuildRequest)) != null,
        "director exposes provider and local request-source contracts");

    var layout = AuraDirectorPortraitLayout.Calculate(
        1080d,
        0.13d,
        -0.75d,
        -1.5d,
        0.75d,
        1.5d);
    Assert(Math.Abs(layout.BarHeight - 140.4d) < 0.001d
           && Math.Abs(layout.DisplayHeight - 779.2d) < 0.001d
           && Math.Abs((1080d - layout.BarHeight * 2d - layout.DisplayHeight) * 0.5d
                       - AuraDirectorPortraitLayout.VerticalInsetPixels) < 0.001d,
        "director portrait visible bounds keep ten pixels from expanded letterbox edges");

    var shifted = AuraDirectorPortraitLayout.Calculate(
        1080d,
        0.13d,
        -0.2d,
        -0.8d,
        1.8d,
        1.2d);
    Assert(Math.Abs(shifted.SourceCenterX - 0.8d) < 0.001d
           && Math.Abs(shifted.SourceCenterY - 0.2d) < 0.001d
           && Math.Abs(shifted.DisplayHeight - layout.DisplayHeight) < 0.001d,
        "director portrait layout recenters asymmetric sprite mesh bounds");

    var rightOutside = AuraDirectorPortraitLayout.ResolveAnchoredX(1.15d, 1920d, 2200d);
    var leftOutside = AuraDirectorPortraitLayout.ResolveAnchoredX(-0.15d, 1920d, 2200d);
    Assert(rightOutside >= 2070d && leftOutside <= -2070d,
        "director keeps height-priority wide portraits fully outside before and after slides");
}

AuraDirectorRequest DirectorRequest(int actorCount)
{
    var request = new AuraDirectorRequest
    {
        ContractId = AuraDirectorProtocol.ContractId,
        SchemaVersion = AuraDirectorProtocol.CurrentSchemaVersion,
        MinimumReaderSchemaVersion = AuraDirectorProtocol.MinimumSupportedSchemaVersion,
        OwnerModId = "Tests",
        RequestId = "opening",
        BattleSessionId = 7
    };
    for (var i = 0; i < actorCount; i++)
    {
        var player = i == 0;
        request.Actors.Add(new AuraDirectorActorRef
        {
            ActorKey = player ? "player-a" : "e" + (i - 1),
            ActorKind = player ? AuraDirectorActorKind.Player : AuraDirectorActorKind.Enemy,
            Side = player ? AuraDirectorActorSide.Friendly : AuraDirectorActorSide.Hostile,
            OwnerPlayerId = player ? "player-a" : "",
            ContentOwnerModId = "Tests",
            ContentId = player ? "role-a" : "enemy-" + (i - 1),
            Resource = new AuraDirectorResourceRef
            {
                ProviderId = "aura.cg",
                OwnerModId = "Tests",
                ResourceId = player ? "role-a-portrait" : "enemy-" + (i - 1) + "-portrait"
            }
        });
    }
    return request;
}

AuraSharedInstallRequest Request(string owner, string system, string id, string package, long version, string source, string destination)
{
    return new AuraSharedInstallRequest
    {
        OwnerModId = owner,
        System = system,
        LogicalId = id,
        PackageId = package,
        PackageVersion = version,
        Kind = AuraSharedResourceKinds.File,
        SourcePath = source,
        DestinationRelativePath = destination
    };
}

void TestAuthoritativeSyncContracts()
{
    var domain = AuraAuthoritativeSyncRuntime.RegisterDomain(new AuraAuthoritativeSyncDomainOptions
    {
        OwnerModId = "Tests",
        DomainId = "sender-scoped-" + Guid.NewGuid().ToString("N"),
        SnapshotRequestThrottleSeconds = 0.05d,
        MaxResolvedTokens = 16
    });

    Assert(domain.TryClaimToken("player-a", 17), "first sender token claim");
    Assert(domain.TryClaimToken("player-b", 17), "same token from another sender must not collide");
    Assert(!domain.TryClaimToken("player-a", 17), "same sender token replay must be rejected");

    Assert(AuraSharedPayloadBudget.TryMeasureUtf8Json(new { text = "payload" }, out var bytes, out _)
           && bytes > 0,
        "payload budget measures serialized UTF-8 bytes");
    Assert(!AuraSharedPayloadBudget.FitsSoftLimit(new { text = new string('x', 512) }, 32, out _, out _),
        "payload budget rejects oversized serialized payloads");
}

void Increment(AuraSharedStorageCoordinator coordinator)
{
    for (var attempt = 0; attempt < 200; attempt++)
    {
        var read = coordinator.Read(new AuraSharedStorageRequest
        {
            Scope = AuraSharedStorageScopes.Shared,
            System = "Concurrency",
            FileName = "counter.json"
        });
        var value = read.Found ? JObject.Parse(read.PayloadJson)["value"]!.Value<int>() : 0;
        var write = coordinator.Write(new AuraSharedStorageRequest
        {
            Scope = AuraSharedStorageScopes.Shared,
            System = "Concurrency",
            FileName = "counter.json",
            WriterId = "Counter",
            AuthorityId = "Counter",
            ExpectedRevision = read.Revision,
            PayloadJson = "{\"value\":" + (value + 1) + "}"
        });
        if (write.Success)
        {
            return;
        }
        if (!write.Conflict)
        {
            throw new InvalidOperationException(write.Message);
        }
    }
    throw new TimeoutException("CAS increment did not converge.");
}

void TestRecovery(AuraSharedStorageCoordinator coordinator, AuraSharedPackageCoordinator packageCoordinator)
{
    var destination = Path.Combine(tempRoot, "Audio", "Recovery", "file.wav");
    var backup = Path.Combine(tempRoot, "Backups", "Recovery", "old.wav");
    var staging = Path.Combine(tempRoot, "Cache", "Packages", "recovery-test");
    var registry = Path.Combine(tempRoot, "Registries", "Recovery", "resources.json");
    var registryBackup = Path.Combine(staging, "registry.backup.json");
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
    Directory.CreateDirectory(staging);
    Directory.CreateDirectory(Path.GetDirectoryName(registry)!);
    File.WriteAllText(destination, "new");
    File.WriteAllText(backup, "old");
    File.WriteAllText(registry, "{\"new\":true}");
    File.WriteAllText(registryBackup, "{\"old\":true}");

    var journal = new AuraSharedTransactionJournal
    {
        TransactionId = "recovery-test",
        State = "ContentCommitted",
        DestinationPath = destination,
        BackupPath = backup,
        StagingPath = staging,
        RegistryPath = registry,
        RegistryBackupPath = registryBackup,
        DestinationExisted = true,
        RegistryExisted = true,
        Kind = AuraSharedResourceKinds.File
    };
    coordinator.WriteRawJsonAtomic(Path.Combine(tempRoot, "Transactions", "recovery-test.json"), journal, false);
    var recovered = packageCoordinator.RecoverTransactions();
    if (recovered != 1 || File.ReadAllText(destination) != "old" || !File.ReadAllText(registry).Contains("old"))
    {
        throw new InvalidOperationException("Interrupted transaction was not restored.");
    }
}

void TestSecureEnvelopeContracts()
{
    using var encryptionKey = new System.Security.Cryptography.RSACryptoServiceProvider(2048);
    using var signatureKey = new System.Security.Cryptography.RSACryptoServiceProvider(2048);
    var envelopeJson = AuraSharedSecureEnvelope.EncryptJson(
        "TestEnvelope",
        "test-key",
        "{\"value\":42}",
        encryptionKey.ToXmlString(false),
        signatureKey.ToXmlString(true));

    Assert(envelopeJson.Contains("RSA-OAEP-SHA1+A256CBC-HS256")
           && envelopeJson.Contains("RSA-SHA256"),
        "secure envelope records crypto algorithms");

    var plainJson = AuraSharedSecureEnvelope.DecryptJson(
        envelopeJson,
        "TestEnvelope",
        encryptionKey.ToXmlString(true),
        signatureKey.ToXmlString(false));
    Assert(JObject.Parse(plainJson)["value"]!.Value<int>() == 42,
        "secure envelope decrypts signed payload");

    var tamperedEnvelope = JObject.Parse(envelopeJson);
    tamperedEnvelope["ciphertext"] = "AA" + tamperedEnvelope["ciphertext"]!.Value<string>();
    var tampered = tamperedEnvelope.ToString(Newtonsoft.Json.Formatting.None);
    try
    {
        AuraSharedSecureEnvelope.DecryptJson(
            tampered,
            "TestEnvelope",
            encryptionKey.ToXmlString(true),
            signatureKey.ToXmlString(false));
        throw new InvalidOperationException("Tampered envelope was accepted.");
    }
    catch (System.Security.Cryptography.CryptographicException)
    {
        Assert(true, "secure envelope rejects tampered payload");
    }
    catch (InvalidOperationException)
    {
        Assert(true, "secure envelope rejects tampered payload");
    }
    catch (FormatException)
    {
        Assert(true, "secure envelope rejects malformed payload");
    }
}

void TestLifecycleContracts()
{
    AuraFeatureSwitchRuntime.RegisterFeature("OwnerA", "FeatureA", defaultEnabled: true, "test");
    Assert(AuraFeatureSwitchRuntime.IsEnabled("OwnerA", "FeatureA"), "feature default enabled");
    AuraFeatureSwitchRuntime.SetLocalOverride("ToolA", "OwnerA", "FeatureA", false);
    Assert(!AuraFeatureSwitchRuntime.IsEnabled("OwnerA", "FeatureA"), "feature effective override disabled");
    AuraFeatureSwitchRuntime.SetLocalOverride("ToolA", "OwnerA", "FeatureA", true);
    Assert(AuraFeatureSwitchRuntime.IsEnabled("OwnerA", "FeatureA"), "feature effective override enabled");

    AuraLifecycleOperationLedger.ClearScopePrefix("test-battle:");

    AuraLifecycleSessionRuntime.EndBattleSession();
    var firstEpoch = AuraLifecycleSessionRuntime.RestartBattleSession();
    var secondEpoch = AuraLifecycleSessionRuntime.RestartBattleSession();
    Assert(secondEpoch > firstEpoch,
        "RestartBattleSession should advance the epoch even while the previous battle session is active");
    AuraLifecycleSessionRuntime.EndBattleSession();
    Assert(AuraLifecycleOperationLedger.TryClaim("test-battle:1", "OwnerA", "FeatureA", "AddStartBuff", "Status1", "buff", "BuffA"),
        "first lifecycle operation claim");
    Assert(!AuraLifecycleOperationLedger.TryClaim("test-battle:1", "OwnerA", "FeatureA", "AddStartBuff", "Status1", "buff", "BuffA"),
        "duplicate lifecycle operation rejected");
    Assert(AuraLifecycleOperationLedger.TryClaim("test-battle:1", "OwnerA", "FeatureA", "AddStartBuff", "Status1", "buff", "BuffB"),
        "different buff effect can claim");
    Assert(AuraLifecycleOperationLedger.TryClaim("test-battle:1", "OwnerA", "FeatureA", "AddStartBuff", "Status1", "marker", "BuffA"),
        "different effect category can claim");
    AuraLifecycleOperationLedger.ClearScopePrefix("test-battle:");
}

void TestIdentityContracts()
{
    Assert(AuraSharedIdentity.NormalizeRoleId("1") == "career_1", "short numeric career id normalizes");
    Assert(AuraSharedIdentity.NormalizeRoleId("*SunExp_wuna_wuna") == "SunExp_wuna_wuna", "mod role id trims legacy star prefix");
    Assert(AuraSharedIdentity.IsRuntimeNumericId("76561198326385152"), "long numeric runtime owner id detected");
    Assert(!AuraSharedIdentity.IsUsableRoleId("76561198326385152"), "long numeric runtime owner id is not a role");
    Assert(AuraSharedIdentity.SelectRoleId("76561198326385152", "SunExp_wuna_wuna") == "SunExp_wuna_wuna",
        "role selector falls back past runtime owner id");
    Assert(AuraSharedIdentity.SelectRoleId("wuna", "SunExp_wuna_wuna") == "wuna",
        "role selector preserves usable short mod role id");

    Assert(AuraSharedContentId.Matches("careercard_*8", "careercard_8", knownPrefixes: new[] { "careercard_" }),
        "content id matcher accepts internal table protocol markers");
    Assert(AuraSharedContentId.Matches("8", "careercard_8", knownPrefixes: new[] { "careercard_" }),
        "content id matcher accepts deterministic official short ids");
    Assert(AuraSharedContentId.Matches("solar_prayer", "SunExp_solar_prayer", "SunExp"),
        "content id matcher accepts owner-scoped short ids");

    var uniqueShort = AuraSharedContentId.Resolve(
        "solar_prayer",
        new[] { "SunExp_solar_prayer", "Other_card" },
        "SunExp");
    Assert(uniqueShort.Success && uniqueShort.ResolvedId == "SunExp_solar_prayer",
        "content id resolver returns the unique owner-scoped full id");
    var ambiguousShort = AuraSharedContentId.Resolve(
        "prayer",
        new[] { "SunExp_solar_prayer", "Other_lunar_prayer" });
    Assert(!ambiguousShort.Success && ambiguousShort.Kind == AuraSharedContentIdResolutionKind.Ambiguous,
        "content id resolver rejects colliding short ids");

    var resourceCandidates = AuraSharedResourceReference.BuildCandidates(
        "CG/AuraToolsExp/Roles/career_7/skill_cg_1.png",
        new AuraSharedResourceAlias("CG/Roles/", "CG/AuraToolsExp/Roles/"));
    Assert(resourceCandidates.Count == 2
           && resourceCandidates[0] == "CG/AuraToolsExp/Roles/career_7/skill_cg_1.png"
           && resourceCandidates[1] == "CG/Roles/career_7/skill_cg_1.png",
        "resource compatibility keeps the declared path first and adds a bidirectional alias fallback");
}

void TestJourneyContracts()
{
    var context = new AuraJourneyConditionContext
    {
        RoleIds = new List<string> { "SunExp_wuna_wuna", "SanGuoShaExp_shenzhugeliang" },
        PlayerCount = 2,
        Flags = new Dictionary<string, bool> { ["solar_memory_unlocked"] = true },
        Values = new Dictionary<string, string> { ["route"] = "sun" },
        Counters = new Dictionary<string, int> { ["embers"] = 3 }
    };

    Assert(AuraJourneyConditionEvaluator.EvaluateAll(new[]
    {
        new AuraJourneyCondition { Kind = AuraJourneyConditionKinds.Flag, Key = "solar_memory_unlocked" },
        new AuraJourneyCondition { Kind = AuraJourneyConditionKinds.Equals, Key = "route", Value = "sun" },
        new AuraJourneyCondition { Kind = AuraJourneyConditionKinds.MinCounter, Key = "embers", Number = 2 },
        new AuraJourneyCondition { Kind = AuraJourneyConditionKinds.AnyRole, Value = "SunExp_wuna_wuna" },
        new AuraJourneyCondition { Kind = AuraJourneyConditionKinds.PlayerCountAtLeast, Number = 2 }
    }, context), "journey condition evaluator");

    var request = new AuraJourneyCommitRequest
    {
        JourneyId = "SunExp.SolarMemory",
        OwnerModId = "SunExp",
        Action = "SelectNode",
        NodeId = "memory_1",
        Message = "selected first memory",
        Mutation = new AuraJourneyMutation
        {
            Run = new AuraJourneyRunBinding
            {
                RunId = "solar-memory-run",
                NativeModeKey = "SunExp_SolarMemoryMode",
                NativeModeValue = "1",
                StartedUtc = DateTime.UnixEpoch.ToString("O")
            },
            ActiveNodeId = "memory_1",
            SelectRouteId = "solar_route",
            SetFlags = new Dictionary<string, bool> { ["entered"] = true },
            AddCounters = new Dictionary<string, int> { ["embers"] = 1 }
        }
    };

    var state = AuraJourneyStateReducer.Apply(null, request, DateTime.UnixEpoch);
    Assert(state.Version == 1
           && state.Run.RunId == "solar-memory-run"
           && state.ActiveNodeId == "memory_1"
           && state.SelectedRouteIds.Contains("solar_route")
           && state.Flags["entered"]
           && state.Counters["embers"] == 1
           && state.Events.Count == 1,
        "journey state reducer first event");

    var next = AuraJourneyStateReducer.Apply(state, new AuraJourneyCommitRequest
    {
        JourneyId = "SunExp.SolarMemory",
        OwnerModId = "SunExp",
        Action = "CompleteNode",
        NodeId = "memory_1",
        Mutation = new AuraJourneyMutation
        {
            CompleteNodeId = "memory_1",
            AddCounters = new Dictionary<string, int> { ["embers"] = 2 }
        }
    }, DateTime.UnixEpoch.AddSeconds(1));

    Assert(next.Version == 2
           && next.CompletedNodeIds.Contains("memory_1")
           && next.Counters["embers"] == 3
           && next.Events.Count == 2,
        "journey state reducer append event");

    var projection = AuraJourneyMapNodeDataBuilder.Build(new AuraJourneyMapNodeSpec
    {
        MapId = "SunExp_sunexp_solar_memory_black_sun_after",
        FallbackMapId = "solar_memory_black_sun_after",
        NodeId = "SunExp_sunexp_Sub_solar_memory_black_sun_after",
        Type = AuraJourneyNodeKinds.Event,
        Note = "普通事件",
        Level = "-1",
        DicePolicy = AuraJourneyDicePolicies.Default
    }, id => id == "SunExp_sunexp_solar_memory_black_sun_after"
        ? new Dictionary<string, string> { ["Id"] = "old", ["Type"] = "", ["Note"] = "", ["Level"] = "" }
        : null);

    Assert(projection.Valid
           && projection.Data["Id"] == "SunExp_sunexp_solar_memory_black_sun_after"
           && projection.Data["Type"] == AuraJourneyNodeKinds.Event
           && projection.Data["NodeId"] == "SunExp_sunexp_Sub_solar_memory_black_sun_after"
           && projection.Data["Level"] == "-1"
           && projection.DicePolicy == AuraJourneyDicePolicies.Default,
        "journey map node projection fills native fields");

    var maps = new[] { "wrong", "Breaks_keep", "old" };
    var mapData = new[] { "wrong_event", "Breaks_keep_data", "old_node" };
    var repair = AuraJourneySyncProjection.Repair(maps, mapData, new[]
    {
        new AuraJourneySlotRule
        {
            SlotIndex = 0,
            MapNode = new AuraJourneyMapNodeSpec
            {
                MapId = "fixed_event_map",
                NodeId = "fixed_event_node",
                Type = AuraJourneyNodeKinds.Event
            }
        },
        new AuraJourneySlotRule
        {
            SlotIndex = 1,
            ReplacementPolicy = AuraJourneyReplacementPolicies.PreserveBreak,
            MapNode = new AuraJourneyMapNodeSpec
            {
                MapId = "should_not_replace",
                NodeId = "should_not_replace_node"
            }
        }
    });

    Assert(repair.Changed
           && repair.Repaired == 1
           && repair.Preserved == 1
           && maps[0] == "fixed_event_map"
           && mapData[0] == "fixed_event_node"
           && maps[1] == "Breaks_keep",
        "journey sync projection repairs fixed slots and preserves break nodes");

    AuraJourneyMapIdAliasRegistry.RegisterPrefixAlias("test.full-to-short", "TestMod_full_", "");
    var aliases = AuraJourneyMapIdAliasRegistry.Expand("*TestMod_full_map_1");
    Assert(aliases.Contains("*TestMod_full_map_1")
           && aliases.Contains("TestMod_full_map_1")
           && aliases.Contains("map_1"),
        "journey map id alias registry expands registered prefixes without shared content rules");
}

void TestOnlineChatContracts()
{
    var parsed = AuraChatEmojiParser.Parse("hi #[role:smile] ok").ToList();
    Assert(parsed.Count == 3
           && parsed[1].Kind == "Sticker"
           && parsed[1].PackId == "role"
           && parsed[1].StickerId == "smile"
           && AuraChatEmojiParser.DisplayLength("hi #[role:smile] ok") == 7,
        "chat emoji token parsing");

    var limited = AuraChatTextLimiter.LimitPlayerText("12345678901234567890123456789012345678901234");
    Assert(limited == "1234567890123456789012345678901234567890...", "chat player text limit");

    var wrapped = AuraChatTextLimiter.WrapPlainText("123456789012345678901234567890123456789012345678901234567890X");
    Assert(wrapped == "123456789012345678901234567890123456789012345678901234567890\nX", "chat display line wrap");

    var status = AuraChatModSyncSnapshot.BuildStatus(new object[]
    {
        new FakeLobbyPlayer("p1", "A", new[] { new FakeLobbyMod("ChatExp", "0.1", true), new FakeLobbyMod("Other", "1", true), new FakeLobbyMod("SuperLongModName", "2", true), new FakeLobbyMod("中文MOD名称超长", "3", true), new FakeLobbyMod("Unused", "9", false) }),
        new FakeLobbyPlayer("p2", "LongPlayerName", new[] { new FakeLobbyMod("ChatExp", "0.1", false), new FakeLobbyMod("Unused", "9", false) }),
        new FakeLobbyPlayer("p3", "玩家甲乙丙丁", new[] { new FakeLobbyMod("ChatExp", "0.1", true) })
    }, "ChatExp");
    Assert(status.Contains("MOD同步状态")
           && status.Contains("MOD\tA\tLongPlayer\t玩家甲乙丙")
           && status.Contains("A")
           && status.Contains("LongPlayer")
           && status.Contains("玩家甲乙丙")
           && !status.Contains("LongPlayerName")
           && !status.Contains("玩家甲乙丙丁")
           && status.Contains("ChatExp")
           && status.Contains("ChatExp\t0.1\tOFF\t0.1")
           && status.Contains("0.1")
           && status.Contains("OFF")
           && status.Contains("Other")
           && status.Contains("SuperLongM")
           && !status.Contains("SuperLongModName")
           && status.Contains("中文MOD名")
           && !status.Contains("中文MOD名称超长")
           && !status.Contains("Unused"),
        "chat mod sync status");

    var localStore = new AuraChatLocalStore(2);
    Assert(localStore.Add(new AuraChatMessage { MessageId = "one", RawText = "one" })
           && localStore.Add(new AuraChatMessage { MessageId = "two", RawText = "two" })
           && localStore.Add(new AuraChatMessage { MessageId = "three", RawText = "three" })
           && !localStore.Add(new AuraChatMessage { MessageId = "three", RawText = "duplicate" })
           && localStore.Messages.Count == 2
           && localStore.Messages[0].RawText == "two"
           && localStore.Messages[1].RawText == "three",
        "chat bounded local store");

    AuraChatRuntime.Initialize("ChatExp", 2);
    AuraChatRuntime.Receive(new AuraChatMessage
    {
        MessageId = "unsigned-free-text",
        ContentKind = AuraChatKinds.PlayerText,
        RawText = "must be rejected"
    });
    Assert(AuraChatRuntime.Messages.Count == 0, "chat rejects content outside the verified catalog");
    AuraChatRuntime.ClearMessages();
    Assert(AuraChatRuntime.Messages.Count == 0, "chat clear local history");
}

void Assert(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed: " + name);
    }
    assertions++;
}

bool OperationLogContains(string root, string kind, string phase)
{
    var directory = Path.Combine(root, "Logs", "Operations");
    if (!Directory.Exists(directory))
    {
        return false;
    }

    return Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.TopDirectoryOnly)
        .SelectMany(File.ReadAllLines)
        .Any(line =>
        {
            var json = JObject.Parse(line);
            return string.Equals(json["kind"]?.Value<string>(), kind, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(json["phase"]?.Value<string>(), phase, StringComparison.OrdinalIgnoreCase);
        });
}

void TryDelete(string path)
{
    try
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
    catch
    {
    }
}

sealed class FakeLobbyPlayer
{
    public FakeLobbyPlayer(string id, string name, IEnumerable<FakeLobbyMod> mods)
    {
        Id = id;
        Name = name;
        Mods = mods.ToList();
    }

    public string Id { get; }

    public string Name { get; }

    public List<FakeLobbyMod> Mods { get; }
}

sealed class FakeLobbyMod
{
    public FakeLobbyMod(string modName, string modVersion, bool enabled)
    {
        ModName = modName;
        ModVersion = modVersion;
        Enabled = enabled;
    }

    public string ModName { get; }

    public string ModVersion { get; }

    public bool Enabled { get; }
}

sealed class PoolValue
{
    public PoolValue(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public bool IsValid { get; set; } = true;
}
