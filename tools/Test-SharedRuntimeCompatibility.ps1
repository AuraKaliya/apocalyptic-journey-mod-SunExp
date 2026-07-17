param(
    [string]$Configuration = "Release",
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

$baseline = Get-Content -Raw -LiteralPath $baselinePath | ConvertFrom-Json
if ($baseline.schemaVersion -ne 1) {
    throw "Unsupported shared runtime compatibility schemaVersion: $($baseline.schemaVersion)"
}

foreach ($contract in $baseline.sourceContracts) {
    $directory = Join-Path $repoRoot $contract.directory
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        throw "Compatibility source directory is missing: $($contract.directory)"
    }

    $files = @(Get-ChildItem -LiteralPath $directory -Recurse -Filter "*.cs" -File | Sort-Object FullName)
    if ($files.Count -eq 0) {
        throw "Compatibility source directory has no C# files: $($contract.directory)"
    }

    $sourceText = (($files | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join [Environment]::NewLine)
    foreach ($snippet in $contract.requiredSnippets) {
        if (-not $sourceText.Contains([string]$snippet)) {
            throw "Shared source contract '$($contract.name)' is missing: $snippet"
        }
    }
}

dotnet build $sharedProject -c $Configuration /p:ManagedPath="$managedPath" /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Aura.Shared build failed before compatibility verification."
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
