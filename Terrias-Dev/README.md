# Terrias C# implementation

This project compiles Terrias content behavior into the Terrias.Aura assembly.
The game loads the published file as Terrias/Scripts/Entry.dll.

From the repository root:

```powershell
tools/Test-TerriasGate.ps1 -Profile csharp
```

This builds the shared runtime and both product consumers once, publishes their
packages through the shared transaction, and runs Terrias C# behavior tests.

A direct `dotnet build Terrias-Dev/Terrias.Dll.csproj -c Release` compiles but
does not update the MOD package. Use tools/Build-MainSharedConsumers.ps1 when
publishing a product C# change without that test profile.

See [technical docs](../docs/Terrias/README.md),
[content development](../.codex/skills/terrias-mod-dev/SKILL.md), and
[validation selection](../.codex/skills/aura-project-dev/references/validation.md).
