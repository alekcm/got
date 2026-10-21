#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Предпросмотр генератора карт SoQ без Unity.

Это независимая реализация того же конвейера, что в
Assets/IronThrone/Scripts/Core/Worldgen/SoqMapGenerator.cs:
  1) маска суши (блоб + фрактальный шум + срез мелких островов);
  2) регионы из layout'а (fill, группы, дочерние регионы);
  3) связи/дороги (Дейкстра, насыпь через воду);
  4) рельеф по биому;
  5) главные строения и стартовые города;
  6) blueprint: объекты, охрана;
  7) заполнение свободной земли.

Нужен только Pillow:  pip install --user pillow
Запуск:  python3 tools/soq_mapgen_preview.py [--seeds 1,2,3]
Результат: tools/soq_seed_<layout>_<seed>.png и tools/soq_mapgen_contact.png
"""
import argparse
import json
import bisect
import math
import os
import sys

try:
    from PIL import Image, ImageDraw
except ImportError:
    print("Нужен Pillow: pip install --user pillow", file=sys.stderr)
    raise

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, "Assets", "IronThrone", "Resources", "IronThroneData", "MapGen")
OUT = os.path.join(ROOT, "tools")

MASK = 0xFFFFFFFFFFFFFFFF


class Rng:
    """Тот же SplitMix64, что в C# SoqRng."""

    def __init__(self, seed):
        self.seed = seed
        self.state = ((seed & 0xFFFFFFFF) * 0x9E3779B97F4A7C15 + 0x165667B19E3779F9) & MASK
        self.next_ulong()
        self.next_ulong()

    def next_ulong(self):
        self.state = (self.state + 0x9E3779B97F4A7C15) & MASK
        z = self.state
        z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & MASK
        z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & MASK
        return z ^ (z >> 31)

    def next_double(self):
        return (self.next_ulong() >> 11) / 9007199254740992.0

    def next(self, n):
        return 0 if n <= 1 else self.next_ulong() % n

    def next_range(self, a, b):
        return a if b <= a else a + self.next(b - a)

    def range(self, a, b):
        return a + self.next_double() * (b - a)

    def chance(self, p):
        return p > 0 and self.next_double() < p

    def shuffle(self, items):
        for i in range(len(items) - 1, 0, -1):
            j = self.next(i + 1)
            items[i], items[j] = items[j], items[i]


def hash01(x, y, salt):
    h = (x * 374761393 + y * 668265263 + salt * 2246822519) & MASK
    h = ((h ^ (h >> 13)) * 1274126177) & MASK
    h ^= h >> 16
    return (h & 0xFFFFFF) / 16777215.0


class Noise:
    """Value-noise с таблицей перестановок (как SoqRng.Noise)."""

    def __init__(self, rng):
        table = list(range(256))
        for i in range(255, 0, -1):
            j = rng.next(i + 1)
            table[i], table[j] = table[j], table[i]
        self.perm = [table[i & 255] for i in range(512)]

    def value(self, x, y):
        h = self.perm[(self.perm[x & 255] + (y & 255)) & 511]
        # h = 0..255; вариант с 0xFFFF/65535 давал шум ≈ 0.002 (плоский рельеф)
        return h / 255.0

    def eval(self, x, y):
        x0, y0 = math.floor(x), math.floor(y)
        tx = x - x0
        ty = y - y0
        tx = tx * tx * (3 - 2 * tx)
        ty = ty * ty * (3 - 2 * ty)
        a = self.value(x0, y0)
        b = self.value(x0 + 1, y0)
        c = self.value(x0, y0 + 1)
        d = self.value(x0 + 1, y0 + 1)
        return (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty

    def fbm(self, x, y, octaves=3, persistence=0.5):
        total = 0.0
        amp = 1.0
        norm = 0.0
        freq = 1.0
        for _ in range(octaves):
            total += self.eval(x * freq, y * freq) * amp
            norm += amp
            amp *= persistence
            freq *= 2.03
        return total / norm if norm else 0.0


# --------------------------------------------------------------------- данные
def load_data():
    layout_files = sorted(f for f in os.listdir(DATA) if f.startswith("layout_") and f.endswith(".json"))
    layouts = [json.load(open(os.path.join(DATA, f), encoding="utf-8")) for f in layout_files]
    bp = json.load(open(os.path.join(DATA, "blueprint_default.json"), encoding="utf-8"))
    objs = json.load(open(os.path.join(DATA, "objects.json"), encoding="utf-8"))["objects"]
    return layouts, bp, {o["id"]: o for o in objs}


TERRAIN_COLORS = {
    "Temperate": [(0x6f, 0x8f, 0x4e), (0x4d, 0x6b, 0x3c), (0xa8, 0xb4, 0x5a), (0x8d, 0x7a, 0x3f), (0x5f, 0x7a, 0x45), (0x4f, 0x61, 0x52)],
    "North": [(0x8f, 0xa0, 0x7a), (0x3d, 0x5a, 0x38), (0x7d, 0x8f, 0x74), (0xe6, 0xee, 0xf2), (0x8f, 0x8f, 0x8f)],
    "Desert": [(0xd8, 0xc1, 0x84), (0xb3, 0x9a, 0x63), (0xc2, 0xa7, 0x77), (0x8a, 0x71, 0x50)],
    "Swamp": [(0x4f, 0x61, 0x52), (0x3d, 0x5a, 0x38), (0x8a, 0x71, 0x50)],
    "Highlands": [(0x7d, 0x7a, 0x72), (0x5f, 0x7a, 0x45), (0x6f, 0x8f, 0x4e)],
    "Wasteland": [(0x8a, 0x71, 0x50), (0xb3, 0x9a, 0x63), (0x7d, 0x7a, 0x72)],
}

OBJECT_COLORS = {
    "settlement": (0xff, 0xe0, 0x66),
    "resource": (0x7f, 0xd0, 0xff),
    "dwelling": (0xd0, 0x9c, 0xff),
    "treasure": (0xff, 0xc8, 0x50),
    "neutral": (0xc8, 0xf0, 0xa0),
    "objective": (0xff, 0x60, 0x8c),
    "guard": (0xff, 0x40, 0x40),
}

BIOME_BY_ID = {
    "north": "North", "desert": "Desert", "swamp": "Swamp",
    "highlands": "Highlands", "wasteland": "Wasteland", "temperate": "Temperate",
}


def biome_for(node):
    b = node.get("biome")
    if b:
        return BIOME_BY_ID.get(b, "Temperate")
    rid = (node.get("region_identifier") or ["default"])[0].lower()
    for key, name in [("north", "North"), ("snow", "North"), ("desert", "Desert"), ("sand", "Desert"),
                      ("swamp", "Swamp"), ("marsh", "Swamp"), ("high", "Highlands"),
                      ("rock", "Highlands"), ("waste", "Wasteland"), ("ash", "Wasteland")]:
        if key in rid:
            return name
    return "Temperate"


def generate(layout, sub, blueprint, catalog, seed, land_fraction=0.70):
    size = int(layout.get("size", 48))
    rng = Rng(seed)
    noise = Noise(Rng((seed * 486187739 + 7919) & 0x7FFFFFFF))
    player_count = int(layout.get("playerCount", 2))

    # ---------------------------------------------------------------- суша
    field = [0.0] * (size * size)
    blobs = rng.next_range(2, 4)
    centers = []
    for i in range(blobs):
        cx = rng.range(0.34, 0.66) + (rng.range(-0.12, 0.12) if i == 1 else 0)
        cy = rng.range(0.34, 0.66) + (rng.range(-0.12, 0.12) if i == 2 else 0)
        centers.append((cx, cy, rng.range(0.30, 0.46), rng.range(0.30, 0.46)))
    for y in range(size):
        for x in range(size):
            nx, ny = (x + 0.5) / size, (y + 0.5) / size
            continent = 0.0
            for (cx, cy, rx, ry) in centers:
                dx, dy = (nx - cx) / rx, (ny - cy) / ry
                continent = max(continent, 1.0 - (dx * dx + dy * dy))
            n = noise.fbm(x * 0.055, y * 0.055, 4, 0.52)
            edge = min(min(nx, 1 - nx), min(ny, 1 - ny))
            edge_falloff = min(1.0, edge / 0.16)
            field[y * size + x] = continent * 0.82 + n * 0.42 - (1 - edge_falloff) * 0.55

    target = int(size * size * max(0.5, min(0.9, land_fraction)))
    threshold = sorted(field)[max(0, len(field) - 1 - target)]
    land = [v >= threshold for v in field]
    for y in range(size):
        for x in range(size):
            if x == 0 or y == 0 or x == size - 1 or y == size - 1:
                land[y * size + x] = False

    for _ in range(2):
        copy = list(land)
        for y in range(1, size - 1):
            for x in range(1, size - 1):
                n = sum(1 for dy in (-1, 0, 1) for dx in (-1, 0, 1)
                        if not (dx == 0 and dy == 0) and copy[(y + dy) * size + (x + dx)])
                if n >= 5:
                    land[y * size + x] = True
                elif n <= 2:
                    land[y * size + x] = False

    min_size = max(24, int(size * size * 0.012))
    seen = [False] * len(land)
    for start in range(len(land)):
        if not land[start] or seen[start]:
            continue
        stack = [start]
        seen[start] = True
        comp = []
        while stack:
            cur = stack.pop()
            comp.append(cur)
            cx, cy = cur % size, cur // size
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = cx + dx, cy + dy
                if 0 <= nx < size and 0 <= ny < size:
                    ni = ny * size + nx
                    if land[ni] and not seen[ni]:
                        seen[ni] = True
                        stack.append(ni)
        if len(comp) < min_size:
            for i in comp:
                land[i] = False

    land_count = sum(1 for v in land if v)

    # ---------------------------------------------------------------- регионы
    region_of = [-1] * (size * size)
    regions = []
    seeds = []

    # нормировка fill на всю сушу (как SoqMapGenerator.ComputeQuotaScale):
    # Σfill обычно < 1, иначе остаток земли раздувает случайные регионы
    def _sum_fill(node):
        f = node.get("fill", 0) or 0
        total = f if f > 0.0001 else 0.0
        for ch in node.get("children", []) or []:
            total += _sum_fill(ch)
        return total

    _total_fill = sum(_sum_fill(n) for n in sub.get("nodes", []))
    quota_scale = min(4.0, max(1.0, 1.0 / _total_fill)) if _total_fill > 0.0001 else 1.0

    def quota_of(node):
        fill = node.get("fill", 0)
        if fill <= 0.0001:
            return rng.next_range(4, 11)
        return max(4, int(round(fill * land_count * quota_scale)))

    def seed_tile(point, min_dist):
        px = point.get("x") or [0.5]
        py = point.get("y") or [0.5]
        tx = rng.range(min(px), max(px))
        ty = rng.range(min(py), max(py))
        cx = max(1, min(size - 2, int(tx * size)))
        cy = max(1, min(size - 2, int(ty * size)))
        best, best_score = -1, 1 << 30
        radius = 0
        while radius < size and best < 0:
            for dy in range(-radius, radius + 1):
                for dx in range(-radius, radius + 1):
                    if abs(dx) != radius and abs(dy) != radius:
                        continue
                    x, y = cx + dx, cy + dy
                    if not (0 <= x < size and 0 <= y < size):
                        continue
                    idx = y * size + x
                    if not land[idx] or region_of[idx] >= 0:
                        continue
                    dist = min((abs(sx - x) + abs(sy - y) for sx, sy in seeds), default=10 ** 9)
                    if dist < min_dist:
                        continue
                    score = abs(dx) + abs(dy) + rng.next(3)
                    if score < best_score:
                        best_score, best = score, idx
            radius += 1
        if best >= 0:
            seeds.append((best % size, best // size))
        return best

    group_quota = {}
    for node in sub["nodes"]:
        if node.get("group", 0):
            group_quota[node["group"]] = max(group_quota.get(node["group"], 0), quota_of(node))

    def new_region(node, node_id):
        rid = node.get("region_identifier") or []
        return {
            "id": node_id,
            "node": node,
            "name": node.get("displayName") or (rid[0] if rid else "область"),
            "ident": rid[0] if rid else "default",
            "biome": biome_for(node),
            "player": node.get("player", 0),
            "group": node.get("group", 0),
            "tiles": [],
            "center": (0, 0),
            "radius": 1.0,
        }

    def claim(region, idx):
        region["tiles"].append(idx)
        region_of[idx] = region["id"]
        region["center"] = (idx % size, idx // size)

    def frontier(region):
        result = []
        for idx in region["tiles"]:
            x, y = idx % size, idx // size
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if not (0 <= nx < size and 0 <= ny < size):
                    continue
                ni = ny * size + nx
                if land[ni] and region_of[ni] < 0 and ni not in result:
                    result.append(ni)
        return result

    growths = []
    for node in sub["nodes"]:
        if node.get("spawnChance", 1.0) < 1.0 and not rng.chance(node["spawnChance"]):
            continue
        quota = group_quota.get(node.get("group", 0), quota_of(node)) if node.get("group", 0) else quota_of(node)
        region = new_region(node, len(regions))
        tile = seed_tile(node.get("point", {}), max(3, size // 8))
        if tile < 0:
            continue
        claim(region, tile)
        regions.append(region)
        growths.append({"region": region, "quota": max(quota, 1), "group": region["group"],
                        "frontier": frontier(region), "node": node})

    def grow_one(g):
        region = g["region"]
        for _ in range(64):
            if not g["frontier"]:
                return
            pick = rng.next(len(g["frontier"]))
            idx = g["frontier"].pop(pick)
            if not land[idx] or region_of[idx] >= 0:
                continue
            claim(region, idx)
            if len(region["tiles"]) >= g["quota"]:
                return
            x, y = idx % size, idx // size
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < size and 0 <= ny < size:
                    ni = ny * size + nx
                    if land[ni] and region_of[ni] < 0 and ni not in g["frontier"]:
                        g["frontier"].append(ni)
            return

    def grow_set(items):
        for _ in range(size * size * 8):
            best = 0
            candidates = []
            for g in items:
                deficit = g["quota"] - len(g["region"]["tiles"])
                if deficit <= 0 or not g["frontier"]:
                    continue
                if deficit > best:
                    best = deficit
                    candidates = [g]
                elif deficit == best:
                    candidates.append(g)
            if not candidates:
                break
            chosen = candidates[rng.next(len(candidates))]
            if chosen["group"]:
                for g in items:
                    if g["group"] == chosen["group"]:
                        grow_one(g)
            else:
                grow_one(chosen)

    grow_set(growths)

    # дочерние регионы
    pending_connects = []
    def collect_children(parent_node, parent_region, depth):
        if depth > 4:
            return
        for child in parent_node.get("children", []):
            if child.get("spawnChance", 1.0) < 1.0 and not rng.chance(child["spawnChance"]):
                continue
            quota = quota_of(child)
            if child.get("group", 0):
                quota = max(quota, max((quota_of(s) for s in parent_node.get("children", [])
                                        if s.get("group") == child.get("group")), default=quota))
            border = []
            for idx in parent_region["tiles"]:
                x, y = idx % size, idx // size
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < size and 0 <= ny < size:
                        ni = ny * size + nx
                        if land[ni] and region_of[ni] < 0:
                            border.append(ni)
            if not border:
                continue
            region = new_region(child, len(regions))
            claim(region, border[rng.next(len(border))])
            regions.append(region)
            g = {"region": region, "quota": max(quota, 1), "group": region["group"],
                 "frontier": frontier(region), "node": child}
            children.append(g)
            if child.get("road", 0) > 0:
                pending_connects.append((parent_region, region, child.get("road", 0)))
            collect_children(child, region, depth + 1)

    children = []
    for g in list(growths):
        collect_children(g["node"], g["region"], 0)
    grow_set(children)

    # остатки суши — ближайшему региону
    queue = []
    for region in regions:
        for idx in region["tiles"]:
            queue.append(idx)
    head = 0
    while head < len(queue):
        cur = queue[head]
        head += 1
        x, y = cur % size, cur // size
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if 0 <= nx < size and 0 <= ny < size:
                ni = ny * size + nx
                if land[ni] and region_of[ni] < 0:
                    region_of[ni] = region_of[cur]
                    regions[region_of[cur]]["tiles"].append(ni)
                    queue.append(ni)
    for idx in range(size * size):
        if land[idx] and region_of[idx] < 0:
            best = min(regions, key=lambda r: (r["center"][0] - idx % size) ** 2 + (r["center"][1] - idx // size) ** 2)
            region_of[idx] = best["id"]
            best["tiles"].append(idx)

    for region in regions:
        if not region["tiles"]:
            continue
        sx = sum(i % size for i in region["tiles"]) / len(region["tiles"])
        sy = sum(i // size for i in region["tiles"]) / len(region["tiles"])
        best = min(region["tiles"], key=lambda i: (i % size - sx) ** 2 + (i // size - sy) ** 2)
        region["center"] = (best % size, best // size)
        region["radius"] = max(math.hypot(i % size - sx, i // size - sy) for i in region["tiles"])

    # ---------------------------------------------------------------- дороги
    road = [False] * (size * size)

    def find_path(a, b):
        import heapq
        dist = {i: 0.0 for i in a["tiles"]}
        prev = {}
        heap = [(0.0, i) for i in a["tiles"]]
        heapq.heapify(heap)
        visited = set()
        goal = -1
        while heap:
            d, cur = heapq.heappop(heap)
            if cur in visited:
                continue
            visited.add(cur)
            if region_of[cur] == b["id"]:
                goal = cur
                break
            x, y = cur % size, cur // size
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if not (0 <= nx < size and 0 <= ny < size):
                    continue
                ni = ny * size + nx
                if ni in visited:
                    continue
                step = 14.0 if not land[ni] else 1.0
                if land[ni] and region_of[ni] not in (a["id"], b["id"]):
                    step += 2.5
                nd = d + step
                if nd < dist.get(ni, 1e18):
                    dist[ni] = nd
                    prev[ni] = cur
                    heapq.heappush(heap, (nd, ni))
        if goal < 0:
            return None
        path = []
        cur = goal
        while True:
            path.append(cur)
            if dist.get(cur, 0) <= 0:
                break
            cur = prev[cur]
        return list(reversed(path))

    def connect(a, b, road_type, guard_chance, guard_tier):
        path = find_path(a, b)
        if not path:
            return
        half = len(path) // 2
        for i, idx in enumerate(path):
            if not land[idx]:
                land[idx] = True
                region_of[idx] = -1
            if region_of[idx] < 0:
                owner = a["id"] if i <= half else b["id"]
                region_of[idx] = owner
                regions[owner]["tiles"].append(idx)
            if road_type > 0:
                road[idx] = True

    for (a, b, road_type) in pending_connects:
        connect(a, b, road_type, 0, "Random")

    for conn in sub.get("connections", []):
        by_id = {}
        def idx_nodes(nodes):
            for n in nodes:
                by_id[n["id"]] = n
                idx_nodes(n.get("children", []))
        idx_nodes(sub["nodes"])
        by_id = {}
        def collect(nodes):
            for n in nodes:
                by_id[n["id"]] = n
                collect(n.get("children", []))
        collect(sub["nodes"])
        a_node = by_id.get(conn.get("nodeFrom"))
        b_node = by_id.get(conn.get("nodeTo"))
        if not a_node or not b_node:
            continue
        a = next((r for r in regions if r["node"] is a_node), None)
        b = next((r for r in regions if r["node"] is b_node), None)
        if a and b:
            connect(a, b, conn.get("road", 1), conn.get("guardChance", 0), conn.get("guardTier", ["Random"])[0])

    # ---------------------------------------------------------------- рельеф
    # Patch-шум нормируется по рангу внутри биома: сырой Fbm живёт в узком
    # диапазоне (~0.30..0.85, медиана 0.5), поэтому «редкие» классы (скалы, горы)
    # на маленьких картах не появлялись вообще. После нормировки пороги в
    # pick_terrain — это проценты площади, одинаковые на карте любого размера.
    raw = {}
    by_biome = {}
    for y in range(size):
        for x in range(size):
            idx = y * size + x
            if not land[idx]:
                continue
            rid = region_of[idx]
            biome = regions[rid]["biome"] if rid >= 0 else "Temperate"
            patch = noise.fbm(x * 0.075 + (rid * 7.3 if rid >= 0 else 0), y * 0.075, 3, 0.55)
            raw[idx] = patch
            by_biome.setdefault(biome, []).append(patch)
    sorted_by_biome = {b: sorted(v) for b, v in by_biome.items()}

    def rank_of(biome, value):
        vals = sorted_by_biome[biome]
        if len(vals) < 2:
            return 0.5
        return bisect.bisect_left(vals, value) / float(len(vals) - 1)

    terrain = ["Grass"] * (size * size)
    for y in range(size):
        for x in range(size):
            idx = y * size + x
            if not land[idx]:
                continue
            rid = region_of[idx]
            biome = regions[rid]["biome"] if rid >= 0 else "Temperate"
            patch = rank_of(biome, raw[idx])
            near_sea = any(0 <= x + dx < size and 0 <= y + dy < size and not land[(y + dy) * size + (x + dx)]
                           for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
            coast = (1 if near_sea else 0) / 4.0 + noise.eval(x * 0.3, y * 0.3) * 0.35
            terrain[idx] = pick_terrain(biome, patch, coast, near_sea)

    # ---------------------------------------------------------------- объекты
    objects = []
    occupied = set()

    def is_occupied(x, y):
        return (x, y) in occupied

    def can_occupy(x, y, blocking):
        if not (0 <= x < size and 0 <= y < size):
            return False
        idx = y * size + x
        if not land[idx] or is_occupied(x, y):
            return False
        if blocking:
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    if dx == 0 and dy == 0:
                        continue
                    if is_occupied(x + dx, y + dy):
                        return False
        return True

    def place(region_id, defn, x, y, player):
        idx = y * size + x
        occupied.add((x, y))
        objects.append({
            "id": len(objects) + 1, "def": defn["id"], "cat": defn["category"],
            "x": x, "y": y, "region": region_of[idx] if region_of[idx] >= 0 else region_id,
            "player": player, "guarded": False,
        })

    def place_guard(x, y, tier, owner):
        tables = {"Low": "GuardBandit", "Medium": "GuardArcher", "High": "GuardLeader", "Random": "GuardWolf"}
        defn = catalog.get(tables.get(tier, "GuardBandit"))
        if not defn:
            return None
        return place(region_of[y * size + x], defn, x, y, 0)

    # стартовые города
    starts = []
    start_nodes = [n for n in sub["nodes"] if n.get("player", 0) > 0]
    if layout.get("randomizeStartingPositions", True) and len(start_nodes) > 1:
        players = list(range(1, len(start_nodes) + 1))
        rng.shuffle(players)
        for n, p in zip(start_nodes, players):
            n["player"] = p
    for node in start_nodes:
        region = next((r for r in regions if r["node"] is node), None)
        if not region:
            continue
        pid = node.get("player", 1)
        region["player"] = pid
        defn = catalog.get((node.get("primary_building") or ["AgnosticTown"])[0])
        if not defn:
            defn = catalog["AgnosticTown"]
        cx, cy = region["center"]
        if not can_occupy(cx, cy, True):
            spot = next(((i % size, i // size) for i in region["tiles"] if can_occupy(i % size, i // size, True)), None)
            if not spot:
                continue
            cx, cy = spot
        place(region["id"], defn, cx, cy, pid)
        starts.append({"player": pid, "x": cx, "y": cy, "region": region["id"]})

    # главные строения остальных регионов
    for region in regions:
        node = region["node"]
        if node.get("player", 0) > 0 or not node.get("primary_building"):
            continue
        defn = catalog.get(node["primary_building"][0])
        if defn and can_occupy(*region["center"], True):
            place(region["id"], defn, region["center"][0], region["center"][1], 0)

    by_ident = {r["identifier"]: r for r in blueprint["regions"]}

    def spawn_region(region, ident):
        bp_region = by_ident.get(ident)
        if not bp_region:
            return 0
        entries = list(bp_region["entries"])
        if bp_region.get("shuffle", True):
            rng.shuffle(entries)
        placed = 0
        for _ in range(max(1, bp_region.get("iterations", 1))):
            for e in entries:
                cmin = e.get("count", [1, 1])
                if isinstance(cmin, list):
                    lo, hi = (cmin[0], cmin[-1])
                else:
                    lo = hi = cmin
                for _ in range(rng.next_range(lo, hi + 1)):
                    if not e.get("objects"):
                        continue
                    defn = catalog.get(e["objects"][rng.next(len(e["objects"]))])
                    if not defn:
                        continue
                    cand = []
                    for idx in region["tiles"]:
                        x, y = idx % size, idx // size
                        if not land[idx]:
                            continue
                        if e.get("distance", 0) > 0:
                            dx, dy = x - region["center"][0], y - region["center"][1]
                            if math.hypot(dx, dy) < e["distance"] * region["radius"]:
                                continue
                        if "NearRoad" in e.get("location", []) and not any(
                                0 <= x + dx < size and 0 <= y + dy < size and road[(y + dy) * size + (x + dx)]
                                for dx, dy in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1))):
                            continue
                        cand.append(idx)
                    rng.shuffle(cand)
                    for idx in cand[:120]:
                        x, y = idx % size, idx // size
                        if not can_occupy(x, y, defn.get("blocking", True)):
                            continue
                        obj = place(region["id"], defn, x, y, region["player"])
                        placed += 1
                        if e.get("guardChance", 0) and rng.chance(e["guardChance"]):
                            tier = (e.get("guardTier") or ["Random"])[rng.next(len(e.get("guardTier") or ["Random"]))]
                            if tier == "Random":
                                roll = rng.next_double()
                                tier = "Low" if roll < 0.5 else ("Medium" if roll < 0.85 else "High")
                            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                                if can_occupy(x + dx, y + dy, False):
                                    if place_guard(x + dx, y + dy, tier, obj):
                                        obj["guarded"] = True
                                    break
                        break
        return placed

    for region in regions:
        ident = region["ident"]
        before = len(objects)
        spawn_region(region, ident)
        if len(objects) == before:
            spawn_region(region, "default")

    # заполнение пустой земли
    budget = max(20, land_count // 12)
    fill_region = by_ident.get("fill")
    for idx in range(size * size):
        if budget <= 0:
            break
        if not land[idx] or (idx % size, idx // size) in occupied:
            continue
        if not fill_region or not fill_region["entries"]:
            continue
        if not rng.chance(0.22 if road[idx] else 0.11):
            continue
        e = fill_region["entries"][rng.next(len(fill_region["entries"]))]
        defn = catalog.get(e["objects"][rng.next(len(e["objects"]))])
        if not defn or defn["category"] == "guard":
            continue
        x, y = idx % size, idx // size
        if can_occupy(x, y, defn.get("blocking", True)):
            place(region_of[idx], defn, x, y, 0)
            budget -= 1

    return {
        "size": size, "land": land, "region_of": region_of, "terrain": terrain, "road": road,
        "regions": regions, "objects": objects, "starts": starts, "seed": seed,
        "players": player_count,
    }


def pick_terrain(biome, patch, coast, near_sea):
    if biome == "North":
        if near_sea and coast > 0.55 and patch < 0.22: return "ColdGrass"
        if patch < 0.40: return "Snow"
        if patch < 0.58: return "ForestPine"
        if patch < 0.80: return "ColdGrass"
        if patch < 0.91: return "Tundra"
        return "SnowRock"
    if biome == "Desert":
        if near_sea and coast > 0.6: return "Sand"
        return "Sand" if patch < 0.40 else ("Dry" if patch < 0.62 else ("SandRock" if patch < 0.82 else "Rock"))
    if biome == "Swamp":
        return "Marsh" if patch < 0.35 else ("ForestMix" if patch < 0.62 else ("Dirt" if patch < 0.82 else "Grass"))
    if biome == "Highlands":
        return "Rock" if patch < 0.30 else ("RockGreen" if patch < 0.52 else ("Grass" if patch < 0.70 else ("ForestPine" if patch < 0.86 else "Fields")))
    if biome == "Wasteland":
        return "Dirt" if patch < 0.30 else ("Dry" if patch < 0.52 else ("Rock" if patch < 0.70 else ("RockGreen" if patch < 0.86 else "Autumn")))
    if near_sea and coast > 0.6 and patch < 0.5: return "Sand"
    if patch < 0.34: return "Grass"
    if patch < 0.54: return "ForestMix"
    if patch < 0.64: return "Fields"
    if patch < 0.74: return "Autumn"
    if patch < 0.86: return "ForestPine"
    if patch < 0.90: return "Marsh"
    if patch < 0.95: return "RockGreen"
    return "Rock"


def render(result, path, tile_px=10):
    size = result["size"]
    img = Image.new("RGB", (size * tile_px, size * tile_px), (16, 26, 34))
    draw = ImageDraw.Draw(img)
    region_palette = [(0xfa, 0xd1, 0x6b), (0x9e, 0xd6, 0xfa), (0xfa, 0x9e, 0x94), (0xae, 0xeb, 0xa3),
                      (0xdb, 0xb2, 0xfa), (0xfa, 0xe6, 0x9e), (0x99, 0xe6, 0xdb), (0xf5, 0xa8, 0xd1),
                      (0xcc, 0xdb, 0x94), (0xb8, 0xc2, 0xfa), (0xfa, 0xbd, 0x85), (0xa8, 0xcc, 0xb8)]

    for y in range(size):
        for x in range(size):
            idx = y * size + x
            box = [x * tile_px, y * tile_px, (x + 1) * tile_px - 1, (y + 1) * tile_px - 1]
            if not result["land"][idx]:
                draw.rectangle(box, fill=(28, 52, 70))
                continue
            rid = result["region_of"][idx]
            base = (0x6f, 0x8f, 0x4e)
            if rid >= 0:
                base = region_palette[rid % len(region_palette)]
            draw.rectangle(box, fill=base)
            if result["road"][idx]:
                draw.rectangle([x * tile_px + 2, y * tile_px + 2, (x + 1) * tile_px - 3, (y + 1) * tile_px - 3],
                               fill=(150, 118, 76))

    for obj in result["objects"]:
        x, y = obj["x"], obj["y"]
        color = OBJECT_COLORS.get(obj["cat"], (255, 255, 255))
        cx, cy = x * tile_px + tile_px // 2, y * tile_px + tile_px // 2
        if obj["cat"] == "settlement":
            draw.rectangle([cx - 4, cy - 4, cx + 4, cy + 4], fill=color, outline=(40, 30, 10))
        elif obj["cat"] == "guard":
            draw.ellipse([cx - 2, cy - 2, cx + 2, cy + 2], fill=color)
        elif obj["cat"] == "objective":
            draw.polygon([(cx, cy - 4), (cx + 4, cy), (cx, cy + 4), (cx - 4, cy)], fill=color)
        else:
            draw.rectangle([cx - 2, cy - 2, cx + 2, cy + 2], fill=color)
        if obj["guarded"]:
            draw.point((cx - 3, cy - 3), fill=(255, 0, 0))

    for start in result["starts"]:
        x, y = start["x"], start["y"]
        draw.rectangle([x * tile_px - 2, y * tile_px - 2, (x + 1) * tile_px + 1, (y + 1) * tile_px + 1],
                       outline=(255, 255, 200), width=2)

    img.save(path)
    return path


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seeds", default="1,2,3")
    ap.add_argument("--tile", type=int, default=10)
    args = ap.parse_args()
    seeds = [int(s) for s in args.seeds.split(",") if s.strip()]
    layouts, bp, catalog = load_data()

    images = []
    for layout in layouts:
        for seed in seeds:
            sub = layout["layouts"][0]
            result = generate(layout, sub, bp, catalog, seed)
            name = f"soq_{layout['id']}_seed{seed}.png"
            path = os.path.join(OUT, name)
            render(result, path, args.tile)
            counts = {}
            for o in result["objects"]:
                counts[o["cat"]] = counts.get(o["cat"], 0) + 1
            print(f"{layout['id']} seed {seed}: {result['size']}x{result['size']}, "
                  f"land {sum(result['land'])}, regions {len(result['regions'])}, objects {len(result['objects'])}, starts {len(result['starts'])}, {counts}")
            images.append((f"{layout['id']} / сид {seed}", path))

    # контактный лист
    cols = 3
    cell = 420
    rows = (len(images) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * cell), (18, 24, 28))
    dr = ImageDraw.Draw(sheet)
    for i, (label, path) in enumerate(images):
        im = Image.open(path).convert("RGB")
        im.thumbnail((cell - 16, cell - 40))
        x = (i % cols) * cell + (cell - im.width) // 2
        y = (i // cols) * cell + (cell - im.height - 8)
        sheet.paste(im, (x, y))
        dr.text(((i % cols) * cell + 8, (i // cols) * cell + 6), label, fill=(240, 240, 220))
    out = os.path.join(OUT, "soq_mapgen_contact.png")
    sheet.save(out)
    print("Контактный лист:", out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
