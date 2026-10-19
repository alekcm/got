#!/usr/bin/env python3
"""Canonical Westeros hex map generator.

Produces Assets/IronThrone/Resources/IronThroneData/canonical_map.json and a PNG preview.
Grid: 48x36, pointy-top hexes, odd-r offset coordinates (c, r).
"""
import json, math, os, random
from collections import defaultdict, deque
from PIL import Image, ImageDraw, ImageFont

W, H = 48, 36
SQ3 = math.sqrt(3)

# ---------------------------------------------------------------- hex utils
def px(c, r):
    """grid-space position (not pixels)"""
    return (c + 0.5 * (r & 1), r)

def cube(c, r):
    x = c - (r - (r & 1)) // 2
    z = r
    return (x, -x - z, z)

def dist(a, b):
    a = (int(round(a[0])), int(round(a[1])))
    b = (int(round(b[0])), int(round(b[1])))
    ax, ay, az = cube(*a)
    bx, by, bz = cube(*b)
    return max(abs(ax - bx), abs(ay - by), abs(az - bz))

NEIGH_OFFSETS = [(1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1)]

def neighbors(c, r):
    x, y, z = cube(c, r)
    out = []
    for dx, dy, dz in [(1, -1, 0), (1, 0, -1), (0, 1, -1), (-1, 1, 0), (-1, 0, 1), (0, -1, 1)]:
        nx, ny, nz = x + dx, y + dy, z + dz
        nr = nz
        nc = nx + (nz - (nz & 1)) // 2
        if 0 <= nc < W and 0 <= nr < H:
            out.append((nc, nr))
    return out

def lerp(a, b, t):
    return a + (b - a) * t

def clamp(x, a, b):
    return max(a, min(b, x))

def ellipse(x, y, cx, cy, rx, ry, rot=0.0):
    dx, dy = x - cx, y - cy
    if rot:
        ca, sa = math.cos(rot), math.sin(rot)
        dx, dy = dx * ca + dy * sa, -dx * sa + dy * ca
    return (dx / rx) ** 2 + (dy / ry) ** 2

def capsule(x, y, x1, y1, x2, y2, r):
    vx, vy = x2 - x1, y2 - y1
    wx, wy = x - x1, y - y1
    L = vx * vx + vy * vy
    t = 0 if L == 0 else clamp((wx * vx + wy * vy) / L, 0, 1)
    dx, dy = x - (x1 + t * vx), y - (y1 + t * vy)
    return dx * dx + dy * dy <= r * r

# ---------------------------------------------------------------- noise
def _hash(x, y, seed=7):
    n = x * 374761393 + y * 668265263 + seed * 1442695040888963407
    n = (n ^ (n >> 13)) * 1274126177
    return ((n ^ (n >> 16)) & 0xFFFFFFFF) / 0xFFFFFFFF

def vnoise(x, y, seed=7):
    xi, yi = math.floor(x), math.floor(y)
    xf, yf = x - xi, y - yi
    u, v = xf * xf * (3 - 2 * xf), yf * yf * (3 - 2 * yf)
    a = _hash(xi, yi, seed); b = _hash(xi + 1, yi, seed)
    c = _hash(xi, yi + 1, seed); d = _hash(xi + 1, yi + 1, seed)
    return lerp(lerp(a, b, u), lerp(c, d, u), v)

def fbm(x, y, seed=7, octaves=3):
    val, amp, freq = 0.0, 0.5, 1.0
    for i in range(octaves):
        val += amp * vnoise(x * freq, y * freq, seed + i * 13)
        freq *= 2.03
        amp *= 0.5
    return val

# ---------------------------------------------------------------- land shape
MAINLAND = [
    # the North
    (24, 4.6, 9.4, 3.9, 0),
    (23.5, 8.0, 10.6, 4.3, 0.05),
    (19.5, 6.2, 4.2, 3.4, 0.3),
    (29.5, 6.6, 4.6, 3.8, -0.25),
    # the Neck
    (24.2, 11.4, 3.4, 1.25, 0),
    # riverlands
    (23.0, 15.2, 6.4, 3.4, -0.08),
    (20.0, 14.0, 3.2, 2.2, 0),
    # vale
    (33.2, 12.6, 5.0, 3.8, -0.35),
    (30.0, 13.2, 2.8, 2.4, 0),
    # crownlands
    (31.0, 17.8, 4.4, 2.9, 0.15),
    # westerlands
    (15.6, 19.8, 4.2, 4.4, 0.12),
    (18.3, 22.0, 2.4, 2.4, 0),
    # reach
    (20.8, 24.4, 7.3, 5.2, -0.08),
    (25.5, 27.6, 4.0, 3.0, 0),
    (16.8, 26.4, 2.8, 2.6, 0),
    # stormlands
    (31.8, 22.2, 5.4, 4.0, 0.1),
    (34.6, 24.6, 3.4, 2.4, 0),
    # dorne
    (29.5, 28.8, 9.4, 3.2, -0.05),
    (22.8, 28.0, 4.4, 3.2, 0),
    (36.0, 30.0, 3.2, 2.2, 0),
]
BRIDGES = [
    (22.8, 12.6, 25.4, 11.2, 1.15),   # neck
    (27.6, 12.6, 31.0, 12.8, 1.35),   # vale-riverlands
    (25.0, 16.4, 28.4, 16.2, 1.25),   # crownlands-riverlands
    (28.6, 19.6, 28.2, 22.0, 1.25),   # crownlands-stormlands
    (24.2, 20.0, 27.8, 21.2, 1.5),    # reach-stormlands
    (22.6, 26.2, 26.4, 27.2, 1.4),    # reach-dorne
]
# explicit island shapes (list of tiles) - carved out of the sea after mainland generation
EXPLICIT_ISLANDS = {
    "great_wyk": [(2, 15), (3, 15), (2, 16), (3, 16)],
    "pyke": [(5, 13), (6, 13), (5, 14), (6, 14)],
    "harlaw": [(8, 12), (9, 12), (8, 13), (9, 13)],
    "blacktyde": [(9, 16), (10, 16), (9, 17)],
    "old_wyk": [(6, 17), (7, 17), (6, 18)],
    "saltcliffe": [(2, 18), (3, 18)],
    "bear_island": [(11, 1), (12, 1), (11, 2), (12, 2), (13, 2)],
    "dragonstone": [(38, 18), (39, 18), (38, 19), (39, 19)],
    "driftmark": [(36, 21), (37, 21), (36, 22)],
    "tarth": [(40, 20), (41, 20), (40, 21), (41, 21)],
    "estermont": [(43, 26), (44, 26), (43, 27)],
    "arbor": [(6, 29), (7, 29), (6, 30), (7, 30), (8, 30)],
}

def land_at(x, y):
    v = 1e9
    for cx, cy, rx, ry, rot in MAINLAND:
        v = min(v, ellipse(x, y, cx, cy, rx, ry, rot))
    for x1, y1, x2, y2, r in BRIDGES:
        if capsule(x, y, x1, y1, x2, y2, r):
            v = 0
    n = fbm(x * 0.33, y * 0.33, 11, 3) - 0.5
    return v < 1.0 + n * 0.55

LAND = [[False] * W for _ in range(H)]
for r in range(H):
    for c in range(W):
        x, y = px(c, r)
        LAND[r][c] = land_at(x, y)

# carve explicit islands: set land, clear the ring around them
for _iid, _tiles in EXPLICIT_ISLANDS.items():
    _tset = set(_tiles)
    for (c, r) in _tiles:
        if 0 <= c < W and 0 <= r < H:
            LAND[r][c] = True
    for (c, r) in _tiles:
        for n in neighbors(c, r):
            if n not in _tset:
                LAND[n[1]][n[0]] = False

def comps_of(grid, allowed=None):
    seen = set(); comps = []
    for r in range(H):
        for c in range(W):
            if not grid[r][c] or (c, r) in seen:
                continue
            stack = [(c, r)]; seen.add((c, r)); comp = []
            while stack:
                q = stack.pop(); comp.append(q)
                for n in neighbors(*q):
                    if grid[n[1]][n[0]] and n not in seen:
                        seen.add(n); stack.append(n)
            comps.append(comp)
    return comps

land_comps = comps_of(LAND)
main_comp = max(land_comps, key=len)
island_comps = [c for c in land_comps if c is not main_comp]
# drop tiny stray pixels (<2 tiles) into sea
for comp in list(land_comps):
    if len(comp) < 2:
        for c, r in comp:
            LAND[r][c] = False
land_comps = comps_of(LAND)
main_comp = max(land_comps, key=len)
COMP_INDEX = {}
for ci, comp in enumerate(land_comps):
    for t in comp:
        COMP_INDEX[t] = ci

# ---------------------------------------------------------------- seeds
# id, name, c, r, size level, house, kind  (kind: castle / town / rural)
SEEDS_RAW = {
"north": [
    ("winterfell", "Винтерфелл", 24, 8, 4, "stark", "castle"),
    ("white_harbor", "Белая Гавань", 29.5, 9, 3, "manderly", "town"),
    ("dreadfort", "Дредфорт", 28, 5, 3, "bolton", "castle"),
    ("karhold", "Кархолд", 30, 3.5, 2, "karstark", "rural"),
    ("last_hearth", "Последний Очаг", 25, 3.0, 2, "umber", "rural"),
    ("deepwood", "Дремучий лес", 18, 6, 2, "glover", "castle"),
    ("torrhen", "Торрхенов удел", 20.5, 8.2, 2, "stark", "rural"),
    ("moat_cailin", "Ров Кейлин", 22, 11.5, 2, "stark", "rural"),
    ("bear_island", "Медвежий остров", 12, 2, 2, "mormont", "rural"),
    ("barrowton", "Барроутон", 20, 9.5, 2, "dustin", "rural"),
    ("hornwood", "Хорнвуд", 27, 7.5, 2, "hornwood", "rural"),
    ("widows_watch", "Вдовий Дозор", 22, 6.0, 2, "stark", "rural"),
    ("oldcastle", "Старый Замок", 28, 9.5, 2, "stark", "rural"),
    ("seadragon", "Морской Дракон", 18.3, 3.8, 2, "stark", "rural"),
    ("ramsgate", "Рамсгейт", 30.5, 7.5, 2, "stark", "rural"),
],
"wall": [
    ("castle_black", "Чёрный Замок", 24, 1.0, 3, "nightswatch", "castle"),
    ("eastwatch", "Восточный Дозор", 30, 1.0, 2, "nightswatch", "castle"),
    ("shadow_tower", "Сумеречная башня", 17.5, 1.0, 2, "nightswatch", "rural"),
    ("nightfort", "Ночной Форт", 21, 1.0, 2, "nightswatch", "rural"),
],
"islands": [
    ("pyke", "Пайк", 5.5, 13.5, 3, "greyjoy", "castle"),
    ("great_wyk", "Большой Вик", 2.5, 15.5, 2, "goodbrother", "rural"),
    ("old_wyk", "Старый Вик", 6.4, 17.2, 2, "wyk", "rural"),
    ("harlaw", "Харлоу", 8.5, 12.4, 2, "harlaw", "castle"),
    ("blacktyde", "Чёрный Прилив", 9.4, 16.4, 2, "blacktyde", "rural"),
    ("saltcliffe", "Солтклифф", 2.4, 18.0, 2, "saltcliffe", "rural"),
],
"riverlands": [
    ("riverrun", "Риверран", 19.5, 16.6, 3, "tully", "castle"),
    ("harrenhal", "Харренхол", 26.2, 16.0, 3, "strong", "castle"),
    ("twins", "Близнецы", 23.0, 14.2, 2, "frey", "castle"),
    ("seagard", "Сигард", 16.9, 15.2, 2, "mallister", "town"),
    ("fairmarket", "Ярмарка", 21.2, 15.2, 2, "tully", "rural"),
    ("maidenpool", "Девичий Пруд", 28.0, 15.4, 2, "mooton", "rural"),
    ("pinkmaiden", "Розовый Девичник", 17.6, 18.0, 2, "piper", "rural"),
    ("raventree", "Чёрное Древо", 21.4, 17.4, 2, "blackwood", "rural"),
    ("stone_hedge", "Каменная Изгородь", 23.6, 18.0, 2, "bracken", "rural"),
    ("darry", "Дарри", 25.5, 14.8, 2, "darry", "rural"),
    ("saltpans", "Соляные Ямы", 27.2, 16.8, 2, "roote", "rural"),
],
"vale": [
    ("eyrie", "Орлиное Гнездо", 33.8, 11.6, 4, "arryn", "castle"),
    ("gulltown", "Чаячий город", 37.4, 13.4, 3, "grafton", "town"),
    ("runestone", "Рунный камень", 36.4, 10.4, 2, "royce", "castle"),
    ("ironoaks", "Железные Дубы", 32.6, 13.6, 2, "waynwood", "rural"),
    ("redfort", "Красный замок", 34.2, 15.0, 2, "redfort", "rural"),
    ("strongsong", "Крепкая Песня", 30.8, 10.8, 2, "belmore", "rural"),
    ("hearths_home", "Домашний Очаг", 31.6, 12.8, 2, "corbray", "rural"),
    ("wickenden", "Викенден", 38.6, 12.0, 2, "waxley", "rural"),
    ("longbow_hall", "Зал Длинного Лука", 33.6, 14.8, 2, "hunter", "rural"),
    ("old_anchor", "Старый Якорь", 36.6, 14.8, 2, "arwyn", "rural"),
    ("sisterton", "Сестрин", 39.4, 9.0, 2, "sunderland", "rural"),
],
"westerlands": [
    ("casterly", "Утёс Кастерли", 15.6, 19.4, 4, "lannister", "castle"),
    ("lannisport", "Ланниспорт", 14.2, 18.6, 3, "lannister", "town"),
    ("golden_tooth", "Золотой Зуб", 19.6, 20.4, 2, "lefford", "castle"),
    ("ashemark", "Пепельный замок", 17.6, 21.6, 2, "marbrand", "rural"),
    ("crakehall", "Крейкхолл", 14.2, 22.4, 2, "crakehall", "rural"),
    ("banefort", "Погибель", 12.4, 18.4, 2, "banefort", "rural"),
    ("silverhill", "Серебряный холм", 19.2, 18.6, 2, "serrett", "rural"),
    ("fair_isle", "Прекрасный Остров", 11.2, 20.2, 2, "farman", "rural"),
    ("cornfield", "Кукурузный Дол", 13.8, 23.4, 2, "swyft", "rural"),
    ("deep_den", "Глубокая Берлога", 17.4, 19.6, 2, "deep_den", "rural"),
    ("hornvale", "Хорнвейл", 21.4, 18.4, 2, "brax", "rural"),
],
"reach": [
    ("highgarden", "Хайгарден", 19.6, 23.6, 4, "tyrell", "castle"),
    ("oldtown", "Старомест", 13.2, 26.6, 4, "hightower", "town"),
    ("horn_hill", "Рогов Холм", 23.6, 26.6, 2, "tarly", "town"),
    ("arbor", "Арбор", 7.0, 29.6, 3, "redwyne", "town"),
    ("brightwater", "Светлый Ручей", 24.4, 21.8, 2, "florent", "castle"),
    ("bitterbridge", "Горький Мост", 25.4, 20.4, 2, "cobb", "rural"),
    ("tumbleton", "Тамблтон", 27.0, 19.6, 2, "footly", "rural"),
    ("ashford", "Эшфорд", 24.0, 25.6, 2, "ashford", "rural"),
    ("three_towers", "Три Башни", 15.0, 25.4, 2, "oakheart", "rural"),
    ("blackcrown", "Чёрная Корона", 11.2, 24.4, 2, "bulwer", "rural"),
    ("sunhouse", "Солнечный Дом", 16.8, 27.2, 2, "cuy", "rural"),
    ("longtable", "Длинный Стол", 19.6, 20.6, 2, "merryweather", "rural"),
    ("honeyholt", "Медовый Холт", 16.8, 24.0, 2, "beesbury", "rural"),
    ("goldengrove", "Золотая Роща", 17.8, 21.8, 2, "rowan", "rural"),
    ("bands", "Банды", 21.4, 26.6, 2, "ball", "rural"),
    ("red_lake", "Красное Озеро", 22.4, 22.6, 2, "crane", "rural"),
],
"stormlands": [
    ("storms_end", "Штормовой Предел", 32.6, 20.6, 4, "baratheon", "castle"),
    ("griffins_roost", "Гнездо Грифона", 34.6, 18.8, 2, "connington", "castle"),
    ("crows_nest", "Воронье Гнездо", 31.0, 23.4, 2, "morrigen", "rural"),
    ("nightsong", "Ночная Песнь", 27.6, 24.8, 2, "caron", "rural"),
    ("blackhaven", "Чёрный Приют", 29.6, 26.0, 2, "dondarrion", "rural"),
    ("tarth", "Тарт", 40.4, 20.5, 2, "tarth", "castle"),
    ("weeping_town", "Плачущий город", 35.6, 22.4, 2, "swann", "rural"),
    ("stonehelm", "Каменный Шлем", 33.6, 24.4, 2, "swann", "rural"),
    ("grandview", "Грандвью", 32.6, 22.4, 2, "grandison", "rural"),
    ("felwood", "Фелвуд", 34.6, 21.4, 2, "fell", "rural"),
    ("estermont", "Эстермонт", 43.4, 26.2, 2, "estermont", "rural"),
],
"dorne": [
    ("sunspear", "Солнечное Копьё", 34.6, 29.4, 4, "martell", "castle"),
    ("yronwood", "Айронвуд", 29.0, 27.0, 3, "yronwood", "castle"),
    ("starfall", "Звездопад", 22.4, 28.4, 3, "dayne", "castle"),
    ("skyreach", "Небесный Предел", 28.0, 28.4, 2, "fowler", "rural"),
    ("hellholt", "Адский Холт", 30.6, 28.4, 2, "uller", "rural"),
    ("godsgrace", "Божья Милость", 26.6, 29.4, 2, "allyrion", "rural"),
    ("planky", "Планка-Таун", 34.4, 30.2, 2, "martell", "rural"),
    ("vaith", "Вейт", 29.6, 30.0, 2, "vaith", "rural"),
    ("lemonwood", "Лимонный Лес", 33.0, 27.4, 2, "dalt", "rural"),
    ("sandstone", "Песчаник", 25.6, 27.6, 2, "qorgyle", "rural"),
    ("saltshore", "Соляной Берег", 38.0, 31.0, 2, "gargalen", "rural"),
    ("ghost_hill", "Призрачный Холм", 27.0, 26.6, 2, "toland", "rural"),
],
"crownlands": [
    ("kings_landing", "Королевская Гавань", 30.8, 19.4, 5, "baratheon", "castle"),
    ("dragonstone", "Драконий Камень", 38.5, 18.5, 3, "targaryen", "castle"),
    ("duskendale", "Сосновый Бор", 33.4, 17.6, 2, "darklyn", "town"),
    ("rosby", "Росби", 30.2, 18.2, 2, "rosby", "rural"),
    ("driftmark", "Дрифтмарк", 36.4, 21.3, 2, "velaryon", "rural"),
    ("stokeworth", "Стокворт", 29.2, 19.8, 2, "stokeworth", "rural"),
    ("sharp_point", "Острый Мыс", 38.0, 21.0, 2, "baratheon", "rural"),
    ("hayford", "Хейфорд", 28.8, 17.4, 2, "hayford", "rural"),
    ("rooks_rest", "Гнездо Воронов", 34.6, 16.6, 2, "staunton", "rural"),
    ("sweetport", "Сладкий Порт", 36.4, 20.6, 2, "sunglass", "town"),
    ("crackclaw", "Клешня", 34.6, 19.2, 2, "crabb", "rural"),
    ("antlers", "Рога", 32.0, 16.8, 2, "buckwell", "rural"),
],
}

REGIONS = [
    # id, name, anchor c, r, color
    ("north", "Север", 23.5, 7.0, "#8aa9bd"),
    ("wall", "Стена", 24, 1.0, "#e8f1f7"),
    ("islands", "Железные острова", 8.8, 15.3, "#7b8794"),
    ("riverlands", "Речные земли", 22.4, 16.0, "#7fb069"),
    ("vale", "Долина", 34.0, 12.6, "#b7a6d6"),
    ("westerlands", "Западные земли", 16.0, 20.2, "#d9b36c"),
    ("reach", "Простор", 20.8, 24.4, "#8fc97e"),
    ("stormlands", "Штормовые земли", 32.4, 22.0, "#c9a35c"),
    ("crownlands", "Королевские земли", 30.8, 18.8, "#d98a8a"),
    ("dorne", "Дорн", 30.0, 28.8, "#e0b96e"),
]
REG_NAME = {r[0]: r[1] for r in REGIONS}
REG_COLOR = {r[0]: r[4] for r in REGIONS}
ANCHORS = {r[0]: (r[2], r[3]) for r in REGIONS}

SEEDS = []
for region, items in SEEDS_RAW.items():
    for sid, name, sc, sr, lvl, house, kind in items:
        SEEDS.append(dict(id=sid, name=name, region=region, c=sc, r=sr, level=lvl,
                          house=house, kind=kind))

def snap(c, r):
    ci, cj = int(round(c)), int(round(r))
    if 0 <= ci < W and 0 <= cj < H and LAND[cj][ci]:
        return ci, cj
    best = None
    for rad in range(1, 8):
        cands = []
        for rr in range(max(0, cj - rad), min(H, cj + rad + 1)):
            for cc in range(max(0, ci - rad), min(W, ci + rad + 1)):
                if LAND[rr][cc] and dist((ci, cj), (cc, rr)) <= rad:
                    cands.append((dist((ci, cj), (cc, rr)), cc, rr))
        if cands:
            return min(cands)[1], min(cands)[2]
    return None

seed_tiles = {}
for s in SEEDS:
    p = snap(s["c"], s["r"])
    if p is None:
        raise SystemExit("cannot place seed " + s["id"])
    s["tile"] = p
    s["comp"] = COMP_INDEX[p]
    seed_tiles[s["id"]] = p

CAPITALS = {
    "north": "winterfell", "wall": "castle_black", "islands": "pyke",
    "riverlands": "riverrun", "vale": "eyrie", "westerlands": "casterly",
    "reach": "highgarden", "stormlands": "storms_end", "crownlands": "kings_landing",
    "dorne": "sunspear",
}

# ---------------------------------------------------------------- province growth
PROV = [[None] * W for _ in range(H)]
random.seed(99)
for s_ in SEEDS:
    c, r = s_["tile"]
    PROV[r][c] = s_["id"]

# target province size: capitals and towns get more land
def target_size(s_):
    base = {1: 4, 2: 5, 3: 7, 4: 9, 5: 11}.get(s_["level"], 5)
    if s_["kind"] == "rural":
        base = max(3, base - 2)
    if CAPITALS.get(s_["region"]) == s_["id"]:
        base += 3
    return base

sizes = {s_["id"]: 1 for s_ in SEEDS}
comp_of = {s_["id"]: s_["comp"] for s_ in SEEDS}
tiles_of = {s_["id"]: [s_["tile"]] for s_ in SEEDS}
TARGET = {s_["id"]: target_size(s_) for s_ in SEEDS}

# balance targets per land component so that sum(targets) covers the land
comp_land = defaultdict(int)
comp_seeds = defaultdict(list)
for r in range(H):
    for c in range(W):
        if LAND[r][c]:
            comp_land[COMP_INDEX[(c, r)]] += 1
for s_ in SEEDS:
    comp_seeds[s_["comp"]].append(s_["id"])
for comp, ids in comp_seeds.items():
    land_n = comp_land[comp]
    weights = {pid: TARGET[pid] for pid in ids}
    total_w = sum(weights.values())
    scale = land_n / max(1, total_w)
    for pid in ids:
        TARGET[pid] = max(2, int(round(weights[pid] * scale)))

# priority: capitals, then castles/towns, then rural
order = sorted(SEEDS, key=lambda s_: (
    0 if CAPITALS.get(s_["region"]) == s_["id"] else 1,
    0 if s_["kind"] != "rural" else 1,
    -s_["level"], s_["id"]))

def frontier(pid):
    out = []
    for (c, r) in tiles_of[pid]:
        for n in neighbors(c, r):
            if PROV[n[1]][n[0]] is None and LAND[n[1]][n[0]] and COMP_INDEX.get(n) == comp_of[pid]:
                out.append(n)
    return out

for _round in range(24):
    progress = False
    for s_ in order:
        pid = s_["id"]
        if sizes[pid] >= TARGET[pid]:
            continue
        fr = frontier(pid)
        if not fr:
            continue
        cen_c = sum(t[0] for t in tiles_of[pid]) / len(tiles_of[pid])
        cen_r = sum(t[1] for t in tiles_of[pid]) / len(tiles_of[pid])
        best = min(fr, key=lambda t: (dist(t, (cen_c, cen_r)), random.random() * 0.4))
        PROV[best[1]][best[0]] = pid
        tiles_of[pid].append(best)
        sizes[pid] += 1
        progress = True
    if not progress:
        break

# provinces that hit their target can still absorb up to 1.3*target while leftovers exist
for _round in range(10):
    progress = False
    left_n = sum(1 for r in range(H) for c in range(W) if LAND[r][c] and PROV[r][c] is None)
    if left_n == 0:
        break
    for s_ in order:
        pid = s_["id"]
        if sizes[pid] >= int(TARGET[pid] * 1.3) + 1:
            continue
        fr = frontier(pid)
        if not fr:
            continue
        cen_c = sum(t[0] for t in tiles_of[pid]) / len(tiles_of[pid])
        cen_r = sum(t[1] for t in tiles_of[pid]) / len(tiles_of[pid])
        best = min(fr, key=lambda t: (dist(t, (cen_c, cen_r)), random.random() * 0.4))
        PROV[best[1]][best[0]] = pid
        tiles_of[pid].append(best)
        sizes[pid] += 1
        progress = True
    if not progress:
        break

# leftover land goes to the nearest province inside the same land component
left = [(c, r) for r in range(H) for c in range(W) if LAND[r][c] and PROV[r][c] is None]
for c, r in left:
    best, bd = None, 1e9
    for s_ in SEEDS:
        if s_["comp"] != COMP_INDEX.get((c, r)):
            continue
        d = dist((c, r), (s_["tile"][0], s_["tile"][1]))
        if d < bd:
            bd, best = d, s_["id"]
    if best:
        PROV[r][c] = best
    else:
        LAND[r][c] = False  # disconnected stray tile

# drop empty seeds, drop unreachable/too-small provinces by merging
tile_count = defaultdict(int)
for r in range(H):
    for c in range(W):
        if PROV[r][c]:
            tile_count[PROV[r][c]] += 1
for _ in range(10):
    small = [s["id"] for s in SEEDS if tile_count.get(s["id"], 0) < 2]
    if not small:
        break
    for pid in small:
        # merge into adjacent biggest province
        adj = defaultdict(int)
        for r in range(H):
            for c in range(W):
                if PROV[r][c] == pid:
                    for nc, nr in neighbors(c, r):
                        if LAND[nr][nc] and PROV[nr][nc] and PROV[nr][nc] != pid:
                            adj[PROV[nr][nc]] += 1
        if not adj:
            continue
        tgt = max(adj, key=adj.get)
        for r in range(H):
            for c in range(W):
                if PROV[r][c] == pid:
                    PROV[r][c] = tgt
    tile_count = defaultdict(int)
    for r in range(H):
        for c in range(W):
            if PROV[r][c]:
                tile_count[PROV[r][c]] += 1
# remove empty seeds
SEEDS = [s for s in SEEDS if tile_count.get(s["id"], 0) > 0]
SEEDS_BY_ID = {s["id"]: s for s in SEEDS}

# ---------------------------------------------------------------- terrain
def terrain_for(region, c, r, pid):
    x, y = px(c, r)
    n = fbm(x * 0.62, y * 0.62, 101, 3)
    if region == "wall":
        return "snow"
    if region == "north":
        if r <= 2:
            return "snow"
        if n < 0.38:
            return "forest"
        if n > 0.68:
            return "mountains"
        if n > 0.56:
            return "hills"
        return "plains"
    if region == "islands":
        return "hills" if n > 0.42 else "plains"
    if region == "riverlands":
        if n < 0.34:
            return "marsh"
        if n < 0.52:
            return "forest"
        if n > 0.76:
            return "hills"
        return "plains"
    if region == "vale":
        if n > 0.58:
            return "mountains"
        if n > 0.43:
            return "hills"
        if n < 0.3:
            return "forest"
        return "plains"
    if region == "westerlands":
        if n > 0.67:
            return "mountains"
        if n > 0.45:
            return "hills"
        if n < 0.3:
            return "forest"
        return "plains"
    if region == "reach":
        if n < 0.24:
            return "forest"
        if n > 0.78:
            return "hills"
        return "plains"
    if region == "stormlands":
        if n < 0.32:
            return "forest"
        if n > 0.68:
            return "hills"
        return "plains"
    if region == "crownlands":
        if n < 0.3:
            return "forest"
        if n > 0.8:
            return "hills"
        return "plains"
    if region == "dorne":
        if n > 0.76:
            return "mountains"
        if n > 0.56:
            return "hills"
        return "desert"
    return "plains"

def tile_region(c, r):
    pid = PROV[r][c]
    return SEEDS_BY_ID[pid]["region"] if pid else None

def is_coastal(c, r):
    return any(not LAND[nr][nc] for nc, nr in neighbors(c, r))

PROVINCES = []
for s in SEEDS:
    tiles = [(c, r) for r in range(H) for c in range(W) if PROV[r][c] == s["id"]]
    terrain_counts = defaultdict(int)
    for c, r in tiles:
        terrain_counts[terrain_for(s["region"], c, r, s["id"])] += 1
    dom_terrain = max(terrain_counts, key=terrain_counts.get)
    river = False
    for c, r in tiles:
        if s["region"] in ("riverlands", "reach") and fbm(c * 0.5, r * 0.5, 77, 2) > 0.63:
            river = True
    PROVINCES.append(dict(
        id=s["id"], name=s["name"], region=s["region"], house=s["house"], kind=s["kind"],
        level=s["level"], tiles=[[c, r] for c, r in tiles],
        settlement=list(s["tile"]), terrain=dom_terrain, river=river,
        coastal=any(is_coastal(c, r) for c, r in tiles),
        is_capital=False,
        resource=("farm" if dom_terrain in ("plains", "marsh") else
                  "mine" if dom_terrain in ("hills", "mountains") else
                  "lumber" if dom_terrain == "forest" else
                  "fishery" if any(is_coastal(c, r) for c, r in tiles) else "farm"),
    ))

# capitals (region seats)
for p in PROVINCES:
    if CAPITALS.get(p["region"]) == p["id"]:
        p["is_capital"] = True

# ---------------------------------------------------------------- ports & sea routes
PORT_IDS = {
    "white_harbor", "seagard", "gulltown", "old_anchor", "wickenden", "sisterton",
    "lannisport", "fair_isle", "banefort", "oldtown", "arbor", "highgarden",
    "kings_landing", "dragonstone", "driftmark", "duskendale", "sweetport", "sharp_point",
    "tarth", "weeping_town", "stonehelm", "estermont", "sunspear", "saltshore", "planky",
    "pyke", "great_wyk", "old_wyk", "harlaw", "blacktyde", "saltcliffe",
    "bear_island", "eastwatch", "shadow_tower", "crakehall", "three_towers", "goldengrove",
    "riverrun", "horn_hill", "bitterbridge", "red_lake",
}
for p in PROVINCES:
    p["port"] = p["id"] in PORT_IDS and p["coastal"]

def center(p):
    ts = p["tiles"]
    return (sum(t[0] for t in ts) / len(ts), sum(t[1] for t in ts) / len(ts))

PORTS = [p for p in PROVINCES if p["port"]]
routes = []
for i, a in enumerate(PORTS):
    for b in PORTS[i + 1:]:
        ca, cb = center(a), center(b)
        d = math.hypot(ca[0] - cb[0], ca[1] - cb[1])
        if d <= 6.4:
            if a["region"] != b["region"] or a["region"] == "islands":
                routes.append([a["id"], b["id"]])
for pair in [("pyke", "seagard"), ("pyke", "banefort"), ("harlaw", "seagard"),
             ("blacktyde", "banefort"), ("old_wyk", "fair_isle"),
             ("bear_island", "deepwood"), ("bear_island", "oldcastle"),
             ("dragonstone", "kings_landing"), ("dragonstone", "duskendale"),
             ("driftmark", "kings_landing"), ("driftmark", "duskendale"),
             ("dragonstone", "gulltown"), ("tarth", "storms_end"),
             ("tarth", "weeping_town"), ("estermont", "tarth"),
             ("arbor", "oldtown"), ("arbor", "three_towers"),
             ("eastwatch", "white_harbor"), ("shadow_tower", "white_harbor"),
             ("sisterton", "gulltown"), ("sisterton", "white_harbor"),
             ("sweetport", "dragonstone"), ("sharp_point", "dragonstone")]:
    if [pair[0], pair[1]] not in routes and [pair[1], pair[0]] not in routes:
        routes.append([pair[0], pair[1]])

# ---------------------------------------------------------------- initial domains
PROV_BY_ID = {p["id"]: p for p in PROVINCES}
PROV_OF_TILE = {tuple(t): p["id"] for p in PROVINCES for t in p["tiles"]}

def prov_neighbors(pid):
    out = set()
    for t in PROV_BY_ID[pid]["tiles"]:
        for n in neighbors(*t):
            q = PROV_OF_TILE.get(n)
            if q and q != pid:
                out.add(q)
    return sorted(out)

for p in PROVINCES:
    p["neighbors"] = prov_neighbors(p["id"])

def house_of(pid):
    return PROV_BY_ID[pid]["house"]

DOMAIN = {}
# each settlement starts with up to 3 neighbouring rural provinces of the same house/faction
for p in PROVINCES:
    if p["kind"] == "rural":
        continue
    taken = 0
    for q in p["neighbors"]:
        qp = PROV_BY_ID[q]
        if qp["kind"] == "rural" and q not in DOMAIN and qp["house"] == p["house"] and taken < 3:
            DOMAIN[q] = p["id"]
            taken += 1
for p in PROVINCES:
    if p["kind"] != "rural":
        DOMAIN[p["id"]] = p["id"]
for p in PROVINCES:
    p["domain"] = DOMAIN.get(p["id"])

# ---------------------------------------------------------------- houses catalogue
MAJOR_HOUSES = {
    "stark": ("Старки", "Зима близко", "#8aa9bd"),
    "lannister": ("Ланнистеры", "Услышь мой рёв!", "#c9a227"),
    "baratheon": ("Баратеоны", "Нам ярость!", "#d0b04c"),
    "tyrell": ("Тиреллы", "Растём и крепнем", "#4f9d4f"),
    "martell": ("Мартеллы", "Непокорённые, несломленные, непреклонные", "#d97a2b"),
    "greyjoy": ("Грейджои", "Мы не сеем", "#4c6478"),
    "tully": ("Талли", "Семья, долг, честь", "#4f7fa8"),
    "arryn": ("Аррены", "Высоки как честь", "#8fa6d6"),
    "targaryen": ("Таргариены", "Пламя и кровь", "#a33a3a"),
    "gardener": ("Гарднеры", "Растём и крепнем", "#4f9d4f"),
    "durrandon": ("Дюрандоны", "Нам ярость!", "#d0b04c"),
    "hoare": ("Хоары", "Мы не сеем", "#4c6478"),
    "nightswatch": ("Ночной Дозор", "И теперь наша стража начинается", "#5a6b78"),
}
HOUSES = []
seen = set()
for s in SEEDS:
    if s["house"] in seen:
        continue
    seen.add(s["house"])
    name, words, color = MAJOR_HOUSES.get(
        s["house"], (f"Дом {s['name']}", "Верность и честь", "#9a9a9a"))
    HOUSES.append(dict(id=s["house"], name=name, words=words, color=color,
                       major=s["house"] in MAJOR_HOUSES, region=s["region"],
                       seat=s["id"]))

# ---------------------------------------------------------------- tiles output
tiles_out = []
for r in range(H):
    for c in range(W):
        if not LAND[r][c]:
            tiles_out.append(dict(c=c, r=r, sea=True))
            continue
        pid = PROV[r][c]
        region = PROV_BY_ID[pid]["region"]
        tiles_out.append(dict(
            c=c, r=r, region=region, province=pid,
            terrain=terrain_for(region, c, r, pid),
            river=(region in ("riverlands", "reach") and fbm(c * 0.5, r * 0.5, 77, 2) > 0.63),
            coastal=is_coastal(c, r),
        ))

out = dict(
    width=W, height=H, layout="odd-r-pointy",
    regions=[dict(id=r[0], name=r[1], color=r[4],
                  capital=CAPITALS.get(r[0])) for r in REGIONS],
    provinces=PROVINCES, houses=HOUSES, tiles=tiles_out, seaRoutes=routes,
)
os.makedirs("Assets/IronThrone/Resources/IronThroneData", exist_ok=True)
with open("Assets/IronThrone/Resources/IronThroneData/canonical_map.json", "w", encoding="utf-8") as f:
    json.dump(out, f, ensure_ascii=False, indent=1)

# ---------------------------------------------------------------- preview
S = 24
img = Image.new("RGB", (int(W * SQ3 * S) + 60, H * int(1.5 * S) + 60), "#22485f")
d = ImageDraw.Draw(img)
try:
    font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 11)
    fontb = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 12)
except Exception:
    font = fontb = ImageFont.load_default()

def hpx(c, r):
    return (SQ3 * S * (c + 0.5 * (r & 1)) + 30, 1.5 * S * r + 30)

shades = {"snow": "#eaf2f8", "mountains": "#8b8b8b", "hills": "#b69d70",
          "forest": "#4a7a46", "desert": "#e8d193", "marsh": "#5f7c55",
          "plains": "#87b96b"}
for t in tiles_out:
    c, r = t["c"], t["r"]; cx, cy = hpx(c, r)
    fill = "#2a5670" if t.get("sea") else shades.get(t["terrain"], "#888")
    pts = [(cx, cy - 14), (cx + 12, cy - 7), (cx + 12, cy + 7), (cx, cy + 14), (cx - 12, cy + 7), (cx - 12, cy - 7)]
    d.polygon(pts, fill=fill, outline="#1a2c36")
    if t.get("river"):
        d.line([(cx - 4, cy - 9), (cx + 3, cy - 1), (cx - 2, cy + 9)], fill="#3b7fa8", width=2)
for p in PROVINCES:
    for (c, r) in p["tiles"]:
        cx, cy = hpx(c, r)
        for nc, nr in neighbors(c, r):
            if not (0 <= nc < W and 0 <= nr < H) or not LAND[nr][nc] or PROV[nr][nc] != p["id"]:
                q = PROV_OF_TILE.get((nc, nr)) if (0 <= nc < W and 0 <= nr < H) else None
                col = "#0e1c24" if (not LAND[nr][nc] if 0 <= nc < W and 0 <= nr < H else True) else "#e8e8c8"
                # border edge midpoints
                ang = math.atan2(nr - r, (nx := (nc + 0.5 * (nr & 1)) - (c + 0.5 * (r & 1))))
                ex, ey = cx + math.cos(ang) * 11, cy + math.sin(ang) * 63 / 8
                d.ellipse([ex - 1.6, ey - 1.6, ex + 1.6, ey + 1.6], fill=col)

for p in PROVINCES:
    c, r = p["settlement"]
    cx, cy = hpx(c, r)
    col = "#f5d76e" if p["is_capital"] else ("#ffffff" if p["kind"] != "rural" else "#c9d6de")
    rad = 5 if p["kind"] != "rural" else 2.5
    d.ellipse([cx - rad, cy - rad, cx + rad, cy + rad], fill=col, outline="#000")
    show = p["is_capital"] or p["kind"] != "rural"
    if show:
        d.text((cx + 7, cy - 9), p["name"], fill="#ffffff",
               font=fontb if p["is_capital"] else font, stroke_width=2, stroke_fill="#000")
img.save("tools/canonical_preview.png")

print("provinces:", len(PROVINCES),
      "settlements:", sum(1 for p in PROVINCES if p["kind"] != "rural"),
      "land:", sum(1 for t in tiles_out if not t.get("sea")),
      "houses:", len(HOUSES), "routes:", len(routes),
      "land comps:", len(land_comps), "island comps:", [len(c) for c in land_comps if len(c) < 20])
print("province sizes: min", min(len(p["tiles"]) for p in PROVINCES),
      "max", max(len(p["tiles"]) for p in PROVINCES))
