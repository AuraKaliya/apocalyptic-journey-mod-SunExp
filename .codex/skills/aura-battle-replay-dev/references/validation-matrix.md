# Battle Replay Validation Matrix

Select checks by the highest-risk boundary changed. Run build/test commands
serially because product consumers share DLL outputs.

## Automated Layers

| Boundary | Required evidence |
| --- | --- |
| Pure journal/model | reducer, canonical JSON, roots, tamper rejection, checkpoints/seek, late presentation, durability watermark |
| Recorder routing | lifecycle hooks, transaction ownership, card/intent classification, deferred MOD obligations, terminal diagnostics |
| Module compatibility | same declared contract across different builds; reject owner/type/schema/portability/capability drift; preserve sealed bytes and roots |
| Native UI clone | exclude runtime-only owned subtrees before activation; retain normal graphics and source prefab; nested/inactive owners and reopening |
| Database/package | Recording→Finalizing→Ready, recovery, immutable roots, import budgets, migration/cutover, asset reachability |
| Playback core | event ordering, state commit, transient/persistent reconstruction, seek/speed/export clock parity |
| Renderer ownership | camera/target/renderer leases, Feature policy, deep-cloned Feature identity, intermediate color declaration, generation replacement, duplicate/foreign release, teardown |
| Managed compatibility | current Unity/URP members and current game `Managed/` signatures |
| Product | `tools\Test-AuraToolsExp.ps1` and warning-free Release build |
| Shared/provider | shared behavior/ABI/package gates when provider, module, lifecycle, or shared protocol changes |
| Terrias consumer | Terrias build, architecture, Spirit/Projection behavior, resources, and package checks when Terrias integration changes |

Source-text scans may enforce retired-path or ownership boundaries. They cannot
replace a behavior test for timing, pooling, rendering, or cleanup.

For the native UI clone boundary, run
`tools/Test-AuraToolsReplayNativeUi.ps1 -UnityPath <editor.exe>`.
This executes the production cloner in Unity PlayMode with isolated fixtures;
it complements the real-game rendering cases below.

For lit HUD regressions, supply `-GameDataDirectory <installed game _Data>` to
that runner. It requires Python dependencies from
`tools/requirements-replay-native-ui.txt` for read-only fixture extraction
and a real graphics device (not `-nographics`). The native health/defense
textures and original lit shader must reproduce the old black pixels, then
match the normal-camera reference pixel-for-pixel with the production ambient
feature. Fixtures and comparison images stay in the ignored Unity test tree.
The test receipt distinguishes an extracted-native run from synthetic fixtures.

Extension intent checks cover configured historical paths, resolved new paths,
separate icon/background fallbacks, missing/empty resources, wait visibility,
unknown schema/contract rejection, preflight of late events, and unchanged
sealed document bytes and roots.

Card-motion cases additionally cover held poses followed by movement, delayed
burn activation, concurrent physical views of one card, cancellation/return,
and measured versus legacy companion anchors across attack sprite changes.
Hand lifecycle tests execute the production acquisition/layout adapter in
Unity with native API and journal-sink fixtures: consecutive draws before any
use, nested DrawScript creation, immediate consumption, queue/full-hand cases,
and foreign-surface rejection. Playback tests run both the production moving
card and static hand projections through arrival/reflow and the final handoff.
Keep these distinct from full-game acceptance with actual native callbacks.
Hash-work deferral must produce identical sealed documents; stale prepared
diffs and unadvertised new document capabilities must be rejected.

Use the test executable's `--benchmark-replay <assembled document.json>
<result.json>` mode for the deterministic C# recording workload. Compare the
same input and final state hash. Report its allocation/time results separately
from Unity frame time, native initialization, GC pauses and real-game FPS.

## Required Unity/Game Runtime Cases

For renderer or native-prefab changes, automated .NET tests are insufficient.
Exercise the actual game renderer profile with RenderGraph in its shipped mode:

1. create the replay host from the normal menu state;
2. render the first offscreen frame with every retained renderer feature active;
3. verify no Unity/Command exception and validate non-empty/non-black pixels;
4. survive at least one normal game render frame before display activation;
5. seek backward/forward and change speed;
6. acquire/release an export target and render at another size;
7. close replay, reopen it, then enter another battle; and
8. verify native renderer arrays/features and main camera state are unchanged.

Record the renderer-data type, feature types and relevant configuration, camera
flags, target descriptor, RenderGraph mode, camera id, renderer slot, and cleanup
result. A preview project with no matching game feature stack is not sufficient.

## Cross-MOD Acceptance

Use a newly recorded battle after writer changes. At minimum cover:

- ordinary player Card use, discard and burn;
- native EnemyCard and PartnerCard intents;
- Terrias Spirit spawn, resolved intent, action focus, HP/BUFF, and despawn;
- Terrias Projection spawn, intent, card action, HP/BUFF, and despawn;
- one owner-attached/provider-required visual and one portable extension;
- hand layout, card frame/art/text/cost, native FightUI and colored HP bars;
- playback speed, seeking, MP4 export, exit, reopen, and the next battle.

Compare semantic anchors, not only screenshots at arbitrary wall-clock times.
The new log must have no capture diagnostics, observer failures, RenderGraph
errors, resource/module rejection, or teardown residue.

## Release and Deployment

After the relevant matrices pass:

- rebuild the canonical shared runtime and both production consumers when shared
  code changed;
- verify packaged `Aura.Shared.dll` hashes are identical;
- compare repository package DLL hashes with the exact game installation paths;
- stop the game before replacing loaded binaries;
- retain old records unless deletion was explicitly requested; and
- state whether existing sealed records remain playable, require migration, or
  require re-recording.
