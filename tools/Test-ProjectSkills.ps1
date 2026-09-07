param(
    [string]$PythonPath = "",
    [string]$RepoRoot = "",
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
if ([string]::IsNullOrWhiteSpace($PythonPath)) {
    $localPython = Join-Path $RepoRoot ".venv/skills/Scripts/python.exe"
    if (Test-Path -LiteralPath $localPython -PathType Leaf) {
        $PythonPath = $localPython
    }
    else {
        $command = Get-Command python -ErrorAction SilentlyContinue
        if ($null -eq $command) {
            throw "Python is unavailable. Install Python 3.10+ and follow the Aura skill-evolution validation guide."
        }
        $PythonPath = $command.Source
    }
}

& $PythonPath -B -c "import sys; import yaml; sys.exit(0 if sys.version_info >= (3, 10) else 1)" 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "Skill tooling requires Python 3.10+ and PyYAML. Run: python -m venv .venv/skills; then .venv/skills/Scripts/python.exe -m pip install -r tools/requirements-skills.txt"
}
$arguments = @("-B", "-X", "utf8", (Join-Path $PSScriptRoot "validate_project_skills.py"), "--repo-root", $RepoRoot)
if ($AsJson) { $arguments += "--json" }
& $PythonPath @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Project skill validation failed."
}
