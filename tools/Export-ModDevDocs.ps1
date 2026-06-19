[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path -LiteralPath $Root).Path
$docsRoot = Join-Path $Root 'docs\mod-dev'
$generatedRoot = Join-Path $docsRoot 'generated'
New-Item -ItemType Directory -Force -Path $generatedRoot | Out-Null

function New-Text {
    param([int[]]$CodePoints)
    return -join ($CodePoints | ForEach-Object { [char]$_ })
}

function Get-RelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $full = (Resolve-Path -LiteralPath $Path).Path
    if ($full.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase)) {
        return ($full.Substring($Root.Length).TrimStart('\') -replace '\\', '/')
    }

    return ($full -replace '\\', '/')
}

function Split-CsvHeader {
    param([Parameter(Mandatory = $true)][string]$Line)

    return @($Line -split ',' | ForEach-Object { $_.Trim().Trim('"') } | Where-Object { $_ -ne '' })
}

function Get-ReferenceRoot {
    $referenceParentName = New-Text @(0x5F00, 0x53D1, 0x53C2, 0x8003, 0x8D44, 0x6599)
    $decompiledPrefix = New-Text @(0x53CD, 0x7F16, 0x8BD1, 0x6587, 0x4EF6, 0x5939)
    $parent = Join-Path $Root $referenceParentName
    if (-not (Test-Path -LiteralPath $parent)) {
        return $null
    }

    $candidate = Get-ChildItem -LiteralPath $parent -Directory |
        Where-Object { $_.Name -like "$decompiledPrefix*" } |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if ($candidate) {
        return $candidate.FullName
    }

    return $null
}

function Get-CsvSources {
    $sources = New-Object System.Collections.Generic.List[hashtable]
    $templatePath = Join-Path $Root 'apocalyptic-journey-mod-tutorial\ModTemplate'
    if (Test-Path -LiteralPath $templatePath) {
        $sources.Add(@{ Name = 'Official ModTemplate'; Path = 'apocalyptic-journey-mod-tutorial\ModTemplate' })
    }

    $modDirs = Get-ChildItem -LiteralPath $Root -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'ModConfig.json') } |
        Sort-Object Name

    foreach ($dir in $modDirs) {
        $sources.Add(@{ Name = $dir.Name; Path = $dir.Name })
    }

    return @($sources)
}

function Write-CsvSchemaIndex {
    $output = Join-Path $generatedRoot 'csv-schema-index.md'
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('# Generated CSV Schema Index')
    $lines.Add('')
    $lines.Add("Generated from workspace CSV files. Refresh with ``tools\Export-ModDevDocs.ps1``.")
    $lines.Add('')

    foreach ($source in (Get-CsvSources)) {
        $sourcePath = Join-Path $Root $source.Path
        if (-not (Test-Path -LiteralPath $sourcePath)) {
            continue
        }

        $csvFiles = Get-ChildItem -LiteralPath $sourcePath -Recurse -Filter '*.csv' |
            Where-Object { $_.FullName -match '\\(Data|Text)\\' } |
            Where-Object { $_.FullName -notmatch '\\Scripts\\Lib\\DataConfigs\\' } |
            Sort-Object FullName

        if (-not $csvFiles) {
            continue
        }

        $lines.Add("## $($source.Name)")
        $lines.Add('')

        foreach ($file in $csvFiles) {
            $relative = Get-RelativePath $file.FullName
            $firstLine = Get-Content -LiteralPath $file.FullName -Encoding UTF8 -TotalCount 1
            $columns = Split-CsvHeader $firstLine
            $lines.Add("### ``$relative``")
            $lines.Add('')
            $lines.Add('Columns:')
            $lines.Add('')
            foreach ($column in $columns) {
                $lines.Add("- ``$column``")
            }
            $lines.Add('')
        }
    }

    Set-Content -LiteralPath $output -Encoding UTF8 -Value $lines
}

function Write-PublicApiIndex {
    $output = Join-Path $generatedRoot 'public-api-index.md'
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('# Generated Public API Index')
    $lines.Add('')
    $lines.Add("Generated from ``*-Dev`` C# projects. Refresh with ``tools\Export-ModDevDocs.ps1``.")
    $lines.Add('')

    $devDirs = Get-ChildItem -LiteralPath $Root -Directory -Filter '*-Dev' | Sort-Object Name
    foreach ($dir in $devDirs) {
        $files = Get-ChildItem -LiteralPath $dir.FullName -Recurse -Filter '*.cs' |
            Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
            Sort-Object FullName

        if (-not $files) {
            continue
        }

        $lines.Add("## $($dir.Name)")
        $lines.Add('')

        foreach ($file in $files) {
            $entries = New-Object System.Collections.Generic.List[string]
            $content = Get-Content -LiteralPath $file.FullName -Encoding UTF8
            foreach ($line in $content) {
                $trimmed = $line.Trim()
                if ($trimmed -match '^public\s+(static\s+)?(sealed\s+)?(class|enum|struct|interface)\s+[A-Za-z0-9_]+') {
                    $entries.Add($trimmed)
                    continue
                }
                if ($trimmed -match '^public\s+(static\s+)?[A-Za-z0-9_<>,\[\]\?\s]+\s+[A-Za-z0-9_]+\s*\(') {
                    $entries.Add($trimmed)
                }
            }

            if ($entries.Count -gt 0) {
                $relative = Get-RelativePath $file.FullName
                $lines.Add("### ``$relative``")
                $lines.Add('')
                foreach ($entry in $entries) {
                    $lines.Add("- ``$entry``")
                }
                $lines.Add('')
            }
        }
    }

    Set-Content -LiteralPath $output -Encoding UTF8 -Value $lines
}

function Write-ScriptHookPointIndex {
    $output = Join-Path $generatedRoot 'script-hook-point-index.md'
    $referenceRoot = Get-ReferenceRoot
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('# Generated Script Hook Point Index')
    $lines.Add('')
    $lines.Add("Generated from the current decompiled snapshot and C# projects. Refresh with ``tools\Export-ModDevDocs.ps1``.")
    $lines.Add('')

    if (-not $referenceRoot) {
        $lines.Add('No decompiled reference folder was found.')
        Set-Content -LiteralPath $output -Encoding UTF8 -Value $lines
        return
    }

    $lines.Add("Reference snapshot: ``$(Get-RelativePath $referenceRoot)``")
    $lines.Add('')

    $scriptRefs = @{}
    $runScriptFiles = @()
    foreach ($child in @('Witch', 'AllScripts')) {
        $candidate = Join-Path $referenceRoot $child
        if (Test-Path -LiteralPath $candidate) {
            $runScriptFiles += $candidate
        }
    }

    foreach ($rootPath in $runScriptFiles) {
        $files = Get-ChildItem -LiteralPath $rootPath -Recurse -Filter '*.cs' | Sort-Object FullName
        foreach ($file in $files) {
            $matches = Select-String -LiteralPath $file.FullName -Pattern 'RunScript\("([^"]+)"' -AllMatches
            foreach ($match in $matches) {
                foreach ($capture in $match.Matches) {
                    $scriptName = $capture.Groups[1].Value
                    if (-not $scriptRefs.ContainsKey($scriptName)) {
                        $scriptRefs[$scriptName] = New-Object System.Collections.Generic.List[string]
                    }
                    if ($scriptRefs[$scriptName].Count -lt 20) {
                        $scriptRefs[$scriptName].Add("$(Get-RelativePath $file.FullName):$($match.LineNumber)")
                    }
                }
            }
        }
    }

    $lines.Add('## RunScript Call Sites')
    $lines.Add('')
    foreach ($scriptName in ($scriptRefs.Keys | Sort-Object)) {
        $lines.Add("### ``$scriptName``")
        $lines.Add('')
        foreach ($ref in $scriptRefs[$scriptName]) {
            $lines.Add("- ``$ref``")
        }
        $lines.Add('')
    }

    $lines.Add('## Current C# Hook Registrations')
    $lines.Add('')
    $hookFiles = Get-ChildItem -LiteralPath $Root -Directory -Filter '*-Dev' |
        ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Recurse -Filter '*.cs' } |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        Sort-Object FullName

    foreach ($file in $hookFiles) {
        $matches = Select-String -LiteralPath $file.FullName -Pattern '\[Hook(Before|After)|AddMethodHook(Before|After)\('
        if ($matches) {
            $lines.Add("### ``$(Get-RelativePath $file.FullName)``")
            $lines.Add('')
            foreach ($match in $matches) {
                $lines.Add("- line $($match.LineNumber): ``$($match.Line.Trim())``")
            }
            $lines.Add('')
        }
    }

    Set-Content -LiteralPath $output -Encoding UTF8 -Value $lines
}

Write-CsvSchemaIndex
Write-PublicApiIndex
Write-ScriptHookPointIndex

Write-Host "Generated MOD developer docs in $generatedRoot"
