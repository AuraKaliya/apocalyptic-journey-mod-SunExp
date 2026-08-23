using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AuraShared.Core;

public sealed class AuraSharedStorageCoordinator : IDisposable
{
    public const int MaxPortablePathLength = 259;
    public const int MaximumBackupsPerDocument = 12;
    private readonly object lifecycleGate = new();
    private readonly AuraSharedResourceLockTable locks = new();
    private string rootDirectory;
    private bool disposed;

    public AuraSharedStorageCoordinator(string rootDirectory)
    {
        this.rootDirectory = FullPath(rootDirectory);
        EnsureRootDirectories();
    }

    public string RootDirectory => rootDirectory;

    public void EnsurePortablePath(string path, string operation)
    {
        var fullPath = Path.GetFullPath(path ?? "");
        if (fullPath.Length > MaxPortablePathLength)
        {
            throw new AuraSharedPathBudgetException(operation, fullPath, MaxPortablePathLength);
        }
    }

    public void InitializeRoot(string root)
    {
        var fullRoot = FullPath(root);
        if (string.IsNullOrWhiteSpace(fullRoot))
        {
            throw new ArgumentException("Shared storage root is empty.", nameof(root));
        }

        lock (lifecycleGate)
        {
            ThrowIfDisposed();
            if (!string.IsNullOrWhiteSpace(rootDirectory)
                && !SamePath(rootDirectory, fullRoot))
            {
                throw new InvalidOperationException("Shared storage root cannot change after initialization.");
            }

            rootDirectory = fullRoot;
            EnsureRootDirectories();
        }
    }

    public AuraSharedStorageResponse Read(AuraSharedStorageRequest request)
    {
        try
        {
            return ExecuteRead(StorageLockKey(request), () =>
            {
                ThrowIfDisposed();
                var path = ResolveDocumentPath(request);
                if (!File.Exists(path))
                {
                    return new AuraSharedStorageResponse
                    {
                        Success = true,
                        Found = false,
                        Path = path
                    };
                }

                var envelope = LoadEnvelope(path);
                return new AuraSharedStorageResponse
                {
                    Success = true,
                    Found = true,
                    Revision = envelope.Revision,
                    SchemaVersion = envelope.SchemaVersion,
                    AuthorityId = envelope.AuthorityId,
                    PayloadJson = envelope.Data.ToString(Formatting.None),
                    Path = path
                };
            });
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
    }

    public AuraSharedStorageResponse Write(AuraSharedStorageRequest request)
    {
        var started = Stopwatch.StartNew();
        var response = ExecuteWrite(StorageLockKey(request), () => WriteNoLock(request));
        if (!response.Success || response.Conflict || response.Changed)
        {
            var owner = request == null || string.IsNullOrWhiteSpace(request.OwnerModId) ? request?.WriterId ?? "" : request.OwnerModId;
            AuraSharedOperationLog.Write(rootDirectory, AuraSharedOperationLog.Create(
                operationId: "",
                transactionId: "",
                ownerModId: owner,
                system: request?.System ?? "",
                logicalId: request?.FileName ?? "",
                kind: "StorageWrite",
                phase: response.Conflict ? "Conflict" : "Committed",
                result: response.Success ? "Success" : response.Conflict ? "Conflict" : "Failure",
                message: response.Message,
                revision: response.Revision,
                elapsedMs: started.ElapsedMilliseconds));
        }
        return response;
    }

    public T ExecuteRead<T>(Func<T> action)
    {
        return ExecuteRead("Global", action);
    }

    public T ExecuteRead<T>(string lockKey, Func<T> action)
    {
        return locks.ExecuteRead(lockKey, () =>
        {
            ThrowIfDisposed();
            return action();
        });
    }

    public T ExecuteWrite<T>(Func<T> action)
    {
        return ExecuteWrite("Global", action);
    }

    public T ExecuteWrite<T>(string lockKey, Func<T> action)
    {
        return locks.ExecuteWrite(lockKey, () =>
        {
            ThrowIfDisposed();
            using var mutex = CreateWriteMutex();
            var acquired = false;
            try
            {
                try
                {
                    acquired = mutex.WaitOne(TimeSpan.FromSeconds(15));
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }

                if (!acquired)
                {
                    throw new TimeoutException("Timed out waiting for the shared storage writer.");
                }

                return action();
            }
            finally
            {
                if (acquired)
                {
                    mutex.ReleaseMutex();
                }
            }
        });
    }

    public string StorageLockKey(AuraSharedStorageRequest request)
    {
        try
        {
            ValidateRequest(request);
            var owner = string.Equals(request.Scope, AuraSharedStorageScopes.Owner, StringComparison.OrdinalIgnoreCase)
                ? request.OwnerModId
                : "";
            return "Config/"
                   + SafeSegment(request.Scope, "Shared") + "/"
                   + SafeSegment(owner, "_") + "/"
                   + SafeSegment(request.System, "General") + "/"
                   + SafeFileName(request.FileName);
        }
        catch (Exception ex)
        {
            return "Config/Invalid/" + ex.GetType().Name;
        }
    }

    public string ResolveDocumentPath(AuraSharedStorageRequest request)
    {
        ValidateRequest(request);
        var system = SafeSegment(request.System, "General");
        var fileName = SafeFileName(request.FileName);
        string directory;
        if (string.Equals(request.Scope, AuraSharedStorageScopes.Shared, StringComparison.OrdinalIgnoreCase))
        {
            directory = Path.Combine(rootDirectory, "Config", "Shared", system);
        }
        else if (string.Equals(request.Scope, AuraSharedStorageScopes.Owner, StringComparison.OrdinalIgnoreCase))
        {
            directory = Path.Combine(rootDirectory, "Config", "Owners", SafeSegment(request.OwnerModId, "UnknownOwner"), system);
        }
        else if (string.Equals(request.Scope, AuraSharedStorageScopes.Runtime, StringComparison.OrdinalIgnoreCase))
        {
            directory = Path.Combine(rootDirectory, "Config", "Runtime", system);
        }
        else if (string.Equals(request.Scope, AuraSharedStorageScopes.Registry, StringComparison.OrdinalIgnoreCase))
        {
            directory = Path.Combine(rootDirectory, "Registries", system);
        }
        else
        {
            throw new InvalidDataException("Unknown shared storage scope: " + request.Scope);
        }

        var path = Path.GetFullPath(Path.Combine(directory, fileName));
        if (!IsInside(path, rootDirectory))
        {
            throw new InvalidDataException("Shared storage path escapes its root.");
        }

        return path;
    }

    public void WriteRawJsonAtomic(string path, object value, bool createBackup)
    {
        WriteTextAtomic(path, AuraSharedJson.Serialize(value), createBackup);
    }

    public T LoadRawJsonOrDefault<T>(string path, T fallback)
    {
        try
        {
            return File.Exists(path)
                ? AuraSharedJson.Deserialize<T>(File.ReadAllText(path)) ?? fallback
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public bool IsFileWriteLeaseHeld(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsInside(fullPath, rootDirectory))
        {
            throw new InvalidDataException(
                "Lease probe target escapes shared storage: " + path);
        }
        EnsurePortablePath(fullPath, "lease-probe");
        if (!File.Exists(fullPath))
        {
            return false;
        }
        try
        {
            using var probe = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    public void WriteTextAtomic(string path, string text, bool createBackup)
    {
        WriteBytesAtomic(path, new UTF8Encoding(false).GetBytes(text ?? ""), createBackup);
    }

    public void WriteBytesAtomic(string path, byte[] bytes, bool createBackup)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsInside(fullPath, rootDirectory))
        {
            throw new InvalidDataException("Atomic write target escapes shared storage: " + path);
        }

        var directory = Path.GetDirectoryName(fullPath) ?? rootDirectory;
        EnsurePortablePath(fullPath, "atomic-target");
        bytes ??= Array.Empty<byte>();
        if (File.Exists(fullPath) && FileContentEquals(fullPath, bytes))
        {
            return;
        }

        var tempPath = Path.Combine(directory, ".aura-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp");
        EnsurePortablePath(tempPath, "atomic-temporary");
        Directory.CreateDirectory(directory);
        if (createBackup && File.Exists(fullPath))
        {
            CreateBackup(fullPath);
        }

        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (!File.Exists(fullPath))
            {
                File.Move(tempPath, fullPath);
                return;
            }

            try
            {
                File.Replace(tempPath, fullPath, null);
            }
            catch (PlatformNotSupportedException)
            {
                ReplaceWithRollback(tempPath, fullPath);
            }
            catch (IOException)
            {
                ReplaceWithRollback(tempPath, fullPath);
            }
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        locks.Dispose();
    }

    private AuraSharedStorageResponse WriteNoLock(AuraSharedStorageRequest request)
    {
        try
        {
            ValidateRequest(request);
            if (string.IsNullOrWhiteSpace(request.WriterId))
            {
                throw new InvalidDataException("Shared storage writerId is empty.");
            }

            var path = ResolveDocumentPath(request);
            var current = File.Exists(path) ? LoadEnvelopeForWrite(path) : null;
            var currentRevision = current?.Revision ?? 0;
            if (request.ExpectedRevision >= 0 && request.ExpectedRevision != currentRevision)
            {
                return new AuraSharedStorageResponse
                {
                    Success = false,
                    Conflict = true,
                    Found = current != null,
                    Revision = currentRevision,
                    Path = path,
                    Message = "Expected revision " + request.ExpectedRevision + " but found " + currentRevision + "."
                };
            }

            var authority = ResolveAuthority(request, current);
            var payload = string.IsNullOrWhiteSpace(request.PayloadJson)
                ? JValue.CreateNull()
                : JToken.Parse(request.PayloadJson);
            if (current != null
                && current.SchemaVersion == Math.Max(1, request.SchemaVersion)
                && string.Equals(current.AuthorityId, authority, StringComparison.Ordinal)
                && JToken.DeepEquals(current.Data, payload))
            {
                return new AuraSharedStorageResponse
                {
                    Success = true,
                    Found = true,
                    Changed = false,
                    Revision = current.Revision,
                    SchemaVersion = current.SchemaVersion,
                    AuthorityId = current.AuthorityId,
                    PayloadJson = current.Data.ToString(Formatting.None),
                    Path = path,
                    Message = "Payload is unchanged."
                };
            }

            var envelope = new AuraSharedStorageEnvelope
            {
                SchemaVersion = Math.Max(1, request.SchemaVersion),
                Revision = currentRevision + 1,
                UpdatedUtc = DateTime.UtcNow.ToString("O"),
                UpdatedBy = request.WriterId.Trim(),
                AuthorityId = authority,
                Data = payload
            };
            WriteRawJsonAtomic(path, envelope, request.CreateBackup);
            return new AuraSharedStorageResponse
            {
                Success = true,
                Found = true,
                Changed = true,
                Revision = envelope.Revision,
                SchemaVersion = envelope.SchemaVersion,
                AuthorityId = envelope.AuthorityId,
                PayloadJson = envelope.Data.ToString(Formatting.None),
                Path = path
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            return new AuraSharedStorageResponse
            {
                Success = false,
                Conflict = true,
                Message = ex.Message
            };
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
    }

    private static string ResolveAuthority(AuraSharedStorageRequest request, AuraSharedStorageEnvelope? current)
    {
        var writer = request.WriterId.Trim();
        if (string.Equals(request.Scope, AuraSharedStorageScopes.Owner, StringComparison.OrdinalIgnoreCase))
        {
            var owner = request.OwnerModId.Trim();
            if (!string.Equals(owner, writer, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Owner configuration can only be written by " + owner + ".");
            }

            return owner;
        }

        var requestedAuthority = string.IsNullOrWhiteSpace(request.AuthorityId)
            ? writer
            : request.AuthorityId.Trim();
        var authority = string.IsNullOrWhiteSpace(current?.AuthorityId)
            ? requestedAuthority
            : current!.AuthorityId.Trim();
        if (!string.Equals(authority, writer, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Shared document is writable only by authority " + authority + ".");
        }

        return authority;
    }

    private AuraSharedStorageEnvelope LoadEnvelope(string path)
    {
        var envelope = AuraSharedJson.Deserialize<AuraSharedStorageEnvelope>(File.ReadAllText(path));
        if (envelope == null || envelope.Revision < 1 || envelope.Data == null)
        {
            throw new InvalidDataException("Invalid shared storage envelope: " + path);
        }

        return envelope;
    }

    private AuraSharedStorageEnvelope? LoadEnvelopeForWrite(string path)
    {
        try
        {
            return LoadEnvelope(path);
        }
        catch
        {
            var quarantinePath = CompactArchivePath("Invalid", path, ".invalid");
            Directory.CreateDirectory(Path.GetDirectoryName(quarantinePath) ?? Path.Combine(rootDirectory, "Backups", "Storage", "Invalid"));
            EnsurePortablePath(quarantinePath, "invalid-storage-quarantine");
            File.Move(path, quarantinePath);
            return null;
        }
    }

    private void CreateBackup(string sourcePath)
    {
        var backupPath = CompactArchivePath("Versions", sourcePath, ".bak");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath) ?? Path.Combine(rootDirectory, "Backups"));
        EnsurePortablePath(backupPath, "storage-backup");
        File.Copy(sourcePath, backupPath, false);
        PruneDocumentBackups(backupPath);
    }

    private void PruneDocumentBackups(string newestBackupPath)
    {
        var directory = Path.GetDirectoryName(newestBackupPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var fileName = Path.GetFileName(newestBackupPath);
        var timestampSeparator = fileName.IndexOf('.');
        if (timestampSeparator <= 0)
        {
            return;
        }

        var prefix = fileName.Substring(0, timestampSeparator) + ".";
        var backups = Directory.EnumerateFiles(directory, prefix + "*.bak", SearchOption.TopDirectoryOnly)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(MaximumBackupsPerDocument)
            .ToArray();
        foreach (var stale in backups)
        {
            TryDeleteFile(stale);
        }
    }

    private static bool FileContentEquals(string path, byte[] expected)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length != expected.LongLength)
            {
                return false;
            }

            var buffer = new byte[8192];
            var offset = 0;
            while (offset < expected.Length)
            {
                var read = stream.Read(buffer, 0, Math.Min(buffer.Length, expected.Length - offset));
                if (read <= 0)
                {
                    return false;
                }

                for (var index = 0; index < read; index++)
                {
                    if (buffer[index] != expected[offset + index])
                    {
                        return false;
                    }
                }
                offset += read;
            }
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private void ReplaceWithRollback(string tempPath, string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath) ?? rootDirectory;
        var rollbackPath = Path.Combine(directory, ".aura-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".rollback");
        EnsurePortablePath(rollbackPath, "atomic-rollback");
        File.Move(destinationPath, rollbackPath);
        try
        {
            File.Move(tempPath, destinationPath);
            TryDeleteFile(rollbackPath);
        }
        catch
        {
            if (!File.Exists(destinationPath) && File.Exists(rollbackPath))
            {
                File.Move(rollbackPath, destinationPath);
            }

            throw;
        }
    }

    public byte[] ReadCompleteFileSnapshot(
        string path,
        byte recordTerminator = (byte)'\n',
        int maximumBytes = int.MaxValue)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsInside(fullPath, rootDirectory))
        {
            throw new InvalidDataException("Snapshot source escapes shared storage: " + path);
        }
        EnsurePortablePath(fullPath, "snapshot-source");
        if (!File.Exists(fullPath))
        {
            return Array.Empty<byte>();
        }
        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > Math.Max(0, maximumBytes) || stream.Length > int.MaxValue)
        {
            throw new InvalidDataException("Snapshot source exceeds the configured limit: " + path);
        }
        var bytes = new byte[(int)stream.Length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read <= 0)
            {
                break;
            }
            offset += read;
        }
        if (offset != bytes.Length)
        {
            Array.Resize(ref bytes, offset);
        }
        var completeLength = bytes.Length;
        while (completeLength > 0 && bytes[completeLength - 1] != recordTerminator)
        {
            completeLength--;
        }
        if (completeLength != bytes.Length)
        {
            Array.Resize(ref bytes, completeLength);
        }
        return bytes;
    }

    public void MoveFileInsideRoot(string sourcePath, string destinationPath)
    {
        var source = Path.GetFullPath(sourcePath);
        var destination = Path.GetFullPath(destinationPath);
        if (!IsInside(source, rootDirectory) || !IsInside(destination, rootDirectory))
        {
            throw new InvalidDataException("Move target escapes shared storage.");
        }
        EnsurePortablePath(source, "move-source");
        EnsurePortablePath(destination, "move-destination");
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Shared move source does not exist.", source);
        }
        if (File.Exists(destination))
        {
            throw new IOException("Shared move destination already exists: " + destination);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? rootDirectory);
        File.Move(source, destination);
    }

    public void ReplaceFileInsideRoot(
        string sourcePath,
        string destinationPath,
        string backupPath = "")
    {
        var source = Path.GetFullPath(sourcePath);
        var destination = Path.GetFullPath(destinationPath);
        var backup = string.IsNullOrWhiteSpace(backupPath)
            ? ""
            : Path.GetFullPath(backupPath);
        if (!IsInside(source, rootDirectory)
            || !IsInside(destination, rootDirectory)
            || !string.IsNullOrWhiteSpace(backup)
               && !IsInside(backup, rootDirectory))
        {
            throw new InvalidDataException(
                "Replace target escapes shared storage.");
        }
        EnsurePortablePath(source, "replace-source");
        EnsurePortablePath(destination, "replace-destination");
        if (!string.IsNullOrWhiteSpace(backup))
        {
            EnsurePortablePath(backup, "replace-backup");
        }
        if (!File.Exists(source))
        {
            throw new FileNotFoundException(
                "Shared replace source does not exist.",
                source);
        }
        if (!File.Exists(destination))
        {
            throw new FileNotFoundException(
                "Shared replace destination does not exist.",
                destination);
        }
        if (!string.IsNullOrWhiteSpace(backup) && File.Exists(backup))
        {
            throw new IOException(
                "Shared replace backup already exists: " + backup);
        }
        Directory.CreateDirectory(
            Path.GetDirectoryName(destination) ?? rootDirectory);
        if (!string.IsNullOrWhiteSpace(backup))
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(backup) ?? rootDirectory);
        }
        File.Replace(
            source,
            destination,
            string.IsNullOrWhiteSpace(backup) ? null : backup,
            ignoreMetadataErrors: true);
    }

    public void DeleteFileInsideRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsInside(fullPath, rootDirectory))
        {
            throw new InvalidDataException(
                "Delete target escapes shared storage.");
        }
        EnsurePortablePath(fullPath, "delete-file");
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    public void MoveDirectoryInsideRoot(string sourcePath, string destinationPath)
    {
        var source = Path.GetFullPath(sourcePath);
        var destination = Path.GetFullPath(destinationPath);
        if (!IsInside(source, rootDirectory) || !IsInside(destination, rootDirectory))
        {
            throw new InvalidDataException("Directory move target escapes shared storage.");
        }
        EnsurePortablePath(source, "move-directory-source");
        EnsurePortablePath(destination, "move-directory-destination");
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException("Shared directory move source does not exist: " + source);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("Shared directory move destination already exists: " + destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? rootDirectory);
        Directory.Move(source, destination);
    }

    internal void CommitStagedFileInsideRoot(string stagingPath, string destinationPath, bool overwrite)
    {
        var staging = Path.GetFullPath(stagingPath);
        var destination = Path.GetFullPath(destinationPath);
        if (!IsInside(staging, rootDirectory) || !IsInside(destination, rootDirectory))
            throw new InvalidDataException("Staged file commit escapes shared storage.");
        EnsurePortablePath(staging, "staged-commit-source");
        EnsurePortablePath(destination, "staged-commit-destination");
        if (!File.Exists(staging)) throw new FileNotFoundException("Staged file is missing.", staging);
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? rootDirectory);
        if (!File.Exists(destination))
        {
            File.Move(staging, destination);
            return;
        }
        if (!overwrite) throw new IOException("Staged destination already exists: " + destination);
        try
        {
            File.Replace(staging, destination, null, ignoreMetadataErrors: true);
        }
        catch (PlatformNotSupportedException)
        {
            ReplaceWithRollback(staging, destination);
        }
        catch (IOException)
        {
            ReplaceWithRollback(staging, destination);
        }
    }

    private string CompactArchivePath(string category, string sourcePath, string extension)
    {
        var relative = MakeRelative(rootDirectory, sourcePath);
        string hash;
        using (var sha = SHA256.Create())
        {
            hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(relative.ToLowerInvariant())))
                .Replace("-", "")
                .ToLowerInvariant();
        }

        return Path.Combine(
            rootDirectory,
            "Backups",
            "Storage",
            category,
            hash.Substring(0, 2),
            hash.Substring(2, 30) + "." + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + extension);
    }

    private Mutex CreateWriteMutex()
    {
        using var sha = SHA256.Create();
        var hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(rootDirectory.ToLowerInvariant())))
            .Replace("-", "")
            .Substring(0, 24);
        return new Mutex(false, "AuraShared.Storage." + hash);
    }

    private void EnsureRootDirectories()
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            return;
        }

        foreach (var relative in new[]
                 {
                     "Config/Shared", "Config/Owners", "Config/Runtime", "Data/Owners", "Registries", "Backups/Storage", "Cache", "Transactions", "Logs/Operations"
                  })
        {
            Directory.CreateDirectory(Path.Combine(rootDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
        }
    }

    private static void ValidateRequest(AuraSharedStorageRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.System))
        {
            throw new InvalidDataException("Shared storage system is empty.");
        }

        if (string.Equals(request.Scope, AuraSharedStorageScopes.Owner, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(request.OwnerModId))
        {
            throw new InvalidDataException("Owner configuration requires ownerModId.");
        }
    }

    private static string SafeFileName(string value)
    {
        var fileName = Path.GetFileName((value ?? "").Trim());
        return string.IsNullOrWhiteSpace(fileName) ? "config.json" : fileName;
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

    private static string FullPath(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : Path.GetFullPath(value);
    }

    private static bool SamePath(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsInside(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static string MakeRelative(string root, string path)
    {
        var rootUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(root)));
        var pathUri = new Uri(Path.GetFullPath(path));
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static string AppendDirectorySeparator(string value)
    {
        return value.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
               || value.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? value
            : value + Path.DirectorySeparatorChar;
    }

    private static AuraSharedStorageResponse Failure(string message)
    {
        return new AuraSharedStorageResponse
        {
            Success = false,
            Message = message
        };
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(AuraSharedStorageCoordinator));
        }
    }
}
