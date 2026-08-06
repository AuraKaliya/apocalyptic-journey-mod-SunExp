using System;
using System.IO;

namespace AuraCombatAi.Shared;

public static class CombatFoundationPathRuntime
{
    public const string WindowsExtendedPathPrefix = @"\\?\";

    public const string WindowsExtendedUncPathPrefix = @"\\?\UNC\";

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }
        return Path.GetFullPath(RemoveExtendedPrefix(path.Trim()));
    }

    public static string ForFileSystem(string path)
    {
        var fullPath = Normalize(path);
        if (Path.DirectorySeparatorChar != '\\' || fullPath.Length == 0)
        {
            return fullPath;
        }
        if (fullPath.StartsWith(
                WindowsExtendedPathPrefix,
                StringComparison.Ordinal))
        {
            return fullPath;
        }
        return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
            ? WindowsExtendedUncPathPrefix + fullPath.Substring(2)
            : WindowsExtendedPathPrefix + fullPath;
    }

    public static string ForExternalProcess(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            return path ?? "";
        }
        return ForFileSystem(path);
    }

    public static bool FileExists(string path)
    {
        return !string.IsNullOrWhiteSpace(path)
               && File.Exists(ForFileSystem(path));
    }

    public static bool DirectoryExists(string path)
    {
        return !string.IsNullOrWhiteSpace(path)
               && Directory.Exists(ForFileSystem(path));
    }

    public static void CreateDirectory(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            Directory.CreateDirectory(ForFileSystem(path));
        }
    }

    public static long FileLength(string path)
    {
        return new FileInfo(ForFileSystem(path)).Length;
    }

    public static void DeleteFile(string path)
    {
        if (FileExists(path))
        {
            File.Delete(ForFileSystem(path));
        }
    }

    public static string RemoveExtendedPrefix(string path)
    {
        if (path.StartsWith(
                WindowsExtendedUncPathPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path.Substring(WindowsExtendedUncPathPrefix.Length);
        }
        return path.StartsWith(
            WindowsExtendedPathPrefix,
            StringComparison.OrdinalIgnoreCase)
            ? path.Substring(WindowsExtendedPathPrefix.Length)
            : path;
    }
}
