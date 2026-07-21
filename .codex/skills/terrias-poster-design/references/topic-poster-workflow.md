# Topic Poster Workflow

## 1. Analyze The Topic

Start from repo content, not from a generic art prompt.

- For role posters, read the matching role docs and current role images under
  `SunExp/ModResource/Images/Character/`.
- For card-pack posters, read the card-pack docs, pack covers, and representative
  card icons.
- For gameplay-mode posters, read the mode docs and use UI title images, map
  node art, CG frames, or mode-specific assets as references.
- For mechanism posters, read `相关BUFF与核心机制介绍.md` and decide whether the
  output should be a cinematic poster or a diagram-like info poster.

Summarize before generating:

- poster topic and series container title;
- exact title, subtitle, and main copy;
- featured subjects and reference image paths;
- 3-5 visual motifs that should drive the prompt;
- avoid list, especially unwanted extra characters, UI panels, screenshots,
  gacha-banner composition, and fake text.

## 2. Write The Prompt

Save one prompt per poster:

```text
docs/Terrias/Posters/<slug>_gpt_image2_prompt.txt
```

Prompt rules:

- Describe each input image by role: visual identity reference, style reference,
  supporting scene reference, etc.
- For role references, ask GPT-IMAGE2 to preserve recognizable appearance,
  costume language, hair color, silhouette, and signature magical motifs, but
  redraw a new poster key visual.
- Put all required text in an explicit `Text to include exactly` section.
- Use exact Chinese text and say it must be legible and verbatim.
- If text accuracy matters more than fully integrated typography, consider a
  fallback plan: generate a no-text key visual, then overlay text locally.
- Keep prompt constraints practical: composition, negative space, subject count,
  lighting palette, mood, and concrete avoid list.

## 3. Generate With GPT-IMAGE2

### CLI/API Path

Use this when `OPENAI_API_KEY` is set:

```powershell
python C:\Users\75601\.codex\skills\.system\imagegen\scripts\image_gen.py edit `
  --model gpt-image-2 `
  --image <reference-1.png> `
  --image <reference-2.png> `
  --prompt-file docs\Terrias\Posters\<slug>_gpt_image2_prompt.txt `
  --no-augment `
  --size 2160x3840 `
  --quality high `
  --output-format png `
  --out docs\Terrias\Posters\<slug>_gpt-image2.png
```

Notes:

- Do not set `--input-fidelity` for `gpt-image-2`; image inputs already use high
  fidelity.
- Use the `edit` endpoint for image-reference generation.
- Use `--no-augment` when the prompt file is already complete.

### Browser/ChatGPT Path

Use this when the user asks to generate through logged-in ChatGPT:

1. Open `https://chatgpt.com/`.
2. If login is needed, ask the user to complete login manually.
3. Paste or upload all reference images into the composer.
4. Paste the saved prompt, prefixed with the target generation settings:
   `Please use GPT-IMAGE2 / high quality / vertical 2160x3840 image generation.`
5. Submit and wait for the image to finish.
6. Save the result into `docs/Terrias/Posters/`.

Browser saving gotchas observed in this repo:

- ChatGPT may return a natural image smaller than the requested size; record the
  actual dimensions.
- Direct `Invoke-WebRequest` against `backend-api/estuary/content` may fail with
  `File stream access denied` outside the logged-in browser session.
- If page download events are not exposed, open the generated image's raw URL in
  the logged-in browser and capture/export it from that page; crop any browser
  background before saving.
- Preserve both the original generated image and a delivery-size resized copy
  when dimensions differ from the requested spec.

## 4. Review

Before reporting completion, inspect the saved PNG. Check:

- exact title and major copy;
- no obvious Chinese text corruption;
- role identity and reference likeness;
- no extra characters unless requested;
- no unintended UI panels, card frames, watermarks, QR codes, or platform badges;
- actual dimensions and saved paths.

When text is close but not perfect, do not hide the issue. Either regenerate with
tighter text constraints or propose a hybrid pass with local text overlay.

## 5. Archive

For every accepted poster, keep:

- final PNG;
- original/generated-size PNG if different from delivery size;
- prompt file;
- short notes file only when it records non-obvious generation settings,
  reference paths, or browser/API quirks.
