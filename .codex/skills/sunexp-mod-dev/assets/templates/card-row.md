# Card Row Template

- Data file: `SunExp/Data/Card/sunexp.csv`
- Text file: `SunExp/Text/Card/sunexp.csv`
- Runtime full id: `SunExp_sunexp_<Id>`
- Pack: one of the full `SunExp_sunexp_cardpack_*` ids.

Checklist:

- `InitScript` sets `BaseScript`.
- Target cards use `AttackCardItem` and `Action=Attack`.
- Description placeholders match `AddDescription`.
- `UseScript` and display calculation use the same formula or shared helper.
- Image path exists under `SunExp/ModResource`.
