using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

if (args.Length == 0)
{
    PrintUsage();
    return 2;
}

var command = args[0].ToLowerInvariant();
var options = CommandLineOptions.Parse(args.Skip(1));
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

switch (command)
{
    case "inventory":
        RunInventory(options, jsonOptions);
        return 0;
    case "compare":
        RunCompare(options, jsonOptions);
        return 0;
    default:
        Console.Error.WriteLine("Unknown command: " + command);
        PrintUsage();
        return 2;
}

static void RunInventory(CommandLineOptions options, JsonSerializerOptions jsonOptions)
{
    var input = RequireDirectory(options, "input");
    var output = RequirePath(options, "output");
    var markdown = options.GetSingle("markdown");
    var csv = options.GetSingle("csv");
    var expectedCount = options.GetInt("expected-count");
    var requiredAssemblies = options.GetMany("required-assembly")
        .Concat(options.GetMany("expected-assembly"))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var assemblies = Directory.EnumerateFiles(input, "*.dll", SearchOption.TopDirectoryOnly)
        .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
        .Select(path => AssemblyMetadataReader.Read(path))
        .ToList();
    var names = assemblies.Select(item => item.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var missing = requiredAssemblies.Where(item => !names.Contains(item)).ToArray();
    var metadataFailures = assemblies.Where(item => item.MetadataError != null).Select(item => item.FileName).ToArray();
    var complete = (expectedCount == null || assemblies.Count == expectedCount)
        && missing.Length == 0
        && metadataFailures.Length == 0;

    var manifest = new ManagedInventory
    {
        SchemaVersion = 1,
        AppId = options.GetSingle("app-id") ?? "",
        SteamBuildId = options.GetSingle("steam-build-id") ?? "",
        RuntimeVersion = options.GetSingle("runtime-version") ?? "",
        UnityVersion = options.GetSingle("unity-version") ?? "",
        SourcePath = Path.GetFullPath(options.GetSingle("source-path") ?? input),
        InventoryPath = input,
        CapturedAtUtc = DateTimeOffset.UtcNow,
        ExpectedAssemblyCount = expectedCount,
        RequiredAssemblies = requiredAssemblies,
        AssemblyCount = assemblies.Count,
        Complete = complete,
        MissingAssemblies = missing,
        MetadataFailures = metadataFailures,
        Assemblies = assemblies
    };

    WriteJson(output, manifest, jsonOptions);
    if (!string.IsNullOrWhiteSpace(csv))
    {
        WriteInventoryCsv(csv, manifest);
    }
    if (!string.IsNullOrWhiteSpace(markdown))
    {
        WriteInventoryMarkdown(markdown, manifest);
    }

    Console.WriteLine($"Managed inventory: assemblies={assemblies.Count}, complete={complete}, missing={missing.Length}, metadataFailures={metadataFailures.Length}");
}

static void RunCompare(CommandLineOptions options, JsonSerializerOptions jsonOptions)
{
    var baselinePath = RequireDirectory(options, "baseline");
    var currentPath = RequireDirectory(options, "current");
    var output = RequirePath(options, "output");
    var markdown = options.GetSingle("markdown");
    var requested = options.GetMany("assembly").ToHashSet(StringComparer.OrdinalIgnoreCase);
    var baselineFiles = Directory.EnumerateFiles(baselinePath, "*.dll", SearchOption.TopDirectoryOnly)
        .ToDictionary(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
    var currentFiles = Directory.EnumerateFiles(currentPath, "*.dll", SearchOption.TopDirectoryOnly)
        .ToDictionary(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
    var names = baselineFiles.Keys.Concat(currentFiles.Keys)
        .Where(name => requested.Count == 0 || requested.Contains(name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    var comparisons = new List<AssemblyComparison>();

    foreach (var name in names)
    {
        if (!baselineFiles.TryGetValue(name, out var baselineFile))
        {
            var added = AssemblyMetadataReader.Read(currentFiles[name]);
            comparisons.Add(AssemblyComparison.Added(added));
            continue;
        }
        if (!currentFiles.TryGetValue(name, out var currentFile))
        {
            var removed = AssemblyMetadataReader.Read(baselineFile);
            comparisons.Add(AssemblyComparison.Removed(removed));
            continue;
        }

        var baseline = AssemblyMetadataReader.Read(baselineFile, includeSurface: true);
        var current = AssemblyMetadataReader.Read(currentFile, includeSurface: true);
        var baselineTypes = baseline.PublicTypes.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var currentTypes = current.PublicTypes.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var removedTypes = baselineTypes.Keys.Except(currentTypes.Keys, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var addedTypes = currentTypes.Keys.Except(baselineTypes.Keys, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var changedTypes = baselineTypes.Keys.Intersect(currentTypes.Keys, StringComparer.Ordinal)
            .Where(nameValue => !string.Equals(baselineTypes[nameValue].Shape, currentTypes[nameValue].Shape, StringComparison.Ordinal))
            .Select(nameValue => new ChangedTypeShape
            {
                Name = nameValue,
                Baseline = baselineTypes[nameValue].Shape,
                Current = currentTypes[nameValue].Shape
            })
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        var removedMembers = baseline.PublicMembers.Except(current.PublicMembers, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var addedMembers = current.PublicMembers.Except(baseline.PublicMembers, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var hashChanged = !string.Equals(baseline.Sha256, current.Sha256, StringComparison.OrdinalIgnoreCase);

        comparisons.Add(new AssemblyComparison
        {
            FileName = name,
            Status = hashChanged ? "Changed" : "Unchanged",
            BaselineSha256 = baseline.Sha256,
            CurrentSha256 = current.Sha256,
            BaselineSize = baseline.Size,
            CurrentSize = current.Size,
            BaselineTypeCount = baseline.TypeCount,
            CurrentTypeCount = current.TypeCount,
            RemovedTypes = removedTypes,
            AddedTypes = addedTypes,
            ChangedTypeShapes = changedTypes,
            RemovedMembers = removedMembers,
            AddedMembers = addedMembers,
            BreakingCount = removedTypes.Length + changedTypes.Length + removedMembers.Length,
            AdditiveCount = addedTypes.Length + addedMembers.Length,
            BehaviorReview = hashChanged
        });
    }

    var report = new ManagedComparisonReport
    {
        SchemaVersion = 1,
        BaselinePath = baselinePath,
        CurrentPath = currentPath,
        GeneratedAtUtc = DateTimeOffset.UtcNow,
        Assemblies = comparisons,
        BreakingCount = comparisons.Sum(item => item.BreakingCount),
        AdditiveCount = comparisons.Sum(item => item.AdditiveCount),
        BehaviorReviewCount = comparisons.Count(item => item.BehaviorReview)
    };
    WriteJson(output, report, jsonOptions);
    if (!string.IsNullOrWhiteSpace(markdown))
    {
        WriteComparisonMarkdown(markdown, report);
    }
    Console.WriteLine($"Managed comparison: assemblies={comparisons.Count}, breaking={report.BreakingCount}, additive={report.AdditiveCount}, behaviorReview={report.BehaviorReviewCount}");
}

static void WriteInventoryCsv(string path, ManagedInventory manifest)
{
    EnsureParent(path);
    var lines = new List<string> { "fileName,size,sha256,mvid,assemblyName,assemblyVersion,typeCount,publicTypeCount,methodCount,publicMemberCount,status" };
    lines.AddRange(manifest.Assemblies.Select(item => string.Join(",", new[]
    {
        Csv(item.FileName), item.Size.ToString(CultureInfo.InvariantCulture), Csv(item.Sha256), Csv(item.Mvid), Csv(item.AssemblyName),
        Csv(item.AssemblyVersion), item.TypeCount.ToString(CultureInfo.InvariantCulture), item.PublicTypeCount.ToString(CultureInfo.InvariantCulture),
        item.MethodCount.ToString(CultureInfo.InvariantCulture), item.PublicMemberCount.ToString(CultureInfo.InvariantCulture), Csv(item.MetadataError == null ? "Present" : "MetadataError")
    })));
    lines.AddRange(manifest.MissingAssemblies.Select(name => string.Join(",", new[] { Csv(name), "", "", "", "", "", "", "", "", "", "Missing" })));
    File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
}

static void WriteInventoryMarkdown(string path, ManagedInventory manifest)
{
    EnsureParent(path);
    var builder = new StringBuilder();
    builder.AppendLine("# Game Managed Assembly Inventory");
    builder.AppendLine();
    builder.AppendLine($"- Runtime version: `{EscapeMarkdown(manifest.RuntimeVersion)}`");
    builder.AppendLine($"- Steam build ID: `{EscapeMarkdown(manifest.SteamBuildId)}`");
    builder.AppendLine($"- Unity version: `{EscapeMarkdown(manifest.UnityVersion)}`");
    builder.AppendLine($"- Assemblies present: {manifest.AssemblyCount}");
    builder.AppendLine($"- Expected assemblies: {(manifest.ExpectedAssemblyCount?.ToString(CultureInfo.InvariantCulture) ?? "not specified")}");
    builder.AppendLine($"- Completeness: `{(manifest.Complete ? "complete" : "partial")}`");
    if (manifest.MissingAssemblies.Length > 0)
    {
        builder.AppendLine($"- Missing: {string.Join(", ", manifest.MissingAssemblies.Select(value => $"`{EscapeMarkdown(value)}`"))}");
    }
    builder.AppendLine();
    builder.AppendLine("| Assembly | Size (B) | SHA-256 | MVID | Types | Public types | Public members | Status |");
    builder.AppendLine("|---|---:|---|---|---:|---:|---:|---|");
    foreach (var item in manifest.Assemblies)
    {
        builder.AppendLine($"| `{EscapeMarkdown(item.FileName)}` | {item.Size} | `{ShortHash(item.Sha256)}` | `{EscapeMarkdown(item.Mvid)}` | {item.TypeCount} | {item.PublicTypeCount} | {item.PublicMemberCount} | {(item.MetadataError == null ? "Present" : "Metadata error")} |");
    }
    foreach (var missing in manifest.MissingAssemblies)
    {
        builder.AppendLine($"| `{EscapeMarkdown(missing)}` |  |  |  |  |  |  | **Missing** |");
    }
    File.WriteAllText(path, builder.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
}

static void WriteComparisonMarkdown(string path, ManagedComparisonReport report)
{
    EnsureParent(path);
    var builder = new StringBuilder();
    builder.AppendLine("# Game Managed API Comparison");
    builder.AppendLine();
    builder.AppendLine($"- Baseline: `{EscapeMarkdown(report.BaselinePath)}`");
    builder.AppendLine($"- Current: `{EscapeMarkdown(report.CurrentPath)}`");
    builder.AppendLine($"- Breaking candidates: {report.BreakingCount}");
    builder.AppendLine($"- Additive candidates: {report.AdditiveCount}");
    builder.AppendLine($"- Behavior review candidates: {report.BehaviorReviewCount}");
    builder.AppendLine();
    builder.AppendLine("| Assembly | Status | Size delta | Types | Breaking | Additive | Behavior review |");
    builder.AppendLine("|---|---|---:|---:|---:|---:|---|");
    foreach (var item in report.Assemblies)
    {
        var sizeDelta = item.CurrentSize - item.BaselineSize;
        var typeDelta = item.CurrentTypeCount - item.BaselineTypeCount;
        builder.AppendLine($"| `{EscapeMarkdown(item.FileName)}` | {item.Status} | {sizeDelta:+#;-#;0} | {item.BaselineTypeCount} -> {item.CurrentTypeCount} ({typeDelta:+#;-#;0}) | {item.BreakingCount} | {item.AdditiveCount} | {(item.BehaviorReview ? "Yes" : "No")} |");
    }

    foreach (var item in report.Assemblies.Where(value => value.BreakingCount > 0 || value.AdditiveCount > 0))
    {
        builder.AppendLine();
        builder.AppendLine("## " + item.FileName);
        AppendDetails(builder, "Removed types", item.RemovedTypes);
        AppendDetails(builder, "Added types", item.AddedTypes);
        AppendDetails(builder, "Changed type shapes", item.ChangedTypeShapes.Select(value => $"{value.Name}: {value.Baseline} -> {value.Current}").ToArray());
        AppendDetails(builder, "Removed members", item.RemovedMembers);
        AppendDetails(builder, "Added members", item.AddedMembers);
    }
    File.WriteAllText(path, builder.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
}

static void AppendDetails(StringBuilder builder, string title, IReadOnlyCollection<string> values)
{
    if (values.Count == 0)
    {
        return;
    }
    builder.AppendLine();
    builder.AppendLine("### " + title);
    foreach (var value in values.Take(200))
    {
        builder.AppendLine("- `" + EscapeMarkdown(value) + "`");
    }
    if (values.Count > 200)
    {
        builder.AppendLine($"- ... {values.Count - 200} more entries are available in the JSON report.");
    }
}

static string RequireDirectory(CommandLineOptions options, string key)
{
    var path = RequirePath(options, key);
    if (!Directory.Exists(path))
    {
        throw new DirectoryNotFoundException(path);
    }
    return path;
}

static string RequirePath(CommandLineOptions options, string key)
{
    var value = options.GetSingle(key);
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new ArgumentException("Missing required option --" + key);
    }
    return Path.GetFullPath(value);
}

static void WriteJson<T>(string path, T value, JsonSerializerOptions options)
{
    EnsureParent(path);
    File.WriteAllText(path, JsonSerializer.Serialize(value, options).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
}

static void EnsureParent(string path)
{
    var parent = Path.GetDirectoryName(Path.GetFullPath(path));
    if (!string.IsNullOrEmpty(parent))
    {
        Directory.CreateDirectory(parent);
    }
}

static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
static string ShortHash(string value) => value.Length <= 16 ? value : value[..16];
static string EscapeMarkdown(string value) => value.Replace("|", "\\|").Replace("`", "'");

static void PrintUsage()
{
    Console.WriteLine("GameManagedInspector inventory --input <dir> --output <json> [--csv <csv>] [--markdown <md>]");
    Console.WriteLine("GameManagedInspector compare --baseline <dir> --current <dir> --output <json> [--markdown <md>] [--assembly <dll>]");
}

internal sealed class CommandLineOptions
{
    private readonly Dictionary<string, List<string>> values = new(StringComparer.OrdinalIgnoreCase);

    public static CommandLineOptions Parse(IEnumerable<string> arguments)
    {
        var result = new CommandLineOptions();
        var items = arguments.ToArray();
        for (var index = 0; index < items.Length; index++)
        {
            var token = items[index];
            if (!token.StartsWith("--", StringComparison.Ordinal) || index + 1 >= items.Length)
            {
                throw new ArgumentException("Options must use --name value syntax: " + token);
            }
            var key = token[2..];
            var value = items[++index];
            if (!result.values.TryGetValue(key, out var list))
            {
                list = new List<string>();
                result.values[key] = list;
            }
            list.Add(value);
        }
        return result;
    }

    public string? GetSingle(string key) => values.TryGetValue(key, out var list) ? list[^1] : null;
    public string[] GetMany(string key) => values.TryGetValue(key, out var list) ? list.ToArray() : Array.Empty<string>();
    public int? GetInt(string key) => int.TryParse(GetSingle(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}

internal static class AssemblyMetadataReader
{
    public static AssemblyInventoryItem Read(string path, bool includeSurface = false)
    {
        var file = new FileInfo(path);
        var item = new AssemblyInventoryItem
        {
            FileName = file.Name,
            Size = file.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
        };
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                throw new BadImageFormatException("PE file does not contain CLR metadata.");
            }
            var reader = peReader.GetMetadataReader();
            var module = reader.GetModuleDefinition();
            item.Mvid = reader.GetGuid(module.Mvid).ToString("D");
            if (reader.IsAssembly)
            {
                var assembly = reader.GetAssemblyDefinition();
                item.AssemblyName = reader.GetString(assembly.Name);
                item.AssemblyVersion = assembly.Version.ToString();
            }
            item.TypeCount = reader.TypeDefinitions.Count(handle => reader.GetString(reader.GetTypeDefinition(handle).Name) != "<Module>");
            item.MethodCount = reader.MethodDefinitions.Count;
            var surface = PublicSurfaceReader.Read(reader);
            item.PublicTypeCount = surface.Types.Count;
            item.PublicMemberCount = surface.Members.Count;
            if (includeSurface)
            {
                item.PublicTypes = surface.Types;
                item.PublicMembers = surface.Members;
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or InvalidOperationException)
        {
            item.MetadataError = ex.ToString();
        }
        return item;
    }
}

internal static class PublicSurfaceReader
{
    public static PublicSurface Read(MetadataReader reader)
    {
        var provider = new TypeNameProvider();
        var types = new List<PublicTypeShape>();
        var members = new List<string>();
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (!IsExternallyVisible(reader, handle))
            {
                continue;
            }
            var typeName = GetFullName(reader, handle);
            var baseType = provider.GetTypeName(reader, type.BaseType);
            var interfaces = type.GetInterfaceImplementations()
                .Select(interfaceHandle => provider.GetTypeName(reader, reader.GetInterfaceImplementation(interfaceHandle).Interface))
                .OrderBy(value => value, StringComparer.Ordinal);
            types.Add(new PublicTypeShape
            {
                Name = typeName,
                Shape = $"{GetKind(reader, type)}|base={baseType}|interfaces={string.Join(",", interfaces)}"
            });

            foreach (var fieldHandle in type.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Public)
                {
                    continue;
                }
                var fieldType = field.DecodeSignature(provider, null);
                var scope = (field.Attributes & FieldAttributes.Static) != 0 ? "static" : "instance";
                var literal = (field.Attributes & FieldAttributes.Literal) != 0 ? "|const=" + ReadConstant(reader, field.GetDefaultValue()) : "";
                members.Add($"F|{typeName}|{reader.GetString(field.Name)}|{fieldType}|{scope}{literal}");
            }

            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                {
                    continue;
                }
                var signature = method.DecodeSignature(provider, null);
                var scope = (method.Attributes & MethodAttributes.Static) != 0 ? "static" : "instance";
                var parameterMetadata = method.GetParameters()
                    .Select(parameterHandle => reader.GetParameter(parameterHandle))
                    .Where(parameter => parameter.SequenceNumber > 0)
                    .OrderBy(parameter => parameter.SequenceNumber)
                    .Select(parameter => FormatParameterMetadata(reader, parameter))
                    .ToArray();
                var parameterDetails = parameterMetadata.Any(value => value.Length > 0)
                    ? "|parameterMetadata=" + string.Join(",", parameterMetadata.Select((value, index) => $"{index + 1}:{value}"))
                    : "";
                members.Add($"M|{typeName}|{reader.GetString(method.Name)}`{method.GetGenericParameters().Count}|{scope}|({string.Join(",", signature.ParameterTypes)})->{signature.ReturnType}{parameterDetails}");
            }

            foreach (var propertyHandle in type.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                var accessors = property.GetAccessors();
                var getterPublic = IsPublic(reader, accessors.Getter);
                var setterPublic = IsPublic(reader, accessors.Setter);
                if (!getterPublic && !setterPublic)
                {
                    continue;
                }
                var signature = property.DecodeSignature(provider, null);
                var access = getterPublic && setterPublic ? "get,set" : getterPublic ? "get" : "set";
                members.Add($"P|{typeName}|{reader.GetString(property.Name)}|({string.Join(",", signature.ParameterTypes)})->{signature.ReturnType}|{access}");
            }

            foreach (var eventHandle in type.GetEvents())
            {
                var eventDefinition = reader.GetEventDefinition(eventHandle);
                var accessors = eventDefinition.GetAccessors();
                if (!IsPublic(reader, accessors.Adder) && !IsPublic(reader, accessors.Remover) && !IsPublic(reader, accessors.Raiser))
                {
                    continue;
                }
                members.Add($"E|{typeName}|{reader.GetString(eventDefinition.Name)}|{provider.GetTypeName(reader, eventDefinition.Type)}");
            }
        }
        types.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        members.Sort(StringComparer.Ordinal);
        return new PublicSurface(types, members);
    }

    internal static string GetFullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var name = reader.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        return declaring.IsNil ? JoinNamespace(reader.GetString(type.Namespace), name) : GetFullName(reader, declaring) + "+" + name;
    }

    internal static string GetFullName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var type = reader.GetTypeReference(handle);
        var name = reader.GetString(type.Name);
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? GetFullName(reader, (TypeReferenceHandle)type.ResolutionScope) + "+" + name
            : JoinNamespace(reader.GetString(type.Namespace), name);
    }

    private static bool IsExternallyVisible(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var visibility = type.Attributes & TypeAttributes.VisibilityMask;
        var declaring = type.GetDeclaringType();
        return declaring.IsNil
            ? visibility == TypeAttributes.Public
            : visibility == TypeAttributes.NestedPublic && IsExternallyVisible(reader, declaring);
    }

    private static string GetKind(MetadataReader reader, TypeDefinition type)
    {
        if ((type.Attributes & TypeAttributes.Interface) != 0)
        {
            return "interface";
        }
        var provider = new TypeNameProvider();
        return provider.GetTypeName(reader, type.BaseType) switch
        {
            "System.Enum" => "enum",
            "System.ValueType" => "struct",
            "System.MulticastDelegate" => "delegate",
            _ => "class"
        };
    }

    private static bool IsPublic(MetadataReader reader, MethodDefinitionHandle handle) => !handle.IsNil
        && (reader.GetMethodDefinition(handle).Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;

    private static string FormatParameterMetadata(MetadataReader reader, Parameter parameter)
    {
        var parts = new List<string>();
        if ((parameter.Attributes & ParameterAttributes.Optional) != 0)
        {
            parts.Add("optional");
        }
        if ((parameter.Attributes & ParameterAttributes.HasDefault) != 0)
        {
            parts.Add("default=" + ReadConstant(reader, parameter.GetDefaultValue()));
        }
        return string.Join("+", parts);
    }

    private static string ReadConstant(MetadataReader reader, ConstantHandle handle)
    {
        if (handle.IsNil)
        {
            return "<missing>";
        }
        var constant = reader.GetConstant(handle);
        if (constant.Value.IsNil)
        {
            return "null";
        }
        var blob = reader.GetBlobReader(constant.Value);
        object? value = constant.TypeCode switch
        {
            ConstantTypeCode.Boolean => blob.ReadBoolean(),
            ConstantTypeCode.Char => (char)blob.ReadUInt16(),
            ConstantTypeCode.SByte => blob.ReadSByte(),
            ConstantTypeCode.Byte => blob.ReadByte(),
            ConstantTypeCode.Int16 => blob.ReadInt16(),
            ConstantTypeCode.UInt16 => blob.ReadUInt16(),
            ConstantTypeCode.Int32 => blob.ReadInt32(),
            ConstantTypeCode.UInt32 => blob.ReadUInt32(),
            ConstantTypeCode.Int64 => blob.ReadInt64(),
            ConstantTypeCode.UInt64 => blob.ReadUInt64(),
            ConstantTypeCode.Single => blob.ReadSingle(),
            ConstantTypeCode.Double => blob.ReadDouble(),
            ConstantTypeCode.String => blob.ReadUTF16(blob.Length),
            ConstantTypeCode.NullReference => null,
            _ => "<unsupported>"
        };
        return value is string or char ? JsonSerializer.Serialize(value) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
    }

    private static string JoinNamespace(string ns, string name) => string.IsNullOrEmpty(ns) ? name : ns + "." + name;
}

internal sealed class TypeNameProvider : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr(" + string.Join(",", signature.ParameterTypes) + ")->" + signature.ReturnType;
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifierType, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => PublicSurfaceReader.GetFullName(reader, handle);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => PublicSurfaceReader.GetFullName(reader, handle);
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    public string GetTypeName(MetadataReader reader, EntityHandle handle)
    {
        try
        {
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
                HandleKind.TypeReference => GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
                HandleKind.TypeSpecification => GetTypeFromSpecification(reader, null, (TypeSpecificationHandle)handle, 0),
                _ => "<none>"
            };
        }
        catch (BadImageFormatException)
        {
            return $"<invalid-token:0x{MetadataTokens.GetToken(handle):X8}>";
        }
    }
}

internal sealed class ManagedInventory
{
    public int SchemaVersion { get; set; }
    public string AppId { get; set; } = "";
    public string SteamBuildId { get; set; } = "";
    public string RuntimeVersion { get; set; } = "";
    public string UnityVersion { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string InventoryPath { get; set; } = "";
    public DateTimeOffset CapturedAtUtc { get; set; }
    public int? ExpectedAssemblyCount { get; set; }
    public string[] RequiredAssemblies { get; set; } = Array.Empty<string>();
    public int AssemblyCount { get; set; }
    public bool Complete { get; set; }
    public string[] MissingAssemblies { get; set; } = Array.Empty<string>();
    public string[] MetadataFailures { get; set; } = Array.Empty<string>();
    public List<AssemblyInventoryItem> Assemblies { get; set; } = new();
}

internal sealed class AssemblyInventoryItem
{
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string Mvid { get; set; } = "";
    public string AssemblyName { get; set; } = "";
    public string AssemblyVersion { get; set; } = "";
    public int TypeCount { get; set; }
    public int PublicTypeCount { get; set; }
    public int MethodCount { get; set; }
    public int PublicMemberCount { get; set; }
    public string? MetadataError { get; set; }
    [JsonIgnore] public List<PublicTypeShape> PublicTypes { get; set; } = new();
    [JsonIgnore] public List<string> PublicMembers { get; set; } = new();
}

internal sealed class PublicTypeShape
{
    public string Name { get; set; } = "";
    public string Shape { get; set; } = "";
}

internal sealed record PublicSurface(List<PublicTypeShape> Types, List<string> Members);

internal sealed class ManagedComparisonReport
{
    public int SchemaVersion { get; set; }
    public string BaselinePath { get; set; } = "";
    public string CurrentPath { get; set; } = "";
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public int BreakingCount { get; set; }
    public int AdditiveCount { get; set; }
    public int BehaviorReviewCount { get; set; }
    public List<AssemblyComparison> Assemblies { get; set; } = new();
}

internal sealed class AssemblyComparison
{
    public string FileName { get; set; } = "";
    public string Status { get; set; } = "";
    public string BaselineSha256 { get; set; } = "";
    public string CurrentSha256 { get; set; } = "";
    public long BaselineSize { get; set; }
    public long CurrentSize { get; set; }
    public int BaselineTypeCount { get; set; }
    public int CurrentTypeCount { get; set; }
    public int BreakingCount { get; set; }
    public int AdditiveCount { get; set; }
    public bool BehaviorReview { get; set; }
    public string[] RemovedTypes { get; set; } = Array.Empty<string>();
    public string[] AddedTypes { get; set; } = Array.Empty<string>();
    public ChangedTypeShape[] ChangedTypeShapes { get; set; } = Array.Empty<ChangedTypeShape>();
    public string[] RemovedMembers { get; set; } = Array.Empty<string>();
    public string[] AddedMembers { get; set; } = Array.Empty<string>();

    public static AssemblyComparison Added(AssemblyInventoryItem item) => new()
    {
        FileName = item.FileName, Status = "Added", CurrentSha256 = item.Sha256, CurrentSize = item.Size,
        CurrentTypeCount = item.TypeCount, AdditiveCount = item.PublicTypeCount + item.PublicMemberCount, BehaviorReview = true
    };

    public static AssemblyComparison Removed(AssemblyInventoryItem item) => new()
    {
        FileName = item.FileName, Status = "Removed", BaselineSha256 = item.Sha256, BaselineSize = item.Size,
        BaselineTypeCount = item.TypeCount, BreakingCount = item.PublicTypeCount + item.PublicMemberCount, BehaviorReview = true
    };
}

internal sealed class ChangedTypeShape
{
    public string Name { get; set; } = "";
    public string Baseline { get; set; } = "";
    public string Current { get; set; } = "";
}
