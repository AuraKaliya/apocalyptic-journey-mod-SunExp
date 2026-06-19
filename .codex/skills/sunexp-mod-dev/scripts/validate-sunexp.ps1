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
    param(
        [string]$Id,
        [string]$FileStem = "sunexp"
    )
    return "SunExp_${FileStem}_$(Normalize-Id $Id)"
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
            if ($Kind -eq "Card" -and (Normalize-Id $row.Id) -ne $row.Id) {
                continue
            }
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

function Test-EnemyAnimationMapResources {
    param(
        [object[]]$Rows,
        [string]$RepoRoot
    )
    foreach ($row in $Rows) {
        if (-not ($row.PSObject.Properties.Name -contains "Animation")) {
            continue
        }
        $value = $row.Animation
        if ([string]::IsNullOrWhiteSpace($value) -or $value -notlike "Mods/SunExp/*") {
            continue
        }

        $relative = $value -replace "^Mods/SunExp/", "SunExp/"
        $animationRoot = Join-Path $RepoRoot $relative
        $mapRoot = Join-Path $animationRoot "Map"
        $mapFrames = @()
        if (Test-Path -LiteralPath $mapRoot) {
            $mapFrames = @(Get-ChildItem -LiteralPath $mapRoot -File -ErrorAction SilentlyContinue | Where-Object {
                $_.Extension -in @(".png", ".jpg", ".jpeg")
            })
        }

        if ($mapFrames.Count -eq 0) {
            Add-Failure "Enemy '$($row.Id)' Animation '$value' is missing Map/*.png or Map/*.jpg. MapItem.Init calls Animation/Map before falling back to Idle, and empty mod folders return null."
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
    $allowedNotes = @{
        $noteNormalEvent = $true
        $noteNormal = $true
        $noteElite = $true
        $noteBoss = $true
        $noteBuild = $true
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

function Get-KindCsvFiles {
    param(
        [string]$ModRootPath,
        [string]$Area,
        [string]$Kind
    )
    $dir = Join-Path $ModRootPath "$Area\$Kind"
    if (-not (Test-Path -LiteralPath $dir)) {
        return @()
    }
    return @(Get-ChildItem -LiteralPath $dir -Filter "*.csv" -File | Sort-Object Name)
}

function Get-TextCsvForDataCsv {
    param(
        [string]$ModRootPath,
        [System.IO.FileInfo]$DataFile
    )
    $dataRoot = Join-Path $ModRootPath "Data"
    $relative = $DataFile.FullName.Substring($dataRoot.Length).TrimStart("\")
    return Join-Path (Join-Path $ModRootPath "Text") $relative
}

function Add-RowsFromFiles {
    param([System.IO.FileInfo[]]$Files)
    $allRows = @()
    foreach ($file in $Files) {
        $allRows += @(Read-Rows $file.FullName)
    }
    return @($allRows)
}

function Test-KindDataTextPairs {
    param(
        [string]$Kind,
        [System.IO.FileInfo[]]$DataFiles,
        [string]$ModRootPath,
        [bool]$TextRequired = $true
    )
    foreach ($dataFile in $DataFiles) {
        $textFile = Get-TextCsvForDataCsv $ModRootPath $dataFile
        if (-not (Test-Path -LiteralPath $textFile)) {
            if ($TextRequired) {
                Add-Failure "$Kind data file '$($dataFile.FullName)' has no matching text file."
            }
            continue
        }
        Test-TextPair $Kind (Read-Rows $dataFile.FullName) (Read-Rows $textFile)
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

$cardFiles = Get-KindCsvFiles $modRootPath "Data" "Card"
$buffFiles = Get-KindCsvFiles $modRootPath "Data" "Buff"
$relicFiles = Get-KindCsvFiles $modRootPath "Data" "Relic"
$packFiles = Get-KindCsvFiles $modRootPath "Data" "CardPack"
$enemyFiles = Get-KindCsvFiles $modRootPath "Data" "Enemy"

$cards = Add-RowsFromFiles $cardFiles
$buffs = Add-RowsFromFiles $buffFiles
$relics = Add-RowsFromFiles $relicFiles
$packs = Add-RowsFromFiles $packFiles
$enemies = Add-RowsFromFiles $enemyFiles

Test-KindDataTextPairs "Card" $cardFiles $modRootPath
Test-KindDataTextPairs "Buff" $buffFiles $modRootPath
Test-KindDataTextPairs "Relic" $relicFiles $modRootPath
Test-KindDataTextPairs "CardPack" $packFiles $modRootPath

$packIds = @{}
foreach ($packFile in $packFiles) {
    $fileStem = [System.IO.Path]::GetFileNameWithoutExtension($packFile.Name)
    foreach ($pack in (Read-Rows $packFile.FullName)) {
        $packIds[(Full-SunExp-Id $pack.Id $fileStem)] = $true
    }
}
Test-PackRefs "Card" $cards $packIds
Test-PackRefs "Relic" $relics $packIds

foreach ($file in $cardFiles) {
    $rows = Read-Rows $file.FullName
    Test-ResourcePaths "Card" $rows $repoRoot
    Test-ScriptResidue "Data/Card/$($file.Name)" $rows
    $textFile = Get-TextCsvForDataCsv $modRootPath $file
    Test-CardDescriptions $rows (Read-Rows $textFile)
}
foreach ($file in $buffFiles) {
    $rows = Read-Rows $file.FullName
    Test-ResourcePaths "Buff" $rows $repoRoot
    Test-ScriptResidue "Data/Buff/$($file.Name)" $rows
}
foreach ($file in $relicFiles) {
    $rows = Read-Rows $file.FullName
    Test-ResourcePaths "Relic" $rows $repoRoot
    Test-ScriptResidue "Data/Relic/$($file.Name)" $rows
}
foreach ($file in $enemyFiles) {
    $rows = Read-Rows $file.FullName
    Test-ResourcePaths "Enemy" $rows $repoRoot
    Test-EnemyAnimationMapResources $rows $repoRoot
}

$optionalKinds = @("RoleData", "Dialogue", "EventList", "Career", "Map", "EnemyCard", "Partner", "PartnerCard", "Blessing", "EnchTag", "Hard")
foreach ($kind in $optionalKinds) {
    $dataFiles = Get-KindCsvFiles $modRootPath "Data" $kind
    if ($dataFiles.Count -eq 0) {
        continue
    }
    Test-KindDataTextPairs $kind $dataFiles $modRootPath
    foreach ($dataFile in $dataFiles) {
        $dataRows = Read-Rows $dataFile.FullName
        $textRows = Read-Rows (Get-TextCsvForDataCsv $modRootPath $dataFile)
        Test-ScriptResidue "Data/$kind/$($dataFile.Name)" $dataRows
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

Write-Host "SunExp validation passed: cards=$($cards.Count), relics=$($relics.Count), buffs=$($buffs.Count), packs=$($packs.Count), enemies=$($enemies.Count), warnings=$($script:Warnings.Count)."
