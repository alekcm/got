#!/usr/bin/env python3
"""Regenerates canonical_map.json from 'Карта Вестероса.webp' (canonical geography).

- Land mask is sampled from the parchment map (morphological opening kills text noise).
- 106 provinces keep ids/names/regions/houses/levels/kinds; they are re-placed into
  the true regional boxes of Westeros; settlements snap to real castle/town locations.
- Tiles get region-aware terrains; neighbors/coastal recomputed.

Usage: regen_canonical_map.py [--preview] [--out path]
"""
import argparse
import hashlib
import json
import os
import sys

from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WEBP = os.path.join(ROOT, "Как должно выглядеть", "Карта Вестероса.webp")
CANON = os.path.join(ROOT, "Assets", "IronThrone", "Resources", "IronThroneData", "canonical_map.json")

W, H = 22, 56                      # new map grid (portrait, like real Westeros)
CONTENT_C, CONTENT_R = 18, 48      # content box (Westeros spans this)
OFF_C, OFF_R = 2, 4

# land bbox in the webp (px) measured from the parchment test (Westeros + Skagos)
LB_X0, LB_Y0, LB_X1, LB_Y1 = 28, 25, 1000, 2510
WP_W, WP_H = 1758, 2560


def px_to_lb(px, py):
    return ((px - LB_X0) / (LB_X1 - LB_X0), (py - LB_Y0) / (LB_Y1 - LB_Y0))


def wp_to_lb(fx, fy):
    return px_to_lb(fx * WP_W, fy * WP_H)


# region rules evaluated in order against webp-fraction (x, y) of a land cell.
# Fallback: riverlands. Rules were fitted to the canonical webp labels/castles.
REGION_RULES = [
    # Iron Islands archipelago (west of the mainland at mid-height)
    ("islands",     lambda x, y: x < 0.155 and 0.415 < y < 0.565),
    # the Wall strip + Haunted Forest edge
    ("wall",        lambda x, y: y < 0.168 and x < 0.50),
    # the North (down to the Neck / Moat Cailin)
    ("north",       lambda x, y: y < 0.475 or (x < 0.25 and y < 0.515)),
    # Dorne: south of the Red Mountains diagonal
    ("dorne",       lambda x, y: y > 0.80 and y > 0.875 - 0.207 * (x - 0.185)),
    # Dragonstone island in Blackwater Bay
    ("crownlands",  lambda x, y: 0.375 < x < 0.425 and 0.715 < y < 0.775),
    # Stormlands (the Marches + the storm coast)
    ("stormlands",  lambda x, y: x > 0.32 and y > 0.735),
    # Crownlands (KL, Duskendale, Crackclaw Point)
    ("crownlands",  lambda x, y: x > 0.325 and 0.60 < y <= 0.735),
    # the Vale (the Eyrie, Gulltown, the Fingers)
    ("vale",        lambda x, y: x > 0.375 and 0.42 < y < 0.60),
    # the Westerlands (Casterly Rock, Lannisport, the Cornfield)
    ("westerlands", lambda x, y: (x < 0.22 and 0.55 < y < 0.72) or (x < 0.28 and 0.60 <= y < 0.72)),
    # the Reach (Highgarden, Oldtown, the Arbor)
    ("reach",       lambda x, y: y > 0.655),
    # the Riverlands (the Trident valley)
    ("riverlands",  lambda x, y: 0.18 < x < 0.385 and 0.46 < y < 0.64),
]


def region_for(fx, fy):
    """fx, fy are LB-fractions; rules use webp fractions."""
    wx = (fx * (LB_X1 - LB_X0) + LB_X0) / WP_W
    wy = (fy * (LB_Y1 - LB_Y0) + LB_Y0) / WP_H
    for name, rule in REGION_RULES:
        if rule(wx, wy):
            return name
    return None

# real castle/town anchors: province id -> (fx, fy) fractions of the full webp
ANCHORS = {
    # the Wall
    "castle_black": (0.325, 0.155), "eastwatch": (0.430, 0.135), "shadow_tower": (0.185, 0.145),
    "nightfort": (0.295, 0.150),
    # the North
    "winterfell": (0.305, 0.385), "white_harbor": (0.345, 0.425), "moat_cailin": (0.265, 0.445),
    "dreadfort": (0.410, 0.300), "karhold": (0.445, 0.230), "last_hearth": (0.385, 0.245),
    "widows_watch": (0.475, 0.330), "hornwood": (0.385, 0.365), "cerwyn": (0.335, 0.345),
    "torrhen": (0.245, 0.345), "barrowton": (0.225, 0.425), "deepwood": (0.185, 0.285),
    "seadragon": (0.195, 0.335), "bear_island": (0.115, 0.300), "ramsgate": (0.465, 0.375),
    "oldcastle": (0.305, 0.295), "hearths_home": (0.425, 0.285), "longbow_hall": (0.425, 0.265),
    # the Iron Islands
    "pyke": (0.088, 0.505), "great_wyk": (0.105, 0.530), "old_wyk": (0.118, 0.505),
    "harlaw": (0.135, 0.490), "saltcliffe": (0.095, 0.545), "blacktyde": (0.105, 0.485),
    # the Riverlands
    "riverrun": (0.235, 0.585), "harrenhal": (0.315, 0.575), "seagard": (0.225, 0.520),
    "maidenpool": (0.345, 0.555), "darry": (0.305, 0.545), "twins": (0.285, 0.505),
    "raventree": (0.265, 0.520), "pinkmaiden": (0.255, 0.545), "stone_hedge": (0.295, 0.565),
    "saltpans": (0.380, 0.545), "fairmarket": (0.315, 0.515), "old_anchor": (0.415, 0.545),
    "hornvale": (0.265, 0.585),
    # the Vale
    "eyrie": (0.395, 0.515), "gulltown": (0.465, 0.515), "runestone": (0.445, 0.495),
    "redfort": (0.445, 0.480), "ironoaks": (0.425, 0.497), "strongsong": (0.375, 0.472),
    "sisterton": (0.325, 0.435),
    # the Westerlands
    "casterly": (0.205, 0.635), "lannisport": (0.195, 0.655), "golden_tooth": (0.255, 0.600),
    "silverhill": (0.245, 0.625), "deep_den": (0.225, 0.615), "crakehall": (0.175, 0.655),
    "banefort": (0.165, 0.585), "cornfield": (0.215, 0.668), "ashemark": (0.195, 0.612),
    # the Crownlands
    "kings_landing": (0.375, 0.655), "duskendale": (0.415, 0.635), "dragonstone": (0.395, 0.745),
    "rosby": (0.400, 0.645), "stokeworth": (0.388, 0.632), "hayford": (0.355, 0.655),
    "wickenden": (0.455, 0.655), "rooks_rest": (0.395, 0.612), "crackclaw": (0.445, 0.615),
    "sweetport": (0.390, 0.730),
    # the Reach
    "highgarden": (0.235, 0.735), "oldtown": (0.175, 0.845), "brightwater": (0.275, 0.795),
    "goldengrove": (0.245, 0.780), "red_lake": (0.225, 0.755), "bitterbridge": (0.290, 0.715),
    "tumbleton": (0.315, 0.685), "longtable": (0.300, 0.775), "horn_hill": (0.265, 0.815),
    "sunhouse": (0.210, 0.880), "blackcrown": (0.245, 0.845), "honeyholt": (0.275, 0.855),
    "arbor": (0.135, 0.825),
    # the Stormlands
    "storms_end": (0.425, 0.755), "nightsong": (0.345, 0.775), "blackhaven": (0.385, 0.815),
    "stonehelm": (0.365, 0.795), "ashford": (0.325, 0.745), "crows_nest": (0.335, 0.835),
    "felwood": (0.365, 0.800), "griffins_roost": (0.415, 0.785), "grandview": (0.395, 0.802),
    "tarth": (0.445, 0.765), "estermont": (0.440, 0.825),
    # Dorne
    "sunspear": (0.475, 0.885), "yronwood": (0.325, 0.855), "sandstone": (0.295, 0.905),
    "godsgrace": (0.375, 0.915), "ghost_hill": (0.420, 0.900), "lemonwood": (0.455, 0.925),
    "hellholt": (0.335, 0.925), "vaith": (0.300, 0.940), "saltshore": (0.400, 0.950),
    "skyreach": (0.330, 0.865), "starfall": (0.250, 0.900), "three_towers": (0.220, 0.885),
    "planky": (0.415, 0.945),
}

# terrain pool per region (old canonical terrains are kept as strings)
REGION_TERRAIN = {
    "wall": ["snow"] * 6 + ["hills", "forest"],
    "north": ["plains"] * 4 + ["forest"] * 3 + ["hills"] * 2 + ["snow"],
    "islands": ["plains"] * 2 + ["hills"] * 2,
    "riverlands": ["plains"] * 2 + ["forest"] * 2 + ["marsh", "hills"],
    "vale": ["hills"] * 3 + ["plains"] * 2 + ["forest", "mountains"],
    "westerlands": ["hills"] * 3 + ["plains"] * 2 + ["forest", "mountains"],
    "reach": ["plains"] * 5 + ["forest", "hills"],
    "stormlands": ["plains"] * 3 + ["forest"] * 2 + ["hills"],
    "crownlands": ["plains"] * 3 + ["forest"] * 2,
    "dorne": ["desert"] * 5 + ["hills", "desert"],
}


def hx(h, n):
    return int(hashlib.md5(h.encode()).hexdigest(), 16) % n


def build_landmask():
    im = Image.open(WEBP).convert("RGB")
    wpx, hpx = im.size
    px = im.load()
    mask = [[0] * wpx for _ in range(hpx)]
    for y in range(hpx):
        for x in range(wpx):
            r, g, b = px[x, y]
            mask[y][x] = 1 if (r > 120 and g > 100 and b > 70 and r > b + 20) else 0
    # morphological opening with radius 3 via PIL: erode then dilate
    m = Image.new("L", (wpx, hpx))
    m.putdata([255 if v else 0 for row in mask for v in row])
    m = m.filter(ImageFilter.MinFilter(7)).filter(ImageFilter.MaxFilter(7))
    md = m.load()
    return md, wpx, hpx


def sample_land(md, size, fx, fy):
    wpx, hpx = size
    cx = LB_X0 + fx * (LB_X1 - LB_X0)
    cy = LB_Y0 + fy * (LB_Y1 - LB_Y0)
    # majority vote over the cell footprint (~50 px), 6x6 probes
    hit = 0
    n = 0
    for dy in range(-20, 21, 8):
        for dx in range(-20, 21, 8):
            x = int(cx + dx)
            y = int(cy + dy)
            n += 1
            if 0 <= x < wpx and 0 <= y < hpx and md[x, y] > 127:
                hit += 1
    return hit * 100 >= n * 10


def grid_to_fx(c):
    """grid col -> webp fraction x (content stretched across CONTENT_C cols)"""
    return (c - OFF_C + 0.5) / CONTENT_C


def grid_to_fy(r):
    return (r - OFF_R + 0.5) / CONTENT_R


def fx_to_grid(fx):
    return fx * CONTENT_C + OFF_C


def fy_to_grid(fy):
    return fy * CONTENT_R + OFF_R


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", action="store_true")
    ap.add_argument("--out", default=CANON)
    args = ap.parse_args()

    md, wpx, hpx = build_landmask()

    # assign land + region per cell
    cells = {}  # (c,r) -> dict
    for r in range(H):
        for c in range(W):
            fx, fy = grid_to_fx(c), grid_to_fy(r)
            land = sample_land(md, (wpx, hpx), fx, fy) if 0 <= fx <= 1 and 0 <= fy <= 1 else False
            reg = region_for(fx, fy) if land else None
            cells[(c, r)] = {"land": land, "region": reg, "fx": fx, "fy": fy}

    if args.preview:
        letters = {"wall": "W", "north": "N", "islands": "I", "riverlands": "R",
                   "vale": "V", "westerlands": "L", "reach": "H", "stormlands": "S",
                   "crownlands": "C", "dorne": "D"}
        print("   " + "".join(str(c % 10) for c in range(W)))
        for r in range(H):
            line = ""
            for c in range(W):
                cell = cells[(c, r)]
                if not cell["land"]:
                    line += "~"
                else:
                    line += letters.get(cell["region"], "?")
            print(f"{r:2d} {line}")
        n_land = sum(1 for v in cells.values() if v["land"])
        from collections import Counter
        cnt = Counter(v["region"] for v in cells.values() if v["land"])
        print(f"land={n_land} {dict(cnt)}")
        return

    with open(CANON, "r", encoding="utf-8") as f:
        data = json.load(f)

    # --- group land cells per region
    region_cells = {}
    for (c, r), v in cells.items():
        if v["land"] and v["region"]:
            region_cells.setdefault(v["region"], []).append((c, r))

    # --- place provinces: anchored ones first, then farthest-point sampling in region
    prov_by_region = {}
    for p in data["provinces"]:
        prov_by_region.setdefault(p["region"], []).append(p)

    placements = {}  # province id -> (c, r) settlement cell
    used_cells = set()
    for region, plist in prov_by_region.items():
        free = [cell for cell in region_cells.get(region, []) if cell not in used_cells]
        if not free:
            print(f"WARNING: no cells for region {region}", file=sys.stderr)
            free = [(OFF_C + 1, OFF_R + 1)]
        anchored = []
        others = []
        for p in plist:
            if p["id"] in ANCHORS:
                anchored.append(p)
            else:
                others.append(p)
        for p in anchored:
            ax, ay = ANCHORS[p["id"]]
            lx, ly = wp_to_lb(ax, ay)
            tc, tr = fx_to_grid(lx), fy_to_grid(ly)
            best, bd = None, 1e9
            for cell in free:
                d = (cell[0] - tc) ** 2 + (cell[1] - tr) ** 2
                if d < bd:
                    best, bd = cell, d
            placements[p["id"]] = best
            used_cells.add(best)
            free.remove(best)
        # farthest-point for the rest
        if free:
            if used_cells:
                seed = max(free, key=lambda cell: min(
                    (cell[0] - u[0]) ** 2 + (cell[1] - u[1]) ** 2 for u in used_cells))
            else:
                seed = free[0]
            for p in others:
                if not free:
                    placements[p["id"]] = seed
                    continue
                best = seed if seed in free else max(free, key=lambda cell: min(
                    (cell[0] - u[0]) ** 2 + (cell[1] - u[1]) ** 2 for u in used_cells))
                placements[p["id"]] = best
                used_cells.add(best)
                free.remove(best)
                seed = best

    # --- grow province territories round-robin until the whole region is claimed;
    #     higher-level provinces expand faster (bigger territories around big castles)
    prov_tiles = {}
    occupied = {}
    region_cells_sets = {k: set(v) for k, v in region_cells.items()}
    for p in data["provinces"]:
        start = placements[p["id"]]
        prov_tiles[p["id"]] = [start]
        occupied[start] = p["id"]

    def frontier_of(pid):
        out = set()
        for (c, r) in prov_tiles[pid]:
            for dc, dr in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nc = (c + dc, r + dr)
                if nc not in occupied and cells.get(nc, {}).get("land") and \
                        cells[nc]["region"] == prov_by_id_region[pid]:
                    out.add(nc)
        return out

    prov_by_id_region = {p["id"]: p["region"] for p in data["provinces"]}
    changed = True
    while changed:
        changed = False
        for p in sorted(data["provinces"], key=lambda p: (-p["level"], p["id"])):
            pid = p["id"]
            take = max(1, p["level"])
            fr = sorted(frontier_of(pid))
            for cell in fr[:take]:
                if cell not in occupied and cells.get(cell, {}).get("land") and \
                        cells[cell]["region"] == prov_by_id_region[pid]:
                    occupied[cell] = pid
                    prov_tiles[pid].append(cell)
                    changed = True

    # claim any leftover land cells by nearest province tile (region boundary quirks)
    for (c, r), v in cells.items():
        if v["land"] and (c, r) not in occupied:
            best, bd = None, 1e18
            for pid, tl in prov_tiles.items():
                for t in tl:
                    dd = (t[0] - c) ** 2 + (t[1] - r) ** 2
                    if dd < bd:
                        best, bd = pid, dd
            occupied[(c, r)] = best
            prov_tiles[best].append((c, r))

    # --- rebuild provinces list (keep metadata, update tiles/settlement/terrain/coastal)
    def tile_coastal(c, r):
        for dc, dr in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, -1), (-1, 1)):
            nc, nr = c + dc, r + dr
            if not (0 <= nc < W and 0 <= nr < H) or not cells[(nc, nr)]["land"]:
                return True
        return False

    prov_neighbors = {p["id"]: set() for p in data["provinces"]}
    for (c, r), pid in occupied.items():
        for dc, dr in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, -1), (-1, 1)):
            nc, nr = c + dc, r + dr
            if (nc, nr) in occupied and occupied[(nc, nr)] != pid:
                prov_neighbors[pid].add(occupied[(nc, nr)])

    for p in data["provinces"]:
        tiles = sorted(prov_tiles[p["id"]], key=lambda t: (t[1], t[0]))
        p["tiles"] = [[c, r] for c, r in tiles]
        p["settlement"] = [placements[p["id"]][0], placements[p["id"]][1]]
        tpool = REGION_TERRAIN[p["region"]]
        p["terrain"] = tpool[hx(p["id"], len(tpool))]
        p["coastal"] = any(tile_coastal(c, r) for c, r in tiles)
        p["neighbors"] = sorted(prov_neighbors[p["id"]])

    # --- rebuild tiles
    prov_by_id = {p["id"]: p for p in data["provinces"]}
    tiles = []
    for r in range(H):
        for c in range(W):
            cell = cells[(c, r)]
            if cell["land"] and (c, r) in occupied:
                p = prov_by_id[occupied[(c, r)]]
                tiles.append({
                    "c": c, "r": r, "sea": False, "region": p["region"],
                    "province": p["id"], "terrain": p["terrain"],
                    "river": p.get("river", False) and (c, r) == tuple(p["settlement"]),
                    "coastal": tile_coastal(c, r),
                })
            else:
                tiles.append({"c": c, "r": r, "sea": True, "region": "", "province": "",
                              "terrain": "", "river": False, "coastal": False})

    data["width"] = W
    data["height"] = H
    data["tiles"] = tiles

    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
    n_land = sum(1 for t in tiles if not t["sea"])
    print(f"wrote {args.out}: {W}x{H}, land={n_land}, provinces={len(data['provinces'])}")


if __name__ == "__main__":
    main()
