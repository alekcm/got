using System;
using System.Collections.Generic;
using System.Linq;

namespace IronThrone.Core.Worldgen
{
    /// <summary>Один тайл карты приключений (квадратная сетка, как в Songs of Conquest).</summary>
    public sealed class AdventureTile
    {
        public int x;
        public int y;
        /// <summary>Вода (море). Вода непроходима и не принадлежит регионам.</summary>
        public bool sea;
        /// <summary>Индекс региона в <see cref="AdventureMap.Regions"/>; -1 — ничейная земля.</summary>
        public int region = -1;
        public AdvTerrain terrain = AdvTerrain.Grass;
        /// <summary>Дорога (проходит по региональным связям SoQ).</summary>
        public bool road;
        /// <summary>Уровень высоты 0..3 — задел под поднятия/обрывы.</summary>
        public int elevation;

        public bool Passable => !sea;
    }

    /// <summary>Область карты — аналог региона из layout-файла SoQ.</summary>
    public sealed class AdventureRegion
    {
        public int id;
        /// <summary>Идентификатор региона из layout (region_identifier[0]).</summary>
        public string identifier;
        public string name;
        public AdvBiome biome = AdvBiome.Temperate;
        /// <summary>Игрок-владелец стартовой области (0 — ничья).</summary>
        public int player;
        /// <summary>Группа регионов (SoQ): синхронизирует размер и содержимое.</summary>
        public int group;
        public bool isStart;
        public string primaryBuilding;
        public bool claimed;
        public float difficultyMin;
        public float difficultyMax;
        public int centerX;
        public int centerY;
        /// <summary>Радиус области в тайлах (для расстановки объектов «от центра»).</summary>
        public float radius;

        public readonly List<int> tiles = new List<int>();

        public int Size => tiles.Count;

        public bool Contains(int x, int y)
        {
            int w = _mapWidth;
            return w > 0 && tiles.Contains(y * w + x);
        }

        // ширина карты нужна для быстрой проверки принадлежности тайла
        internal int _mapWidth;

        public IEnumerable<AdventureTile> TileRefs(AdventureMap map)
        {
            for (int i = 0; i < tiles.Count; i++) yield return map.Tiles[tiles[i]];
        }
    }

    /// <summary>Объект на карте: шахта, поселение, жилище, сокровище, страж.</summary>
    public sealed class AdventureObject
    {
        public int id;
        /// <summary>Идентификатор из каталога (GoldMine, TreasureChestLevel1, guard_bandit...).</summary>
        public string defId;
        public string name;
        public string category;
        public int x;
        public int y;
        public int region = -1;
        /// <summary>Игрок-владелец (стартовые объекты), 0 — нейтральный.</summary>
        public int player;
        public bool isTown;
        /// <summary>У объекта есть охрана (вражеский отряд рядом).</summary>
        public bool guarded;
        public string guardTier = "Random";
        public float guardDifficulty;
        /// <summary>Страж стоит на тайле (x,y) — так игрок видит, что объект защищён.</summary>
        public int guardObjectId = -1;
        public bool removed;
        /// <summary>Декор земли (деревья): не объект взаимодействия.</summary>
        public bool decor;

        public string Category => category;
    }

    /// <summary>Стартовая позиция игрока (wielder spawn).</summary>
    public sealed class AdventureStart
    {
        public int player;
        public int x;
        public int y;
        public int region;
    }

    /// <summary>
    /// Готовая карта приключений. Чистые данные без Unity: карту можно
    /// сгенерировать, проверить и отрисовать любым слоем.
    /// </summary>
    public sealed class AdventureMap
    {
        public int Width;
        public int Height;
        public int Seed;
        public string LayoutId;
        public string LayoutName;
        public string SublayoutName;
        public int PlayerCount;

        public AdventureTile[] Tiles;
        public readonly List<AdventureRegion> Regions = new List<AdventureRegion>();
        public readonly List<AdventureObject> Objects = new List<AdventureObject>();
        /// <summary>Декор пустой земли (деревья). Не считается объектами карты.</summary>
        public readonly List<AdventureObject> Decor = new List<AdventureObject>();
        public readonly List<AdventureStart> Starts = new List<AdventureStart>();
        /// <summary>Диагностика генерации: что не поместилось и почему.</summary>
        public readonly List<string> Report = new List<string>();

        public int LandCount;
        public int RoadCount;
        public int GuardedCount;

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public AdventureTile Tile(int x, int y) => InBounds(x, y) ? Tiles[y * Width + x] : null;

        public AdventureObject ObjectAt(int x, int y)
        {
            for (int i = 0; i < Objects.Count; i++)
            {
                var o = Objects[i];
                if (!o.removed && o.x == x && o.y == y) return o;
            }
            return null;
        }

        public AdventureRegion RegionAt(int x, int y)
        {
            var t = Tile(x, y);
            if (t == null || t.region < 0 || t.region >= Regions.Count) return null;
            return Regions[t.region];
        }

        public IEnumerable<AdventureTile> AllTiles()
        {
            for (int i = 0; i < Tiles.Length; i++) yield return Tiles[i];
        }

        public IEnumerable<AdventureTile> LandTiles()
        {
            for (int i = 0; i < Tiles.Length; i++)
                if (!Tiles[i].sea) yield return Tiles[i];
        }

        public int CountObjects(string category)
        {
            int n = 0;
            for (int i = 0; i < Objects.Count; i++)
                if (!Objects[i].removed && Objects[i].category == category) n++;
            return n;
        }

        /// <summary>Название стартовой области игрока — для подписи на экране.</summary>
        public string RegionNameOf(int x, int y)
        {
            var r = RegionAt(x, y);
            return r != null ? r.name : "Море";
        }

        /// <summary>Соседи тайла (4 направления — карта SoQ квадратная).</summary>
        public static readonly int[] Dx = { 1, -1, 0, 0 };
        public static readonly int[] Dy = { 0, 0, 1, -1 };

        public IEnumerable<AdventureTile> Neighbors4(int x, int y)
        {
            for (int i = 0; i < 4; i++)
            {
                var t = Tile(x + Dx[i], y + Dy[i]);
                if (t != null) yield return t;
            }
        }

        public IEnumerable<AdventureTile> Neighbors8(int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var t = Tile(x + dx, y + dy);
                    if (t != null) yield return t;
                }
        }

        /// <summary>Краткая сводка для UI/логов.</summary>
        public string Summary()
        {
            var byCat = new Dictionary<string, int>();
            foreach (var o in Objects)
            {
                if (o.removed) continue;
                byCat.TryGetValue(o.category, out var n);
                byCat[o.category] = n + 1;
            }
            var parts = byCat.OrderByDescending(kv => kv.Value)
                             .Select(kv => kv.Key + " " + kv.Value)
                             .ToList();
            return $"Карта {Width}×{Height}, сид {Seed}\n" +
                   $"layout: {LayoutName}" + (string.IsNullOrEmpty(SublayoutName) ? "" : " / " + SublayoutName) + "\n" +
                   $"земли: {LandCount} тайлов, областей: {Regions.Count}, объектов: {Objects.Count} (охраняемых {GuardedCount}), декора: {Decor.Count}\n" +
                   $"дорог: {RoadCount} тайлов\n" +
                   string.Join(", ", parts);
        }
    }
}
