# v1.1.25405741 compatibility review

Steam build: 25405741. The new Globals.VersionMinor is 1, so the runtime version
is v1.1.25405741. Repository Managed and installed core assemblies match.
The 253-assembly snapshot and complete decompilation are in
开发参考资料/Managed快照/v1.1.25405741 and 开发参考资料/反编译文件夹v1.1.25405741.
The integrity gate passed: 253 projects, 28,010 C# files, zero failures.

## Host changes

The complete 253-assembly API comparison (including Witch, Witch.Core and AllScripts) found no removed
contracts and 233 additive candidates. ResourceLoader, MapItem, NormalMapManager,
GameConfigManager and DataConfig retain their prior source semantics.

FightManager.ReadyToStart, its Rougamo wrapper and Mirror command retain their
hold/resume semantics. The wrapper IL hash changed with metadata tokens:
CEE9ED2F1011A646BB3F251764F1C4CEBD69A8AE10306BAC8887A9C8BDC3EFE9.
The director allowlist accepts this reviewed body alongside the previous one.
Unknown bodies remain rejected. Current-host tests require the feature to work.
Other FightManager changes refresh TopBarUI; RoleTable copying now includes
enchasedDict and IsStarted.

ScriptExecutor adds native popularity, ascension-seal, tagged-card and zero-cost
card operations. Tagged random creation reaches FightUI's existing card
materialization path, already observed by Terrias card-gain handling. Native
skill affordability now checks popularity. BuffBarUI corrects Partner/client
ownership and adds GetBuff; FightUI adds console/settings input guards and skill
description refresh. These native behaviors are retained.

## Recurrent third-layer map failure

Evidence: AuraTools-20260920-121253.log at 12:21:26.585; MapItem.Init throws an
array bounds exception. Witch MVID d5cb8a4b-1075-4acb-b85f-bc031c283a11 matches
the investigated assembly.

Native MapItem selects the enemy with the largest strictly positive HP, loads
Texture2D frames from Animation/Map, then indexes Animation/Idle[0] if necessary.
Native CustomLoadAll<T> accepts MOD PNG/JPG files only for exactly Texture or
Sprite, not Texture2D. Both third-layer custom bosses contain real Map and Idle
PNGs, but this native call returns empty arrays.

The old probe used TerriasResourceCache.LoadAll<Texture2D>. That shared adapter
loads Texture and casts it, so it reported success for a capability native
MapItem does not have. The preview override was consequently skipped. This
is a capability-check mismatch, not a missing node or dice migration.

The corrected probe calls the exact native generic loader through the resource
boundary and checks frame zero. Application owns fallback selection; GameApi
owns only host access. The fallback itself must pass the native probe. The
temporary change reaches the table row backing DataConfig's read-only view.
After-hook cleanup restores it before combat; existing next-frame and scene/menu
cleanup drain failed initializations. Duplicate/stale cleanup and external
changes have regression coverage. No fixed node identities, sync arrays or save
nodes are rewritten. Existing third-layer saves use the same corrected consumer.

Test-TerriasNativeMapPreview.ps1 executes the current native loader on both
shipped boss folders, reproduces the original frame-zero exception, then checks
the built product's probe. If the host later repairs Texture2D loading, that
regression requires explicit review. The 918-assertion Terrias behavior suite
passed after the preview change. Unity/game acceptance is recorded separately;
a native-loader test alone does not prove scene rendering or multiplayer flow.

## Resource replacement

AssetRipper 2.0.0 PrimaryContent exported 48,279 files. All 8,525 exported PNGs
passed structure checks. D:/MNRS was replaced only after verification; the old
directory is retained at D:/MNRS_work/25405741/previous-MNRS.
Seventeen official portrait PNGs were copied and hash-checked in
Mod的素材参考/角色立绘; existing MOD portraits were preserved.
The previous reference directory is backed up in
D:/MNRS_work/25405741/previous-character-reference.

Export warnings were limited to 14 Font Texture entries and one LiberationSans
SDF Atlas entry. Matching source texture records inspected have zero dimensions;
see asset-font-placeholders.json. Character PNGs passed validation.

## Feast CG coverage

The extracted Career table contains 18 identities. Eight new CGs complete
coverage with 16 images. career_4 shares career_2's Nana portrait, and career_13
shares career_12's Chaos Angel portrait. Both semantic IDs and resource aliases
declare the relationships. The coverage gate uses the role catalog matching the
current Witch DLL instead of the obsolete fixed CG count.
The built-in ImageGen tool generated these images; its API does not expose a
model selector. Exact prompts and selected outputs are in feast-cg-prompts.json.

Release versions: Terrias 0.6.1 and AuraToolsExp 0.12.1. Final validation,
deployment and game acceptance evidence is recorded in acceptance.md.
