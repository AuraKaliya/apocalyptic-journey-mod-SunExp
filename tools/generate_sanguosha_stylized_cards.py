from __future__ import annotations

import hashlib
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
SOURCE_DIR = ROOT / "TestMods" / "SanGuoShaExp" / "ModResource" / "Images"
OFFICIAL_REFERENCE_DIR = ROOT / "tools" / "previews" / "sanguosha_cached_refs"
PREVIEW_DIR = ROOT / "tools" / "previews" / "sanguosha_stylized_batch"
ACCEPTED_SHA = (
    ROOT
    / "tools"
    / "previews"
    / "sanguosha_style_trials"
    / "sha-official-style-redraw-selected.png"
)

SIZE = 128
BG = (5, 4, 34)
INK = (1, 1, 12)
CREAM = (246, 232, 164)
OCHRE = (190, 138, 54)
SEPIA = (86, 54, 35)
CRIMSON = (201, 54, 92)
PALETTE = (BG, INK, CREAM, OCHRE, SEPIA, CRIMSON)

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
]
EXCLUDED = "\u501f\u4e1c\u98ce"


class Brush:
    def __init__(self, name: str):
        seed = int(hashlib.sha256(name.encode("utf-8")).hexdigest()[:16], 16)
        self.rng = random.Random(seed)
        self.image = Image.new("RGB", (SIZE, SIZE), BG)
        self.draw = ImageDraw.Draw(self.image)

    def _jitter(self, points, amount=1):
        return [
            (
                x + self.rng.randint(-amount, amount),
                y + self.rng.randint(-amount, amount),
            )
            for x, y in points
        ]

    def poly(self, points, color, jitter=1, passes=1):
        for _ in range(passes):
            self.draw.polygon(self._jitter(points, jitter), fill=color)

    def line(self, points, color, width=2, jitter=1, passes=1):
        for _ in range(passes):
            self.draw.line(
                self._jitter(points, jitter),
                fill=color,
                width=width,
                joint="curve",
            )

    def ellipse(self, box, color, jitter=1):
        x0, y0, x1, y1 = box
        j = self.rng.randint(-jitter, jitter)
        k = self.rng.randint(-jitter, jitter)
        self.draw.ellipse((x0 + j, y0 + k, x1 - j, y1 - k), fill=color)

    def arc(self, box, start, end, color, width=2):
        self.draw.arc(box, start, end, fill=color, width=width)

    def outlined_poly(self, outer, inner, fill, inset_color=None):
        self.poly(outer, INK, jitter=2, passes=2)
        self.poly(inner, fill, jitter=1)
        if inset_color:
            self.line(inner + [inner[0]], inset_color, width=1, jitter=1)

    def outlined_ellipse(self, box, fill, inset=4):
        self.ellipse(box, INK, jitter=2)
        x0, y0, x1, y1 = box
        self.ellipse((x0 + inset, y0 + inset, x1 - inset, y1 - inset), fill, jitter=1)

    def flecks(self, count=80, colors=(BG, INK, OCHRE, CRIMSON)):
        for _ in range(count):
            x = self.rng.randrange(4, 124)
            y = self.rng.randrange(4, 124)
            color = self.rng.choice(colors)
            if self.rng.random() < 0.8:
                self.draw.point((x, y), fill=color)
            else:
                self.draw.rectangle((x, y, x + 1, y), fill=color)

    def finish(self):
        pixels = self.image.load()
        for y in range(SIZE):
            for x in range(SIZE):
                color = pixels[x, y]
                pixels[x, y] = min(
                    PALETTE,
                    key=lambda item: sum(
                        (color[channel] - item[channel]) ** 2
                        for channel in range(3)
                    ),
                )
        return self.image.resize((512, 512), Image.Resampling.NEAREST)


def draw_shan(b: Brush):
    # Official composition: a slim figure turning away from a fan of light.
    b.poly([(4, 24), (44, 17), (71, 36), (61, 61), (22, 72), (2, 60)], CREAM, 2)
    for y in (31, 42, 54, 66):
        b.line([(4, 49), (60, y)], OCHRE, b.rng.choice((1, 2)), 1)
    b.line([(80, 13), (70, 35), (65, 60), (77, 84), (96, 110)], INK, 8, 2, 2)
    b.line([(79, 18), (72, 40), (70, 60), (82, 79)], SEPIA, 4, 1)
    b.ellipse((70, 25, 88, 45), INK, 2)
    b.line([(68, 50), (48, 63), (34, 82)], INK, 6, 2)
    b.line([(70, 54), (50, 66), (37, 83)], CREAM, 2, 1)
    b.line([(59, 47), (76, 58), (99, 61), (116, 54)], CRIMSON, 3, 2)
    b.line([(74, 76), (57, 91), (45, 113)], INK, 7, 2)


def draw_tao(b: Brush):
    # Peach held forward, matching the official fruit-first composition.
    b.line([(26, 104), (51, 91), (68, 72)], INK, 13, 2, 2)
    b.line([(29, 103), (52, 89), (68, 73)], SEPIA, 7, 1)
    b.outlined_ellipse((42, 29, 98, 87), CREAM, 5)
    b.poly([(68, 38), (62, 57), (70, 78), (80, 62), (84, 42)], OCHRE, 2)
    b.line([(70, 42), (70, 76)], CRIMSON, 3, 1)
    b.outlined_poly(
        [(66, 33), (44, 13), (21, 18), (35, 39)],
        [(61, 31), (44, 20), (29, 22), (39, 34)],
        OCHRE,
    )
    b.line([(46, 26), (67, 39)], CREAM, 2, 1)


def draw_jiu(b: Brush):
    # Three rear cups and one large wine jar from the official card.
    for x in (20, 52, 84):
        b.outlined_ellipse((x, 16, x + 26, 32), CREAM, 4)
        b.outlined_poly(
            [(x + 3, 24), (x + 23, 24), (x + 21, 49), (x + 6, 49)],
            [(x + 7, 28), (x + 19, 28), (x + 17, 44), (x + 9, 44)],
            OCHRE,
        )
    b.outlined_poly(
        [(35, 50), (47, 39), (82, 39), (95, 52), (92, 105), (76, 116), (47, 111), (33, 98)],
        [(44, 55), (52, 47), (77, 47), (87, 55), (84, 98), (74, 107), (50, 104), (42, 95)],
        SEPIA,
    )
    b.outlined_ellipse((48, 41, 80, 55), CREAM, 4)
    b.line([(55, 72), (65, 65), (74, 74), (65, 88), (55, 72)], CREAM, 3, 1)
    b.line([(85, 77), (109, 70), (116, 84), (96, 91)], CRIMSON, 3, 2)


def draw_juedou(b: Brush):
    # Two opposing warriors and crossing weapons.
    b.ellipse((16, 35, 34, 54), INK, 2)
    b.poly([(14, 51), (37, 49), (49, 88), (29, 108), (11, 87)], SEPIA, 2)
    b.ellipse((93, 34, 112, 54), INK, 2)
    b.poly([(89, 51), (114, 50), (119, 87), (99, 108), (80, 84)], OCHRE, 2)
    b.line([(10, 104), (45, 66), (111, 17)], INK, 8, 2, 2)
    b.line([(12, 101), (47, 64), (112, 19)], CREAM, 3, 1)
    b.line([(116, 105), (81, 65), (19, 16)], INK, 8, 2, 2)
    b.line([(114, 101), (79, 63), (18, 18)], CRIMSON, 3, 1)
    b.poly([(52, 58), (64, 48), (75, 59), (64, 72)], CREAM, 1)


def draw_wuzhong(b: Brush):
    # An empty dark face/mask emerging from the official card's bright tear.
    b.poly([(3, 29), (31, 14), (62, 17), (83, 37), (73, 63), (39, 78), (6, 70)], CREAM, 3)
    for end in ((22, 5), (45, 3), (73, 11), (91, 26), (96, 48), (77, 78), (48, 91), (15, 88)):
        b.line([(38, 51), end], OCHRE, 2, 1)
    b.outlined_poly(
        [(43, 24), (80, 21), (101, 49), (91, 88), (65, 109), (37, 84), (30, 51)],
        [(49, 31), (75, 29), (91, 52), (83, 80), (64, 99), (44, 78), (37, 53)],
        SEPIA,
    )
    b.poly([(46, 50), (57, 43), (61, 57)], INK, 1)
    b.poly([(70, 44), (82, 51), (70, 59)], INK, 1)
    b.line([(49, 76), (62, 82), (78, 74)], INK, 4, 1)
    b.line([(30, 23), (48, 40), (94, 89)], CRIMSON, 3, 2)
    b.line([(17, 100), (42, 90), (55, 109)], CREAM, 2, 1)


def draw_guohe(b: Brush):
    # Broken bridge deck, split diagonally like the official scene.
    b.line([(4, 103), (46, 68), (113, 25)], INK, 18, 2, 2)
    b.line([(6, 101), (47, 67), (111, 27)], SEPIA, 11, 1)
    for t in range(8):
        x = 14 + t * 13
        y = 96 - t * 9
        b.line([(x - 4, y - 8), (x + 7, y + 7)], CREAM, 2, 1)
    b.poly([(52, 72), (64, 60), (69, 69), (58, 81)], BG, 1)
    b.line([(56, 78), (69, 63)], CRIMSON, 4, 1)
    b.line([(17, 45), (41, 67)], OCHRE, 3, 1)
    b.line([(83, 68), (109, 92)], OCHRE, 3, 1)
    for x, y in ((55, 56), (66, 78), (72, 59), (48, 85)):
        b.poly([(x, y), (x + 5, y - 4), (x + 8, y + 2)], CREAM, 1)


def draw_shunshou(b: Brush):
    # A hooked hand quietly pulling a sheep-horn silhouette.
    b.outlined_ellipse((60, 47, 103, 89), CREAM, 5)
    b.arc((48, 29, 84, 66), 155, 355, INK, 7)
    b.arc((79, 29, 115, 66), 185, 380, INK, 7)
    b.arc((53, 34, 80, 59), 165, 350, OCHRE, 3)
    b.arc((83, 34, 110, 59), 190, 375, OCHRE, 3)
    b.ellipse((69, 58, 78, 67), INK, 1)
    b.ellipse((86, 58, 95, 67), INK, 1)
    b.poly([(78, 70), (84, 77), (90, 70), (87, 85), (80, 85)], OCHRE, 1)
    b.line([(9, 97), (34, 82), (58, 75), (76, 82)], INK, 15, 2, 2)
    b.line([(13, 94), (37, 83), (58, 79), (74, 84)], SEPIA, 8, 1)
    # Two hooked fingers make the stealing gesture readable at thumbnail size.
    b.line([(31, 81), (25, 58), (35, 38)], INK, 8, 2)
    b.line([(45, 79), (43, 58), (52, 45)], INK, 7, 2)
    b.line([(31, 78), (29, 58), (38, 42)], CRIMSON, 3, 1)
    b.line([(98, 75), (113, 69), (119, 77)], OCHRE, 3, 1)


def draw_nanman(b: Brush):
    # Broad barbarian mask, horned helmet, and heavy shoulders.
    b.poly([(13, 102), (23, 59), (42, 38), (64, 27), (88, 39), (108, 60), (117, 105)], INK, 3, 2)
    b.poly([(24, 97), (31, 61), (47, 45), (64, 36), (82, 46), (99, 64), (107, 98)], SEPIA, 2)
    b.poly([(46, 38), (35, 9), (56, 29)], OCHRE, 2)
    b.poly([(80, 39), (94, 10), (73, 29)], OCHRE, 2)
    b.poly([(44, 56), (57, 50), (61, 64), (47, 69)], CREAM, 1)
    b.poly([(68, 51), (83, 56), (80, 69), (65, 63)], CREAM, 1)
    b.line([(63, 59), (64, 86)], CRIMSON, 4, 1)
    b.line([(48, 87), (64, 96), (81, 86)], CREAM, 3, 1)
    b.line([(20, 77), (5, 69)], CRIMSON, 3, 1)
    b.line([(108, 76), (123, 68)], CRIMSON, 3, 1)


def draw_wanjian(b: Brush):
    # Arrow fan converging on the center, directly echoing the official card.
    center = (64, 68)
    for index, end in enumerate(
        [(8, 10), (27, 4), (47, 1), (80, 1), (102, 5), (121, 16), (124, 43), (120, 99), (99, 122), (27, 121), (6, 99)]
    ):
        color = CRIMSON if index in (1, 5, 8) else CREAM
        b.line([end, center], INK, 5, 1)
        b.line([end, center], color, 2, 1)
        x, y = end
        b.poly([(x, y), (x + (64 - x) // 8 + 3, y + (68 - y) // 8), (x + (64 - x) // 8, y + (68 - y) // 8 + 3)], color, 1)
    b.outlined_ellipse((50, 54, 78, 82), OCHRE, 5)
    b.poly([(58, 61), (72, 61), (76, 73), (64, 79), (54, 72)], INK, 1)


def draw_taoyuan(b: Brush):
    # Oath table with three cups and peach branches.
    b.outlined_poly(
        [(15, 58), (112, 58), (105, 105), (22, 105)],
        [(22, 64), (105, 64), (98, 97), (29, 97)],
        SEPIA,
    )
    for x in (34, 58, 82):
        b.outlined_ellipse((x, 45, x + 15, 57), CREAM, 3)
        b.poly([(x + 3, 52), (x + 12, 52), (x + 10, 67), (x + 5, 67)], OCHRE, 1)
    b.line([(12, 45), (34, 31), (58, 28)], INK, 5, 1)
    b.line([(115, 45), (94, 29), (72, 27)], INK, 5, 1)
    for x, y in ((28, 30), (42, 24), (91, 27), (103, 35)):
        b.outlined_ellipse((x - 6, y - 6, x + 6, y + 6), CREAM, 3)
    b.line([(30, 83), (64, 74), (98, 84)], CRIMSON, 3, 2)


def draw_wuxie(b: Brush):
    # Stone guardian head compressed into a defensive emblem.
    b.poly([(15, 94), (23, 43), (46, 20), (79, 17), (105, 42), (115, 91), (94, 112), (36, 113)], INK, 3, 2)
    b.poly([(25, 89), (31, 48), (50, 29), (77, 27), (96, 46), (105, 88), (88, 102), (41, 102)], OCHRE, 2)
    b.poly([(38, 50), (51, 43), (58, 57), (43, 63)], CREAM, 1)
    b.poly([(70, 43), (87, 50), (82, 64), (67, 57)], CREAM, 1)
    b.poly([(53, 66), (66, 58), (76, 68), (65, 79)], INK, 1)
    b.line([(42, 86), (64, 93), (88, 85)], INK, 6, 1)
    b.line([(47, 85), (64, 89), (83, 84)], CRIMSON, 2, 1)
    for x in (29, 98):
        b.line([(x, 39), (x - 8, 24)], SEPIA, 4, 1)


def draw_shandian(b: Brush):
    # Official central lightning stroke with a few secondary forks.
    bolt = [(70, 4), (47, 50), (62, 50), (42, 89), (59, 82), (50, 124), (88, 68), (71, 69), (93, 27), (74, 35)]
    b.poly(bolt, INK, 2, 2)
    inner = [(69, 10), (53, 47), (68, 45), (50, 78), (66, 70), (56, 108), (81, 65), (65, 65), (86, 31), (70, 39)]
    b.poly(inner, CREAM, 1)
    b.line([(47, 51), (22, 34), (8, 42)], CRIMSON, 3, 1)
    b.line([(62, 70), (93, 88), (116, 81)], OCHRE, 3, 1)
    b.line([(78, 35), (103, 18), (119, 24)], CREAM, 2, 1)


def draw_wugu(b: Brush):
    # Granary and bound grain sheaves, based on the official storehouse image.
    b.outlined_poly(
        [(25, 45), (64, 18), (105, 45), (100, 112), (29, 112)],
        [(33, 49), (64, 29), (96, 49), (92, 103), (37, 103)],
        SEPIA,
    )
    b.line([(25, 45), (105, 45)], CREAM, 4, 1)
    b.poly([(53, 67), (77, 67), (79, 105), (51, 105)], INK, 1)
    b.poly([(58, 72), (72, 72), (73, 103), (57, 103)], OCHRE, 1)
    for x in (16, 28, 98, 110):
        b.line([(x, 111), (x + (64 - x) // 4, 63)], INK, 5, 1)
        b.line([(x + 2, 108), (x + (64 - x) // 4, 66)], CREAM, 2, 1)
    b.line([(13, 88), (38, 82)], CRIMSON, 2, 1)
    b.line([(90, 83), (116, 91)], CRIMSON, 2, 1)


def draw_tiesuo(b: Brush):
    # Two heavy chain arcs around a bright impact center.
    for box, start, end in [((7, 18, 81, 96), 230, 60), ((47, 23, 121, 104), 50, 235)]:
        b.arc(box, start, end, INK, 11)
        b.arc(box, start, end, OCHRE, 5)
    for x, y in ((24, 35), (35, 28), (48, 25), (94, 37), (104, 49), (106, 65), (85, 91), (73, 98), (39, 87), (28, 76)):
        b.outlined_ellipse((x - 7, y - 4, x + 7, y + 5), SEPIA, 3)
    for angle in range(0, 360, 45):
        import math

        x = 64 + int(math.cos(math.radians(angle)) * 35)
        y = 63 + int(math.sin(math.radians(angle)) * 35)
        b.line([(64, 63), (x, y)], CRIMSON if angle % 90 else CREAM, 3, 1)
    b.outlined_ellipse((48, 47, 80, 79), CREAM, 6)


def draw_huogong(b: Brush):
    # Burning brazier/chest from the official card.
    b.outlined_poly(
        [(25, 74), (101, 74), (94, 112), (34, 112)],
        [(33, 80), (93, 80), (87, 104), (41, 104)],
        SEPIA,
    )
    b.line([(24, 75), (101, 75)], CREAM, 4, 1)
    flames = [
        [(37, 74), (31, 48), (47, 59), (50, 24), (62, 52), (68, 13), (81, 54), (96, 36), (91, 75)],
        [(45, 72), (42, 54), (55, 62), (60, 38), (69, 60), (80, 44), (83, 72)],
    ]
    b.poly(flames[0], INK, 3, 2)
    b.poly(flames[1], CRIMSON, 2)
    b.poly([(57, 71), (57, 59), (65, 64), (71, 50), (75, 72)], CREAM, 1)
    b.line([(34, 96), (92, 96)], OCHRE, 2, 1)


def draw_bingliang(b: Brush):
    # Grain sack and severing diagonal blade/rope.
    b.outlined_poly(
        [(29, 35), (45, 22), (84, 23), (99, 39), (95, 105), (78, 117), (45, 114), (28, 100)],
        [(38, 41), (49, 31), (80, 31), (90, 43), (87, 98), (75, 108), (49, 106), (37, 96)],
        OCHRE,
    )
    b.line([(43, 39), (86, 39)], CREAM, 4, 1)
    b.line([(45, 28), (84, 29)], INK, 4, 1)
    b.line([(9, 108), (49, 70), (113, 19)], INK, 10, 2, 2)
    b.line([(12, 105), (51, 69), (112, 22)], CREAM, 3, 1)
    b.line([(49, 76), (63, 62)], CRIMSON, 4, 1)
    for x, y in ((41, 93), (53, 101), (91, 90), (99, 102)):
        b.ellipse((x - 3, y - 2, x + 4, y + 3), SEPIA, 1)


def draw_tengjia(b: Brush):
    # Armor torso woven from broad vine bands.
    outer = [(44, 14), (84, 14), (104, 38), (94, 108), (72, 119), (54, 114), (32, 104), (24, 38)]
    inner = [(48, 24), (80, 24), (94, 42), (86, 100), (70, 109), (55, 105), (41, 98), (34, 42)]
    b.outlined_poly(outer, inner, SEPIA)
    for y in (39, 54, 69, 84, 99):
        b.line([(35, y), (93, y - 3)], OCHRE, 5, 2)
        b.line([(39, y - 2), (90, y - 5)], CREAM, 1, 1)
    for x in (47, 63, 79):
        b.line([(x, 27), (x + 4, 104)], INK, 3, 1)
    b.line([(29, 47), (17, 30), (8, 45), (22, 66)], CRIMSON, 3, 2)
    b.line([(98, 46), (112, 29), (121, 45), (108, 66)], CRIMSON, 3, 2)


def draw_guding(b: Brush):
    # Tall ancient scimitar with a hooked black silhouette.
    b.poly([(74, 7), (94, 14), (85, 32), (71, 54), (59, 78), (53, 103), (66, 116), (55, 124), (39, 109), (43, 83), (55, 55)], INK, 3, 2)
    b.poly([(74, 14), (86, 18), (78, 30), (65, 54), (54, 79), (49, 101), (57, 108), (49, 114), (44, 105), (49, 83), (61, 55)], CREAM, 2)
    b.line([(70, 18), (63, 49), (51, 86)], OCHRE, 4, 1)
    b.outlined_poly(
        [(35, 93), (68, 91), (78, 103), (66, 112), (35, 108), (25, 101)],
        [(36, 98), (65, 97), (69, 102), (63, 106), (37, 104), (31, 101)],
        SEPIA,
    )
    b.line([(83, 12), (100, 25), (91, 43)], CRIMSON, 3, 2)


DRAWERS = {
    "\u95ea": draw_shan,
    "\u6843": draw_tao,
    "\u9152": draw_jiu,
    "\u51b3\u6597": draw_juedou,
    "\u65e0\u4e2d\u751f\u6709": draw_wuzhong,
    "\u8fc7\u6cb3\u62c6\u6865": draw_guohe,
    "\u987a\u624b\u7275\u7f8a": draw_shunshou,
    "\u5357\u86ee\u5165\u4fb5": draw_nanman,
    "\u4e07\u7bad\u9f50\u53d1": draw_wanjian,
    "\u6843\u56ed\u7ed3\u4e49": draw_taoyuan,
    "\u65e0\u61c8\u53ef\u51fb": draw_wuxie,
    "\u95ea\u7535": draw_shandian,
    "\u4e94\u8c37\u4e30\u767b": draw_wugu,
    "\u94c1\u7d22\u8fde\u73af": draw_tiesuo,
    "\u706b\u653b": draw_huogong,
    "\u5175\u7cae\u5bf8\u65ad": draw_bingliang,
    "\u85e4\u7532": draw_tengjia,
    "\u53e4\u952d\u5200": draw_guding,
}


def load_font(size: int):
    candidates = [
        Path("C:/Windows/Fonts/msyh.ttc"),
        Path("C:/Windows/Fonts/simhei.ttf"),
    ]
    for candidate in candidates:
        if candidate.exists():
            return ImageFont.truetype(str(candidate), size)
    return ImageFont.load_default()


def create_contact_sheet(files: list[Path], output: Path):
    thumb = 160
    cell_w = 180
    cell_h = 194
    columns = 5
    rows = (len(files) + columns - 1) // columns
    sheet = Image.new("RGB", (columns * cell_w, rows * cell_h), BG)
    draw = ImageDraw.Draw(sheet)
    font = load_font(16)
    for index, path in enumerate(files):
        x = (index % columns) * cell_w + 10
        y = (index // columns) * cell_h + 8
        image = Image.open(path).convert("RGB").resize(
            (thumb, thumb), Image.Resampling.NEAREST
        )
        sheet.paste(image, (x, y))
        draw.text((x, y + 165), path.stem, fill=CREAM, font=font)
    sheet.save(output)


def create_comparison_sheet(files: list[Path], output: Path):
    thumb = 120
    cell_w = 276
    cell_h = 157
    columns = 4
    rows = (len(files) + columns - 1) // columns
    sheet = Image.new("RGB", (columns * cell_w, rows * cell_h), BG)
    draw = ImageDraw.Draw(sheet)
    font = load_font(15)
    for index, stylized_path in enumerate(files):
        name = stylized_path.stem
        official_path = OFFICIAL_REFERENCE_DIR / f"{name}.png"
        if not official_path.exists():
            raise FileNotFoundError(official_path)
        x = (index % columns) * cell_w + 8
        y = (index // columns) * cell_h + 7
        official = Image.open(official_path).convert("RGB").resize(
            (thumb, thumb), Image.Resampling.LANCZOS
        )
        stylized = Image.open(stylized_path).convert("RGB").resize(
            (thumb, thumb), Image.Resampling.NEAREST
        )
        sheet.paste(official, (x, y))
        sheet.paste(stylized, (x + 132, y))
        draw.text((x, y + 126), f"{name}  official / redraw", fill=CREAM, font=font)
    sheet.save(output)


def main():
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)
    output_files = []
    for name in CARD_NAMES:
        output = PREVIEW_DIR / f"{name}.png"
        if name == "\u6740":
            if not ACCEPTED_SHA.exists():
                raise FileNotFoundError(ACCEPTED_SHA)
            image = Image.open(ACCEPTED_SHA).convert("RGB")
        else:
            brush = Brush(name)
            DRAWERS[name](brush)
            brush.flecks()
            image = brush.finish()
        image.save(output)
        output_files.append(output)

    create_contact_sheet(
        output_files,
        PREVIEW_DIR / "sanguosha-stylized-batch-contact-sheet.png",
    )
    create_comparison_sheet(
        output_files,
        PREVIEW_DIR / "sanguosha-official-vs-stylized-contact-sheet.png",
    )

    for output in output_files:
        image = Image.open(output).convert("RGB")
        colors = image.getcolors(maxcolors=1_000_000)
        print(f"{output.stem}: size={image.size}, colors={len(colors or [])}")
    print(f"excluded={EXCLUDED}")


if __name__ == "__main__":
    main()
