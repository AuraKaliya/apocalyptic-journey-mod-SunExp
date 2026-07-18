param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-SourceFiles {
    param([string[]]$Roots)

    foreach ($root in $Roots) {
        $path = Join-Path $repoRoot $root
        if (-not (Test-Path -LiteralPath $path)) {
            continue
        }

        Get-ChildItem -LiteralPath $path -Recurse -File | Where-Object {
            ($_.Extension -in @(".cs", ".csproj")) -and
            ($_.FullName -notmatch "\\bin\\") -and
            ($_.FullName -notmatch "\\obj\\")
        }
    }
}

function Read-RepoText {
    param([string]$RelativePath)

    $path = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required file is missing: $RelativePath"
    }

    return [System.IO.File]::ReadAllText($path)
}

function Assert-NoMatches {
    param(
        [object[]]$Files,
        [string]$Pattern,
        [string]$Message
    )

    $matches = @($Files | Select-String -Pattern $Pattern)
    if ($matches.Count -gt 0) {
        $matches | Select-Object -First 20 | ForEach-Object {
            Write-Host "$($_.Path):$($_.LineNumber): $($_.Line.Trim())"
        }

        throw $Message
    }
}

function Assert-Contains {
    param(
        [string]$Text,
        [string]$Needle,
        [string]$Message
    )

    if (-not $Text.Contains($Needle)) {
        throw $Message
    }
}

function Assert-NotContains {
    param(
        [string]$Text,
        [string]$Needle,
        [string]$Message
    )

    if ($Text.Contains($Needle)) {
        throw $Message
    }
}

$sharedRoots = @(
    "AuraAudioShared",
    "AuraCardUseFxShared",
    "AudioArbiterShared",
    "BattleBgmArbiterShared",
    "AuraCgShared",
    "AuraDirectorShared",
    "AuraDirectorDetour-Dev",
    "AuraJourneyShared",
    "AuraLogShared",
    "AuraModeShared",
    "AuraOnlineShared",
    "AuraSharedCore",
    "AuraSkinShared",
    "AuraUiShared",
    "StarterDeckArbiterShared",
    "UiRaycastSafetyShared",
    "UiTransitionGuardShared"
)

$auraToolsFiles = @(Get-SourceFiles @("AuraToolsExp-Dev"))
$sunExpFiles = @(Get-SourceFiles @("SunExp-Dev"))
$sharedFiles = @(Get-SourceFiles $sharedRoots)

Assert-NoMatches $auraToolsFiles "SunExp-Dev|SunExp\.Dll|using\s+SunExp|CS\.SunExp|SunExpIds|SunExpHook|SunExpUi|SunExpResourceCache" `
    "AuraToolsExp must not depend on SunExp internals."

$auraToolsDirectHooks = @($auraToolsFiles | Where-Object { $_.Name -ne "AuraToolsHookRegistry.cs" })
Assert-NoMatches $auraToolsDirectHooks "AuraSharedHooks\.(RegisterBefore|RegisterAfter)" `
    "AuraTools features must register hooks through AuraToolsHookRegistry."

$sunExpDirectHooks = @($sunExpFiles | Where-Object { $_.Name -ne "SunExpHookRegistry.cs" })
Assert-NoMatches $sunExpDirectHooks "AuraSharedHooks\.(RegisterBefore|RegisterAfter)" `
    "SunExp features must register hooks through SunExpHookRegistry."

Assert-NoMatches $sharedFiles "SunExpIds|SunExp\.Dll|CS\.SunExp|晨星|EndlessSea|SolarMemory|TongtianTower" `
    "Shared runtimes must not contain SunExp content semantics."

Assert-NoMatches $sharedFiles "AuraUiStyleIds\.(SunExp|AuraTools)|SunExp:solar|AuraToolsExp:arcane" `
    "Shared UI runtimes must not own consumer-specific style ids."

Assert-NoMatches $sharedFiles '"(?:AuraToolsExp|SunExp|SkinExp|SanGuoShaExp)\.' `
    "Shared runtimes must not hard-code concrete consumer content owner ids."

$sunExpSkillCg = Read-RepoText "SunExp-Dev\Features\SkillCg\SunExpSkillCgRuntime.cs"
Assert-NotContains $sunExpSkillCg '"AuraToolsExp"' "SunExp SkillCG preload must not hard-code AuraToolsExp ownership."
Assert-Contains $sunExpSkillCg "SunExpIds.ModId" "SunExp SkillCG must keep SunExp-owned preload registration."

$auraToolsSkillCg = Read-RepoText "AuraToolsExp-Dev\Features\SkillCg\AuraToolsSkillCgRuntime.cs"
Assert-Contains $auraToolsSkillCg "AuraBattleLifecycleRouter.Register" "AuraTools SkillCG must use the shared battle lifecycle router."
Assert-NotContains $auraToolsSkillCg 'RegisterBefore("GameEntryUI.StartGame"' "AuraTools SkillCG must not own a private adventure-start hook."

$auraToolsSkillCgSettings = Read-RepoText "AuraToolsExp\Config\SkillCgSettings.json"
Assert-NotContains $auraToolsSkillCgSettings '"SunExp' "AuraTools SkillCG defaults must not embed SunExp content semantics."

$starterDeck = (Get-ChildItem -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Features\StarterDeck") -Recurse -Filter "*.cs" |
    Sort-Object FullName |
    ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }) -join "`n"
Assert-Contains $starterDeck "EvaluateStarterDeckMutation" "AuraTools starter deck must consume the generic active-mode policy decision."
Assert-NotContains $starterDeck "SolarMemory" "AuraTools starter deck must not branch on a content-owned semantic mode."

$damageMeterMode = (Get-ChildItem -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Features\DamageMeter") -Recurse -Filter "*.cs" |
    Sort-Object FullName |
    ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }) -join "`n"
Assert-Contains $damageMeterMode "AuraModeRuntime.Current" "DamageMeter must resolve semantic mode labels through AuraMode shared state."
Assert-NotContains $damageMeterMode "SolarMemory" "DamageMeter must not hard-code a content-owned semantic mode."

$damageMeter = $damageMeterMode
$damageMeterCapture = Read-RepoText "AuraToolsExp-Dev\Features\DamageMeter\Capture\DamageCaptureCoordinator.cs"
$damageMeterLifecycle = Read-RepoText "AuraToolsExp-Dev\Features\DamageMeter\DamageMeterLifecycleCoordinator.cs"
Assert-Contains $damageMeter "AuraBattleLifecycleRouter.Register" "DamageMeter battle hooks must use the shared battle lifecycle router."
Assert-NotContains $damageMeter 'RegisterBefore("FightInit.Init"' "DamageMeter must not own a private fight-init hook when shared lifecycle exists."
Assert-Contains $damageMeter 'RegisterAfter("DamageText.Create", AfterDamageTextCreate)' "DamageMeter must observe DamageText.Create only after native command creation completes."
Assert-NotContains $damageMeter 'RegisterBefore("DamageText.Create"' "DamageMeter must not put native damage text creation behind a before-hook."
Assert-Contains $damageMeter 'RegisterAfter("DamageText.InternalExecute", AfterDamageTextInternalExecute)' "DamageMeter diagnostics must observe native damage-text command execution."
Assert-Contains $damageMeter 'RegisterAfter("FightUI.EnqueueDamageText", AfterFightUiEnqueueDamageText)' "DamageMeter diagnostics must observe the native damage-text UI queue."
Assert-Contains $damageMeter "DamageMeterHookContextMapper.MapDamageText" "DamageMeter hook arguments must be mapped at the adapter boundary."
Assert-NotContains $damageMeterCapture "ModHookContext" "DamageMeter capture must not depend on raw hook contexts."
Assert-NotContains $damageMeterLifecycle "ModHookContext" "DamageMeter lifecycle must not depend on raw hook contexts."
Assert-Contains $damageMeter "if (AuraToolsPerformanceSettings.DiagnosticsEnabled)" "Pure damage-text diagnostics hooks must not register during normal gameplay."

$sharedRuntimeProject = Read-RepoText "AuraSharedRuntime-Dev\Aura.Shared.csproj"
Assert-Contains $sharedRuntimeProject "..\AuraUiShared\*.cs" "AuraUiShared must be packaged into Aura.Shared.dll."
Assert-Contains $sharedRuntimeProject "..\AuraModeShared\*.cs" "AuraModeShared must be packaged into Aura.Shared.dll."

$auraToolsResourceFiles = @($auraToolsFiles | Where-Object { $_.Name -ne "AuraToolsResourceCache.cs" })
Assert-NoMatches $auraToolsResourceFiles "ResourceLoader\.Load(?:All)?<" `
    "AuraToolsExp feature resource loads must route through AuraToolsResourceCache."

Write-Host "Content/tool/shared boundary assertions passed."
