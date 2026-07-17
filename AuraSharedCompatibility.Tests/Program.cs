using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

if (args.Length < 2)
{
    throw new ArgumentException("Usage: AuraSharedCompatibility.Tests <assembly> <baseline> [--capture]");
}

var assemblyPath = Path.GetFullPath(args[0]);
var baselinePath = Path.GetFullPath(args[1]);
var capture = args.Skip(2).Any(value => string.Equals(value, "--capture", StringComparison.OrdinalIgnoreCase));
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

if (!File.Exists(assemblyPath))
{
    throw new FileNotFoundException("Aura.Shared assembly is missing.", assemblyPath);
}

if (!File.Exists(baselinePath))
{
    throw new FileNotFoundException("Compatibility baseline is missing.", baselinePath);
}

var baseline = JsonSerializer.Deserialize<CompatibilityBaseline>(File.ReadAllText(baselinePath), jsonOptions)
    ?? throw new InvalidOperationException("Compatibility baseline could not be parsed.");
if (baseline.SchemaVersion != 1)
{
    throw new InvalidOperationException("Unsupported compatibility baseline schema: " + baseline.SchemaVersion);
}

using var stream = File.OpenRead(assemblyPath);
using var peReader = new PEReader(stream);
if (!peReader.HasMetadata)
{
    throw new InvalidOperationException("Aura.Shared output does not contain CLR metadata.");
}

var reader = peReader.GetMetadataReader();
var assemblyName = reader.GetString(reader.GetAssemblyDefinition().Name);
if (!string.Equals(assemblyName, baseline.AssemblyName, StringComparison.Ordinal))
{
    throw new InvalidOperationException($"Assembly name changed: expected={baseline.AssemblyName}, actual={assemblyName}");
}

foreach (var module in baseline.Modules)
{
    module.PublicSurface = PublicSurfaceReader.Read(reader, module.Namespace);
}

if (capture)
{
    File.WriteAllText(baselinePath, JsonSerializer.Serialize(baseline, jsonOptions) + Environment.NewLine);
    Console.WriteLine($"Captured Aura.Shared compatibility baseline: {baseline.Modules.Sum(module => module.PublicSurface.Count)} entries");
    return;
}

var expected = JsonSerializer.Deserialize<CompatibilityBaseline>(File.ReadAllText(baselinePath), jsonOptions)
    ?? throw new InvalidOperationException("Compatibility baseline could not be parsed for comparison.");
var failures = new List<string>();
foreach (var actualModule in baseline.Modules)
{
    var expectedModule = expected.Modules.SingleOrDefault(module => string.Equals(module.Name, actualModule.Name, StringComparison.Ordinal));
    if (expectedModule == null)
    {
        failures.Add("Missing baseline module: " + actualModule.Name);
        continue;
    }

    var missing = expectedModule.PublicSurface.Except(actualModule.PublicSurface, StringComparer.Ordinal).ToArray();
    var added = actualModule.PublicSurface.Except(expectedModule.PublicSurface, StringComparer.Ordinal).ToArray();
    if (missing.Length == 0 && added.Length == 0)
    {
        continue;
    }

    failures.Add($"{actualModule.Name}: missing={missing.Length}, added={added.Length}");
    failures.AddRange(missing.Take(20).Select(value => "  - " + value));
    failures.AddRange(added.Take(20).Select(value => "  + " + value));
}

if (failures.Count > 0)
{
    throw new InvalidOperationException("Aura.Shared public compatibility baseline changed:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
}

Console.WriteLine($"Aura.Shared compatibility baseline passed: {baseline.Modules.Sum(module => module.PublicSurface.Count)} public API entries");

internal sealed class CompatibilityBaseline
{
    public int SchemaVersion { get; set; }

    public string AssemblyName { get; set; } = "";

    public List<CompatibilityModule> Modules { get; set; } = new();

    public List<SourceContract> SourceContracts { get; set; } = new();
}

internal sealed class CompatibilityModule
{
    public string Name { get; set; } = "";

    public string Namespace { get; set; } = "";

    public List<string> PublicSurface { get; set; } = new();
}

internal sealed class SourceContract
{
    public string Name { get; set; } = "";

    public string Directory { get; set; } = "";

    public List<string> RequiredSnippets { get; set; } = new();
}

internal static class PublicSurfaceReader
{
    public static List<string> Read(MetadataReader reader, string targetNamespace)
    {
        var provider = new TypeNameProvider();
        var entries = new List<string>();
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (!IsExternallyVisible(reader, handle) || !string.Equals(GetNamespace(reader, handle), targetNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            var typeName = GetFullName(reader, handle);
            entries.Add($"T|{GetKind(reader, type)}|{typeName}");

            foreach (var fieldHandle in type.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Public)
                {
                    continue;
                }

                var fieldType = field.DecodeSignature(provider, null);
                var flags = (field.Attributes & FieldAttributes.Literal) != 0
                    ? "const=" + ReadConstant(reader, field.GetDefaultValue())
                    : (field.Attributes & FieldAttributes.Static) != 0 ? "static" : "instance";
                entries.Add($"F|{typeName}|{reader.GetString(field.Name)}|{fieldType}|{flags}");
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
                var genericCount = method.GetGenericParameters().Count;
                entries.Add($"M|{typeName}|{reader.GetString(method.Name)}`{genericCount}|{scope}|({string.Join(",", signature.ParameterTypes)})->{signature.ReturnType}");
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
                entries.Add($"P|{typeName}|{reader.GetString(property.Name)}|({string.Join(",", signature.ParameterTypes)})->{signature.ReturnType}|{access}");
            }

            foreach (var eventHandle in type.GetEvents())
            {
                var eventDefinition = reader.GetEventDefinition(eventHandle);
                var accessors = eventDefinition.GetAccessors();
                if (!IsPublic(reader, accessors.Adder) && !IsPublic(reader, accessors.Remover) && !IsPublic(reader, accessors.Raiser))
                {
                    continue;
                }

                entries.Add($"E|{typeName}|{reader.GetString(eventDefinition.Name)}|{provider.GetTypeName(reader, eventDefinition.Type)}");
            }
        }

        entries.Sort(StringComparer.Ordinal);
        return entries;
    }

    internal static string GetFullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var name = reader.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        return declaring.IsNil
            ? JoinNamespace(reader.GetString(type.Namespace), name)
            : GetFullName(reader, declaring) + "+" + name;
    }

    internal static string GetFullName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var type = reader.GetTypeReference(handle);
        var name = reader.GetString(type.Name);
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? GetFullName(reader, (TypeReferenceHandle)type.ResolutionScope) + "+" + name
            : JoinNamespace(reader.GetString(type.Namespace), name);
    }

    private static string GetNamespace(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var declaring = type.GetDeclaringType();
        return declaring.IsNil ? reader.GetString(type.Namespace) : GetNamespace(reader, declaring);
    }

    private static bool IsExternallyVisible(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var visibility = type.Attributes & TypeAttributes.VisibilityMask;
        var declaring = type.GetDeclaringType();
        if (declaring.IsNil)
        {
            return visibility == TypeAttributes.Public;
        }

        return visibility == TypeAttributes.NestedPublic && IsExternallyVisible(reader, declaring);
    }

    private static string GetKind(MetadataReader reader, TypeDefinition type)
    {
        if ((type.Attributes & TypeAttributes.Interface) != 0)
        {
            return "interface";
        }

        var baseType = type.BaseType.Kind switch
        {
            HandleKind.TypeDefinition => GetFullName(reader, (TypeDefinitionHandle)type.BaseType),
            HandleKind.TypeReference => GetFullName(reader, (TypeReferenceHandle)type.BaseType),
            _ => ""
        };
        return baseType switch
        {
            "System.Enum" => "enum",
            "System.ValueType" => "struct",
            "System.MulticastDelegate" => "delegate",
            _ => "class"
        };
    }

    private static bool IsPublic(MetadataReader reader, MethodDefinitionHandle handle)
    {
        return !handle.IsNil
               && (reader.GetMethodDefinition(handle).Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
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
        return value is string or char
            ? JsonSerializer.Serialize(value)
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
    }

    private static string JoinNamespace(string @namespace, string name)
    {
        return string.IsNullOrEmpty(@namespace) ? name : @namespace + "." + name;
    }
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

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetTypeName(MetadataReader reader, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
            HandleKind.TypeReference => GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
            HandleKind.TypeSpecification => GetTypeFromSpecification(reader, null, (TypeSpecificationHandle)handle, 0),
            _ => "<unknown>"
        };
    }
}
