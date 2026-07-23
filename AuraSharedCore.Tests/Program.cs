using AuraShared.Core;
using AuraJourney.Shared;
using AuraMode.Shared;
using AuraOnline.Shared;
using AuraDirector.Shared;
using AuraRole.Shared;
using AuraGameData.Shared;
using Newtonsoft.Json.Linq;

var assertions = 0;
var tempRoot = Path.Combine(Path.GetTempPath(), "AuraSharedCore.Tests", Guid.NewGuid().ToString("N"));
var sourceRoot = Path.Combine(Path.GetTempPath(), "AuraSharedCore.Tests.Sources", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
Directory.CreateDirectory(sourceRoot);

try
{
    var compactJson = AuraSharedJson.SerializeCompact(new
    {
        protocol = "jsonl-test",
        nested = new { value = 1 }
    });
    Assert(!compactJson.Contains('\r')
           && !compactJson.Contains('\n')
           && JObject.Parse(compactJson)["nested"]!["value"]!.Value<int>() == 1,
        "compact JSON serialization stays on one line");

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

    var overBudgetPath = tempRoot;
    while (Path.Combine(overBudgetPath, "value.json").Length <= AuraSharedStorageCoordinator.MaxPortablePathLength)
    {
        overBudgetPath = Path.Combine(overBudgetPath, "segment-xxxxxxxxxxxxxxxxxxxxxxxx");
    }
    AuraSharedPathBudgetException? pathBudgetFailure = null;
    try
    {
        storage.WriteTextAtomic(Path.Combine(overBudgetPath, "value.json"), "{}", false);
    }
    catch (AuraSharedPathBudgetException ex)
    {
        pathBudgetFailure = ex;
    }
    Assert(pathBudgetFailure != null
           && pathBudgetFailure.PathLength > AuraSharedStorageCoordinator.MaxPortablePathLength
           && pathBudgetFailure.Operation == "atomic-target",
        "atomic storage rejects over-budget paths before creating partial directories");

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
        DestinationRelativePath = "CG/Role/role-a/Feast/Tool/manual.local/content.png"
    };
    var editableCreated = editable.Seed(editableRequest);
    Assert(editableCreated.Success && editableCreated.Changed && !editableCreated.Customized
           && editableCreated.Status == AuraSharedEditableResourceStatuses.Created,
        "editable resource creates a missing working copy");
    var editableExisting = editable.Seed(editableRequest);
    Assert(editableExisting.Success && !editableExisting.Changed && !editableExisting.Customized
           && editableExisting.Status == AuraSharedEditableResourceStatuses.ExistingDefault,
        "editable resource seed is idempotent");
    var editablePath = Path.Combine(tempRoot, "CG", "Role", "role-a", "Feast", "Tool", "manual.local", "content.png");
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
        DestinationRelativePath = "CG/Role/role-b/Feast/Tool/manual.local/content.png"
    };
    var untouchedCreated = editable.Seed(untouchedRequest);
    File.WriteAllText(editableSeed, "seed-updated");
    untouchedRequest.PreviousSeedHash = untouchedCreated.SeedHash;
    var untouchedUpdated = editable.Seed(untouchedRequest);
    Assert(untouchedUpdated.Success && untouchedUpdated.Changed && !untouchedUpdated.Customized
           && untouchedUpdated.Status == AuraSharedEditableResourceStatuses.UpdatedDefault
           && File.ReadAllText(untouchedUpdated.InstalledPath) == "seed-updated",
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
    TestResourceProtocolV4();
    TestQualifiedResourceIdentityConflicts();
    TestRoleRegistryContracts();
    TestSecureEnvelopeContracts();
    TestLifecycleContracts();
    TestJourneyContracts();
    TestOnlineChatContracts();
    TestAuthoritativeSyncContracts();
    TestObjectPoolContracts();
    TestModeContracts();
    TestDirectorContracts();

    TestGameDataCatalog();
    Console.WriteLine($"AuraSharedCore tests passed: {assertions} assertions.");
}

finally
{
    TryDelete(tempRoot);
    TryDelete(sourceRoot);
}

return;

void TestGameDataCatalog()
{
    AuraSharedConfigStore.ResetGameDataTestStore();
    var source = new FakeGameDataSource(
        new AuraGameDataDefinition
        {
            Key = new AuraGameDataKey("Card", "card_a"),
            OwnerModId = "BaseGame",
            WriterId = AuraGameDataConstants.RegistryAuthorityId,
            SourceKind = AuraGameDataSourceKinds.Native,
            Fields = new Dictionary<string, string> { ["Id"] = "card_a", ["Name"] = "Native" }
        },
        new AuraGameDataDefinition
        {
            Key = new AuraGameDataKey("Card", "card_overlay"),
            OwnerModId = "BaseGame",
            WriterId = AuraGameDataConstants.RegistryAuthorityId,
            SourceKind = AuraGameDataSourceKinds.Native,
            Fields = new Dictionary<string, string>
            {
                ["Id"] = "card_overlay",
                ["Name"] = "Native Overlay Base",
                ["Cost"] = "2"
            }
        });
    AuraGameDataCatalogRuntime.ConfigureSource(source);

    var ownerRule = AuraGameDataCatalogRuntime.RegisterOwnerRules("ModOwner", new[]
    {
        new AuraGameDataOwnerRule
        {
            OwnerModId = "ModOwner",
            WriterId = "ModOwner",
            IdPrefix = "card_"
        }
    });
    var nativeOwned = AuraGameDataCatalogRuntime.Query(new AuraGameDataQuery
    {
        DataType = "Card",
        CandidateIds = new List<string> { "card_a" },
        IncludeAllCandidates = true
    }).Items.FirstOrDefault(value => value.SourceKind == AuraGameDataSourceKinds.Native);
    Assert(ownerRule.Success && nativeOwned?.OwnerModId == "ModOwner",
        "game data v5 owner rules assign provenance without copying native rows");

    var overlay = AuraGameDataCatalogRuntime.Register("OverlayMod", new AuraGameDataDefinition
    {
        Key = new AuraGameDataKey("Card", "card_overlay"),
        OwnerModId = "OverlayMod",
        WriterId = "OverlayMod",
        SourceKind = AuraGameDataSourceKinds.Registered,
        StorageKind = AuraGameDataStorageKinds.Overlay,
        Fields = new Dictionary<string, string> { ["Name"] = "Overlay" },
        RemoveFields = new List<string> { "Cost" }
    });
    var overlaid = AuraGameDataCatalogRuntime.Resolve("Card", new[] { "card_overlay" });
    Assert(overlay.Success
           && overlaid?.Fields["Name"] == "Overlay"
           && !overlaid.Fields.ContainsKey("Cost"),
        "game data v5 overlays merge once during compilation");
    var overlayHandle = overlay.Handle;
    source.Invalidate();
    AuraGameDataCatalogRuntime.Rebuild();
    Assert(overlayHandle != null
           && !AuraGameDataCatalogRuntime.ValidateHandle(overlayHandle, out _),
        "game data handles become stale after a catalog generation change");

    var rejectedV4 = AuraGameDataCatalogRuntime.Register("ModA", new AuraGameDataDefinition
    {
        SchemaVersion = 4,
        Key = new AuraGameDataKey("Card", "card_a"),
        OwnerModId = "ModA",
        WriterId = "ModA"
    });
    Assert(!rejectedV4.Success && rejectedV4.Message.Contains("schemaVersion 5"), "game data rejects non-v5 registration");

    var registered = AuraGameDataCatalogRuntime.Register("ModA", new AuraGameDataDefinition
    {
        Key = new AuraGameDataKey("Card", "card_a"),
        OwnerModId = "ModA",
        WriterId = "ModA",
        SourceKind = AuraGameDataSourceKinds.Registered,
        Fields = new Dictionary<string, string> { ["Id"] = "card_a", ["Name"] = "Registered" }
    });
    Assert(registered.Success && registered.Handle != null, "game data registers owner-qualified v5 definition");

    var effective = AuraGameDataCatalogRuntime.Resolve("Card", new[] { "card_a" });
    Assert(effective != null
           && effective.SourceKind == AuraGameDataSourceKinds.Registered
           && effective.Fields["Name"] == "Registered",
        "game data uses centralized source search order");
    var captureCount = source.CaptureCount;
    AuraGameDataDiagnostics.Reset();
    for (var index = 0; index < 1000; index++)
    {
        Assert(AuraGameDataCatalogRuntime.TryGet("Card", "card_a", out _), "game data indexed point lookup resolves");
    }
    Assert(source.CaptureCount == captureCount, "game data hot point lookups never recapture native tables");
    var diagnostics = AuraGameDataDiagnostics.Snapshot();
    Assert(diagnostics.PointLookups == 1000
           && diagnostics.PointHits == 1000
           && diagnostics.NativeCaptures == 0
           && diagnostics.CatalogBuilds == 0,
        "game data diagnostics prove hot point lookups are pure snapshot reads");
    AuraGameDataCatalogRuntime.TryGet("Card", "card_a", out _);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var lookupBenchmark = System.Diagnostics.Stopwatch.StartNew();
    for (var index = 0; index < 10_000; index++)
    {
        AuraGameDataCatalogRuntime.TryGet("Card", "card_a", out _);
    }
    lookupBenchmark.Stop();
    var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    Assert(allocatedBytes <= 1024,
        "game data performs ten thousand hot point lookups without meaningful allocation");
    Assert(lookupBenchmark.ElapsedMilliseconds < 250,
        "game data performs ten thousand hot point lookups within the regression budget");
    Assert(AuraGameDataCatalogRuntime.TryResolveUniqueType("card_a", out var resolvedType)
           && resolvedType == "Card",
        "game data unique-type index resolves without table probes");
    var tableA = AuraGameDataCatalogRuntime.GetTable("Card");
    var tableB = AuraGameDataCatalogRuntime.GetTable("Card");
    Assert(ReferenceEquals(tableA, tableB), "game data table view is stable within one catalog epoch");

    var foreignPatch = AuraGameDataCatalogRuntime.Patch(
        "OtherMod",
        new AuraGameDataKey("Card", "card_a"),
        "ModA",
        new AuraGameDataPatch { SetFields = new Dictionary<string, string> { ["Name"] = "Foreign" } },
        registered.Handle!.Revision);
    Assert(!foreignPatch.Success && foreignPatch.Conflict, "game data rejects foreign definition patch");

    Assert(!AuraGameDataFieldPolicy.IsScriptField("Description")
           && !AuraGameDataFieldPolicy.IsScriptField("Description_zh-Hant")
           && !AuraGameDataFieldPolicy.IsScriptField("Description1")
           && AuraGameDataFieldPolicy.IsScriptField("UseScript")
           && AuraGameDataFieldPolicy.IsScriptField("ChoiceScript1"),
        "game data distinguishes description fields from executable script columns");

    var descriptionPatch = AuraGameDataCatalogRuntime.Patch(
        "ModA",
        new AuraGameDataKey("Card", "card_a"),
        "ModA",
        new AuraGameDataPatch { SetFields = new Dictionary<string, string> { ["Description"] = "Localized effect" } },
        registered.Handle.Revision);
    Assert(descriptionPatch.Success, "game data permits runtime description patch");

    var scriptPatch = AuraGameDataCatalogRuntime.Patch(
        "ModA",
        new AuraGameDataKey("Card", "card_a"),
        "ModA",
        new AuraGameDataPatch { SetFields = new Dictionary<string, string> { ["UseScript"] = "unsafe" } },
        descriptionPatch.Handle!.Revision);
    Assert(!scriptPatch.Success && scriptPatch.Message.Contains("registration-time"), "game data blocks runtime script patch");

    var numberedScriptPatch = AuraGameDataCatalogRuntime.Patch(
        "ModA",
        new AuraGameDataKey("Card", "card_a"),
        "ModA",
        new AuraGameDataPatch { SetFields = new Dictionary<string, string> { ["ChoiceScript1"] = "unsafe" } },
        descriptionPatch.Handle.Revision);
    Assert(!numberedScriptPatch.Success && numberedScriptPatch.Message.Contains("registration-time"),
        "game data blocks numbered runtime script patch");

    var retired = AuraGameDataCatalogRuntime.Retire(
        "ModA",
        new AuraGameDataKey("Card", "card_a"),
        "ModA",
        descriptionPatch.Handle.Revision);
    var history = AuraGameDataCatalogRuntime.QueryHistory(new AuraGameDataQuery { DataType = "Card" });
    Assert(retired.Success && history.Items.Count == 1 && history.Items[0].Retired,
        "game data keeps retired definitions in independent history view");

    var lastGood = AuraGameDataCatalogRuntime.AcquireSnapshot();
    var delayed = new DelayedGameDataSource(new AuraGameDataDefinition
    {
        Key = new AuraGameDataKey("Buff", "field_buff"),
        OwnerModId = "DelayedMod",
        WriterId = AuraGameDataConstants.RegistryAuthorityId,
        SourceKind = AuraGameDataSourceKinds.Native,
        Fields = new Dictionary<string, string> { ["Id"] = "field_buff", ["Name"] = "Field" }
    });
    AuraGameDataCatalogRuntime.ConfigureSource(delayed, rebuildImmediately: false);
    AuraGameDataCatalogRuntime.Rebuild();
    Assert(AuraGameDataCatalogRuntime.State == AuraGameDataCatalogState.AwaitingNativeCapture
           && ReferenceEquals(AuraGameDataCatalogRuntime.AcquireSnapshot(), lastGood)
           && AuraGameDataCatalogRuntime.AcquireSnapshot().Version.NativeReady,
        "game data rejects incomplete native generations and preserves the last-good snapshot");

    delayed.CompleteCapture();
    AuraGameDataCatalogRuntime.Rebuild();
    var completed = AuraGameDataCatalogRuntime.AcquireSnapshot();
    Assert(AuraGameDataCatalogRuntime.State == AuraGameDataCatalogState.Ready
           && completed.Version.NativeReady
           && completed.TryGet("Buff", "field_buff", out _),
        "game data publishes a completed native generation after deferred capture");
}

void TestResourceProtocolV4()
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

void TestQualifiedResourceIdentityConflicts()
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
    Assert(AuraSharedIdentity.NormalizeRoleId("*Terrias_wuna_wuna") == "Terrias_wuna_wuna", "mod role id trims legacy star prefix");
    Assert(AuraSharedIdentity.IsRuntimeNumericId("76561198326385152"), "long numeric runtime owner id detected");
    Assert(!AuraSharedIdentity.IsUsableRoleId("76561198326385152"), "long numeric runtime owner id is not a role");
    Assert(AuraSharedIdentity.SelectRoleId("76561198326385152", "Terrias_wuna_wuna") == "Terrias_wuna_wuna",
        "role selector falls back past runtime owner id");
    Assert(AuraSharedIdentity.SelectRoleId("wuna", "Terrias_wuna_wuna") == "wuna",
        "role selector preserves usable short mod role id");

    Assert(AuraSharedContentId.Matches("careercard_*8", "careercard_8", knownPrefixes: new[] { "careercard_" }),
        "content id matcher accepts internal table protocol markers");
    Assert(AuraSharedContentId.Matches("8", "careercard_8", knownPrefixes: new[] { "careercard_" }),
        "content id matcher accepts deterministic official short ids");
    Assert(AuraSharedContentId.Matches("solar_prayer", "Terrias_solar_prayer", "Terrias"),
        "content id matcher accepts owner-scoped short ids");

    var uniqueShort = AuraSharedContentId.Resolve(
        "solar_prayer",
        new[] { "Terrias_solar_prayer", "Other_card" },
        "Terrias");
    Assert(uniqueShort.Success && uniqueShort.ResolvedId == "Terrias_solar_prayer",
        "content id resolver returns the unique owner-scoped full id");
    var ambiguousShort = AuraSharedContentId.Resolve(
        "prayer",
        new[] { "Terrias_solar_prayer", "Other_lunar_prayer" });
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
        RoleIds = new List<string> { "Terrias_wuna_wuna", "SanGuoShaExp_shenzhugeliang" },
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
        new AuraJourneyCondition { Kind = AuraJourneyConditionKinds.AnyRole, Value = "Terrias_wuna_wuna" },
        new AuraJourneyCondition { Kind = AuraJourneyConditionKinds.PlayerCountAtLeast, Number = 2 }
    }, context), "journey condition evaluator");

    var request = new AuraJourneyCommitRequest
    {
        JourneyId = "Terrias.SolarMemory",
        OwnerModId = "Terrias",
        Action = "SelectNode",
        NodeId = "memory_1",
        Message = "selected first memory",
        Mutation = new AuraJourneyMutation
        {
            Run = new AuraJourneyRunBinding
            {
                RunId = "solar-memory-run",
                NativeModeKey = "Terrias_SolarMemoryMode",
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
        JourneyId = "Terrias.SolarMemory",
        OwnerModId = "Terrias",
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
        MapId = "Terrias_terrias_solar_memory_black_sun_after",
        FallbackMapId = "solar_memory_black_sun_after",
        NodeId = "Terrias_terrias_Sub_solar_memory_black_sun_after",
        Type = AuraJourneyNodeKinds.Event,
        Note = "普通事件",
        Level = "-1",
        DicePolicy = AuraJourneyDicePolicies.Default
    }, id => id == "Terrias_terrias_solar_memory_black_sun_after"
        ? new Dictionary<string, string> { ["Id"] = "old", ["Type"] = "", ["Note"] = "", ["Level"] = "" }
        : null);

    Assert(projection.Valid
           && projection.Data["Id"] == "Terrias_terrias_solar_memory_black_sun_after"
           && projection.Data["Type"] == AuraJourneyNodeKinds.Event
           && projection.Data["NodeId"] == "Terrias_terrias_Sub_solar_memory_black_sun_after"
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

sealed class FakeGameDataSource : IAuraGameDataSource
{
    private readonly IReadOnlyList<AuraGameDataDefinition> definitions;

    public FakeGameDataSource(params AuraGameDataDefinition[] definitions)
    {
        this.definitions = definitions.Select(value => value.Clone()).ToList();
    }

    public long Revision { get; private set; } = 1;

    public int CaptureCount { get; private set; }

    public AuraGameDataSourceSnapshot Capture()
    {
        CaptureCount++;
        return new AuraGameDataSourceSnapshot(Revision, definitions);
    }

    public void Invalidate()
    {
        Revision++;
    }
}

sealed class DelayedGameDataSource : IAuraGameDataSource
{
    private readonly IReadOnlyList<AuraGameDataDefinition> definitions;
    private bool complete;

    public DelayedGameDataSource(params AuraGameDataDefinition[] definitions)
    {
        this.definitions = definitions.Select(value => value.Clone()).ToList();
    }

    public long Revision { get; private set; } = 1;

    public AuraGameDataSourceSnapshot Capture()
    {
        return new AuraGameDataSourceSnapshot(
            Revision,
            complete ? definitions : Array.Empty<AuraGameDataDefinition>(),
            complete);
    }

    public void CompleteCapture()
    {
        complete = true;
    }

    public void Invalidate()
    {
        Revision++;
        complete = false;
    }
}
