using System.Text;

namespace AuraShared.Core;

public static class AuraSharedFileStore
{
    public static void WriteAllBytes(string ownerId, string path, byte[] payload, bool createBackup = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, payload ?? Array.Empty<byte>());
    }

    public static void WriteAllText(string ownerId, string path, string text, bool createBackup = false)
    {
        WriteAllBytes(ownerId, path, new UTF8Encoding(false).GetBytes(text ?? ""), createBackup);
    }

    public static void MoveFile(string ownerId, string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        File.Move(sourcePath, destinationPath);
    }

    public static void MoveDirectory(string ownerId, string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        Directory.Move(sourcePath, destinationPath);
    }

    public static void DeleteFile(string ownerId, string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    public static AuraSharedFileWriteTransaction BeginWrite(
        string ownerId,
        string destinationPath,
        bool overwrite = true)
    {
        var safeOwner = string.Concat((ownerId ?? "AuraShared")
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var stagingDirectory = Path.Combine(
            Path.GetTempPath(),
            "AuraSharedFileStore.Tests",
            string.IsNullOrWhiteSpace(safeOwner) ? "AuraShared" : safeOwner);
        Directory.CreateDirectory(stagingDirectory);
        return new AuraSharedFileWriteTransaction(
            Path.Combine(stagingDirectory, Guid.NewGuid().ToString("N") + ".tmp"),
            destinationPath,
            overwrite);
    }
}

public sealed class AuraSharedFileWriteTransaction : IDisposable
{
    private readonly string destinationPath;
    private readonly bool overwrite;
    private FileStream? stream;
    private bool committed;

    internal AuraSharedFileWriteTransaction(string stagingPath, string destinationPath, bool overwrite)
    {
        StagingPath = Path.GetFullPath(stagingPath);
        this.destinationPath = Path.GetFullPath(destinationPath);
        this.overwrite = overwrite;
        stream = new FileStream(StagingPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
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
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        if (File.Exists(destinationPath))
        {
            if (!overwrite) throw new IOException("Destination already exists: " + destinationPath);
            File.Delete(destinationPath);
        }
        File.Move(StagingPath, destinationPath);
        committed = true;
    }

    public void Dispose()
    {
        stream?.Dispose();
        stream = null;
        if (!committed && File.Exists(StagingPath)) File.Delete(StagingPath);
    }
}
