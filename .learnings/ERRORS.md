# Errors

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
