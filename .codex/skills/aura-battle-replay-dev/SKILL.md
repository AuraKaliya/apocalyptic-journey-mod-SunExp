---
name: aura-battle-replay-dev
description: Project-local skill for diagnosing, designing, implementing, reviewing, or validating AuraToolsExp 对局记录/战斗回放, including recording rejection, sealed replay documents, cross-MOD state and presentation, native card/intent rendering, pooled visual lifecycles, isolated FightUI playback, URP/RenderGraph crashes, seeking, export, migration, and real-game acceptance. Do not use for combat-AI training replay datasets.
---

# Aura Battle Replay Dev

Use this skill for the AuraToolsExp `MatchRecords` structured battle replay.
The product contract is recorded visible data plus deterministic re-enactment;
playback must not execute gameplay scripts, AI, rewards, save mutations, RPCs,
or a second native battle.

## Route Before Acting

- For a defect fix, migration, compatibility change, or retired path, apply
  `terrias-complete-solution-gate` before designing the repair.
- For owner-qualified state/presentation providers or shared lifecycle changes,
  use `terrias-shared-runtime-dev`.
- For URP, shaders, materials, card visuals, FightUI projection, or first-frame
  rendering, use `terrias-visual-runtime-dev`.
- For synthetic Partner/Status identity or native manager/queue ownership, use
  `terrias-architecture-dev`.

Do not move Terrias content semantics into AuraToolsExp or Aura shared/core.

## Diagnostic Workflow

1. Build a stage matrix before naming the cause. Read
   [references/diagnostic-playbook.md](references/diagnostic-playbook.md) and run
   `scripts/summarize-replay-log.ps1` for supplied logs.
2. Verify the loaded product and shared DLL hashes when a prior build may still
   be installed. A source fix is not runtime evidence.
3. Find the first incorrect owner or contract, not the last warning. Separate
   recording, finalization, resource/module preflight, render-host preflight,
   active playback, and teardown.
4. Inspect the newest applicable decompile call chain and current `Managed/`
   signatures. Discover the current decompile root; never anchor the skill to an
   old snapshot version.
5. Classify the failure against
   [references/replay-invariants.md](references/replay-invariants.md). Reject
   fixes based on timeouts, swallowed exceptions, guessed assets/types, or a
   second writer/player.
6. Choose tests from
   [references/validation-matrix.md](references/validation-matrix.md). Unity
   rendering and real cross-MOD presentation require real runtime evidence.

## Hard Rules

- `Ready` proves that recording/finalization sealed a valid document. It does
  not prove that the current machine can pass module, resource, URP, pixel, or
  active-playback gates.
- Use the earliest exception in the failed stage. Pixel-empty rejection,
  reporter failures, and teardown errors after a render exception are cascades
  unless independent evidence proves otherwise.
- Reflection/member-existence checks and source-text assertions prove shape,
  not URP runtime compatibility. A pixel readback performed after
  `Camera.Render()` cannot contain a RenderGraph crash that already occurred.
- Never preserve or delete every `ScriptableRendererFeature` by blanket rule.
  Inventory each feature's camera, intermediate color/depth, injection-point,
  and RenderGraph assumptions; keep native renderer state untouched.
- Cloning RendererData does not prove Feature ownership. Retained Feature
  ScriptableObjects must be replay-owned clones; never let two renderers call
  `Create` or `Dispose` on the same Feature instance.
- Truth time is monotonic state authority. Presentation observations may arrive
  late but retain observed time. Persist only the immutable durability prefix;
  never rewrite already durable events.
- Canonicalize extension JSON at the shared publish boundary and validate it
  again at document sealing. Preserve array order and reject duplicate keys.
- Presentation lifetime is not C# object lifetime. For pooled views, close the
  exact root/source/generation on authoritative reset, inactive, destruction,
  or rebind; watchdog expiry is a defect signal, not normal completion.
- Classify native action sources by authoritative `IDataConfig.Type`.
  `Card` uses card descriptors; `EnemyCard` and `PartnerCard` use intent
  descriptors. Never infer type from MOD id, content id, icon path, or name.
- A sealed document is immutable. Repair the writer and re-record unless a
  bounded, lossless migration can reconstruct every field and root hash.
- Do not claim completion from core tests or a successful product build alone.
  The highest-risk boundary changed determines the required runtime matrix.

## Completion Evidence

For implementation work, report separately:

- recording/finalization result and diagnostics;
- resource/module preflight result;
- render-host preflight, normal-frame barrier, activation, and teardown;
- cross-MOD entity/state/presentation coverage;
- automated gates and real-game cases actually executed;
- packaged versus installed DLL hashes;
- retained-record migration or explicit immutable-record disposition.
