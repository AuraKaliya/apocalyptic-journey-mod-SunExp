param(
    [string]$Configuration = "Release",
    [string]$ManagedPath = "",
    [string]$GamePath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "LogExp-Dev\LogExp.Dll.csproj"

if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    if ([string]::IsNullOrWhiteSpace($GamePath)) {
        $ManagedPath = Join-Path $repoRoot "Managed"
    }
    else {
        $ManagedPath = Join-Path $GamePath "Witch's Apocalyptic Journey_Data\Managed"
    }
}

dotnet build $project -c $Configuration /p:ManagedPath="$ManagedPath" /v:minimal
