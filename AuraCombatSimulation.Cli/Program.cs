using System.Text.Json;
using System.Text.Json.Serialization;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;

var parsed = CommandLine.Parse(args);
if (!parsed.Success)
{
    Console.Error.WriteLine(parsed.Message);
    Console.Error.WriteLine(
        "Usage: --ruleset <rules.json> --scenario <scenario.json> "
        + "[--output <result.json>] [--count N] [--parallel N] "
        + "[--seed-start N] [--policy greedy|first|chance-puct]");
    return 2;
}

var json = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};
json.Converters.Add(new JsonStringEnumConverter());

try
{
    var rulesDocument = JsonSerializer.Deserialize<CombatRulesetDocument>(
                            File.ReadAllText(parsed.RulesetPath),
                            json)
                        ?? throw new InvalidDataException("Ruleset JSON is empty.");
    var scenario = JsonSerializer.Deserialize<CombatScenarioDefinition>(
                       File.ReadAllText(parsed.ScenarioPath),
                       json)
                   ?? throw new InvalidDataException("Scenario JSON is empty.");
    var build = CombatSimulationRegistry.BuildRuleset(rulesDocument);
    if (!build.Success)
    {
        Console.Error.WriteLine("Ruleset validation failed:");
        foreach (var error in build.Errors) Console.Error.WriteLine("  " + error);
        return 3;
    }

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

    object output;
    if (parsed.Count <= 1)
    {
        scenario.Seed = parsed.SeedStart ?? scenario.Seed;
        output = new CombatSimulationEngine().Run(
            scenario,
            build.Ruleset,
            Policy(parsed.Policy),
            cancellation.Token);
    }
    else
    {
        output = new CombatBatchRunner().Run(
            new CombatBatchRequest
            {
                Scenario = scenario,
                SeedStart = parsed.SeedStart ?? scenario.Seed,
                SimulationCount = parsed.Count,
                MaximumDegreeOfParallelism = parsed.Parallel,
                KeepInvalidResults = true
            },
            build.Ruleset,
            PolicyFactory(parsed.Policy),
            cancellation.Token);
    }

    var serialized = JsonSerializer.Serialize(output, output.GetType(), json);
    if (string.IsNullOrWhiteSpace(parsed.OutputPath))
    {
        Console.WriteLine(serialized);
    }
    else
    {
        var fullPath = Path.GetFullPath(parsed.OutputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullPath, serialized);
        Console.WriteLine("Combat simulation result written to " + fullPath);
    }
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Combat simulation cancelled.");
    return 4;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
    return 1;
}

static ICombatSimulationPolicy Policy(string policy)
{
    switch (policy)
    {
        case "first":
            return FirstLegalCombatSimulationPolicy.Instance;
        case "chance-puct":
            return new CombatDecisionSimulationPolicy(new CombatDecisionProfile());
        default:
            return new GreedyCombatSimulationPolicy();
    }
}

static ICombatSimulationPolicyFactory PolicyFactory(string policy)
{
    switch (policy)
    {
        case "first":
            return new FirstLegalPolicyFactory();
        case "chance-puct":
            return new CombatDecisionSimulationPolicyFactory(new CombatDecisionProfile());
        default:
            return new GreedyCombatSimulationPolicyFactory();
    }
}

sealed class FirstLegalPolicyFactory : ICombatSimulationPolicyFactory
{
    public string PolicyId => FirstLegalCombatSimulationPolicy.Instance.PolicyId;

    public ICombatSimulationPolicy Create()
    {
        return FirstLegalCombatSimulationPolicy.Instance;
    }
}

sealed class CommandLine
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public string RulesetPath { get; set; } = "";

    public string ScenarioPath { get; set; } = "";

    public string OutputPath { get; set; } = "";

    public int Count { get; set; } = 1;

    public int Parallel { get; set; } = 1;

    public ulong? SeedStart { get; set; }

    public string Policy { get; set; } = "greedy";

    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        var result = new CommandLine();
        for (var i = 0; i < args.Count; i++)
        {
            var key = args[i];
            if (i + 1 >= args.Count)
            {
                result.Message = "Missing value for " + key;
                return result;
            }
            var value = args[++i];
            switch (key)
            {
                case "--ruleset":
                    result.RulesetPath = value;
                    break;
                case "--scenario":
                    result.ScenarioPath = value;
                    break;
                case "--output":
                    result.OutputPath = value;
                    break;
                case "--count":
                    if (!int.TryParse(value, out var count) || count <= 0)
                    {
                        result.Message = "Invalid --count value.";
                        return result;
                    }
                    result.Count = count;
                    break;
                case "--parallel":
                    if (!int.TryParse(value, out var parallel) || parallel <= 0)
                    {
                        result.Message = "Invalid --parallel value.";
                        return result;
                    }
                    result.Parallel = parallel;
                    break;
                case "--seed-start":
                    if (!ulong.TryParse(value, out var seed))
                    {
                        result.Message = "Invalid --seed-start value.";
                        return result;
                    }
                    result.SeedStart = seed;
                    break;
                case "--policy":
                    result.Policy = value.Trim().ToLowerInvariant();
                    if (result.Policy != "greedy"
                        && result.Policy != "first"
                        && result.Policy != "chance-puct")
                    {
                        result.Message = "Unknown policy: " + value;
                        return result;
                    }
                    break;
                default:
                    result.Message = "Unknown option: " + key;
                    return result;
            }
        }
        if (string.IsNullOrWhiteSpace(result.RulesetPath)
            || string.IsNullOrWhiteSpace(result.ScenarioPath))
        {
            result.Message = "--ruleset and --scenario are required.";
            return result;
        }
        if (!File.Exists(result.RulesetPath) || !File.Exists(result.ScenarioPath))
        {
            result.Message = "Ruleset or scenario file does not exist.";
            return result;
        }
        result.Success = true;
        return result;
    }
}
