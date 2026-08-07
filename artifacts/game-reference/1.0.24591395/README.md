# Game Reference v1.0.24591395

This is the complete local reference run for Steam build `24591395` and Unity
`6000.0.46f1`.

## Local Result

- Repository `Managed/`: 253 assemblies.
- Frozen snapshot: `开发参考资料/Managed快照/v1.0.24591395`.
- Decompiled projects: 253 succeeded, 0 failed.
- Decompiled root: `开发参考资料/反编译文件夹v1.0.24591395`.
- Decompiled output: 253 projects, 28,843 files, 140,316,281 bytes.
- C# source files: 28,009.
- Tool: `ilspycmd 9.1.0.7988`.

`Live2D.Cubism.dll` is included with SHA-256
`8F86029AF6A6D60A988654EAEBECA44B020882963E0970D3F1D7C3A5AD1443E1`
and MVID `9fde22d7-a68a-44ad-a80a-c1564fe8d2cd`. It produced 153 files from
226 metadata types, including 197 public types.

The full assembly table is in `managed-assemblies.md` and
`managed-assemblies.csv`. The machine-readable fingerprints are in
`managed.manifest.json`; per-assembly decompile results are in
`decompile.manifest.json`.

## Recovered Baseline

The old `AllScripts.dll`, `Witch.dll`, and `Witch.Core.dll` were recovered from
test output before subsequent builds could replace them. They are archived as
the partial snapshot `开发参考资料/Managed快照/v1.0.23816797.partial`.

The public metadata comparison for these three assemblies reports 38 breaking
candidates, 552 additive candidates, and 3 behavior-review assemblies. These
are review candidates rather than a claim that every entry affects a mod.

## Validation On This Machine

- Managed decompile gate: passed for all 253 assemblies, including the Live2D
  high-frequency source check.
- Terrias build against current `Managed/`: passed with 0 warnings and 0 errors.
- Terrias architecture and C# tests: passed.
- Aura.Shared compatibility gate: passed with 1,721 public API entries.
- Network RPC authority tests: passed.
- AuraToolsExp tests: passed with 724 assertions.
- Aura combat knowledge compiler contract: passed using the complete decompile
  source fixture; the package and reports were regenerated from source hash
  `1e4859af3d987bccb1019d85619dbeb9c1e0c23379275c4ebd5e48b0b94906f2`.
- AuraDirector current-build probe: failed closed at the Witch hash allowlist,
  as expected. See `ready-to-start-review.md`.

The earlier `.partial` snapshot and decompile directories remain as historical
local artifacts. Operational references now point to the complete unsuffixed
roots.
