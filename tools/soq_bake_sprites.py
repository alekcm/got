#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Собирает арт объектов карты приключений из дампа Songs of Conquest
(папка «Как должно выглядеть/Материалы») в атласы Unity-проекта.

Что делает:
  1. читает Sprite/*.asset (rect, pivot, pixels-to-units, guid текстуры);
  2. находит PNG в Texture2D/ по guid;
  3. вырезает одиночные спрайты и собирает многослойные (города, лагеря, таверна,
     норы) по общим «якорям» (pivot), как это делает сама SoQ;
  4. упаковывает результат в несколько атласов (shelf packing) в
     Assets/IronThrone/Resources/Art/SoQ/;
  5. пишет Assets/IronThrone/Resources/Art/soq_sprite_slices.json
     (имя спрайта -> атлас + rect в координатах Unity: снизу-слева) и .meta файлы.

Запуск:  python3 tools/soq_bake_sprites.py [--dump DIR] [--force]
Требуется Pillow. Скрипт идемпотентен: повторный запуск даёт те же файлы.
"""
import argparse
import glob
import json
import os
import re
import struct
import sys
import hashlib

try:
    from PIL import Image
except ImportError:
    print("Нужен Pillow: pip install --user pillow", file=sys.stderr)
    raise

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_DUMP = os.path.join(ROOT, "Как должно выглядеть", "Материалы")
OUT_ART = os.path.join(ROOT, "Assets", "IronThrone", "Resources", "Art")
OUT_SOQ = os.path.join(OUT_ART, "SoQ")
OUT_SLICES = os.path.join(OUT_ART, "soq_sprite_slices.json")

PPU = 32.0
PAD = 2

# --------------------------------------------------------------------------- рецепты
# Одиночный спрайт: имя_в_атласе -> имя_спрайта SoQ
SINGLES = {
    # шахты (фракционные варианты одной и той же постройки)
    "GoldMine_Arleon": "GoldMine_Default_Arleon",
    "GoldMine_Vanir": "GoldMine_Default_Vanir",
    "GoldMine_Barya": "GoldMine_Default_Barya",
    "GoldMine_Rana": "GoldMine_Default_Rana",
    "GoldMine_Loth": "GoldMine_Default_Loth",
    "GoldMine_Roots": "GoldMine_Default_Roots",
    "GoldMine_Yulan": "GoldMine_Default_Yulan",
    # ресурсные шахты
    "AmberMine": "AncientAmberMine_Default_1",
    "StoneMine": "StoneMine_Default_1",
    "WoodMine": "WoodMine_Default_1",
    "CelestialMine": "CelestialOreMine_Default_1",
    "GlimmerLoom": "GlimmerweaveLoom_Default_1",
    # мирные постройки
    "Farmhouse": "Farmhouse_Default_1",
    "WaterMill": "WaterMill_Default_1",
    # цели и природа
    "BeaconOn": "Beacon_Default_On_1",
    "BeaconOff": "Beacon_Default_Off_1",
    "Weirwood": "RootsEssenceTree_Default",
    "MushroomGrove": "RootsFingerMushrooms_Full_Mid_Default",
    "SageCap": "RootsSageCap_Anim_1",
    "Portal": ("PermanentMapEntityAtlas", "Portal_Main_On_1"),
    # заглушки (арта объекта в дампе нет)
    "PlaceholderBuildingLarge": "AdventurePlaceholderBuildingLarge",
    "PlaceholderBuildingMedium": "AdventurePlaceholderBuildingMedium",
    "PlaceholderBuildingSmall": "AdventurePlaceholderBuildingSmall",
    "PlaceholderBaseLarge": "AdventurePlaceholderBaseLarge",
    "PlaceholderBaseMedium": "AdventurePlaceholderBaseMedium",
    "PlaceholderBaseSmall": "AdventurePlaceholderBaseSmall",
    # стражи
    "GuardBandit": "NeutralBandit_Default_1_Idle_1",
    "GuardArcher": "NeutralBanditArcher_Default_1_Idle_1",
    "GuardWolf": "NeutralWolf_Default_1_Idle_1",
    "GuardLeader": "NeutralBanditLeader_Default_1_Idle_1",
}

# Многослойные объекты: имя_в_атласе -> список слоёв (сзади -> спереди)
COMPOSITES = {
    "TownTemperate": [
        "RanaCamp_WayBack_17_Level3_1", "RanaCamp_Back_17_Level3_1", "RanaCamp_Base_17_Level3_1",
        "RanaCamp_Mid_17_Level3_1", "RanaCamp_Main_17_Level3_1", "RanaCamp_LeftSide_17_Level3_1",
        "RanaCamp_RightSide_17_Level3_1", "RanaCamp_FrontSideCamps_17_Level3_1", "RanaCamp_Front_17_Level3_1",
    ],
    "TownSwamp": [
        "RanaSettlementLarge_Back_1_Level1_1", "RanaSettlementLarge_Base_1_Level1_1",
        "RanaSettlementLarge_Mid_1_Level1_1", "RanaSettlementLarge_Main_1_Level1_1",
        "RanaSettlementLarge_Front_1_Level1_1",
    ],
    "TownDesert": [
        "BaryaSettlementLarge_Base_1_Level1_1", "BaryaSettlementLarge_Mid_1_Level1_1",
        "BaryaSettlementLarge_Default_1_Level1_1", "BaryaSettlementLarge_Front_1_Level1_1",
    ],
    "TownNorth": [
        "VanirSettlementLarge_Level1_Wayback", "VanirSettlementLarge_Level1_Back_Default",
        "VanirSettlementLarge_Level1_Base", "VanirSettlementLarge_Level1_Mid_Default",
        "VanirSettlementLarge_Level1_MidFront_Default", "VanirSettlementLarge_Level1_Front_Default",
    ],
    "Camp1": [
        "RanaCamp_WayBack_1_Level1_1", "RanaCamp_Back_1_Level1_1", "RanaCamp_Base_1_Level1_1",
        "RanaCamp_Mid_1_Level1_1", "RanaCamp_Main_1_Level1_1", "RanaCamp_LeftSide_1_Level1_1",
        "RanaCamp_RightSide_1_Level1_1", "RanaCamp_FrontSideCamps_1_Level1_1", "RanaCamp_Front_1_Level1_1",
    ],
    "Camp2": [
        "RanaCamp_Back_9_Level2_1", "RanaCamp_Mid_9_Level2_1", "RanaCamp_Main_9_Level2_1",
        "RanaCamp_FrontSideCamps_9_Level2_1", "RanaCamp_Front_9_Level2_1",
    ],
    "Camp3": [
        "RanaCamp_Back_25_Level4_1", "RanaCamp_Mid_25_Level4_1", "RanaCamp_Main_25_Level4_1",
        "RanaCamp_LeftSide_25_Level4_1", "RanaCamp_RightSide_25_Level4_1",
        "RanaCamp_FrontSideCamps_25_Level4_1", "RanaCamp_Front_25_Level4_1",
    ],
    "Camp4": [
        "RanaCamp_Back_33_Level5_1", "RanaCamp_Mid_33_Level5_1", "RanaCamp_Main_33_Level5_1",
        "RanaCamp_LeftSide_33_Level5_1", "RanaCamp_RightSide_33_Level5_1",
        "RanaCamp_FrontSideCamps_33_Level5_1", "RanaCamp_Front_33_Level5_1",
    ],
    "Tavern": [
        "ArleonTavern_BackBuilding_1", "ArleonTavern_BackStage_1", "ArleonTavern_Base_1",
        "ArleonTavern_BuildingLevel1_1", "ArleonTavern_FrontBarrels_1",
    ],
    "TangledBurrow": [
        "RootsTangledBurrow_Back", "RootsTangledBurrow_Base", "RootsTangledBurrow_Mid",
        "RootsTangledBurrow_FarFront", "RootsTangledBurrow_Front",
    ],
    "GoldStream": [
        "RootsGoldStream_Back_Default", "RootsGoldStream_Base_Default", "RootsGoldStream_Mid_Default",
    ],
    "Ruins": [
        "RanaRuins_Variant1_Back_1", "RanaRuins_Variant1_Base_1",
        "RanaRuins_Variant1_Main_1", "RanaRuins_Variant1_Front_1",
    ],
    # посёлки: у SoQ три уровня поселений, здесь — «малый» и «большой» по фракциям
    "SettlementSmallTemperate": [
        "RanaSettlementSmall_Back_9_Level2_1", "RanaSettlementSmall_Base_9_Level2_1",
        "RanaSettlementSmall_Mid_9_Level2_1", "RanaSettlementSmall_Default_9_Level2_1",
        "RanaSettlementSmall_Front_9_Level2_1",
    ],
    "SettlementLargeTemperate": [
        "RanaSettlementLarge_Back_17_Level3_1", "RanaSettlementLarge_Base_17_Level3_1",
        "RanaSettlementLarge_Mid_17_Level3_1", "RanaSettlementLarge_Default_17_Level3_1",
        "RanaSettlementLarge_Front_17_Level3_1",
    ],
    "SettlementLargeNorth": [
        "VanirSettlementLarge_Level3_Wayback", "VanirSettlementLarge_Level3_Back_Default",
        "VanirSettlementLarge_Level3_Mid_Default", "VanirSettlementLarge_Level3_MidFront_Default",
        "VanirSettlementLarge_Level3_Front_Default",
    ],
    "SettlementLargeDesert": [
        "BaryaSettlementLarge_Back_17_Level3_1", "BaryaSettlementLarge_Mid_17_Level3_1",
        "BaryaSettlementLarge_Default_17_Level3_1", "BaryaSettlementLarge_Front_17_Level3_1",
    ],
    "SettlementLargeSwamp": [
        "RootsLargeSettlement_Level3_Back_Default", "RootsLargeSettlement_Level3_Base_Pond_Default",
        "RootsLargeSettlement_Level3_Mid_Default", "RootsLargeSettlement_Level3_Thicket_Default",
        "RootsLargeSettlement_Level3_Tree_Default", "RootsLargeSettlement_Level3_Front_Default",
    ],
}

# В какой атлас класть результат
ATLAS_OF = {}
for _n in SINGLES:
    ATLAS_OF[_n] = "soq_objects"
for _n in COMPOSITES:
    ATLAS_OF[_n] = "soq_settlements" if (_n.startswith("Town") or _n.startswith("Camp")) else "soq_nature"
ATLAS_OF["GuardBandit"] = ATLAS_OF["GuardArcher"] = ATLAS_OF["GuardWolf"] = ATLAS_OF["GuardLeader"] = "soq_units"
ATLAS_OF["Portal"] = "soq_units"
ATLAS_OF["StoneOfWinning"] = "soq_objects"
ATLAS_OF["ArcaneSpire"] = "soq_objects"
ATLAS_OF["GreenTown"] = "soq_settlements"
ATLAS_OF["Tavern"] = "soq_settlements"
ATLAS_OF["Ruins"] = "soq_objects"
for _n in COMPOSITES:
    if _n.startswith("Settlement"):
        ATLAS_OF[_n] = "soq_settlements"
ATLAS_OF["TangledBurrow"] = "soq_nature"
ATLAS_OF["GoldStream"] = "soq_nature"

# Готовые нарезки из общих атласов проекта (Resources/Art/atlas_slices.json):
# там лежат постройки SoQ, для которых в дампе нет отдельных Sprite-ассетов.
EXTRA_SLICES = {
    # имя_в_атласе -> (атлас, имя нарезки)
    "StoneOfWinning": ("PermanentMapEntityAtlas", "pyramid"),
    "ArcaneSpire": ("PermanentMapEntityAtlas", "temple"),
    "GreenTown": ("PermanentMapEntityAtlas", "green_town"),
}

ATLAS_SIZE = {
    "soq_objects": (512, 512),
    "soq_settlements": (512, 512),
    "soq_nature": (256, 512),
    "soq_units": (256, 256),
    "soq_decor": (512, 1024),
}

# Декор пустой земли — то, чем SoQ засевает сушу: деревья, кусты, камни,
# пучки травы, сухостой и грядки. Режется из уже нарезанных атласов
# (atlas_slices.json): там лежат готовые rect'ы StandaloneAdventureDecorationAtlas.
#   prefix — префикс имени в атласе проекта, limit — сколько вариантов брать,
#   flat   — рисовать лежащим пятном (камни, трава, грядки), а не стенди.
DECOR_CATS = {
    "trees_big": ("DecorTree", 3, False),
    "trees_pine": ("DecorPine", 4, False),
    "bushes": ("DecorBush", 6, False),
    "rocks": ("DecorRock", 7, True),
    "small": ("DecorTuft", 12, True),
    "dead_trees": ("DecorDead", 3, False),
    "fields": ("DecorCrop", 8, True),
}
# срезы-«сухостой» лежат в misc и берутся по именам — см. DECOR_EXTRA
# Значение — имя нарезки в том же атласе, либо (атлас, имя) для другого атласа.
DECOR_EXTRA = {
    "DecorDeadA": "deco_47",
    "DecorDeadB": "deco_48",
    "DecorDeadC": "deco_49",
    # Горные массивы и плато: блокеры края карты из ArleonAdventureDecorationAtlas —
    # именно ими SoQ обводит горные области и границу карты.
    "DecorMountainA": ("ArleonAdventureDecorationAtlas", "mtn_a"),
    "DecorMountainB": ("ArleonAdventureDecorationAtlas", "mtn_b"),
    "DecorHillFlat": ("ArleonAdventureDecorationAtlas", "hill_a"),
}

RECT_RE = re.compile(
    r"m_Rect:\s*\n\s*serializedVersion: \d+\s*\n\s*x: (-?[\d.]+)\s*\n\s*y: (-?[\d.]+)\s*\n"
    r"\s*width: ([\d.]+)\s*\n\s*height: ([\d.]+)"
)
PIVOT_RE = re.compile(r"m_Pivot: \{x: ([-\d.e]+), y: ([-\d.e]+)\}")
GUID_RE = re.compile(r"texture: \{fileID: \d+, guid: ([a-f0-9]{32})")
PPU_RE = re.compile(r"m_PixelsToUnits: ([\d.]+)")


def guid_of_meta(path):
    with open(path, encoding="utf-8", errors="ignore") as f:
        m = re.search(r"guid: ([a-f0-9]{32})", f.read(400))
    return m.group(1) if m else None


def read_sprite_db(dump):
    """Sprite .asset -> {name: {rect, pivot, ppu, guid}}; плюс guid -> png."""
    sprites = {}
    for path in glob.glob(os.path.join(dump, "Sprite", "*.asset")):
        try:
            with open(path, encoding="utf-8", errors="ignore") as f:
                text = f.read(6000)
        except OSError:
            continue
        m = RECT_RE.search(text)
        if not m:
            continue
        g = GUID_RE.search(text)
        p = PIVOT_RE.search(text)
        pp = PPU_RE.search(text)
        sprites[os.path.basename(path)[:-6]] = {
            "x": float(m.group(1)), "y": float(m.group(2)),
            "w": float(m.group(3)), "h": float(m.group(4)),
            "pivot": (float(p.group(1)), float(p.group(2))) if p else (0.5, 0.0),
            "ppu": float(pp.group(1)) if pp else PPU,
            "guid": g.group(1) if g else None,
        }

    png_by_guid = {}
    for meta in glob.glob(os.path.join(dump, "Texture2D", "*.png.meta")):
        g = guid_of_meta(meta)
        if g:
            png_by_guid[g] = meta[: -len(".meta")]
    return sprites, png_by_guid


class SpriteSource:
    def __init__(self, sprites, png_by_guid):
        self.sprites = sprites
        self.png_by_guid = png_by_guid
        self.images = {}
        self.missing = []

    def image(self, png):
        if png not in self.images:
            self.images[png] = Image.open(png).convert("RGBA")
        return self.images[png]

    def crop(self, name):
        """Возвращает (RGBA-картинка, pivot) или None."""
        d = self.sprites.get(name)
        if not d:
            self.missing.append(name)
            return None
        png = self.png_by_guid.get(d["guid"])
        if not png or not os.path.exists(png):
            self.missing.append(name)
            return None
        im = self.image(png)
        x, y, w, h = int(round(d["x"])), int(round(d["y"])), int(round(d["w"])), int(round(d["h"]))
        top = im.height - y - h
        # допускаем выход за границы атласа на 1-2 пикселя (в дампе бывает)
        left = max(0, x)
        top = max(0, top)
        right = min(im.width, x + w)
        bottom = min(im.height, top + h)
        if right <= left or bottom <= top:
            self.missing.append(name)
            return None
        return im.crop((left, top, right, bottom)), tuple(d["pivot"])

    def compose(self, layers):
        """Собирает слои по общим pivot-якорям (как многослойные постройки SoQ)."""
        parts = []
        for name in layers:
            got = self.crop(name)
            if got:
                parts.append(got)
        if not parts:
            return None
        left = max(px * c.width for c, (px, py) in parts)
        right = max((1 - px) * c.width for c, (px, py) in parts)
        bottom = max(py * c.height for c, (px, py) in parts)
        top = max((1 - py) * c.height for c, (px, py) in parts)
        w = int(round(left + right)) + 3
        h = int(round(top + bottom)) + 3
        canvas = Image.new("RGBA", (max(1, w), max(1, h)), (0, 0, 0, 0))
        ax, ay = int(round(left)) + 1, int(round(h - bottom)) + -1
        for c, (px, py) in parts:
            ox = int(round(ax - px * c.width))
            oy = int(round(ay - (1 - py) * c.height))
            canvas.alpha_composite(c, (ox, oy))
        return canvas


def trim(img):
    """Обрезает полностью прозрачные поля; возвращает (картинка, сдвиг вверх)."""
    bbox = img.getbbox()
    if not bbox:
        return img, 0
    left, top, right, bottom = bbox
    return img.crop(bbox), top


def pack(atlas_name, items):
    """Shelf packing. items: [(name, image)] -> (sheet, placements{name:(x,y,w,h)})."""
    width, height = ATLAS_SIZE[atlas_name]
    # сортируем по высоте, чтобы полки были плотнее
    ordered = sorted(items, key=lambda kv: (-kv[1].height, kv[0]))
    while True:
        sheet = Image.new("RGBA", (width, height), (0, 0, 0, 0))
        placements = {}
        x = y = PAD
        shelf = 0
        ok = True
        for name, img in ordered:
            w, h = img.size
            if w + 2 * PAD > width:
                ok = False
                break
            if x + w + PAD > width:
                x = PAD
                y += shelf + PAD
                shelf = 0
            if y + h + PAD > height:
                ok = False
                break
            sheet.alpha_composite(img, (x, y))
            placements[name] = (x, y, w, h)
            x += w + PAD
            shelf = max(shelf, h)
        if ok:
            return sheet, placements
        width *= 2
        height *= 2
        if width > 2048:
            raise RuntimeError("Не удалось упаковать атлас " + atlas_name)


META_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    # карта рисуется без мип-уровней: спрайты мелкие, мипы только мылят их
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 25
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    # Point: пиксель-арт SoQ не должен размываться билинейной фильтрацией
    filterMode: 0
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 0
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 32
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 0
  flipbookColumns: 0
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings: []
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID:
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def guid_for(name):
    """Стабильный guid из имени (чтобы .meta не менялся между запусками)."""
    return hashlib.md5(("ironthrone-soq:" + name).encode("utf-8")).hexdigest()


def _read_atlas_slices():
    path = os.path.join(OUT_ART, "atlas_slices.json")
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as f:
        return json.load(f).get("sprites", [])


def _crop_atlas(entry):
    """Нарезка из готового атласа проекта: rect уже в координатах снизу-слева."""
    png = os.path.join(OUT_ART, entry["atlas"] + ".png")
    if not os.path.exists(png):
        return None
    img = Image.open(png).convert("RGBA")
    x, y, w, h = int(entry["x"]), int(entry["y"]), int(entry["w"]), int(entry["h"])
    top = img.height - y - h
    return img.crop((x, top, x + w, top + h))


def bake_extra_slices():
    out = {}
    by_key = {(e["atlas"], e["name"]): e for e in _read_atlas_slices()}
    for name, key in EXTRA_SLICES.items():
        entry = by_key.get(key)
        img = _crop_atlas(entry) if entry else None
        if img is not None:
            out[name] = img
    return out


def bake_decor():
    """Деревья/кусты/камни/трава/грядки: по нескольку вариантов на категорию."""
    out = {}
    groups = {}
    by_name = {}
    by_key = {}
    for entry in _read_atlas_slices():
        by_name[entry.get("name")] = entry
        by_key[(entry.get("atlas"), entry.get("name"))] = entry
        cat = entry.get("cat")
        if cat in DECOR_CATS:
            groups.setdefault(cat, []).append(entry)
    for cat, entries in groups.items():
        prefix, limit, _flat = DECOR_CATS[cat]
        # крупные первыми; у small/rocks порядок в атласе и так «по размеру»
        entries.sort(key=lambda e: (-e["h"], e["name"]))
        for i, entry in enumerate(entries[:limit]):
            img = _crop_atlas(entry)
            if img is None:
                continue
            name = "%s%s" % (prefix, "ABCDEFGHIJKLMNOP"[i])
            out[name] = img
            ATLAS_OF[name] = "soq_decor"
    for name, src in DECOR_EXTRA.items():
        entry = by_key.get(src) if isinstance(src, tuple) else by_name.get(src)
        img = _crop_atlas(entry) if entry else None
        if img is not None:
            out[name] = img
            ATLAS_OF[name] = "soq_decor"
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", default=DEFAULT_DUMP)
    ap.add_argument("--force", action="store_true")
    args = ap.parse_args()

    if not os.path.isdir(args.dump):
        raise SystemExit("Нет папки дампа: " + args.dump)
    os.makedirs(OUT_SOQ, exist_ok=True)

    sprites, png_by_guid = read_sprite_db(args.dump)
    src = SpriteSource(sprites, png_by_guid)
    print(f"Спрайтов в дампе: {len(sprites)}, PNG: {len(set(png_by_guid.values()))}")

    # --- одиночные
    baked = {}
    for out_name, source in SINGLES.items():
        if isinstance(source, tuple):
            atlas_name, sprite_name = source
            got = src.crop(sprite_name)
            if got is None:
                # спрайт лежит в другом атласе — пробуем найти по имени
                src.missing.pop() if src.missing else None
            else:
                baked[out_name] = got[0]
            continue
        got = src.crop(source)
        if got:
            baked[out_name] = got[0]

    # --- многослойные
    for out_name, layers in COMPOSITES.items():
        img = src.compose(layers)
        if img is not None:
            baked[out_name] = img

    # --- готовые нарезки общих атласов (постройки без Sprite-ассетов)
    baked.update(bake_extra_slices())

    # --- декор пустой земли
    baked.update(bake_decor())

    if src.missing:
        # часть слоёв может отсутствовать — это не ошибка, печатаем для отчёта
        uniq = sorted(set(src.missing))
        print("Нет в дампе (пропущено):", ", ".join(uniq[:20]))

    # --- обрезка и упаковка по атласам
    slices = []
    atlases = []
    by_atlas = {}
    for name, img in baked.items():
        trimmed, _ = trim(img)
        if trimmed.width < 1 or trimmed.height < 1:
            continue
        by_atlas.setdefault(ATLAS_OF.get(name, "soq_objects"), []).append((name, trimmed))

    for atlas_name, items in sorted(by_atlas.items()):
        sheet, placements = pack(atlas_name, items)
        png_path = os.path.join(OUT_SOQ, atlas_name + ".png")
        if args.force or not os.path.exists(png_path):
            sheet.save(png_path, optimize=True)
        else:
            sheet.save(png_path, optimize=True)  # атлас детерминирован — просто перезаписываем
        meta_path = png_path + ".meta"
        with open(meta_path, "w", encoding="utf-8") as f:
            f.write(META_TEMPLATE.format(guid=guid_for(atlas_name)))
        atlases.append({
            "name": atlas_name,
            "file": "Art/SoQ/" + atlas_name,
            "width": sheet.width,
            "height": sheet.height,
            "pixelsPerUnit": PPU,
        })
        for name, (x, y, w, h) in placements.items():
            slices.append({
                "name": name,
                "atlas": atlas_name,
                # Unity: (x,y) от левого нижнего угла, y = H - (top + h)
                "x": x,
                "y": sheet.height - (y + h),
                "w": w,
                "h": h,
                "pivotX": 0.5,
                "pivotY": 0.0,
                "ppu": PPU,
            })
        print(f"Атлас {atlas_name}: {len(placements)} спрайтов, {sheet.width}x{sheet.height}")

    slices.sort(key=lambda s: s["name"])
    data = {"atlases": atlases, "sprites": slices}
    with open(OUT_SLICES, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")

    # .meta для папки SoQ (Unity сам создаст, но в репозитории пусть будет)
    folder_meta = OUT_SOQ + ".meta"
    if not os.path.exists(folder_meta):
        with open(folder_meta, "w", encoding="utf-8") as f:
            f.write("fileFormatVersion: 2\nguid: " + guid_for("SoQ-folder") +
                    "\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n"
                    "  assetBundleName: \n  assetBundleVariant: \n")

    print("Записано:", OUT_SLICES, "—", len(slices), "спрайтов")
    return 0


if __name__ == "__main__":
    sys.exit(main())
