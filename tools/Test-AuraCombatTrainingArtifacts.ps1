param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSEdition -eq "Desktop") {
    $pwshCommand = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($null -eq $pwshCommand) {
        throw "PowerShell 7 is required to load the Unity net472 production assemblies without desktop CLR strong-name rejection."
    }
    & $pwshCommand.Source `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $PSCommandPath `
        -Configuration $Configuration
    exit $LASTEXITCODE
}
$root = Split-Path -Parent $PSScriptRoot
$trainer = Join-Path $root "tools\train_aura_combat_ai.py"
$manifestPath = Join-Path $root (
    "AuraToolsExp\Config\combat-programs.base-game.manifest.json")
$generatedProgramsPath = Join-Path $root (
    "AuraToolsExp-Dev\Features\AutoBattle\Generated\AuraToolsNativePrograms.g.cs")
$workerProject = Join-Path $root (
    "AuraFoundationTrainer.Worker\AuraFoundationTrainer.Worker.csproj")
$installer = Join-Path $root "tools\Install-AuraPyTorch.cmd"
$bundledModelDirectory = Join-Path $root "AuraToolsExp\ModResource\Model"
$foundationAllowlistPath = Join-Path $root (
    "AuraToolsExp\Config\aura-director.foundation-model-allowlist.json")
$sharedRuntimeAssembly = Join-Path $root "AuraToolsExp\Scripts\Aura.Shared.dll"
$newtonsoftAssembly = Join-Path $root "Managed\Newtonsoft.Json.dll"

foreach ($requiredPath in @(
    $trainer,
    $manifestPath,
    $generatedProgramsPath,
    $workerProject,
    $installer,
    $foundationAllowlistPath,
    $sharedRuntimeAssembly,
    $newtonsoftAssembly
)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Aura combat training artifact is missing: $requiredPath"
    }
}
if (-not (Test-Path -LiteralPath $bundledModelDirectory -PathType Container)) {
    throw "Bundled foundation model directory is missing: $bundledModelDirectory"
}

function Assert-BundledModelDirectorySegment {
    param(
        [Parameter(Mandatory = $true)][string]$DirectoryName,
        [Parameter(Mandatory = $true)][string]$Kind
    )

    if ($DirectoryName.Length -eq 0 -or $DirectoryName.Length -gt 200) {
        throw "Bundled model $Kind directory segment length is invalid: $DirectoryName"
    }
    foreach ($character in $DirectoryName.ToCharArray()) {
        if ([char]::IsControl($character) `
                -or ([char]::GetUnicodeCategory($character) `
                    -eq [Globalization.UnicodeCategory]::Format)) {
            throw "Bundled model $Kind directory contains a control or Unicode Format character."
        }
    }
    if (-not $DirectoryName.EndsWith("]", [StringComparison]::Ordinal)) {
        throw "Bundled model $Kind directory must end with a bracketed machine suffix: $DirectoryName"
    }
    $marker = $DirectoryName.LastIndexOf(" [", [StringComparison]::Ordinal)
    if ($marker -le 0 -or $marker + 3 -ge $DirectoryName.Length) {
        throw "Bundled model $Kind directory has an invalid bracketed machine suffix: $DirectoryName"
    }
    $displayLabel = $DirectoryName.Substring(0, $marker)
    $machineSuffix = $DirectoryName.Substring(
        $marker + 2,
        $DirectoryName.Length - $marker - 3)
    if (($displayLabel.Length -eq 0) `
            -or ($displayLabel.Length -gt 96) `
            -or ($displayLabel -cne $displayLabel.Trim()) `
            -or ($machineSuffix.Length -eq 0) `
            -or ($machineSuffix -cne $machineSuffix.Trim())) {
        throw "Bundled model $Kind directory display label or machine suffix is invalid: $DirectoryName"
    }
}

function Get-BundledFoundationPackageFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ModelRoot
    )

    $resolvedRoot = [IO.Path]::GetFullPath($ModelRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $packages = [Collections.Generic.List[object]]::new()

    $rootItem = Get-Item -LiteralPath $resolvedRoot -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Bundled model root must not be a reparse point: $resolvedRoot"
    }
    $rootEntries = @(Get-ChildItem -LiteralPath $resolvedRoot -Force |
        Sort-Object Name)
    foreach ($entry in $rootEntries) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Bundled model root entry must not be a reparse point: $($entry.FullName)"
        }
    }
    $legacyNames = @(
        "foundation-model-package-v3.json",
        "foundation-model-package-v4.json",
        "foundation-model-package-v5.json"
    )
    $rootFiles = @($rootEntries | Where-Object { -not $_.PSIsContainer })
    $legacyPackages = @($rootFiles | Where-Object {
        $legacyNames -ccontains $_.Name
    })
    if ($legacyPackages.Count -gt 1) {
        throw "Bundled model root may contain at most one fixed-name legacy package."
    }
    $unexpectedRootFiles = @($rootFiles | Where-Object {
        ($legacyNames -cnotcontains $_.Name) `
            -and $_.Name -cne "foundation-model-weights-v5.bin"
    })
    if ($unexpectedRootFiles.Count -ne 0) {
        throw "Bundled model root contains a file outside the fixed legacy allowlist: $($unexpectedRootFiles[0].FullName)"
    }
    $rootWeights = @($rootFiles | Where-Object {
        $_.Name -ceq "foundation-model-weights-v5.bin"
    })
    if ($rootWeights.Count -ne 0 -and $legacyPackages.Count -eq 0) {
        throw "Bundled model root weights have no paired fixed-name legacy package."
    }

    foreach ($legacyPackage in $legacyPackages) {
        $packages.Add([pscustomobject]@{
            Layout = "legacy-top-level"
            PackageFile = $legacyPackage
            RoleDirectoryName = ""
            ReleaseDirectoryName = ""
        })
    }

    foreach ($roleDirectory in @($rootEntries | Where-Object { $_.PSIsContainer })) {
        if (($roleDirectory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Bundled model role directory must not be a reparse point: $($roleDirectory.FullName)"
        }
        Assert-BundledModelDirectorySegment -DirectoryName $roleDirectory.Name `
            -Kind "role"
        $roleEntries = @(Get-ChildItem -LiteralPath $roleDirectory.FullName `
            -Force | Sort-Object Name)
        foreach ($roleEntry in $roleEntries) {
            if (($roleEntry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Bundled model role entry must not be a reparse point: $($roleEntry.FullName)"
            }
        }
        $roleFiles = @($roleEntries | Where-Object { -not $_.PSIsContainer })
        if ($roleFiles.Count -ne 0) {
            throw "Bundled model role directory may only contain release directories: $($roleDirectory.FullName)"
        }
        $releaseDirectories = @($roleEntries | Where-Object { $_.PSIsContainer })
        if ($releaseDirectories.Count -eq 0) {
            throw "Bundled model role directory has no release directory: $($roleDirectory.FullName)"
        }

        foreach ($releaseDirectory in $releaseDirectories) {
            if (($releaseDirectory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Bundled model release directory must not be a reparse point: $($releaseDirectory.FullName)"
            }
            Assert-BundledModelDirectorySegment `
                -DirectoryName $releaseDirectory.Name -Kind "release"
            $releaseEntries = @(Get-ChildItem -LiteralPath $releaseDirectory.FullName `
                -Force | Sort-Object Name)
            foreach ($releaseEntry in $releaseEntries) {
                if (($releaseEntry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Bundled model release entry must not be a reparse point: $($releaseEntry.FullName)"
                }
            }
            $nestedDirectories = @($releaseEntries | Where-Object { $_.PSIsContainer })
            if ($nestedDirectories.Count -ne 0) {
                throw "Bundled model layout must be exactly two directories deep: $($releaseDirectory.FullName)"
            }
            $releaseFiles = @($releaseEntries | Where-Object { -not $_.PSIsContainer })
            $expectedNames = @(
                "foundation-model-package-v5.json",
                "foundation-model-weights-v5.bin"
            )
            $unexpectedFiles = @($releaseFiles | Where-Object {
                $expectedNames -cnotcontains $_.Name
            })
            if ($unexpectedFiles.Count -ne 0) {
                throw "Bundled model release directory contains unexpected files: $($releaseDirectory.FullName)"
            }
            foreach ($expectedName in $expectedNames) {
                if (-not (Test-Path -LiteralPath (
                        Join-Path $releaseDirectory.FullName $expectedName) -PathType Leaf)) {
                    throw "Bundled model release directory is missing $expectedName`: $($releaseDirectory.FullName)"
                }
            }
            $packages.Add([pscustomobject]@{
                Layout = "role-release-v1"
                PackageFile = Get-Item -LiteralPath (
                    Join-Path $releaseDirectory.FullName "foundation-model-package-v5.json")
                RoleDirectoryName = $roleDirectory.Name
                ReleaseDirectoryName = $releaseDirectory.Name
            })
        }
    }

    return $packages.ToArray()
}

function Test-BundledFoundationDiscoveryFixture {
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $fixtureRoot = Join-Path $temporaryRoot (
        "aura-bundled-model-discovery-" + [Guid]::NewGuid().ToString("N"))
    $fixtureRoot = [IO.Path]::GetFullPath($fixtureRoot)
    if (-not $fixtureRoot.StartsWith(
            $temporaryRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Bundled model discovery fixture escaped the temporary root."
    }

    try {
        foreach ($fixture in @(
            @("Role-A [role_a]", "Partner-A-v5.0.0 [111111111111]"),
            @("Role-B [role_b]", "Partner-B-v5.0.0 [222222222222]")
        )) {
            $directory = Join-Path $fixtureRoot (Join-Path $fixture[0] $fixture[1])
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
            [IO.File]::WriteAllText(
                (Join-Path $directory "foundation-model-package-v5.json"),
                "{}",
                [Text.UTF8Encoding]::new($false))
            [IO.File]::WriteAllBytes(
                (Join-Path $directory "foundation-model-weights-v5.bin"),
                [byte[]]@(0))
        }
        $discovered = @(Get-BundledFoundationPackageFiles -ModelRoot $fixtureRoot)
        $uniquePackageNames = @($discovered |
            Select-Object -ExpandProperty PackageFile |
            Select-Object -ExpandProperty Name -Unique)
        if (($discovered.Count -ne 2) -or ($uniquePackageNames.Count -ne 1)) {
            throw "Bundled model discovery does not support repeated canonical filenames in separate role releases."
        }
    }
    finally {
        if (Test-Path -LiteralPath $fixtureRoot -PathType Container) {
            Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
        }
    }
}

function Assert-BundledFoundationCollectionIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Identities
    )

    $byModelId = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[object]]]::new(
        [StringComparer]::Ordinal)
    $byRelease = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[object]]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($identity in $Identities) {
        if (-not $byModelId.ContainsKey($identity.ModelId)) {
            $byModelId[$identity.ModelId] = [System.Collections.Generic.List[object]]::new()
        }
        $byModelId[$identity.ModelId].Add($identity)
        if (-not $byRelease.ContainsKey($identity.ReleaseKey)) {
            $byRelease[$identity.ReleaseKey] = [System.Collections.Generic.List[object]]::new()
        }
        $byRelease[$identity.ReleaseKey].Add($identity)
    }
    foreach ($group in $byModelId.Values) {
        $hashes = @($group | Select-Object -ExpandProperty PackageSha256 -Unique)
        if ($hashes.Count -gt 1) {
            throw "Bundled foundation collection reuses one ModelId with different package SHA-256 values: $(@($group | Select-Object -ExpandProperty Source) -join ', ')"
        }
    }
    foreach ($group in $byRelease.Values) {
        $modelIds = [System.Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        foreach ($identity in $group) {
            $null = $modelIds.Add($identity.ModelId)
        }
        if ($modelIds.Count -gt 1) {
            throw "Bundled foundation collection contains multiple ModelIds for the same role/partner/card-pack/ModelVersion release: $(@($group | Select-Object -ExpandProperty Source) -join ', ')"
        }
    }
}

function Test-BundledFoundationCollectionIdentityFixture {
    $hashA = "A" * 64
    $hashB = "B" * 64
    $exact = [pscustomobject]@{
        ModelId = "model-a"
        PackageSha256 = $hashA
        ReleaseKey = "release-a"
        Source = "a/package.json"
    }
    Assert-BundledFoundationCollectionIdentity -Identities @($exact, $exact)

    $sameIdChanged = [pscustomobject]@{
        ModelId = "model-a"
        PackageSha256 = $hashB
        ReleaseKey = "release-b"
        Source = "b/package.json"
    }
    $rejected = $false
    try {
        Assert-BundledFoundationCollectionIdentity -Identities @(
            $exact,
            $sameIdChanged)
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw "Bundled collection fixture did not reject one ModelId with different package hashes."
    }

    $sameReleaseChangedId = [pscustomobject]@{
        ModelId = "model-b"
        PackageSha256 = $hashB
        ReleaseKey = "release-a"
        Source = "c/package.json"
    }
    $rejected = $false
    try {
        Assert-BundledFoundationCollectionIdentity -Identities @(
            $exact,
            $sameReleaseChangedId)
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw "Bundled collection fixture did not reject multiple ModelIds for one release key."
    }
}

function Get-StrictUtf8Text {
    param([Parameter(Mandatory = $true)][string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    return [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
}

function Assert-CurrentArtifactFields {
    param(
        [Parameter(Mandatory = $true)]$Artifact,
        [Parameter(Mandatory = $true)][string]$PackageName
    )

    if (([int]$Artifact.SchemaVersion -ne 1) `
            -or ([string]$Artifact.ArtifactKind -cne "aura.combat-policy-value.weights") `
            -or ([string]$Artifact.Precision -cne "float32-le") `
            -or ([string]$Artifact.WeightLayout -cne "fixed-v1-state-action-input-major") `
            -or ([string]$Artifact.ModelProtocol -cne "aura.combat-policy-value.mlp.v2") `
            -or ([int]$Artifact.ProtocolVersion -ne 2) `
            -or ([int]$Artifact.FeatureSchemaVersion -ne 26) `
            -or ([string]$Artifact.FeatureEncodingMode -cne "partitioned-v3") `
            -or ([int]$Artifact.StateDimensions -lt 16) `
            -or ([int]$Artifact.ActionDimensions -lt 16) `
            -or ([int]$Artifact.HiddenDimensions -lt 8) `
            -or ([int]$Artifact.ActionQuantileCount -lt 4) `
            -or (-not [bool]$Artifact.ActionQuantileHeadReady) `
            -or [string]::IsNullOrWhiteSpace([string]$Artifact.ModelId) `
            -or [string]::IsNullOrWhiteSpace([string]$Artifact.DecisionProfile)) {
        throw "Bundled foundation model artifact protocol is invalid: $PackageName"
    }

    $state = [int64]$Artifact.StateDimensions
    $action = [int64]$Artifact.ActionDimensions
    $hidden = [int64]$Artifact.HiddenDimensions
    $quantiles = [int64]$Artifact.ActionQuantileCount
    $expectedValues = $state * $hidden + $hidden `
        + $action * $hidden + $hidden `
        + $hidden + 1L `
        + $quantiles * $hidden + $quantiles `
        + 5L * ($hidden + 1L)
    if (([int64]$Artifact.WeightValueCount -ne $expectedValues) `
            -or ([int64]$Artifact.WeightsByteLength -ne $expectedValues * 4L)) {
        throw "Bundled foundation model weight layout is inconsistent: $PackageName"
    }
}

function Assert-ExactFoundationAllowlistEntry {
    param(
        [Parameter(Mandatory = $true)]$Package,
        [Parameter(Mandatory = $true)][string]$PackageSha256,
        [Parameter(Mandatory = $true)]$Allowlist,
        [Parameter(Mandatory = $true)][string]$PackageName
    )

    $model = if ($null -ne $Package.ModelArtifact) {
        $Package.ModelArtifact
    } else {
        $Package.Model
    }
    $lineage = if ([int]$Package.SchemaVersion -le 4) {
        "Aura.Foundation.V1"
    } else {
        [string]$Package.FoundationLineage
    }
    $nativeProgramHash = [string]$Package.Compatibility.NativeProgramPackageHash
    $matches = @($Allowlist.Entries | Where-Object {
        ([string]$_.FoundationLineage -ceq $lineage) `
            -and ([string]$_.ModelId -ceq [string]$model.ModelId) `
            -and ([string]$_.ArtifactSha256 -ceq $PackageSha256) `
            -and ([string]$_.WeightsSha256 -ceq "") `
            -and ([int]$_.FeatureSchemaVersion -eq [int]$model.FeatureSchemaVersion) `
            -and ([string]$_.ContentSetHash -ceq [string]$Package.ContentSetHash) `
            -and ([string]$_.RulesetHash -ceq [string]$Package.RulesetHash) `
            -and ([string]$_.NativeProgramPackageHash -ceq $nativeProgramHash) `
            -and ([string]$_.RequiredStartGateCapability -ceq "ReadyToStartGate.V1")
    })
    if ($matches.Count -ne 1) {
        throw "Bundled foundation model has no unique exact artifact allowlist entry: $PackageName"
    }
}

Test-BundledFoundationDiscoveryFixture
Test-BundledFoundationCollectionIdentityFixture

& python $trainer --self-test
if ($LASTEXITCODE -ne 0) {
    throw "Aura combat AI trainer self-test failed with exit code $LASTEXITCODE."
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.runtimeProtocol -ne "aura.native-programs.precompiled.v1" `
        -or $manifest.programCount -lt 400 `
        -or [string]::IsNullOrWhiteSpace($manifest.programSetSha256)) {
    throw "Aura precompiled native program manifest is invalid."
}

$allowlist = Get-StrictUtf8Text -Path $foundationAllowlistPath |
    ConvertFrom-Json
if ([int]$allowlist.SchemaVersion -ne 1 -or $null -eq $allowlist.Entries) {
    throw "AuraDirector foundation model allowlist is missing or incompatible."
}
$weightsOnlyEntries = @($allowlist.Entries | Where-Object {
    -not [string]::IsNullOrWhiteSpace([string]$_.WeightsSha256)
})
if ($weightsOnlyEntries.Count -ne 0) {
    throw "Bundled foundation trust entries must use exact ArtifactSha256 and leave WeightsSha256 empty."
}

[Reflection.Assembly]::LoadFrom($newtonsoftAssembly) | Out-Null
[Reflection.Assembly]::LoadFrom($sharedRuntimeAssembly) | Out-Null

$bundledModels = @(Get-BundledFoundationPackageFiles `
    -ModelRoot $bundledModelDirectory)
if ($bundledModels.Count -lt 1) {
    throw "AuraToolsExp must publish at least one bundled foundation model."
}
if ($bundledModels.Count -gt 128) {
    throw "Bundled foundation model count exceeds the runtime limit of 128."
}
$layoutEntries = @(Get-ChildItem -LiteralPath $bundledModelDirectory `
    -Force -Recurse)
if ($layoutEntries.Count -gt 2048) {
    throw "Bundled foundation model layout exceeds the runtime limit of 2048 entries."
}
$aggregateModelBytes = [int64]0
$publishedModelIdentities = [System.Collections.Generic.List[object]]::new()
foreach ($candidate in $bundledModels) {
    $modelFile = $candidate.PackageFile
    if ($modelFile.Length -lt 1 -or $modelFile.Length -gt 64MB) {
        throw "Bundled foundation manifest must be between 1 byte and 64MB: $($modelFile.FullName)"
    }
    $aggregateModelBytes += [int64]$modelFile.Length
    $packageJson = Get-StrictUtf8Text -Path $modelFile.FullName
    $packageSha256 = (Get-FileHash -Algorithm SHA256 `
        -LiteralPath $modelFile.FullName).Hash.ToUpperInvariant()
    $package = [AuraShared.Core.AuraSharedJson]::Deserialize(
        $packageJson,
        [AuraCombatAi.Shared.CombatFoundationModelPackage])
    $diagnostic = ""
    if (-not [AuraCombatAi.Shared.CombatFoundationModelPackageProtocol]::TryValidate(
            $package,
            [ref]$diagnostic)) {
        throw "Bundled foundation package protocol validation failed: $($modelFile.FullName): $diagnostic"
    }

    if ($candidate.Layout -eq "role-release-v1") {
        $roleMatch = [regex]::Match(
            $candidate.RoleDirectoryName,
            '^.+ \[([^\[\]]+)\]$')
        if ((-not $roleMatch.Success) `
                -or ($roleMatch.Groups[1].Value -cne [string]$package.RoleId)) {
            throw "Bundled model role directory suffix does not match package RoleId: $($modelFile.FullName)"
        }
        $releaseMatch = [regex]::Match(
            $candidate.ReleaseDirectoryName,
            '^.+ \[([0-9A-Fa-f]{12})\]$')
        if ((-not $releaseMatch.Success) `
                -or $releaseMatch.Groups[1].Value.ToUpperInvariant() `
                    -cne $packageSha256.Substring(0, 12)) {
            throw "Bundled model release directory hash suffix does not match package SHA-256: $($modelFile.FullName)"
        }
        if (([int]$package.SchemaVersion -ne 5) `
                -or ([string]$package.ModelVersion -cne "5.0.0") `
                -or ([string]$package.FoundationLineage -cne "Aura.Foundation.V2")) {
            throw "Nested bundled foundation models must use the current v5/V2 protocol: $($modelFile.FullName)"
        }
    }

    if ($null -ne $package.ModelArtifact) {
        Assert-CurrentArtifactFields -Artifact $package.ModelArtifact `
            -PackageName $modelFile.FullName
        $weightsFileName = [string]$package.ModelArtifact.WeightsFile
        if ([IO.Path]::GetFileName($weightsFileName) -cne $weightsFileName) {
            throw "Bundled model weights must be a same-directory filename: $($modelFile.FullName)"
        }
        if (($candidate.Layout -eq "role-release-v1") `
                -and ($weightsFileName -cne "foundation-model-weights-v5.bin")) {
            throw "Nested bundled model must preserve the canonical v5 weights filename: $($modelFile.FullName)"
        }
        $modelDirectory = $modelFile.Directory.FullName
        $weightsPath = [IO.Path]::GetFullPath((
            Join-Path $modelDirectory $weightsFileName))
        if ([IO.Path]::GetDirectoryName($weightsPath).TrimEnd('\', '/') `
                -cne $modelDirectory.TrimEnd('\', '/')) {
            throw "Bundled model weights escaped the package directory: $($modelFile.FullName)"
        }
        if (-not (Test-Path -LiteralPath $weightsPath -PathType Leaf)) {
            throw "Bundled model weights are missing: $weightsPath"
        }
        $weightsInfo = Get-Item -LiteralPath $weightsPath
        $aggregateModelBytes += [int64]$weightsInfo.Length
        if ($weightsInfo.Length -ne [int64]$package.ModelArtifact.WeightsByteLength) {
            throw "Bundled model weights length does not match the manifest: $weightsPath"
        }
        $weightsSha256 = (Get-FileHash -Algorithm SHA256 `
            -LiteralPath $weightsPath).Hash.ToUpperInvariant()
        if ($weightsSha256 -cne (
                [string]$package.ModelArtifact.WeightsSha256).ToUpperInvariant()) {
            throw "Bundled model weights SHA-256 does not match the manifest: $weightsPath"
        }
        $diagnostic = ""
        if (-not [AuraCombatAi.Shared.CombatPolicyValueArtifactProtocol]::TryValidatePayload(
                $modelDirectory,
                $package.ModelArtifact,
                [ref]$diagnostic)) {
            throw "Bundled model FP32 payload validation failed: $($modelFile.FullName): $diagnostic"
        }
        $runtime = [AuraCombatAi.Shared.CombatPolicyValueRuntimeDefinition]::new()
        $diagnostic = ""
        if (-not [AuraCombatAi.Shared.CombatPolicyValueArtifactProtocol]::TryLoad(
                $modelDirectory,
                $package.ModelArtifact,
                [ref]$runtime,
                [ref]$diagnostic)) {
            throw "Bundled model FP32 production reload failed: $($modelFile.FullName): $diagnostic"
        }
        $null = [AuraCombatAi.Shared.ManagedCombatPolicyValueModel]::new($runtime)
    }

    Assert-ExactFoundationAllowlistEntry -Package $package `
        -PackageSha256 $packageSha256 -Allowlist $allowlist `
        -PackageName $modelFile.FullName

    $model = if ($null -ne $package.ModelArtifact) {
        $package.ModelArtifact
    } else {
        $package.Model
    }
    $normalizedCardPacks = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($cardPackId in @($package.EnabledRewardCardPackIds)) {
        $normalizedId = ([string]$cardPackId).Trim()
        if (-not [string]::IsNullOrWhiteSpace($normalizedId)) {
            $null = $normalizedCardPacks.Add($normalizedId)
        }
    }
    $sortedCardPacks = [string[]]@($normalizedCardPacks)
    [Array]::Sort($sortedCardPacks, [StringComparer]::OrdinalIgnoreCase)
    $publishedModelIdentities.Add([pscustomobject]@{
        ModelId = ([string]$model.ModelId).Trim()
        PackageSha256 = $packageSha256
        ReleaseKey = (([string]$package.RoleId).Trim().ToUpperInvariant()) `
            + [string][char]0x001E `
            + (([string]$package.PartnerId).Trim().ToUpperInvariant()) `
            + [string][char]0x001E `
            + (([string]$package.ModelVersion).Trim().TrimStart('v', 'V').ToUpperInvariant()) `
            + [string][char]0x001E `
            + (($sortedCardPacks | ForEach-Object { $_.ToUpperInvariant() }) -join ([string][char]0x001F))
        Source = $modelFile.FullName
    })
}
if ($aggregateModelBytes -gt 1GB) {
    throw "Bundled foundation model aggregate bytes exceed the runtime limit of 1GB."
}

Assert-BundledFoundationCollectionIdentity `
    -Identities $publishedModelIdentities.ToArray()

Write-Host (
    "Aura combat training artifacts passed: programs={0}, models={1}, layouts={2}." -f `
        $manifest.programCount,
        $bundledModels.Count,
        (@($bundledModels | Select-Object -ExpandProperty Layout -Unique) -join ","))
