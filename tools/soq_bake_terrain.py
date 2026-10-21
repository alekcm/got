#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Собирает тайлы рельефа из настоящих текстур Songs of Conquest.

В дампе лежит `Texture2DArray/CartographyAdventureTerrainTextureArray.png` —
это Texture2DArray 128x128, разложенная «лентой» по вертикали: 172 слоя.
Слои идут парами: [подложка, полог]. Полог — маска (альфа = крона, красный
канал = её освещённость), поэтому в игре он накладывается поверх земли и даёт
ту самую «шумную» траву/лес без квадратных границ биомов.

Что делает скрипт:
  1. читает ленту и режет её на слои;
  2. для каждого из 16 классов рельефа берёт слой подложки и подкрашивает его
     в цвет класса — структура земли остаётся настоящей; печёт в
     Resources/Art/SoQ/Terrain/<имя>.png (основная карта партии не трогается);
  3. нормализует среднюю яркость всех тайлов к одной величине (иначе снег
     «выгорает», а лес проваливается в чёрное);
  4. печёт `soq_road.png` — кусок настоящей грунтовой дороги (RGB дорожной
     ленты), которым потом рисуются дороги на карте;
  5. пишет отчёт с измеренной яркостью.

Запуск:  python3 tools/soq_bake_terrain.py [--dump DIR]
Требуется Pillow. Скрипт идемпотентен.
"""
import argparse
import math
import os
import random
import sys

try:
    from PIL import Image, ImageFilter
except ImportError:  # pragma: no cover
    print("Нужен Pillow: pip install --user pillow", file=sys.stderr)
    raise

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_DUMP = os.path.join(ROOT, "Как должно выглядеть", "Материалы")
# Отдельная папка: Resources/Art/Terrain — это тайлы ОСНОВНОЙ карты партии
# (WorldMapView). Карта SoQ берёт свои тайлы отсюда, чтобы не менять вид игры.
OUT_TERRAIN = os.path.join(ROOT, "Assets", "IronThrone", "Resources", "Art", "SoQ", "Terrain")
OUT_SOQ = os.path.join(ROOT, "Assets", "IronThrone", "Resources", "Art", "SoQ")

TILE = 128
# Целевая средняя яркость тайла: все классы приводятся к ней, чтобы карта
# не «светилась» пятнами разной экспозиции.
TARGET_LUMA = 116.0

# Класс -> (файл, слой, цвет подкраски, доля подкраски, целевая яркость).
#
# Главное правило SoQ: земля — это ВСЕГДА трава/песок/камень, а лес — не
# «ковёр из крон», а отдельные деревья-спрайты на обычной траве. Поэтому
# классы ForestMix/ForestPine берут тёмно-зелёные слои травы, а не 102/106
# (там нарисован частокол сосен — на карте он превращался в «чешую»).
#
# Лента — Texture2DArray 128x128, 172 слоя; чётные слои — подложки.
CLASSES = [
    # имя файла        слой  цвет                    доля  яркость
    # Оттенки подобраны по скриншотам SoQ: трава там ОЛИВКОВАЯ (R ≈ G, B сильно
    # ниже), а не насыщенно-зелёная. Иначе карта выглядит «мультяшной».
    ("000_grass",        8,   (0.62, 0.57, 0.30),     0.85,  104),
    ("010_tundra",      60,   (0.58, 0.54, 0.34),     0.80,   98),
    ("024_dirt",         0,   (0.56, 0.46, 0.28),     0.70,   94),
    ("028_dry",         88,   (0.62, 0.54, 0.30),     0.75,   98),
    ("034_rock_green", 164,   (0.54, 0.56, 0.42),     0.80,   96),
    ("040_sand",        40,   (0.72, 0.66, 0.46),     0.80,  112),
    ("042_sand_rock",   44,   (0.62, 0.58, 0.44),     0.80,  104),
    ("044_fields",      68,   (0.66, 0.60, 0.24),     0.75,  100),
    ("050_autumn",     124,   (0.70, 0.42, 0.20),     0.70,   96),
    # Скалы: слои 62/94 — это ВЕРТИКАЛЬНЫЕ обрывы (полосатая порода), как земля
    # они читаются «грязной рябью». Поэтому берём каменистый слой 34 и красим
    # его в холодный камень — так в SoQ выглядит подножие гор.
    ("062_rock",        34,   (0.46, 0.47, 0.40),     0.55,   78, 1.1),
    ("094_snow_rock",   34,   (0.72, 0.74, 0.74),     0.60,  104, 1.1),
    ("096_snow",        96,   (0.88, 0.91, 0.96),     0.40,  140),
    ("100_cold_grass", 100,   (0.46, 0.56, 0.38),     0.50,   94),
    ("102_forest_mix",  12,   (0.44, 0.48, 0.26),     0.80,   84),
    ("104_marsh",      104,   (0.44, 0.50, 0.34),     0.75,   88),
    ("106_forest_pine",112,   (0.34, 0.42, 0.26),     0.85,   76),
]


def load_layers(path):
    img = Image.open(path).convert("RGBA")
    w, h = img.size
    count = h // w
    return [img.crop((0, i * w, w, (i + 1) * w)) for i in range(count)]


def over(base, top):
    """Наложить полог на подложку по его альфе — как это делает SoQ."""
    b = base.convert("RGBA")
    t = top.convert("RGBA")
    return Image.alpha_composite(b, t).convert("RGB")


def colorize(ground, tint, amount):
    """
    Яркость настоящего слоя -> цвет класса. Структура (травинки, галька, кроны)
    сохраняется, уходит только холодный уклон и разнобой экспозиции.
    """
    px = ground.load()
    out = Image.new("RGB", (TILE, TILE))
    o = out.load()
    tr, tg, tb = tint
    tl = max(1e-3, 0.299 * tr + 0.587 * tg + 0.114 * tb)
    tr, tg, tb = tr / tl, tg / tl, tb / tl
    for y in range(TILE):
        for x in range(TILE):
            c = px[x, y]
            r, g, b = c[0], c[1], c[2]
            lum = 0.299 * r + 0.587 * g + 0.114 * b
            rr = r * (1 - amount) + lum * tr * amount
            rg = g * (1 - amount) + lum * tg * amount
            rb = b * (1 - amount) + lum * tb * amount
            o[x, y] = (min(255, int(rr)), min(255, int(rg)), min(255, int(rb)))
    return out


def luma(img):
    px = img.load()
    total = 0.0
    for y in range(0, TILE, 2):
        for x in range(0, TILE, 2):
            r, g, b = px[x, y]
            total += 0.299 * r + 0.587 * g + 0.114 * b
    return total / ((TILE // 2) * (TILE // 2))


def normalize(img, target=None):
    """
    Приводит среднюю яркость тайла к целевой. Гамма применяется к яркости,
    а не к каналам: оттенок не уезжает, пересветов не бывает.
    """
    target = TARGET_LUMA if target is None else target
    cur = max(2.0, luma(img))
    gamma = math.log(target / 255.0) / math.log(cur / 255.0)
    lut = [min(255, int(255.0 * ((v / 255.0) ** gamma) + 0.5)) for v in range(256)]
    px = img.load()
    for y in range(TILE):
        for x in range(TILE):
            r, g, b = px[x, y]
            l = 0.299 * r + 0.587 * g + 0.114 * b
            if l < 1.0:
                px[x, y] = (0, 0, 0)
                continue
            k = lut[int(l)] / l
            px[x, y] = (min(255, int(r * k)), min(255, int(g * k)), min(255, int(b * k)))
    return gamma


def crop_road(layers):
    """
    Мостовая для карты SoQ.

    В дорожной ленте RGB — это камень, но альфа хранит маску тонких тропинок,
    а не сплошное полотно, поэтому «вырезать кусок дороги» нельзя: в кадр
    попадают полосы и направляющие. Вместо этого собираем мостовую из
    настоящих каменных пикселей всех слоёв: берём только те, где альфа
    высокая, и укладываем их в тайл 128×128 со случайным сдвигом — структура
    камня настоящая, швов и полос нет.
    """
    rng = random.Random(20240611)
    stones = []
    for layer in layers[:14]:
        px = layer.load()
        w, h = layer.size
        for y in range(0, h, 3):
            for x in range(0, w, 3):
                r, g, b, a = px[x, y]
                if a > 200 and r + g + b > 250:
                    # камень SoQ — приглушённый серо-бежевый (~150,150,142), не белый
                    stones.append((min(255, int(r * 0.95 + 34)),
                                   min(255, int(g * 0.95 + 34)),
                                   min(255, int(b * 0.92 + 30))))
    if not stones:
        return Image.new("RGB", (TILE, TILE), (150, 142, 124))

    out = Image.new("RGB", (TILE, TILE))
    op = out.load()
    step = 4
    for y in range(0, TILE, step):
        for x in range(0, TILE, step):
            # средний цвет нескольких камней — получается зернистая мостовая
            n = 3
            acc = [0, 0, 0]
            for _ in range(n):
                r, g, b = stones[rng.randrange(len(stones))]
                acc[0] += r; acc[1] += g; acc[2] += b
            c = (acc[0] // n, acc[1] // n, acc[2] // n)
            for dy in range(step):
                for dx in range(step):
                    if x + dx < TILE and y + dy < TILE:
                        jitter = rng.randint(-8, 8)
                        op[x + dx, y + dy] = tuple(
                            max(0, min(255, v + jitter)) for v in c)
    # лёгкое размытие: камни сливаются в полотно, но фактура остаётся
    out = out.filter(ImageFilter.GaussianBlur(0.5))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", default=DEFAULT_DUMP)
    args = ap.parse_args()

    src = os.path.join(args.dump, "Texture2DArray")
    terrain_png = os.path.join(src, "CartographyAdventureTerrainTextureArray.png")
    road_png = os.path.join(src, "CartographyAdventureRoadTextureArray.png")
    if not os.path.isfile(terrain_png):
        raise SystemExit("Нет ленты рельефа: " + terrain_png)

    layers = load_layers(terrain_png)
    print("Слоёв в ленте рельефа:", len(layers))

    os.makedirs(OUT_TERRAIN, exist_ok=True)
    os.makedirs(OUT_SOQ, exist_ok=True)
    report = []
    for entry in CLASSES:
        name, layer_i, tint, amount, target = entry[:5]
        blur = entry[5] if len(entry) > 5 else 0.0
        img = colorize(layers[layer_i].convert("RGB"), tint, amount)
        k = normalize(img, target)
        if blur > 0:
            # галечник 128->24 px превращается в муар; лёгкое размытие оставляет
            # структуру камня, но убирает «штриховку» при уменьшении
            img = img.filter(ImageFilter.GaussianBlur(blur))
        img.save(os.path.join(OUT_TERRAIN, name + ".png"), optimize=True)
        report.append((name, round(luma(img), 1), round(k, 2)))

    if os.path.isfile(road_png):
        road_layers = load_layers(road_png)
        road = crop_road(road_layers)
        road.save(os.path.join(OUT_SOQ, "soq_road.png"), optimize=True)
        print("Дорога: soq_road.png 64x64")

    print("Тайлы (файл, средняя яркость, гамма):")
    for name, l, k in report:
        print("  %-16s luma %5.1f  g%.2f" % (name, l, k))
    print("Записано в", OUT_TERRAIN)
    return 0


if __name__ == "__main__":
    sys.exit(main())
