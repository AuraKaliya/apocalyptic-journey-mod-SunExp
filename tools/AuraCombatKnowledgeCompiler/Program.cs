using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AuraCombatAi.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var options = CompilerOptions.Parse(args);
if (!options.Success)
{
    Console.Error.WriteLine(options.Error);
    Console.Error.WriteLine(
        "Usage: dotnet run -- --scripts <AllScripts.cs> --output <base-game.json> "
        + "--game-build <version> [--report <report.json>] [--tables <table-export.json>]");
    return 2;
}

var source = await File.ReadAllTextAsync(options.ScriptsPath);
var sanitizedSource = DecompiledSourceSanitizer.Sanitize(source);
var tree = CSharpSyntaxTree.ParseText(sanitizedSource);
var root = await tree.GetRootAsync();
var compiler = new KnowledgeCompiler(
    options.GameBuild,
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant(),
    tree,
    source);
var result = compiler.Compile(root);
if (!string.IsNullOrWhiteSpace(options.TablesPath))
{
    BaseGameTableEnricher.Enrich(result.Package, options.TablesPath);
    result.Report.TableExportPath = Path.GetFullPath(options.TablesPath);
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};
await File.WriteAllTextAsync(
    options.OutputPath,
    JsonSerializer.Serialize(result.Package, jsonOptions),
    new UTF8Encoding(false));

var reportPath = string.IsNullOrWhiteSpace(options.ReportPath)
    ? Path.ChangeExtension(options.OutputPath, ".report.json")
    : options.ReportPath;
await File.WriteAllTextAsync(
    reportPath,
    JsonSerializer.Serialize(result.Report, jsonOptions),
    new UTF8Encoding(false));

Console.WriteLine(
    "Combat knowledge compiled: actions=" + result.Package.Actions.Count
    + ", statuses=" + result.Package.Statuses.Count
    + ", enemies=" + result.Package.Enemies.Count
    + ", operations=" + result.Report.OperationCount
    + ", unsupported=" + result.Report.UnsupportedOperationCount);
Console.WriteLine("Package: " + Path.GetFullPath(options.OutputPath));
Console.WriteLine("Report: " + Path.GetFullPath(reportPath));
return 0;

internal sealed class KnowledgeCompiler
{
    private static readonly Regex ScriptClassPattern = new(
        "^(?<id>.+)_(?<stage>[A-Za-z0-9]+)Script$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> SupportedApis = new(
        new[]
        {
            "Damage", "ChangeHp", "ChangeDefence", "DrawCount", "ChangePower",
            "AddBuff", "RemoveBuff", "ChangeDynamicVar", "ChangeDynamicVarPercent",
            "CreateCard", "DropCard", "BurnCard", "AddEvent", "RemoveEvent",
            "SetStatus", "SetStatusById", "AddDescription", "ChangeMaxHp",
            "ChangeMaxPower", "AddCardTag", "RemoveCardTag"
        },
        StringComparer.OrdinalIgnoreCase);

    private readonly string gameBuild;
    private readonly string sourceHash;
    private readonly SyntaxTree tree;
    private readonly string originalSource;

    public KnowledgeCompiler(
        string gameBuild,
        string sourceHash,
        SyntaxTree tree,
        string originalSource)
    {
        this.gameBuild = gameBuild;
        this.sourceHash = sourceHash;
        this.tree = tree;
        this.originalSource = originalSource;
    }

    public CompileResult Compile(SyntaxNode root)
    {
        var operations = new Dictionary<string, List<CombatKnowledgeOperation>>(
            StringComparer.OrdinalIgnoreCase);
        var unsupported = new List<CompilerUnsupportedOperation>();
        foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var match = ScriptClassPattern.Match(declaration.Identifier.ValueText);
            if (!match.Success)
            {
                continue;
            }

            var id = match.Groups["id"].Value;
            var stage = match.Groups["stage"].Value;
            var key = id + "|" + stage;
            if (!operations.TryGetValue(key, out var list))
            {
                list = new List<CombatKnowledgeOperation>();
                operations[key] = list;
            }

            foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!TryReadScriptApi(invocation, out var api, out var arguments))
                {
                    continue;
                }
                var line = tree.GetLineSpan(invocation.Span).StartLinePosition.Line + 1;
                var fidelity = SupportedApis.Contains(api)
                    ? InferFidelity(arguments)
                    : CombatKnowledgeFidelity.Unsupported;
                var operation = new CombatKnowledgeOperation
                {
                    Stage = stage,
                    Api = api,
                    Arguments = arguments,
                    Fidelity = fidelity,
                    SourceLocation = "AllScripts.cs:" + line
                };
                list.Add(operation);
                if (fidelity == CombatKnowledgeFidelity.Unsupported)
                {
                    unsupported.Add(new CompilerUnsupportedOperation
                    {
                        EntityId = id,
                        Stage = stage,
                        Api = api,
                        SourceLocation = operation.SourceLocation,
                        Reason = SupportedApis.Contains(api)
                            ? "argument expression needs manual lowering"
                            : "unsupported ScriptExecutor API"
                    });
                }
            }
        }

        var catalog = ReadRegisteredScripts(originalSource);
        foreach (var script in catalog)
        {
            var key = script.EntityId + "|" + script.Stage;
            if (!operations.ContainsKey(key))
            {
                operations[key] = new List<CombatKnowledgeOperation>();
            }
        }
        var grouped = operations.Keys
            .Select(key => key.Substring(0, key.LastIndexOf('|')))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new
            {
                Id = id,
                Operations = operations
                    .Where(pair => pair.Key.StartsWith(id + "|", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(pair => pair.Value)
                    .ToList()
            })
            .ToList();
        var package = new CombatKnowledgePackage
        {
            OwnerId = "witch.base-game",
            PackageId = "decompiled-script-inventory",
            GameBuild = gameBuild,
            SourceHash = sourceHash,
            GeneratedAtUtc = DateTime.UtcNow
        };

        foreach (var entity in grouped.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var entityOperations = entity.Operations;
            if (entity.Id.StartsWith("buff_", StringComparison.OrdinalIgnoreCase))
            {
                package.Statuses.Add(BuildStatus(entity.Id, entityOperations));
            }
            else if (entity.Id.StartsWith("enemy_", StringComparison.OrdinalIgnoreCase))
            {
                package.Enemies.Add(BuildEnemy(entity.Id, entityOperations));
            }
            else
            {
                package.Actions.Add(BuildAction(entity.Id, entityOperations));
            }
        }

        package.Inventory = new CombatKnowledgeInventory
        {
            DiscoveredActions = package.Actions.Count,
            DiscoveredStatuses = package.Statuses.Count,
            DiscoveredEnemies = package.Enemies.Count,
            AuthoritativeActions = package.Actions.Count(item =>
                item.Fidelity == CombatKnowledgeFidelity.Authoritative),
            AuthoritativeStatuses = package.Statuses.Count(item =>
                item.Fidelity == CombatKnowledgeFidelity.Authoritative),
            AuthoritativeEnemies = package.Enemies.Count(item =>
                item.Fidelity == CombatKnowledgeFidelity.Authoritative),
            UnsupportedScripts = unsupported.Select(item => item.EntityId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
        };
        var diagnostics = tree.GetDiagnostics()
            .Where(item => item.Severity == DiagnosticSeverity.Error)
            .Select(item => item.ToString())
            .ToList();
        return new CompileResult
        {
            Package = package,
            Report = new CompilerReport
            {
                GameBuild = gameBuild,
                SourceHash = sourceHash,
                EntityCount = grouped.Count,
                RegisteredScriptCount = catalog.Count,
                OperationCount = grouped.Sum(item => item.Operations.Count),
                UnsupportedOperationCount = unsupported.Count,
                Unsupported = unsupported,
                ParseDiagnostics = diagnostics
            }
        };
    }

    private static List<RegisteredScript> ReadRegisteredScripts(string source)
    {
        var result = new List<RegisteredScript>();
        var pattern = new Regex(
            "totalScripts\\.Add\\(\\\"(?<name>[^\\\"]+_(?<stage>[A-Za-z0-9]+)Script)\\\"",
            RegexOptions.CultureInvariant);
        foreach (Match match in pattern.Matches(source))
        {
            var fullName = match.Groups["name"].Value;
            var suffix = "_" + match.Groups["stage"].Value + "Script";
            result.Add(new RegisteredScript
            {
                EntityId = fullName.Substring(0, fullName.Length - suffix.Length),
                Stage = match.Groups["stage"].Value
            });
        }
        return result;
    }

    private static CombatKnowledgeActionDefinition BuildAction(
        string id,
        List<CombatKnowledgeOperation> operations)
    {
        var definition = new CombatKnowledgeActionDefinition
        {
            SourceId = id,
            Fidelity = ExtractedEntityFidelity(operations),
            Confidence = operations.Count == 0 ? 0d : 0.8d,
            Operations = operations,
            Provenance = "Roslyn AST extraction from AllScripts.cs"
        };
        foreach (var operation in operations.Where(item =>
                     string.Equals(item.Stage, "Use", StringComparison.OrdinalIgnoreCase)))
        {
            ProjectSemantics(definition.Semantics, operation);
        }
        if (definition.Semantics.Damage > 0d || definition.Semantics.TrueDamage > 0d)
        {
            definition.Roles.Add("damage");
        }
        if (definition.Semantics.Draw > 0d)
        {
            definition.Roles.Add("draw");
        }
        if (definition.Semantics.Buff > 0d || definition.Semantics.PersistentValue > 0d)
        {
            definition.Roles.Add("setup");
        }
        return definition;
    }

    private static CombatKnowledgeStatusDefinition BuildStatus(
        string id,
        List<CombatKnowledgeOperation> operations)
    {
        var definition = new CombatKnowledgeStatusDefinition
        {
            StatusId = id,
            Fidelity = ExtractedEntityFidelity(operations),
            Operations = operations,
            Provenance = "Roslyn AST extraction from AllScripts.cs"
        };
        definition.Triggers.AddRange(operations
            .Where(item => string.Equals(item.Api, "AddEvent", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Arguments.FirstOrDefault() ?? "event")
            .Distinct(StringComparer.OrdinalIgnoreCase));
        foreach (var operation in operations.Where(item =>
                     string.Equals(item.Api, "ChangeDynamicVar", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(
                         item.Api,
                         "ChangeDynamicVarPercent",
                         StringComparison.OrdinalIgnoreCase)))
        {
            var key = operation.Arguments.FirstOrDefault(value =>
                value.StartsWith("\"", StringComparison.Ordinal));
            var amount = operation.Arguments
                .Select(TryConstant)
                .LastOrDefault(value => value.HasValue);
            if (key != null && amount.HasValue)
            {
                definition.DynamicModifiersPerStack[Unquote(key)] =
                    string.Equals(
                        operation.Api,
                        "ChangeDynamicVarPercent",
                        StringComparison.OrdinalIgnoreCase)
                        ? amount.Value / 100d
                        : amount.Value;
            }
        }
        return definition;
    }

    private static CombatKnowledgeEnemyDefinition BuildEnemy(
        string id,
        List<CombatKnowledgeOperation> operations)
    {
        return new CombatKnowledgeEnemyDefinition
        {
            EnemyId = id,
            Fidelity = ExtractedEntityFidelity(operations),
            Provenance = "Roslyn AST extraction from AllScripts.cs"
        };
    }

    private static void ProjectSemantics(
        CombatActionSemantics semantics,
        CombatKnowledgeOperation operation)
    {
        var amount = operation.Arguments
            .Select(TryConstant)
            .LastOrDefault(value => value.HasValue);
        var value = Math.Max(0d, amount ?? 0d);
        switch (operation.Api)
        {
            case "Damage":
                semantics.Damage += value;
                break;
            case "ChangeDefence":
                semantics.Defend += value;
                break;
            case "DrawCount":
                semantics.Draw += value;
                break;
            case "ChangePower":
                semantics.EnergyGain += value;
                break;
            case "AddBuff":
                semantics.Buff += value;
                var status = operation.Arguments.FirstOrDefault(item =>
                    item.Contains("buff_", StringComparison.OrdinalIgnoreCase));
                if (status != null)
                {
                    semantics.StateChanges["status:" + Unquote(status)] = value;
                }
                break;
            case "RemoveBuff":
                semantics.Cleanse += Math.Max(1d, value);
                break;
            case "CreateCard":
                semantics.CardGeneration += Math.Max(1d, value);
                break;
            case "ChangeHp":
                semantics.Heal += value;
                break;
        }
        if (operation.Fidelity != CombatKnowledgeFidelity.Authoritative)
        {
            semantics.Uncertainty = Math.Max(semantics.Uncertainty, 1d);
        }
    }

    private static bool TryReadScriptApi(
        InvocationExpressionSyntax invocation,
        out string api,
        out List<string> arguments)
    {
        api = "";
        arguments = new List<string>();
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return false;
        }
        var receiver = member.Expression.ToString();
        var directName = member.Name.Identifier.ValueText;
        var indirect = string.Equals(directName, "Method", StringComparison.Ordinal)
                       || string.Equals(directName, "MethodAsync", StringComparison.Ordinal);
        if (!indirect)
        {
            if (!receiver.EndsWith("sc", StringComparison.Ordinal)
                && !receiver.EndsWith(".sc", StringComparison.Ordinal))
            {
                return false;
            }
            api = directName;
            arguments.AddRange(invocation.ArgumentList.Arguments.Select(argument =>
                Normalize(argument.Expression)));
            return true;
        }

        var invocationArguments = invocation.ArgumentList.Arguments;
        if (invocationArguments.Count == 0
            || invocationArguments[0].Expression is not LiteralExpressionSyntax literal
            || literal.Token.Value is not string methodName)
        {
            return false;
        }
        api = methodName;
        foreach (var argument in invocationArguments.Skip(1))
        {
            if (argument.Expression is ArrayCreationExpressionSyntax array
                && array.Initializer != null)
            {
                arguments.AddRange(array.Initializer.Expressions.Select(Normalize));
            }
            else if (argument.Expression is ImplicitArrayCreationExpressionSyntax implicitArray)
            {
                arguments.AddRange(implicitArray.Initializer.Expressions.Select(Normalize));
            }
            else
            {
                arguments.Add(Normalize(argument.Expression));
            }
        }
        return true;
    }

    private static string Normalize(SyntaxNode node)
    {
        return Regex.Replace(node.ToString(), "\\s+", " ").Trim();
    }

    private static CombatKnowledgeFidelity InferFidelity(IReadOnlyList<string> arguments)
    {
        return arguments.All(argument =>
                   TryConstant(argument).HasValue
                   || argument.StartsWith("\"", StringComparison.Ordinal)
                   || argument is "sc.Self" or "sc.Target" or "sc.Arguments[0]")
            ? CombatKnowledgeFidelity.Authoritative
            : CombatKnowledgeFidelity.Derived;
    }

    private static CombatKnowledgeFidelity OverallFidelity(
        IReadOnlyList<CombatKnowledgeOperation> operations)
    {
        if (operations.Count == 0)
        {
            return CombatKnowledgeFidelity.Unsupported;
        }
        return operations.Max(item => item.Fidelity);
    }

    private static CombatKnowledgeFidelity ExtractedEntityFidelity(
        IReadOnlyList<CombatKnowledgeOperation> operations)
    {
        var fidelity = OverallFidelity(operations);
        return fidelity == CombatKnowledgeFidelity.Authoritative
            ? CombatKnowledgeFidelity.Derived
            : fidelity;
    }

    private static double? TryConstant(string expression)
    {
        var normalized = expression.Trim().TrimEnd('f', 'd', 'm');
        return double.TryParse(
            normalized,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static string Unquote(string value)
    {
        return value.Trim().Trim('"');
    }
}

internal static class DecompiledSourceSanitizer
{
    public static string Sanitize(string source)
    {
        var result = Regex.Replace(
            source,
            @"<>c__DisplayClass(?<suffix>[A-Za-z0-9_]+)",
            "GeneratedDisplayClass${suffix}");
        result = Regex.Replace(result, @"<>c(?![A-Za-z0-9_])", "GeneratedClosure");
        result = Regex.Replace(
            result,
            @"<(?<method>[A-Za-z0-9_]+)>b__(?<suffix>[A-Za-z0-9_]+)",
            "Generated_${method}_b__${suffix}");
        result = Regex.Replace(
            result,
            @"<(?<member>[A-Za-z0-9_]+)>k__BackingField",
            "Generated_${member}_BackingField");
        result = Regex.Replace(
            result,
            @"<>9__(?<suffix>[A-Za-z0-9_]+)",
            "GeneratedCached_${suffix}");
        result = Regex.Replace(result, @"<>9(?![A-Za-z0-9_])", "GeneratedCached");
        result = Regex.Replace(
            result,
            @"CS\$<>8__locals(?<suffix>[A-Za-z0-9_]+)",
            "GeneratedLocals${suffix}");
        result = Regex.Replace(
            result,
            @"<(?<method>[A-Za-z0-9_]+)>g__(?<local>[A-Za-z0-9_]+)\|(?<suffix>[A-Za-z0-9_]+)",
            "Generated_${method}_g__${local}_${suffix}");
        return result;
    }
}

internal static class BaseGameTableEnricher
{
    public static void Enrich(CombatKnowledgePackage package, string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("tables", out var tables)
            && !document.RootElement.TryGetProperty("Tables", out tables))
        {
            throw new InvalidDataException("table export has no tables object");
        }
        EnrichActions(package, Rows(tables, "Card").Concat(Rows(tables, "PartnerCard")));
        EnrichStatuses(package, Rows(tables, "Buff"));
        EnrichEnemies(package, Rows(tables, "Enemy"));
        EnrichEncounters(package, Rows(tables, "Level"));
        package.Inventory.DiscoveredActions = package.Actions.Count;
        package.Inventory.DiscoveredStatuses = package.Statuses.Count;
        package.Inventory.DiscoveredEnemies = package.Enemies.Count;
        package.Inventory.DiscoveredEncounters = package.Encounters.Count;
    }

    private static void EnrichActions(
        CombatKnowledgePackage package,
        IEnumerable<Dictionary<string, string>> rows)
    {
        foreach (var row in rows)
        {
            var id = Value(row, "Id");
            if (id.Length == 0)
            {
                continue;
            }
            var definition = package.Actions.FirstOrDefault(item =>
                string.Equals(item.SourceId, id, StringComparison.OrdinalIgnoreCase));
            if (definition == null)
            {
                definition = new CombatKnowledgeActionDefinition
                {
                    SourceId = id,
                    Fidelity = CombatKnowledgeFidelity.Unsupported,
                    Provenance = "runtime table export; script not lowered"
                };
                package.Actions.Add(definition);
            }
            definition.DisplayName = Value(row, "Name", definition.DisplayName);
            definition.BaseCost = Int(row, "Cost", Int(row, "Power", definition.BaseCost));
            definition.Tags = Split(Value(row, "Tags", Value(row, "Tag")))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            definition.TableFields = row;
        }
    }

    private static void EnrichStatuses(
        CombatKnowledgePackage package,
        IEnumerable<Dictionary<string, string>> rows)
    {
        foreach (var row in rows)
        {
            var id = Value(row, "Id");
            if (id.Length == 0)
            {
                continue;
            }
            var definition = package.Statuses.FirstOrDefault(item =>
                string.Equals(item.StatusId, id, StringComparison.OrdinalIgnoreCase));
            if (definition == null)
            {
                definition = new CombatKnowledgeStatusDefinition
                {
                    StatusId = id,
                    Fidelity = CombatKnowledgeFidelity.Unsupported,
                    Provenance = "runtime table export; script not lowered"
                };
                package.Statuses.Add(definition);
            }
            definition.DisplayName = Value(row, "Name", definition.DisplayName);
            definition.UpperBound = Int(row, "UpperBound");
            definition.ReducePerTurn = Int(row, "ReducePerTurn");
            definition.ReducePerUse = Int(row, "ReducePerUse");
            definition.ReducePerAttacked = Int(row, "ReducePerAttacked");
            definition.CanRemainAtZero = string.Equals(
                Value(row, "CanZero"),
                "True",
                StringComparison.OrdinalIgnoreCase);
            definition.TableFields = row;
        }
    }

    private static void EnrichEnemies(
        CombatKnowledgePackage package,
        IEnumerable<Dictionary<string, string>> rows)
    {
        foreach (var row in rows)
        {
            var id = Value(row, "Id");
            if (id.Length == 0)
            {
                continue;
            }
            var definition = package.Enemies.FirstOrDefault(item =>
                string.Equals(item.EnemyId, id, StringComparison.OrdinalIgnoreCase));
            if (definition == null)
            {
                definition = new CombatKnowledgeEnemyDefinition
                {
                    EnemyId = id,
                    Fidelity = CombatKnowledgeFidelity.Unsupported,
                    Provenance = "runtime table export; script not lowered"
                };
                package.Enemies.Add(definition);
            }
            definition.DisplayName = Value(row, "Name", definition.DisplayName);
            definition.MaxHp = Int(row, "Hp", Int(row, "MaxHp"));
            definition.ActionCount = Math.Max(
                1,
                Int(row, "ActionCount", definition.ActionCount));
            definition.ActionIds = Split(Value(
                    row,
                    "CardList",
                    Value(row, "Cards", Value(row, "ActionCards"))))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            definition.TableFields = row;
            foreach (var pair in row)
            {
                if (double.TryParse(
                        pair.Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var value))
                {
                    definition.Features[pair.Key] = value;
                }
            }
        }
    }

    private static void EnrichEncounters(
        CombatKnowledgePackage package,
        IEnumerable<Dictionary<string, string>> rows)
    {
        foreach (var row in rows)
        {
            var id = Value(row, "Id");
            var enemies = Split(Value(
                row,
                "EnemyIds",
                Value(row, "EnemyId", Value(row, "Enemies"))));
            if (id.Length == 0 || enemies.Count == 0)
            {
                continue;
            }
            package.Encounters.Add(new CombatKnowledgeEncounterDefinition
            {
                EncounterId = id,
                EnemyIds = enemies,
                Fidelity = enemies.All(enemy => package.Enemies.Any(item =>
                    string.Equals(
                        item.EnemyId,
                        enemy,
                        StringComparison.OrdinalIgnoreCase)))
                    ? CombatKnowledgeFidelity.Derived
                    : CombatKnowledgeFidelity.Unsupported,
                Provenance = "runtime Level table export"
            });
        }
    }

    private static IEnumerable<Dictionary<string, string>> Rows(
        JsonElement tables,
        string name)
    {
        if (!tables.TryGetProperty(name, out var table)
            || table.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<Dictionary<string, string>>();
        }
        return table.EnumerateArray().Select(element =>
            element.EnumerateObject().ToDictionary(
                property => property.Name,
                property => property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? ""
                    : property.Value.ToString(),
                StringComparer.OrdinalIgnoreCase));
    }

    private static string Value(
        IReadOnlyDictionary<string, string> row,
        string key,
        string fallback = "")
    {
        return row.TryGetValue(key, out var value) ? value ?? "" : fallback;
    }

    private static int Int(
        IReadOnlyDictionary<string, string> row,
        string key,
        int fallback = 0)
    {
        return int.TryParse(Value(row, key), out var value) ? value : fallback;
    }

    private static List<string> Split(string value)
    {
        return Regex.Split(value ?? "", @"[\s,;|]+")
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }
}

internal sealed class CompilerOptions
{
    public bool Success { get; private set; }
    public string Error { get; private set; } = "";
    public string ScriptsPath { get; private set; } = "";
    public string OutputPath { get; private set; } = "";
    public string GameBuild { get; private set; } = "";
    public string ReportPath { get; private set; } = "";
    public string TablesPath { get; private set; } = "";

    public static CompilerOptions Parse(string[] args)
    {
        var result = new CompilerOptions();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            values[args[i]] = args[i + 1];
        }
        values.TryGetValue("--scripts", out var scripts);
        values.TryGetValue("--output", out var output);
        values.TryGetValue("--game-build", out var build);
        values.TryGetValue("--report", out var report);
        values.TryGetValue("--tables", out var tables);
        if (string.IsNullOrWhiteSpace(scripts) || !File.Exists(scripts))
        {
            result.Error = "AllScripts.cs was not found.";
            return result;
        }
        if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(build))
        {
            result.Error = "--output and --game-build are required.";
            return result;
        }
        result.Success = true;
        result.ScriptsPath = scripts;
        result.OutputPath = output;
        result.GameBuild = build;
        result.ReportPath = report ?? "";
        result.TablesPath = tables ?? "";
        if (result.TablesPath.Length > 0 && !File.Exists(result.TablesPath))
        {
            result.Success = false;
            result.Error = "runtime table export was not found.";
        }
        return result;
    }
}

internal sealed class CompileResult
{
    public CombatKnowledgePackage Package { get; set; } = new();
    public CompilerReport Report { get; set; } = new();
}

internal sealed class CompilerReport
{
    public string GameBuild { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public string TableExportPath { get; set; } = "";
    public int EntityCount { get; set; }
    public int RegisteredScriptCount { get; set; }
    public int OperationCount { get; set; }
    public int UnsupportedOperationCount { get; set; }
    public List<CompilerUnsupportedOperation> Unsupported { get; set; } = new();
    public List<string> ParseDiagnostics { get; set; } = new();
}

internal sealed class RegisteredScript
{
    public string EntityId { get; set; } = "";
    public string Stage { get; set; } = "";
}

internal sealed class CompilerUnsupportedOperation
{
    public string EntityId { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Api { get; set; } = "";
    public string SourceLocation { get; set; } = "";
    public string Reason { get; set; } = "";
}
