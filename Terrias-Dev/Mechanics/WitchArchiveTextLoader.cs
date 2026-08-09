using System;
using System.IO;

namespace Terrias.Dll.Mechanics;

public static class WitchArchiveTextLoader
{
    public static bool TryRead(
        string modDirectory,
        string relativePath,
        out string text,
        out string error)
    {
        text = "";
        error = "";

        if (string.IsNullOrWhiteSpace(modDirectory))
        {
            error = "mod directory is empty";
            return false;
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            error = "path is empty";
            return false;
        }

        try
        {
            var normalizedPath = relativePath.Trim().Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalizedPath))
            {
                error = "absolute paths are not allowed";
                return false;
            }

            if (!string.Equals(Path.GetExtension(normalizedPath), ".txt", StringComparison.OrdinalIgnoreCase))
            {
                error = "only .txt files are allowed";
                return false;
            }

            var root = Path.GetFullPath(modDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(root, normalizedPath));
            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                error = "path escapes the mod directory";
                return false;
            }

            if (!File.Exists(candidate))
            {
                error = "file does not exist";
                return false;
            }

            var loaded = Normalize(File.ReadAllText(candidate));
            if (loaded.Length == 0)
            {
                error = "file is empty";
                return false;
            }

            text = loaded;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static string Normalize(string value)
    {
        return (value ?? "")
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Trim();
    }
}
