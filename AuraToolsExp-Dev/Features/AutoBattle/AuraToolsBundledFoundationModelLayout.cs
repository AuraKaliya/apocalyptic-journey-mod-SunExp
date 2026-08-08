using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace AuraToolsExp.Dll.Features.AutoBattle;

internal sealed class BundledFoundationModelLayoutSource
{
    public string ManifestPath { get; set; } = "";

    public string ManifestDirectory { get; set; } = "";

    public string RelativeManifestPath { get; set; } = "";

    public string RoleDirectoryName { get; set; } = "";

    public string ReleaseDirectoryName { get; set; } = "";

    public bool LegacyRootPackage { get; set; }
}

internal sealed class BundledFoundationModelLayoutDiscovery
{
    public List<BundledFoundationModelLayoutSource> Sources { get; } = new();

    public List<string> Diagnostics { get; } = new();

    public int Rejected { get; set; }
}

/// <summary>
/// Pure filesystem/layout contract for bundled foundation models. Display-name
/// portions of directories are deliberately ignored; only bracketed machine
/// suffixes are validated against package metadata by <see cref="TryValidateIdentity"/>.
/// </summary>
internal static class AuraToolsBundledFoundationModelLayout
{
    public const string ManifestFileName = "foundation-model-package-v5.json";

    public const string WeightsFileName = "foundation-model-weights-v5.bin";

    public const string LegacyV4ManifestFileName =
        "foundation-model-package-v4.json";

    public const string LegacyV3ManifestFileName =
        "foundation-model-package-v3.json";

    public const int MaximumPackageCount = 128;

    public const int MaximumLayoutEntryCount = 2048;

    public const int MaximumDirectorySegmentLength = 200;

    public const int MaximumDisplayLabelLength = 96;

    public const long MaximumManifestBytes = 64L * 1024L * 1024L;

    public const long MaximumAggregateBytes = 1024L * 1024L * 1024L;

    public static BundledFoundationModelLayoutDiscovery Discover(
        string modelRoot,
        CancellationToken cancellation)
    {
        var result = new BundledFoundationModelLayoutDiscovery();
        if (string.IsNullOrWhiteSpace(modelRoot) || !Directory.Exists(modelRoot))
        {
            Reject(result, "内置底模目录不存在：" + (modelRoot ?? ""));
            return result;
        }

        string root;
        try
        {
            root = NormalizeDirectory(modelRoot);
            if (HasReparsePoint(root))
            {
                Reject(result, "内置底模根目录不能是 reparse point：" + root);
                return result;
            }
        }
        catch (Exception ex)
        {
            Reject(result, "内置底模根目录不可用：" + ex.Message);
            return result;
        }

        var entryCount = 0;
        if (!TryEnumerateEntries(
                root,
                root,
                ref entryCount,
                cancellation,
                result,
                out var rootEntries))
        {
            result.Sources.Clear();
            return result;
        }

        var legacyManifests = new List<string>();
        var legacyLayoutValid = true;
        var roleDirectories = new List<string>();
        foreach (var entry in rootEntries)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!TryReadEntry(
                    root,
                    entry,
                    result,
                    out var relative,
                    out var isDirectory))
            {
                legacyLayoutValid = false;
                continue;
            }

            if (isDirectory)
            {
                roleDirectories.Add(entry);
                continue;
            }

            var name = Path.GetFileName(entry);
            if (IsLegacyManifestFileName(name))
            {
                legacyManifests.Add(entry);
            }
            else if (!string.Equals(name, WeightsFileName, StringComparison.Ordinal))
            {
                legacyLayoutValid = false;
                Reject(
                    result,
                    relative + "：Model 根目录只允许旧版单包的固定文件名，或两层角色目录");
            }
        }

        if (legacyManifests.Count > 1)
        {
            Reject(result, "Model 根目录只允许一个 legacy 底模清单");
        }
        else if (legacyManifests.Count == 1 && legacyLayoutValid)
        {
            result.Sources.Add(NewSource(
                root,
                legacyManifests[0],
                true,
                "",
                ""));
        }
        else if (rootEntries.Any(entry =>
                     !Directory.Exists(entry)
                     && string.Equals(
                         Path.GetFileName(entry),
                         WeightsFileName,
                         StringComparison.Ordinal)))
        {
            Reject(result, WeightsFileName + "：根目录权重缺少配套固定清单");
        }

        foreach (var roleDirectory in roleDirectories
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(Path.GetFileName, StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!TryEnumerateEntries(
                    root,
                    roleDirectory,
                    ref entryCount,
                    cancellation,
                    result,
                    out var roleEntries))
            {
                result.Sources.Clear();
                return result;
            }

            var releaseDirectories = new List<string>();
            foreach (var entry in roleEntries)
            {
                if (!TryReadEntry(
                        root,
                        entry,
                        result,
                        out var relative,
                        out var isDirectory))
                {
                    continue;
                }
                if (isDirectory)
                {
                    releaseDirectories.Add(entry);
                }
                else
                {
                    Reject(result, relative + "：角色目录下只允许发布目录");
                }
            }

            if (releaseDirectories.Count == 0)
            {
                TryRelativePath(root, roleDirectory, out var roleRelative);
                Reject(result, roleRelative + "：角色目录不包含发布目录");
                continue;
            }

            foreach (var releaseDirectory in releaseDirectories
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(Path.GetFileName, StringComparer.Ordinal))
            {
                cancellation.ThrowIfCancellationRequested();
                if (!TryEnumerateEntries(
                        root,
                        releaseDirectory,
                        ref entryCount,
                        cancellation,
                        result,
                        out var releaseEntries))
                {
                    result.Sources.Clear();
                    return result;
                }

                var releaseValid = true;
                string manifestPath = "";
                foreach (var entry in releaseEntries)
                {
                    if (!TryReadEntry(
                            root,
                            entry,
                            result,
                            out var relative,
                            out var isDirectory))
                    {
                        releaseValid = false;
                        continue;
                    }
                    if (isDirectory)
                    {
                        releaseValid = false;
                        Reject(result, relative + "：内置底模只允许固定两层目录");
                        continue;
                    }

                    var name = Path.GetFileName(entry);
                    if (string.Equals(name, ManifestFileName, StringComparison.Ordinal))
                    {
                        manifestPath = entry;
                    }
                    else if (!string.Equals(name, WeightsFileName, StringComparison.Ordinal))
                    {
                        releaseValid = false;
                        Reject(result, relative + "：发布目录只允许固定清单和权重文件名");
                    }
                }

                if (string.IsNullOrWhiteSpace(manifestPath))
                {
                    TryRelativePath(root, releaseDirectory, out var releaseRelative);
                    Reject(result, releaseRelative + "：发布目录缺少 " + ManifestFileName);
                    continue;
                }
                if (!releaseValid)
                {
                    continue;
                }

                result.Sources.Add(NewSource(
                    root,
                    manifestPath,
                    false,
                    Path.GetFileName(roleDirectory),
                    Path.GetFileName(releaseDirectory)));
            }
        }

        var ordered = result.Sources
            .OrderBy(
                source => source.RelativeManifestPath,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                source => source.RelativeManifestPath,
                StringComparer.Ordinal)
            .ToList();
        result.Sources.Clear();
        result.Sources.AddRange(ordered.Take(MaximumPackageCount));
        if (ordered.Count > MaximumPackageCount)
        {
            Reject(
                result,
                "内置底模包数量超过上限 "
                + MaximumPackageCount
                + "；仅处理相对路径排序后的前 "
                + MaximumPackageCount
                + " 个");
        }
        return result;
    }

    public static bool TryValidateIdentity(
        BundledFoundationModelLayoutSource source,
        string roleId,
        string packageSha256,
        out string diagnostic)
    {
        if (source == null)
        {
            diagnostic = "模型布局来源为空";
            return false;
        }
        if (source.LegacyRootPackage)
        {
            diagnostic = "";
            return true;
        }
        if (!TryReadBracketedSuffix(
                source.RoleDirectoryName,
                out _,
                out var declaredRoleId))
        {
            diagnostic = "角色目录必须使用“可读名 [RoleId]”格式";
            return false;
        }
        if (!string.Equals(
                declaredRoleId,
                (roleId ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            diagnostic = "角色目录 RoleId 后缀与模型包不一致："
                         + declaredRoleId
                         + " != "
                         + (roleId ?? "").Trim();
            return false;
        }
        if (!TryReadBracketedSuffix(
                source.ReleaseDirectoryName,
                out _,
                out var declaredSha12))
        {
            diagnostic = "发布目录必须使用“可读名 [PackageSha12]”格式";
            return false;
        }
        var sha = (packageSha256 ?? "").Trim();
        if (sha.Length != 64 || !sha.All(IsHex))
        {
            diagnostic = "模型包 SHA-256 无效";
            return false;
        }
        var expectedSha12 = sha.Substring(0, 12);
        if (!string.Equals(
                declaredSha12,
                expectedSha12,
                StringComparison.OrdinalIgnoreCase))
        {
            diagnostic = "发布目录 PackageSha12 后缀与原始模型包不一致："
                         + declaredSha12
                         + " != "
                         + expectedSha12;
            return false;
        }

        diagnostic = "";
        return true;
    }

    public static bool TryResolveWeightsPath(
        BundledFoundationModelLayoutSource source,
        string declaredWeightsFile,
        out string weightsPath,
        out string diagnostic)
    {
        weightsPath = "";
        if (source == null || string.IsNullOrWhiteSpace(source.ManifestDirectory))
        {
            diagnostic = "模型布局来源为空";
            return false;
        }
        if (!string.Equals(
                declaredWeightsFile,
                WeightsFileName,
                StringComparison.Ordinal))
        {
            diagnostic = "FP32 权重必须使用固定文件名 " + WeightsFileName;
            return false;
        }
        try
        {
            var directory = NormalizeDirectory(source.ManifestDirectory);
            var candidate = Path.GetFullPath(Path.Combine(
                directory,
                declaredWeightsFile));
            if (!string.Equals(
                    Path.GetDirectoryName(candidate),
                    directory,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "FP32 权重文件必须与清单位于同一目录";
                return false;
            }
            if (!File.Exists(candidate))
            {
                diagnostic = "FP32 权重文件缺失";
                return false;
            }
            var attributes = File.GetAttributes(candidate);
            if ((attributes & FileAttributes.Directory) != 0
                || (attributes & FileAttributes.ReparsePoint) != 0)
            {
                diagnostic = "FP32 权重文件不能是目录或 reparse point";
                return false;
            }
            weightsPath = candidate;
            diagnostic = "";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = "FP32 权重路径无效：" + ex.Message;
            return false;
        }
    }

    public static bool TryReserveBytes(
        ref long aggregateBytes,
        long bytes,
        out string diagnostic)
    {
        if (bytes < 0
            || aggregateBytes < 0
            || aggregateBytes > MaximumAggregateBytes - bytes)
        {
            diagnostic = "内置底模扫描总字节数超过上限 "
                         + MaximumAggregateBytes;
            return false;
        }
        aggregateBytes += bytes;
        diagnostic = "";
        return true;
    }

    private static BundledFoundationModelLayoutSource NewSource(
        string root,
        string manifestPath,
        bool legacy,
        string roleDirectoryName,
        string releaseDirectoryName)
    {
        if (!TryRelativePath(root, manifestPath, out var relative))
        {
            throw new InvalidDataException("模型清单路径越出 Model 根目录");
        }
        return new BundledFoundationModelLayoutSource
        {
            ManifestPath = Path.GetFullPath(manifestPath),
            ManifestDirectory = NormalizeDirectory(
                Path.GetDirectoryName(manifestPath) ?? ""),
            RelativeManifestPath = relative,
            RoleDirectoryName = roleDirectoryName ?? "",
            ReleaseDirectoryName = releaseDirectoryName ?? "",
            LegacyRootPackage = legacy
        };
    }

    private static bool TryEnumerateEntries(
        string root,
        string directory,
        ref int entryCount,
        CancellationToken cancellation,
        BundledFoundationModelLayoutDiscovery result,
        out List<string> entries)
    {
        entries = new List<string>();
        try
        {
            if (!TryRelativePath(root, directory, out var relative)
                && !string.Equals(
                    NormalizeDirectory(root),
                    NormalizeDirectory(directory),
                    StringComparison.OrdinalIgnoreCase))
            {
                Reject(result, "目录越出 Model 根目录：" + directory);
                return true;
            }
            if (HasReparsePoint(directory))
            {
                Reject(
                    result,
                    (string.IsNullOrWhiteSpace(relative) ? "." : relative)
                    + "：目录不能是 reparse point");
                return true;
            }
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellation.ThrowIfCancellationRequested();
                entryCount++;
                if (entryCount > MaximumLayoutEntryCount)
                {
                    Reject(
                        result,
                        "Model 布局条目数超过上限 " + MaximumLayoutEntryCount);
                    return false;
                }
                entries.Add(entry);
            }
            entries = entries
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(Path.GetFileName, StringComparer.Ordinal)
                .ToList();
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            TryRelativePath(root, directory, out var relative);
            Reject(
                result,
                (string.IsNullOrWhiteSpace(relative) ? "." : relative)
                + "：目录枚举失败："
                + ex.Message);
            return true;
        }
    }

    private static bool TryReadEntry(
        string root,
        string entry,
        BundledFoundationModelLayoutDiscovery result,
        out string relative,
        out bool isDirectory)
    {
        relative = entry;
        isDirectory = false;
        try
        {
            if (!TryRelativePath(root, entry, out relative))
            {
                Reject(result, "路径越出 Model 根目录：" + entry);
                return false;
            }
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                Reject(result, relative + "：不允许 reparse point");
                return false;
            }
            isDirectory = (attributes & FileAttributes.Directory) != 0;
            return true;
        }
        catch (Exception ex)
        {
            Reject(result, relative + "：布局条目不可用：" + ex.Message);
            return false;
        }
    }

    private static bool TryReadBracketedSuffix(
        string directoryName,
        out string displayLabel,
        out string machineSuffix)
    {
        displayLabel = "";
        machineSuffix = "";
        var name = directoryName ?? "";
        if (name.Length == 0
            || name.Length > MaximumDirectorySegmentLength
            || name.Any(IsUnsafeDisplayCharacter)
            || !name.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }
        var marker = name.LastIndexOf(" [", StringComparison.Ordinal);
        if (marker <= 0 || marker + 3 >= name.Length)
        {
            return false;
        }
        displayLabel = name.Substring(0, marker);
        machineSuffix = name.Substring(marker + 2, name.Length - marker - 3);
        return displayLabel.Length > 0
               && displayLabel.Length <= MaximumDisplayLabelLength
               && string.Equals(displayLabel, displayLabel.Trim(), StringComparison.Ordinal)
               && machineSuffix.Length > 0
               && string.Equals(machineSuffix, machineSuffix.Trim(), StringComparison.Ordinal);
    }

    private static bool TryRelativePath(
        string root,
        string path,
        out string relative)
    {
        relative = "";
        try
        {
            var normalizedRoot = NormalizeDirectory(root);
            var fullPath = Path.GetFullPath(path);
            var prefix = normalizedRoot + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            relative = fullPath.Substring(prefix.Length)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar)
            {
                relative = relative.Replace(Path.AltDirectorySeparatorChar, '/');
            }
            return relative.Length > 0
                   && !relative.Split('/').Any(segment =>
                       segment.Length == 0
                       || string.Equals(segment, ".", StringComparison.Ordinal)
                       || string.Equals(segment, "..", StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeDirectory(string path)
    {
        var full = Path.GetFullPath(path ?? "");
        var pathRoot = Path.GetPathRoot(full) ?? "";
        while (full.Length > pathRoot.Length
               && (full.EndsWith(
                       Path.DirectorySeparatorChar.ToString(),
                       StringComparison.Ordinal)
                   || full.EndsWith(
                       Path.AltDirectorySeparatorChar.ToString(),
                       StringComparison.Ordinal)))
        {
            full = full.Substring(0, full.Length - 1);
        }
        return full;
    }

    private static bool HasReparsePoint(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static bool IsHex(char value)
    {
        return value >= '0' && value <= '9'
               || value >= 'a' && value <= 'f'
               || value >= 'A' && value <= 'F';
    }

    private static bool IsLegacyManifestFileName(string name)
    {
        return string.Equals(name, ManifestFileName, StringComparison.Ordinal)
               || string.Equals(
                   name,
                   LegacyV4ManifestFileName,
                   StringComparison.Ordinal)
               || string.Equals(
                   name,
                   LegacyV3ManifestFileName,
                   StringComparison.Ordinal);
    }

    private static bool IsUnsafeDisplayCharacter(char value)
    {
        return char.IsControl(value)
               || char.GetUnicodeCategory(value) == UnicodeCategory.Format
               || value == '\u061c'
               || value == '\u200e'
               || value == '\u200f'
               || value >= '\u202a' && value <= '\u202e'
               || value >= '\u2066' && value <= '\u2069';
    }

    private static void Reject(
        BundledFoundationModelLayoutDiscovery result,
        string diagnostic)
    {
        result.Rejected++;
        result.Diagnostics.Add(diagnostic ?? "内置底模布局无效");
    }
}
