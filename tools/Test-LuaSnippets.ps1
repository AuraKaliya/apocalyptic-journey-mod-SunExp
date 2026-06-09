param(
    [string]$DataRoot = "SunExp\Data",
    [string]$LuaPath = ""
)

$ErrorActionPreference = "Stop"

function Resolve-LuaExe {
    param([string]$RequestedPath)

    if ($RequestedPath -and (Test-Path -LiteralPath $RequestedPath)) {
        return (Resolve-Path -LiteralPath $RequestedPath).Path
    }

    $cmd = Get-Command lua -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Lua\bin\lua.exe",
        "$env:ProgramFiles\Lua\bin\lua.exe",
        "${env:ProgramFiles(x86)}\Lua\bin\lua.exe"
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    throw "lua.exe was not found. Install Lua or pass -LuaPath <path-to-lua.exe>."
}

$luaExe = Resolve-LuaExe -RequestedPath $LuaPath
$root = Resolve-Path -LiteralPath $DataRoot
$scriptColumns = @(
    "InitScript",
    "DrawScript",
    "UseScript",
    "DropScript",
    "OwnScript",
    "FightScript",
    "ApplyScript",
    "ClearScript",
    "SkillScript"
)

$tmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("sunexp-lua-check-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tmpRoot | Out-Null

$checked = 0
$failures = New-Object System.Collections.Generic.List[string]

try {
    $csvFiles = Get-ChildItem -Path $root -Recurse -Filter *.csv
    foreach ($csv in $csvFiles) {
        $rows = Import-Csv -LiteralPath $csv.FullName
        $rowNumber = 1

        foreach ($row in $rows) {
            $rowNumber += 1
            $id = $row.Id
            if ($rowNumber -eq 2 -or [string]::IsNullOrWhiteSpace($id) -or $id -eq "唯一标识") {
                continue
            }

            foreach ($column in $scriptColumns) {
                if (-not ($row.PSObject.Properties.Name -contains $column)) {
                    continue
                }

                $code = $row.$column
                if ([string]::IsNullOrWhiteSpace($code)) {
                    continue
                }

                $checked += 1
                $safeName = (($csv.BaseName + "_" + $rowNumber + "_" + $id + "_" + $column) -replace '[^\w.-]', '_')
                $tmpFile = Join-Path $tmpRoot ($safeName + ".lua")
                Set-Content -LiteralPath $tmpFile -Value $code -Encoding UTF8

                $oldErrorActionPreference = $ErrorActionPreference
                $ErrorActionPreference = "Continue"
                $output = & $luaExe -e "assert(loadfile([[$tmpFile]]))" 2>&1
                $ErrorActionPreference = $oldErrorActionPreference
                if ($LASTEXITCODE -ne 0) {
                    $relative = Resolve-Path -LiteralPath $csv.FullName -Relative
                    $failures.Add("${relative}:$rowNumber [$id.$column] $($output -join ' ')")
                }
            }
        }
    }

    $modRoot = Split-Path -Parent $root
    $luaRoot = Join-Path $modRoot "Scripts"
    if (Test-Path -LiteralPath $luaRoot) {
        $luaFiles = Get-ChildItem -Path $luaRoot -Recurse -Filter *.lua
        foreach ($luaFile in $luaFiles) {
            $checked += 1
            $oldErrorActionPreference = $ErrorActionPreference
            $ErrorActionPreference = "Continue"
            $output = & $luaExe -e "assert(loadfile([[$($luaFile.FullName)]]))" 2>&1
            $ErrorActionPreference = $oldErrorActionPreference
            if ($LASTEXITCODE -ne 0) {
                $relative = Resolve-Path -LiteralPath $luaFile.FullName -Relative
                $failures.Add("${relative}:1 [lua-file] $($output -join ' ')")
            }
        }
    }
}
finally {
    Remove-Item -LiteralPath $tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Host "Lua syntax check failed: $($failures.Count) failure(s), $checked snippet(s) checked."
    foreach ($failure in $failures) {
        Write-Host $failure
    }
    exit 1
}

Write-Host "Lua syntax check passed: $checked snippet(s) checked with $luaExe."
