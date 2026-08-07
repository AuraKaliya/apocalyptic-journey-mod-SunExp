Set-StrictMode -Version Latest

function Get-MatrixValues {
    param(
        [AllowNull()][object]$Object,
        [Parameter(Mandatory)][string]$Name
    )

    if ($null -eq $Object) {
        return @()
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        return @()
    }
    return @($property.Value)
}

function Test-MatrixValue {
    param(
        [object[]]$Values,
        [string]$Expected
    )

    return @($Values | Where-Object {
        ([string]$_).Equals($Expected, [System.StringComparison]::OrdinalIgnoreCase)
    }).Count -gt 0
}

function Convert-MatrixArguments {
    param([AllowNull()][object]$Arguments)

    $result = @{}
    if ($null -eq $Arguments) {
        return $result
    }
    foreach ($property in $Arguments.PSObject.Properties) {
        $result[$property.Name] = $property.Value
    }
    return $result
}

function Invoke-TestMatrix {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$MatrixPath,
        [string]$Configuration = "Release",
        [string]$Profile = "",
        [string[]]$Tag = @(),
        [string[]]$StepId = @(),
        [switch]$List
    )

    $resolvedRepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
    $resolvedMatrixPath = [System.IO.Path]::GetFullPath($MatrixPath)
    if (-not (Test-Path -LiteralPath $resolvedMatrixPath -PathType Leaf)) {
        throw "Test matrix is missing: $resolvedMatrixPath"
    }

    $matrix = Get-Content -Raw -LiteralPath $resolvedMatrixPath | ConvertFrom-Json
    if ($matrix.schemaVersion -ne 2) {
        throw "Unsupported test matrix schemaVersion: $($matrix.schemaVersion)"
    }

    $enabledSteps = @($matrix.steps | Where-Object {
        $enabledProperty = $_.PSObject.Properties["enabled"]
        $null -eq $enabledProperty -or $enabledProperty.Value -ne $false
    })
    $stepIds = @($enabledSteps | ForEach-Object { [string]$_.id })
    $duplicateIds = @($stepIds | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
    if ($duplicateIds.Count -gt 0) {
        throw "Duplicate test matrix step id(s): $($duplicateIds -join ', ')"
    }
    foreach ($step in $enabledSteps) {
        foreach ($requiredProperty in @("id", "kind", "path", "owner", "category", "cost")) {
            $property = $step.PSObject.Properties[$requiredProperty]
            if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
                throw "Test matrix step '$($step.id)' is missing metadata: $requiredProperty"
            }
        }
        if (@(Get-MatrixValues -Object $step -Name "impactTags").Count -eq 0 -or
            @(Get-MatrixValues -Object $step -Name "profiles").Count -eq 0) {
            throw "Test matrix step '$($step.id)' must declare impactTags and profiles."
        }
    }

    if ($List) {
        $enabledSteps | ForEach-Object {
            [pscustomobject]@{
                Id = $_.id
                Owner = $_.owner
                Category = $_.category
                Cost = $_.cost
                Profiles = (Get-MatrixValues -Object $_ -Name "profiles") -join ","
                Tags = (Get-MatrixValues -Object $_ -Name "impactTags") -join ","
            }
        } | Format-Table -AutoSize
        return
    }

    $selectedSteps = $enabledSteps
    if ($StepId.Count -gt 0) {
        $unknownIds = @($StepId | Where-Object { $stepIds -notcontains $_ })
        if ($unknownIds.Count -gt 0) {
            throw "Unknown test matrix step id(s): $($unknownIds -join ', ')"
        }
        $selectedSteps = @($selectedSteps | Where-Object { $StepId -contains $_.id })
    }
    elseif (-not [string]::IsNullOrWhiteSpace($Profile)) {
        $selectedSteps = @($selectedSteps | Where-Object {
            Test-MatrixValue -Values (Get-MatrixValues -Object $_ -Name "profiles") -Expected $Profile
        })
        if ($selectedSteps.Count -eq 0) {
            $profiles = @($enabledSteps | ForEach-Object { Get-MatrixValues -Object $_ -Name "profiles" } | Sort-Object -Unique)
            throw "Unknown or empty test profile '$Profile'. Available profiles: $($profiles -join ', ')"
        }
    }
    elseif ($Tag.Count -eq 0) {
        throw "Select a test profile, impact tag, or explicit step id."
    }

    if ($Tag.Count -gt 0) {
        $selectedSteps = @($selectedSteps | Where-Object {
            $stepTags = Get-MatrixValues -Object $_ -Name "impactTags"
            @($Tag | Where-Object { Test-MatrixValue -Values $stepTags -Expected $_ }).Count -gt 0
        })
    }
    if ($selectedSteps.Count -eq 0) {
        throw "No enabled test steps matched the requested selection."
    }

    Write-Host "Selected test matrix: $($matrix.name)"
    if ($StepId.Count -eq 0 -and -not [string]::IsNullOrWhiteSpace($Profile)) {
        Write-Host "Profile: $Profile"
    }
    if ($Tag.Count -gt 0) {
        Write-Host "Tags: $($Tag -join ', ')"
    }
    Write-Host "Steps: $($selectedSteps.id -join ', ')"

    foreach ($step in $selectedSteps) {
        if ($step.kind -ne "script") {
            throw "Unsupported test matrix step kind: $($step.kind)"
        }

        $script = Join-Path $resolvedRepoRoot ([string]$step.path)
        $script = [System.IO.Path]::GetFullPath($script)
        $repoPrefix = $resolvedRepoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        if (-not $script.StartsWith($repoPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Test matrix step path escapes the repository: $($step.id) -> $script"
        }
        if (-not (Test-Path -LiteralPath $script -PathType Leaf)) {
            throw "Test matrix step script is missing: $($step.id) -> $script"
        }

        $argumentsProperty = $step.PSObject.Properties["arguments"]
        $arguments = Convert-MatrixArguments -Arguments $(if ($null -eq $argumentsProperty) { $null } else { $argumentsProperty.Value })
        $passConfigurationProperty = $step.PSObject.Properties["passConfiguration"]
        if ($null -ne $passConfigurationProperty -and $passConfigurationProperty.Value -eq $true) {
            $arguments["Configuration"] = $Configuration
        }

        Write-Host "Running test matrix step: $($step.id) [$($step.owner)/$($step.category)/$($step.cost)]"
        $global:LASTEXITCODE = 0
        & {
            Set-StrictMode -Off
            & $script @arguments
        }
        if ($LASTEXITCODE -ne 0) {
            throw "Test matrix step failed: $($step.id)"
        }
    }

    Write-Host "Test matrix passed: $($matrix.name); steps=$($selectedSteps.Count)."
}

Export-ModuleMember -Function Invoke-TestMatrix
