using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AuraShared.Core;

public sealed class AuraSharedRegistrationCoordinator
{
    private readonly object gate = new();
    private readonly AuraSharedStorageCoordinator storage;
    private readonly AuraSharedPackageCoordinator packages;
    private readonly AuraSharedEditableResourceCoordinator editable;
    private readonly Dictionary<string, AuraSharedRegistrationManifestV4> active =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> activeAvailability =
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

    public AuraSharedRegistrationResultV4 Register(
        string callerOwnerModId,
        AuraSharedRegistrationManifestV4 manifest,
        string baseDirectory)
    {
        lock (gate)
        {
            return RegisterNoLock(callerOwnerModId, manifest, baseDirectory);
        }
    }

    public AuraSharedRegistrationItemResultV4 UpsertManualResource(
        string callerOwnerModId,
        AuraSharedManualResourceRequestV4 request)
    {
        lock (gate)
        {
            var item = new AuraSharedRegistrationItemResultV4();
            var owner = (callerOwnerModId ?? "").Trim();
            if (request == null || !string.Equals(owner, request.OwnerModId?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                item.Status = AuraSharedRegistrationStatuses.Invalid;
                item.Message = "Manual resource owner does not match caller.";
                return item;
            }
            var resource = request.Resource ?? new AuraSharedResourceDeclarationV4();
            resource.Normalize();
            resource.OriginKind = AuraSharedOriginKinds.UserManual;
            resource.WriterId = "LocalUser";
            resource.Archived = request.Archive;
            item.ScopeKey = resource.Scope.Key;
            item.ResourceId = resource.ResourceId;
            item.CanonicalPath = AuraSharedResourcePathPolicy.ResourcePath(resource.Scope, owner, resource);
            if (!HasCanonicalIdentitySegments(owner, resource)
                || string.IsNullOrWhiteSpace(resource.ScopeOwnerModId)
                || !string.Equals(request.WriterId, "LocalUser", StringComparison.Ordinal))
            {
                item.Status = AuraSharedRegistrationStatuses.Invalid;
                item.Message = "Manual v4 identity, scope owner, or writer is invalid.";
                return item;
            }
            if (!request.Archive)
            {
                var source = Path.GetFullPath(request.SourcePath ?? "");
                var isDirectory = string.Equals(resource.Kind, AuraSharedResourceKinds.Directory, StringComparison.OrdinalIgnoreCase);
                if (isDirectory ? !Directory.Exists(source) : !File.Exists(source))
                {
                    item.Status = AuraSharedRegistrationStatuses.Unavailable;
                    item.Message = "Manual source is missing.";
                    return item;
                }
                if (isDirectory)
                {
                    if (!SamePath(source, Absolute(item.CanonicalPath)))
                    {
                        item.Status = AuraSharedRegistrationStatuses.Invalid;
                        item.Message = "Manual directories must already be inside their canonical v4 location.";
                        return item;
                    }
                    item.Changed = true;
                }
                else
                {
                    var seeded = editable.Seed(new AuraSharedEditableResourceRequest
                    {
                        OwnerModId = owner,
                        System = resource.ModuleId,
                        LogicalId = resource.Scope.Key + ":" + resource.ResourceId,
                        SourcePath = source,
                        DestinationRelativePath = item.CanonicalPath,
                        ForceReset = true
                    });
                    if (!seeded.Success)
                    {
                        item.Status = AuraSharedRegistrationStatuses.Invalid;
                        item.Message = seeded.Message;
                        return item;
                    }
                    item.Changed = seeded.Changed;
                }
            }
            resource.Source = item.CanonicalPath;
            var packageId = owner + ".LocalResources";
            var persisted = ReadPersistedManifests().FirstOrDefault(manifest =>
                string.Equals(manifest.OwnerModId, owner, StringComparison.OrdinalIgnoreCase)
                && string.Equals(manifest.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
                ?? new AuraSharedRegistrationManifestV4
                {
                    OwnerModId = owner,
                    ParticipantKind = AuraSharedParticipantKinds.Tool,
                    PackageSourceKind = AuraSharedPackageSourceKinds.LocalPackage,
                    PackageId = packageId
                };
            persisted.Resources.RemoveAll(existing => string.Equals(
                ResourceIdentity(owner, existing), ResourceIdentity(owner, resource), StringComparison.OrdinalIgnoreCase));
            persisted.Resources.Add(resource);
            persisted.PackageVersion++;
            var key = RegistrationKey(owner, packageId);
            active[key] = persisted;
            activeAvailability[AvailabilityKey(key, resource)] = !request.Archive && Exists(Absolute(item.CanonicalPath));
            storage.WriteRawJsonAtomic(
                Absolute("_Registry/V4/Owners/" + Safe(owner) + "/" + Safe(packageId) + ".json"),
                persisted,
                true);
            item.Success = true;
            item.Status = request.Archive ? "Archived" : AuraSharedRegistrationStatuses.Updated;
            revisions[resource.Scope.Key] = ++revision;
            WriteLayeredMetadata(persisted, new[] { item });
            WriteRuntimeIndex();
            return item;
        }
    }

    public int ActivateLocalPackages(string callerOwnerModId)
    {
        lock (gate)
        {
            var owner = (callerOwnerModId ?? "").Trim();
            var activated = 0;
            foreach (var manifest in ReadPersistedManifests().Where(manifest =>
                         string.Equals(manifest.OwnerModId, owner, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(manifest.PackageSourceKind, AuraSharedPackageSourceKinds.LocalPackage, StringComparison.Ordinal)))
            {
                var key = RegistrationKey(owner, manifest.PackageId);
                active[key] = manifest;
                foreach (var resource in manifest.Resources)
                {
                    activeAvailability[AvailabilityKey(key, resource)] = !resource.Archived
                        && Exists(Absolute(AuraSharedResourcePathPolicy.ResourcePath(resource.Scope, owner, resource)));
                    revisions[resource.Scope.Key] = ++revision;
                }
                activated++;
            }
            if (activated > 0) WriteRuntimeIndex();
            return activated;
        }
    }

    public AuraSharedResourceResolutionV4 Resolve(string requestedPath)
    {
        lock (gate)
        {
            var requested = AuraSharedPaths.NormalizeRelativePath(requestedPath);
            var candidate = active.Values
                .SelectMany(manifest => manifest.Resources.Select(resource => MatchResource(manifest, resource, requested)))
                .FirstOrDefault(match => match != null);
            if (candidate == null)
            {
                return new AuraSharedResourceResolutionV4
                {
                    Success = false,
                    Active = false,
                    ResolvedPath = ResolveAbsolute(requested),
                    Outcome = "Unregistered",
                    Fallback = "Unregistered"
                };
            }

            var canonicalPath = ResolveAbsolute(candidate!.CanonicalRequestPath);
            var available = Exists(canonicalPath);
            var scopeKey = candidate.Resource.Scope.Key;
            return new AuraSharedResourceResolutionV4
            {
                Success = available,
                Active = true,
                OwnerModId = candidate.Manifest.OwnerModId,
                ResourceId = candidate.Resource.ResourceId,
                ScopeKey = scopeKey,
                ResolvedPath = canonicalPath,
                Outcome = available ? "Resolved" : "Unavailable",
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

    public AuraSharedEffectiveResolutionV4 ResolveEffective(
        AuraSharedScopeKey scope,
        AuraSharedLocalOverrideV4? localOverride = null)
    {
        lock (gate)
        {
            scope ??= new AuraSharedScopeKey();
            scope.Normalize();
            return AuraSharedEffectiveResolverV4.Resolve(
                scope,
                active.Values,
                localOverride ?? TryReadUserOverride(scope)?.Override,
                (owner, resource) => Resolve(AuraSharedResourcePathPolicy.ResourcePath(resource.Scope, owner, resource)),
                GetScopeRevision(scope.Key));
        }
    }

    public AuraSharedUserOverrideDocumentV4 ReadUserOverride(AuraSharedScopeKey scope)
    {
        lock (gate)
        {
            scope ??= new AuraSharedScopeKey();
            scope.Normalize();
            return storage.LoadRawJsonOrDefault(
                Absolute(AuraSharedResourcePathPolicy.UserOverridePath(scope)),
                new AuraSharedUserOverrideDocumentV4());
        }
    }

    private AuraSharedUserOverrideDocumentV4? TryReadUserOverride(AuraSharedScopeKey scope)
    {
        var path = Absolute(AuraSharedResourcePathPolicy.UserOverridePath(scope));
        return File.Exists(path)
            ? storage.LoadRawJsonOrDefault(path, new AuraSharedUserOverrideDocumentV4())
            : null;
    }

    public AuraSharedUserOverrideWriteResultV4 WriteUserOverride(
        AuraSharedScopeKey scope,
        string writerId,
        AuraSharedLocalOverrideV4 localOverride,
        long expectedRevision)
    {
        lock (gate)
        {
            scope ??= new AuraSharedScopeKey();
            scope.Normalize();
            var path = Absolute(AuraSharedResourcePathPolicy.UserOverridePath(scope));
            return storage.ExecuteWrite("UserOverrideV4/" + scope.Key, () =>
            {
                var current = storage.LoadRawJsonOrDefault(path, new AuraSharedUserOverrideDocumentV4());
                if (expectedRevision >= 0 && current.Revision != expectedRevision)
                {
                    return new AuraSharedUserOverrideWriteResultV4
                    {
                        Conflict = true,
                        Revision = current.Revision,
                        Message = "User override revision conflict."
                    };
                }

                localOverride ??= new AuraSharedLocalOverrideV4();
                localOverride.Normalize();
                var next = new AuraSharedUserOverrideDocumentV4
                {
                    Revision = current.Revision + 1,
                    WriterId = string.IsNullOrWhiteSpace(writerId) ? "LocalUser" : writerId.Trim(),
                    UpdatedUtc = DateTime.UtcNow.ToString("O"),
                    Override = localOverride
                };
                storage.WriteRawJsonAtomic(path, next, true);
                revisions[scope.Key] = ++revision;
                WriteRuntimeIndex();
                return new AuraSharedUserOverrideWriteResultV4
                {
                    Success = true,
                    Revision = next.Revision
                };
            });
        }
    }

    public AuraSharedActiveLeaseV4[] GetActiveLeases()
    {
        lock (gate)
        {
            return active.Values
                .OrderBy(manifest => manifest.OwnerModId, StringComparer.OrdinalIgnoreCase)
                .Select(CreateLease)
                .ToArray();
        }
    }

    public AuraSharedCatalogSnapshotV4 QueryCatalog(AuraSharedCatalogQueryV4? query)
    {
        lock (gate)
        {
            query ??= new AuraSharedCatalogQueryV4();
            query.Normalize();
            var activeKeys = new HashSet<string>(active.Keys, StringComparer.OrdinalIgnoreCase);
            var manifests = active.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            if (query.Visibility != AuraSharedCatalogVisibilities.Active)
            {
                foreach (var manifest in ReadPersistedManifests())
                {
                    var key = RegistrationKey(manifest.OwnerModId, manifest.PackageId);
                    if (!manifests.ContainsKey(key))
                    {
                        manifests[key] = manifest;
                    }
                }
            }

            var entries = manifests
                .SelectMany(pair => pair.Value.Resources.Select(resource => CreateCatalogEntry(
                    pair.Value,
                    resource,
                    activeKeys.Contains(pair.Key),
                    IsActiveResourceAvailable(pair.Key, resource))))
                .Where(entry => MatchesCatalogQuery(entry, query))
                .Where(entry => query.Visibility == AuraSharedCatalogVisibilities.All
                                || (query.Visibility == AuraSharedCatalogVisibilities.History
                                    ? entry.HistoryReasons.Count > 0
                                    : entry.Active && entry.Available && !entry.Resource.Archived && !entry.Resource.Retired))
                .OrderBy(entry => entry.Resource.ModuleId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Resource.ScopeType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Resource.ScopeId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Resource.FeatureId, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(entry => entry.Resource.Priority)
                .ThenBy(entry => entry.OwnerModId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Resource.ResourceId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new AuraSharedCatalogSnapshotV4
            {
                SessionId = SessionId,
                Revision = revision,
                Entries = entries
            };
        }
    }

    private AuraSharedRegistrationResultV4 RegisterNoLock(
        string callerOwnerModId,
        AuraSharedRegistrationManifestV4 manifest,
        string baseDirectory)
    {
        var result = new AuraSharedRegistrationResultV4
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
            if (manifest.SchemaVersion != AuraSharedResourceSchemaVersions.Current)
            {
                result.Message = "Unsupported registration schemaVersion=" + manifest.SchemaVersion + ".";
                result.Status = AuraSharedRegistrationStatuses.UnsupportedSchema;
                result.Items.Add(new AuraSharedRegistrationItemResultV4
                {
                    Status = AuraSharedRegistrationStatuses.UnsupportedSchema,
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
            var registrationIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var resource in manifest.Resources.Where(item => item != null))
            {
                var identity = ResourceIdentity(manifest.OwnerModId, resource);
                AuraSharedRegistrationItemResultV4 item;
                if (!HasCanonicalIdentitySegments(manifest.OwnerModId, resource))
                {
                    item = InvalidIdentityResult(resource, "Resource identity contains a non-canonical path segment: " + identity);
                }
                else if (!registrationIdentities.Add(identity))
                {
                    item = InvalidIdentityResult(resource, "Duplicate qualified resource identity in registration: " + identity);
                }
                else if (HasActiveIdentityConflict(registrationKey, manifest.OwnerModId, resource))
                {
                    item = InvalidIdentityResult(resource, "Qualified resource identity is already owned by another active package: " + identity);
                }
                else
                {
                    item = ValidateResource(manifest, resource, sourceRoot);
                }
                result.Items.Add(item);
            }

            if (result.Items.Any(item => !item.Success))
            {
                result.Status = AuraSharedRegistrationStatuses.Invalid;
                result.Message = "v4 package rejected atomically; every declaration must be valid and available.";
                return result;
            }

            result.Items.Clear();
            foreach (var resource in manifest.Resources.Where(item => item != null))
            {
                var item = RegisterResource(manifest, resource, sourceRoot);
                result.Items.Add(item);
                activeAvailability[AvailabilityKey(registrationKey, resource)] = item.Success;
            }
            if (result.Items.Any(item => !item.Success))
            {
                result.Status = AuraSharedRegistrationStatuses.Invalid;
                result.Message = "v4 package installation failed; package was not activated.";
                return result;
            }

            var activeManifest = CloneWithResources(manifest, manifest.Resources);
            active[registrationKey] = activeManifest;
            PersistRetiredResources(manifest, registered);
            WriteLayeredMetadata(activeManifest, result.Items);
            storage.ExecuteWrite("RegistrationV4/" + manifest.OwnerModId, () =>
            {
                storage.WriteRawJsonAtomic(
                    Absolute("_Registry/V4/Owners/" + Safe(manifest.OwnerModId) + "/" + Safe(manifest.PackageId) + ".json"),
                    manifest,
                    true);
                storage.WriteRawJsonAtomic(
                    Absolute("_Runtime/Leases/" + Safe(SessionId) + "/" + Safe(manifest.OwnerModId)
                             + "/" + Safe(manifest.PackageId) + ".json"),
                    CreateLease(activeManifest),
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
            result.Success = true;
            result.Status = AuraSharedRegistrationStatuses.Installed;
            result.Message = "registered=" + result.Items.Count
                             + ", unavailable=" + result.Items.Count(item => !item.Success)
                             + ", changedScopes=" + result.ChangedScopeKeys.Count;
            WriteRuntimeIndex();
            AuraSharedOperationLog.Write(storage.RootDirectory, AuraSharedOperationLog.Create(
                operationId: Guid.NewGuid().ToString("N"),
                transactionId: "",
                ownerModId: manifest.OwnerModId,
                system: "RegistrationV4",
                logicalId: manifest.PackageId,
                kind: "RegisterPackageV4",
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

    private void PersistRetiredResources(
        AuraSharedRegistrationManifestV4 current,
        AuraSharedRegistrationManifestV4? activePrevious)
    {
        var ownerPath = Absolute("_Registry/V4/Owners/" + Safe(current.OwnerModId) + "/" + Safe(current.PackageId) + ".json");
        var previous = activePrevious;
        if (previous == null && File.Exists(ownerPath))
        {
            previous = storage.LoadRawJsonOrDefault(ownerPath, new AuraSharedRegistrationManifestV4());
            previous.Normalize(current.OwnerModId);
        }
        if (previous == null || previous.SchemaVersion != AuraSharedResourceSchemaVersions.Current) return;
        var currentIds = current.Resources.Select(resource => ResourceIdentity(current.OwnerModId, resource))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retired = previous.Resources.Where(resource => !currentIds.Contains(ResourceIdentity(current.OwnerModId, resource)))
            .Select(resource => AuraSharedJson.Deserialize<AuraSharedResourceDeclarationV4>(AuraSharedJson.Serialize(resource)))
            .Where(resource => resource != null)
            .Select(resource =>
            {
                resource!.Retired = true;
                return resource;
            })
            .ToList();
        if (retired.Count == 0) return;
        var historyManifest = CloneWithResources(previous, retired);
        historyManifest.PackageId = current.PackageId + ".Retired." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        storage.WriteRawJsonAtomic(
            Absolute("_Registry/V4/History/Owners/" + Safe(current.OwnerModId) + "/" + Safe(historyManifest.PackageId) + ".json"),
            historyManifest,
            true);
    }

    private IEnumerable<AuraSharedRegistrationManifestV4> ReadPersistedManifests()
    {
        var roots = new[]
        {
            Absolute("_Registry/V4/Owners"),
            Absolute("_Registry/V4/History/Owners")
        }.Where(Directory.Exists).ToArray();
        if (roots.Length == 0)
        {
            return Array.Empty<AuraSharedRegistrationManifestV4>();
        }

        var manifests = new List<AuraSharedRegistrationManifestV4>();
        foreach (var path in roots.SelectMany(root => Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
                     .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var manifest = AuraSharedJson.Deserialize<AuraSharedRegistrationManifestV4>(File.ReadAllText(path));
                if (manifest == null)
                {
                    continue;
                }

                manifest.Normalize(manifest.OwnerModId);
                if (manifest.SchemaVersion == AuraSharedResourceSchemaVersions.Current
                    && !string.IsNullOrWhiteSpace(manifest.OwnerModId))
                {
                    manifests.Add(manifest);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AuraShared.Catalog] Ignored persisted registration " + path + ": " + ex.Message);
            }
        }

        return manifests;
    }

    private AuraSharedCatalogEntryV4 CreateCatalogEntry(
        AuraSharedRegistrationManifestV4 manifest,
        AuraSharedResourceDeclarationV4 resource,
        bool isActive,
        bool activeResourceAvailable)
    {
        var canonical = AuraSharedResourcePathPolicy.ResourcePath(resource.Scope, manifest.OwnerModId, resource);
        var available = isActive ? activeResourceAvailable : Exists(Absolute(canonical));
        var historyReasons = new List<string>();
        if (!isActive) historyReasons.Add(AuraSharedHistoryReasons.InactiveOwner);
        if (!available) historyReasons.Add(AuraSharedHistoryReasons.Unavailable);
        if (resource.Archived) historyReasons.Add(AuraSharedHistoryReasons.Archived);
        if (resource.Retired) historyReasons.Add(AuraSharedHistoryReasons.Retired);
        var user = TryReadUserOverride(resource.Scope)?.Override;
        var configuredEnabled = resource.DefaultEnabled;
        if (user?.ResourceOverrides != null)
        {
            var qualified = resource.ModuleId + "/" + resource.ScopeType + "/" + resource.ScopeId + "/"
                            + resource.FeatureId + "/" + manifest.OwnerModId + "/" + resource.ResourceId;
            var shortId = manifest.OwnerModId + ":" + resource.ResourceId;
            if (user.ResourceOverrides.TryGetValue(qualified, out var qualifiedValue)) configuredEnabled = qualifiedValue;
            else if (user.ResourceOverrides.TryGetValue(shortId, out var shortValue)) configuredEnabled = shortValue;
        }
        return new AuraSharedCatalogEntryV4
        {
            Registered = true,
            Active = isActive,
            Available = available,
            Applicable = true,
            ConfiguredEnabled = configuredEnabled,
            EffectiveEnabled = isActive && available && configuredEnabled && user?.Enabled != false
                               && !resource.Archived && !resource.Retired,
            HistoryReasons = historyReasons,
            OwnerModId = manifest.OwnerModId,
            ParticipantKind = manifest.ParticipantKind,
            PackageSourceKind = manifest.PackageSourceKind,
            PackageId = manifest.PackageId,
            PackageVersion = manifest.PackageVersion,
            Resource = resource,
            Defaults = manifest.Defaults
                .Where(profile => profile.Scope.Equals(resource.Scope))
                .Where(profile => string.IsNullOrWhiteSpace(profile.ResourceOwnerModId)
                                  || string.Equals(profile.ResourceOwnerModId, manifest.OwnerModId, StringComparison.OrdinalIgnoreCase))
                .Where(profile => string.IsNullOrWhiteSpace(profile.ResourceId)
                                  || string.Equals(profile.ResourceId, resource.ResourceId, StringComparison.OrdinalIgnoreCase))
                .ToList(),
            CanonicalPath = canonical
        };
    }

    private static bool MatchesCatalogQuery(AuraSharedCatalogEntryV4 entry, AuraSharedCatalogQueryV4 query)
    {
        return MatchesFilter(entry.Resource.ModuleId, query.ModuleId)
               && MatchesFilter(entry.Resource.FeatureId, query.FeatureId)
               && MatchesFilter(entry.Resource.ScopeType, query.ScopeType)
               && MatchesFilter(entry.Resource.ScopeId, query.ScopeId)
               && MatchesFilter(entry.OwnerModId, query.OwnerModId);
    }

    private static bool MatchesFilter(string value, string filter)
    {
        return string.IsNullOrWhiteSpace(filter)
               || string.Equals(value, filter, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsActiveResourceAvailable(string registrationKey, AuraSharedResourceDeclarationV4 resource)
    {
        return activeAvailability.TryGetValue(AvailabilityKey(registrationKey, resource), out var available)
               && available;
    }

    private static string AvailabilityKey(string registrationKey, AuraSharedResourceDeclarationV4 resource)
    {
        return registrationKey + "\n" + resource.Scope.Key + "\n" + resource.ResourceId;
    }

    private bool HasActiveIdentityConflict(
        string registrationKey,
        string ownerModId,
        AuraSharedResourceDeclarationV4 resource)
    {
        var identity = ResourceIdentity(ownerModId, resource);
        return active
            .Where(pair => !string.Equals(pair.Key, registrationKey, StringComparison.OrdinalIgnoreCase))
            .SelectMany(pair => pair.Value.Resources.Select(candidate => new
            {
                pair.Value.OwnerModId,
                Resource = candidate
            }))
            .Any(candidate => string.Equals(
                ResourceIdentity(candidate.OwnerModId, candidate.Resource),
                identity,
                StringComparison.OrdinalIgnoreCase));
    }

    private static AuraSharedRegistrationItemResultV4 InvalidIdentityResult(
        AuraSharedResourceDeclarationV4 resource,
        string message)
    {
        return new AuraSharedRegistrationItemResultV4
        {
            ScopeKey = resource.Scope.Key,
            ResourceId = resource.ResourceId,
            Status = AuraSharedRegistrationStatuses.Invalid,
            Message = message
        };
    }

    private static string ResourceIdentity(string ownerModId, AuraSharedResourceDeclarationV4 resource)
    {
        return resource.Scope.Key + ":" + (ownerModId ?? "").Trim() + ":" + resource.ResourceId;
    }

    private static bool HasCanonicalIdentitySegments(string ownerModId, AuraSharedResourceDeclarationV4 resource)
    {
        return IsCanonicalIdentitySegment(resource.ModuleId)
               && IsCanonicalIdentitySegment(resource.ScopeType)
               && IsCanonicalIdentitySegment(resource.ScopeId)
               && IsCanonicalIdentitySegment(resource.FeatureId)
               && IsCanonicalIdentitySegment(ownerModId)
               && IsCanonicalIdentitySegment(resource.ResourceId);
    }

    private static bool IsCanonicalIdentitySegment(string value)
    {
        var normalized = (value ?? "").Trim();
        return normalized.Length > 0
               && normalized != "."
               && normalized != ".."
               && string.Equals(AuraSharedPaths.SafeSegment(normalized, "invalid"), normalized, StringComparison.Ordinal);
    }

    private static AuraSharedRegistrationManifestV4 CloneWithResources(
        AuraSharedRegistrationManifestV4 manifest,
        IEnumerable<AuraSharedResourceDeclarationV4> resources)
    {
        return new AuraSharedRegistrationManifestV4
        {
            SchemaVersion = manifest.SchemaVersion,
            OwnerModId = manifest.OwnerModId,
            ParticipantKind = manifest.ParticipantKind,
            PackageSourceKind = manifest.PackageSourceKind,
            PackageId = manifest.PackageId,
            PackageVersion = manifest.PackageVersion,
            Resources = resources.ToList(),
            Defaults = manifest.Defaults.ToList()
        };
    }

    private AuraSharedRegistrationItemResultV4 ValidateResource(
        AuraSharedRegistrationManifestV4 manifest,
        AuraSharedResourceDeclarationV4 resource,
        string sourceRoot)
    {
        resource.Normalize();
        var item = new AuraSharedRegistrationItemResultV4
        {
            ScopeKey = resource.Scope.Key,
            ResourceId = resource.ResourceId,
            CanonicalPath = AuraSharedResourcePathPolicy.ResourcePath(resource.Scope, manifest.OwnerModId, resource),
            Success = true,
            Status = "Validated"
        };
        if (string.IsNullOrWhiteSpace(resource.ResourceId) || string.IsNullOrWhiteSpace(resource.Source))
        {
            item.Success = false;
            item.Status = AuraSharedRegistrationStatuses.Invalid;
            item.Message = "Resource id or source is empty.";
            return item;
        }
        if (!AuraSharedOriginKinds.IsValid(resource.OriginKind)
            || string.IsNullOrWhiteSpace(resource.WriterId)
            || string.IsNullOrWhiteSpace(resource.ScopeOwnerModId))
        {
            item.Success = false;
            item.Status = AuraSharedRegistrationStatuses.Invalid;
            item.Message = "v4 resource requires originKind, writerId, and scopeOwnerModId.";
            return item;
        }
        if (resource.OriginKind == AuraSharedOriginKinds.UserManual
            && (!string.Equals(manifest.PackageSourceKind, AuraSharedPackageSourceKinds.LocalPackage, StringComparison.Ordinal)
                || !string.Equals(manifest.ParticipantKind, AuraSharedParticipantKinds.Tool, StringComparison.Ordinal)
                || !string.Equals(resource.WriterId, "LocalUser", StringComparison.Ordinal)))
        {
            item.Success = false;
            item.Status = AuraSharedRegistrationStatuses.Invalid;
            item.Message = "UserManual resources require a Tool LocalPackage written by LocalUser.";
            return item;
        }
        var validParticipantOrigin = manifest.ParticipantKind == AuraSharedParticipantKinds.Content
            ? resource.OriginKind == AuraSharedOriginKinds.ContentRegistered
            : manifest.ParticipantKind == AuraSharedParticipantKinds.Foundation
                ? resource.OriginKind == AuraSharedOriginKinds.FoundationDefault
                : resource.OriginKind == AuraSharedOriginKinds.ToolRegistered
                  || resource.OriginKind == AuraSharedOriginKinds.ToolDefault
                  || resource.OriginKind == AuraSharedOriginKinds.UserManual;
        if (!validParticipantOrigin)
        {
            item.Success = false;
            item.Status = AuraSharedRegistrationStatuses.Invalid;
            item.Message = "Resource originKind does not match participantKind.";
            return item;
        }
        var source = Path.GetFullPath(Path.Combine(sourceRoot, resource.Source.Replace('/', Path.DirectorySeparatorChar)));
        if (!AuraSharedStorageCoordinator.IsInside(source, sourceRoot) || !Exists(source))
        {
            item.Success = false;
            item.Status = AuraSharedRegistrationStatuses.Unavailable;
            item.Message = "Resource source is unavailable or escapes the package: " + resource.Source;
        }
        return item;
    }

    private AuraSharedRegistrationItemResultV4 RegisterResource(
        AuraSharedRegistrationManifestV4 manifest,
        AuraSharedResourceDeclarationV4 resource,
        string sourceRoot)
    {
        resource.Normalize();
        var item = new AuraSharedRegistrationItemResultV4
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
            PreserveLocalChanges = false
        });
        item.Success = installed.Success;
        item.Changed = installed.Changed;
        item.Status = installed.Status;
        item.Message = installed.Message;
        var state = new AuraSharedResourceStateV4
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

    private void WriteLayeredMetadata(
        AuraSharedRegistrationManifestV4 manifest,
        IReadOnlyList<AuraSharedRegistrationItemResultV4> items)
    {
        storage.WriteRawJsonAtomic(Absolute(AuraSharedResourcePathPolicy.RootManifestPath()), new
        {
            schemaVersion = AuraSharedResourceSchemaVersions.Current,
            protocolVersion = AuraSharedResourceProtocolVersions.Current,
            layout = "module/scopeType/scopeId/featureId/ownerModId/resourceId",
            readPolicy = "onDemand",
            generated = true
        }, false);

        foreach (var module in manifest.Resources.Select(item => item.ModuleId)
                     .Concat(manifest.Defaults.Select(item => item.ModuleId))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            storage.WriteRawJsonAtomic(Absolute(AuraSharedResourcePathPolicy.ModuleManifestPath(module)), new
            {
                schemaVersion = AuraSharedResourceSchemaVersions.Current,
                moduleId = module,
                layout = "module/scopeType/scopeId/featureId/ownerModId/resourceId",
                readPolicy = "onDemand"
            }, false);
        }


        foreach (var scopeType in manifest.Resources.Select(item => item.Scope)
                     .Concat(manifest.Defaults.Select(item => item.Scope))
                     .GroupBy(scope => scope.ModuleId + "\n" + scope.ScopeType, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            storage.WriteRawJsonAtomic(
                Absolute(AuraSharedResourcePathPolicy.ScopeTypeManifestPath(scopeType.ModuleId, scopeType.ScopeType)),
                new
                {
                    schemaVersion = AuraSharedResourceSchemaVersions.Current,
                    moduleId = scopeType.ModuleId,
                    scopeType = scopeType.ScopeType,
                    generated = true
                },
                false);
        }

        var scopes = manifest.Resources.Select(item => item.Scope)
            .Concat(manifest.Defaults.Select(item => item.Scope))
            .GroupBy(scope => scope.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
        foreach (var scope in scopes)
        {
            storage.WriteRawJsonAtomic(Absolute(AuraSharedResourcePathPolicy.ScopeManifestPath(scope)), new
            {
                schemaVersion = AuraSharedResourceSchemaVersions.Current,
                moduleId = scope.ModuleId,
                scopeType = scope.ScopeType,
                scopeId = scope.ScopeId,
                generated = true
            }, false);

            var scopeResources = active.Values.SelectMany(value => value.Resources)
                .Where(item => item.Scope.Equals(scope))
                .ToArray();
            storage.WriteRawJsonAtomic(Absolute(AuraSharedResourcePathPolicy.FeatureManifestPath(scope)), new
            {
                schemaVersion = AuraSharedResourceSchemaVersions.Current,
                scope,
                effectModes = scopeResources
                    .Select(item => item.EffectMode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                missingPolicies = scopeResources
                    .Select(item => item.MissingPolicy).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            }, false);

            var ownerManifests = active.Values
                .Where(value => string.Equals(value.OwnerModId, manifest.OwnerModId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var defaults = ownerManifests
                .SelectMany(value => value.Defaults)
                .Where(item => item.Scope.Equals(scope))
                .ToArray();
            var providerResources = ownerManifests
                .SelectMany(value => value.Resources)
                .Where(item => item.Scope.Equals(scope))
                .ToArray();
            storage.WriteRawJsonAtomic(
                Absolute(AuraSharedResourcePathPolicy.ProviderManifestPath(scope, manifest.OwnerModId)),
                new
                {
                    schemaVersion = AuraSharedResourceSchemaVersions.Current,
                    ownerModId = manifest.OwnerModId,
                    participantKinds = ownerManifests.Select(value => value.ParticipantKind)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    packages = ownerManifests.Select(value => new
                        {
                            value.PackageId,
                            value.PackageVersion
                        })
                        .OrderBy(value => value.PackageId, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    resources = providerResources.Select(item => item.ResourceId)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    generated = true
                },
                false);
            storage.WriteRawJsonAtomic(
                Absolute(AuraSharedResourcePathPolicy.ProviderDefaultsPath(scope, manifest.OwnerModId)),
                new
                {
                    schemaVersion = AuraSharedResourceSchemaVersions.Current,
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
                    schemaVersion = AuraSharedResourceSchemaVersions.Current,
                    ownerModId = manifest.OwnerModId,
                    resource,
                    canonicalPath = item?.CanonicalPath ?? ""
                },
                false);
        }
    }

    private void WriteRuntimeIndex()
    {
        storage.WriteRawJsonAtomic(Absolute("_Runtime/Index/resources.v4.json"), new
        {
            schemaVersion = AuraSharedResourceSchemaVersions.Current,
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

    private AuraSharedActiveLeaseV4 CreateLease(AuraSharedRegistrationManifestV4 manifest)
    {
        return new AuraSharedActiveLeaseV4
        {
            SessionId = SessionId,
            OwnerModId = manifest.OwnerModId,
            ParticipantKind = manifest.ParticipantKind,
            PackageSourceKind = manifest.PackageSourceKind,
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
        AuraSharedRegistrationManifestV4 manifest,
        AuraSharedResourceDeclarationV4 resource,
        string requested)
    {
        var canonical = AuraSharedResourcePathPolicy.ResourcePath(resource.Scope, manifest.OwnerModId, resource);
        if (string.Equals(requested, canonical, StringComparison.OrdinalIgnoreCase))
        {
            return new ResourcePathMatch(manifest, resource, canonical);
        }

        var directory = string.Equals(resource.Kind, AuraSharedResourceKinds.Directory, StringComparison.OrdinalIgnoreCase);
        if (directory && requested.StartsWith(canonical.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = requested.Substring(canonical.TrimEnd('/').Length);
            return new ResourcePathMatch(
                manifest,
                resource,
                canonical.TrimEnd('/') + suffix);
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

    private static bool SamePath(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Safe(string value) => AuraSharedPaths.SafeSegment(value, "unknown");

    private static string RegistrationKey(string ownerModId, string packageId)
    {
        return (ownerModId ?? "").Trim() + "\n" + (packageId ?? "").Trim();
    }

    private sealed class ResourcePathMatch
    {
        public ResourcePathMatch(
            AuraSharedRegistrationManifestV4 manifest,
            AuraSharedResourceDeclarationV4 resource,
            string canonicalRequestPath)
        {
            Manifest = manifest;
            Resource = resource;
            CanonicalRequestPath = canonicalRequestPath;
        }

        public AuraSharedRegistrationManifestV4 Manifest { get; }
        public AuraSharedResourceDeclarationV4 Resource { get; }
        public string CanonicalRequestPath { get; }
    }
}
