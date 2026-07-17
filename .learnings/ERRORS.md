# Errors

## [ERR-20260706-001] skill-creator-init-interface-length

**Logged**: 2026-07-06T11:20:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
`init_skill.py` can create the skill directory but fail while generating
`agents/openai.yaml` when an interface field is too long.

### Error
```text
[ERROR] short_description must be 25-64 characters (got 109).
```

### Context
- Command attempted: `skill-creator/scripts/init_skill.py sunexp-poster-design`
  with `--interface short_description=...`.
- The skill folder and `SKILL.md` were created before the metadata step failed.

### Suggested Fix
Use a 25-64 character `short_description`, then run
`generate_openai_yaml.py` separately if initialization partially succeeds.

### Metadata
- Reproducible: yes
- Related Files: .codex/skills/sunexp-poster-design/agents/openai.yaml

---

## [ERR-20260717-001] shared-runtime-isexternalinit

**Logged**: 2026-07-17T12:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
AuraCg unit tests accepted `init` accessors, but the actual Aura.Shared target framework could not resolve `System.Runtime.CompilerServices.IsExternalInit`.

### Error
```text
CS0518: Predefined type 'System.Runtime.CompilerServices.IsExternalInit' is not defined or imported
```

### Suggested Fix
Validate pure shared sources against the real Aura.Shared target immediately after the focused .NET 8 tests, and avoid `init` accessors in shared runtime DTOs unless the compatibility shim is already present.

### Metadata
- Reproducible: yes
- Related Files: AuraCgShared/AuraCgMediaCache.cs, AuraSharedRuntime-Dev/Aura.Shared.csproj
- Recurrence-Count: 1

### Resolution
- **Resolved**: 2026-07-17T12:05:00+08:00
- **Notes**: Replaced internal statistics `init` accessors with ordinary setters before rerunning the shared compatibility build.

---

## [ERR-20260716-006] sunexp-toolbar-button-namespace

**Logged**: 2026-07-16T18:30:00+08:00
**Priority**: low
**Status**: resolved
**Area**: frontend

### Summary
The first Endless Abyss evacuation build could not resolve `ButtonManager` because the cloned AuraTools implementation relied on a namespace not imported in the new SunExp UI runtime.

### Error
```text
CS0246: The type or namespace name 'ButtonManager' could not be found.
```

### Context
- Attempted `dotnet build SunExp-Dev/SunExp.Dll.csproj -c Release --no-restore`.
- The TopBar clone pattern was adapted from `AuraToolsSafeBoxRuntime.cs`.

### Suggested Fix
Resolve `ButtonManager` from the current Managed contract and import its declaring namespace before rebuilding.

### Metadata
- Reproducible: yes
- Related Files: SunExp-Dev/Hooks/Ui/EndlessAbyssEvacuationButtonRuntime.cs

### Resolution
- **Resolved**: 2026-07-16T18:34:00+08:00
- **Notes**: Imported `Michsky.MUIP`, rebuilt the shipped DLL with zero warnings, and retained the native TopBar template pattern.

---

## [ERR-20260716-004] flight-glyph-cache-namespace

**Logged**: 2026-07-16T16:06:00+08:00
**Priority**: low
**Status**: resolved
**Area**: build

### Summary
The first Star Score flight-glyph build omitted the `SunExp.Dll.GameApi` import for `SunExpResourceCache`.

### Error
```text
CS0103: The name 'SunExpResourceCache' does not exist in the current context.
```

### Suggested Fix
Check the existing resource loader namespace before adding a new visual asset catalog; `SunExpResourceCache` lives in `SunExp.Dll.GameApi`, not Infrastructure.

### Metadata
- Reproducible: yes
- Related Files: SunExp-Dev/Hooks/Visual/StarScoreFlightGlyphAssets.cs

### Resolution
- **Resolved**: 2026-07-16T16:08:00+08:00
- **Notes**: Added the existing GameApi namespace import and rebuilt.

---

## [ERR-20260716-005] unity-batch-wrapper-exit-mismatch

**Logged**: 2026-07-16T16:19:29+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
The PowerShell visual-bundle wrapper returned exit code 1 with no console output even though Unity completed the requested method, rebuilt the bundle, and logged return code 0.

### Error
```text
Build-SunExpVisualBundle.ps1: process exit code 1
Unity log: Built SunExp visual bundle ... return code 0
```

### Suggested Fix
Make the wrapper launch Unity through a process API that reliably captures the real child exit code, then accept success only when both the build marker and updated bundle are present.

### Metadata
- Reproducible: yes
- Related Files: tools/Build-SunExpVisualBundle.ps1, SunExp-Dev/VisualAssets/sunexp_visuals.unity-build.log

### Resolution
- **Resolved**: 2026-07-16T16:21:59+08:00
- **Notes**: Replaced the direct native invocation with a hidden `Start-Process -Wait -PassThru` launch so the wrapper captures Unity's child exit code without terminating before its artifact checks.

---

## [ERR-20260715-004] dimension-shop-missing-runtime-reference

**Logged**: 2026-07-15T15:10:00+08:00
**Priority**: low
**Status**: resolved
**Area**: build

### Summary
The first Dimension Shop build omitted the assembly and namespace needed by newly referenced game types.

### Error
```text
Loxodon.Framework.Obfuscation types and GameEntryUI could not be resolved.
```

### Context
- `DimensionShopGameApi` reaches `GameRuntimeData`, whose dependency graph requires the Loxodon obfuscation assembly.
- `DimensionShopService` uses `GameEntryUI`, which lives in `Witch.UI.Window`.

### Suggested Fix
When a new GameApi facade crosses into another managed assembly, inspect the type's assembly before the first build and add the matching project reference and namespace together.

### Metadata
- Reproducible: yes
- Related Files: SunExp-Dev/SunExp.Dll.csproj, SunExp-Dev/Mechanics/DimensionShopService.cs

### Resolution
- **Resolved**: 2026-07-15T15:13:00+08:00
- **Notes**: Added `Loxodon.Framework.Obfuscation.dll` and `using Witch.UI.Window`; the release build now completes with zero warnings and errors.

---

## [ERR-20260715-005] powershell-source-assertion-quoting

**Logged**: 2026-07-15T15:27:00+08:00
**Priority**: low
**Status**: resolved
**Area**: test

### Summary
A PowerShell source assertion used C-style escaped quotes and a second assertion named a method that did not exist.

### Error
```text
The term '\ + DimensionShopGameApi.LocalPlayerScope()' is not recognized.
Dimension shop UI must expose the crystal-priced refresh action.
```

### Context
- PowerShell does not use backslash to escape quotes in a double-quoted string.
- The implemented service method is `Refresh`, not `TryRefresh`.

### Suggested Fix
Use a single-quoted PowerShell literal when asserting C# text that contains double quotes, and verify exact symbols with `rg` before adding source assertions.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-SunExpArchitecture.ps1

### Resolution
- **Resolved**: 2026-07-15T15:29:00+08:00
- **Notes**: Corrected the literal and method name; the architecture gate passes.

---

## [ERR-20260714-001] card-art-skill-path-assumption

**Logged**: 2026-07-14T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
An inspection command assumed shipped mod resource paths were rooted directly at `SunExp/ModResource` and `GoldExp/ModResource`.

### Error
```text
rg: GoldExp: system cannot find the file specified
rg: SunExp/ModResource/Data/Card: system cannot find the path specified
```

### Context
- Command attempted while reviewing `.codex/skills/sunexp-card-art-style`.
- This workspace contains `SunExp`, `SunExp-Dev`, and `GoldExp-Dev`; resource ownership must be discovered before querying fixed paths.
- `rg --files` on Windows emits backslash-separated paths, so slash-only filters can also miss results.

### Suggested Fix
Discover resource roots with `rg --files` or `Get-ChildItem` first, then use separator-agnostic filters such as `[\\/]`.

### Metadata
- Reproducible: yes
- Related Files: .codex/skills/sunexp-card-art-style/SKILL.md

### Resolution
- **Resolved**: 2026-07-14T00:00:00+08:00
- **Notes**: Switched to repository discovery instead of assuming the documented example paths exist verbatim in the current checkout.

---

## [ERR-20260708-002] aura-ui-modal-host-missing-system-using

**Logged**: 2026-07-08T20:20:00+08:00
**Priority**: low
**Status**: pending
**Area**: backend

### Summary
Adding a shared UI helper that uses `Action<string>` needs an explicit `using System;` in the shared source file.

### Error
```text
AuraUiShared\AuraUiModalHost.cs: error CS0246: could not find type or namespace name Action<>
AuraUiShared\AuraUiModalHost.cs: error CS0104: Object is ambiguous between UnityEngine.Object and object
```

### Context
- Command attempted: `tools\Build-SunExpDll.ps1`.
- New file: `AuraUiShared\AuraUiModalHost.cs`.

### Suggested Fix
Add `using System;` before Unity/UI using directives in files that expose `Action<>`, and call `UnityEngine.Object.Destroy` explicitly when `System` is imported.

### Metadata
- Reproducible: yes
- Related Files: AuraUiShared/AuraUiModalHost.cs

### Resolution
- **Resolved**: 2026-07-08T20:23:00+08:00
- **Notes**: Added `using System;` and qualified `UnityEngine.Object.Destroy`.

---

## [ERR-20260708-003] resource-cache-delegation-leftover-fields

**Logged**: 2026-07-08T20:25:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
When delegating a local cache facade to a shared cache, old catch blocks can retain references to removed local cache fields.

### Error
```text
SunExpResourceCache.cs: error CS0103: current context does not contain ObjectArrayCache/key/AddCategoryKey
```

### Context
- Command attempted: `tools\Build-SunExpDll.ps1`.
- File migrated from local dictionaries to `AuraSharedResourceCache`.

### Suggested Fix
After removing local cache fields, scan the whole file for deleted helper names before rebuilding.

### Metadata
- Reproducible: yes
- Related Files: SunExp-Dev/GameApi/SunExpResourceCache.cs

### Resolution
- **Resolved**: 2026-07-08T20:26:00+08:00
- **Notes**: Removed leftover local cache writes from the delegated `LoadAll` catch block.

---

## [ERR-20260708-004] shared-core-test-project-missing-new-runtime-includes

**Logged**: 2026-07-08T20:35:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
`AuraSharedCore.Tests` uses an explicit compile list, so new shared core runtime files must be added to the test csproj before contract tests can reference them.

### Error
```text
Program.cs: error CS0103: current context does not contain AuraFeatureSwitchRuntime/AuraLifecycleOperationLedger
```

### Context
- Command attempted: `tools\Test-AuraSharedCore.ps1`.
- Added tests for shared feature switches and lifecycle operation claims.

### Suggested Fix
When adding `AuraSharedCore/*.cs` files used by the test harness, update `AuraSharedCore.Tests/AuraSharedCore.Tests.csproj`.

### Metadata
- Reproducible: yes
- Related Files: AuraSharedCore.Tests/AuraSharedCore.Tests.csproj, AuraSharedCore.Tests/Program.cs

### Resolution
- **Resolved**: 2026-07-08T20:37:00+08:00
- **Notes**: Added feature switch, lifecycle session, and lifecycle operation ledger files to the test project.

---

## [ERR-20260708-005] auratools-test-project-needs-shared-rpc-sender-only

**Logged**: 2026-07-08T20:45:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
AuraTools tests compile selected source files without Witch references, so shared DTO-like RPC sender context should be split from the Witch-dependent RPC authority binder.

### Error
```text
AuraToolsRpcSender.cs: error CS0246: could not find AuraRpcSender
```

### Context
- Command attempted: `tools\Test-SharedReleaseGate.ps1`.
- The release gate reached `auratools-feature-tests`, which uses `AuraToolsExp-Dev.Tests`.

### Suggested Fix
Keep `AuraRpcSender` in a small no-Witch shared file and include that file in consumer unit-test projects; keep hook registration in `AuraRpcAuthorityRuntime`.

### Metadata
- Reproducible: yes
- Related Files: AuraSharedCore/AuraRpcSender.cs, AuraSharedCore/AuraRpcAuthorityRuntime.cs, AuraToolsExp-Dev.Tests/AuraToolsExp-Dev.Tests.csproj

### Resolution
- **Resolved**: 2026-07-08T20:47:00+08:00
- **Notes**: Split `AuraRpcSender` into its own shared file and linked it into AuraTools tests.

---

## [ERR-20260708-001] shared-release-gate-parallel-test-contention

**Logged**: 2026-07-08T18:31:33+08:00
**Priority**: low
**Status**: pending
**Area**: tests

### Summary
`tools\Test-SharedReleaseGate.ps1` can fail with a locked test DLL if it is run in parallel with `tools\Test-AuraSharedCore.ps1`.

### Error
```text
CSC : error CS2012: cannot open AuraSharedCore.Tests.dll for writing because it is being used by another process.
```

### Context
- Commands attempted in parallel: `tools\Test-AuraSharedCore.ps1` and `tools\Test-SharedReleaseGate.ps1`.
- The release gate internally runs the shared core contract step, so both commands write the same `AuraSharedCore.Tests\obj\Release\net8.0` output.
- Serial rerun of `tools\Test-SharedReleaseGate.ps1` passed.

### Suggested Fix
Run shared release gates and shared core test harnesses serially, or give parallel invocations separate MSBuild output directories.

### Metadata
- Reproducible: yes
- Related Files: tools\Test-SharedReleaseGate.ps1, tools\Test-AuraSharedCore.ps1

---

## [ERR-20260715-001] parallel-inventory-rg-no-match

**Logged**: 2026-07-15T14:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A parallel repository inventory call failed because `rg --files -g AGENTS.md` returned exit code 1 when no file matched.

### Error
```text
Script error: Exit code: 1
```

### Context
- The no-match search ran inside `Promise.all`, so one expected `rg` exit code hid the other successful command results.
- The repository contains no matching `AGENTS.md` file.

### Suggested Fix
Normalize expected `rg` no-match results with `if ($LASTEXITCODE -eq 1) { exit 0 }` before using the command in a parallel batch.

### Metadata
- Reproducible: yes
- Related Files: none
- Recurrence-Count: 4
- Last-Seen: 2026-07-16

### Resolution
- **Resolved**: 2026-07-15T14:01:00+08:00
- **Notes**: Re-ran the inventory with explicit no-match handling. The pattern recurred on 2026-07-16 in an AGENTS.md inventory and a compound binary-symbol probe; both follow-ups normalized expected no-match results.

---

## [ERR-20260715-002] powershell-rg-directory-wildcard

**Logged**: 2026-07-15T14:05:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
Passing `.codex\skills\sunexp-*` as an rg path is invalid on Windows because rg does not expand that directory wildcard.

### Error
```text
rg: .codex\skills\sunexp-*: The filename, directory name, or volume label syntax is incorrect. (os error 123)
```

### Context
- The intended search covered several sibling skill directories.
- The invalid path also caused an otherwise useful search command to return exit code 1.

### Suggested Fix
Search the concrete parent directory and constrain matches with rg globs, or enumerate explicit directories in PowerShell before invoking rg.

### Metadata
- Reproducible: yes
- Related Files: .codex/skills
- Recurrence-Count: 3

### Resolution
- **Resolved**: 2026-07-15T14:06:00+08:00
- **Notes**: Re-ran searches against `.codex\skills` or explicit paths.

### Recurrence
- **Observed**: 2026-07-16T18:05:00+08:00
- **Notes**: Passed `Aura*Shared`, `*ArbiterShared`, and `Ui*Shared` as rg directory arguments during a performance scan; use explicit directory arrays on Windows.
- **Observed**: 2026-07-16T18:35:00+08:00
- **Notes**: Reused `Aura*Shared` in a cache-lifecycle search; future repository searches must enumerate the concrete shared directories.
- **Observed**: 2026-07-16T20:10:00+08:00
- **Notes**: Passed `**/.editorconfig` as a PowerShell/rg path while checking line-ending policy; use `rg --files -g .editorconfig` and handle an empty result explicitly.

---

## [ERR-20260715-003] broad-parallel-decompile-search-timeout

**Logged**: 2026-07-15T14:08:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A broad content search across the whole decompile snapshot timed out and caused the enclosing parallel batch to discard completed results.

### Error
```text
command timed out after 34099 milliseconds
```

### Context
- The search mixed broad `Truth`, currency, price, and shop terms across multiple large decompiled assemblies.
- The commands ran under `Promise.all`, so the timeout rejected the combined result.

### Suggested Fix
First locate candidate filenames with `rg --files` or `rg -l`, then search only the small set of relevant classes with a longer timeout.

### Metadata
- Reproducible: yes
- Related Files: 开发参考资料/反编译文件夹v1.0.23816797
- Recurrence-Count: 3
- Last-Seen: 2026-07-16

### Resolution
- **Resolved**: 2026-07-15T14:09:00+08:00
- **Notes**: Narrowed analysis to ShopUI, ShopItem, OutsiderShopUI, OutsideShopItem, map flow, and currency persistence classes. On 2026-07-16, the same broad-search pattern recurred while locating ModHookContext; the successful retry targeted the four exact decompiled files.

---

## [ERR-20260715-004] powershell-assembly-resolve-recursion

**Logged**: 2026-07-15T15:10:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: infra

### Summary
Loading `Aura.Shared.dll` through a PowerShell `AssemblyResolve` scriptblock recursively re-entered the resolver and overflowed the PowerShell process stack.

### Error
```text
Stack overflow ... DynamicClass.lambda_method9 ... AssemblyLoadContext.InvokeResolveEvent
Exit code: -1073741571
```

### Context
- The ad hoc harness attempted to invoke the native start-barrier capability probe outside Unity.
- Resolving managed game dependencies from inside PowerShell's resolver callback recursively triggered the same callback.
- The product assembly itself still built successfully; the failure was isolated to the external reflection harness.

### Suggested Fix
Use a small compiled probe harness with explicit dependency references and deterministic load paths, or rely on compile-time and source-contract checks until an in-game probe host is available. Do not install a PowerShell `AssemblyResolve` scriptblock for this dependency graph.

### Metadata
- Reproducible: yes
- Related Files: AuraDirectorShared/AuraDirectorNativeStartBarrierProbe.cs

### Resolution
- **Resolved**: 2026-07-15T15:12:00+08:00
- **Notes**: Did not retry the unsafe resolver path; retained build validation and source-level capability assertions.

---

## [ERR-20260715-005] repeated-powershell-search-quoting-errors

**Logged**: 2026-07-15T15:20:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
Two repository search attempts failed because one reused an invalid Windows wildcard path and another embedded an improperly quoted regular expression in PowerShell.

### Error
```text
rg: *.ps1: The filename, directory name, or volume label syntax is incorrect. (os error 123)
ParserError: Missing expression after unary operator ','.
```

### Context
- The first command repeated the directory-wildcard mistake already recorded in `ERR-20260715-002`.
- The second mixed PowerShell double-quoted syntax with regex quote characters.

### Suggested Fix
Pass a concrete search root with `-g '*.ps1'` and use a single-quoted regex. Normalize expected no-match exit code 1 when searches run in a batch.

### Metadata
- Reproducible: yes
- Related Files: tools

### Resolution
- **Resolved**: 2026-07-15T15:22:00+08:00
- **Notes**: Re-ran the search against the concrete `tools` root with single-quoted globs.

---

## [ERR-20260715-006] harmony-242-unpatch-api-drift

**Logged**: 2026-07-15T16:05:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: dependencies

### Summary
The isolated Detour backend initially used `Harmony.UnpatchSelf()`, which is not present in Lib.Harmony 2.4.2.

### Error
```text
CS1061: 'Harmony' does not contain a definition for 'UnpatchSelf'
```

### Context
- The technical spike intentionally selected the current Lib.Harmony 2.4.2 package.
- Reflection over its net35-compatible `0Harmony.dll` showed `UnpatchAll(string harmonyID)` and targeted `Unpatch` overloads instead.

### Suggested Fix
Inspect the installed package API rather than relying on examples from older Harmony versions. Unpatch by the backend's unique owner ID so unrelated MOD patches remain intact.

### Metadata
- Reproducible: yes
- Related Files: AuraDirectorDetour-Dev/AuraDirectorReadyToStartDetourBackend.cs

### Resolution
- **Resolved**: 2026-07-15T16:08:00+08:00
- **Notes**: Replaced backend cleanup paths with `harmony.UnpatchAll(HarmonyId)`; a follow-up compile caught and corrected the same stale call in the fixture test.

---

## [ERR-20260716-001] director-provider-registration-signature

**Logged**: 2026-07-16T17:20:00+08:00
**Priority**: low
**Status**: resolved
**Area**: code

### Summary
The first SunExp integration build omitted the owner MOD argument required by `AuraDirectorRuntime.RegisterStartGateProvider`.

### Error
```text
CS7036: No argument was provided for the required parameter 'provider' of RegisterStartGateProvider(string, IAuraDirectorStartGateProvider)
```

### Suggested Fix
Read the new public signature at the call site before compiling integrations, and keep the owner identity explicit for shared provider registration.

### Metadata
- Reproducible: yes
- Related Files: SunExp-Dev/Features/Director/SunExpDirectorRuntime.cs

### Resolution
- **Resolved**: 2026-07-16T17:21:00+08:00
- **Notes**: Passed `SunExpIds.ModId` as the owner and rebuilt successfully.

---

## [ERR-20260716-002] shared-packaging-stale-prototype-copies

**Logged**: 2026-07-16T17:35:00+08:00
**Priority**: low
**Status**: pending
**Area**: infra

### Summary
The first shared release gate failed because prototype MOD roots still contained the previous `Aura.Shared.dll` build.

### Error
```text
Packaged Aura.Shared.dll hash mismatch: TestMods\SkinExp\Scripts\Aura.Shared.dll
```

### Suggested Fix
After changing shared runtime sources, rebuild every consumer listed by `Test-SharedDllPackaging.ps1`, not only the three main consumers.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-SharedDllPackaging.ps1
- Recurrence-Count: 2

### Resolution
- **Resolved**: 2026-07-16T17:37:00+08:00
- **Notes**: Rebuilt all five prototype consumers, propagated the shared binary, and reran the complete release gate successfully.

### Recurrence
- **Observed**: 2026-07-16T14:26:00+08:00
- **Notes**: A clean-source release-gate run rebuilt `Aura.Shared.dll` to 902144 bytes while all five prototype packages remained at 901120 bytes; the packaging hash gate failed again.
- **Observed**: 2026-07-16T18:45:00+08:00
- **Notes**: Building the SunExp evacuation feature refreshed the shared project output and SunExp package while the SanGuoShaExp and AuraToolsExp packages retained the earlier hash; resolved through the main-consumer build before final validation.

---

## [ERR-20260716-003] powershell-inventory-batch-failure

**Logged**: 2026-07-16T14:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
The first repository inventory batches stopped when an expected `rg` no-match and an invalid PowerShell pipeline caused non-zero exits.

### Error
```text
rg returned exit code 1 when no AGENTS.md existed.
ParserError: An empty pipe element is not allowed.
```

### Suggested Fix
Use `Get-ChildItem` for optional-file discovery, collect PowerShell rows before piping to `Format-Table`, and use `Promise.allSettled` so one independent inventory command does not hide other results.

### Metadata
- Reproducible: yes
- Related Files: .learnings/ERRORS.md
- Recurrence-Count: 12

### Resolution
- **Resolved**: 2026-07-16T14:30:00+08:00
- **Notes**: Re-ran the inventory with a valid row accumulator and failure-isolated command orchestration.

### Recurrence
- **Observed**: 2026-07-16T17:55:00+08:00
- **Notes**: Repeated the invalid direct `foreach (...) { ... } | Format-Table` form in a parallel inventory batch; fixed by accumulating rows first and using failure-isolated orchestration.
- **Observed**: 2026-07-16T18:10:00+08:00
- **Notes**: Repeated the same invalid direct `foreach (...) { ... } | Format-Table` form while counting test-project LOC; collect `$rows` before formatting.
- **Observed**: 2026-07-16T20:12:00+08:00
- **Notes**: Combined optional `rg`/`git config` probes without normalizing their expected exit code 1, causing the whole inspection cell to report failure.
- **Observed**: 2026-07-16T20:45:00+08:00
- **Notes**: Twice piped a top-level PowerShell `foreach` expression directly into `Format-Table` during architecture inventory; assign the loop output to `$rows` before piping.
- **Observed**: 2026-07-17T12:40:00+08:00
- **Notes**: Appended an expected zero-match `rg` probe to otherwise successful diff/status checks, causing the combined inspection command to return exit code 1; keep optional absence assertions failure-isolated.

---
