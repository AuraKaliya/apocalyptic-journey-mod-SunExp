param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}

$skinModRoot = Join-Path $RepoRoot "TestMods\SkinExp"
$skinDevRoot = Join-Path $RepoRoot "TestMods\SkinExp-Dev"
$required = @(
    (Join-Path $skinModRoot "ModConfig.json"),
    (Join-Path $skinModRoot "Icon.png"),
    (Join-Path $skinModRoot "Scripts\Entry.dll"),
    (Join-Path $skinModRoot "SharedResources\Skins\package.json"),
    (Join-Path $skinDevRoot "Entry.cs"),
    (Join-Path $skinDevRoot "SkinExp.Dll.csproj")
)
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing archived SkinExp artifact: $path"
    }
}

$config = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $skinModRoot "ModConfig.json") | ConvertFrom-Json
if ($config.ModName -ne "SkinExp" -or $config.MustSame -ne $false) {
    throw "SkinExp ModConfig identity or local-cosmetic policy is invalid."
}

$projectText = Get-Content -Raw -LiteralPath (Join-Path $skinDevRoot "SkinExp.Dll.csproj")
if (-not $projectText.Contains("AuraSharedRuntime-Dev\Aura.Shared.csproj")) {
    throw "SkinExp prototype must reference the shared runtime project."
}

$entryText = Get-Content -Raw -LiteralPath (Join-Path $skinDevRoot "Entry.cs")
if (-not $entryText.Contains("AuraSkinRuntime.Initialize") -or
    -not $entryText.Contains("AuraSkinRuntime.RegisterPackage")) {
    throw "SkinExp prototype must initialize AuraSkinShared and register its archived package."
}

Import-Module (Join-Path $RepoRoot "tools\modules\SkinPackageValidation.psm1") -Force
$validation = Test-SkinPackageContent -PackagePath (Join-Path $skinModRoot "SharedResources\Skins\package.json")
if ($validation.Package.packageId -ne "SkinExp.BundledSkins" -or $validation.Skins.Count -eq 0) {
    throw "SkinExp archived skin package identity or content is invalid."
}

Write-Host "Archived SkinExp validation passed: $($validation.Skins.Count) skin(s)."
