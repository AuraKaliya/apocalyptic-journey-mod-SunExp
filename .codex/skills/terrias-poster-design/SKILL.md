---
name: terrias-poster-design
description: Design and iterate Terrias topic posters from current character, card-pack, mode or mechanic content, including exact Chinese copy, reference images and archived prompts. Card/relic icons use the separate card-art skill.
---

# Terrias Poster Design

Create reviewable topic posters with accurate content and recognizable
characters. Use the available imagegen skill for generation tools and current
parameters; preserve any explicit user choice of provider or workflow.

## Workflow

1. Read the relevant docs/Terrias content and current user copy. Verify changing
   mechanics or release claims against the current product.
2. Choose the topic, reusable series title, subtitle, exact text, featured
   subjects and reference roles.
3. Draft and save the prompt in docs/Terrias/Posters. Use
   [the prompt template](references/poster-prompt-template.md) as a
   composition aid, adapting it to the requested output.
4. Generate through the active image-generation capability. Default tool
   generation does not require an API key. Use CLI/API or a logged-in browser
   when the user has chosen that path; obtain its current invocation from the
   available skill/tool documentation rather than a saved machine path.
5. Inspect text, identity, composition and actual dimensions. Preserve original
   output and accepted earlier versions. Clearly distinguish a delivery-size
   derivative from the natural generated image.
6. Update the saved prompt before a revision. Save project deliverables in the
   workspace and show the reviewable result.

Use [the workflow reference](references/topic-poster-workflow.md) for
composition, reference handling and delivery. Runtime CG/shaders belong to
[aura-visual-runtime-dev](../aura-visual-runtime-dev/SKILL.md); small card/relic
art follows [its benchmark workflow](../terrias-card-art-style/SKILL.md).

After changing this skill, run `tools/Test-ProjectSkills.ps1`. A metadata pass
does not validate poster content or visual quality.
