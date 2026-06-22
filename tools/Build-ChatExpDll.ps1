param(
    [string]$Configuration = "Release",
    [string]$ManagedPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    $ManagedPath = Join-Path $repoRoot "Managed"
}

$project = Join-Path $repoRoot "TestMods\ChatExp-Dev\ChatExp.Dll.csproj"

Write-Host "Building ChatExp DLL..."
dotnet build $project -c $Configuration /p:ManagedPath="$ManagedPath" /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "ChatExp DLL build failed."
}

Write-Host "ChatExp DLL copied to TestMods\ChatExp\Scripts\Entry.dll"
