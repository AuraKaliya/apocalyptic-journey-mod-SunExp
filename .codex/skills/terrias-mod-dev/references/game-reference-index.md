# Game Reference Index

Use this reference before searching the decompiled game project under
`开发参考资料`. It is an index, not an implementation source. Verify current
compile signatures against repository `Managed/` assemblies before shipping C#
changes.

## Current Decompile Snapshot

- Current root: `开发参考资料\反编译文件夹v1.0.24831968`
- Status: complete local decompile snapshot as of 2026-08-25. All 253
  assemblies, including `Live2D.Cubism.dll`, were frozen and decompiled
  successfully with `ilspycmd 9.1.0.7988`. Input fingerprints and per-assembly
  results are recorded under
  `artifacts/game-reference/1.0.24831968/`. The focused Witch/Witch.Core API
  comparison against `1.0.24605918` reports no breaking or additive API drift;
  both assemblies require behavior review because their binaries changed.
- Use for: game flow, official script shape, UI manager behavior, map/event
  generation, card use flow, event listener APIs, Mirror/network types, and
  comparable official implementation patterns.
- Do not use for: copying large code blocks, overriding the current repository
  architecture, or replacing `Managed/` as the compile contract.

If a future decompile folder appears, prefer the newest folder whose version
matches the game build being investigated. Move old folder notes to
`terrias-skill-evolution/references/stale-anchor-registry.md`.

## High-Frequency Search Routes

Script executor and official CSV script shape:

```powershell
rg -n "ScriptExecutor|RunImmediately|AddBuff|AddDescription" "开发参考资料\反编译文件夹v1.0.24831968\AllScripts"
rg -n "class ScriptExecutor|class VisualScriptExecutor|IScriptExecutor" "开发参考资料\反编译文件夹v1.0.24831968\Witch" "开发参考资料\反编译文件夹v1.0.24831968\Witch.Core"
```

Native Partner ownership and per-target local/remote execution:

```powershell
rg -n "RegisterPartnerOwner|RoleStatusMap" "开发参考资料\反编译文件夹v1.0.24831968\Witch\PatternManager.cs" "开发参考资料\反编译文件夹v1.0.24831968\Witch\ScriptExecutor.cs"
rg -n "ForEachObject|TrySendOnlineEvent" "开发参考资料\反编译文件夹v1.0.24831968\Witch.Core\ForEachObject.cs" "开发参考资料\反编译文件夹v1.0.24831968\Witch\ScriptExecutor.cs"
```

Interpret return values from their callers and downstream branches; a method
named `Try*` may report that work was routed or sent rather than locally
applied. For synthetic objects, load the native synthetic runtime object
reference through `terrias-architecture-dev` before changing executor flags or
adding a parallel effect channel.

Card use, card UI, and action flow:

```powershell
rg -n "class CommonCardItem|class AttackCardItem|TrueUse|RunScript|ActionAfter" "开发参考资料\反编译文件夹v1.0.24831968\AllScripts" "开发参考资料\反编译文件夹v1.0.24831968\Witch"
```

EventList fields and official option scripts:

```powershell
rg -n "EventList|Choice1|Choice2|EndEvent|ContinueEvent|InitScript|EntryScript" "开发参考资料\反编译文件夹v1.0.24831968\AllScripts" "开发参考资料\反编译文件夹v1.0.24831968\Witch"
```

Map generation, map selection, and visible map nodes:

```powershell
rg -n "MapSelectUI|NormalMapManager|MapManager|SelectNode|TypeGenerate|RandomGenerate|NodeDice" "开发参考资料\反编译文件夹v1.0.24831968"
```

Event listener registration and cleanup:

```powershell
rg -n "AddEventListener|RemoveEventListener|EventCenter|EventDispose|EventListener" "开发参考资料\反编译文件夹v1.0.24831968"
```

UI overlay, raycast, and transition behavior:

```powershell
rg -n "CanvasGroup|GraphicRaycaster|raycastTarget|upperCanvasTf|GraphicRegistry|SetActive" "开发参考资料\反编译文件夹v1.0.24831968\Witch" "开发参考资料\反编译文件夹v1.0.24831968\Assembly-CSharp"
```

Mirror and network shape:

```powershell
rg -n "NetworkBehaviour|Command|ClientRpc|TargetRpc|NetworkWriter|NetworkReader|OnSerialize|OnDeserialize" "开发参考资料\反编译文件夹v1.0.24831968\Mirror" "开发参考资料\反编译文件夹v1.0.24831968\Witch"
```

Live2D model, motion, rendering, and parameter APIs:

```powershell
rg -n "CubismModel|CubismMotion|CubismRenderer|CubismParameter" "开发参考资料\反编译文件夹v1.0.24831968\Live2D.Cubism"
```

## Versioned Correction Notes

When the decompiled snapshot disagrees with current `Managed/` or current game
behavior:

1. Prefer `Managed/` for compile signatures.
2. Add or update a focused `GameApi` compatibility wrapper if older signatures
   must remain supported.
3. Record the mismatch with:
   - decompile root version;
   - `Managed/` assembly or game build checked;
   - class/method searched;
   - current project rule or fallback;
   - removal condition once a newer decompile confirms the behavior.
4. Keep correction notes in this index only while they affect current work. Move
   obsolete corrections to the skill-evolution stale anchor registry or delete
   them after distillation.

## External References

For Unity or mature-project best-practice routes, load
`references/external-best-practice-index.md`. External references are idea
sources only; final implementation must follow this repository's shared/core,
content-mod, and tool-mod boundaries.
