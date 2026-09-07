# Event CG Scene v2 Prototype

Archived: this is the historical Idle-sprite stage prototype. Current event CG acceptance uses the production portrait renderer through `tools/Preview-EventCgPoster.ps1`; see `docs/AuraToolsExp/team-event-cg-poster-v3-design.md`.

This standalone HTML prototype explores the approved adaptive team-scene design
without changing the product runtime. It uses actual repository role PNGs and
the programmatic AuraToolsExp scene themes as visual inputs; no bitmap background is required.

Open `index.html` directly, or run the repeatable Playwright capture:

```powershell
tools\Preview-EventCgSceneV2.ps1
```

The capture suite covers all participant counts, every event tone, reduced
motion, the wide/non-humanoid panel fallback, and the supported `1280x720` and
`922x838` viewport budgets. Output is written to
`output/playwright/event-cg-scene-v2/`.

This is a composition prototype, not a second product renderer. The production
implementation must replace it with the shared `AuraCgSceneRenderer` described
in `docs/AuraToolsExp/team-event-cg-v2-design.md`.
