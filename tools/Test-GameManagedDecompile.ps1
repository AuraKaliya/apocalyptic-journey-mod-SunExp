[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SnapshotPath,
    [Parameter(Mandatory)][string]$DecompilePath,
    [switch]$AllowPartial
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$SnapshotPath = (Resolve-Path -LiteralPath $SnapshotPath).Path
$DecompilePath = (Resolve-Path -LiteralPath $DecompilePath).Path
$snapshotManifestPath = Join-Path $SnapshotPath "managed.manifest.json"
$snapshotManifest = Get-Content -LiteralPath $snapshotManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$decompileManifest = Get-Content -LiteralPath (Join-Path $DecompilePath "decompile.manifest.json") -Raw -Encoding UTF8 | ConvertFrom-Json

if (-not [bool]$snapshotManifest.complete -and -not $AllowPartial) {
    throw "The Managed snapshot is partial. Pass -AllowPartial only when this is deliberate."
}
if ([int]$decompileManifest.inputAssemblyCount -ne [int]$snapshotManifest.assemblyCount) {
    throw "Input count mismatch between snapshot and decompile manifests."
}
$snapshotManifestHash = (Get-FileHash -LiteralPath $snapshotManifestPath -Algorithm SHA256).Hash
if ([string]$decompileManifest.sourceManifestSha256 -ne $snapshotManifestHash) {
    throw "The decompile manifest does not reference the current snapshot manifest fingerprint."
}
if (@($snapshotManifest.metadataFailures).Count -gt 0) {
    throw "The snapshot contains unreadable CLR metadata."
}
if ([int]$decompileManifest.succeededCount -ne [int]$snapshotManifest.assemblyCount -or [int]$decompileManifest.failedCount -ne 0) {
    throw "Not every snapshot assembly produced a successful project."
}

$expectedDirectories = @($snapshotManifest.assemblies | ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension([string]$_.fileName) })
$actualDirectories = @(Get-ChildItem -LiteralPath $DecompilePath -Directory | Where-Object Name -ne "_logs" | Select-Object -ExpandProperty Name)
$missingDirectories = @($expectedDirectories | Where-Object { $_ -notin $actualDirectories })
$unexpectedDirectories = @($actualDirectories | Where-Object { $_ -notin $expectedDirectories })
if ($missingDirectories.Count -gt 0 -or $unexpectedDirectories.Count -gt 0) {
    throw "Assembly output set mismatch. missing=$($missingDirectories -join ','), unexpected=$($unexpectedDirectories -join ',')"
}

foreach ($result in $decompileManifest.results) {
    $projectPath = Join-Path $DecompilePath ([string]$result.outputDirectory)
    if (@(Get-ChildItem -LiteralPath $projectPath -Filter "*.csproj" -File).Count -ne 1) {
        throw "Expected one project file for $($result.assembly)."
    }
}

$requiredFiles = @(
    "AllScripts\AllScripts\AllScripts.cs",
    "Witch\ScriptExecutor.cs",
    "Witch\VisualScriptExecutor.cs",
    "Witch\FightManager.cs",
    "Witch\FightPlayer.cs",
    "Witch\StatusManager.cs",
    "Witch\CommonCardItem.cs",
    "Witch\AttackCardItem.cs",
    "Witch\Witch\NormalMapManager.cs",
    "Witch\Witch\UI\Window\MapSelectUI.cs",
    "Witch.Core\HealData.cs",
    "Witch.Core\TrueData.cs",
    "Live2D.Cubism\Live2D\Cubism\Core\CubismModel.cs"
)
foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $DecompilePath $relativePath) -PathType Leaf)) {
        throw "High-frequency decompile reference is missing: $relativePath"
    }
}

$contentChecks = @(
    @{ Path = "Witch\ScriptExecutor.cs"; Pattern = "class ScriptExecutor" },
    @{ Path = "Witch\FightManager.cs"; Pattern = "public void ReadyToStart\(\)" },
    @{ Path = "Witch\StatusManager.cs"; Pattern = "DamageCalculate\(int BaseDamage, bool IsTrue = false\)" },
    @{ Path = "Witch.Core\HealData.cs"; Pattern = "class HealData" },
    @{ Path = "Witch.Core\TrueData.cs"; Pattern = "class TrueData" }
)
foreach ($check in $contentChecks) {
    $path = Join-Path $DecompilePath $check.Path
    if (-not (Select-String -LiteralPath $path -Pattern $check.Pattern -Quiet)) {
        throw "Expected decompiled signature was not found: $($check.Pattern) in $($check.Path)"
    }
}

$projectCount = @(Get-ChildItem -LiteralPath $DecompilePath -Recurse -Filter "*.csproj" -File).Count
$sourceFileCount = @(Get-ChildItem -LiteralPath $DecompilePath -Recurse -Filter "*.cs" -File).Count
Write-Host "Game Managed decompile gate passed: assemblies=$($snapshotManifest.assemblyCount), projects=$projectCount, C# files=$sourceFileCount, completeGameInput=$($snapshotManifest.complete)"
