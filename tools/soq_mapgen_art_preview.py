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

import numpy as np

from PIL import Image, ImageDraw, ImageFilter, ImageFont

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

SEA_DEEP = (18, 46, 56)
SEA_SHALLOW = (38, 76, 82)
ROAD_COLOR = (163, 125, 77)
GROUND_BRIGHTNESS = 1.05  # как SoqMapView.GroundBrightness
# тайлы уже нормализованы по средней яркости в tools/soq_bake_terrain.py
TERRAIN_BRIGHTNESS = [1.0] * 16
TEX_PX = 128  # размер исходного тайла рельефа
GROUND_UV_SCALE = 1.0 / 3.2  # как _TileScale в SoqMapGround
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
    "ForestPine": 1.35, "ForestMix": 1.15, "Autumn": 0.55, "Fields": 0.30,
    "Marsh": 0.55, "Grass": 0.16, "ColdGrass": 0.18, "RockGreen": 0.5,
    "Tundra": 0.22, "Rock": 0.45, "SnowRock": 0.45, "Snow": 0.14,
    "Dirt": 0.22, "Dry": 0.22, "SandRock": 0.2, "Sand": 0.08,
}


def scatter_decor(result, catalog):
    """Тот же проход, что ScatterDecor в SoqMapGenerator: несколько мелочей на тайл."""
    defs = [d for d in catalog.values()
            if (d.get("category") or "").lower() == "decor" and not d.get("mountain")]
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
            density = DECOR_DENSITY.get(name, 0.35)
            # рощи: низкочастотный шум модулирует плотность (SoqRng.GroveNoise)
            grove = base.hash01(x >> 1, y >> 1, seed * 977 + 41) * 0.6 + \
                    base.hash01(x >> 2, y >> 2, seed * 977 + 83) * 0.4
            grove_k = 0.25 + 1.5 * grove * grove
            r = base.hash01(x, y, seed * 31 + 7)
            scaled = density * grove_k + (r - 0.5) * 0.9
            count = int(scaled // 1)
            if base.hash01(x, y, seed * 31 + 19) < scaled - count:
                count += 1
            if count <= 0:
                continue
            count = min(count, 4)
            picks = [d for d in defs if not d.get("terrain")
                     or name in d.get("terrain", [])]
            if not picks:
                picks = defs
            for k in range(count):
                pick = int(base.hash01(x * 7 + k, y * 13 + k, seed * 71 + 13) * len(picks))
                pick = max(0, min(len(picks) - 1, pick))
                decor.append({"x": x, "y": y, "k": k, "def": picks[pick]["id"],
                              "region": result["region_of"][idx]})
    return decor


MOUNTAIN_CHANCE = {"Rock": 0.65, "SnowRock": 0.30, "RockGreen": 0.10, "SandRock": 0.12}


def scatter_mountains(result, catalog):
    """Тот же проход, что ScatterMountains в SoqMapGenerator."""
    defs = [d for d in catalog.values() if d.get("mountain")]
    if not defs:
        return []
    size = result["size"]
    seed = result["seed"]
    terrain = result["terrain"]
    road = result["road"]
    land = result["land"]
    occupied = {(o["x"], o["y"]) for o in result["objects"]}
    out = []
    for y in range(size):
        for x in range(size):
            idx = y * size + x
            if not land[idx] or road[idx] or (x, y) in occupied:
                continue
            name = terrain[idx] if not isinstance(terrain, dict) else terrain.get(idx, "Grass")
            chance = MOUNTAIN_CHANCE.get(name, 0.0)
            if chance <= 0:
                continue
            if base.hash01(x, y, seed * 131 + 5) > chance:
                continue
            pick = int(base.hash01(x, y, seed * 137 + 11) * len(defs))
            pick = max(0, min(len(defs) - 1, pick))
            out.append({"x": x, "y": y, "k": 0, "def": defs[pick]["id"],
                        "region": result["region_of"][idx]})
    return out


def render(result, layout, catalog, sprites, atlases, path, tile_px=24):
    size = result["size"]
    region_of = result["region_of"]
    terrain = result["terrain"]
    road = result["road"]
    regions = result["regions"]
    biome_of_region = {r["id"]: r.get("biome", "Temperate") for r in regions}

    # ---- рельеф: сплат-смешивание тайлов, как в шейдере SoqMapGround
    names = TERRAIN_NAMES
    tiles = {}
    for i, name in enumerate(names):
        f = os.path.join(ART, "SoQ", "Terrain", TERRAIN_FILES[i] + ".png")
        tex = Image.open(f).convert("RGB").resize((TEX_PX, TEX_PX), Image.BILINEAR)
        tiles[i] = np.asarray(tex).astype(np.float32)

    px = size * tile_px
    yy, xx = np.mgrid[0:px, 0:px].astype(np.float32)
    tx = (xx + 0.5) / tile_px - 0.5
    ty = (yy + 0.5) / tile_px - 0.5
    x0 = np.floor(tx).astype(np.int32)
    y0 = np.floor(ty).astype(np.int32)
    fx = tx - x0
    fy = ty - y0

    def class_at(gx, gy):
        gx = np.clip(gx, 0, size - 1)
        gy = np.clip(gy, 0, size - 1)
        idx = gy * size + gx
        land = np.array(result["land"], dtype=bool)[idx]
        cls = np.array([names.index(terrain[i]) if isinstance(terrain, dict) is False else
                        names.index(terrain.get(i, "Grass")) for i in range(size * size)],
                       dtype=np.int32)[idx]
        return np.where(land, cls, -1)

    c00 = class_at(x0, y0)
    c10 = class_at(x0 + 1, y0)
    c01 = class_at(x0, y0 + 1)
    c11 = class_at(x0 + 1, y0 + 1)
    w00 = (1 - fx) * (1 - fy)
    w10 = fx * (1 - fy)
    w01 = (1 - fx) * fy
    w11 = fx * fy
    land_cover = (w00 * (c00 >= 0) + w10 * (c10 >= 0) + w01 * (c01 >= 0) + w11 * (c11 >= 0))

    # UV мира: тайл текстуры продолжается на соседних клетках
    u = (xx / tile_px * GROUND_UV_SCALE * TEX_PX).astype(np.int32) % TEX_PX
    v = (yy / tile_px * GROUND_UV_SCALE * TEX_PX).astype(np.int32) % TEX_PX

    color = np.zeros((px, px, 3), dtype=np.float32)
    total = np.zeros((px, px, 1), dtype=np.float32)
    forest = np.zeros((px, px, 1), dtype=np.float32)
    for ci in range(len(names)):
        w = (w00 * (c00 == ci) + w10 * (c10 == ci) + w01 * (c01 == ci) + w11 * (c11 == ci))
        m = w > 0.0005
        if not m.any():
            continue
        tex = tiles[ci]
        sample = tex[v[m], u[m]]
        color[m] += sample * w[m][..., None]
        total[m] += w[m][..., None]
        # ForestMix (13) и ForestPine (15) притеняют землю — как _ForestShade в шейдере
        if ci in (13, 15):
            forest[m] += w[m][..., None]
    color = np.divide(color, np.maximum(total, 1e-4))
    forest = np.divide(forest, np.maximum(total, 1e-4))

    # «погода» карты: крупные пятна + мелкое зерно (как _MacroTex/_DetailTex)
    def value_noise(cells, seed):
        rng = np.random.default_rng(seed)
        grid = rng.random((cells + 1, cells + 1)).astype(np.float32)
        grid[-1, :] = grid[0, :]
        grid[:, -1] = grid[:, 0]
        img = Image.fromarray((grid * 255).astype(np.uint8)).resize((px, px), Image.BICUBIC)
        return np.asarray(img).astype(np.float32) / 255.0

    macro = value_noise(10, 11)[..., None]
    grain = value_noise(64, 29)[..., None]
    color *= 1.0 + (macro - 0.5) * 2.0 * 0.11
    color *= 1.0 + (grain - 0.5) * 2.0 * 0.07
    color *= 1.0 - forest * 0.30

    # море: сглаженное поле берега (как ComputeCoastField) + глубина
    land_arr = np.array(result["land"], dtype=bool).reshape(size, size)
    sea_arr = ~land_arr
    # 1.0 — «далеко от воды»; у линии воды перезапишем расстоянием 1/4..3/4
    coast_sea = np.ones((size, size), dtype=np.float32)
    coast_land = np.ones((size, size), dtype=np.float32)
    for grid, out in ((sea_arr, coast_sea), (land_arr, coast_land)):
        frontier = [(x, y) for y in range(size) for x in range(size)
                    if grid[y, x] and any(
                        0 <= x + dx < size and 0 <= y + dy < size and not grid[y + dy, x + dx]
                        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))]
        seen = np.zeros((size, size), dtype=bool)
        for (x, y) in frontier:
            seen[y, x] = True
        level = 0
        while frontier and level < 3:
            nxt = []
            for (x, y) in frontier:
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < size and 0 <= ny < size and grid[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        out[ny, nx] = (level + 1) / 4.0
                        nxt.append((nx, ny))
            frontier = nxt
            level += 1
    coast_field = np.where(land_arr, 0.5 + np.minimum(coast_land, 1.0) * 0.45,
                           0.5 - np.minimum(coast_sea, 1.0) * 0.45)
    # глубина воды: расстояние от суши (0 у берега, 1 вдали)
    depth = np.zeros((size, size), dtype=np.float32)
    seen_w = land_arr.copy()
    frontier_w = [(x, y) for y in range(size) for x in range(size) if land_arr[y, x]]
    lvl = 0
    while frontier_w and lvl < 8:
        nxt = []
        for (x, y) in frontier_w:
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < size and 0 <= ny < size and not seen_w[ny, nx]:
                    seen_w[ny, nx] = True
                    depth[ny, nx] = (lvl + 1) / 8.0
                    nxt.append((nx, ny))
        frontier_w = nxt
        lvl += 1
    # билинейно в пиксели и размываем — так край получается плавным
    coast_px = np.asarray(Image.fromarray((coast_field * 255).astype(np.uint8)).resize((px, px), Image.BILINEAR)).astype(np.float32) / 255.0
    for _ in range(6):
        coast_px = (coast_px * 2 + np.roll(coast_px, 1, 0) + np.roll(coast_px, -1, 0)
                    + np.roll(coast_px, 1, 1) + np.roll(coast_px, -1, 1)) / 6.0

    seabed = np.array([18, 46, 56], dtype=np.float32)
    shallow = np.array([40, 78, 80], dtype=np.float32)
    depth_px = np.repeat(np.repeat(depth, tile_px, axis=0), tile_px, axis=1)
    water = seabed * (1 - depth_px[..., None]) + shallow * depth_px[..., None]

    # мокрый песок вдоль воды
    shore = (np.clip((coast_px - 0.42) / 0.16, 0, 1) * np.clip((0.62 - coast_px) / 0.12, 0, 1))[..., None]
    color = color * (1 - shore * 0.6) + np.array([150, 138, 96], dtype=np.float32) * shore * 0.6
    # дно
    seabed_mix = np.clip((0.47 - coast_px) / 0.30, 0, 1)[..., None]
    color = color * (1 - seabed_mix) + water * seabed_mix
    # пена вдоль линии воды
    foam = np.clip(1 - np.abs(coast_px - 0.46) / 0.05, 0, 1)[..., None]
    color = color * (1 - foam * 0.5) + np.array([210, 224, 224], dtype=np.float32) * foam * 0.5

    img = Image.fromarray(np.clip(color, 0, 255).astype(np.uint8), "RGB")
    draw = ImageDraw.Draw(img)

    # ---- дороги: маска по расстоянию (как BuildRoads в SoqMapView), затем фактура
    road_path = os.path.join(SOQ_ART, "soq_road.png")
    mask = np.zeros((px, px), dtype=np.float32)
    radius = tile_px * 0.175
    gy, gx = np.mgrid[0:px, 0:px].astype(np.float32)
    gy += 0.5
    gx += 0.5

    def splat(cx, cy, r):
        x0, x1 = max(0, int(cx - r)), min(px - 1, int(cx + r))
        y0, y1 = max(0, int(cy - r)), min(px - 1, int(cy + r))
        if x1 <= x0 or y1 <= y0:
            return
        d = np.sqrt((gx[y0:y1 + 1, x0:x1 + 1] - cx) ** 2 + (gy[y0:y1 + 1, x0:x1 + 1] - cy) ** 2)
        v = np.clip(1.0 - (d - r * 0.55) / (r * 0.45), 0, 1)
        mask[y0:y1 + 1, x0:x1 + 1] = np.maximum(mask[y0:y1 + 1, x0:x1 + 1], v)

    for y in range(size):
        for x in range(size):
            idx = y * size + x
            if not road[idx]:
                continue
            cx, cy = (x + 0.5) * tile_px, (y + 0.5) * tile_px
            splat(cx, cy, radius * 1.25)
            for dx, dy in ((1, 0), (0, 1)):
                nx, ny = x + dx, y + dy
                if not (0 <= nx < size and 0 <= ny < size):
                    continue
                if not road[ny * size + nx]:
                    continue
                ex, ey = (nx + 0.5) * tile_px, (ny + 0.5) * tile_px
                steps = max(2, int(np.hypot(ex - cx, ey - cy) / (tile_px * 0.12)))
                for sstep in range(steps + 1):
                    t = sstep / steps
                    splat(cx + (ex - cx) * t, cy + (ey - cy) * t, radius)

    for _ in range(2):
        mask = (mask + np.roll(mask, 1, 0) + np.roll(mask, -1, 0)
                + np.roll(mask, 1, 1) + np.roll(mask, -1, 1)) / 5.0
    # резкая кромка: полотно не «расплывается» в пыль, как при мягкой маске
    mask = np.clip((mask - 0.28) / 0.44, 0, 1)
    mask = mask * mask * (3 - 2 * mask)

    if os.path.exists(road_path):
        road_img = Image.open(road_path).convert("RGB")
        # тайлинг фактуры дороги по миру, как mainTextureScale в Unity
        rep = Image.new("RGB", (px, px))
        tw = max(6, int(round(tile_px * 0.55)))
        tile_small = road_img.resize((tw, tw), Image.BILINEAR)
        for ty in range(0, px, tw):
            for tx in range(0, px, tw):
                rep.paste(tile_small, (tx, ty))
        mask3 = np.clip(mask, 0, 1)[..., None]
        base_px = np.asarray(img).astype(np.float32)
        roadpx = np.asarray(rep).astype(np.float32)
        blended = base_px * (1 - mask3 * 0.92) + roadpx * (mask3 * 0.92)
        img = Image.fromarray(np.clip(blended, 0, 255).astype(np.uint8), "RGB")
        draw = ImageDraw.Draw(img)
    else:
        draw = ImageDraw.Draw(img)

    # ---- границы регионов: тонкая линия по стыку владений (как BuildBorders в
    # SoqMapView). Цвет — из той же палитры, альфа ~0.55.
    land_mask = result["land"]
    overlay = Image.new("RGBA", (px, px), (0, 0, 0, 0))
    odraw = ImageDraw.Draw(overlay)
    for y in range(size):
        for x in range(size):
            idx = y * size + x
            if not land_mask[idx]:
                continue
            rid = region_of[idx]
            col = REGION_PALETTE[rid % len(REGION_PALETTE)] + (78,)
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if not (0 <= nx < size and 0 <= ny < size):
                    continue
                if not land_mask[ny * size + nx]:
                    continue                       # по берегу линию не рисуем
                if region_of[ny * size + nx] == rid:
                    continue
                if dx != 0:
                    ex = (x + (1 if dx > 0 else 0)) * tile_px
                    odraw.line([(ex, y * tile_px), (ex, (y + 1) * tile_px)], fill=col, width=1)
                else:
                    ey = (y + (1 if dy > 0 else 0)) * tile_px
                    odraw.line([(x * tile_px, ey), ((x + 1) * tile_px, ey)], fill=col, width=1)
    img = Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB")
    draw = ImageDraw.Draw(img)

    # ---- объекты и декор
    OBJECT_SCALE = 1.6

    def object_width(sprite, scale):
        return max(3, int(round(sprite["w"] / 32.0 * scale * OBJECT_SCALE * tile_px / 4.0)))

    def draw_one(obj, defn, scale_mul, seed_k=0, tint_mul=1.0):
        biome = biome_of_region.get(obj.get("region", -1), "Temperate")
        name = sprite_name_for(defn, biome)
        sprite = sprites.get(name)
        if not sprite:
            return False
        crop = crop_sprite(sprite, atlases)
        if crop is None:
            return False
        scale = float(defn.get("scale", 1.0)) * scale_mul
        w = object_width(sprite, scale)
        h = max(2, int(round(crop.height * w / crop.width)))
        crop = crop.resize((w, h), Image.BILINEAR)
        if tint_mul != 1.0:
            crop = crop.point(lambda v: max(0, min(255, int(v * tint_mul))))
        jx = (base.hash01(obj["x"] * 13 + seed_k, obj["y"], 91) - 0.5) * tile_px * 0.55
        jz = (base.hash01(obj["x"], obj["y"] * 17 + seed_k, 137) - 0.5) * tile_px * 0.55
        cx = int((obj["x"] + 0.5) * tile_px + jx - w * float(sprite.get("pivotX", 0.5)))
        by = int((obj["y"] + 0.92) * tile_px + jz)
        if defn.get("flat"):
            # плоские предметы (камни/трава/грядки) — лежат на земле
            flat = crop.resize((w, max(2, int(h * 0.75))), Image.BILINEAR)
            img.paste(flat, (cx, by - flat.height // 2), flat)
        else:
            img.paste(crop, (cx, by - h), crop)
        return True

    # горы и деревья в одном списке: сортировка по y даёт правильные перекрытия
    # (в Unity это делает Z-буфер, здесь — порядок вставки)
    for deco in sorted(scatter_mountains(result, catalog) + scatter_decor(result, catalog),
                       key=lambda o: (o["y"], o["x"], o["k"])):
        defn = catalog.get(deco["def"])
        if not defn:
            continue
        sprite = sprites.get(sprite_name_for(defn, biome_of_region.get(deco.get("region", -1), "Temperate")))
        # отдельные блоб-тени у деревьев не рисуем: в SoQ притенение уже
        # впечено в землю под лесом (см. forest в рендере рельефа), а тёмные
        # овалы поверх травы выглядят грязно
        # тот же оттенок, что BuildDecor в SoqMapView: 0.78..1.12
        shade = 0.70 + base.hash01(deco["x"], deco["y"], 211) * 0.30
        draw_one(deco, defn, 1.0, deco["k"], shade)

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
