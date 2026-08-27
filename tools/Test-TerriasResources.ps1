param()

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$modRoot = Join-Path $repoRoot "Terrias"
$sharedRoot = Join-Path $modRoot "SharedResources"
$failures = [System.Collections.Generic.List[string]]::new()
Import-Module (Join-Path $repoRoot "tools\modules\SkinPackageValidation.psm1") -Force

function Add-Failure {
    param([string]$Message)
    $failures.Add($Message)
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        Add-Failure $Message
    }
}

function Add-ModResourceReference {
    param(
        [System.Collections.Generic.HashSet[string]]$References,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return
    }

    foreach ($match in [regex]::Matches($Value, 'Mods/Terrias/[^"'']+')) {
        $path = $match.Value
        try {
            $path = [regex]::Unescape($path)
        }
        catch {
            # Keep the original value; malformed escape text will fail path resolution.
        }

        $path = $path.Trim().TrimEnd(')', ']', '}', ',', ';')
        $References.Add($path) | Out-Null
    }
}

function Walk-JsonValue {
    param(
        [object]$Value,
        [scriptblock]$StringVisitor
    )

    if ($null -eq $Value) {
        return
    }
    if ($Value -is [string]) {
        & $StringVisitor ([string]$Value)
        return
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        foreach ($item in $Value) {
            Walk-JsonValue $item $StringVisitor
        }
        return
    }
    foreach ($property in $Value.PSObject.Properties) {
        Walk-JsonValue $property.Value $StringVisitor
    }
}

function Test-ModResourcePath {
    param([string]$ResourcePath)

    $relative = $ResourcePath.Substring("Mods/Terrias/".Length).Replace('/', '\')
    $candidate = Join-Path $modRoot $relative
    if (Test-Path -LiteralPath $candidate) {
        return $true
    }

    foreach ($extension in @('.png', '.jpg', '.jpeg', '.wav', '.ogg', '.mp3', '.json', '.asset', '.mat', '.prefab')) {
        if (Test-Path -LiteralPath ($candidate + $extension)) {
            return $true
        }
    }

    $leaf = [IO.Path]::GetFileName($candidate)
    $parent = [IO.Path]::GetDirectoryName($candidate)
    if ($leaf.EndsWith('_', [StringComparison]::Ordinal) -and [IO.Directory]::Exists($parent)) {
        $prefixMatch = Get-ChildItem -LiteralPath $parent -File | Where-Object {
            $_.BaseName.StartsWith($leaf, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1
        if ($prefixMatch) {
            return $true
        }
    }

    return $false
}

Push-Location $repoRoot
try {
    $modConfig = Get-Content -LiteralPath (Join-Path $modRoot "ModConfig.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ($modConfig.ModName -eq "Terrias") "ModConfig.ModName must be Terrias."
    Assert-True ($modConfig.ModAuthor -eq "Aura") "ModConfig.ModAuthor must remain Aura."
    Assert-True (("{0}.{1}" -f $modConfig.ModName, $modConfig.ModAuthor) -eq "Terrias.Aura") "The game-loader ModId must resolve to Terrias.Aura."

    [xml]$project = Get-Content -LiteralPath (Join-Path $repoRoot "Terrias-Dev\Terrias.Dll.csproj") -Raw -Encoding UTF8
    $assemblyName = @($project.Project.PropertyGroup.AssemblyName | Where-Object { $_ })[0]
    $rootNamespace = @($project.Project.PropertyGroup.RootNamespace | Where-Object { $_ })[0]
    Assert-True ($assemblyName -eq "Terrias.Aura") "Terrias assembly name must be Terrias.Aura."
    Assert-True ($rootNamespace -eq "Terrias.Dll") "Terrias root namespace must be Terrias.Dll."

    $entryDll = Join-Path $modRoot "Scripts\Entry.dll"
    Assert-True ([IO.File]::Exists($entryDll)) "Shipped Terrias/Scripts/Entry.dll is missing."
    if ([IO.File]::Exists($entryDll)) {
        Assert-True ([Reflection.AssemblyName]::GetAssemblyName($entryDll).Name -eq "Terrias.Aura") "Shipped Entry.dll does not contain the Terrias.Aura assembly."
    }

    $modReferences = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($jsonFile in Get-ChildItem -LiteralPath $modRoot -Recurse -File -Filter *.json) {
        $document = Get-Content -LiteralPath $jsonFile.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        Walk-JsonValue $document { param($value) Add-ModResourceReference $modReferences $value }
    }
    foreach ($csvFile in Get-ChildItem -LiteralPath $modRoot -Recurse -File -Filter *.csv) {
        foreach ($row in @(Import-Csv -LiteralPath $csvFile.FullName)) {
            foreach ($property in $row.PSObject.Properties) {
                Add-ModResourceReference $modReferences ([string]$property.Value)
            }
        }
    }
    foreach ($sourceFile in Get-ChildItem -LiteralPath (Join-Path $repoRoot "Terrias-Dev") -Recurse -File -Include *.cs, *.json) {
        if ($sourceFile.FullName -match '\\(?:bin|obj|UnityProject\\(?:Library|Temp|Logs|Build))\\') {
            continue
        }
        $source = Get-Content -LiteralPath $sourceFile.FullName -Raw -Encoding UTF8
        foreach ($match in [regex]::Matches($source, '["''](Mods/Terrias/[^"'']+)["'']')) {
            Add-ModResourceReference $modReferences $match.Groups[1].Value
        }
    }
    foreach ($resourcePath in $modReferences) {
        Assert-True (Test-ModResourcePath $resourcePath) "Unresolved Terrias mod resource path: $resourcePath"
    }

    $registrationPath = Join-Path $sharedRoot "aura.registration.json"
    $entrySource = Get-Content -LiteralPath (Join-Path $repoRoot "Terrias-Dev\Entry.cs") -Raw -Encoding UTF8
    Assert-True ($entrySource -notmatch 'RegisterSharedResourcePackage|cg\.retire\.registry|retire\.registration') "Terrias Entry must not actively register optional media or retirement manifests."
    $registration = Get-Content -LiteralPath $registrationPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ($registration.ownerModId -eq "Terrias") "Shared package ownerModId must be Terrias."
    Assert-True ($registration.participantKind -eq "Content") "Terrias shared package must register as Content."
    Assert-True (@($registration.resources).Count -eq 9) "Terrias must publish its nine content-owned voice/CG resources."
    Assert-True (@($registration.defaults).Count -eq 9) "Terrias content media must provide one registered default per resource."

    $registrationIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $installedResources = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($resource in $registration.resources) {
        $key = "{0}|{1}|{2}|{3}|Terrias|{4}" -f $resource.moduleId, $resource.featureId, $resource.scopeType, $resource.scopeId, $resource.resourceId
        Assert-True ($registrationIds.Add($key)) "Duplicate shared resource registration: $key"
        Assert-True ($resource.writerId -eq "Terrias") "Shared resource writerId must be Terrias: $key"
        Assert-True ($resource.scopeOwnerModId -eq "Terrias") "Shared resource scopeOwnerModId must be Terrias: $key"

        $sourcePath = Join-Path $sharedRoot ([string]$resource.source).Replace('/', '\')
        Assert-True (Test-Path -LiteralPath $sourcePath) "Shared registration source is missing: $($resource.source)"
        if (-not (Test-Path -LiteralPath $sourcePath)) {
            continue
        }

        $installRoot = "{0}/{1}/{2}/{3}/Terrias/{4}" -f $resource.moduleId, $resource.scopeType, $resource.scopeId, $resource.featureId, $resource.resourceId
        if ($resource.kind -eq "File") {
            $fileName = if ([string]::IsNullOrWhiteSpace([string]$resource.fileName)) {
                [IO.Path]::GetFileName($sourcePath)
            }
            else {
                [string]$resource.fileName
            }
            $installedResources.Add("$installRoot/$fileName") | Out-Null
        }
        elseif ($resource.kind -eq "Directory") {
            foreach ($file in Get-ChildItem -LiteralPath $sourcePath -Recurse -File) {
                $relative = $file.FullName.Substring($sourcePath.Length).TrimStart('\').Replace('\', '/')
                $installedResources.Add("$installRoot/content/$relative") | Out-Null
            }
        }
        else {
            Add-Failure "Unsupported shared registration kind '$($resource.kind)': $key"
        }
    }

    Assert-True (-not (Test-Path -LiteralPath (Join-Path $modRoot "audio.registry.json"))) "Terrias audio registry must live under SharedResources."
    $discovery = Get-Content -LiteralPath (Join-Path $sharedRoot "aura.discovery.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ([int]$discovery.schemaVersion -eq 1 -and $discovery.ownerModId -eq "Terrias") "Terrias shared discovery identity is invalid."
    Assert-True (@($discovery.contributions).Count -eq 3) "Terrias discovery must declare resource, audio, and CG contributions."
    Assert-True ((Get-Content -LiteralPath (Join-Path $modRoot "Terrias.modproj") -Raw).Trim() -eq "3741157062") "Terrias discovery source id must come from Terrias.modproj."

    $audioRegistry = Get-Content -LiteralPath (Join-Path $sharedRoot "audio.registry.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ($audioRegistry.ownerModId -eq "Terrias" -and @($audioRegistry.providers).Count -eq 9) "Terrias audio registry must own nine voice providers."
    foreach ($provider in $audioRegistry.providers) {
        Assert-True ($provider.ownerModId -eq "Terrias") "Terrias audio provider owner is invalid: $($provider.providerId)"
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$provider.displayName)) "Terrias audio provider displayName is missing: $($provider.providerId)"
        Assert-True ([string]$provider.path -match '^Shared:' -and [string]$provider.path -match '/Voice/Terrias/') "Terrias audio provider path is not content-owned: $($provider.providerId)"
    }

    $cgRegistry = Get-Content -LiteralPath (Join-Path $sharedRoot "cg.registry.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ($cgRegistry.ownerModId -eq "Terrias" -and @($cgRegistry.entries).Count -eq 7) "Terrias CG registry must own seven media entries."
    foreach ($entry in $cgRegistry.entries) {
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$entry.displayName)) "Terrias CG displayName is missing: $($entry.cgId)"
        Assert-True ([string]$entry.media.resource -match '/Terrias/') "Terrias CG resource path is not content-owned: $($entry.cgId)"
        Assert-True ($entry.defaultActivation.consumerMode -eq "toolManaged" -and $entry.defaultActivation.consumerModId -eq "AuraToolsExp") "Terrias CG must be configured by AuraToolsExp without changing ownership: $($entry.cgId)"
    }
    $roleRegistry = Get-Content -LiteralPath (Join-Path $sharedRoot "role.registry.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ($roleRegistry.ownerModId -eq "Terrias") "Role registry ownerModId must be Terrias."
    $roleIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $roleRegistry.entries) {
        Assert-True ($roleIds.Add([string]$entry.roleId)) "Duplicate role registry id: $($entry.roleId)"
        Assert-True ($entry.packBelong -eq "Terrias") "Role registry packBelong must be Terrias: $($entry.roleId)"
    }

    $localizationPath = Join-Path $modRoot "localization.registry.json"
    Assert-True ([IO.File]::Exists($localizationPath)) "Terrias localization registry is missing."
    if ([IO.File]::Exists($localizationPath)) {
        $localization = Get-Content -LiteralPath $localizationPath -Raw -Encoding UTF8 | ConvertFrom-Json
        Assert-True ([int]$localization.schemaVersion -eq 1) "Terrias localization registry schemaVersion must be 1."
        $localizationKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $requiredLocales = @('zh-Hans', 'zh-Hant', 'en', 'ja')
        foreach ($property in $localization.entries.PSObject.Properties) {
            $key = [string]$property.Name
            $entry = $property.Value
            Assert-True ($localizationKeys.Add($key)) "Duplicate localization key: $key"
            Assert-True ($key -match '^[a-z0-9_]+(?:\.[A-Za-z0-9_]+)+$') "Invalid localization key: $key"

            $referencePlaceholders = $null
            foreach ($locale in $requiredLocales) {
                $value = [string]$entry.$locale
                Assert-True (-not [string]::IsNullOrWhiteSpace($value)) "Localization '$key' is missing locale '$locale'."
                $placeholders = @([regex]::Matches($value, '\{([A-Za-z][A-Za-z0-9_]*)\}') |
                    ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
                if ($null -eq $referencePlaceholders) {
                    $referencePlaceholders = $placeholders
                }
                else {
                    Assert-True (($referencePlaceholders -join '|') -ceq ($placeholders -join '|')) `
                        "Localization '$key' has inconsistent placeholders in locale '$locale'."
                }
            }
        }

        $localizationSourceRoot = Join-Path $repoRoot "Terrias-Dev"
        $referencedLocalizationKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($sourceFile in Get-ChildItem -LiteralPath $localizationSourceRoot -Recurse -File -Filter *.cs) {
            if ($sourceFile.FullName -match '\\(?:bin|obj|UnityProject\\(?:Library|Temp|Logs|Build))\\') {
                continue
            }
            $source = Get-Content -LiteralPath $sourceFile.FullName -Raw -Encoding UTF8
            foreach ($match in [regex]::Matches($source, 'TerriasTextCatalog\.(?:Get|Format|GetForLocale|FormatForLocale)\(\s*"([^"]+)"')) {
                $key = $match.Groups[1].Value
                if (-not $key.EndsWith('.')) {
                    $referencedLocalizationKeys.Add($key) | Out-Null
                }
            }
            foreach ($match in [regex]::Matches($source, '\bL\(\s*"([^"]+)"')) {
                $referencedLocalizationKeys.Add($match.Groups[1].Value) | Out-Null
            }
        }
        foreach ($key in $referencedLocalizationKeys) {
            Assert-True ($localizationKeys.Contains($key)) "Code references missing localization key: $key"
        }
        foreach ($elementId in @('pyro', 'hydro', 'geo', 'dendro', 'electro', 'cryo', 'anemo')) {
            Assert-True ($localizationKeys.Contains("element.$elementId")) "Spirit element is missing localization key: element.$elementId"
        }

        foreach ($failureCode in @(
            'TransportNotSent', 'ProtocolMismatch', 'BattleEpochMismatch', 'CardModelMismatch',
            'RoleDeckUnavailable', 'RoleDeckTimedOut', 'UnknownRole', 'MissingSender',
            'SenderOutsideLobby', 'OwnerMismatch', 'TokenConflict', 'OwnerAlreadyHasProjection',
            'FriendlySeatsFull', 'SeatReservationExpired', 'SpawnFailed', 'Cancelled')) {
            $key = "caption.projection.failure.$failureCode"
            Assert-True ($localizationKeys.Contains($key)) "Projection failure is missing localization key: $key"
        }
    }

    $witchArchive = Get-Content -LiteralPath (Join-Path $modRoot "witch.archive.registry.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ([int]$witchArchive.schemaVersion -eq 2) "Witch Archive registry schemaVersion must be 2."
    Assert-True ($witchArchive.ownerModId -eq "Terrias") "Witch Archive registry ownerModId must be Terrias."
    $witchArchiveIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $enabledWitchArchiveEntries = @($witchArchive.entries | Where-Object { $_.enabled -ne $false })
    Assert-True ($enabledWitchArchiveEntries.Count -ge 3) "Witch Archive must ship at least the three initial Terrias witches."
    foreach ($entry in $enabledWitchArchiveEntries) {
        Assert-True ($witchArchiveIds.Add([string]$entry.id)) "Duplicate Witch Archive entry id: $($entry.id)"
        Assert-True ($roleIds.Contains([string]$entry.roleId)) "Witch Archive entry references an unknown role: $($entry.roleId)"
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$entry.avatarPath)) "Witch Archive avatar path is missing: $($entry.id)"
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$entry.portraitPath)) "Witch Archive portrait path is missing: $($entry.id)"
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$entry.name.'zh-Hans')) "Witch Archive Simplified Chinese name is missing: $($entry.id)"
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$entry.background.'zh-Hans')) "Witch Archive Simplified Chinese background is missing: $($entry.id)"

        $backgroundTextPath = [string]$entry.backgroundFiles.'zh-Hans'
        Assert-True (-not [string]::IsNullOrWhiteSpace($backgroundTextPath)) "Witch Archive Simplified Chinese background file is missing: $($entry.id)"
        if ([string]::IsNullOrWhiteSpace($backgroundTextPath)) {
            continue
        }

        $normalizedBackgroundTextPath = $backgroundTextPath.Trim().Replace('/', [IO.Path]::DirectorySeparatorChar)
        $isRelativeArchiveText = -not [IO.Path]::IsPathRooted($normalizedBackgroundTextPath)
        $isTextArchiveResource = [IO.Path]::GetExtension($normalizedBackgroundTextPath) -eq '.txt'
        Assert-True $isRelativeArchiveText "Witch Archive background file must be relative: $($entry.id)"
        Assert-True $isTextArchiveResource "Witch Archive background file must use .txt: $($entry.id)"
        if (-not $isRelativeArchiveText -or -not $isTextArchiveResource) {
            continue
        }

        try {
            $archiveRoot = [IO.Path]::GetFullPath($modRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
            $archiveTextFile = [IO.Path]::GetFullPath((Join-Path $archiveRoot $normalizedBackgroundTextPath))
            $insideModRoot = $archiveTextFile.StartsWith($archiveRoot, [StringComparison]::OrdinalIgnoreCase)
            Assert-True $insideModRoot "Witch Archive background file escapes the mod root: $($entry.id)"
            if ($insideModRoot) {
                Assert-True ([IO.File]::Exists($archiveTextFile)) "Witch Archive background file does not exist: $($entry.id)"
                if ([IO.File]::Exists($archiveTextFile)) {
                    $archiveText = Get-Content -LiteralPath $archiveTextFile -Raw -Encoding UTF8
                    Assert-True (-not [string]::IsNullOrWhiteSpace($archiveText)) "Witch Archive background file is empty: $($entry.id)"
                }
            }
        }
        catch {
            Add-Failure "Witch Archive background file path is invalid for '$($entry.id)': $($_.Exception.Message)"
        }
    }

    $skinRoot = Join-Path $sharedRoot "Skins"
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $skinRoot "package.json"))) "Terrias must not ship replacement-skin packages."
    $skinRetirementFiles = @(Get-ChildItem -LiteralPath $skinRoot -Recurse -File -ErrorAction SilentlyContinue)
    Assert-True ($skinRetirementFiles.Count -eq 0) "Completed Terrias skin retirement manifests must not remain operational."
    $visualRegistry = Get-Content -LiteralPath (Join-Path $modRoot "visual.registry.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ($visualRegistry.ownerModId -eq "Terrias") "Visual registry ownerModId must be Terrias."
    $shaderIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($shader in $visualRegistry.shaders) {
        Assert-True ($shaderIds.Add([string]$shader.id)) "Duplicate visual shader id: $($shader.id)"
    }
    foreach ($effect in $visualRegistry.effects) {
        Assert-True ($shaderIds.Contains([string]$effect.shaderId)) "Visual effect '$($effect.id)' references missing shader '$($effect.shaderId)'."
    }

    $visualBundle = Join-Path $modRoot "ModResource\VisualBundles\terrias_visuals"
    Assert-True ([IO.File]::Exists($visualBundle)) "Terrias visual bundle is missing."
    Assert-True (([IO.FileInfo]$visualBundle).Length -gt 0) "Terrias visual bundle is empty."

    if ($failures.Count -gt 0) {
        $details = $failures | ForEach-Object { " - $_" }
        throw ("Terrias resource audit failed with {0} issue(s):`n{1}" -f $failures.Count, ($details -join "`n"))
    }

    Write-Host ("Terrias resource audit passed: modRefs={0}, sharedResources={1}, roles={2}." -f `
        $modReferences.Count,
        $registration.resources.Count,
        $roleRegistry.entries.Count)
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
