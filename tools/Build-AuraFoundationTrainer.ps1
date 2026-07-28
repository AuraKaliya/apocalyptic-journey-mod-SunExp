param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "AuraFoundationTrainer.Worker\AuraFoundationTrainer.Worker.csproj"
$controlCenterProject = Join-Path $repoRoot "AuraFoundationTrainer.ControlCenter\AuraFoundationTrainer.ControlCenter.csproj"
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "AuraToolsExp\TrainingWorker"
}
$resolvedRepoRoot = [System.IO.Path]::GetFullPath($repoRoot)
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
$expectedRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "AuraToolsExp\TrainingWorker"))
if (-not $resolvedOutput.StartsWith($expectedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Foundation trainer output must stay inside $expectedRoot"
}

New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null
& dotnet publish $project `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    --nologo `
    -v:minimal `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -o $resolvedOutput
if ($LASTEXITCODE -ne 0) {
    throw "Aura foundation trainer publish failed with exit code $LASTEXITCODE"
}

& dotnet publish $controlCenterProject `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    --nologo `
    -v:minimal `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -o $resolvedOutput
if ($LASTEXITCODE -ne 0) {
    throw "Aura foundation trainer control center publish failed with exit code $LASTEXITCODE"
}

$worker = Join-Path $resolvedOutput "AuraFoundationTrainer.Worker.exe"
if (-not (Test-Path -LiteralPath $worker -PathType Leaf)) {
    throw "Published foundation trainer is missing: $worker"
}
$controlCenter = Join-Path $resolvedOutput "AuraFoundationTrainer.ControlCenter.exe"
if (-not (Test-Path -LiteralPath $controlCenter -PathType Leaf)) {
    throw "Published foundation trainer control center is missing: $controlCenter"
}
Write-Host "Aura foundation trainer published: $worker"
Write-Host "Aura foundation trainer control center published: $controlCenter"
