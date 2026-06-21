param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}

$modRoot = Join-Path $RepoRoot "SkinExp"
$required = @(
    (Join-Path $modRoot "ModConfig.json"),
    (Join-Path $modRoot "Icon.png"),
    (Join-Path $modRoot "Scripts\Entry.dll"),
    (Join-Path $modRoot "skin.schema.json"),
    (Join-Path $modRoot "character.schema.json"),
    (Join-Path $modRoot "Skins\README.md")
)

foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing required SkinExp artifact: $path"
    }
}

$config = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $modRoot "ModConfig.json") | ConvertFrom-Json
if ($config.ModName -ne "SkinExp") {
    throw "SkinExp ModConfig.ModName is invalid."
}
if ($config.MustSame -ne $false) {
    throw "SkinExp must remain a local cosmetic mod (MustSame=false)."
}

$script:seenIds = @{}
$script:legacyCount = 0
$script:structuredCount = 0

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

function Test-IsUnsignedNumber([string]$value) {
    return -not [string]::IsNullOrWhiteSpace($value) -and $value -match '^\d+$'
}

function Assert-ValidCareerId([string]$careerId, [string]$path) {
    if (Test-IsUnsignedNumber $careerId) {
        throw "Official career id '$careerId' in $path must use runtime id 'career_$careerId'."
    }
}

function Test-SkinManifest([string]$path, $manifest, [string]$inheritedCareerId, [int]$expectedSchemaVersion) {
    if ($manifest.enabled -eq $false) {
        return
    }
    if ($manifest.schemaVersion -ne $expectedSchemaVersion) {
        throw "Expected schemaVersion $expectedSchemaVersion in $path"
    }
    if ([string]::IsNullOrWhiteSpace($manifest.skinId)) {
        throw "Missing skinId in $path"
    }

    $targetCareerId = [string]$manifest.targetCareerId
    if ([string]::IsNullOrWhiteSpace($targetCareerId)) {
        $targetCareerId = $inheritedCareerId
    }
    elseif (-not [string]::IsNullOrWhiteSpace($inheritedCareerId) -and
            -not $targetCareerId.Equals($inheritedCareerId, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "targetCareerId in $path differs from its character.json"
    }
    if ([string]::IsNullOrWhiteSpace($targetCareerId)) {
        throw "Missing targetCareerId in $path"
    }
    Assert-ValidCareerId $targetCareerId $path

    $key = $manifest.skinId.ToLowerInvariant()
    if ($script:seenIds.ContainsKey($key)) {
        throw "Duplicate skinId '$($manifest.skinId)' in $path and $($script:seenIds[$key])"
    }
    $script:seenIds[$key] = $path

    if ($null -eq $manifest.assets) {
        throw "Missing assets in $path"
    }

    $manifestDirectory = Split-Path -Parent $path
    $validAssets = 0
    foreach ($field in @("CareerImage", "Avatar", "Character", "DollIcon", "ChoiceIcon", "Animation")) {
        $configuredPath = [string]$manifest.assets.$field
        if ([string]::IsNullOrWhiteSpace($configuredPath)) {
            continue
        }

        $candidate = [System.IO.Path]::GetFullPath((Join-Path $manifestDirectory $configuredPath))
        if (-not (Test-IsInside $candidate $manifestDirectory)) {
            throw "Asset '$configuredPath' escapes its skin directory in $path"
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
            throw "Missing asset '$configuredPath' declared by $path"
        }
        $validAssets++
    }

    if ($validAssets -eq 0) {
        throw "No valid assets declared by $path"
    }
}

$legacyManifests = Get-ChildItem -LiteralPath $RepoRoot -Recurse -File -Filter "*.skin.json"
foreach ($file in $legacyManifests) {
    $manifest = Read-JsonFile $file.FullName
    Test-SkinManifest $file.FullName $manifest "" 1
    if ($manifest.enabled -ne $false) {
        $script:legacyCount++
    }
}

$characterManifests = Get-ChildItem -LiteralPath $RepoRoot -Recurse -File -Filter "character.json" | Where-Object {
    $_.Directory.Parent -ne $null -and $_.Directory.Parent.Name -eq "Skins"
}
foreach ($file in $characterManifests) {
    $character = Read-JsonFile $file.FullName
    if ($character.enabled -eq $false) {
        continue
    }
    if ($character.schemaVersion -ne 2) {
        throw "Expected character schemaVersion 2 in $($file.FullName)"
    }

    $targetCareerId = [string]$character.targetCareerId
    if ([string]::IsNullOrWhiteSpace($targetCareerId)) {
        $targetCareerId = $file.Directory.Name
    }
    if ([string]::IsNullOrWhiteSpace($targetCareerId)) {
        throw "Missing targetCareerId in $($file.FullName)"
    }
    Assert-ValidCareerId $targetCareerId $file.FullName

    foreach ($skinDirectory in Get-ChildItem -LiteralPath $file.Directory.FullName -Directory) {
        $skinManifestPath = Join-Path $skinDirectory.FullName "skin.json"
        if (-not (Test-Path -LiteralPath $skinManifestPath -PathType Leaf)) {
            continue
        }
        $skin = Read-JsonFile $skinManifestPath
        Test-SkinManifest $skinManifestPath $skin $targetCareerId 2
        if ($skin.enabled -ne $false) {
            $script:structuredCount++
        }
    }
}

Write-Host "SkinExp validation passed. Structured skins: $script:structuredCount; legacy skins: $script:legacyCount."
