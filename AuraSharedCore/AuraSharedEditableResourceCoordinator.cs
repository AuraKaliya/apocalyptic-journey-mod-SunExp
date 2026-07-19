using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace AuraShared.Core;

public sealed class AuraSharedEditableResourceCoordinator
{
    private static readonly object Gate = new();
    private readonly string rootDirectory;

    public AuraSharedEditableResourceCoordinator(string rootDirectory)
    {
        this.rootDirectory = Path.GetFullPath(rootDirectory ?? "");
    }

    public AuraSharedEditableResourceResult Seed(AuraSharedEditableResourceRequest request)
    {
        lock (Gate)
        {
            return SeedNoLock(request);
        }
    }

    public string StageTemporary(string ownerModId, string logicalId, string extension, byte[] content)
    {
        lock (Gate)
        {
            if (content == null || content.Length == 0)
            {
                throw new InvalidOperationException("Editable temporary resource content is empty.");
            }

            var directory = Path.Combine(
                rootDirectory,
                "Cache",
                "Editable",
                "External",
                SafeSegment(ownerModId),
                SafeSegment(logicalId));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + NormalizeExtension(extension));
            File.WriteAllBytes(path, content);
            return path;
        }
    }

    public void ReleaseTemporary(string path)
    {
        lock (Gate)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var fullPath = Path.GetFullPath(path);
            var temporaryRoot = Path.GetFullPath(Path.Combine(rootDirectory, "Cache", "Editable", "External"));
            var prefix = temporaryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Editable temporary resource path escapes the staging directory.");
            }

            TryDelete(fullPath);
        }
    }

    private AuraSharedEditableResourceResult SeedNoLock(AuraSharedEditableResourceRequest request)
    {
        try
        {
            Validate(request);
            var sourcePath = Path.GetFullPath(request.SourcePath);
            var destinationPath = ResolveDestination(request.DestinationRelativePath);
            var seedHash = HashFile(sourcePath);
            if (!File.Exists(destinationPath))
            {
                Commit(sourcePath, destinationPath, "", out _);
                return Success(AuraSharedEditableResourceStatuses.Created, true, false, seedHash, seedHash, destinationPath, "");
            }

            var contentHash = HashFile(destinationPath);
            if (HashEquals(contentHash, seedHash))
            {
                return Success(AuraSharedEditableResourceStatuses.ExistingDefault, false, false, seedHash, contentHash, destinationPath, "");
            }

            var previousSeedHash = (request.PreviousSeedHash ?? "").Trim();
            if (!request.ForceReset && !string.IsNullOrWhiteSpace(previousSeedHash) && !HashEquals(contentHash, previousSeedHash))
            {
                return Success(AuraSharedEditableResourceStatuses.PreservedCustomized, false, true, seedHash, contentHash, destinationPath, "");
            }

            if (!request.ForceReset && string.IsNullOrWhiteSpace(previousSeedHash))
            {
                return Success(AuraSharedEditableResourceStatuses.PreservedCustomized, false, true, seedHash, contentHash, destinationPath, "");
            }

            Commit(sourcePath, destinationPath, request.OwnerModId, out var backupPath);
            var status = request.ForceReset
                ? AuraSharedEditableResourceStatuses.Reset
                : AuraSharedEditableResourceStatuses.UpdatedDefault;
            return Success(status, true, false, seedHash, seedHash, destinationPath, backupPath);
        }
        catch (Exception ex)
        {
            return new AuraSharedEditableResourceResult
            {
                Success = false,
                Status = AuraSharedEditableResourceStatuses.Failed,
                Message = ex.Message
            };
        }
    }

    private void Validate(AuraSharedEditableResourceRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.OwnerModId)
            || string.IsNullOrWhiteSpace(request.System)
            || string.IsNullOrWhiteSpace(request.LogicalId))
        {
            throw new InvalidOperationException("Editable resource owner, system, and logical id are required.");
        }

        if (string.IsNullOrWhiteSpace(request.SourcePath) || !File.Exists(request.SourcePath))
        {
            throw new FileNotFoundException("Editable resource seed file is missing.", request.SourcePath);
        }

        if (string.IsNullOrWhiteSpace(request.DestinationRelativePath)
            || Path.IsPathRooted(request.DestinationRelativePath))
        {
            throw new InvalidOperationException("Editable resource destination must be relative to AuraShared.");
        }

        ResolveDestination(request.DestinationRelativePath);
    }

    private string ResolveDestination(string relativePath)
    {
        var normalized = (relativePath ?? "").Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var destination = Path.GetFullPath(Path.Combine(rootDirectory, normalized));
        var rootPrefix = rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Editable resource destination escapes AuraShared.");
        }

        return destination;
    }

    private void Commit(string sourcePath, string destinationPath, string ownerModId, out string backupPath)
    {
        var directory = Path.GetDirectoryName(destinationPath) ?? rootDirectory;
        Directory.CreateDirectory(directory);
        var transactionId = Guid.NewGuid().ToString("N");
        var stagingDirectory = Path.Combine(rootDirectory, "Cache", "Editable", transactionId);
        var stagingPath = Path.Combine(stagingDirectory, "payload");
        Directory.CreateDirectory(stagingDirectory);
        backupPath = "";
        try
        {
            File.Copy(sourcePath, stagingPath, false);
            if (File.Exists(destinationPath))
            {
                backupPath = Path.Combine(
                    rootDirectory,
                    "Backups",
                    "Editable",
                    SafeSegment(ownerModId),
                    DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + transactionId + Path.GetExtension(destinationPath));
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath) ?? rootDirectory);
                File.Replace(stagingPath, destinationPath, backupPath, true);
            }
            else
            {
                File.Move(stagingPath, destinationPath);
            }
        }
        finally
        {
            TryDelete(stagingPath);
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private static AuraSharedEditableResourceResult Success(
        string status,
        bool changed,
        bool customized,
        string seedHash,
        string contentHash,
        string installedPath,
        string backupPath)
    {
        return new AuraSharedEditableResourceResult
        {
            Success = true,
            Changed = changed,
            Customized = customized,
            Status = status,
            SeedHash = seedHash,
            ContentHash = contentHash,
            InstalledPath = installedPath,
            BackupPath = backupPath
        };
    }

    private static string HashFile(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }

    private static bool HashEquals(string left, string right)
    {
        return string.Equals((left ?? "").Trim(), (right ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeSegment(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "UnknownOwner" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            text = text.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(text) ? "UnknownOwner" : text;
    }

    private static string NormalizeExtension(string value)
    {
        var extension = (value ?? "").Trim().TrimStart('.');
        if (extension.Length == 0 || extension.Length > 12 || extension.Any(character => !char.IsLetterOrDigit(character)))
        {
            return ".tmp";
        }

        return "." + extension.ToLowerInvariant();
    }

    private static void TryDelete(string path)
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

    private static void TryDeleteDirectory(string path)
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
}
