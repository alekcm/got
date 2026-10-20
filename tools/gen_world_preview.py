#!/usr/bin/env python3
"""Renders a top-down preview of canonical_map.json using the real 16 terrain tiles,
region tints and atlas decor — mirrors WorldMapView v2 (biome classes, pools, density).

Output: tools/world_map_preview.png
"""
import json
import math
import os
import random

from PIL import Image, ImageEnhance

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CANON = os.path.join(ROOT, "Assets/IronThrone/Resources/IronThroneData/canonical_map.json")
TERRAIN_DIR = os.path.join(ROOT, "Assets/IronThrone/Resources/Art/Terrain")
SLICES = os.path.join(ROOT, "Assets/IronThrone/Resources/Art/atlas_slices.json")
DECO_ATLAS = os.path.join(ROOT, "Assets/IronThrone/Resources/Art/StandaloneAdventureDecorationAtlas.png")
ENT_ATLAS = os.path.join(ROOT, "Assets/IronThrone/Resources/Art/PermanentMapEntityAtlas.png")
OUT = os.path.join(ROOT, "tools/world_map_preview.png")

TILE_NAMES = ["000_grass", "010_tundra", "024_dirt", "028_dry", "034_rock_green", "040_sand",
              "042_sand_rock", "044_fields", "050_autumn", "062_rock", "094_snow_rock", "096_snow",
              "100_cold_grass", "102_forest_mix", "104_marsh", "106_forest_pine"]

CLASS_HEIGHT = [1.00, 1.10, 1.05, 1.00, 1.35, 0.95, 1.40, 1.00,
                1.05, 1.60, 1.90, 1.30, 1.15, 1.20, 0.75, 1.25]

REGION_TINT = {
    "dorne": (1.12, 1.02, 0.80), "islands": (0.82, 0.86, 0.92), "wall": (0.92, 0.96, 1.08),
    "north": (0.86, 0.95, 0.92), "riverlands": (0.94, 1.02, 0.88), "vale": (0.92, 0.98, 1.06),
    "westerlands": (1.08, 1.00, 0.80), "reach": (1.06, 1.06, 0.84),
    "stormlands": (0.90, 1.00, 0.95), "crownlands": (1.00, 1.00, 0.92),
}


def hash01(c, r, salt):
    h = (c * 374761393 + r * 668265263 + salt * 2246822519) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    h = h ^ (h >> 16)
    return (h & 0xFFFFFF) / 16777215.0


def biome_class(region, terrain, c, r):
    n = hash01(c, r, 91)
    t = terrain or ""
    forest = t.startswith("forest") or t.startswith("wood")
    hills = t.startswith("hill")
    mount = t.startswith("mount") or t.startswith("peak")
    snow = t.startswith("snow") or t.startswith("frost")
    marsh = t.startswith("marsh") or t.startswith("swamp")
    desert = t.startswith("desert") or t.startswith("sand")
    if region == "dorne":
        if hills or mount:
            return 6 if n < 0.75 else 9
        return 5 if n < 0.70 else (3 if n < 0.90 else 2)
    if region == "islands":
        if hills or mount:
            return 9 if n < 0.60 else 4
        return 4 if n < 0.55 else (9 if n < 0.80 else 2)
    if region == "wall":
        return 11 if n < 0.60 else (10 if n < 0.85 else 1)
    if region == "north":
        if snow:
            return 11 if n < 0.70 else 10
        if mount:
            return 10
        if hills:
            return 1 if n < 0.50 else (12 if n < 0.80 else 9)
        if forest:
            return 15 if n < 0.75 else 12
        return 12 if n < 0.70 else (1 if n < 0.90 else 8)
    if region == "riverlands":
        if marsh:
            return 14
        if forest:
            return 13 if n < 0.70 else 8
        if hills or mount:
            return 4 if n < 0.60 else 9
        return 0 if n < 0.75 else (13 if n < 0.90 else 7)
    if region == "vale":
        if mount:
            return 10 if n < 0.60 else 9
        if hills:
            return 4 if n < 0.60 else 9
        if forest:
            return 13
        return 0 if n < 0.80 else 4
    if region == "westerlands":
        if mount:
            return 9
        if hills:
            return 9 if n < 0.60 else 4
        if forest:
            return 13
        return 0 if n < 0.70 else (7 if n < 0.85 else 2)
    if region == "reach":
        if hills or mount:
            return 4 if n < 0.60 else 9
        if forest:
            return 13 if n < 0.60 else 0
        return 0 if n < 0.55 else (7 if n < 0.85 else 3)
    if region == "stormlands":
        if hills or mount:
            return 4 if n < 0.60 else 9
        if forest:
            return 13
        return 0 if n < 0.80 else 7
    if region == "crownlands":
        if hills or mount:
            return 4
        if forest:
            return 13 if n < 0.80 else 0
        return 0 if n < 0.75 else 7
    return 0


def decor_count(cls, c, r):
    j = int(hash01(c, r, 77) * 7)
    if cls in (15, 13):
        return 12 + j
    if cls in (10, 9):
        return 10 + (j % 7)
    if cls in (6, 4):
        return 6 + (j % 5)
    if cls == 14:
        return 4 + (j % 5)
    if cls in (5, 3):
        return 2 + (j % 4)
    return 3 + (j % 4)


def pool_for(region, cls, cats):
    forest = cls in (15, 13)
    rocky = cls in (9, 10, 6, 4)
    u = lambda *names: [s for n in names for s in cats.get(n, [])]
    dry_names = {"deco_11", "deco_12", "deco_16", "deco_20", "deco_30", "deco_31", "deco_32",
                 "deco_36", "deco_37", "deco_38", "deco_39", "deco_45", "deco_46", "deco_51",
                 "deco_22", "deco_23", "deco_25", "deco_26", "deco_28"}
    dry = lambda *names: [s for n in names for s in cats.get(n, []) if s["name"] in dry_names]
    if region == "dorne":
        return dry("rocks", "small", "bushes")
    if region == "islands":
        return dry("rocks", "small") + u("trees_pine")
    if region == "wall":
        return u("trees_pine", "rocks", "small")
    if region == "north":
        return u("trees_pine", "trees_big", "bushes") if forest else u("trees_big", "bushes", "small", "rocks")
    if region == "riverlands":
        return u("trees_big", "bushes", "trees_pine") if forest else u("bushes", "small", "fields")
    if region == "vale":
        return u("rocks", "trees_pine", "small") if rocky else u("trees_big", "bushes", "rocks")
    if region == "westerlands":
        return u("rocks", "small", "bushes") if rocky else u("trees_big", "bushes", "rocks")
    if region == "reach":
        return u("trees_big", "bushes") if forest else u("fields", "bushes", "small", "trees_big")
    if region == "stormlands":
        return u("trees_big", "bushes", "trees_pine") if forest else u("trees_big", "bushes", "rocks", "small")
    if region == "crownlands":
        return u("trees_big", "bushes") if forest else u("bushes", "small", "trees_big")
    return u("trees_big", "bushes", "small")


def main():
    d = json.load(open(CANON, encoding="utf-8"))
    W, H = d["width"], d["height"]

    tiles_img = [Image.open(os.path.join(TERRAIN_DIR, n + ".png")).convert("RGB") for n in TILE_NAMES]

    slices = json.load(open(SLICES, encoding="utf-8"))["sprites"]
    cats = {}
    buildings = {}
    deco_atlas = Image.open(DECO_ATLAS).convert("RGBA")
    ent_atlas = Image.open(ENT_ATLAS).convert("RGBA")
    for s in slices:
        if s["atlas"] == "StandaloneAdventureDecorationAtlas":
            cats.setdefault(s["cat"], []).append(s)
        else:
            buildings[s["name"]] = s

    # hex cell: flat-top-ish rectangle per hex for a quick impression
    CS = 26  # cell px
    PAD = 30
    img = Image.new("RGB", (W * CS + PAD * 2, H * CS + PAD * 2), (36, 78, 96))
    # subtle sea gradient
    for y in range(img.height):
        k = y / img.height
        col = (int(30 + 18 * k), int(70 + 16 * k), int(92 + 12 * k))
        for x in range(0, img.width, 8):
            img.paste(Image.new("RGB", (8, 1), col), (x, y))

    tiles = {(t["c"], t["r"]): t for t in d["tiles"]}
    rnd = random.Random(1234)

    for r in range(H):
        for c in range(W):
            t = tiles[(c, r)]
            x0 = PAD + c * CS + (CS // 2 if r & 1 else 0)
            y0 = PAD + r * CS
            if t["sea"]:
                continue
            cls = biome_class(t["region"], t["terrain"], c, r)
            tile = tiles_img[cls].resize((CS + 2, CS + 2), Image.BILINEAR)
            tint = REGION_TINT.get(t["region"], (1, 1, 1))
            # region tint + height shading (fake relief)
            tile = ImageEnhance.Brightness(tile).enhance(0.92 + 0.10 * hash01(c, r, 55))
            px = tile.load()
            for yy in range(CS + 2):
                for xx in range(CS + 2):
                    rr, gg, bb = px[xx, yy]
                    px[xx, yy] = (min(255, int(rr * tint[0])), min(255, int(gg * tint[1])),
                                  min(255, int(bb * tint[2])))
            img.paste(tile, (x0 - 1, y0 - 1))

    # decor
    for r in range(H):
        for c in range(W):
            t = tiles[(c, r)]
            if t["sea"]:
                continue
            cls = biome_class(t["region"], t["terrain"], c, r)
            pool = pool_for(t["region"], cls, cats)
            if not pool:
                continue
            x0 = PAD + c * CS + (CS // 2 if r & 1 else 0)
            y0 = PAD + r * CS
            n = decor_count(cls, c, r)
            for k in range(n):
                s = pool[int(hash01(c, r, k * 7 + 3) * len(pool)) % len(pool)]
                dx = (hash01(c, r, k * 13 + 1) - 0.5) * CS * 1.5
                dy = (hash01(c, r, k * 13 + 2) - 0.5) * CS * 1.4
                sc = 0.28 + hash01(c, r, k * 13 + 5) * 0.22
                if cls in (9, 10, 6):
                    sc *= 1.25
                sp = deco_atlas.crop((s["x"], deco_atlas.height - s["y"] - s["h"],
                                      s["x"] + s["w"], deco_atlas.height - s["y"]))
                sp = sp.resize((max(2, int(s["w"] * sc)), max(2, int(s["h"] * sc))), Image.BILINEAR)
                img.paste(sp, (int(x0 + dx - sp.width / 2), int(y0 + dy - sp.height)), sp)

    # buildings
    for p in d["provinces"]:
        if len(p["settlement"]) < 2:
            continue
        c, r = p["settlement"]
        if p.get("isCapital", p.get("is_capital")):
            key = "green_town"
        elif p["port"] and "hut2" in buildings:
            key = "hut2"
        else:
            opts = {"dorne": ["pyramid", "temple", "village", "hut"],
                    "islands": ["hut", "hut2", "village"],
                    "reach": ["village", "hut", "temple", "fountain", "hut2"]}.get(
                p["region"], ["village", "hut", "hut2", "temple", "fountain"])
            key = opts[int(hash01(c, r, 17) * len(opts)) % len(opts)]
        s = buildings.get(key)
        if not s:
            continue
        x0 = PAD + c * CS + (CS // 2 if r & 1 else 0)
        y0 = PAD + r * CS
        sp = ent_atlas.crop((s["x"], ent_atlas.height - s["y"] - s["h"],
                             s["x"] + s["w"], ent_atlas.height - s["y"]))
        sc = 0.34
        sp = sp.resize((max(3, int(s["w"] * sc)), max(3, int(s["h"] * sc))), Image.BILINEAR)
        img.paste(sp, (int(x0 - sp.width / 2), int(y0 - sp.height + 4)), sp)

    img.save(OUT)
    print(f"-> {OUT} ({img.width}, {img.height})")


if __name__ == "__main__":
    main()
