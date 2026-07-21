# Stale Anchor Registry

Use this reference only while evolving project-local skills. Do not route normal
Terrias, AuraToolsExp, shared-runtime, event, or visual work from this file.

## Purpose

Keep historical anchors here so operational skills stay clean:

- retired repository roots;
- retired data-only workflows;
- renamed or removed mode names;
- removed event, map, card, or balance-session ids;
- old decompile folder versions;
- versioned corrections where a decompiled snapshot disagreed with the current
  game, current `Managed/` assemblies, or current repository code.

## Current Stale Anchors

- Retired standalone data-only Terrias root: `D:\workfile\project\Mod_1\Terrias`.
- Retired pure data workflow anchors: `pure-data`, `cardpack burst`,
  `burst-balance`.
- Retired balance-session names: `Solar Radiance`, `Gathered Flame`,
  `Crown Manifestation`.
- Retired mode name: `TongtianTower` / `通天塔`.
- Old decompile folder version: `开发参考资料\反编译文件夹v1.0.23693118`.
- Removed event-id families that may still be guarded by validation scripts:
  `wuna_event_*`, `Sub_wuna_event_*`, `Sub_solar_finale_*`,
  `Sub_solar_memory_start`.

## Correction Packet

When a decompiled reference is wrong or outdated, record the correction here or
in `terrias-mod-dev/references/game-reference-index.md` with:

- decompile folder version;
- game or `Managed/` version checked;
- searched class/method or data row;
- observed mismatch;
- current project rule;
- removal condition, such as "delete after the next decompile version confirms
  the new behavior".
