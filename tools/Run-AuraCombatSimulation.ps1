param(
    [Parameter(Mandatory = $true)]
    [string]$Ruleset,

    [Parameter(Mandatory = $true)]
    [string]$Scenario,

    [string]$Output = "",

    [ValidateRange(1, 1000000)]
    [int]$Count = 1,

    [ValidateRange(1, 256)]
    [int]$Parallel = 1,

    [ValidateSet("greedy", "first", "chance-puct")]
    [string]$Policy = "greedy",

    [Nullable[UInt64]]$SeedStart
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "AuraCombatSimulation.Cli\AuraCombatSimulation.Cli.csproj"
$arguments = @(
    "run",
    "--project", $project,
    "-c", "Release",
    "--",
    "--ruleset", (Resolve-Path -LiteralPath $Ruleset).Path,
    "--scenario", (Resolve-Path -LiteralPath $Scenario).Path,
    "--count", $Count,
    "--parallel", $Parallel,
    "--policy", $Policy
)
if (-not [string]::IsNullOrWhiteSpace($Output)) {
    $arguments += @("--output", [IO.Path]::GetFullPath($Output))
}
if ($null -ne $SeedStart) {
    $arguments += @("--seed-start", $SeedStart.Value)
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Aura combat simulation failed with exit code $LASTEXITCODE."
}
