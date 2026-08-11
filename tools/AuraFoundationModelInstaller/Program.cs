using System.Security.Cryptography;
using System.Text;
using AuraCombatAi.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

var packagePath = Argument(args, "--package");
var sharedRoot = Argument(args, "--aura-shared-root");
var displayName = Argument(args, "--display-name");
var activate = Flag(args, "--activate");
var acknowledgeExperimental = Flag(args, "--acknowledge-experimental");
if (string.IsNullOrWhiteSpace(packagePath)
    || string.IsNullOrWhiteSpace(sharedRoot)
    || string.IsNullOrWhiteSpace(displayName))
{
    Console.Error.WriteLine(
        "Usage: AuraFoundationModelInstaller "
        + "--package <foundation-model-package-v5.json> "
        + "--aura-shared-root <ModsData/AuraShared> "
        + "--display-name <name> [--activate] "
        + "[--acknowledge-experimental]");
    return 2;
}
if (displayName.Trim().Length > 40)
{
    Console.Error.WriteLine("Display name must not exceed 40 characters.");
    return 2;
}

packagePath = Path.GetFullPath(packagePath);
sharedRoot = Path.GetFullPath(sharedRoot);
if (!File.Exists(packagePath) || !Directory.Exists(sharedRoot))
{
    Console.Error.WriteLine("Package or AuraShared root does not exist.");
    return 2;
}
var packageDirectory = Path.GetDirectoryName(packagePath) ?? "";
var initialPackageBytes = new FileInfo(packagePath).Length;
if (!CombatFoundationModelPackageProtocol.TryValidateSerializedSize(
        initialPackageBytes,
        out var packageSizeDiagnostic))
{
    Console.Error.WriteLine("Package size validation failed: " + packageSizeDiagnostic);
    return 3;
}

var utf8 = new UTF8Encoding(false, true);
var packageJson = File.ReadAllText(packagePath, utf8);
var package = JsonConvert.DeserializeObject<CombatFoundationModelPackage>(
    packageJson);
if (!CombatFoundationModelPackageProtocol.TryValidate(
        package,
        out var diagnostic))
{
    Console.Error.WriteLine("Package validation failed: " + diagnostic);
    return 3;
}
var deploymentTier = CombatFoundationModelPackageProtocol
    .ResolveDeploymentTier(package);
if (activate
    && string.Equals(
        deploymentTier,
        CombatFoundationDeploymentTier.Experimental,
        StringComparison.Ordinal)
    && !acknowledgeExperimental)
{
    Console.Error.WriteLine(
        "Experimental foundation models require "
        + "--acknowledge-experimental when used with --activate.");
    return 2;
}
if (string.Equals(
        deploymentTier,
        CombatFoundationDeploymentTier.Experimental,
        StringComparison.Ordinal)
    && string.Equals(
        package!.CapabilityStatus,
        CombatFoundationModelPackageProtocol.CapabilityStatusFail,
        StringComparison.Ordinal))
{
    Console.Error.WriteLine(
        "WARNING: capability probe detected a baseline regression; "
        + "this high-risk experimental model is intended only for "
        + "live configuration testing and issue collection.");
}
if (package!.ModelArtifact != null
    && !CombatPolicyValueArtifactProtocol.TryValidatePayload(
        packageDirectory,
        package.ModelArtifact,
        out diagnostic))
{
    Console.Error.WriteLine("Package payload validation failed: " + diagnostic);
    return 3;
}
if (package.ModelArtifact != null
    && !CombatFoundationModelPackageProtocol.TryValidateSerializedSize(
        checked(initialPackageBytes
                + package.ModelArtifact.WeightsByteLength),
        out packageSizeDiagnostic))
{
    Console.Error.WriteLine("Package size validation failed: " + packageSizeDiagnostic);
    return 3;
}

var packageNode = JObject.Parse(packageJson);
var modelId = package.Model?.ModelId
              ?? package.ModelArtifact?.ModelId
              ?? "";
var packageSha256 = Convert.ToHexString(
        SHA256.HashData(utf8.GetBytes(packageJson)))
    .ToLowerInvariant();
var modelLibraryDirectory = InsideRoot(
    sharedRoot,
    "Data",
    "Owners",
    "AuraToolsExp",
    "FoundationModels");
var legacyModelLibraryDirectory = InsideRoot(
    sharedRoot,
    "Logs",
    "AuraToolsExp",
    "model-library");
MigrateLegacyModelLibrary(
    legacyModelLibraryDirectory,
    modelLibraryDirectory);
var manifestPath = InsideRoot(modelLibraryDirectory, "models.json");
var settingsPath = InsideRoot(
    sharedRoot,
    "Config",
    "Owners",
    "AuraToolsExp",
    "AuraTools",
    "MatchExperienceSettings.json");
if (!File.Exists(manifestPath) || !File.Exists(settingsPath))
{
    Console.Error.WriteLine(
        "Existing model library or MatchExperience settings are missing.");
    return 4;
}

var bundleFile = "model-"
                 + Sha256(modelId)[..16].ToLowerInvariant()
                 + ".json";
var bundlePath = InsideRoot(modelLibraryDirectory, bundleFile);
var weightsFile = "weights-"
                  + Sha256(modelId)[..16].ToLowerInvariant()
                  + ".bin";
var weightsPath = InsideRoot(modelLibraryDirectory, weightsFile);
CombatPolicyValueArtifactManifest installedArtifact;
if (package.ModelArtifact != null)
{
    CopyFileAtomic(
        Path.Combine(packageDirectory, package.ModelArtifact.WeightsFile),
        weightsPath);
    package.ModelArtifact.WeightsFile = weightsFile;
    installedArtifact = package.ModelArtifact;
}
else
{
    installedArtifact = CombatPolicyValueArtifactProtocol.Write(
        weightsPath,
        package.Model
        ?? throw new InvalidDataException("Foundation package has no model."));
}
if (!CombatPolicyValueArtifactProtocol.TryValidatePayload(
        modelLibraryDirectory,
        installedArtifact,
        out diagnostic))
{
    Console.Error.WriteLine("Installed FP32 payload validation failed: " + diagnostic);
    return 3;
}
if (!CombatPolicyValueArtifactProtocol.TryLoad(
        modelLibraryDirectory,
        installedArtifact,
        out var installedRuntime,
        out diagnostic))
{
    Console.Error.WriteLine("Installed FP32 payload reload failed: " + diagnostic);
    return 3;
}
_ = new ManagedCombatPolicyValueModel(installedRuntime);
var settings = JObject.Parse(File.ReadAllText(settingsPath, utf8));
var autoBattle = settings["data"]?["autoBattle"] as JObject
                 ?? throw new InvalidDataException(
                     "MatchExperience settings have no autoBattle object.");
var oldSelectedModelId = (string?)autoBattle["selectedModelId"] ?? "";

var normalizedAcceptance = CombatFoundationModelPackageProtocol
    .NormalizeAcceptance(package);
var bundle = new JObject
{
    ["SchemaVersion"] = 6,
    ["BundleId"] = Clone(packageNode["PackageId"]),
    ["Profile"] = Clone(packageNode["Profile"]),
    ["RoleId"] = Clone(packageNode["RoleId"]),
    ["CardPoolScope"] = Clone(packageNode["CardPoolScope"]),
    ["PartnerId"] = Clone(packageNode["PartnerId"]),
    ["EnabledRewardCardPackIds"] =
        Clone(packageNode["EnabledRewardCardPackIds"]),
    ["PreferredDeckSizeMinimum"] =
        Clone(packageNode["PreferredDeckSizeMinimum"]),
    ["PreferredDeckSizeMaximum"] =
        Clone(packageNode["PreferredDeckSizeMaximum"]),
    ["TrainingSubject"] = Clone(packageNode["TrainingSubject"]),
    ["DeclaredCoverage"] = Clone(packageNode["DeclaredCoverage"]),
    ["FoundationArtifactValidated"] = true,
    ["FoundationPackageId"] = Clone(packageNode["PackageId"]),
    ["FoundationWorkerSha256"] = Clone(packageNode["WorkerSha256"]),
    ["FoundationRulesetHash"] = Clone(packageNode["RulesetHash"]),
    ["FoundationModelVersion"] = Clone(packageNode["ModelVersion"]),
    ["FoundationAcceptanceKind"] = normalizedAcceptance.Classification,
    ["FoundationDeploymentTier"] = deploymentTier,
    ["FoundationQualityCertification"] = package.QualityCertification,
    ["FoundationSameModelEvidenceBound"] =
        package.SameModelEvidenceBound,
    ["FoundationCapabilityStatus"] = package.CapabilityStatus,
    ["FoundationPromotionProtocolVersion"] =
        normalizedAcceptance.PromotionProtocolVersion,
    ["FoundationPairedRegressionUpperBound"] =
        normalizedAcceptance.PairedRegressionWilsonUpperBound,
    ["FoundationAcceptance"] = JObject.FromObject(normalizedAcceptance),
    ["FoundationDistributionOrigin"] = "external-installer",
    ["FoundationSourcePackageSha256"] = packageSha256,
    ["FoundationSourcePackageFile"] = Path.GetFileName(packagePath),
    ["ModelPurpose"] = "foundation",
    ["ProjectionNormalWinRate"] =
        Clone(packageNode["Validation"]?["NormalWinRate"]),
    ["ProjectionAdvancedWinRate"] =
        Clone(packageNode["Validation"]?["AdvancedWinRate"]),
    ["TrainingReportDirectory"] =
        Path.GetDirectoryName(packagePath) ?? "",
    ["GeneratedUtc"] = Clone(packageNode["CreatedUtc"]),
    ["TrainingSnapshotId"] = "",
    ["TrainingSnapshotHash"] = "",
    ["Residual"] = null,
    ["SearchGuidance"] = null,
    ["PolicyValue"] = null,
    ["PolicyValueArtifact"] = JObject.FromObject(installedArtifact)
};

var library = JObject.Parse(File.ReadAllText(manifestPath, utf8));
library["SchemaVersion"] = Math.Max(6, (int?)library["SchemaVersion"] ?? 0);
var models = library["Models"] as JArray
             ?? throw new InvalidDataException(
                 "Model library manifest has no Models array.");
foreach (var entry in models
             .Children<JObject>()
             .Where(entry =>
                 string.Equals(
                     (string?)entry["ModelId"],
                     oldSelectedModelId,
                     StringComparison.Ordinal)
                 || string.Equals(
                     (string?)entry["ModelId"],
                     modelId,
                     StringComparison.Ordinal))
             .ToArray())
{
    entry.Remove();
}
models.Add(new JObject
{
    ["ModelId"] = modelId,
    ["DisplayName"] = displayName.Trim(),
    ["Profile"] = package.Profile,
    ["RoleId"] = package.RoleId,
    ["CardPoolScope"] = package.CardPoolScope,
    ["PartnerId"] = package.PartnerId,
    ["EnabledRewardCardPackIds"] =
        Clone(packageNode["EnabledRewardCardPackIds"]),
    ["PreferredDeckSizeMinimum"] = package.PreferredDeckSizeMinimum,
    ["PreferredDeckSizeMaximum"] = package.PreferredDeckSizeMaximum,
    ["CoverageLevel"] = "full",
    ["CoverageSummary"] = "完全覆盖",
    ["ModelPurpose"] = "foundation",
    ["ProjectionNormalWinRate"] = package.Validation.NormalWinRate,
    ["ProjectionAdvancedWinRate"] = package.Validation.AdvancedWinRate,
    ["BundleFile"] = bundleFile,
    ["ModelVersion"] = Clone(packageNode["ModelVersion"]),
    ["AcceptanceKind"] = normalizedAcceptance.Classification,
    ["DeploymentTier"] = deploymentTier,
    ["QualityCertification"] = package.QualityCertification,
    ["CapabilityStatus"] = package.CapabilityStatus,
    ["DistributionOrigin"] = "external-installer",
    ["SourcePackageSha256"] = packageSha256,
    ["SourcePackageFile"] = Path.GetFileName(packagePath),
    ["CreatedUtc"] = Clone(packageNode["CreatedUtc"])
});

if (activate)
{
    autoBattle["profile"] = package.Profile;
    autoBattle["selectedModelId"] = modelId;
    autoBattle["trainedModelMode"] = "full";
    if (string.Equals(
            deploymentTier,
            CombatFoundationDeploymentTier.Experimental,
            StringComparison.Ordinal))
    {
        autoBattle["experimentalModelAcknowledgement"] =
            "sha256:" + packageSha256;
    }
    if (settings["data"] is JObject settingsData)
    {
        settingsData["schemaVersion"] = Math.Max(
            28,
            (int?)settingsData["schemaVersion"] ?? 0);
    }
    settings["revision"] = ((int?)settings["revision"] ?? 0) + 1;
    settings["updatedBy"] = "AuraToolsExp";
    settings["updatedUtc"] = DateTime.UtcNow.ToString("o");
}

var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
var bundleBackup = WriteAtomic(
    bundlePath,
    bundle.ToString(Formatting.Indented),
    utf8,
    timestamp);
var manifestBackup = WriteAtomic(
    manifestPath,
    library.ToString(Formatting.Indented),
    utf8,
    timestamp);
var settingsBackup = activate
    ? WriteAtomic(
        settingsPath,
        settings.ToString(Formatting.Indented),
        utf8,
        timestamp)
    : "";

Console.WriteLine(JsonConvert.SerializeObject(new
{
    Success = true,
    ModelId = modelId,
    DisplayName = displayName.Trim(),
    Profile = package.Profile,
    DeploymentTier = deploymentTier,
    CapabilityStatus = package.CapabilityStatus,
    BundlePath = bundlePath,
    BundleBackup = bundleBackup,
    ManifestPath = manifestPath,
    ManifestBackup = manifestBackup,
    SettingsPath = activate ? settingsPath : "",
    SettingsBackup = settingsBackup,
    Activated = activate
}, Formatting.Indented));
return 0;

static JToken? Clone(JToken? value)
{
    return value?.DeepClone();
}

static string InsideRoot(string root, params string[] segments)
{
    var fullRoot = Path.GetFullPath(root)
        .TrimEnd(Path.DirectorySeparatorChar)
        + Path.DirectorySeparatorChar;
    var result = Path.GetFullPath(
        segments.Aggregate(root, Path.Combine));
    if (!result.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "Resolved path escaped the intended root: " + result);
    }
    return result;
}

static string Sha256(string value)
{
    return Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value ?? "")));
}

static string WriteAtomic(
    string path,
    string value,
    Encoding encoding,
    string timestamp)
{
    Directory.CreateDirectory(
        Path.GetDirectoryName(path)
        ?? throw new InvalidOperationException("Path has no directory."));
    var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
    File.WriteAllText(temporary, value, encoding);
    if (!File.Exists(path))
    {
        File.Move(temporary, path);
        return "";
    }
    var backup = path + ".bak-" + timestamp;
    File.Replace(temporary, path, backup);
    return backup;
}

static void CopyFileAtomic(string source, string destination)
{
    Directory.CreateDirectory(
        Path.GetDirectoryName(destination)
        ?? throw new InvalidOperationException("Path has no directory."));
    var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
    File.Copy(source, temporary, overwrite: false);
    if (File.Exists(destination))
    {
        File.Replace(temporary, destination, null);
    }
    else
    {
        File.Move(temporary, destination);
    }
}

static void MigrateLegacyModelLibrary(string source, string destination)
{
    var destinationManifest = Path.Combine(destination, "models.json");
    var sourceManifest = Path.Combine(source, "models.json");
    if (File.Exists(destinationManifest) || !File.Exists(sourceManifest))
    {
        return;
    }

    Directory.CreateDirectory(destination);
    foreach (var sourcePath in Directory.EnumerateFiles(source)
                 .OrderBy(path => string.Equals(
                     Path.GetFileName(path),
                     "models.json",
                     StringComparison.OrdinalIgnoreCase)
                     ? 1
                     : 0))
    {
        var destinationPath = Path.Combine(
            destination,
            Path.GetFileName(sourcePath));
        if (File.Exists(destinationPath))
        {
            continue;
        }

        var temporary = destinationPath
                        + ".migration-"
                        + Guid.NewGuid().ToString("N");
        File.Copy(sourcePath, temporary, overwrite: false);
        File.Move(temporary, destinationPath);
    }
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

static bool Flag(string[] values, string name)
{
    return values.Any(value => string.Equals(
        value,
        name,
        StringComparison.OrdinalIgnoreCase));
}
