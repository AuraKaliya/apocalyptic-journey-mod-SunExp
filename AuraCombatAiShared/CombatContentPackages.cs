using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AuraCombatSimulation.Shared;
#if NET8_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;
#else
using Newtonsoft.Json;
#endif

namespace AuraCombatAi.Shared;

public static class CombatContentPackageProtocol
{
    public const string Protocol = "aura.combat-ai.content-package.v1";
    public const int SchemaVersion = 1;
    public const string SharedModuleId = "CombatAI";
    public const string SharedFeatureId = "ContentPackage";
    public const string SharedScopeType = "Global";
    public const string SharedScopeId = "all";
    public const string EntryFileName = "package.json";
}

public sealed class CombatContentPackage
{
    public string Protocol { get; set; } = CombatContentPackageProtocol.Protocol;

    public int SchemaVersion { get; set; } = CombatContentPackageProtocol.SchemaVersion;

    public string OwnerModId { get; set; } = "";

    public string PackageId { get; set; } = "";

    public string PackageVersion { get; set; } = "1.0.0";

    public string GameBuild { get; set; } = "";

    public bool FoundationTrainingEnabled { get; set; }

    public List<CombatContentPackageDependency> Dependencies { get; set; } = new();

    public CombatContentPackageArtifacts Artifacts { get; set; } = new();

    public CombatFoundationDeclaredCoverage DeclaredCoverage { get; set; } = new();

    public List<CombatContentPublicFeatureDeclaration> PublicFeatures { get; set; } = new();
}

public sealed class CombatContentPackageDependency
{
    public string OwnerModId { get; set; } = "";

    public string PackageId { get; set; } = "";

    public string MinimumVersion { get; set; } = "";

    public bool Optional { get; set; }
}

public sealed class CombatContentPackageArtifacts
{
    public CombatContentArtifactReference? Knowledge { get; set; }

    public CombatContentArtifactReference? Ruleset { get; set; }

    public CombatContentArtifactReference? FoundationOverlay { get; set; }

    public CombatContentArtifactReference? TransitionAudit { get; set; }

    public CombatContentArtifactReference? PolicyAdapter { get; set; }

    public CombatContentArtifactReference? TransformerAdapter { get; set; }

    public List<CombatContentArtifactReference> TrainingEpisodes { get; set; } = new();
}

public sealed class CombatContentArtifactReference
{
    public string Path { get; set; } = "";

    public string Sha256 { get; set; } = "";
}

public sealed class CombatContentPublicFeatureDeclaration
{
    public string Name { get; set; } = "";

    public string Scope { get; set; } = "state";

    public string ValueType { get; set; } = "number";

    public double Minimum { get; set; }

    public double Maximum { get; set; }

    public double DefaultValue { get; set; }

    public bool PubliclyObservable { get; set; } = true;
}

public sealed class CombatContentFoundationOverlay
{
    public string Protocol { get; set; } = "aura.combat-ai.foundation-overlay.v1";

    public int SchemaVersion { get; set; } = 1;

    public List<CombatCampaignEnemyCatalogEntry> Enemies { get; set; } = new();

    public List<CombatCampaignEncounterDefinition> Encounters { get; set; } = new();

    public List<CombatCampaignRewardDefinition> Rewards { get; set; } = new();

    public List<CombatCampaignStrategyDefinition> Strategies { get; set; } = new();

    public List<CombatCampaignDifficultyDefinition> Difficulties { get; set; } = new();

    public List<string> EnabledRewardCardPackIds { get; set; } = new();

    public Dictionary<string, double> RolePrior { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> BuildTendency { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CombatTransitionAuditCorpus
{
    public string Protocol { get; set; } = "aura.combat-ai.transition-audit.v1";

    public int SchemaVersion { get; set; } = 1;

    public List<CombatTransitionAuditCase> Cases { get; set; } = new();
}

public sealed class CombatTransitionAuditCase
{
    public string CaseId { get; set; } = "";

    public string CompactStateFingerprint { get; set; } = "";

    public string FullStateHash { get; set; } = "";

    public string ActionFingerprint { get; set; } = "";

    public string NextCompactStateFingerprint { get; set; } = "";

    public string NextFullStateHash { get; set; } = "";

    public string Outcome { get; set; } = "";

    public string RuntimeSettlementHash { get; set; } = "";

    public string SimulationSettlementHash { get; set; } = "";
}

public sealed class CombatTransitionAuditReport
{
    public int CaseCount { get; set; }

    public int AliasedStateCount { get; set; }

    public int DivergentTransitionCount { get; set; }

    public int RuntimeMismatchCount { get; set; }

    public List<string> Diagnostics { get; set; } = new();

    public bool Passed => CaseCount > 0
                          && DivergentTransitionCount == 0
                          && RuntimeMismatchCount == 0;
}

public static class CombatTransitionAuditAnalyzer
{
    public static CombatTransitionAuditReport Analyze(CombatTransitionAuditCorpus? corpus)
    {
        var report = new CombatTransitionAuditReport();
        if (corpus == null
            || corpus.SchemaVersion != 1
            || !string.Equals(
                corpus.Protocol,
                "aura.combat-ai.transition-audit.v1",
                StringComparison.Ordinal))
        {
            report.Diagnostics.Add("transition audit protocol is incompatible");
            return report;
        }

        var valid = (corpus.Cases ?? new List<CombatTransitionAuditCase>())
            .Where(item => item != null)
            .ToList();
        report.CaseCount = valid.Count;
        foreach (var duplicate in valid
                     .Where(item => !string.IsNullOrWhiteSpace(item.CaseId))
                     .GroupBy(item => item.CaseId, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            report.DivergentTransitionCount++;
            report.Diagnostics.Add("transition audit case id is duplicated: " + duplicate.Key);
        }
        foreach (var item in valid)
        {
            if (string.IsNullOrWhiteSpace(item.CaseId)
                || string.IsNullOrWhiteSpace(item.CompactStateFingerprint)
                || string.IsNullOrWhiteSpace(item.FullStateHash)
                || string.IsNullOrWhiteSpace(item.ActionFingerprint)
                || string.IsNullOrWhiteSpace(item.NextCompactStateFingerprint)
                || string.IsNullOrWhiteSpace(item.NextFullStateHash)
                || string.IsNullOrWhiteSpace(item.Outcome)
                || string.IsNullOrWhiteSpace(item.RuntimeSettlementHash)
                || string.IsNullOrWhiteSpace(item.SimulationSettlementHash))
            {
                report.DivergentTransitionCount++;
                report.Diagnostics.Add("transition audit case is incomplete: " + item.CaseId);
            }
            if (!string.IsNullOrWhiteSpace(item.RuntimeSettlementHash)
                && !string.IsNullOrWhiteSpace(item.SimulationSettlementHash)
                && !string.Equals(
                    item.RuntimeSettlementHash,
                    item.SimulationSettlementHash,
                    StringComparison.Ordinal))
            {
                report.RuntimeMismatchCount++;
                report.Diagnostics.Add("runtime/simulation settlement mismatch: " + item.CaseId);
            }
        }

        foreach (var state in valid.GroupBy(
                     item => item.CompactStateFingerprint,
                     StringComparer.Ordinal))
        {
            var fullStates = state.Select(item => item.FullStateHash)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (fullStates.Count > 1)
            {
                report.AliasedStateCount++;
            }
            foreach (var action in state.GroupBy(
                         item => item.ActionFingerprint,
                         StringComparer.Ordinal))
            {
                var outcomes = action.Select(item =>
                        item.NextCompactStateFingerprint + "|"
                        + item.NextFullStateHash + "|"
                        + item.Outcome + "|"
                        + item.RuntimeSettlementHash)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (outcomes.Count <= 1)
                {
                    continue;
                }
                report.DivergentTransitionCount++;
                report.Diagnostics.Add(
                    "state alias changes transition: "
                    + state.Key + "|" + action.Key);
            }
        }

        if (report.CaseCount == 0)
        {
            report.Diagnostics.Add("transition audit contains no cases");
        }
        return report;
    }
}

public static class CombatContentTrainingEpisodeProtocol
{
    public const int MaximumEpisodesPerContentSet = 8192;

    public const long MaximumArtifactBytes = 128L * 1024L * 1024L;

    public const long MaximumContentSetBytes = 256L * 1024L * 1024L;

    public static bool TryValidate(
        CombatEpisode? episode,
        string expectedContentSetHash,
        string expectedOwnerModSetHash,
        string expectedRulesetHash,
        out string reason)
    {
        if (episode == null
            || episode.ModelProtocol != CombatPolicyValueProtocol.EpisodeProtocol
            || episode.FeatureSchemaVersion
               != CombatPolicyValueProtocol.FeatureSchemaVersion
            || string.IsNullOrWhiteSpace(episode.EpisodeId)
            || !episode.Authoritative
            || episode.Campaign?.IntegrityValid != true
            || !string.Equals(
                episode.ContentSetHash,
                expectedContentSetHash,
                StringComparison.Ordinal)
            || !string.Equals(
                episode.OwnerModSetHash,
                expectedOwnerModSetHash,
                StringComparison.Ordinal)
            || !string.Equals(
                episode.RulesetHash,
                expectedRulesetHash,
                StringComparison.Ordinal))
        {
            reason = "内容训练 Episode 的协议、权威性或训练环境绑定无效";
            return false;
        }
        var frames = episode.Frames ?? new List<CombatEpisodeFrame>();
        if (frames.Count == 0 || frames.Count > 4096)
        {
            reason = "内容训练 Episode 的帧数量无效";
            return false;
        }
        foreach (var frame in frames)
        {
            var candidates = frame?.Candidates
                             ?? new List<CombatEpisodeCandidate>();
            if (frame == null
                || string.IsNullOrWhiteSpace(frame.StateFingerprint)
                || !Finite(frame.LongTermReturn)
                || !Finite(frame.WinTarget)
                || !Finite(frame.DeathTarget)
                || !Finite(frame.RemainingHpRatioTarget)
                || !Finite(frame.RemainingTurnsTarget)
                || !Finite(frame.TrainingWeight)
                || !FiniteValues(frame.StateFeatures)
                || candidates.Count == 0
                || candidates.Count > 512
                || candidates.Any(candidate => !ValidCandidate(candidate))
                || candidates.GroupBy(
                        candidate => candidate.CandidateId,
                        StringComparer.Ordinal)
                    .Any(group => group.Count() > 1)
                || !CombatPolicyValueBatchTrainer
                    .PolicyIntegrityValidForTraining(frame))
            {
                reason = "内容训练 Episode 包含无效状态、动作或策略完整性帧";
                return false;
            }
        }
        reason = "";
        return true;
    }

    private static bool ValidCandidate(CombatEpisodeCandidate? candidate)
    {
        return candidate != null
               && !string.IsNullOrWhiteSpace(candidate.CandidateId)
               && !string.IsNullOrWhiteSpace(candidate.SourceId)
               && !string.IsNullOrWhiteSpace(candidate.OwnerModId)
               && !string.Equals(
                   candidate.OwnerModId,
                   "unregistered",
                   StringComparison.OrdinalIgnoreCase)
               && candidate.SearchVisits >= 0
               && Finite(candidate.SearchPrior)
               && Finite(candidate.SearchValue)
               && Finite(candidate.SearchDeathRisk)
               && Finite(candidate.SearchMeanReturn)
               && Finite(candidate.SearchReturnStandardError)
               && Finite(candidate.SearchLowerTailMean)
               && FiniteValues(candidate.Features)
               && (candidate.SearchReturnQuantiles
                   ?? new List<double>()).Count <= 64
               && (candidate.SearchReturnQuantiles
                   ?? new List<double>()).All(Finite);
    }

    private static bool FiniteValues(
        IReadOnlyDictionary<string, double>? values)
    {
        return values != null
               && values.All(pair => !string.IsNullOrWhiteSpace(pair.Key)
                                     && Finite(pair.Value));
    }

    private static bool Finite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

public sealed class CombatContentLoadedPackage
{
    public CombatContentPackage Package { get; set; } = new();

    public string RootDirectory { get; set; } = "";

    public string PackageFingerprint { get; set; } = "";

    public CombatKnowledgePackage? Knowledge { get; set; }

    public CombatRulesetDocument? Ruleset { get; set; }

    public CombatContentFoundationOverlay? FoundationOverlay { get; set; }

    public CombatTransitionAuditCorpus? TransitionAudit { get; set; }

    public CombatTransitionAuditReport TransitionAuditReport { get; set; } = new();

    public string PolicyAdapterPath { get; set; } = "";

    public CombatLowRankPolicyAdapterDefinition? PolicyAdapter { get; set; }

    public string TransformerAdapterPath { get; set; } = "";

    public CombatTransformerLoRAAdapterDefinition? TransformerAdapter { get; set; }

    public List<string> TrainingEpisodePaths { get; set; } = new();

    public bool FoundationTrainingReady => Package.FoundationTrainingEnabled
                                           && TransitionAuditReport.Passed
                                           && Ruleset != null
                                           && FoundationOverlay != null;
}

public sealed class CombatContentPackageLoadResult
{
    public bool Success { get; set; }

    public CombatContentLoadedPackage? Loaded { get; set; }

    public List<string> Errors { get; set; } = new();
}

public static class CombatContentPackageLoader
{
    public static CombatContentPackageLoadResult Load(
        string rootDirectory,
        string registeredOwnerModId,
        string registeredPackageId)
    {
        var result = new CombatContentPackageLoadResult();
        try
        {
            var root = Path.GetFullPath(rootDirectory ?? "");
            var entry = Path.Combine(root, CombatContentPackageProtocol.EntryFileName);
            if (!Directory.Exists(root) || !File.Exists(entry))
            {
                result.Errors.Add("content package directory or package.json is missing");
                return result;
            }
            var manifestJson = File.ReadAllText(entry);
            var package = Deserialize<CombatContentPackage>(manifestJson);
            if (package == null)
            {
                result.Errors.Add("content package manifest is empty");
                return result;
            }
            ValidateIdentity(package, registeredOwnerModId, registeredPackageId, result.Errors);
            var loaded = new CombatContentLoadedPackage
            {
                Package = package,
                RootDirectory = root
            };
            loaded.Knowledge = ReadOptional<CombatKnowledgePackage>(
                root, package.Artifacts?.Knowledge, "knowledge", result.Errors);
            loaded.Ruleset = ReadOptional<CombatRulesetDocument>(
                root, package.Artifacts?.Ruleset, "ruleset", result.Errors);
            loaded.FoundationOverlay = ReadOptional<CombatContentFoundationOverlay>(
                root, package.Artifacts?.FoundationOverlay, "foundation overlay", result.Errors);
            loaded.TransitionAudit = ReadOptional<CombatTransitionAuditCorpus>(
                root, package.Artifacts?.TransitionAudit, "transition audit", result.Errors);
            loaded.TransitionAuditReport = CombatTransitionAuditAnalyzer.Analyze(
                loaded.TransitionAudit);

            foreach (var artifact in package.Artifacts?.TrainingEpisodes
                         ?? new List<CombatContentArtifactReference>())
            {
                var path = ResolveArtifact(root, artifact, "training episodes", result.Errors);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    loaded.TrainingEpisodePaths.Add(path);
                }
            }
            loaded.PolicyAdapterPath = ResolveArtifact(
                root, package.Artifacts?.PolicyAdapter, "policy adapter", result.Errors);
            if (!string.IsNullOrWhiteSpace(loaded.PolicyAdapterPath))
            {
                loaded.PolicyAdapter = ReadRequired<CombatLowRankPolicyAdapterDefinition>(
                    loaded.PolicyAdapterPath,
                    "policy adapter",
                    result.Errors);
                if (loaded.PolicyAdapter != null
                    && (!CombatModelAdapterValidator.TryValidate(
                            loaded.PolicyAdapter,
                            loaded.PolicyAdapter.Manifest.BaseModelId,
                            "",
                            out var adapterReason)
                        || !string.Equals(
                            loaded.PolicyAdapter.Manifest.OwnerModId,
                            package.OwnerModId,
                            StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(
                            loaded.PolicyAdapter.Manifest.PackageId,
                            package.PackageId,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    result.Errors.Add(
                        "policy adapter owner/package binding is invalid: "
                        + adapterReason);
                }
            }
            loaded.TransformerAdapterPath = ResolveArtifact(
                root,
                package.Artifacts?.TransformerAdapter,
                "Transformer adapter",
                result.Errors);
            if (!string.IsNullOrWhiteSpace(loaded.TransformerAdapterPath))
            {
                loaded.TransformerAdapter =
                    ReadRequired<CombatTransformerLoRAAdapterDefinition>(
                        loaded.TransformerAdapterPath,
                        "Transformer adapter",
                        result.Errors);
                if (loaded.TransformerAdapter != null
                    && (!CombatTransformerAdapterValidator.TryValidate(
                            loaded.TransformerAdapter,
                            loaded.TransformerAdapter.Manifest.BaseModelId,
                            loaded.TransformerAdapter.Manifest.BaseModelHash,
                            "",
                            out var transformerReason)
                        || !string.Equals(
                            loaded.TransformerAdapter.Manifest.OwnerModId,
                            package.OwnerModId,
                            StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(
                            loaded.TransformerAdapter.Manifest.PackageId,
                            package.PackageId,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    result.Errors.Add(
                        "Transformer adapter owner/package binding is invalid: "
                        + transformerReason);
                }
            }

            if (package.FoundationTrainingEnabled)
            {
                if (loaded.Ruleset == null || loaded.FoundationOverlay == null)
                {
                    result.Errors.Add(
                        "foundation-enabled content requires ruleset and foundation overlay artifacts");
                }
                if (loaded.Ruleset != null)
                {
                    ValidateFoundationCoverage(
                        package,
                        loaded.Ruleset,
                        result.Errors);
                }
                if (!loaded.TransitionAuditReport.Passed)
                {
                    result.Errors.Add(
                        "foundation-enabled content failed transition consistency/state alias audit: "
                        + string.Join("; ", loaded.TransitionAuditReport.Diagnostics.Take(4)));
                }
            }
            if (loaded.Knowledge != null
                && !string.Equals(
                    loaded.Knowledge.OwnerId,
                    package.OwnerModId,
                    StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add("knowledge owner does not match content package owner");
            }
            loaded.PackageFingerprint = Fingerprint(package);
            result.Loaded = loaded;
            result.Success = result.Errors.Count == 0;
            return result;
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.GetType().Name + ": " + ex.Message);
            return result;
        }
    }

    private static void ValidateIdentity(
        CombatContentPackage package,
        string registeredOwnerModId,
        string registeredPackageId,
        ICollection<string> errors)
    {
        if (package.SchemaVersion != CombatContentPackageProtocol.SchemaVersion
            || !string.Equals(
                package.Protocol,
                CombatContentPackageProtocol.Protocol,
                StringComparison.Ordinal))
        {
            errors.Add("content package protocol is incompatible");
        }
        if (string.IsNullOrWhiteSpace(package.OwnerModId)
            || !string.Equals(
                package.OwnerModId.Trim(),
                (registeredOwnerModId ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("content package owner does not match AuraShared registration owner");
        }
        if (string.IsNullOrWhiteSpace(package.PackageId)
            || !string.Equals(
                package.PackageId.Trim(),
                (registeredPackageId ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("content package id does not match AuraShared registration resource id");
        }
        if (string.IsNullOrWhiteSpace(package.PackageVersion))
        {
            errors.Add("content package version is required");
        }
        if (string.IsNullOrWhiteSpace(package.GameBuild))
        {
            errors.Add("content package game build is required");
        }
        if (package.Artifacts == null)
        {
            errors.Add("content package artifacts declaration is required");
        }
        ValidateCoverage(package, errors);
        var features = package.PublicFeatures
                       ?? new List<CombatContentPublicFeatureDeclaration>();
        var validScopes = new HashSet<string>(
            new[] { "state", "unit", "action", "statechange", "state-change" },
            StringComparer.OrdinalIgnoreCase);
        if (features.Any(feature =>
                feature == null
                || string.IsNullOrWhiteSpace(feature.Name)
                || !feature.PubliclyObservable
                || CombatPublicFeaturePolicy.IsBuiltIn(
                    FeatureScope(feature.Scope),
                    feature.Name)
                || !(string.Equals(feature.ValueType, "number", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(feature.ValueType, "boolean", StringComparison.OrdinalIgnoreCase))
                || !Finite(feature.Minimum)
                || !Finite(feature.Maximum)
                || !Finite(feature.DefaultValue)
                || string.Equals(
                       feature.ValueType,
                       "boolean",
                       StringComparison.OrdinalIgnoreCase)
                   && (feature.Minimum != 0d
                       || feature.Maximum != 1d
                       || feature.DefaultValue is not (0d or 1d))
                || feature.Maximum < feature.Minimum
                || feature.DefaultValue < feature.Minimum
                || feature.DefaultValue > feature.Maximum
                || !validScopes.Contains(feature.Scope ?? ""))
            || features.Where(feature => feature != null)
                .GroupBy(
                    feature => (feature.Scope ?? "") + "\n" + feature.Name,
                    StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
        {
            errors.Add("content package contains an invalid or hidden model feature declaration");
        }
        if ((package.Dependencies ?? new List<CombatContentPackageDependency>()).Any(
                dependency => dependency == null
                              || string.IsNullOrWhiteSpace(dependency.OwnerModId)
                              || string.IsNullOrWhiteSpace(dependency.PackageId)
                              || string.Equals(
                                  dependency.OwnerModId,
                                  package.OwnerModId,
                                  StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(
                                     dependency.PackageId,
                                     package.PackageId,
                                     StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("content package contains an invalid or self dependency");
        }
        var artifacts = new[]
            {
                package.Artifacts?.Knowledge,
                package.Artifacts?.Ruleset,
                package.Artifacts?.FoundationOverlay,
                package.Artifacts?.TransitionAudit,
                package.Artifacts?.PolicyAdapter,
                package.Artifacts?.TransformerAdapter
            }
            .Concat(package.Artifacts?.TrainingEpisodes
                    ?? new List<CombatContentArtifactReference>())
            .Where(artifact => artifact != null)
            .ToList();
        if (artifacts.Any(artifact =>
                string.IsNullOrWhiteSpace(artifact!.Path)
                || string.IsNullOrWhiteSpace(artifact.Sha256)))
        {
            errors.Add("content package artifact declarations require path and SHA-256");
        }
        if (artifacts.GroupBy(
                artifact => artifact!.Path.Trim().Replace('\\', '/'),
                StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            errors.Add("content package contains duplicate artifact paths");
        }
    }

    private static void ValidateCoverage(
        CombatContentPackage package,
        ICollection<string> errors)
    {
        var coverage = package.DeclaredCoverage;
        if (coverage == null || coverage.SchemaVersion != 1)
        {
            errors.Add("content package declared coverage is required and must use schema 1");
            return;
        }
        var collections = new[]
        {
            coverage.CardIds,
            coverage.RoleSkillCardIds,
            coverage.EnemyIds,
            coverage.StatusIds,
            coverage.RelicIds,
            coverage.BlessingIds
        };
        if (collections.Any(values => values == null
                                      || values.Any(string.IsNullOrWhiteSpace)
                                      || values.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                                         != values.Count))
        {
            errors.Add("content package declared coverage contains blank or duplicate ids");
        }
        if (package.FoundationTrainingEnabled && !coverage.EntityCoverageKnown)
        {
            errors.Add("foundation-enabled content requires authoritative entity coverage");
        }
    }

    private static bool Finite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static CombatPublicFeatureScope FeatureScope(string? value)
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

    private static void ValidateFoundationCoverage(
        CombatContentPackage package,
        CombatRulesetDocument ruleset,
        ICollection<string> errors)
    {
        var coverage = package.DeclaredCoverage;
        if (coverage == null)
        {
            return;
        }
        var cards = ruleset.Cards ?? new List<CombatCardDefinition>();
        var enemies = ruleset.Enemies ?? new List<CombatEnemyDefinition>();
        var statuses = ruleset.Statuses ?? new List<CombatStatusDefinition>();
        if (cards.Any(item => item == null
                              || !string.Equals(
                                  item.OwnerModId,
                                  package.OwnerModId,
                                  StringComparison.OrdinalIgnoreCase))
            || enemies.Any(item => item == null
                                    || !string.Equals(
                                        item.OwnerModId,
                                        package.OwnerModId,
                                        StringComparison.OrdinalIgnoreCase))
            || statuses.Any(item => item == null
                                     || !string.Equals(
                                         item.OwnerModId,
                                         package.OwnerModId,
                                         StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("foundation ruleset contains an entity owned by another MOD");
        }
        var declaredCards = (coverage.CardIds ?? new List<string>())
            .Concat(coverage.RoleSkillCardIds ?? new List<string>());
        if (!ExactIds(declaredCards, cards.Select(item => item?.CardId ?? ""))
            || !ExactIds(
                coverage.EnemyIds,
                enemies.Select(item => item?.EnemyId ?? ""))
            || !ExactIds(
                coverage.StatusIds,
                statuses.Select(item => item?.StatusId ?? "")))
        {
            errors.Add(
                "foundation ruleset card/enemy/status ids do not exactly match declared coverage");
        }
    }

    private static bool ExactIds(
        IEnumerable<string>? declared,
        IEnumerable<string>? actual)
    {
        var declaredIds = (declared ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        var actualIds = (actual ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        return declaredIds.Count
               == declaredIds.Distinct(StringComparer.OrdinalIgnoreCase).Count()
               && actualIds.Count
               == actualIds.Distinct(StringComparer.OrdinalIgnoreCase).Count()
               && new HashSet<string>(
                   declaredIds,
                   StringComparer.OrdinalIgnoreCase).SetEquals(actualIds);
    }

    private static T? ReadOptional<T>(
        string root,
        CombatContentArtifactReference? artifact,
        string label,
        ICollection<string> errors)
        where T : class
    {
        var path = ResolveArtifact(root, artifact, label, errors);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        try
        {
            return Deserialize<T>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            errors.Add(label + " JSON is invalid: " + ex.Message);
            return null;
        }
    }

    private static T? ReadRequired<T>(
        string path,
        string label,
        ICollection<string> errors)
        where T : class
    {
        try
        {
            return Deserialize<T>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            errors.Add(label + " JSON is invalid: " + ex.Message);
            return null;
        }
    }

    private static string ResolveArtifact(
        string root,
        CombatContentArtifactReference? artifact,
        string label,
        ICollection<string> errors)
    {
        if (artifact == null || string.IsNullOrWhiteSpace(artifact.Path))
        {
            return "";
        }
        var path = Path.GetFullPath(Path.Combine(
            root,
            artifact.Path.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInside(path, root) || !File.Exists(path))
        {
            errors.Add(label + " artifact is missing or escapes package root: " + artifact.Path);
            return "";
        }
        var declared = (artifact.Sha256 ?? "").Trim();
        var expected = declared.ToLowerInvariant();
        if (expected.Length != 64
            || expected.Any(character => !Uri.IsHexDigit(character))
            || !string.Equals(declared, expected, StringComparison.Ordinal))
        {
            errors.Add(label + " artifact requires a lowercase SHA-256 digest");
            return "";
        }
        var actual = Sha256File(path);
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            errors.Add(label + " artifact SHA-256 mismatch: " + artifact.Path);
            return "";
        }
        return path;
    }

    private static string Fingerprint(CombatContentPackage package)
    {
        var artifacts = new[]
            {
                package.Artifacts?.Knowledge,
                package.Artifacts?.Ruleset,
                package.Artifacts?.FoundationOverlay,
                package.Artifacts?.TransitionAudit,
                package.Artifacts?.PolicyAdapter,
                package.Artifacts?.TransformerAdapter
            }
            .Concat(package.Artifacts?.TrainingEpisodes
                    ?? new List<CombatContentArtifactReference>())
            .Where(item => item != null)
            .Select(item => item!.Path.Trim().Replace('\\', '/') + "#" + item.Sha256.Trim().ToLowerInvariant())
            .OrderBy(item => item, StringComparer.Ordinal);
        var dependencies = (package.Dependencies
                            ?? new List<CombatContentPackageDependency>())
            .Where(item => item != null)
            .Select(item => item.OwnerModId.Trim().ToLowerInvariant() + ":"
                            + item.PackageId.Trim().ToLowerInvariant() + "@"
                            + (item.MinimumVersion ?? "").Trim() + "#"
                            + (item.Optional ? "optional" : "required"))
            .OrderBy(item => item, StringComparer.Ordinal);
        var features = (package.PublicFeatures
                        ?? new List<CombatContentPublicFeatureDeclaration>())
            .Where(item => item != null)
            .Select(item => (item.Scope ?? "").Trim().ToLowerInvariant() + ":"
                            + item.Name.Trim().ToLowerInvariant() + "#"
                            + (item.ValueType ?? "").Trim().ToLowerInvariant() + "#"
                            + Number(item.Minimum) + ":" + Number(item.Maximum)
                            + ":" + Number(item.DefaultValue) + "#"
                            + (item.PubliclyObservable ? "public" : "hidden"))
            .OrderBy(item => item, StringComparer.Ordinal);
        var coverage = package.DeclaredCoverage
                       ?? new CombatFoundationDeclaredCoverage();
        var coverageIdentity = string.Join(
            "\n",
            new[]
            {
                "coverage-source=" + (coverage.Source ?? "").Trim(),
                "coverage-known=" + coverage.EntityCoverageKnown,
                IdSet("cards", coverage.CardIds),
                IdSet("role-skills", coverage.RoleSkillCardIds),
                IdSet("enemies", coverage.EnemyIds),
                IdSet("statuses", coverage.StatusIds),
                IdSet("relics", coverage.RelicIds),
                IdSet("blessings", coverage.BlessingIds)
            });
        return Sha256Text(
            "content-package-fingerprint-v3\n"
            + package.OwnerModId.Trim().ToLowerInvariant() + "\n"
            + package.PackageId.Trim().ToLowerInvariant() + "\n"
            + package.PackageVersion.Trim() + "\n"
            + "foundation=" + package.FoundationTrainingEnabled + "\n"
            + coverageIdentity + "\n"
            + string.Join("\n", dependencies) + "\n"
            + string.Join("\n", features) + "\n"
            + string.Join("\n", artifacts));
    }

    private static string IdSet(string label, IEnumerable<string>? values)
    {
        return label + "=" + string.Join(
            ",",
            (values ?? Array.Empty<string>())
            .Select(value => (value ?? "").Trim().ToLowerInvariant())
            .OrderBy(value => value, StringComparer.Ordinal));
    }

    private static string Number(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static bool IsInside(string path, string root)
    {
        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(
                   fullRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        using var hash = SHA256.Create();
        return Hex(hash.ComputeHash(stream));
    }

    private static string Sha256Text(string value)
    {
        using var hash = SHA256.Create();
        return Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")));
    }

    private static string Hex(IEnumerable<byte> bytes)
    {
        return string.Concat(bytes.Select(value => value.ToString("x2")));
    }

    private static T? Deserialize<T>(string json)
        where T : class
    {
#if NET8_0_OR_GREATER
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        });
#else
        return JsonConvert.DeserializeObject<T>(json);
#endif
    }
}

public sealed class CombatContentSetSnapshot
{
    public string ContentSetHash { get; set; } = CombatContentSetProtocol.EmptyContentSetHash;

    public string OwnerModSetHash { get; set; } = CombatContentSetProtocol.EmptyOwnerModSetHash;

    public List<CombatContentSetEntry> Packages { get; set; } = new();
}

public sealed class CombatContentSetEntry
{
    public string OwnerModId { get; set; } = "";

    public string PackageId { get; set; } = "";

    public string PackageVersion { get; set; } = "";

    public string PackageFingerprint { get; set; } = "";

    public bool FoundationTrainingReady { get; set; }
}

public static class CombatContentSetProtocol
{
    // Preserve the shipped no-content identity so base models do not require a one-time
    // revalidation merely because game-build metadata stopped participating in hashes.
    public static readonly string EmptyContentSetHash = Hash("content-set-v1\nempty");

    public static readonly string EmptyOwnerModSetHash = Hash("owner-mod-set-v1\nempty");

    public static CombatContentSetSnapshot Create(
        IEnumerable<CombatContentLoadedPackage>? source,
        string gameBuild,
        string rulesetHash = "",
        string nativeProgramHash = "")
    {
        var entries = (source ?? Array.Empty<CombatContentLoadedPackage>())
            .Where(item => item != null)
            .Select(item => new CombatContentSetEntry
            {
                OwnerModId = item.Package.OwnerModId,
                PackageId = item.Package.PackageId,
                PackageVersion = item.Package.PackageVersion,
                PackageFingerprint = item.PackageFingerprint,
                FoundationTrainingReady = item.FoundationTrainingReady
            })
            .OrderBy(item => item.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var packageIdentity = string.Join(
            "\n",
            entries.Select(item =>
                item.OwnerModId.Trim().ToLowerInvariant() + ":"
                + item.PackageId.Trim().ToLowerInvariant() + "@"
                + item.PackageVersion.Trim() + "#"
                + item.PackageFingerprint.Trim().ToLowerInvariant()));
        return new CombatContentSetSnapshot
        {
            Packages = entries,
            OwnerModSetHash = entries.Count == 0
                ? EmptyOwnerModSetHash
                : Hash("owner-mod-set-v1\n" + string.Join(
                    "\n",
                    entries.Select(item => item.OwnerModId.Trim().ToLowerInvariant())
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(item => item, StringComparer.Ordinal))),
            ContentSetHash = entries.Count == 0
                ? EmptyContentSetHash
                : Hash(
                    "content-set-v2\n"
                    + (rulesetHash ?? "").Trim() + "\n"
                    + (nativeProgramHash ?? "").Trim() + "\n"
                    + packageIdentity)
        };
    }

    private static string Hash(string value)
    {
        using var hash = SHA256.Create();
        return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""))
            .Select(item => item.ToString("x2")));
    }
}

public static class CombatContentFoundationMerger
{
    public static CombatRulesetDocument MergeRulesets(
        CombatRulesetDocument basis,
        IEnumerable<CombatContentLoadedPackage>? packages)
    {
        var result = new CombatRulesetDocument
        {
            Version = basis?.Version ?? "1",
            Cards = (basis?.Cards ?? new List<CombatCardDefinition>())
                .Select(item => item.Clone()).ToList(),
            Enemies = (basis?.Enemies ?? new List<CombatEnemyDefinition>())
                .Select(item => item.Clone()).ToList(),
            Statuses = (basis?.Statuses ?? new List<CombatStatusDefinition>())
                .Select(item => item.Clone()).ToList()
        };
        foreach (var package in Ready(packages))
        {
            MergeOwned(
                result.Cards,
                package.Ruleset!.Cards,
                item => item.CardId,
                item => item.OwnerModId,
                package.Package.OwnerModId,
                "card");
            MergeOwned(
                result.Enemies,
                package.Ruleset.Enemies,
                item => item.EnemyId,
                item => item.OwnerModId,
                package.Package.OwnerModId,
                "enemy");
            MergeOwned(
                result.Statuses,
                package.Ruleset.Statuses,
                item => item.StatusId,
                item => item.OwnerModId,
                package.Package.OwnerModId,
                "status");
        }
        return result;
    }

    public static void ApplyCampaignOverlays(
        CombatCampaignDefinition campaign,
        IEnumerable<CombatContentLoadedPackage>? packages)
    {
        if (campaign == null)
        {
            throw new ArgumentNullException(nameof(campaign));
        }
        foreach (var package in Ready(packages))
        {
            var overlay = package.FoundationOverlay!;
            MergeById(campaign.Enemies, overlay.Enemies, item => item.EnemyId);
            MergeById(campaign.Encounters, overlay.Encounters, item => item.EncounterId);
            MergeById(campaign.Rewards, overlay.Rewards, item => item.RewardId);
            MergeById(campaign.Strategies, overlay.Strategies, item => item.StrategyId);
            MergeById(campaign.Difficulties, overlay.Difficulties, item => item.DifficultyId);
            AddDistinct(campaign.EnabledRewardCardPackIds, overlay.EnabledRewardCardPackIds);
            MergeDictionary(campaign.RolePrior, overlay.RolePrior);
            MergeDictionary(campaign.BuildTendency, overlay.BuildTendency);
        }
    }

    private static IEnumerable<CombatContentLoadedPackage> Ready(
        IEnumerable<CombatContentLoadedPackage>? packages)
    {
        return (packages ?? Array.Empty<CombatContentLoadedPackage>())
            .Where(item => item.FoundationTrainingReady)
            .OrderBy(item => item.Package.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Package.PackageId, StringComparer.OrdinalIgnoreCase);
    }

    private static void MergeOwned<T>(
        List<T> target,
        IEnumerable<T> additions,
        Func<T, string> id,
        Func<T, string> owner,
        string expectedOwner,
        string label)
    {
        foreach (var item in additions ?? Array.Empty<T>())
        {
            if (item == null || string.IsNullOrWhiteSpace(id(item)))
            {
                throw new InvalidDataException("content " + label + " id is required");
            }
            if (!string.Equals(
                    owner(item),
                    expectedOwner,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "content " + label + " owner mismatch: " + id(item));
            }
            if (target.Any(existing => string.Equals(
                    id(existing), id(item), StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    "content " + label + " conflicts with existing id: " + id(item));
            }
            target.Add(item);
        }
    }

    private static void MergeById<T>(
        List<T> target,
        IEnumerable<T> additions,
        Func<T, string> id)
    {
        foreach (var item in additions ?? Array.Empty<T>())
        {
            var value = id(item);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException("foundation overlay contains an empty identity");
            }
            if (target.Any(existing => string.Equals(
                    id(existing), value, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    "foundation overlay conflicts with existing identity: " + value);
            }
            target.Add(item);
        }
    }

    private static void AddDistinct(ICollection<string> target, IEnumerable<string> additions)
    {
        var known = new HashSet<string>(target, StringComparer.OrdinalIgnoreCase);
        foreach (var item in additions ?? Array.Empty<string>())
        {
            var value = (item ?? "").Trim();
            if (value.Length > 0 && known.Add(value))
            {
                target.Add(value);
            }
        }
    }

    private static void MergeDictionary(
        IDictionary<string, double> target,
        IEnumerable<KeyValuePair<string, double>> additions)
    {
        foreach (var pair in additions ?? Array.Empty<KeyValuePair<string, double>>())
        {
            if (!string.IsNullOrWhiteSpace(pair.Key))
            {
                target[pair.Key.Trim()] = pair.Value;
            }
        }
    }
}
