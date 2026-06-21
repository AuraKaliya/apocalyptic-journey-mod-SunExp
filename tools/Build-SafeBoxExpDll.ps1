param(
    [string]$Configuration = "Release",
    [string]$GamePath = "D:\Steam\steamapps\common\Witch's Apocalyptic Journey"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "TestMods\SafeBoxExp-Dev\SafeBoxExp.Dll.csproj"
$repoManaged = Join-Path $repoRoot "Managed"
$gameManaged = Join-Path $GamePath "Witch's Apocalyptic Journey_Data\Managed"

if (Test-Path $gameManaged) {
    $dllPath = $gameManaged
} elseif (Test-Path $repoManaged) {
    Write-Warning "Game managed DLL path not found: $gameManaged. Falling back to repository Managed folder."
    $dllPath = $repoManaged
} else {
    throw "No managed DLL folder found. Checked game path '$gameManaged' and repository path '$repoManaged'."
}

dotnet build $project -c $Configuration /p:GamePath="$GamePath" /p:DllPath="$dllPath" /v:minimal
