# Relic Row Template

- Data file: `SunExp/Data/Relic/sunexp.csv`
- Text file: `SunExp/Text/Relic/sunexp.csv`
- Runtime full id: `SunExp_sunexp_<Id>`

Checklist:

- `FightScript` registers combat events.
- Per-fight state is reset on `FightStart` when needed.
- `self:UpdateRelicShow()` is called after changing display state.
- `PackBelong` points to an existing SunExp card pack.
- Image path exists under `SunExp/ModResource`.
