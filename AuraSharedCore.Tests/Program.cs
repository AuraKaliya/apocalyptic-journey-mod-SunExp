using AuraShared.Core;
using AuraJourney.Shared;
using AuraMode.Shared;
using AuraOnline.Shared;
using AuraDirector.Shared;
using AuraRole.Shared;
using AuraGameData.Shared;
using Newtonsoft.Json.Linq;
using static CoreTestSuite;

assertions = 0;
var tempRoot = Path.Combine(Path.GetTempPath(), "AuraSharedCore.Tests", Guid.NewGuid().ToString("N"));
var sourceRoot = Path.Combine(Path.GetTempPath(), "AuraSharedCore.Tests.Sources", Guid.NewGuid().ToString("N"));
CoreTestSuite.tempRoot = tempRoot;
CoreTestSuite.sourceRoot = sourceRoot;
Directory.CreateDirectory(tempRoot);
Directory.CreateDirectory(sourceRoot);
AuraSharedPaths.RootDirectory = tempRoot;

try
{
    TestPresentationMaterialCoordinatorContracts();
    TestNativeCardPresentationBoundary();

    Assert(AuraModeRunIdentity.IsNativeWorldSimulation(
            AuraModeRunIdentity.NativeWorldSimulationModeType,
            AuraModeRunIdentity.NativeWorldSimulationModeId,
            null),
        "native world-simulation identity requires explicit provenance and permits no active custom mode");
    Assert(!AuraModeRunIdentity.IsNativeWorldSimulation(
            AuraModeRunIdentity.NativeWorldSimulationModeType,
            "",
            null),
        "normal-hosted runs without explicit provenance fail closed");
    Assert(!AuraModeRunIdentity.IsNativeWorldSimulation(
            AuraModeRunIdentity.NativeWorldSimulationModeType,
            AuraModeRunIdentity.NativeWorldSimulationModeId,
            new AuraActiveModeSnapshot
            {
                Status = AuraModeStates.Active,
                ModeId = "Terrias:solar-memory",
                OwnerModId = "Terrias",
                Run = new AuraModeRunBinding { SaveSlotId = "SolarRun" }
            },
            "SolarRun"),
        "an active custom mode overrides native-host structural similarity");
    Assert(AuraModeRunIdentity.IsNativeWorldSimulation(
            AuraModeRunIdentity.NativeWorldSimulationModeType,
            AuraModeRunIdentity.NativeWorldSimulationModeId,
            new AuraActiveModeSnapshot
            {
                Status = AuraModeStates.Active,
                ModeId = "Terrias:solar-memory",
                OwnerModId = "Terrias",
                Run = new AuraModeRunBinding { SaveSlotId = "RetiredSolarRun" }
            },
            "Normal123"),
        "a stale custom-mode snapshot from another save does not block explicit native provenance");

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

    var leasePath = Path.Combine(tempRoot, "Logs", "lease-test.lock");
    Directory.CreateDirectory(Path.GetDirectoryName(leasePath)!);
    File.WriteAllText(leasePath, "lease");
    using (var lease = new FileStream(
               leasePath,
               FileMode.Open,
               FileAccess.ReadWrite,
               FileShare.Read))
    {
        Assert(storage.IsFileWriteLeaseHeld(leasePath),
            "shared storage detects a held cross-process file lease");
    }
    Assert(!storage.IsFileWriteLeaseHeld(leasePath),
        "shared storage reports a released cross-process file lease");

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
    Assert(first.Success && first.Changed && first.Revision == 1, "first shared write");

    var unchanged = storage.Write(new AuraSharedStorageRequest
    {
        Scope = AuraSharedStorageScopes.Shared,
        System = "Test",
        FileName = "shared.json",
        WriterId = "TestAuthority",
        AuthorityId = "TestAuthority",
        ExpectedRevision = 1,
        PayloadJson = "{\"value\":1}"
    });
    Assert(unchanged.Success
           && !unchanged.Changed
           && unchanged.Revision == 1
           && (!Directory.Exists(
                   Path.Combine(tempRoot, "Backups", "Storage", "Versions"))
               || !Directory.EnumerateFiles(
                   Path.Combine(tempRoot, "Backups", "Storage", "Versions"),
                   "*.bak",
                   SearchOption.AllDirectories).Any()),
        "semantic no-op write preserves revision and creates no backup");
    Assert(!OperationLogContains(tempRoot, "StorageWrite", "Unchanged"),
        "semantic no-op write creates no operation-log entry");

    var retentionRevision = 0L;
    for (var value = 0; value < 20; value++)
    {
        var retained = storage.Write(new AuraSharedStorageRequest
        {
            Scope = AuraSharedStorageScopes.Shared,
            System = "Retention",
            FileName = "bounded.json",
            WriterId = "RetentionAuthority",
            AuthorityId = "RetentionAuthority",
            ExpectedRevision = retentionRevision,
            PayloadJson = "{\"value\":" + value + "}"
        });
        Assert(retained.Success && retained.Changed, "retention write " + value);
        retentionRevision = retained.Revision;
    }
    Assert(Directory.EnumerateFiles(
               Path.Combine(tempRoot, "Backups", "Storage", "Versions"),
               "*.bak",
               SearchOption.AllDirectories).Count()
           <= AuraSharedStorageCoordinator.MaximumBackupsPerDocument,
        "per-document storage backup retention is bounded");

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

    var transactionDirectory = Path.Combine(tempRoot, "Data", "TransactionTest");
    Directory.CreateDirectory(transactionDirectory);
    var transactionTarget = Path.Combine(transactionDirectory, "target.bin");
    var transactionStaged = Path.Combine(transactionDirectory, "staged.bin");
    var transactionBackup = Path.Combine(transactionDirectory, "backup.bin");
    File.WriteAllText(transactionTarget, "old");
    File.WriteAllText(transactionStaged, "new");
    storage.ReplaceFileInsideRoot(
        transactionStaged,
        transactionTarget,
        transactionBackup);
    Assert(File.ReadAllText(transactionTarget) == "new"
           && File.ReadAllText(transactionBackup) == "old",
        "shared storage replacement retains an exact rollback file");
    storage.ReplaceFileInsideRoot(transactionBackup, transactionTarget);
    Assert(File.ReadAllText(transactionTarget) == "old"
           && !File.Exists(transactionBackup),
        "shared storage replacement restores a rollback file atomically");
    var transactionMoved = Path.Combine(transactionDirectory, "moved.bin");
    storage.MoveFileInsideRoot(transactionTarget, transactionMoved);
    storage.DeleteFileInsideRoot(transactionMoved);
    Assert(!File.Exists(transactionTarget) && !File.Exists(transactionMoved),
        "shared storage owns move and delete operations inside its root");
    var escapedDeleteRejected = false;
    try
    {
        storage.DeleteFileInsideRoot(Path.Combine(sourceRoot, "outside.bin"));
    }
    catch (InvalidDataException)
    {
        escapedDeleteRejected = true;
    }
    Assert(escapedDeleteRejected,
        "shared storage rejects file transaction paths outside its root");

    var portableDestinationDirectory = transactionDirectory;
    while (Path.Combine(portableDestinationDirectory, "replay-package.aura-replay.zip").Length < 220)
    {
        portableDestinationDirectory = Path.Combine(portableDestinationDirectory, "portable-segment");
    }
    var portableDestination = Path.Combine(portableDestinationDirectory, "replay-package.aura-replay.zip");
    using (var fileWrite = AuraSharedFileStore.BeginWrite("Replay", portableDestination))
    {
        Assert(fileWrite.StagingPath.Length < portableDestination.Length
               && fileWrite.StagingPath.Contains(
                   Path.Combine("Transactions", "FileWrites", "Replay"),
                   StringComparison.OrdinalIgnoreCase),
            "file transaction stages long-path exports under the bounded shared transaction root");
        fileWrite.Stream.Write(new byte[] { 1, 2, 3, 4 });
        fileWrite.Commit();
    }
    Assert(File.ReadAllBytes(portableDestination).SequenceEqual(new byte[] { 1, 2, 3, 4 }),
        "file transaction atomically commits bytes to the final long path");
    string abandonedStaging;
    using (var abandonedWrite = AuraSharedFileStore.BeginWrite(
               "Replay",
               Path.Combine(transactionDirectory, "abandoned.bin")))
    {
        abandonedStaging = abandonedWrite.StagingPath;
        abandonedWrite.Stream.WriteByte(9);
    }
    Assert(!File.Exists(abandonedStaging),
        "disposing an uncommitted file transaction removes its staging file");

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

    var emptyRegistration = AuraSharedBootstrapResult.FromRegistration(
        new AuraSharedRegistrationResultV4
        {
            Success = true,
            Activated = true,
            Status = AuraSharedRegistrationStatuses.Installed,
            ExpectedItemCount = 0,
            ProcessedItemCount = 0
        });
    Assert(emptyRegistration.Success
           && emptyRegistration.HasExplicitOutcome
           && emptyRegistration.Responses.Count == 0,
        "explicit successful empty registration remains successful");
    Assert(!AuraSharedBootstrapResult.FromResponses(Array.Empty<AuraSharedInstallResponse>()).Success,
        "legacy empty response list remains a failure");
    Assert(!AuraSharedBootstrapResult.FromRegistration(new AuraSharedRegistrationResultV4
        {
            Success = true,
            Items = new List<AuraSharedRegistrationItemResultV4>
            {
                new() { Success = false, Status = AuraSharedRegistrationStatuses.Invalid }
            }
        }).Success,
        "explicit registration success cannot mask a failed item");

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
    TestSharedDiscoveryProtocol();
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
    TestReplayVisibleStateRuntime();
    TestReplayPresentationRuntime();
    Console.WriteLine($"AuraSharedCore tests passed: {assertions} assertions.");
}

finally
{
    TryDelete(tempRoot);
    TryDelete(sourceRoot);
}

return;
