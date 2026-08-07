param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root (
    "AuraCombatSimulation.Cli\AuraCombatSimulation.Cli.csproj")
$rules = Join-Path $root (
    "docs\AuraCombatAI\examples\simulation-ruleset.example.json")
$scenario = Join-Path $root (
    "docs\AuraCombatAI\examples\simulation-scenario.example.json")
$output = Join-Path ([IO.Path]::GetTempPath()) (
    "aura-combat-simulation-acceptance-" + [Guid]::NewGuid().ToString("N") `
        + ".json")

try {
    & dotnet run --project $project -c $Configuration -- `
        --ruleset $rules `
        --scenario $scenario `
        --output $output `
        --count 4 `
        --parallel 2 `
        --policy risk-puct
    if ($LASTEXITCODE -ne 0) {
        throw "Aura combat simulation acceptance failed with exit code $LASTEXITCODE."
    }

    $result = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    if ($result.Statistics.CompletedSimulations -ne 4 `
            -or $result.Statistics.Invalid -ne 0 `
            -or $result.Statistics.AuthoritativeSimulations -ne 4 `
            -or $result.Results[0].FinalStateHash -ne "6eb962488d6833d1" `
            -or [string]::IsNullOrWhiteSpace($result.RulesetHash)) {
        throw "Aura combat simulation acceptance result is invalid."
    }

    Write-Host "Aura combat simulation acceptance passed."
} finally {
    if (Test-Path -LiteralPath $output -PathType Leaf) {
        Remove-Item -LiteralPath $output -Force
    }
}
