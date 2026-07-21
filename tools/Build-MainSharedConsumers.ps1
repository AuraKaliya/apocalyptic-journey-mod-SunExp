param(
    [string]$Configuration = "Release",
    [string]$ManagedPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    $ManagedPath = Join-Path $repoRoot "Managed"
}

$projects = @(
    "Terrias-Dev\Terrias.Dll.csproj",
    "SanGuoShaExp-Dev\SanGuoShaExp.Dll.csproj",
    "AuraToolsExp-Dev\AuraToolsExp.Dll.csproj"
)

foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project
    Write-Host "Building main shared runtime consumer: $project"
    dotnet build $projectPath -c $Configuration /p:ManagedPath="$ManagedPath" /v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Main shared runtime consumer build failed: $project"
    }
}

Write-Host "Main shared runtime consumers built successfully: $($projects.Count) projects."
