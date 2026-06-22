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
    "TestMods\SkinExp-Dev\SkinExp.Dll.csproj",
    "TestMods\BackgroundAudioReplaceExp-Dev\BackgroundAudioReplaceExp.Dll.csproj",
    "TestMods\CardUseCialloExp-Dev\CardUseCialloExp.Dll.csproj",
    "TestMods\ChatExp-Dev\ChatExp.Dll.csproj"
)

foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project
    Write-Host "Building test shared runtime consumer: $project"
    dotnet build $projectPath -c $Configuration /p:ManagedPath="$ManagedPath" /v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Test shared runtime consumer build failed: $project"
    }
}

Write-Host "Test shared runtime consumers built successfully: $($projects.Count) projects."
