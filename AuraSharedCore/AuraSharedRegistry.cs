using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Witch.Mod;
using IOPath = System.IO.Path;

namespace AuraShared.Core;

public static class AuraSharedSystems
{
    public const string Skin = "Skin";
    public const string Audio = "Audio";
    public const string Cg = "CG";
    public const string Config = "Config";
    public const string Log = "Log";
    public const string Journey = "Journey";
    public const string CardUseFx = "CardUseFx";
    public const string Role = "Role";
    public const string GameData = "GameData";
}

public sealed class AuraSharedRegistryManifest
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("protocol")]
    public AuraSharedProtocolManifest Protocol { get; set; } = new();

    [JsonProperty("resources")]
    public List<AuraSharedResourceRecord> Resources { get; set; } = new();

    public void Normalize(string fallbackOwner)
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
        OwnerModId = string.IsNullOrWhiteSpace(OwnerModId) ? fallbackOwner : OwnerModId.Trim();
        Protocol ??= new AuraSharedProtocolManifest();
        Resources ??= new List<AuraSharedResourceRecord>();
        foreach (var resource in Resources)
        {
            if (string.IsNullOrWhiteSpace(resource.OwnerModId))
            {
                resource.OwnerModId = OwnerModId;
            }

            resource.Normalize();
        }
    }
}

public sealed class AuraSharedProtocolManifest
{
    [JsonProperty("minVersion")]
    public int MinVersion { get; set; } = 1;

    [JsonProperty("preferredVersion")]
    public int PreferredVersion { get; set; } = 1;
}

public sealed class AuraSharedResourceRecord
{
    [JsonProperty("system")]
    public string System { get; set; } = "";

    [JsonProperty("resourceId")]
    public string ResourceId { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("sourceOwners")]
    public string[] SourceOwners { get; set; } = Array.Empty<string>();

    [JsonProperty("kind")]
    public string Kind { get; set; } = "";

    [JsonProperty("path")]
    public string Path { get; set; } = "";

    [JsonProperty("absolutePath")]
    public string AbsolutePath { get; set; } = "";

    [JsonProperty("sourceRoot")]
    public string SourceRoot { get; set; } = "";

    [JsonProperty("targetRoleIds")]
    public string[] TargetRoleIds { get; set; } = Array.Empty<string>();

    [JsonProperty("tags")]
    public string[] Tags { get; set; } = Array.Empty<string>();

    [JsonProperty("priority")]
    public int Priority { get; set; }

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public string UniqueKey => NormalizeKey(System) + "::" + NormalizeKey(ResourceId);

    public void Normalize()
    {
        System = (System ?? "").Trim();
        ResourceId = (ResourceId ?? "").Trim();
        OwnerModId = (OwnerModId ?? "").Trim();
        SourceOwners = CleanArray((SourceOwners ?? Array.Empty<string>()).Concat(new[] { OwnerModId }));
        Kind = (Kind ?? "").Trim();
        Path = AuraSharedPaths.NormalizeRelativePath(Path);
        SourceRoot = SafeFullPath(SourceRoot);
        AbsolutePath = ResolveAbsolutePath(SourceRoot, Path, AbsolutePath);
        TargetRoleIds = CleanArray(TargetRoleIds);
        Tags = CleanArray(Tags);
        Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static AuraSharedResourceRecord? FromObject(object? source)
    {
        if (source == null)
        {
            return null;
        }

        if (source is AuraSharedResourceRecord typed)
        {
            return typed;
        }

        var record = new AuraSharedResourceRecord
        {
            System = AuraSharedReflection.ReadString(source, "System"),
            ResourceId = AuraSharedReflection.ReadString(source, "ResourceId"),
            OwnerModId = AuraSharedReflection.ReadString(source, "OwnerModId"),
            SourceOwners = AuraSharedReflection.EnumerateStrings(AuraSharedReflection.GetMemberValue(source, "SourceOwners")).ToArray(),
            Kind = AuraSharedReflection.ReadString(source, "Kind"),
            Path = AuraSharedReflection.ReadString(source, "Path"),
            AbsolutePath = AuraSharedReflection.ReadString(source, "AbsolutePath"),
            SourceRoot = AuraSharedReflection.ReadString(source, "SourceRoot"),
            TargetRoleIds = AuraSharedReflection.EnumerateStrings(AuraSharedReflection.GetMemberValue(source, "TargetRoleIds")).ToArray(),
            Tags = AuraSharedReflection.EnumerateStrings(AuraSharedReflection.GetMemberValue(source, "Tags")).ToArray(),
            Priority = AuraSharedReflection.ReadInt(source, "Priority"),
            Enabled = AuraSharedReflection.ReadBool(source, "Enabled", true),
            Metadata = AuraSharedReflection.AsStringDictionary(AuraSharedReflection.GetMemberValue(source, "Metadata"))
                       ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
        record.Normalize();
        return record;
    }

    private static string ResolveAbsolutePath(string sourceRoot, string relativePath, string absolutePath)
    {
        try
        {
            var candidate = (absolutePath ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return IOPath.IsPathRooted(candidate)
                    ? IOPath.GetFullPath(candidate)
                    : IOPath.GetFullPath(IOPath.Combine(sourceRoot, candidate.Replace('/', IOPath.DirectorySeparatorChar)));
            }

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return "";
            }

            if (IOPath.IsPathRooted(relativePath))
            {
                return IOPath.GetFullPath(relativePath);
            }

            return string.IsNullOrWhiteSpace(sourceRoot)
                ? AuraSharedPaths.ResolveSharedPath(relativePath)
                : IOPath.GetFullPath(IOPath.Combine(sourceRoot, relativePath.Replace('/', IOPath.DirectorySeparatorChar)));
        }
        catch
        {
            return absolutePath ?? "";
        }
    }

    private static string[] CleanArray(IEnumerable<string>? values)
    {
        return values?
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();
    }

    private static string SafeFullPath(string value)
    {
        try
        {
            return string.IsNullOrWhiteSpace(value) ? "" : IOPath.GetFullPath(value);
        }
        catch
        {
            return "";
        }
    }

    private static string NormalizeKey(string value)
    {
        return (value ?? "").Trim().ToLowerInvariant();
    }

}

public static class AuraSharedRegistry
{
    [Obsolete("Use AuraSharedResourceProtocol.RegisterManifest for canonical layered resource registration.")]
    public static bool RegisterManifest(ModConfig? modConfig, string ownerModId, string manifestRelativePath = "aura.shared.registry.json")
    {
        AuraSharedRuntime.Initialize(modConfig, ownerModId);
        var modRoot = modConfig?.DirectoryName ?? AuraSharedPaths.PackageDirectory;
        var manifestPath = string.IsNullOrWhiteSpace(manifestRelativePath)
            ? ""
            : Path.Combine(modRoot, manifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        return RegisterManifestPath(ownerModId, manifestPath, modRoot);
    }

    public static bool RegisterManifestPath(string ownerModId, string manifestPath, string baseDirectory)
    {
        var result = AuraSharedRuntime.InvokeComponent(null, ownerModId, "RegisterManifestPath", ownerModId, manifestPath, baseDirectory);
        return result is bool registered && registered;
    }

    public static bool RegisterManifestJson(string ownerModId, string manifestJson, string baseDirectory)
    {
        var result = AuraSharedRuntime.InvokeComponent(null, ownerModId, "RegisterManifestJson", ownerModId, manifestJson, baseDirectory);
        return result is bool registered && registered;
    }

    public static bool RegisterResource(string ownerModId, AuraSharedResourceRecord record)
    {
        AuraSharedRuntime.Initialize(null, ownerModId);
        if (string.IsNullOrWhiteSpace(record.OwnerModId))
        {
            record.OwnerModId = ownerModId;
        }

        var result = AuraSharedRuntime.InvokeComponent(null, ownerModId, "RegisterResource", record);
        return result is bool registered && registered;
    }

    public static IReadOnlyList<AuraSharedResourceRecord> GetResources(string ownerModId, string system)
    {
        AuraSharedRuntime.Initialize(null, ownerModId);
        var json = AuraSharedRuntime.InvokeComponent(null, ownerModId, "GetResourcesJson", system) as string;
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<AuraSharedResourceRecord>();
        }

        try
        {
            return AuraSharedJson.Deserialize<AuraSharedResourceRecord[]>(json!) ?? Array.Empty<AuraSharedResourceRecord>();
        }
        catch
        {
            return Array.Empty<AuraSharedResourceRecord>();
        }
    }

    public static int LoadPersistentManifests(string ownerModId, string system)
    {
        AuraSharedRuntime.Initialize(null, ownerModId);
        var directory = AuraSharedPaths.RegistryDirectory(system);
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var loaded = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.manifest.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(path);
            var owner = fileName.Substring(0, fileName.Length - ".manifest.json".Length);
            var response = AuraSharedStorage.Read(owner, new AuraSharedStorageRequest
            {
                Scope = AuraSharedStorageScopes.Registry,
                System = system,
                FileName = fileName
            });
            if (response.Success && response.Found
                && RegisterManifestJson(owner, response.PayloadJson, directory))
            {
                loaded++;
            }
        }

        return loaded;
    }

    public static string SavePersistentManifest(string system, string ownerModId, AuraSharedRegistryManifest manifest)
    {
        AuraSharedRuntime.Initialize(null, ownerModId);
        manifest.Normalize(ownerModId);
        var directory = AuraSharedPaths.RegistryDirectory(system);
        Directory.CreateDirectory(directory);
        var fileName = AuraSharedPaths.SafeSegment(ownerModId, "UnknownOwner") + ".manifest.json";
        var response = AuraSharedStorage.Write(ownerModId, new AuraSharedStorageRequest
        {
            Scope = AuraSharedStorageScopes.Registry,
            System = system,
            FileName = fileName,
            WriterId = ownerModId,
            AuthorityId = ownerModId,
            PayloadJson = AuraSharedJson.Serialize(manifest),
            CreateBackup = true
        });
        return response.Success ? response.Path : "";
    }

    internal static bool RegisterManifestPathNoComponent(
        string ownerModId,
        string manifestPath,
        string baseDirectory,
        Func<object?, bool> registerResource)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            var text = File.ReadAllText(manifestPath);
            var root = string.IsNullOrWhiteSpace(baseDirectory)
                ? Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? ""
                : baseDirectory;
            return RegisterManifestJsonNoComponent(ownerModId, text, root, registerResource);
        }
        catch
        {
            return false;
        }
    }

    internal static bool RegisterManifestJsonNoComponent(
        string ownerModId,
        string manifestJson,
        string baseDirectory,
        Func<object?, bool> registerResource)
    {
        try
        {
            var manifest = AuraSharedJson.Deserialize<AuraSharedRegistryManifest>(manifestJson);
            if (manifest == null)
            {
                return false;
            }

            manifest.Normalize(ownerModId);
            if (manifest.Protocol.MinVersion > AuraSharedRuntime.CurrentProtocolVersion)
            {
                return false;
            }

            var root = baseDirectory;
            var count = 0;
            foreach (var resource in manifest.Resources)
            {
                if (resource == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(resource.SourceRoot))
                {
                    resource.SourceRoot = root;
                }

                if (string.IsNullOrWhiteSpace(resource.OwnerModId))
                {
                    resource.OwnerModId = manifest.OwnerModId;
                }

                resource.Normalize();
                if (registerResource(resource))
                {
                    count++;
                }
            }

            return count > 0;
        }
        catch
        {
            return false;
        }
    }
}
