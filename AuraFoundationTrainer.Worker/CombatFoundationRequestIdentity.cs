using System.IO;
using System.Security.Cryptography;
using System.Text;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AuraFoundationTrainer.Worker;

/// <summary>
/// Owns the durable request identity used by exact checkpoint continuation.
/// Runtime-generated random seeds are intentionally excluded: the persisted
/// resume seed plan is authoritative once a checkpoint has been selected.
/// </summary>
internal static class CombatFoundationRequestIdentity
{
    internal const string CurrentProtocol =
        "foundation-continuation-v4-canonical-campaign";

    private const string PersistedSeedProtocol =
        "foundation-continuation-v3-persisted-seed-plan";

    private const string LegacyProtocol =
        "foundation-continuation-v2-stable-budget";

    internal static string CreateFingerprint(
        CombatFoundationWorkerJob job,
        string rulesetHash)
    {
        return Hash(BuildPayload(
            job,
            rulesetHash,
            CurrentProtocol,
            BuildTeacherIdentity(job.Request.TransformerTeacher),
            canonicalizeCampaigns: true));
    }

    internal static string CreateLegacyFingerprint(
        CombatFoundationWorkerJob job,
        string rulesetHash,
        int transformerRandomSeed)
    {
        var teacher = job.Request.TransformerTeacher;
        if (teacher == null)
        {
            return Hash(BuildPayload(
                job,
                rulesetHash,
                LegacyProtocol,
                HashCompact(null),
                canonicalizeCampaigns: false));
        }

        var currentSeed = teacher.RandomSeed;
        try
        {
            teacher.RandomSeed = transformerRandomSeed;
            return Hash(BuildPayload(
                job,
                rulesetHash,
                LegacyProtocol,
                HashCompact(teacher),
                canonicalizeCampaigns: false));
        }
        finally
        {
            teacher.RandomSeed = currentSeed;
        }
    }

    internal static string CreatePersistedSeedFingerprint(
        CombatFoundationWorkerJob job,
        string rulesetHash)
    {
        return Hash(BuildPayload(
            job,
            rulesetHash,
            PersistedSeedProtocol,
            BuildTeacherIdentity(job.Request.TransformerTeacher),
            canonicalizeCampaigns: true));
    }

    internal static Dictionary<string, string> CreateFields(
        CombatFoundationWorkerJob job,
        string rulesetHash)
    {
        return CreateFields(
            job,
            rulesetHash,
            CurrentProtocol,
            canonicalizeCampaigns: true);
    }

    private static Dictionary<string, string> CreateFields(
        CombatFoundationWorkerJob job,
        string rulesetHash,
        string protocol,
        bool canonicalizeCampaigns)
    {
        var payload = JObject.FromObject(
            BuildPayload(
                job,
                rulesetHash,
                protocol,
                BuildTeacherIdentity(job.Request.TransformerTeacher),
                canonicalizeCampaigns),
            CreateSerializer());
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        Flatten(payload, "", fields);
        return fields;
    }

    internal static bool Matches(
        CombatFoundationWorkerJob job,
        CombatFoundationWorkerCheckpoint checkpoint,
        string rulesetHash,
        out string diagnostic)
    {
        if (!string.Equals(
                checkpoint.RulesetHash,
                rulesetHash,
                StringComparison.Ordinal))
        {
            diagnostic = "RulesetHash: checkpoint="
                         + checkpoint.RulesetHash
                         + ", request="
                         + rulesetHash;
            return false;
        }

        var currentFingerprint = CreateFingerprint(job, rulesetHash);
        if (string.Equals(
                checkpoint.RequestFingerprint,
                currentFingerprint,
                StringComparison.Ordinal))
        {
            diagnostic = "exact identity";
            return true;
        }

        var checkpointFields = checkpoint.RequestIdentityFields
                               ?? new Dictionary<string, string>();
        var currentFields = CreateFields(job, rulesetHash);
        if (checkpointFields.Count > 0
            && FieldsEqual(checkpointFields, currentFields))
        {
            diagnostic = "structured identity matched";
            return true;
        }

        // v3 checkpoints were always produced by a Worker after the job crossed
        // the JSON process boundary. Rebuild that identity from a canonicalized
        // campaign so an equivalent in-memory Control Center request compares
        // exactly with the persisted Worker request.
        var persistedSeedFingerprint = CreatePersistedSeedFingerprint(
            job,
            rulesetHash);
        if (string.Equals(
                checkpoint.RequestFingerprint,
                persistedSeedFingerprint,
                StringComparison.Ordinal))
        {
            diagnostic =
                "legacy v3 identity matched after canonical Campaign normalization";
            return true;
        }
        if (checkpointFields.Count > 0
            && FieldsEqual(
                checkpointFields,
                CreateFields(
                    job,
                    rulesetHash,
                    PersistedSeedProtocol,
                    canonicalizeCampaigns: true)))
        {
            diagnostic =
                "legacy v3 structured identity matched after canonical Campaign normalization";
            return true;
        }

        // Schema-v16 checkpoints created by the v2 identity protocol included
        // TransformerTeacher.RandomSeed. Reconstruct that legacy identity from
        // the checkpoint seed plan instead of the newly generated request seed.
        var persistedRunSeed = checkpoint.Resume?.RunSeed ?? 0UL;
        if (persistedRunSeed != 0UL)
        {
            var legacyFingerprint = CreateLegacyFingerprint(
                job,
                rulesetHash,
                unchecked((int)persistedRunSeed));
            if (string.Equals(
                    checkpoint.RequestFingerprint,
                    legacyFingerprint,
                    StringComparison.Ordinal))
            {
                diagnostic =
                    "legacy v2 identity matched after restoring the persisted seed plan";
                return true;
            }
        }

        if (TryMatchSourceJobIdentity(
                job,
                checkpoint,
                rulesetHash,
                currentFields,
                out var sourceJobDiagnostic))
        {
            diagnostic = sourceJobDiagnostic;
            return true;
        }

        diagnostic = checkpointFields.Count == 0
            ? "legacy checkpoint identity differs after persisted-seed normalization"
              + (string.IsNullOrWhiteSpace(sourceJobDiagnostic)
                  ? ""
                  : "; " + sourceJobDiagnostic)
            : DescribeDifferences(checkpointFields, currentFields);
        return false;
    }

    private static bool TryMatchSourceJobIdentity(
        CombatFoundationWorkerJob currentJob,
        CombatFoundationWorkerCheckpoint checkpoint,
        string rulesetHash,
        IReadOnlyDictionary<string, string> currentFields,
        out string diagnostic)
    {
        diagnostic = "";
        try
        {
            if (string.IsNullOrWhiteSpace(currentJob.CheckpointCatalogPath)
                || !CombatFoundationPathRuntime.FileExists(
                    currentJob.CheckpointCatalogPath))
            {
                diagnostic = "source-job identity unavailable: checkpoint catalog is missing";
                return false;
            }
            var catalog = JsonConvert.DeserializeObject<
                CombatFoundationCheckpointCatalog>(
                CombatFoundationCheckpointStorage.ReadAllTextShared(
                    currentJob.CheckpointCatalogPath));
            var selectedPath = currentJob.ResumeCheckpointPath;
            var entry = catalog?.Entries?.FirstOrDefault(item =>
                item != null
                && string.Equals(
                    CombatFoundationPathRuntime.Normalize(item.CheckpointPath),
                    CombatFoundationPathRuntime.Normalize(selectedPath),
                    StringComparison.OrdinalIgnoreCase));
            if (entry == null
                || !entry.SupportsExact
                || !string.Equals(
                    entry.RequestFingerprint,
                    checkpoint.RequestFingerprint,
                    StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(entry.SourceJobId)
                || entry.SourceJobId.Any(character =>
                    !char.IsLetterOrDigit(character)
                    && character != '-'
                    && character != '_'))
            {
                diagnostic =
                    "source-job identity unavailable: exact catalog binding is invalid";
                return false;
            }

            var catalogDirectory = Path.GetDirectoryName(
                CombatFoundationPathRuntime.Normalize(
                    currentJob.CheckpointCatalogPath));
            var checkpointRoot = string.IsNullOrWhiteSpace(catalogDirectory)
                ? null
                : Directory.GetParent(catalogDirectory);
            var resultsRoot = checkpointRoot?.Parent?.FullName;
            if (string.IsNullOrWhiteSpace(resultsRoot))
            {
                diagnostic =
                    "source-job identity unavailable: results root cannot be resolved";
                return false;
            }
            var sourceJobPath = Path.Combine(
                resultsRoot,
                entry.SourceJobId,
                "foundation-worker-job.json");
            if (!CombatFoundationPathRuntime.FileExists(sourceJobPath))
            {
                diagnostic = "source-job identity unavailable: " + sourceJobPath;
                return false;
            }
            var sourceJob = JsonConvert.DeserializeObject<CombatFoundationWorkerJob>(
                CombatFoundationCheckpointStorage.ReadAllTextShared(
                    sourceJobPath),
                CreateProcessBoundarySettings());
            if (sourceJob == null
                || sourceJob.Request == null
                || sourceJob.Request.RunSeed == 0UL
                || sourceJob.Request.RunSeed != checkpoint.Resume?.RunSeed)
            {
                diagnostic =
                    "source-job identity unavailable: persisted RunSeed binding differs";
                return false;
            }
            var sourceFields = CreateFields(sourceJob, rulesetHash);
            if (!FieldsEqual(sourceFields, currentFields))
            {
                diagnostic = "source-job fields differ: "
                             + DescribeDifferences(sourceFields, currentFields);
                return false;
            }
            diagnostic =
                "legacy source-job identity matched; runtime-generated seeds, archive projection hash, and duplicated Campaign defaults were normalized";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = "source-job identity check failed: " + ex.Message;
            return false;
        }
    }

    internal static string DescribeDifferences(
        IReadOnlyDictionary<string, string> checkpoint,
        IReadOnlyDictionary<string, string> current,
        int maximumFields = 12)
    {
        var differences = checkpoint.Keys
            .Concat(current.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .Where(key => !checkpoint.TryGetValue(key, out var left)
                          || !current.TryGetValue(key, out var right)
                          || !string.Equals(left, right, StringComparison.Ordinal))
            .Take(Math.Max(1, maximumFields))
            .Select(key => key
                           + ": checkpoint="
                           + Display(checkpoint.TryGetValue(key, out var left)
                               ? left
                               : "<missing>")
                           + ", request="
                           + Display(current.TryGetValue(key, out var right)
                               ? right
                               : "<missing>"))
            .ToList();
        return differences.Count == 0
            ? "identity fingerprint differs but no structured field difference was found"
            : string.Join("; ", differences);
    }

    private static object BuildPayload(
        CombatFoundationWorkerJob job,
        string rulesetHash,
        string protocol,
        object transformerTeacherIdentity,
        bool canonicalizeCampaigns)
    {
        var request = job.Request;
        var training = request.Training;
        return new
        {
            Protocol = protocol,
            RulesetHash = rulesetHash,
            request.ContentSetHash,
            request.OwnerModSetHash,
            FeatureSchemaVersion = CombatPolicyValueProtocol.FeatureSchemaVersion,
            request.DecisionProfile,
            Profile = HashCompact(request.Profile),
            request.TrainingPolicyVersion,
            CombatPolicyValueProtocol.TrainingSemanticsVersion,
            SemanticCanaryVersion =
                CombatFoundationSemanticProbeResult.CurrentCanaryVersion,
            request.TrainingCampaignsPerIteration,
            request.ArenaCampaignsPerDifficulty,
            request.ArenaConfirmationCampaignsPerDifficulty,
            request.NormalValidationCampaigns,
            request.AdvancedValidationCampaigns,
            request.CapabilityProbeCampaignsPerDifficulty,
            request.RequireCapabilityProbeBaselineGain,
            request.CapabilityProbeMinimumVictoryGain,
            request.CapabilityProbeMinimumDepthGain,
            request.EnableEarlyValidationStop,
            request.ValidationEarlyStopBatchSize,
            request.EnableCurriculum,
            request.EnableStratifiedReplay,
            request.EnablePrioritizedReplay,
            request.EnableHardSeedCurriculum,
            request.EnableCounterfactualHardEncounters,
            request.EnableSuccessCaseArchive,
            request.EnableArenaRecovery,
            request.ArenaInvalidRetryCount,
            request.ArenaInvalidRateLimit,
            request.EnableTuningArena,
            request.TuningNormalCampaigns,
            request.TuningAdvancedCampaigns,
            request.EnableProgressiveTuning,
            request.TuningScreeningNormalCampaigns,
            request.TuningScreeningAdvancedCampaigns,
            request.TuningFinalistCount,
            request.NormalAcceptanceRate,
            request.AdvancedAcceptanceRate,
            request.MinimumArenaDiscordantPairs,
            request.MaximumOfflineHeadRegression,
            request.HardSeedReplayShare,
            HardEncounterWeights = HashCompact(request.HardEncounterWeights),
            request.MinimumAdvancedReplayShare,
            request.MinimumAdvancedDefeatReplayShare,
            request.ExpertReplayEpisodeLimit,
            request.AuthoritativeContentReplayShare,
            request.SelfPlayExplorationProbability,
            request.SelfPlayExplorationTemperature,
            CampaignId = request.TrainingCampaign?.CampaignId ?? "",
            CampaignVersion = request.TrainingCampaign?.CampaignVersion ?? "",
            TrainingCampaign = CampaignFingerprint(
                request.TrainingCampaign,
                canonicalizeCampaigns),
            ValidationCampaign = CampaignFingerprint(
                request.ValidationCampaign,
                canonicalizeCampaigns),
            training.StateDimensions,
            training.ActionDimensions,
            training.HiddenDimensions,
            training.GradientShardCount,
            training.FeatureEncodingMode,
            training.LearningRate,
            training.L2,
            training.Epochs,
            training.MinimumEpochs,
            training.EarlyStoppingPatience,
            training.EarlyStoppingMinimumDelta,
            training.BatchSize,
            training.EnableFrameStratification,
            training.EnableEndTurnSpecialization,
            training.EndTurnFrameWeight,
            training.MaximumUnsafeEndTurnFrameShare,
            training.UnsafeEndTurnRiskAuxiliaryShare,
            training.PolicyTargetTemperature,
            training.MaximumPolicyTargetProbability,
            training.MaximumFrameStratumWeight,
            training.MaximumFramesPerEpisode,
            training.ReplayEpisodeLimit,
            training.ReplayFrameLimit,
            training.ReplayEstimatedBytesLimit,
            training.RetainedModelCandidates,
            TransformerTeacher = transformerTeacherIdentity,
            request.FinalizeTransformerTeacher
        };
    }

    private static string CampaignFingerprint(
        CombatCampaignDefinition? campaign,
        bool canonicalize)
    {
        if (campaign == null)
        {
            return "";
        }
        if (!canonicalize)
        {
            return CombatCampaignFoundationTrainer.CampaignFingerprint(campaign);
        }

        // The Control Center constructs campaigns in memory, while the Worker
        // receives them through JSON. A compact round-trip normalizes null,
        // empty and property-initializer defaults before identity is computed.
        var canonical = JsonConvert.DeserializeObject<CombatCampaignDefinition>(
                            SerializeCompact(campaign),
                            CreateSettings())
                        ?? throw new InvalidDataException(
                            "Campaign identity normalization returned null");
        NormalizeSetLikeCampaignCollections(canonical);
        return CombatCampaignFoundationTrainer.CampaignFingerprint(canonical);
    }

    private static void NormalizeSetLikeCampaignCollections(
        CombatCampaignDefinition campaign)
    {
        // Older Newtonsoft process boundaries populated these non-empty
        // property-initializer lists by appending the JSON values. They are
        // sets in the Campaign protocol, so repeated defaults must not make an
        // otherwise identical legacy checkpoint look like a different job.
        // Do not apply this to ordered multisets such as Player.Deck.
        campaign.AttributeIds = DistinctIds(campaign.AttributeIds);
        campaign.EnabledRewardCardPackIds = DistinctIds(
            campaign.EnabledRewardCardPackIds);
        campaign.CardRewardEncounterKinds = (campaign.CardRewardEncounterKinds
                                              ?? new List<
                                                  CombatCampaignEncounterKind>())
            .Distinct()
            .ToList();
    }

    private static List<string> DistinctIds(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static JObject BuildTeacherIdentity(
        CombatTransformerTeacherOptions? teacher)
    {
        if (teacher == null)
        {
            return new JObject();
        }
        var identity = JObject.FromObject(teacher, CreateSerializer());
        identity.Remove(nameof(CombatTransformerTeacherOptions.RandomSeed));
        return identity;
    }

    private static void Flatten(
        JToken token,
        string path,
        IDictionary<string, string> fields)
    {
        if (token is JObject objectToken)
        {
            foreach (var property in objectToken.Properties())
            {
                Flatten(
                    property.Value,
                    string.IsNullOrEmpty(path)
                        ? property.Name
                        : path + "." + property.Name,
                    fields);
            }
            return;
        }
        if (token is JArray arrayToken)
        {
            for (var index = 0; index < arrayToken.Count; index++)
            {
                Flatten(arrayToken[index], path + "[" + index + "]", fields);
            }
            return;
        }
        fields[path] = token.Type == JTokenType.Null
            ? "null"
            : token.ToString(Formatting.None);
    }

    private static bool FieldsEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        return left.Count == right.Count
               && left.All(pair => right.TryGetValue(pair.Key, out var value)
                                   && string.Equals(
                                       pair.Value,
                                       value,
                                       StringComparison.Ordinal));
    }

    private static string Hash(object value)
    {
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(SerializeCompact(value))));
    }

    private static string HashCompact(object? value)
    {
        var payload = value == null ? "null" : SerializeCompact(value);
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(payload)));
    }

    private static JsonSerializer CreateSerializer()
    {
        return JsonSerializer.Create(CreateSettings());
    }

    private static JsonSerializerSettings CreateSettings()
    {
        return new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            FloatFormatHandling = FloatFormatHandling.DefaultValue,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            ContractResolver = WorkerCompactEpisodeContractResolver.Instance
        };
    }

    private static JsonSerializerSettings CreateProcessBoundarySettings()
    {
        return new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };
    }

    private static string SerializeCompact(object value)
    {
        return JsonConvert.SerializeObject(
            value,
            Formatting.None,
            CreateSettings());
    }

    private static string Display(string value)
    {
        const int maximum = 96;
        return value.Length <= maximum
            ? value
            : value[..maximum] + "...";
    }
}
