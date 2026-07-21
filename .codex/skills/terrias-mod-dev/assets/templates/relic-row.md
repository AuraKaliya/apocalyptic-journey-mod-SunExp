# Relic Row Template

- Data file: `Terrias/Data/Relic/terrias.csv`
- Text file: `Terrias/Text/Relic/terrias.csv`
- Runtime full id: `Terrias_terrias_<Id>`

Checklist:

- `OwnScript` / `FightScript` call `CS.Terrias.Dll.Scripting.RelicScripts.*`.
- Shared relic behavior lives in C# helpers, not inline CSV logic.
- Per-fight state is reset on `FightStart` when needed.
- Display-state refresh is exposed through C# when needed.
- `PackBelong` points to an existing Terrias card pack.
- Image path exists under `Terrias/ModResource`.
