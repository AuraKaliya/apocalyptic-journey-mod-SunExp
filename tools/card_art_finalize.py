from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

from PIL import Image


PALETTE = {
    "deep_ultramarine": (0x13, 0x24, 0x5A),
    "smoke_violet": (0x4B, 0x32, 0x6C),
    "wine_red": (0x65, 0x17, 0x2A),
    "crimson": (0xB8, 0x32, 0x4B),
    "old_gold_ivory": (0xE6, 0xB9, 0x6F),
}


def is_chroma_green(pixel: tuple[int, int, int]) -> bool:
    red, green, blue = pixel
    return green >= 80 and green > red * 1.2 and green > blue * 1.2


def nearest_palette_group(pixel: tuple[int, int, int]) -> str:
    return min(
        PALETTE,
        key=lambda name: sum(
            (channel - anchor) ** 2
            for channel, anchor in zip(pixel, PALETTE[name])
        ),
    )


def foreground_bbox(image: Image.Image) -> tuple[int, int, int, int] | None:
    mask = Image.new("1", image.size)
    mask.putdata([pixel != (0, 0, 0) for pixel in image.get_flattened_data()])
    return mask.getbbox()


def metrics(image: Image.Image) -> dict[str, object]:
    pixels = list(image.get_flattened_data())
    pixel_count = len(pixels)
    black_count = sum(pixel == (0, 0, 0) for pixel in pixels)
    width, height = image.size
    border = []
    border.extend(image.crop((0, 0, width, 1)).get_flattened_data())
    border.extend(
        image.crop((0, height - 1, width, height)).get_flattened_data()
    )
    border.extend(image.crop((0, 0, 1, height)).get_flattened_data())
    border.extend(
        image.crop((width - 1, 0, width, height)).get_flattened_data()
    )

    bbox = foreground_bbox(image)
    bbox_fraction = None
    if bbox:
        left, top, right, bottom = bbox
        bbox_fraction = {
            "width": round((right - left) / width, 6),
            "height": round((bottom - top) / height, 6),
        }

    groups = {name: 0 for name in PALETTE}
    foreground = [pixel for pixel in pixels if pixel != (0, 0, 0)]
    for pixel in foreground:
        groups[nearest_palette_group(pixel)] += 1
    foreground_count = max(1, len(foreground))

    return {
        "size": [width, height],
        "exact_black_pixels": black_count,
        "exact_black_ratio": round(black_count / pixel_count, 6),
        "border_non_black_pixels": sum(pixel != (0, 0, 0) for pixel in border),
        "foreground_bbox_fraction": bbox_fraction,
        "nearest_palette_area_ratios": {
            name: round(count / foreground_count, 6)
            for name, count in groups.items()
        },
    }


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Key a flat green card-art background to exact black and resize."
    )
    parser.add_argument("source", type=Path)
    parser.add_argument("stage3", type=Path)
    parser.add_argument("final", type=Path)
    parser.add_argument("--subject-scale", type=float, default=1.0)
    args = parser.parse_args()

    source = Image.open(args.source).convert("RGB")
    alpha = Image.new("L", source.size)
    alpha.putdata(
        [
            0 if is_chroma_green(pixel) else 255
            for pixel in source.get_flattened_data()
        ]
    )
    bbox = alpha.getbbox()
    if bbox is None:
        raise SystemExit("No foreground remained after chroma-key extraction.")

    subject = source.convert("RGBA")
    subject.putalpha(alpha)
    subject = subject.crop(bbox)

    scale = args.subject_scale
    if not math.isfinite(scale) or scale <= 0:
        raise SystemExit("--subject-scale must be a finite positive number.")
    if scale != 1.0:
        subject = subject.resize(
            (
                max(1, round(subject.width * scale)),
                max(1, round(subject.height * scale)),
            ),
            Image.Resampling.LANCZOS,
        )

    source_center_x = (bbox[0] + bbox[2]) / 2
    source_center_y = (bbox[1] + bbox[3]) / 2
    paste_x = round(source_center_x - subject.width / 2)
    paste_y = round(source_center_y - subject.height / 2)
    if (
        paste_x < 0
        or paste_y < 0
        or paste_x + subject.width > source.width
        or paste_y + subject.height > source.height
    ):
        raise SystemExit("Scaled subject would leave the source canvas.")

    stage3 = Image.new("RGB", source.size, (0, 0, 0))
    stage3.paste(subject.convert("RGB"), (paste_x, paste_y), subject.getchannel("A"))
    args.stage3.parent.mkdir(parents=True, exist_ok=True)
    stage3.save(args.stage3, format="PNG", optimize=True)

    final = stage3.resize((256, 256), Image.Resampling.LANCZOS)
    args.final.parent.mkdir(parents=True, exist_ok=True)
    final.save(args.final, format="PNG", optimize=True)

    print(
        json.dumps(
            {"stage3": metrics(stage3), "final": metrics(final)},
            ensure_ascii=False,
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
