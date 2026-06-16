param(
    [string]$Configuration = "Release",
    [string]$GamePath = "D:\Steam\steamapps\common\Witch's Apocalyptic Journey"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "SafeBoxExp-Dev\SafeBoxExp.Dll.csproj"

dotnet build $project -c $Configuration /p:GamePath="$GamePath" /v:minimal
