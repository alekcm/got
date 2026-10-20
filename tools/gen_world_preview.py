#!/usr/bin/env python3
"""Превью глобальной карты в стиле эталонов (Songs of Conquest 2.5D).
Те же текстуры/атласы и та же гекс-математика, что и в WorldMapView.
Пишет tools/world_map_preview.png."""
import json, math, random
from PIL import Image, ImageDraw, ImageFilter, ImageChops

MATS = "Как должно выглядеть/Материалы"
OUT = "tools/world_map_preview.png"
SCALE = 2                  # пикселей на условную единицу мира
HEX_W = 42                 # ширина гекса в пикселях
ROW_H = int(1.5 / 1.7320508 * HEX_W)
HEX_H = int(2.0 / 1.7320508 * HEX_W)
TILE = 52                  # размер штампа текстуры (чуть больше гекса)

TERRAIN_LAYER = {"plains": 4, "forest": 106, "hills": 24, "mountains": 62,
                 "desert": 40, "marsh": 104, "snow": 96}

def terrain_key(t):
    t = (t or "plains").lower()
    for k in TERRAIN_LAYER:
        if t.startswith(k[:4]):
            return k
    if t.startswith("step") or t.startswith("sand") or t.startswith("dune"):
        return "desert"
    if t.startswith("swa") or t.startswith("bog"):
        return "marsh"
    if t.startswith("tun") or t.startswith("fro"):
        return "snow"
    return "plains"

def load_tiles():
    im = Image.open(f"{MATS}/Texture2DArray/CartographyAdventureTerrainTextureArray.png")
    tiles = {}
    for k, layer in TERRAIN_LAYER.items():
        tiles[k] = im.crop((0, layer * 128, 128, (layer + 1) * 128)).convert("RGB").resize((TILE, TILE), Image.NEAREST)
    return tiles

def soft_mask(size, feather=10):
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    d.ellipse((feather // 2, feather // 2, size - feather // 2, size - feather // 2), fill=255)
    return m.filter(ImageFilter.GaussianBlur(feather / 2))

def hex_pos(c, r):
    x = HEX_W * (c + 0.5 * (r & 1))
    y = ROW_H * r
    return x, y

def main():
    map_data = json.load(open("Assets/IronThrone/Resources/IronThroneData/canonical_map.json"))
    W, H = map_data["width"], map_data["height"]
    tiles = {(t["c"], t["r"]): t for t in map_data["tiles"]}

    canvas_w = int(HEX_W * (W + 1.5))
    canvas_h = int(ROW_H * (H + 1.2))
    canvas = Image.new("RGB", (canvas_w, canvas_h), (28, 42, 52))
    mask = soft_mask(TILE, 12)

    terr = load_tiles()
    water_tile = Image.open(f"{MATS}/Texture2DArray/CartographyAdventureWaterTextureArray.png") \
        .crop((0, 0, 128, 128)).convert("RGB").resize((TILE, TILE), Image.NEAREST)

    # ---- вода (сначала, под всем): крупный тайл 128px, без повтора по гексам
    sea_layer = Image.new("RGB", (canvas_w, canvas_h), (24, 74, 88))
    wt128 = Image.open(f"{MATS}/Texture2DArray/CartographyAdventureWaterTextureArray.png") \
        .crop((0, 0, 128, 128)).convert("RGB")
    for ty in range(0, canvas_h, 128):
        for tx in range(0, canvas_w, 128):
            sea_layer.paste(wt128, (tx, ty))
    canvas.paste(sea_layer, (0, 0))

    # ---- рельеф: мягкие штампы с перекрытием (сплейтинг как в шейдере),
    #      но только внутри маски суши — чтобы не вылезать в воду
    land_img = Image.new("RGB", (canvas_w, canvas_h), (70, 90, 60))
    landm = Image.new("L", (canvas_w, canvas_h), 0)
    ld = ImageDraw.Draw(landm)
    for r in range(H + 1):
        for c in range(W + 1):
            t = tiles.get((c, r))
            if not t or t.get("sea"):
                continue
            k = terrain_key(t.get("terrain"))
            x, y = hex_pos(c, r)
            land_img.paste(terr[k], (int(x - TILE / 2), int(y - TILE / 2)), mask)
            ld.ellipse((x - HEX_W * 0.56, y - HEX_W * 0.48, x + HEX_W * 0.56, y + HEX_W * 0.48), fill=255)
    landm = landm.filter(ImageFilter.GaussianBlur(2))
    canvas.paste(land_img, (0, 0), landm)

    # ---- береговая пена: кольцо вдоль уреза воды (расширение маски суши)
    dilated = landm.filter(ImageFilter.MaxFilter(11)).filter(ImageFilter.GaussianBlur(2))
    foam = ImageChops.subtract(dilated, landm).filter(ImageFilter.GaussianBlur(3))
    canvas.paste(Image.new("RGB", (canvas_w, canvas_h), (200, 228, 224)), (0, 0),
                 foam.point(lambda v: int(v * 0.62)))

    # ---- декор из атласа
    db = json.load(open("Assets/IronThrone/Resources/Art/atlas_slices.json"))["sprites"]
    deco = Image.open(f"{MATS}/Texture2D/StandaloneAdventureDecorationAtlas.png").convert("RGBA")
    ents = Image.open(f"{MATS}/Texture2D/PermanentMapEntityAtlas.png").convert("RGBA")

    def sprite_img(s, atlas):
        x, y, w, h = s["x"], s["y"], s["w"], s["h"]
        top = atlas.size[1] - y - h
        return atlas.crop((x, top, x + w, top + h))

    pools = {}
    for s in db:
        pools.setdefault(s["cat"] if s["atlas"].startswith("Standalone") else "building", []).append(s)

    rnd = random.Random(20261020)
    draw_list = sorted(tiles.keys(), key=lambda cr: (cr[1], cr[0]))
    for (c, r) in draw_list:
        t = tiles[(c, r)]
        if t.get("sea"):
            continue
        k = terrain_key(t.get("terrain"))
        x, y = hex_pos(c, r)
        picks = []
        if k == "forest":
            picks = rnd.choices(pools.get("trees_big", []) + pools.get("trees_pine", []), k=2)
        elif k == "mountains":
            picks = rnd.choices(pools.get("rocks", []), k=2)
        elif k == "hills":
            picks = rnd.choices(pools.get("rocks", []) + pools.get("trees_pine", []), k=2)
        elif k == "marsh":
            picks = rnd.choices(pools.get("bushes", []) + pools.get("rocks", []), k=1)
        elif k == "snow":
            picks = rnd.choices(pools.get("trees_pine", []) + pools.get("bushes", []), k=1)
        elif k == "desert":
            picks = rnd.choices(pools.get("rocks", []), k=1)
        else:
            if rnd.random() < 0.45:
                picks = rnd.choices(pools.get("fields", []) + pools.get("bushes", []) + pools.get("small", []), k=1)
        for s in picks:
            sp = sprite_img(s, deco)
            if s["cat"] == "fields":
                fw = int(HEX_W * 1.15)
                sp = sp.resize((fw, fw), Image.LANCZOS)
                canvas.paste(sp, (int(x - fw / 2), int(y - fw / 2)), sp)
                continue
            # масштаб как в WorldMapView: h/32 юнита * (HEX_W/1.732) пикс/юнит
            th = max(8, int(s["h"] / 32.0 * (HEX_W / 1.7320508)))
            th = min(th, 72)
            tw = max(6, int(sp.size[0] * th / sp.size[1]))
            sp = sp.resize((tw, th), Image.LANCZOS)
            jx = rnd.uniform(-HEX_W * 0.22, HEX_W * 0.22)
            canvas.paste(sp, (int(x + jx - tw / 2), int(y - th * 0.72)), sp)

    # ---- постройки
    for p in map_data.get("provinces", []):
        st = p.get("settlement")
        if not st or len(st) < 2:
            continue
        c, r = st[0], st[1]
        key = "green_town" if p.get("isCapital") else rnd.choice(["village", "hut", "hut2", "temple"])
        cand = [s for s in db if s["name"] == key] or [s for s in db if s["name"] in ("village", "hut")]
        if not cand:
            continue
        s = cand[0]
        sp = sprite_img(s, ents)
        th = int(HEX_W * 1.15)
        tw = max(8, int(sp.size[0] * th / sp.size[1]))
        sp = sp.resize((tw, th), Image.LANCZOS)
        x, y = hex_pos(c, r)
        canvas.paste(sp, (int(x - tw / 2), int(y - th * 0.78)), sp)

    # ---- атмосфера: тёплый градиент + виньетка
    grad = Image.new("RGB", (canvas_w, canvas_h), (255, 244, 214))
    gy = Image.linear_gradient("L").resize((canvas_w, canvas_h))
    canvas = Image.composite(canvas, ImageChops.multiply(canvas, grad), gy.point(lambda v: 255 - int(v * 0.35)))
    vig = Image.new("L", (canvas_w, canvas_h), 0)
    vd = ImageDraw.Draw(vig)
    vd.ellipse((-canvas_w * 0.25, -canvas_h * 0.35, canvas_w * 1.25, canvas_h * 1.35), fill=255)
    vig = vig.filter(ImageFilter.GaussianBlur(80))
    canvas = ImageChops.multiply(canvas, Image.merge("RGB", (vig.point(lambda v: 128 + v // 2),) * 3))

    canvas.save(OUT)
    print("->", OUT, canvas.size)

if __name__ == "__main__":
    main()
