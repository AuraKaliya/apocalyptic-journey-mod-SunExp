# SunExp C# DLL

This project builds the C# mod entry assembly for SunExp.

`Docs/` contains development notes and workshop copy moved out of the runtime mod folder.

Build:

```powershell
dotnet build .\SunExp.Dll.csproj -c Release
```

Test:

```powershell
..\tools\Test-SunExpCSharp.ps1
```

The build target copies the compiled assembly to `..\SunExp\Scripts\Entry.dll`, which is the file name loaded by the game.

The internal assembly name is `SunExp.Aura` to avoid runtime conflicts with other mods that also ship an `Entry.dll`.
