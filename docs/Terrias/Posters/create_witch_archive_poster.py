from __future__ import annotations

import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageOps


ROOT = Path(__file__).resolve().parents[3]
OUT_DIR = ROOT / "docs" / "Terrias" / "Posters"
OUT = OUT_DIR / "witch_archive_role_poster.png"

W, H = 1440, 2560

FONT_SERIF = Path("C:/Windows/Fonts/NotoSerifSC-VF.ttf")
FONT_SANS = Path("C:/Windows/Fonts/NotoSansSC-VF.ttf")
FONT_SANS_BOLD = Path("C:/Windows/Fonts/msyhbd.ttc")


def font(path: Path, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(path), size=size)


def load_rgba(path: str | Path) -> Image.Image:
    return Image.open(ROOT / path).convert("RGBA")


def scale_to_height(img: Image.Image, height: int) -> Image.Image:
    ratio = height / img.height
    return img.resize((int(img.width * ratio), height), Image.Resampling.LANCZOS)


def scale_to_width(img: Image.Image, width: int) -> Image.Image:
    ratio = width / img.width
    return img.resize((width, int(img.height * ratio)), Image.Resampling.LANCZOS)


def alpha_paste(base: Image.Image, layer: Image.Image, xy: tuple[int, int], opacity: float = 1.0) -> None:
    if opacity < 1:
        layer = layer.copy()
        a = layer.getchannel("A").point(lambda v: int(v * opacity))
        layer.putalpha(a)
    base.alpha_composite(layer, xy)


def add_glow(canvas: Image.Image, center: tuple[int, int], radius: int, color: tuple[int, int, int], strength: float) -> None:
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    x, y = center
    draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=(*color, int(255 * strength)))
    layer = layer.filter(ImageFilter.GaussianBlur(radius // 2))
    canvas.alpha_composite(layer)


def rounded_rect(
    draw: ImageDraw.ImageDraw,
    box: tuple[int, int, int, int],
    radius: int,
    fill: tuple[int, int, int, int],
    outline: tuple[int, int, int, int] | None = None,
    width: int = 1,
) -> None:
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def draw_centered(
    draw: ImageDraw.ImageDraw,
    text: str,
    y: int,
    fnt: ImageFont.FreeTypeFont,
    fill: tuple[int, int, int, int],
    shadow: tuple[int, int, int, int] | None = None,
) -> None:
    bbox = draw.textbbox((0, 0), text, font=fnt)
    x = (W - (bbox[2] - bbox[0])) // 2
    if shadow:
        draw.text((x + 3, y + 4), text, font=fnt, fill=shadow)
    draw.text((x, y), text, font=fnt, fill=fill)


def draw_spaced_centered(
    draw: ImageDraw.ImageDraw,
    text: str,
    y: int,
    fnt: ImageFont.FreeTypeFont,
    fill: tuple[int, int, int, int],
    spacing: int,
    shadow: tuple[int, int, int, int] | None = None,
) -> None:
    widths = [draw.textlength(ch, font=fnt) for ch in text]
    total = sum(widths) + spacing * (len(text) - 1)
    x = int((W - total) / 2)
    if shadow:
        sx = x
        for i, ch in enumerate(text):
            draw.text((sx + 2, y + 3), ch, font=fnt, fill=shadow)
            sx += int(widths[i] + spacing)
    for i, ch in enumerate(text):
        draw.text((x, y), ch, font=fnt, fill=fill)
        x += int(widths[i] + spacing)


def draw_vertical_line_pattern(canvas: Image.Image) -> None:
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    for x in range(90, W, 120):
        alpha = 10 if x < W // 2 else 13
        draw.line((x, 360, x + 80, H - 210), fill=(245, 230, 160, alpha), width=1)
    for x in range(140, W, 170):
        draw.line((x, 420, x - 110, H - 320), fill=(120, 150, 255, 11), width=1)
    canvas.alpha_composite(layer)


def draw_starfield(canvas: Image.Image) -> None:
    rng = random.Random(20260706)
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    for _ in range(520):
        x = rng.randrange(20, W - 20)
        y = rng.randrange(120, H - 80)
        if 650 < y < 1800 and 360 < x < 1110 and rng.random() < 0.55:
            continue
        warm_side = x < W // 2
        color = (255, 216, 122) if warm_side else (160, 185, 255)
        alpha = rng.randrange(35, 120)
        r = 1 if rng.random() < 0.82 else 2
        draw.ellipse((x - r, y - r, x + r, y + r), fill=(*color, alpha))
    canvas.alpha_composite(layer)


def make_background() -> Image.Image:
    img = Image.new("RGBA", (W, H))
    px = img.load()
    for y in range(H):
        t = y / (H - 1)
        for x in range(W):
            side = x / (W - 1)
            top = (8, 10, 28)
            bottom = (2, 3, 10)
            warm = max(0.0, 1.0 - side * 1.25)
            cool = max(0.0, (side - 0.25) * 1.25)
            r = int(top[0] * (1 - t) + bottom[0] * t + warm * 18 + cool * 7)
            g = int(top[1] * (1 - t) + bottom[1] * t + warm * 8 + cool * 8)
            b = int(top[2] * (1 - t) + bottom[2] * t + warm * 2 + cool * 30)
            vignette = 1.0 - 0.36 * math.dist((x / W, y / H), (0.5, 0.48))
            px[x, y] = (int(r * vignette), int(g * vignette), int(b * vignette), 255)

    add_glow(img, (360, 980), 620, (255, 190, 74), 0.34)
    add_glow(img, (1040, 1040), 660, (90, 120, 255), 0.36)
    add_glow(img, (720, 1540), 520, (255, 235, 180), 0.10)
    draw_vertical_line_pattern(img)
    draw_starfield(img)

    shard = load_rgba("SunExp/ModResource/Images/CG/炽冕崩落/matte_00020.png")
    shard = ImageOps.fit(shard, (W, W), method=Image.Resampling.LANCZOS, centering=(0.52, 0.45))
    shard = shard.filter(ImageFilter.GaussianBlur(1.8))
    a = shard.getchannel("A").point(lambda v: int(v * 0.17))
    shard.putalpha(a)
    alpha_paste(img, shard, (0, 390))

    shade = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(shade)
    d.rectangle((0, 0, W, 430), fill=(0, 0, 0, 120))
    d.rectangle((0, H - 430, W, H), fill=(0, 0, 0, 132))
    shade = shade.filter(ImageFilter.GaussianBlur(40))
    img.alpha_composite(shade)
    return img


def place_character(canvas: Image.Image, path: str, height: int, center_x: int, top: int, glow: tuple[int, int, int]) -> None:
    char = scale_to_height(load_rgba(path), height)
    x = int(center_x - char.width / 2)
    y = top

    silhouette = Image.new("RGBA", char.size, (*glow, 0))
    silhouette.putalpha(char.getchannel("A"))
    silhouette = silhouette.filter(ImageFilter.GaussianBlur(26))
    alpha_paste(canvas, silhouette, (x, y), 0.42)

    soft_shadow = Image.new("RGBA", char.size, (0, 0, 0, 0))
    soft_shadow.putalpha(char.getchannel("A").filter(ImageFilter.GaussianBlur(10)))
    alpha_paste(canvas, soft_shadow, (x + 18, y + 26), 0.34)
    alpha_paste(canvas, char, (x, y), 1)


def draw_icon_row(canvas: Image.Image, icons: list[str], start_x: int, y: int, accent: tuple[int, int, int]) -> None:
    draw = ImageDraw.Draw(canvas)
    for i, icon_path in enumerate(icons):
        icon = load_rgba(icon_path)
        icon = ImageOps.contain(icon, (72, 72), method=Image.Resampling.LANCZOS)
        x = start_x + i * 88
        rounded_rect(draw, (x - 8, y - 8, x + 80, y + 80), 18, (8, 10, 20, 160), (*accent, 140), 2)
        alpha_paste(canvas, icon, (x + (72 - icon.width) // 2, y + (72 - icon.height) // 2), 0.95)


def draw_character_card(
    canvas: Image.Image,
    box: tuple[int, int, int, int],
    name: str,
    title: str,
    tags: list[str],
    accent: tuple[int, int, int],
    icons: list[str],
) -> None:
    draw = ImageDraw.Draw(canvas)
    x1, y1, x2, y2 = box
    panel = Image.new("RGBA", (x2 - x1, y2 - y1), (0, 0, 0, 0))
    pd = ImageDraw.Draw(panel)
    pd.rounded_rectangle(
        (0, 0, x2 - x1, y2 - y1),
        radius=28,
        fill=(8, 11, 26, 182),
        outline=(*accent, 146),
        width=2,
    )
    panel = panel.filter(ImageFilter.GaussianBlur(0.2))
    canvas.alpha_composite(panel, (x1, y1))

    small = font(FONT_SANS, 28)
    medium = font(FONT_SANS_BOLD, 40)
    big = font(FONT_SERIF, 70)
    draw.text((x1 + 42, y1 + 36), name, font=big, fill=(255, 248, 222, 255))
    draw.text((x1 + 46, y1 + 122), title, font=medium, fill=(*accent, 245))
    draw.line((x1 + 42, y1 + 184, x2 - 42, y1 + 184), fill=(*accent, 120), width=2)

    tx = x1 + 42
    ty = y1 + 218
    for tag in tags:
        bbox = draw.textbbox((0, 0), tag, font=small)
        tw = bbox[2] - bbox[0] + 34
        rounded_rect(draw, (tx, ty, tx + tw, ty + 48), 24, (*accent, 44), (*accent, 120), 1)
        draw.text((tx + 17, ty + 8), tag, font=small, fill=(246, 246, 255, 235))
        tx += tw + 16

    draw_icon_row(canvas, icons, x1 + 42, y1 + 296, accent)


def draw_pack_emblems(canvas: Image.Image) -> None:
    draw = ImageDraw.Draw(canvas)
    paths = [
        "SunExp/ModResource/Images/CardPack/cardpack_radiant_spark.png",
        "SunExp/ModResource/Images/CardPack/cardpack_ember_crown.png",
        "SunExp/ModResource/Images/CardPack/cardpack_solar_canopy.png",
        "SunExp/ModResource/Images/CardPack/cardpack_morning_star_overture.png",
    ]
    x = 500
    y = 2140
    for i, p in enumerate(paths):
        img = load_rgba(p)
        img = ImageOps.fit(img, (126, 188), method=Image.Resampling.LANCZOS)
        dx = x + i * 114
        if i == 3:
            dx += 18
        rounded_rect(draw, (dx - 5, y - 5, dx + 131, y + 193), 12, (0, 0, 0, 140), (255, 218, 122, 84), 1)
        alpha_paste(canvas, img, (dx, y), 0.9)


def draw_header(canvas: Image.Image) -> None:
    draw = ImageDraw.Draw(canvas)
    title = font(FONT_SERIF, 134)
    subtitle = font(FONT_SANS, 34)
    copy = font(FONT_SERIF, 42)

    draw_centered(draw, "魔女档案", 120, title, (255, 244, 205, 255), (0, 0, 0, 190))
    draw_spaced_centered(draw, "角色专题 / Witch Archive", 282, subtitle, (172, 190, 255, 230), 2)
    draw_spaced_centered(
        draw,
        "黑日之后，黄金梦碎，晨星晦暗，白曜升濯",
        356,
        copy,
        (255, 248, 226, 245),
        4,
        (0, 0, 0, 180),
    )

    line = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(line)
    d.line((230, 465, 1210, 465), fill=(255, 220, 128, 95), width=2)
    d.line((420, 480, 1020, 480), fill=(125, 158, 255, 70), width=1)
    canvas.alpha_composite(line)


def draw_footer(canvas: Image.Image) -> None:
    draw = ImageDraw.Draw(canvas)
    f = font(FONT_SANS, 28)
    draw_spaced_centered(draw, "SUNEXP MOD  ·  WITCH ARCHIVE 01", H - 116, f, (184, 194, 225, 190), 2)


def build() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    canvas = make_background()
    draw_header(canvas)

    place_character(canvas, "SunExp/ModResource/Images/Character/WuNa.png", 1210, 405, 650, (255, 190, 82))
    place_character(canvas, "SunExp/ModResource/Images/Character/Loneer.png", 1130, 1030, 720, (122, 142, 255))

    veil = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    vd = ImageDraw.Draw(veil)
    vd.rectangle((0, 1760, W, H), fill=(0, 0, 0, 120))
    veil = veil.filter(ImageFilter.GaussianBlur(32))
    canvas.alpha_composite(veil)

    draw_character_card(
        canvas,
        (88, 1760, 674, 2160),
        "乌娜",
        "曜日魔女",
        ["日耀", "余烬", "圣冕显化"],
        (255, 203, 102),
        [
            "SunExp/ModResource/Images/Buff/SunExp/solar_radiance.png",
            "SunExp/ModResource/Images/Buff/SunExp/wuna_ember.png",
            "SunExp/ModResource/Images/Buff/SunExp/solar_crown.png",
        ],
    )
    draw_character_card(
        canvas,
        (766, 1760, 1352, 2160),
        "洛奈尔",
        "晨星魔女",
        ["星谱", "奇迹时钟", "关键牌复制"],
        (154, 175, 255),
        [
            "SunExp/ModResource/Images/Buff/Loneer/星谱.png",
            "SunExp/ModResource/Images/Buff/Loneer/奇迹时钟.png",
            "SunExp/ModResource/Images/Buff/Loneer/星辉.png",
        ],
    )

    draw_pack_emblems(canvas)
    draw_footer(canvas)

    # Export a social-size derivative beside the master for easier review.
    canvas.save(OUT)
    social = canvas.resize((1080, 1920), Image.Resampling.LANCZOS)
    social.save(OUT_DIR / "witch_archive_role_poster_1080x1920.png")
    print(OUT)
    print(OUT_DIR / "witch_archive_role_poster_1080x1920.png")


if __name__ == "__main__":
    build()
