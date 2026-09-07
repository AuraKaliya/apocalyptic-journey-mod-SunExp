# Modules and UI

## Current source and contracts

- [Tool feature source](../../../../AuraToolsExp-Dev/Features)
- [Module/settings architecture](../../../../docs/AuraToolsExp/toolbox-settings-and-module-architecture-design.md)
- [Presets, health, lobby and archives](../../../../docs/AuraToolsExp/foundation-modules.md)
- [Unity preview workflow](../../../../docs/AuraToolsExp/toolbox-unity-preview-player.md)
- [Version compatibility](../../../../docs/AuraToolsExp/version-compatibility-contract.md)
- [Unified CG](../../../../docs/AuraToolsExp/unified-cg-system-contract.md)

Search the active module identity across production definitions, codecs, icons,
page factories and preview inventory. A new category/page is incomplete if the
real toolbox cannot select it or the preview substitutes a different catalog.

## Settings transactions

Follow the owning module's persistence and change-notification model. Validate
an import before writing; batch notifications during a multi-module transaction
and restore touched modules in reverse order on failure. Presets contain
settings and logical resource references, not media, datasets, databases or
secrets. Preserve unknown-module/version handling according to the current codec.

Custom-start loadouts require the declared native world-simulation run identity;
the presence of a particular map manager does not establish ownership of a run.
Content-defined modes retain their own setup and starter-deck contracts.

## UI acceptance

Verify selection, effective value, reset, close/reopen and module disable.
For pooled or asynchronous content also exercise replacement during loading,
listener cleanup, canceled work and next reuse. Check backdrop/content hit
testing and the current supported preview sizes. Use actual loaded-mod state
for discovery/health checks; do not instantiate or initialize arbitrary MODs
merely to produce a status indicator.
