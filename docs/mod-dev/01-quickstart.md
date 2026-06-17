# Quickstart

This is the shortest safe path for adding a new piece of content in this
workspace.

## Choose an Authoring Path

Use the official Lua path when:

- the behavior is simple and can stay in `Scripts/Entry.lua`
- you only need table edits, resource redirection, or a small hook
- you are following `apocalyptic-journey-mod-tutorial/ModTemplate`

Use the C# DLL path when:

- the behavior has several branches or shared helpers
- the behavior needs reliable type access to game objects
- you need hooks that are easier to maintain in C#
- you are working in SunExp-style projects

In this workspace, SunExp-style projects use:

- published content in `<ModName>/`
- C# source in `<ModName>-Dev/`
- compiled DLL copied to `<ModName>/Scripts/Entry.dll`

## Add Content

1. Add or edit the `Data/<Table>/<file>.csv` row.
2. Add or edit the matching `Text/<Table>/<file>.csv` row when the table has text.
3. Add referenced assets under `ModResource/`, or use a known original-game path.
4. Keep script columns short.
5. Put behavior in C# `Scripting/` methods for DLL-based projects.
6. Reuse `GameApi/`, `Mechanics/`, and `Infrastructure/` helpers before adding new ones.
7. Run the project validation checks.

## ID Rules

Runtime MOD IDs are composed as:

```text
ModName_FileName_Id
```

For a row in `SunExp/Data/Card/sunexp.csv` with `Id = spark`, the full runtime
card ID is usually:

```text
SunExp_sunexp_spark
```

When a script references MOD-defined content, prefer the full runtime ID. An ID
that starts with `*` is normally excluded from random pools and is used for
career cards, tokens, fixed event cards, or other non-random content.

## Script Columns

For SunExp-style C# projects, CSV script columns should look like short bridges:

```csv
CS.SunExp.Dll.Scripting.CardScripts.Use(self, "spark");
```

Avoid long inline CSV scripts. Long scripts are hard to test, hard to diff, and
easy to desynchronize from displayed text.

## Validation

For SunExp:

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

The local validators cannot prove Unity runtime behavior, so UI hooks, map flow,
and deep scene interactions still need in-game verification.
