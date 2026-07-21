# Terrias C# DLL

This project builds the C# mod entry assembly for Terrias.

`Docs/` contains development notes and workshop copy moved out of the runtime mod folder.

Build:

```powershell
dotnet build .\Terrias.Dll.csproj -c Release
```

Test:

```powershell
..\tools\Test-TerriasCSharp.ps1
```

The build target copies the compiled assembly to `..\Terrias\Scripts\Entry.dll`, which is the file name loaded by the game.

The internal assembly name is `Terrias.Aura` to avoid runtime conflicts with other mods that also ship an `Entry.dll`.
