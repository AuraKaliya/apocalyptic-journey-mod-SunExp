# Runtime Arbiters and Extension Points

This workspace has several runtime extension layers that are not represented by ordinary CSV tables. Treat these as shared services: initialize them from a MOD entry point, then register providers or manifest entries.

## Sound effects

Source files:

- `AudioArbiterShared/AudioArbiterRuntime.cs`
- `SunExp-Dev/GameApi/AudioApi.cs`
- `SunExp/audio.registry.json`
- `CardUseCialloExp-Dev/Hooks/CardUseSoundRuntime.cs`

`AudioArbiterRuntime` creates one global Unity object named `AudioArbiter.Global`. Mods either register a provider object directly with `RegisterSoundProvider(...)` or load a manifest with `RegisterManifest(...)`.

Current manifest fields used by SunExp:

- `schemaVersion`, `ownerModId`
- `audioProtocol.minVersion`, `audioProtocol.preferredVersion`
- `defaults.sync`, `defaults.hardClaim`, `defaults.cooldownSeconds`, `defaults.gainDb`, `defaults.volumeMultiplier`
- `providers[].providerId`, `kind`, `bus`, `policy`, `priority`, `path`
- `providers[].match`: role/career filters, battle result filters, HP threshold filters, local-owner restrictions
- `providers[].suppressOriginal`: original vocal or narration suppression rules

Known sound kinds include built-in events such as `CardUse`, `CareerSelected`, `LowHealth`, and `BattleCompleted`. SunExp also uses custom kinds such as `SunExp.Wuna.WhiteSunPrayer` and `SunExp.Wuna.GraveSong`, triggered through `AudioApi.PlayWhiteSunPrayer()` and `AudioApi.PlayGraveSong()`.

`CardUseCialloExp` is the smallest direct-provider example: it registers a `FileSoundProvider` for `SoundEventKinds.CardUse`, uses the `Effect` bus, replaces the original, hard-claims the request, and syncs remotely.

## Battle BGM

Source files:

- `BattleBgmArbiterShared/BattleBgmArbiterRuntime.cs`
- `SunExp-Dev/GameApi/BattleBgmProviderRuntime.cs`
- `BackgroundAudioReplaceExp-Dev/Hooks/BackgroundBattleMusicRuntime.cs`

`BattleBgmArbiterRuntime` creates one global Unity object named `BattleBgmArbiter.Global`. Providers are registered with `RegisterProvider(...)`. Providers can match adventure context, battle context, or both.

SunExp reads battle BGM definitions from `audio.registry.json`:

- `battleBgmDefaults.priority`
- `battleBgmDefaults.hardClaim`
- `battleBgmDefaults.silenceWhenLoading`
- `battleBgmDefaults.fallbackToOriginalWhenFailed`
- `battleBgmDefaults.allowMidBattleSwitch`
- `battleBgmProviders[]`

`BackgroundAudioReplaceExp` is the smallest direct-provider example: it registers one `FileBattleBgmProvider` backed by `BGM.mp3`, hard-claims battle music, silences while loading, and falls back to original music on load failure.

## Skill CG overlay

Source files:

- `SkillCGExp-Dev/Hooks/SkillCgRuntime.cs`
- `SkillCGExp-Dev/Hooks/SkillCgArbiterRuntime.cs`
- `SkillCGExp-Dev/Config/SkillCgConfig.cs`
- `SkillCGExp/SkillCGConfig.json`

`SkillCGExp` is an overlay arbiter, not an audio arbiter. It watches skill-card use requests, resolves the highest-priority enabled rule, and shows a configured image with fade/hold timing. The runtime has a global overlay object named `SkillCGExp.CgArbiter.Global`.

Config rules currently support:

- `enabled`
- `providerId`
- `cardId`
- `action`
- `ownerInstanceId`
- `image`
- `priority`
- `fadeIn`, `hold`, `fadeOut`

If `SkillCGConfig.json` is missing or empty, `SkillCGExp` falls back to built-in official role skill CG rules.

## Authoring rule

Prefer these shared arbiters when multiple mods could touch the same sound, BGM, or overlay surface. A mod-specific hook that directly replaces Unity audio or UI state should either become a provider for the arbiter or document why it must bypass the shared service.
