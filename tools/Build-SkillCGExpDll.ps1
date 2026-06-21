param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "TestMods\SkillCGExp-Dev\SkillCGExp.Dll.csproj"

dotnet build $project -c $Configuration /v:minimal
