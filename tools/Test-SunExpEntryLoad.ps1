param(
    [string]$SourceRoot = "",
    [string]$ManifestPath = "",
    [string]$EntryPath = "",
    [string]$LuaPath = "",
    [switch]$KeepTemp
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
}

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
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "lua.exe was not found. Install Lua or pass -LuaPath <path-to-lua.exe>."
}

function Resolve-RepoPath {
    param(
        [string]$Path,
        [string]$DefaultPath,
        [string]$RepoRoot
    )

    if (-not $Path) {
        $Path = $DefaultPath
    }
    elseif (-not [System.IO.Path]::IsPathRooted($Path)) {
        $Path = Join-Path $RepoRoot $Path
    }
    return $Path
}

function Read-AllText {
    param([string]$Path)
    return [System.IO.File]::ReadAllText($Path)
}

function Write-Utf8NoBom {
    param(
        [string]$Path,
        [string]$Text
    )
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

function New-LuaLongString {
    param([string]$Value)
    return "[==[$Value]==]"
}

$repoRoot = Get-RepoRoot
$sourceRootPath = Resolve-RepoPath -Path $SourceRoot -DefaultPath (Join-Path $repoRoot "SunExp\Scripts\_src") -RepoRoot $repoRoot
$manifestPathResolved = Resolve-RepoPath -Path $ManifestPath -DefaultPath (Join-Path $sourceRootPath "manifest.txt") -RepoRoot $repoRoot
$entryPathResolved = Resolve-RepoPath -Path $EntryPath -DefaultPath (Join-Path $repoRoot "SunExp\Scripts\Entry.lua") -RepoRoot $repoRoot

$sourceRootPath = (Resolve-Path -LiteralPath $sourceRootPath).Path
$manifestPathResolved = (Resolve-Path -LiteralPath $manifestPathResolved).Path
$entryPathResolved = (Resolve-Path -LiteralPath $entryPathResolved).Path
$luaExe = Resolve-LuaExe -RequestedPath $LuaPath

$tmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("sunexp-entry-load-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tmpRoot | Out-Null

try {
    $generatedEntry = Join-Path $tmpRoot "Entry.generated.lua"
    & (Join-Path $repoRoot "tools\Build-SunExpEntry.ps1") -SourceRoot $sourceRootPath -ManifestPath $manifestPathResolved -OutputPath $generatedEntry | Out-Host

    $entryHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $entryPathResolved).Hash
    $generatedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $generatedEntry).Hash
    if ($entryHash -ne $generatedHash) {
        throw "SunExp/Scripts/Entry.lua is not in sync with _src modules. Run tools\Build-SunExpEntry.ps1."
    }

    $entryText = Read-AllText -Path $entryPathResolved
    $definedMethods = @(
        [regex]::Matches($entryText, "(?m)^function\s+(SunExp_[A-Za-z0-9_]+)\s*\(") |
            ForEach-Object { $_.Groups[1].Value } |
            Sort-Object -Unique
    )
    $registeredMethods = @(
        [regex]::Matches($entryText, 'SunExp_RegisterDynamicMethod\s*\(\s*config\s*,\s*"(?<name>SunExp_[A-Za-z0-9_]+)"') |
            ForEach-Object { $_.Groups["name"].Value } |
            Sort-Object -Unique
    )

    if ($registeredMethods.Count -eq 0) {
        throw "No SunExp dynamic methods were found in Entry.lua."
    }

    $csvTextParts = New-Object System.Collections.Generic.List[string]
    foreach ($csv in Get-ChildItem -Path (Join-Path $repoRoot "SunExp\Data") -Recurse -Filter *.csv -ErrorAction SilentlyContinue) {
        $csvTextParts.Add((Read-AllText -Path $csv.FullName)) | Out-Null
    }
    $csvText = $csvTextParts -join "`n"
    $csvCalls = @(
        [regex]::Matches($csvText, "(SunExp_[A-Za-z0-9_]+)\s*\(") |
            ForEach-Object { $_.Groups[1].Value } |
            Sort-Object -Unique
    )

    $missingDefinitions = @($csvCalls | Where-Object { $definedMethods -notcontains $_ })
    if ($missingDefinitions.Count -gt 0) {
        throw "CSV scripts call undefined SunExp helper(s): $($missingDefinitions -join ', ')"
    }

    $missingRegistrations = @($csvCalls | Where-Object { $registeredMethods -notcontains $_ })
    if ($missingRegistrations.Count -gt 0) {
        throw "CSV scripts call unregistered SunExp dynamic method(s): $($missingRegistrations -join ', ')"
    }

    $methodRows = $registeredMethods | ForEach-Object { "    [""$_""] = true," }
    $methodTable = $methodRows -join "`n"
    $entryLuaPath = New-LuaLongString -Value $entryPathResolved
    $expectedCount = $registeredMethods.Count

    $runner = @"
local entry_path = $entryLuaPath
local expected_methods = {
$methodTable
}
local expected_count = $expectedCount

ModConfig = {
    dynamicMethods = {},
    hooksBefore = {}
}

function ModConfig:AddDynamicMethod(name, fn)
    assert(type(name) == "string", "AddDynamicMethod name must be string")
    assert(type(fn) == "function", "AddDynamicMethod function missing for " .. tostring(name))
    assert(self.dynamicMethods[name] == nil, "duplicate dynamic method: " .. name)
    self.dynamicMethods[name] = fn
end

function ModConfig:AddMethodHookBefore(typeDotMethod, fn)
    assert(type(typeDotMethod) == "string", "AddMethodHookBefore target must be string")
    assert(type(fn) == "function", "AddMethodHookBefore function missing for " .. tostring(typeDotMethod))
    table.insert(self.hooksBefore, { typeDotMethod = typeDotMethod, fn = fn })
end

local chunk, load_error = loadfile(entry_path)
assert(chunk ~= nil, load_error)

local ok, err = pcall(chunk)
assert(ok, err)
assert(type(ModConfig.Setup) == "function", "ModConfig:Setup was not defined by Entry.lua")

ok, err = pcall(function()
    ModConfig:Setup()
end)
assert(ok, err)

local actual_count = 0
for name, _ in pairs(ModConfig.dynamicMethods) do
    actual_count = actual_count + 1
    assert(expected_methods[name] == true, "unexpected dynamic method registered: " .. name)
end
assert(actual_count == expected_count, "dynamic method count mismatch: expected " .. expected_count .. ", got " .. actual_count)

for name, _ in pairs(expected_methods) do
    assert(type(ModConfig.dynamicMethods[name]) == "function", "missing dynamic method after Setup: " .. name)
end

local has_map_hook = false
for _, hook in ipairs(ModConfig.hooksBefore) do
    if hook.typeDotMethod == "Witch.UI.Window.MapSelectUI.CreateMapItem" and type(hook.fn) == "function" then
        has_map_hook = true
    end
end
assert(has_map_hook, "missing map injection hook registration")

print("Entry load/setup simulation passed: dynamicMethods=" .. actual_count .. ", hooksBefore=" .. #ModConfig.hooksBefore)
"@

    $runnerPath = Join-Path $tmpRoot "entry-load-test.lua"
    Write-Utf8NoBom -Path $runnerPath -Text $runner

    $output = & $luaExe $runnerPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Lua entry load simulation failed: $($output -join ' ')"
    }

    Write-Host "Entry.lua source sync passed: $entryHash"
    Write-Host "CSV helper surface passed: calls=$($csvCalls.Count), registered=$($registeredMethods.Count), defined=$($definedMethods.Count)."
    foreach ($line in $output) {
        Write-Host $line
    }
}
finally {
    if (-not $KeepTemp) {
        Remove-Item -LiteralPath $tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    else {
        Write-Host "Kept temp directory: $tmpRoot"
    }
}
