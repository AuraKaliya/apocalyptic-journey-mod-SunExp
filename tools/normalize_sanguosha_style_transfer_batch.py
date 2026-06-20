from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
BATCH_DIR = ROOT / "tools" / "previews" / "sanguosha_official_style_transfer_batch"
RAW_DIR = BATCH_DIR / "raw"
FINAL_DIR = BATCH_DIR / "final"
OFFICIAL_DIR = ROOT / "tools" / "previews" / "sanguosha_cached_refs"

CARD_NAMES = [
    "\u6740",
    "\u95ea",
    "\u6843",
    "\u9152",
    "\u51b3\u6597",
    "\u65e0\u4e2d\u751f\u6709",
    "\u8fc7\u6cb3\u62c6\u6865",
    "\u987a\u624b\u7275\u7f8a",
    "\u5357\u86ee\u5165\u4fb5",
    "\u4e07\u7bad\u9f50\u53d1",
    "\u6843\u56ed\u7ed3\u4e49",
    "\u65e0\u61c8\u53ef\u51fb",
    "\u95ea\u7535",
    "\u4e94\u8c37\u4e30\u767b",
    "\u94c1\u7d22\u8fde\u73af",
    "\u706b\u653b",
    "\u5175\u7cae\u5bf8\u65ad",
    "\u85e4\u7532",
    "\u53e4\u952d\u5200",
    "\u4e50\u4e0d\u601d\u8700",
]

# Freshly sampled from the user-approved Image 2 style reference.
PALETTE = [
    (5, 3, 41),
    (16, 10, 55),
    (245, 222, 160),
    (190, 123, 57),
    (194, 47, 74),
    (99, 30, 49),
]


def palette_image() -> Image.Image:
    image = Image.new("P", (1, 1))
    flattened = [channel for color in PALETTE for channel in color]
    padding = list(PALETTE[0]) * (256 - len(PALETTE))
    image.putpalette(flattened + padding)
    return image


def normalize(source: Path, destination: Path) -> None:
    image = Image.open(source).convert("RGB")
    image = image.resize((256, 256), Image.Resampling.LANCZOS)
    image = image.quantize(
        palette=palette_image(),
        dither=Image.Dither.NONE,
    ).convert("RGB")
    image = image.resize((512, 512), Image.Resampling.NEAREST)
    image.save(destination)


def font(size: int):
    for path in (Path("C:/Windows/Fonts/msyh.ttc"), Path("C:/Windows/Fonts/simhei.ttf")):
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def create_contact_sheet() -> None:
    columns = 5
    cell_width = 190
    cell_height = 212
    rows = (len(CARD_NAMES) + columns - 1) // columns
    sheet = Image.new("RGB", (columns * cell_width, rows * cell_height), PALETTE[0])
    draw = ImageDraw.Draw(sheet)
    label_font = font(17)
    for index, name in enumerate(CARD_NAMES):
        x = index % columns * cell_width + 10
        y = index // columns * cell_height + 8
        image = Image.open(FINAL_DIR / f"{name}.png").convert("RGB")
        sheet.paste(image.resize((170, 170), Image.Resampling.NEAREST), (x, y))
        draw.text((x, y + 178), name, fill=PALETTE[2], font=label_font)
    sheet.save(BATCH_DIR / "sanguosha-style-transfer-contact-sheet.png")


def create_comparison_sheet() -> None:
    columns = 4
    cell_width = 344
    cell_height = 190
    rows = (len(CARD_NAMES) + columns - 1) // columns
    sheet = Image.new("RGB", (columns * cell_width, rows * cell_height), PALETTE[0])
    draw = ImageDraw.Draw(sheet)
    label_font = font(15)
    for index, name in enumerate(CARD_NAMES):
        x = index % columns * cell_width + 8
        y = index // columns * cell_height + 8
        official = Image.open(OFFICIAL_DIR / f"{name}.png").convert("RGB")
        redraw = Image.open(FINAL_DIR / f"{name}.png").convert("RGB")
        sheet.paste(official.resize((156, 156), Image.Resampling.LANCZOS), (x, y))
        sheet.paste(redraw.resize((156, 156), Image.Resampling.NEAREST), (x + 168, y))
        draw.text((x, y + 162), f"{name}  official / redraw", fill=PALETTE[2], font=label_font)
    sheet.save(BATCH_DIR / "sanguosha-official-vs-style-transfer.png")


def main() -> None:
    FINAL_DIR.mkdir(parents=True, exist_ok=True)
    for name in CARD_NAMES:
        normalize(RAW_DIR / f"{name}.png", FINAL_DIR / f"{name}.png")

    create_contact_sheet()
    create_comparison_sheet()

    for name in CARD_NAMES:
        image = Image.open(FINAL_DIR / f"{name}.png")
        colors = image.convert("RGB").getcolors(maxcolors=1_000_000)
        print(f"{name}: size={image.size}, mode={image.mode}, colors={len(colors or [])}")


if __name__ == "__main__":
    main()
