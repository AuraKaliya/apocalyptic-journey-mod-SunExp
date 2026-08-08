using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AuraCombatAi.Shared;
using Newtonsoft.Json;

namespace AuraFoundationTrainer.Worker;

internal static class CombatFoundationCheckpointCatalogStore
{
    private const int ChecksumVersion = 1;

    private const string ResetMarkerProtocol =
        "foundation-checkpoint-reset-v1";

    private const string ResetMarkerFileName =
        ".foundation-checkpoint-reset-v1.pending";

    public static string LegacyCheckpointPath(string activePath)
    {
        return Path.Combine(
            Path.GetDirectoryName(
                CombatFoundationPathRuntime.Normalize(activePath)) ?? "",
            CombatFoundationWorkerProtocol.LegacyCheckpointFileName);
    }

    public static IReadOnlyList<string> ResumeCandidates(
        string requestedPath,
        bool explicitlySelected)
    {
        if (ResetPendingForCandidate(requestedPath))
        {
            return Array.Empty<string>();
        }
        if (explicitlySelected)
        {
            return new[] { requestedPath };
        }
        var legacyPath = LegacyCheckpointPath(requestedPath);
        return new[]
        {
            requestedPath,
            CombatFoundationCheckpointStorage.BackupPath(requestedPath),
            legacyPath,
            CombatFoundationCheckpointStorage.BackupPath(legacyPath)
        };
    }

    public static bool TrySelectResumeCandidate(
        string requestedPath,
        bool explicitlySelected,
        out string selectedPath,
        out CombatFoundationWorkerCheckpoint? checkpoint,
        out CombatFoundationEpisodeSnapshot? snapshot,
        out string diagnostic)
    {
        selectedPath = "";
        checkpoint = null;
        snapshot = null;
        var errors = new List<string>();
        foreach (var candidate in ResumeCandidates(
                     requestedPath,
                     explicitlySelected)
                 .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryReadResumeCandidate(
                    candidate,
                    out checkpoint,
                    out snapshot,
                    out var candidateDiagnostic))
            {
                selectedPath = candidate;
                diagnostic = "";
                return true;
            }
            if (!string.IsNullOrWhiteSpace(candidateDiagnostic))
            {
                errors.Add(Path.GetFileName(candidate) + ": "
                           + candidateDiagnostic);
            }
        }
        checkpoint = null;
        snapshot = null;
        diagnostic = errors.Count == 0
            ? "no checkpoint candidate exists"
            : string.Join(" | ", errors);
        return false;
    }

    public static bool TryReadResumeCandidate(
        string path,
        out CombatFoundationWorkerCheckpoint? checkpoint,
        out CombatFoundationEpisodeSnapshot? snapshot,
        out string diagnostic)
    {
        checkpoint = null;
        snapshot = null;
        if (ResetPendingForCandidate(path))
        {
            diagnostic = "checkpoint reset is pending";
            return false;
        }
        if (string.IsNullOrWhiteSpace(path)
            || !CombatFoundationPathRuntime.FileExists(path))
        {
            diagnostic = "checkpoint file is missing";
            return false;
        }
        try
        {
            checkpoint = JsonConvert.DeserializeObject<
                CombatFoundationWorkerCheckpoint>(
                CombatFoundationCheckpointStorage.ReadAllTextShared(path));
            if (checkpoint == null
                || checkpoint.SchemaVersion
                   != CombatFoundationWorkerProtocol.SchemaVersion
                || checkpoint.Resume == null
                || checkpoint.Resume.SchemaVersion
                   != CombatFoundationWorkerProtocol.SchemaVersion)
            {
                throw new InvalidDataException(
                    "checkpoint or resume protocol is incompatible");
            }
            snapshot = checkpoint.EpisodeSnapshot
                       ?? new CombatFoundationEpisodeSnapshot
                       {
                           StorageVersion = 1,
                           Path = checkpoint.EpisodesPath,
                           EpisodeCount = -1,
                           CreatedUtc = checkpoint.UpdatedUtc
                       };
            CombatFoundationCheckpointStorage.ValidateEpisodeSnapshotEnvelope(
                snapshot);
            diagnostic = "";
            return true;
        }
        catch (Exception ex)
        {
            checkpoint = null;
            snapshot = null;
            diagnostic = ex.Message;
            return false;
        }
    }

    public static CombatFoundationCheckpointCatalogReadResult Read(string path)
    {
        if (ResetPendingForCandidate(path))
        {
            return new CombatFoundationCheckpointCatalogReadResult
            {
                RecoveryUncertain = true,
                Diagnostic = "checkpoint reset is pending"
            };
        }
        var candidates = new[]
        {
            path,
            CombatFoundationCheckpointStorage.BackupPath(path)
        };
        var existing = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate)
                                && CombatFoundationPathRuntime.FileExists(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (existing.Count == 0)
        {
            return HasHistoricalArtifacts(path)
                ? new CombatFoundationCheckpointCatalogReadResult
                {
                    RecoveryUncertain = true,
                    Diagnostic = "checkpoint catalog is missing while historical artifacts exist"
                }
                : new CombatFoundationCheckpointCatalogReadResult();
        }

        var errors = new List<string>();
        foreach (var candidate in existing)
        {
            try
            {
                var catalog = JsonConvert.DeserializeObject<
                    CombatFoundationCheckpointCatalog>(
                    CombatFoundationCheckpointStorage.ReadAllTextShared(
                        candidate));
                if (!TryValidateCatalog(
                        catalog,
                        path,
                        validateArtifactContents: false,
                        out var unchecksummedLegacyCatalog,
                        out var diagnostic))
                {
                    throw new InvalidDataException(diagnostic);
                }
                return new CombatFoundationCheckpointCatalogReadResult
                {
                    Catalog = catalog,
                    RecoveredFromBackup = !string.Equals(
                        candidate,
                        path,
                        StringComparison.OrdinalIgnoreCase),
                    RecoveryUncertain = unchecksummedLegacyCatalog,
                    CanRewriteSafely = unchecksummedLegacyCatalog,
                    Diagnostic = unchecksummedLegacyCatalog
                        ? "legacy catalog has no checksum; recovery is read-only until a checksummed generation is committed"
                        : errors.Count == 0
                            ? ""
                            : "primary catalog was invalid; backup recovered"
                };
            }
            catch (Exception ex)
            {
                errors.Add(Path.GetFileName(candidate) + ": " + ex.Message);
            }
        }
        return new CombatFoundationCheckpointCatalogReadResult
        {
            RecoveryUncertain = true,
            Diagnostic = "no valid checkpoint catalog generation: "
                         + string.Join(" | ", errors)
        };
    }

    public static CombatFoundationCheckpointArtifactRetention
        ReadArtifactRetention(string catalogPath)
    {
        if (ResetPendingForCandidate(catalogPath))
        {
            return new CombatFoundationCheckpointArtifactRetention();
        }
        var checkpointPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var snapshotPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var validGenerations = 0;
        foreach (var candidate in new[]
                 {
                     catalogPath,
                     CombatFoundationCheckpointStorage.BackupPath(catalogPath)
                 }
                 .Where(path => !string.IsNullOrWhiteSpace(path))
                 .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!CombatFoundationPathRuntime.FileExists(candidate))
                {
                    continue;
                }
                var catalog = JsonConvert.DeserializeObject<
                    CombatFoundationCheckpointCatalog>(
                    CombatFoundationCheckpointStorage.ReadAllTextShared(
                        candidate));
                if (!TryValidateCatalog(
                        catalog,
                        catalogPath,
                        validateArtifactContents: false,
                        out _,
                        out _)
                    || catalog == null)
                {
                    continue;
                }
                validGenerations++;
                foreach (var entry in catalog.Entries)
                {
                    checkpointPaths.Add(
                        CombatFoundationPathRuntime.Normalize(
                            entry.CheckpointPath));
                    snapshotPaths.Add(
                        CombatFoundationPathRuntime.Normalize(
                            entry.EpisodeSnapshotPath));
                }
            }
            catch
            {
                // Invalid generations cannot authorize retention or cleanup.
            }
        }
        return new CombatFoundationCheckpointArtifactRetention
        {
            ValidGenerationCount = validGenerations,
            CheckpointPaths = checkpointPaths.ToArray(),
            SnapshotPaths = snapshotPaths.ToArray()
        };
    }

    public static IReadOnlyList<string> ReadActiveSnapshotRetentionPaths(
        string checkpointPath,
        string baseEpisodesPath)
    {
        var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fullBasePath = CombatFoundationPathRuntime.Normalize(
            baseEpisodesPath);
        var root = Path.GetDirectoryName(fullBasePath) ?? "";
        var snapshotPrefix = Path.GetFileNameWithoutExtension(fullBasePath)
                             + ".snapshot-";
        foreach (var candidate in new[]
                 {
                     checkpointPath,
                     CombatFoundationCheckpointStorage.BackupPath(
                         checkpointPath)
                 }
                 .Where(path => !string.IsNullOrWhiteSpace(path))
                 .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!CombatFoundationPathRuntime.FileExists(candidate))
                {
                    continue;
                }
                var checkpoint = JsonConvert.DeserializeObject<
                    CombatFoundationWorkerCheckpoint>(
                    CombatFoundationCheckpointStorage.ReadAllTextShared(
                        candidate));
                if (checkpoint == null
                    || checkpoint.SchemaVersion
                       != CombatFoundationWorkerProtocol.SchemaVersion
                    || checkpoint.Resume == null
                    || checkpoint.Resume.SchemaVersion
                       != CombatFoundationWorkerProtocol.SchemaVersion)
                {
                    continue;
                }
                var snapshot = checkpoint.EpisodeSnapshot;
                var snapshotPath = snapshot?.Path ?? checkpoint.EpisodesPath;
                var normalized = CombatFoundationPathRuntime.Normalize(
                    snapshotPath);
                if (string.IsNullOrWhiteSpace(root)
                    || !string.Equals(
                        Path.GetDirectoryName(normalized),
                        root,
                        StringComparison.OrdinalIgnoreCase)
                    || !Path.GetFileName(normalized).StartsWith(
                        snapshotPrefix,
                        StringComparison.OrdinalIgnoreCase)
                    || !IsSafeTopLevelRegularFile(normalized, root)
                    || snapshot != null
                       && snapshot.Length > 0L
                       && CombatFoundationPathRuntime.FileLength(normalized)
                       != snapshot.Length)
                {
                    continue;
                }
                retained.Add(normalized);
            }
            catch
            {
                // A malformed pointer must not influence destructive cleanup.
            }
        }
        return retained.ToArray();
    }

    public static void PrepareForWrite(
        CombatFoundationCheckpointCatalog catalog,
        string catalogPath,
        string newEntryId = "")
    {
        if (catalog == null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }
        catalog.Entries ??= new List<CombatFoundationCheckpointCatalogEntry>();
        foreach (var entry in catalog.Entries)
        {
            var validateArtifact = !ValidSha256(
                                       entry.CheckpointContentSha256)
                                   || !ValidSha256(
                                       entry.EpisodeSnapshotContentSha256)
                                   || !string.IsNullOrWhiteSpace(newEntryId)
                                   && string.Equals(
                                       entry.Id,
                                       newEntryId,
                                       StringComparison.Ordinal);
            if (!validateArtifact)
            {
                continue;
            }
            entry.CheckpointContentSha256 =
                CombatFoundationCheckpointStorage.ComputeFileSha256(
                    entry.CheckpointPath);
            var checkpoint = JsonConvert.DeserializeObject<
                CombatFoundationWorkerCheckpoint>(
                CombatFoundationCheckpointStorage.ReadAllTextShared(
                    entry.CheckpointPath))
                ?? throw new InvalidDataException(
                    "Checkpoint catalog entry cannot be read before commit.");
            var snapshot = checkpoint.EpisodeSnapshot
                           ?? throw new InvalidDataException(
                               "Checkpoint catalog entry has no snapshot descriptor.");
            CombatFoundationCheckpointStorage.ValidateEpisodeSnapshotEnvelope(
                snapshot);
            entry.EpisodeSnapshotContentSha256 = snapshot.ContentSha256;
            if (!EntryDescriptorMatches(
                    catalog,
                    entry,
                    checkpoint,
                    snapshot,
                    out var diagnostic))
            {
                throw new InvalidDataException(diagnostic);
            }
        }
        if (catalog.Generation == long.MaxValue)
        {
            throw new InvalidDataException(
                "Checkpoint catalog generation is exhausted.");
        }
        catalog.Generation = Math.Max(0L, catalog.Generation) + 1L;
        catalog.ChecksumVersion = ChecksumVersion;
        catalog.ContentChecksumSha256 = ComputeChecksum(catalog);
        if (!TryValidateCatalog(
                catalog,
                catalogPath,
                validateArtifactContents: false,
                out _,
                out var validationDiagnostic))
        {
            throw new InvalidDataException(validationDiagnostic);
        }
    }

    public static void WriteCatalogAtomic(
        string catalogPath,
        string contents,
        CombatFoundationCheckpointCatalogReadResult priorRead)
    {
        if (priorRead?.RecoveredFromBackup == true)
        {
            var verifiedBackup = Read(catalogPath);
            if (!verifiedBackup.RecoveredFromBackup
                || verifiedBackup.RecoveryUncertain
                   && !verifiedBackup.CanRewriteSafely
                || verifiedBackup.Catalog == null)
            {
                throw new InvalidDataException(
                    "Recovered checkpoint catalog backup changed before commit.");
            }
            CombatFoundationPathRuntime.DeleteFile(catalogPath);
            if (CombatFoundationPathRuntime.FileExists(catalogPath)
                || !CombatFoundationPathRuntime.FileExists(
                    CombatFoundationCheckpointStorage.BackupPath(catalogPath)))
            {
                throw new IOException(
                    "Checkpoint catalog primary could not be isolated before recovery commit.");
            }
        }
        CombatFoundationCheckpointStorage.WriteAtomicText(
            catalogPath,
            contents,
            retainBackup: true);
    }

    public static bool ExecuteCleanupIfCertain(
        CombatFoundationCheckpointCatalogReadResult readResult,
        Action cleanup)
    {
        if (readResult == null
            || readResult.RecoveryUncertain
            || cleanup == null)
        {
            return false;
        }
        cleanup();
        return true;
    }

    public static void EnsureWritableBaseline(
        CombatFoundationCheckpointCatalogReadResult readResult)
    {
        if (readResult == null)
        {
            throw new ArgumentNullException(nameof(readResult));
        }
        if (readResult.RecoveryUncertain && !readResult.CanRewriteSafely)
        {
            throw new InvalidDataException(
                "Checkpoint catalog cannot be updated safely; immutable history "
                + "was preserved. "
                + readResult.Diagnostic);
        }
    }

    public static bool TryValidateSelectedImmutableCheckpoint(
        string catalogPath,
        string checkpointPath,
        CombatFoundationWorkerCheckpoint checkpoint,
        out string diagnostic)
    {
        diagnostic = "";
        try
        {
            var root = Path.GetDirectoryName(
                           CombatFoundationPathRuntime.Normalize(catalogPath))
                       ?? "";
            var immutableRoot = Path.Combine(
                root,
                CombatFoundationCheckpointCatalogProtocol.ImmutableDirectoryName);
            if (!IsContainedPath(checkpointPath, immutableRoot))
            {
                diagnostic = "";
                return true;
            }
            var read = Read(catalogPath);
            if (read.Catalog == null
                || read.RecoveryUncertain && !read.CanRewriteSafely)
            {
                diagnostic = "immutable checkpoint catalog binding is unavailable: "
                             + read.Diagnostic;
                return false;
            }
            var entry = read.Catalog.Entries.SingleOrDefault(item => SamePath(
                item.CheckpointPath,
                checkpointPath));
            var snapshot = checkpoint.EpisodeSnapshot;
            if (entry == null
                || snapshot == null
                || !EntryDescriptorMatches(
                    read.Catalog,
                    entry,
                    checkpoint,
                    snapshot,
                    out diagnostic))
            {
                diagnostic = string.IsNullOrWhiteSpace(diagnostic)
                    ? "immutable checkpoint is absent from its catalog"
                    : diagnostic;
                return false;
            }
            if (ValidSha256(entry.CheckpointContentSha256)
                && !string.Equals(
                    CombatFoundationCheckpointStorage.ComputeFileSha256(
                        checkpointPath),
                    entry.CheckpointContentSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "immutable checkpoint content hash mismatch";
                return false;
            }
            if (ValidSha256(entry.EpisodeSnapshotContentSha256)
                && !string.Equals(
                    snapshot.ContentSha256,
                    entry.EpisodeSnapshotContentSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "immutable checkpoint snapshot hash binding mismatch";
                return false;
            }
            diagnostic = "";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = ex.Message;
            return false;
        }
    }

    public static string ResetMarkerPath(string artifactRoot)
    {
        return Path.Combine(
            CombatFoundationPathRuntime.Normalize(artifactRoot),
            ResetMarkerFileName);
    }

    public static bool HasPendingReset(CombatFoundationWorkerJob job)
    {
        if (job == null || string.IsNullOrWhiteSpace(job.CheckpointPath))
        {
            return false;
        }
        try
        {
            var root = Path.GetDirectoryName(
                CombatFoundationPathRuntime.Normalize(job.CheckpointPath));
            if (string.IsNullOrWhiteSpace(root))
            {
                return false;
            }
            var markerPath = ResetMarkerPath(root);
            return CombatFoundationPathRuntime.FileExists(markerPath)
                   || CombatFoundationPathRuntime.DirectoryExists(markerPath);
        }
        catch
        {
            return true;
        }
    }

    public static void ResetCheckpointArtifacts(
        CombatFoundationWorkerJob job,
        Action? afterResetMarkerCommitted = null)
    {
        if (!TryGetResetBoundary(
                job,
                out var artifactRoot,
                out var immutableDirectory,
                out var diagnostic))
        {
            throw new InvalidOperationException(
                "Checkpoint reset was refused: " + diagnostic);
        }

        var markerPath = ResetMarkerPath(artifactRoot);
        var marker = CreateResetMarker(job, artifactRoot, immutableDirectory);
        if (CombatFoundationPathRuntime.FileExists(markerPath))
        {
            if (!ResetMarkerMatches(
                    markerPath,
                    marker,
                    artifactRoot,
                    out diagnostic))
            {
                throw new InvalidOperationException(
                    "Checkpoint reset marker does not match this boundary: "
                    + diagnostic);
            }
        }
        else
        {
            CombatFoundationCheckpointStorage.WriteAtomicText(
                markerPath,
                JsonConvert.SerializeObject(marker),
                retainBackup: false);
            if (!ResetMarkerMatches(
                    markerPath,
                    marker,
                    artifactRoot,
                    out diagnostic))
            {
                throw new IOException(
                    "Checkpoint reset marker was not committed safely: "
                    + diagnostic);
            }
        }

        afterResetMarkerCommitted?.Invoke();

        var activeCheckpoint = CombatFoundationPathRuntime.Normalize(
            job.CheckpointPath);
        var legacyCheckpoint = LegacyCheckpointPath(activeCheckpoint);
        var resumePointers = new[]
            {
                activeCheckpoint,
                CombatFoundationCheckpointStorage.BackupPath(
                    activeCheckpoint),
                legacyCheckpoint,
                CombatFoundationCheckpointStorage.BackupPath(
                    legacyCheckpoint),
                CombatFoundationPathRuntime.Normalize(
                    job.CheckpointCatalogPath),
                CombatFoundationCheckpointStorage.BackupPath(
                    job.CheckpointCatalogPath)
            }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var pointer in resumePointers)
        {
            CombatFoundationPathRuntime.DeleteFile(pointer);
        }
        if (resumePointers.Any(path =>
                CombatFoundationPathRuntime.FileExists(path)
                || CombatFoundationPathRuntime.DirectoryExists(path)))
        {
            throw new IOException(
                "Checkpoint resume metadata remained after reset invalidation.");
        }

        DeleteActiveArtifactsWithinResetBoundary(
            artifactRoot,
            job.CheckpointPath,
            job.CheckpointEpisodesPath);
        DeleteLegacyArtifactsWithinResetBoundary(artifactRoot);
        if (CombatFoundationPathRuntime.DirectoryExists(immutableDirectory))
        {
            DeleteImmutableDirectoryWithinResetBoundary(
                artifactRoot,
                immutableDirectory);
        }
        CombatFoundationPathRuntime.DeleteFile(job.ModelSelectionAnchorPath);
        if (CombatFoundationPathRuntime.FileExists(job.ModelSelectionAnchorPath)
            || CombatFoundationPathRuntime.DirectoryExists(
                job.ModelSelectionAnchorPath)
            || CombatFoundationPathRuntime.DirectoryExists(immutableDirectory))
        {
            throw new IOException(
                "Checkpoint reset artifacts remained after cleanup.");
        }

        if (!ResetMarkerMatches(
                markerPath,
                marker,
                artifactRoot,
                out diagnostic))
        {
            throw new InvalidOperationException(
                "Checkpoint reset marker changed before completion: "
                + diagnostic);
        }
        CombatFoundationPathRuntime.DeleteFile(markerPath);
        if (CombatFoundationPathRuntime.FileExists(markerPath)
            || CombatFoundationPathRuntime.DirectoryExists(markerPath))
        {
            throw new IOException(
                "Checkpoint reset marker remained after completion.");
        }
    }

    public static bool TryGetResetBoundary(
        CombatFoundationWorkerJob job,
        out string artifactRoot,
        out string immutableDirectory,
        out string diagnostic)
    {
        artifactRoot = "";
        immutableDirectory = "";
        if (job == null)
        {
            diagnostic = "checkpoint reset job is missing";
            return false;
        }
        try
        {
            var paths = new[]
            {
                (job.CheckpointPath, ""),
                (job.CheckpointEpisodesPath, ""),
                (job.CheckpointCatalogPath,
                    CombatFoundationCheckpointCatalogProtocol.CatalogFileName),
                (job.ModelSelectionAnchorPath,
                    CombatFoundationCheckpointCatalogProtocol
                        .SelectionAnchorFileName)
            };
            var normalized = paths
                .Select(item => CombatFoundationPathRuntime.Normalize(item.Item1))
                .ToArray();
            if (normalized.Any(string.IsNullOrWhiteSpace)
                || normalized.Any(path => !SafeLeafName(
                    Path.GetFileName(path)))
                || normalized.Any(path => string.IsNullOrWhiteSpace(
                    Path.GetFileName(path)))
                || paths.Where((item, index) =>
                        !string.IsNullOrWhiteSpace(item.Item2)
                        && !string.Equals(
                        Path.GetFileName(normalized[index]),
                        item.Item2,
                        StringComparison.OrdinalIgnoreCase))
                    .Any())
            {
                diagnostic = "checkpoint reset artifact names are outside the fixed contract";
                return false;
            }
            var resolvedArtifactRoot = Path.GetDirectoryName(normalized[0]) ?? "";
            var volumeRoot = Path.GetPathRoot(resolvedArtifactRoot) ?? "";
            if (string.IsNullOrWhiteSpace(resolvedArtifactRoot)
                || string.Equals(
                    resolvedArtifactRoot.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    volumeRoot.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase)
                || normalized.Any(path => !string.Equals(
                    Path.GetDirectoryName(path),
                    resolvedArtifactRoot,
                    StringComparison.OrdinalIgnoreCase))
                || normalized.Any(
                    CombatFoundationPathRuntime.DirectoryExists)
                || normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                   != normalized.Length
                || normalized.Any(path => ContainsReparsePoint(
                    path,
                    resolvedArtifactRoot))
                || !TryGetActiveResetTargets(
                    resolvedArtifactRoot,
                    normalized[0],
                    normalized[1],
                    out _)
                || !TryGetActiveSnapshotTargets(
                    resolvedArtifactRoot,
                    normalized[1],
                    out _))
            {
                diagnostic = "checkpoint reset artifacts do not share a safe non-root directory";
                return false;
            }
            var resolvedImmutableDirectory = Path.Combine(
                resolvedArtifactRoot,
                CombatFoundationCheckpointCatalogProtocol.ImmutableDirectoryName);
            var resetMarkerPath = ResetMarkerPath(resolvedArtifactRoot);
            if (!IsContainedPath(
                    resolvedImmutableDirectory,
                    resolvedArtifactRoot)
                || UnsafeImmutableResetTree(resolvedImmutableDirectory)
                || UnsafeLegacyResetArtifacts(resolvedArtifactRoot)
                || CombatFoundationPathRuntime.DirectoryExists(resetMarkerPath)
                || CombatFoundationPathRuntime.FileExists(resetMarkerPath)
                   && !IsSafeTopLevelRegularFile(
                       resetMarkerPath,
                       resolvedArtifactRoot)
                || CombatFoundationPathRuntime.FileExists(resetMarkerPath)
                   && !ResetMarkerMatches(
                       resetMarkerPath,
                       CreateResetMarker(
                           job,
                           resolvedArtifactRoot,
                           resolvedImmutableDirectory),
                       resolvedArtifactRoot,
                       out _))
            {
                diagnostic = "checkpoint reset directory contains an unsafe path boundary";
                return false;
            }
            artifactRoot = resolvedArtifactRoot;
            immutableDirectory = resolvedImmutableDirectory;
            diagnostic = "";
            return true;
        }
        catch (Exception ex)
        {
            artifactRoot = "";
            immutableDirectory = "";
            diagnostic = ex.Message;
            return false;
        }
    }

    public static void DeleteImmutableDirectoryWithinResetBoundary(
        string artifactRoot,
        string immutableDirectory)
    {
        var fullRoot = CombatFoundationPathRuntime.Normalize(artifactRoot);
        var fullDirectory = CombatFoundationPathRuntime.Normalize(
            immutableDirectory);
        if (!string.Equals(
                Path.GetDirectoryName(fullDirectory),
                fullRoot,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                Path.GetFileName(fullDirectory),
                CombatFoundationCheckpointCatalogProtocol.ImmutableDirectoryName,
                StringComparison.OrdinalIgnoreCase)
            || UnsafeImmutableResetTree(fullDirectory))
        {
            throw new InvalidOperationException(
                "Checkpoint immutable reset boundary changed before deletion.");
        }
        if (!Directory.Exists(fullDirectory))
        {
            return;
        }
        foreach (var path in Directory.EnumerateFiles(
                     fullDirectory,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            var normalized = CombatFoundationPathRuntime.Normalize(path);
            if (!string.Equals(
                    Path.GetDirectoryName(normalized),
                    fullDirectory,
                    StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(normalized).StartsWith(
                    "foundation-checkpoint-",
                    StringComparison.OrdinalIgnoreCase)
                || (File.GetAttributes(normalized)
                    & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "Checkpoint immutable reset encountered an unsafe file.");
            }
            File.Delete(CombatFoundationPathRuntime.ForFileSystem(normalized));
        }
        if ((File.GetAttributes(fullDirectory) & FileAttributes.ReparsePoint) != 0
            || Directory.EnumerateFileSystemEntries(fullDirectory).Any())
        {
            throw new InvalidOperationException(
                "Checkpoint immutable reset directory changed during deletion.");
        }
        Directory.Delete(
            CombatFoundationPathRuntime.ForFileSystem(fullDirectory),
            recursive: false);
    }

    public static void DeleteActiveArtifactsWithinResetBoundary(
        string artifactRoot,
        string checkpointPath,
        string episodesPath)
    {
        var fullRoot = CombatFoundationPathRuntime.Normalize(artifactRoot);
        var volumeRoot = Path.GetPathRoot(fullRoot) ?? "";
        if (string.IsNullOrWhiteSpace(fullRoot)
            || string.Equals(
                fullRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                volumeRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase)
            || !TryGetActiveResetTargets(
                fullRoot,
                checkpointPath,
                episodesPath,
                out var targets)
            || !TryGetActiveSnapshotTargets(
                fullRoot,
                episodesPath,
                out var snapshotTargets))
        {
            throw new InvalidOperationException(
                "Active checkpoint reset boundary changed before deletion.");
        }

        foreach (var target in targets)
        {
            CombatFoundationPathRuntime.DeleteFile(target);
        }
        foreach (var target in snapshotTargets)
        {
            if (CombatFoundationPathRuntime.DirectoryExists(target)
                || CombatFoundationPathRuntime.FileExists(target)
                && !IsSafeTopLevelRegularFile(target, fullRoot))
            {
                throw new InvalidOperationException(
                    "Active checkpoint snapshot boundary changed before deletion.");
            }
            CombatFoundationPathRuntime.DeleteFile(target);
        }
        if (targets.Any(CombatFoundationPathRuntime.FileExists))
        {
            throw new IOException(
                "Active checkpoint artifacts remained after reset.");
        }
        if (!TryGetActiveSnapshotTargets(
                fullRoot,
                episodesPath,
                out var remainingSnapshots)
            || remainingSnapshots.Count != 0)
        {
            throw new IOException(
                "Active checkpoint snapshot artifacts remained after reset.");
        }
    }

    public static void DeleteLegacyArtifactsWithinResetBoundary(
        string artifactRoot)
    {
        var fullRoot = CombatFoundationPathRuntime.Normalize(artifactRoot);
        var volumeRoot = Path.GetPathRoot(fullRoot) ?? "";
        if (string.IsNullOrWhiteSpace(fullRoot)
            || string.Equals(
                fullRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                volumeRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase)
            || UnsafeLegacyResetArtifacts(fullRoot))
        {
            throw new InvalidOperationException(
                "Legacy checkpoint reset boundary changed before deletion.");
        }

        var checkpointPath = Path.Combine(
            fullRoot,
            CombatFoundationWorkerProtocol.LegacyCheckpointFileName);
        var episodesPath = Path.Combine(
            fullRoot,
            CombatFoundationWorkerProtocol.LegacyCheckpointEpisodesFileName);
        CombatFoundationCheckpointStorage.DeleteCheckpointArtifacts(
            checkpointPath,
            episodesPath);
        CombatFoundationPathRuntime.DeleteFile(
            CombatFoundationCheckpointStorage.BackupPath(episodesPath));

        if (LegacyResetArtifactsExist(fullRoot))
        {
            throw new IOException(
                "Legacy checkpoint artifacts remained after reset.");
        }
    }

    private static bool TryValidateCatalog(
        CombatFoundationCheckpointCatalog? catalog,
        string catalogPath,
        bool validateArtifactContents,
        out bool unchecksummedLegacyCatalog,
        out string diagnostic)
    {
        unchecksummedLegacyCatalog = false;
        if (catalog == null
            || !string.Equals(
                catalog.Protocol,
                CombatFoundationCheckpointCatalogProtocol.Version,
                StringComparison.Ordinal))
        {
            diagnostic = "checkpoint catalog protocol is incompatible";
            return false;
        }
        catalog.Entries ??= new List<CombatFoundationCheckpointCatalogEntry>();
        var hasChecksum = catalog.ChecksumVersion != 0
                          || !string.IsNullOrWhiteSpace(
                              catalog.ContentChecksumSha256);
        if (hasChecksum
            && (catalog.ChecksumVersion != ChecksumVersion
                || catalog.Generation <= 0L
                || !FixedTimeEqualsHex(
                    catalog.ContentChecksumSha256,
                    ComputeChecksum(catalog))))
        {
            diagnostic = "checkpoint catalog checksum is invalid";
            return false;
        }
        unchecksummedLegacyCatalog = !hasChecksum;
        if (catalog.Entries.Count
            > CombatFoundationCheckpointCatalogProtocol.MaximumEntries
            || catalog.Entries.Any(entry => entry == null))
        {
            diagnostic = "checkpoint catalog entry count is invalid";
            return false;
        }

        var root = Path.GetDirectoryName(
                       CombatFoundationPathRuntime.Normalize(catalogPath))
                   ?? "";
        var immutableRoot = Path.Combine(
            root,
            CombatFoundationCheckpointCatalogProtocol.ImmutableDirectoryName);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in catalog.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id)
                || entry.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || entry.Id.Contains(Path.DirectorySeparatorChar)
                || entry.Id.Contains(Path.AltDirectorySeparatorChar)
                || !ids.Add(entry.Id)
                || entry.NextIteration < 0
                || entry.CompletedCampaigns < 0
                || entry.CompletedEpochs < 0
                || entry.BestEpoch < 0
                || entry.BestValidationEpoch < 0
                || entry.DeploymentSelectedEpoch < 0
                || entry.EpisodeCount < 0
                || string.IsNullOrWhiteSpace(entry.RequestFingerprint)
                || string.IsNullOrWhiteSpace(entry.RulesetHash)
                || entry.CreatedUtc == default
                || entry.SelectionAnchorMetrics == null
                || !FiniteEntryMetrics(entry)
                || !ValidImmutableCheckpointName(entry)
                || !ValidEpisodeSnapshotName(
                    entry.EpisodeSnapshotPath,
                    root)
                || hasChecksum
                   && (!ValidSha256(entry.CheckpointContentSha256)
                       || !ValidSha256(
                           entry.EpisodeSnapshotContentSha256))
                || !ValidContainedFile(entry.CheckpointPath, immutableRoot)
                || !ValidContainedFile(entry.EpisodeSnapshotPath, root)
                || (!hasChecksum || validateArtifactContents)
                   && !TryValidateEntryArtifacts(
                    catalog,
                    entry,
                    requireCatalogedHashes: hasChecksum,
                    out _))
            {
                diagnostic = "checkpoint catalog contains an invalid entry";
                return false;
            }
        }
        if (!string.IsNullOrWhiteSpace(catalog.RecommendedCheckpointId)
            && !ids.Contains(catalog.RecommendedCheckpointId))
        {
            diagnostic = "checkpoint catalog recommendation is dangling";
            return false;
        }
        if (catalog.Entries.Any(entry => entry.Recommended
                                         != string.Equals(
                                             entry.Id,
                                             catalog.RecommendedCheckpointId,
                                             StringComparison.Ordinal))
            || catalog.SelectionAnchorEpisodes < 0
            || catalog.SelectionAnchorEpisodes == 0
               && !string.IsNullOrWhiteSpace(catalog.SelectionAnchorIdentity)
            || catalog.SelectionAnchorEpisodes > 0
               && string.IsNullOrWhiteSpace(catalog.SelectionAnchorIdentity)
            || !ValidSelectionAnchor(catalog, root))
        {
            diagnostic = "checkpoint catalog recommendation or selection anchor is invalid";
            return false;
        }
        diagnostic = "";
        return true;
    }

    private static bool FiniteEntryMetrics(
        CombatFoundationCheckpointCatalogEntry entry)
    {
        return double.IsFinite(entry.TrainingLoss)
               && double.IsFinite(entry.ValidationLoss)
               && double.IsFinite(entry.TestLoss)
               && double.IsFinite(entry.GeneralizationGap);
    }

    private static bool SafeLeafName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value, ".", StringComparison.Ordinal)
            || string.Equals(value, "..", StringComparison.Ordinal)
            || value.EndsWith(".", StringComparison.Ordinal)
            || value.EndsWith(" ", StringComparison.Ordinal)
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || value.Contains('*')
            || value.Contains('?'))
        {
            return false;
        }
        var deviceName = value.Split('.')[0].TrimEnd(' ', '.');
        if (deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (deviceName.Length == 4
            && deviceName[3] >= '1'
            && deviceName[3] <= '9'
            && (deviceName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || deviceName.StartsWith(
                    "LPT",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        return true;
    }

    private static bool ValidImmutableCheckpointName(
        CombatFoundationCheckpointCatalogEntry entry)
    {
        return string.Equals(
            Path.GetFileName(entry.CheckpointPath),
            "foundation-checkpoint-" + entry.Id + ".json.gz",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ValidEpisodeSnapshotName(
        string path,
        string catalogRoot)
    {
        const string marker = ".snapshot-";
        const string extension = ".afes";
        try
        {
            var normalizedPath = CombatFoundationPathRuntime.Normalize(path);
            var normalizedRoot = CombatFoundationPathRuntime.Normalize(
                catalogRoot);
            if (!string.Equals(
                    Path.GetDirectoryName(normalizedPath),
                    normalizedRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            var fileName = Path.GetFileName(normalizedPath);
            if (!fileName.EndsWith(
                    extension,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            var stem = fileName.Substring(
                0,
                fileName.Length - extension.Length);
            var markerIndex = stem.LastIndexOf(
                marker,
                StringComparison.OrdinalIgnoreCase);
            if (markerIndex <= 0)
            {
                return false;
            }
            var baseLeaf = stem.Substring(0, markerIndex);
            var generation = stem.Substring(markerIndex + marker.Length);
            if (!SafeLeafName(baseLeaf)
                || generation.Length != 30
                || generation[17] != '-')
            {
                return false;
            }
            var timestamp = generation.Substring(0, 17);
            var nonce = generation.Substring(18, 12);
            return timestamp.All(character =>
                       character >= '0' && character <= '9')
                   && DateTime.TryParseExact(
                       timestamp,
                       "yyyyMMddHHmmssfff",
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.AssumeUniversal
                       | DateTimeStyles.AdjustToUniversal,
                       out _)
                   && nonce.All(Uri.IsHexDigit);
        }
        catch
        {
            return false;
        }
    }

    private static bool ValidSelectionAnchor(
        CombatFoundationCheckpointCatalog catalog,
        string root)
    {
        if (string.IsNullOrWhiteSpace(catalog.SelectionAnchorPath))
        {
            return catalog.SelectionAnchorEpisodes == 0;
        }
        try
        {
            var normalized = CombatFoundationPathRuntime.Normalize(
                catalog.SelectionAnchorPath);
            if (!string.Equals(
                    Path.GetDirectoryName(normalized),
                    CombatFoundationPathRuntime.Normalize(root),
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    Path.GetFileName(normalized),
                    CombatFoundationCheckpointCatalogProtocol
                        .SelectionAnchorFileName,
                    StringComparison.OrdinalIgnoreCase)
                || ContainsReparsePoint(normalized, root))
            {
                return false;
            }
            return catalog.SelectionAnchorEpisodes == 0
                   || CombatFoundationPathRuntime.FileExists(normalized);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryValidateEntryArtifacts(
        CombatFoundationCheckpointCatalog catalog,
        CombatFoundationCheckpointCatalogEntry entry,
        bool requireCatalogedHashes,
        out string diagnostic)
    {
        try
        {
            var checkpointHash = CombatFoundationCheckpointStorage
                .ComputeFileSha256(entry.CheckpointPath);
            if (requireCatalogedHashes
                && (!ValidSha256(entry.CheckpointContentSha256)
                    || !string.Equals(
                        checkpointHash,
                        entry.CheckpointContentSha256,
                        StringComparison.OrdinalIgnoreCase)))
            {
                diagnostic = "checkpoint content hash mismatch";
                return false;
            }
            var checkpoint = JsonConvert.DeserializeObject<
                CombatFoundationWorkerCheckpoint>(
                CombatFoundationCheckpointStorage.ReadAllTextShared(
                    entry.CheckpointPath));
            var snapshot = checkpoint?.EpisodeSnapshot;
            if (checkpoint == null
                || snapshot == null)
            {
                diagnostic = "checkpoint or snapshot descriptor is missing";
                return false;
            }
            if (!EntryDescriptorMatches(
                    catalog,
                    entry,
                    checkpoint,
                    snapshot,
                    out diagnostic))
            {
                return false;
            }
            if (requireCatalogedHashes
                && (!ValidSha256(entry.EpisodeSnapshotContentSha256)
                    || !string.Equals(
                        snapshot.ContentSha256,
                        entry.EpisodeSnapshotContentSha256,
                        StringComparison.OrdinalIgnoreCase)))
            {
                diagnostic = "episode snapshot catalog hash mismatch";
                return false;
            }
            CombatFoundationCheckpointStorage.ValidateEpisodeSnapshotEnvelope(
                snapshot);
            diagnostic = "";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = ex.Message;
            return false;
        }
    }

    private static bool EntryDescriptorMatches(
        CombatFoundationCheckpointCatalog catalog,
        CombatFoundationCheckpointCatalogEntry entry,
        CombatFoundationWorkerCheckpoint checkpoint,
        CombatFoundationEpisodeSnapshot snapshot,
        out string diagnostic)
    {
        if (checkpoint.SchemaVersion
                != CombatFoundationWorkerProtocol.SchemaVersion
            || checkpoint.Resume == null
            || !string.Equals(
                checkpoint.RequestFingerprint,
                entry.RequestFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                checkpoint.RulesetHash,
                entry.RulesetHash,
                StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(catalog.RequestFingerprint)
               && !string.Equals(
                   catalog.RequestFingerprint,
                   entry.RequestFingerprint,
                   StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(catalog.RulesetHash)
               && !string.Equals(
                   catalog.RulesetHash,
                   entry.RulesetHash,
                   StringComparison.Ordinal)
            || !string.Equals(
                checkpoint.Resume.Stage,
                entry.Stage,
                StringComparison.Ordinal)
            || checkpoint.Resume.NextIteration != entry.NextIteration
            || checkpoint.Resume.CompletedCampaigns
               != entry.CompletedCampaigns)
        {
            diagnostic = "checkpoint descriptor does not match its catalog entry";
            return false;
        }
        if (!SamePath(snapshot.Path, entry.EpisodeSnapshotPath)
            || snapshot.EpisodeCount != entry.EpisodeCount
            || !string.Equals(
                snapshot.ReplayIdentity,
                entry.ReplayIdentity,
                StringComparison.Ordinal)
            || !ValidSha256(snapshot.ContentSha256))
        {
            diagnostic = "episode snapshot descriptor does not match its catalog entry";
            return false;
        }
        diagnostic = "";
        return true;
    }

    private static bool ValidSha256(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
        {
            return false;
        }
        try
        {
            return Convert.FromHexString(value).Length == 32;
        }
        catch
        {
            return false;
        }
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                CombatFoundationPathRuntime.Normalize(left),
                CombatFoundationPathRuntime.Normalize(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool ValidContainedFile(string path, string root)
    {
        return !string.IsNullOrWhiteSpace(path)
               && IsContainedPath(path, root)
               && CombatFoundationPathRuntime.FileExists(path)
               && !ContainsReparsePoint(path, root);
    }

    private static bool IsContainedPath(string path, string root)
    {
        try
        {
            var fullRoot = CombatFoundationPathRuntime.Normalize(root)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            var fullPath = CombatFoundationPathRuntime.Normalize(path);
            return fullPath.StartsWith(
                fullRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsReparsePoint(string path, string root)
    {
        try
        {
            var fullRoot = CombatFoundationPathRuntime.Normalize(root);
            var current = CombatFoundationPathRuntime.Normalize(path);
            if (File.Exists(current)
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
            current = Path.GetDirectoryName(current) ?? "";
            while (true)
            {
                if (Directory.Exists(current)
                    && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
                if (string.Equals(
                        current,
                        fullRoot,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrWhiteSpace(parent)
                    || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                current = parent;
            }
        }
        catch
        {
            return true;
        }
    }

    private static bool UnsafeImmutableResetTree(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return false;
            }
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
            if (Directory.EnumerateDirectories(
                    directory,
                    "*",
                    SearchOption.TopDirectoryOnly).Any())
            {
                return true;
            }
            return Directory.EnumerateFiles(
                    directory,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .Any(path => !Path.GetFileName(path).StartsWith(
                                 "foundation-checkpoint-",
                                 StringComparison.OrdinalIgnoreCase)
                             || (File.GetAttributes(path)
                                 & FileAttributes.ReparsePoint) != 0);
        }
        catch
        {
            return true;
        }
    }

    private static bool TryGetActiveResetTargets(
        string artifactRoot,
        string checkpointPath,
        string episodesPath,
        out IReadOnlyList<string> targets)
    {
        targets = Array.Empty<string>();
        try
        {
            var fullRoot = CombatFoundationPathRuntime.Normalize(artifactRoot);
            var checkpoint = CombatFoundationPathRuntime.Normalize(
                checkpointPath);
            var episodes = CombatFoundationPathRuntime.Normalize(episodesPath);
            var candidates = new[]
            {
                checkpoint,
                CombatFoundationCheckpointStorage.BackupPath(checkpoint),
                episodes,
                CombatFoundationCheckpointStorage.BackupPath(episodes)
            };
            if (candidates.Any(path => string.IsNullOrWhiteSpace(
                        Path.GetFileName(path))
                    || !string.Equals(
                        Path.GetDirectoryName(path),
                        fullRoot,
                        StringComparison.OrdinalIgnoreCase)
                    || Directory.Exists(path)
                    || ContainsReparsePoint(path, fullRoot))
                || candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                   != candidates.Length)
            {
                return false;
            }

            var reserved = new[]
            {
                Path.Combine(
                    fullRoot,
                    CombatFoundationCheckpointCatalogProtocol.CatalogFileName),
                Path.Combine(
                    fullRoot,
                    CombatFoundationCheckpointCatalogProtocol.CatalogFileName)
                + ".bak",
                Path.Combine(
                    fullRoot,
                    CombatFoundationCheckpointCatalogProtocol
                        .SelectionAnchorFileName),
                Path.Combine(
                    fullRoot,
                    CombatFoundationCheckpointCatalogProtocol
                        .ImmutableDirectoryName),
                ResetMarkerPath(fullRoot)
            }
            .Concat(LegacyResetFixedPaths(fullRoot))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (candidates.Any(reserved.Contains))
            {
                return false;
            }
            targets = candidates;
            return true;
        }
        catch
        {
            targets = Array.Empty<string>();
            return false;
        }
    }

    private static bool TryGetActiveSnapshotTargets(
        string artifactRoot,
        string episodesPath,
        out IReadOnlyList<string> targets)
    {
        targets = Array.Empty<string>();
        try
        {
            var fullRoot = CombatFoundationPathRuntime.Normalize(artifactRoot);
            var episodes = CombatFoundationPathRuntime.Normalize(episodesPath);
            if (!string.Equals(
                    Path.GetDirectoryName(episodes),
                    fullRoot,
                    StringComparison.OrdinalIgnoreCase)
                || ContainsReparsePoint(episodes, fullRoot))
            {
                return false;
            }
            if (!CombatFoundationPathRuntime.DirectoryExists(fullRoot))
            {
                return true;
            }

            var pattern = Path.GetFileNameWithoutExtension(episodes)
                          + ".snapshot-*.*";
            var matches = Directory.EnumerateFiles(
                    CombatFoundationPathRuntime.ForFileSystem(fullRoot),
                    pattern,
                    SearchOption.TopDirectoryOnly)
                .Select(CombatFoundationPathRuntime.Normalize)
                .ToArray();
            if (matches.Any(path => !IsSafeTopLevelRegularFile(
                    path,
                    fullRoot)))
            {
                return false;
            }
            targets = matches
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return true;
        }
        catch
        {
            targets = Array.Empty<string>();
            return false;
        }
    }

    private static bool IsSafeTopLevelRegularFile(string path, string root)
    {
        try
        {
            var fullRoot = CombatFoundationPathRuntime.Normalize(root);
            var fullPath = CombatFoundationPathRuntime.Normalize(path);
            if (!IsContainedPath(fullPath, fullRoot)
                || !string.Equals(
                    Path.GetDirectoryName(fullPath),
                    fullRoot,
                    StringComparison.OrdinalIgnoreCase)
                || !CombatFoundationPathRuntime.FileExists(fullPath)
                || CombatFoundationPathRuntime.DirectoryExists(fullPath)
                || ContainsReparsePoint(fullPath, fullRoot))
            {
                return false;
            }
            var attributes = File.GetAttributes(
                CombatFoundationPathRuntime.ForFileSystem(fullPath));
            return (attributes & (FileAttributes.Directory
                                  | FileAttributes.ReparsePoint)) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool UnsafeLegacyResetArtifacts(string artifactRoot)
    {
        try
        {
            var fullRoot = CombatFoundationPathRuntime.Normalize(artifactRoot);
            var fixedPaths = LegacyResetFixedPaths(fullRoot);
            if (fixedPaths.Any(path => !string.Equals(
                        Path.GetDirectoryName(path),
                        fullRoot,
                        StringComparison.OrdinalIgnoreCase)
                    || ContainsReparsePoint(path, fullRoot)))
            {
                return true;
            }
            if (!Directory.Exists(fullRoot))
            {
                return false;
            }
            return EnumerateLegacySnapshotPaths(fullRoot).Any(path =>
            {
                var normalized = CombatFoundationPathRuntime.Normalize(path);
                return !string.Equals(
                           Path.GetDirectoryName(normalized),
                           fullRoot,
                           StringComparison.OrdinalIgnoreCase)
                       || ContainsReparsePoint(normalized, fullRoot);
            });
        }
        catch
        {
            return true;
        }
    }

    private static bool LegacyResetArtifactsExist(string artifactRoot)
    {
        var fullRoot = CombatFoundationPathRuntime.Normalize(artifactRoot);
        return LegacyResetFixedPaths(fullRoot)
                   .Any(CombatFoundationPathRuntime.FileExists)
               || Directory.Exists(fullRoot)
               && EnumerateLegacySnapshotPaths(fullRoot).Any();
    }

    private static IReadOnlyList<string> LegacyResetFixedPaths(string artifactRoot)
    {
        var checkpointPath = Path.Combine(
            artifactRoot,
            CombatFoundationWorkerProtocol.LegacyCheckpointFileName);
        var episodesPath = Path.Combine(
            artifactRoot,
            CombatFoundationWorkerProtocol.LegacyCheckpointEpisodesFileName);
        return new[]
        {
            checkpointPath,
            CombatFoundationCheckpointStorage.BackupPath(checkpointPath),
            episodesPath,
            CombatFoundationCheckpointStorage.BackupPath(episodesPath)
        };
    }

    private static IEnumerable<string> EnumerateLegacySnapshotPaths(
        string artifactRoot)
    {
        var baseName = Path.GetFileNameWithoutExtension(
            CombatFoundationWorkerProtocol.LegacyCheckpointEpisodesFileName);
        return Directory.EnumerateFiles(
            CombatFoundationPathRuntime.ForFileSystem(artifactRoot),
            baseName + ".snapshot-*.*",
            SearchOption.TopDirectoryOnly);
    }

    private static bool ResetPendingForCandidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        try
        {
            var candidate = CombatFoundationPathRuntime.Normalize(path);
            var directory = Path.GetDirectoryName(candidate);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }
            var roots = new List<string> { directory };
            if (string.Equals(
                    Path.GetFileName(directory),
                    CombatFoundationCheckpointCatalogProtocol
                        .ImmutableDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
            {
                var parent = Path.GetDirectoryName(directory);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    roots.Add(parent);
                }
            }
            return roots
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(ResetMarkerPath)
                .Any(marker =>
                    CombatFoundationPathRuntime.FileExists(marker)
                    || CombatFoundationPathRuntime.DirectoryExists(marker));
        }
        catch
        {
            return true;
        }
    }

    private static CombatFoundationCheckpointResetMarker CreateResetMarker(
        CombatFoundationWorkerJob job,
        string artifactRoot,
        string immutableDirectory)
    {
        return new CombatFoundationCheckpointResetMarker
        {
            Protocol = ResetMarkerProtocol,
            ArtifactRoot = CombatFoundationPathRuntime.Normalize(artifactRoot),
            ImmutableDirectory = CombatFoundationPathRuntime.Normalize(
                immutableDirectory),
            CheckpointPath = CombatFoundationPathRuntime.Normalize(
                job.CheckpointPath),
            EpisodesPath = CombatFoundationPathRuntime.Normalize(
                job.CheckpointEpisodesPath),
            CatalogPath = CombatFoundationPathRuntime.Normalize(
                job.CheckpointCatalogPath),
            SelectionAnchorPath = CombatFoundationPathRuntime.Normalize(
                job.ModelSelectionAnchorPath)
        };
    }

    private static bool ResetMarkerMatches(
        string markerPath,
        CombatFoundationCheckpointResetMarker expected,
        string artifactRoot,
        out string diagnostic)
    {
        try
        {
            if (!IsSafeTopLevelRegularFile(markerPath, artifactRoot))
            {
                diagnostic = "reset marker is not a safe regular file";
                return false;
            }
            var actual = JsonConvert.DeserializeObject<
                CombatFoundationCheckpointResetMarker>(
                CombatFoundationCheckpointStorage.ReadAllTextShared(
                    markerPath));
            if (actual == null
                || !string.Equals(
                    actual.Protocol,
                    ResetMarkerProtocol,
                    StringComparison.Ordinal)
                || !SamePath(actual.ArtifactRoot, expected.ArtifactRoot)
                || !SamePath(
                    actual.ImmutableDirectory,
                    expected.ImmutableDirectory)
                || !SamePath(actual.CheckpointPath, expected.CheckpointPath)
                || !SamePath(actual.EpisodesPath, expected.EpisodesPath)
                || !SamePath(actual.CatalogPath, expected.CatalogPath)
                || !SamePath(
                    actual.SelectionAnchorPath,
                    expected.SelectionAnchorPath))
            {
                diagnostic = "reset marker payload does not match";
                return false;
            }
            diagnostic = "";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = ex.Message;
            return false;
        }
    }

    private static bool HasHistoricalArtifacts(string catalogPath)
    {
        try
        {
            var root = Path.GetDirectoryName(
                           CombatFoundationPathRuntime.Normalize(catalogPath))
                       ?? "";
            if (!Directory.Exists(root))
            {
                return false;
            }
            var immutableRoot = Path.Combine(
                root,
                CombatFoundationCheckpointCatalogProtocol.ImmutableDirectoryName);
            var immutableArtifacts = Directory.Exists(immutableRoot)
                                     && Directory.EnumerateFiles(
                                         immutableRoot,
                                         "foundation-checkpoint-*",
                                         SearchOption.TopDirectoryOnly).Any();
            var snapshotArtifacts = Directory.EnumerateFiles(
                root,
                "*.snapshot-*.*",
                SearchOption.TopDirectoryOnly).Any();
            var activeArtifacts = new[]
            {
                CombatFoundationWorkerProtocol.CheckpointFileName,
                CombatFoundationWorkerProtocol.CheckpointFileName + ".bak",
                CombatFoundationWorkerProtocol.CheckpointEpisodesFileName,
                CombatFoundationWorkerProtocol.LegacyCheckpointFileName,
                CombatFoundationWorkerProtocol.LegacyCheckpointFileName + ".bak",
                CombatFoundationWorkerProtocol.LegacyCheckpointEpisodesFileName
            }.Any(name => File.Exists(Path.Combine(root, name)));
            return immutableArtifacts || snapshotArtifacts || activeArtifacts;
        }
        catch
        {
            return true;
        }
    }

    private static string ComputeChecksum(
        CombatFoundationCheckpointCatalog catalog)
    {
        var payload = JsonConvert.SerializeObject(
            new
            {
                catalog.Protocol,
                catalog.RequestFingerprint,
                catalog.RulesetHash,
                UpdatedUtcTicks = catalog.UpdatedUtc.Ticks,
                catalog.Generation,
                catalog.SelectionAnchorPath,
                catalog.SelectionAnchorIdentity,
                catalog.SelectionAnchorEpisodes,
                catalog.RecommendedCheckpointId,
                catalog.Entries
            },
            Formatting.None,
            new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }

    private static bool FixedTimeEqualsHex(string left, string right)
    {
        try
        {
            var leftBytes = Convert.FromHexString(left ?? "");
            var rightBytes = Convert.FromHexString(right ?? "");
            return leftBytes.Length == rightBytes.Length
                   && CryptographicOperations.FixedTimeEquals(
                       leftBytes,
                       rightBytes);
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class CombatFoundationCheckpointCatalogReadResult
{
    public CombatFoundationCheckpointCatalog? Catalog { get; set; }

    public bool RecoveredFromBackup { get; set; }

    public bool RecoveryUncertain { get; set; }

    public bool CanRewriteSafely { get; set; }

    public string Diagnostic { get; set; } = "";
}

internal sealed class CombatFoundationCheckpointArtifactRetention
{
    public int ValidGenerationCount { get; set; }

    public IReadOnlyList<string> CheckpointPaths { get; set; } =
        Array.Empty<string>();

    public IReadOnlyList<string> SnapshotPaths { get; set; } =
        Array.Empty<string>();
}

internal sealed class CombatFoundationCheckpointResetMarker
{
    public string Protocol { get; set; } = "";

    public string ArtifactRoot { get; set; } = "";

    public string ImmutableDirectory { get; set; } = "";

    public string CheckpointPath { get; set; } = "";

    public string EpisodesPath { get; set; } = "";

    public string CatalogPath { get; set; } = "";

    public string SelectionAnchorPath { get; set; } = "";
}
