param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot\..").Path
)

$ErrorActionPreference = "Stop"

$script = @'
from PIL import Image, ImageDraw, ImageFilter
from pathlib import Path

repo = Path(r"{repo}")
out_dir = repo / "TestMods/GoldExp/ModResource/Images/Buff/GoldExp"
preview_dir = repo / "tools/previews"
out_dir.mkdir(parents=True, exist_ok=True)
preview_dir.mkdir(parents=True, exist_ok=True)

FINAL = 256
GRID = 64

BG = (4, 2, 48)
INK = (4, 2, 18)
INK2 = (18, 14, 42)
MAGENTA = (205, 82, 145)
MAGENTA_DARK = (130, 43, 101)
CREAM = (253, 244, 180)
PALE = (245, 224, 247)
PERI = (85, 92, 177)
PERI_DARK = (47, 54, 124)
GOLD = (229, 179, 64)
OCHRE = (168, 111, 38)
MINT = (216, 243, 95)
MINT2 = (109, 244, 181)

PALETTE = [
    BG, INK, INK2, MAGENTA, MAGENTA_DARK, CREAM, PALE,
    PERI, PERI_DARK, GOLD, OCHRE, MINT, MINT2,
]

SOURCES = {
    "debt": preview_dir / "goldexp-buff-ai-source-debt.png",
    "false_gold": preview_dir / "goldexp-buff-ai-source-false_gold.png",
    "midas_raven_trait": preview_dir / "goldexp-buff-ai-source-midas_raven_trait.png",
}

def palette_image():
    pal = Image.new("P", (1, 1))
    flat = []
    for color in PALETTE:
        flat.extend(color)
    flat.extend([0, 0, 0] * (256 - len(PALETTE)))
    pal.putpalette(flat)
    return pal

def normalize_dark_background(im):
    im = im.convert("RGB").resize((FINAL, FINAL), Image.Resampling.LANCZOS)
    pix = im.load()
    for y in range(FINAL):
        for x in range(FINAL):
            r, g, b = pix[x, y]
            maxc = max(r, g, b)
            minc = min(r, g, b)
            if maxc < 34 and (maxc - minc) < 18:
                pix[x, y] = BG
    return im

def subject_mask(im):
    mask = Image.new("L", (FINAL, FINAL), 0)
    pix = im.load()
    mp = mask.load()
    for y in range(FINAL):
        for x in range(FINAL):
            r, g, b = pix[x, y]
            distance = abs(r - BG[0]) + abs(g - BG[1]) + abs(b - BG[2])
            if distance > 50 and max(r, g, b) > 38:
                mp[x, y] = 255
    return mask

def postprocess(name, src_path, pal):
    src = Image.open(src_path)
    normalized = normalize_dark_background(src)
    mask = subject_mask(normalized)

    # Expand the detected subject before paste so the simplified icon keeps
    # the source silhouette but gains the bold outline needed at buff size.
    outline_mask = mask.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.MaxFilter(5))
    outlined = Image.new("RGB", (FINAL, FINAL), BG)
    outlined.paste(INK, mask=outline_mask)
    outlined.paste(normalized, mask=mask)

    small = outlined.resize((GRID, GRID), Image.Resampling.BOX)
    quantized = small.quantize(palette=pal, dither=Image.Dither.NONE).convert("RGBA")
    final = quantized.resize((FINAL, FINAL), Image.Resampling.NEAREST)
    final.save(out_dir / f"{name}.png")
    return final

def make_sheets(finals):
    order = ["debt", "false_gold", "midas_raven_trait"]

    source_sheet = Image.new("RGBA", (FINAL * 3, FINAL + 44), (20, 18, 28, 255))
    sd = ImageDraw.Draw(source_sheet)
    for i, name in enumerate(order):
        src = Image.open(SOURCES[name]).convert("RGBA").resize((FINAL, FINAL), Image.Resampling.LANCZOS)
        source_sheet.alpha_composite(src, (i * FINAL, 0))
        sd.text((i * FINAL + 8, FINAL + 12), f"source {name}", fill=(240, 230, 200, 255))
    source_sheet.save(preview_dir / "goldexp-buff-icons-source-sheet.png")

    sheet = Image.new("RGBA", (FINAL * 3, FINAL + 44), (20, 18, 28, 255))
    d = ImageDraw.Draw(sheet)
    for i, name in enumerate(order):
        sheet.alpha_composite(finals[name], (i * FINAL, 0))
        d.text((i * FINAL + 8, FINAL + 12), f"{name}.png", fill=(240, 230, 200, 255))
    sheet.save(preview_dir / "goldexp-buff-icons-after.png")

    thumb = Image.new("RGBA", (128 * 3, 172), (20, 18, 28, 255))
    td = ImageDraw.Draw(thumb)
    for i, name in enumerate(order):
        t = finals[name].resize((32, 32), Image.Resampling.BOX).resize((128, 128), Image.Resampling.NEAREST)
        thumb.alpha_composite(t, (i * 128, 0))
        td.text((i * 128 + 4, 136), name[:18], fill=(240, 230, 200, 255))
    thumb.save(preview_dir / "goldexp-buff-icons-32px-check.png")

pal = palette_image()
finals = {}
for name, src_path in SOURCES.items():
    if not src_path.exists():
        raise FileNotFoundError(src_path)
    finals[name] = postprocess(name, src_path, pal)

make_sheets(finals)

for name, im in finals.items():
    colors = len(im.getcolors(maxcolors=1000000) or [])
    print(f"{name}.png {im.size} {im.mode} colors={colors}")
print("source", (preview_dir / "goldexp-buff-icons-source-sheet.png").resolve())
print("after", (preview_dir / "goldexp-buff-icons-after.png").resolve())
print("small", (preview_dir / "goldexp-buff-icons-32px-check.png").resolve())
'@

$script = $script.Replace("{repo}", ($RepoRoot -replace "\\", "\\"))
$script | python -
