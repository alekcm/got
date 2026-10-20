#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Предпросмотр карты SoQ с настоящим артом (без Unity).

Берёт тот же генератор, что и `soq_mapgen_preview.py`, но рисует карту так, как
её увидит сцена SoQMap: тайлы рельефа из `Resources/Art/Terrain`, спрайты
объектов из атласов `Resources/Art/SoQ` (нарезка — `soq_sprite_slices.json`),
дороги, границы регионов, метки стартов.

Нужен только Pillow:
    PYTHONPATH=/tmp/pylibs python3 tools/soq_mapgen_art_preview.py --tile 26
Результат:
    tools/soq_art_<layout>_seed<N>.png
    tools/soq_art_preview.png  (контактный лист)
"""
import argparse
import copy
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import soq_mapgen_preview as base  # noqa: E402

ROOT = base.ROOT
ART = os.path.join(ROOT, "Assets", "IronThrone", "Resources", "Art")
SOQ_ART = os.path.join(ART, "SoQ")
OUT = os.path.join(ROOT, "tools")

# порядок совпадает с AdvTerrainInfo.TileNames и палитрой из WorldMapView
TERRAIN_FILES = [
    "000_grass", "010_tundra", "024_dirt", "028_dry", "034_rock_green", "040_sand",
    "042_sand_rock", "044_fields", "050_autumn", "062_rock", "094_snow_rock", "096_snow",
    "100_cold_grass", "102_forest_mix", "104_marsh", "106_forest_pine",
]
TERRAIN_NAMES = [
    "Grass", "Tundra", "Dirt", "Dry", "RockGreen", "Sand",
    "SandRock", "Fields", "Autumn", "Rock", "SnowRock", "Snow",
    "ColdGrass", "ForestMix", "Marsh", "ForestPine",
]
TERRAIN_BY_NAME = dict(zip(TERRAIN_NAMES, TERRAIN_FILES))

# тот же порядок цветов, что RegionPalette в SoqMapView.cs
REGION_PALETTE = [
    (250, 209, 107), (158, 214, 250), (250, 158, 148), (173, 235, 163),
    (219, 179, 250), (250, 230, 158), (153, 230, 219), (245, 168, 209),
    (204, 219, 148), (184, 194, 250), (250, 189, 133), (168, 204, 184),
]

SEA_DEEP = (26, 58, 82)
SEA_SHALLOW = (48, 96, 120)
ROAD_COLOR = (163, 125, 77)
GROUND_BRIGHTNESS = 1.5  # как SoqMapView.GroundBrightness
# как AdvTerrainInfo.Brightness: привести среднюю яркость тайлов к одному уровню
TERRAIN_BRIGHTNESS = [1.36, 1.45, 1.69, 1.62, 1.67, 1.00, 1.22, 1.08,
                      1.26, 1.69, 1.46, 0.95, 1.32, 1.70, 1.48, 1.70]
TEX_PX = 128  # размер исходного тайла рельефа
GROUND_UV_SCALE = 0.5  # как SoqMapView.GroundUvScale
BORDER_COLOR = (24, 30, 26)


def font(size):
    for name in ("DejaVuSans.ttf", "LiberationSans-Regular.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except Exception:
            continue
    return ImageFont.load_default()


def load_slices():
    with open(os.path.join(ART, "soq_sprite_slices.json"), encoding="utf-8") as f:
        db = json.load(f)
    sprites = {s["name"]: s for s in db["sprites"]}
    atlases = {}
    for a in db["atlases"]:
        path = os.path.join(ART, a["file"].replace("Art/", "") + ".png")
        atlases[a["name"]] = Image.open(path).convert("RGBA")
    return sprites, atlases


def crop_sprite(sprite, atlases):
    atlas = atlases.get(sprite["atlas"])
    if atlas is None:
        return None
    x, y, w, h = sprite["x"], sprite["y"], sprite["w"], sprite["h"]
    tex_h = atlas.height
    # в JSON координаты снизу-вверх, у PIL — сверху вниз
    top = tex_h - y - h
    return atlas.crop((x, top, x + w, top + h))


def sprite_name_for(defn, biome):
    """Вариант спрайта по биому региона, иначе основной."""
    key = {"North": "north", "Desert": "desert", "Swamp": "swamp",
           "Highlands": "highlands", "Wasteland": "wasteland"}.get(biome, "temperate")
    for v in defn.get("variants", []) or []:
        if (v.get("biome") or "").lower() == key:
            return v.get("sprite") or defn.get("sprite")
    return defn.get("sprite")


def tint(img, color, amount):
    """Подмешать цвет региона, как GroundTint в SoqMapView."""
    overlay = Image.new("RGB", img.size, color)
    return Image.blend(img.convert("RGB"), overlay, amount)


DECOR_DENSITY = {
    "ForestMix": 0.26, "ForestPine": 0.26, "Autumn": 0.16, "Marsh": 0.13,
    "Grass": 0.08, "Fields": 0.08, "ColdGrass": 0.08,
    "Tundra": 0.06, "Snow": 0.06,
    "Rock": 0.05, "RockGreen": 0.05, "SnowRock": 0.05,
    "Sand": 0.025, "Dry": 0.025, "SandRock": 0.025,
}


def scatter_decor(result, catalog):
    """Тот же проход, что ScatterDecor в SoqMapGenerator: деревья на пустой земле."""
    defs = [d for d in catalog.values() if (d.get("category") or "").lower() == "decor"]
    if not defs:
        return []
    size = result["size"]
    seed = result["seed"]
    terrain = result["terrain"]
    road = result["road"]
    occupied = {(o["x"], o["y"]) for o in result["objects"]}
    decor = []
    for y in range(size):
        for x in range(size):
            idx = y * size + x
            if not result["land"][idx] or road[idx] or (x, y) in occupied:
                continue
            name = terrain.get(idx, "Grass") if isinstance(terrain, dict) else terrain[idx]
            density = DECOR_DENSITY.get(name, 0.07)
            if base.hash01(x, y, seed * 31 + 7) > density:
                continue
            pick = int(base.hash01(x, y, seed * 71 + 13) * len(defs))
            pick = max(0, min(len(defs) - 1, pick))
            decor.append({"x": x, "y": y, "def": defs[pick]["id"], "region": result["region_of"][idx]})
    return decor


def render(result, layout, catalog, sprites, atlases, path, tile_px=24):
    size = result["size"]
    region_of = result["region_of"]
    terrain = result["terrain"]
    road = result["road"]
    regions = result["regions"]
    biome_of_region = {r["id"]: r.get("biome", "Temperate") for r in regions}

    img = Image.new("RGB", (size * tile_px, size * tile_px), SEA_DEEP)
    draw = ImageDraw.Draw(img)

    terrain_cache = {}
    for name in TERRAIN_NAMES:
        f = os.path.join(ART, "Terrain", TERRAIN_BY_NAME[name] + ".png")
        tile_img = Image.open(f).convert("RGB").resize((tile_px, tile_px), Image.BILINEAR)
        # тайлы тёмные (в WorldMapView их освещает ramp-шейдер) — компенсируем
        big = tile_img.resize((TEX_PX, TEX_PX), Image.BILINEAR)
        k = TERRAIN_BRIGHTNESS[TERRAIN_NAMES.index(name)]
        big = big.point(lambda v: min(255, int(v * k)))
        screen = max(1, int(round(tile_px / GROUND_UV_SCALE)))
        big = big.resize((screen, screen), Image.BILINEAR)
        # 2×2 копия: из неё можно вырезать окно в любом месте без выхода за край
        side = big.width
        tiled = Image.new("RGB", (side * 2, side * 2))
        for ty in range(2):
            for tx in range(2):
                tiled.paste(big, (tx * side, ty * side))
        terrain_cache[name] = tiled

    # вода: чем дальше от берега, тем темнее
    for y in range(size):
        for x in range(size):
            idx = y * size + x
            if result["land"][idx]:
                continue
            near = False
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < size and 0 <= ny < size and result["land"][ny * size + nx]:
                        near = True
            draw.rectangle([x * tile_px, y * tile_px, (x + 1) * tile_px - 1, (y + 1) * tile_px - 1],
                           fill=SEA_SHALLOW if near else SEA_DEEP)

    # земля
    for y in range(size):
        for x in range(size):
            idx = y * size + x
            if not result["land"][idx]:
                continue
            name = terrain.get(idx, "Grass") if isinstance(terrain, dict) else terrain[idx]
            tile_img = terrain_cache.get(name, terrain_cache["Grass"])
            # как GroundUvScale в SoqMapView: UV в мировых координатах, текстура
            # продолжается на соседних тайлах, а не начинается заново
            stride = max(1, int(round(tile_px * GROUND_UV_SCALE)))
            side = tile_img.width // 2
            ox = (x * stride) % side
            oy = (y * stride) % side
            crop = tile_img.crop((ox, oy, ox + tile_px, oy + tile_px))
            rid = region_of[idx]
            if rid >= 0:
                crop = tint(crop, REGION_PALETTE[rid % len(REGION_PALETTE)], 0.10)
            img.paste(crop, (x * tile_px, y * tile_px))

    # границы регионов
    for y in range(size):
        for x in range(size):
            idx = y * size + x
            if not result["land"][idx]:
                continue
            rid = region_of[idx]
            for dx, dy in ((1, 0), (0, 1)):
                nx, ny = x + dx, y + dy
                if not (0 <= nx < size and 0 <= ny < size):
                    continue
                if not result["land"][ny * size + nx]:
                    continue
                if region_of[ny * size + nx] == rid:
                    continue
                if dx:
                    draw.line([(x + 1) * tile_px, y * tile_px, (x + 1) * tile_px, (y + 1) * tile_px],
                              fill=BORDER_COLOR, width=1)
                else:
                    draw.line([x * tile_px, (y + 1) * tile_px, (x + 1) * tile_px, (y + 1) * tile_px],
                              fill=BORDER_COLOR, width=1)

    # дороги: соединяем центры соседних дорожных тайлов
    rw = max(2, int(tile_px * 0.22))
    for y in range(size):
        for x in range(size):
            idx = y * size + x
            if not road[idx]:
                continue
            cx, cy = (x + 0.5) * tile_px, (y + 0.5) * tile_px
            for dx, dy in ((1, 0), (0, 1)):
                nx, ny = x + dx, y + dy
                if not (0 <= nx < size and 0 <= ny < size):
                    continue
                if not road[ny * size + nx]:
                    continue
                draw.line([cx, cy, (nx + 0.5) * tile_px, (ny + 0.5) * tile_px], fill=ROAD_COLOR, width=rw)

    # объекты: стоят на тайле, якорь — низ по центру (pivotY=0 в нарезке)
    OBJECT_SCALE = 1.6  # как SoqMapView.ObjectScale

    def object_width(sprite, scale):
        return max(3, int(round(sprite["w"] / 32.0 * scale * OBJECT_SCALE * tile_px / 4.0)))

    def draw_one(obj, defn, scale_mul):
        biome = biome_of_region.get(obj.get("region", -1), "Temperate")
        name = sprite_name_for(defn, biome)
        sprite = sprites.get(name)
        if not sprite:
            return
        crop = crop_sprite(sprite, atlases)
        if crop is None:
            return
        scale = float(defn.get("scale", 1.0)) * scale_mul
        w = object_width(sprite, scale)
        h = max(2, int(round(crop.height * w / crop.width)))
        crop = crop.resize((w, h), Image.BILINEAR)
        cx = int((obj["x"] + 0.5) * tile_px - w * float(sprite.get("pivotX", 0.5)))
        by = int((obj["y"] + 0.92) * tile_px)
        img.paste(crop, (cx, by - h), crop)

    # декор земли — до объектов, чтобы деревья не перекрывали находки
    for deco in sorted(scatter_decor(result, catalog), key=lambda o: (o["y"], o["x"])):
        defn = catalog.get(deco["def"])
        if defn:
            draw_one(deco, defn, 0.85)

    objects = sorted(result["objects"], key=lambda o: (o["y"], o["x"]))
    for obj in objects:
        defn = catalog.get(obj["def"])
        if not defn:
            continue
        sprite = sprites.get(sprite_name_for(defn, biome_of_region.get(obj.get("region", -1), "Temperate")))
        if sprite and obj.get("guarded"):
            gx = int((obj["x"] + 0.5) * tile_px)
            gy = int((obj["y"] + 0.9) * tile_px)
            draw.ellipse([gx - 4, gy - 3, gx + 4, gy + 2], fill=(150, 40, 34))
        if sprite:
            w = object_width(sprite, float(defn.get("scale", 1.0)))
            if w >= tile_px * 0.5:
                shx = int((obj["x"] + 0.5) * tile_px)
                shy = int((obj["y"] + 0.95) * tile_px)
                draw.ellipse([shx - w // 3, shy - tile_px // 7, shx + w // 3, shy + 2], fill=(30, 36, 30))
        draw_one(obj, defn, 1.0)

    # метки стартов
    for start in result["starts"]:
        cx = int((start["x"] + 0.5) * tile_px)
        cy = int((start["y"] + 0.2) * tile_px)
        draw.ellipse([cx - 9, cy - 9, cx + 9, cy + 9], fill=(20, 18, 12), outline=(250, 226, 150))
        draw.text((cx - 5, cy - 7), "P%d" % start["player"], fill=(250, 226, 150), font=font(10))

    # подпись
    panel_h = 46
    canvas = Image.new("RGB", (img.width, img.height + panel_h), (16, 20, 22))
    canvas.paste(img, (0, panel_h))
    d = ImageDraw.Draw(canvas)
    counts = {}
    for o in result["objects"]:
        counts[o["cat"]] = counts.get(o["cat"], 0) + 1
    cats = ", ".join("%s %d" % (k, counts[k]) for k in sorted(counts))
    d.text((10, 6), "%s / %s · сид %d · %d×%d" %
           (layout.get("name", layout.get("id", "?")), result.get("sub_name", "вариант"),
            result["seed"], size, size), fill=(240, 228, 190), font=font(14))
    d.text((10, 26), "земли %d · областей %d · объектов %d (%s)" %
           (sum(result["land"]), len(regions), len(result["objects"]), cats),
           fill=(170, 180, 170), font=font(11))
    canvas.save(path)
    return path


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seeds", default="1,7")
    ap.add_argument("--tile", type=int, default=24)
    ap.add_argument("--out", default="soq_art_preview.png")
    args = ap.parse_args()

    layouts, blueprint, catalog = base.load_data()
    sprites, atlases = load_slices()
    seeds = [int(s) for s in args.seeds.split(",") if s.strip()]

    images = []
    for layout in layouts:
        for seed in seeds:
            lay = copy.deepcopy(layout)
            sub = lay["layouts"][0]
            result = base.generate(lay, sub, blueprint, catalog, seed)
            result["sub_name"] = sub.get("name", "вариант")
            name = "soq_art_%s_seed%d.png" % (layout["id"], seed)
            path = os.path.join(OUT, name)
            render(result, layout, catalog, sprites, atlases, path, args.tile)
            print("%s: %s" % (name, path))
            images.append(("%s / сид %d" % (layout.get("name", layout["id"]), seed), path))

    # контактный лист 2×2
    cols = 2
    cell_w = 640
    thumbs = []
    for label, path in images:
        im = Image.open(path)
        k = min((cell_w - 12) / im.width, 1.0)
        thumbs.append((label, im.resize((int(im.width * k), int(im.height * k)), Image.LANCZOS)))
    cell_h = max(t.height for _, t in thumbs) + 26
    rows = (len(thumbs) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell_w, rows * cell_h), (14, 18, 20))
    d = ImageDraw.Draw(sheet)
    for i, (label, t) in enumerate(thumbs):
        x = (i % cols) * cell_w + (cell_w - t.width) // 2
        y = (i // cols) * cell_h + 22
        sheet.paste(t, (x, y))
        d.text(((i % cols) * cell_w + 8, (i // cols) * cell_h + 5), label,
               fill=(235, 225, 190), font=font(13))
    out = os.path.join(OUT, args.out)
    sheet.save(out)
    print("Контактный лист:", out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
