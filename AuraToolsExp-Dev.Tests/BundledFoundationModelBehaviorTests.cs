using AuraToolsExp.Dll.Features.AutoBattle;

internal static partial class AuraToolsTestSuite
{
    public static void TestBundledFoundationRegistrationPlanner()
    {
        var hashA = new string('A', 64);
        var hashB = new string('B', 64);
        var duplicateLate = RegistrationIdentity(
            "z/foundation-model-package-v5.json",
            "model-a",
            hashA,
            "5.0.0");
        var duplicateEarly = RegistrationIdentity(
            "a/foundation-model-package-v5.json",
            "model-a",
            hashA,
            "5.0.0");

        var forward = AuraToolsBundledFoundationRegistrationPlanner.Plan(
            new[] { duplicateLate, duplicateEarly },
            Array.Empty<BundledFoundationRegistrationPlanIdentity>());
        var reverse = AuraToolsBundledFoundationRegistrationPlanner.Plan(
            new[] { duplicateEarly, duplicateLate },
            Array.Empty<BundledFoundationRegistrationPlanIdentity>());
        Assert(
            Decision(forward, duplicateEarly).Disposition
            == BundledFoundationRegistrationPlanDisposition.Install,
            "bundled planner chooses stable relative path for exact batch duplicate");
        Assert(
            Decision(forward, duplicateLate).Disposition
            == BundledFoundationRegistrationPlanDisposition.DeduplicateBatch,
            "bundled planner deduplicates exact model id and artifact sha");
        Assert(
            PlanSignature(forward) == PlanSignature(reverse),
            "bundled planner exact duplicate result is input-order independent");

        var releaseConflict = RegistrationIdentity(
            "b/foundation-model-package-v5.json",
            "model-b",
            hashB,
            "5.0.0");
        var conflictForward = AuraToolsBundledFoundationRegistrationPlanner.Plan(
            new[] { duplicateLate, releaseConflict, duplicateEarly },
            Array.Empty<BundledFoundationRegistrationPlanIdentity>());
        var conflictReverse = AuraToolsBundledFoundationRegistrationPlanner.Plan(
            new[] { releaseConflict, duplicateEarly, duplicateLate },
            Array.Empty<BundledFoundationRegistrationPlanIdentity>());
        Assert(
            conflictForward.All(decision =>
                decision.Disposition
                == BundledFoundationRegistrationPlanDisposition.Conflict),
            "bundled planner rejects the whole same-release-key group");
        Assert(
            PlanSignature(conflictForward) == PlanSignature(conflictReverse),
            "bundled planner same-release conflict is input-order independent");

        var existing = RegistrationIdentity(
            "installed/model-a.json",
            "model-a",
            hashA,
            "5.0.0");
        var exactExisting = AuraToolsBundledFoundationRegistrationPlanner.Plan(
            new[] { duplicateEarly },
            new[] { existing });
        Assert(
            exactExisting.Single().Disposition
            == BundledFoundationRegistrationPlanDisposition.DeduplicateExisting,
            "bundled planner deduplicates exact installed model identity");

        var changedArtifact = RegistrationIdentity(
            "changed/foundation-model-package-v5.json",
            "model-a",
            hashB,
            "5.0.0");
        var changedExisting = AuraToolsBundledFoundationRegistrationPlanner.Plan(
            new[] { changedArtifact },
            new[] { existing });
        Assert(
            changedExisting.Single().Disposition
            == BundledFoundationRegistrationPlanDisposition.Conflict,
            "bundled planner rejects reused model id with changed artifact sha");

        var mixedExistingForward =
            AuraToolsBundledFoundationRegistrationPlanner.Plan(
                new[] { duplicateEarly, changedArtifact },
                new[] { existing });
        var mixedExistingReverse =
            AuraToolsBundledFoundationRegistrationPlanner.Plan(
                new[] { changedArtifact, duplicateEarly },
                new[] { existing });
        Assert(
            mixedExistingForward.All(decision =>
                decision.Disposition
                == BundledFoundationRegistrationPlanDisposition.Conflict),
            "bundled planner rejects exact and changed artifacts as one existing-model group");
        Assert(
            PlanSignature(mixedExistingForward)
            == PlanSignature(mixedExistingReverse),
            "bundled planner existing-model mixed artifact conflict is input-order independent");

        var reusedBatchId = AuraToolsBundledFoundationRegistrationPlanner.Plan(
            new[] { duplicateEarly, changedArtifact },
            Array.Empty<BundledFoundationRegistrationPlanIdentity>());
        Assert(
            reusedBatchId.All(decision =>
                decision.Disposition
                == BundledFoundationRegistrationPlanDisposition.Conflict),
            "bundled planner rejects every same-model-id candidate when batch artifacts differ");

        var exactBesideReleaseConflict =
            AuraToolsBundledFoundationRegistrationPlanner.Plan(
                new[] { duplicateEarly, releaseConflict },
                new[] { existing });
        Assert(
            exactBesideReleaseConflict.All(decision =>
                decision.Disposition
                == BundledFoundationRegistrationPlanDisposition.Conflict),
            "bundled planner rejects the complete incoming release group beside an installed owner");

        var newerRelease = RegistrationIdentity(
            "new/foundation-model-package-v5.json",
            "model-b",
            hashB,
            "5.0.1");
        var sideBySide = AuraToolsBundledFoundationRegistrationPlanner.Plan(
            new[] { newerRelease },
            new[] { existing });
        Assert(
            sideBySide.Single().Disposition
            == BundledFoundationRegistrationPlanDisposition.Install,
            "bundled planner permits a different model id at a newer version");

        TestBundledFoundationFileRollback();
    }

    public static void TestBundledFoundationModelLayout()
    {
        WithModelLayoutRoot(root =>
        {
            AddLayoutPackage(
                root,
                "同名角色 [career_12]",
                "同名使魔 [Partner_10012]");
            AddLayoutPackage(
                root,
                "同名角色 [career_13]",
                "同名使魔 [Partner_10013]",
                "玩家自定义发布名");

            var discovery = AuraToolsBundledFoundationModelLayout.Discover(
                root,
                CancellationToken.None);
            Assert(
                discovery.Sources.Count == 2 && discovery.Rejected == 0,
                "bundled layout discovers same display name with distinct role ids");

            var career12 = discovery.Sources.Single(source =>
                source.RoleDirectoryName.EndsWith(
                    "[career_12]",
                    StringComparison.Ordinal));
            Assert(
                AuraToolsBundledFoundationRegistrationPlanner
                    .TryResolveSourceManifest(
                        career12.ManifestDirectory,
                        career12.RelativeManifestPath,
                        out var resolvedManifest,
                        out var normalizedReference,
                        out _)
                && string.Equals(
                    resolvedManifest,
                    career12.ManifestPath,
                    StringComparison.OrdinalIgnoreCase)
                && normalizedReference == career12.RelativeManifestPath,
                "bundled registration resolves nested source directory without duplicating relative provenance");
            Assert(
                !AuraToolsBundledFoundationRegistrationPlanner
                    .TryResolveSourceManifest(
                        career12.ManifestDirectory,
                        "../" + AuraToolsBundledFoundationModelLayout.ManifestFileName,
                        out _,
                        out _,
                        out _),
                "bundled registration rejects parent traversal in source provenance");
            Assert(
                !AuraToolsBundledFoundationModelLayout.TryValidateIdentity(
                    career12,
                    "career_13",
                    "Partner_10012",
                    new string('A', 64),
                    out _),
                "bundled layout rejects role id suffix mismatch");
            Assert(
                !AuraToolsBundledFoundationModelLayout.TryValidateIdentity(
                    career12,
                    "career_12",
                    "Partner_10013",
                    new string('C', 64),
                    out _),
                "bundled layout rejects partner id suffix mismatch");
            var career13 = discovery.Sources.Single(source =>
                source.RoleDirectoryName.EndsWith(
                    "[career_13]",
                    StringComparison.Ordinal));
            Assert(
                AuraToolsBundledFoundationModelLayout.TryValidateIdentity(
                    career13,
                    "career_13",
                    "Partner_10013",
                    new string('C', 64),
                    out _)
                && career13.ReleaseDirectoryName == "玩家自定义发布名",
                "bundled layout ignores user-authored release labels for model identity");
            Assert(
                AuraToolsBundledFoundationModelLayout.TryResolveWeightsPath(
                    career12,
                    AuraToolsBundledFoundationModelLayout.WeightsFileName,
                    out var weightsPath,
                    out _)
                && File.Exists(weightsPath),
                "bundled layout resolves canonical weights beside nested manifest");
        });

        WithModelLayoutRoot(root =>
        {
            File.WriteAllText(
                Path.Combine(
                    root,
                    AuraToolsBundledFoundationModelLayout.LegacyV4ManifestFileName),
                "{}");
            AddLayoutPackage(root, "角色甲 [career_1]", "使魔甲 [partner_1]");
            AddLayoutPackage(
                root,
                "角色乙 [career_2]",
                "使魔乙 [partner_2]",
                "第二版");

            var discovery = AuraToolsBundledFoundationModelLayout.Discover(
                root,
                CancellationToken.None);
            Assert(
                discovery.Sources.Count == 3
                && discovery.Sources.Count(source => source.LegacyRootPackage) == 1,
                "bundled layout keeps one legacy v4 package beside two-level packages");
        });

        WithModelLayoutRoot(root =>
        {
            AddLayoutPackage(
                root,
                "伪装\u202e角色 [career_1]",
                "使魔 [partner_1]");
            var discovery = AuraToolsBundledFoundationModelLayout.Discover(
                root,
                CancellationToken.None);
            Assert(
                discovery.Sources.Count == 1
                && !AuraToolsBundledFoundationModelLayout.TryValidateIdentity(
                    discovery.Sources[0],
                    "career_1",
                    "partner_1",
                    new string('A', 64),
                    out _),
                "bundled layout rejects bidi role directory");
        });

        WithModelLayoutRoot(root =>
        {
            var release = Path.Combine(
                root,
                "角色 [career_1]",
                "使魔 [partner_1]",
                "玩家发布");
            var fourthLevel = Path.Combine(release, "unexpected");
            Directory.CreateDirectory(fourthLevel);
            File.WriteAllText(
                Path.Combine(
                    fourthLevel,
                    AuraToolsBundledFoundationModelLayout.ManifestFileName),
                "{}");
            var discovery = AuraToolsBundledFoundationModelLayout.Discover(
                root,
                CancellationToken.None);
            Assert(
                discovery.Sources.Count == 0 && discovery.Rejected > 0,
                "bundled layout rejects an extra fourth directory level");
        });

        WithModelLayoutRoot(root =>
        {
            AddLayoutPackage(
                root,
                "旧角色 [career_1]",
                "旧发布 [aaaaaaaaaaaa]");
            var discovery = AuraToolsBundledFoundationModelLayout.Discover(
                root,
                CancellationToken.None);
            var source = discovery.Sources.Single();
            Assert(
                AuraToolsBundledFoundationModelLayout.TryValidateIdentity(
                    source,
                    "career_1",
                    "Partner_10001",
                    new string('A', 64),
                    out _)
                && source.LegacyHashReleasePackage,
                "bundled layout reads the previous hash-suffixed release layout as migration input");
        });
    }

    private static BundledFoundationRegistrationPlanIdentity RegistrationIdentity(
        string source,
        string modelId,
        string sha256,
        string version)
    {
        return new BundledFoundationRegistrationPlanIdentity
        {
            SourceReference = source,
            ModelId = modelId,
            SourceSha256 = sha256,
            RoleId = "career_1",
            PartnerId = "Partner_10001",
            EnabledRewardCardPackIds = new List<string>
            {
                "cardpack_1",
                "cardpack_2"
            },
            ModelVersion = version
        };
    }

    private static BundledFoundationRegistrationPlanDecision Decision(
        IEnumerable<BundledFoundationRegistrationPlanDecision> decisions,
        BundledFoundationRegistrationPlanIdentity candidate)
    {
        return decisions.Single(decision => ReferenceEquals(
            decision.Candidate,
            candidate));
    }

    private static string PlanSignature(
        IEnumerable<BundledFoundationRegistrationPlanDecision> decisions)
    {
        return string.Join(
            "|",
            decisions
                .OrderBy(
                    decision => decision.Candidate.SourceReference,
                    StringComparer.Ordinal)
                .Select(decision =>
                    decision.Candidate.SourceReference
                    + ":"
                    + decision.Disposition));
    }

    private static void AddLayoutPackage(
        string root,
        string roleDirectory,
        string partnerDirectory,
        string releaseDirectory = "")
    {
        var directory = string.IsNullOrWhiteSpace(releaseDirectory)
            ? Path.Combine(root, roleDirectory, partnerDirectory)
            : Path.Combine(
                root,
                roleDirectory,
                partnerDirectory,
                releaseDirectory);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(
                directory,
                AuraToolsBundledFoundationModelLayout.ManifestFileName),
            "{}");
        File.WriteAllBytes(
            Path.Combine(
                directory,
                AuraToolsBundledFoundationModelLayout.WeightsFileName),
            new byte[] { 1 });
    }

    private static void WithModelLayoutRoot(Action<string> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AuraToolsBundledLayoutTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void TestBundledFoundationFileRollback()
    {
        WithFileTransactionRoot(root =>
        {
            var weightsPath = Path.Combine(root, "weights.bin");
            var bundlePath = Path.Combine(root, "bundle.json");
            var originalWeights = new byte[] { 1, 2, 3, 4 };
            var originalBundle = new byte[] { 5, 6, 7, 8 };
            File.WriteAllBytes(weightsPath, originalWeights);
            File.WriteAllBytes(bundlePath, originalBundle);

            using var transaction = new BundledFoundationFileTransaction(
                new TestBundledFoundationFileStorage(),
                target =>
                {
                    if (string.Equals(
                            target,
                            Path.GetFullPath(bundlePath),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("injected bundle publish failure");
                    }
                });
            var failed = false;
            try
            {
                transaction.Publish(
                    weightsPath,
                    staged => File.WriteAllBytes(staged, new byte[] { 9, 9 }));
                transaction.Publish(
                    bundlePath,
                    staged => File.WriteAllBytes(staged, new byte[] { 8, 8 }));
            }
            catch (IOException)
            {
                failed = transaction.TryRollback(out _);
            }

            Assert(
                failed
                && File.ReadAllBytes(weightsPath).SequenceEqual(originalWeights)
                && File.ReadAllBytes(bundlePath).SequenceEqual(originalBundle),
                "bundled file transaction restores old weights and bundle after publish failure");
            Assert(
                !HasTransactionDebris(root),
                "bundled file transaction removes staging and rollback files after publish failure");
        });

        WithFileTransactionRoot(root =>
        {
            var weightsPath = Path.Combine(root, "weights.bin");
            var bundlePath = Path.Combine(root, "bundle.json");
            var originalWeights = new byte[] { 10, 11, 12 };
            var originalBundle = new byte[] { 13, 14, 15 };
            File.WriteAllBytes(weightsPath, originalWeights);
            File.WriteAllBytes(bundlePath, originalBundle);

            using var transaction =
                new BundledFoundationFileTransaction(
                    new TestBundledFoundationFileStorage());
            transaction.Publish(
                weightsPath,
                staged => File.WriteAllBytes(staged, new byte[] { 20, 21 }));
            transaction.Publish(
                bundlePath,
                staged => File.WriteAllBytes(staged, new byte[] { 22, 23 }));
            var committed = BundledFoundationFileTransaction.TryCommitIndex(
                new[] { transaction },
                () => throw new IOException("injected models.json failure"),
                out var failure,
                out var rollbackDiagnostic);

            Assert(
                !committed
                && failure is IOException
                && string.IsNullOrWhiteSpace(rollbackDiagnostic)
                && File.ReadAllBytes(weightsPath).SequenceEqual(originalWeights)
                && File.ReadAllBytes(bundlePath).SequenceEqual(originalBundle),
                "bundled index commit failure restores exact old weights and bundle bytes");
            Assert(
                !HasTransactionDebris(root),
                "bundled index commit rollback removes staging and backup files");
        });
    }

    private static bool HasTransactionDebris(string root)
    {
        return Directory.EnumerateFiles(root)
            .Select(Path.GetFileName)
            .Any(name => name != null
                         && (name.Contains(".staging-", StringComparison.Ordinal)
                             || name.Contains(
                                 ".rollback-",
                                 StringComparison.Ordinal)));
    }

    private static void WithFileTransactionRoot(Action<string> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AuraToolsBundledFileTransactionTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class TestBundledFoundationFileStorage
        : IBundledFoundationFileStorage
    {
        public void Move(string sourcePath, string destinationPath)
        {
            File.Move(sourcePath, destinationPath);
        }

        public void Replace(
            string sourcePath,
            string destinationPath,
            string backupPath = "")
        {
            File.Replace(
                sourcePath,
                destinationPath,
                string.IsNullOrWhiteSpace(backupPath) ? null : backupPath,
                ignoreMetadataErrors: true);
        }

        public void Delete(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
