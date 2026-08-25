param(
    [string]$Configuration = "Release",
    [string]$ManagedPath = "",
    [string]$GamePath = "",
    [switch]$ContinueOnError
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

# Edit this list to add, remove, replace, or reorder mod build steps.
$buildScripts = @(
    "Build-MainSharedConsumers.ps1"
)

function Resolve-BuildScriptPath {
    param([string]$ScriptName)

    if ([System.IO.Path]::IsPathRooted($ScriptName)) {
        return $ScriptName
    }

    return Join-Path $PSScriptRoot $ScriptName
}

function Get-ScriptArguments {
    param(
        [string]$ScriptPath,
        [string]$Configuration,
        [string]$ManagedPath,
        [string]$GamePath
    )

    $tokens = $null
    $parseErrors = $null
    $scriptAst = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) {
        throw "Build script has parse errors: $ScriptPath"
    }

    $parameters = @{}
    if ($null -ne $scriptAst.ParamBlock) {
        foreach ($parameter in $scriptAst.ParamBlock.Parameters) {
            $parameters[$parameter.Name.VariablePath.UserPath] = $true
        }
    }

    $arguments = @{}

    if ($parameters.ContainsKey("Configuration")) {
        $arguments["Configuration"] = $Configuration
    }

    if ($parameters.ContainsKey("ManagedPath") -and -not [string]::IsNullOrWhiteSpace($ManagedPath)) {
        $arguments["ManagedPath"] = $ManagedPath
    }

    if ($parameters.ContainsKey("GamePath") -and -not [string]::IsNullOrWhiteSpace($GamePath)) {
        $arguments["GamePath"] = $GamePath
    }

    return $arguments
}

$failures = [System.Collections.Generic.List[string]]::new()

Write-Host "Building listed Exp DLL scripts from $repoRoot"
Write-Host "Build script list:"
foreach ($scriptName in $buildScripts) {
    Write-Host " - $scriptName"
}

for ($index = 0; $index -lt $buildScripts.Count; $index++) {
    $scriptName = $buildScripts[$index]
    $scriptPath = Resolve-BuildScriptPath -ScriptName $scriptName

    try {
        if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
            throw "Build script is missing: $scriptPath"
        }

        $arguments = Get-ScriptArguments -ScriptPath $scriptPath -Configuration $Configuration -ManagedPath $ManagedPath -GamePath $GamePath

        Write-Host ""
        Write-Host "[$($index + 1)/$($buildScripts.Count)] Running $scriptName"

        $global:LASTEXITCODE = 0
        & $scriptPath @arguments

        if ($LASTEXITCODE -ne 0) {
            throw "Build script exited with code ${LASTEXITCODE}: $scriptName"
        }

        Write-Host "Finished $scriptName"
    }
    catch {
        $message = "$scriptName failed: $($_.Exception.Message)"
        $failures.Add($message)
        Write-Host $message -ForegroundColor Red

        if (-not $ContinueOnError) {
            throw
        }
    }
}

if ($failures.Count -gt 0) {
    throw "One or more build scripts failed: $($failures -join '; ')"
}

Write-Host ""
Write-Host "All listed Exp DLL build scripts completed successfully."
