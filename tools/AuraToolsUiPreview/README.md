# AuraToolsExp HTML UI Preview

This preview renders the production toolbox information architecture without
starting the game. It uses the shipped toolbox icon resources, production
module ids, matching theme tokens, and the same 144/52/84 layout metrics as the
Unity UI.

Run the repeatable Playwright capture and assertions:

```powershell
tools\Preview-AuraToolsToolbox.ps1
```

Generate captures and open the interactive page:

```powershell
tools\Preview-AuraToolsToolbox.ps1 -Open
```

Open the page without rerunning captures:

```powershell
tools\Preview-AuraToolsToolbox.ps1 -SkipCapture -Open
```

Outputs are written to `output/playwright/aura-tools-toolbox/`:

- ten viewport/scenario screenshots;
- global-search and settings-overlay interaction screenshots;
- `report.json` with layout, opacity, image and interaction assertions.

The HTML preview validates layout and visual states. It does not replace a
future Unity preview player for Canvas sorting, `GraphicRaycaster`, TMP font
fallback, or native `SettingUI` hierarchy fidelity.
