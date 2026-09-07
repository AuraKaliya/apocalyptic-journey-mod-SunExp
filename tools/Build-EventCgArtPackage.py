"""Prepare owner-qualified event-CG artwork from reviewed sources; does not generate new art."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageOps

REPO = Path(__file__).resolve().parents[1]
NEUTRAL_FACES = {
    "caroline": (1480, 302, 300, 255), "hermia": (1380, 365, 320, 250),
    "coco": (1415, 426, 300, 250), "caroline-alt": (1380, 375, 300, 260),
    "hermia-alt": (1430, 445, 300, 260), "coco-alt": (1370, 410, 290, 240),
    "amelia": (1320, 470, 300, 250), "nana": (1385, 362, 285, 225),
    "adela": (1335, 400, 310, 270), "vivian": (1290, 440, 315, 250),
    "husk": (1300, 460, 330, 260), "wuna": (571, 251, 220, 180),
    "loneer": (700, 248, 205, 185), "columbina": (452, 276, 200, 215),
    "olimya": (638, 262, 200, 175),
}
POSES = [("caroline", "victory.standard", "P01", 210, 150),
         ("coco", "victory.standard", "P02", 220, 164),
         ("amelia", "victory.standard", "P03", 170, 146),
         ("wuna", "victory.standard", "P04", 200, 175),
         ("caroline", "battle-defeat", "P05", 220, 180),
         ("coco", "victory.ritual", "P06", 220, 180)]
THEMES = {"battle-opening": "opening", "victory.standard": "victory", "victory.midas": "midas",
          "victory.ritual": "ritual", "victory.curse": "curse", "battle-defeat": "defeat",
          "adventure-settlement": "settlement"}


def load(path: Path) -> Image.Image:
    with Image.open(path) as image:
        return image.convert("RGBA")


def write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--references", type=Path, required=True)
    parser.add_argument("--sources", type=Path, default=REPO / "output/imagegen/event-cg")
    parser.add_argument("--output", type=Path, default=REPO / "AuraToolsExp/SharedResources/EventCg")
    args = parser.parse_args()
    spec = json.loads((REPO / "docs/AuraToolsExp/event-cg-art-direction/character-briefs.json").read_text(encoding="utf-8"))
    pilot = json.loads((args.sources / "previews/composition.json").read_text(encoding="utf-8"))
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    assets, characters, themes = {}, [], {}
    provenance = []

    def save_asset(key: str, image: Image.Image, relative: str, portrait: dict | None = None) -> None:
        target = output / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        image.save(target)
        assets[key] = {"path": relative, "portrait": portrait or {"enabled": False}, "layers": []}

    def portrait_asset(key: str, source: Path, face: tuple, relative: str, companions: list[tuple[str, Path]]) -> None:
        original = load(source)
        bounds = original.getchannel("A").getbbox()
        if bounds is None:
            raise ValueError(f"Empty portrait: {source}")
        cropped = original.crop(bounds)
        padding = max(8, round(max(cropped.size) * 0.025))
        size = (cropped.width + padding * 2, cropped.height + padding * 2)
        center = (face[0] - bounds[0] + padding, face[1] - bounds[1] + padding)
        if not (0 < center[0] < size[0] and 0 < center[1] < size[1]):
            raise ValueError(f"Face outside portrait: {key}")
        face_metadata = {"enabled": True, "faceX": center[0] / size[0], "faceY": center[1] / size[1],
                         "faceWidth": face[2] / size[0], "faceHeight": face[3] / size[1], "canMirror": False}
        canvas = Image.new("RGBA", size)
        canvas.alpha_composite(cropped, (padding, padding))
        pixels = np.array(canvas)
        # The reviewed assets use a waist crop. Fade only its lowest few percent.
        end = padding + cropped.height
        depth = max(10, round(cropped.height * 0.035))
        ramp = np.clip((end - np.arange(size[1])) / depth, 0, 1)
        ramp = ramp * ramp * (3 - 2 * ramp)
        pixels[:, :, 3] = (pixels[:, :, 3] * ramp[:, None]).astype(np.uint8)
        canvas = Image.fromarray(pixels)
        canvas.thumbnail((1280, 1280), Image.Resampling.LANCZOS)
        save_asset(key, canvas, relative, face_metadata)
        provenance.append({"asset": key, "source": str(source), "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest()})
        for companion_key, companion_path in companions:
            layer = Image.new("RGBA", size)
            layer.alpha_composite(load(companion_path).crop(bounds), (padding, padding))
            layer = layer.resize(canvas.size, Image.Resampling.LANCZOS)
            companion_relative = f"Characters/{key.split('.')[1]}/{companion_key.split('.')[-1]}.png"
            save_asset(companion_key, layer, companion_relative)
            assets[key]["layers"].append({"asset": companion_key, "foreground": True, "required": True,
                                         "opacity": 1.0, "pulse": 0.06 if key.startswith("role.wuna") else 0.0})

    for character in spec["characters"]:
        identity = character["id"]
        neutral = f"role.{identity}.neutral"
        portrait_asset(neutral, args.references / character["file"], NEUTRAL_FACES[identity],
                       f"Characters/{identity}/neutral.png", [])
        characters.append({"id": identity, "roleIds": character["roleIds"], "variantIds": [], "neutral": neutral, "poses": {}})
    for identity, scene, code, width, height in POSES:
        key = f"role.{identity}.{scene}"
        face = tuple(pilot["assets"][code]["face"]) + (width, height)
        companions = []
        if code == "P02": companions.append(("role.coco.front-hand", args.sources / "selected/P02-front-hand.png"))
        if code == "P04": companions.append(("role.wuna.flame", args.sources / "selected/P04-flame.png"))
        portrait_asset(key, args.sources / f"selected/{code}.png", face,
                       f"Characters/{identity}/{scene}.png", companions)
        next(character for character in characters if character["id"] == identity)["poses"][scene] = key
    for key, source in (("theme.leaves", "selected/FG-V01.png"), ("theme.light", "selected/FX-V01.png")):
        image = ImageOps.fit(load(args.sources / source), (1600, 900), method=Image.Resampling.LANCZOS)
        save_asset(key, image, f"Themes/{key.split('.')[-1]}.png")
    for scene, theme in THEMES.items():
        source = args.sources / ("selected/BG-V01.png" if theme == "victory" else f"runtime-themes/{theme}.png")
        image = ImageOps.fit(load(source), (1600, 900), method=Image.Resampling.LANCZOS)
        key = f"theme.{theme}.background"
        save_asset(key, image, f"Themes/{theme}.png")
        layers = []
        if theme not in {"defeat", "curse"}:
            layers.append({"asset": "theme.light", "foreground": True, "required": True, "opacity": 0.18,
                           "motionX": 0.004, "motionY": -0.002, "pulse": 0.06})
        if theme in {"victory", "midas", "settlement"}:
            layers.append({"asset": "theme.leaves", "foreground": True, "required": True, "opacity": 0.72,
                           "motionX": -0.005, "motionY": 0.003})
        themes[scene] = {"background": key, "darkTitle": theme in {"victory", "defeat", "settlement"},
                         "cameraPush": 0.015 if theme == "defeat" else 0.02, "layers": layers}
        provenance.append({"asset": key, "source": str(source), "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest()})
    catalog = {"schemaVersion": 1, "revision": "event-poster-2026-09-06-r1",
               "previewRoles": ["career_5", "career_7", "Terrias_wuna_wuna", "career_1"] +
                               [character["roleIds"][0] for character in characters
                                for role in [character["roleIds"][0]]
                                if role not in {"career_5", "career_7", "Terrias_wuna_wuna", "career_1"}],
               "assets": assets, "themes": themes, "characters": characters}
    write_json(output / "event-cg.art.json", catalog)
    write_json(args.sources / "runtime-themes/package-provenance.json", provenance)
    # This contact sheet checks the authored face metadata against the actual alpha image.
    sheet = Image.new("RGB", (1200, 5 * 290), "#202a3b")
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 16)
    for index, character in enumerate(characters):
        asset = assets[character["neutral"]]
        image = load(output / asset["path"])
        face = asset["portrait"]
        crop_width = round(image.width * face["faceWidth"] * 1.75)
        crop_height = round(image.height * face["faceHeight"] * 1.75)
        cx, cy = image.width * face["faceX"], image.height * face["faceY"]
        image = image.crop((round(cx - crop_width / 2), round(cy - crop_height / 2), round(cx + crop_width / 2), round(cy + crop_height / 2)))
        image = ImageOps.contain(image, (360, 240), Image.Resampling.LANCZOS)
        x, y = index % 3 * 400, index // 3 * 290
        sheet.paste(image, (x + (400 - image.width) // 2, y + 34), image)
        draw.text((x + 12, y + 8), character["id"], fill="white", font=font)
    sheet.save(args.sources / "runtime-themes/neutral-face-audit.jpg", quality=94)
    print(json.dumps({"characters": len(characters), "poses": len(POSES), "themes": len(themes), "assets": len(assets), "output": str(output)}))


if __name__ == "__main__":
    main()
