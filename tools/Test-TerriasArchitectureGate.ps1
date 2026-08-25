param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "tools\TerriasArchitectureGate\TerriasArchitectureGate.csproj"
$fixtures = Join-Path $repoRoot "tools\TerriasArchitectureGate\Fixtures"
$rules = Join-Path $fixtures "rules.json"
$exceptions = Join-Path $fixtures "empty-exceptions.json"

dotnet build $project -c Release /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Terrias architecture gate tool build failed."
}

function Invoke-Fixture {
    param(
        [string]$Name,
        [bool]$ShouldPass,
        [string]$Expected
    )

    $output = & dotnet run --project $project -c Release --no-build -- `
        --repo-root (Join-Path $fixtures $Name) `
        --rules $rules `
        --exceptions $exceptions `
        2>&1
    $exitCode = $LASTEXITCODE
    $text = [string]::Join("`n", @($output))
    if ($ShouldPass -and $exitCode -ne 0) {
        throw "Architecture fixture should pass: $Name`n$text"
    }
    if (-not $ShouldPass -and $exitCode -eq 0) {
        throw "Architecture fixture should fail: $Name"
    }
    if (-not [string]::IsNullOrWhiteSpace($Expected) -and -not $text.Contains($Expected)) {
        throw "Architecture fixture did not report '$Expected': $Name`n$text"
    }
}

Invoke-Fixture -Name "compliant" -ShouldPass $true -Expected "passed"
Invoke-Fixture -Name "alias" -ShouldPass $false -Expected "GameApi -> Mechanics"
Invoke-Fixture -Name "fully-qualified" -ShouldPass $false -Expected "GameApi -> Mechanics"
Invoke-Fixture -Name "signature" -ShouldPass $false -Expected "GameApi -> Mechanics"
Invoke-Fixture -Name "cycle" -ShouldPass $false -Expected "dependency-cycle"

$global:LASTEXITCODE = 0
Write-Host "Terrias architecture semantic fixtures passed."
