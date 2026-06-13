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
    $forbiddenCaptions = @(
        "SunExp card recovered.",
        "SunExp relic recovered.",
        "SunExp blessing recovered.",
        "SunExp note closed."
    )
    foreach ($caption in $forbiddenCaptions) {
        if ($entryText.Contains($caption)) {
            throw "Entry.lua contains forbidden hard-coded solar event caption: $caption"
        }
    }

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

    if ($registeredMethods.Count -eq 0 -and $csvCalls.Count -gt 0) {
        throw "CSV scripts still call SunExp helper(s), but no SunExp dynamic methods were found in Entry.lua: $($csvCalls -join ', ')"
    }

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
    hooksBefore = {},
    hooksAfter = {}
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

function ModConfig:AddMethodHookAfter(typeDotMethod, fn)
    assert(type(typeDotMethod) == "string", "AddMethodHookAfter target must be string")
    assert(type(fn) == "function", "AddMethodHookAfter function missing for " .. tostring(typeDotMethod))
    table.insert(self.hooksAfter, { typeDotMethod = typeDotMethod, fn = fn })
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

if expected_count == 0 then
    print("Entry load/setup simulation passed: migrated DLL mode, hooksBefore=" .. #ModConfig.hooksBefore .. ", hooksAfter=" .. #ModConfig.hooksAfter)
    return
end

local before_hooks = {}
for _, hook in ipairs(ModConfig.hooksBefore) do
    if type(hook.fn) == "function" then
        before_hooks[hook.typeDotMethod] = hook.fn
    end
end
local ready_hook = before_hooks["MapSelectUI.ReadyToSelect"]
local cmd_hook = before_hooks["MapManager.CmdSelectMap"]
local user_cmd_hook = before_hooks["MapManager.UserCode_CmdSelectMap__String[]__String[]__NetworkConnectionToClient"]
local target_update_hook = before_hooks["MapManager.TargetUpdateMap"]
assert(ready_hook ~= nil, "missing ReadyToSelect solar layer hook")
assert(cmd_hook ~= nil, "missing narrow CmdSelectMap repair hook")
assert(user_cmd_hook ~= nil, "missing server CmdSelectMap repair hook")
assert(target_update_hook ~= nil, "missing TargetUpdateMap repair hook")

local after_hooks = {}
for _, hook in ipairs(ModConfig.hooksAfter) do
    if type(hook.fn) == "function" then
        after_hooks[hook.typeDotMethod] = hook.fn
    end
end
assert(after_hooks["NormalMapManager.GeneratrMap"] ~= nil, "missing GeneratrMap solar layer hook")
assert(after_hooks["NormalMapManager.RandomGenerate"] ~= nil, "missing RandomGenerate solar layer hook")

local function new_node(id, node_id)
    local dict = { values = { Id = id, Type = "Event", NodeId = node_id, Level = "-1" } }
    function dict:ContainsKey(key)
        return self.values[key] ~= nil
    end
    function dict:get_Item(key)
        return self.values[key]
    end
    function dict:set_Item(key, value)
        self.values[key] = value
    end
    return { type = "Event", data = dict }
end

local function new_fight_node(id, node_id)
    local node = new_node(id, node_id)
    node.type = "Fight"
    node.data.values.Type = "Fight"
    return node
end

local function new_array(values)
    local arr = { values = values, Length = #values, Count = #values }
    function arr:get_Item(index)
        return self.values[index + 1]
    end
    function arr:set_Item(index, value)
        self.values[index + 1] = value
    end
    function arr:SetValue(value, index)
        self.values[index + 1] = value
    end
    function arr:Add(value)
        table.insert(self.values, value)
        self.Length = #self.values
        self.Count = #self.values
    end
    return arr
end

local fake_vars = { values = {} }
function fake_vars:set_Item(key, value)
    self.values[key] = value
end
assert(ModConfig.dynamicMethods.SunExp_SetEventChoices({ Vars = fake_vars }, "1", "1") == true, "event choice helper returned false")
assert(fake_vars.values.Choice1 == "1", "event choice helper did not enable Choice1")
assert(fake_vars.values.Choice2 == "1", "event choice helper did not enable Choice2")
local begin_vars = { values = {} }
function begin_vars:set_Item(key, value)
    self.values[key] = value
end
assert(ModConfig.dynamicMethods.SunExp_BeginWunaEvent({ Vars = begin_vars }, 1) == true, "begin event helper did not enable first WuNa event")
assert(begin_vars.values.Choice1 == "1", "begin event helper did not enable first event Choice1")
assert(begin_vars.values.Choice2 == "1", "begin event helper did not enable first event Choice2")

CS = { MapManager = { Instance = { Level = 6 } } }
SunExp_TestExDeleteDes = 0

local select_nodes = new_array({
    new_fight_node("fight_a", "level_1"),
    new_node("event_a", "event_100"),
    new_fight_node("fight_b", "level_2"),
    new_node("event_b", "event_101"),
    new_fight_node("fight_c", "level_3"),
    new_node("event_c", "event_102"),
    new_fight_node("fight_d", "level_4"),
    new_node("break", "Breaks"),
    new_fight_node("fight_e", "level_5"),
    new_node("event_d", "event_103"),
    new_fight_node("fight_f", "level_6"),
    new_node("event_e", "event_104"),
    new_fight_node("fight_g", "level_7"),
    new_node("event_f", "event_105"),
    new_fight_node("fight_h", "level_8"),
    new_node("break", "Breaks")
})

local fake_tree = {
    SelectNode = select_nodes,
    GetNodeByNodeId = function(self, id)
        assert(id == "SunExp_sunexp_solar_event", "solar hook requested an unexpected placeholder node")
        return new_node("solar_event", "")
    end
}

local fake_ui = {
    mapTree = fake_tree,
    MapTree = fake_tree
}

local before_count = select_nodes.Count
local changed = ready_hook({ Target = fake_ui })
assert(changed == true, "ReadyToSelect hook did not ensure a solar node in the current layer")
assert(select_nodes.Count == before_count, "solar layer hook must replace, not append")
local start_index, segment_size = ModConfig.dynamicMethods.SunExp_GetSolarLayerRange()
assert(start_index == 8 and segment_size == 8, "unexpected layer range for level 6")
local solar_count = 0
local solar_node = nil
for i = start_index, start_index + segment_size - 1 do
    local node = select_nodes:get_Item(i)
    if ModConfig.dynamicMethods.SunExp_IsSolarEventNode(node) then
        solar_count = solar_count + 1
        solar_node = node
    end
end
assert(solar_count == 1, "current layer must contain exactly one solar node")
assert(solar_node.data.values.Id == "SunExp_sunexp_solar_event", "solar node did not normalize map id")
assert(solar_node.data.values.Type == "Event", "solar node type must stay Event")
assert(solar_node.data.values.NodeId == "SunExp_sunexp_Sub_wuna_event_01", "solar node must point at the current WuNa event, not a random event")
changed = ready_hook({ Target = fake_ui })
assert(select_nodes.Count == before_count, "second ensure call must not append")
solar_count = 0
for i = start_index, start_index + segment_size - 1 do
    if ModConfig.dynamicMethods.SunExp_IsSolarEventNode(select_nodes:get_Item(i)) then
        solar_count = solar_count + 1
    end
end
assert(solar_count == 1, "second ensure call duplicated the solar node")

local maps = new_array({ "event_a", "SunExp_sunexp_solar_event", "event_b" })
local mapdata = new_array({ "event_100", "event_random", "event_2001" })
changed = cmd_hook({ Arguments = new_array({ maps, mapdata, nil }) })
assert(changed == true, "CmdSelectMap hook did not repair solar mapdata")
assert(mapdata.values[1] == "event_100", "CmdSelectMap hook changed a non-solar event")
assert(mapdata.values[2] == "SunExp_sunexp_Sub_wuna_event_01", "CmdSelectMap hook did not set the current WuNa event")
assert(mapdata.values[3] == "event_2001", "CmdSelectMap hook changed a fixed story event")

local shifted_maps = new_array({ "solar_event" })
local shifted_mapdata = new_array({ "event_random" })
changed = target_update_hook({ Arguments = new_array({ nil, shifted_maps, shifted_mapdata }) })
assert(changed == true, "TargetUpdateMap hook did not repair shifted solar mapdata")
assert(shifted_maps.values[1] == "SunExp_sunexp_solar_event", "solar map id was not normalized")
assert(shifted_mapdata.values[1] == "SunExp_sunexp_Sub_wuna_event_01", "shifted solar mapdata was not repaired")

local progress_vars = { SunExp_WunaEventProgressV2 = "6" }
CS.ScriptExecutor = {
    PlayerInfo = {
        GetGameVar = function(key)
            return progress_vars[key]
        end
    }
}
local repeat_node = new_node("SunExp_sunexp_solar_event", "event_random")
assert(ModConfig.dynamicMethods.SunExp_TrySetSolarEventNode(repeat_node) == true, "solar node repeat repair returned false")
assert(repeat_node.data.values.NodeId == "SunExp_sunexp_Sub_wuna_event_repeat", "solar node should become repeat only after progress reaches 6")

print("Entry load/setup simulation passed: dynamicMethods=" .. actual_count .. ", hooksBefore=" .. #ModConfig.hooksBefore .. ", hooksAfter=" .. #ModConfig.hooksAfter)
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
