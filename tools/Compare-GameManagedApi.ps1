[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaselinePath,
    [Parameter(Mandatory)][string]$CurrentPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string[]]$Assemblies = @()
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$BaselinePath = (Resolve-Path -LiteralPath $BaselinePath).Path
$CurrentPath = (Resolve-Path -LiteralPath $CurrentPath).Path
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$project = Join-Path $repoRoot "tools\GameManagedInspector\GameManagedInspector.csproj"
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

dotnet build $project -c Release --nologo | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "GameManagedInspector build failed."
}

$arguments = @(
    "run", "--project", $project, "-c", "Release", "--no-build", "--",
    "compare",
    "--baseline", $BaselinePath,
    "--current", $CurrentPath,
    "--output", (Join-Path $OutputDirectory "api-diff.json"),
    "--markdown", (Join-Path $OutputDirectory "api-diff.md")
)
foreach ($assembly in $Assemblies) {
    $arguments += @("--assembly", $assembly)
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "GameManagedInspector compare failed with exit code $LASTEXITCODE."
}

Write-Host "API comparison report: $(Join-Path $OutputDirectory 'api-diff.md')"
