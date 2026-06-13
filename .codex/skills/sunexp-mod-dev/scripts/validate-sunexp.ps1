param(
    [string]$ModRoot = ""
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..\..\..")).Path
}

function Read-Rows {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        return @()
    }
    $rows = @(Import-Csv -LiteralPath $Path)
    if ($rows.Count -le 1) {
        return @()
    }
    return @($rows | Select-Object -Skip 1 | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_.Id)
    })
}

function Add-Failure {
    param([string]$Message)
    $script:Failures.Add($Message) | Out-Null
}

function Add-Warning {
    param([string]$Message)
    $script:Warnings.Add($Message) | Out-Null
}

function Test-NoLuaProductionFiles {
    param(
        [string]$ModRootPath,
        [string]$RepoRoot
    )
    $luaFiles = @(Get-ChildItem -LiteralPath $ModRootPath -Recurse -File -Filter *.lua -ErrorAction SilentlyContinue)
    foreach ($luaFile in $luaFiles) {
        $relative = Resolve-Path -LiteralPath $luaFile.FullName -Relative
        Add-Failure "Production Lua file is not allowed: $relative"
    }

    $forbiddenToolNames = @(
        "Build-SunExpEntry.ps1",
        "Test-LuaSnippets.ps1",
        "Test-SunExpEntryLoad.ps1"
    )
    foreach ($toolName in $forbiddenToolNames) {
        $toolPath = Join-Path $RepoRoot "tools\$toolName"
        if (Test-Path -LiteralPath $toolPath) {
            Add-Failure "Legacy Lua tool is not allowed: tools/$toolName"
        }
    }
}

function Normalize-Id {
    param([string]$Id)
    if ($null -eq $Id) {
        return ""
    }
    return $Id.TrimStart("*")
}

function Full-SunExp-Id {
    param([string]$Id)
    return "SunExp_sunexp_$(Normalize-Id $Id)"
}

function Get-Placeholders {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return @()
    }
    return @([regex]::Matches($Text, "\{(\d+)\}") | ForEach-Object { [int]$_.Groups[1].Value } | Sort-Object -Unique)
}

function Get-AddDescriptionIndexes {
    param([string]$ScriptText)
    if ([string]::IsNullOrWhiteSpace($ScriptText)) {
        return @()
    }
    if ($ScriptText -match "CS\.SunExp\.Dll\.Scripting\.CardScripts\.Init\s*\(") {
        return @(1, 2, 3, 4, 5, 6, 7, 8, 9)
    }
    $pattern = "(?:SunExp_AddDamageDescription\s*\(\s*[^,]+,\s*|AddDescription\s*\(\s*)[""'](\d+)[""']"
    return @([regex]::Matches($ScriptText, $pattern) | ForEach-Object { [int]$_.Groups[1].Value } | Sort-Object -Unique)
}

function Test-TextPair {
    param(
        [string]$Kind,
        [object[]]$DataRows,
        [object[]]$TextRows
    )
    $textIds = @{}
    foreach ($row in $TextRows) {
        $textIds[(Normalize-Id $row.Id)] = $true
    }
    foreach ($row in $DataRows) {
        $id = Normalize-Id $row.Id
        if (-not $textIds.ContainsKey($id)) {
            Add-Failure "$Kind data row '$($row.Id)' has no matching text row."
        }
    }
}

function Test-ScriptResidue {
    param(
        [string]$File,
        [object[]]$Rows
    )
    $scriptColumns = @(
        "InitScript", "DrawScript", "UseScript", "DropScript", "OwnScript", "FightScript",
        "ApplyScript", "ClearScript", "SkillScript", "BaseScript", "EndScript", "EntryScript",
        "1Script", "2Script", "3Script", "4Script", "ChoiceScript1", "ChoiceScript2",
        "ChoiceScript3", "ChoiceScript4"
    )
    $patterns = @(
        @{ Pattern = "(^|[^.A-Za-z0-9_])SunExp_[A-Za-z0-9_]+\s*\("; Hint = "Old dynamic helper call is not allowed; use CS.SunExp.Dll.Scripting.*." },
        @{ Pattern = "\bfunction\s*\("; Hint = "Inline Lua callback in CSV script column is not allowed; use a C# entry point." },
        @{ Pattern = "\bself\s*:"; Hint = "Lua-style ScriptExecutor call in CSV script column is not allowed; use a C# entry point." },
        @{ Pattern = "\bend\s*;?\s*$"; Hint = "Lua-style block ending in CSV script column is not allowed; use a C# entry point." },
        @{ Pattern = "\bforeach\s*\("; Hint = "Inline implementation in CSV script column; move logic into C# and call a stable entry point." },
        @{ Pattern = "\bvar\s+\w+"; Hint = "Inline implementation in CSV script column; move logic into C# and call a stable entry point." },
        @{ Pattern = "\bnew\s+DataConfig\b"; Hint = "Inline object construction in CSV script column; move logic into C#." },
        @{ Pattern = "\b[A-Za-z_][A-Za-z0-9_]*\s*\+\+"; Hint = "Inline implementation in CSV script column; move logic into C# and call a stable entry point." },
        @{ Pattern = "(^|[^:.])\b(AddBuff|RemoveBuff|Damage|ChangeDefence|DrawCount|ChangePower|SetStatus|AddDescription)\s*\("; Hint = "Direct ScriptExecutor call in CSV script column; prefer a CS.SunExp.Dll.Scripting.* wrapper." }
    )

    $rowNumber = 1
    foreach ($row in $Rows) {
        $rowNumber += 1
        foreach ($column in $scriptColumns) {
            if (-not ($row.PSObject.Properties.Name -contains $column)) {
                continue
            }
            $code = $row.$column
            if ([string]::IsNullOrWhiteSpace($code)) {
                continue
            }
            foreach ($entry in $patterns) {
                if ($code -cmatch $entry.Pattern) {
                    Add-Failure "${File}:$rowNumber [$($row.Id).$column] $($entry.Hint)"
                }
            }
        }
    }
}

function Test-PackRefs {
    param(
        [string]$Kind,
        [object[]]$Rows,
        [hashtable]$PackIds
    )
    foreach ($row in $Rows) {
        if (-not ($row.PSObject.Properties.Name -contains "PackBelong")) {
            continue
        }
        if ([string]::IsNullOrWhiteSpace($row.PackBelong)) {
            Add-Failure "$Kind '$($row.Id)' has empty PackBelong."
            continue
        }
        if (-not $PackIds.ContainsKey($row.PackBelong)) {
            Add-Failure "$Kind '$($row.Id)' references missing PackBelong '$($row.PackBelong)'."
        }
    }
}

function Test-ResourcePaths {
    param(
        [string]$Kind,
        [object[]]$Rows,
        [string]$RepoRoot
    )
    $columns = @("Icon", "Avatar", "CharacterImage", "HouseAvatar", "Animation", "DollIcon", "Character", "CareerImage", "ActionImage1", "ActionImage2", "Dialogue", "EmojiPath", "FightWidget")
    foreach ($row in $Rows) {
        foreach ($column in $columns) {
            if (-not ($row.PSObject.Properties.Name -contains $column)) {
                continue
            }
            $value = $row.$column
            if ([string]::IsNullOrWhiteSpace($value)) {
                continue
            }
            if ($value -notlike "Mods/SunExp/*") {
                continue
            }
            $relative = $value -replace "^Mods/SunExp/", "SunExp/"
            $candidate = Join-Path $RepoRoot $relative
            $exists = (Test-Path -LiteralPath $candidate) -or (Test-Path -LiteralPath ($candidate + ".png")) -or (Test-Path -LiteralPath ($candidate + ".jpg")) -or (Test-Path -LiteralPath ($candidate + ".jpeg"))
            if (-not $exists) {
                Add-Failure "$Kind '$($row.Id)' column '$column' points to missing resource '$value'."
            }
        }
    }
}

function Test-CardDescriptions {
    param(
        [object[]]$Cards,
        [object[]]$CardTexts
    )
    $cardById = @{}
    foreach ($card in $Cards) {
        $cardById[(Normalize-Id $card.Id)] = $card
    }
    foreach ($text in $CardTexts) {
        $id = Normalize-Id $text.Id
        if (-not $cardById.ContainsKey($id)) {
            continue
        }
        $placeholders = Get-Placeholders $text.Description
        if ($placeholders.Count -eq 0) {
            continue
        }
        $card = $cardById[$id]
        $indexes = Get-AddDescriptionIndexes $card.InitScript
        foreach ($placeholder in $placeholders) {
            $expectedIndex = $placeholder + 1
            if ($indexes -notcontains $expectedIndex) {
                Add-Warning "Card '$($text.Id)' Description contains {$placeholder}, but InitScript has no AddDescription index '$expectedIndex'."
            }
        }
    }
}

function Test-EventListTexts {
    param(
        [object[]]$EventRows,
        [object[]]$EventTexts
    )
    $textById = @{}
    foreach ($text in $EventTexts) {
        $textById[(Normalize-Id $text.Id)] = $text
    }
    foreach ($event in $EventRows) {
        $id = Normalize-Id $event.Id
        if (-not $textById.ContainsKey($id)) {
            continue
        }
        $text = $textById[$id]
        if ([string]::IsNullOrWhiteSpace($text.TotalDescribe) -or -not $text.TotalDescribe.TrimStart().StartsWith("<main>")) {
            Add-Failure "EventList '$($event.Id)' text TotalDescribe is missing or not aligned to the TotalDescribe column."
        }
        for ($i = 1; $i -le 4; $i++) {
            $scriptColumn = "${i}Script"
            $describeColumn = "${i}Describe"
            if (-not ($event.PSObject.Properties.Name -contains $scriptColumn) -or -not ($text.PSObject.Properties.Name -contains $describeColumn)) {
                continue
            }
            $hasScript = -not [string]::IsNullOrWhiteSpace($event.$scriptColumn)
            $description = $text.$describeColumn
            if ($hasScript) {
                if ([string]::IsNullOrWhiteSpace($description) -or -not $description.TrimStart().StartsWith("<main>")) {
                    Add-Failure "EventList '$($event.Id)' option $i has script but missing/misaligned '$describeColumn'."
                }
            }
            elseif (-not [string]::IsNullOrWhiteSpace($description)) {
                Add-Warning "EventList '$($event.Id)' option $i has '$describeColumn' text but no '$scriptColumn'."
            }
        }
    }
}

function Test-SunExpWunaEventIds {
    param([object[]]$EventRows)
    $ids = @{}
    foreach ($event in $EventRows) {
        $id = Normalize-Id $event.Id
        if (-not [string]::IsNullOrWhiteSpace($id)) {
            $ids[$id] = $true
        }
    }
    for ($i = 1; $i -le 6; $i++) {
        $topLevel = "wuna_event_{0:D2}" -f $i
        $subEvent = "Sub_wuna_event_{0:D2}" -f $i
        if ($ids.ContainsKey($topLevel)) {
            Add-Failure "EventList '$topLevel' must not be a top-level event; use '$subEvent' so the ordinary event pool cannot draw it."
        }
        if (-not $ids.ContainsKey($subEvent)) {
            Add-Failure "EventList '$subEvent' is missing from the ordered WuNa solar event chain."
        }
    }
}

function New-Text {
    param([int[]]$CodePoints)
    return -join ($CodePoints | ForEach-Object { [char]$_ })
}

function Test-MapTextNotes {
    param([object[]]$MapTexts)
    $noteNormalEvent = New-Text @(0x666E, 0x901A, 0x4E8B, 0x4EF6)
    $noteNormal = New-Text @(0x666E, 0x901A)
    $noteElite = New-Text @(0x7CBE, 0x82F1)
    $noteBoss = New-Text @(0x9996, 0x9886)
    $noteBuild = New-Text @(0x5EFA, 0x7B51)
    $noteSolarEvent = New-Text @(0x65E5, 0x8000, 0x4E8B, 0x4EF6)
    $allowedNotes = @{
        $noteNormalEvent = $true
        $noteNormal = $true
        $noteElite = $true
        $noteBoss = $true
        $noteBuild = $true
        $noteSolarEvent = $true
    }
    foreach ($text in $MapTexts) {
        if (-not ($text.PSObject.Properties.Name -contains "Note")) {
            continue
        }
        $note = $text.Note
        if ([string]::IsNullOrWhiteSpace($note)) {
            continue
        }
        if (-not $allowedNotes.ContainsKey($note)) {
            Add-Failure "Map text row '$($text.Id)' uses unsupported Note '$note'. Use one of: $($allowedNotes.Keys -join ', ')."
        }
    }
}

$repoRoot = Get-RepoRoot
if (-not $ModRoot) {
    $ModRoot = Join-Path $repoRoot "SunExp"
}
elseif (-not [System.IO.Path]::IsPathRooted($ModRoot)) {
    $ModRoot = Join-Path $repoRoot $ModRoot
}

$modRootPath = (Resolve-Path -LiteralPath $ModRoot).Path
$script:Failures = New-Object System.Collections.Generic.List[string]
$script:Warnings = New-Object System.Collections.Generic.List[string]

Test-NoLuaProductionFiles $modRootPath $repoRoot

$cards = Read-Rows (Join-Path $modRootPath "Data\Card\sunexp.csv")
$cardTexts = Read-Rows (Join-Path $modRootPath "Text\Card\sunexp.csv")
$buffs = Read-Rows (Join-Path $modRootPath "Data\Buff\sunexp.csv")
$buffTexts = Read-Rows (Join-Path $modRootPath "Text\Buff\sunexp.csv")
$relics = Read-Rows (Join-Path $modRootPath "Data\Relic\sunexp.csv")
$relicTexts = Read-Rows (Join-Path $modRootPath "Text\Relic\sunexp.csv")
$packs = Read-Rows (Join-Path $modRootPath "Data\CardPack\sunexp.csv")
$packTexts = Read-Rows (Join-Path $modRootPath "Text\CardPack\sunexp.csv")

Test-TextPair "Card" $cards $cardTexts
Test-TextPair "Buff" $buffs $buffTexts
Test-TextPair "Relic" $relics $relicTexts
Test-TextPair "CardPack" $packs $packTexts

$packIds = @{}
foreach ($pack in $packs) {
    $packIds[(Full-SunExp-Id $pack.Id)] = $true
}
Test-PackRefs "Card" $cards $packIds
Test-PackRefs "Relic" $relics $packIds

Test-ResourcePaths "Card" $cards $repoRoot
Test-ResourcePaths "Relic" $relics $repoRoot

Test-ScriptResidue "Data/Card/sunexp.csv" $cards
Test-ScriptResidue "Data/Buff/sunexp.csv" $buffs
Test-ScriptResidue "Data/Relic/sunexp.csv" $relics
Test-CardDescriptions $cards $cardTexts

$optionalKinds = @("RoleData", "Dialogue", "EventList", "Career", "Map")
foreach ($kind in $optionalKinds) {
    $dataFile = Join-Path $modRootPath "Data\$kind\sunexp.csv"
    $textFile = Join-Path $modRootPath "Text\$kind\sunexp.csv"
    if ((Test-Path -LiteralPath $dataFile) -or (Test-Path -LiteralPath $textFile)) {
        $dataRows = Read-Rows $dataFile
        $textRows = Read-Rows $textFile
        Test-TextPair $kind $dataRows $textRows
        Test-ScriptResidue "Data/$kind/sunexp.csv" $dataRows
        Test-ResourcePaths $kind $dataRows $repoRoot
        if ($kind -eq "EventList") {
            Test-EventListTexts $dataRows $textRows
            Test-SunExpWunaEventIds $dataRows
        }
        if ($kind -eq "Map") {
            Test-MapTextNotes $textRows
        }
    }
}

if ($script:Warnings.Count -gt 0) {
    Write-Host "Warnings:"
    foreach ($warning in $script:Warnings) {
        Write-Host "  - $warning"
    }
}

if ($script:Failures.Count -gt 0) {
    Write-Host "Validation failed: $($script:Failures.Count) failure(s)."
    foreach ($failure in $script:Failures) {
        Write-Host "  - $failure"
    }
    exit 1
}

Write-Host "SunExp validation passed: cards=$($cards.Count), relics=$($relics.Count), buffs=$($buffs.Count), packs=$($packs.Count), warnings=$($script:Warnings.Count)."
