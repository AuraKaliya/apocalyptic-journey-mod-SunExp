using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

return ArchitectureGate.Run(args);

internal static class ArchitectureGate
{
    public static int Run(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            var rules = RuleDocument.Load(options.RulesPath, options.RuleSet);
            var exceptions = ExceptionDocument.Load(options.ExceptionsPath, rules.MaxExceptions);
            var result = Analyze(options.RepoRoot, rules, exceptions);
            foreach (var diagnostic in result.Diagnostics)
            {
                Console.Error.WriteLine(diagnostic);
            }
            if (!result.Success)
            {
                Console.Error.WriteLine(
                    $"Terrias semantic architecture gate failed: violations={result.ViolationCount}, cycles={result.CycleCount}.");
                return 1;
            }

            Console.WriteLine(
                $"Terrias semantic architecture gate passed: files={result.FileCount}, edges={result.EdgeCount}, exceptions={exceptions.Items.Count}.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Terrias semantic architecture gate error: " + ex.Message);
            return 2;
        }
    }

    private static AnalysisResult Analyze(
        string repoRoot,
        RuleSet rules,
        ExceptionDocument exceptions)
    {
        var root = Path.GetFullPath(repoRoot);
        var fileLayers = DiscoverFiles(root, rules);
        var diagnostics = new List<string>();
        ValidateNamespaces(root, fileLayers, diagnostics);

        var trees = fileLayers.Keys
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => CSharpSyntaxTree.ParseText(
                File.ReadAllText(path),
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
                path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "TerriasArchitectureSemanticModel",
            trees,
            MetadataReferences(root),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var layerByTree = trees.ToDictionary(
            tree => tree,
            tree => fileLayers[Path.GetFullPath(tree.FilePath)]);
        var actualEdges = new HashSet<LayerEdge>();
        var effectiveEdges = new HashSet<LayerEdge>();
        var violations = new List<DependencyViolation>();
        var matchedExceptions = new HashSet<DependencyException>();

        foreach (var tree in trees)
        {
            var sourceLayer = layerByTree[tree];
            var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            foreach (var node in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                var symbolInfo = model.GetSymbolInfo(node);
                var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
                if (symbol is IAliasSymbol alias)
                {
                    symbol = alias.Target;
                }
                symbol ??= model.GetTypeInfo(node).Type;
                if (symbol is INamespaceSymbol)
                {
                    continue;
                }
                if (symbol is null || !TryResolveTargetLayer(symbol, layerByTree, out var targetLayer))
                {
                    continue;
                }
                if (string.Equals(sourceLayer.Id, targetLayer.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                var edge = new LayerEdge(sourceLayer.Id, targetLayer.Id);
                actualEdges.Add(edge);
                if (sourceLayer.AllowedDependencies.Contains(targetLayer.Id))
                {
                    effectiveEdges.Add(edge);
                    continue;
                }

                var relative = Normalize(Path.GetRelativePath(root, tree.FilePath));
                var matchingException = exceptions.Items.FirstOrDefault(item =>
                    string.Equals(item.SourceFile, relative, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.TargetLayer, targetLayer.Id, StringComparison.Ordinal));
                if (matchingException is not null)
                {
                    matchedExceptions.Add(matchingException);
                    continue;
                }

                var line = tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
                violations.Add(new DependencyViolation(
                    relative,
                    line,
                    sourceLayer.Id,
                    targetLayer.Id,
                    symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                effectiveEdges.Add(edge);
            }
        }

        foreach (var violation in violations
                     .Distinct()
                     .OrderBy(item => item.SourceFile, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Line)
                     .ThenBy(item => item.TargetLayer, StringComparer.Ordinal))
        {
            diagnostics.Add(
                $"dependency: {violation.SourceFile}:{violation.Line} {violation.SourceLayer} -> {violation.TargetLayer} via {violation.Symbol}");
        }

        foreach (var stale in exceptions.Items.Except(matchedExceptions))
        {
            diagnostics.Add(
                $"stale-exception: {stale.SourceFile} -> {stale.TargetLayer}; remove the completed migration allowance");
        }

        var cycles = FindShortestCycles(rules.Layers.Select(layer => layer.Id), effectiveEdges);
        foreach (var cycle in cycles)
        {
            diagnostics.Add("dependency-cycle: " + string.Join(" -> ", cycle));
        }

        var namespaceFailures = diagnostics.Count(item => item.StartsWith("namespace:", StringComparison.Ordinal));
        var staleFailures = diagnostics.Count(item => item.StartsWith("stale-exception:", StringComparison.Ordinal));
        var distinctViolations = violations.Distinct().Count();
        return new AnalysisResult(
            Success: namespaceFailures == 0 && staleFailures == 0 && distinctViolations == 0 && cycles.Count == 0,
            FileCount: trees.Length,
            EdgeCount: actualEdges.Count,
            ViolationCount: distinctViolations + namespaceFailures + staleFailures,
            CycleCount: cycles.Count,
            Diagnostics: diagnostics);
    }

    private static Dictionary<string, LayerRule> DiscoverFiles(string root, RuleSet rules)
    {
        var result = new Dictionary<string, LayerRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in rules.Layers)
        {
            foreach (var relativeRoot in layer.Roots)
            {
                var directory = ResolveInside(root, relativeRoot);
                if (!Directory.Exists(directory))
                {
                    throw new InvalidOperationException($"Architecture layer root is missing: {relativeRoot}");
                }
                foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
                {
                    if (IsGenerated(file)) continue;
                    AddFile(result, file, layer);
                }
            }
            foreach (var relativeFile in layer.Files)
            {
                var file = ResolveInside(root, relativeFile);
                if (!File.Exists(file))
                {
                    throw new InvalidOperationException($"Architecture layer file is missing: {relativeFile}");
                }
                AddFile(result, file, layer);
            }
        }

        var projectRoot = ResolveInside(root, rules.ProjectRoot);
        foreach (var file in Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsGenerated(file)) continue;
            if (!result.ContainsKey(Path.GetFullPath(file)))
            {
                throw new InvalidOperationException(
                    "C# source is not assigned to an architecture layer: " + Normalize(Path.GetRelativePath(root, file)));
            }
        }
        return result;
    }

    private static void AddFile(
        IDictionary<string, LayerRule> files,
        string path,
        LayerRule layer)
    {
        var full = Path.GetFullPath(path);
        if (files.TryGetValue(full, out var existing) && !ReferenceEquals(existing, layer))
        {
            throw new InvalidOperationException(
                $"C# source is assigned to multiple architecture layers: {full} ({existing.Id}, {layer.Id})");
        }
        files[full] = layer;
    }

    private static void ValidateNamespaces(
        string root,
        IReadOnlyDictionary<string, LayerRule> files,
        ICollection<string> diagnostics)
    {
        foreach (var pair in files)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(pair.Key), path: pair.Key);
            var declarations = tree.GetRoot().DescendantNodes()
                .Where(node => node is BaseNamespaceDeclarationSyntax)
                .Cast<BaseNamespaceDeclarationSyntax>()
                .ToArray();
            var relative = Normalize(Path.GetRelativePath(root, pair.Key));
            if (declarations.Length == 0)
            {
                diagnostics.Add($"namespace: {relative} must declare {pair.Value.NamespacePrefix}");
                continue;
            }
            foreach (var declaration in declarations)
            {
                var actual = declaration.Name.ToString();
                if (!actual.Equals(pair.Value.NamespacePrefix, StringComparison.Ordinal)
                    && !actual.StartsWith(pair.Value.NamespacePrefix + ".", StringComparison.Ordinal))
                {
                    var line = tree.GetLineSpan(declaration.Name.Span).StartLinePosition.Line + 1;
                    diagnostics.Add(
                        $"namespace: {relative}:{line} expected {pair.Value.NamespacePrefix}, found {actual}");
                }
            }
        }
    }

    private static bool TryResolveTargetLayer(
        ISymbol symbol,
        IReadOnlyDictionary<SyntaxTree, LayerRule> layerByTree,
        out LayerRule layer)
    {
        IEnumerable<ISymbol> candidates = symbol switch
        {
            IMethodSymbol method => new ISymbol[] { method.OriginalDefinition, method.ContainingType },
            IPropertySymbol property => new ISymbol[] { property.OriginalDefinition, property.ContainingType },
            IFieldSymbol field => new ISymbol[] { field.OriginalDefinition, field.ContainingType },
            IEventSymbol @event => new ISymbol[] { @event.OriginalDefinition, @event.ContainingType },
            INamedTypeSymbol type => new ISymbol[] { type.OriginalDefinition },
            _ => new[] { symbol.OriginalDefinition }
        };
        foreach (var candidate in candidates)
        {
            foreach (var location in candidate.Locations)
            {
                if (location.SourceTree is not null && layerByTree.TryGetValue(location.SourceTree, out layer!))
                {
                    return true;
                }
            }
        }
        layer = null!;
        return false;
    }

    private static IReadOnlyList<MetadataReference> MetadataReferences(string root)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trusted = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var path in trusted)
        {
            paths.TryAdd(Path.GetFileName(path), path);
        }
        foreach (var directory in new[]
                 {
                     Path.Combine(root, "Managed"),
                     Path.Combine(root, "AuraSharedRuntime-Dev", "bin", "Release", "net472"),
                     Path.Combine(root, "AuraDirectorDetour-Dev", "bin", "Release", "net472")
                 })
        {
            if (!Directory.Exists(directory)) continue;
            foreach (var path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                paths.TryAdd(Path.GetFileName(path), path);
            }
        }
        var references = new List<MetadataReference>();
        foreach (var path in paths.Values)
        {
            try
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            catch (BadImageFormatException)
            {
                // Native or otherwise non-managed DLLs are irrelevant to source-layer binding.
            }
        }
        return references;
    }

    private static IReadOnlyList<IReadOnlyList<string>> FindShortestCycles(
        IEnumerable<string> nodes,
        IReadOnlyCollection<LayerEdge> edges)
    {
        var adjacency = nodes.ToDictionary(
            node => node,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            adjacency[edge.Source].Add(edge.Target);
        }

        var canonical = new HashSet<string>(StringComparer.Ordinal);
        var cycles = new List<IReadOnlyList<string>>();
        foreach (var start in adjacency.Keys.OrderBy(value => value, StringComparer.Ordinal))
        {
            var queue = new Queue<List<string>>();
            queue.Enqueue(new List<string> { start });
            var shortest = int.MaxValue;
            while (queue.Count > 0)
            {
                var path = queue.Dequeue();
                if (path.Count >= shortest) continue;
                foreach (var next in adjacency[path[^1]])
                {
                    if (next == start && path.Count > 1)
                    {
                        var cycle = path.Concat(new[] { start }).ToArray();
                        shortest = cycle.Length;
                        var key = CanonicalCycle(cycle);
                        if (canonical.Add(key)) cycles.Add(cycle);
                        continue;
                    }
                    if (!path.Contains(next, StringComparer.Ordinal))
                    {
                        queue.Enqueue(path.Concat(new[] { next }).ToList());
                    }
                }
            }
        }
        return cycles
            .OrderBy(cycle => cycle.Count)
            .ThenBy(cycle => string.Join("/", cycle), StringComparer.Ordinal)
            .ToArray();
    }

    private static string CanonicalCycle(IReadOnlyList<string> cycle)
    {
        var body = cycle.Take(cycle.Count - 1).ToArray();
        var rotations = Enumerable.Range(0, body.Length)
            .Select(index => string.Join("/", body.Skip(index).Concat(body.Take(index))))
            .OrderBy(value => value, StringComparer.Ordinal);
        return rotations.First();
    }

    private static string ResolveInside(string root, string relative)
    {
        if (Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException("Architecture paths must be repository-relative: " + relative);
        }
        var resolved = Path.GetFullPath(Path.Combine(root, relative));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        if (!resolved.Equals(root, StringComparison.OrdinalIgnoreCase)
            && !resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Architecture path escapes repository: " + relative);
        }
        return resolved;
    }

    private static bool IsGenerated(string path)
    {
        var normalized = Normalize(path);
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/VisualAssets/UnityProject/", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value) => value.Replace('\\', '/');
}

internal sealed record Options(string RepoRoot, string RulesPath, string ExceptionsPath, string RuleSet)
{
    public static Options Parse(IReadOnlyList<string> args)
    {
        string? repoRoot = null;
        string? rules = null;
        string? exceptions = null;
        var ruleSet = "terrias";
        for (var index = 0; index < args.Count; index++)
        {
            var value = args[index];
            string Next()
            {
                if (++index >= args.Count) throw new ArgumentException("Missing value for " + value);
                return args[index];
            }
            switch (value)
            {
                case "--repo-root": repoRoot = Next(); break;
                case "--rules": rules = Next(); break;
                case "--exceptions": exceptions = Next(); break;
                case "--rule-set": ruleSet = Next(); break;
                default: throw new ArgumentException("Unknown argument: " + value);
            }
        }
        repoRoot ??= Directory.GetCurrentDirectory();
        rules ??= Path.Combine(repoRoot, "tools", "architecture-boundary-rules.json");
        exceptions ??= Path.Combine(repoRoot, "tools", "architecture-boundary-exceptions.json");
        return new Options(
            Path.GetFullPath(repoRoot),
            Path.GetFullPath(rules),
            Path.GetFullPath(exceptions),
            ruleSet);
    }
}

internal sealed class RuleDocument
{
    public static RuleSet Load(string path, string ruleSet)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 2)
        {
            throw new InvalidOperationException("Semantic architecture gate requires rules schemaVersion 2.");
        }
        var selected = root.GetProperty("ruleSets").GetProperty(ruleSet).GetProperty("semanticLayers");
        var layers = selected.GetProperty("layers").EnumerateArray()
            .Select(element => new LayerRule(
                element.GetProperty("id").GetString()!,
                element.TryGetProperty("roots", out var roots)
                    ? roots.EnumerateArray().Select(item => item.GetString()!).ToArray()
                    : Array.Empty<string>(),
                element.TryGetProperty("files", out var files)
                    ? files.EnumerateArray().Select(item => item.GetString()!).ToArray()
                    : Array.Empty<string>(),
                element.GetProperty("namespacePrefix").GetString()!,
                element.GetProperty("allowedDependencies").EnumerateArray()
                    .Select(item => item.GetString()!)
                    .ToHashSet(StringComparer.Ordinal)))
            .ToArray();
        var ids = layers.Select(layer => layer.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var layer in layers)
        {
            foreach (var dependency in layer.AllowedDependencies)
            {
                if (!ids.Contains(dependency))
                    throw new InvalidOperationException($"Layer {layer.Id} allows unknown dependency {dependency}.");
            }
        }
        return new RuleSet(
            selected.GetProperty("projectRoot").GetString()!,
            selected.GetProperty("maxExceptions").GetInt32(),
            layers);
    }
}

internal sealed class ExceptionDocument
{
    public IReadOnlyList<DependencyException> Items { get; }

    private ExceptionDocument(IReadOnlyList<DependencyException> items) => Items = items;

    public static ExceptionDocument Load(string path, int maxExceptions)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || !root.GetProperty("locked").GetBoolean())
        {
            throw new InvalidOperationException("Architecture exception ledger must be schemaVersion 1 and locked.");
        }
        var items = root.GetProperty("exceptions").EnumerateArray()
            .Select(element => new DependencyException(
                Normalize(element.GetProperty("sourceFile").GetString()!),
                element.GetProperty("targetLayer").GetString()!,
                Required(element, "reason"),
                Required(element, "owner"),
                Required(element, "removeByMilestone")))
            .ToArray();
        if (items.Length > maxExceptions)
        {
            throw new InvalidOperationException(
                $"Architecture exception budget exceeded: {items.Length} > {maxExceptions}.");
        }
        var duplicate = items.GroupBy(
                item => item.SourceFile + "\n" + item.TargetLayer,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException("Duplicate architecture exception: " + duplicate.Key.Replace('\n', ' '));
        }
        return new ExceptionDocument(items);
    }

    private static string Required(JsonElement element, string name)
    {
        var value = element.GetProperty(name).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Architecture exception is missing " + name + ".");
        return value;
    }

    private static string Normalize(string value) => value.Replace('\\', '/');
}

internal sealed record RuleSet(string ProjectRoot, int MaxExceptions, IReadOnlyList<LayerRule> Layers);
internal sealed record LayerRule(
    string Id,
    IReadOnlyList<string> Roots,
    IReadOnlyList<string> Files,
    string NamespacePrefix,
    IReadOnlySet<string> AllowedDependencies);
internal sealed record DependencyException(
    string SourceFile,
    string TargetLayer,
    string Reason,
    string Owner,
    string RemoveByMilestone);
internal sealed record LayerEdge(string Source, string Target);
internal sealed record DependencyViolation(
    string SourceFile,
    int Line,
    string SourceLayer,
    string TargetLayer,
    string Symbol);
internal sealed record AnalysisResult(
    bool Success,
    int FileCount,
    int EdgeCount,
    int ViolationCount,
    int CycleCount,
    IReadOnlyList<string> Diagnostics);
