# AuraTools Unity UI Preview Player

This is a standalone Unity 2022.3.62f3c1 project. It does not load the game,
`Witch.dll`, a save file, network services, or any gameplay scene.

The player recreates the complete settings window with five tabs:

- Audio/Visual;
- Game;
- Feedback;
- Key Bindings;
- AuraTools toolbox.

Build the Windows player and run twenty-two automated captures, including role
CG and event CG configuration at the two supported layout budgets:

```powershell
tools\Build-AuraToolsUnityUiPreview.ps1
```

Build, validate, and open the interactive player:

```powershell
tools\Build-AuraToolsUnityUiPreview.ps1 -Open
```

Open an existing build without rebuilding or recapturing:

```powershell
tools\Build-AuraToolsUnityUiPreview.ps1 -SkipBuild -SkipCapture -Open
```

Outputs are written under `output/unity/aura-tools-ui-preview/`. The capture
report checks page ownership, toolbox opacity, center raycast ownership,
category text fit, fixed row height, nonblank rendering, and the hidden native
content probe.

The interactive player supports the visible tabs and controls. F1-F5 select
the five settings pages, F6 cycles toolbox scenarios, F9 saves a manual
screenshot, and Escape closes the active overlay or exits the player.
