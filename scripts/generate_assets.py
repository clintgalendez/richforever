"""Generate RichForever's original artwork.

Outputs:
  companion/src/RichForever.App/richforever.ico   tray and executable icon
  companion/assets/discord/*.png                  Discord Rich Presence art assets

Every image is drawn from simple shapes and text; nothing is taken from Blizzard
or Discord. Requires Pillow:  python -m pip install pillow
Run from the repository root:  python scripts/generate_assets.py
"""

import json
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CATALOG = os.path.join(ROOT, "companion", "assets", "catalog.json")
OUT = os.path.join(ROOT, "companion", "assets", "discord")
ICON = os.path.join(ROOT, "companion", "src", "RichForever.App", "richforever.ico")

GOLD = (212, 169, 74)
GOLD_LIGHT = (240, 210, 140)
NIGHT = (18, 14, 32)
GEM = (79, 195, 247)

# Class colours are plain hex values commonly used by the WoW community.
CLASS_COLORS = {
    "WARRIOR": (198, 155, 109), "PALADIN": (244, 140, 186), "HUNTER": (170, 211, 114),
    "ROGUE": (255, 244, 104), "PRIEST": (235, 235, 235), "SHAMAN": (0, 112, 221),
    "MAGE": (63, 199, 235), "WARLOCK": (135, 136, 238), "DRUID": (255, 124, 10),
}

EASTERN_KINGDOMS = set(range(1416, 1438)) | {1453, 1455, 1458}
KALIMDOR = {1411, 1412, 1413, 1454, 1456, 1457, 2482} | set(range(1438, 1453))
CITIES = {1453, 1454, 1455, 1456, 1457, 1458}
RAIDS = {409, 249, 469, 309, 509, 531, 533}
BATTLEGROUNDS = {489, 529, 30}


def font(size):
    for name in ("georgiab.ttf", "Georgia Bold.ttf", "DejaVuSerif-Bold.ttf", "LiberationSerif-Bold.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default(size)


def radial(size, inner, outer, steps=96):
    """Radial gradient, computed small and upscaled (it is smooth anyway)."""
    image = Image.new("RGB", (steps, steps), outer)
    pixels = image.load()
    center = steps / 2
    for y in range(steps):
        for x in range(steps):
            t = min(1.0, math.hypot(x + 0.5 - center, y + 0.5 - center) / (center * 1.15))
            pixels[x, y] = tuple(int(inner[i] + (outer[i] - inner[i]) * t) for i in range(3))
    return image.resize((size, size), Image.BICUBIC)


def save(image, path):
    """Palette PNGs keep the repository small; 256 colours is plenty for this art."""
    image.quantize(256, method=Image.Quantize.FASTOCTREE if image.mode == "RGBA" else Image.Quantize.MEDIANCUT).save(path, optimize=True)


def star(draw, cx, cy, outer, inner, points, fill, rotation=-math.pi / 2):
    vertices = []
    for i in range(points * 2):
        radius = outer if i % 2 == 0 else inner
        angle = rotation + i * math.pi / points
        vertices.append((cx + radius * math.cos(angle), cy + radius * math.sin(angle)))
    draw.polygon(vertices, fill=fill)


def circle_mask(size, scale=4):
    mask = Image.new("L", (size * scale, size * scale), 0)
    ImageDraw.Draw(mask).ellipse((0, 0, size * scale - 1, size * scale - 1), fill=255)
    return mask.resize((size, size), Image.LANCZOS)


def logo(size=1024):
    s = size * 2  # draw large, downsample for smooth edges
    image = radial(s, (58, 40, 98), NIGHT).convert("RGBA")
    draw = ImageDraw.Draw(image)
    c = s / 2
    draw.ellipse((s * 0.04, s * 0.04, s * 0.96, s * 0.96), outline=GOLD, width=int(s * 0.035))
    draw.ellipse((s * 0.12, s * 0.12, s * 0.88, s * 0.88), outline=GOLD + (120,), width=int(s * 0.008))
    star(draw, c, c, s * 0.36, s * 0.07, 4, GOLD)
    star(draw, c, c, s * 0.22, s * 0.05, 4, GOLD_LIGHT, rotation=-math.pi / 4)
    draw.ellipse((c - s * 0.055, c - s * 0.055, c + s * 0.055, c + s * 0.055), fill=GEM)
    image = image.resize((size, size), Image.LANCZOS)
    image.putalpha(circle_mask(size))
    return image


def glyph(draw, token, c, r, color):
    """Simple geometric class symbols."""
    w = max(2, int(r * 0.12))
    if token == "WARRIOR":  # crossed blades
        for sign in (1, -1):
            draw.line((c - r * 0.6, c - sign * r * 0.6, c + r * 0.6, c + sign * r * 0.6), fill=color, width=w)
        draw.ellipse((c - r * 0.15, c - r * 0.15, c + r * 0.15, c + r * 0.15), fill=color)
    elif token == "PALADIN":  # cross
        draw.rectangle((c - r * 0.12, c - r * 0.65, c + r * 0.12, c + r * 0.65), fill=color)
        draw.rectangle((c - r * 0.45, c - r * 0.35, c + r * 0.45, c - r * 0.11), fill=color)
    elif token == "HUNTER":  # arrow
        draw.line((c - r * 0.55, c + r * 0.55, c + r * 0.4, c - r * 0.4), fill=color, width=w)
        draw.polygon([(c + r * 0.6, c - r * 0.6), (c + r * 0.15, c - r * 0.45), (c + r * 0.45, c - r * 0.15)], fill=color)
    elif token == "ROGUE":  # dagger
        draw.polygon([(c, c - r * 0.7), (c + r * 0.13, c + r * 0.2), (c - r * 0.13, c + r * 0.2)], fill=color)
        draw.rectangle((c - r * 0.35, c + r * 0.2, c + r * 0.35, c + r * 0.3), fill=color)
        draw.rectangle((c - r * 0.07, c + r * 0.3, c + r * 0.07, c + r * 0.65), fill=color)
    elif token == "PRIEST":  # halo over a dot
        draw.ellipse((c - r * 0.5, c - r * 0.65, c + r * 0.5, c - r * 0.25), outline=color, width=w)
        draw.ellipse((c - r * 0.25, c - r * 0.05, c + r * 0.25, c + r * 0.45), fill=color)
    elif token == "SHAMAN":  # lightning
        draw.polygon([(c + r * 0.15, c - r * 0.7), (c - r * 0.35, c + r * 0.1), (c, c + r * 0.1),
                      (c - r * 0.15, c + r * 0.7), (c + r * 0.35, c - r * 0.1), (c, c - r * 0.1)], fill=color)
    elif token == "MAGE":  # six-pointed star
        star(draw, c, c, r * 0.65, r * 0.3, 6, color)
    elif token == "WARLOCK":  # flame
        draw.polygon([(c, c - r * 0.7), (c + r * 0.4, c), (c + r * 0.3, c + r * 0.45), (c, c + r * 0.6),
                      (c - r * 0.3, c + r * 0.45), (c - r * 0.4, c)], fill=color)
        draw.ellipse((c - r * 0.15, c + r * 0.05, c + r * 0.15, c + r * 0.4), fill=NIGHT)
    elif token == "DRUID":  # leaf
        draw.ellipse((c - r * 0.3, c - r * 0.65, c + r * 0.3, c + r * 0.55), fill=color)
        draw.line((c, c - r * 0.5, c, c + r * 0.7), fill=NIGHT, width=max(2, w // 2))


def class_badge(token, size=512):
    s = size * 2
    color = CLASS_COLORS[token]
    image = radial(s, tuple(int(v * 0.35) for v in color), NIGHT).convert("RGBA")
    draw = ImageDraw.Draw(image)
    draw.ellipse((s * 0.04, s * 0.04, s * 0.96, s * 0.96), outline=color, width=int(s * 0.05))
    glyph(draw, token, s / 2, s * 0.42, color)
    image = image.resize((size, size), Image.LANCZOS)
    image.putalpha(circle_mask(size))
    return image


def card(title, label, tint, size=512):
    s = size * 2
    image = radial(s, tint, NIGHT)
    draw = ImageDraw.Draw(image)
    draw.rectangle((s * 0.03, s * 0.03, s * 0.97, s * 0.97), outline=GOLD, width=int(s * 0.014))
    draw.rectangle((s * 0.06, s * 0.06, s * 0.94, s * 0.94), outline=GOLD + (0,), width=int(s * 0.004))
    star(draw, s / 2, s * 0.22, s * 0.07, s * 0.018, 4, GOLD)
    words = title.split()
    lines, line = [], ""
    for word in words:
        trial = (line + " " + word).strip()
        if len(trial) > 13 and line:
            lines.append(line)
            line = word
        else:
            line = trial
    lines.append(line)
    size_px = int(s * (0.13 if max(len(x) for x in lines) <= 10 else 0.1))
    title_font = font(size_px)
    total = len(lines) * size_px * 1.15
    y = s * 0.52 - total / 2
    for text in lines:
        draw.text((s / 2, y), text, font=title_font, fill=GOLD_LIGHT, anchor="mt")
        y += size_px * 1.15
    if label:
        draw.text((s / 2, s * 0.84), label.upper(), font=font(int(s * 0.045)), fill=GOLD, anchor="mm")
    return image.resize((size, size), Image.LANCZOS)


def main():
    os.makedirs(OUT, exist_ok=True)
    with open(CATALOG, encoding="utf-8") as handle:
        catalog = json.load(handle)

    big = logo(1024)
    save(big, os.path.join(OUT, catalog["default"] + ".png"))
    big.resize((256, 256), Image.LANCZOS).save(ICON, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

    for token, key in catalog["classes"].items():
        save(class_badge(token), os.path.join(OUT, key + ".png"))

    type_cards = {"type_world": ("Exploring", "Azeroth", (40, 70, 60)), "type_dungeon": ("Dungeon", "", (70, 45, 30)),
                  "type_raid": ("Raid", "", (90, 25, 30)), "type_pvp": ("Battleground", "", (40, 50, 95))}
    for key, (title, label, tint) in type_cards.items():
        save(card(title, label, tint), os.path.join(OUT, key + ".png"))

    for zone in catalog["zones"]:
        map_id = zone.get("mapIds", [0])[0]
        label = "City" if map_id in CITIES else "Eastern Kingdoms" if map_id in EASTERN_KINGDOMS else "Kalimdor" if map_id in KALIMDOR else "Azeroth"
        tint = (60, 50, 25) if map_id in CITIES else (35, 60, 45) if map_id in EASTERN_KINGDOMS else (70, 45, 35)
        save(card(zone["names"][0], label, tint), os.path.join(OUT, zone["key"] + ".png"))

    for instance in catalog["instances"]:
        instance_id = instance["instanceIds"][0]
        label, tint = ("Raid", (90, 25, 30)) if instance_id in RAIDS else ("Battleground", (40, 50, 95)) if instance_id in BATTLEGROUNDS else ("Dungeon", (70, 45, 30))
        save(card(instance["names"][0], label, tint), os.path.join(OUT, instance["key"] + ".png"))

    count = len(os.listdir(OUT))
    print(f"wrote {count} images to {OUT} and {ICON}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
