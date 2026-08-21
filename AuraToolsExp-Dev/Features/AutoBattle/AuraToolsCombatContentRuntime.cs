using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.AutoBattle;

/// <summary>
/// Consumes content-owned CombatAI packages exclusively through the active
/// AuraShared catalog. It never scans another MOD's private directory.
/// </summary>
internal static class AuraToolsCombatContentRuntime
{
    private static readonly object Gate = new();
    private static readonly List<IDisposable> Registrations = new();
    private static List<CombatContentLoadedPackage> packages = new();
    private static CombatContentSetSnapshot contentSet = CombatContentSetProtocol.Create(
        Array.Empty<CombatContentLoadedPackage>(), "");
    private static bool initialized;
    private static bool refreshQueued;
    private static long observedCatalogRevision = -1;

    public static void Initialize()
    {
        if (initialized)
        {
            return;
        }
        initialized = true;
        AuraSharedResourceProtocol.ScopeChanged += OnScopeChanged;
        RequestRefresh(force: true);
    }

    public static CombatContentSetSnapshot SnapshotContentSet()
    {
        lock (Gate)
        {
            return new CombatContentSetSnapshot
            {
                ContentSetHash = contentSet.ContentSetHash,
                OwnerModSetHash = contentSet.OwnerModSetHash,
                Packages = contentSet.Packages.Select(item => new CombatContentSetEntry
                {
                    OwnerModId = item.OwnerModId,
                    PackageId = item.PackageId,
                    PackageVersion = item.PackageVersion,
                    PackageFingerprint = item.PackageFingerprint,
                    FoundationTrainingReady = item.FoundationTrainingReady
                }).ToList()
            };
        }
    }

    public static IReadOnlyList<CombatContentLoadedPackage> SnapshotPackages()
    {
        lock (Gate)
        {
            return packages.ToArray();
        }
    }

    public static IReadOnlyList<string> SnapshotPackageDirectories()
    {
        lock (Gate)
        {
            return packages.Select(item => item.RootDirectory)
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public static bool TryLoadAuthoritativeTrainingEpisodes(
        string expectedContentSetHash,
        string expectedOwnerModSetHash,
        string expectedRulesetHash,
        out List<CombatEpisode> episodes,
        out string diagnostic)
    {
        ContentEpisodeSource[] sources;
        lock (Gate)
        {
            if (!string.Equals(
                    expectedContentSetHash,
                    contentSet.ContentSetHash,
                    StringComparison.Ordinal)
                || !string.Equals(
                    expectedOwnerModSetHash,
                    contentSet.OwnerModSetHash,
                    StringComparison.Ordinal))
            {
                episodes = new List<CombatEpisode>();
                diagnostic = "内容集合在训练语料读取前已变化";
                return false;
            }
            sources = packages.Where(item => item.FoundationTrainingReady)
                .SelectMany(item => item.TrainingEpisodePaths.Select(path =>
                    new ContentEpisodeSource
                    {
                        OwnerModId = item.Package.OwnerModId,
                        PackageId = item.Package.PackageId,
                        Path = path
                    }))
                .OrderBy(item => item.OwnerModId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        episodes = new List<CombatEpisode>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            long totalBytes = 0;
            foreach (var source in sources)
            {
                var sourceBytes = new FileInfo(source.Path).Length;
                totalBytes += sourceBytes;
                if (sourceBytes
                    > CombatContentTrainingEpisodeProtocol.MaximumArtifactBytes
                    || totalBytes
                    > CombatContentTrainingEpisodeProtocol.MaximumContentSetBytes)
                {
                    diagnostic = SourceError(
                        source,
                        0,
                        "训练工件超过单文件或内容集合字节上限");
                    return false;
                }
                var sourceEpisodes = 0;
                var lineNumber = 0;
                foreach (var line in File.ReadLines(source.Path))
                {
                    lineNumber++;
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }
                    var episode = AuraSharedJson.Deserialize<CombatEpisode>(line);
                    if (episode == null)
                    {
                        diagnostic = SourceError(source, lineNumber, "Episode JSON 为空");
                        return false;
                    }
                    var originalEpisodeId = episode.EpisodeId ?? "";
                    episode.EpisodeId = source.OwnerModId + ":"
                                        + source.PackageId + ":"
                                        + originalEpisodeId;
                    if (!string.IsNullOrWhiteSpace(episode.JourneyRunId))
                    {
                        episode.JourneyRunId = source.OwnerModId + ":"
                                               + source.PackageId + ":"
                                               + episode.JourneyRunId;
                    }
                    episode.ContentSetHash = expectedContentSetHash;
                    episode.OwnerModSetHash = expectedOwnerModSetHash;
                    episode.RulesetHash = expectedRulesetHash;
                    episode.Provenance = "content-package:"
                                         + source.OwnerModId + ":"
                                         + source.PackageId + ":"
                                         + (episode.Provenance ?? "");
                    foreach (var frame in episode.Frames
                                 ?? new List<CombatEpisodeFrame>())
                    {
                        foreach (var candidate in frame.Candidates
                                     ?? new List<CombatEpisodeCandidate>())
                        {
                            candidate.OwnerModId = ResolveOwnerModId(
                                candidate.SourceId);
                        }
                    }
                    if (!CombatContentTrainingEpisodeProtocol.TryValidate(
                            episode,
                            expectedContentSetHash,
                            expectedOwnerModSetHash,
                            expectedRulesetHash,
                            out var reason))
                    {
                        diagnostic = SourceError(source, lineNumber, reason);
                        return false;
                    }
                    if (!identities.Add(episode.EpisodeId))
                    {
                        diagnostic = SourceError(
                            source,
                            lineNumber,
                            "EpisodeId 在同一内容集合中重复");
                        return false;
                    }
                    episodes.Add(episode);
                    sourceEpisodes++;
                    if (episodes.Count
                        > CombatContentTrainingEpisodeProtocol
                            .MaximumEpisodesPerContentSet)
                    {
                        diagnostic = "内容训练 Episode 超过集合上限 "
                                     + CombatContentTrainingEpisodeProtocol
                                         .MaximumEpisodesPerContentSet;
                        return false;
                    }
                }
                if (sourceEpisodes == 0)
                {
                    diagnostic = SourceError(source, 0, "训练工件不含 Episode");
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            diagnostic = "内容训练 Episode 读取失败：" + ex.Message;
            return false;
        }
        var latest = SnapshotContentSet();
        if (!string.Equals(
                latest.ContentSetHash,
                expectedContentSetHash,
                StringComparison.Ordinal)
            || !string.Equals(
                latest.OwnerModSetHash,
                expectedOwnerModSetHash,
                StringComparison.Ordinal))
        {
            episodes.Clear();
            diagnostic = "内容集合在训练语料读取期间发生变化";
            return false;
        }
        diagnostic = sources.Length == 0
            ? "未注册内容训练 Episode"
            : "已读取内容训练 Episode " + episodes.Count + " 条";
        return true;
    }

    public static IReadOnlyList<CombatLowRankPolicyAdapterDefinition>
        SnapshotPolicyAdapters(string baseModelId)
    {
        lock (Gate)
        {
            return packages
                .Where(item => item.PolicyAdapter != null
                               && CombatModelAdapterValidator.TryValidate(
                                   item.PolicyAdapter,
                                   baseModelId,
                                   contentSet.ContentSetHash,
                                   out _))
                .Select(item => item.PolicyAdapter!)
                .OrderBy(
                    item => item.Manifest.OwnerModId,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    item => item.Manifest.AdapterId,
                    StringComparer.Ordinal)
                .ToArray();
        }
    }

    public static string ResolveOwnerModId(string sourceId)
    {
        var source = (sourceId ?? "").Trim();
        if (source.Length == 0)
        {
            return "";
        }
        lock (Gate)
        {
            foreach (var item in packages)
            {
                var coverage = item.Package.DeclaredCoverage
                               ?? new CombatFoundationDeclaredCoverage();
                if ((coverage.CardIds ?? new List<string>()).Contains(
                        source, StringComparer.OrdinalIgnoreCase)
                    || (coverage.RoleSkillCardIds ?? new List<string>()).Contains(
                        source, StringComparer.OrdinalIgnoreCase)
                    || (coverage.EnemyIds ?? new List<string>()).Contains(
                        source, StringComparer.OrdinalIgnoreCase)
                    || (coverage.StatusIds ?? new List<string>()).Contains(
                        source, StringComparer.OrdinalIgnoreCase)
                    || (coverage.RelicIds ?? new List<string>()).Contains(
                        source, StringComparer.OrdinalIgnoreCase)
                    || (coverage.BlessingIds ?? new List<string>()).Contains(
                        source, StringComparer.OrdinalIgnoreCase))
                {
                    return item.Package.OwnerModId;
                }
            }
        }
        if (source.StartsWith("AuraToolsExp_", StringComparison.OrdinalIgnoreCase))
        {
            return AuraToolsIds.ModId;
        }
        if (string.Equals(
                source,
                "simulation:end-turn",
                StringComparison.OrdinalIgnoreCase))
        {
            return "witch.base-game";
        }
        var knowledgeOwner = CombatKnowledgeRegistry.SnapshotPackages()
            .Where(package => (package.Actions
                               ?? new List<CombatKnowledgeActionDefinition>())
                .Any(action => string.Equals(
                    action.SourceId,
                    source,
                    StringComparison.OrdinalIgnoreCase)))
            .Select(package => package.OwnerId)
            .FirstOrDefault(owner => !string.IsNullOrWhiteSpace(owner));
        return string.IsNullOrWhiteSpace(knowledgeOwner)
            ? "unregistered"
            : knowledgeOwner;
    }

    public static bool TryApplyFoundationContent(
        CombatCampaignDefinition campaign,
        CombatRulesetDocument baseRuleset,
        out CombatRulesetDocument mergedRuleset,
        out string diagnostic)
    {
        try
        {
            var snapshot = SnapshotPackages();
            mergedRuleset = CombatContentFoundationMerger.MergeRulesets(
                baseRuleset,
                snapshot);
            CombatContentFoundationMerger.ApplyCampaignOverlays(
                campaign,
                snapshot);
            var selected = snapshot.Count(item => item.FoundationTrainingReady);
            diagnostic = selected == 0
                ? "未启用内容 MOD 训练包"
                : "已合并 " + selected + " 个通过转移审计的内容 MOD 训练包";
            return true;
        }
        catch (Exception ex)
        {
            mergedRuleset = baseRuleset;
            diagnostic = "内容 MOD 训练包合并失败：" + ex.Message;
            return false;
        }
    }

    public static string LiveDatasetDirectory()
    {
        return LiveDatasetDirectory(SnapshotContentSet().ContentSetHash);
    }

    public static string LiveDatasetDirectory(string contentSetHash)
    {
        var hash = string.IsNullOrWhiteSpace(contentSetHash)
            ? CombatContentSetProtocol.EmptyContentSetHash
            : contentSetHash.Trim();
        var directory = Path.Combine(
            AuraSharedPaths.OwnerSystemDataDirectory(
                AuraToolsIds.ModId,
                "AuraCombatAI"),
            "Datasets",
            "Live",
            AuraSharedPaths.SafeSegment(hash, "empty"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static void RequestRefresh(bool force = false)
    {
        if (!initialized && !force)
        {
            return;
        }
        var catalog = AuraSharedResourceProtocol.QueryCatalog(
            AuraToolsIds.ModId,
            new AuraSharedCatalogQueryV4
            {
                ModuleId = CombatContentPackageProtocol.SharedModuleId,
                FeatureId = CombatContentPackageProtocol.SharedFeatureId,
                ScopeType = CombatContentPackageProtocol.SharedScopeType,
                ScopeId = CombatContentPackageProtocol.SharedScopeId,
                Visibility = AuraSharedCatalogVisibilities.Active
            });
        lock (Gate)
        {
            if (refreshQueued
                || !force && catalog.Revision == observedCatalogRevision)
            {
                return;
            }
            refreshQueued = true;
        }
        var descriptors = catalog.Entries
            .Where(entry => entry.Active
                            && entry.Available
                            && entry.EffectiveEnabled
                            && string.Equals(
                                entry.ParticipantKind,
                                AuraSharedParticipantKinds.Content,
                                StringComparison.Ordinal)
                            && string.Equals(
                                entry.Resource.Kind,
                                AuraSharedResourceKinds.Directory,
                                StringComparison.OrdinalIgnoreCase))
            .Select(entry => new CatalogPackageDescriptor
            {
                OwnerModId = entry.OwnerModId,
                PackageId = entry.Resource.ResourceId,
                RootDirectory = AuraSharedResourceProtocol.ResolvePath(
                    AuraToolsIds.ModId,
                    entry.CanonicalPath)
            })
            .OrderBy(item => item.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var queued = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<ContentLoadBatch>
            {
                OwnerId = AuraToolsIds.ModId + ".AutoBattle",
                Key = "AutoBattle.CombatContent.Refresh",
                Source = "AutoBattle.CombatContent.Refresh",
                Kind = AuraSharedBackgroundWorkKind.Io,
                Work = _ => Load(descriptors, catalog.Revision),
                ApplyOnMainThread = Apply,
                OnFailedOnMainThread = ex =>
                {
                    lock (Gate)
                    {
                        refreshQueued = false;
                    }
                    AuraToolsLog.Warn(
                        "[AutoBattle][Content] AuraShared 内容包刷新失败：" + ex);
                }
            });
        if (!queued)
        {
            lock (Gate)
            {
                refreshQueued = false;
            }
            AuraToolsLog.Warn("[AutoBattle][Content] AuraShared 内容包刷新任务未能提交");
        }
    }

    private static ContentLoadBatch Load(
        IEnumerable<CatalogPackageDescriptor> descriptors,
        long revision)
    {
        var batch = new ContentLoadBatch { Revision = revision };
        var currentBuild = CurrentGameBuild();
        foreach (var descriptor in descriptors)
        {
            var result = CombatContentPackageLoader.Load(
                descriptor.RootDirectory,
                descriptor.OwnerModId,
                descriptor.PackageId);
            if (result.Success && result.Loaded != null)
            {
                batch.Loaded.Add(result.Loaded);
            }
            else
            {
                batch.Diagnostics.Add(
                    descriptor.OwnerModId + ":" + descriptor.PackageId + "："
                    + (result.Errors.Count == 0
                        ? "内容包加载失败"
                        : string.Join("；", result.Errors)));
            }
        }
        ValidateDependencies(batch);
        ValidateFeatureContracts(batch);
        ValidateDependencies(batch);
        batch.ContentSet = CombatContentSetProtocol.Create(
            batch.Loaded,
            currentBuild);
        return batch;
    }

    private static void Apply(ContentLoadBatch batch)
    {
        lock (Gate)
        {
            foreach (var registration in Registrations)
            {
                registration.Dispose();
            }
            Registrations.Clear();
            var accepted = new List<CombatContentLoadedPackage>();
            foreach (var item in batch.Loaded)
            {
                IDisposable? knowledgeRegistration = null;
                if (item.Knowledge != null)
                {
                    knowledgeRegistration = CombatKnowledgeRegistry.RegisterPackage(
                        item.Knowledge,
                        out var errors);
                    if (errors.Count > 0)
                    {
                        knowledgeRegistration.Dispose();
                        batch.Diagnostics.Add(
                            item.Package.OwnerModId + ":" + item.Package.PackageId
                            + " 知识包：" + string.Join("；", errors));
                        continue;
                    }
                }
                if (knowledgeRegistration != null)
                {
                    Registrations.Add(knowledgeRegistration);
                }
                foreach (var feature in item.Package.PublicFeatures
                             ?? new List<CombatContentPublicFeatureDeclaration>())
                {
                    Registrations.Add(CombatPublicFeatureRegistry.Register(
                        item.Package.OwnerModId,
                        ParseFeatureScope(feature.Scope),
                        feature.Name,
                        feature.ValueType,
                        feature.Minimum,
                        feature.Maximum,
                        feature.DefaultValue));
                }
                accepted.Add(item);
            }
            batch.Loaded = accepted;
            batch.ContentSet = CombatContentSetProtocol.Create(
                accepted,
                CurrentGameBuild());
            packages = accepted;
            contentSet = batch.ContentSet;
            observedCatalogRevision = batch.Revision;
            refreshQueued = false;
        }
        AuraToolsAutoBattleSimulationRuntime.InvalidateFoundationPackageCache();
        AuraToolsLog.Info(
            "[AutoBattle][Content] AuraShared 内容包=" + batch.Loaded.Count
            + "，foundationReady="
            + batch.Loaded.Count(item => item.FoundationTrainingReady)
            + "，contentSet=" + batch.ContentSet.ContentSetHash);
        foreach (var diagnostic in batch.Diagnostics.Take(12))
        {
            AuraToolsLog.Warn("[AutoBattle][Content] " + diagnostic);
        }
        // A catalog event can arrive while this batch is in flight. Re-query once
        // after applying so that such a revision cannot be lost behind refreshQueued.
        RequestRefresh();
    }

    private static void ValidateDependencies(ContentLoadBatch batch)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            var available = batch.Loaded.ToDictionary(
                item => item.Package.OwnerModId + "\n" + item.Package.PackageId,
                item => item,
                StringComparer.OrdinalIgnoreCase);
            var rejected = new HashSet<CombatContentLoadedPackage>();
            foreach (var item in batch.Loaded)
            {
                foreach (var dependency in item.Package.Dependencies
                             ?? new List<CombatContentPackageDependency>())
                {
                    if (dependency.Optional)
                    {
                        continue;
                    }
                    var key = dependency.OwnerModId + "\n" + dependency.PackageId;
                    if (!available.TryGetValue(key, out var target)
                        || !VersionAtLeast(
                            target.Package.PackageVersion,
                            dependency.MinimumVersion))
                    {
                        rejected.Add(item);
                        batch.Diagnostics.Add(
                            item.Package.OwnerModId + ":" + item.Package.PackageId
                            + " 缺少依赖 " + dependency.OwnerModId + ":"
                            + dependency.PackageId + "@" + dependency.MinimumVersion);
                    }
                }
            }
            if (rejected.Count > 0)
            {
                batch.Loaded.RemoveAll(item => rejected.Contains(item));
                changed = true;
            }
        }
    }

    private static void ValidateFeatureContracts(ContentLoadBatch batch)
    {
        var declarations = batch.Loaded.SelectMany(package =>
            (package.Package.PublicFeatures
             ?? new List<CombatContentPublicFeatureDeclaration>())
            .Select(feature => new
            {
                Package = package,
                Key = (feature.Scope ?? "").Trim().ToLowerInvariant() + "\n"
                      + feature.Name.Trim().ToLowerInvariant(),
                Contract = (feature.ValueType ?? "").Trim().ToLowerInvariant()
                           + "|" + feature.Minimum.ToString(
                               "R", CultureInfo.InvariantCulture)
                           + "|" + feature.Maximum.ToString(
                               "R", CultureInfo.InvariantCulture)
                           + "|" + feature.DefaultValue.ToString(
                               "R", CultureInfo.InvariantCulture)
            }))
            .ToList();
        var rejected = new HashSet<CombatContentLoadedPackage>();
        foreach (var conflict in declarations
                     .GroupBy(item => item.Key, StringComparer.Ordinal)
                     .Where(group => group.Select(item => item.Contract)
                         .Distinct(StringComparer.Ordinal).Count() > 1))
        {
            foreach (var item in conflict)
            {
                rejected.Add(item.Package);
            }
            batch.Diagnostics.Add(
                "公开模型特征契约冲突：" + conflict.Key.Replace('\n', ':'));
        }
        if (rejected.Count > 0)
        {
            batch.Loaded.RemoveAll(item => rejected.Contains(item));
        }
    }

    private static bool VersionAtLeast(string actual, string minimum)
    {
        if (string.IsNullOrWhiteSpace(minimum))
        {
            return true;
        }
        return Version.TryParse(actual, out var actualVersion)
               && Version.TryParse(minimum, out var minimumVersion)
            ? actualVersion.CompareTo(minimumVersion) >= 0
            : string.Equals(actual, minimum, StringComparison.OrdinalIgnoreCase);
    }

    private static CombatPublicFeatureScope ParseFeatureScope(string value)
    {
        return (value ?? "").Trim().ToLowerInvariant() switch
        {
            "unit" => CombatPublicFeatureScope.Unit,
            "action" => CombatPublicFeatureScope.Action,
            "statechange" => CombatPublicFeatureScope.StateChange,
            "state-change" => CombatPublicFeatureScope.StateChange,
            _ => CombatPublicFeatureScope.State
        };
    }

    private static string CurrentGameBuild()
    {
        try
        {
            return GameConfigManager.Version ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static void OnScopeChanged(string scopeKey, long revision)
    {
        if (scopeKey.StartsWith(
                CombatContentPackageProtocol.SharedModuleId + ":"
                + CombatContentPackageProtocol.SharedFeatureId + ":",
                StringComparison.OrdinalIgnoreCase))
        {
            RequestRefresh();
        }
    }

    private sealed class CatalogPackageDescriptor
    {
        public string OwnerModId { get; set; } = "";

        public string PackageId { get; set; } = "";

        public string RootDirectory { get; set; } = "";
    }

    private static string SourceError(
        ContentEpisodeSource source,
        int lineNumber,
        string message)
    {
        return source.OwnerModId + ":" + source.PackageId
               + " 训练工件 " + Path.GetFileName(source.Path)
               + (lineNumber > 0 ? " 第 " + lineNumber + " 行" : "")
               + "：" + message;
    }

    private sealed class ContentEpisodeSource
    {
        public string OwnerModId { get; set; } = "";

        public string PackageId { get; set; } = "";

        public string Path { get; set; } = "";
    }

    private sealed class ContentLoadBatch
    {
        public long Revision { get; set; }

        public List<CombatContentLoadedPackage> Loaded { get; set; } = new();

        public CombatContentSetSnapshot ContentSet { get; set; } = new();

        public List<string> Diagnostics { get; set; } = new();
    }
}
