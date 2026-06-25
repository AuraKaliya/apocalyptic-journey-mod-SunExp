# Asset Validation

Use this reference before replacing final project image assets.

## Cards

- File is `512x512` PNG.
- Mode is `RGB` or `RGBA`.
- Subject remains readable at `128x128`.
- Icon path matches the `Icon` field in `Data/Card/*.csv`.
- Pack palette is consistent across a contact sheet.
- No text, numbers, watermark, UI frame, card border, scenery, or character
  scene appears.

## Relics

- File is `512x512` PNG.
- Mode is `RGB` or `RGBA`.
- Subject remains readable at `32x32`.
- Outer-edge pixels are pure black `#000000`.
- Icon path matches the `Icon` field in `Data/Relic/*.csv`.
- The object is a centered physical item, not a card scene or abstract effect
  background.

## Contact Sheets

Save review sheets outside the game resource folder, for example:

- `tools/previews/sunexp_card_redesign_contact_sheet.png`
- `tools/previews/goldexp-card-icons-generated-remake.png`

Use contact sheets to compare pack consistency, silhouette variety, and small
size readability before replacing final resources.
