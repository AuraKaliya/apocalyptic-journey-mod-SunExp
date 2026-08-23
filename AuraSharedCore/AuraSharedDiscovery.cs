using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AuraShared.Core;

public static class AuraSharedDiscoveryProtocol
{
    public const int CurrentSchemaVersion = 1;
    public const string DefaultManifestPath = "SharedResources/aura.discovery.json";
    public const int MaximumSharedResourceFiles = 4096;
    public const long MaximumSharedResourceBytes = 1024L * 1024L * 1024L;
}

public static class AuraSharedDiscoveryContributionKinds
{
    public const string Resources = "resources";
    public const string Audio = "audio";
    public const string Cg = "cg";

    public static string Normalize(string value)
    {
        return (value ?? "").Trim().ToLowerInvariant();
    }
}

public sealed class AuraSharedDiscoveryManifest
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = AuraSharedDiscoveryProtocol.CurrentSchemaVersion;

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("participantKind")]
    public string ParticipantKind { get; set; } = AuraSharedParticipantKinds.Content;

    [JsonProperty("contributions")]
    public List<AuraSharedDiscoveryContribution> Contributions { get; set; } = new();

    public void Normalize()
    {
        OwnerModId = (OwnerModId ?? "").Trim();
        ParticipantKind = AuraSharedParticipantKinds.Normalize(ParticipantKind);
        Contributions ??= new List<AuraSharedDiscoveryContribution>();
        Contributions.ForEach(item => item?.Normalize());
    }
}

public sealed class AuraSharedDiscoveryContribution
{
    [JsonProperty("kind")]
    public string Kind { get; set; } = "";

    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("path")]
    public string Path { get; set; } = "";

    [JsonProperty("required")]
    public bool Required { get; set; } = true;

    public void Normalize()
    {
        Kind = AuraSharedDiscoveryContributionKinds.Normalize(Kind);
        Id = (Id ?? "").Trim();
        Path = AuraSharedPaths.NormalizeRelativePath(Path);
    }
}

public sealed class AuraSharedDiscoveryContributionPlan
{
    public string Kind { get; set; } = "";

    public string Id { get; set; } = "";

    public string RelativePath { get; set; } = "";

    public string AbsolutePath { get; set; } = "";

    public bool Required { get; set; }
}

public sealed class AuraSharedDiscoverySource
{
    public string ModProjectId { get; set; } = "";

    public string OwnerModId { get; set; } = "";

    public string ParticipantKind { get; set; } = AuraSharedParticipantKinds.Content;

    public string ModRoot { get; set; } = "";

    public string SharedResourcesRoot { get; set; } = "";

    public string ManifestPath { get; set; } = "";

    public string Fingerprint { get; set; } = "";

    public IReadOnlyList<AuraSharedDiscoveryContributionPlan> Contributions { get; set; } =
        Array.Empty<AuraSharedDiscoveryContributionPlan>();
}

public sealed class AuraSharedDiscoveryLoadResult
{
    public bool Found { get; set; }

    public bool Success { get; set; }

    public string ErrorCode { get; set; } = "";

    public string Message { get; set; } = "";

    public AuraSharedDiscoverySource? Source { get; set; }
}

public static class AuraSharedDiscoveryLoader
{
    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, CachedDiscovery> Cache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);

    public static AuraSharedDiscoveryLoadResult Load(string modRoot, bool forceRefresh = false)
    {
        string root;
        try
        {
            root = Path.GetFullPath(modRoot ?? "");
        }
        catch (Exception ex)
        {
            return Failed(ex.GetType().Name, ex.Message);
        }
        lock (CacheGate)
        {
            if (!forceRefresh
                && Cache.TryGetValue(root, out var cached)
                && DateTime.UtcNow - cached.CreatedUtc <= CacheTtl)
            {
                return cached.Result;
            }
        }
        var result = LoadUncached(root);
        lock (CacheGate)
        {
            Cache[root] = new CachedDiscovery(DateTime.UtcNow, result);
        }
        return result;
    }

    private static AuraSharedDiscoveryLoadResult LoadUncached(string root)
    {
        try
        {
            var sharedRoot = Path.GetFullPath(Path.Combine(root, "SharedResources"));
            var manifestPath = Path.GetFullPath(Path.Combine(
                root,
                AuraSharedDiscoveryProtocol.DefaultManifestPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!AuraSharedPaths.IsInsideDirectory(manifestPath, root) || !File.Exists(manifestPath))
            {
                return new AuraSharedDiscoveryLoadResult { Found = false, Success = true };
            }

            if (IsReparsePoint(root) || IsReparsePoint(sharedRoot) || IsReparsePoint(manifestPath))
            {
                return Failed("ReparsePoint", "Shared resource discovery does not accept junction or symbolic-link roots.");
            }

            var projectFiles = Directory.GetFiles(root, "*.modproj", SearchOption.TopDirectoryOnly);
            if (projectFiles.Length != 1)
            {
                return Failed("ModProjectIdentity", "A discovered Mod must contain exactly one root *.modproj file.");
            }

            var projectText = File.ReadAllText(projectFiles[0]).Trim();
            if (!ulong.TryParse(projectText, out var projectId) || projectId == 0)
            {
                return Failed("ModProjectIdentity", "The root *.modproj file must contain one positive numeric id.");
            }
            var configPath = Path.Combine(root, "ModConfig.json");
            if (File.Exists(configPath))
            {
                var config = JObject.Parse(File.ReadAllText(configPath));
                var publishedText = config.GetValue("PublishedFileId", StringComparison.OrdinalIgnoreCase)?.ToString().Trim()
                                    ?? config.GetValue("WorkshopPublishedFileId", StringComparison.OrdinalIgnoreCase)?.ToString().Trim()
                                    ?? "";
                if (publishedText.Length > 0
                    && (!ulong.TryParse(publishedText, out var publishedId) || publishedId != projectId))
                {
                    return Failed("ModProjectIdentity", "ModConfig published id does not match the root *.modproj id.");
                }
            }

            var manifest = AuraSharedJson.Deserialize<AuraSharedDiscoveryManifest>(File.ReadAllText(manifestPath));
            if (manifest == null)
            {
                return Failed("InvalidManifest", "Shared resource discovery manifest JSON is invalid.");
            }
            manifest.Normalize();
            if (manifest.SchemaVersion != AuraSharedDiscoveryProtocol.CurrentSchemaVersion)
            {
                return Failed("UnsupportedSchema", "Unsupported shared discovery schemaVersion=" + manifest.SchemaVersion + ".");
            }
            if (string.IsNullOrWhiteSpace(manifest.OwnerModId))
            {
                return Failed("OwnerIdentity", "Shared discovery ownerModId is empty.");
            }
            if (manifest.Contributions.Count == 0)
            {
                return Failed("EmptyDiscovery", "Shared discovery must declare at least one contribution.");
            }

            var plans = new List<AuraSharedDiscoveryContributionPlan>();
            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var contribution in manifest.Contributions.Where(item => item != null))
            {
                if (string.IsNullOrWhiteSpace(contribution.Kind)
                    || string.IsNullOrWhiteSpace(contribution.Id)
                    || string.IsNullOrWhiteSpace(contribution.Path))
                {
                    return Failed("InvalidContribution", "Every shared discovery contribution needs kind, id, and path.");
                }
                var identity = contribution.Kind + ":" + contribution.Id;
                if (!identities.Add(identity))
                {
                    return Failed("DuplicateContribution", "Duplicate shared discovery contribution: " + identity);
                }
                var absolute = Path.GetFullPath(Path.Combine(
                    sharedRoot,
                    contribution.Path.Replace('/', Path.DirectorySeparatorChar)));
                if (!AuraSharedPaths.IsInsideDirectory(absolute, sharedRoot) || IsReparsePoint(absolute))
                {
                    return Failed("ContributionPath", "Shared discovery contribution escapes SharedResources: " + contribution.Path);
                }
                if (!File.Exists(absolute))
                {
                    if (contribution.Required)
                    {
                        return Failed("ContributionMissing", "Required shared discovery contribution is missing: " + contribution.Path);
                    }
                    continue;
                }
                plans.Add(new AuraSharedDiscoveryContributionPlan
                {
                    Kind = contribution.Kind,
                    Id = contribution.Id,
                    RelativePath = contribution.Path,
                    AbsolutePath = absolute,
                    Required = contribution.Required
                });
            }

            return new AuraSharedDiscoveryLoadResult
            {
                Found = true,
                Success = true,
                Source = new AuraSharedDiscoverySource
                {
                    ModProjectId = projectId.ToString(),
                    OwnerModId = manifest.OwnerModId,
                    ParticipantKind = manifest.ParticipantKind,
                    ModRoot = root,
                    SharedResourcesRoot = sharedRoot,
                    ManifestPath = manifestPath,
                    Fingerprint = Fingerprint(sharedRoot),
                    Contributions = plans
                }
            };
        }
        catch (Exception ex)
        {
            return Failed(ex.GetType().Name, ex.Message);
        }
    }

    private static string Fingerprint(string sharedRoot)
    {
        foreach (var directory in Directory.GetDirectories(sharedRoot, "*", SearchOption.AllDirectories))
        {
            if (IsReparsePoint(directory))
            {
                throw new InvalidDataException("SharedResources contains a reparse-point directory: " + directory);
            }
        }
        var files = Directory.GetFiles(sharedRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count > AuraSharedDiscoveryProtocol.MaximumSharedResourceFiles)
        {
            throw new InvalidDataException("SharedResources contains too many files: " + files.Count);
        }

        long totalBytes = 0;
        var inventory = new StringBuilder();
        foreach (var file in files)
        {
            if (IsReparsePoint(file))
            {
                throw new InvalidDataException("SharedResources contains a reparse-point file: " + file);
            }
            var info = new FileInfo(file);
            totalBytes += info.Length;
            if (totalBytes > AuraSharedDiscoveryProtocol.MaximumSharedResourceBytes)
            {
                throw new InvalidDataException("SharedResources exceeds the discovery byte budget.");
            }
            inventory.Append(Relative(sharedRoot, file).Replace('\\', '/'))
                .Append(':').Append(info.Length)
                .Append(':').Append(FileSha256(file))
                .Append('\n');
        }
        return Sha256(Encoding.UTF8.GetBytes(inventory.ToString()));
    }

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return Hex(algorithm.ComputeHash(stream));
    }

    private static string Sha256(byte[] bytes)
    {
        using var algorithm = SHA256.Create();
        return Hex(algorithm.ComputeHash(bytes));
    }

    private static string Hex(IEnumerable<byte> bytes)
    {
        return string.Concat(bytes.Select(value => value.ToString("x2")));
    }

    private static string Relative(string root, string path)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? full.Substring(prefix.Length)
            : full;
    }

    private static bool IsReparsePoint(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static AuraSharedDiscoveryLoadResult Failed(string code, string message)
    {
        return new AuraSharedDiscoveryLoadResult
        {
            Found = true,
            Success = false,
            ErrorCode = code ?? "",
            Message = message ?? ""
        };
    }

    private sealed class CachedDiscovery
    {
        public CachedDiscovery(DateTime createdUtc, AuraSharedDiscoveryLoadResult result)
        {
            CreatedUtc = createdUtc;
            Result = result;
        }

        public DateTime CreatedUtc { get; }
        public AuraSharedDiscoveryLoadResult Result { get; }
    }
}
