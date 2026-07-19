using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using AuraShared.Core;
using AuraSkin.Shared.GameApi;
using AuraSkin.Shared.Infrastructure;
using AuraSkin.Shared.Models;

namespace AuraSkin.Shared.Services;

public static class SkinPackageInstaller
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, List<RegisteredSkinResource>> ActivePackages =
        new(StringComparer.OrdinalIgnoreCase);

    public static SkinPackageInstallResult InstallPackage(string ownerModId, string packageManifestPath)
    {
        lock (SyncRoot)
        {
            return InstallPackageLocked(ownerModId, packageManifestPath);
        }
    }

    public static IReadOnlyList<RegisteredSkinResource> GetActiveResources()
    {
        lock (SyncRoot)
        {
            return ActivePackages.Values
                .SelectMany(resources => resources)
                .OrderBy(resource => resource.TargetCareerId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(resource => resource.OwnerModId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(resource => resource.SkinId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private static SkinPackageInstallResult InstallPackageLocked(string ownerModId, string packageManifestPath)
    {
        var result = new SkinPackageInstallResult();
        try
        {
            var owner = (ownerModId ?? "").Trim();
            var manifestPath = SafeFullPath(packageManifestPath);
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                throw new InvalidDataException("Skin package owner or manifest path is invalid: " + packageManifestPath);
            }

            var package = JsonConvert.DeserializeObject<SkinPackageManifest>(File.ReadAllText(manifestPath));
            if (package == null
                || package.SchemaVersion != 1
                || string.IsNullOrWhiteSpace(package.PackageId)
                || package.PackageVersion < 1
                || package.Resources == null
                || package.Resources.Count == 0)
            {
                throw new InvalidDataException("Invalid skin package manifest: " + manifestPath);
            }

            package.PackageId = package.PackageId.Trim();
            package.ParticipantKind = AuraSharedParticipantKinds.Normalize(package.ParticipantKind);
            var packageDirectory = Path.GetDirectoryName(manifestPath) ?? "";
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var preparedResources = new List<PreparedSkinResource>();
            foreach (var resource in package.Resources)
            {
                var prepared = PrepareResource(packageDirectory, resource);
                if (!seenKeys.Add(prepared.ResourceKey))
                {
                    throw new InvalidDataException("Duplicate skin identity in package " + package.PackageId + ": " + prepared.ResourceKey);
                }
                preparedResources.Add(prepared);
            }

            var registration = new AuraSharedRegistrationManifestV3
            {
                OwnerModId = owner,
                ParticipantKind = package.ParticipantKind,
                PackageId = package.PackageId,
                PackageVersion = package.PackageVersion,
                Resources = preparedResources.Select(prepared => CreateDeclaration(prepared)).ToList()
            };
            var registered = AuraSharedResourceProtocol.Register(owner, registration, packageDirectory);
            result.Success = registered.Success && registered.Items.All(item => item.Success);
            result.Changed = registered.Items.Any(item => item.Changed)
                             || registered.ChangedScopeKeys.Count > 0;
            foreach (var item in registered.Items)
            {
                switch (item.Status)
                {
                    case AuraSharedRegistrationStatuses.Installed:
                        result.Installed++;
                        break;
                    case AuraSharedRegistrationStatuses.Updated:
                        result.Updated++;
                        break;
                    case "Repaired":
                        result.Repaired++;
                        break;
                    case AuraSharedRegistrationStatuses.Invalid:
                    case AuraSharedRegistrationStatuses.RejectedProtocol:
                    case AuraSharedRegistrationStatuses.Unavailable:
                        result.Conflicts++;
                        SkinLog.Error("Shared skin registration rejected for " + item.ResourceId + ": " + item.Message);
                        break;
                    default:
                        result.Deduplicated++;
                        break;
                }
            }

            if (result.Success)
            {
                ActivePackages[owner + "\n" + package.PackageId] = preparedResources
                    .Select(prepared => new RegisteredSkinResource
                    {
                        OwnerModId = owner,
                        PackageId = package.PackageId,
                        PackageVersion = package.PackageVersion,
                        TargetCareerId = prepared.TargetCareerId,
                        SkinId = prepared.SkinId,
                        CanonicalRelativePath = AuraSharedResourcePathPolicy.ResourcePath(
                            CreateDeclaration(prepared).Scope,
                            owner,
                            CreateDeclaration(prepared))
                    })
                    .ToList();
            }

            SkinLog.Info("Skin package " + package.PackageId
                         + " v" + package.PackageVersion
                         + " owner=" + owner
                         + " installed=" + result.Installed
                         + " updated=" + result.Updated
                         + " repaired=" + result.Repaired
                         + " deduplicated=" + result.Deduplicated
                         + " conflicts=" + result.Conflicts);
        }
        catch (Exception ex)
        {
            result.Success = false;
            SkinLog.Error("Skin package installation failed: " + packageManifestPath, ex);
        }

        return result;
    }

    private static PreparedSkinResource PrepareResource(string packageDirectory, SkinPackageResource? resource)
    {
        var relativeSource = AuraSharedPaths.NormalizeRelativePath(resource?.Source ?? "");
        if (string.IsNullOrWhiteSpace(relativeSource) || Path.IsPathRooted(relativeSource))
        {
            throw new InvalidDataException("Skin package resource source must be relative.");
        }

        var sourceDirectory = Path.GetFullPath(Path.Combine(
            packageDirectory,
            relativeSource.Replace('/', Path.DirectorySeparatorChar)));
        if (!AuraSharedPaths.IsInsideDirectory(sourceDirectory, packageDirectory) || !Directory.Exists(sourceDirectory))
        {
            throw new InvalidDataException("Skin package source is missing or escapes its package: " + relativeSource);
        }

        var skinManifestPath = Path.Combine(sourceDirectory, "skin.json");
        var characterDirectory = Directory.GetParent(sourceDirectory)?.FullName ?? "";
        var characterManifestPath = Path.Combine(characterDirectory, "character.json");
        if (!File.Exists(skinManifestPath) || !File.Exists(characterManifestPath))
        {
            throw new InvalidDataException("Skin package source requires skin.json and parent character.json: " + relativeSource);
        }

        var character = JsonConvert.DeserializeObject<CharacterSkinManifest>(File.ReadAllText(characterManifestPath));
        var skin = JsonConvert.DeserializeObject<SkinManifest>(File.ReadAllText(skinManifestPath));
        if (character == null || character.SchemaVersion != 2 || !character.Enabled
            || skin == null || skin.SchemaVersion != 2 || !skin.Enabled)
        {
            throw new InvalidDataException("Skin package source contains a disabled or invalid schema: " + relativeSource);
        }

        var targetCareerId = CareerConfigApi.NormalizeId(character.TargetCareerId);
        if (string.IsNullOrWhiteSpace(targetCareerId))
        {
            targetCareerId = CareerConfigApi.NormalizeId(new DirectoryInfo(characterDirectory).Name);
        }

        var skinCareerId = CareerConfigApi.NormalizeId(skin.TargetCareerId);
        if (!string.IsNullOrWhiteSpace(skinCareerId)
            && !string.Equals(skinCareerId, targetCareerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Skin targetCareerId differs from its character package: " + relativeSource);
        }

        skin.SkinId = (skin.SkinId ?? "").Trim();
        ValidatePathSegment(targetCareerId, "targetCareerId");
        ValidatePathSegment(skin.SkinId, "skinId");
        ValidateAssets(sourceDirectory, skin);

        return new PreparedSkinResource
        {
            ResourceKey = targetCareerId.Trim().ToLowerInvariant() + "::" + skin.SkinId.ToLowerInvariant(),
            TargetCareerId = targetCareerId,
            SkinId = skin.SkinId,
            RelativeSource = relativeSource
        };
    }

    private static AuraSharedResourceDeclarationV3 CreateDeclaration(PreparedSkinResource prepared)
    {
        return new AuraSharedResourceDeclarationV3
        {
            ModuleId = AuraSharedSystems.Skin,
            FeatureId = "Skin",
            ScopeType = "Role",
            ScopeId = prepared.TargetCareerId,
            ResourceId = prepared.SkinId,
            Kind = AuraSharedResourceKinds.Directory,
            Source = prepared.RelativeSource,
            LegacyPaths = new List<string>
            {
                "Skins/" + prepared.TargetCareerId + "/" + prepared.SkinId
            },
            EffectMode = AuraSharedEffectModes.Additive,
            MissingPolicy = AuraSharedMissingPolicies.Skip,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["targetCareerId"] = prepared.TargetCareerId,
                ["skinId"] = prepared.SkinId
            }
        };
    }

    private static void ValidateAssets(string sourceDirectory, SkinManifest skin)
    {
        var assets = skin.Assets ?? new SkinAssets();
        var configured = new[]
        {
            new AssetReference(assets.CareerImage, false),
            new AssetReference(assets.Avatar, false),
            new AssetReference(assets.Character, false),
            new AssetReference(assets.DollIcon, false),
            new AssetReference(assets.ChoiceIcon, false),
            new AssetReference(assets.Animation, true)
        };
        var count = 0;
        foreach (var asset in configured)
        {
            if (string.IsNullOrWhiteSpace(asset.Path))
            {
                continue;
            }
            count++;
            if (!AssetExistsInside(sourceDirectory, asset.Path, asset.Directory))
            {
                throw new InvalidDataException("Skin asset is missing or escapes its source directory: " + asset.Path);
            }
        }

        if (count == 0)
        {
            throw new InvalidDataException("Skin package must declare at least one asset.");
        }

        if (!string.IsNullOrWhiteSpace(skin.Preview)
            && !AssetExistsInside(sourceDirectory, skin.Preview, false))
        {
            throw new InvalidDataException("Skin preview is missing or escapes its source directory: " + skin.Preview);
        }
    }

    private static bool AssetExistsInside(string root, string configuredPath, bool directory)
    {
        try
        {
            var candidate = configuredPath.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(root, candidate));
            if (!AuraSharedPaths.IsInsideDirectory(fullPath, root))
            {
                return false;
            }

            return directory
                ? Directory.Exists(fullPath)
                : File.Exists(fullPath)
                  || File.Exists(fullPath + ".png")
                  || File.Exists(fullPath + ".jpg")
                  || File.Exists(fullPath + ".jpeg");
        }
        catch
        {
            return false;
        }
    }

    private static void ValidatePathSegment(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value == "."
            || value == ".."
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || value.Contains(Path.DirectorySeparatorChar.ToString())
            || value.Contains(Path.AltDirectorySeparatorChar.ToString()))
        {
            throw new InvalidDataException("Skin " + field + " is not a safe directory segment: " + value);
        }
    }

    private static string SafeFullPath(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path);
        }
        catch
        {
            return "";
        }
    }

    private sealed class PreparedSkinResource
    {
        public string ResourceKey { get; set; } = "";
        public string TargetCareerId { get; set; } = "";
        public string SkinId { get; set; } = "";
        public string RelativeSource { get; set; } = "";
    }

    public sealed class RegisteredSkinResource
    {
        public string OwnerModId { get; set; } = "";
        public string PackageId { get; set; } = "";
        public int PackageVersion { get; set; }
        public string TargetCareerId { get; set; } = "";
        public string SkinId { get; set; } = "";
        public string CanonicalRelativePath { get; set; } = "";
    }

    private sealed class AssetReference
    {
        public AssetReference(string path, bool directory)
        {
            Path = path ?? "";
            Directory = directory;
        }

        public string Path { get; }
        public bool Directory { get; }
    }
}
