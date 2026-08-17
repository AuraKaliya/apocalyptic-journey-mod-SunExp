param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [switch]$Capture
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$sharedProject = Join-Path $repoRoot "AuraSharedRuntime-Dev\Aura.Shared.csproj"
$compatibilityProject = Join-Path $repoRoot "AuraSharedCompatibility.Tests\AuraSharedCompatibility.Tests.csproj"
$baselinePath = Join-Path $repoRoot "tools\shared-runtime-compatibility-baseline.json"
$managedPath = Join-Path $repoRoot "Managed"
$sharedDll = Join-Path $repoRoot "AuraSharedRuntime-Dev\bin\$Configuration\net472\Aura.Shared.dll"

foreach ($path in @($sharedProject, $compatibilityProject, $baselinePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Shared runtime compatibility input is missing: $path"
    }
}

[xml]$projectXml = Get-Content -Raw -LiteralPath $sharedProject
$compileIncludes = @($projectXml.Project.ItemGroup.Compile | ForEach-Object { [string]$_.Include })
foreach ($directory in @("AudioArbiterShared", "BattleBgmArbiterShared", "StarterDeckArbiterShared")) {
    $expected = "..\$directory\*.cs"
    if ($compileIncludes -notcontains $expected) {
        throw "Aura.Shared must compile $directory through the directory glob: $expected"
    }

    $singleFileIncludes = @($compileIncludes | Where-Object {
        $_ -like "..\$directory\*.cs" -and $_ -ne $expected
    })
    if ($singleFileIncludes.Count -gt 0) {
        throw "Aura.Shared still has file-specific compile entries for ${directory}: $($singleFileIncludes -join ', ')"
    }
}

$toolingCompile = "..\AuraToolingShared\*.cs"
if ($compileIncludes -notcontains $toolingCompile) {
    throw "Aura.Shared must compile the public tooling protocol through: $toolingCompile"
}

$baseline = Get-Content -Raw -LiteralPath $baselinePath | ConvertFrom-Json
if ($baseline.schemaVersion -ne 1) {
    throw "Unsupported shared runtime compatibility schemaVersion: $($baseline.schemaVersion)"
}

if (-not $SkipBuild) {
    & (Join-Path $repoRoot "tools\Build-AuraSharedRuntime.ps1") `
        -Configuration $Configuration `
        -ManagedPath $managedPath
}

$testArguments = @($sharedDll, $baselinePath)
if ($Capture) {
    $testArguments += "--capture"
}

dotnet run --project $compatibilityProject -c $Configuration -- @testArguments
if ($LASTEXITCODE -ne 0) {
    throw "Aura.Shared compatibility verification failed."
}

Write-Host "Shared runtime compatibility gate passed."
