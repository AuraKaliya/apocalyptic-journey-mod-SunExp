param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}

$skinModRoot = Join-Path $RepoRoot "TestMods\SkinExp"
$auraToolsModRoot = Join-Path $RepoRoot "AuraToolsExp"
$sunModRoot = Join-Path $RepoRoot "SunExp"
$sharedRoot = Join-Path $RepoRoot "AuraSkinShared"
$required = @(
    (Join-Path $auraToolsModRoot "ModConfig.json"),
    (Join-Path $auraToolsModRoot "Icon.png"),
    (Join-Path $auraToolsModRoot "Scripts\Entry.dll"),
    (Join-Path $auraToolsModRoot "skin.schema.json"),
    (Join-Path $auraToolsModRoot "character.schema.json"),
    (Join-Path $auraToolsModRoot "SharedResources\Skins\package.json"),
    (Join-Path $auraToolsModRoot "SharedResources\Skins\README.md"),
    (Join-Path $skinModRoot "ModConfig.json"),
    (Join-Path $skinModRoot "Icon.png"),
    (Join-Path $skinModRoot "Scripts\Entry.dll"),
    (Join-Path $skinModRoot "skin.schema.json"),
    (Join-Path $skinModRoot "character.schema.json"),
    (Join-Path $skinModRoot "SharedResources\Skins\package.json"),
    (Join-Path $skinModRoot "SharedResources\Skins\README.md"),
    (Join-Path $sunModRoot "SharedResources\Skins\package.json"),
    (Join-Path $sharedRoot "Schemas\skin-package.schema.json")
)

foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing required shared skin artifact: $path"
    }
}

foreach ($forbidden in @(
    (Join-Path $auraToolsModRoot "Skins"),
    (Join-Path $skinModRoot "Skins"),
    (Join-Path $sunModRoot "Skins")
)) {
    if (Test-Path -LiteralPath $forbidden) {
        throw "Direct runtime skin source directory is forbidden: $forbidden"
    }
}

$requiredShared = @(
    "AuraSkinRuntime.cs",
    "Infrastructure\SkinPaths.cs",
    "Models\SkinPackageManifest.cs",
    "Services\SkinPackageInstaller.cs",
    "Services\SkinRegistry.cs",
    "Services\SkinSelectionStore.cs",
    "Mechanics\SkinRuntime.cs",
    "Hooks\SkinRuntimeHooks.cs"
)
foreach ($relative in $requiredShared) {
    $path = Join-Path $sharedRoot $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing required AuraSkinShared source: $path"
    }
}

$consumers = @(
    @{ Entry = "TestMods\SkinExp-Dev\Entry.cs"; Project = "TestMods\SkinExp-Dev\SkinExp.Dll.csproj" },
    @{ Entry = "AuraToolsExp-Dev\Entry.cs"; Project = "AuraToolsExp-Dev\AuraToolsExp.Dll.csproj" },
    @{ Entry = "SunExp-Dev\Entry.cs"; Project = "SunExp-Dev\SunExp.Dll.csproj" }
)
foreach ($consumer in $consumers) {
    $entryText = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot $consumer.Entry)
    $hasSkinInitialization = $entryText.Contains("AuraSkinRuntime.Initialize") -or
        ($consumer.Entry -eq "AuraToolsExp-Dev\Entry.cs" -and $entryText.Contains("AuraToolsSkinRuntime.Initialize"))
    if (-not $hasSkinInitialization) {
        throw "AuraSkinShared initialization is missing from $($consumer.Entry)"
    }

    $projectText = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot $consumer.Project)
    if (-not ($projectText.Contains("AuraSkinShared") -or $projectText.Contains("AuraSharedRuntime-Dev\Aura.Shared.csproj") -or $projectText.Contains("AuraSharedRuntime-Dev/Aura.Shared.csproj"))) {
        throw "AuraSkinShared runtime reference is missing from $($consumer.Project)"
    }
}

foreach ($providerEntry in @("TestMods\SkinExp-Dev\Entry.cs", "SunExp-Dev\Entry.cs")) {
    $entryText = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot $providerEntry)
    if (-not $entryText.Contains("AuraSkinRuntime.RegisterPackage")) {
        throw "Bundled skin package registration is missing from $providerEntry"
    }
}

$auraToolsEntryText = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot "AuraToolsExp-Dev\Entry.cs")
if (-not $auraToolsEntryText.Contains("AuraToolsSkinRuntime.Initialize")) {
    throw "AuraToolsExp skin runtime initialization is missing."
}

$auraToolsSkinRuntimeText = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot "AuraToolsExp-Dev\Features\Skin\AuraToolsSkinRuntime.cs")
foreach ($requiredText in @("AuraSkinRuntime.RegisterPackage", "AuraSkinSelectionCommand", "AuraToolsRpcTransport.Send", "excludeOwner: true", "ApplyRemoteSelection")) {
    if (-not $auraToolsSkinRuntimeText.Contains($requiredText)) {
        throw "AuraToolsExp skin runtime contract is missing: $requiredText"
    }
}

$skinRuntimeText = Get-Content -Raw -LiteralPath (Join-Path $sharedRoot "AuraSkinRuntime.cs")
if (-not $skinRuntimeText.Contains('private const string GlobalObjectName = "AuraSkin.Global"')) {
    throw "AuraSkinShared global runtime identity is missing."
}
if (-not $skinRuntimeText.Contains("CurrentProtocolVersion = 6") -or
    -not $skinRuntimeText.Contains('"RegisterPackage"')) {
    throw "AuraSkinShared sparse ManualSelection candidate protocol v6 is incomplete."
}
if ($skinRuntimeText.Contains("RegisterSkinRoot")) {
    throw "Legacy external skin root registration must not remain in AuraSkinShared."
}

$skinPathsText = Get-Content -Raw -LiteralPath (Join-Path $sharedRoot "Infrastructure\SkinPaths.cs")
foreach ($requiredText in @(
    'AuraSharedPaths.SharedSystemConfigDirectory(AuraSharedSystems.Skin)',
    'Path.Combine(AuraSharedPaths.RegistriesRootDirectory, "Skin")'
)) {
    if (-not $skinPathsText.Contains($requiredText)) {
        throw "AuraSkinShared path contract is missing: $requiredText"
    }
}
foreach ($forbiddenText in @("ModsRootDirectory", "PackageDirectories", "AdditionalSkinRoots", "LegacySettingsPaths")) {
    if ($skinPathsText.Contains($forbiddenText)) {
        throw "Legacy or non-shared skin path remains in SkinPaths: $forbiddenText"
    }
}

$skinRegistryText = Get-Content -Raw -LiteralPath (Join-Path $sharedRoot "Services\SkinRegistry.cs")
if (-not $skinRegistryText.Contains("SkinPaths.SkinRootDirectory") -or
    -not $skinRegistryText.Contains("ByQualifiedId") -or
    -not $skinRegistryText.Contains("BySemanticKey") -or
    -not $skinRegistryText.Contains("QualifiedSkinId") -or
    -not $skinRegistryText.Contains("ConfigureCandidateEnablement") -or
    -not $skinRegistryText.Contains("non-canonical shared skin directory")) {
    throw "SkinRegistry does not preserve owner-qualified candidates and semantic groups."
}
if ($skinRegistryText.Contains("Ignored duplicate shared skin identity")) {
    throw "SkinRegistry must not destructively discard cross-owner semantic duplicates."
}
if ($skinRegistryText.Contains('"*.skin.json"') -or $skinRegistryText.Contains("SearchOption.AllDirectories")) {
    throw "SkinRegistry still contains legacy or broad recursive discovery."
}

$selectionText = Get-Content -Raw -LiteralPath (Join-Path $sharedRoot "Services\SkinSelectionStore.cs")
if ($selectionText.Contains("MigrateLegacy") -or $selectionText.Contains("AuraSharedMigration")) {
    throw "Legacy skin selection migration must not remain."
}
foreach ($forbiddenText in @("SkinExp.career_1.summer_cool", "AuraToolsExp.career_1.summer_cool")) {
    if ($selectionText.Contains($forbiddenText)) {
        throw "SkinSelectionStore must not hard-code concrete consumer skin ids: $forbiddenText"
    }
}
if (-not $selectionText.Contains("AuraSharedConfigStore.ReadShared") -or
    -not $selectionText.Contains("AuraSharedConfigStore.WriteShared")) {
    throw "Skin selections do not use shared configuration storage."
}

foreach ($requiredText in @("SkinExp.career_1.summer_cool", "AuraToolsExp.career_1.summer_cool", "SkinRuntime.TryRemapSelection")) {
    if (-not $auraToolsSkinRuntimeText.Contains($requiredText)) {
        throw "AuraTools skin migration contract is missing: $requiredText"
    }
}

$skinSettingsText = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot "AuraToolsExp-Dev\Config\AuraToolsSkinSettings.cs")
foreach ($requiredText in @("CandidateSelectionConfigured", "EnabledSkinIds", "SetCandidateEnabled")) {
    if (-not $skinSettingsText.Contains($requiredText)) {
        throw "AuraTools skin candidate configuration is missing: $requiredText"
    }
}

$installerText = Get-Content -Raw -LiteralPath (Join-Path $sharedRoot "Services\SkinPackageInstaller.cs")
foreach ($requiredText in @(
    "AuraSharedResourceProtocol.Register",
    "AuraSharedResourceProtocol.QueryCatalog",
    "entry.Active && entry.Available",
    "CanonicalRelativePath = entry.CanonicalPath",
    "AuraSharedResourceKinds.Directory",
    "AuraSharedSystems.Skin",
    "LegacyPaths",
    "GetActiveResources"
)) {
    if (-not $installerText.Contains($requiredText)) {
        throw "Skin package installer safety contract is missing: $requiredText"
    }
}
foreach ($forbiddenText in @("SHA256.Create", "File.Copy", "Directory.Move", "InstalledSkinRegistry", "ActivePackages")) {
    if ($installerText.Contains($forbiddenText)) {
        throw "Skin adapter still owns Core-level storage behavior: $forbiddenText"
    }
}

$legacyImplementationFiles = @(Get-ChildItem -LiteralPath (Join-Path $RepoRoot "TestMods\SkinExp-Dev") -Recurse -File -Filter "*.cs" |
    Where-Object { $_.FullName -notlike "*\obj\*" -and $_.Name -ne "Entry.cs" })
if ($legacyImplementationFiles.Count -gt 0) {
    throw "SkinExp consumer still contains implementation source outside AuraSkinShared: $($legacyImplementationFiles.FullName -join ', ')"
}

$config = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $skinModRoot "ModConfig.json") | ConvertFrom-Json
if ($config.ModName -ne "SkinExp" -or $config.MustSame -ne $false) {
    throw "SkinExp ModConfig identity or local-cosmetic policy is invalid."
}

$auraToolsConfig = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $auraToolsModRoot "ModConfig.json") | ConvertFrom-Json
if ($auraToolsConfig.ModName -ne "AuraToolsExp" -or [string]::IsNullOrWhiteSpace([string]$auraToolsConfig.ModVersion)) {
    throw "AuraToolsExp ModConfig identity or version is invalid."
}

function Read-JsonFile([string]$path) {
    return Get-Content -Raw -Encoding UTF8 -LiteralPath $path | ConvertFrom-Json
}

function Test-IsInside([string]$path, [string]$directory) {
    $fullPath = [System.IO.Path]::GetFullPath($path).TrimEnd('\', '/')
    $fullDirectory = [System.IO.Path]::GetFullPath($directory).TrimEnd('\', '/')
    return $fullPath.Equals($fullDirectory, [System.StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($fullDirectory + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($fullDirectory + [System.IO.Path]::AltDirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-ValidCareerId([string]$careerId, [string]$path) {
    if ([string]::IsNullOrWhiteSpace($careerId)) {
        throw "Missing targetCareerId in $path"
    }
    if ($careerId -match '^\d+$') {
        throw "Official career id '$careerId' in $path must use runtime id 'career_$careerId'."
    }
}

function Test-SkinSource([string]$skinDirectory, [hashtable]$seenIdentities) {
    $skinManifestPath = Join-Path $skinDirectory "skin.json"
    $characterManifestPath = Join-Path (Split-Path -Parent $skinDirectory) "character.json"
    if (-not (Test-Path -LiteralPath $skinManifestPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $characterManifestPath -PathType Leaf)) {
        throw "Skin source requires skin.json and parent character.json: $skinDirectory"
    }

    $character = Read-JsonFile $characterManifestPath
    $skin = Read-JsonFile $skinManifestPath
    if ($character.schemaVersion -ne 2 -or $character.enabled -eq $false) {
        throw "Invalid character manifest: $characterManifestPath"
    }
    if ($skin.schemaVersion -ne 2 -or $skin.enabled -eq $false) {
        throw "Invalid skin manifest: $skinManifestPath"
    }

    $targetCareerId = [string]$character.targetCareerId
    if ([string]::IsNullOrWhiteSpace($targetCareerId)) {
        $targetCareerId = Split-Path -Leaf (Split-Path -Parent $skinDirectory)
    }
    Assert-ValidCareerId $targetCareerId $characterManifestPath

    if (-not [string]::IsNullOrWhiteSpace([string]$skin.targetCareerId) -and
        -not ([string]$skin.targetCareerId).Equals($targetCareerId, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Skin targetCareerId differs from its character manifest: $skinManifestPath"
    }
    if ([string]::IsNullOrWhiteSpace([string]$skin.skinId)) {
        throw "Missing skinId in $skinManifestPath"
    }

    $identity = ($targetCareerId + "::" + [string]$skin.skinId).ToLowerInvariant()
    if ($seenIdentities.ContainsKey($identity)) {
        throw "Duplicate skin identity '$identity' in $skinManifestPath and $($seenIdentities[$identity])"
    }
    $seenIdentities[$identity] = $skinManifestPath

    if ($null -eq $skin.assets) {
        throw "Missing assets in $skinManifestPath"
    }

    $validAssets = 0
    foreach ($field in @("CareerImage", "Avatar", "Character", "DollIcon", "ChoiceIcon", "Animation")) {
        $configuredPath = [string]$skin.assets.$field
        if ([string]::IsNullOrWhiteSpace($configuredPath)) {
            continue
        }

        $candidate = [System.IO.Path]::GetFullPath((Join-Path $skinDirectory $configuredPath))
        if (-not (Test-IsInside $candidate $skinDirectory)) {
            throw "Asset '$configuredPath' escapes its skin source in $skinManifestPath"
        }

        $exists = if ($field -eq "Animation") {
            Test-Path -LiteralPath $candidate -PathType Container
        }
        else {
            (Test-Path -LiteralPath $candidate -PathType Leaf) -or
            (Test-Path -LiteralPath ($candidate + ".png") -PathType Leaf) -or
            (Test-Path -LiteralPath ($candidate + ".jpg") -PathType Leaf) -or
            (Test-Path -LiteralPath ($candidate + ".jpeg") -PathType Leaf)
        }
        if (-not $exists) {
            throw "Missing asset '$configuredPath' declared by $skinManifestPath"
        }
        $validAssets++
    }

    if ($validAssets -eq 0) {
        throw "No valid assets declared by $skinManifestPath"
    }

    return [PSCustomObject]@{
        TargetCareerId = $targetCareerId
        SkinId = [string]$skin.skinId
        Path = $skinManifestPath
    }
}

$seenPackages = @{}
$seenIdentities = @{}
$installedSources = @()
$packagePaths = @(
    (Join-Path $auraToolsModRoot "SharedResources\Skins\package.json"),
    (Join-Path $skinModRoot "SharedResources\Skins\package.json"),
    (Join-Path $sunModRoot "SharedResources\Skins\package.json")
)
foreach ($packagePath in $packagePaths) {
    $package = Read-JsonFile $packagePath
    if ($package.schemaVersion -ne 1 -or
        [string]::IsNullOrWhiteSpace([string]$package.packageId) -or
        [int]$package.packageVersion -lt 1 -or
        $null -eq $package.resources -or
        $package.resources.Count -eq 0) {
        throw "Invalid skin package manifest: $packagePath"
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$package.participantKind) -and
        @("Content", "Tool") -notcontains [string]$package.participantKind) {
        throw "Invalid skin participantKind: $packagePath"
    }

    $packageKey = ([string]$package.packageId).ToLowerInvariant()
    if ($seenPackages.ContainsKey($packageKey)) {
        throw "Duplicate skin packageId '$($package.packageId)'"
    }
    $seenPackages[$packageKey] = $packagePath

    $packageDirectory = Split-Path -Parent $packagePath
    foreach ($resource in $package.resources) {
        $source = [string]$resource.source
        if ([string]::IsNullOrWhiteSpace($source) -or [System.IO.Path]::IsPathRooted($source)) {
            throw "Skin package source must be relative: $packagePath"
        }

        $sourceDirectory = [System.IO.Path]::GetFullPath((Join-Path $packageDirectory $source))
        if (-not (Test-IsInside $sourceDirectory $packageDirectory) -or
            -not (Test-Path -LiteralPath $sourceDirectory -PathType Container)) {
            throw "Skin package source is missing or escapes package: $source"
        }
        $installedSources += Test-SkinSource $sourceDirectory $seenIdentities
    }
}

$wuna = @($installedSources | Where-Object { $_.TargetCareerId -eq "SunExp_wuna_wuna" })
if ($wuna.Count -ne 1 -or $wuna[0].SkinId -ne "SunExp.SunExp_wuna_wuna.summer_cool" -or
    $wuna[0].Path -notlike "$(Join-Path $sunModRoot '*')") {
    throw "WuNa summer skin must be published exactly once by SunExp."
}

$columbina = @($installedSources | Where-Object { $_.TargetCareerId -eq "SunExp_columbina_columbina" })
if ($columbina.Count -ne 1 -or $columbina[0].SkinId -ne "SunExp.SunExp_columbina_columbina.restore_colors" -or
    $columbina[0].Path -notlike "$(Join-Path $sunModRoot '*')") {
    throw "Columbina Restore Colors skin must be published exactly once by SunExp."
}

$sunSkinPackage = Read-JsonFile (Join-Path $sunModRoot "SharedResources\Skins\package.json")
if ([int]$sunSkinPackage.packageVersion -lt 3 -or
    @($sunSkinPackage.resources | Where-Object { $_.source -eq "SunExp_columbina_columbina/DoByHand" }).Count -ne 1) {
    throw "SunExp skin package must publish Columbina Restore Colors at package version 3 or newer."
}

$columbinaSkinDirectory = Split-Path -Parent $columbina[0].Path
$columbinaManifest = Read-JsonFile $columbina[0].Path
$expectedColumbinaSkinName = -join @([char]0x590D, [char]0x539F, [char]0x8272, [char]0x5F69)
if ($columbinaManifest.name -ne $expectedColumbinaSkinName -or $columbinaManifest.author -ne "Aura" -or
    $columbinaManifest.preview -ne "Columbina.png" -or
    $columbinaManifest.assets.CareerImage -ne "Columbina.png" -or
    $columbinaManifest.assets.Character -ne "Columbina.png" -or
    $columbinaManifest.assets.Animation -ne ".") {
    throw "Columbina Restore Colors skin metadata is incomplete or inconsistent."
}

$columbinaIdleFirst = Join-Path $columbinaSkinDirectory "Idle\frame_01.png"
$columbinaIdleHash = (Get-FileHash -LiteralPath $columbinaIdleFirst -Algorithm SHA256).Hash
$columbinaStates = @("Idle", "Attack", "Hit", "Buff", "Debuff", "Skill", "Special", "Special1", "Special2", "Defend")
foreach ($state in $columbinaStates) {
    $stateDirectory = Join-Path $columbinaSkinDirectory $state
    $stateConfigPath = Join-Path $stateDirectory "config.json"
    if (-not (Test-Path -LiteralPath $stateDirectory -PathType Container) -or
        -not (Test-Path -LiteralPath $stateConfigPath -PathType Leaf)) {
        throw "Columbina Restore Colors is missing animation state '$state'."
    }

    $stateConfig = Read-JsonFile $stateConfigPath
    if ([double]$stateConfig.AnimationPerFrame -ne 0.2 -or $stateConfig.isLoop -ne $true -or $stateConfig.Direction -ne "Right") {
        throw "Columbina Restore Colors animation config is invalid for state '$state'."
    }

    $stateFrames = @(Get-ChildItem -LiteralPath $stateDirectory -Filter "*.png" -File)
    $expectedFrameCount = if ($state -eq "Idle") { 8 } else { 1 }
    if ($stateFrames.Count -ne $expectedFrameCount) {
        throw "Columbina Restore Colors state '$state' expected $expectedFrameCount frame(s), found $($stateFrames.Count)."
    }

    if ($state -ne "Idle" -and (Get-FileHash -LiteralPath $stateFrames[0].FullName -Algorithm SHA256).Hash -ne $columbinaIdleHash) {
        throw "Columbina Restore Colors state '$state' must reuse Idle frame_01.png."
    }
}

$official = @($installedSources | Where-Object { $_.TargetCareerId -eq "career_1" -and $_.SkinId -eq "AuraToolsExp.career_1.summer_cool" })
if ($official.Count -ne 1 -or $official[0].Path -notlike "$(Join-Path $auraToolsModRoot '*')") {
    throw "Official career_1 summer skin must be published exactly once by AuraToolsExp."
}
if ((Read-JsonFile (Join-Path $auraToolsModRoot "SharedResources\Skins\package.json")).participantKind -ne "Tool") {
    throw "AuraToolsExp skin package must declare Tool participantKind."
}

Write-Host "AuraSkinShared validation passed. Packages: $($packagePaths.Count); installable skins: $($installedSources.Count); identities: $($seenIdentities.Count)."
