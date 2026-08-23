using System;
using System.IO;
using System.Text;

namespace AuraShared.Core;

public static class AuraSharedFileStore
{
    private static readonly object Gate = new();
    private static AuraSharedStorageCoordinator? coordinator;
    private static string activeRoot = "";

    public static void WriteAllBytes(string ownerId, string path, byte[] payload, bool createBackup = false)
    {
        Coordinator().WriteBytesAtomic(path, payload ?? Array.Empty<byte>(), createBackup);
    }

    public static void WriteAllText(string ownerId, string path, string text, bool createBackup = false)
    {
        Coordinator().WriteTextAtomic(path, text ?? "", createBackup);
    }

    public static void MoveFile(string ownerId, string sourcePath, string destinationPath)
    {
        Coordinator().MoveFileInsideRoot(sourcePath, destinationPath);
    }

    public static void MoveDirectory(string ownerId, string sourcePath, string destinationPath)
    {
        Coordinator().MoveDirectoryInsideRoot(sourcePath, destinationPath);
    }

    public static void DeleteFile(string ownerId, string path)
    {
        Coordinator().DeleteFileInsideRoot(path);
    }

    public static AuraSharedFileWriteTransaction BeginWrite(
        string ownerId,
        string destinationPath,
        bool overwrite = true)
    {
        var store = Coordinator();
        var owner = SafeSegment(ownerId, "AuraShared");
        var stagingDirectory = Path.Combine(store.RootDirectory, "Transactions", "FileWrites", owner);
        Directory.CreateDirectory(stagingDirectory);
        var stagingPath = Path.Combine(stagingDirectory, Guid.NewGuid().ToString("N") + ".tmp");
        store.EnsurePortablePath(stagingPath, "file-write-staging");
        return new AuraSharedFileWriteTransaction(store, stagingPath, destinationPath, overwrite);
    }

    private static AuraSharedStorageCoordinator Coordinator()
    {
        var root = Path.GetFullPath(AuraSharedPaths.RootDirectory);
        lock (Gate)
        {
            if (coordinator != null && string.Equals(activeRoot, root, StringComparison.OrdinalIgnoreCase))
                return coordinator;
            coordinator?.Dispose();
            coordinator = new AuraSharedStorageCoordinator(root);
            activeRoot = root;
            return coordinator;
        }
    }

    private static string SafeSegment(string value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(source.Length);
        foreach (var character in source)
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        return builder.Length == 0 ? fallback : builder.ToString();
    }
}

public sealed class AuraSharedFileWriteTransaction : IDisposable
{
    private readonly AuraSharedStorageCoordinator coordinator;
    private readonly string destinationPath;
    private readonly bool overwrite;
    private FileStream? stream;
    private bool committed;

    internal AuraSharedFileWriteTransaction(
        AuraSharedStorageCoordinator coordinator,
        string stagingPath,
        string destinationPath,
        bool overwrite)
    {
        this.coordinator = coordinator;
        StagingPath = Path.GetFullPath(stagingPath);
        this.destinationPath = Path.GetFullPath(destinationPath);
        this.overwrite = overwrite;
        coordinator.EnsurePortablePath(this.destinationPath, "file-write-destination");
        stream = new FileStream(
            StagingPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.Read,
            8192,
            FileOptions.SequentialScan);
    }

    public string StagingPath { get; }

    public Stream Stream => stream ?? throw new ObjectDisposedException(nameof(AuraSharedFileWriteTransaction));

    public void Commit()
    {
        if (committed) return;
        if (stream == null) throw new ObjectDisposedException(nameof(AuraSharedFileWriteTransaction));
        stream.Flush(true);
        stream.Dispose();
        stream = null;
        coordinator.CommitStagedFileInsideRoot(StagingPath, destinationPath, overwrite);
        committed = true;
    }

    public void Dispose()
    {
        stream?.Dispose();
        stream = null;
        if (!committed)
        {
            try { coordinator.DeleteFileInsideRoot(StagingPath); } catch { }
        }
    }
}
