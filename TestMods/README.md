# Archived TestMods

`TestMods` contains prototypes and historical feature experiments. These MODs
are not product consumers and are not part of shared/core, AuraToolsExp, or
Terrias validation and release gates.

Run their isolated validation only when explicitly maintaining or inspecting a
prototype:

```powershell
tools\Test-TestMods.ps1
```

The default entry builds the current shared-runtime prototypes, validates the
archived SkinExp package, and runs GoldExp source validation without rebuilding
its legacy game-path project. Use `-BuildLegacyGoldExp` only when that archived
project itself is being maintained.
