# Buff Row Template

- Data file: `Terrias/Data/Buff/terrias.csv`
- Text file: `Terrias/Text/Buff/terrias.csv`
- Runtime full id: `Terrias_terrias_<Id>`

Checklist:

- `InitScript` / `ApplyScript` / `ClearScript` call `CS.Terrias.Dll.Scripting.BuffScripts.*`.
- Persistent behavior is implemented in C#.
- Event cleanup or token gating is present when reapplication can duplicate listeners.
- `ClearScript` removes hook state when needed.
- `UpperBound`, decay fields, `Type`, and `CanZero` match intended behavior.
