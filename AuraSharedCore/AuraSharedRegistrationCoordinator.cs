using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AuraShared.Core;

public sealed class AuraSharedRegistrationCoordinator
{
    private readonly object gate = new();
    private readonly AuraSharedStorageCoordinator storage;
    private readonly AuraSharedPackageCoordinator packages;
    private readonly AuraSharedEditableResourceCoordinator editable;
    private readonly Dictionary<string, AuraSharedRegistrationManifestV3> active =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> revisions =
        new(StringComparer.OrdinalIgnoreCase);
    private long revision;

    public AuraSharedRegistrationCoordinator(
        AuraSharedStorageCoordinator storage,
        AuraSharedPackageCoordinator packages,
        string? sessionId = null)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        this.packages = packages ?? throw new ArgumentNullException(nameof(packages));
        editable = new AuraSharedEditableResourceCoordinator(this.storage.RootDirectory);
        SessionId = string.IsNullOrWhiteSpace(sessionId)
            ? DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N")
            : sessionId!.Trim();
    }

    public string SessionId { get; }

    public AuraSharedRegistrationResultV3 Register(
        string callerOwnerModId,
        AuraSharedRegistrationManifestV3 manifest,
        string baseDirectory)
    {
        lock (gate)
        {
            return RegisterNoLock(callerOwnerModId, manifest, baseDirectory);
        }
    }

    public AuraSharedResourceResolutionV3 Resolve(string requestedPath)
    {
        lock (gate)
        {
            var requested = AuraSharedPaths.NormalizeRelativePath(requestedPath);
            var candidate = active.Values
                .SelectMany(manifest => manifest.Resources.Select(resource => MatchResource(manifest, resource, requested)))
                .FirstOrDefault(match => match != null);
            if (candidate == null)
            {
                var direct = ResolveAbsolute(requested);
                return new AuraSharedResourceResolutionV3
                {
                    Success = Exists(direct),
                    Active = false,
                    UsedLegacyPath = true,
                    ResolvedPath = direct,
                    Outcome = Exists(direct) ? "LegacyUnregistered" : "Missing",
                    Fallback = "Unregistered"
                };
            }

            var canonicalPath = ResolveAbsolute(candidate!.CanonicalRequestPath);
            var preferredLegacy = candidate.PreferLegacy
                ? candidate.LegacyRequestPaths.Select(ResolveAbsolute).FirstOrDefault(Exists)
                : "";
            var resolved = string.IsNullOrWhiteSpace(preferredLegacy) ? canonicalPath : preferredLegacy;
            var usedLegacy = false;
            if (!string.IsNullOrWhiteSpace(preferredLegacy))
            {
                usedLegacy = true;
            }
            else if (!Exists(canonicalPath))
            {
                var legacy = candidate.LegacyRequestPaths
                    .Select(ResolveAbsolute)
                    .FirstOrDefault(Exists);
                if (!string.IsNullOrWhiteSpace(legacy))
                {
                    resolved = legacy;
                    usedLegacy = true;
                }
            }

            var available = Exists(resolved);
            var scopeKey = candidate.Resource.Scope.Key;
            return new AuraSharedResourceResolutionV3
            {
                Success = available,
                Active = true,
                UsedLegacyPath = usedLegacy,
                OwnerModId = candidate.Manifest.OwnerModId,
                ResourceId = candidate.Resource.ResourceId,
                ScopeKey = scopeKey,
                ResolvedPath = resolved,
                Outcome = available ? (usedLegacy ? "LegacyFallback" : "Resolved") : "Unavailable",
                Fallback = available
                    ? "None"
                    : candidate.Resource.MissingPolicy,
                Revision = revisions.TryGetValue(scopeKey, out var current) ? current : 0
            };
        }
    }

    public long GetScopeRevision(string scopeKey)
    {
        lock (gate)
        {
            return revisions.TryGetValue((scopeKey ?? "").Trim(), out var value) ? value : 0;
        }
    }

    public AuraSharedEffectiveResolutionV3 ResolveEffective(
        AuraSharedScopeKey scope,
        AuraSharedLocalOverrideV3? localOverride = null)
    {
        lock (gate)
        {
            scope ??= new AuraSharedScopeKey();
            scope.Normalize();
            return AuraSharedEffectiveResolverV3.Resolve(
                scope,
                active.Values,
                localOverride ?? TryReadUserOverride(scope)?.Override,
                (owner, resource) => Resolve(AuraSharedResourcePathPolicy.ResourcePath(resource.Scope, owner, resource)),
                GetScopeRevision(scope.Key));
        }
    }

    public AuraSharedUserOverrideDocumentV3 ReadUserOverride(AuraSharedScopeKey scope)
    {
        scope ??= new AuraSharedScopeKey();
        scope.Normalize();
        return storage.LoadRawJsonOrDefault(
            Absolute(AuraSharedResourcePathPolicy.UserOverridePath(scope)),
            new AuraSharedUserOverrideDocumentV3());
    }

    private AuraSharedUserOverrideDocumentV3? TryReadUserOverride(AuraSharedScopeKey scope)
    {
        var path = Absolute(AuraSharedResourcePathPolicy.UserOverridePath(scope));
        return File.Exists(path)
            ? storage.LoadRawJsonOrDefault(path, new AuraSharedUserOverrideDocumentV3())
            : null;
    }

    public AuraSharedUserOverrideWriteResultV3 WriteUserOverride(
        AuraSharedScopeKey scope,
        string writerId,
        AuraSharedLocalOverrideV3 localOverride,
        long expectedRevision)
    {
        lock (gate)
        {
            scope ??= new AuraSharedScopeKey();
            scope.Normalize();
            var path = Absolute(AuraSharedResourcePathPolicy.UserOverridePath(scope));
            return storage.ExecuteWrite("UserOverrideV3/" + scope.Key, () =>
            {
                var current = storage.LoadRawJsonOrDefault(path, new AuraSharedUserOverrideDocumentV3());
                if (expectedRevision >= 0 && current.Revision != expectedRevision)
                {
                    return new AuraSharedUserOverrideWriteResultV3
                    {
                        Conflict = true,
                        Revision = current.Revision,
                        Message = "User override revision conflict."
                    };
                }

                var next = new AuraSharedUserOverrideDocumentV3
                {
                    Revision = current.Revision + 1,
                    WriterId = string.IsNullOrWhiteSpace(writerId) ? "LocalUser" : writerId.Trim(),
                    UpdatedUtc = DateTime.UtcNow.ToString("O"),
                    Override = localOverride ?? new AuraSharedLocalOverrideV3()
                };
                storage.WriteRawJsonAtomic(path, next, true);
                revisions[scope.Key] = ++revision;
                WriteRuntimeIndex();
                return new AuraSharedUserOverrideWriteResultV3
                {
                    Success = true,
                    Revision = next.Revision
                };
            });
        }
    }

    public AuraSharedActiveLeaseV3[] GetActiveLeases()
    {
        lock (gate)
        {
            return active.Values
                .OrderBy(manifest => manifest.OwnerModId, StringComparer.OrdinalIgnoreCase)
                .Select(CreateLease)
                .ToArray();
        }
    }

    private AuraSharedRegistrationResultV3 RegisterNoLock(
        string callerOwnerModId,
        AuraSharedRegistrationManifestV3 manifest,
        string baseDirectory)
    {
        var result = new AuraSharedRegistrationResultV3
        {
            OwnerModId = (callerOwnerModId ?? "").Trim(),
            SessionId = SessionId
        };
        try
        {
            if (manifest == null)
            {
                result.Message = "Registration manifest is null.";
                return result;
            }

            manifest.Normalize(callerOwnerModId ?? "");
            result.OwnerModId = manifest.OwnerModId;
            if (manifest.SchemaVersion != 3)
            {
                result.Message = "Unsupported registration schemaVersion=" + manifest.SchemaVersion + ".";
                result.Items.Add(new AuraSharedRegistrationItemResultV3
                {
                    Status = AuraSharedRegistrationStatuses.RejectedProtocol,
                    Message = result.Message
                });
                return result;
            }

            if (string.IsNullOrWhiteSpace(callerOwnerModId)
                || !string.Equals((callerOwnerModId ?? "").Trim(), manifest.OwnerModId, StringComparison.OrdinalIgnoreCase))
            {
                result.Message = "Registration owner does not match caller.";
                return result;
            }

            var sourceRoot = Path.GetFullPath(baseDirectory ?? "");
            if (!Directory.Exists(sourceRoot))
            {
                result.Message = "Registration base directory is missing: " + sourceRoot;
                return result;
            }

            var registrationKey = RegistrationKey(manifest.OwnerModId, manifest.PackageId);
            var previous = active.TryGetValue(registrationKey, out var registered)
                ? AuraSharedJson.Serialize(registered)
                : "";
            foreach (var resource in manifest.Resources.Where(item => item != null))
            {
                result.Items.Add(RegisterResource(manifest, resource, sourceRoot));
            }

            WriteLayeredMetadata(manifest, result.Items);
            active[registrationKey] = manifest;
            storage.ExecuteWrite("RegistrationV3/" + manifest.OwnerModId, () =>
            {
                storage.WriteRawJsonAtomic(
                    Absolute("_Registry/V3/Owners/" + Safe(manifest.OwnerModId) + "/" + Safe(manifest.PackageId) + ".json"),
                    manifest,
                    true);
                storage.WriteRawJsonAtomic(
                    Absolute("_Runtime/Leases/" + Safe(SessionId) + "/" + Safe(manifest.OwnerModId)
                             + "/" + Safe(manifest.PackageId) + ".json"),
                    CreateLease(manifest),
                    false);
                return true;
            });

            var changedScopes = manifest.Resources.Select(item => item.Scope.Key)
                .Concat(manifest.Defaults.Select(item => item.Scope.Key))
                .Concat(registered?.Resources.Select(item => item.Scope.Key) ?? Array.Empty<string>())
                .Concat(registered?.Defaults.Select(item => item.Scope.Key) ?? Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var registrationChanged = !string.Equals(previous, AuraSharedJson.Serialize(manifest), StringComparison.Ordinal)
                                      || result.Items.Any(item => item.Changed);
            if (registrationChanged)
            {
                foreach (var scopeKey in changedScopes)
                {
                    revisions[scopeKey] = ++revision;
                    result.ChangedScopeKeys.Add(scopeKey);
                }
            }

            result.Revision = revision;
            // Registration is intentionally per declaration. One unavailable
            // optional resource must not disable unrelated modules owned by
            // the same Mod; callers inspect item outcomes for fallback.
            result.Success = true;
            result.Message = "registered=" + result.Items.Count
                             + ", unavailable=" + result.Items.Count(item => !item.Success)
                             + ", changedScopes=" + result.ChangedScopeKeys.Count;
            WriteRuntimeIndex();
            AuraSharedOperationLog.Write(storage.RootDirectory, AuraSharedOperationLog.Create(
                operationId: Guid.NewGuid().ToString("N"),
                transactionId: "",
                ownerModId: manifest.OwnerModId,
                system: "RegistrationV3",
                logicalId: manifest.PackageId,
                kind: "RegisterPackageV3",
                phase: "Completed",
                result: result.Success ? "Success" : "Partial",
                message: result.Message));
            return result;
        }
        catch (Exception ex)
        {
            result.Message = ex.Message;
            return result;
        }
    }

    private AuraSharedRegistrationItemResultV3 RegisterResource(
        AuraSharedRegistrationManifestV3 manifest,
        AuraSharedResourceDeclarationV3 resource,
        string sourceRoot)
    {
        resource.Normalize();
        var item = new AuraSharedRegistrationItemResultV3
        {
            ScopeKey = resource.Scope.Key,
            ResourceId = resource.ResourceId
        };
        if (string.IsNullOrWhiteSpace(resource.ResourceId)
            || string.IsNullOrWhiteSpace(resource.Source))
        {
            item.Status = AuraSharedRegistrationStatuses.Invalid;
            item.Message = "Resource id or source is empty.";
            return item;
        }

        var source = Path.GetFullPath(Path.Combine(
            sourceRoot,
            resource.Source.Replace('/', Path.DirectorySeparatorChar)));
        if (!AuraSharedStorageCoordinator.IsInside(source, sourceRoot))
        {
            item.Status = AuraSharedRegistrationStatuses.Invalid;
            item.Message = "Resource source escapes registration directory.";
            return item;
        }

        var canonical = AuraSharedResourcePathPolicy.ResourcePath(resource.Scope, manifest.OwnerModId, resource);
        item.CanonicalPath = canonical;
        if (!Exists(source))
        {
            item.Status = AuraSharedRegistrationStatuses.Unavailable;
            item.Message = "Resource source is missing: " + resource.Source;
            return item;
        }

        MigrateLegacyCustomization(manifest, resource, source, canonical);
        var installed = packages.Install(new AuraSharedInstallRequest
        {
            OwnerModId = manifest.OwnerModId,
            System = resource.ModuleId,
            LogicalId = resource.Scope.Key + ":" + manifest.OwnerModId + ":" + resource.ResourceId,
            PackageId = manifest.PackageId,
            PackageVersion = manifest.PackageVersion,
            Kind = resource.Kind,
            SourcePath = source,
            DestinationRelativePath = canonical,
            PreserveLocalChanges = true
        });
        item.Success = installed.Success;
        item.Changed = installed.Changed;
        item.Status = installed.Status;
        item.Message = installed.Message;
        var state = new AuraSharedResourceStateV3
        {
            SeedHash = installed.SeedHash,
            ContentHash = installed.ContentHash,
            Customized = installed.Customized,
            Status = installed.Status,
            UpdatedUtc = DateTime.UtcNow.ToString("O")
        };
        storage.WriteRawJsonAtomic(
            Absolute(AuraSharedResourcePathPolicy.ResourceStatePath(
                resource.Scope,
                manifest.OwnerModId,
                resource.ResourceId)),
            state,
            false);
        return item;
    }

    private void MigrateLegacyCustomization(
        AuraSharedRegistrationManifestV3 manifest,
        AuraSharedResourceDeclarationV3 resource,
        string packagedSource,
        string canonicalRelativePath)
    {
        var canonical = Absolute(canonicalRelativePath);
        resource.Metadata?.Remove("aura.preferLegacy");
        if (Exists(canonical)
            && string.Equals(resource.Kind, AuraSharedResourceKinds.File, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (Directory.Exists(canonical)
            && Directory.Exists(packagedSource)
            && !string.Equals(HashDirectory(canonical), HashDirectory(packagedSource), StringComparison.OrdinalIgnoreCase))
        {
            // A customization already made in the v3 location always wins over
            // an older v2 directory left on disk.
            return;
        }

        foreach (var legacyRelative in resource.LegacyPaths)
        {
            var legacy = Absolute(legacyRelative);
            if (!Exists(legacy))
            {
                continue;
            }

            var record = new AuraSharedMigrationRecordV3
            {
                Source = legacyRelative,
                Destination = canonicalRelativePath,
                RecordedUtc = DateTime.UtcNow.ToString("O")
            };
            if (File.Exists(legacy) && File.Exists(packagedSource))
            {
                var legacyHash = HashFile(legacy);
                var seedHash = HashFile(packagedSource);
                record.SourceHash = legacyHash;
                if (!string.Equals(legacyHash, seedHash, StringComparison.OrdinalIgnoreCase))
                {
                    var migrated = editable.Seed(new AuraSharedEditableResourceRequest
                    {
                        OwnerModId = manifest.OwnerModId,
                        System = resource.ModuleId,
                        LogicalId = "migration." + resource.ResourceId,
                        SourcePath = legacy,
                        DestinationRelativePath = canonicalRelativePath
                    });
                    record.Classification = "UserCustomized";
                    record.Result = migrated.Success ? "Migrated" : "PreservedLegacy";
                }
                else
                {
                    record.Classification = "ExactDuplicate";
                    record.Result = "CanonicalSeedPreferred";
                }
            }
            else if (Directory.Exists(legacy) && Directory.Exists(packagedSource))
            {
                var legacyHash = HashDirectory(legacy);
                var seedHash = HashDirectory(packagedSource);
                record.SourceHash = legacyHash;
                if (!string.Equals(legacyHash, seedHash, StringComparison.OrdinalIgnoreCase))
                {
                    resource.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    resource.Metadata["aura.preferLegacy"] = legacyRelative;
                    record.Classification = "UserCustomizedDirectory";
                    record.Result = "PreservedAndPreferred";
                }
                else
                {
                    record.Classification = "ExactDuplicateDirectory";
                    record.Result = "CanonicalSeedPreferred";
                }
            }
            else
            {
                record.Classification = "LegacyKindMismatch";
                record.Result = "PreservedLegacy";
            }

            AppendMigrationRecord(manifest.OwnerModId, record);
            break;
        }
    }

    private void WriteLayeredMetadata(
        AuraSharedRegistrationManifestV3 manifest,
        IReadOnlyList<AuraSharedRegistrationItemResultV3> items)
    {
        foreach (var module in manifest.Resources.Select(item => item.ModuleId)
                     .Concat(manifest.Defaults.Select(item => item.ModuleId))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            storage.WriteRawJsonAtomic(Absolute(AuraSharedResourcePathPolicy.ModuleManifestPath(module)), new
            {
                schemaVersion = 3,
                moduleId = module,
                layout = "module/scopeType/scopeId/featureId/ownerModId/resourceId",
                readPolicy = "onDemand"
            }, false);
        }

        var scopes = manifest.Resources.Select(item => item.Scope)
            .Concat(manifest.Defaults.Select(item => item.Scope))
            .GroupBy(scope => scope.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
        foreach (var scope in scopes)
        {
            storage.WriteRawJsonAtomic(Absolute(AuraSharedResourcePathPolicy.FeatureManifestPath(scope)), new
            {
                schemaVersion = 3,
                scope,
                effectModes = manifest.Resources.Where(item => item.Scope.Equals(scope))
                    .Select(item => item.EffectMode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                missingPolicies = manifest.Resources.Where(item => item.Scope.Equals(scope))
                    .Select(item => item.MissingPolicy).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            }, false);

            var defaults = manifest.Defaults.Where(item => item.Scope.Equals(scope)).ToArray();
            storage.WriteRawJsonAtomic(
                Absolute(AuraSharedResourcePathPolicy.ProviderDefaultsPath(scope, manifest.OwnerModId)),
                new
                {
                    schemaVersion = 3,
                    ownerModId = manifest.OwnerModId,
                    participantKind = manifest.ParticipantKind,
                    profiles = defaults
                },
                false);
        }

        foreach (var resource in manifest.Resources)
        {
            var item = items.FirstOrDefault(value =>
                string.Equals(value.ScopeKey, resource.Scope.Key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(value.ResourceId, resource.ResourceId, StringComparison.OrdinalIgnoreCase));
            storage.WriteRawJsonAtomic(
                Absolute(AuraSharedResourcePathPolicy.ResourceManifestPath(
                    resource.Scope,
                    manifest.OwnerModId,
                    resource.ResourceId)),
                new
                {
                    schemaVersion = 3,
                    ownerModId = manifest.OwnerModId,
                    resource,
                    canonicalPath = item?.CanonicalPath ?? ""
                },
                false);
        }
    }

    private void WriteRuntimeIndex()
    {
        storage.WriteRawJsonAtomic(Absolute("_Runtime/Index/resources.v3.json"), new
        {
            schemaVersion = 3,
            sessionId = SessionId,
            revision,
            owners = active.Values.Select(value => value.OwnerModId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            packages = active.Values.Select(value => value.OwnerModId + ":" + value.PackageId)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            scopeRevisions = revisions
        }, false);
    }

    private void AppendMigrationRecord(string ownerModId, AuraSharedMigrationRecordV3 record)
    {
        var path = Absolute("_Migration/V2ToV3/" + Safe(ownerModId) + "/journal.json");
        var records = storage.LoadRawJsonOrDefault(path, new List<AuraSharedMigrationRecordV3>());
        if (!records.Any(existing =>
                string.Equals(existing.Source, record.Source, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Destination, record.Destination, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.SourceHash, record.SourceHash, StringComparison.OrdinalIgnoreCase)))
        {
            records.Add(record);
            storage.WriteRawJsonAtomic(path, records, true);
        }
    }

    private AuraSharedActiveLeaseV3 CreateLease(AuraSharedRegistrationManifestV3 manifest)
    {
        return new AuraSharedActiveLeaseV3
        {
            SessionId = SessionId,
            OwnerModId = manifest.OwnerModId,
            ParticipantKind = manifest.ParticipantKind,
            PackageId = manifest.PackageId,
            PackageVersion = manifest.PackageVersion,
            RegisteredUtc = DateTime.UtcNow.ToString("O"),
            ScopeKeys = manifest.Resources.Select(item => item.Scope.Key)
                .Concat(manifest.Defaults.Select(item => item.Scope.Key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static ResourcePathMatch? MatchResource(
        AuraSharedRegistrationManifestV3 manifest,
        AuraSharedResourceDeclarationV3 resource,
        string requested)
    {
        var canonical = AuraSharedResourcePathPolicy.ResourcePath(resource.Scope, manifest.OwnerModId, resource);
        if (string.Equals(requested, canonical, StringComparison.OrdinalIgnoreCase))
        {
            return new ResourcePathMatch(manifest, resource, canonical, resource.LegacyPaths, PreferLegacy(resource));
        }

        var directory = string.Equals(resource.Kind, AuraSharedResourceKinds.Directory, StringComparison.OrdinalIgnoreCase);
        if (directory && requested.StartsWith(canonical.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = requested.Substring(canonical.TrimEnd('/').Length);
            return new ResourcePathMatch(
                manifest,
                resource,
                canonical.TrimEnd('/') + suffix,
                resource.LegacyPaths.Select(path => path.TrimEnd('/') + suffix),
                PreferLegacy(resource));
        }

        foreach (var legacy in resource.LegacyPaths)
        {
            if (string.Equals(requested, legacy, StringComparison.OrdinalIgnoreCase))
            {
                return new ResourcePathMatch(manifest, resource, canonical, new[] { legacy }, PreferLegacy(resource));
            }

            if (directory && requested.StartsWith(legacy.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = requested.Substring(legacy.TrimEnd('/').Length);
                return new ResourcePathMatch(
                    manifest,
                    resource,
                    canonical.TrimEnd('/') + suffix,
                    new[] { legacy.TrimEnd('/') + suffix },
                    PreferLegacy(resource));
            }
        }

        return null;
    }

    private string ResolveAbsolute(string relativeOrAbsolute)
    {
        var normalized = AuraSharedPaths.NormalizeRelativePath(relativeOrAbsolute);
        return Path.IsPathRooted(relativeOrAbsolute ?? "")
            ? Path.GetFullPath(relativeOrAbsolute ?? "")
            : Absolute(normalized);
    }

    private string Absolute(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(
            storage.RootDirectory,
            (relative ?? "").Replace('/', Path.DirectorySeparatorChar)));
        if (!AuraSharedStorageCoordinator.IsInside(path, storage.RootDirectory))
        {
            throw new InvalidDataException("Shared protocol path escapes AuraShared root: " + relative);
        }
        return path;
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static string Safe(string value) => AuraSharedPaths.SafeSegment(value, "unknown");

    private static string RegistrationKey(string ownerModId, string packageId)
    {
        return (ownerModId ?? "").Trim() + "\n" + (packageId ?? "").Trim();
    }

    private static bool PreferLegacy(AuraSharedResourceDeclarationV3 resource)
    {
        return resource.Metadata != null
               && resource.Metadata.ContainsKey("aura.preferLegacy");
    }

    private static string HashFile(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")));
    }

    private static string HashDirectory(string path)
    {
        var entries = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Select(file => AuraSharedStorageCoordinator.MakeRelative(path, file).Replace('\\', '/').ToLowerInvariant()
                            + "|" + new FileInfo(file).Length
                            + "|" + HashFile(file).ToLowerInvariant())
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", entries)))
            .Select(value => value.ToString("x2")));
    }

    private sealed class ResourcePathMatch
    {
        public ResourcePathMatch(
            AuraSharedRegistrationManifestV3 manifest,
            AuraSharedResourceDeclarationV3 resource,
            string canonicalRequestPath,
            IEnumerable<string> legacyRequestPaths,
            bool preferLegacy)
        {
            Manifest = manifest;
            Resource = resource;
            CanonicalRequestPath = canonicalRequestPath;
            LegacyRequestPaths = legacyRequestPaths.ToArray();
            PreferLegacy = preferLegacy;
        }

        public AuraSharedRegistrationManifestV3 Manifest { get; }
        public AuraSharedResourceDeclarationV3 Resource { get; }
        public string CanonicalRequestPath { get; }
        public string[] LegacyRequestPaths { get; }
        public bool PreferLegacy { get; }
    }
}
