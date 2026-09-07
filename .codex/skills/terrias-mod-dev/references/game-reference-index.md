# Game Reference Workflow

Use local decompilation to understand host control flow. Repository Managed
assemblies are the current compilation contract; installed game assemblies
determine the runtime being diagnosed.

## Discover the applicable snapshot

```powershell
tools/Get-AuraProjectContext.ps1
```

The decompileCandidates field lists immediate versioned folders under
开发参考资料. Select the snapshot matching the investigated game/Managed
fingerprints. A numerically newer folder alone is not proof of a match.

Inspect artifacts/game-reference and 开发参考资料/Managed快照 for input
fingerprints and decompile results. Existing tools include
`tools/Export-GameManagedSnapshot.ps1`,
`tools/Decompile-GameManaged.ps1`, and
`tools/Compare-GameManagedApi.ps1`; inspect their parameters before generating
or comparing a snapshot. Reference export/decompilation is explicit work, not
an automatic prerequisite for every content edit.

If no matching snapshot exists, separate what current Managed signatures prove
from behavior inferred from an older snapshot. Obtain the needed runtime or
decompile evidence before relying on that inference for a native fix.

## Search by behavior

Run rg against the selected directory rather than a remembered version path.

| Boundary | Useful search terms |
| --- | --- |
| Script execution/locality | ScriptExecutor, ForEachObject, TrySendOnlineEvent, RoleStatusMap |
| Battle lifecycle | Fight_Start, Fight_PlayerTurn, StartRound, FightEnd, EventCenter |
| Card and intent presentation | CardUI, EnemyCard, PartnerCard, FightUI |
| Maps/events | MapSelectUI, NormalMapManager, NodeDice, TypeGenerate |
| Listener lifecycle | AddEventListener, RemoveEventListener, EventDispose |
| UI/input/transition | CanvasGroup, GraphicRaycaster, raycastTarget, GraphicRegistry |
| Multiplayer | NetworkBehaviour, Command, ClientRpc, NetworkWriter |
| Live2D | CubismModel, CubismMotion, CubismRenderer |

Read the complete relevant call chain and return-value semantics. Current
public signatures may match while behavior has changed. Avoid copying large
decompiled implementations or overriding repository architecture with native
placement patterns.

## Corrections

Record the checked assembly/snapshot, symbol, observed mismatch, consequence
and revalidation/removal condition with the incident evidence. Promote only
the durable invariant into a skill. Do not store current protocol versions or
host-specific findings in multiple skill bodies.

For focused upstream research, use
[external sources](external-best-practice-index.md). Historical repository and
snapshot anchors belong in the
[evolution registry](../../aura-skill-evolution/references/stale-anchor-registry.md).
