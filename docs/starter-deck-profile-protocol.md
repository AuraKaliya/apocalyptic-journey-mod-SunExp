# StarterDeck Profile Protocol

This document defines the shared starter-deck profile contract used by
AuraToolsExp world-simulation starter deck customization and by participating
Mods such as SunExp and SanGuoShaExp.

The general shared-component rules live in
`docs/shared-component-architecture-guidelines.md`. StarterDeck is the reference
implementation for a user-selectable domain arbiter under those rules.

## Layer Boundaries

`AuraSharedCore` is the bottom service layer. It provides:

- a cross-DLL global component;
- owner and shared storage with revision checks;
- the generic resource registry;
- package install, transaction recovery, and operation diagnostics.

`AuraSharedCore` does not know starter-deck semantics. It should not decide which
deck wins, whether a profile is editable, or whether a role belongs to a Mod.

`StarterDeckArbiterShared` is the starter-deck domain layer. It owns:

- `StarterDeckProfile` and `starterdeck.registry.json`;
- profile registration into `AuraSharedRegistry`;
- profile validation and candidate sorting;
- effective-profile resolution;
- starter deck application and run ownership markers.

`AuraToolsExp` is a product/UI layer. It owns:

- editing AuraTools local profiles;
- choosing a profile for a role;
- copying read-only registered profiles into editable local profiles;
- displaying profile status and validation feedback.

AuraToolsExp must not mutate or delete profiles owned by other Mods.

## Profile Sources

There are two source kinds:

- `Registered`: provided by a Mod through `starterdeck.registry.json` or an
  equivalent shared registration call.
- `Local`: created and owned by AuraToolsExp configuration.

Registered profiles are immutable from AuraToolsExp. They may be selected,
inspected, and copied, but not edited or deleted. Local AuraToolsExp profiles may
be edited or deleted according to their own local settings.

When a registered profile is copied, the copy becomes a new `Local` profile owned
by AuraToolsExp. The copy should keep `derivedFromProfileId` so the UI can show
where it came from, but subsequent edits are fully local.

## Manifest

Each participating Mod may ship a file named `starterdeck.registry.json` at its
Mod root:

```json
{
  "schemaVersion": 1,
  "ownerModId": "SunExp",
  "profiles": [
    {
      "profileId": "wuna.world-simulation.default",
      "displayName": "WuNa default starter deck",
      "modeIds": [ "AuraTools.WorldSimulation" ],
      "targetRoleIds": [ "SunExp_wuna_wuna", "wuna" ],
      "deckSize": 11,
      "priority": 1000,
      "cardIds": [
        "SunExp_sunexp_spark"
      ]
    }
  ]
}
```

`ownerModId` must be stable and must identify the Mod that owns the profile.
`profileId` is unique within that owner. The technical identity is:

```text
ownerModId + ":" + profileId
```

Two profiles targeting the same role are not a conflict. They are separate
candidates. The only technical replacement identity is the same
`ownerModId/profileId` pair.

## Effective Profile Priority

The starter-deck domain resolver must use this order:

1. User selected profile for the role.
2. Registered profile owned by the role's Mod, when `PreferRoleModProfile` is
   enabled.
3. AuraToolsExp local role profile, when role-specific local mode is enabled.
4. AuraToolsExp local global profile.
5. Optional non-owner registered fallback, only when explicitly enabled by the
   caller policy.

If the selected profile is missing, disabled, role/mode mismatched, or incomplete,
the resolver skips it and continues through the fallback order. The invalid
selection should remain visible to the UI so the user can fix or clear it.

## Role Ownership

Role ownership is inferred conservatively:

- an explicit role-owner id in the resolution context wins;
- otherwise a role id prefixed by the owner id matches, for example
  `SunExp_wuna_wuna`;
- a profile can also prove ownership through its own `targetRoleIds`, so short
  role ids such as `wuna` still match a SunExp profile that also targets
  `SunExp_wuna_wuna`;
- product layers may add local hints from resource paths such as
  `Mods/SunExp/...`, then pass the resolved owner into the shared context.

Owner inference is for default priority only. It does not grant edit authority.

## Validation

Callers should validate profiles before applying or presenting them as complete.
`StarterDeckArbiterShared.ValidateProfile` reports:

- disabled profile;
- mode mismatch;
- role mismatch;
- empty deck;
- deck-size mismatch;
- candidate packs that require a caller-provided deck resolver.

AuraToolsExp should pass a resolver that expands candidate packs and filters
unavailable cards in the current lobby. The shared layer records the common
result shape; the product layer supplies game-context-specific card resolution.

## AuraToolsExp Rules

AuraToolsExp may:

- create and edit the global local profile;
- create, edit, and delete role local profiles;
- select any eligible registered or local profile for a role;
- clear a role selection and return it to automatic resolution;
- copy a registered profile into an AuraToolsExp local profile.

AuraToolsExp must not:

- write into another Mod's `starterdeck.registry.json`;
- edit or delete `Registered` profiles;
- collapse multiple same-role registered profiles into one;
- silently replace a user's explicit selection with an automatic default.

## New Mod Checklist

1. Add `starterdeck.registry.json` at the Mod root.
2. Set `ownerModId` to the stable Mod id.
3. Use full card ids in `cardIds`.
4. Include at least one full target role id in `targetRoleIds`.
5. Register the manifest from the Mod entry point with
   `StarterDeckArbiterRuntime.RegisterProfileManifest`.
6. Build the Mod DLL and run the shared consumer build.
7. Verify AuraToolsExp lists the profile as read-only and can copy it to a local
   editable profile.
