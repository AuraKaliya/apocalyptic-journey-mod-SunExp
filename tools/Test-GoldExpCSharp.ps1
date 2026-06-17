param(
    [string]$Configuration = "Release",
    [string]$GamePath = "D:\Steam\steamapps\common\Witch's Apocalyptic Journey",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Read-Utf8 {
    param([string]$Path)
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

$repoRoot = Get-RepoRoot

if (-not $SkipBuild) {
    & (Join-Path $repoRoot "tools\Build-GoldExpDll.ps1") -Configuration $Configuration -GamePath $GamePath | Out-Host
}

$ids = Read-Utf8 (Join-Path $repoRoot "GoldExp-Dev\Infrastructure\GoldExpIds.cs")
$entry = Read-Utf8 (Join-Path $repoRoot "GoldExp-Dev\Entry.cs")
$service = Read-Utf8 (Join-Path $repoRoot "GoldExp-Dev\Mechanics\GoldDreamService.cs")
$executorApi = Read-Utf8 (Join-Path $repoRoot "GoldExp-Dev\GameApi\ExecutorApi.cs")
$cardApi = Read-Utf8 (Join-Path $repoRoot "GoldExp-Dev\GameApi\CardApi.cs")
$cardConfigApi = Read-Utf8 (Join-Path $repoRoot "GoldExp-Dev\GameApi\CardConfigApi.cs")
$buffApi = Read-Utf8 (Join-Path $repoRoot "GoldExp-Dev\GameApi\BuffApi.cs")
$playerApi = Read-Utf8 (Join-Path $repoRoot "GoldExp-Dev\GameApi\PlayerApi.cs")
$goldDreamRuntime = Read-Utf8 (Join-Path $repoRoot "GoldExp-Dev\Hooks\GoldDreamTagRuntime.cs")
$cards = Read-Utf8 (Join-Path $repoRoot "GoldExp-Dev\Scripting\CardScripts.cs")
$enchTags = Read-Utf8 (Join-Path $repoRoot "GoldExp-Dev\Scripting\EnchTagScripts.cs")
$career = Read-Utf8 (Join-Path $repoRoot "GoldExp-Dev\Scripting\GoldWitchScripts.cs")
$relics = Read-Utf8 (Join-Path $repoRoot "GoldExp-Dev\Scripting\RelicScripts.cs")
$partner = Read-Utf8 (Join-Path $repoRoot "GoldExp-Dev\Scripting\PartnerScripts.cs")
$cardData = Read-Utf8 (Join-Path $repoRoot "GoldExp\Data\Card\goldexp.csv")
$enchTagData = Read-Utf8 (Join-Path $repoRoot "GoldExp\Data\EnchTag\goldexp.csv")
$enchTagText = Read-Utf8 (Join-Path $repoRoot "GoldExp\Text\EnchTag\goldexp.csv")
$careerData = Read-Utf8 (Join-Path $repoRoot "GoldExp\Data\Career\goldwitch.csv")
$roleData = Read-Utf8 (Join-Path $repoRoot "GoldExp\Data\RoleData\goldwitch.csv")
$partnerData = Read-Utf8 (Join-Path $repoRoot "GoldExp\Data\Partner\goldexp.csv")
$relicData = Read-Utf8 (Join-Path $repoRoot "GoldExp\Data\Relic\goldexp.csv")
$relicText = Read-Utf8 (Join-Path $repoRoot "GoldExp\Text\Relic\goldexp.csv")
$removedRelicDesign = Read-Utf8 (Join-Path $repoRoot "GoldExp\Design\removed-relics.md")
$buffData = Read-Utf8 (Join-Path $repoRoot "GoldExp\Data\Buff\goldexp.csv")
$cardText = Read-Utf8 (Join-Path $repoRoot "GoldExp\Text\Card\goldexp.csv")
$buffText = Read-Utf8 (Join-Path $repoRoot "GoldExp\Text\Buff\goldexp.csv")
$keywordText = Read-Utf8 (Join-Path $repoRoot "GoldExp\Text\KeyWordsDic\goldexp.csv")

Assert-True $ids.Contains('public const string FalseGold = "GoldExp_goldexp_false_gold";') "FalseGold full id is missing or changed."
Assert-True $ids.Contains('public const string Debt = "GoldExp_goldexp_debt";') "Debt full id is missing or changed."
Assert-True $ids.Contains('public const string DebtDue = "GoldExp_goldexp_debt_1";') "DebtDue full id is missing or changed."
Assert-True $ids.Contains('public const string DebtNext = "GoldExp_goldexp_debt_2";') "DebtNext full id is missing or changed."
Assert-True $ids.Contains('public const string DebtLater = "GoldExp_goldexp_debt_3";') "DebtLater full id is missing or changed."
Assert-True $ids.Contains('public const string GoldenPotential = "GoldExp_goldexp_golden_potential";') "GoldenPotential full id is missing or changed."
Assert-True $ids.Contains('public const string GoldDreamTag = "') "Golden Dream tag constant is missing."
Assert-True $ids.Contains('public const string TempGoldDream = "GoldExpTempGoldDream";') "Temporary Golden Dream marker is missing."
Assert-True $entry.Contains("GoldDreamTagRuntime.Initialize();") "Gold Dream runtime hook must be initialized at mod startup."
Assert-True $service.Contains("Math.Min(8, Math.Max(0, PlayerApi.GetMoney() / 60))") "Opening False Gold must be capped and tied to current gold."
Assert-True $service.Contains("current > previous && ExecutorApi.CombatIntGet(GoldExpIds.FirstGoldGainDone) == 0") "First real-gold-gain gate is missing."
Assert-True $service.Contains("var falseGold = FalseGold(self);") "Gold payment must inspect False Gold first."
Assert-True $service.Contains("CanPayGold(ScriptExecutor self, int amount)") "Gold payment must expose a False Gold plus real gold affordability check."
Assert-True $service.Contains("TryCanPayGold(ScriptExecutor self, int amount, out bool canPay)") "Gold payment must expose a preview-safe affordability check."
Assert-True $service.Contains("FalseGold(self) + money >= amount") "Gold payment must allow False Gold plus real gold."
Assert-True $playerApi.Contains("TryGetMoney(out int money)") "Player money reads must support preview-safe failure."
Assert-True $playerApi.Contains("read skipped") "Player money reflection failures must be swallowed during preview."
Assert-True $service.Contains("PayRealGoldUpTo(ScriptExecutor self, int maxAmount)") "Actual-gold-only spending helper is missing."
Assert-True $service.Contains("DynamicWagerLimit()") "Wager dynamic real-gold limit helper is missing."
Assert-True $service.Contains("PlayerApi.GetMoney() / 10 + 100") "Wager limit must be current real gold * 10% + 100."
Assert-True $service.Contains("HasGoldenPotential(ScriptExecutor self)") "Golden Potential condition helper is missing."
Assert-True $service.Contains("ApplyGoldenPotentialAtFightStart()") "Golden Potential fight-start helper is missing."
Assert-True $buffApi.Contains("Clear(IStatusManager? status, string buffId)") "Buff API must expose a direct status clear helper."
Assert-True $service.Contains("BuffApi.Clear(FightPlayer.Instance?.Status, GoldExpIds.GoldenPotential);") "Golden Potential must be re-evaluated cleanly at fight start."
Assert-True $service.Contains("PlayerApi.GetMoney() <= 2000") "Golden Potential must only be granted above 2000 real gold."
Assert-True $service.Contains("FightPlayer.Instance?.Status?.AddBuff(GoldExpIds.GoldenPotential, 1)") "Golden Potential must be granted on the player at fight start."
Assert-True $service.Contains("var falseSpend = ConsumeFalseGold(self, Math.Min(falseGold, amount));") "Debt settlement must spend False Gold first."
Assert-True $service.Contains("var realSpend = Math.Min(money, remaining);") "Debt settlement must spend real gold after False Gold."
Assert-True $service.Contains("var canFullyPay = falseGold + money >= amount;") "Debt failure must consider False Gold plus real gold."
Assert-True $service.Contains("AddDebtWithCountdown(self, 3, amount);") "New Debt must enter the 3-round countdown bucket."
Assert-True $service.Contains("SetAllDebtCountdownToOne") "Golden Dreamland debt acceleration helper is missing."
Assert-True $service.Contains('self.SetPower("0");') "Debt failure must clear Mana."
Assert-True $service.Contains("ConsumeAllFalseGold(self);") "End combat must clear False Gold."
Assert-True $service.Contains("IncreaseFalseGoldByPercent(ScriptExecutor self, int percent)") "Golden Dream percentage helper is missing."
Assert-True $service.Contains("(current * percent + 99) / 100") "Golden Dream percentage gain should round up."
Assert-True $service.Contains("IncreaseDebtByPercent(ScriptExecutor self, int percent)") "Golden Dream debt percentage helper is missing."
Assert-True $service.Contains("var current = Debt(self);") "Golden Dream debt percentage helper must total all Debt countdown buffs."
Assert-True $service.Contains("HandleGoldDreamCardPlayed(ScriptExecutor self, string source)") "Golden Dream play handler is missing."
Assert-True $service.Contains("IncreaseFalseGoldByPercent(self, 10)") "Golden Dream must increase False Gold by 10%."
Assert-True $service.Contains("IncreaseDebtByPercent(self, 10)") "Golden Dream must increase total Debt by 10%."
Assert-True (-not $service.Contains("IncreaseFalseGoldByPercent(self, 20)")) "Golden Dream must no longer increase False Gold by 20%."
Assert-True $service.Contains("SettleFalseGoldToRealGold(ScriptExecutor self, int numerator, int denominator)") "Golden Dreamland needs ratio-based False Gold settlement."
Assert-True $service.Contains("amount * Math.Max(0, numerator) / denominator") "Ratio-based settlement must scale False Gold before gaining real gold."
Assert-True $service.Contains('CardApi.RefreshUsableByLocalId(self, "fortune_throw", CanPayGold(self, 1000));') "Fortune Throw usability must refresh after GoldExp gold-state changes."
Assert-True $executorApi.Contains('DealDamageRandomEnemy(ScriptExecutor? executor, int amount, string damageType = "")') "Random damage helper must accept a damage type."
Assert-True $executorApi.Contains("DealDamage(executor, amount, damageType);") "Random damage helper must forward the damage type."
Assert-True $cardApi.Contains('DictionaryUtil.Set(card.Vars, GoldExpIds.TempGoldDream, "1");') "Gilding must mark temporary Golden Dream tags."
Assert-True $cardApi.Contains("AddCardToHand(ScriptExecutor self, string cardId, string addTag)") "Tagged card creation helper is missing."
Assert-True $cardApi.Contains("CreateCardInHand(ScriptExecutor self, string cardId, string addTag)") "Direct-to-hand card creation helper is missing."
Assert-True $cardApi.Contains("self.CreateCard(dataConfig);") "Direct-to-hand card creation must call ScriptExecutor.CreateCard."
Assert-True $cardConfigApi.Contains("HasNativeGoldDream") "Gold Dream config native tag detector is missing."
Assert-True $cardConfigApi.Contains("TryClaimTemporaryGoldDream") "Temporary Golden Dream claim guard is missing."
Assert-True $goldDreamRuntime.Contains('AddEventListener("Action" + statusId') "Gold Dream runtime must listen for actual Action events."
Assert-True $goldDreamRuntime.Contains('AddEventListener("ActionAfter" + statusId') "Gold Dream runtime must resolve after actual card use."
Assert-True $goldDreamRuntime.Contains("GoldDreamService.HandleGoldDreamCardPlayed") "Gold Dream runtime must call the play handler."
Assert-True $goldDreamRuntime.Contains("GoldDreamService.ApplyGoldenPotentialAtFightStart();") "Gold Dream runtime must apply Golden Potential at fight start."
Assert-True $cards.Contains("CardApi.EnsureHandTags(self, GoldExpIds.GoldDreamTag);") "Gilding must add Golden Dream to the current hand."
Assert-True $cards.Contains('ExecutorApi.AddDescription(self, "1", "Money", GoldDreamService.DynamicWagerLimit());') "Wager must show its dynamic real-gold limit on the card."
Assert-True $cards.Contains("GoldDreamService.PayRealGoldUpTo(self, GoldDreamService.DynamicWagerLimit());") "Wager must spend up to its dynamic real-gold limit."
Assert-True (-not $cards.Contains("PayGold(self, 500")) "Wager must not spend False Gold through PayGold."
Assert-True (-not $cards.Contains('self.DrawCount("1");')) "Wager must no longer draw a card."
Assert-True $cards.Contains("GoldDreamService.TryCanPayGold(self, 1000, out var canPay)") "Fortune Throw init must avoid reading unavailable player money during previews."
Assert-True $cards.Contains("SetUsable(self, canPay);") "Fortune Throw must set Usable from False Gold plus real gold when available."
Assert-True $cards.Contains('ExecutorApi.AddDescription(self, "1", "TrueDamage", FortuneThrowDamage(self));') "Fortune Throw must display its Ascension-scaled true damage."
Assert-True $cards.Contains('ExecutorApi.AddDescription(self, "2", "Value", FortuneThrowCheckValue);') "Fortune Throw must display its check value."
Assert-True $cards.Contains("GoldDreamService.PayGold(self, 1000)") "Fortune Throw must pay 1000 gold."
Assert-True $cards.Contains("private const int FortuneThrowCheckValue = 50;") "Fortune Throw must use Check 50."
Assert-True $cards.Contains("self.CheckDice.Roll().Value") "Fortune Throw must use the game CheckDice system."
Assert-True $cards.Contains("check >= FortuneThrowCheckValue") "Fortune Throw check success must compare against Check 50."
Assert-True $cards.Contains("check > 100") "Fortune Throw must honor critical check extra trigger semantics."
Assert-True (-not $cards.Contains("UnityEngine.Random.value < 0.5f")) "Fortune Throw must not use ad hoc random chance."
Assert-True $cards.Contains("private const int FortuneThrowBaseDamage = 3;") "Fortune Throw must start at 3 true damage per hit."
Assert-True $cards.Contains("private const int FortuneThrowDamageStep = 3;") "Fortune Throw Ascension must add 3 true damage per hit."
Assert-True $cards.Contains('ExecutorApi.DealDamageRandomEnemy(self, FortuneThrowDamage(self), "True");') "Fortune Throw must deal Ascension-scaled true damage on success."
Assert-True $cards.Contains("AscendFortuneThrow(self);") "Fortune Throw must Ascend after a paid use."
Assert-True $cards.Contains('CardApi.CreateCardInHand(self, GoldExpIds.WagerCardId, "Burnout");') "Display Wealth must create Burnout Wagers directly in hand."
Assert-True $cards.Contains("GoldDreamService.AddFalseGold(self, 1000);") "Blank Check must gain 1000 False Gold."
Assert-True $cards.Contains("GoldDreamService.AddDebt(self, 2000);") "Blank Check must gain 2000 Debt."
Assert-True $cards.Contains("GoldDreamService.HasGoldenPotential(self)") "Blank Check draw and Mana restore must require Golden Potential."
Assert-True $cards.Contains("GoldDreamService.SettleFalseGoldToRealGold(self, 1, 2);") "Golden Dreamland must settle False Gold at a 50% rate."
Assert-True $cards.Contains("GoldDreamService.SetAllDebtCountdownToOne(self);") "Golden Dreamland must set Debt countdowns to 1."
Assert-True $enchTags.Contains("runtime ActionAfter hook resolves the effect") "Golden Dream EnchTag script should delegate to the runtime hook."
Assert-True (-not $enchTags.Contains("GoldDreamService.IncreaseFalseGoldByPercent(self, 20);")) "Golden Dream EnchTag UseScript must not double-trigger the 20% effect."
Assert-True $career.Contains("PlayerApi.SetSkillTime(key, cooldown);") "Active skill cooldown write is missing."
Assert-True $career.Contains("GoldDreamService.SettleDebt(self, (debt + 1) / 2") "Golden Audit must settle half Debt."
Assert-True (-not $relics.Contains("old_king_coin")) "Old King's Coin script branch must be removed from GoldExp relic scripts."
Assert-True (-not $relics.Contains("bankruptcy_contract")) "Bankruptcy Contract script branch must be removed from GoldExp relic scripts."
Assert-True $partner.Contains("GoldDreamService.ConsumeFalseGold(self, 2);") "Midas Raven must spend False Gold."
Assert-True $cardData.Contains("GoldExp_goldexp_cardpack_gold_dream") "Cards must belong to the Gold Dream card pack."
Assert-True $cardData.Contains('"fortune_throw","3","0"') "Fortune Throw must be rarity 3 and 0-cost."
Assert-True $cardData.Contains('"fortune_throw","3","0","Recycle,') "Fortune Throw must use the official Recycle tag id."
Assert-True $cardData.Contains("Recycle,Ascension,") "Fortune Throw must use the official Ascension tag id."
Assert-True ([regex]::IsMatch($cardData, '"fortune_throw","3","0","Recycle,[^"]*".*,"","","GoldExp_goldexp_cardpack_gold_dream"')) "Fortune Throw must not request a target."
Assert-True (-not $cardData.Contains((-join ([char[]](0x56DE, 0x8F6C))) + ",")) "Fortune Throw must not use the localized Recycle label as a runtime tag."
Assert-True $cardData.Contains('"gilded_amulet","3","1"') "Gilding must be rarity 3 and 1-cost."
Assert-True $cardData.Contains('"gold_dream_wager","1","0"') "Wager must be 0-cost."
Assert-True $cardData.Contains('"golden_age","3","3"') "Golden Dreamland must cost 3."
Assert-True (-not $enchTagData.Contains('CS.GoldExp.Dll.Scripting.EnchTagScripts.Use(self, ""gold_dream_keyword"");')) "Golden Dream EnchTag UseScript must stay empty because the runtime hook resolves it."
Assert-True $enchTagText.Contains("10%") "Golden Dream EnchTag text must mention 10%."
Assert-True $enchTagText.Contains("GoldExp_goldexp_false_gold") "Golden Dream EnchTag text must mention False Gold."
Assert-True $enchTagText.Contains("GoldExp_goldexp_debt_3") "Golden Dream EnchTag text must mention the 3-round Debt display buff."
Assert-True (-not $enchTagText.Contains("20%")) "Golden Dream EnchTag text must no longer mention 20%."
Assert-True $buffData.Contains('"false_gold"') "False Gold buff row is missing."
Assert-True $buffData.Contains('"debt_1"') "Debt countdown 1 buff row is missing."
Assert-True $buffData.Contains('"debt_2"') "Debt countdown 2 buff row is missing."
Assert-True $buffData.Contains('"debt_3"') "Debt countdown 3 buff row is missing."
Assert-True $buffData.Contains('"golden_potential"') "Golden Potential buff row is missing."
Assert-True ([regex]::IsMatch($buffData, '"false_gold".*"Icon/Buff/')) "False Gold must use an official buff icon."
Assert-True ([regex]::Matches($buffData, '"debt_[123]".*"Icon/Buff/').Count -eq 3) "Debt must use an official buff icon."
Assert-True ([regex]::IsMatch($buffData, '"golden_potential".*"Icon/Buff/')) "Golden Potential must use an official buff icon."
Assert-True $cardText.Contains("GoldExp_goldexp_debt_3") "Golden Dreamland must reference the 3-round Debt buff."
Assert-True ([regex]::IsMatch($cardText, '"blank_check".*GoldExp_goldexp_debt_3')) "Blank Check must reference the 3-round Debt buff."
Assert-True (-not [regex]::IsMatch($cardText, '"blank_check".*GoldExp_goldexp_debt[},]')) "Blank Check must not reference the concept-only Debt id."
$wagerTextLine = ($cardText -split "\r?\n" | Where-Object { $_.StartsWith('"gold_dream_wager"') } | Select-Object -First 1)
$drawOneZh = -join ([char[]](0x62BD, 0x0031))
$drawOneJa = -join ([char[]](0x0031, 0x679A))
Assert-True $wagerTextLine.Contains("{0}") "Wager text must use the dynamic gold amount placeholder."
Assert-True (-not $wagerTextLine.Contains($drawOneZh)) "Wager text must no longer mention drawing a card."
Assert-True (-not $wagerTextLine.Contains("draw 1")) "Wager English text must no longer mention drawing a card."
Assert-True (-not $wagerTextLine.Contains($drawOneJa)) "Wager Japanese text must no longer mention drawing a card."
Assert-True ([regex]::IsMatch($cardText, '(?m)^"fortune_throw"[^\r\n]*1000')) "Fortune Throw text must mention paying 1000 gold."
$trueDamageZh = -join ([char[]](0x771F, 0x5B9E, 0x4F24, 0x5BB3))
$ascensionZh = -join ([char[]](0x98DE, 0x5347))
$checkZh = -join ([char[]](0x68C0, 0x5B9A))
Assert-True ([regex]::IsMatch($cardText, '(?m)^"fortune_throw"[^\r\n]*' + $trueDamageZh)) "Fortune Throw text must mention true damage."
Assert-True ([regex]::IsMatch($cardText, '(?m)^"fortune_throw"[^\r\n]*\{0\}')) "Fortune Throw text must display its dynamic true damage."
Assert-True ([regex]::IsMatch($cardText, '(?m)^"fortune_throw"[^\r\n]*\{1\}')) "Fortune Throw text must display its dynamic check value."
Assert-True ([regex]::IsMatch($cardText, '(?m)^"fortune_throw"[^\r\n]*' + $checkZh)) "Fortune Throw text must mention checks."
Assert-True ([regex]::IsMatch($cardText, '(?m)^"fortune_throw"[^\r\n]*' + $ascensionZh)) "Fortune Throw text must mention Ascension."
Assert-True ([regex]::IsMatch($cardText, '(?m)^"fortune_throw"[^\r\n]*Check')) "Fortune Throw English text must mention checks."
Assert-True ([regex]::IsMatch($cardText, '(?m)^"fortune_throw"[^\r\n]*Ascension')) "Fortune Throw English text must mention Ascension."
Assert-True ([regex]::IsMatch($cardText, '(?m)^"blank_check"[^\r\n]*GoldExp_goldexp_golden_potential')) "Blank Check must reference Golden Potential."
Assert-True ([regex]::IsMatch($cardText, '(?m)^"golden_age"[^\r\n]*50%')) "Golden Dreamland text must mention 50% settlement."
$goldenAgeTextLine = ($cardText -split "\r?\n" | Where-Object { $_.StartsWith('"golden_age"') } | Select-Object -First 1)
Assert-True $goldenAgeTextLine.Contains("GoldExp_goldexp_debt_3") "Golden Dreamland text must display the 3-round Debt buff."
Assert-True $cardText.Contains((-join ([char[]](0x8C6A, 0x63B7, 0x5343, 0x91D1)))) "Fortune Throw display name must be renamed."
Assert-True (-not $cardText.Contains((-join ([char[]](0x4E7E, 0x5764, 0x4E00, 0x63B7))))) "Old Fortune Throw display name should not remain."
Assert-True $buffText.Contains((-join ([char[]](0x3010, 0x0031, 0x3011, 0x56DE, 0x5408, 0x540E, 0x8FDB, 0x5165, 0x7ED3, 0x7B97, 0x3002)))) "Debt 1 text must only describe its countdown."
Assert-True $buffText.Contains((-join ([char[]](0x3010, 0x0032, 0x3011, 0x56DE, 0x5408, 0x540E, 0x8FDB, 0x5165, 0x7ED3, 0x7B97, 0x3002)))) "Debt 2 text must only describe its countdown."
Assert-True $buffText.Contains((-join ([char[]](0x3010, 0x0033, 0x3011, 0x56DE, 0x5408, 0x540E, 0x8FDB, 0x5165, 0x7ED3, 0x7B97, 0x3002)))) "Debt 3 text must only describe its countdown."
Assert-True $buffText.Contains("Golden Potential") "Golden Potential text must be registered."
Assert-True (-not $buffText.Contains((-join ([char[]](0x91D1, 0x5E01, 0x4E0D, 0x8DB3, 0x5219, 0x6E05, 0x7A7A, 0x624B, 0x724C, 0x548C, 0x9B54, 0x80FD))))) "Debt buff text should not include settlement failure rules."
Assert-True $keywordText.Contains("[Wager] spends real gold only") "False Gold keyword must exclude Wager from False Gold spending."
Assert-True $keywordText.Contains("spend False Gold first, then real gold") "Debt keyword must describe False Gold-first settlement."
Assert-True $keywordText.Contains("discard your hand and clear Mana") "Debt keyword must describe the new failure settlement."
Assert-True (-not $careerData.Contains("GoldExp_goldwitch_goldwitch_midas_contract")) "Gold Witch career is temporarily hidden and should not register skill 1."
Assert-True (-not $careerData.Contains("GoldExp_goldwitch_goldwitch_final_audit")) "Gold Witch career is temporarily hidden and should not register skill 2."
Assert-True (-not [regex]::IsMatch($careerData, "(?m)^goldwitch,")) "Gold Witch career data row should be hidden."
Assert-True (-not [regex]::IsMatch($roleData, "(?m)^goldwitch,")) "Gold Witch role data row should be hidden."
Assert-True (-not [regex]::IsMatch($partnerData, "(?m)^midas_raven,")) "Midas Raven partner data row should be hidden."
Assert-True (-not $relicData.Contains("old_king_coin")) "Old King's Coin must not be registered in GoldExp Data/Relic."
Assert-True (-not $relicData.Contains("bankruptcy_contract")) "Bankruptcy Contract must not be registered in GoldExp Data/Relic."
Assert-True (-not $relicText.Contains("old_king_coin")) "Old King's Coin must not be registered in GoldExp Text/Relic."
Assert-True (-not $relicText.Contains("bankruptcy_contract")) "Bankruptcy Contract must not be registered in GoldExp Text/Relic."
Assert-True $removedRelicDesign.Contains("old_king_coin") "Old King's Coin design must be preserved outside game-loaded CSV."
Assert-True $removedRelicDesign.Contains("bankruptcy_contract") "Bankruptcy Contract design must be preserved outside game-loaded CSV."

Write-Host "GoldExp C# source assertions passed."
