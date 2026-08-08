using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AuraToolsExp.Dll.Features.AutoBattle;

internal enum BundledFoundationRegistrationPlanDisposition
{
    Install,
    DeduplicateExisting,
    DeduplicateBatch,
    Conflict
}

internal sealed class BundledFoundationRegistrationPlanIdentity
{
    public string SourceReference { get; set; } = "";

    public string ModelId { get; set; } = "";

    public string SourceSha256 { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string PartnerId { get; set; } = "";

    public List<string> EnabledRewardCardPackIds { get; set; } = new();

    public string ModelVersion { get; set; } = "";
}

internal sealed class BundledFoundationRegistrationPlanDecision
{
    public BundledFoundationRegistrationPlanIdentity Candidate { get; set; } =
        new();

    public BundledFoundationRegistrationPlanDisposition Disposition { get; set; }

    public BundledFoundationRegistrationPlanIdentity? CanonicalCandidate { get; set; }

    public string Diagnostic { get; set; } = "";
}

internal sealed class BundledFoundationFileTransaction : IDisposable
{
    private readonly List<PublishedFile> publishedFiles = new();
    private readonly Action<string>? beforePublish;
    private bool completed;

    public BundledFoundationFileTransaction(
        Action<string>? beforePublish = null)
    {
        this.beforePublish = beforePublish;
    }

    public void Publish(string targetPath, Action<string> writeStagedFile)
    {
        if (completed)
        {
            throw new InvalidOperationException("底模文件事务已经结束");
        }
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("发布目标路径为空", nameof(targetPath));
        }
        if (writeStagedFile == null)
        {
            throw new ArgumentNullException(nameof(writeStagedFile));
        }

        var target = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(target)
                        ?? throw new InvalidDataException("发布目标缺少父目录");
        Directory.CreateDirectory(directory);
        var token = Guid.NewGuid().ToString("N");
        var fileName = Path.GetFileName(target);
        var staged = Path.Combine(
            directory,
            "." + fileName + ".staging-" + token + ".tmp");
        var backup = Path.Combine(
            directory,
            "." + fileName + ".rollback-" + token + ".bak");
        var publication = new PublishedFile(
            target,
            staged,
            backup,
            File.Exists(target));
        try
        {
            writeStagedFile(staged);
            if (!File.Exists(staged))
            {
                throw new IOException("暂存写入未生成文件：" + staged);
            }

            beforePublish?.Invoke(target);
            if (publication.OriginalExisted)
            {
                File.Replace(staged, target, backup, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(staged, target);
            }
            publication.Published = true;
            publishedFiles.Add(publication);
        }
        catch
        {
            RestoreUncertainPublication(publication);
            TryDelete(staged);
            TryDelete(backup);
            throw;
        }
    }

    public void Commit()
    {
        if (completed)
        {
            return;
        }
        completed = true;
        foreach (var publication in publishedFiles)
        {
            TryDelete(publication.StagedPath);
            TryDelete(publication.BackupPath);
        }
        publishedFiles.Clear();
    }

    public static bool TryCommitIndex(
        IReadOnlyList<BundledFoundationFileTransaction>? transactions,
        Action commitIndex,
        out Exception? failure,
        out string rollbackDiagnostic)
    {
        var items = (transactions
                     ?? Array.Empty<BundledFoundationFileTransaction>())
            .Where(item => item != null)
            .ToList();
        try
        {
            commitIndex();
            foreach (var transaction in items)
            {
                transaction.Commit();
            }
            failure = null;
            rollbackDiagnostic = "";
            return true;
        }
        catch (Exception ex)
        {
            var rollbackFailures = new List<string>();
            for (var index = items.Count - 1; index >= 0; index--)
            {
                if (!items[index].TryRollback(out var diagnostic))
                {
                    rollbackFailures.Add(diagnostic);
                }
            }
            failure = ex;
            rollbackDiagnostic = string.Join("；", rollbackFailures);
            return false;
        }
    }

    public bool TryRollback(out string diagnostic)
    {
        if (completed)
        {
            diagnostic = "";
            return true;
        }

        var failures = new List<string>();
        for (var index = publishedFiles.Count - 1; index >= 0; index--)
        {
            var publication = publishedFiles[index];
            try
            {
                RestorePublishedFile(publication);
            }
            catch (Exception ex)
            {
                failures.Add(
                    Path.GetFileName(publication.TargetPath) + "：" + ex.Message);
            }
            finally
            {
                TryDelete(publication.StagedPath);
                TryDelete(publication.BackupPath);
            }
        }
        publishedFiles.Clear();
        completed = true;
        diagnostic = string.Join("；", failures);
        return failures.Count == 0;
    }

    public void Dispose()
    {
        if (!completed)
        {
            TryRollback(out _);
        }
    }

    private static void RestoreUncertainPublication(PublishedFile publication)
    {
        if (publication.OriginalExisted && File.Exists(publication.BackupPath))
        {
            RestorePublishedFile(publication);
            return;
        }
        if (!publication.OriginalExisted
            && !File.Exists(publication.StagedPath)
            && File.Exists(publication.TargetPath))
        {
            File.Delete(publication.TargetPath);
        }
    }

    private static void RestorePublishedFile(PublishedFile publication)
    {
        if (publication.OriginalExisted)
        {
            if (!File.Exists(publication.BackupPath))
            {
                if (publication.Published)
                {
                    throw new IOException("回滚备份不存在");
                }
                return;
            }
            if (File.Exists(publication.TargetPath))
            {
                File.Replace(
                    publication.BackupPath,
                    publication.TargetPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(publication.BackupPath, publication.TargetPath);
            }
            return;
        }

        if (File.Exists(publication.TargetPath))
        {
            File.Delete(publication.TargetPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed class PublishedFile
    {
        public PublishedFile(
            string targetPath,
            string stagedPath,
            string backupPath,
            bool originalExisted)
        {
            TargetPath = targetPath;
            StagedPath = stagedPath;
            BackupPath = backupPath;
            OriginalExisted = originalExisted;
        }

        public string TargetPath { get; }

        public string StagedPath { get; }

        public string BackupPath { get; }

        public bool OriginalExisted { get; }

        public bool Published { get; set; }
    }
}

internal static class AuraToolsBundledFoundationRegistrationPlanner
{
    public static bool TryResolveSourceManifest(
        string sourceDirectory,
        string sourceReference,
        out string manifestPath,
        out string normalizedReference,
        out string diagnostic)
    {
        manifestPath = "";
        normalizedReference = "";
        try
        {
            var directory = (sourceDirectory ?? "").Trim();
            var reference = (sourceReference ?? "").Trim();
            if (string.IsNullOrWhiteSpace(directory)
                || string.IsNullOrWhiteSpace(reference)
                || Path.IsPathRooted(reference))
            {
                diagnostic = "来源清单路径无效";
                return false;
            }
            reference = reference
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            var segments = reference.Split('/');
            if (segments.Length == 0
                || segments.Any(segment =>
                    string.IsNullOrWhiteSpace(segment)
                    || string.Equals(segment, ".", StringComparison.Ordinal)
                    || string.Equals(segment, "..", StringComparison.Ordinal)
                    || segment.Any(char.IsControl)))
            {
                diagnostic = "来源清单相对路径包含不安全段";
                return false;
            }

            var root = Path.GetFullPath(directory);
            var path = Path.GetFullPath(Path.Combine(
                root,
                segments[segments.Length - 1]));
            if (!string.Equals(
                    Path.GetDirectoryName(path),
                    root.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "来源清单文件必须位于声明的清单目录";
                return false;
            }
            manifestPath = path;
            normalizedReference = string.Join("/", segments);
            diagnostic = "";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = "来源清单路径无效：" + ex.Message;
            return false;
        }
    }

    public static IReadOnlyList<BundledFoundationRegistrationPlanDecision> Plan(
        IReadOnlyList<BundledFoundationRegistrationPlanIdentity>? candidates,
        IReadOnlyList<BundledFoundationRegistrationPlanIdentity>? existing)
    {
        var candidateNodes = (candidates
                              ?? Array.Empty<BundledFoundationRegistrationPlanIdentity>())
            .Where(item => item != null)
            .Select(item => new CandidateNode(item))
            .OrderBy(item => item.Identity, IdentityComparer.Instance)
            .ToList();
        var existingItems = (existing
                             ?? Array.Empty<BundledFoundationRegistrationPlanIdentity>())
            .Where(item => item != null)
            .ToList();

        foreach (var modelGroup in candidateNodes.GroupBy(
                     item => Normalize(item.Identity.ModelId),
                     StringComparer.Ordinal))
        {
            var distinctHashes = modelGroup
                .Select(item => CanonicalSha256(item.Identity.SourceSha256))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var distinctReleases = modelGroup
                .Select(item => FoundationReleaseKey.From(item.Identity))
                .Distinct()
                .Count();
            if (distinctHashes.Count != 1
                || string.IsNullOrWhiteSpace(distinctHashes[0])
                || distinctReleases != 1)
            {
                MarkConflict(
                    modelGroup,
                    "同批次模型 ID 对应多个来源工件或不一致的发布身份");
                continue;
            }

            var matchingExisting = existingItems
                .Where(item => string.Equals(
                    Normalize(item.ModelId),
                    modelGroup.Key,
                    StringComparison.Ordinal))
                .ToList();
            if (matchingExisting.Count > 1)
            {
                MarkConflict(
                    modelGroup,
                    "模型库包含重复模型 ID，无法安全判定来源工件");
                continue;
            }

            if (matchingExisting.Count == 1)
            {
                var existingHash = CanonicalSha256(
                    matchingExisting[0].SourceSha256);
                foreach (var node in modelGroup)
                {
                    var candidateHash = CanonicalSha256(
                        node.Identity.SourceSha256);
                    if (!string.IsNullOrWhiteSpace(existingHash)
                        && string.Equals(
                            existingHash,
                            candidateHash,
                            StringComparison.Ordinal))
                    {
                        node.Decision.Disposition =
                            BundledFoundationRegistrationPlanDisposition
                                .DeduplicateExisting;
                        node.Decision.Diagnostic =
                            "模型 ID 与来源工件 SHA-256 已存在";
                    }
                    else
                    {
                        node.Decision.Disposition =
                            BundledFoundationRegistrationPlanDisposition.Conflict;
                        node.Decision.Diagnostic =
                            "模型 ID 已存在，但来源工件 SHA-256 不同或不可验证";
                    }
                }
                continue;
            }

            var canonical = modelGroup.First();
            canonical.Decision.Disposition =
                BundledFoundationRegistrationPlanDisposition.Install;
            canonical.Decision.CanonicalCandidate = canonical.Identity;
            canonical.Decision.Diagnostic = "新模型待安装";
            foreach (var duplicate in modelGroup.Skip(1))
            {
                duplicate.Decision.Disposition =
                    BundledFoundationRegistrationPlanDisposition.DeduplicateBatch;
                duplicate.Decision.CanonicalCandidate = canonical.Identity;
                duplicate.Decision.Diagnostic =
                    "同批次模型 ID 与来源工件 SHA-256 完全相同";
            }
        }

        var existingByRelease = existingItems
            .GroupBy(FoundationReleaseKey.From)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => Normalize(item.ModelId))
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList());
        foreach (var releaseGroup in candidateNodes.GroupBy(
                     item => FoundationReleaseKey.From(item.Identity)))
        {
            var modelIds = releaseGroup
                .Select(item => Normalize(item.Identity.ModelId))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
            if (existingByRelease.TryGetValue(
                    releaseGroup.Key,
                    out var existingModelIds))
            {
                modelIds.AddRange(existingModelIds);
            }
            var distinctModelIds = modelIds
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            if (distinctModelIds.Count <= 1)
            {
                continue;
            }

            var diagnostic =
                "相同角色、使魔、卡池与 ModelVersion 包含多个模型 ID，整组拒绝："
                + string.Join(", ", distinctModelIds);
            foreach (var node in releaseGroup)
            {
                node.Decision.Disposition =
                    BundledFoundationRegistrationPlanDisposition.Conflict;
                node.Decision.CanonicalCandidate = null;
                node.Decision.Diagnostic = diagnostic;
            }
        }

        return candidateNodes
            .Select(item => item.Decision)
            .ToList();
    }

    private static void MarkConflict(
        IEnumerable<CandidateNode> nodes,
        string diagnostic)
    {
        foreach (var node in nodes)
        {
            node.Decision.Disposition =
                BundledFoundationRegistrationPlanDisposition.Conflict;
            node.Decision.CanonicalCandidate = null;
            node.Decision.Diagnostic = diagnostic;
        }
    }

    private static string CanonicalSha256(string value)
    {
        var normalized = Normalize(value).ToUpperInvariant();
        return normalized.Length == 64
               && normalized.All(character =>
                   character >= '0' && character <= '9'
                   || character >= 'A' && character <= 'F')
            ? normalized
            : "";
    }

    private static string Normalize(string value)
    {
        return (value ?? "").Trim();
    }

    private sealed class CandidateNode
    {
        public CandidateNode(BundledFoundationRegistrationPlanIdentity identity)
        {
            Identity = identity;
            Decision = new BundledFoundationRegistrationPlanDecision
            {
                Candidate = identity,
                Disposition = BundledFoundationRegistrationPlanDisposition.Conflict,
                Diagnostic = "尚未完成批量注册规划"
            };
        }

        public BundledFoundationRegistrationPlanIdentity Identity { get; }

        public BundledFoundationRegistrationPlanDecision Decision { get; }
    }

    private sealed class IdentityComparer :
        IComparer<BundledFoundationRegistrationPlanIdentity>
    {
        public static readonly IdentityComparer Instance = new();

        public int Compare(
            BundledFoundationRegistrationPlanIdentity? left,
            BundledFoundationRegistrationPlanIdentity? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }
            if (left == null)
            {
                return -1;
            }
            if (right == null)
            {
                return 1;
            }

            var comparison = StringComparer.OrdinalIgnoreCase.Compare(
                left.SourceReference,
                right.SourceReference);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = StringComparer.Ordinal.Compare(
                left.SourceReference,
                right.SourceReference);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = StringComparer.Ordinal.Compare(left.ModelId, right.ModelId);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = StringComparer.OrdinalIgnoreCase.Compare(
                left.SourceSha256,
                right.SourceSha256);
            if (comparison != 0)
            {
                return comparison;
            }
            return FoundationReleaseKey.From(left).CompareTo(
                FoundationReleaseKey.From(right));
        }
    }

    private sealed class FoundationReleaseKey :
        IEquatable<FoundationReleaseKey>,
        IComparable<FoundationReleaseKey>
    {
        private static readonly StringComparer Comparer =
            StringComparer.OrdinalIgnoreCase;

        private FoundationReleaseKey(
            string roleId,
            string partnerId,
            IEnumerable<string>? cardPackIds,
            string modelVersion)
        {
            RoleId = Normalize(roleId);
            PartnerId = Normalize(partnerId);
            CardPackIds = (cardPackIds ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(Normalize)
                .Distinct(Comparer)
                .OrderBy(id => id, Comparer)
                .ThenBy(id => id, StringComparer.Ordinal)
                .ToArray();
            ModelVersion = Normalize(modelVersion).TrimStart('v', 'V');
        }

        private string RoleId { get; }

        private string PartnerId { get; }

        private string[] CardPackIds { get; }

        private string ModelVersion { get; }

        public static FoundationReleaseKey From(
            BundledFoundationRegistrationPlanIdentity identity)
        {
            return new FoundationReleaseKey(
                identity.RoleId,
                identity.PartnerId,
                identity.EnabledRewardCardPackIds,
                identity.ModelVersion);
        }

        public bool Equals(FoundationReleaseKey? other)
        {
            return other != null
                   && Comparer.Equals(RoleId, other.RoleId)
                   && Comparer.Equals(PartnerId, other.PartnerId)
                   && Comparer.Equals(ModelVersion, other.ModelVersion)
                   && CardPackIds.SequenceEqual(other.CardPackIds, Comparer);
        }

        public override bool Equals(object? value)
        {
            return Equals(value as FoundationReleaseKey);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + Comparer.GetHashCode(RoleId);
                hash = hash * 31 + Comparer.GetHashCode(PartnerId);
                hash = hash * 31 + Comparer.GetHashCode(ModelVersion);
                foreach (var cardPackId in CardPackIds)
                {
                    hash = hash * 31 + Comparer.GetHashCode(cardPackId);
                }
                return hash;
            }
        }

        public int CompareTo(FoundationReleaseKey? other)
        {
            if (other == null)
            {
                return 1;
            }
            var comparison = Comparer.Compare(RoleId, other.RoleId);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = Comparer.Compare(PartnerId, other.PartnerId);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = Comparer.Compare(ModelVersion, other.ModelVersion);
            if (comparison != 0)
            {
                return comparison;
            }
            var count = Math.Min(CardPackIds.Length, other.CardPackIds.Length);
            for (var index = 0; index < count; index++)
            {
                comparison = Comparer.Compare(
                    CardPackIds[index],
                    other.CardPackIds[index]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }
            return CardPackIds.Length.CompareTo(other.CardPackIds.Length);
        }
    }
}
