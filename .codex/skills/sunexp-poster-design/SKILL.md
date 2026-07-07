---
name: sunexp-poster-design
description: Project-local skill for designing, generating, reviewing, archiving, or updating SunExp MOD topic posters such as character archive, card-pack, gameplay-mode, and core-mechanic posters. Use when the user asks to make posters from docs/Terrias content, write GPT-IMAGE2 poster prompts, use role/card images as references, generate through ChatGPT or the image CLI, or iterate poster themes and text.
---

# SunExp Poster Design

Use this skill for SunExp MOD introduction posters. Keep it separate from
`sunexp-card-art-style`: that skill covers small card/relic icons, while this
skill covers full topic posters with text, references, and presentation copy.

## Workflow

1. Analyze before generating.
   - Read the relevant `docs/Terrias/*.md` files and any current user copy.
   - Group content by poster topic, not by source file count.
   - Decide the poster's reusable series title, issue-specific subtitle, exact
     copy, featured subjects, and reference images.
2. Draft the GPT-IMAGE2 prompt.
   - Use role/card/CG images only as visual references unless the user asks for
     direct compositing.
   - Put exact Chinese text in the prompt and repeat that it must be rendered
     verbatim.
   - Keep the poster title generic enough for future series expansion.
   - Save the prompt under `docs/Terrias/Posters/<topic>_gpt_image2_prompt.txt`.
3. Generate with GPT-IMAGE2.
   - Preferred API/CLI path when `OPENAI_API_KEY` is available: use
     `image_gen.py edit --model gpt-image-2 --quality high --size 2160x3840`
     with `--no-augment` and one `--image` per reference.
   - Browser path when the user asks to use logged-in ChatGPT: upload/paste the
     reference images, paste the prompt, submit, then save the generated image
     back into `docs/Terrias/Posters/`.
4. Review and archive.
   - Inspect the generated image visually before reporting success.
   - Check title/copy, featured character identity, composition, extra
     characters, unwanted UI/collage artifacts, and actual natural dimensions.
   - Keep the original generated image and create a delivery-size copy when the
     web UI returns a smaller natural image than requested.
5. Iterate from feedback.
   - Update the saved prompt first, then regenerate.
   - Preserve earlier accepted outputs unless the user explicitly asks to
     replace them.

## References

- `references/topic-poster-workflow.md`: detailed analysis, generation, saving,
  and review procedure.
- `references/gpt-image2-prompt-template.md`: reusable prompt template for new
  SunExp poster topics.

## Validation

Run after editing this skill:

```powershell
python C:\Users\75601\.codex\skills\.system\skill-creator\scripts\quick_validate.py .codex\skills\sunexp-poster-design
```
