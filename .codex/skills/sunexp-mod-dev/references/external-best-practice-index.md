# External Best-Practice Index

Use this reference when a task benefits from Unity documentation or mature
project patterns. External sources are thinking aids only. Final design must
fit the current SunExp/AuraToolsExp architecture: shared/core foundation,
content mod registration, tool mod configuration, and no content/tool
cross-dependency.

## Official Unity References

- Unity Manual: https://docs.unity3d.com/Manual/index.html
  - Use for current Unity concepts, editor/runtime behavior, packages,
    rendering, UI, scripting, profiling, and platform guidance.
- Unity Scripting API: https://docs.unity3d.com/ScriptReference/index.html
  - Use for UnityEngine API shape and class/member behavior when `Managed/`
    does not make intent obvious.
- AssetBundles introduction:
  https://docs.unity3d.com/Manual/AssetBundlesIntro.html
  - Use for runtime asset packaging and loading concepts.
- AssetBundle dependencies:
  https://docs.unity3d.com/Manual/AssetBundles-Dependencies.html
  - Use when avoiding duplicate shared assets or reasoning about bundle
    dependency layout.
- uGUI Raw Image:
  https://docs.unity3d.com/Packages/com.unity.ugui@1.0/manual/script-RawImage.html
  - Use for image UI controls and `Raycast Target` behavior.

## Use Rules

- Prefer official documentation URLs over search snippets.
- Note the Unity documentation version if the answer depends on versioned
  behavior.
- Do not import an external architecture wholesale. Translate the idea into the
  repository's existing surfaces: `AuraSharedCore`, domain shared components,
  `SunExp-Dev`, and `AuraToolsExp-Dev`.
- If an external source reveals a durable rule for this project, add a short
  entry here with the source URL and the local project interpretation.
- If a source is only useful for one debugging session, keep it in the task
  context instead of adding it to the skill.
