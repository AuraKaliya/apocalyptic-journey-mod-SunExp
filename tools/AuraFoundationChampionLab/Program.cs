using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraToolsExp.Dll.Features.AutoBattle;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

Console.OutputEncoding = Encoding.UTF8;
var resultAPath = Argument(args, "--result-a");
var resultBPath = Argument(args, "--result-b");
var jobPath = Argument(args, "--job");
var outputDirectory = Argument(args, "--output");
if (string.IsNullOrWhiteSpace(resultAPath)
    || string.IsNullOrWhiteSpace(resultBPath)
    || string.IsNullOrWhiteSpace(jobPath)
    || string.IsNullOrWhiteSpace(outputDirectory)
    || !File.Exists(resultAPath)
    || !File.Exists(resultBPath)
    || !File.Exists(jobPath))
{
    Console.Error.WriteLine(
        "Usage: AuraFoundationChampionLab "
        + "--result-a <foundation-worker-result.json> "
        + "--result-b <foundation-worker-result.json> "
        + "--job <foundation-worker-job.json> "
        + "--output <directory> "
        + "[--ruleset <ruleset.json>] [--campaign <campaign.json>] "
        + "[--campaigns 64] [--seed-start 3000000] "
        + "[--parallelism 16]");
    return 2;
}

var settings = new JsonSerializerSettings
{
    Formatting = Formatting.Indented,
    MissingMemberHandling = MissingMemberHandling.Ignore,
    NullValueHandling = NullValueHandling.Include
};
settings.Converters.Add(new StringEnumConverter());

var resultA = Read<CombatFoundationWorkerResult>(resultAPath, settings);
var resultB = Read<CombatFoundationWorkerResult>(resultBPath, settings);
var job = Read<CombatFoundationWorkerJob>(jobPath, settings);
var championA = RequiredChampion(resultA, "A");
var championB = RequiredChampion(resultB, "B");
Directory.CreateDirectory(outputDirectory);

var artifactA = Artifact("A", resultAPath, resultA, championA);
var artifactB = Artifact("B", resultBPath, resultB, championB);
Write(
    Path.Combine(outputDirectory, "champion-a.json"),
    artifactA,
    settings);
Write(
    Path.Combine(outputDirectory, "champion-b.json"),
    artifactB,
    settings);

var rulesetDocument = job.Ruleset;
var rulesetPath = Argument(args, "--ruleset");
if (!string.IsNullOrWhiteSpace(rulesetPath))
{
    rulesetDocument = Read<CombatRulesetDocument>(
        rulesetPath,
        settings);
}
var build = CombatSimulationRegistry.BuildRuleset(rulesetDocument);
if (!build.Success)
{
    throw new InvalidOperationException(
        "Ruleset build failed: " + string.Join("; ", build.Errors));
}
var campaign = job.Request.ValidationCampaign;
var campaignPath = Argument(args, "--campaign");
if (!string.IsNullOrWhiteSpace(campaignPath))
{
    campaign = Read<CombatCampaignDefinition>(
        campaignPath,
        settings);
}
CombatCampaignWorldPlanner.Validate(campaign);

var campaignsPerDifficulty = Math.Max(
    1,
    Math.Min(512, IntArgument(args, "--campaigns", 64)));
var seedStart = ULongArgument(args, "--seed-start", 3_000_000UL);
var parallelism = Math.Max(
    1,
    Math.Min(
        Environment.ProcessorCount,
        IntArgument(
            args,
            "--parallelism",
            Math.Max(1, Math.Min(16, Environment.ProcessorCount)))));
var profile = job.Request.Profile ?? new CombatDecisionProfile();
var comparison = new ChampionComparisonReport
{
    CreatedUtc = DateTime.UtcNow,
    DiagnosticOnly = true,
    Publishable = false,
    RulesetHash = build.Ruleset.RulesetHash,
    CampaignId = campaign.CampaignId,
    CampaignVersion = campaign.CampaignVersion,
    CampaignsPerDifficulty = campaignsPerDifficulty,
    EffectiveParallelism = parallelism,
    SeedStart = seedStart,
    ChampionA = artifactA.Manifest,
    ChampionB = artifactB.Manifest
};
var progressGate = new object();

foreach (var difficulty in new[] { "normal", "advanced" })
{
    var pairs = new ChampionComparisonPair[campaignsPerDifficulty];
    var completed = 0;
    Parallel.For(
        0,
        campaignsPerDifficulty,
        new ParallelOptions
        {
            MaxDegreeOfParallelism = parallelism
        },
        index =>
        {
            var seed = seedStart + (ulong)index;
            var plan = CombatCampaignWorldPlanner.Build(
                campaign,
                difficulty,
                seed);
            var runA = RunChampion(
                campaign,
                plan,
                build.Ruleset,
                profile,
                championA);
            var runB = RunChampion(
                campaign,
                plan,
                build.Ruleset,
                profile,
                championB);
            pairs[index] = new ChampionComparisonPair
            {
                DifficultyId = difficulty,
                WorldSeed = seed,
                ChampionAVictory = runA.FinalBossVictory,
                ChampionBVictory = runB.FinalBossVictory,
                ChampionAInvalid = runA.Invalid,
                ChampionBInvalid = runB.Invalid,
                ChampionACompletedBattles = runA.CompletedBattles,
                ChampionBCompletedBattles = runB.CompletedBattles,
                ChampionAInvalidReason = InvalidReason(runA),
                ChampionBInvalidReason = InvalidReason(runB),
                ChampionAFingerprint = Fingerprint(runA),
                ChampionBFingerprint = Fingerprint(runB)
            };
            var progress = Interlocked.Increment(ref completed);
            lock (progressGate)
            {
                Console.WriteLine(
                    difficulty
                    + " "
                    + progress
                    + "/"
                    + campaignsPerDifficulty
                    + ": A="
                    + Outcome(runA)
                    + ", B="
                    + Outcome(runB));
            }
        });
    comparison.Pairs.AddRange(pairs);
}

var firstPair = comparison.Pairs.First();
var repeatPlan = CombatCampaignWorldPlanner.Build(
    campaign,
    firstPair.DifficultyId,
    firstPair.WorldSeed);
var repeatA = RunChampion(
    campaign,
    repeatPlan,
    build.Ruleset,
    profile,
    championA);
var repeatB = RunChampion(
    campaign,
    repeatPlan,
    build.Ruleset,
    profile,
    championB);
comparison.Deterministic =
    firstPair.ChampionAFingerprint == Fingerprint(repeatA)
    && firstPair.ChampionBFingerprint == Fingerprint(repeatB);
Summarize(comparison);
Write(
    Path.Combine(outputDirectory, "champion-ab-report.json"),
    comparison,
    settings);
WriteCsv(
    Path.Combine(outputDirectory, "champion-ab-pairs.csv"),
    comparison.Pairs);

Console.WriteLine(
    "A/B complete: verdict="
    + comparison.Verdict
    + ", exclusiveWins B:A="
    + comparison.ChampionBOnlyWins
    + ":"
    + comparison.ChampionAOnlyWins
    + ", deterministic="
    + comparison.Deterministic);
return comparison.Deterministic ? 0 : 3;

static CombatCampaignResult RunChampion(
    CombatCampaignDefinition campaign,
    CombatCampaignWorldPlan plan,
    CombatRuleset ruleset,
    CombatDecisionProfile profile,
    CombatPolicyValueNetworkDefinition champion)
{
    var runner = new CombatCampaignRunner(
        new CombatSimulationEngine(
            new AuraToolsNativeRewardExtensionFactory()));
    var model = new ManagedCombatPolicyValueModel(
        champion,
        allowDiagnosticLegacySchema: true);
    var factory = new CombatDecisionSimulationPolicyFactory(
        CombatSearchBudgetPolicy.WithContext(profile, "deployment"),
        policyValueModel: model);
    return runner.Run(campaign, plan, ruleset, factory);
}

static ChampionArtifact Artifact(
    string label,
    string sourcePath,
    CombatFoundationWorkerResult result,
    CombatPolicyValueNetworkDefinition model)
{
    var compatibility = result.Training?.Compatibility
                        ?? new CombatFoundationCompatibilityManifest();
    return new ChampionArtifact
    {
        Manifest = new ChampionArtifactManifest
        {
            Label = label,
            ChampionId = model.ModelId,
            SourceResultPath = Path.GetFullPath(sourcePath),
            SourceJobId = result.JobId,
            RulesetHash = result.RulesetHash,
            NativeProgramPackageHash =
                compatibility.NativeProgramPackageHash,
            FeatureSchemaVersion = model.FeatureSchemaVersion,
            FeatureEncodingMode = model.FeatureEncodingMode,
            TrainingSemanticsVersion =
                compatibility.TrainingSemanticsVersion,
            ModelSha256 = ModelHash(model),
            DiagnosticOnly =
                model.FeatureSchemaVersion
                != CombatPolicyValueProtocol.FeatureSchemaVersion
                || !string.Equals(
                    compatibility.TrainingSemanticsVersion,
                    CombatPolicyValueProtocol.TrainingSemanticsVersion,
                    StringComparison.Ordinal),
            ExtractedUtc = DateTime.UtcNow
        },
        Model = model
    };
}

static void Summarize(ChampionComparisonReport report)
{
    report.ChampionAOnlyWins = report.Pairs.Count(item =>
        item.ChampionAVictory && !item.ChampionBVictory);
    report.ChampionBOnlyWins = report.Pairs.Count(item =>
        item.ChampionBVictory && !item.ChampionAVictory);
    report.InvalidPairs = report.Pairs.Count(item =>
        item.ChampionAInvalid || item.ChampionBInvalid);
    var discordant =
        report.ChampionAOnlyWins + report.ChampionBOnlyWins;
    report.ChampionBWinWilsonLowerBound =
        CombatFoundationCurriculum.WilsonLowerBound(
            report.ChampionBOnlyWins,
            discordant);
    report.ChampionBWinWilsonUpperBound = discordant <= 0
        ? 1d
        : 1d - CombatFoundationCurriculum.WilsonLowerBound(
            report.ChampionAOnlyWins,
            discordant);
    report.PairedLossMedianDepthGainBMinusA = Median(report.Pairs
        .Where(item =>
            !item.ChampionAVictory && !item.ChampionBVictory)
        .Select(item =>
            (double)(item.ChampionBCompletedBattles
                     - item.ChampionACompletedBattles))
        .ToList());
    report.Verdict = report.InvalidPairs > 0
        ? "invalid"
        : report.ChampionBWinWilsonLowerBound > 0.5d
            ? "champion-b"
            : report.ChampionBWinWilsonUpperBound < 0.5d
                ? "champion-a"
                : "inconclusive";
}

static string Fingerprint(CombatCampaignResult run)
{
    var builder = new StringBuilder()
        .Append(run.DifficultyId)
        .Append('|').Append(run.FinalBossVictory)
        .Append('|').Append(run.Invalid)
        .Append('|').Append(run.CompletedBattles)
        .Append('|').Append(run.TotalBattles);
    foreach (var battle in run.Battles)
    {
        builder.Append('|')
            .Append(battle.Outcome)
            .Append(':')
            .Append(battle.FinalStateHash);
    }
    return Sha256(builder.ToString());
}

static string ModelHash(CombatPolicyValueNetworkDefinition model)
{
    return Sha256(JsonConvert.SerializeObject(model, Formatting.None));
}

static string Sha256(string value)
{
    return Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value ?? "")))
        .ToLowerInvariant();
}

static string Outcome(CombatCampaignResult run)
{
    return run.Invalid
        ? "invalid"
        : run.FinalBossVictory
            ? "victory"
            : "depth-" + run.CompletedBattles;
}

static string InvalidReason(CombatCampaignResult run)
{
    if (!run.Invalid) return "";
    var invalidBattles = run.Battles
        .Select((battle, index) => new { Battle = battle, Index = index })
        .Where(item =>
            item.Battle.Outcome == CombatSimulationOutcome.Invalid)
        .Select(item =>
            item.Index.ToString(CultureInfo.InvariantCulture)
            + ":"
            + item.Battle.ScenarioId
            + ":"
            + item.Battle.TerminationReason
            + ":"
            + item.Battle.FailureDiagnostics.LimitScope
            + ":"
            + item.Battle.FailureDiagnostics.ActionDefinitionId)
        .ToList();
    if (invalidBattles.Count > 0)
    {
        return string.Join(";", invalidBattles);
    }
    return run.UnsupportedDefinitions.Count > 0
        ? "unsupported:" + string.Join(",", run.UnsupportedDefinitions)
        : "semantic-coverage:battle="
          + run.BattleSemanticCoverage.ToString("R", CultureInfo.InvariantCulture)
          + ",progression="
          + run.ProgressionSemanticCoverage.ToString("R", CultureInfo.InvariantCulture);
}

static double Median(IReadOnlyList<double> values)
{
    if (values.Count == 0) return 0d;
    var ordered = values.OrderBy(item => item).ToArray();
    var middle = ordered.Length / 2;
    return ordered.Length % 2 == 0
        ? (ordered[middle - 1] + ordered[middle]) / 2d
        : ordered[middle];
}

static T Read<T>(string path, JsonSerializerSettings settings)
{
    return JsonConvert.DeserializeObject<T>(
               File.ReadAllText(Path.GetFullPath(path)),
               settings)
           ?? throw new InvalidOperationException(
               "Could not deserialize " + path);
}

static void Write(
    string path,
    object value,
    JsonSerializerSettings settings)
{
    File.WriteAllText(
        path,
        JsonConvert.SerializeObject(value, settings),
        new UTF8Encoding(false));
}

static void WriteCsv(
    string path,
    IReadOnlyList<ChampionComparisonPair> pairs)
{
    var rows = new List<string>
    {
        "difficulty,seed,a_victory,b_victory,a_invalid,b_invalid,"
        + "a_depth,b_depth,a_invalid_reason,b_invalid_reason,"
        + "a_fingerprint,b_fingerprint"
    };
    rows.AddRange(pairs.Select(item => string.Join(
        ",",
        item.DifficultyId,
        item.WorldSeed.ToString(CultureInfo.InvariantCulture),
        item.ChampionAVictory ? "1" : "0",
        item.ChampionBVictory ? "1" : "0",
        item.ChampionAInvalid ? "1" : "0",
        item.ChampionBInvalid ? "1" : "0",
        item.ChampionACompletedBattles.ToString(
            CultureInfo.InvariantCulture),
        item.ChampionBCompletedBattles.ToString(
            CultureInfo.InvariantCulture),
        Csv(item.ChampionAInvalidReason),
        Csv(item.ChampionBInvalidReason),
        item.ChampionAFingerprint,
        item.ChampionBFingerprint)));
    File.WriteAllLines(path, rows, new UTF8Encoding(false));
}

static string Csv(string value)
{
    return "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
}

static CombatPolicyValueNetworkDefinition RequiredChampion(
    CombatFoundationWorkerResult result,
    string label)
{
    return result.Training?.Champion
           ?? throw new InvalidOperationException(
               "Result " + label + " has no champion");
}

static string Argument(string[] values, string name)
{
    for (var index = 0; index + 1 < values.Length; index++)
    {
        if (string.Equals(
                values[index],
                name,
                StringComparison.OrdinalIgnoreCase))
        {
            return values[index + 1];
        }
    }
    return "";
}

static int IntArgument(string[] values, string name, int fallback)
{
    return int.TryParse(
        Argument(values, name),
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var parsed)
        ? parsed
        : fallback;
}

static ulong ULongArgument(
    string[] values,
    string name,
    ulong fallback)
{
    return ulong.TryParse(
        Argument(values, name),
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var parsed)
        ? parsed
        : fallback;
}

public sealed class ChampionArtifact
{
    public ChampionArtifactManifest Manifest { get; set; } = new();

    public CombatPolicyValueNetworkDefinition Model { get; set; } = new();
}

public sealed class ChampionArtifactManifest
{
    public string Label { get; set; } = "";

    public string ChampionId { get; set; } = "";

    public string SourceResultPath { get; set; } = "";

    public string SourceJobId { get; set; } = "";

    public string RulesetHash { get; set; } = "";

    public string NativeProgramPackageHash { get; set; } = "";

    public int FeatureSchemaVersion { get; set; }

    public string FeatureEncodingMode { get; set; } = "";

    public string TrainingSemanticsVersion { get; set; } = "";

    public string ModelSha256 { get; set; } = "";

    public bool DiagnosticOnly { get; set; }

    public DateTime ExtractedUtc { get; set; }
}

public sealed class ChampionComparisonPair
{
    public string DifficultyId { get; set; } = "";

    public ulong WorldSeed { get; set; }

    public bool ChampionAVictory { get; set; }

    public bool ChampionBVictory { get; set; }

    public bool ChampionAInvalid { get; set; }

    public bool ChampionBInvalid { get; set; }

    public int ChampionACompletedBattles { get; set; }

    public int ChampionBCompletedBattles { get; set; }

    public string ChampionAInvalidReason { get; set; } = "";

    public string ChampionBInvalidReason { get; set; } = "";

    public string ChampionAFingerprint { get; set; } = "";

    public string ChampionBFingerprint { get; set; } = "";
}

public sealed class ChampionComparisonReport
{
    public DateTime CreatedUtc { get; set; }

    public bool DiagnosticOnly { get; set; }

    public bool Publishable { get; set; }

    public bool Deterministic { get; set; }

    public string RulesetHash { get; set; } = "";

    public string CampaignId { get; set; } = "";

    public string CampaignVersion { get; set; } = "";

    public int CampaignsPerDifficulty { get; set; }

    public int EffectiveParallelism { get; set; }

    public ulong SeedStart { get; set; }

    public ChampionArtifactManifest ChampionA { get; set; } = new();

    public ChampionArtifactManifest ChampionB { get; set; } = new();

    public List<ChampionComparisonPair> Pairs { get; set; } = new();

    public int ChampionAOnlyWins { get; set; }

    public int ChampionBOnlyWins { get; set; }

    public int InvalidPairs { get; set; }

    public double ChampionBWinWilsonLowerBound { get; set; }

    public double ChampionBWinWilsonUpperBound { get; set; }

    public double PairedLossMedianDepthGainBMinusA { get; set; }

    public string Verdict { get; set; } = "inconclusive";
}
