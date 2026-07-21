using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AuraShared.Core;

public sealed class AuraSharedPackageCoordinator
{
    private readonly AuraSharedStorageCoordinator storage;

    public AuraSharedPackageCoordinator(AuraSharedStorageCoordinator storage)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public AuraSharedInstallResponse Install(AuraSharedInstallRequest request)
    {
        if (request == null)
        {
            return new AuraSharedInstallResponse
            {
                Success = false,
                Message = "Resource install request is null."
            };
        }

        var started = Stopwatch.StartNew();
        var response = storage.ExecuteWrite(ResourceLockKey(request), () =>
            storage.ExecuteWrite(RegistryLockKey(request.System), () => InstallNoLock(request)));
        if (!response.Success)
        {
            AuraSharedOperationLog.Write(storage.RootDirectory, AuraSharedOperationLog.Create(
                operationId: "",
                transactionId: "",
                ownerModId: request.OwnerModId,
                system: request.System,
                logicalId: request.LogicalId,
                kind: "InstallResource",
                phase: response.Conflict ? "Conflict" : "Failed",
                result: response.Conflict ? "Conflict" : "Failure",
                message: response.Message,
                elapsedMs: started.ElapsedMilliseconds));
        }

        return response;
    }

    public AuraSharedInstalledResource[] GetResources(string system)
    {
        return storage.ExecuteRead(RegistryLockKey(system), () =>
        {
            var index = LoadIndex(system);
            return index.Resources
                .OrderBy(resource => resource.ResourceKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        });
    }

    public int RecoverTransactions()
    {
        return storage.ExecuteWrite("Transactions/Recovery", RecoverTransactionsNoLock);
    }

    private AuraSharedInstallResponse InstallNoLock(AuraSharedInstallRequest request)
    {
        try
        {
            ValidateRequest(request);
            var sourcePath = Path.GetFullPath(request.SourcePath);
            var destinationPath = ResolveDestination(request.DestinationRelativePath);
            ValidatePayloadPathBudget(sourcePath, destinationPath, request.Kind);
            var sourceFingerprint = Inspect(sourcePath, request.Kind);
            var index = LoadIndex(request.System);
            var key = ResourceKey(request.System, request.LogicalId);
            var existing = index.Resources.FirstOrDefault(resource =>
                string.Equals(resource.ResourceKey, key, StringComparison.OrdinalIgnoreCase));
            var destinationFingerprint = Exists(destinationPath, request.Kind)
                ? Inspect(destinationPath, request.Kind)
                : null;

            if (existing == null)
            {
                if (destinationFingerprint != null && !HashEquals(destinationFingerprint.Hash, sourceFingerprint.Hash))
                {
                    if (request.PreserveLocalChanges)
                    {
                        var preserved = CreateRecord(request, sourceFingerprint, destinationPath);
                        preserved.Customized = true;
                        AddOrUpdateSource(preserved, request);
                        index.Resources.Add(preserved);
                        SaveIndex(index, request.System);
                        return Success(
                            "PreservedLocal",
                            false,
                            destinationFingerprint.Hash,
                            destinationPath,
                            sourceFingerprint.Hash,
                            customized: true);
                    }

                    return Conflict("Unmanaged destination already contains different content.");
                }

                var record = CreateRecord(request, sourceFingerprint, destinationPath);
                AddOrUpdateSource(record, request);
                if (destinationFingerprint != null)
                {
                    index.Resources.Add(record);
                    SaveIndex(index, request.System);
                    return Success("Deduplicated", false, sourceFingerprint.Hash, destinationPath);
                }

                return Commit(index, null, record, request, sourceFingerprint, destinationPath, "Installed");
            }

            var incomingRelativePath = request.DestinationRelativePath.Replace('\\', '/').TrimStart('/');
            var relocating = !string.Equals(existing.Path, incomingRelativePath, StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(existing.Kind, request.Kind, StringComparison.OrdinalIgnoreCase)
                || (relocating && !request.AllowCanonicalRelocation))
            {
                return Conflict("Resource identity is already bound to a different kind or canonical destination.");
            }

            var previousDestinationPath = relocating ? ResolveDestination(existing.Path) : "";

            existing.Sources ??= new List<AuraSharedInstalledSource>();
            if (HashEquals(existing.ContentHash, sourceFingerprint.Hash))
            {
                var sourceChanged = AddOrUpdateSource(existing, request);
                if (destinationFingerprint != null && HashEquals(destinationFingerprint.Hash, sourceFingerprint.Hash))
                {
                    if (relocating)
                    {
                        var relocated = CreateRecord(request, sourceFingerprint, destinationPath, existing.Sources);
                        AddOrUpdateSource(relocated, request);
                        ReplaceRecord(index, existing, relocated);
                        SaveIndex(index, request.System);
                        TryDeletePath(previousDestinationPath, request.Kind);
                        return Success("Relocated", true, sourceFingerprint.Hash, destinationPath);
                    }
                    if (sourceChanged)
                    {
                        SaveIndex(index, request.System);
                    }

                    return Success("Deduplicated", false, sourceFingerprint.Hash, destinationPath);
                }

                if (request.PreserveLocalChanges && destinationFingerprint != null)
                {
                    existing.Customized = true;
                    if (sourceChanged)
                    {
                        SaveIndex(index, request.System);
                    }

                    return Success(
                        "PreservedLocal",
                        false,
                        destinationFingerprint.Hash,
                        destinationPath,
                        sourceFingerprint.Hash,
                        customized: true);
                }

                var repaired = CreateRecord(request, sourceFingerprint, destinationPath, existing.Sources);
                var repairedResult = Commit(index, existing, repaired, request, sourceFingerprint, destinationPath,
                    relocating ? "Relocated" : "Repaired");
                if (repairedResult.Success && relocating)
                {
                    TryDeletePath(previousDestinationPath, request.Kind);
                }
                return repairedResult;
            }

            var ownerSources = existing.Sources
                .Where(source => string.Equals(source.OwnerModId, request.OwnerModId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (ownerSources.Length == 0)
            {
                return Conflict("Another owner already installed different content.");
            }

            if (existing.Sources.Any(source =>
                    !string.Equals(source.OwnerModId, request.OwnerModId, StringComparison.OrdinalIgnoreCase)))
            {
                return Conflict("Content has multiple owners and cannot be replaced implicitly.");
            }

            var previousVersion = ownerSources.Max(source => source.PackageVersion);
            if (request.PackageVersion <= previousVersion)
            {
                return Conflict("Different content requires a higher packageVersion.");
            }

            if (request.PreserveLocalChanges
                && destinationFingerprint != null
                && !HashEquals(destinationFingerprint.Hash, existing.ContentHash))
            {
                var preserved = CreateRecord(request, sourceFingerprint, destinationPath, existing.Sources);
                preserved.Customized = true;
                AddOrUpdateSource(preserved, request);
                ReplaceRecord(index, existing, preserved);
                SaveIndex(index, request.System);
                return Success(
                    "PreservedLocal",
                    false,
                    destinationFingerprint.Hash,
                    destinationPath,
                    sourceFingerprint.Hash,
                    customized: true);
            }

            var updated = CreateRecord(request, sourceFingerprint, destinationPath, existing.Sources);
            AddOrUpdateSource(updated, request);
            var updatedResult = Commit(index, existing, updated, request, sourceFingerprint, destinationPath,
                relocating ? "Relocated" : "Updated");
            if (updatedResult.Success && relocating)
            {
                TryDeletePath(previousDestinationPath, request.Kind);
            }
            return updatedResult;
        }
        catch (Exception ex)
        {
            return Failure(ex, "");
        }
    }

    private AuraSharedInstallResponse Commit(
        AuraSharedResourceIndex index,
        AuraSharedInstalledResource? existing,
        AuraSharedInstalledResource replacement,
        AuraSharedInstallRequest request,
        ResourceFingerprint fingerprint,
        string destinationPath,
        string status)
    {
        var operationId = Guid.NewGuid().ToString("N");
        var transactionId = Guid.NewGuid().ToString("N");
        var started = Stopwatch.StartNew();
        var stagingRoot = Path.Combine(storage.RootDirectory, "Cache", "Packages", transactionId);
        var stagingPayload = Path.Combine(stagingRoot, "payload");
        var registryPath = IndexPath(request.System);
        var registryBackup = Path.Combine(stagingRoot, "registry.backup.json");
        var backupPath = Path.Combine(
            storage.RootDirectory,
            "Backups",
            SafeSegment(request.System, "General"),
            CompactLogicalId(request.LogicalId),
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + transactionId);
        var journalPath = Path.Combine(storage.RootDirectory, "Transactions", transactionId + ".json");
        var journal = new AuraSharedTransactionJournal
        {
            TransactionId = transactionId,
            State = "Prepared",
            DestinationPath = destinationPath,
            BackupPath = backupPath,
            StagingPath = stagingRoot,
            RegistryPath = registryPath,
            RegistryBackupPath = registryBackup,
            DestinationExisted = Exists(destinationPath, request.Kind),
            RegistryExisted = File.Exists(registryPath),
            Kind = request.Kind
        };
        storage.EnsurePortablePath(stagingPayload, "resource-staging");
        storage.EnsurePortablePath(registryBackup, "resource-registry-backup");
        storage.EnsurePortablePath(backupPath, "resource-backup");
        storage.EnsurePortablePath(journalPath, "resource-journal");

        try
        {
            Directory.CreateDirectory(stagingRoot);
            CopyPayload(request.SourcePath, stagingPayload, request.Kind);
            var stagedFingerprint = Inspect(stagingPayload, request.Kind);
            if (!HashEquals(stagedFingerprint.Hash, fingerprint.Hash))
            {
                throw new IOException("Staged resource hash differs from source.");
            }

            if (journal.RegistryExisted)
            {
                File.Copy(registryPath, registryBackup, false);
            }
            storage.WriteRawJsonAtomic(journalPath, journal, false);
            LogOperation(operationId, transactionId, request, "Prepared", "Started", "Prepared resource transaction.", started);

            if (journal.DestinationExisted)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath) ?? Path.Combine(storage.RootDirectory, "Backups"));
                MovePayload(destinationPath, backupPath, request.Kind);
            }

            journal.State = "BackupCommitted";
            storage.WriteRawJsonAtomic(journalPath, journal, false);
            LogOperation(operationId, transactionId, request, "BackupCommitted", "Success", "Previous payload moved to backup.", started);

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? storage.RootDirectory);
            MovePayload(stagingPayload, destinationPath, request.Kind);
            journal.State = "ContentCommitted";
            storage.WriteRawJsonAtomic(journalPath, journal, false);
            LogOperation(operationId, transactionId, request, "ContentCommitted", "Success", "Payload committed.", started);

            ReplaceRecord(index, existing, replacement);
            SaveIndex(index, request.System);
            journal.State = "RegistryCommitted";
            storage.WriteRawJsonAtomic(journalPath, journal, false);
            LogOperation(operationId, transactionId, request, "RegistryCommitted", "Success", "Registry committed.", started);

            TryDeleteFile(registryBackup);
            TryDeletePath(stagingRoot, AuraSharedResourceKinds.Directory);
            TryDeleteFile(journalPath);
            LogOperation(operationId, transactionId, request, "Completed", "Success", status, started);
            return Success(status, true, fingerprint.Hash, destinationPath);
        }
        catch (Exception ex)
        {
            Rollback(journal);
            LogOperation(operationId, transactionId, request, "RolledBack", "Failure", ex.Message, started);
            return Failure(ex, "Resource transaction failed: ");
        }
    }

    private int RecoverTransactionsNoLock()
    {
        var transactionsDirectory = Path.Combine(storage.RootDirectory, "Transactions");
        if (!Directory.Exists(transactionsDirectory))
        {
            return 0;
        }

        var recovered = 0;
        foreach (var path in Directory.EnumerateFiles(transactionsDirectory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var journal = storage.LoadRawJsonOrDefault(path, new AuraSharedTransactionJournal());
                if (string.Equals(journal.State, "RegistryCommitted", StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(journal.RegistryBackupPath);
                    TryDeletePath(journal.StagingPath, AuraSharedResourceKinds.Directory);
                    TryDeleteFile(path);
                }
                else
                {
                    Rollback(journal);
                }
                recovered++;
            }
            catch
            {
                // Leave an unreadable journal in place for manual inspection.
            }
        }

        return recovered;
    }

    private void Rollback(AuraSharedTransactionJournal journal)
    {
        try
        {
            ValidateJournalPath(journal.DestinationPath);
            ValidateJournalPath(journal.BackupPath);
            ValidateJournalPath(journal.StagingPath);
            ValidateJournalPath(journal.RegistryPath);
            ValidateJournalPath(journal.RegistryBackupPath);

            if (!string.Equals(journal.State, "Prepared", StringComparison.OrdinalIgnoreCase))
            {
                TryDeletePath(journal.DestinationPath, journal.Kind);
            }

            if (journal.DestinationExisted && Exists(journal.BackupPath, journal.Kind))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(journal.DestinationPath) ?? storage.RootDirectory);
                MovePayload(journal.BackupPath, journal.DestinationPath, journal.Kind);
            }

            if (journal.RegistryExisted && File.Exists(journal.RegistryBackupPath))
            {
                storage.WriteTextAtomic(journal.RegistryPath, File.ReadAllText(journal.RegistryBackupPath), false);
            }
            else if (!journal.RegistryExisted)
            {
                TryDeleteFile(journal.RegistryPath);
            }
        }
        finally
        {
            TryDeleteFile(journal.RegistryBackupPath);
            TryDeletePath(journal.StagingPath, AuraSharedResourceKinds.Directory);
            TryDeleteFile(Path.Combine(storage.RootDirectory, "Transactions", journal.TransactionId + ".json"));
        }
    }

    private void LogOperation(
        string operationId,
        string transactionId,
        AuraSharedInstallRequest request,
        string phase,
        string result,
        string message,
        Stopwatch started)
    {
        AuraSharedOperationLog.Write(storage.RootDirectory, AuraSharedOperationLog.Create(
            operationId,
            transactionId,
            request.OwnerModId,
            request.System,
            request.LogicalId,
            "InstallResource",
            phase,
            result,
            message,
            elapsedMs: started.ElapsedMilliseconds));
    }

    private AuraSharedResourceIndex LoadIndex(string system)
    {
        var index = storage.LoadRawJsonOrDefault(IndexPath(system), new AuraSharedResourceIndex());
        index.SchemaVersion = 2;
        index.Resources ??= new List<AuraSharedInstalledResource>();
        foreach (var record in index.Resources)
        {
            record.Sources ??= new List<AuraSharedInstalledSource>();
            record.Files ??= new List<AuraSharedInstalledFile>();
        }
        return index;
    }

    private void SaveIndex(AuraSharedResourceIndex index, string system)
    {
        index.SchemaVersion = 2;
        index.Revision++;
        index.Resources = index.Resources
            .OrderBy(resource => resource.ResourceKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        storage.WriteRawJsonAtomic(IndexPath(system), index, true);
    }

    private string IndexPath(string system)
    {
        return Path.Combine(storage.RootDirectory, "Registries", SafeSegment(system, "General"), "resources.json");
    }

    private static string ResourceLockKey(AuraSharedInstallRequest? request)
    {
        return "Resource/"
               + SafeSegment(request?.System ?? "General", "General") + "/"
               + SafeSegment(request?.LogicalId ?? "unknown", "unknown");
    }

    private static string RegistryLockKey(string system)
    {
        return "Registry/" + SafeSegment(system, "General");
    }

    private string ResolveDestination(string relativePath)
    {
        var normalized = (relativePath ?? "").Trim().TrimStart('/', '\\')
            .Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized))
        {
            throw new InvalidDataException("Resource destination must be relative to AuraShared root.");
        }

        var destination = Path.GetFullPath(Path.Combine(storage.RootDirectory, normalized));
        if (!AuraSharedStorageCoordinator.IsInside(destination, storage.RootDirectory))
        {
            throw new InvalidDataException("Resource destination escapes AuraShared root.");
        }
        return destination;
    }

    private static void ValidateRequest(AuraSharedInstallRequest request)
    {
        if (request == null
            || string.IsNullOrWhiteSpace(request.OwnerModId)
            || string.IsNullOrWhiteSpace(request.System)
            || string.IsNullOrWhiteSpace(request.LogicalId)
            || string.IsNullOrWhiteSpace(request.PackageId)
            || request.PackageVersion < 1
            || string.IsNullOrWhiteSpace(request.SourcePath))
        {
            throw new InvalidDataException("Resource install request is incomplete.");
        }

        if (!string.Equals(request.Kind, AuraSharedResourceKinds.File, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.Kind, AuraSharedResourceKinds.Directory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Unsupported resource kind: " + request.Kind);
        }

        var sourceExists = Exists(request.SourcePath, request.Kind);
        if (!sourceExists)
        {
            throw new FileNotFoundException("Resource source does not exist.", request.SourcePath);
        }
    }

    private void ValidateJournalPath(string path)
    {
        if (!string.IsNullOrWhiteSpace(path)
            && !AuraSharedStorageCoordinator.IsInside(path, storage.RootDirectory))
        {
            throw new InvalidDataException("Transaction path escapes AuraShared root: " + path);
        }
    }

    private static AuraSharedInstalledResource CreateRecord(
        AuraSharedInstallRequest request,
        ResourceFingerprint fingerprint,
        string destinationPath,
        IEnumerable<AuraSharedInstalledSource>? sources = null)
    {
        return new AuraSharedInstalledResource
        {
            ResourceKey = ResourceKey(request.System, request.LogicalId),
            System = request.System.Trim(),
            LogicalId = request.LogicalId.Trim(),
            Kind = request.Kind,
            ContentHash = fingerprint.Hash,
            Path = request.DestinationRelativePath.Replace('\\', '/').TrimStart('/'),
            InstalledUtc = DateTime.UtcNow.ToString("O"),
            Sources = sources?.Select(CloneSource).ToList() ?? new List<AuraSharedInstalledSource>(),
            Files = fingerprint.Files.Select(CloneFile).ToList()
        };
    }

    private static bool AddOrUpdateSource(AuraSharedInstalledResource record, AuraSharedInstallRequest request)
    {
        var source = record.Sources.FirstOrDefault(item =>
            string.Equals(item.OwnerModId, request.OwnerModId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.PackageId, request.PackageId, StringComparison.OrdinalIgnoreCase));
        if (source == null)
        {
            record.Sources.Add(new AuraSharedInstalledSource
            {
                OwnerModId = request.OwnerModId.Trim(),
                PackageId = request.PackageId.Trim(),
                PackageVersion = request.PackageVersion
            });
            return true;
        }

        if (source.PackageVersion >= request.PackageVersion)
        {
            return false;
        }

        source.PackageVersion = request.PackageVersion;
        return true;
    }

    private static void ReplaceRecord(
        AuraSharedResourceIndex index,
        AuraSharedInstalledResource? previous,
        AuraSharedInstalledResource replacement)
    {
        if (previous != null)
        {
            index.Resources.Remove(previous);
        }
        index.Resources.Add(replacement);
    }

    private static ResourceFingerprint Inspect(string path, string kind)
    {
        if (string.Equals(kind, AuraSharedResourceKinds.File, StringComparison.OrdinalIgnoreCase))
        {
            var info = new FileInfo(path);
            var hash = HashFile(path);
            return new ResourceFingerprint
            {
                Hash = hash,
                Files = new List<AuraSharedInstalledFile>
                {
                    new() { Path = info.Name, Sha256 = hash, Length = info.Length }
                }
            };
        }

        var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Select(file => new AuraSharedInstalledFile
            {
                Path = AuraSharedStorageCoordinator.MakeRelative(path, file).Replace('\\', '/'),
                Sha256 = HashFile(file),
                Length = new FileInfo(file).Length
            })
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var canonical = string.Join("\n", files.Select(file =>
            file.Path.ToLowerInvariant() + "|" + file.Length + "|" + file.Sha256.ToLowerInvariant()));
        return new ResourceFingerprint
        {
            Hash = HashBytes(Encoding.UTF8.GetBytes(canonical)),
            Files = files
        };
    }

    private static void CopyPayload(string source, string destination, string kind)
    {
        if (string.Equals(kind, AuraSharedResourceKinds.File, StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ".");
            File.Copy(source, destination, false);
            return;
        }

        CopyDirectory(source, destination);
    }

    private static void MovePayload(string source, string destination, string kind)
    {
        if (string.Equals(kind, AuraSharedResourceKinds.File, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(source, destination);
        }
        else
        {
            Directory.Move(source, destination);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, AuraSharedStorageCoordinator.MakeRelative(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, AuraSharedStorageCoordinator.MakeRelative(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
            File.Copy(file, target, false);
        }
    }

    private static bool Exists(string path, string kind)
    {
        return string.Equals(kind, AuraSharedResourceKinds.File, StringComparison.OrdinalIgnoreCase)
            ? File.Exists(path)
            : Directory.Exists(path);
    }

    private static void TryDeletePath(string path, string kind)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            if (string.Equals(kind, AuraSharedResourceKinds.File, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(path);
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return ToHex(sha.ComputeHash(stream));
    }

    private static string HashBytes(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return ToHex(sha.ComputeHash(bytes));
    }

    private static string ToHex(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    private static bool HashEquals(string left, string right)
    {
        return !string.IsNullOrWhiteSpace(left)
               && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResourceKey(string system, string logicalId)
    {
        return (system ?? "").Trim().ToLowerInvariant()
               + "::"
               + (logicalId ?? "").Trim().ToLowerInvariant();
    }

    private static string SafeSegment(string value, string fallback)
    {
        var result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(c, '_');
        }
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    private static AuraSharedInstalledSource CloneSource(AuraSharedInstalledSource source)
    {
        return new AuraSharedInstalledSource
        {
            OwnerModId = source.OwnerModId,
            PackageId = source.PackageId,
            PackageVersion = source.PackageVersion
        };
    }

    private static AuraSharedInstalledFile CloneFile(AuraSharedInstalledFile file)
    {
        return new AuraSharedInstalledFile
        {
            Path = file.Path,
            Sha256 = file.Sha256,
            Length = file.Length
        };
    }

    private static AuraSharedInstallResponse Success(
        string status,
        bool changed,
        string hash,
        string path,
        string seedHash = "",
        bool customized = false)
    {
        return new AuraSharedInstallResponse
        {
            Success = true,
            Changed = changed,
            Status = status,
            ContentHash = hash,
            SeedHash = string.IsNullOrWhiteSpace(seedHash) ? hash : seedHash,
            Customized = customized,
            InstalledPath = path
        };
    }

    private static string CompactLogicalId(string logicalId)
    {
        var normalized = (logicalId ?? "").Trim().ToLowerInvariant();
        return HashBytes(Encoding.UTF8.GetBytes(normalized)).Substring(0, 32);
    }

    private void ValidatePayloadPathBudget(string sourcePath, string destinationPath, string kind)
    {
        storage.EnsurePortablePath(destinationPath, "resource-destination");
        if (!string.Equals(kind, AuraSharedResourceKinds.Directory, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(sourcePath))
        {
            return;
        }

        var sourceRoot = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = sourceFile.Substring(sourceRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            storage.EnsurePortablePath(Path.Combine(destinationPath, relative), "resource-payload");
        }
    }

    private static AuraSharedInstallResponse Conflict(string message)
    {
        return new AuraSharedInstallResponse
        {
            Success = false,
            Conflict = true,
            Status = "Conflict",
            Message = message
        };
    }

    private static AuraSharedInstallResponse Failure(Exception exception, string prefix)
    {
        var response = new AuraSharedInstallResponse
        {
            Success = false,
            Status = "Failed",
            FailureCode = exception is AuraSharedPathBudgetException ? "PathBudgetExceeded" : exception.GetType().Name,
            Message = (prefix ?? "") + exception.Message
        };
        if (exception is AuraSharedPathBudgetException pathError)
        {
            response.FailedPath = pathError.Path;
            response.FailedPathLength = pathError.PathLength;
        }
        return response;
    }

    private sealed class ResourceFingerprint
    {
        public string Hash { get; set; } = "";
        public List<AuraSharedInstalledFile> Files { get; set; } = new();
    }
}
