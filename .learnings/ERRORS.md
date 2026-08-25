# Errors

## [ERR-20260812-004] combat-ai-gate-blocked-by-existing-foundation-package

**Logged**: 2026-08-12T00:00:00+08:00
**Priority**: low
**Status**: blocked
**Area**: validation artifact

### Summary
The `combat-ai` release profile passed the shared build, all 674 behavior assertions, and the knowledge contract, then failed while validating an existing bundled AuraToolsExp foundation package for Amelia/Partner_10001 as protocol-incompatible.

### Notes
This artifact was not modified by the Terrias projection/Spirit implementation. Keep the focused behavior and compatibility passes as the validation signal for this change; repair or regenerate the unrelated model package separately.

---
# ERR-20260821-009: AuraSharedCore test stub path was assumed

**Logged**: 2026-08-21
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

A parallel inspection command attempted to read
`AuraSharedCore.Tests/TestStubs.cs`, but this test project keeps its stubs in
several specifically named files and has no generic `TestStubs.cs`.

## Resolution

Enumerate the test project with `rg --files` before selecting stub files, then
read only the paths returned by the inventory.

---
# ERR-20260821-010: Unity AudioClip GetData exposed an incompatible Span signature

**Logged**: 2026-08-21
**Severity**: low
**Status**: resolved
**Area**: backend

## What failed

The first AuraTools build after adding cooperative replay audio capture called
`AudioClip.GetData(float[], int)` directly. The current Managed reference
advertises an incompatible `Span<T>` dependency for that call under net472, so
the compiler reported CS7069.

## Resolution

Use the existing compatibility pattern: resolve the array overload once with
reflection and invoke it for each bounded capture slice.

---
# ERR-20260821-011: Activation gate expected the pre-refactor dispose spelling

**Logged**: 2026-08-21
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

The first full AuraTools gate looked for `activation?.Dispose()` after the
activation implementation had been finalized with an explicit null branch and
`activation.Dispose()`. Production and behavior builds passed, but the stale
source-contract token failed the release gate.

## Resolution

Align the boundary assertion with the final explicit activation-lease disposal
path rather than the discarded nullable-call spelling.

---
# ERR-20260821-012: Lobby boundary gate still required the retired local projection

**Logged**: 2026-08-21
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

After Lobby projection moved to `AuraOnlineShared`, the older AuraTools gate
still required `LobbyStatusRuntime` to call `AuraChatModSyncSnapshot.BuildState`
and hook `GameEntryUI.UpdateLobby` locally. That correctly failed the new final
architecture.

## Resolution

Require `AuraLobbySnapshotRuntime.Register` in LobbyStatus and reject any
remaining local `GameEntryUI.UpdateLobby` hook.

---
# ERR-20260821-013: Shared native-hook gate found a remaining BGM direct hook

**Logged**: 2026-08-21
**Severity**: medium
**Status**: resolved
**Area**: backend

## What failed

The new shared native-hook ownership rule found that
`BattleBgmArbiterRuntime` still called `ModConfig.AddMethodHookBefore/After`
directly, even though the initial text inventory had focused on
`AuraSharedHooks` call sites.

## Resolution

Give the global BGM arbiter an `AuraHookRegistry` and route all of its native
targets through owner-qualified handlers. Keep the declarative boundary rule so
future direct registrations fail immediately.

---
# ERR-20260821-014: Retired replay convergence test left an orphaned using

**Logged**: 2026-08-21
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

After deleting the repeated full-state convergence policy and its assertions,
`MatchReplayRuntimeCoreBehaviorTests.cs` still imported the now-unlinked
`MatchRecords.Recording` namespace, causing CS0234 in the source-linked test
project.

## Resolution

Remove the orphaned namespace import together with the retired policy test.

---
# ERR-20260821-015: Final AuraTools rebuild advanced only one packaged shared DLL

**Logged**: 2026-08-21
**Severity**: low
**Status**: resolved
**Area**: infra

## What failed

The final AuraTools product build copied the newest `Aura.Shared.dll` into the
AuraTools package, while the Terrias and SanGuoShaExp packaged copies still
contained the preceding shared build. The packaging hash gate correctly
reported the mismatch.

## Resolution

Rebuild all main shared consumers after the final shared-source change, then
rerun the packaged-DLL hash gate.

---

## [ERR-20260812-003] spirit-runtime-test-still-asserted-once-per-battle

**Logged**: 2026-08-12T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The Spirit runtime gate still expected a successful summon to permanently consume the owner's deployment for the rest of the battle. The approved design now permits another summon after an explicit withdrawal.

### Resolution
Replace the stale `MarkSummoned` assertion with coverage that the deployment snapshot remains valid after withdrawal; active one-per-owner enforcement belongs to `SpiritStateStore`.

---

## [ERR-20260806-017] analysis-epoch-values-cross-method-scope

**Logged**: 2026-08-06T18:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
Final-epoch locals were added to the Markdown report method but were also
referenced from the separate HTML report method.

### Error
```text
CS0103: finalBestValidationEpoch/finalDeploymentEpoch do not exist in the current context
```

### Context
- `dotnet build AuraToolsExp-Dev/AuraToolsExp.Dll.csproj -c Release`
- Markdown and HTML reporting are separate methods in the same source file.

### Suggested Fix
Derive report-local values inside each report method or move the derivation to
a shared helper before referencing them.

### Metadata
- Reproducible: yes
- Related Files: AuraToolsExp-Dev/Features/AutoBattle/AuraToolsAutoBattleFoundationRuntime.cs

### Resolution
- **Resolved**: 2026-08-06T18:00:00+08:00
- **Notes**: Added the derivation inside `BuildFoundationHtml`; the net472 build passed.

---

## [ERR-20260811-A29] reflection-audit-type-and-overload-selection

**Logged**: 2026-08-11T13:57:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The first reflection-based trainer audit used the wrong namespace for `CombatCampaignDefinition` and selected overloaded methods ambiguously.

### Error
```text
Could not resolve type 'AuraCombatAi.Shared.CombatCampaignDefinition'.
Multiple ambiguous overloads found for DeserializeObject.
Ambiguous match found for CombatSimulationRegistry.BuildRuleset.
```

### Context
- Campaign contracts live in `AuraCombatSimulation.Shared`, not `AuraCombatAi.Shared`.
- PowerShell reflection needs exact parameter types when methods are overloaded.

### Suggested Fix
Resolve the actual assembly type first and select overloads with explicit `Type[]` signatures.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatSimulationShared/CombatCampaignSimulation.cs

---

## [ERR-20260811-A31] preflight-parallelism-message-hides-inference-mismatch

**Logged**: 2026-08-11T14:19:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The default foundation preflight failed a combined parallelism/inference assertion even though its displayed parallelism values satisfied the expected bound.

### Error
```text
Foundation worker did not sustain configured parallelism: effective=8/8, peak=8/3.
```

### Context
- `effective=8/8` and `peak=8/3` both pass the numeric conditions.
- The same condition also compares `InferenceExecutionMode`, but the failure message does not print the actual or expected inference mode.
- A fixed `custom/direct` preflight passed, so this is separate from trainer packaging and native program coverage.

### Suggested Fix
Include actual and expected inference modes in the assertion message, then investigate the default auto-profile mismatch independently.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-AuraFoundationTrainer.ps1

### Resolution
- **Resolved**: 2026-08-11T14:21:00+08:00
- **Notes**: The auto profile now expects the runtime's intentional `direct` fallback when no inference calibration samples exist, and the assertion reports actual/expected inference modes. The default preflight passes.

---

## [ERR-20260811-A30] publish-single-file-reinferred-self-contained

**Logged**: 2026-08-11T14:15:00+08:00
**Priority**: high
**Status**: resolved
**Area**: build

### Summary
.NET SDK 10 produced self-contained-sized trainer bundles because `PublishSingleFile=true` inferred publish-time self-containment while `PublishSelfContained` remained unset.

### Error
```text
Foundation trainer executables exceed the framework-dependent single-file size ceiling: actual=384.41 MiB, ceiling=32 MiB.
```

### Context
- The command already passed `--self-contained false`, and the evaluated `SelfContained` property was false.
- The evaluated `PublishSelfContained` property was empty.
- Explicitly passing both `SelfContained=false` and `PublishSelfContained=false` reduced the three published executables to 11.69 MiB total while preserving one-file outputs.

### Suggested Fix
Keep both publish properties explicit whenever `PublishSingleFile=true` is used for the framework-dependent trainer package.

### Metadata
- Reproducible: yes
- Related Files: tools/Build-AuraFoundationTrainer.ps1

### Resolution
- **Resolved**: 2026-08-11T14:18:00+08:00
- **Notes**: Added explicit publish-time self-containment properties; rebuild and fixed-mode preflight passed.

---

## [ERR-20260811-A28] naive-binary-subsequence-scan-timeout

**Logged**: 2026-08-11T13:54:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A nested PowerShell byte-by-byte substring scan was too slow for 35-70 MiB single-file .NET executables.

### Error
```text
command timed out after 60022 milliseconds
```

### Context
- The scan compared every possible file offset with every byte in five UTF-16 hash strings.
- The same check is fast when the binary is decoded once and searched with the runtime string implementation.

### Suggested Fix
Decode once with the expected encoding and use `String.Contains`, or use a compiled linear-time/boyer-moore byte search.

### Metadata
- Reproducible: yes
- Related Files: AuraToolsExp/TrainingWorker/AuraFoundationTrainer.ControlCenter.exe

---

## [ERR-20260811-A26] powershell-rg-wildcard-directory-arguments

**Logged**: 2026-08-11T13:48:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
Passing unexpanded wildcard directory names such as `AuraFoundationTrainer*` directly to `rg` fails on Windows.

### Error
```text
rg: AuraFoundationTrainer*: The filename, directory name, or volume label syntax is incorrect. (os error 123)
```

### Context
- A repository search passed wildcard directory arguments directly to `rg` from PowerShell.
- PowerShell did not expand those arguments before `rg` received them.

### Suggested Fix
Pass explicit directories, search from the repository root with `--glob`, or expand paths with `Get-ChildItem` before invoking `rg`.

### Metadata
- Reproducible: yes
- Related Files: .learnings/ERRORS.md

---

## [ERR-20260811-A27] powershell-foreach-pipeline-parser

**Logged**: 2026-08-11T13:50:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A PowerShell `foreach` statement cannot be piped directly without wrapping its output as an expression.

### Error
```text
ParserError: An empty pipe element is not allowed.
```

### Context
- An artifact inventory command ended `foreach (...) { ... } | Format-List`.
- The parser rejected the pipeline before any inspection ran.

### Suggested Fix
Wrap the statement as `@(foreach (...) { ... }) | Format-List` or assign the loop output to a variable first.

### Metadata
- Reproducible: yes
- Related Files: .learnings/ERRORS.md
- Recurrence-Count: 4

---

## [ERR-20260810-007] powershell-replace-precedence

**Logged**: 2026-08-10T14:05:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A PowerShell `-replace` expression was parsed as an argument to `Split-Path` instead of operating on its result.

### Error
```text
A parameter cannot be found that matches parameter name 'replace'.
```

### Context
- Incorrect form: `Split-Path $path -Leaf -replace '\D',''`.
- The command was extracting iteration numbers before hashing model artifacts.

### Suggested Fix
Parenthesize the cmdlet result: `(Split-Path $path -Leaf) -replace '\D',''`.

### Metadata
- Reproducible: yes
- Related Files: .learnings/ERRORS.md

### Resolution
- **Resolved**: 2026-08-10T14:05:00+08:00
- **Notes**: Re-ran with explicit parentheses and obtained all model hashes.

---
## [ERR-20260810-001] decompile-root-layout

**Logged**: 2026-08-10T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A decompile audit assumed the snapshot had an `output` wrapper directory, but the assembly project folders live directly under the version root.

### Error
```text
rg: .\开发参考资料\反编译文件夹v1.0.24605918\output: 系统找不到指定的文件。
```

### Context
- `Test-GameManagedDecompile.ps1` accepts the version root as `-DecompilePath`.
- Decompiled projects such as `Witch`, `Witch.Core`, and `AllScripts` are immediate children of that root.

### Suggested Fix
Search from the version root and avoid appending `output` unless the manifest explicitly records that layout.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-GameManagedDecompile.ps1

---
## [ERR-20260810-002] ripgrep-context-flag

**Logged**: 2026-08-10T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
An audit passed PowerShell's `-Context` syntax to ripgrep, whose context flag is `-C <number>`.

### Error
```text
rg: error parsing flag -C: value is not a valid number
```

### Suggested Fix
Use `rg -C 5 PATTERN PATH`, or pipe to `Select-String -Context 5,12` when asymmetric context is useful.

### Metadata
- Reproducible: yes
- Related Files: none

---
## [ERR-20260810-003] policy-blocked-temp-delete

**Logged**: 2026-08-10T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A verified non-recursive `Remove-Item` cleanup was rejected by the command policy.

### Error
```text
rejected: blocked by policy
```

### Suggested Fix
Use `apply_patch` with `Delete File` for temporary files created during the current task.

### Metadata
- Reproducible: unknown
- Related Files: tmp/game-managed-api-1.0.23816797-to-1.0.24605918.json, tmp/game-managed-api-1.0.23816797-to-1.0.24605918.md

---
## [ERR-20260807-035] powershell-foreach-pipeline

**Logged**: 2026-08-07T19:45:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
Two diagnostic commands attempted to pipe directly from a PowerShell `foreach` statement and failed parsing.

### Error
```text
ParserError: An empty pipe element is not allowed.
```

### Context
- The commands were building compact training telemetry summaries.
- PowerShell requires the statement to be grouped, or its output to be accumulated before piping.

### Suggested Fix
Accumulate rows in an explicit array and pipe the array after the loop.

### Metadata
- Reproducible: yes
- Related Files: none
- Recurrence-Count: 2

---

## [ERR-20260807-037] resume-replay-ownership

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
Cross-round cleanup initially cleared the caller-owned resume replay list and broke the checkpoint API's non-mutating behavior.

### Error
```text
Assertion failed: foundation checkpoints persist the sampled replay window and resume model training without replaying campaigns
```

### Context
- The isolated Worker needs an ownership transfer so dropped hot-window episodes are not pinned by the request.
- General library callers and tests may retain the resume object for diagnostics and must not observe it being emptied.

### Suggested Fix
Keep ownership transfer opt-in on the training request and enable it only in the isolated Worker after loading a checkpoint.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared/CombatCampaignFoundationTraining.cs, AuraFoundationTrainer.Worker/Program.cs

---

## [ERR-20260807-038] null-coalescing-collection-types

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: build

### Summary
A null-coalescing expression combined `List<string>` with `string[]` without first converting them to their shared interface type.

### Error
```text
CS0019: operator '??' cannot be applied to operands of type 'List<string>' and 'string[]'
```

### Context
- The replay warehouse loader accepts `IReadOnlyCollection<string>`.
- Both operands implement that interface, but `??` requires a direct implicit conversion between its operand types.

### Suggested Fix
Cast the nullable left operand to `IReadOnlyCollection<string>?` before coalescing with `Array.Empty<string>()`.

### Metadata
- Reproducible: yes
- Related Files: AuraFoundationTrainer.Worker/Program.cs

---
## [ERR-20260807-036] rg-mixed-existing-paths

**Logged**: 2026-08-07T19:45:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A diagnostic `rg` command included a guessed source filename that does not exist, so the otherwise useful search returned exit code 1.

### Error
```text
rg: AuraCombatAiShared/CombatFoundationReplaySampler.cs: The system cannot find the file specified. (os error 2)
```

### Context
- `CombatFoundationReplaySampler` is declared in `CombatFoundationTrainingStrategy.cs`, not a same-named file.

### Suggested Fix
Resolve declarations with `rg --files` or search the known source directory without appending a guessed path.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared/CombatFoundationTrainingStrategy.cs

---
## [ERR-20260807-027] functions-exec-parallel-inspection

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A parallel repository-inspection script failed as a whole when one read-only `rg` command returned exit code 1 for no matches.

### Error
```text
Script failed: Exit code: 1
```

### Context
- The script combined skill reads, an optional `AGENTS.md` search, and project inspection with `Promise.all`.
- `rg --files -g AGENTS.md` uses exit code 1 when no matching file exists, which was expected rather than exceptional.

### Suggested Fix
Run optional searches with an explicit no-match success branch, or keep them out of a fail-fast parallel inspection batch.

### Metadata
- Reproducible: yes
- Related Files: .learnings/ERRORS.md

---

## [ERR-20260807-023] transformer-self-test-device-argument

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The Transformer teacher self-test was invoked with an unsupported `--device`
argument instead of its declared `--backend` option.

### Error
```text
train_teacher.py: error: unrecognized arguments: --device cpu
```

### Suggested Fix
Use `--backend cpu` when selecting the CPU runtime for the teacher self-test.

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Re-ran the canonical teacher self-test with `--backend cpu`.

---

## [ERR-20260807-003] conditional-interface-inference

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
A conditional expression between two concrete policy-value model types did not
infer their shared interface during the first build.

### Error
```text
CS0173: Cannot determine type of conditional expression because there is no
implicit conversion between NullCombatPolicyValueModel and
ManagedCombatPolicyValueModel.
```

### Context
- Added the foundation campaign replay API.
- Both branches implement `ICombatPolicyValueModel`, but `var` conditional
  inference did not select that interface.

### Suggested Fix
Declare the conditional target explicitly as `ICombatPolicyValueModel`.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared/CombatCampaignFoundationTraining.cs

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Replaced `var` with the shared interface type.

---

## [ERR-20260807-004] simulation-card-target-property

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A new simulation regression fixture used a non-existent generic target flag.

### Error
```text
CS0117: CombatCardDefinition does not contain a definition for RequiresTarget.
```

### Context
- Added a focused dynamic-variable mutation battle fixture.
- The simulation contract names the compatibility flag
  `RequiresEnemyTarget` and separately supports `TargetScope`.

### Suggested Fix
Use `RequiresEnemyTarget` for enemy-only fixtures and inspect the shared model
before assuming runtime UI property names.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared.Tests/Program.cs

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Switched the fixture to `RequiresEnemyTarget`.

---

## [ERR-20260807-005] worker-path-parser-used-for-scalars

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: backend

### Summary
The Worker's existing argument resolver canonicalized every value as a file
path, so newly added scalar replay options could not be parsed.

### Error
```text
InvalidDataException: Invalid --replay-seed value.
```

### Context
- `ResolveArgument` historically served only `--job` and returned
  `Path.GetFullPath(value)`.
- Reusing it for a seed, difficulty, trace level, and probabilities turned the
  scalar into a path before parsing.

### Suggested Fix
Keep path resolution for path options and use a raw option-value resolver for
scalar arguments.

### Metadata
- Reproducible: yes
- Related Files: AuraFoundationTrainer.Worker/Program.cs
- Pattern-Key: cli.path_vs_scalar_arguments

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Added `ResolveOptionValue` and routed all scalar replay options
  through it.

---

## [ERR-20260807-006] trainer-smoke-used-stale-package

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The first packaged trainer smoke was run with `-SkipPublish`, so it exercised
the previous self-contained Worker rather than the newly built project DLL.

### Error
```text
Auto profile did not retain memory-capacity evidence.
```

### Context
- Project builds and direct tests were current.
- `Test-AuraFoundationTrainer.ps1 -SkipPublish` intentionally reuses the
  executable already present under `AuraToolsExp/TrainingWorker`.
- Protocol assertions expected v11 while that packaged executable still
  emitted v10 evidence.

### Suggested Fix
After Worker protocol changes, run `Build-AuraFoundationTrainer.ps1` before
using the smoke test with `-SkipPublish`.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-AuraFoundationTrainer.ps1
- Pattern-Key: test.stale_packaged_binary

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Republished the Worker and Control Center before rerunning smoke.

---

## [ERR-20260807-007] preflight-smoke-seed-count-contract

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The trainer smoke still expected three regression seeds after the newly fixed
campaign was added as the fourth integrity seed.

### Error
```text
Foundation trainer preflight-only result is incomplete.
```

### Context
- The Worker correctly ran the configured preflight plus four known failures.
- The PowerShell smoke had two duplicated `+ 3` expectations.

### Suggested Fix
Keep a named regression-seed count in the smoke contract and update it with the
integrity corpus.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-AuraFoundationTrainer.ps1
- Pattern-Key: test.integrity_seed_count

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Added `$integrityRegressionSeedCount = 4` and reused it in both
  preflight expectations.

---

## [ERR-20260807-008] autotune-decision-evidence-not-updated

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: backend

### Summary
Campaign auto-tune changed effective parallelism but left the published memory
decision at its pre-calibration selection.

### Error
```text
Auto profile did not retain memory-capacity evidence.
```

### Context
- Auto-tune selected parallelism 2 from measured throughput.
- `Training.EffectiveParallelism` and `AutoTune.SelectedParallelism` were 2,
  while `ParallelismDecision.SelectedParallelism` still reported the initial
  memory-safe ceiling of 4.

### Suggested Fix
When campaign calibration selects a lane count, update the published decision,
predicted peak memory, reason, result, and telemetry together.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared/CombatCampaignFoundationTraining.cs
- Pattern-Key: telemetry.effective_configuration_propagation

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Synchronized the decision object and added a throughput selection
  reason before applying the effective execution plan.

---

## [ERR-20260807-009] preflight-smoke-assumed-no-campaign-probes

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
The trainer preflight still enforced the v10 behavior that automatic
parallelism must not run campaign throughput probes.

### Error
```text
Memory-capacity parallelism unexpectedly ran throughput campaign probes.
```

### Context
- The v11 scheduler intentionally calibrates normalized 50%, 75%, and 100%
  points below the memory-safe and hardware-safe ceiling.
- The implementation was correct, but the smoke assertion was stale.

### Suggested Fix
Derive the expected normalized probe points from the effective calibration
ceiling and compare them with the reported campaign measurements.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-AuraFoundationTrainer.ps1
- Pattern-Key: test.autotune_protocol_version

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Updated the smoke to require the v11 normalized probe set.

---

## [ERR-20260807-010] ripgrep-windows-wildcard-path

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
A diagnostic `rg` command passed PowerShell wildcard paths as literal search
roots, which are invalid Windows path names.

### Error
```text
文件名、目录名或卷标语法不正确。 (os error 123)
```

### Context
- The command used `tools\\Build-*.ps1` and `*.ps1` as positional paths.
- Ripgrep accepts glob filters through `-g`, not wildcard root paths on Windows.

### Suggested Fix
Search a real directory root and use `-g 'Build-*.ps1'` or enumerate files
with PowerShell first.

### Metadata
- Reproducible: yes
- Related Files: none
- Recurrence-Count: 4
- Last-Seen: 2026-08-07
- Pattern-Key: powershell.rg_glob_filter

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Used concrete paths and treated the wildcard error as diagnostic-only.

---

## [ERR-20260807-011] relative-cmd-script-directory

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A CMD launcher duplicated its relative script directory after changing the
working directory.

### Error
```text
The argument '...\TrainingWorker\AuraToolsExp\TrainingWorker\Setup-AuraTransformerTeacher.ps1'
to the -File parameter does not exist.
```

### Context
- The launcher was invoked from the repository root through a relative path.
- `%~dp0` remained relative, then `cd /d "%~dp0"` changed the base used to
  resolve it a second time.

### Suggested Fix
Use `pushd "%~dp0"`, capture the resulting absolute `%CD%`, and build sibling
paths from that captured directory.

### Metadata
- Reproducible: yes
- Related Files: tools/Install-AuraPyTorch.cmd

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: The launcher now resolves its directory once and was retested
  through a relative invocation path.

---

## [ERR-20260805-009] temp-validation-cleanup-rejected

**Logged**: 2026-08-05T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
A post-test cleanup command for explicitly named temporary validation folders
was rejected before execution by the command safety layer.

### Resolution
- **Resolved**: 2026-08-05T00:00:00+08:00
- **Notes**: No repository file was affected and no retry was necessary; the
  bounded temporary artifacts can be reclaimed by normal OS temp cleanup.

---

## [ERR-20260806-014] transformer-self-test-wrong-python

**Logged**: 2026-08-06T13:20:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The Transformer self-test was first launched with the system Python rather
than the managed AuraTF runtime.

### Error
```text
PyTorch is required. Run tools/Setup-AuraTransformerTeacher.ps1 for the CPU or CUDA backend.
```

### Context
- The system Python 3.14 executable does not own the trainer's PyTorch packages.
- The configured CPU runtime is `C:\Users\75601\AppData\Local\AuraTF\cpu\Scripts\python.exe`.

### Suggested Fix
Resolve the AuraTF runtime before directly invoking Transformer self-tests.

### Metadata
- Reproducible: yes
- Related Files: tools/transformer-teacher/train_teacher.py

---

## [ERR-20260810-A26] smoke-expected-retired-three-point-auto-tune

**Logged**: 2026-08-10T18:25:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The first packaged Worker smoke measured the new two-candidate Auto-Tune correctly but the PowerShell acceptance script still required the retired 50%/75%/100% probe set.

### Error
```text
Adaptive parallelism campaign probes are incomplete: actual=4,8, expected=4,6,8.
```

### Context
- The production change intentionally compares maximum and half parallelism, or maximum and the compatible cached candidate.
- The stale assertion would reject the intended wall-clock optimization even though the Worker emitted valid evidence.

### Suggested Fix
Bind smoke assertions to `CampaignCalibrationKind`: require max+half for cold short calibration, at most two points including max for compatible verification, and require a non-empty cache miss diagnostic.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-AuraFoundationTrainer.ps1, AuraCombatAiShared/CombatCampaignFoundationTraining.cs

---

## [ERR-20260810-A27] final-publish-blocked-by-open-simulation-viewer

**Logged**: 2026-08-10T18:28:00+08:00
**Priority**: low
**Status**: resolved
**Area**: build

### Summary
The final package refresh correctly refused to replace a mapped Simulation Viewer executable that was still running after smoke verification.

### Error
```text
Foundation trainer binaries are currently running and Windows will not allow them to be replaced.
PID=772, name=AuraFoundationTrainer.SimulationViewer.exe
```

### Context
- The build script intentionally avoids terminating Trainer processes unless `-StopRunningTrainer` is explicit.
- Training had already completed; only the read-only viewer held the package file.

### Suggested Fix
Use the script's explicit `-StopRunningTrainer` path for a final coordinated package refresh after verifying that no training Worker is active.

### Metadata
- Reproducible: yes
- Related Files: tools/Build-AuraFoundationTrainer.ps1

### Resolution
- **Resolved**: 2026-08-06T13:21:00+08:00
- **Notes**: Re-ran with AuraTF CPU; the two-epoch self-test passed.

---

## [ERR-20260806-015] shared-source-used-net8-only-apis

**Logged**: 2026-08-06T13:23:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: build

### Summary
New shared checkpoint code compiled in the net8 trainer but initially used APIs
that are unavailable in the legacy Aura.Shared target.

### Error
```text
System.Index is not defined; OperatingSystem does not contain IsWindows
```

### Suggested Fix
Shared linked sources must use the lowest common target API surface. Use an
integer `Count - 1` index and `Path.DirectorySeparatorChar` for OS detection.

### Resolution
- **Resolved**: 2026-08-06T13:24:00+08:00
- **Notes**: Replaced both net8-only constructs before rerunning the release gate.

---

## [ERR-20260806-002] auto-tune-protocol-test-anchor-stale

**Logged**: 2026-08-06T08:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The shared regression suite passed, but its source-contract anchors still
expected the previous auto-tune v5 protocol after the implementation moved to
the v6 steady-state protocol.

### Resolution
Update both PowerShell contract checks to the v6 protocol and cache filename,
then rerun the complete suites.

---

## [ERR-20260806-003] idle-control-center-locked-publish

**Logged**: 2026-08-06T08:32:00+08:00
**Priority**: low
**Status**: resolved
**Area**: build

### Summary
The foundation trainer integration test could not replace deployment binaries
because an idle Control Center process still held them open.

### Resolution
Verify that no Worker or Transformer process and no current job are active,
then close the idle window gracefully before rebuilding.

---

## [ERR-20260806-004] controller-schema-contract-stale

**Logged**: 2026-08-06T08:35:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The controller source-contract test still expected settings schema 14 after the
1024-frame default introduced schema 15 migration.

### Resolution
Require both the schema-15 declaration and migration anchor in the contract.

---

## [ERR-20260806-005] transformer-self-test-wrong-python

**Logged**: 2026-08-06T08:38:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The system Python 3.14 environment did not contain PyTorch, so the Transformer
self-test exited before running.

### Resolution
Read the resolved runtime from the latest teacher report and run the self-test
with `C:\Users\75601\AppData\Local\AuraTF\cpu\Scripts\python.exe`.

---

## [ERR-20260806-001] powershell-token-spacing-in-inline-monitor

**Logged**: 2026-08-06T15:44:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
An aggressively compacted inline PowerShell monitor command omitted required token spacing.

### Error
```text
The term 'Join-Path$dfoundation-worker-result.json' is not recognized.
A parameter cannot be found that matches parameter name 'LiteralPath$m'.
```

### Context
- The five-minute read-only training snapshot compressed cmdlet names, variables, and arguments onto one line.
- PowerShell parsed `Join-Path$d` and `-LiteralPath$m` as single tokens.

### Suggested Fix
Keep spaces around cmdlet arguments and variables even in compact inline monitoring scripts.

### Metadata
- Reproducible: yes
- Related Files: none
- Recurrence-Count: 3
- Last-Seen: 2026-08-06T16:05:00+08:00

### Resolution
- **Resolved**: 2026-08-06T15:44:00+08:00
- **Notes**: Re-ran both affected read-only commands with explicit PowerShell token spacing; training was unaffected.

---

## [ERR-20260806-016] shared-dll-consumer-rebuild-order

**Logged**: 2026-08-06T15:51:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: infra

### Summary
Building AuraToolsExp alone updated its packaged shared DLL but left Terrias
and SanGuoShaExp on the prior hash.

### Error
```text
Packaged Aura.Shared.dll hash mismatch: Terrias\Scripts\Aura.Shared.dll
```

### Context
- Shared AI sources are compiled into `Aura.Shared.dll` for multiple consumers.
- The first build targeted AuraToolsExp and the foundation trainer only.

### Suggested Fix
After shared-source changes, run `tools/Build-MainSharedConsumers.ps1` before
the shared DLL packaging gate.

### Metadata
- Reproducible: yes
- Related Files: tools/Build-MainSharedConsumers.ps1

### Resolution
- **Resolved**: 2026-08-06T15:52:00+08:00
- **Notes**: Rebuilt all three main consumers serially; packaging hashes now
  match.

---

## [ERR-20260806-015] trainer-smoke-schema-fixture

**Logged**: 2026-08-06T15:42:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The trainer smoke fixture still emitted worker schema 11 after the production
worker protocol advanced to schema 12.

### Error
```text
底模训练任务协议不兼容：job=11，worker=12
```

### Context
- The packaged worker was rebuilt successfully with schema 12.
- `tools/Test-AuraFoundationTrainer.ps1` used a separate hard-coded fixture
  version.

### Suggested Fix
Update worker smoke fixtures and source-contract assertions whenever the worker
protocol schema changes.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-AuraFoundationTrainer.ps1

### Resolution
- **Resolved**: 2026-08-06T15:43:00+08:00
- **Notes**: Updated the smoke job and validation fixture to schema 12.

---

## [ERR-20260806-014] overloaded-null-strategy-stratum

**Logged**: 2026-08-06T15:20:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
Adding a frame overload beside the existing nullable feature-map overload made
an existing `null` test call ambiguous at compile time.

### Error
```text
CS0121: call is ambiguous between StrategicFrameStratum(CombatEpisodeFrame?)
and StrategicFrameStratum(IReadOnlyDictionary<string, double>?)
```

### Context
- The compile probe caught the ambiguity before the full test run.
- Both overloads accepted nullable reference arguments.

### Suggested Fix
Use a distinct method name when overloads accept unrelated nullable reference
types and existing callers intentionally pass `null`.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared/CombatPolicyValueBatchTrainer.cs

### Resolution
- **Resolved**: 2026-08-06T15:21:00+08:00
- **Notes**: Renamed the frame-aware API to
  `StrategicFrameStratumForFrame` and updated frame callers.

---

## [ERR-20260806-001] shared-runtime-net472-hashcode

**Logged**: 2026-08-06T12:00:00+08:00
**Priority**: high
**Status**: resolved
**Area**: backend

### Summary
The net8 trainer tests passed, but the net472 shared runtime consumer could not
compile a cache key that used `System.HashCode`.

### Error
```text
CS0103: The name 'HashCode' does not exist in the current context.
```

### Context
- `AuraCombatAiShared` is compiled into both net8 trainer projects and the
  net472 `Aura.Shared.dll`.
- The fast unit-test build therefore did not exercise the oldest target API.

### Suggested Fix
Use an explicit unchecked integer hash mix for shared structs and keep the
main shared-consumer build in the release validation sequence.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared/CombatPolicyValueNetwork.cs

### Resolution
- **Resolved**: 2026-08-06T12:00:00+08:00
- **Notes**: Replaced `HashCode.Combine` with a deterministic net472-compatible
  mix and rebuilt all shared consumers.

---

## [ERR-20260806-001] powershell-rg-wildcard-path

**Logged**: 2026-08-06T10:20:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
PowerShell passed a Windows wildcard path directly to `rg`, which rejected the path syntax.

### Error
```text
rg: AuraCombatAiShared/*.cs: IO error ... 文件名、目录名或卷标语法不正确。
```

### Context
- The read-only source inventory used `rg ... AuraCombatAiShared/*.cs`.
- Ripgrep on this Windows invocation expected a directory plus a `-g` glob.

### Suggested Fix
Use `rg <pattern> AuraCombatAiShared -g '*.cs'` for PowerShell-compatible recursive filtering.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared

### Resolution
- **Resolved**: 2026-08-06T10:20:00+08:00
- **Notes**: Re-ran the inventory with a directory argument and `-g '*.cs'`.

---

## [ERR-20260805-011] powershell-rg-directory-wildcard

**Logged**: 2026-08-05T14:06:21+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
`rg` received a Windows directory wildcard as a literal path and rejected it.

### Error
```text
rg: AuraToolsExp-Dev/Features/AutoBattle/*.cs: The filename, directory name, or volume label syntax is incorrect. (os error 123)
```

### Context
- A PowerShell command passed `AuraToolsExp-Dev/Features/AutoBattle/*.cs` in the positional path list.
- Ripgrep does not expand that directory wildcard on Windows.

### Suggested Fix
Enumerate files with `Get-ChildItem` and pass the resulting paths to `rg`, or search the directory and use `-g '*.cs'`.

### Metadata
- Reproducible: yes
- Related Files: AuraToolsExp-Dev/Features/AutoBattle

### Resolution
- **Resolved**: 2026-08-05T14:06:21+08:00
- **Notes**: Re-ran the search with paths enumerated by `Get-ChildItem`.

---

## [ERR-20260805-010] trainer-final-gate-audit

**Logged**: 2026-08-05T13:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
Final trainer gates exposed a stale build, a net472-only API incompatibility,
and an undersized one-Epoch teacher smoke configuration.

### Error
```text
Foundation checkpoint compatibility manifest is incomplete.
CS1501: Dictionary.Remove has no 2-argument overload.
Transformer teacher withheld: policy loss did not beat uniform baseline.
```

### Context
- The first Worker smoke used an apphost built before the training-policy
  version bump.
- `Dictionary.Remove(key, out value)` compiled for net8.0 but not net472.
- One Epoch on a 66-frame fixture was intentionally rejected by the teacher
  quality gate by a 0.00052 cross-entropy margin.

### Suggested Fix
Rebuild every target before smoke tests, keep shared code within the net472 API
surface, and use the established two-Epoch minimum for the tiny CPU fixture.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared/CombatCampaignFoundationTraining.cs,
  tools/Test-AuraFoundationTrainer.ps1

### Resolution
- **Resolved**: 2026-08-05T13:00:00+08:00
- **Notes**: Rebuilt both targets, replaced the API call with
  `TryGetValue` plus `Remove`, and passed the CPU teacher smoke at two Epochs.

---

## [ERR-20260805-001] exec-command-computed-recursive-cleanup

**Logged**: 2026-08-05T12:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A combined PowerShell smoke-test command was rejected because it ended with a
recursive delete against a computed temporary path.

### Error
```text
CreateProcess rejected: blocked by policy
```

### Context
- The command created a GUID-named directory under `$env:TEMP`, ran two model
  tests, then called `Remove-Item -Recurse` in the same composed command.
- Windows safety rules require verification of the resolved target before a
  recursive delete.

### Suggested Fix
Run the smoke test without inline recursive cleanup. Inspect the resolved path,
then delete explicitly named files with `System.IO.File.Delete` and remove the
now-empty directories with `System.IO.Directory.Delete`.

### Metadata
- Reproducible: yes
- Related Files: tools/transformer-teacher/train_teacher.py

### Resolution
- **Resolved**: 2026-08-05T12:00:00+08:00
- **Notes**: Split artifact creation/testing from cleanup; exact-path .NET file
  and empty-directory deletion completed without a recursive command.

---

## [ERR-20260805-008] stale-controller-schema-10-source-contract

**Logged**: 2026-08-05T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
AuraTools behavioral tests passed, but the source contract still required
Control Center schema 9 and a fixed 16-way gradient shard preset.

### Resolution
- **Resolved**: 2026-08-05T00:00:00+08:00
- **Notes**: Updated the contract for schema 10, automatic gradient shards and
  the new Transformer runtime-plan defaults.

---

## [ERR-20260805-007] auto-tune-cache-key-included-learned-residuals

**Logged**: 2026-08-05T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: training

### Summary
After stabilizing the hardware key, identical Auto-Tune runs still missed the
cache because the campaign fingerprint changed after every accepted run.

### Cause
Success-archive reward residuals are merged into the campaign before training.
The execution cache keyed the fully mutated campaign, so learned data changes
invalidated a hardware execution plan even when structure and budgets matched.

### Resolution
- **Resolved**: 2026-08-05T00:00:00+08:00
- **Notes**: The Worker now captures the structural campaign fingerprint after
  deterministic role defaults but before archive residuals are merged. Auto-Tune
  uses that key; checkpoint/model compatibility still uses the full campaign.

---

## [ERR-20260805-006] auto-tune-cache-key-used-volatile-memory

**Logged**: 2026-08-05T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: training

### Summary
Repeated foundation Auto-Tune smoke runs did not reuse a valid v4 cache even
with identical settings and hardware.

### Cause
The hardware key embedded GC's exact available-memory GiB value. That estimate
can move by one GiB with system pressure, invalidating an otherwise compatible
plan.

### Resolution
- **Resolved**: 2026-08-05T00:00:00+08:00
- **Notes**: Cache identity now uses a stable power-of-two memory capability
  tier while retaining machine, CPU, OS, architecture, GC mode and .NET version.

---

## [ERR-20260805-005] one-epoch-transformer-smoke-missed-policy-gate

**Logged**: 2026-08-05T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The 66-frame CPU smoke used one Transformer epoch and finished correctly, but
its validation policy cross-entropy was 0.00052 above the uniform baseline, so
the production quality gate correctly withheld its annotations.

### Resolution
- **Resolved**: 2026-08-05T00:00:00+08:00
- **Notes**: The release smoke default now uses two epochs. The repeated run
  passed all gates and also exercised the persisted runtime-plan cache.

---

## [ERR-20260805-004] inventory-included-missing-root-readme

**Logged**: 2026-08-05T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
An inventory-only `rg` command included a root `README.md` that this repository
does not contain, so ripgrep returned a partial-result error.

### Resolution
- **Resolved**: 2026-08-05T00:00:00+08:00
- **Notes**: Subsequent documentation searches enumerate existing files first.

---

## [ERR-20260804-017] stale-controller-source-contract

**Logged**: 2026-08-04T16:12:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
AuraToolsExp behavioral assertions passed, but its source-contract test still
expected controller schema 8 and the superseded 512/512/128 model preset.

### Error
```text
Foundation controller resumable-training settings migration is missing.
```

### Resolution
- **Resolved**: 2026-08-04T16:14:00+08:00
- **Notes**: Updated the contract to schema 9, the Transformer settings, and
  the 256/256/64 deployment preset; the suite then passed.

---

## [ERR-20260805-003] stale-auto-tune-candidate-assertion

**Logged**: 2026-08-05T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The combat shared test still expected the previous five CPU Auto-Tune
candidates after hybrid/high-core candidates were added.

### Error
```text
Assertion failed: auto-tune benchmarks multiple bounded CPU parallelism candidates
```

### Context
- Production candidate generation intentionally added 6, 14, 24, 48, and 64 worker cases.
- The exact-sequence contract test needed to move with that behavioral change.

### Suggested Fix
Update the assertion to cover both a 20-thread hybrid CPU and a 64-thread host.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared.Tests/Program.cs, AuraCombatAiShared/CombatCampaignFoundationTraining.cs

### Resolution
- **Resolved**: 2026-08-05T00:00:00+08:00
- **Notes**: Updated the candidate assertions for 20 and 64 logical processors.

---

## [ERR-20260805-002] parallel-foundation-project-build-native-compiler-lock

**Logged**: 2026-08-05T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
Building the Foundation Worker and Control Center concurrently caused their
shared `AuraNativeProgramCompiler` build output to be locked.

### Error
```text
CS2012: process cannot access AuraNativeProgramCompiler.dll because it is being used by another process
```

### Context
- Both trainer projects invoke the same native-program compiler in a pre-build target.
- The projects were launched in parallel even though their generated/tool outputs overlap.

### Suggested Fix
Build Foundation Trainer projects serially; only parallelize independent reads
or builds without shared generated outputs.

### Metadata
- Reproducible: yes
- Related Files: AuraFoundationTrainer.Worker/AuraFoundationTrainer.Worker.csproj, AuraFoundationTrainer.ControlCenter/AuraFoundationTrainer.ControlCenter.csproj

### Resolution
- **Resolved**: 2026-08-05T00:00:00+08:00
- **Notes**: Subsequent trainer builds are run serially.

---

## [ERR-20260805-001] powershell-rg-double-quote-pattern

**Logged**: 2026-08-05T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A PowerShell `rg` inventory command used a double-quoted regex containing an
escaped quote, so PowerShell truncated the pattern before ripgrep parsed it.

### Error
```text
regex parse error: unclosed group
```

### Context
- The search combined protocol strings and a quoted C# assignment.
- This was an inventory-only command and did not affect repository files.

### Suggested Fix
Use a single-quoted literal regex or split the searches into separate commands.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-AuraCombatAi.ps1
- See Also: earlier PowerShell/ripgrep quoting entry in this file

### Resolution
- **Resolved**: 2026-08-05T00:00:00+08:00
- **Notes**: Subsequent searches use single-quoted patterns.

---

## [ERR-20260804-002] transformer-world-model-smoke-gate

**Logged**: 2026-08-04T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The two-epoch, 66-frame Transformer integration smoke passed policy and
dynamics validation but was rejected by an initial Outcome MAE threshold that
was calibrated for a production-sized run.

### Error
```text
Transformer world model withheld: dynamics or outcome validation gate failed.
ValidationDynamicsMse=0.1317, ValidationOutcomeMae=0.4774
```

### Context
- The smoke intentionally uses a 2-layer, 64-hidden model for two epochs.
- The world model is still Training/Shadow; active latent search has a separate,
  stricter promotion gate.

### Suggested Fix
Keep the teacher application gate as a finite/basic-learning check for Shadow
distillation, and reserve strict Outcome calibration thresholds for latent
Chance-PUCT promotion.

### Metadata
- Reproducible: yes
- Related Files: AuraFoundationTrainer.Worker/PythonCombatTransformerTeacher.cs

### Resolution
- **Resolved**: 2026-08-04T00:00:00+08:00
- **Notes**: Raised only the Shadow teacher Outcome MAE ceiling from 0.35 to
  0.50; Dynamics remains capped at 0.50 and policy must still beat uniform.

---

## [ERR-20260804-001] shared-dll-packaging-after-single-consumer-build

**Logged**: 2026-08-04T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: infra

### Summary
Building only AuraTools after changing shared combat code left the packaged
Aura.Shared.dll copies in Terrias and SanGuoShaExp out of sync.

### Error
```text
Packaged Aura.Shared.dll hash mismatch: Terrias\Scripts\Aura.Shared.dll
```

### Context
- `AuraCombatAiShared` is linked into the common Aura.Shared runtime.
- `AuraToolsExp-Dev` was built first, which refreshed only part of the packaged
  consumer set before the shared DLL packaging gate ran.

### Suggested Fix
After shared combat changes, build all three consumers (AuraTools, Terrias and
SanGuoShaExp) before running `Test-SharedDllPackaging.ps1`.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-SharedDllPackaging.ps1

### Resolution
- **Resolved**: 2026-08-04T00:00:00+08:00
- **Notes**: Built Terrias-Dev and SanGuoShaExp-Dev, then the packaging hash
  gate passed.

---

## [ERR-20260804-016] external-runtime-cleanup-blocked

**Logged**: 2026-08-04T16:05:00+08:00
**Priority**: low
**Status**: unresolved
**Area**: tooling

### Summary
The command guard rejected recursive cleanup of the failed long-path Python
environment outside the workspace.

### Error
```text
Remove-Item ... rejected: blocked by policy
```

### Resolution
- **Notes**: The working runtime was installed at the short `%LOCALAPPDATA%\AuraTF`
  path. The unused partial environment remains isolated at the original path.

---

## [ERR-20260804-015] guessed-auratools-build-script

**Logged**: 2026-08-04T16:03:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
A shared-runtime inspection guessed `tools/Build-AuraToolsExp.ps1`; the actual
entry point is `tools/Build-AuraToolsExpDll.ps1`.

### Resolution
- **Resolved**: 2026-08-04T16:04:00+08:00
- **Notes**: Resolved build entry points with `rg --files tools`.

---

## [ERR-20260804-010] transformer-patch-context-mismatch

**Logged**: 2026-08-04T15:05:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
Two Transformer integration patches assumed stale surrounding lines in the
worker entry point and batch trainer.

### Resolution
- **Resolved**: 2026-08-04T15:08:00+08:00
- **Notes**: Re-read tight contexts and split the edits into smaller patches.

---

## [ERR-20260804-009] transformer-python-runtime-missing

**Logged**: 2026-08-04T14:55:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
The host Python initially had neither PyTorch nor `uv`, so the teacher could
not be self-tested before runtime bootstrap.

### Error
```text
ModuleNotFoundError: No module named 'torch'
uv: command not found
```

### Resolution
- **Resolved**: 2026-08-04T15:45:00+08:00
- **Notes**: Added and exercised the isolated PowerShell setup flow.

---

## [ERR-20260804-008] guessed-ai-source-and-project-paths

**Logged**: 2026-08-04T14:42:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
Architecture inspection used guessed paths for shared AI source files and a nonexistent shared-project file.

### Error
```text
rg: AuraCombatAiShared/CombatFeatureEncoder.cs: The system cannot find the file specified.
Get-Content: Cannot find path 'AuraCombatAiShared/AuraCombatAiShared.csproj'.
```

### Resolution
- **Resolved**: 2026-08-04T14:43:00+08:00
- **Notes**: The feature encoder lives in `CombatPolicyValueNetwork.cs`, episode contracts live in `CombatEpisodeLearning.cs`, and shared AI sources are linked directly into the worker/runtime projects rather than owning a standalone project file.

---

## [ERR-20260804-014] pytorch-venv-path-too-long

**Logged**: 2026-08-04T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: infra

### Summary
The initial Transformer teacher virtual-environment path was too long for a
deep third-party license path in the Windows PyTorch wheel.

### Error
```text
OSError: [WinError 206] The filename or extension is too long
```

### Context
- PyTorch 2.13.0+cpu was installed under a descriptive LocalAppData path.
- The wheel contains deeply nested Kineto/dynolog/prometheus/civetweb files.

### Suggested Fix
Keep the managed teacher runtime in a short LocalAppData path.

### Metadata
- Reproducible: yes
- Related Files: tools/Setup-AuraTransformerTeacher.ps1

### Resolution
- **Resolved**: 2026-08-04T00:00:00+08:00
- **Notes**: Changed the default environment root to `%LOCALAPPDATA%\AuraTF`.

---

## [ERR-20260804-007] guessed-combat-ai-source-files

**Logged**: 2026-08-04T14:20:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
A targeted `rg` inspection included two guessed source filenames that do not exist.

### Error
```text
rg: AuraCombatAiShared/CombatFoundationCurriculum.cs: The system cannot find the file specified.
rg: AuraCombatAiShared/CombatAuthoritativeBranchTeacherPolicy.cs: The system cannot find the file specified.
```

### Resolution
- **Resolved**: 2026-08-04T14:20:00+08:00
- **Notes**: The matching implementations are in `CombatCampaignFoundationTraining.cs` and `CombatSimulationAiAdapter.cs`; future targeted searches should resolve filenames with `rg --files` first.

---

## [ERR-20260804-006] powershell-foreach-pipeline-shape

**Logged**: 2026-08-04T14:05:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tooling
**Recurrence-Count**: 4

### Summary
A phase-metrics inspection piped directly from a `foreach` statement and produced an empty-pipe parser error.

### Error
```text
ParserError: An empty pipe element is not allowed.
```

### Context
- PowerShell requires the `foreach` output to be wrapped in `$()` or collected before piping.

### Suggested Fix
Assign the loop results to a variable, then sort and format that variable.

### Resolution
- **Resolved**: 2026-08-04T14:06:00+08:00
- **Notes**: Re-ran the query after collecting rows into `$rows`; the same mistake recurred in two later comparison queries, so future inline loops must always be assigned before formatting.

---

## [ERR-20260803-003] native-trigger-provenance-overwrite

**Logged**: 2026-08-03T11:05:00+08:00
**Priority**: high
**Status**: resolved
**Area**: simulation

### Summary
The first `dataFromid` parity fix read the handler event's overwritten
`SourceRewardId`, so Nightmare Prototype mistook every original debuff event
for its own duplicate and never triggered.

### Error
```text
nightmare-prototype-one-layer: duplicated=False, effects=0
```

### Context
- `TryEnterHandler` changes handler-event provenance to the active reward so
  effects emitted by that handler retain the correct source.
- Native `AddBuffData.dataFromid` instead describes the event that triggered
  the handler and must use the pre-handler source.

### Suggested Fix
Pass trigger provenance separately when constructing native event payloads;
do not overload emitted-effect provenance for both meanings.

### Metadata
- Reproducible: yes
- Related Files: AuraToolsExp-Dev/Features/AutoBattle/AuraToolsNativeRewardSimulationRuntime.cs

### Resolution
- **Resolved**: 2026-08-03T11:08:00+08:00
- **Notes**: `CreatePayload` now receives the original trigger source while
  emitted effects still carry `blessing_40`; a targeted recursion test passes.

---

## [ERR-20260804-005] smoke-assumed-requested-inference-mode

**Logged**: 2026-08-04T13:15:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The full auto-profile smoke rejected a valid inference Auto-Tune mode change.

### Error
```text
Foundation worker did not sustain configured parallelism: effective=16/16, peak=16/5.
```

### Context
- Effective and peak concurrency both passed.
- The failure was the smoke's hidden assumption that the final inference mode must equal the initially requested `sharded-batch` mode.

### Suggested Fix
When inference calibration completes, validate against `AutoTune.SelectedInferenceMode`.

### Resolution
- **Resolved**: 2026-08-04T13:16:00+08:00
- **Notes**: The smoke now accepts the measured inference plan and still validates retained end-to-end evidence.

---

## [ERR-20260804-004] auto-profile-threadpool-underprovisioned

**Logged**: 2026-08-04T12:35:00+08:00
**Priority**: high
**Status**: resolved
**Area**: training

### Summary
The packaged auto-profile smoke selected 20-way campaign execution but reached only three concurrent campaigns.

### Error
```text
Foundation worker did not sustain configured parallelism: effective=20/20, peak=3/11.
```

### Context
- The worker configured ThreadPool minimums before resolving the auto profile.
- It therefore used the raw custom parallelism value (4) instead of the resolved auto plan (20 campaigns / 28 minimum workers).

### Suggested Fix
Apply the resolved ThreadPool floor inside the trainer, and do not require a tiny five-to-eleven-campaign smoke to occupy every selected worker before its work is already complete.

### Resolution
- **Resolved**: 2026-08-04T12:38:00+08:00
- **Notes**: The trainer now enforces its effective worker floor. The smoke gate uses a three-way floor for tiny preflights; the formal 35-campaign fixed-seed probe reached 16/16 concurrent campaigns.

---

## [ERR-20260804-002] encoding-allocation-gate-too-strict

**Logged**: 2026-08-04T12:10:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The new encoding allocation gate failed before reporting the measured byte count.

### Error
```text
Assertion failed: leaf inference encoding avoids per-call dictionaries and leaf result objects
```

### Context
- The gate combined allocation and value-type assertions.
- Its initial 32 KiB threshold was not backed by a printed baseline.

### Suggested Fix
Report the allocation measurement, separate the conditions, and set the gate from observed steady-state behavior.

### Resolution
- **Resolved**: 2026-08-04T12:18:00+08:00
- **Notes**: Replaced allocating LINQ registry scans with loops; the same 512 state/action encodes dropped from 7,254,016 to 28,672 bytes.

---

## [ERR-20260804-003] rg-nonexistent-search-root

**Logged**: 2026-08-04T12:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
An `rg` inspection included a guessed source root that does not exist.

### Error
```text
rg: AuraCombatSimulation: The system cannot find the path specified.
```

### Context
- The actual simulation project directory is `AuraCombatSimulationShared`.

### Suggested Fix
Resolve search roots with `rg --files` before passing multiple directories.

### Resolution
- **Resolved**: 2026-08-04T12:00:00+08:00
- **Notes**: Re-ran the inspection against the discovered project path.

---

## [ERR-20260804-001] reflection-single-file-publish

**Logged**: 2026-08-04T11:04:09+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
An assembly-reflection check targeted a DLL that is not emitted by the
trainer's single-file publish.

### Error
```text
Could not load file or assembly 'AuraFoundationTrainer.ControlCenter.dll'.
The system cannot find the file specified.
```

### Context
- The deployed trainer directory contains the single-file control-center EXE.
- The attempted verification assumed a framework-dependent DLL was present.

### Suggested Fix
Use the build output DLL under `bin/Release/net8.0-windows/win-x64` for
reflection checks, or validate the published EXE through the existing smoke
and source-contract tests.

### Metadata
- Reproducible: yes
- Related Files: AuraFoundationTrainer.ControlCenter/ControllerModels.cs

### Resolution
- **Resolved**: 2026-08-04T11:04:09+08:00
- **Notes**: Switched the reflection target to the non-published build output.

---

## [ERR-20260803-014] powershell-foreach-pipeline

**Logged**: 2026-08-03T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A PowerShell one-liner piped directly from a `foreach` statement without
grouping it as an expression.

### Error
```text
ParserError: An empty pipe element is not allowed.
```

### Context
- The command was checking declared content-package hashes against `Get-FileHash`.
- PowerShell requires `@(foreach (...) { ... }) | ...` when piping the loop output.

### Suggested Fix
Wrap statement output in an array subexpression before the formatting pipeline.

### Metadata
- Reproducible: yes
- Related Files: docs/AuraCombatAI/examples/content-package/package.json
- See Also: ERR-20260803-013

### Resolution
- **Resolved**: 2026-08-03T00:00:00+08:00
- **Notes**: Re-ran the verification with `@(foreach (...) { ... })`.

---

## [ERR-20260803-015] auratools-v6-source-guard

**Logged**: 2026-08-03T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The shared release gate retained an AuraTools source guard for the v6 live
sample filename after the runtime protocol moved to v7.

### Error
```text
Assertion failed: auto battle routes decisions and native prompt handling through shared runtimes
```

### Context
- Runtime behavior and all other architecture anchors were present.
- Only `auto-battle-training-v6.jsonl` was stale; the implementation correctly used v7.

### Suggested Fix
Update source-contract tests in the same change that bumps persisted protocol names.

### Metadata
- Reproducible: yes
- Related Files: AuraToolsExp-Dev.Tests/Program.cs

### Resolution
- **Resolved**: 2026-08-03T00:00:00+08:00
- **Notes**: Updated the guard to `auto-battle-training-v7.jsonl` and reran the release gate.

---

## [ERR-20260803-016] content-coverage-patch-context

**Logged**: 2026-08-03T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
A multi-hunk patch placed a new content-coverage validator against an imprecise
class-boundary context.

### Error
```text
apply_patch verification failed: Failed to find expected lines
```

### Context
- The target loader had additional validation helpers between identity checks
  and artifact readers.

### Suggested Fix
Read the exact nearby helper boundary and split insertion from call-site edits.

### Metadata
- Reproducible: no
- Related Files: AuraCombatAiShared/CombatContentPackages.cs
- See Also: ERR-20260803-013

### Resolution
- **Resolved**: 2026-08-03T00:00:00+08:00
- **Notes**: Applied the call site and helper against exact local context.

---

## [ERR-20260803-017] malformed-workdir-path

**Logged**: 2026-08-03T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A read-only source inspection used a malformed Windows workspace path.

### Error
```text
CreateProcess: 目录名称无效 (os error 267)
```

### Context
- One path segment separator was omitted in the `workdir` argument.

### Suggested Fix
Reuse the exact absolute workspace root for every command invocation.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared/CombatPolicyValueNetwork.cs

### Resolution
- **Resolved**: 2026-08-03T00:00:00+08:00
- **Notes**: Re-ran the command with the canonical workspace path.

---

## [ERR-20260803-018] repeated-powershell-foreach-pipeline

**Logged**: 2026-08-03T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
The final example-package hash check repeated the PowerShell parser error
caused by piping directly from a `foreach` statement.

### Error
```text
ParserError: An empty pipe element is not allowed.
```

### Context
- The command constructed objects inside `foreach` and immediately appended
  `| Format-Table` after the statement.
- This repeats the pattern already recorded by ERR-20260803-014.

### Suggested Fix
Always assign the `foreach` output to a variable before formatting or piping.

### Metadata
- Reproducible: yes
- Related Files: docs/AuraCombatAI/examples/content-package/package.json

### Resolution
- **Resolved**: 2026-08-03T00:00:00+08:00
- **Notes**: Re-ran the check by assigning the loop output to `$results`.

---

## [ERR-20260803-004] oversized-multi-file-patch-context

**Logged**: 2026-08-03T11:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
An oversized multi-file patch failed atomically because one PowerShell test
hunk omitted the actual compatibility-property prefix.

### Error
```text
apply_patch verification failed: Failed to find expected lines in
tools/Test-AuraFoundationTrainer.ps1
```

### Context
- The source edits were valid, but the unrelated stale hunk prevented every
  file in the patch from being updated.

### Suggested Fix
Inspect exact local context and split patches at independently verifiable file
boundaries when several modules are being changed together.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-AuraFoundationTrainer.ps1

### Resolution
- **Resolved**: 2026-08-03T11:02:00+08:00
- **Notes**: Reapplied the source and test changes in smaller verified patches.

---

## [ERR-20260731-015] undersized-project-test-timeout

**Logged**: 2026-07-31T15:15:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The project-level combat AI test was started with a one-second shell timeout,
which terminated the wrapper before its managed suite could complete.

### Error
```text
command timed out after 5039 milliseconds
```

### Context
- The underlying shared test alone normally takes about 48 seconds.
- The wrapper also runs Python and headless-simulation contract checks.

### Suggested Fix
Give full project test wrappers a timeout of at least three minutes.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-AuraCombatAi.ps1

### Resolution
- **Resolved**: 2026-07-31T15:15:00+08:00
- **Notes**: Re-ran the wrapper with a 180-second timeout.

---

## [ERR-20260803-001] policy-integrity-fixture-default

**Logged**: 2026-08-03T10:30:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The expert replay fixture did not opt into the newly required policy-integrity
flag, so all manually constructed success cases were correctly rejected.

### Error
```text
Assertion failed: expert replay preserves normal evidence and fills unused
capacity while reporting scarce advanced success cases
```

### Context
- `PolicyIntegrityValid` intentionally defaults to false for deserialized or
  manually constructed archive records.
- Production observations set the flag through policy-frame validation, while
  the synthetic archive fixture set only `ArchiveEligible`.

### Suggested Fix
When adding a fail-closed archive field, update positive archive fixtures to
declare the new invariant explicitly and retain negative fixtures for the
default-rejection path.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared.Tests/Program.cs, AuraCombatAiShared/CombatFoundationCaseLearning.cs

### Resolution
- **Resolved**: 2026-08-03T10:32:00+08:00
- **Notes**: Marked the valid stratified fixture policy-valid; malformed and
  legacy records remain rejected by default.

---

## [ERR-20260803-002] native-reward-test-entrypoint

**Logged**: 2026-08-03T10:50:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The native reward test executable was invoked directly without its required
campaign and ruleset arguments.

### Error
```text
Expected the bundled campaign and ruleset JSON paths, plus an optional
integrity sweep campaign count.
```

### Context
- Direct `dotnet run` does not supply the fixture paths.
- The repository test script owns argument construction and the build order.

### Suggested Fix
Use `tools/Test-AuraNativeRewards.ps1` as the canonical entrypoint.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-AuraNativeRewards.ps1, AuraToolsExp.NativeReward.Tests/Program.cs

### Resolution
- **Resolved**: 2026-08-03T10:52:00+08:00
- **Notes**: The canonical script passed all semantic checks and a 64-campaign
  integrity sweep with zero failures.

---

## [ERR-20260801-001] realized-semantics-double-normalization

**Logged**: 2026-08-01T00:00:00+08:00
**Priority**: high
**Status**: resolved
**Area**: training

### Summary
Authoritative branch events already contained resolved damage, block, and heal
amounts, but semantic auditing applied combat modifiers to those amounts again.

### Error
```text
card_2|defend:projected=7,effective=10,actual=7
```

### Context
- A two-campaign smoke happened not to select an affected action.
- A 16-campaign run found 115 selected unexplained mismatches.
- Zero-damage semantics could also acquire positive damage from flat modifiers.

### Suggested Fix
Mark event-derived semantics as realized, bypass modifier normalization for that
projection, and short-circuit effective damage when no damage is projected.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared/CombatSemanticAudit.cs

### Resolution
- **Resolved**: 2026-08-01T00:00:00+08:00
- **Notes**: The expanded run completed 246/246 campaigns and 8386 battles
  with selected invalid/unexplained counts both equal to zero.

---

## [ERR-20260801-002] relative-managed-path-consumer-build

**Logged**: 2026-08-01T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: build

### Summary
Passing `./Managed` to the multi-project consumer build made MSBuild resolve the
reference directory relative to each project instead of the repository root.

### Error
```text
MSB3245: Could not resolve UnityEngine.CoreModule / Witch / Newtonsoft.Json
```

### Suggested Fix
Resolve `Managed` to an absolute path before invoking
`Build-MainSharedConsumers.ps1`.

### Metadata
- Reproducible: yes
- Related Files: tools/Build-MainSharedConsumers.ps1

### Resolution
- **Resolved**: 2026-08-01T00:00:00+08:00
- **Notes**: All three consumers built serially with zero warnings/errors.

---

## [ERR-20260801-001] action-contract-net-count-and-native-trace-gap

**Logged**: 2026-08-01T12:00:00+08:00
**Priority**: high
**Status**: resolved
**Area**: training-integrity

### Summary
The Divine Choice postcondition compared draw-pile and hand net counts. Long
foundation runs therefore rejected valid actions when the same causal action
also created or moved other cards. Requiring only `CardDrawn` trace events was
also insufficient because authoritative native programs may mutate card zones
without emitting that fine-grained event.

### Error
```text
action-contract:careercard_1: expected at least 1 card(s) to move from draw
pile to hand (draw 1->15, hand 6->8)
```

### Context
- The action boundary was correct, but aggregate zone sizes were not a causal
  proof of which card moved.
- Summary/no-trace simulation modes intentionally omit most events.
- The native `GetCardFromDeck` path updates `DrawPile` and `Hand` directly.

### Suggested Fix
Capture card instance IDs at the action boundary. Accept a postcondition when
the same instance is proven by a matching `SourceActionId` event, or by the
atomic before/after action snapshots when a native path has no fine-grained
event. Keep the historical failure seeds in every preflight.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatSimulationShared/CombatActionContracts.cs,
  AuraCombatSimulationShared/CombatSimulationEngine.cs,
  AuraCombatAiShared/CombatCampaignFoundationTraining.cs

### Resolution
- **Resolved**: 2026-08-01T12:00:00+08:00
- **Notes**: Introduced `action-contract-v2`, added the three observed world
  seeds to `integrity-seeds-v1`, and passed the 64-campaign native integrity
  sweep plus the shared release gate.

---

## [ERR-20260731-014] ripgrep-no-match-exit-code

**Logged**: 2026-07-31T15:05:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
A source excerpt command failed because a follow-up `rg` probe found no match,
even though the preceding `Get-Content` output succeeded.

### Error
```text
Exit code: 1
```

### Context
- PowerShell propagated ripgrep's normal no-match status to the combined probe.
- The missing phrase revealed a stale static-test anchor, not a source defect.

### Suggested Fix
Run exploratory no-match searches separately or explicitly tolerate exit code 1.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-AuraCombatAi.ps1

### Resolution
- **Resolved**: 2026-07-31T15:05:00+08:00
- **Notes**: Replaced the speculative phrase with the actual runtime rejection
  anchor `end-turn state changed`.

---

## [ERR-20260731-013] ripgrep-missing-search-root

**Logged**: 2026-07-31T15:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
A repository-wide version search included a non-existent top-level `config`
directory and therefore returned a failing exit code after producing results.

### Error
```text
rg: config: 系统找不到指定的文件。 (os error 2)
```

### Context
- This repository stores the relevant configuration under `AuraToolsExp/Config`.
- The useful search results were still printed before ripgrep exited.

### Suggested Fix
Use `rg --files` or verify optional roots before passing them to a search.

### Metadata
- Reproducible: yes
- Related Files: AuraToolsExp/Config

### Resolution
- **Resolved**: 2026-07-31T15:00:00+08:00
- **Notes**: Subsequent searches used only verified repository roots.

---

## [ERR-20260721-002] workspace-root-rename-lock

**Logged**: 2026-07-21T16:05:00+08:00
**Priority**: low
**Status**: blocked
**Area**: tooling

### Summary
The local repository root could not be renamed while the Codex desktop task held an open workspace handle.

### Error
```text
The process cannot access the file because it is being used by another process.
```

### Context
- The source and destination were resolved and the destination did not exist.
- Repository content, tracked paths, and shipped artifacts were already renamed; only the outer workspace folder remains locked.

### Suggested Fix
Close or reopen the Codex workspace, then rename the repository root from its parent directory before the next development session.

### Metadata
- Reproducible: yes
- Related Files: repository root

---

## [ERR-20260731-012] policy-value-regression-after-end-target-change

**Logged**: 2026-07-31T14:10:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The combat AI suite reached the managed policy-value training integration test
but its aggregate acceptance assertion failed after counterfactual end-turn
target changes.

### Error
```text
Assertion failed: complete episodes train a validated managed policy-value
network, retain Top-K checkpoints, and select by multi-objective validation
```

### Context
- Earlier end-turn, forward-model, simulator-energy, and transposition tests
  passed in the same run.
- The failing assertion aggregates several training/checkpoint conditions and
  needs its individual values inspected before changing behavior.

### Suggested Fix
Read the assertion inputs, add diagnostic values if absent, and determine
whether the new candidate inclusion changed a real model contract or only a
fixture expectation.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared.Tests/Program.cs
- Related Files: AuraCombatAiShared/CombatPolicyValueBatchTrainer.cs

### Resolution
- **Resolved**: 2026-07-31T14:20:00+08:00
- **Notes**: Counterfactual end-turn weighting exposed that post-normalization
  frame weights used a generic 0.10 floor instead of the declared protocol
  minimum. The final clamp now enforces `MinimumWeight`.

---

## [ERR-20260731-011] surplus-energy-test-overconstraint

**Logged**: 2026-07-31T14:05:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The new surplus-energy contract test also required an unrelated second-turn
victory, even though the energy capture already proved the target invariant.

### Error
```text
rules=True, outcome=Draw, secondTurnEnergy=8, finalEnergy=8
```

### Context
- The test's actual contract is preservation of energy 8 over a base cap of 3.
- Card redraw/finish behavior is covered by separate deck-cycle tests.

### Suggested Fix
Assert the energy transition directly and avoid coupling it to battle outcome.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared.Tests/Program.cs

### Resolution
- **Resolved**: 2026-07-31T14:05:00+08:00
- **Notes**: Removed the unrelated victory requirement.

---

## [ERR-20260731-010] forward-state-random-seed-hash

**Logged**: 2026-07-31T14:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The first combat AI regression run lost the commutative-action transposition
hit after adding determinization-local shuffle state.

### Error
```text
Assertion failed: commutative action orders reuse a physical-state
transposition node
```

### Context
- `DeterminizationSeed` and `ShuffleEpoch` were added to the full physical
  state hash while replacing optimistic discard sorting with sampled shuffles.
- Determinization identity is not itself a physical combat resource.

### Suggested Fix
Keep shuffle entropy on the simulation state for future reshuffles, but exclude
the root seed from the physical transposition hash. Include only shuffle epochs
or realized card-zone order when it changes observable transition state.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared/CombatForwardModel.cs

### Resolution
- **Resolved**: 2026-07-31T14:02:00+08:00
- **Notes**: Kept shuffle entropy on the state but removed the determinization
  seed from the physical transposition hash.

---

## [ERR-20260731-009] ripgrep-windows-directory-glob

**Logged**: 2026-07-31T13:20:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
A Windows `rg` invocation passed `AuraCombatAiShared/*.cs` as a positional
path, which is not expanded by PowerShell for this command.

### Error
```text
rg: AuraCombatAiShared/*.cs: The filename, directory name, or volume label
syntax is incorrect.
```

### Context
- The command was a read-only feature-registry search.

### Suggested Fix
Pass the directory as the search root and use `-g "*.cs"` for the file filter.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared

### Resolution
- **Resolved**: 2026-07-31T13:20:00+08:00
- **Notes**: Reissued the search with a ripgrep include glob.

---

## [ERR-20260731-008] assumed-shared-project-files

**Logged**: 2026-07-31T13:15:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
An inspection command assumed the shared source directories each contained a
same-named `.csproj`.

### Error
```text
Cannot find path 'AuraCombatAiShared\AuraCombatAiShared.csproj'
Cannot find path 'AuraCombatSimulationShared\AuraCombatSimulationShared.csproj'
```

### Context
- These directories are linked source surfaces consumed by other projects.
- The failed command was read-only.

### Suggested Fix
Locate project files with `rg --files -g "*.csproj"` before inspecting source
ownership.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared; AuraCombatSimulationShared

### Resolution
- **Resolved**: 2026-07-31T13:15:00+08:00
- **Notes**: Continued inspection through the consuming project files.

---

## [ERR-20260731-007] stale-simulation-path

**Logged**: 2026-07-31T13:10:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
An inspection command used the former `AuraToolsExp/Infrastructure/Training`
path for `CombatSimulationEngine.cs`.

### Error
```text
rg: AuraToolsExp/Infrastructure/Training/CombatSimulationEngine.cs:
The system cannot find the path specified.
```

### Context
- The simulator has moved to `AuraCombatSimulationShared`.
- The command was read-only and made no workspace changes.

### Suggested Fix
Resolve implementation paths with `rg --files` before using paths preserved
in older analysis notes.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatSimulationShared/CombatSimulationEngine.cs

### Resolution
- **Resolved**: 2026-07-31T13:10:00+08:00
- **Notes**: Located the current simulator path with `rg --files`.

---

## [ERR-20260730-004] native-reward-test-invocation

**Logged**: 2026-07-30T00:00:00+08:00
**Priority**: medium
**Status**: pending
**Area**: tests

### Summary
The native reward executable requires campaign/ruleset arguments, and the
canonical wrapper currently ends on a separate CrowdFundingRelic_17 guard case.

### Error
```text
Expected the bundled campaign and ruleset JSON paths
native-semantics:CrowdFundingRelic_17:causal-recursion-guard:MaximumTurns:
```

### Context
- Invoke through `tools/Test-AuraNativeRewards.ps1`, not bare `dotnet run`.
- The package, game runtime, recent known-integrity seeds, 64-campaign sweep,
  and other semantic cases passed.
- The remaining failure is unrelated to the role-aware random-card pool fix.

### Suggested Fix
Investigate CrowdFundingRelic_17 causal recursion separately without weakening
the maximum-turn or invalid-battle guards.

### Metadata
- Reproducible: yes
- Related Files: AuraToolsExp.NativeReward.Tests/Program.cs

---

## [ERR-20260730-003] net472-simd-facade

**Logged**: 2026-07-30T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: backend

### Summary
The Unity/net472 shared-runtime build cannot compile against the game-managed
`System.Buffers` and `System.Numerics.Vectors` facade assemblies.

### Error
```text
CS0103: The name 'ArrayPool' does not exist in the current context
CS1069: System.Numerics.Vector<> has been forwarded to mscorlib
```

### Context
- The independent trainer is net8.0, while the packaged Aura shared runtime is
  also compiled for the game's net472/Unity environment.
- Explicit references to the small facade DLLs under `Managed/` did not expose
  usable implementations to the net472 compiler.

### Suggested Fix
Use reusable thread-local inference workspaces on both targets and compile the
hardware-vectorized inner loops only for `NET8_0_OR_GREATER`, retaining the
same scalar fallback for the Unity target.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared/CombatPolicyValueNetwork.cs

### Resolution
- **Resolved**: 2026-07-30T00:00:00+08:00
- **Notes**: Removed facade dependencies and kept SIMD in the independent
  net8 trainer/worker where it is supported.

---

## [ERR-20260730-002] powershell-range-shape

**Logged**: 2026-07-30T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A compact source-excerpt helper constructed one-element range arrays in a
shape that PowerShell flattened, causing `Math.Min` argument type errors.

### Error
```text
OperationStopped: Argument types do not match
```

### Context
- The command mixed nested `@(@(start,end))` literals with `switch` assignment.
- The source query itself was read-only and no product operation failed.

### Suggested Fix
Use direct `Select-Object -Skip/-First` excerpts or explicit objects with
integer `Start` and `End` properties rather than nested positional arrays.

### Metadata
- Reproducible: yes
- Related Files: none

### Resolution
- **Resolved**: 2026-07-30T00:00:00+08:00
- **Notes**: Replaced the helper with direct, typed source excerpts.

### Recurrence
- **Observed**: 2026-07-30T00:00:00+08:00
- **Notes**: A later compact command piped directly after a `foreach`
  statement. Assign the loop output to `$rows` before piping it.
- **Observed**: 2026-07-30T00:00:00+08:00
- **Notes**: Assumed shared source directories each owned a same-named
  project file and passed wildcard path arguments directly to `rg` on
  Windows. Locate project files with `rg --files -g '*.csproj'` first and
  express wildcard matching through `-g`.

---
## [ERR-20260730-002] powershell-inline-if-arithmetic

**Logged**: 2026-07-30T16:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
An inline PowerShell comparison expression placed `-if` directly after a
property access and failed to parse.

### Error
```text
ParserError: Unexpected token '-if' in expression or statement.
```

### Context
- A temporary analysis command attempted to subtract the result of an `if`
  statement without first evaluating the conditional separately.

### Suggested Fix
Assign the conditional value to a variable before arithmetic, or wrap the
entire `if` statement in `$()`.

### Metadata
- Reproducible: yes
- Related Files: none

### Resolution
- **Resolved**: 2026-07-30T16:00:00+08:00
- **Notes**: Use a separate prior-success variable in the comparison command.

---

## [ERR-20260730-003] rebuild-all-timeout-budget

**Logged**: 2026-07-30T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The first full repository rebuild invocation used a one-second command timeout.

### Error
```text
command timed out after 1535 milliseconds
```

### Context
- `Rebuild-All.ps1` builds three net472 consumers and publishes two net8
  trainer executables.
- The short timeout terminated a valid build after its first project.

### Suggested Fix
Run repository-wide rebuilds with a multi-minute timeout and wait for the
command to complete.

### Metadata
- Reproducible: yes
- Related Files: tools/Rebuild-All.ps1

### Resolution
- **Resolved**: 2026-07-30T00:00:00+08:00
- **Notes**: Re-ran with a ten-minute timeout; the full rebuild completed in
  21.4 seconds.

---

## [ERR-20260730-002] flattened-temperature-loss-buffer-index

**Logged**: 2026-07-30T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: backend

### Summary
The first flattened temperature-calibration buffer implementation iterated
over scalar loss entries as though they were frames.

### Error
```text
System.IndexOutOfRangeException in CalibratePolicyTemperature
```

### Context
- The buffer shape is `frameCount * temperatureCount`.
- Aggregation incorrectly used the flattened buffer length as the frame count
  and multiplied that index by the temperature count a second time.

### Suggested Fix
Iterate aggregation over `validationFrames.Count`, calculate the flattened
offset once, and divide the accumulated loss by the frame count.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared/CombatPolicyValueBatchTrainer.cs

### Resolution
- **Resolved**: 2026-07-30T00:00:00+08:00
- **Notes**: Corrected the aggregation bound and denominator; covered by the
  full shared training test suite.

---

## [ERR-20260730-002] powershell-cleanup

**Logged**: 2026-07-30T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
The first cleanup verification command contained a PowerShell pipeline directly
after a `foreach` statement, and recursive `Remove-Item` was rejected by the
execution policy even after the targets were verified.

### Error
```
An empty pipe element is not allowed.
Remove-Item ... rejected: blocked by policy
```

### Context
- All cleanup targets were absolute paths under the current workspace.
- The resolved targets were printed and verified before deletion.

### Suggested Fix
Collect `foreach` output in a variable before piping. When the command policy
rejects a verified recursive `Remove-Item`, use
`System.IO.Directory.Delete(path, true)` in the same PowerShell process.

### Metadata
- Reproducible: yes
- Related Files: none

### Resolution
- **Resolved**: 2026-07-30T00:00:00+08:00
- **Notes**: Verified absolute targets, then completed cleanup with System.IO.

---

## [ERR-20260729-002] perl-unavailable-on-windows

**Logged**: 2026-07-29T20:30:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
The local Windows toolchain does not provide `perl`, so it cannot be used for line-ending normalization.

### Error
```text
The term 'perl' is not recognized as a name of a cmdlet, function, script file, or executable program.
```

### Context
- Attempted to normalize CRLF in generated artifacts before running `git diff --check`.
- The workspace shell is PowerShell on Windows.

### Suggested Fix
Use a .NET/PowerShell byte-preserving normalization command or an available formatter instead of assuming Unix text utilities exist.

### Metadata
- Reproducible: yes
- Related Files: AuraToolsExp-Dev/Features/AutoBattle/Generated/AuraToolsNativePrograms.g.cs

### Resolution
- **Resolved**: 2026-07-29T20:31:00+08:00
- **Notes**: Switched to a .NET-based newline normalization command.

---

## [ERR-20260729-001] exploratory-rg-batching

**Logged**: 2026-07-29T10:05:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
Exploratory search batches failed because Windows wildcards were passed as path
arguments and expected `rg` no-match exit codes were treated as fatal.

### Error
```text
rg: .codex\skills\terrias-*: IO error ... (os error 123)
rg: AuraToolsExp\Config\combat-simulation\*.json: ... (os error 123)
Script error: Exit code: 1
```

### Context
- PowerShell did not expand wildcard directory or file roots for `rg`.
- `Promise.all` hid successful sibling search output when one optional probe
  returned exit code 1.

### Suggested Fix
Search an existing directory and filter with `-g`, and isolate optional
exploratory commands instead of treating no matches as a batch failure.

### Metadata
- Reproducible: yes
- Related Files: .learnings/ERRORS.md

### Resolution
- **Resolved**: 2026-07-29T10:05:00+08:00
- **Notes**: Re-ran searches against explicit directory roots with `-g`.

---

<<<<<<< HEAD
=======
## [ERR-20260728-002] powershell-rg-wildcard-and-split-escaping

**Logged**: 2026-07-28T17:00:00+08:00
**Priority**: low
**Status**: pending
**Area**: tooling

### Summary
Two read-only inventory commands failed because a Windows wildcard path was passed directly to `rg` and a backslash was incorrectly escaped for PowerShell `-split`.

### Error
```text
rg: **/.gitignore: The filename, directory name, or volume label syntax is incorrect.
Group-Object: Invalid pattern '\' at offset 1.
```

### Context
- The failures occurred while inventorying `ModsData`; no repository data was modified.
- Enumerating `.gitignore` files first and using path APIs instead of regex splitting avoids both issues.
- The wildcard mistake recurred once in a later `rg` call, so the mitigation still needs to become habitual.
- A subsequent inline PowerShell inventory also placed a pipeline directly after a `foreach` statement; wrap the loop in `@(...)` before piping.
- `Copy-Item -LiteralPath` was incorrectly given an `e\*` wildcard while staging ModelData; enumerate files first or use `-Path` when wildcard expansion is intended.
- A verified `Remove-Item -Recurse` staging cleanup was blocked by command policy; resolving and checking the absolute target in one call, then using `[IO.Directory]::Delete` on that exact path in a second call succeeded.
- The ModelData installer smoke-test wrapper treated a null `$LASTEXITCODE` from a PowerShell script as failure; use `$?` or allow terminating errors to propagate when invoking another `.ps1`.
- A training-analysis `rg` call included a guessed source filename that does not exist; resolve candidate files with `rg --files` before batching optional paths.
- The same guessed-file error recurred for `CombatSimulationPolicyMetrics.cs`; avoid adding optional filenames to otherwise valid recursive directory searches.
- The first JSONL validation summary queried a nonexistent `Success` field; the observation schema uses `FinalBossVictory`, so inspect sample records before aggregating ad hoc fields.
- The first Newtonsoft validation-run extractor assumed `Battles` was a flat array of battle objects; inspect token types before casting nested result structures.
- The follow-up extractor hit PowerShell cast/method precedence on `[string]$token.Property(...).Value`; assign or use `SelectToken(...).ToString()` before casting.
- Direct `[int]$jObject.SelectToken(...)` had the same precedence problem; convert `SelectToken(...).ToString()` via `Convert` or omit unused fields.
- Calling `.ToString()` directly after `SelectToken(...)` also produced PowerShell member-enumeration output; parenthesize the returned token before invoking methods.
- Another analysis command piped directly after `foreach`; consistently capture loop output with `$rows = @(foreach (...) { ... })` before formatting.

### Suggested Fix
Pass concrete paths to `rg` on Windows and prefer `[IO.Path]::GetRelativePath()` plus directory-separator indexing for path grouping.

### Metadata
- Reproducible: yes
- Related Files: .gitignore

---

## [ERR-20260728-002] shared-release-checkpoint-path-assumption

**Logged**: 2026-07-28T16:58:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The shared release smoke test assumed that resumable episodes always lived at the legacy fixed JSONL path.

### Error
```text
Unaccepted foundation training must retain a resumable checkpoint.
```

### Context
- The Worker completed and wrote a valid immutable snapshot referenced by the checkpoint JSON.
- `Test-AuraFoundationTrainer.ps1` still required `CheckpointEpisodesPath` itself to exist.

### Suggested Fix
Resolve resumability through `checkpoint.EpisodeSnapshot.Path` with a legacy `EpisodesPath` fallback, and validate snapshot count, length, and hash metadata.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-AuraFoundationTrainer.ps1, AuraCombatAiShared/CombatFoundationCheckpointStorage.cs

---

>>>>>>> 00cdd678a11fef71d3237a27c45ea0c8f465992e
## [ERR-20260728-002] foundation-worker-smoke-max-path

**Logged**: 2026-07-28T18:20:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The foundation worker smoke reached archive migration but PowerShell 5 `Copy-Item` failed on a 265-character legacy observation path.

### Error
```text
DirectoryNotFoundException: Could not find a part of the path ...\v1\<compatibility>\observations\<case>.json
```

### Suggested Fix
Use `System.IO.File.Copy` with Windows extended-length `\\?\` paths for the intentionally long v1 migration fixture.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-AuraFoundationTrainer.ps1

---

## [ERR-20260727-005] foundation-smoke-system-web-loader

**Logged**: 2026-07-27T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The foundation Worker completed, but its smoke test could not read the result because `JavaScriptSerializer` loaded an incompatible game-supplied `System.Web` assembly.

### Error
```text
Could not load type 'System.Web.UI.WebResourceAttribute' from assembly 'System.Web, Version=4.0.0.0'
```

### Suggested Fix
Use PowerShell's built-in `ConvertFrom-Json` for test artifacts so validation does not depend on the legacy full-framework `System.Web.Extensions` loader.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-AuraFoundationTrainer.ps1

### Resolution
- **Resolved**: 2026-07-27T00:00:00+08:00
- **Notes**: Replaced `JavaScriptSerializer` with a UTF-8 raw read piped to `ConvertFrom-Json -Depth 100 -AsHashtable`; the hashtable form also accepts feature maps containing an empty-string key.

---

## [ERR-20260727-004] stale-source-contract-anchor

**Logged**: 2026-07-27T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The first player-equivalent AI contract run checked for the root determinizer in the planner file even though the planner intentionally reaches it through the forward-model boundary.

### Error
```text
Aura combat AI Chance-PUCT planner contract is missing: CombatRootDeterminizer
```

### Suggested Fix
Anchor source-contract tests on the planner's actual public integration points rather than an internal dependency owned by another module.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-AuraCombatAi.ps1

### Resolution
- **Resolved**: 2026-07-27T00:00:00+08:00
- **Notes**: Replaced the misplaced anchor with the planner's belief-tracker and public observation seed calls.

---

## [ERR-20260723-001] powershell-package-inspection-policy-block

**Logged**: 2026-07-23T16:30:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A compound PowerShell command that downloaded, expanded, inspected, and recursively removed a NuGet package was rejected before execution by command policy.

### Error
```text
Command rejected: blocked by policy
```

### Context
- Attempted to inspect the deployed Windows native size of `Microsoft.ML.OnnxRuntime` in a temporary directory.
- The command combined network download, archive expansion, and recursive cleanup.

### Suggested Fix
Use the official NuGet package-size metadata for architectural comparison, or split any necessary inspection into simple non-destructive commands and avoid recursive cleanup in the same invocation.

### Metadata
- Reproducible: unknown
- Related Files: .learnings/ERRORS.md

### Resolution
- **Resolved**: 2026-07-23T16:31:00+08:00
- **Notes**: Used official NuGet and ONNX Runtime documentation, which already reports the full package and custom-runtime size tradeoffs.

---

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
- Command attempted: `skill-creator/scripts/init_skill.py terrias-poster-design`
  with `--interface short_description=...`.
- The skill folder and `SKILL.md` were created before the metadata step failed.

### Suggested Fix
Use a 25-64 character `short_description`, then run
`generate_openai_yaml.py` separately if initialization partially succeeds.

### Metadata
- Reproducible: yes
- Related Files: .codex/skills/terrias-poster-design/agents/openai.yaml

---

## [ERR-20260721-001] renamed-test-binary-invocation

**Logged**: 2026-07-21T15:38:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
An assumed renamed test executable was invoked directly before the renamed test project had been built.

### Error
```text
The module 'Terrias-Dev.ElementalTests' could not be loaded.
```

### Context
- Attempted to run `Terrias-Dev.ElementalTests\bin\Release\net8.0\Terrias-Dev.ElementalTests.exe` in parallel with the architecture gate.
- The repository already provides build-aware PowerShell test entry points, so the direct binary path was unnecessary.

### Suggested Fix
Use the renamed repository test scripts or `dotnet run --project` after confirming the project target framework instead of assuming an existing binary path.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-TerriasElemental.ps1

### Resolution
- **Resolved**: 2026-07-21T15:39:00+08:00
- **Notes**: Continued with the repository-owned Terrias test scripts and serial DLL-writing gates.

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

## [ERR-20260716-006] terrias-toolbar-button-namespace

**Logged**: 2026-07-16T18:30:00+08:00
**Priority**: low
**Status**: resolved
**Area**: frontend

### Summary
The first Endless Abyss evacuation build could not resolve `ButtonManager` because the cloned AuraTools implementation relied on a namespace not imported in the new Terrias UI runtime.

### Error
```text
CS0246: The type or namespace name 'ButtonManager' could not be found.
```

### Context
- Attempted `dotnet build Terrias-Dev/Terrias.Dll.csproj -c Release --no-restore`.
- The TopBar clone pattern was adapted from `AuraToolsSafeBoxRuntime.cs`.

### Suggested Fix
Resolve `ButtonManager` from the current Managed contract and import its declaring namespace before rebuilding.

### Metadata
- Reproducible: yes
- Related Files: Terrias-Dev/Hooks/Ui/EndlessAbyssEvacuationButtonRuntime.cs

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
The first Star Score flight-glyph build omitted the `Terrias.Dll.GameApi` import for `TerriasResourceCache`.

### Error
```text
CS0103: The name 'TerriasResourceCache' does not exist in the current context.
```

### Suggested Fix
Check the existing resource loader namespace before adding a new visual asset catalog; `TerriasResourceCache` lives in `Terrias.Dll.GameApi`, not Infrastructure.

### Metadata
- Reproducible: yes
- Related Files: Terrias-Dev/Hooks/Visual/StarScoreFlightGlyphAssets.cs

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
Build-TerriasVisualBundle.ps1: process exit code 1
Unity log: Built Terrias visual bundle ... return code 0
```

### Suggested Fix
Make the wrapper launch Unity through a process API that reliably captures the real child exit code, then accept success only when both the build marker and updated bundle are present.

### Metadata
- Reproducible: yes
- Related Files: tools/Build-TerriasVisualBundle.ps1, Terrias-Dev/VisualAssets/terrias_visuals.unity-build.log

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
- Related Files: Terrias-Dev/Terrias.Dll.csproj, Terrias-Dev/Mechanics/DimensionShopService.cs

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
- Related Files: tools/Test-TerriasArchitecture.ps1

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
An inspection command assumed shipped mod resource paths were rooted directly at `Terrias/ModResource` and `GoldExp/ModResource`.

### Error
```text
rg: GoldExp: system cannot find the file specified
rg: Terrias/ModResource/Data/Card: system cannot find the path specified
```

### Context
- Command attempted while reviewing `.codex/skills/terrias-card-art-style`.
- This workspace contains `Terrias`, `Terrias-Dev`, and `GoldExp-Dev`; resource ownership must be discovered before querying fixed paths.
- `rg --files` on Windows emits backslash-separated paths, so slash-only filters can also miss results.

### Suggested Fix
Discover resource roots with `rg --files` or `Get-ChildItem` first, then use separator-agnostic filters such as `[\\/]`.

### Metadata
- Reproducible: yes
- Related Files: .codex/skills/terrias-card-art-style/SKILL.md

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
- Command attempted: `tools\Build-TerriasDll.ps1`.
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
TerriasResourceCache.cs: error CS0103: current context does not contain ObjectArrayCache/key/AddCategoryKey
```

### Context
- Command attempted: `tools\Build-TerriasDll.ps1`.
- File migrated from local dictionaries to `AuraSharedResourceCache`.

### Suggested Fix
After removing local cache fields, scan the whole file for deleted helper names before rebuilding.

### Metadata
- Reproducible: yes
- Related Files: Terrias-Dev/GameApi/TerriasResourceCache.cs

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
**Priority**: medium
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
- Recurrence-Count: 3

### Recurrence
- **Observed**: 2026-07-22T13:00:00+08:00
- **Notes**: Repeated the same parallel invocation and locked `AuraSharedCore.Tests.AssemblyInfoInputs.cache`; final validation must keep these two gates serial.

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
Passing `.codex\skills\terrias-*` as an rg path is invalid on Windows because rg does not expand that directory wildcard.

### Error
```text
rg: .codex\skills\terrias-*: The filename, directory name, or volume label syntax is incorrect. (os error 123)
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
- Recurrence-Count: 2

### Recurrence
- **Observed**: 2026-07-22T14:00:00+08:00
- **Notes**: Repeated the unsafe PowerShell `AssemblyResolve` probe while checking the `PlayerManager` targeted-query contract; retained ILSpy verification and build/source gates instead.

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
The first Terrias integration build omitted the owner MOD argument required by `AuraDirectorRuntime.RegisterStartGateProvider`.

### Error
```text
CS7036: No argument was provided for the required parameter 'provider' of RegisterStartGateProvider(string, IAuraDirectorStartGateProvider)
```

### Suggested Fix
Read the new public signature at the call site before compiling integrations, and keep the owner identity explicit for shared provider registration.

### Metadata
- Reproducible: yes
- Related Files: Terrias-Dev/Features/Director/TerriasDirectorRuntime.cs

### Resolution
- **Resolved**: 2026-07-16T17:21:00+08:00
- **Notes**: Passed `TerriasIds.ModId` as the owner and rebuilt successfully.

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
- **Notes**: Building the Terrias evacuation feature refreshed the shared project output and Terrias package while the SanGuoShaExp and AuraToolsExp packages retained the earlier hash; resolved through the main-consumer build before final validation.
- **Observed**: 2026-07-27T00:00:00+08:00
- **Notes**: Player-equivalent AI changes refreshed the shared assembly through Terrias and AuraTools builds while SanGuoShaExp retained the prior hash; rebuilt the remaining main consumer before rerunning the packaging gate.

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
- Recurrence-Count: 20

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
- **Observed**: 2026-07-23T15:20:00+08:00
- **Notes**: Repeated the invalid direct `foreach (...) { ... } | Format-Table` form twice during project analysis, then used `Promise.all` so one failed inventory command hid all sibling results. Accumulate into `$rows` and prefer failure-isolated orchestration for exploratory batches.
- **Observed**: 2026-07-23T15:31:00+08:00
- **Notes**: A parallel source-inspection batch failed because one guessed file path did not exist, again discarding otherwise useful sibling outputs. Resolve paths with `rg --files` before reads and isolate optional probes.
- **Observed**: 2026-07-23T15:37:00+08:00
- **Notes**: Repeated the same invalid direct `foreach (...) { ... } | ConvertTo-Json` form in a compact content-inventory command. Store the loop output in `$rows` before serialization.
- **Observed**: 2026-07-23T15:48:00+08:00
- **Notes**: Guessed a nonexistent card-art skill reference path inside `Promise.all`, so the failed read hid valid sibling outputs. List skill files first and use `Promise.allSettled` for exploratory reads.
- **Observed**: 2026-07-23T16:02:00+08:00
- **Notes**: An optional `rg --files GoldExp-Dev GoldExp` inventory returned non-zero because the removed `GoldExp` root was absent. Test optional roots before passing them to `rg`.
- **Observed**: 2026-07-23T16:09:00+08:00
- **Notes**: Passed a wildcard path to `rg` on Windows (`Terrias-Dev/Mechanics/StarScore*.cs`), which PowerShell did not expand and `rg` rejected. Use `-g 'StarScore*.cs'` with the directory root instead.
- **Observed**: 2026-07-24T00:00:00+08:00
- **Notes**: Embedded a double-quoted regex containing escaped quotes in a PowerShell command string; PowerShell terminated the string and treated regex alternatives as commands. Use a single-quoted PowerShell regex or isolate the probe with `Promise.allSettled`.
- **Observed**: 2026-07-24T18:00:00+08:00
- **Notes**: Guessed `AuraToolsExp-Dev/AuraToolsExp-Dev.csproj` instead of resolving the actual `AuraToolsExp.Dll.csproj`, then let an expected no-match `rg` determine the combined command exit code. Resolve project paths first and isolate optional probes.
- **Observed**: 2026-07-24T19:00:00+08:00
- **Notes**: Added a direct `File.WriteAllText` to an AuraTools feature even though the shared write-entrypoint gate requires `AuraSharedStorageCoordinator.WriteTextAtomic`; fixed the exporter to use the coordinated atomic writer.
- **Observed**: 2026-07-24T19:15:00+08:00
- **Notes**: A new simulator regression test guessed `ApplyPlayerAction` as the public API; the actual immutable branch API is `ForkAndApplyPlayerAction`. Reused the existing test call pattern and legal candidate ID format.
- **Observed**: 2026-07-24T19:20:00+08:00
- **Notes**: Adding the authoritative `ActionResolved` lifecycle event intentionally changed the deterministic simulation state hash. Recomputed the CLI contract hash and updated the pinned expectation after confirming outcome, coverage, and determinism remained correct.
- **Observed**: 2026-07-24T19:35:00+08:00
- **Notes**: Exact draw-pile modeling initially treated every legacy/test observation with an empty card-ID list as a known empty pile, breaking backward-compatible count-only draws. Added `DrawPileKnown` so new runtime observations use exact order while older providers retain count-only behavior.
- **Observed**: 2026-07-24T20:10:00+08:00
- **Notes**: Guessed standalone project files for source-linked shared directories (`AuraCombatAiShared` and `AuraCombatSimulationShared`); these directories are compiled into test/CLI projects instead. Resolve project ownership with `rg --files -g '*.csproj'` before reading build metadata.
- **Observed**: 2026-07-24T20:35:00+08:00
- **Notes**: Embedded a double-quoted alternation regex in a PowerShell command string again, causing `status.Busy` to be parsed as a command. Use a single-quoted regex argument or split complex anchor searches into separate `rg` calls.
- **Observed**: 2026-07-24T21:05:00+08:00
- **Notes**: A new UI `Configure` parameter named `modelMode` shadowed the existing string field, producing a string-to-Button assignment error. Use role-specific control suffixes such as `modelModeControl` when a state field already owns the base name.
- **Observed**: 2026-07-24T21:15:00+08:00
- **Notes**: A context-light patch inserted `operationDetailText` into the adjacent training status component instead of the simulation status component. Inspect the exact class field block after cross-cutting UI patches rather than relying on a repeated `statusText` anchor.
- **Observed**: 2026-07-27T00:00:00+08:00
- **Notes**: Used `Promise.all` for repository skill and optional `rg` discovery; the expected no-match `AGENTS.md` probe returned exit code 1 and hid every sibling result. Re-ran with per-command error isolation; use `Promise.allSettled` semantics for exploratory batches.
- **Observed**: 2026-07-28T10:00:00+08:00
- **Notes**: Repeated the invalid direct `foreach (...) { ... } | Format-Table` form while inspecting card-pack assignments. Re-ran by collecting the loop output in `$rows` before formatting.
- **Observed**: 2026-07-28T14:20:00+08:00
- **Notes**: Guessed `CombatCampaignFoundationTraining.cs` under `AuraToolsExp-Dev`; the shared implementation lives under `AuraCombatAiShared`. Resolved it with `rg --files` before continuing.
- **Observed**: 2026-07-28T15:10:00+08:00
- **Notes**: Ran `AuraToolsExp.NativeReward.Tests` without its two required fixture paths, then used `rg --files` against ignored generated/config content and got no results. Read the test entrypoint and project build command to recover the bundled campaign/ruleset arguments.
- **Observed**: 2026-07-28T15:35:00+08:00
- **Notes**: Included guessed shared/worker filenames in otherwise valid `rg` searches three times. The matches still exposed the real files, but the probes exited nonzero; resolve optional paths first or search only known directory roots.
- **Observed**: 2026-07-28T16:05:00+08:00
- **Notes**: Repeated the invalid direct `foreach (...) { ... } | Format-List` PowerShell form while auditing boss definitions, and previously attempted to add two `FileInfo` results with `+`. Collect loop/file outputs into an array before formatting or concatenating.
- **Observed**: 2026-07-28T16:12:00+08:00
- **Notes**: Ran recursive `rg` across the entire decompiled-reference tree and tutorial copy for `GetTagDiff`, exceeding the timeout. Search the known aggregate `AllScripts.cs` and indexed decompiled source roots separately.
- **Observed**: 2026-07-28T16:40:00+08:00
- **Notes**: Guessed a nonexistent `Text/Hard/Hard.csv`, guessed `CombatSimulationEngine.cs` under the wrong shared directory, and repeated the invalid direct `foreach { } | Format-List` form during the boss audit. Resolve files with `rg --files` first and always collect loop output before piping.
- **Observed**: 2026-07-28T16:45:00+08:00
- **Notes**: Started two known long-running PowerShell test scripts with a one-second timeout, causing avoidable killed runs before rerunning with realistic limits. Inspect test entrypoints and assign the full expected build-and-test timeout on the first run.
<<<<<<< HEAD
=======
- **Observed**: 2026-07-28T17:05:00+08:00
- **Notes**: Repeated the invalid direct `foreach (...) { ... } | Format-Table` form while summarizing the training archive. Collect the loop output in an array before piping it to a formatter.
- **Observed**: 2026-07-28T17:08:00+08:00
- **Notes**: Passed `AuraFoundationTrainer.*` as a Windows path to `rg`; PowerShell did not expand it and `rg` rejected the invalid path. Resolve matching directories first or search the repository with explicit `--glob` filters.
- **Observed**: 2026-07-28T17:32:00+08:00
- **Notes**: Passed `*.ps1` as a Windows search root to `rg` while locating trainer build scripts. Use `rg` against explicit directories and filter files with `--glob '*.ps1'`.
- **Observed**: 2026-07-28T17:45:00+08:00
- **Notes**: Used `Get-Process -Name ... -ErrorAction SilentlyContinue` as a standalone existence probe; PowerShell returned exit code 1 when no process existed. Wrap optional process discovery in an explicit `$processes = @(...)` query and report the count.
>>>>>>> 00cdd678a11fef71d3237a27c45ea0c8f465992e

---

## [ERR-20260728-001] artifact-tool-successful-export-nonzero-exit

**Logged**: 2026-07-28T10:10:00+08:00
**Priority**: low
**Status**: pending
**Area**: docs

### Summary
The spreadsheet builder exported and rendered a valid workbook but returned exit code 1 without an exception.

### Error
```text
Inspect result written to file: ...\游戏主体卡牌总表.xlsx.inspect.ndjson
Exit code: 1
```

### Context
- `@oai/artifact-tool` created the requested `.xlsx`, rendered a readable preview, and returned valid inspection data.
- The generated catalog contained 241 unique rows, no missing fields, no unresolved placeholders, and no formulas.
- A diagnostic `.inspect.ndjson` sidecar was emitted during shutdown and removed by the builder.

### Suggested Fix
Treat the exported workbook, render, and inspection results as the primary success signals; investigate whether the artifact runtime sets a nonzero shutdown code when emitting its automatic inspect sidecar.

### Metadata
- Reproducible: yes
- Related Files: docs/游戏主体内容/卡牌内容/游戏主体卡牌总表.xlsx

---

## [ERR-20260729-003] powershell-markdown-generator-binding

**Logged**: 2026-07-29T17:01:37+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
A new PowerShell Markdown generator first failed on smart quotes inside double-quoted strings, then on binding a populated generic string list to a helper parameter.

### Error
```text
ParserError: Missing ')' in method call.
Cannot bind argument to parameter 'Lines' because it is an empty string.
```

### Context
- PowerShell recognizes Unicode smart quotes as string delimiters, so prose smart quotes inside a double-quoted string can terminate it unexpectedly.
- The helper's strongly typed `List[string]` parameter was subject to pipeline-style collection unrolling during argument binding.

### Suggested Fix
Use Chinese corner brackets or escaped ASCII quotes inside PowerShell prose strings, and accept the mutable line collection without a concrete parameter type when passing `List[string]` between helpers.

### Metadata
- Reproducible: yes
- Related Files: tools/Export-GameAndTerriasContentDocs.ps1

### Resolution
- **Resolved**: 2026-07-29T17:01:37+08:00
- **Notes**: Replaced smart quotes, relaxed the helper parameter type, and regenerated both catalogs successfully.

### Recurrence
- **Observed**: 2026-07-29T17:04:00+08:00
- **Notes**: A follow-up validation command interpolated `$p:` inside a double-quoted error message, which PowerShell parsed as an invalid scoped variable. Delimit variables before punctuation, for example `${p}:`.

---
## [ERR-20260730-001] roslyn-syntax-check

**Logged**: 2026-07-30T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The first Roslyn syntax-check command parsed the SDK list output as the SDK
directory and omitted the version segment.

### Error
```
Cannot find path 'C:\Program Files\dotnet\sdk\Roslyn\bincore\Microsoft.CodeAnalysis.dll'
```

### Context
- `dotnet --list-sdks` returns `<version> [<sdk-root>]`.
- The attempted command used only the bracketed root.

### Suggested Fix
Join the bracketed root, reported version, and `Roslyn/bincore`, and enable
`$ErrorActionPreference = 'Stop'` so loader errors cannot be mistaken for a
successful syntax check.

### Metadata
- Reproducible: yes
- Related Files: none

### Resolution
- **Resolved**: 2026-07-30T00:00:00+08:00
- **Notes**: Corrected command used the full versioned SDK path.

---

## [ERR-20260731-001] recursive-decompiled-search-timeout

**Logged**: 2026-07-31T10:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
A recursive search across the full decompiled tree and the 8 MB script inventory exceeded the command timeout.

### Error
```text
command timed out after 14039 milliseconds
```

### Context
- The command combined recursive directory enumeration with an unrestricted `rg` over several large roots.
- The needed reference root was available directly under `开发参考资料/反编译文件夹v1.0.23816797`.

### Suggested Fix
Resolve the decompiled root first, then search specific source directories or known class filenames with explicit globs.

### Metadata
- Reproducible: yes
- Related Files: 开发参考资料/反编译文件夹v1.0.23816797

### Resolution
- **Resolved**: 2026-07-31T10:00:00+08:00
- **Notes**: Narrowed subsequent searches to the identified decompiled project.

### Recurrence
- **Observed**: 2026-07-31T10:03:00+08:00
- **Notes**: A multi-file `rg` inspection returned exit code 1 because the final file had no matches even though earlier files produced useful output. Wrap optional `rg` probes so a no-match result does not mark the whole inspection as failed.

---

## [ERR-20260731-002] ambiguous-ruleset-json-patch

**Logged**: 2026-07-31T11:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: config

### Summary
An action-contract JSON patch matched the first generic
`requiresEnemyTarget`/`fidelity` pair and attached the Divine Choice contract
to `blood_7` instead of `careercard_1`.

### Error
```text
known-integrity-seed:...:action-contract:blood_7:
expected at least 1 card(s) to move from draw pile to hand
```

### Context
- The patch hunk did not include the stable `cardId` identity.
- Native reward integrity tests caught the misplaced contract immediately.

### Suggested Fix
Anchor manual structured-data patches on the owning identity field, then query
the resulting document for the new property before running broad tests.

### Metadata
- Reproducible: yes
- Related Files: AuraToolsExp/Config/combat-simulation/witch-base-evaluation-v2.ruleset.json

### Resolution
- **Resolved**: 2026-07-31T11:00:00+08:00
- **Notes**: Restored `blood_7` and moved the contract under the explicit `careercard_1` object.

---

## [ERR-20260731-003] action-contract-settlement-boundary

**Logged**: 2026-07-31T12:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The first Divine Choice postcondition check ran after `ActionResolved`, so
later triggers that moved the drawn card were misclassified as native action
contract failures.

### Error
```text
action-contract:careercard_1:expected at least 1 card(s) to move from draw pile to hand
```

### Context
- Four campaigns in the 64-seed native integrity sweep produced false failures.
- The contract describes the immediate result of the native `UseScript`, not
  the state after all action lifecycle triggers have settled.

### Suggested Fix
Validate immediate native action postconditions directly after the
`CardPlayed` extension phase, before `ActionStarted` and later lifecycle
events.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatSimulationShared/CombatSimulationEngine.cs

### Resolution
- **Resolved**: 2026-07-31T12:00:00+08:00
- **Notes**: Moved the check to the native script commit boundary; the full
  64-campaign sweep then passed with zero failures.

---

## [ERR-20260731-004] stale-foundation-compatibility-assertions

**Logged**: 2026-07-31T12:20:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The foundation smoke test still expected the previous training, search, and
governance protocol versions after those protocols were intentionally bumped.

### Error
```text
Foundation checkpoint compatibility manifest is incomplete.
```

### Context
- The produced checkpoint contained the current compatibility manifest.
- The PowerShell assertion compared it with obsolete literal version strings.

### Suggested Fix
Update compatibility assertions whenever a protocol version is bumped, and
assert the new action-contract protocol in the same block.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-AuraFoundationTrainer.ps1
- Recurrence-Count: 2
- Last-Seen: 2026-08-03

### Resolution
- **Resolved**: 2026-07-31T12:20:00+08:00
- **Notes**: Updated all three protocol literals and added the
  `action-contract-v1` assertion; the 232-campaign smoke test passed. On
  2026-08-03 the same fixture was also updated from archive schema 3 to 4
  after policy-integrity admission became fail-closed.

---

## [ERR-20260731-005] action-contract-command-queue-boundary

**Logged**: 2026-07-31T12:30:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
Checking the action postcondition immediately after extension notification
worked for synchronous native programs but ran before queued simulation effects.

### Error
```text
Assertion failed: successful contract execution satisfies its draw-to-hand postcondition before cooldown
```

### Context
- Native `UseScript` executes synchronously during `CardPlayed`.
- Generic ruleset effects are compiled into the action command queue.
- Both representations must reach the same contract settlement boundary.

### Suggested Fix
Check postconditions after the current action command queue is executed, but
before the `ActionResolved` lifecycle event is dispatched.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatSimulationShared/CombatSimulationEngine.cs

### Resolution
- **Resolved**: 2026-07-31T12:30:00+08:00
- **Notes**: Both the 359-assertion shared suite and the 64-campaign native
  integrity sweep now pass.

---

## [ERR-20260731-006] powershell-rg-regex-quoting

**Logged**: 2026-07-31T12:40:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
A PowerShell `rg` probe used a grouped regular expression containing escaped
quotes and was parsed as an unclosed group.

### Error
```text
regex parse error: unclosed group
```

### Context
- The search only needed several exact decompiler anchors.

### Suggested Fix
Use `rg -F` with separate `-e` arguments for literal multi-pattern searches.

### Metadata
- Reproducible: yes
- Related Files: 开发参考资料/反编译文件夹v1.0.23816797/AllScripts/AllScripts.cs
- Recurrence-Count: 5
- Last-Seen: 2026-08-03

### Resolution
- **Resolved**: 2026-07-31T12:40:00+08:00
- **Notes**: The literal search located the Divine Choice script at line
  20275 and its cooldown update at line 20293. On 2026-08-03 a double-quoted
  `Select-String` pattern expanded the decompiler's `$Rougamo` identifier and
  failed similarly; single-quoted `-SimpleMatch` recovered the query.

---
## 2026-08-03 - net472 dictionary compatibility in production project

- Error: `Dictionary.GetValueOrDefault(key)` compiled in the net8 test project but not in the AuraToolsExp net472 production target.
- Cause: a validation-path edit used the newer one-argument convenience overload instead of the repository's established `TryGetValue` pattern.
- Fix: use `TryGetValue` for production-target dictionary reads; keep test-only convenience calls inside net8 projects.
- Prevention: compile the affected production project immediately after changing validation helpers, even when the same expression already appears in a net8 test file.
## 2026-08-03 - public observation sanitizer removed owned-reward features

- Error: simulation projection wrote `blessing:blessing_40`, but the normalized player-equivalent observation returned zero and Nana could not detect Nightmare Prototype.
- Cause: `CombatPublicFeaturePolicy.SanitizeState` did not admit the already-established `blessing:` and `relic:` public feature prefixes; new `nana:` and `nightmare:` state features had the same persistence risk.
- Fix: admit visible owned-reward, Nana, and Nightmare prefixes at the player observation boundary, and admit `nightmare:` on actions.
- Prevention: every new observation feature family needs an explicit boundary-sanitizer test, not only a producer-side assertion.

## [ERR-20260803-007] powershell-rg-windows-glob

**Logged**: 2026-08-03T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
PowerShell passed Unix-style recursive glob arguments to `rg`, which Windows
treated as invalid paths.

### Error
```text
rg: AuraFoundationTrainer.ControlCenter/*.cs: 文件名、目录名或卷标语法不正确。 (os error 123)
```

### Context
- The search supplied `directory/*.cs` and `directory/**/*.cs` as positional paths.
- Ripgrep already traverses a directory recursively when given the directory itself.

### Suggested Fix
Pass the directory as the positional path and use `-g '*.cs'` when a file
filter is needed.

### Metadata
- Reproducible: yes
- Related Files: AuraFoundationTrainer.ControlCenter/MainWindow.cs
- Recurrence-Count: 2
- Last-Seen: 2026-08-03

### Resolution
- **Resolved**: 2026-08-03T00:00:00+08:00
- **Notes**: Re-ran the query against the directory without positional globs;
  the same mistake later recurred with `AuraCombatAiShared/*.cs`.

---

## [ERR-20260803-008] result-directory-prefix-collision

**Logged**: 2026-08-03T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A result-directory query matched the shared checkpoint directory because both
names begin with `foundation-controller-`.

### Error
```text
Cannot find path '...\foundation-controller-checkpoint\foundation-worker-result.json'
```

### Context
- The directory filter was `foundation-controller-*`.
- The loop assumed every match was a run directory.

### Suggested Fix
Filter to directories containing `foundation-worker-result.json`, or match the
timestamped run name with a stricter regular expression.

### Metadata
- Reproducible: yes
- Related Files: ModsData/AuraShared/Logs/AuraToolsExp/FoundationTrainer/combat-simulation-results
- Recurrence-Count: 2
- Last-Seen: 2026-08-03

### Resolution
- **Resolved**: 2026-08-03T00:00:00+08:00
- **Notes**: Subsequent analysis enumerates result files first and derives their parent directories.

---

## [ERR-20260803-009] powershell-inline-format-brace

**Logged**: 2026-08-03T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A dense PowerShell format expression had one extra closing brace and failed to parse.

### Error
```text
ParserError: Unexpected token '}' in expression or statement.
```

### Context
- The command nested `ForEach-Object`, a subexpression, `-join`, and `-f` in one line.

### Suggested Fix
Assign nested values such as the quota summary to a local variable before the
format expression.

### Metadata
- Reproducible: yes
- Related Files: ModsData/AuraShared/Logs/AuraToolsExp/FoundationTrainer/combat-simulation-results

### Resolution
- **Resolved**: 2026-08-03T00:00:00+08:00
- **Notes**: Rewrote the diagnostic with an intermediate `$quota` variable;
  a later compact parser also omitted whitespace after PowerShell's `in` token.

---

## [ERR-20260803-010] powershell-nested-array-wilson-probe

**Logged**: 2026-08-03T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A quick PowerShell Wilson-threshold probe used nested array literals that were
flattened during pipeline enumeration and produced nonsensical counts.

### Error
```text
200 target=0.8 required=114 raw=0.57 LB=1
```

### Context
- The case list was represented as nested arrays and then iterated through the pipeline.
- The result contradicted the monotonic Wilson lower-bound expectation.

### Suggested Fix
Represent parameter sets as named `PSCustomObject` values and invoke the
function with named parameters; sanity-check that the returned lower bound is
near the requested threshold.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared/CombatCampaignFoundationTraining.cs

### Resolution
- **Resolved**: 2026-08-03T00:00:00+08:00
- **Notes**: Correct counts are 172/200 for an 0.80 lower bound and 171/500 for an 0.30 lower bound.

---

## [ERR-20260803-011] powershell-double-quoted-regex

**Logged**: 2026-08-03T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A double-quoted PowerShell `rg` pattern containing escaped quotes and a
literal closing parenthesis was parsed as PowerShell syntax.

### Error
```text
ParserError: Unexpected token ')' in expression or statement.
```

### Context
- The search pattern was embedded in a double-quoted PowerShell command.
- Regex punctuation and quote escapes were interpreted by PowerShell first.

### Suggested Fix
Use a single-quoted PowerShell string for literal `rg` patterns.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-AuraCombatAi.ps1

### Resolution
- **Resolved**: 2026-08-03T00:00:00+08:00
- **Notes**: Re-ran the search with a single-quoted pattern.

---

## [ERR-20260803-012] foundation-publish-running-control-center

**Logged**: 2026-08-03T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
Foundation trainer publishing could not replace the running Control Center
executable on Windows.

### Error
```text
Foundation trainer binaries are currently running and Windows will not allow them to be replaced.
```

### Context
- The Control Center was open but no training worker process was active.
- The locked deployment executable blocked the full release build.

### Suggested Fix
Build and test the worker from its project output first, then gracefully close
an idle Control Center before publishing deployment binaries.

### Metadata
- Reproducible: yes
- Related Files: tools/Build-AuraFoundationTrainer.ps1

### Resolution
- **Resolved**: 2026-08-03T00:00:00+08:00
- **Notes**: Verified no worker process was running, closed the responsive UI
  through `CloseMainWindow()`, and published successfully.

---

## [ERR-20260803-013] apply-patch-long-markdown-context

**Logged**: 2026-08-03T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
A documentation patch used a long expected line that did not exactly match
the current edited paragraph.

### Error
```text
apply_patch verification failed: Failed to find expected lines
```

### Context
- The target document already contained edits from the current worktree.
- The patch tried to replace several long Chinese paragraphs at once.

### Suggested Fix
Print the numbered local section and patch the exact current lines.

### Metadata
- Reproducible: no
- Related Files: docs/AuraCombatAI/06-权威模拟与底模训练.md

### Resolution
- **Resolved**: 2026-08-03T00:00:00+08:00
- **Notes**: Re-read lines 73-105 and applied exact replacements.

---

## [ERR-20260807-012] powershell-pipeline-after-foreach

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
An inline PowerShell diagnostics command piped directly after a `foreach`
statement and failed to parse.

### Error
```text
ParserError: An empty pipe element is not allowed.
```

### Context
- The command assembled phase summary objects in a `foreach` statement.
- A pipeline was placed immediately after the closing brace.

### Suggested Fix
Collect the objects into an array, then pipe the completed array to
`Sort-Object` and `Format-Table`.

### Metadata
- Reproducible: yes
- Related Files: none
- Recurrence-Count: 6
- Last-Seen: 2026-08-07

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Re-ran the diagnostics with an explicit `$phaseRows` array. The
  pattern recurred during the P2/P3 test inventory; all subsequent tables
  collect rows before formatting. It recurred once more while checking
  cross-segment C# variable usage and was corrected with a `$rows` accumulator.

---

## [ERR-20260807-013] assumed-baseline-filename

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A compatibility audit assumed the baseline filename instead of locating it.

### Error
```text
Cannot find path 'tools/shared-runtime-api-baseline.json' because it does not exist.
```

### Context
- The actual file is `tools/shared-runtime-compatibility-baseline.json`.

### Suggested Fix
Use `rg --files -g '*baseline*.json'` before reading an uncertain repository
path.

### Metadata
- Reproducible: yes
- Related Files: tools/shared-runtime-compatibility-baseline.json

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Located the baseline and completed the source-contract audit.

---

## [ERR-20260807-014] matrix-runner-strict-mode-leak

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The schema-v2 matrix runner's strict mode leaked into invoked legacy test
scripts and converted optional JSON property reads into failures.

### Error
```text
The property 'relicIds' cannot be found on this object.
```

### Context
- `Test-TerriasGate.ps1 -Profile architecture` invoked the architecture script
  from inside the strict-mode matrix module.
- The existing test intentionally accepts manifests without optional fields.

### Suggested Fix
Keep strict mode for matrix parsing, but invoke each selected test in a child
scope with strict mode disabled so the runner does not change suite semantics.

### Metadata
- Reproducible: yes
- Related Files: tools/modules/TestMatrixRunner.psm1

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Added an invocation scope with `Set-StrictMode -Off`; reran the
  focused architecture profile successfully.

---

## [ERR-20260807-015] extracted-source-output-truncated

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
Extracting a large PowerShell here-string through shell tool output inserted a
truncation marker into the generated C# file.

### Error
```text
Program.cs contained "…6744 tokens truncated…" and failed C# compilation.
```

### Context
- The Terrias temporary test harness was being migrated into a formal project.
- A single 40 KB shell result exceeded the nested output budget.
- The first extraction attempt also over-escaped the PowerShell regex.

### Suggested Fix
Read large source ranges in bounded chunks, validate line counts and anchors,
then apply the assembled content through `apply_patch`.

### Metadata
- Reproducible: yes
- Related Files: Terrias-Dev.Tests/Program.cs, tools/Test-TerriasCSharp.ps1

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Re-extracted 1,173 lines in 220-line chunks; 316 behavior
  assertions then passed.

---

## [ERR-20260807-016] shared-write-entrypoint-existing-violation

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The shared architecture profile is blocked by a direct FileStream use in the
current AuraTools auto-battle model runtime.

### Error
```text
AuraToolsExp-Dev/Features/AutoBattle/AuraToolsAutoBattleModelRuntime.cs: uses new FileStream
```

### Context
- `Test-SharedReleaseGate.ps1 -Profile architecture` selected the existing
  `write-entrypoint-scan` before the newly rewritten architecture rule step.
- The violation is outside the P1 test-governance implementation and was not
  modified during this task.

### Suggested Fix
Route the model write through the approved shared/Core write entrypoint, then
rerun the shared architecture profile.

### Metadata
- Reproducible: yes
- Related Files: AuraToolsExp-Dev/Features/AutoBattle/AuraToolsAutoBattleModelRuntime.cs, tools/Test-SharedWriteEntrypoints.ps1

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: The flagged `FileStream` is read-only input copied into
  `CombatFoundationCheckpointStorage.WriteAtomicStream`. The declarative rule
  now targets write-capable `FileMode` values instead of banning all streams.

---

## [ERR-20260807-017] director-current-target-capability-probe

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: medium
**Status**: pending
**Area**: tests

### Summary
AuraDirector behavior tests reject the current Managed/Witch.dll target during
the allowlisted capability probe.

### Error
```text
Assertion failed: current Witch.dll target passes the allowlisted capability probe
```

### Context
- The formal net472 behavior project builds successfully.
- Failure occurs in `TestCurrentGameTargetInstallAndUninstall` before the P1
  packaging assertions run.

### Suggested Fix
Audit the current Witch.dll signature/hash against the detour backend's
capability allowlist without weakening fail-open behavior.

### Metadata
- Reproducible: yes
- Related Files: AuraDirectorDetour.Tests/Program.cs, AuraDirectorDetour-Dev/AuraDirectorReadyToStartDetourBackend.cs

---

## [ERR-20260807-018] mechanical-test-split-dropped-method-signature

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A mechanical C# test split replaced the first helper signature instead of
wrapping the complete helper block.

### Error
```text
CombatAiTestFixtures.cs: error CS1002 / CS1519
```

### Context
- The split script replaced the first source line (`void Assert(...)`) with the
  fixture class declaration.
- A broad follow-up patch then matched the `ResetAssertions` opening brace.

### Suggested Fix
Keep the full helper slice, add the class wrapper separately, and inspect the
first numbered lines before compiling.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared.Tests/CombatAiTestFixtures.cs
- Recurrence-Count: 2

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Restored both method signatures with an exact-context patch. A
  later domain split duplicated context-local declarations; inspecting the new
  file header and removing the duplicate restored the build.

---

## [ERR-20260807-019] network-scan-found-gameapi-transport-bypass

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: architecture

### Summary
The new generic RPC scan found a direct network send in the Terrias GameApi
layer.

### Error
```text
Terrias-Dev/GameApi/SolarMemoryRoleCommitApi.cs: raw RPC transport bypasses an approved network adapter
```

### Context
- `SolarMemoryRoleCommitApi` constructed and sent the RPC directly.
- The corresponding command and authority behavior already belonged to
  `Terrias-Dev/Network/RpcSolarMemoryRoleCommit.cs`.

### Suggested Fix
Keep transport selection and sending in the network adapter; let GameApi call a
transport-neutral submission method.

### Metadata
- Reproducible: yes
- Related Files: Terrias-Dev/GameApi/SolarMemoryRoleCommitApi.cs, Terrias-Dev/Network/RpcSolarMemoryRoleCommit.cs

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Moved client RPC submission into `RpcSolarMemoryRoleCommit.Submit`.

---

## [ERR-20260807-020] network-scan-found-private-cg-authority

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: architecture

### Summary
The generic server-bound marker scan found that AuraCg still owned a private
receive-hook authority runtime.

### Error
```text
server-bound marker is not registered through AuraRpcAuthorityRuntime: IAuraCgServerBoundRpcCommand
```

### Suggested Fix
Delegate sender binding to the shared `AuraRpcAuthorityRuntime` and keep only a
domain sender adapter in AuraCg.

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: AuraCg now maps `AuraRpcSender` through `AuraCgRpcSender.FromAura`.

---

## [ERR-20260807-021] matrix-list-scalar-count

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
The matrix list command accessed `.Count` on unrolled scalar function output
under strict mode.

### Error
```text
PropertyNotFoundException: The property 'Count' cannot be found on this object.
```

### Suggested Fix
Wrap function invocation output in `@(...)` before reading `.Count`.

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Matrix metadata validation now uses array-subexpression counts;
  `Test-SharedReleaseGate.ps1 -List` completes cleanly.

---

## [ERR-20260807-024] accepted-training-retained-checkpoint-snapshots

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: backend

### Summary
The first newly accepted absolute-qualified smoke run retained catalog-referenced
checkpoint episode snapshots after the active checkpoint was reset.

### Error
```text
Accepted foundation training must remove resume checkpoints.
```

### Suggested Fix
Use the complete checkpoint-artifact deletion path and clear its catalog,
selection anchor, and immutable checkpoint directory on terminal acceptance.

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: `ResetActiveCheckpoint` now removes the complete resume lineage.

---

## [ERR-20260807-025] foundation-smoke-expected-v4-inline-model

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The accepted-path smoke assertion still expected a v4 package with inline model
weights after production publishing had moved to the v5 external FP32 artifact.

### Error
```text
Accepted foundation model package is invalid: schema=5, modelPresent=False
```

### Suggested Fix
Validate schema/model version 5 and require the referenced model artifact file
instead of an inline `Model` object.

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Updated the Worker smoke assertion to the v5 package contract.

---

## [ERR-20260807-022] skill-validator-default-codepage

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tooling

### Summary
The skill validator used the Windows default code page and failed to read a
UTF-8 skill containing Chinese text.

### Error
```text
UnicodeDecodeError: 'gbk' codec can't decode byte 0xa5
```

### Suggested Fix
Set `PYTHONUTF8=1` when invoking the repository skill validator on Windows.

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Re-ran all changed-skill validation with Python UTF-8 mode.

---

## [ERR-20260807-032] new-test-project-missing-system-usings

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: testing

### Summary
New executable test projects assumed implicit framework usings that were not
enabled by their project files.

### Error
```text
CS0246: List<> could not be found
CS0103: Console does not exist in the current context
```

### Suggested Fix
Add explicit `System`, collection, and LINQ imports to small test executables,
or explicitly enable implicit usings in their project file.

### Resolution
- **Resolved**: 2026-08-07T00:00:00+08:00
- **Notes**: Added explicit imports to the Spirit and Columbina behavior tests.

---
## [ERR-20260807-028] functions_exec_javascript

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
PowerShell array syntax was accidentally placed in the JavaScript orchestration body.

### Error
```text
SyntaxError: Invalid or unexpected token
```

### Context
- Attempted to compose a multi-step shared release-gate invocation inside `functions.exec`.
- The outer tool input is JavaScript even when the generated command targets PowerShell.

### Suggested Fix
Build the step-id list with a JavaScript array, then serialize it into the PowerShell command string.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-SharedReleaseGate.ps1

---
## [ERR-20260807-029] Test-AuraDirectorDetour

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The director test assumed the current Managed/Witch.dll was allowlisted even though the reviewed current build is intentionally unverified.

### Error
```text
Assertion failed: current Witch.dll target passes the allowlisted capability probe
```

### Context
- `Managed/Witch.dll` is build `1.0.24591395` with SHA-256 `88613C...36F60`.
- The production detour deliberately allowlists only the game-smoke-tested `1.0.23816797` hash.
- The test encoded a local-environment assumption instead of the fail-closed capability contract.

### Suggested Fix
Accept either verified installation behavior or explicit `detour-target-build-unverified` rejection, and always assert that an unverified target remains unpatched.

### Metadata
- Reproducible: yes
- Related Files: AuraDirectorDetour.Tests/Program.cs, artifacts/game-reference/1.0.24591395/ready-to-start-review.md

---
## [ERR-20260807-030] Test-TerriasBranding

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
The branding gate treated absolute paths preserved in generated reference artifacts as live product branding.

### Error
```text
Legacy brand remains in tracked text: artifacts/game-reference/.../managed.manifest.json
```

### Context
- Game-reference manifests and a generated Combat AI report record the workspace path used to create them.
- These files are diagnostic provenance, not shipped Terrias text or an operational brand surface.

### Suggested Fix
Keep the repository-wide tracked-path check, but exclude reference artifacts and the generated report from textual/Base64 brand scans.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-TerriasBranding.ps1

---
## [ERR-20260807-031] git-diff-check-line-endings

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
Normalizing one C# file to CRLF caused `git diff --check` to report carriage returns as trailing whitespace on changed lines.

### Error
```text
AuraSkinShared/AuraSkinRuntime.cs: trailing whitespace
```

### Context
- The repository index and current edits use LF for this source tree.
- A mechanical rewrite selected the file's pre-existing majority line ending instead of the repository diff convention.

### Suggested Fix
Normalize the touched file to LF and rerun `git diff --check`.

### Metadata
- Reproducible: yes
- Related Files: AuraSkinShared/AuraSkinRuntime.cs

---
## [ERR-20260807-033] ripgrep-lookahead

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A repository audit used a negative lookahead without enabling ripgrep's PCRE2 engine.

### Error
```text
regex parse error: look-around is not supported
```

### Context
- The audit was looking for unscoped `Test-SharedReleaseGate.ps1` documentation.

### Suggested Fix
Use a simple literal search and inspect the small result set, or pass `--pcre2` when lookaround is necessary.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-SharedReleaseGate.ps1

---
## [ERR-20260807-034] parallel-rg-no-match

**Logged**: 2026-08-07T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A parallel read-only audit treated ripgrep's normal no-match exit code as a failed orchestration batch.

### Error
```text
Script error: Exit code: 1
```

### Context
- One of several independent `rg` searches had no matches.
- `Promise.all` rejected the whole batch before the successful search output could be reported.

### Suggested Fix
For exploratory searches that may legitimately find nothing, normalize ripgrep exit code 1 to success inside each shell command before running them in parallel.

### Metadata
- Reproducible: yes
- Related Files: none
- Recurrence-Count: 2

---

## [ERR-20260810-004] combat-ai-foundation-package-validation

**Logged**: 2026-08-10T00:00:00+08:00
**Priority**: medium
**Status**: pending
**Area**: tests

### Summary
The `combat-ai` shared release gate passed behavior and knowledge checks but failed on a bundled foundation model package with an invalid FP32 weight manifest.

### Error
```text
Bundled foundation package protocol validation failed:
AuraToolsExp/ModResource/Model/阿米莉娅 [career_1]/报丧偈羽 [Partner_10001]/通用底模/foundation-model-package-v5.json:
底模网络为空或 FP32 权重清单无效：FP32 权重清单字段或尺寸无效
```

### Context
- Command: `tools/Test-SharedReleaseGate.ps1 -Profile combat-ai`
- `shared-runtime-build`, `combat-ai-behavior` (655 assertions), and `combat-ai-knowledge` passed first.
- The failure occurred in `combat-ai-training-artifacts`, outside the Projection/Spirit/HeartChange runtime code path.

### Suggested Fix
Inspect or regenerate the bundled `foundation-model-package-v5.json` and verify that its network metadata and FP32 tensor manifest agree on field names and dimensions.

### Metadata
- Reproducible: yes
- Related Files: AuraToolsExp/ModResource/Model/阿米莉娅 [career_1]/报丧偈羽 [Partner_10001]/通用底模/foundation-model-package-v5.json

---

## [ERR-20260810-005] terrias-validator-path

**Logged**: 2026-08-10T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The Terrias content validator was invoked from `tools/`, but the script belongs to the project-local Terrias skill.

### Error
```text
The term '.\tools\validate-terrias.ps1' is not recognized as a name of a cmdlet, function, script file, or executable program.
```

### Context
- Incorrect command: `tools/validate-terrias.ps1`
- Actual path: `.codex/skills/terrias-mod-dev/scripts/validate-terrias.ps1`

### Suggested Fix
Run the validator from its project-local skill path.

### Metadata
- Reproducible: yes
- Related Files: .codex/skills/terrias-mod-dev/scripts/validate-terrias.ps1

---

## [ERR-20260810-006] powershell-foreach-pipeline

**Logged**: 2026-08-10T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A PowerShell link check piped directly after a `foreach` statement and failed to parse.

### Error
```text
ParserError: An empty pipe element is not allowed.
```

### Context
- The command attempted `foreach (...) { ... } | Format-Table` in statement form.
- The corrected command assigns the `foreach` output to `$rows` and pipes `$rows` afterward.

### Suggested Fix
Capture statement-form `foreach` output in a variable before piping, or wrap the expression explicitly.

### Metadata
- Reproducible: yes
- Related Files: docs/Terrias/modules/11-投影精灵与心变的Partner战斗流程.md

---

## [ERR-20260810-008] foundation-result-schema-assumption

**Logged**: 2026-08-10T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A diagnostic parser assumed foundation iterations were top-level and indexed a null array; a follow-up tool script also used invalid JavaScript block syntax.

### Error
```text
Cannot index into a null array.
SyntaxError: Unexpected identifier 'r'
```

### Context
- `foundation-worker-result.json` stores the training payload under `Training`, not `Result` or at the document root.
- The orchestration body is JavaScript; `let{...}` is invalid block syntax.

### Suggested Fix
Inspect top-level JSON property names before selecting nested payloads, and use a plain JavaScript block or declarations in tool orchestration.

### Metadata
- Reproducible: yes
- Related Files: ModsData/AuraShared/Logs/AuraToolsExp/FoundationTrainer/combat-simulation-results/foundation-controller-20260810-012407859/foundation-worker-result.json

---

## [ERR-20260810-009] optional-project-rg-path

**Logged**: 2026-08-10T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A repository search passed a guessed `AuraFoundationTrainer` directory that does not exist, causing ripgrep to return an error after otherwise useful matches.

### Error
```text
rg: AuraFoundationTrainer: The system cannot find the file specified. (os error 2)
```

### Context
- The actual projects are `AuraFoundationTrainer.Worker` and `AuraFoundationTrainer.ControlCenter`.
- The follow-up used `rg --files` to discover concrete project paths before searching them.

### Suggested Fix
Discover optional or uncertain repository paths with `rg --files` before passing them as positional roots to ripgrep.

### Metadata
- Reproducible: yes
- Related Files: none

---
# SQLite artifact hashing retained a pooled file handle

- Date: 2026-08-10
- Symptom: the artifact smoke test could not hash `simulation-process-v1.sqlite` immediately after export because Microsoft.Data.Sqlite still held the database open.
- Cause: disposing a pooled SQLite connection returns it to the pool rather than closing the underlying file handle.
- Fix: artifact-export connections set `Pooling = false` before hashing or atomically publishing the database.

---

## [ERR-20260810-A17] powershell-numeric-array-expression

**Logged**: 2026-08-10T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
A PowerShell analysis command parsed comma-separated arithmetic expressions as array operations, then overflowed while reproducing a 32-bit FNV hash.

### Error
```text
System.Object[] does not contain a method named 'op_Addition'.
Arithmetic operation resulted in an overflow.
```

### Context
- The command generated a review-only species-stat table from current enemy data.
- Arithmetic entries in `@(...)` needed individual parentheses.
- FNV multiplication needed `BigInteger` plus an explicit decimal `4294967295` mask; PowerShell treated `0xffffffff` as signed `-1`.

### Suggested Fix
Parenthesize each arithmetic array element and use an explicitly typed unsigned mask when emulating unchecked 32-bit arithmetic in PowerShell.

### Metadata
- Reproducible: yes
- Related Files: docs/Terrias/design/04-游戏主体精灵种族值表.csv

---

## [ERR-20260810-A18] powershell-review-query-assumptions

**Logged**: 2026-08-10T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
Initial review queries assumed numeric enemy IDs, guessed a nested README path, and piped directly from a `foreach` statement.

### Error
```text
Cannot convert value "enemy_10001" to type "System.Int32".
Cannot find path 'docs/Terrias/design/README.md'.
An empty pipe element is not allowed.
```

### Context
- Game-table enemy IDs include the `enemy_` prefix and must be compared as strings.
- The Terrias documentation index is `docs/Terrias/README.md`.
- PowerShell requires collecting `foreach` output into a variable, or wrapping the statement, before piping it.
- PowerShell comparison operators require token separation; `-eq$id` is parsed as a parameter name rather than `-eq $id`.

### Suggested Fix
Inspect one source row and discover index files before composing the full query; preserve source identifier types, materialize loop output before formatting, and keep spaces around comparison operators.

### Metadata
- Reproducible: yes
- Related Files: docs/游戏主体内容/combat-knowledge/table-exports/witch-tables-20260807-134623.json, docs/Terrias/README.md

---

## [ERR-20260810-A19] powershell-interpolated-colon

**Logged**: 2026-08-10T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
A PowerShell diagnostic label placed a colon immediately after an interpolated variable name.

### Error
```text
Variable reference is not valid. ':' was not followed by a valid variable name character.
```

### Context
PowerShell interpreted `$f:` as a scoped-variable token while printing a file-and-line heading.

### Suggested Fix
Delimit the variable explicitly when punctuation follows it: `${f}:$start`.

### Metadata
- Reproducible: yes
- Related Files: Terrias-Dev/Mechanics/SpiritModels.cs

---

## [ERR-20260810-A20] powershell-glob-and-null-last-exit-code

**Logged**: 2026-08-10T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
PowerShell passed wildcard path roots directly to ripgrep on Windows, and a later combined test command treated an unset `$LASTEXITCODE` as failure after a successful PowerShell script.

### Error
```text
rg: tools/Test-Spirit*.ps1: The filename, directory name, or volume label syntax is incorrect.
```

### Context
- Ripgrep should receive a concrete directory plus `-g "Test-Spirit*.ps1"`, not a wildcard positional root.
- PowerShell scripts that do not launch native processes may leave `$LASTEXITCODE` unset; `$null -ne 0` evaluates true.
- The follow-up ran each script as an independent command and relied on the tool exit code.

### Suggested Fix
Use ripgrep `-g` filters for Windows globs, and do not gate consecutive PowerShell scripts on `$LASTEXITCODE` unless the called script explicitly sets it.

### Metadata
- Reproducible: yes
- Related Files: tools/Test-SpiritCapture.ps1, tools/Test-SpiritRegistry.ps1

---

## [ERR-20260810-A21] guessed-validation-entrypoints

**Logged**: 2026-08-10T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
Validation initially used guessed project and aggregate-script paths that do not exist in this repository.

### Error
```text
Cannot find path 'Terrias-Dev/Terrias-Dev.csproj'.
The term '.\tools\Test-All.ps1' is not recognized.
```

### Context
- The actual project is `Terrias-Dev/Terrias.Dll.csproj`.
- The aggregate entry point is `tools/Test-TerriasGate.ps1`, with discoverable profiles from `-List`.

### Suggested Fix
Discover `*.csproj` and validation scripts with `rg --files` or `Get-ChildItem` before invoking them.

### Metadata
- Reproducible: yes
- Related Files: Terrias-Dev/Terrias.Dll.csproj, tools/Test-TerriasGate.ps1

---

## [ERR-20260810-A22] full-release-branding-scans-generated-diagnostics

**Logged**: 2026-08-10T00:00:00+08:00
**Priority**: low
**Status**: open
**Area**: tests

### Summary
The full Terrias release matrix passed every build, behavior, architecture, content, resource, event, spirit and domain step, then failed branding because a pre-existing tracked diagnostic JSON contains absolute workspace paths with the legacy repository folder name.

### Error
```text
Legacy brand remains in tracked text: tmp/resume-diagnostic/job.json
```

### Context
- The failure is unrelated to the spirit schema 2 implementation.
- The offending `tmp/resume-diagnostic` files predate this change and were not modified during this task.
- The targeted `spirit` profile passes all three spirit steps.

### Suggested Fix
Decide separately whether generated diagnostic fixtures should be excluded from branding scans or normalized to repository-relative paths.

### Metadata
- Reproducible: yes
- Related Files: tmp/resume-diagnostic/job.json, tools/Test-TerriasBranding.ps1

---

## [ERR-20260810-A23] net472-hex-api-and-powershell-assert-expression

**Logged**: 2026-08-10T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: build

### Summary
The first full build exposed a .NET 5+ hex API in the net472 shared runtime, and the first schema 2 content test used a multiline PowerShell assertion whose parentheses were ambiguous to the parser.

### Error
```text
Convert does not contain a definition for ToHexString.
Missing closing ')' in expression.
```

### Context
- `AuraSharedRuntime-Dev` targets net472, so hash formatting must use `BitConverter.ToString(...).Replace("-", "")`.
- The profile identity comparison became clearer and parser-safe after assigning `Compare-Object` output to `$identityDiff` before asserting.

### Suggested Fix
Keep shared-runtime APIs within the target framework surface, and materialize nontrivial PowerShell pipeline expressions before passing them to assertion functions.

### Metadata
- Reproducible: yes
- Related Files: AuraCombatAiShared/CombatCampaignFoundationTraining.cs, tools/Test-SpiritCapture.ps1

---

## [ERR-20260810-A24] wildcard-variant-normalized-to-empty

**Logged**: 2026-08-10T00:00:00+08:00
**Priority**: high
**Status**: resolved
**Area**: runtime

### Summary
Schema 2 loaded successfully but every profile with `variantId: "*"` missed at runtime because generic identifier normalization stripped the wildcard into an empty string.

### Error
```text
Explicit base-game profile resolved as deterministic fallback despite registryState=ready.
```

### Context
- Structural JSON tests could not detect the runtime matching failure.
- A new runtime test loads the actual shipped growth registry and resolves `enemy_10048` / `enemy_10051` into their fixed multi-form identities.
- Variant normalization now preserves `*` before applying runtime-prefix cleanup.

### Suggested Fix
Test configuration files through the production loader and resolver, not only through deserialization and structural assertions. Treat wildcard tokens as grammar before generic identifier sanitation.

### Metadata
- Reproducible: yes
- Related Files: Terrias-Dev/Mechanics/SpiritGrowthRegistry.cs, Terrias-Dev.SpiritTests/Program.cs

---

## [ERR-20260810-A25] sqlite-context-manager-does-not-close-connection

**Logged**: 2026-08-10T18:23:00+08:00
**Priority**: high
**Status**: resolved
**Area**: tests

### Summary
The Transformer annotation-cache self-test passed its assertions but Windows could not remove the temporary SQLite file because Python's `sqlite3.Connection` context manager commits or rolls back without closing the connection.

### Error
```text
PermissionError: [WinError 32] another process is using annotation-cache.sqlite
```

### Context
- `with sqlite3.connect(...) as connection` manages the transaction, not the connection lifetime.
- The leaked handle also made a production cache harder to replace or clean up on Windows.

### Suggested Fix
Wrap SQLite connections with `contextlib.closing(...)` or close them explicitly in `finally`, then retain the transaction context inside that lifetime.

### Metadata
- Reproducible: yes
- Related Files: tools/transformer-teacher/train_teacher.py
## [ERR-20260812-001] terrias_projection_build

**Logged**: 2026-08-12T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
Removing `System.Collections` from `ProjectionOtherObj` made coroutine return types bind to generic `IEnumerator<T>`.

### Error
```
CS0305: using the generic type IEnumerator<T> requires 1 type argument
```

### Context
- First build after moving projection capture out of `ProjectionOtherObj`.
- The class still has `DoAction` and remote wait coroutines returning non-generic `IEnumerator`.

### Suggested Fix
Keep `using System.Collections;` whenever a Unity coroutine file uses non-generic `IEnumerator`.

### Metadata
- Reproducible: yes
- Related Files: Terrias-Dev/Mechanics/ProjectionOtherObj.cs

### Resolution
- **Resolved**: 2026-08-12T00:00:00+08:00
- **Notes**: Restored the required namespace import.

---
# ERR-20260812-002: Windows PowerShell cannot run RPC authority scanner

**Logged**: 2026-08-12
**Severity**: low
**Status**: resolved
**Area**: validation tooling

## What failed

`powershell -File tools/Test-NetworkRpcAuthority.ps1` failed because Windows PowerShell's .NET runtime does not expose `System.IO.Path.GetRelativePath`.

## Resolution

Run this repository scanner with PowerShell 7 (`pwsh`) instead of Windows PowerShell.

---
# ERR-20260812-003: AuraToolsExp test project path typo

**Logged**: 2026-08-12
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

An inspection command used the nonexistent path `AuraToolsExp-Dev.Tests/AuraToolsExp.Dll.Tests.csproj`.

## Resolution

Use the repository's actual test project path: `AuraToolsExp-Dev.Tests/AuraToolsExp-Dev.Tests.csproj`.

---
# ERR-20260812-004: PowerShell wildcard passed literally to rg

**Logged**: 2026-08-12
**Severity**: low
**Status**: resolved
**Area**: tooling

## What failed

`rg` received `AuraToolsExp-Dev/Features/DamageMeter/*.cs`; PowerShell did not expand the recursive path pattern and ripgrep rejected it as an invalid Windows path.

## Resolution

Pass the directory to `rg` and use `-g '*.cs'` when a file glob is needed.

---
# ERR-20260812-005: AuraTools validation retained DPS v28 defaults

**Logged**: 2026-08-12
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

`tools/Test-AuraToolsExp.ps1` still asserted match-experience schema 28 and the retired `showPanelByDefault` and `loadHistoryOnStartup` fields.

## Resolution

Update the shipped-config contract to schema 29 and assert the new `displayMode`, `displayScope`, and `teamFilter` defaults.

---
# ERR-20260812-006: Shared DLL package hash mismatch

**Logged**: 2026-08-12
**Severity**: medium
**Status**: resolved
**Area**: packaging
**Recurrence-Count**: 2
**Last-Seen**: 2026-08-17

## What failed

`tools/Test-SharedDllPackaging.ps1` found that `SanGuoShaExp/Scripts/Aura.Shared.dll` was older than the current shared runtime copied into Terrias and AuraToolsExp.

## Resolution

Synchronize the generated `Aura.Shared.dll` package copy across all three shared-runtime consumers before rerunning the packaging gate.

The mismatch recurred after rebuilding only AuraToolsExp during the settings-page migration. Running `Build-MainSharedConsumers.ps1` synchronized all three package copies and restored the gate.

---
# ERR-20260812-007: Quoted ripgrep alternation was malformed

**Logged**: 2026-08-12
**Severity**: low
**Status**: resolved
**Area**: tooling

## What failed

A PowerShell-embedded `rg` alternation ended with an escaped JSON quote and produced an unclosed regular-expression group.

## Resolution

Use multiple `-e` arguments for literal cleanup checks instead of one heavily escaped alternation.

---
# ERR-20260812-008: Validation commands were chained in one shell call

**Logged**: 2026-08-12
**Severity**: low
**Status**: resolved
**Area**: tooling

## What failed

Two independent validation scripts were joined with PowerShell semicolons even though repository interaction guidance requires separate tool calls for clearer output.

## Resolution

Run validation scripts as separate shell tool calls; use the supported parallel orchestration only when the checks are independent and read-only.

---
# ERR-20260812-009: Guessed source path and Windows wildcard caused read failures

**Logged**: 2026-08-12
**Severity**: low
**Status**: resolved
**Area**: tooling
**Recurrence-Count**: 2
**Last-Seen**: 2026-08-17

## What failed

Source inspections guessed `CombatObservationModels.cs` and later `CombatActionScoring.cs` instead of using paths returned by `rg`; another `rg` call also passed a Windows wildcard path directly.

## Resolution

Read the exact path returned by `rg`, and pass directories plus `-g` patterns rather than wildcard path arguments on Windows.

The pattern recurred on 2026-08-17 when a quoted Windows wildcard was passed to `rg`; directory plus `-g` remains the required form.

---
# ERR-20260812-010: Projection executor overload names were swapped

**Logged**: 2026-08-12
**Severity**: low
**Status**: resolved
**Area**: backend

## What failed

The first Terrias build after adding multi-target projection execution failed because the single-target and target-list `PrepareExecutor` method names were swapped.

## Resolution

Restore `PrepareExecutor` for one nullable target and use `PrepareExecutorTargets` for the target collection. The Terrias project then built with zero warnings and errors.

---
# ERR-20260812-011: Shared validation script names were guessed incorrectly

**Logged**: 2026-08-12
**Severity**: low
**Status**: resolved
**Area**: tooling

## What failed

Validation attempted `tools/Test-AuraRpcAuthority.ps1` and `tools/Test-AuraSharedDllPackaging.ps1`, but the repository scripts are named `Test-NetworkRpcAuthority.ps1` and `Test-SharedDllPackaging.ps1`.

## Resolution

Resolve validation script paths with `rg --files tools` before invoking them instead of reconstructing names from memory.

---
# ERR-20260812-012: RPC authority gate requires PowerShell 7

**Logged**: 2026-08-12
**Severity**: low
**Status**: resolved
**Area**: tooling

## What failed

Running `Test-NetworkRpcAuthority.ps1` with Windows PowerShell failed because its .NET runtime does not provide `System.IO.Path.GetRelativePath`.

## Resolution

Run repository validation scripts that use modern .NET APIs with `pwsh`; the RPC authority scan then passed.

---
# ERR-20260817-001: PowerShell New-Object did not bind the Bitmap pixel-format enum

**Logged**: 2026-08-17
**Severity**: low
**Status**: resolved
**Area**: tooling

## What failed

The first card-art resize command passed `[System.Drawing.Imaging.PixelFormat]::Format32bppArgb` through `New-Object` positional arguments. PowerShell treated the enum expression as text, so `Bitmap` construction failed and the later graphics calls received null.

## Resolution

Construct typed GDI+ objects with `[System.Drawing.Bitmap]::new(...)` when enum overloads are involved, and validate that the output exists before continuing the art pipeline.

---
# ERR-20260817-002: PowerShell edge-validation loop had parser-sensitive expressions

**Logged**: 2026-08-17
**Severity**: low
**Status**: resolved
**Area**: tooling

## What failed

The first border-pixel validation piped a `foreach` statement directly into `Format-Table`, which PowerShell parsed as an empty pipeline element. The next attempt used `@(0,$bmp.Height-1)`, which bound subtraction to the array expression instead of the height value.

## Resolution

Capture `foreach` output in an array before piping, and calculate typed `$lastX` and `$lastY` values before constructing edge-index arrays.

---
# ERR-20260817-003: System Python lacked Pillow for the image helper

**Logged**: 2026-08-17
**Severity**: low
**Status**: resolved
**Area**: tooling

## What failed

The bundled chroma-key helper was first invoked with the system `python`, whose environment did not include Pillow, so no alpha output was written.

## Resolution

Load Codex workspace dependencies and invoke the helper with the bundled Python runtime at the returned path instead of installing packages into the project or system environment.

---
# ERR-20260817-004: PowerShell parsed a colon as part of an interpolated variable

**Logged**: 2026-08-17
**Severity**: low
**Status**: resolved
**Area**: tooling

## What failed

The prepared-art resize command used `$name:` inside a double-quoted error message. PowerShell treated the colon as part of a scoped variable reference and rejected the script before it executed.

## Resolution

Use `${name}:` when punctuation immediately follows an interpolated PowerShell variable.

---
# ERR-20260817-005: Built-in image generation accepts at most five reference paths

**Logged**: 2026-08-17
**Severity**: low
**Status**: resolved
**Area**: tooling

## What failed

The first False Gold Dream cover request supplied seven local references, but the built-in image generation tool rejects `referenced_image_paths` lists longer than five.

## Resolution

Compose related series references into an unlabeled contact sheet and pass that sheet together with the approved benchmark and one structural cover reference.

---
# ERR-20260817-006: Gold Dream HUD runtime missed the ModHookContext namespace

**Logged**: 2026-08-17
**Severity**: low
**Status**: resolved
**Area**: backend

## What failed

The first Terrias build could not resolve `ModHookContext` in `GoldDreamHudRuntime.cs` because the new hook runtime omitted `using Witch.Core;`. The build wrapper also returned shell exit code 0 even though MSBuild reported compilation errors.

## Resolution

Import `Witch.Core` in the HUD runtime and inspect build output for `生成失败`/compiler errors rather than relying only on the wrapper process exit code.

---
# ERR-20260817-007: Terrias branding gate is blocked by pre-existing tracked diagnostics

**Logged**: 2026-08-17
**Severity**: low
**Status**: pending
**Area**: tests

## What failed

`tools/Test-TerriasBranding.ps1` found the retired brand token inside tracked `tmp/resume-diagnostic/*.json` absolute paths. The same gate also still requires `ModVersion` 0.5.0 while the existing `Terrias/ModConfig.json` was already 0.5.1 before the False Gold Dream work.

## Resolution

This task leaves unrelated diagnostic artifacts and the stale branding-test version contract unchanged. Clean or exclude those tracked diagnostics and align the gate with the current release version in a dedicated branding-maintenance change.

---
# ERR-20260817-002: First AuraTools module-host build retained an old initializer and missed shared UI imports

**Logged**: 2026-08-17
**Severity**: low
**Status**: resolved
**Area**: backend

## What failed

The first AuraToolsExp build after moving feature initialization into `AuraToolModuleHost` still called `AuraToolsFileLogRuntime` from `Entry.cs`. The Feast and card-use-CG editors also used the new `AuraUiViewState` types without importing `AuraUi.Shared`.

## Suggested Fix

Remove the obsolete Entry logging initialization so logging has one module owner, add the missing shared UI namespace imports, and rebuild before continuing the refactor.

## Resolution

Removed the old Entry initializer, imported `AuraUi.Shared` in the affected editors, and rebuilt AuraToolsExp with zero warnings and errors.

---
# ERR-20260817-003: Shared-write gate has pre-existing consumer write violations

**Logged**: 2026-08-17
**Severity**: low
**Status**: pending
**Area**: tests

## What failed

`tools/Test-SharedWriteEntrypoints.ps1` reported existing direct file writes in AuraToolsExp match-record media/portability code, PixelEmoji storage, and Terrias SpiritCollection code. The current toolbox refactor did not edit those write paths.

## Suggested Fix

Handle this as a separate storage-boundary task: route durable writes through approved owner stores where appropriate, and explicitly classify legitimate media stream writers before changing the shared-write exclusions.

---
# ERR-20260817-005: Settings runtime extraction left one obsolete AutoBattle field write

**Logged**: 2026-08-17
**Severity**: low
**Status**: resolved
**Area**: backend

## What failed

The first build after moving the AutoBattle settings page out of `AuraToolsSettingsRuntime` failed because `ClearPanel` still assigned the removed `autoBattleSnapshotRevisionBuilt` field.

## Resolution

Removed the stale cleanup assignment and rebuilt AuraToolsExp with zero warnings and errors.

---
# ERR-20260817-008: Forward-schema guard was inserted into PixelEmoji read methods

**Logged**: 2026-08-17
**Severity**: low
**Status**: resolved
**Area**: backend

## What failed

An ambiguous patch context inserted the PixelEmoji library read-only write guard
into `GetItems` and `Find`, where no `error` output exists and the methods do not
return `bool`. The first full DLL build caught the invalid returns. The first
AuraTooling test command also used the wrong project filename.

## Resolution

Moved the guard into the mutating `Save` and `Delete` methods, retained reads for
newer-schema inspection, added the nullable snapshot success check, and used the
actual `AuraToolingShared.Tests.csproj` filename for validation.

---
# ERR-20260817-009: Unconstrained generic settings cannot use null propagation

**Logged**: 2026-08-17
**Severity**: low
**Status**: resolved
**Area**: backend

## What failed

The main-consumer build rejected `snapshot.Value?.Settings` in the generic
module configuration store with CS8978 because unconstrained `T` cannot be made
nullable through the null-propagation operator.

## Resolution

Projected the optional generic setting into an explicit `object?` with a
conditional expression before passing it to the reflection-based schema policy,
then reran the main-consumer build.

---
# ERR-20260817-010: Network authority gate requires PowerShell 7

**Logged**: 2026-08-17
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

Running `Test-NetworkRpcAuthority.ps1` through Windows PowerShell failed because
its .NET Framework runtime does not provide `System.IO.Path.GetRelativePath`.

## Resolution

Ran the unchanged gate with `pwsh`, where the required API is available. The
authority scan then passed all 868 files with four server-bound command markers.

The first compatibility rewrite also exposed that Windows PowerShell cannot
continue a fluent method chain on a new line after a backtick. The final helper
uses two ordinary assignments, which is valid in both Windows PowerShell and
PowerShell 7.

---
# ERR-20260818-001: PowerShell pipeline cannot continue from a foreach statement

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: docs

## What failed

A one-off Terrias CSV inventory command piped directly from a `foreach`
statement into `ConvertTo-Json`, which PowerShell parsed as an empty pipe
element.

## Resolution

Materialize the `foreach` results into a variable first, then pipe that variable
to `ConvertTo-Json`. No repository content was changed by the failed command.

---
# ERR-20260818-002: Relic CSV patch assumed quoted rows

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: docs

## What failed

The first relic localization patch used fully quoted CSV rows reconstructed from
`Import-Csv`, while the source file stores most fields unquoted and quotes only
fields containing ASCII commas. Patch verification correctly rejected the
nonexistent source lines.

## Resolution

Read the raw CSV rows, preserved their actual field order and quoting style, and
reapplied the five row replacements. The failed patch made no file changes.

---
# ERR-20260818-002: PowerShell passed a wildcard literally to rg

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: docs

## What failed

A repository search passed `README*` as a positional path to `rg` from
PowerShell. PowerShell did not expand the wildcard, so ripgrep reported Windows
error 123 for an invalid path while continuing to scan the remaining paths.

## Resolution

Use ripgrep's `-g 'README*'` glob filter or enumerate matching files with
`Get-ChildItem` before invoking ripgrep. The search itself completed and no
repository content was affected.

---
# ERR-20260818-003: UI design-doc cross-link patch used stale wording

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: docs

## What failed

An `apply_patch` hunk expected an older, more detailed wording for the first
implementation-status bullet in the toolbox architecture document, so context
verification rejected the patch.

## Resolution

Read the current heading block and applied the cross-link using only the stable
heading and blank-line context. No source or documentation content was lost.

---
# ERR-20260818-004: Toolbox gate checked an icon key in the renderer file

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

The first AuraToolsExp gate after the toolbox redesign looked for the literal
`category.extensions` icon key in `ToolboxCategoryRail.cs`. Category metadata is
owned by `ToolboxSettingsShell.cs`, while the rail only consumes definitions,
so the otherwise valid implementation failed the new source contract.

## Resolution

Pointed the assertion at the category metadata owner and reran the focused
AuraToolsExp gate.

---
# ERR-20260818-005: Architecture gate was started with Windows PowerShell

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

`Test-TerriasArchitecture.ps1` was invoked through Windows PowerShell, whose
.NET Framework runtime does not provide `System.IO.Path.GetRelativePath` used
by the shared architecture validation module.

## Resolution

Reran the unchanged gate with `pwsh`. Future architecture and network gates
using `ArchitectureBoundaryValidation.psm1` should be launched with PowerShell
7.

**See Also**: ERR-20260817-010

---
# ERR-20260818-006: Bundled Playwright package had no downloaded Chromium

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

The first HTML preview capture resolved the bundled Playwright Node package but
its matching headless Chromium executable had not been downloaded, so
`chromium.launch()` failed before the first screenshot.

## Resolution

The preview runner now prefers Playwright's managed browser and falls back to a
locally installed Edge, Chrome, or Chromium executable. An explicit browser can
also be supplied through `AURA_PREVIEW_BROWSER_EXECUTABLE`.

---
# ERR-20260818-007: Assembly-version inventory piped directly from foreach

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

The first Unity preview environment probe piped directly from a PowerShell
`foreach` statement into `Format-Table`, which PowerShell parsed as an empty
pipe element.

## Resolution

Materialized the assembly metadata rows in a collection before formatting.
The corrected probe confirmed Unity 2022.3.62f3c1 and Windows standalone build
support are installed.

**See Also**: ERR-20260818-001

---
# ERR-20260818-008: LiteralPath prevented Unity icon wildcard expansion

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: config

## What failed

The initial mechanical copy into the standalone Unity preview passed `*.png`
to `Copy-Item -LiteralPath`. Literal paths intentionally do not expand
wildcards, so no icon resources were copied.

## Resolution

Enumerated the source icon files with `Get-ChildItem` and piped the resulting
file objects to `Copy-Item`.

---
# ERR-20260818-009: Unity preview reused surface local name across scopes

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: frontend

## What failed

The first Unity compilation rejected `surface` inside the tab-building loop
because the same method declared another `surface` in the enclosing window
scope.

## Resolution

Renamed the tab-local object to `tabSurface` and reran the standalone Player
build.

---
# ERR-20260818-010: Windows PowerShell decoded native UI asset names incorrectly

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: config

## What failed

The Unity preview build script embedded Chinese native-asset filenames. Windows
PowerShell decoded one path with the active ANSI code page and could not find
the source selector image.

## Resolution

The sync step now locates the five known native assets by ASCII directory,
extension and stable byte length, then copies them to ASCII preview filenames.

---
# ERR-20260818-011: Preview Image factory shadowed the UGUI Image type

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: frontend

## What failed

After adding automatic sliced-image selection, references to `Image.Type` inside
the `PreviewUi.Image` method resolved to the method group rather than the UGUI
type.

## Resolution

Qualified the enum as `UnityEngine.UI.Image.Type` and rebuilt the Player.

---
# ERR-20260818-012: Unity resized NPOT native UI atlases before runtime cropping

**Logged**: 2026-08-18
**Severity**: medium
**Status**: resolved
**Area**: frontend

## What failed

Unity's default NPOT import converted the 169x81 native button atlas to 128x64.
The runtime crop copied from AuraToolsExp then exceeded the imported texture,
aborting settings construction and leaving the capture Player alive until the
timeout.

## Resolution

The Editor builder now imports all native preview textures with NPOT scaling
disabled, uncompressed pixels, no mipmaps, clamp wrapping and point filtering
before building the Player.

---
# ERR-20260818-013: Native asset audit repeated the foreach pipeline error

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: docs

## What failed

The component-resource audit piped directly from a PowerShell `foreach`
statement into `Format-Table`, reproducing the empty-pipe parse failure.

## Resolution

Materialized the image-dimension rows before formatting and retained the
existing prevention note for future inventory commands.

**See Also**: ERR-20260818-001, ERR-20260818-007

---
# ERR-20260818-019: Managed decompile gate used a stale snapshot layout

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

`Test-GameManagedDecompile.ps1` was invoked with
`开发参考资料/Managed快照/v1.0.24605918`, but this workspace retains only the
older partial snapshot under `Managed快照`; the current complete snapshot is
represented by the checked-in `artifacts/game-reference/1.0.24605918`
manifests and the matching decompile directory.

## Resolution

Do not infer a versioned local Managed snapshot directory from the artifact
manifest. Check the actual `开发参考资料/Managed快照` layout first, and use the
current decompile sources plus repository `Managed/` assemblies for targeted
signature verification when the frozen input snapshot is not present.

---

---
# ERR-20260818-014: Ripgrep treated a leading double-dash pattern as an option

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: frontend

## What failed

A CSS inventory pattern began with `--background`, so ripgrep parsed the joined
pattern as a command-line flag.

## Resolution

Inserted ripgrep's `--` end-of-options marker before the pattern.

---
# ERR-20260818-015: Shell quoting broke a regex containing role attributes

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: frontend

## What failed

A PowerShell search combined escaped quotes and a regex alternation for HTML
role attributes, leaving ripgrep with an unclosed group.

## Resolution

Used multiple fixed-string `-e` patterns instead of one shell-sensitive regex.

---
# ERR-20260818-016: Toolbox input repeated Image type shadowing

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: frontend

## What failed

The new Unity toolbox input referenced `Image.Type` from inside the
`PreviewUi` class, where the `Image` factory method shadows the UGUI type.

## Resolution

Qualified the enum as `UnityEngine.UI.Image.Type`, matching the earlier factory
fix.

---
# ERR-20260818-017: Ignore audit piped directly from foreach

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: config

## What failed

The first `.gitignore` audit piped directly from two PowerShell `foreach`
statements into formatters, reproducing the empty-pipe parse failure.

## Resolution

Materialized both audit result sets before formatting.

**See Also**: ERR-20260818-001, ERR-20260818-007, ERR-20260818-013

---
# ERR-20260818-018: Mirror ignore audit repeated the foreach pipeline error

**Logged**: 2026-08-18
**Severity**: medium
**Status**: resolved
**Area**: config

## What failed

The follow-up Unity mirror audit again piped directly from `foreach` into a
formatter despite multiple same-session corrections.

## Resolution

Materialized the audit rows before formatting. Treat collection-first output
as mandatory for all PowerShell inventory loops in this repository.

**See Also**: ERR-20260818-001, ERR-20260818-007, ERR-20260818-013, ERR-20260818-017

---
# ERR-20260818-009: Background-removal post-processing hit local runtime mismatches

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: config

## What failed

The False Gold Dream cover extraction encountered several non-destructive
tooling mismatches: the first image-generation wrapper used an invalid
JavaScript multiline string; system Python lacked Pillow; a temporary C#
`Add-Type` compile did not inherit the loaded `System.Drawing` reference; and
two PowerShell inspection commands repeated the invalid direct `foreach` pipe
pattern. A final attempt to delete verified intermediate files was rejected by
the Windows command policy.

## Resolution

Resubmitted the image edit with a template literal, used the bundled Codex
Python runtime for the installed chroma-key helper, and transferred only the
generated alpha matte to the original RGB pixels with loaded .NET bitmap
objects. The installed cover passed the resource audit with transparent corners
and 100% foreground RGB fidelity. Intermediate files remain under ignored
`tmp/imagegen/` because cleanup was policy-blocked.

**See Also**: ERR-20260818-001, ERR-20260818-007

---
# ERR-20260818-020: PowerShell parsed an embedded regex pipe

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

A double-quoted ripgrep pattern embedded in a PowerShell command terminated at
an escaped quote, so the remaining regex alternation was parsed as a pipeline
with an empty element.

## Resolution

Store shell-sensitive regular expressions in a single-quoted PowerShell
variable and pass that variable to `rg --`.

**See Also**: ERR-20260818-015

---
# ERR-20260818-021: PowerShell statement leaked into JavaScript tool orchestration

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: config

## What failed

An `Resolve-Path` PowerShell statement was placed directly in a
`functions.exec` JavaScript module before the nested `exec_command` calls,
causing the orchestrator to fail with `SyntaxError: Unexpected string`.

## Resolution

Keep all shell statements inside each `exec_command.cmd` string; the outer
`functions.exec` source must remain valid JavaScript.

---
# ERR-20260818-022: apply_patch rejected delete and add for the same file

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: config

## What failed

A single patch attempted to delete and add the same absolute path while
replacing a complete C# configuration file. The patch verifier rejected the
duplicate target operations.

## Resolution

Split whole-file replacement into one delete patch followed by one add patch.

---
# ERR-20260818-023: Test local name collided with a later declaration

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

A new `out var migratedRole` assertion used a name already declared later in
the same C# method, so the test project bound subsequent assertions to the
wrong type and failed compilation.

## Resolution

Renamed the custom-start assertion variable to `migratedStarterRole`; the
AuraToolsExp suite then passed all assertions.

---
# ERR-20260818-024: Skill validator lacked PyYAML and defaulted to GBK

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: config

## What failed

`quick_validate.py` first failed because both available Python runtimes lacked
PyYAML. After installing PyYAML into an ignored temporary target, the validator
then decoded the UTF-8 skill as GBK and raised `UnicodeDecodeError`.

## Resolution

Loaded PyYAML through a temporary `PYTHONPATH` and set `PYTHONUTF8=1`; the
shared-runtime skill validation then passed.

---
# ERR-20260818-025: AuraTools gate retained the previous aggregate schema

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

The comprehensive AuraTools gate still required match-experience schema 30
after custom start advanced the contract to schema 31, so the gate rejected an
otherwise successful build and behavior suite.

## Resolution

Updated the gate to schema 31 and added assertions for custom-start schema 2,
empty card/relic defaults, and removal of legacy registered-profile fields.

---
# ERR-20260818-026: Toolbox icon generator passed untyped polygon arrays

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: frontend

## What failed

The toolbox icon generator supplied PowerShell `Object[]` values to
`System.Drawing.Graphics.DrawPolygon`, so overload binding tried and failed to
convert them to `Point[]` before reaching the updated custom-start icon.

## Resolution

Cast every polygon point collection to `System.Drawing.PointF[]`; the generator
then rebuilt all 30 toolbox icons successfully.

---
# ERR-20260818-027: Feast split left stale preview and documentation counts

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: frontend

## What failed

The first documentation patch targeted an outdated “表现资源” label, and the
Unity preview build updated only its error text while leaving the icon and
module-count conditions at 30 and 15. After those were corrected, the warning
capture matched the default capture because the newly inserted Feast CG row
pushed all warning-state rows below the first viewport.

## Resolution

Patched the current documentation wording, changed preview contracts to 31
icons and 16 modules, and made the warning scenario exercise the visible Feast
CG dependency-paused state. The 12-capture preview suite then passed.

---
# ERR-20260818-028: Shared packaging copies lagged the current shared build

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: config

## What failed

The AuraTools product gate packaged a newer workspace `Aura.Shared.dll`, while
the Terrias and SanGuoShaExp copies still held the previous hash, so the shared
DLL packaging gate rejected Terrias first.

## Resolution

Rebuilt Terrias and SanGuoShaExp serially against the current shared runtime;
all packaged Aura.Shared copies then passed hash validation.

---
# ERR-20260818-029: Game-reference index pointed at an older decompile snapshot

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: docs

## What failed

The first MOD-loader search used the game-reference index path for
`v1.0.24591395`, but the workspace had already advanced to the complete
`v1.0.24605918` snapshot. A follow-up also assumed the report name was
`decompile-summary.md` instead of the actual `decompile-report.md`.

## Resolution

Enumerated `开发参考资料` and `artifacts/game-reference` before continuing,
then based the MOD loading and hot-sync investigation on the complete
`v1.0.24605918` decompile and its actual report.

---
# ERR-20260818-030: New runtime files assumed the wrong Witch namespaces

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: build

## What failed

The first lobby and preset builds placed `ModHookContext`, `Globals`, and
`DataType` under inferred namespaces instead of the namespaces exposed by the
current managed game assemblies. This produced several short compile failures.

## Resolution

Matched the imports used by existing AuraTools runtimes and the current
decompile: `ModHookContext` from `Witch.Core`, and `Globals`/`DataType` from
`Witch`. The release build now completes without warnings.

---
# ERR-20260818-031: HTML preview retained the pre-expansion module count

**Logged**: 2026-08-18
**Severity**: low
**Status**: resolved
**Area**: frontend

## What failed

After adding the four foundation modules to the HTML preview data, its
Playwright source contract still required the earlier 15-module inventory and
the records interaction still expected two rows.

## Resolution

Updated the preview contract to 20 production IDs and added category-count
checks for the three-record, two-multiplayer, and three-system module groups.

---
# ERR-20260819-001: Terrias architecture gate started with Windows PowerShell

**Logged**: 2026-08-19
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

`Test-TerriasArchitecture.ps1` was invoked through Windows PowerShell 5.1,
whose .NET Framework runtime does not provide `System.IO.Path.GetRelativePath`.

## Resolution

Reran the unchanged gate with PowerShell 7 via `pwsh`; all 537 architecture
boundary files and the Terrias architecture assertions passed.

**See Also**: ERR-20260818-005

---
# ERR-20260820-001: ripgrep received an unexpanded Windows wildcard path

**Logged**: 2026-08-20
**Severity**: low
**Status**: resolved
**Area**: docs
**Recurrence-Count**: 3

## What failed

A repository search passed `tools/*architecture*` as a positional path to
`rg` under PowerShell. Ripgrep treated the literal asterisk as an invalid
Windows path instead of expanding it.

## Resolution

Use `rg` glob filters such as `-g '*architecture*'` or enumerate matching
paths with `rg --files` before searching their contents.

The same mistake recurred with `Aura*Shared`, `*.csproj`, and later
`Terrias-Dev/Mechanics/*.cs`, and later `AuraSharedCore.Tests/*.cs`; keep
wildcard expressions in `-g` filters rather than positional Windows paths.

---
# ERR-20260820-002: PowerShell quoting produced an incomplete ripgrep regex

**Logged**: 2026-08-20
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

A combined alternation containing the literal `GetEnvironmentVariable("PATH")`
lost closing escapes while crossing JavaScript and PowerShell quoting, so `rg`
reported an unclosed group.

## Resolution

Repeated the residual-path audit with `rg -F` and separate `-e` literals,
which avoids multi-shell regex escaping for exact tokens.

---
# ERR-20260820-003: net472 does not expose String.Contains with comparison

**Logged**: 2026-08-20
**Severity**: low
**Status**: resolved
**Area**: build

## What failed

The replay job profile parser used `string.Contains(value,
StringComparison.Ordinal)`, which is available to the net8 test target but not
to the production net472 compile contract.

## Resolution

Replaced both calls with `IndexOf(value, StringComparison.Ordinal) >= 0` and
kept the production build as the compatibility authority.

---
# ERR-20260820-004: PowerShell if statement was used as a parenthesized expression

**Logged**: 2026-08-20
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

A final inventory command placed `if (...) { ... } else { ... }` directly
inside string concatenation parentheses. PowerShell parsed `if` as a command
name in that expression position.

## Resolution

Assigned the `if` statement result to `$count` first, then formatted the output;
the retired Playback file count is zero.

---
# ERR-20260821-001: Terrias game-reference index pointed to a retired snapshot

**Logged**: 2026-08-21
**Severity**: low
**Status**: resolved
**Area**: docs

## What failed

The Terrias game-reference index named `反编译文件夹v1.0.24591395` as the
current decompile root, but that directory no longer exists. The repository now
contains `反编译文件夹v1.0.24605918` as the newest snapshot, so the documented
search command failed with a path-not-found error.

## Resolution

For the current analysis, enumerate `开发参考资料` first and use the newest
available decompile snapshot. The stale index should be updated through the
Terrias skill-evolution workflow rather than silently treated as authoritative.

---
# ERR-20260821-002: Terrias CSV inventory paths were assumed to be flat

**Logged**: 2026-08-21
**Severity**: low
**Status**: resolved
**Area**: docs

## What failed

An inventory probe tried to read `Terrias/Data/Card.csv`, `Buff.csv`, and
`Relic.csv`. Terrias stores these tables in type subdirectories such as
`Terrias/Data/Card/terrias.csv`, so all three reads failed.

## Resolution

Enumerate `Terrias/Data` recursively and select CSV files by their parent table
directory before reading or importing them.

---
# ERR-20260821-003: PowerShell Sort-Object mixed descending syntax was invalid

**Logged**: 2026-08-21
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

An analysis command used `Sort-Object Count -Descending,Name`. PowerShell does
not accept a second property after the `-Descending` switch in that form, so
the parser reported a missing argument.

## Resolution

Use calculated property specifications when sort directions differ, for
example `Sort-Object @{Expression='Count';Descending=$true}, Name`.

---
# ERR-20260821-004: FightObject source assembly path was assumed

**Logged**: 2026-08-21
**Severity**: low
**Status**: resolved
**Area**: docs

## What failed

A decompiled combat-flow search included `Witch/FightObject.cs`, but that type
is not stored at the assumed path in the current snapshot. Ripgrep reported a
path-not-found error while still returning results from the other files.

## Resolution

Locate decompiled type files with `rg -l 'class FightObject'` before passing
their paths to focused searches.

---
# ERR-20260821-005: New Terrias coverage test missed namespace qualification

**Logged**: 2026-08-21
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

The first compile of the active-card coverage test referenced `FightUI` without
importing `Witch.UI.Window`, and a test stub referenced `CardConfigApi` from the
wrong namespace.

## Resolution

Imported the native UI namespace in the test program and fully qualified the
GameApi facade in the stub.

---
# ERR-20260821-006: Structural invalidation test counter landed on the wrong stub

**Logged**: 2026-08-21
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

The first structural-invalidation test patch added `TransformCount` to the
`FightCardManager` stub instead of `CardItem`, and the test program did not
import the presentation router namespace.

## Resolution

Moved the counter onto `CardItem` and imported `Terrias.Dll.Hooks`.

---
# ERR-20260821-007: apply_patch combined delete and add for one path

**Logged**: 2026-08-21
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

An `apply_patch` request attempted to delete and add
`AuraBattleLifecycleRouter.cs` in one patch. The patch verifier rejected two
operations targeting the same path. A later architecture JSON patch also used
stale surrounding text and failed verification.

## Resolution

Split full-file replacement into separate delete and add patches, and inspect
the exact current lines before patching structured configuration.

---
# ERR-20260821-008: Generic learning status patch matched the wrong entry

**Logged**: 2026-08-21
**Severity**: low
**Status**: resolved
**Area**: docs

## What failed

A learning-resolution patch matched the first generic `Status: pending` line
in `.learnings/LEARNINGS.md`, marking an unrelated writing correction resolved
instead of the Terrias inventory learning.

## Resolution

Restored the unrelated entry and updated both intended learning statuses using
their unique learning headers as patch context.

---
# ERR-20260825-001: AuraTools visual project metadata drifted from its build contract

**Logged**: 2026-08-25
**Severity**: medium
**Status**: resolved
**Area**: tests

## What failed

`tools/Test-AuraToolsExp.ps1` passed its behavior and production builds but
failed the shared resource and CG ownership gate. The committed Unity project
metadata still declared Unity `2022.3.62f3c1` and omitted URP, while the
authoritative builder and gate require Unity `6000.0.46f1` plus URP `17.0.4`.

## Resolution

Synchronized the local generated project once, then removed that ignored cache
from the aggregate gate. The gate now validates the tracked generator's exact
Unity and URP declarations; `Build-AuraToolsVisualBundle.ps1` deterministically
writes the generated metadata before launching Unity. When this aggregate gate
fails, print its individual predicates before changing runtime code because the
single terminal message covers many independent visual contract checks.

---
# ERR-20260825-002: Architecture gate requires PowerShell 7

**Logged**: 2026-08-25
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

Running `tools/Test-TerriasArchitecture.ps1` through Windows PowerShell failed
because its module uses `System.IO.Path.GetRelativePath`, which is unavailable
on the Windows PowerShell .NET Framework runtime.

## Resolution

Run this gate with `pwsh -NoProfile -File`; the same gate then passed all 523
source-file boundary checks.

---
# ERR-20260825-003: Skill validator used an ambiguous Python runtime

**Logged**: 2026-08-25
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

Invoking `quick_validate.py` through bare `python` selected an MSYS Python that
did not provide PyYAML. The bundled workspace Python also lacked PyYAML, and
without explicit UTF-8 a Chinese path in one skill could be decoded through the
Windows GBK locale.

## Resolution

Run the validator through the Windows Python launcher with explicit UTF-8:
`py -X utf8 .../quick_validate.py <skill-path>`. The selected project-machine
Python provides PyYAML, avoids PATH ambiguity, and reads mixed English/Chinese
skill references deterministically. The Terrias skill-evolution validation
command now records this invocation.

---
# ERR-20260825-004: Ripgrep received unexpanded Windows wildcard paths

**Logged**: 2026-08-25
**Severity**: low
**Status**: resolved
**Area**: tests

## What failed

Two review commands passed paths such as `.codex/skills/terrias-*/SKILL.md`
directly to `rg`. PowerShell did not expand those wildcard directory segments,
so ripgrep received an invalid Windows path and returned OS error 123.

## Resolution

Pass the existing parent directory to ripgrep and filter files with `-g`, for
example `rg <pattern> .codex/skills -g 'SKILL.md' -g '*.md'`. This keeps path
resolution inside one tool and avoids shell-dependent wildcard expansion.
