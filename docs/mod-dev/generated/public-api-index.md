# Generated Public API Index

Generated from `*-Dev` C# projects. Refresh with `tools\Export-ModDevDocs.ps1`.

## BackgroundAudioReplaceExp-Dev

### `BackgroundAudioReplaceExp-Dev/Entry.cs`

- `public static class Entry`
- `public static void Initialize(ModConfig modConfig)`

### `BackgroundAudioReplaceExp-Dev/Hooks/BackgroundBattleMusicRuntime.cs`

- `public static class BackgroundBattleMusicRuntime`
- `public static void Initialize(ModConfig modConfig)`

## CardPackExp-Dev

### `CardPackExp-Dev/Entry.cs`

- `public static class Entry`
- `public static void Initialize(ModConfig modConfig)`

### `CardPackExp-Dev/Hooks/CardPackSelectionRuntime.cs`

- `public static class CardPackSelectionRuntime`
- `public static void Initialize(ModConfig modConfig)`

### `CardPackExp-Dev/Hooks/StarterDeckRuntime.cs`

- `public static class StarterDeckRuntime`
- `public static void Initialize(ModConfig modConfig)`
- `public static void CaptureSelectedPacks(IEnumerable<string> packs)`
- `public static void MarkPending(RoleTable roleTable, string source)`

### `CardPackExp-Dev/Infrastructure/CardPackExpIds.cs`

- `public static class CardPackExpIds`

### `CardPackExp-Dev/Infrastructure/CardPackExpLog.cs`

- `public static class CardPackExpLog`
- `public static void Info(string message)`
- `public static void Warn(string message)`
- `public static void Error(string message, Exception? exception = null)`

## CardUseCialloExp-Dev

### `CardUseCialloExp-Dev/Entry.cs`

- `public static class Entry`
- `public static void Initialize(ModConfig modConfig)`

### `CardUseCialloExp-Dev/Hooks/CardUseSoundRuntime.cs`

- `public static class CardUseSoundRuntime`
- `public static void Initialize(ModConfig modConfig)`

## GoldExp-Dev

### `GoldExp-Dev/Entry.cs`

- `public static class Entry`
- `public static void Initialize(ModConfig modConfig)`

### `GoldExp-Dev/GameApi/BuffApi.cs`

- `public static class BuffApi`
- `public static int Level(IStatusManager? status, string buffId)`
- `public static bool Has(IStatusManager? status, string buffId)`
- `public static void Clear(IStatusManager? status, string buffId)`

### `GoldExp-Dev/GameApi/CardApi.cs`

- `public static class CardApi`
- `public static void AddCardToHand(ScriptExecutor self, string cardId)`
- `public static void AddCardToHand(ScriptExecutor self, string cardId, string addTag)`
- `public static void CreateCardInHand(ScriptExecutor self, string cardId, string addTag)`
- `public static int DiscardAllHand(ScriptExecutor self)`
- `public static int EnsureHandTags(ScriptExecutor self, params string[] tags)`
- `public static int RefreshUsableByLocalId(ScriptExecutor self, string localId, bool usable)`
- `public static bool EnsureCardTag(CardItem card, string tag)`

### `GoldExp-Dev/GameApi/CardConfigApi.cs`

- `public static class CardConfigApi`
- `public static IDataConfig? FromActionPayload(object? payload)`
- `public static string Id(IDataConfig? config)`
- `public static bool HasNativeGoldDream(IDataConfig? config)`
- `public static bool HasTemporaryGoldDream(IDataConfig? config)`
- `public static bool HasSpecialGoldDream(IDataConfig? config)`
- `public static bool TryClaimTemporaryGoldDream(IDataConfig config)`

### `GoldExp-Dev/GameApi/ExecutorApi.cs`

- `public static class ExecutorApi`
- `public static string GetVar(ScriptExecutor? executor, string key, string fallback = "")`
- `public static void SetVar(ScriptExecutor? executor, string key, object value)`
- `public static int CombatIntGet(string key, int fallback = 0)`
- `public static int CombatIntSet(string key, int value)`
- `public static int CombatIntAdd(string key, int delta)`
- `public static void SetBaseScript(ScriptExecutor executor, string baseScript, bool canSelf = true)`
- `public static bool TryAddEvent(ScriptExecutor? executor, string eventName, Action script, string context = "")`
- `public static int SelfBuffLevel(ScriptExecutor? executor, string buffId)`
- `public static bool DealDamage(ScriptExecutor? executor, int amount, string damageType = "")`
- `public static List<IStatusManager> EnemyTargets(ScriptExecutor? executor)`
- `public static IStatusManager? RandomEnemyTarget(ScriptExecutor? executor)`
- `public static bool SetStatusForTarget(ScriptExecutor? executor, IStatusManager? target, string fallbackStatus = "Self")`
- `public static void DealDamageAllEnemies(ScriptExecutor? executor, int amount, string damageType = "")`
- `public static void DealDamageRandomEnemy(ScriptExecutor? executor, int amount, string damageType = "")`
- `public static int RemoveSelfBuffStacks(ScriptExecutor? executor, string buffId, int amount)`
- `public static void AddDescription(ScriptExecutor? executor, string index, string type, int amount)`

### `GoldExp-Dev/GameApi/PlayerApi.cs`

- `public static class PlayerApi`
- `public static int GetMoney()`
- `public static bool TryGetMoney(out int money)`
- `public static bool SetMoney(int value)`
- `public static bool AddMoneyRaw(int amount)`
- `public static int GetSkillTime(string key)`
- `public static void SetSkillTime(string key, int value)`
- `public static void SetGameVar(string key, string value)`
- `public static string GetGameVar(string key, string fallback = "")`
- `public static void ShowCaption(string text)`

### `GoldExp-Dev/Hooks/GoldDreamTagRuntime.cs`

- `public static class GoldDreamTagRuntime`
- `public static void Initialize()`
- `public static void OnFightStart(Fight_Start __instance)`
- `public static void BeforeCommonTrueUse(CommonCardItem __instance)`
- `public static void BeforeAttackTrueUse(AttackCardItem __instance)`

### `GoldExp-Dev/Infrastructure/DictionaryUtil.cs`

- `public static class DictionaryUtil`
- `public static string Get(IDictionary<string, string>? values, string key, string fallback = "")`
- `public static void Set(IDictionary<string, string>? values, string key, object value)`
- `public static int ParseInt(string? value, int fallback = 0)`
- `public static bool ContainsToken(string? value, string token)`

### `GoldExp-Dev/Infrastructure/GoldExpIds.cs`

- `public static class GoldExpIds`

### `GoldExp-Dev/Infrastructure/GoldExpLog.cs`

- `public static class GoldExpLog`
- `public static void Info(string message)`
- `public static void Warn(string message)`
- `public static void Error(string message, Exception? exception = null)`
- `public static void Debug(string message)`

### `GoldExp-Dev/Mechanics/GoldDreamService.cs`

- `public static class GoldDreamService`
- `public static int FalseGold(ScriptExecutor self)`
- `public static int Debt(ScriptExecutor self)`
- `public static int DebtDue(ScriptExecutor self)`
- `public static int DebtNext(ScriptExecutor self)`
- `public static int DebtLater(ScriptExecutor self)`
- `public static int DynamicWagerLimit()`
- `public static bool HasGoldenPotential(ScriptExecutor self)`
- `public static void ApplyGoldenPotentialAtFightStart()`
- `public static void EnsureCombatHooks(ScriptExecutor self)`
- `public static void AddFalseGold(ScriptExecutor self, int amount, bool countAsRoundGain = true)`
- `public static void AddDebt(ScriptExecutor self, int amount)`
- `public static void AddDebtWithCountdown(ScriptExecutor self, int countdown, int amount)`
- `public static int ConsumeFalseGold(ScriptExecutor self, int amount)`
- `public static int ConsumeAllFalseGold(ScriptExecutor self)`
- `public static bool PayGold(ScriptExecutor self, int amount)`
- `public static bool CanPayGold(ScriptExecutor self, int amount)`
- `public static bool TryCanPayGold(ScriptExecutor self, int amount, out bool canPay)`
- `public static int PayRealGoldUpTo(ScriptExecutor self, int maxAmount)`
- `public static bool PayRealGold(ScriptExecutor self, int amount)`
- `public static void GainGold(ScriptExecutor self, int amount, bool trackMoney = true)`
- `public static int SettleFalseGoldToRealGold(ScriptExecutor self)`
- `public static int SettleFalseGoldToRealGold(ScriptExecutor self, int numerator, int denominator)`
- `public static int IncreaseFalseGoldByPercent(ScriptExecutor self, int percent)`
- `public static int IncreaseDebtByPercent(ScriptExecutor self, int percent)`
- `public static int HandleGoldDreamCardPlayed(ScriptExecutor self, string source)`
- `public static void SetAllDebtCountdownToOne(ScriptExecutor self)`
- `public static void RegisterCareer(ScriptExecutor self)`
- `public static void OnFightStart(ScriptExecutor self)`
- `public static void OnStartRound(ScriptExecutor self)`
- `public static void TrackMoney(ScriptExecutor self)`
- `public static int SettleDebt(ScriptExecutor self, int stacks, bool removeSettledStacks)`
- `public static void EndCombatCleanup(ScriptExecutor self)`
- `public static void EnableBankruptcyContract()`
- `public static void TryTriggerFalseGoldSpentBonus(ScriptExecutor self)`
- `public static void TryTriggerDebtBonus(ScriptExecutor self)`

### `GoldExp-Dev/Scripting/BuffScripts.cs`

- `public static class BuffScripts`
- `public static void Apply(ScriptExecutor self, string id)`

### `GoldExp-Dev/Scripting/CardScripts.cs`

- `public static class CardScripts`
- `public static void Init(ScriptExecutor self, string id)`
- `public static void Use(ScriptExecutor self, string id)`

### `GoldExp-Dev/Scripting/EnchTagScripts.cs`

- `public static class EnchTagScripts`
- `public static void Use(ScriptExecutor self, string id)`

### `GoldExp-Dev/Scripting/GoldWitchScripts.cs`

- `public static class GoldWitchScripts`
- `public static void InitCareer(ScriptExecutor self)`
- `public static void Init(ScriptExecutor self, string id)`
- `public static void Use(ScriptExecutor self, string id)`

### `GoldExp-Dev/Scripting/PartnerScripts.cs`

- `public static class PartnerScripts`
- `public static void Fight(ScriptExecutor self, string id)`
- `public static void RegisterMidasRaven(ScriptExecutor self)`
- `public static void ClearMidasRaven(ScriptExecutor self)`

### `GoldExp-Dev/Scripting/RelicScripts.cs`

- `public static class RelicScripts`
- `public static void Fight(ScriptExecutor self, string id)`

## LogExp-Dev

### `LogExp-Dev/Entry.cs`

- `public static class Entry`
- `public static void Initialize(ModConfig modConfig)`

### `LogExp-Dev/Hooks/CommandLogHooks.cs`

- `public static class CommandLogHooks`
- `public static void AfterLog(string tag, string message)`
- `public static void AfterLogWarning(string tag, string message)`
- `public static void AfterLogError(string tag, string message)`

### `LogExp-Dev/Infrastructure/LogExpRuntime.cs`

- `public static class LogExpRuntime`
- `public static void Initialize(ModConfig modConfig)`
- `public static void RecordCommand(string level, string? tag, string? message)`

### `LogExp-Dev/Infrastructure/LogFileWriter.cs`

- `public void Enqueue(LogRecord record)`
- `public void Dispose()`

### `LogExp-Dev/Infrastructure/LogRecord.cs`

- `public string Format()`

## SafeBoxExp-Dev

### `SafeBoxExp-Dev/Entry.cs`

- `public static class Entry`
- `public static void Initialize(ModConfig modConfig)`

### `SafeBoxExp-Dev/Hooks/SafeBoxRuntime.cs`

- `public static class SafeBoxRuntime`
- `public static void Initialize(ModConfig modConfig)`
- `public static LimitSnapshot Capture()`
- `public void Restore()`
- `public void OnPointerClick(PointerEventData eventData)`
- `public void OnSubmit(BaseEventData eventData)`
- `public void OnPointerEnter(PointerEventData eventData)`
- `public void OnPointerExit(PointerEventData eventData)`

### `SafeBoxExp-Dev/Infrastructure/SafeBoxExpIds.cs`

- `public static class SafeBoxExpIds`

### `SafeBoxExp-Dev/Infrastructure/SafeBoxExpLog.cs`

- `public static class SafeBoxExpLog`
- `public static void Info(string message)`
- `public static void Warn(string message)`
- `public static void Error(string message, Exception? exception = null)`
- `public static void Debug(string message)`

## SanGuoShaExp-Dev

### `SanGuoShaExp-Dev/Entry.cs`

- `public static class Entry`
- `public static void Initialize(ModConfig modConfig)`

### `SanGuoShaExp-Dev/GameApi/AudioApi.cs`

- `public static class AudioApi`
- `public static void Initialize(ModConfig modConfig)`
- `public static void PlayQixing()`
- `public static void PlayRandomWindMist()`

### `SanGuoShaExp-Dev/GameApi/BattleBgmProviderRuntime.cs`

- `public static class BattleBgmProviderRuntime`
- `public static void Initialize(ModConfig modConfig)`
- `public static void RequestBattleSwitch(string reason, bool force = false, bool allowSilenceWhenLoading = false, bool restartIfSameClip = true)`
- `public sealed class BattleAudioRegistryManifest`
- `public sealed class BattleBgmDefaultsManifest`
- `public sealed class BattleBgmProviderManifest`
- `public sealed class BattleBgmMatchManifest`

### `SanGuoShaExp-Dev/GameApi/ExecutorApi.cs`

- `public static class ExecutorApi`
- `public static string GetVar(ScriptExecutor? executor, string key, string fallback = "")`
- `public static void SetVar(ScriptExecutor? executor, string key, object value)`
- `public static bool IsHookTokenActive(ScriptExecutor? executor, string tokenKey, string? token)`
- `public static void ClearHook(ScriptExecutor? executor, string hookKey, string tokenKey)`
- `public static bool TryAddEvent(ScriptExecutor? executor, string eventName, Action script, string context = "")`
- `public static void SetBaseScript(ScriptExecutor executor, string baseScript, bool canSelf = true)`
- `public static List<IStatusManager> EnemyTargets(ScriptExecutor? executor)`
- `public static bool AddStatusBuff(ScriptExecutor? executor, IStatusManager? target, string buffId, int amount, string fallbackStatus = "Target")`

### `SanGuoShaExp-Dev/GameApi/PlayerApi.cs`

- `public static class PlayerApi`
- `public static int GetSkillTime(string key)`
- `public static void SetSkillTime(string key, int value)`

### `SanGuoShaExp-Dev/Infrastructure/DictionaryUtil.cs`

- `public static class DictionaryUtil`
- `public static string Get(IDictionary<string, string>? values, string key, string fallback = "")`
- `public static void Set(IDictionary<string, string>? values, string key, object value)`
- `public static int ParseInt(string? value, int fallback = 0)`

### `SanGuoShaExp-Dev/Infrastructure/SanGuoShaExpIds.cs`

- `public static class SanGuoShaExpIds`

### `SanGuoShaExp-Dev/Infrastructure/SanGuoShaExpLog.cs`

- `public static class SanGuoShaExpLog`
- `public static void Info(string message)`
- `public static void Warn(string message)`
- `public static void Error(string message, Exception? exception = null)`
- `public static void Debug(string message)`

### `SanGuoShaExp-Dev/Scripting/SanGuoShaCardScripts.cs`

- `public static class SanGuoShaCardScripts`
- `public static void Init(ScriptExecutor self, string id)`
- `public static void Use(ScriptExecutor self, string id)`

### `SanGuoShaExp-Dev/Scripting/SanGuoShaRelicScripts.cs`

- `public static class SanGuoShaRelicScripts`
- `public static void Fight(ScriptExecutor self, string id)`

### `SanGuoShaExp-Dev/Scripting/ShenZhugeLiangScripts.cs`

- `public static class ShenZhugeLiangScripts`
- `public static void InitCareer(ScriptExecutor self)`
- `public static void Init(ScriptExecutor self, string id)`
- `public static void Use(ScriptExecutor self, string id)`

## SkillCGExp-Dev

### `SkillCGExp-Dev/Config/SkillCgConfig.cs`

- `public sealed class SkillCgConfig`
- `public static SkillCgConfig Load(string modDirectory)`
- `public void Normalize(string modDirectory)`
- `public sealed class SkillCgRule`
- `public void Normalize(string modDirectory)`
- `public bool Matches(SkillCgTriggerContext context)`
- `public sealed class ConfigSkillCgProvider`
- `public IEnumerable<SkillCgRequest> BuildRequests(object context)`

### `SkillCGExp-Dev/Entry.cs`

- `public static class Entry`
- `public static void Initialize(ModConfig modConfig)`

### `SkillCGExp-Dev/Hooks/SkillCgArbiterRuntime.cs`

- `public static class SkillCgArbiterRuntime`
- `public static void Initialize(ModConfig modConfig, string ownerModId, SkillCgArbiterOptions? options = null)`
- `public static void RegisterProvider(ModConfig modConfig, string ownerModId, object provider)`
- `public static void Trigger(object ownerToken, string ownerModId, SkillCgTriggerContext context)`
- `public static void Clear(string ownerModId, string reason)`
- `public sealed class SkillCgArbiterComponent : MonoBehaviour`
- `public void Configure(object? value)`
- `public void RegisterProvider(object? provider)`
- `public void Trigger(object? value)`
- `public void ClearQueue(object? reason)`
- `public void AppendRequests(SkillCgTriggerContext context, List<SkillCgRequest> output)`
- `public string Describe()`
- `public sealed class SkillCgArbiterOptions`
- `public SkillCgArbiterOptions Normalized()`
- `public sealed class SkillCgTriggerContext`
- `public sealed class SkillCgRequest`
- `public void Normalize()`
- `public static SkillCgRequest? FromObject(object? source, string providerId, string ownerModId, int priority, SkillCgTriggerContext context)`
- `public static int CompareForQueue(QueuedRequest a, QueuedRequest b)`

### `SkillCGExp-Dev/Hooks/SkillCgRuntime.cs`

- `public static class SkillCgRuntime`
- `public static void Initialize(ModConfig modConfig)`

### `SkillCGExp-Dev/Infrastructure/SkillCgExpLog.cs`

- `public static class SkillCgExpLog`
- `public static void Info(string message)`
- `public static void InfoOnce(string key, string message)`
- `public static void Warn(string message)`
- `public static void WarnOnce(string key, string message)`
- `public static void Error(string message, Exception? exception = null)`
- `public static void DebugLog(string message)`

## StarExp-Dev

### `StarExp-Dev/Entry.cs`

- `public static class Entry`
- `public static void Initialize(ModConfig modConfig)`

### `StarExp-Dev/GameApi/BuffApi.cs`

- `public static class BuffApi`
- `public static int Level(IStatusManager? status, string buffId)`
- `public static bool Has(IStatusManager? status, string buffId)`

### `StarExp-Dev/GameApi/CardApi.cs`

- `public static class CardApi`
- `public static void AddCardToHand(ScriptExecutor self, string cardId)`

### `StarExp-Dev/GameApi/ExecutorApi.cs`

- `public static class ExecutorApi`
- `public static int CombatIntGet(string key, int fallback = 0)`
- `public static int CombatIntSet(string key, int value)`
- `public static string GetVar(ScriptExecutor? executor, string key, string fallback = "")`
- `public static void SetVar(ScriptExecutor? executor, string key, object value)`
- `public static void SetBaseScript(ScriptExecutor executor, string baseScript, bool canSelf = true)`
- `public static bool TryAddEvent(ScriptExecutor? executor, string eventName, Action script, string context = "")`
- `public static int SelfBuffLevel(ScriptExecutor? executor, string buffId)`
- `public static IStatusManager? PrimaryTarget(ScriptExecutor? executor)`
- `public static bool IsSelf(ScriptExecutor? executor, IStatusManager? target)`
- `public static bool SetStatusForTarget(ScriptExecutor? executor, IStatusManager? target, string fallbackStatus = "Self")`
- `public static List<IStatusManager> EnemyTargets(ScriptExecutor? executor)`
- `public static IStatusManager? RandomEnemyTarget(ScriptExecutor? executor)`
- `public static void DealDamage(ScriptExecutor? executor, int amount, string damageType = "")`
- `public static int RemoveSelfBuffStacks(ScriptExecutor? executor, string buffId, int amount)`
- `public static void AddDescription(ScriptExecutor? executor, string index, string type, int amount)`

### `StarExp-Dev/GameApi/PlayerApi.cs`

- `public static class PlayerApi`
- `public static int GetSkillTime(string key)`
- `public static void SetSkillTime(string key, int value)`
- `public static void SetGameVar(string key, string value)`
- `public static void ShowCaption(string text)`

### `StarExp-Dev/Infrastructure/DictionaryUtil.cs`

- `public static class DictionaryUtil`
- `public static string Get(IDictionary<string, string>? values, string key, string fallback = "")`
- `public static void Set(IDictionary<string, string>? values, string key, object value)`
- `public static int ParseInt(string? value, int fallback = 0)`

### `StarExp-Dev/Infrastructure/StarExpIds.cs`

- `public static class StarExpIds`

### `StarExp-Dev/Infrastructure/StarExpLog.cs`

- `public static class StarExpLog`
- `public static void Info(string message)`
- `public static void Warn(string message)`
- `public static void Error(string message, Exception? exception = null)`
- `public static void Debug(string message)`

### `StarExp-Dev/Mechanics/StarMiracleService.cs`

- `public static class StarMiracleService`
- `public static void RegisterCareer(ScriptExecutor self)`
- `public static void EnsureCombatHooks(ScriptExecutor self)`
- `public static void OnFightStart(ScriptExecutor self)`
- `public static void OnAction(ScriptExecutor self)`
- `public static void OnStartRound(ScriptExecutor self)`
- `public static void DrawStone(ScriptExecutor self)`
- `public static void RemoveBlackStones(ScriptExecutor self, int amount)`
- `public static void ReduceClock(ScriptExecutor self, int amount, bool canWaiveDebt)`
- `public static void TriggerNaturalMorningStar(ScriptExecutor self)`
- `public static void TriggerBorrowedMiracle(ScriptExecutor self)`
- `public static void AddStarlight(ScriptExecutor self, int amount)`
- `public static void AddClockDebt(ScriptExecutor self, int amount)`
- `public static int BlackStonesThisRound()`
- `public static int BlackStonesThisCombat()`
- `public static int ClockDebt(ScriptExecutor self)`
- `public static void ClearDebt(ScriptExecutor self)`
- `public static void EndCombatCleanup(ScriptExecutor self)`

### `StarExp-Dev/Scripting/BuffScripts.cs`

- `public static class BuffScripts`
- `public static void Apply(ScriptExecutor self, string id)`

### `StarExp-Dev/Scripting/CardScripts.cs`

- `public static class CardScripts`
- `public static void Init(ScriptExecutor self, string id)`
- `public static void Use(ScriptExecutor self, string id)`

### `StarExp-Dev/Scripting/StarMiracleScripts.cs`

- `public static class StarMiracleScripts`
- `public static void InitCareer(ScriptExecutor self)`
- `public static void Init(ScriptExecutor self, string id)`
- `public static void Use(ScriptExecutor self, string id)`

## SunExp-Dev

### `SunExp-Dev/Entry.cs`

- `public static class Entry`
- `public static void Initialize(ModConfig modConfig)`

### `SunExp-Dev/GameApi/AudioApi.cs`

- `public static class AudioApi`
- `public static void Initialize(ModConfig modConfig)`
- `public static void PlayWhiteSunPrayer()`
- `public static void PlayGraveSong()`

### `SunExp-Dev/GameApi/BattleBgmProviderRuntime.cs`

- `public static class BattleBgmProviderRuntime`
- `public static void Initialize(ModConfig modConfig)`
- `public static void RequestBattleSwitch(string reason, bool force = false, bool allowSilenceWhenLoading = false, bool restartIfSameClip = true)`
- `public sealed class BattleAudioRegistryManifest`
- `public sealed class BattleBgmDefaultsManifest`
- `public sealed class BattleBgmProviderManifest`
- `public sealed class BattleBgmMatchManifest`

### `SunExp-Dev/GameApi/BuffApi.cs`

- `public static class BuffApi`
- `public static int Level(IStatusManager? status, string buffId)`
- `public static bool Has(IStatusManager? status, string buffId)`
- `public static int NegativeTotal(IStatusManager? status)`
- `public static bool RemoveNegativeBuffs(ScriptExecutor executor, IStatusManager? status)`
- `public static void SetLevelOrRemove(ScriptExecutor executor, IStatusManager status, string buffId, int nextLevel)`
- `public static int ConsumeEmberBeforeBurn(ScriptExecutor executor, IStatusManager? status)`
- `public static string EmberDamageBonusKey(IStatusManager? status)`
- `public static int SyncEmberDamageBonus(ScriptExecutor? executor, IStatusManager? status)`
- `public static int ClearEmberDamageBonus(ScriptExecutor? executor, IStatusManager? status)`
- `public static int OnEmberConsumed(ScriptExecutor? executor, IStatusManager? status, int consumed)`
- `public static int SavePersistentEmber(ScriptExecutor? executor, IStatusManager? status)`

### `SunExp-Dev/GameApi/CardConfigApi.cs`

- `public static class CardConfigApi`
- `public static IDataConfig? FromActionPayload(object? payload)`
- `public static string Id(IDataConfig? config)`
- `public static int CurrentCost(IDataConfig? config)`
- `public static int BaseCost(IDataConfig? config)`
- `public static int ResolveSolarTriggerCost(IDataConfig? config, int fallback)`
- `public static void ClearSolarTriggerCost(IDataConfig? config)`
- `public static bool HasNativeWhiteRadiance(IDataConfig? config)`
- `public static bool HasTemporaryWhiteRadiance(IDataConfig? config)`
- `public static bool HasSpecialWhiteRadiance(IDataConfig? config)`
- `public static bool TryClaimTemporaryWhiteRadiance(IDataConfig config)`

### `SunExp-Dev/GameApi/ExecutorApi.cs`

- `public static class ExecutorApi`
- `public static string GetVar(ScriptExecutor? executor, string key, string fallback = "")`
- `public static void SetVar(ScriptExecutor? executor, string key, object value)`
- `public static int CombatIntGet(string key, int fallback = 0)`
- `public static int CombatIntSet(string key, int value)`
- `public static int CombatIntAdd(string key, int amount)`
- `public static string? RegisterHook(ScriptExecutor? executor, string hookKey, string tokenKey)`
- `public static bool IsHookTokenActive(ScriptExecutor? executor, string tokenKey, string? token)`
- `public static void ClearHook(ScriptExecutor? executor, string hookKey, string tokenKey)`
- `public static bool TryAddEvent(ScriptExecutor? executor, string eventName, Action script, string context = "")`
- `public static void SetBaseScript(ScriptExecutor executor, string baseScript, bool canSelf = true)`
- `public static int SelfBuffLevel(ScriptExecutor? executor, string buffId)`
- `public static int StatusBuffLevel(IStatusManager? status, string buffId)`
- `public static int BurnUpperBound(IStatusManager? target)`
- `public static int BuffUpperBound(IStatusManager? target, string buffId, int fallback)`
- `public static List<IStatusManager> EnemyTargets(ScriptExecutor? executor)`
- `public static List<IStatusManager> FriendlyTargets(ScriptExecutor? executor, bool includeSelf)`
- `public static IStatusManager? RandomEnemyTarget(ScriptExecutor? executor, bool requireBurn)`
- `public static IStatusManager? RandomFriendlyTarget(ScriptExecutor? executor, bool includeSelf)`
- `public static IStatusManager? PrimaryTarget(ScriptExecutor? executor)`
- `public static IStatusManager? PrimaryTargetIncludingSelf(ScriptExecutor? executor)`
- `public static bool IsSelf(ScriptExecutor? executor, IStatusManager? target)`
- `public static bool SetStatusForTarget(ScriptExecutor? executor, IStatusManager? target, string fallbackStatus = "Self")`
- `public static bool AddStatusBuff(ScriptExecutor? executor, IStatusManager? target, string buffId, int amount, string fallbackStatus = "Target")`
- `public static bool RemoveStatusBuff(ScriptExecutor? executor, IStatusManager? target, string buffId, string fallbackStatus = "Self")`
- `public static int RemoveBuffStacks(ScriptExecutor? executor, IStatusManager? target, string buffId, int amount)`
- `public static bool DealDamage(ScriptExecutor? executor, int amount, string damageType = "")`
- `public static void AddDamageDescription(ScriptExecutor? executor, string index, int amount)`
- `public static void AddValueDescription(ScriptExecutor? executor, string index, int amount)`
- `public static int SolarMultiplier(ScriptExecutor? executor)`
- `public static int SolarCoefficient(ScriptExecutor? executor, IStatusManager? target)`
- `public static int SolarKeywordDamage(ScriptExecutor? executor, int baseDamage, IStatusManager? target, int coefficientScale = 1)`
- `public static int SolarKeywordBlock(ScriptExecutor? executor, int baseBlock)`
- `public static bool DealSolarKeywordDamage(ScriptExecutor? executor, int baseDamage, IStatusManager? target, string fallbackStatus = "Target", int coefficientScale = 1)`
- `public static int DealSolarKeywordDamageAllEnemies(ScriptExecutor? executor, int baseDamage, int coefficientScale)`
- `public static int ApplySolarKeywordSkill(ScriptExecutor? executor, int baseBlock)`
- `public static bool TriggerBurn(ScriptExecutor? executor, IStatusManager? target, string fallbackStatus = "Target")`
- `public static int TriggerBurnAllEnemies(ScriptExecutor? executor, int times = 1)`
- `public static int TriggerBurnAll(ScriptExecutor? executor, int times = 1)`
- `public static bool ApplySelfBurn(ScriptExecutor? executor, int amount, bool includePending)`
- `public static bool ClearSelfBurnIfProtected(ScriptExecutor? executor, bool includePending)`
- `public static bool IsSelfBurnProtected(ScriptExecutor? executor, bool includePending)`
- `public static void ApplyFieldBuff(ScriptExecutor? executor, string fieldId, int amount)`
- `public static bool ClearFieldBuff(ScriptExecutor? executor, string fieldId)`
- `public static string FieldBuffId(string fieldId)`
- `public static string FieldBuffId(SunExpFieldId field)`
- `public static string FieldCombatKey(string fieldId, string name)`
- `public static string FieldCombatKey(SunExpFieldId field, string name)`
- `public static string FieldSlug(SunExpFieldId field)`
- `public static SunExpFieldId ParseFieldId(string fieldId)`
- `public static void SetSharedFieldState(string fieldId, int stacks)`
- `public static void SetSharedFieldState(SunExpFieldId field, int stacks)`
- `public static bool IsSharedFieldActive(string fieldId)`
- `public static bool IsSharedFieldActive(SunExpFieldId field)`
- `public static int FieldStacks(string fieldId)`
- `public static int FieldStacks(SunExpFieldId field)`
- `public static int SyncFieldStacks(ScriptExecutor? executor, string fieldId)`
- `public static int SyncFieldStacks(ScriptExecutor? executor, SunExpFieldId field)`
- `public static int SetActiveField(ScriptExecutor? executor, string fieldId)`
- `public static int SetActiveField(ScriptExecutor? executor, SunExpFieldId field)`
- `public static bool BeginSharedFieldStartRound(ScriptExecutor? executor, string fieldId)`
- `public static bool BeginSharedFieldStartRound(ScriptExecutor? executor, SunExpFieldId field)`
- `public static bool IsActiveField(ScriptExecutor? executor, string fieldId, int? epoch = null, string? token = null)`
- `public static bool IsActiveField(ScriptExecutor? executor, SunExpFieldId field, int? epoch = null, string? token = null)`
- `public static bool IsActiveField(ScriptExecutor? executor, string fieldId)`
- `public static int TransferSelfBurnToRandomFriendly(ScriptExecutor? executor)`
- `public static void AddBurnToRandomEnemy(ScriptExecutor? executor, int amount)`
- `public static int NegativeBuffTotal(IStatusManager? status)`
- `public static bool RemoveAllNegativeBuffs(ScriptExecutor? executor, IStatusManager? status)`
- `public static int SolarCrownTier(ScriptExecutor? executor)`
- `public static void HandleBurnOverflow(IStatusManager? target, string buffId, int amount)`

### `SunExp-Dev/GameApi/GameCompatibilityApi.cs`

- `public static class GameCompatibilityApi`
- `public static bool ShouldEnableOnlineCardPack()`
- `public static void StartLobby()`

### `SunExp-Dev/GameApi/PlayerApi.cs`

- `public static class PlayerApi`
- `public static int GetSkillTime(string key)`
- `public static void SetSkillTime(string key, int value)`
- `public static void SetGameVar(string key, string value)`
- `public static string GetGameVar(string key, string fallback = "")`
- `public static string ScopedGameVarKey(string key, IStatusManager? status)`
- `public static string GetScopedGameVar(string key, IStatusManager? status, string fallback = "", bool migrateLegacyWhenSolo = false)`
- `public static void SetScopedGameVar(string key, IStatusManager? status, string value)`
- `public static void ShowCaption(string text)`
- `public static string GetCurrentCareerId()`
- `public static bool AddMoney(int amount)`
- `public static void AddCard(string cardId)`
- `public static void AddRelic(string relicId)`
- `public static void AddBless(string blessId)`
- `public static void EndEvent()`
- `public static void EventTryChangeMap()`

### `SunExp-Dev/Hooks/AnimatedBlessingIconRuntime.cs`

- `public static class AnimatedBlessingIconRuntime`
- `public static void Initialize(ModConfig modConfig)`
- `public sealed class AnimatedBlessingIcon : MonoBehaviour`
- `public void Configure(float seconds)`

### `SunExp-Dev/Hooks/AnimatedBuffIconRuntime.cs`

- `public static class AnimatedBuffIconRuntime`
- `public static void Initialize(ModConfig modConfig)`
- `public sealed class AnimatedBuffSpriteIcon : MonoBehaviour`
- `public void Configure(float seconds)`

### `SunExp-Dev/Hooks/AnimatedEnemyDictIconRuntime.cs`

- `public static class AnimatedEnemyDictIconRuntime`
- `public static void Initialize(ModConfig modConfig)`
- `public sealed class AnimatedEnemyDictIcon : MonoBehaviour`
- `public void Configure(float seconds, IReadOnlyList<string> framePaths)`

### `SunExp-Dev/Hooks/DuskPartnerRuntime.cs`

- `public static class DuskPartnerRuntime`
- `public static void Initialize(ModConfig modConfig)`

### `SunExp-Dev/Hooks/RuntimeHooks.cs`

- `public static class RuntimeHooks`
- `public static void Initialize(ModConfig modConfig)`

### `SunExp-Dev/Hooks/SolarEventRuntime.cs`

- `public static class SolarEventRuntime`
- `public static void EnsureInCurrentLayer(ModHookContext context)`
- `public static void RepairMapSelection(ModHookContext context)`
- `public static string CurrentEventId()`

### `SunExp-Dev/Hooks/SolarMemoryBlessingPickerRuntime.cs`

- `public static class SolarMemoryBlessingPickerRuntime`
- `public static void Open(Action onCompleted)`
- `public static void Close()`

### `SunExp-Dev/Hooks/SolarMemoryMapItemAnimationRuntime.cs`

- `public static class SolarMemoryMapItemAnimationRuntime`
- `public static void Initialize(ModConfig modConfig)`

### `SunExp-Dev/Hooks/SolarMemoryModeRuntime.cs`

- `public static class SolarMemoryModeRuntime`
- `public static void Initialize(ModConfig modConfig)`
- `public static void OpenOriginWindow()`
- `public static void OpenBlessingWindow()`
- `public static void OpenDeckWindow()`
- `public static void StartSolarFinaleSaintBattle()`
- `public static void OpenSolarFinaleEndingEvent()`
- `public static void ShowSolarMemorySettlement()`
- `public static bool IsSolarMemoryRun()`
- `public static List<string> CurrentPackSelection()`
- `public static bool IsSolarMemoryEventCard(string cardId)`
- `public static int SanitizeSolarMemoryRoleCards(RoleTable? role, string source)`
- `public static void ClearSolarMemoryReservePool()`
- `public static SolarMemoryFixedNodeSpec Event(int slotIndex, int layer, int mapSlotIndex)`
- `public static SolarMemoryFixedNodeSpec Boss(int slotIndex, int layer, string mapId, string levelId)`

### `SunExp-Dev/Hooks/SolarMemoryPreparationRuntime.cs`

- `public enum SolarMemoryPrepStep`
- `public static class SolarMemoryPreparationRuntime`
- `public static void StartOrResume()`
- `public static void CompleteDeckSelection()`
- `public static void CompleteOriginAllocation()`
- `public static void CompleteBlessingSelection()`
- `public static bool IsComplete()`

### `SunExp-Dev/Hooks/SolarMemorySetupFlowRuntime.cs`

- `public static class SolarMemorySetupFlowRuntime`
- `public static void StartAfterStarterDeck()`
- `public static void OpenOriginSetupWindow()`
- `public static void OpenBlessingSetupWindow()`
- `public static void ClosePreparationWindows()`

### `SunExp-Dev/Hooks/SolarMemoryStarterDeckRuntime.cs`

- `public static class SolarMemoryStarterDeckRuntime`
- `public static void Initialize(ModConfig modConfig)`
- `public static void CaptureSelectedPacks(IEnumerable<string> packs)`
- `public static void MarkPending(RoleTable roleTable, string source)`
- `public static bool OpenOrResume()`

### `SunExp-Dev/Hooks/SpecialTagRuntime.cs`

- `public static class SpecialTagRuntime`
- `public static void Initialize()`
- `public static void OnFightStart(Fight_Start __instance)`
- `public static void BeforeCommonTrueUse(CommonCardItem __instance)`
- `public static void BeforeAttackTrueUse(AttackCardItem __instance)`

### `SunExp-Dev/Infrastructure/DictionaryUtil.cs`

- `public static class DictionaryUtil`
- `public static string Get(IDictionary<string, string>? values, string key, string fallback = "")`
- `public static void Set(IDictionary<string, string>? values, string key, string value)`
- `public static int GetInt(IDictionary<string, string>? values, string key, int fallback = 0)`
- `public static int ParseInt(string? value, int fallback = 0)`
- `public static bool ContainsToken(string? text, string token)`

### `SunExp-Dev/Infrastructure/SunExpFieldId.cs`

- `public enum SunExpFieldId`

### `SunExp-Dev/Infrastructure/SunExpIds.cs`

- `public static class SunExpIds`

### `SunExp-Dev/Infrastructure/SunExpLog.cs`

- `public static class SunExpLog`
- `public static void Info(string message)`
- `public static void Warn(string message)`
- `public static void Error(string message, Exception? exception = null)`
- `public static void Debug(string message)`

### `SunExp-Dev/Mechanics/SolarMemoryMapNodePool.cs`

- `public sealed class SolarMemoryMapNodePool`

### `SunExp-Dev/Mechanics/SolarMemoryMapNodePoolApplier.cs`

- `public static class SolarMemoryMapNodePoolApplier`
- `public static void CaptureGenerationState(NormalMapManager manager)`
- `public static void ResetGenerationCapture()`
- `public static bool ApplyToCurrentLayer(NormalMapManager manager, string source, bool trimEventRecord)`

### `SunExp-Dev/Mechanics/SolarMemoryMapNodePoolFactory.cs`

- `public static class SolarMemoryMapNodePoolFactory`
- `public static SolarMemoryMapNodePool GenerateLayer(NormalMapManager manager, MapTree tree)`
- `public static int LayerFor(NormalMapManager manager)`
- `public static int ClampLayer(int layer)`
- `public static int EventIndex(int layer, int mapSlotIndex)`
- `public static int DefaultLayerSegmentSize()`
- `public static int SelectLayerSegmentSize()`
- `public static bool IsSolarMemoryFixedStoryBoss(string? id)`

### `SunExp-Dev/Mechanics/SolarRadianceService.cs`

- `public static class SolarRadianceService`
- `public static bool HandleSolarCardUsed(ScriptExecutor? executor, int cost, string source)`

### `SunExp-Dev/Scripting/BossScripts.cs`

- `public static class BossScripts`
- `public static void InitEnemy(ScriptExecutor self, string bossId)`
- `public static void ApplyTrait(ScriptExecutor self, string traitId)`
- `public static void ClearTrait(ScriptExecutor self, string traitId)`
- `public static void InitCard(ScriptExecutor self, string cardId)`
- `public static void Target(ScriptExecutor self, string target)`
- `public static void UseCard(ScriptExecutor self, string cardId)`

### `SunExp-Dev/Scripting/BuffScripts.cs`

- `public static class BuffScripts`
- `public static void Apply(ScriptExecutor self, string id)`
- `public static void Clear(ScriptExecutor self, string id)`

### `SunExp-Dev/Scripting/CardScripts.cs`

- `public static class CardScripts`
- `public static void Init(ScriptExecutor self, string id)`
- `public static void Use(ScriptExecutor self, string id)`

### `SunExp-Dev/Scripting/EventScripts.cs`

- `public static class EventScripts`
- `public static void Init(ScriptExecutor self, string id)`
- `public static void RewardCard(int progress, string cardId)`
- `public static void RewardRelic(int progress, string relicId)`
- `public static void RewardBless(int progress, string blessId)`
- `public static void RepeatReward()`
- `public static void InitSolarMemoryStart(ScriptExecutor self)`
- `public static void InitSolarMemoryNode(ScriptExecutor self)`
- `public static void ContinueSolarMemory()`
- `public static void OpenSolarMemoryOrigin()`
- `public static void OpenSolarMemoryBless()`
- `public static void OpenSolarMemoryDeck()`
- `public static void OpenSolarMemoryPreparation()`
- `public static void StartSolarMemoryBossRush()`
- `public static void InitSolarFinaleLedger(ScriptExecutor self)`
- `public static void PreserveSolarFinaleLedger()`
- `public static void BurnSolarFinaleName()`
- `public static void NamelessSolarFinaleName()`
- `public static void InitSolarFinaleSecondSun(ScriptExecutor self)`
- `public static void ResolveSolarFinaleSecondSun(string result)`
- `public static void InitSolarFinaleSaint(ScriptExecutor self)`
- `public static void InitSolarFinaleSaintGate(ScriptExecutor self)`
- `public static void EnterSolarFinaleSaintBattle()`
- `public static void SkipSolarFinaleSaintBattle()`
- `public static void ResolveSolarFinaleSaint(string result)`
- `public static void InitSolarFinaleEnding(ScriptExecutor self)`
- `public static void FinishSolarFinaleEnding(string ending)`

### `SunExp-Dev/Scripting/PartnerScripts.cs`

- `public static class PartnerScripts`
- `public static void Fight(ScriptExecutor self, string id)`
- `public static void RegisterDuskAfterheatRecovery(ScriptExecutor self)`
- `public static void ClearDuskAfterheatRecovery(ScriptExecutor self)`

### `SunExp-Dev/Scripting/RelicScripts.cs`

- `public static class RelicScripts`
- `public static void Fight(ScriptExecutor self, string id)`

### `SunExp-Dev/Scripting/WunaScripts.cs`

- `public static class WunaScripts`
- `public static void InitCareer(ScriptExecutor self)`
- `public static void Init(ScriptExecutor self, string id)`
- `public static void Use(ScriptExecutor self, string id)`

