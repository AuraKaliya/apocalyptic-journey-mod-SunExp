# Project Map

Paths below are repository-relative. Discover files with rg before assuming a
class has its own project or a directory name is a product.

| Surface | Source and authoritative entry |
| --- | --- |
| Product classification | [Consumer manifest](../../../../tools/shared-consumers.json) |
| Terrias delivery and implementation | Terrias and Terrias-Dev; [technical index](../../../../docs/Terrias/README.md) |
| Tool delivery and implementation | AuraToolsExp and AuraToolsExp-Dev; [module contract](../../../../docs/AuraToolsExp/toolbox-settings-and-module-architecture-design.md) |
| Shared assembly | [Aura.Shared project](../../../../AuraSharedRuntime-Dev/Aura.Shared.csproj) lists compiled domains |
| Shared Core | [Core contract](../../../../docs/aura-shared-core-v2-contract.md) and AuraSharedCore |
| AI, training and simulation | [AI document index](../../../../docs/AuraCombatAI/README.md) distinguishes active, training and planned work |
| Prototype archive | [TestMods policy](../../../../TestMods/README.md) |
| Current runtime signatures | Managed; [game reference workflow](../../terrias-mod-dev/references/game-reference-index.md) |

## Terrias feature navigation

Use the content skill for these features, adding architecture or shared
references only when the affected responsibility requires them.

- Spirit collection, party, growth, inventory and artifacts:
  [Spirit module](../../../../docs/Terrias/modules/08-精灵球捕获与精灵召唤.md),
  [maintenance rules](../../../../docs/Terrias/design/08-精灵养成能力维护表.md),
  and Terrias-Dev/Application/SpiritArtifactApplicationService.cs.
- Projection, Spirit native identity and status routing:
  [Partner flow](../../../../docs/Terrias/modules/11-投影精灵与心变的Partner战斗流程.md)
  and [synthetic-object contract](../../terrias-architecture-dev/references/native-synthetic-runtime-objects.md).
- Endless Sea and Endless Abyss:
  [map loop](../../../../docs/Terrias/modules/06-无尽之海模式与地图循环.md),
  [pressure and rewards](../../../../docs/Terrias/modules/07-无尽深渊压力与奖励体系.md),
  and [Application/Network decision](../../../../docs/Terrias/13-架构决策与迁移门禁.md).
- Role mechanics and current content:
  [Terrias module index](../../../../docs/Terrias/README.md).

The architecture exception ledger is retained debt, not proof of a finished
cutover. A task that changes an exception's owning boundary must reconcile it;
unrelated skill/content tasks do not implicitly take ownership of all debt.
