import json, math, random, os
from PIL import Image, ImageDraw, ImageFont

W, H = 48, 36
SQ3 = math.sqrt(3)

def hex_to_px(c, r):
    # grid-space coords (x = column, y = row); used for shape/province definition
    return (c + 0.5*(r & 1), r)

def px_to_hex(x, y):
    # pointy-top odd-r
    r = round(y/1.5)
    c = round(x/SQ3 - 0.5*(r & 1))
    return c, r

def cube(c, r):
    x = c - (r - (r & 1))//2
    z = r
    y = -x - z
    return x, y, z

def dist(a, b):
    a = (int(round(a[0])), int(round(a[1])))
    b = (int(round(b[0])), int(round(b[1])))
    ax, ay, az = cube(*a); bx, by, bz = cube(*b)
    return max(abs(ax-bx), abs(ay-by), abs(az-bz))

def lerp(a,b,t): return a+(b-a)*t
def clamp(x,a,b): return max(a,min(b,x))

def ellipse(x, y, cx, cy, rx, ry, rot=0.0):
    dx, dy = x-cx, y-cy
    if rot:
        ca, sa = math.cos(rot), math.sin(rot)
        dx, dy = dx*ca + dy*sa, -dx*sa + dy*ca
    return (dx/rx)**2 + (dy/ry)**2

def capsule(x, y, x1, y1, x2, y2, r):
    vx, vy = x2-x1, y2-y1
    wx, wy = x-x1, y-y1
    L = vx*vx+vy*vy
    t = 0 if L == 0 else clamp((wx*vx+wy*vy)/L, 0, 1)
    dx, dy = x-(x1+t*vx), y-(y1+t*vy)
    return (dx*dx+dy*dy) <= r*r

# value noise
def _hash(x, y, seed=7):
    n = x*374761393 + y*668265263 + seed*1442695040888963407
    n = (n ^ (n >> 13)) * 1274126177
    return ((n ^ (n >> 16)) & 0xffffffff)/0xffffffff

def vnoise(x, y, seed=7):
    xi, yi = math.floor(x), math.floor(y)
    xf, yf = x-xi, y-yi
    u, v = xf*xf*(3-2*xf), yf*yf*(3-2*yf)
    a = _hash(xi, yi, seed); b = _hash(xi+1, yi, seed)
    c = _hash(xi, yi+1, seed); d = _hash(xi+1, yi+1, seed)
    return lerp(lerp(a,b,u), lerp(c,d,u), v)

def fbm(x, y, seed=7, oct=3):
    v, amp, f = 0.0, 0.5, 1.0
    for i in range(oct):
        v += amp*vnoise(x*f, y*f, seed+i*13)
        f *= 2.03; amp *= 0.5
    return v

# land blobs
def land(x, y):
    e = 9
    if ellipse(x,y,24,7.0,12.5,6.8) < 1: e = min(e, ellipse(x,y,24,7.0,12.5,6.8))
    if ellipse(x,y,24,0.7,11.5,1.1) < 1: e = min(e, ellipse(x,y,24,0.7,11.5,1.1))
    if ellipse(x,y,24.5,12.0,4.4,3.2) < 1: e = min(e, ellipse(x,y,24.5,12.0,4.4,3.2))
    if ellipse(x,y,23,15.8,9.3,4.4) < 1: e = min(e, ellipse(x,y,23,15.8,9.3,4.4))
    if ellipse(x,y,35,13.5,6.2,5.6) < 1: e = min(e, ellipse(x,y,35,13.5,6.2,5.6))
    if capsule(x,y,29,14.0,33,12.5,3.2): e = 0.0
    if ellipse(x,y,17.5,19.0,7.4,5.8) < 1: e = min(e, ellipse(x,y,17.5,19.0,7.4,5.8))
    if ellipse(x,y,22,24.0,11.0,6.5) < 1: e = min(e, ellipse(x,y,22,24.0,11.0,6.5))
    if ellipse(x,y,33,22.0,7.2,5.4) < 1: e = min(e, ellipse(x,y,33,22.0,7.2,5.4))
    if ellipse(x,y,29,18.7,5.2,3.6) < 1: e = min(e, ellipse(x,y,29,18.7,5.2,3.6))
    if ellipse(x,y,31,29.0,11.0,4.3) < 1: e = min(e, ellipse(x,y,31,29.0,11.0,4.3))
    if capsule(x,y,35,29.5,39,28.5,3.4): e = 0.0
    if ellipse(x,y,9.5,14.5,2.2,2.2) < 1: e = min(e, ellipse(x,y,9.5,14.5,2.2,2.2))
    if ellipse(x,y,12.5,15.2,1.5,1.4) < 1: e = min(e, ellipse(x,y,12.5,15.2,1.5,1.4))
    if ellipse(x,y,10.5,17.0,1.1,1.0) < 1: e = min(e, ellipse(x,y,10.5,17.0,1.1,1.0))
    if ellipse(x,y,14.0,3.7,1.8,1.2) < 1: return True # bear island
    if ellipse(x,y,41.0,19.5,1.4,1.6) < 1: return True # tarth
    if ellipse(x,y,8.5,28.0,1.4,1.2) < 1: return True # arbor
    if ellipse(x,y,37.0,18.0,1.2,1.1) < 1: return True # dragonstone
    # coastline noise
    n = fbm(x*0.28, y*0.28, 11, 3) - 0.5
    return e < 1.0 + n*0.55

REGIONS = [
    # id, name, anchor, weight (lower = greedier), color
    ("north","Север",(24,7.2),1.00,"#8aa9bd"),
    ("wall","Стена",(24,1.0),1.05,"#dfe9f2"),
    ("islands","Железные острова",(10.5,15.2),1.15,"#7b8794"),
    ("riverlands","Речные земли",(23,15.7),1.00,"#7fb069"),
    ("vale","Долина",(35,13.4),0.95,"#b7a6d6"),
    ("westerlands","Западные земли",(17.6,19.0),1.00,"#d9b36c"),
    ("reach","Простор",(22,24.0),1.00,"#8fc97e"),
    ("stormlands","Штормовые земли",(33,22.0),0.97,"#c9a35c"),
    ("crownlands","Королевские земли",(29.3,18.7),0.80,"#d98a8a"),
    ("dorne","Дорн",(31,29.0),1.00,"#e0b96e"),
]

SEEDS = {
"north": [("winterfell","Винтерфелл",24,8,4,"stark"),("white_harbor","Белая Гавань",29.5,9,3,"manderly"),("dreadfort","Дредфорт",28,5,3,"bolton"),("karhold","Кархолд",30,3.5,2,"karstark"),("last_hearth","Последний Очаг",25,3.0,2,"umber"),("deepwood","Дремучий лес",18,6,2,"glover"),("torrhen","Торрхенов удел",20.5,8.2,2,"stark"),("moat_cailin","Ров Кейлин",22,11.5,2,"stark"),("bear_island","Медвежий остров",14,3.7,2,"mormont"),("barrowton","Барроутон",20,9.5,2,"dustin"),("hornwood","Хорнвуд",27,7.5,2,"hornwood"),("widows_watch","Вдовий Дозор",22,6.0,2,"stark"),("oldcastle","Старый Замок",28,9.5,2,"stark"),("seadragon","Морской Дракон",18,3.6,2,"stark")],
"wall": [("castle_black","Чёрный Замок",24,1.0,3,"nightswatch"),("eastwatch","Восточный Дозор",30,1.0,2,"nightswatch"),("shadow_tower","Сумеречная башня",17.5,1.0,2,"nightswatch"),("nightfort","Ночной Форт",21,1.0,2,"nightswatch")],
"islands": [("pyke","Пайк",10.5,14.3,3,"greyjoy"),("great_wyk","Большой Вик",7.5,15.6,2,"goodbrother"),("old_wyk","Старый Вик",9.5,16.5,2,"wyk"),("harlaw","Харлоу",11.8,13.8,2,"harlaw"),("blacktyde","Чёрный Прилив",13.2,16.4,2,"blacktyde"),("saltcliffe","Солтклифф",9.0,13.6,2,"saltcliffe")],
"riverlands": [("riverrun","Риверран",19.5,17.2,3,"tully"),("harrenhal","Харренхол",27,17.0,3,"strong"),("twins","Близнецы",24,14.2,2,"frey"),("seagard","Сигард",16.6,15.5,2,"mallister"),("fairmarket","Ярмарка",22,15.8,2,"tully"),("maidenpool","Девичий Пруд",29,16.5,2,"mooton"),("pinkmaiden","Розовый Девичник",18,18.5,2,"piper"),("raventree","Чёрное Древо",21.5,17.8,2,"blackwood"),("stone_hedge","Каменная Изгородь",23.5,18.4,2,"bracken"),("darry","Дарри",26,16.0,2,"darry")],
"vale": [("eyrie","Орлиное Гнездо",35,12.0,4,"arryn"),("gulltown","Чаячий город",38,14.0,3,"grafton"),("runestone","Рунный камень",37,11.0,2,"royce"),("ironoaks","Железные Дубы",33.5,14.5,2,"waynwood"),("redfort","Красный замок",35,16.0,2,"redfort"),("strongsong","Крепкая Песня",31.5,11.0,2,"belmore"),("longbow_hall","Зал Длинного Лука",34.5,14.8,2,"hunter"),("old_anchor","Старый Якорь",37,13.2,2,"arwyn"),("hearths_home","Домашний Очаг",32.5,13.0,2,"corbray"),("wickenden","Викенден",39,12.5,2,"waxley")],
"westerlands": [("casterly","Утёс Кастерли",15.5,19.5,4,"lannister"),("lannisport","Ланниспорт",14,19.0,3,"lannister"),("golden_tooth","Золотой Зуб",21,20.5,2,"lefford"),("ashemark","Пепельный замок",18,21.5,2,"marbrand"),("crakehall","Крейкхолл",15,22.5,2,"crakehall"),("banefort","Погибель",12,18.5,2,"banefort"),("silverhill","Серебряный холм",19.5,19.0,2,"serrett"),("fair_isle","Прекрасный Остров",10.5,20.5,2,"farman"),("cornfield","Кукурузный Дол",13.5,23.5,2,"swyft"),("deep_den","Глубокая Берлога",17.5,19.5,2,"deep_den")],
"reach": [("highgarden","Хайгарден",20,24.0,4,"tyrell"),("oldtown","Старомест",13,27.0,4,"hightower"),("horn_hill","Рогов Холм",23,27.0,2,"tarly"),("arbor","Арбор",8.5,28.0,3,"redwyne"),("brightwater","Светлый Ручей",25,22.0,2,"florent"),("bitterbridge","Горький Мост",26,20.5,2,"tully"),("tumbleton","Тамблтон",28,19.5,2,"footly"),("ashford","Эшфорд",25,26.0,2,"ashford"),("three_towers","Три Башни",15,26.0,2,"oakheart"),("blackcrown","Чёрная Корона",11,25.0,2,"bulwer"),("sunhouse","Солнечный Дом",17,27.5,2,"cuy"),("longtable","Длинный Стол",20,21.0,2,"merryweather"),("honeyholt","Медовый Холт",17,24.5,2,"beesbury"),("goldengrove","Золотая Роща",18,22.0,2,"rowan")],
"stormlands": [("storms_end","Штормовой Предел",33,21.0,4,"baratheon"),("griffins_roost","Гнездо Грифона",35,19.0,2,"connington"),("crows_nest","Воронье Гнездо",31,24.0,2,"morrigen"),("nightsong","Ночная Песнь",28,25.5,2,"caron"),("blackhaven","Чёрный Приют",30,26.5,2,"dondarrion"),("tarth","Тарт",41,19.5,2,"tarth"),("weeping_town","Плачущий город",36,23.0,2,"swann"),("stonehelm","Каменный Шлем",34,25.0,2,"swann"),("grandview","Грандвью",33,23.0,2,"grandison")],
"dorne": [("sunspear","Солнечное Копьё",35,29.5,4,"martell"),("yronwood","Айронвуд",29,27.0,3,"yronwood"),("starfall","Звездопад",22,29.0,3,"dayne"),("skyreach","Небесный Предел",28,28.5,2,"fowler"),("hellholt","Адский Холт",31,28.5,2,"uller"),("godsgrace","Божья Милость",27,29.5,2,"allyrion"),("planky","Планка-Таун",35,30.0,2,"martell"),("vaith","Вейт",30,30.0,2,"vaith"),("saltshore","Соляной Берег",38,31.0,2,"gargalen"),("lemonwood","Лимонный Лес",33,27.5,2,"dalt"),("sandstone","Песчаник",25,27.5,2,"qorgyle")],
"crownlands": [("kings_landing","Королевская Гавань",31,19.5,5,"baratheon"),("dragonstone","Драконий Камень",37,18.0,3,"targaryen"),("duskendale","Сосновый Бор",33,18.0,2,"darklyn"),("rosby","Росби",30.5,18.5,2,"rosby"),("driftmark","Дрифтмарк",35,19.5,2,"velaryon"),("stokeworth","Стокворт",29.5,19.6,2,"stokeworth"),("sharp_point","Острый Мыс",38,20.5,2,"baratheon"),("hayford","Хейфорд",29.2,17.3,2,"hayford"),("rooks_rest","Гнездо Воронов",34,16.5,2,"staunton"),("sweetport","Сладкий Порт",36.5,20.8,2,"sunglass")],
}

REG = {r[0]: r for r in REGIONS}
ANCH = {r[0]: r[2] for r in REGIONS}
WEIGHT = {r[0]: r[3] for r in REGIONS}
COLOR = {r[0]: r[4] for r in REGIONS}

def nearest_region(c, r):
    best, bd = None, 1e9
    for rid, _, a, w, _ in REGIONS:
        d = dist((c,r), a)*w
        if d < bd: bd, best = d, rid
    # rule overrides
    if dist((c,r),(14,3.7)) <= 2 and dist((c,r),(24,7.2)) > 3: best = "north"
    if dist((c,r),(41,19.5)) <= 2: best = "stormlands"
    if dist((c,r),(8.5,28)) <= 2: best = "reach"
    if dist((c,r),(37,18)) <= 2: best = "crownlands"
    return best

def snap_to_land(c, r):
    if 0 <= c < W and 0 <= r < H and LAND[r][c]:
        return c, r
    for rad in range(1, 6):
        cands = []
        for rr in range(max(0,r-rad), min(H,r+rad+1)):
            for cc in range(max(0,c-rad), min(W,c+rad+1)):
                if LAND[rr][cc] and dist((c,r),(cc,rr)) <= rad:
                    cands.append(((cc,rr), dist((c,r),(cc,rr))))
        if cands: return min(cands)[0]
    return None

LAND = [[False]*W for _ in range(H)]
TILE_REGION = [[None]*W for _ in range(H)]
for _r in range(H):
    for _c in range(W):
        TILE_REGION[_r][_c] = nearest_region(_c, _r) if LAND[_r][_c] else None
for r in range(H):
    for c in range(W):
        x, y = hex_to_px(c, r)
        LAND[r][c] = land(x, y)

# keep main component + islands
SEEDS_LAND = []
for rid, items in SEEDS.items():
    for sid, name, sc, sr, lvl, house in items:
        p = snap_to_land(int(round(sc)), int(round(sr)))
        if p: SEEDS_LAND.append((rid,sid,name,p[0],p[1],lvl,house))

# fix seed region if seed tile region differs? allow
PROV = [[None]*W for _ in range(H)]
for r in range(H):
    for c in range(W):
        if not LAND[r][c]: continue
        rid = nearest_region(c, r)
        best, bd = None, 1e9
        for srid, sid, name, sc, sr, lvl, house in SEEDS_LAND:
            if srid != rid: continue
            d = dist((c,r),(sc,sr))
            # slight preference to high level settlements
            d -= 0.02*lvl
            if d < bd: bd, best = d, (srid,sid,name,sc,sr,lvl,house)
        if best is None:
            # fallback nearest seed overall
            for srid, sid, name, sc, sr, lvl in SEEDS_LAND:
                d = dist((c,r),(sc,sr))
                if d < bd: bd, best = d, (srid,sid,name,sc,sr,lvl,house)
        PROV[r][c] = best[1]

# count province sizes, drop tiny ones by merging into adjacent province
from collections import defaultdict, deque
def province_sizes():
    d = defaultdict(int)
    for r in range(H):
        for c in range(W):
            if PROV[r][c]: d[PROV[r][c]] += 1
    return d

def neighbors(c,r):
    # odd-r pointy top
    dirs = [(-1,0),(1,0),(0,-1),(1,-1) if r%2==0 else (0,-1),(-1,1) if r%2==0 else (0,1),(-1,-1) if r%2==0 else (-1,0),(0,1)]
    # use cube neighbors proper
    x = c - (r - (r&1))//2; z = r; y = -x-z
    res=[]
    for dx,dy,dz in [(1,-1,0),(1,0,-1),(0,1,-1),(-1,1,0),(-1,0,1),(0,-1,1)]:
        nx,ny,nz = x+dx,y+dy,z+dz
        nr = nz; nc = nx + (nz - (nz&1))//2
        if 0<=nc<W and 0<=nr<H: res.append((nc,nr))
    return res

for _ in range(6):
    sizes = province_sizes()
    tiny = [p for p,n in sizes.items() if n < 3]
    if not tiny: break
    for p in tiny:
        # find neighbor province with most adjacent tiles
        adj = defaultdict(int)
        for r in range(H):
            for c in range(W):
                if PROV[r][c] == p:
                    for nc,nr in neighbors(c,r):
                        if LAND[nr][nc] and PROV[nr][nc] and PROV[nr][nc] != p:
                            adj[PROV[nr][nc]] += 1
        if not adj:
            continue
        target = max(adj, key=adj.get)
        for r in range(H):
            for c in range(W):
                if PROV[r][c] == p: PROV[r][c] = target

# Recompute province centers as average tile px; choose settlement tile = closest to seed or centroid
prov_tiles = defaultdict(list)
for r in range(H):
    for c in range(W):
        if PROV[r][c]: prov_tiles[PROV[r][c]].append((c,r))

prov_meta = {}
seed_by_id = {s[1]: s for s in SEEDS_LAND}

# Terrain
random.seed(42)
def terrain_for(rid, c, r, p):
    x,y = hex_to_px(c,r)
    n = fbm(x*0.6, y*0.6, 101, 3)
    if rid == "wall": return "snow"
    if rid == "north":
        if r <= 3: return "snow"
        if n < 0.42: return "forest"
        if n > 0.64: return "hills"
        return "plains"
    if rid == "islands": return "hills" if n > 0.45 else "plains"
    if rid == "riverlands":
        if n < 0.38: return "marsh"
        if n < 0.55: return "forest"
        if n > 0.72: return "hills"
        return "plains"
    if rid == "vale":
        if n > 0.60: return "mountains"
        if n > 0.45: return "hills"
        if n < 0.30: return "forest"
        return "plains"
    if rid == "westerlands":
        if n > 0.66: return "mountains"
        if n > 0.45: return "hills"
        if n < 0.30: return "forest"
        return "plains"
    if rid == "reach":
        if n < 0.25: return "forest"
        if n > 0.74: return "hills"
        return "plains"
    if rid == "stormlands":
        if n < 0.35: return "forest"
        if n > 0.66: return "hills"
        return "plains"
    if rid == "crownlands":
        if n < 0.30: return "forest"
        if n > 0.78: return "hills"
        return "plains"
    if rid == "dorne":
        if n > 0.75: return "mountains"
        if n > 0.55: return "hills"
        if n < 0.30: return "desert"
        return "desert" if random.random()<0.35 else "plains"
    return "plains"

# coastal check
def is_coastal(c,r):
    for nc,nr in neighbors(c,r):
        if not LAND[nr][nc]: return True
    return False

PORT_IDS = {"white_harbor","seagard","gulltown","old_anchor","wickenden","lannisport","fair_isle","banefort","oldtown","arbor","highgarden","kings_landing","dragonstone","driftmark","duskendale","sweetport","tarth","weeping_town","stonehelm","sunspear","saltshore","planky","pyke","great_wyk","old_wyk","harlaw","blacktyde","saltcliffe","bear_island","eastwatch","sharp_point","runestone","crakehall","three_towers","goldengrove","riverrun"}

provinces = []
for pid, tiles in sorted(prov_tiles.items()):
    rid, sid, name, sc, sr, lvl, house = seed_by_id[pid]
    # settlement tile: actual seed tile if belongs, else nearest to seed
    st = None; bd = 99
    for (c,r) in tiles:
        d = dist((c,r),(sc,sr))
        if d < bd: bd, st = d, (c,r)
    sizes = len(tiles)
    cap = lvl >= 4
    provinces.append({
        "id": pid, "name": name, "region": rid, "seed": [sc,sr], "settlement": list(st), "house": house,
        "level": lvl, "tiles": [[c,r] for c,r in tiles],
        "terrain": terrain_for(rid, st[0], st[1], pid),
        "coastal": any(is_coastal(c,r) for c,r in tiles),
        "port": pid in PORT_IDS,
        "is_capital": cap,
    })

# ensure each region has a capital province (highest level seed)
by_region = defaultdict(list)
for p in provinces: by_region[p["region"]].append(p)
for rid, ps in by_region.items():
    if not any(p["is_capital"] for p in ps) and ps:
        max(ps, key=lambda p:p["level"])["is_capital"] = True

# dominant terrain per province
from collections import Counter as _C
for p in provinces:
    cnt = _C(terrain_for(p["region"], c, r, p["id"]) for c,r in p["tiles"])
    p["terrain"] = cnt.most_common(1)[0][0]
# region of each tile follows province
PROV_REGION = {p["id"]: p["region"] for p in provinces}
for r in range(H):
    for c in range(W):
        if PROV[r][c]: TILE_REGION[r][c] = PROV_REGION[PROV[r][c]]

# tiles output
tiles_out = []
for r in range(H):
    for c in range(W):
        if not LAND[r][c]:
            tiles_out.append({"c":c,"r":r,"sea":True})
            continue
        rid = TILE_REGION[r][c]
        terr = terrain_for(rid, c, r, PROV[r][c])
        # river flag in riverlands/reach
        river = False
        if rid == "riverlands" and fbm(c*0.5, r*0.5, 77, 2) > 0.60: river = True
        if rid == "reach" and fbm(c*0.5, r*0.5, 78, 2) > 0.68: river = True
        tiles_out.append({"c":c,"r":r,"region":rid,"province":PROV[r][c],"terrain":terr,"river":river,"coastal":is_coastal(c,r)})

# sea routes based on province ports / proximity
routes = []
def center(p):
    ts = p["tiles"]; return (sum(t[0] for t in ts)/len(ts), sum(t[1] for t in ts)/len(ts))
ports = [p for p in provinces if p["port"]]
for i,a in enumerate(ports):
    for b in ports[i+1:]:
        ca, cb = center(a), center(b)
        d = math.hypot(ca[0]-cb[0], ca[1]-cb[1])
        if d < 7.5 and a["region"] != b["region"]:
            # only if reasonably close and at least one island
            if a["region"]=="islands" or b["region"]=="islands" or d < 5.5:
                routes.append([a["id"], b["id"]])
# manual essential routes
for pair in [("pyke","seagard"),("pyke","banefort"),("harlaw","seagard"),("blacktyde","banefort"),
             ("bear_island","deepwood"),("bear_island","torrhen"),
             ("dragonstone","kings_landing"),("driftmark","duskendale"),("dragonstone","duskendale"),
             ("tarth","storms_end"),("tarth","weeping_town"),
             ("arbor","oldtown"),("arbor","three_towers"),
             ("eastwatch","white_harbor")]:
    if [pair[0],pair[1]] not in routes and [pair[1],pair[0]] not in routes:
        routes.append([pair[0],pair[1]])

out = {"width":W,"height":H,"regions":[{"id":r[0],"name":r[1],"color":r[4],"capital":next((p["id"] for p in provinces if p["region"]==r[0] and p["is_capital"]), None)} for r in REGIONS],
       "provinces":provinces,"tiles":tiles_out,"seaRoutes":routes}
os.makedirs("Assets/IronThrone/Resources/IronThroneData", exist_ok=True)
with open("Assets/IronThrone/Resources/IronThroneData/canonical_map.json","w",encoding="utf-8") as f:
    json.dump(out,f,ensure_ascii=False,indent=1)

# preview
S = 22
img = Image.new("RGB", (int(W*SQ3*S)+S, H*int(1.5*S)+S), "#234a63")
d = ImageDraw.Draw(img)
def hpx(c,r):
    return (SQ3*S*(c+0.5*(r&1))+30, 1.5*S*r+30)
try:
    font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 11)
    fontb = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 12)
except Exception:
    font = fontb = ImageFont.load_default()
for t in tiles_out:
    c,r = t["c"],t["r"]; cx,cy = hpx(c,r)
    if t.get("sea"):
        fill = "#2b5770"
    else:
        base = COLOR.get(t["region"], "#888")
        # darken by terrain
        shades = {"snow":"#e8f1f7","mountains":"#8d8d8d","hills":"#b39a6b","forest":"#4f7d4a","desert":"#e6cf8a","marsh":"#627d54","plains":"#8fbf72"}
        fill = shades.get(t["terrain"], base)
    pts=[(cx, cy-12),(cx+11, cy-6),(cx+11, cy+6),(cx, cy+12),(cx-11, cy+6),(cx-11, cy-6)]
    d.polygon(pts, fill=fill, outline="#20323c")
    if t.get("river"):
        d.line([(cx-4,cy-8),(cx+2,cy-1),(cx-2,cy+7)], fill="#2f6f9e", width=2)
# province borders
for p in provinces:
    for (c,r) in p["tiles"]:
        cx,cy=hpx(c,r)
        for nc,nr in neighbors(c,r):
            if 0<=nc<W and 0<=nr<H and (not LAND[nr][nc] or PROV[nr][nc]!=p["id"]):
                # draw edge roughly: just a dot
                d.ellipse([cx-2,cy-2,cx+2,cy+2], fill="#101c24")
for p in provinces:
    c,r = p["settlement"]; cx,cy=hpx(c,r)
    col = "#f5d76e" if p["is_capital"] else "#ffffff"
    d.ellipse([cx-4,cy-4,cx+4,cy+4], fill=col, outline="#000")
    if p["is_capital"] or p["level"]>=3:
        d.text((cx+6,cy-8), p["name"], fill="#fff", font=fontb if p["is_capital"] else font, stroke_width=2, stroke_fill="#000")
img.save("tools/canonical_preview.png")
print("provinces", len(provinces), "land", sum(1 for t in tiles_out if not t.get("sea")), "routes", len(routes))
