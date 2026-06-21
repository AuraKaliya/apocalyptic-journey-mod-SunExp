using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AuraShared.Core;

public sealed class AuraSharedStorageCoordinator : IDisposable
{
    private readonly ReaderWriterLockSlim gate = new(LockRecursionPolicy.NoRecursion);
    private string rootDirectory;
    private bool disposed;

    public AuraSharedStorageCoordinator(string rootDirectory)
    {
        this.rootDirectory = FullPath(rootDirectory);
        EnsureRootDirectories();
    }

    public string RootDirectory => rootDirectory;

    public void InitializeRoot(string root)
    {
        var fullRoot = FullPath(root);
        if (string.IsNullOrWhiteSpace(fullRoot))
        {
            throw new ArgumentException("Shared storage root is empty.", nameof(root));
        }

        gate.EnterWriteLock();
        try
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
        finally
        {
            gate.ExitWriteLock();
        }
    }

    public AuraSharedStorageResponse Read(AuraSharedStorageRequest request)
    {
        gate.EnterReadLock();
        try
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
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    public AuraSharedStorageResponse Write(AuraSharedStorageRequest request)
    {
        return ExecuteWrite(() => WriteNoLock(request));
    }

    public T ExecuteRead<T>(Func<T> action)
    {
        gate.EnterReadLock();
        try
        {
            ThrowIfDisposed();
            return action();
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    public T ExecuteWrite<T>(Func<T> action)
    {
        gate.EnterWriteLock();
        try
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
        }
        finally
        {
            gate.ExitWriteLock();
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

    public void WriteTextAtomic(string path, string text, bool createBackup)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsInside(fullPath, rootDirectory))
        {
            throw new InvalidDataException("Atomic write target escapes shared storage: " + path);
        }

        var directory = Path.GetDirectoryName(fullPath) ?? rootDirectory;
        Directory.CreateDirectory(directory);
        if (createBackup && File.Exists(fullPath))
        {
            CreateBackup(fullPath);
        }

        var tempPath = fullPath + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            var bytes = new UTF8Encoding(false).GetBytes(text ?? "");
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
        gate.Dispose();
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
            var quarantinePath = Path.Combine(
                rootDirectory,
                "Backups",
                "Storage",
                "Invalid",
                MakeRelative(rootDirectory, path) + "." + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + ".invalid");
            Directory.CreateDirectory(Path.GetDirectoryName(quarantinePath) ?? Path.Combine(rootDirectory, "Backups", "Storage", "Invalid"));
            File.Move(path, quarantinePath);
            return null;
        }
    }

    private void CreateBackup(string sourcePath)
    {
        var relative = MakeRelative(rootDirectory, sourcePath);
        var backupPath = Path.Combine(
            rootDirectory,
            "Backups",
            "Storage",
            relative + "." + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + ".bak");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath) ?? Path.Combine(rootDirectory, "Backups"));
        File.Copy(sourcePath, backupPath, false);
    }

    private static void ReplaceWithRollback(string tempPath, string destinationPath)
    {
        var rollbackPath = destinationPath + ".rollback." + Guid.NewGuid().ToString("N");
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
                     "Config/Shared", "Config/Owners", "Config/Runtime", "Registries", "Backups/Storage", "Cache", "Transactions"
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
