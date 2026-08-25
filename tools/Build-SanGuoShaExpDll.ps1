param(
    [string]$Configuration = "Release",
    [string]$ManagedPath = "",
    [string]$GamePath = "D:\Steam\steamapps\common\Witch's Apocalyptic Journey"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "TestMods\SanGuoShaExp-Dev\SanGuoShaExp.Dll.csproj"

if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    $ManagedPath = if ([string]::IsNullOrWhiteSpace($GamePath)) {
        Join-Path $repoRoot "Managed"
    }
    else {
        Join-Path $GamePath "Witch's Apocalyptic Journey_Data\Managed"
    }
}

dotnet build $project -c $Configuration /p:ManagedPath="$ManagedPath" /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Archived SanGuoShaExp build failed."
}
