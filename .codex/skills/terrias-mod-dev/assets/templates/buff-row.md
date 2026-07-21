# Buff Row Template

- Data file: `SunExp/Data/Buff/sunexp.csv`
- Text file: `SunExp/Text/Buff/sunexp.csv`
- Runtime full id: `SunExp_sunexp_<Id>`

Checklist:

- `InitScript` / `ApplyScript` / `ClearScript` call `CS.SunExp.Dll.Scripting.BuffScripts.*`.
- Persistent behavior is implemented in C#.
- Event cleanup or token gating is present when reapplication can duplicate listeners.
- `ClearScript` removes hook state when needed.
- `UpperBound`, decay fields, `Type`, and `CanZero` match intended behavior.
