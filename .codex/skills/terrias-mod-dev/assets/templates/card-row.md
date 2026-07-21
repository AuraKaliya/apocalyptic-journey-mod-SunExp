# Card Row Template

- Data file: `Terrias/Data/Card/terrias.csv`
- Text file: `Terrias/Text/Card/terrias.csv`
- Runtime full id: `Terrias_terrias_<Id>`
- Pack: one of the full `Terrias_terrias_cardpack_*` ids.

Checklist:

- `InitScript` / `UseScript` call `CS.Terrias.Dll.Scripting.CardScripts.*`.
- `InitScript` sets `BaseScript`.
- Target cards use `AttackCardItem` and `Action=Attack`.
- Description placeholders match `AddDescription`.
- Display setup and runtime behavior are kept in sync through C#.
- Image path exists under `Terrias/ModResource`.
