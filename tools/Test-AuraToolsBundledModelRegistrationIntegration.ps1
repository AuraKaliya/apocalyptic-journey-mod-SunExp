param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [switch]$RestartVerification,
    [string]$RestartRoot = "",
    [string]$ExpectedModelIds = "",
    [string]$ExpectedConfigSha256 = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($PSVersionTable.PSEdition -eq "Desktop") {
    $pwshCommand = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($null -eq $pwshCommand) {
        throw "PowerShell 7 is required to load the Unity net472 production assemblies without desktop CLR strong-name rejection."
    }
    $forwardedArguments = @(
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        $PSCommandPath,
        "-Configuration",
        $Configuration)
    if ($SkipBuild) {
        $forwardedArguments += "-SkipBuild"
    }
    if ($RestartVerification) {
        $forwardedArguments += "-RestartVerification"
        $forwardedArguments += @("-RestartRoot", $RestartRoot)
        $forwardedArguments += @("-ExpectedModelIds", $ExpectedModelIds)
        $forwardedArguments += @(
            "-ExpectedConfigSha256",
            $ExpectedConfigSha256)
    }
    & $pwshCommand.Source @forwardedArguments
    exit $LASTEXITCODE
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$bindingFlags = [Reflection.BindingFlags]::Public `
    -bor [Reflection.BindingFlags]::NonPublic `
    -bor [Reflection.BindingFlags]::Static `
    -bor [Reflection.BindingFlags]::Instance
$script:assemblySearchDirectories = @(
    (Join-Path $repoRoot "AuraToolsExp-Dev\bin\$Configuration\net472"),
    (Join-Path $repoRoot "AuraSharedRuntime-Dev\bin\$Configuration\net472"),
    (Join-Path $repoRoot "Managed")
)
$script:assemblyResolveHandler = $null

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-Sha256 {
    param([string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-ObjectProperty {
    param(
        [object]$Value,
        [string]$Name
    )

    Assert-True ($null -ne $Value) "Cannot read property '$Name' from a null object."
    $property = $Value.GetType().GetProperty($Name, $bindingFlags)
    Assert-True ($null -ne $property) (
        "Production type '{0}' no longer exposes property '{1}'." -f `
            $Value.GetType().FullName,
            $Name)
    return $property.GetValue($Value, $null)
}

function Set-ObjectProperty {
    param(
        [object]$Value,
        [string]$Name,
        [object]$PropertyValue
    )

    $property = $Value.GetType().GetProperty($Name, $bindingFlags)
    Assert-True ($null -ne $property) (
        "Production type '{0}' no longer exposes property '{1}'." -f `
            $Value.GetType().FullName,
            $Name)
    $property.SetValue($Value, $PropertyValue, $null)
}

function Add-StringListPropertyValues {
    param(
        [object]$Value,
        [string]$Name,
        [object[]]$Items
    )

    $property = $Value.GetType().GetProperty($Name, $bindingFlags)
    Assert-True ($null -ne $property) (
        "Production type '{0}' no longer exposes property '{1}'." -f `
            $Value.GetType().FullName,
            $Name)
    $list = $property.GetValue($Value, $null)
    Assert-True ($null -ne $list) (
        "Production list property '{0}.{1}' is null." -f `
            $Value.GetType().FullName,
            $Name)
    foreach ($item in @($Items)) {
        [void]$list.Add([string]$item)
    }
}

function Get-StaticMethod {
    param(
        [Type]$Type,
        [string]$Name,
        [int]$ParameterCount
    )

    $methods = @($Type.GetMethods($bindingFlags) | Where-Object {
        $_.Name -eq $Name -and $_.GetParameters().Count -eq $ParameterCount
    })
    Assert-True ($methods.Count -eq 1) (
        "Expected exactly one production method {0}.{1}/{2}, found {3}." -f `
            $Type.FullName,
            $Name,
            $ParameterCount,
            $methods.Count)
    return $methods[0]
}

function Invoke-StaticMethod {
    param(
        [Reflection.MethodInfo]$Method,
        [object[]]$Arguments
    )

    [object[]]$unwrappedArguments = New-Object object[] $Arguments.Count
    for ($index = 0; $index -lt $Arguments.Count; $index++) {
        $argument = $Arguments[$index]
        $unwrappedArguments[$index] = if ($null -eq $argument) {
            $null
        }
        else {
            $argument.PSObject.BaseObject
        }
    }

    try {
        $result = $Method.Invoke($null, $unwrappedArguments)
        for ($index = 0; $index -lt $Arguments.Count; $index++) {
            $Arguments[$index] = $unwrappedArguments[$index]
        }
        return $result
    }
    catch [Reflection.TargetInvocationException] {
        if ($null -ne $_.Exception.InnerException) {
            throw $_.Exception.InnerException
        }
        throw
    }
    catch {
        $argumentTypes = @($Arguments | ForEach-Object {
            if ($null -eq $_) {
                "<null>"
            }
            else {
                $_.GetType().FullName
            }
        }) -join ", "
        throw (
            "Reflection invocation failed for {0}.{1}; arguments={2}; {3}" -f `
                $Method.DeclaringType.FullName,
                $Method.Name,
                $argumentTypes,
                $_.Exception.Message)
    }
}

function Open-ProductionAssemblies {
    foreach ($directory in $script:assemblySearchDirectories) {
        Assert-True (Test-Path -LiteralPath $directory -PathType Container) (
            "Production assembly search directory is missing: " + $directory)
    }

    $script:assemblyResolveHandler = [ResolveEventHandler] {
        param($sender, $eventArgs)

        $assemblyName = New-Object Reflection.AssemblyName($eventArgs.Name)
        $fileName = $assemblyName.Name + ".dll"
        foreach ($directory in $script:assemblySearchDirectories) {
            $candidate = Join-Path $directory $fileName
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return [Reflection.Assembly]::LoadFrom($candidate)
            }
        }
        return $null
    }
    [AppDomain]::CurrentDomain.add_AssemblyResolve(
        $script:assemblyResolveHandler)

    $sharedPath = Join-Path (
        $script:assemblySearchDirectories[1]) "Aura.Shared.dll"
    $entryPath = Join-Path (
        $script:assemblySearchDirectories[0]) "AuraToolsExp.Aura.dll"
    Assert-True (Test-Path -LiteralPath $sharedPath -PathType Leaf) (
        "Production Aura.Shared.dll is missing: " + $sharedPath)
    Assert-True (Test-Path -LiteralPath $entryPath -PathType Leaf) (
        "Production AuraToolsExp.Aura.dll is missing: " + $entryPath)

    return [pscustomobject]@{
        Shared = [Reflection.Assembly]::LoadFrom($sharedPath)
        Entry = [Reflection.Assembly]::LoadFrom($entryPath)
        EntryPath = $entryPath
        SharedPath = $sharedPath
    }
}

function Close-ProductionAssemblies {
    if ($null -ne $script:assemblyResolveHandler) {
        [AppDomain]::CurrentDomain.remove_AssemblyResolve(
            $script:assemblyResolveHandler)
        $script:assemblyResolveHandler = $null
    }
}

function Set-SharedRoot {
    param(
        [Reflection.Assembly]$SharedAssembly,
        [string]$Root
    )

    $fullRoot = [IO.Path]::GetFullPath($Root)
    [IO.Directory]::CreateDirectory($fullRoot) | Out-Null
    $pathsType = $SharedAssembly.GetType(
        "AuraShared.Core.AuraSharedPaths",
        $true)
    $rootField = $pathsType.GetField(
        "rootDirectory",
        [Reflection.BindingFlags]::NonPublic `
            -bor [Reflection.BindingFlags]::Static)
    Assert-True ($null -ne $rootField) (
        "AuraSharedPaths.rootDirectory backing field is unavailable.")
    $rootField.SetValue($null, $fullRoot)
}

function Get-ModelRuntimeType {
    param([Reflection.Assembly]$EntryAssembly)

    return $EntryAssembly.GetType(
        "AuraToolsExp.Dll.Features.AutoBattle.AuraToolsAutoBattleModelRuntime",
        $true)
}

function Read-ProductionLibrary {
    param([Reflection.Assembly]$EntryAssembly)

    $runtimeType = Get-ModelRuntimeType $EntryAssembly
    $readLibrary = Get-StaticMethod $runtimeType "ReadLibraryNoMigration" 0
    return Invoke-StaticMethod $readLibrary @()
}

function Read-ProductionBundle {
    param(
        [Reflection.Assembly]$EntryAssembly,
        [string]$BundleFile
    )

    $runtimeType = Get-ModelRuntimeType $EntryAssembly
    $readBundle = Get-StaticMethod $runtimeType "ReadLibraryBundleFile" 1
    return Invoke-StaticMethod $readBundle @($BundleFile)
}

function Assert-ProductionPayloadLoads {
    param(
        [Reflection.Assembly]$SharedAssembly,
        [string]$LibraryDirectory,
        [object]$Bundle,
        [string]$ModelId
    )

    $artifact = Get-ObjectProperty $Bundle "PolicyValueArtifact"
    Assert-True ($null -ne $artifact) "Registered bundle has no policy-value artifact."
    $protocolType = $SharedAssembly.GetType(
        "AuraCombatAi.Shared.CombatPolicyValueArtifactProtocol",
        $true)
    $tryLoad = Get-StaticMethod $protocolType "TryLoad" 4
    [object[]]$arguments = @($LibraryDirectory, $artifact, $null, "")
    $loaded = [bool](Invoke-StaticMethod $tryLoad $arguments)
    Assert-True $loaded (
        "Production payload loader rejected registered weights: " `
            + [string]$arguments[3])
    $runtime = $arguments[2]
    Assert-True ($null -ne $runtime) (
        "Production payload loader returned no runtime definition.")
    Assert-True (([string](Get-ObjectProperty $runtime "ModelId")) -eq $ModelId) (
        "Restart payload model id does not match the registered model id.")
}

function Assert-RestartState {
    param(
        [Reflection.Assembly]$EntryAssembly,
        [Reflection.Assembly]$SharedAssembly,
        [string]$SharedRoot,
        [string[]]$ModelIds,
        [string]$ConfigSha256
    )

    Set-SharedRoot $SharedAssembly $SharedRoot
    $library = Read-ProductionLibrary $EntryAssembly
    $models = @(Get-ObjectProperty $library "Models")
    Assert-True ($models.Count -eq $ModelIds.Count) (
        "Restart read expected {0} registered models, found {1}." -f `
            $ModelIds.Count,
            $models.Count)
    foreach ($modelId in $ModelIds) {
        $entry = @($models | Where-Object {
            ([string](Get-ObjectProperty $_ "ModelId")) -eq $modelId
        })
        Assert-True ($entry.Count -eq 1) (
            "Restart read could not find expected model '" `
                + $modelId + "'.")

        $bundleFile = [string](Get-ObjectProperty $entry[0] "BundleFile")
        $bundle = Read-ProductionBundle $EntryAssembly $bundleFile
        Assert-True ($null -ne $bundle) (
            "Restart read could not deserialize bundle for model '" `
                + $modelId + "'.")
        $libraryDirectory = Join-Path $SharedRoot (
            "Data\Owners\AuraToolsExp\FoundationModels")
        Assert-ProductionPayloadLoads `
            $SharedAssembly `
            $libraryDirectory `
            $bundle `
            $modelId
    }

    $configPath = Join-Path $SharedRoot (
        "Config\Owners\AuraToolsExp\AuraTools\MatchExperienceSettings.json")
    Assert-True (Test-Path -LiteralPath $configPath -PathType Leaf) (
        "Restart verification config sentinel is missing.")
    Assert-True ((Get-Sha256 $configPath) -eq $ConfigSha256) (
        "selectedModelId/trainedModelMode config changed across registration or restart.")
}

function Copy-FileToDirectory {
    param(
        [string]$Source,
        [string]$Directory,
        [string]$FileName
    )

    try {
        [IO.Directory]::CreateDirectory($Directory) | Out-Null
    }
    catch {
        throw "Cannot create integration fixture directory '$Directory': $($_.Exception.Message)"
    }
    Copy-Item -LiteralPath $Source -Destination (
        Join-Path $Directory $FileName) -Force
}

function Get-LibraryInventory {
    param([string]$Directory)

    return (@(Get-ChildItem -LiteralPath $Directory -File | Sort-Object Name | ForEach-Object {
        $_.Name + ":" + $_.Length + ":" + (Get-Sha256 $_.FullName)
    }) -join "|")
}

function Remove-IntegrationRoot {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }
    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $requiredPrefix = Join-Path $tempRoot "AuraToolsBundledModelIntegration"
    Assert-True ($fullPath.StartsWith(
        $requiredPrefix + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) (
        "Refusing to remove an integration path outside the dedicated temp root: " `
            + $fullPath)
    Remove-Item -LiteralPath $fullPath -Recurse -Force
}

if ($RestartVerification) {
    Assert-True (-not [string]::IsNullOrWhiteSpace($RestartRoot)) (
        "RestartRoot is required for restart verification.")
    Assert-True (-not [string]::IsNullOrWhiteSpace($ExpectedModelIds)) (
        "ExpectedModelIds is required for restart verification.")
    Assert-True ($ExpectedConfigSha256 -match "^[0-9A-Fa-f]{64}$") (
        "ExpectedConfigSha256 is invalid.")

    $assemblies = Open-ProductionAssemblies
    try {
        Assert-RestartState `
            $assemblies.Entry `
            $assemblies.Shared `
            ([IO.Path]::GetFullPath($RestartRoot)) `
            @($ExpectedModelIds.Split(
                @("|"),
                [StringSplitOptions]::RemoveEmptyEntries)) `
            $ExpectedConfigSha256.ToUpperInvariant()
        Write-Host "Fresh-process model library restart verification passed."
    }
    finally {
        Close-ProductionAssemblies
    }
    exit 0
}

if (-not $SkipBuild) {
    $project = Join-Path $repoRoot "AuraToolsExp-Dev\AuraToolsExp.Dll.csproj"
    & dotnet build $project -c $Configuration -v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "AuraToolsExp production build failed with exit code $LASTEXITCODE."
    }
}

$modelRoot = Join-Path $repoRoot "AuraToolsExp\ModResource\Model"
$sourcePackages = @(Get-ChildItem `
    -LiteralPath $modelRoot `
    -Recurse `
    -File `
    -Filter "foundation-model-package-v5.json" `
    | Sort-Object FullName)
Assert-True ($sourcePackages.Count -ge 1) (
    "Integration fixture requires at least one shipped v5 package.")
$sourcePackage = $sourcePackages[0].FullName
$sourceDirectory = Split-Path -Parent $sourcePackage
$sourceWeights = Join-Path $sourceDirectory "foundation-model-weights-v5.bin"
Assert-True (Test-Path -LiteralPath $sourceWeights -PathType Leaf) (
    "Shipped package is missing its canonical weights pair.")
$package = Get-Content -Raw -Encoding UTF8 -LiteralPath $sourcePackage `
    | ConvertFrom-Json
$modelId = [string]$package.modelArtifact.modelId
$roleId = [string]$package.roleId
$profile = [string]$package.profile
$derivedRoleId = if ($roleId -ne "career_2") {
    "career_2"
}
else {
    "career_3"
}
$derivedModelId = $modelId + "-integration-" + $derivedRoleId
$modelIdsUnderTest = @($modelId, $derivedModelId)
$expectedRoles = @{}
$expectedRoles[$modelId] = $roleId
$expectedRoles[$derivedModelId] = $derivedRoleId
$packageSha256 = Get-Sha256 $sourcePackage
Assert-True (-not [string]::IsNullOrWhiteSpace($modelId)) (
    "Shipped package model id is empty.")
Assert-True (-not [string]::IsNullOrWhiteSpace($roleId)) (
    "Shipped package role id is empty.")

$integrationBase = Join-Path (
    [IO.Path]::GetTempPath()) "AuraToolsBundledModelIntegration"
$testRoot = Join-Path $integrationBase ([Guid]::NewGuid().ToString("N"))
$fakeModRoot = Join-Path $testRoot "Game_Data\Mods\AuraToolsExp"
$sharedRoot = Join-Path $testRoot "Game_Data\ModsData\AuraShared"
$configurationPath = Join-Path $sharedRoot (
    "Config\Owners\AuraToolsExp\AuraTools\MatchExperienceSettings.json")
$assemblies = $null

try {
    [IO.Directory]::CreateDirectory($fakeModRoot) | Out-Null
    [IO.Directory]::CreateDirectory($sharedRoot) | Out-Null

    $assemblies = Open-ProductionAssemblies
    $trainingProtocolType = $assemblies.Shared.GetType(
        "AuraCombatAi.Shared.CombatFoundationTrainingProtocol",
        $true)
    $policyProtocolType = $assemblies.Shared.GetType(
        "AuraCombatAi.Shared.CombatPolicyValueProtocol",
        $true)
    $trainingPolicyVersion = [string]$trainingProtocolType.GetField(
        "TrainingPolicyVersion",
        $bindingFlags).GetRawConstantValue()
    $searchPolicyVersion = [string]$trainingProtocolType.GetField(
        "SearchPolicyVersion",
        $bindingFlags).GetRawConstantValue()
    $trainingSemanticsVersion = [string]$policyProtocolType.GetField(
        "TrainingSemanticsVersion",
        $bindingFlags).GetRawConstantValue()
    $featureSchemaVersion = [int]$policyProtocolType.GetField(
        "FeatureSchemaVersion",
        $bindingFlags).GetRawConstantValue()
    $trainingOptionsType = $assemblies.Shared.GetType(
        "AuraCombatAi.Shared.CombatPolicyValueTrainingOptions",
        $true)
    $trainingOptions = [Activator]::CreateInstance($trainingOptionsType)
    $featureEncodingMode = [string](Get-ObjectProperty `
        $trainingOptions `
        "FeatureEncodingMode")

    # This integration gate verifies directory discovery, hash
    # deduplication, registration and restart loading. Keep that mechanics
    # test independent from the separate shipped-artifact compatibility gate
    # by adapting its temporary copy to the current protocol constants. The
    # committed model package itself is never rewritten or re-certified.
    $compatiblePackage = Get-Content `
        -Raw `
        -Encoding UTF8 `
        -LiteralPath $sourcePackage `
        | ConvertFrom-Json
    $compatiblePackage.compatibility.trainingPolicyVersion =
        $trainingPolicyVersion
    $compatiblePackage.compatibility.searchPolicyVersion =
        $searchPolicyVersion
    $compatiblePackage.compatibility.trainingSemanticsVersion =
        $trainingSemanticsVersion
    $compatiblePackage.compatibility.featureSchemaVersion =
        $featureSchemaVersion
    $compatiblePackage.compatibility.featureEncodingMode =
        $featureEncodingMode
    $compatiblePackage.modelArtifact.featureSchemaVersion =
        $featureSchemaVersion
    $compatiblePackage.modelArtifact.featureEncodingMode =
        $featureEncodingMode
    $compatiblePackagePath = Join-Path $testRoot (
        "compatible-foundation-model-package-v5.json")
    [IO.File]::WriteAllText(
        $compatiblePackagePath,
        ($compatiblePackage | ConvertTo-Json -Depth 100 -Compress),
        ([Text.UTF8Encoding]::new($false)))

    $derivedPackage = Get-Content `
        -Raw `
        -Encoding UTF8 `
        -LiteralPath $compatiblePackagePath `
        | ConvertFrom-Json
    $derivedPackage.packageId = (
        [string]$derivedPackage.packageId) + "-integration-" + $derivedRoleId
    $derivedPackage.displayName = (
        "Integration " + $derivedRoleId + " foundation model")
    $derivedPackage.roleId = $derivedRoleId
    $derivedPackage.trainingSubject.roleId = $derivedRoleId
    $derivedPackage.modelArtifact.modelId = $derivedModelId
    $derivedPackagePath = Join-Path $testRoot (
        "derived-foundation-model-package-v5.json")
    [IO.File]::WriteAllText(
        $derivedPackagePath,
        ($derivedPackage | ConvertTo-Json -Depth 100 -Compress),
        ([Text.UTF8Encoding]::new($false)))
    $derivedPackageSha256 = Get-Sha256 $derivedPackagePath

    foreach ($fixture in @(
        [pscustomobject]@{
            Role = "Integration Role A [$roleId]"
            Partner = "Integration Familiar A [$([string]$package.partnerId)]"
            Release = "玩家发布 A"
            Package = $compatiblePackagePath
        },
        [pscustomobject]@{
            Role = "Integration Role A Duplicate [$roleId]"
            Partner = "Integration Familiar A Duplicate [$([string]$package.partnerId)]"
            Release = ""
            Package = $compatiblePackagePath
        },
        [pscustomobject]@{
            Role = "Integration Role B [$derivedRoleId]"
            Partner = "Integration Familiar B [$([string]$derivedPackage.partnerId)]"
            Release = "官方发布 B"
            Package = $derivedPackagePath
        })) {
        $fixtureDirectory = Join-Path `
            (Join-Path `
                (Join-Path $fakeModRoot "ModResource\Model") `
                $fixture.Role) `
            $fixture.Partner
        if (-not [string]::IsNullOrWhiteSpace([string]$fixture.Release)) {
            $fixtureDirectory = Join-Path $fixtureDirectory $fixture.Release
        }
        Copy-FileToDirectory `
            $fixture.Package `
            $fixtureDirectory `
            "foundation-model-package-v5.json"
        Copy-FileToDirectory `
            $sourceWeights `
            $fixtureDirectory `
            "foundation-model-weights-v5.bin"
    }

    Copy-FileToDirectory `
        (Join-Path $repoRoot (
            "AuraToolsExp\Config\combat-simulation\witch-game-subjects-v1.catalog.json")) `
        (Join-Path $fakeModRoot "Config\combat-simulation") `
        "witch-game-subjects-v1.catalog.json"

    $derivedTrust = [pscustomobject]@{
        FoundationLineage = [string]$derivedPackage.foundationLineage
        ModelId = $derivedModelId
        ArtifactSha256 = $derivedPackageSha256
        WeightsSha256 = ""
        FeatureSchemaVersion = [int]$derivedPackage.modelArtifact.featureSchemaVersion
        ContentSetHash = [string]$derivedPackage.contentSetHash
        RulesetHash = [string]$derivedPackage.rulesetHash
        NativeProgramPackageHash = [string]$derivedPackage.compatibility.nativeProgramPackageHash
        RequiredStartGateCapability = "ReadyToStartGate.V1"
    }
    $temporaryTrust = [pscustomobject]@{
        SchemaVersion = 1
        Entries = @($derivedTrust)
    }
    $temporaryTrustPath = Join-Path `
        (Join-Path $fakeModRoot "Config") `
        "aura-director.foundation-model-allowlist.json"
    [IO.Directory]::CreateDirectory((Split-Path -Parent $temporaryTrustPath)) `
        | Out-Null
    [IO.File]::WriteAllText(
        $temporaryTrustPath,
        ($temporaryTrust | ConvertTo-Json -Depth 20 -Compress),
        ([Text.UTF8Encoding]::new($false)))

    [IO.Directory]::CreateDirectory((Split-Path -Parent $configurationPath)) `
        | Out-Null
    $configurationSentinel = @"
{
  "schemaVersion": 26,
  "autoBattle": {
    "selectedModelId": "keep-selected-model",
    "trainedModelMode": "shadow"
  }
}
"@
    [IO.File]::WriteAllText(
        $configurationPath,
        $configurationSentinel,
        ([Text.UTF8Encoding]::new($false)))
    $configSha256 = Get-Sha256 $configurationPath

    Set-SharedRoot $assemblies.Shared $sharedRoot

    $contextType = $assemblies.Shared.GetType(
        "AuraCombatAi.Shared.CombatModelRuntimeContext",
        $true)
    $context = [Activator]::CreateInstance($contextType)
    Set-ObjectProperty $context "RoleId" $roleId
    Set-ObjectProperty $context "PartnerId" ([string]$package.partnerId)
    Add-StringListPropertyValues `
        $context `
        "EnabledRewardCardPackIds" `
        @($package.enabledRewardCardPackIds)
    Set-ObjectProperty `
        $context `
        "PreferredDeckSizeMinimum" `
        ([int]$package.preferredDeckSizeMinimum)
    Set-ObjectProperty `
        $context `
        "PreferredDeckSizeMaximum" `
        ([int]$package.preferredDeckSizeMaximum)

    $bundledRuntimeType = $assemblies.Entry.GetType(
        "AuraToolsExp.Dll.Features.AutoBattle.AuraToolsBundledFoundationModelRuntime",
        $true)
    $import = Get-StaticMethod $bundledRuntimeType "Import" 3

    [object[]]$importArguments = @(
        $fakeModRoot,
        $context,
        [Threading.CancellationToken]::None)
    $first = Invoke-StaticMethod $import $importArguments
    $firstScan = Get-ObjectProperty $first "Scan"
    $firstRegistration = Get-ObjectProperty $first "Registration"
    $firstScanDiagnostics = @(
        Get-ObjectProperty $firstScan "Diagnostics"
    ) -join " | "
    Assert-True (([int](Get-ObjectProperty $firstScan "Scanned")) -eq 3) (
        "Production scanner did not inspect all three canonical pair directories.")
    Assert-True (([int](Get-ObjectProperty $firstScan "Deduplicated")) -eq 1) (
        "Production scanner did not hash-deduplicate the repeated package; " `
            + "scanned=$([int](Get-ObjectProperty $firstScan 'Scanned')), " `
            + "failed=$([int](Get-ObjectProperty $firstScan 'Failed')), " `
            + "diagnostics=$firstScanDiagnostics")
    Assert-True (([int](Get-ObjectProperty $firstScan "Failed")) -eq 0) (
        "Production scanner rejected a valid canonical pair fixture.")
    Assert-True (([int](Get-ObjectProperty $firstScan "OfficialTrusted")) -eq 1) (
        "Production scanner did not classify the exact-allowlisted package as official.")
    Assert-True (([int](Get-ObjectProperty $firstScan "PlayerValidated")) -eq 1) (
        "Production scanner did not classify the unique player-trained package through local gates.")
    Assert-True (([int](Get-ObjectProperty $firstRegistration "Installed")) -eq 2) (
        "First production import did not install both role models.")
    Assert-True (([int](Get-ObjectProperty $firstRegistration "Failed")) -eq 0) (
        "First production import reported a registration failure.")
    Assert-True (([int](Get-ObjectProperty $firstRegistration "Conflicts")) -eq 0) (
        "First production import reported an unexpected registration conflict.")
    Assert-True ([bool](Get-ObjectProperty $firstRegistration "LibraryChanged")) (
        "First production import did not commit the model library index.")

    $libraryDirectory = Join-Path $sharedRoot (
        "Data\Owners\AuraToolsExp\FoundationModels")
    $manifestPath = Join-Path $libraryDirectory "models.json"
    Assert-True (Test-Path -LiteralPath $manifestPath -PathType Leaf) (
        "Registration did not create models.json in temporary ModsData.")
    $manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath `
        | ConvertFrom-Json
    $manifestModels = @($manifest.models)
    Assert-True ($manifestModels.Count -eq 2) (
        "models.json expected two role-model entries after package deduplication.")
    foreach ($expectedModelId in $modelIdsUnderTest) {
        $manifestEntry = @($manifestModels | Where-Object {
            ([string]$_.modelId) -eq $expectedModelId
        })
        Assert-True ($manifestEntry.Count -eq 1) (
            "models.json is missing expected model '" `
                + $expectedModelId + "'.")
        Assert-True (([string]$manifestEntry[0].roleId) -eq (
            [string]$expectedRoles[$expectedModelId])) (
            "models.json role id does not match model '" `
                + $expectedModelId + "'.")
        Assert-True (-not [IO.Path]::IsPathRooted(
            [string]$manifestEntry[0].sourcePackageFile)) (
            "models.json leaked an absolute source package path.")
        $expectedOrigin = if ($expectedModelId -eq $derivedModelId) {
            "bundled"
        }
        else {
            "player-trained"
        }
        Assert-True (([string]$manifestEntry[0].distributionOrigin -eq $expectedOrigin)) (
            "models.json distribution origin does not match model '" `
                + $expectedModelId + "'.")

        $bundlePath = Join-Path `
            $libraryDirectory `
            ([string]$manifestEntry[0].bundleFile)
        Assert-True (Test-Path -LiteralPath $bundlePath -PathType Leaf) (
            "Registration did not publish bundle for model '" `
                + $expectedModelId + "'.")
        $bundleJson = Get-Content `
            -Raw `
            -Encoding UTF8 `
            -LiteralPath $bundlePath `
            | ConvertFrom-Json
        $weightsPath = Join-Path `
            $libraryDirectory `
            ([string]$bundleJson.policyValueArtifact.weightsFile)
        Assert-True (Test-Path -LiteralPath $weightsPath -PathType Leaf) (
            "Registration did not publish weights for model '" `
                + $expectedModelId + "'.")
        Assert-True ((Get-Sha256 $weightsPath) -eq (
            [string]$package.modelArtifact.weightsSha256).ToUpperInvariant()) (
            "Registered weights SHA-256 does not match the source artifact.")
    }

    $productionLibrary = Read-ProductionLibrary $assemblies.Entry
    $productionModels = @(Get-ObjectProperty $productionLibrary "Models")
    Assert-True ($productionModels.Count -eq 2) (
        "Production library reader did not read the committed model index.")
    foreach ($expectedModelId in $modelIdsUnderTest) {
        $productionEntry = @($productionModels | Where-Object {
            ([string](Get-ObjectProperty $_ "ModelId")) -eq $expectedModelId
        })
        Assert-True ($productionEntry.Count -eq 1) (
            "Production library reader missed model '" `
                + $expectedModelId + "'.")
        $productionBundle = Read-ProductionBundle `
            $assemblies.Entry `
            ([string](Get-ObjectProperty `
                $productionEntry[0] `
                "BundleFile"))
        Assert-True ($null -ne $productionBundle) (
            "Production bundle reader rejected model '" `
                + $expectedModelId + "'.")
        Assert-ProductionPayloadLoads `
            $assemblies.Shared `
            $libraryDirectory `
            $productionBundle `
            $expectedModelId
    }

    Assert-True ((Get-Sha256 $configurationPath) -eq $configSha256) (
        "First import changed selectedModelId/trainedModelMode config.")
    $manifestShaBeforeRetry = Get-Sha256 $manifestPath
    $inventoryBeforeRetry = Get-LibraryInventory $libraryDirectory

    $second = Invoke-StaticMethod $import $importArguments
    $secondScan = Get-ObjectProperty $second "Scan"
    $secondRegistration = Get-ObjectProperty $second "Registration"
    Assert-True (([int](Get-ObjectProperty $secondScan "Scanned")) -eq 3) (
        "Retry scanner did not inspect all canonical pairs.")
    Assert-True (([int](Get-ObjectProperty $secondScan "Deduplicated")) -eq 1) (
        "Retry scanner did not preserve package hash deduplication.")
    Assert-True (([int](Get-ObjectProperty $secondRegistration "Installed")) -eq 0) (
        "Retry import installed a duplicate model.")
    Assert-True (([int](Get-ObjectProperty $secondRegistration "Deduplicated")) -eq 2) (
        "Retry import did not report both existing models as deduplicated.")
    Assert-True (([int](Get-ObjectProperty $secondRegistration "Failed")) -eq 0) (
        "Retry import reported a registration failure.")
    Assert-True (([int](Get-ObjectProperty $secondRegistration "Conflicts")) -eq 0) (
        "Retry import reported an unexpected registration conflict.")
    Assert-True (-not [bool](Get-ObjectProperty `
        $secondRegistration `
        "LibraryChanged")) (
        "Retry import rewrote an unchanged model library.")
    Assert-True ((Get-Sha256 $manifestPath) -eq $manifestShaBeforeRetry) (
        "Retry import changed models.json bytes.")
    Assert-True ((Get-LibraryInventory $libraryDirectory) -eq (
        $inventoryBeforeRetry)) (
        "Retry import changed bundle or payload bytes.")
    Assert-True ((Get-Sha256 $configurationPath) -eq $configSha256) (
        "Retry import changed selectedModelId/trainedModelMode config.")

    $entryAssemblyPath = $assemblies.EntryPath
    $sharedAssemblyPath = $assemblies.SharedPath
    Close-ProductionAssemblies
    $assemblies = $null

    $childPowerShell = if ($PSVersionTable.PSEdition -eq "Core") {
        Join-Path $PSHOME "pwsh.exe"
    }
    else {
        Join-Path $env:WINDIR (
            "System32\WindowsPowerShell\v1.0\powershell.exe")
    }
    & $childPowerShell `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $PSCommandPath `
        -RestartVerification `
        -Configuration $Configuration `
        -RestartRoot $sharedRoot `
        -ExpectedModelIds ($modelIdsUnderTest -join "|") `
        -ExpectedConfigSha256 $configSha256
    if ($LASTEXITCODE -ne 0) {
        throw "Fresh-process restart verification failed with exit code $LASTEXITCODE."
    }

    Assert-True ((Get-Sha256 $configurationPath) -eq $configSha256) (
        "Fresh-process restart read changed selectedModelId/trainedModelMode config.")

    $entrySha256 = Get-Sha256 $entryAssemblyPath
    $sharedSha256 = Get-Sha256 $sharedAssemblyPath
    Write-Host (
        "Temporary ModsData bundled-model integration passed: scanned=3, " `
            + "official=1, playerValidated=1, installed=2, " `
            + "scanDeduplicated=1, retryDeduplicated=2, " `
            + "restartLoad=2/2.")
    Write-Host ("AuraToolsExp.Aura.dll SHA256=" + $entrySha256)
    Write-Host ("Aura.Shared.dll SHA256=" + $sharedSha256)
}
finally {
    if ($null -ne $assemblies) {
        Close-ProductionAssemblies
    }
    Remove-IntegrationRoot $testRoot
}
