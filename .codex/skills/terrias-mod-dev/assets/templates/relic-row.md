# Relic Row Template

- Data file: `SunExp/Data/Relic/sunexp.csv`
- Text file: `SunExp/Text/Relic/sunexp.csv`
- Runtime full id: `SunExp_sunexp_<Id>`

Checklist:

- `OwnScript` / `FightScript` call `CS.SunExp.Dll.Scripting.RelicScripts.*`.
- Shared relic behavior lives in C# helpers, not inline CSV logic.
- Per-fight state is reset on `FightStart` when needed.
- Display-state refresh is exposed through C# when needed.
- `PackBelong` points to an existing SunExp card pack.
- Image path exists under `SunExp/ModResource`.
