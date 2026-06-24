# Errors

## [ERR-20260622-001] powershell-variable-colon-interpolation

**Logged**: 2026-06-22T10:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
PowerShell parsed `$required:` inside a double-quoted validation error string as an invalid variable reference.

### Error
```text
Variable reference is not valid. ':' was not followed by a valid variable name character.
```

### Context
- Command attempted: `tools\Test-MainSharedFramework.ps1`
- File: `tools/Test-MainSharedFramework.ps1`

### Suggested Fix
Use `${required}:` when a variable is immediately followed by a colon in a double-quoted PowerShell string.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-MainSharedFramework.ps1

### Resolution
- **Resolved**: 2026-06-22T10:00:00+08:00
- **Notes**: Changed `$required:` to `${required}:`.

---

## [ERR-20260624-001] aura-shared-core-test-harness

**Logged**: 2026-06-24T17:45:14.7968790+08:00
**Priority**: medium
**Status**: pending
**Area**: tests

### Summary
`tools\Test-AuraSharedCore.ps1` failed because the test harness still calls a removed or renamed `AuraChatRuntime.ConfirmPlayerMessage` API.

### Error
```text
error CS0117: “AuraChatRuntime”未包含“ConfirmPlayerMessage”的定义
AuraSharedCore test harness failed.
```

### Context
- Command attempted after adding the AuraTools DPS meter module.
- `dotnet build AuraToolsExp-Dev\AuraToolsExp.Dll.csproj -c Release -v:minimal` succeeded with 0 warnings and 0 errors, so this failure is outside the DPS module build path.

### Suggested Fix
Update `AuraSharedCore.Tests\Program.cs` to the current AuraChatRuntime confirmation API, or remove stale confirmation assertions if the runtime no longer exposes that flow.

### Metadata
- Reproducible: yes
- Related Files: AuraSharedCore.Tests/Program.cs

---

## 2026-06-10 - image generation output size mismatch

- **Context:** Batch-generating SunExp card-face PNGs with the built-in image generation tool.
- **Expected:** Prompted 512x512 square assets could be copied directly into `SunExp/ModResource/Images/Card/SunExp/`.
- **Observed:** Generated PNGs were 1254x1254, so the first direct copy failed the asset-size validation.
- **Fix:** Treat generated dimensions as non-authoritative and run a deterministic center-crop/resize pass to 512x512 before replacing project assets.

## 2026-06-10 - large PNG atlas optimized save timeout

- **Context:** Regenerating `_sunexp_atlas.png` and `_sunexp_source_atlas.png` as a 2560x3072 contact atlas.
- **Expected:** Pillow `save(..., optimize=True)` would finish within the default shell timeout.
- **Observed:** Optimized PNG save exceeded the 10s command timeout.
- **Fix:** Use a longer timeout or normal PNG compression such as `compress_level=6` for large generated preview atlases.

## 2026-06-11 - Lua varargs inside nested pcall

- **Context:** Adding SunExp map-injection helper `SunExp_TryInjectSolarEventMapCard(...)`.
- **Expected:** `local args = {...}` inside an anonymous `pcall(function() ... end)` body would pass Lua syntax validation.
- **Observed:** Lua rejected `...` inside the nested anonymous function: `cannot use '...' outside a vararg function`.
- **Fix:** Capture `local args = {...}` in the vararg function body before entering nested callbacks.

## 2026-06-11 - EventList localized text CSV comma drift

- **Context:** Adding `SunExp/Text/EventList/sunexp.csv` with English placeholder prose.
- **Expected:** The text table would import with `1Describe` and `2Describe` aligned to their option columns.
- **Observed:** English commas inside unquoted fields shifted later columns while the main validation still passed.
- **Fix:** After editing multi-language CSV by hand, import it with `Import-Csv` and inspect key columns; quote comma-bearing fields or use comma-free placeholder text.

## [ERR-20260616-001] powershell-rg-glob-argument

**Logged**: 2026-06-16T15:30:19.1783337+08:00
**Priority**: low
**Status**: pending
**Area**: docs

### Summary
PowerShell treated bare wildcard path arguments in an `rg` path list as invalid path patterns.

### Error
```text
rg: *.ps1: 文件名、目录名或卷标语法不正确。 (os error 123)
```

### Context
- Command attempted: `rg -n "LogExp|日志|log" "LogExp" "tools" "*.ps1"`
- Recurred with: `rg -n "Application\\.logMessageReceived|..." "*-Dev" -g "*.cs"`
- Environment: Windows PowerShell in the SunExp repository.

### Suggested Fix
Use `rg -n "pattern" LogExp tools -g "*.ps1"` for file globs, or enumerate wildcard directories with PowerShell first and pass resolved paths.

### Metadata
- Reproducible: yes
- Related Files: none

---

## [ERR-20260620-001] managed-method-signature-drift

**Logged**: 2026-06-20T01:20:00+08:00
**Priority**: high
**Status**: resolved
**Area**: runtime-compatibility

### Summary
`SolarMemoryStarterDeckRuntime` compiled against an older direct
`GameConfigManager.GetPackItems` signature and failed at runtime after Managed
assemblies changed.

### Error
```text
System.MissingMethodException: Method not found: GameConfigManager.GetPackItems(string)
```

### Distilled Fix
Treat repository `Managed/` as the current contract. Route known drifting APIs
through one `GameApi` reflection wrapper that supports current and legacy
signatures, then falls back to a deterministic table scan. Rebuild `Entry.dll`
after updating Managed.

### Resolution
- **Resolved**: 2026-06-20T01:20:00+08:00
- **Related Files**: `SunExp-Dev/GameApi/GameCompatibilityApi.cs`, `SunExp-Dev/Hooks/SolarMemoryStarterDeckRuntime.cs`

---

## [ERR-20260620-002] map-breaks-is-not-mode-isolation

**Logged**: 2026-06-20T01:20:00+08:00
**Priority**: high
**Status**: resolved
**Area**: map-runtime

### Summary
Solar Memory event Map rows appeared in World Simulation even though their
`NodeId` used `Breaks_` placeholders.

### Cause
`MapTree.TypeGenerate` draws from the global Map table by `Note` and does not
apply the `Breaks` filter used by `NormalMapManager.RandomGenerate`. Ordinary
event rows also ignore `Level`, so neither `Breaks_` nor level 99 fully isolates
them.

### Distilled Fix
Mark every mode-exclusive Map row with `Rarity=7`, keep story events as `Sub_`,
admit them only through a mode-guarded direct factory, and sanitize old map trees
plus multiplayer `maps`/`mapData` arrays outside the owning mode.

### Resolution
- **Resolved**: 2026-06-20T01:20:00+08:00
- **Related Files**: `SunExp/Data/Map/sunexp.csv`, `SunExp-Dev/Hooks/SolarMemoryContentIsolationRuntime.cs`

---

## [ERR-20260620-003] custom-map-node-missing-deterministic-dice

**Logged**: 2026-06-20T01:20:00+08:00
**Priority**: high
**Status**: resolved
**Area**: map-runtime

### Summary
Custom Solar Memory nodes could reach map loading without a valid `NodeDice`,
making downstream map initialization and multiplayer behavior unsafe.

### Distilled Fix
Assign every custom or replacement `MapTree.Node` a deterministic `NodeDice`.
Prefer the owning tree's dice cursor for generated nodes; use `Dice.Default`
only for fixed nodes that perform no random draws. Add source assertions for all
custom node factories.

### Resolution
- **Resolved**: 2026-06-20T01:20:00+08:00
- **Related Files**: `SunExp-Dev/Mechanics/SolarMemoryMapNodePoolFactory.cs`, `SunExp-Dev/Hooks/SolarMemoryModeRuntime.cs`

---

## [ERR-20260620-004] fight-start-step-aborted-later-setup

**Logged**: 2026-06-20T01:20:00+08:00
**Priority**: high
**Status**: resolved
**Area**: battle-runtime

### Summary
A fight-start hard-tag operation could throw while applying HP loss and prevent
unrelated listeners and later setup steps from running.

### Cause
The operation borrowed an unrelated active executor and called a path that
expected a valid data-config `Id`. Multiple independent setup actions shared one
failure boundary.

### Distilled Fix
Apply global effects through the resolved synchronized status API, keep shared
progression host-authoritative, and execute independent fight-start actions in
separately named/logged failure boundaries.

### Resolution
- **Resolved**: 2026-06-20T01:20:00+08:00
- **Related Files**: `SunExp-Dev/Hooks/SunExpHardTagRuntime.cs`

---

## [ERR-20260616-002] dotnet-objectdisposed-catch-order

**Logged**: 2026-06-16T15:42:04.8648026+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
Adding `catch (ObjectDisposedException)` after `catch (InvalidOperationException)` caused unreachable catch code in net472.

### Error
```text
error CS0160: 上一个 catch 子句已经捕获了此类型或超类型(“InvalidOperationException”)的所有异常
```

### Context
- Command attempted: `tools\Build-LogExpDll.ps1`
- `BlockingCollection.Add` disposal races were already covered by `InvalidOperationException` because `ObjectDisposedException` derives from it.

### Suggested Fix
Keep only the broader `InvalidOperationException` catch or put narrower derived exceptions first when both are genuinely needed.

### Metadata
- Reproducible: yes
- Related Files: LogExp-Dev/Infrastructure/LogFileWriter.cs

### Resolution
- **Resolved**: 2026-06-16T15:42:04.8648026+08:00
- **Notes**: Removed the unreachable `ObjectDisposedException` catch.

---

## [ERR-20260617-001] powershell-replace-literal-paths

**Logged**: 2026-06-17T13:42:16.5907412+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
PowerShell `-replace` treated literal documentation text containing backslashes and backticks as a regex pattern while converting generated docs.

### Error
```text
The regular expression pattern Generated from workspace CSV files\. Refresh with `tools\Export-ModDevDocs\.ps1`\. is not valid.
```

### Context
- Command attempted: mechanical Chinese backup conversion for `docs/mod-dev-zh-CN/generated/*.md`.
- Environment: Windows PowerShell in the SunExp repository.
- Cause: `-replace` expects a regex pattern; literal strings containing backslashes and backticks are safer with `.Replace(...)` or `[regex]::Escape(...)`.

### Suggested Fix
Use string `.Replace(old, new)` for literal prose replacements, or build regex patterns with `[regex]::Escape($old)`.

### Metadata
- Reproducible: yes
- Related Files: docs/mod-dev-zh-CN/generated/*.md

### Resolution
- **Resolved**: 2026-06-17T13:42:16.5907412+08:00
- **Notes**: Replaced the remaining generated-index prose with a direct patch.

---
