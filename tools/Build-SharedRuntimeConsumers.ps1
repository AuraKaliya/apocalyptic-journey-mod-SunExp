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
    "SunExp-Dev\SunExp.Dll.csproj",
    "AuraToolsExp-Dev\AuraToolsExp.Dll.csproj",
    "TestMods\SkinExp-Dev\SkinExp.Dll.csproj",
    "SanGuoShaExp-Dev\SanGuoShaExp.Dll.csproj",
    "TestMods\BackgroundAudioReplaceExp-Dev\BackgroundAudioReplaceExp.Dll.csproj",
    "TestMods\CardUseCialloExp-Dev\CardUseCialloExp.Dll.csproj"
)

foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project
    Write-Host "Building shared runtime consumer: $project"
    dotnet build $projectPath -c $Configuration /p:ManagedPath="$ManagedPath" /v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Shared runtime consumer build failed: $project"
    }
}

Write-Host "Shared runtime consumers built successfully: $($projects.Count) projects."
