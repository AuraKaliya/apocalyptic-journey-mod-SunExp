# Buff Row Template

- Data file: `SunExp/Data/Buff/sunexp.csv`
- Text file: `SunExp/Text/Buff/sunexp.csv`
- Runtime full id: `SunExp_sunexp_<Id>`

Checklist:

- Persistent behavior goes in `ApplyScript`.
- Event cleanup or token gating is present when reapplication can duplicate listeners.
- `ClearScript` removes hook state when needed.
- `UpperBound`, decay fields, `Type`, and `CanZero` match intended behavior.
