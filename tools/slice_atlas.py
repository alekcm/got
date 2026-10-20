#!/usr/bin/env python3
"""Нарезка атласов 'Как должно выглядеть/Материалы' по альфа-островам.
Пишет Assets/IronThrone/Resources/Art/atlas_slices.json и превью-сетки в tools/."""
import json, sys
from collections import deque
from PIL import Image, ImageDraw

SRC = "Как должно выглядеть/Материалы/Texture2D"
OUT_JSON = "Assets/IronThrone/Resources/Art/atlas_slices.json"
PREVIEW_DIR = "tools"

ATLASES = {
    "StandaloneAdventureDecorationAtlas": "deco",
    "PermanentMapEntityAtlas": "entities",
}

def slice_islands(img, min_area=60, gap=0, alpha_min=128, erode=2):
    """Connected components по непрозрачным пикселям -> bbox'ы.
    erode: сколько пикселей срезать с маски связности, чтобы разорвать тени-мосты."""
    w, h = img.size
    a = img.split()[-1]
    px = a.load()

    def solid(x, y):
        if px[x, y] < alpha_min: return False
        for dx in range(-erode, erode + 1):
            for dy in range(-erode, erode + 1):
                xx, yy = x + dx, y + dy
                if xx < 0 or yy < 0 or xx >= w or yy >= h or px[xx, yy] < alpha_min:
                    return False
        return True

    seen = [[False] * w for _ in range(h)]
    boxes = []
    for y0 in range(h):
        for x0 in range(w):
            if seen[y0][x0] or not solid(x0, y0):
                continue
            q = deque([(x0, y0)])
            seen[y0][x0] = True
            minx = maxx = x0
            miny = maxy = y0
            area = 0
            while q:
                x, y = q.popleft()
                area += 1
                if x < minx: minx = x
                if x > maxx: maxx = x
                if y < miny: miny = y
                if y > maxy: maxy = y
                for nx, ny in ((x-1,y),(x+1,y),(x,y-1),(x,y+1)):
                    if 0 <= nx < w and 0 <= ny < h and not seen[ny][nx] and solid(nx, ny):
                        seen[ny][nx] = True
                        q.append((nx, ny))
            if area >= min_area:
                boxes.append([minx, miny, maxx + 1, maxy + 1])
    # склейка пересекающихся/близких боксов (дерево = ствол+крона)
    merged = True
    while merged:
        merged = False
        out = []
        while boxes:
            b = boxes.pop()
            for o in out:
                if b[0] < o[2] + gap and b[2] + gap > o[0] and b[1] < o[3] + gap and b[3] + gap > o[1]:
                    o[0] = min(o[0], b[0]); o[1] = min(o[1], b[1])
                    o[2] = max(o[2], b[2]); o[3] = max(o[3], b[3])
                    merged = True
                    break
            else:
                out.append(b)
        boxes = out
    boxes.sort(key=lambda b: (b[1], b[0]))
    return boxes

def main():
    # Ручные rect'ы построек (x, y сверху, w, h) — атлас упакован вплотную, авто-нарезка склеивает.
    entity_rects = {
        "hut":        (2, 2, 54, 60),        # башня с соломенной крышей
        "hut2":       (58, 2, 58, 60),       # вторая башня
        "village":    (288, 2, 80, 60),      # деревня с забором
        "pyramid":    (128, 252, 66, 68),    # ступенчатая мшистая пирамида
        "fountain":   (320, 66, 64, 60),     # каменный монумент-фонтан
        "temple":     (752, 256, 48, 64),    # дом с оранжевой крышей
        "green_town": (608, 320, 112, 72),   # город с зелёными крышами (столица)
    }
    # выверено конвейером ресайза (/tmp/pipeline_test.png): только чистые спрайты
    deco_categories = {
        "trees_big":   [0, 33, 34],               # дуб, ивы
        "trees_pine":  [1, 19, 54, 55],           # сосна, кипарис, ели (47-49 отсеяны: тёмные глыбы)
        "trees_bare":  [],
        "bushes":      [11, 12, 13, 14, 16, 20],
        "rocks":       [22, 23, 25, 26, 28, 29, 50],
        "fields":      [2, 3, 7, 8, 40, 41],
        "small":       [30, 31, 32, 36, 37, 38, 39, 45, 46, 51, 56, 57],
    }
    db = {}
    for atlas, tag in ATLASES.items():
        img = Image.open(f"{SRC}/{atlas}.png").convert("RGBA")
        w, h = img.size
        if atlas == "PermanentMapEntityAtlas":
            boxes = [[x, y, x + ww, y + hh] for (x, y, ww, hh) in entity_rects.values()]
        else:
            boxes = slice_islands(img)
        db[atlas] = {
            "size": [w, h],
            "slices": [
                {"i": i, "x": b[0], "y": h - b[3], "w": b[2] - b[0], "h": b[3] - b[1]}  # y снизу (Unity)
                for i, b in enumerate(boxes)
            ],
        }
        if atlas == "PermanentMapEntityAtlas":
            db[atlas]["names"] = list(entity_rects.keys())
        if atlas == "StandaloneAdventureDecorationAtlas":
            db[atlas]["categories"] = deco_categories
        print(f"{atlas}: {w}x{h}, {len(boxes)} спрайтов")
        # превью с нумерацией
        vis = Image.new("RGBA", img.size, (30, 30, 40, 255))
        vis.alpha_composite(img)
        d = ImageDraw.Draw(vis)
        for i, b in enumerate(boxes):
            d.rectangle(b, outline=(255, 80, 80, 255))
            d.text((b[0] + 2, b[1] + 2), str(i), fill=(255, 220, 80, 255))
        vis.convert("RGB").save(f"{PREVIEW_DIR}/slice_{tag}.png")
    import os
    os.makedirs("Assets/IronThrone/Resources/Art", exist_ok=True)
    # Плоский формат под JsonUtility в Unity
    entries = []
    inv_cat = {}
    for cat, idxs in db["StandaloneAdventureDecorationAtlas"].get("categories", {}).items():
        for i in idxs:
            inv_cat[i] = cat
    for s in db["StandaloneAdventureDecorationAtlas"]["slices"]:
        entries.append({"atlas": "StandaloneAdventureDecorationAtlas", "name": "deco_%d" % s["i"],
                        "cat": inv_cat.get(s["i"], "misc"),
                        "x": s["x"], "y": s["y"], "w": s["w"], "h": s["h"]})
    names = db["PermanentMapEntityAtlas"].get("names", [])
    for k, s in enumerate(db["PermanentMapEntityAtlas"]["slices"]):
        nm = names[k] if k < len(names) else "ent_%d" % k
        entries.append({"atlas": "PermanentMapEntityAtlas", "name": nm, "cat": "building",
                        "x": s["x"], "y": s["y"], "w": s["w"], "h": s["h"]})
    with open(OUT_JSON, "w") as f:
        json.dump({"sprites": entries}, f, ensure_ascii=False, indent=1)
    print("->", OUT_JSON, len(entries), "entries")

if __name__ == "__main__":
    main()
