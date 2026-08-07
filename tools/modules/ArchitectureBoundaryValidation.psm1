Set-StrictMode -Version Latest

function Get-OptionalValues {
    param(
        [object]$Object,
        [string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        return @()
    }
    return @($property.Value)
}

function Get-RuleFiles {
    param(
        [string]$RepoRoot,
        [object]$Rule
    )

    $patternProperty = $Rule.PSObject.Properties["filePattern"]
    $filePattern = if ($null -eq $patternProperty) { "*.cs" } else { [string]$patternProperty.Value }
    $files = foreach ($relativeRoot in @($Rule.roots)) {
        $root = Join-Path $RepoRoot ([string]$relativeRoot)
        if (-not (Test-Path -LiteralPath $root -PathType Container)) {
            throw "Architecture rule root is missing: $relativeRoot"
        }
        Get-ChildItem -LiteralPath $root -Recurse -File -Filter $filePattern
    }

    $excludePatterns = Get-OptionalValues -Object $Rule -Name "excludePathRegex"
    return @($files | Sort-Object FullName -Unique | Where-Object {
        $relative = [System.IO.Path]::GetRelativePath($RepoRoot, $_.FullName).Replace("\", "/")
        -not @($excludePatterns | Where-Object { $relative -match [string]$_ }).Count
    })
}

function Get-LineNumber {
    param(
        [string]$Text,
        [int]$Index
    )

    if ($Index -le 0) {
        return 1
    }
    return ([regex]::Matches($Text.Substring(0, $Index), "\n").Count + 1)
}

function Invoke-ArchitectureBoundaryValidation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$RuleSet,
        [string]$RulesPath = ""
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($RepoRoot)
    if ([string]::IsNullOrWhiteSpace($RulesPath)) {
        $RulesPath = Join-Path $resolvedRoot "tools\architecture-boundary-rules.json"
    }
    if (-not (Test-Path -LiteralPath $RulesPath -PathType Leaf)) {
        throw "Architecture boundary rules are missing: $RulesPath"
    }

    $document = Get-Content -Raw -LiteralPath $RulesPath | ConvertFrom-Json
    if ($document.schemaVersion -ne 1) {
        throw "Unsupported architecture boundary schemaVersion: $($document.schemaVersion)"
    }
    $ruleSetProperty = $document.ruleSets.PSObject.Properties[$RuleSet]
    if ($null -eq $ruleSetProperty) {
        throw "Unknown architecture rule set: $RuleSet"
    }
    $selected = $ruleSetProperty.Value
    $violations = New-Object System.Collections.Generic.List[string]
    $checkedFiles = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($namespaceRule in (Get-OptionalValues -Object $selected -Name "namespaceRules")) {
        $files = Get-RuleFiles -RepoRoot $resolvedRoot -Rule ([pscustomobject]@{
            roots = @($namespaceRule.root)
            filePattern = "*.cs"
        })
        $namespacePattern = "(?m)^\s*namespace\s+" + [regex]::Escape([string]$namespaceRule.prefix) + "(?:[.;\s{])"
        foreach ($file in $files) {
            [void]$checkedFiles.Add($file.FullName)
            $text = [System.IO.File]::ReadAllText($file.FullName)
            if ($text -notmatch $namespacePattern) {
                $relative = [System.IO.Path]::GetRelativePath($resolvedRoot, $file.FullName)
                $violations.Add("namespace-prefix: $relative must use $($namespaceRule.prefix)")
            }
        }
    }

    foreach ($rule in (Get-OptionalValues -Object $selected -Name "scanRules")) {
        foreach ($file in (Get-RuleFiles -RepoRoot $resolvedRoot -Rule $rule)) {
            [void]$checkedFiles.Add($file.FullName)
            $text = [System.IO.File]::ReadAllText($file.FullName)
            foreach ($pattern in @($rule.deny)) {
                $match = [regex]::Match($text, [string]$pattern)
                if (-not $match.Success) {
                    continue
                }
                $relative = [System.IO.Path]::GetRelativePath($resolvedRoot, $file.FullName)
                $line = Get-LineNumber -Text $text -Index $match.Index
                $violations.Add("$($rule.id): $relative`:$line matches '$pattern'")
            }
        }
    }

    if ($violations.Count -gt 0) {
        throw "Architecture boundary validation failed ($RuleSet):`n - $($violations -join "`n - ")"
    }

    Write-Host "Architecture boundary rules passed: set=$RuleSet, files=$($checkedFiles.Count)."
}

Export-ModuleMember -Function Invoke-ArchitectureBoundaryValidation
