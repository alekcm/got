using System;
using System.Collections.Generic;

namespace IronThrone.Core.Worldgen
{
    /// <summary>Параметры генерации карты приключений.</summary>
    public sealed class AdventureGenRequest
    {
        /// <summary>Ключ layout'а (id/name), null — первый доступный.</summary>
        public string LayoutKey;
        /// <summary>Индекс саблayout'а; -1 — выбрать случайно по сиду.</summary>
        public int SublayoutIndex = -1;
        public int Seed = 1;
        /// <summary>Сторона карты в тайлах; 0 — из layout'а.</summary>
        public int Size;
        /// <summary>Число игроков; 0 — из layout'а.</summary>
        public int PlayerCount;
        /// <summary>Доля суши (0.55..0.9).</summary>
        public float LandFraction = 0.70f;
        /// <summary>Ставить ли воду по краю карты (как в SoQ).</summary>
        public bool WaterBorder = true;
    }

    /// <summary>
    /// Генератор карт «как в Songs of Conquest».
    ///
    /// Пайплайн повторяет порядок SoQ RMG:
    /// 1) маска суши (континент из блобов + фрактальный шум, сглаживание, срез мелких островов);
    /// 2) регионы из узлов layout'а: seed-тайл в точке узла, рост до «fill» доли суши,
    ///    дочерние регионы от границы родителя, синхронизация групп;
    /// 3) связи регионов: A* дорога, при необходимости — насыпь через воду;
    /// 4) рельеф по биому региона с шумовыми пятнами и прибрежными песками;
    /// 5) главные здания и стартовые города;
    /// 6) содержимое регионов по blueprint'у (итерации/количество/удаление от центра/
    ///    ограничения места/дети объектов);
    /// 7) охрана объектов и проходов (отряды-стражи на соседних тайлах);
    /// 8) заполнение пустых тайлов мелкими находками и придорожными объектами.
    ///
    /// Всё случайное берётся из <see cref="SoqRng"/> по сиду: одинаковый сид —
    /// одинаковая карта, другой сид — другая карта.
    /// </summary>
    public static class SoqMapGenerator
    {
        public static AdventureMap Generate(SoqRmgPack pack, AdventureGenRequest request)
        {
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            return new Generator(pack, request ?? new AdventureGenRequest()).Run();
        }

        /// <summary>Удобная перегрузка: сгенерировать по ключу layout'а и сиду.</summary>
        public static AdventureMap Generate(SoqRmgPack pack, string layoutKey, int seed, int size = 0)
        {
            return Generate(pack, new AdventureGenRequest { LayoutKey = layoutKey, Seed = seed, Size = size });
        }

        // ==================================================================
        private sealed class Generator
        {
            private readonly SoqRmgPack _pack;
            private readonly AdventureGenRequest _req;
            private readonly AdventureMap _map = new AdventureMap();
            private readonly AdventureCatalog _catalog;

            private SoqRng _rng;
            private SoqRng.Noise _noise;
            private int _size;
            private SoqLayout _layout;
            private SoqSublayout _sub;
            private SoqBlueprint _blueprint;

            private readonly Dictionary<int, SoqNode> _nodeByRegion = new Dictionary<int, SoqNode>();
            private readonly Dictionary<int, SoqNode> _nodeById = new Dictionary<int, SoqNode>();
            private readonly Dictionary<string, int> _placedByDef = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<int> _occupied = new HashSet<int>();
            private int _landCount;
            /// <summary>Множитель квот: Σfill обычно меньше 1, и без него половина суши
            /// доставалась «ближайшему» региону, раздувая случайные области.</summary>
            private float _quotaScale = 1f;
            private int _nextObjectId = 1;

            public Generator(SoqRmgPack pack, AdventureGenRequest req)
            {
                _pack = pack;
                _req = req;
                _catalog = pack.Catalog;
            }

            public AdventureMap Run()
            {
                _layout = _pack.Layout(_req.LayoutKey);
                if (_layout == null)
                {
                    _map.Report.Add("Нет ни одного layout'а в данных генератора.");
                    return _map;
                }

                int seed = _req.Seed;
                _rng = new SoqRng(seed);
                _noise = new SoqRng.Noise(new SoqRng(unchecked(seed * 486187739 + 7919)));
                _blueprint = _pack.Blueprint(_layout.blueprint);
                _size = _req.Size > 0 ? _req.Size : _layout.size;
                if (_size < 16) _size = 16;
                if (_size > 128) _size = 128;

                _sub = PickSublayout();
                _map.Seed = seed;
                _map.Width = _size;
                _map.Height = _size;
                _map.LayoutId = _layout.Key;
                _map.LayoutName = _layout.DisplayName;
                _map.SublayoutName = _sub != null ? _sub.DisplayName : "";
                _map.PlayerCount = _req.PlayerCount > 0 ? _req.PlayerCount : _layout.playerCount;
                _map.Tiles = new AdventureTile[_size * _size];
                for (int y = 0; y < _size; y++)
                    for (int x = 0; x < _size; x++)
                        _map.Tiles[y * _size + x] = new AdventureTile { x = x, y = y, sea = true };

                if (_sub == null || _sub.nodes.Count == 0)
                {
                    _map.Report.Add("В layout'е нет узлов — карта останется пустой.");
                    return _map;
                }
                foreach (var node in _sub.nodes) IndexNode(node, null);

                BuildLand();
                ComputeQuotaScale();
                BuildRegions();
                CarveConnections();
                AssignTerrain();
                PlacePrimaryBuildings();
                PlaceBlueprintObjects();
                FillEmptyTiles();
                ScatterDecor();
                Validate();

                if (_pack.Warnings.Count > 0)
                    _map.Report.Add("Предупреждения данных: " + _pack.Warnings.Count);
                return _map;
            }

            // ---------------------------------------------------------- данные
            private SoqSublayout PickSublayout()
            {
                if (_layout.layouts.Count == 0) return null;
                int index = _req.SublayoutIndex >= 0 && _req.SublayoutIndex < _layout.layouts.Count
                    ? _req.SublayoutIndex
                    : _rng.Next(_layout.layouts.Count);
                return _layout.layouts[index];
            }

            private void IndexNode(SoqNode node, SoqNode parent)
            {
                node.Parent = parent;
                if (node.id != 0) _nodeById[node.id] = node;
                foreach (var child in node.children) IndexNode(child, node);
            }

            private void Warn(string key, string text)
            {
                if (!_warned.Add(key)) return;
                _map.Report.Add(text);
            }

            // ---------------------------------------------------------- 1. суша
            private void BuildLand()
            {
                int size = _size;
                var field = new float[size * size];
                int blobs = _rng.Next(2, 4);
                var centers = new List<(float x, float y, float rx, float ry)>();
                for (int i = 0; i < blobs; i++)
                {
                    float cx = _rng.Range(0.34f, 0.66f) + (i == 1 ? _rng.Range(-0.12f, 0.12f) : 0f);
                    float cy = _rng.Range(0.34f, 0.66f) + (i == 2 ? _rng.Range(-0.12f, 0.12f) : 0f);
                    float rx = _rng.Range(0.30f, 0.46f);
                    float ry = _rng.Range(0.30f, 0.46f);
                    centers.Add((cx, cy, rx, ry));
                }

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float nx = (x + 0.5f) / size;
                        float ny = (y + 0.5f) / size;
                        float continent = 0f;
                        for (int i = 0; i < centers.Count; i++)
                        {
                            float dx = (nx - centers[i].x) / centers[i].rx;
                            float dy = (ny - centers[i].y) / centers[i].ry;
                            float v = 1f - (dx * dx + dy * dy);
                            if (v > continent) continent = v;
                        }
                        float n = _noise.Fbm(x * 0.055f, y * 0.055f, 4, 0.52f);
                        // край карты всегда ниже — карта получает естественную береговую линию
                        float edge = Math.Min(Math.Min(nx, 1f - nx), Math.Min(ny, 1f - ny));
                        float edgeFalloff = Math.Min(1f, edge / 0.16f);
                        field[y * size + x] = continent * 0.82f + n * 0.42f - (1f - edgeFalloff) * 0.55f;
                    }
                }

                // порог по доле суши
                int target = (int)(size * size * Clamp(_req.LandFraction, 0.5f, 0.9f));
                var sorted = new float[field.Length];
                Array.Copy(field, sorted, field.Length);
                Array.Sort(sorted);
                float threshold = sorted[Math.Max(0, Math.Min(sorted.Length - 1, sorted.Length - 1 - target))];

                var land = new bool[size * size];
                for (int i = 0; i < land.Length; i++) land[i] = field[i] >= threshold;

                if (_req.WaterBorder)
                {
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                            if (x == 0 || y == 0 || x == size - 1 || y == size - 1) land[y * size + x] = false;
                }

                // сглаживание: убираем одиночные тайлы и «дыры»
                Smooth(land, size, 2);

                // срезаем мелкие острова, оставляя только крупные
                RemoveSmallComponents(land, size, Math.Max(24, (int)(size * size * 0.012f)));

                _landCount = 0;
                for (int i = 0; i < land.Length; i++)
                    if (land[i]) _landCount++;

                _map.LandCount = _landCount;
                for (int i = 0; i < land.Length; i++) _map.Tiles[i].sea = !land[i];
                if (_landCount < size * size * 0.25f)
                    Warn("land", "Суши мало (" + _landCount + " тайлов) — проверьте LandFraction.");
            }

            private static void Smooth(bool[] land, int size, int passes)
            {
                for (int pass = 0; pass < passes; pass++)
                {
                    var copy = (bool[])land.Clone();
                    for (int y = 1; y < size - 1; y++)
                        for (int x = 1; x < size - 1; x++)
                        {
                            int n = 0;
                            for (int dy = -1; dy <= 1; dy++)
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    if (dx == 0 && dy == 0) continue;
                                    if (copy[(y + dy) * size + (x + dx)]) n++;
                                }
                            if (n >= 5) land[y * size + x] = true;
                            else if (n <= 2) land[y * size + x] = false;
                        }
                }
            }

            private static void RemoveSmallComponents(bool[] land, int size, int minSize)
            {
                var seen = new bool[land.Length];
                var stack = new Stack<int>();
                var component = new List<int>();
                for (int start = 0; start < land.Length; start++)
                {
                    if (!land[start] || seen[start]) continue;
                    component.Clear();
                    stack.Clear();
                    stack.Push(start);
                    seen[start] = true;
                    while (stack.Count > 0)
                    {
                        int cur = stack.Pop();
                        component.Add(cur);
                        int cx = cur % size, cy = cur / size;
                        for (int d = 0; d < 4; d++)
                        {
                            int nx = cx + AdventureMap.Dx[d], ny = cy + AdventureMap.Dy[d];
                            if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
                            int ni = ny * size + nx;
                            if (land[ni] && !seen[ni]) { seen[ni] = true; stack.Push(ni); }
                        }
                    }
                    if (component.Count < minSize)
                        foreach (int i in component) land[i] = false;
                }
            }

            // ---------------------------------------------------------- 2. регионы
            private sealed class Growth
            {
                public AdventureRegion region;
                public int quota;
                public int group;
                public readonly List<int> frontier = new List<int>();
            }

            private void BuildRegions()
            {
                _map.Regions.Clear();
                _nodeByRegion.Clear();
                var roots = new List<Growth>();
                var groups = new Dictionary<int, int>();

                // синхронизация групп: квота группы = максимум fill среди участников
                foreach (var node in _sub.nodes)
                {
                    if (node.group == 0) continue;
                    int quota = QuotaFor(node);
                    if (!groups.TryGetValue(node.group, out var cur) || quota > cur) groups[node.group] = quota;
                }

                foreach (var node in _sub.nodes)
                {
                    if (node.spawnChance < 1f && !_rng.Chance(node.spawnChance))
                    {
                        Warn("spawn_" + node.id, "Регион #" + node.id + " не появился (spawnChance).");
                        continue;
                    }

                    int quota = node.group != 0 && groups.TryGetValue(node.group, out var gq) ? gq : QuotaFor(node);
                    var region = new AdventureRegion
                    {
                        id = _map.Regions.Count,
                        identifier = node.Identifier,
                        name = string.IsNullOrEmpty(node.displayName) ? node.Identifier : node.displayName,
                        biome = !string.IsNullOrEmpty(node.biome) ? AdvTerrainInfo.BiomeByName(node.biome) : AdvTerrainInfo.BiomeByName(node.Identifier),
                        player = node.player,
                        group = node.group,
                        isStart = node.player > 0,
                        primaryBuilding = node.PrimaryBuilding,
                        claimed = node.isClaimed,
                        difficultyMin = node.difficultyMin,
                        difficultyMax = node.difficultyMax,
                        _mapWidth = _size,
                    };
                    SeedAndAttach(region, node);
                    if (region.tiles.Count == 0)
                    {
                        Warn("noseed_" + node.id, "Не нашлось места под регион #" + node.id + " (" + node.Identifier + ").");
                        continue;
                    }
                    _map.Regions.Add(region);
                    _nodeByRegion[region.id] = node;
                    var growth = new Growth { region = region, quota = Math.Max(quota, region.tiles.Count), group = node.group };
                    SeedFrontier(growth);
                    roots.Add(growth);
                }

                GrowSet(roots);

                // дочерние регионы: рождаются у границы родителя, когда он вырос достаточно
                var children = new List<Growth>();
                foreach (var growth in roots.ToArray())
                {
                    var node = _nodeByRegion[growth.region.id];
                    CollectChildren(node, growth.region, children, 0);
                }
                GrowSet(children);

                AssignLeftoverLand();

                foreach (var region in _map.Regions)
                {
                    if (region.tiles.Count == 0)
                        Warn("empty_" + region.id, "Регион " + region.name + " остался без земли.");
                    ComputeCenter(region);
                }
            }

            /// <summary>
            /// Нормировка «fill» на всю сушу: в SoQ доли регионов покрывают карту,
            /// а не оставляют половину земли на произвол BFS-остатка.
            /// </summary>
            private void ComputeQuotaScale()
            {
                float total = 0f;
                foreach (var node in _sub.nodes) total += SumFill(node);
                if (total <= 0.0001f || _landCount <= 0)
                {
                    _quotaScale = 1f;
                    return;
                }
                _quotaScale = Clamp(1f / total, 1f, 4f);
            }

            private static float SumFill(SoqNode node)
            {
                float sum = node.fill > 0.0001f ? node.fill : 0f;
                foreach (var child in node.children) sum += SumFill(child);
                return sum;
            }

            private int QuotaFor(SoqNode node)
            {
                if (node.fill <= 0.0001f) return _rng.Next(4, 11);
                int quota = (int)Math.Round(node.fill * _landCount * _quotaScale);
                return Math.Max(4, quota);
            }

            private void CollectChildren(SoqNode node, AdventureRegion parentRegion, List<Growth> output, int depth)
            {
                if (depth > 4) return;
                foreach (var child in node.children)
                {
                    if (child.spawnChance < 1f && !_rng.Chance(child.spawnChance))
                        continue;

                    // группа детей синхронизируется по размеру
                    int quota = QuotaFor(child);
                    if (child.group != 0)
                    {
                        int best = quota;
                        foreach (var sibling in node.children)
                            if (sibling.group == child.group) best = Math.Max(best, QuotaFor(sibling));
                        quota = best;
                    }

                    int seedTile = FindChildSeed(parentRegion, child);
                    if (seedTile < 0)
                    {
                        Warn("child_" + child.id, "Дочерний регион #" + child.id + " не поместился рядом с " + parentRegion.name + ".");
                        continue;
                    }

                    var region = new AdventureRegion
                    {
                        id = _map.Regions.Count,
                        identifier = child.Identifier,
                        name = string.IsNullOrEmpty(child.displayName) ? child.Identifier : child.displayName,
                        biome = !string.IsNullOrEmpty(child.biome) ? AdvTerrainInfo.BiomeByName(child.biome) : AdvTerrainInfo.BiomeByName(child.Identifier),
                        player = child.player,
                        group = child.group,
                        isStart = child.player > 0,
                        primaryBuilding = child.PrimaryBuilding,
                        claimed = child.isClaimed,
                        difficultyMin = child.difficultyMin,
                        difficultyMax = child.difficultyMax,
                        _mapWidth = _size,
                    };
                    Claim(region, seedTile);
                    _map.Regions.Add(region);
                    _nodeByRegion[region.id] = child;
                    var growth = new Growth { region = region, quota = Math.Max(quota, 1), group = child.group };
                    SeedFrontier(growth);
                    output.Add(growth);

                    if (child.road > 0 && child.Parent != null)
                        ConnectRegions(parentRegion, region, child.road, 0f, "Random", true);

                    CollectChildren(child, region, output, depth + 1);
                }
            }

            private void SeedAndAttach(AdventureRegion region, SoqNode node)
            {
                int tile = FindSeedTile(node.point, region.player);
                if (tile < 0) return;
                Claim(region, tile);
            }

            private readonly List<int> _seedTiles = new List<int>();

            private int FindSeedTile(SoqPoint point, int player)
            {
                int size = _size;
                float tx = point.HasRange ? _rng.Range(point.XMin, point.XMax) : 0.5f;
                float ty = point.HasRange ? _rng.Range(point.YMin, point.YMax) : 0.5f;
                int cx = Math.Max(1, Math.Min(size - 2, (int)(tx * size)));
                int cy = Math.Max(1, Math.Min(size - 2, (int)(ty * size)));

                int minDist = Math.Max(3, size / 8);
                int best = -1, bestScore = int.MaxValue;
                for (int radius = 0; radius < size && best < 0; radius++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            if (Math.Abs(dx) != radius && Math.Abs(dy) != radius) continue;
                            int x = cx + dx, y = cy + dy;
                            int index = y * size + x;
                            if (!_map.InBounds(x, y) || _map.Tiles[index].sea || _map.Tiles[index].region >= 0) continue;
                            int dist = int.MaxValue;
                            for (int s = 0; s < _seedTiles.Count; s++)
                            {
                                int sx = _seedTiles[s] % size, sy = _seedTiles[s] / size;
                                dist = Math.Min(dist, Math.Abs(sx - x) + Math.Abs(sy - y));
                            }
                            if (dist < minDist) continue;
                            int score = Math.Abs(dx) + Math.Abs(dy) + _rng.Next(3);
                            if (score < bestScore) { bestScore = score; best = index; }
                        }
                }
                if (best >= 0) _seedTiles.Add(best);
                return best;
            }

            private int FindChildSeed(AdventureRegion parent, SoqNode child)
            {
                int size = _size;
                var candidates = new List<int>();
                for (int i = 0; i < parent.tiles.Count; i++)
                {
                    int idx = parent.tiles[i];
                    int x = idx % size, y = idx / size;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + AdventureMap.Dx[d], ny = y + AdventureMap.Dy[d];
                        if (!_map.InBounds(nx, ny)) continue;
                        int ni = ny * size + nx;
                        if (_map.Tiles[ni].sea || _map.Tiles[ni].region >= 0) continue;
                        candidates.Add(ni);
                    }
                }
                if (candidates.Count == 0) return -1;
                return candidates[_rng.Next(candidates.Count)];
            }

            private void Claim(AdventureRegion region, int tileIndex)
            {
                int x = tileIndex % _size, y = tileIndex / _size;
                region.tiles.Add(tileIndex);
                _map.Tiles[tileIndex].region = region.id;
                region.centerX = x;
                region.centerY = y;
            }

            private void SeedFrontier(Growth growth)
            {
                int size = _size;
                foreach (int idx in growth.region.tiles)
                {
                    int x = idx % size, y = idx / size;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + AdventureMap.Dx[d], ny = y + AdventureMap.Dy[d];
                        if (!_map.InBounds(nx, ny)) continue;
                        int ni = ny * size + nx;
                        if (_map.Tiles[ni].sea || _map.Tiles[ni].region >= 0) continue;
                        if (!growth.frontier.Contains(ni)) growth.frontier.Add(ni);
                    }
                }
            }

            private void GrowSet(List<Growth> set)
            {
                if (set.Count == 0) return;
                int guard = _size * _size * 8;
                var candidates = new List<Growth>();
                while (guard-- > 0)
                {
                    candidates.Clear();
                    int bestDeficit = 0;
                    for (int i = 0; i < set.Count; i++)
                    {
                        var g = set[i];
                        int deficit = g.quota - g.region.tiles.Count;
                        if (deficit <= 0 || g.frontier.Count == 0) continue;
                        if (deficit > bestDeficit)
                        {
                            bestDeficit = deficit;
                            candidates.Clear();
                            candidates.Add(g);
                        }
                        else if (deficit == bestDeficit) candidates.Add(g);
                    }
                    if (candidates.Count == 0) break;

                    var chosen = candidates[_rng.Next(candidates.Count)];
                    if (chosen.group != 0)
                    {
                        // регионы одной группы растут «ноздря в ноздрю»
                        for (int i = 0; i < set.Count; i++)
                            if (set[i].group == chosen.group) GrowOne(set[i]);
                    }
                    else GrowOne(chosen);
                }
            }

            private void GrowOne(Growth growth)
            {
                int size = _size;
                int attempts = 0;
                while (growth.frontier.Count > 0 && attempts++ < 64)
                {
                    int pick = _rng.Next(growth.frontier.Count);
                    int idx = growth.frontier[pick];
                    growth.frontier[pick] = growth.frontier[growth.frontier.Count - 1];
                    growth.frontier.RemoveAt(growth.frontier.Count - 1);
                    if (_map.Tiles[idx].sea || _map.Tiles[idx].region >= 0) continue;

                    growth.region.tiles.Add(idx);
                    _map.Tiles[idx].region = growth.region.id;
                    if (growth.region.tiles.Count >= growth.quota) return;

                    int x = idx % size, y = idx / size;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + AdventureMap.Dx[d], ny = y + AdventureMap.Dy[d];
                        if (!_map.InBounds(nx, ny)) continue;
                        int ni = ny * size + nx;
                        if (_map.Tiles[ni].sea || _map.Tiles[ni].region >= 0) continue;
                        if (!growth.frontier.Contains(ni)) growth.frontier.Add(ni);
                    }
                    return;
                }
            }

            /// <summary>Вся свободная суша достаётся ближайшему региону (в SoQ суша без региона невозможна).</summary>
            private void AssignLeftoverLand()
            {
                if (_map.Regions.Count == 0) return;
                int size = _size;
                var queue = new Queue<int>();
                var owner = new int[size * size];
                for (int i = 0; i < owner.Length; i++) owner[i] = -2;

                foreach (var region in _map.Regions)
                    foreach (int idx in region.tiles)
                    {
                        owner[idx] = region.id;
                        queue.Enqueue(idx);
                    }

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    int x = cur % size, y = cur / size;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + AdventureMap.Dx[d], ny = y + AdventureMap.Dy[d];
                        if (!_map.InBounds(nx, ny)) continue;
                        int ni = ny * size + nx;
                        if (_map.Tiles[ni].sea || owner[ni] != -2) continue;
                        owner[ni] = owner[cur];
                        _map.Tiles[ni].region = owner[cur];
                        _map.Regions[owner[cur]].tiles.Add(ni);
                        queue.Enqueue(ni);
                    }
                }

                // острова, не связанные с материком: отдаём ближайшему региону
                for (int i = 0; i < owner.Length; i++)
                {
                    if (_map.Tiles[i].sea || owner[i] != -2) continue;
                    int x = i % size, y = i / size;
                    int best = 0;
                    float bestDist = float.MaxValue;
                    for (int r = 0; r < _map.Regions.Count; r++)
                    {
                        var region = _map.Regions[r];
                        float dx = region.centerX - x, dy = region.centerY - y;
                        float dist = dx * dx + dy * dy;
                        if (dist < bestDist) { bestDist = dist; best = r; }
                    }
                    owner[i] = best;
                    _map.Tiles[i].region = best;
                    _map.Regions[best].tiles.Add(i);
                    Warn("island", "Островная суша приписана региону " + _map.Regions[best].name + ".");
                }
            }

            private void ComputeCenter(AdventureRegion region)
            {
                if (region.tiles.Count == 0) return;
                int size = _size;
                float sx = 0, sy = 0;
                foreach (int idx in region.tiles) { sx += idx % size; sy += idx / size; }
                sx /= region.tiles.Count;
                sy /= region.tiles.Count;

                int best = region.tiles[0];
                float bestDist = float.MaxValue;
                float radius = 1f;
                foreach (int idx in region.tiles)
                {
                    float dx = idx % size - sx, dy = idx / size - sy;
                    float dist = dx * dx + dy * dy;
                    if (dist < bestDist) { bestDist = dist; best = idx; }
                    radius = Math.Max(radius, (float)Math.Sqrt(dist));
                }
                region.centerX = best % size;
                region.centerY = best / size;
                region.radius = radius;
            }

            // ---------------------------------------------------------- 3. связи
            private void CarveConnections()
            {
                if (_sub == null) return;
                foreach (var conn in _sub.connections)
                {
                    if (!_nodeById.TryGetValue(conn.nodeFrom, out var fromNode) ||
                        !_nodeById.TryGetValue(conn.nodeTo, out var toNode))
                    {
                        Warn("conn_" + conn.nodeFrom + "_" + conn.nodeTo,
                            "Связь #" + conn.nodeFrom + "→" + conn.nodeTo + ": узел не найден.");
                        continue;
                    }
                    var fromRegion = RegionOfNode(fromNode);
                    var toRegion = RegionOfNode(toNode);
                    if (fromRegion == null || toRegion == null)
                    {
                        Warn("connr_" + conn.nodeFrom + "_" + conn.nodeTo,
                            "Связь #" + conn.nodeFrom + "→" + conn.nodeTo + ": регион не создан.");
                        continue;
                    }
                    ConnectRegions(fromRegion, toRegion, conn.road, conn.guardChance, conn.guardTier, false,
                        conn.hasDifficulty ? conn.difficultyMin : 0f,
                        conn.hasDifficulty ? conn.difficultyMax : 0f);
                }
                _map.RoadCount = 0;
                foreach (var t in _map.Tiles) if (t.road) _map.RoadCount++;
            }

            private AdventureRegion RegionOfNode(SoqNode node)
            {
                foreach (var kv in _nodeByRegion) if (kv.Value == node) return _map.Regions[kv.Key];
                return null;
            }

            private void ConnectRegions(AdventureRegion a, AdventureRegion b, int roadType,
                float guardChance, string guardTier, bool isChildLink,
                float diffMin = 0f, float diffMax = 0f)
            {
                var path = FindPath(a, b);
                if (path == null || path.Count == 0)
                {
                    Warn("nopath_" + a.id + "_" + b.id, "Не проложен путь " + a.name + " → " + b.name + ".");
                    return;
                }

                int half = path.Count / 2;
                for (int i = 0; i < path.Count; i++)
                {
                    int idx = path[i];
                    var tile = _map.Tiles[idx];
                    if (tile.sea)
                    {
                        // насыпь через воду: в SoQ дороги по воде не ходят, но карта должна быть связной
                        tile.sea = false;
                        tile.terrain = AdvTerrain.Dirt;
                        _map.LandCount++;
                        _landCount++;
                    }
                    if (tile.region < 0)
                    {
                        int owner = i <= half ? a.id : b.id;
                        tile.region = owner;
                        _map.Regions[owner].tiles.Add(idx);
                    }
                    if (roadType > 0) tile.road = true;
                }

                // регионы должны граничить: если между ними кто-то вклинился, пробиваем узкий проход
                EnsureAdjacent(a, b);

                if (guardChance > 0 && _rng.Chance(guardChance))
                {
                    int mid = path[path.Count / 2];
                    var midTile = _map.Tiles[mid];
                    if (!_occupiedAt(midTile.x, midTile.y))
                        PlaceGuard(midTile.x, midTile.y, guardTier, (diffMin + diffMax) * 0.5f, null);
                }
            }

            private void EnsureAdjacent(AdventureRegion a, AdventureRegion b)
            {
                if (a.id == b.id) return;
                int size = _size;
                foreach (int idx in a.tiles)
                {
                    int x = idx % size, y = idx / size;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + AdventureMap.Dx[d], ny = y + AdventureMap.Dy[d];
                        if (!_map.InBounds(nx, ny)) continue;
                        if (_map.Tiles[ny * size + nx].region == b.id) return;
                    }
                }
                // не граничат — ищем тайл между ними и отдаём его регионам
                int bestIdx = -1;
                float bestDist = float.MaxValue;
                for (int i = 0; i < _map.Tiles.Length; i++)
                {
                    var t = _map.Tiles[i];
                    if (t.sea) continue;
                    if (t.region == a.id || t.region == b.id) continue;
                    int da = DistanceToRegion(t.x, t.y, a), db = DistanceToRegion(t.x, t.y, b);
                    if (da != 1 || db != 1) continue;
                    float d = (t.x - a.centerX) * (t.x - a.centerX) + (t.y - a.centerY) * (t.y - a.centerY);
                    if (d < bestDist) { bestDist = d; bestIdx = i; }
                }
                if (bestIdx >= 0)
                {
                    var t = _map.Tiles[bestIdx];
                    if (t.region >= 0) _map.Regions[t.region].tiles.Remove(bestIdx);
                    t.region = a.id;
                    a.tiles.Add(bestIdx);
                    if (!t.road) t.road = true;
                    Warn("adj_" + a.id + "_" + b.id, "Проход между " + a.name + " и " + b.name + " расширен вручную.");
                }
            }

            private int DistanceToRegion(int x, int y, AdventureRegion region)
            {
                if (region.Contains(x, y)) return 0;
                int best = int.MaxValue;
                foreach (int idx in region.tiles)
                {
                    int rx = idx % _size, ry = idx / _size;
                    int d = Math.Abs(rx - x) + Math.Abs(ry - y);
                    if (d < best) best = d;
                }
                return best;
            }

            /// <summary>Поиск пути Дейкстрой между регионами (дорога / проход).</summary>
            private List<int> FindPath(AdventureRegion a, AdventureRegion b)
            {
                int size = _size;
                int n = size * size;
                var dist = new float[n];
                var prev = new int[n];
                var visited = new bool[n];
                for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prev[i] = -1; }

                var heap = new IntHeap(n * 4);
                foreach (int idx in a.tiles)
                {
                    dist[idx] = 0f;
                    heap.Push(idx, 0f);
                }

                int goal = -1;
                while (heap.Count > 0)
                {
                    int cur = heap.Pop();
                    if (visited[cur]) continue;
                    visited[cur] = true;
                    if (_map.Tiles[cur].region == b.id) { goal = cur; break; }

                    int x = cur % size, y = cur / size;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + AdventureMap.Dx[d], ny = y + AdventureMap.Dy[d];
                        if (!_map.InBounds(nx, ny)) continue;
                        int ni = ny * size + nx;
                        if (visited[ni]) continue;
                        float step = _map.Tiles[ni].sea ? 14f : 1f;
                        if (!_map.Tiles[ni].sea && _map.Tiles[ni].region != a.id && _map.Tiles[ni].region != b.id)
                            step += 2.5f;
                        if (!_map.Tiles[ni].sea && AdvTerrainInfo.IsMountain(_map.Tiles[ni].terrain)) step += 1.5f;
                        float nd = dist[cur] + step;
                        if (nd < dist[ni] - 0.0001f)
                        {
                            dist[ni] = nd;
                            prev[ni] = cur;
                            heap.Push(ni, nd);
                        }
                    }
                }

                if (goal < 0) return null;
                var path = new List<int>();
                for (int cur = goal; cur >= 0; cur = prev[cur])
                {
                    path.Add(cur);
                    if (dist[cur] <= 0f) break;
                }
                path.Reverse();
                return path;
            }

            /// <summary>Минимальная двоичная куча для Дейкстры (без внешних зависимостей).</summary>
            private sealed class IntHeap
            {
                private int[] _items;
                private float[] _keys;
                private int _count;

                public IntHeap(int capacity)
                {
                    _items = new int[Math.Max(4, capacity)];
                    _keys = new float[Math.Max(4, capacity)];
                }

                public int Count => _count;

                public void Push(int item, float key)
                {
                    if (_count == _items.Length)
                    {
                        Array.Resize(ref _items, _items.Length * 2);
                        Array.Resize(ref _keys, _keys.Length * 2);
                    }
                    _items[_count] = item;
                    _keys[_count] = key;
                    int i = _count++;
                    while (i > 0)
                    {
                        int parent = (i - 1) / 2;
                        if (_keys[parent] <= _keys[i]) break;
                        Swap(i, parent);
                        i = parent;
                    }
                }

                public int Pop()
                {
                    int result = _items[0];
                    _count--;
                    if (_count > 0)
                    {
                        _items[0] = _items[_count];
                        _keys[0] = _keys[_count];
                        int i = 0;
                        while (true)
                        {
                            int left = i * 2 + 1, right = left + 1, smallest = i;
                            if (left < _count && _keys[left] < _keys[smallest]) smallest = left;
                            if (right < _count && _keys[right] < _keys[smallest]) smallest = right;
                            if (smallest == i) break;
                            Swap(i, smallest);
                            i = smallest;
                        }
                    }
                    return result;
                }

                private void Swap(int a, int b)
                {
                    int ti = _items[a]; _items[a] = _items[b]; _items[b] = ti;
                    float tk = _keys[a]; _keys[a] = _keys[b]; _keys[b] = tk;
                }
            }

            // ---------------------------------------------------------- 4. рельеф
            private void AssignTerrain()
            {
                for (int y = 0; y < _size; y++)
                {
                    for (int x = 0; x < _size; x++)
                    {
                        var tile = _map.Tiles[y * _size + x];
                        if (tile.sea) { tile.terrain = AdvTerrain.Grass; continue; }
                        var region = tile.region >= 0 ? _map.Regions[tile.region] : null;
                        var biome = region != null ? region.biome : AdvBiome.Temperate;
                        // пятна крупнее тайла: частота 0.075 даёт массивы леса/полей
                        // размером в несколько тайлов, как на картах SoQ
                        float patch = _noise.Fbm(x * 0.075f + (region != null ? region.id * 7.3f : 0f), y * 0.075f, 3, 0.55f);
                        bool nearSea = false;
                        int seaNeighbors = 0;
                        for (int d = 0; d < 4; d++)
                        {
                            var nt = _map.Tile(x + AdventureMap.Dx[d], y + AdventureMap.Dy[d]);
                            if (nt != null && nt.sea) { nearSea = true; seaNeighbors++; }
                        }
                        float coast = seaNeighbors / 4f + _noise.Eval(x * 0.3f, y * 0.3f) * 0.35f;
                        tile.terrain = AdvTerrainInfo.Pick(biome, patch, coast, nearSea, null);
                        tile.elevation = AdvTerrainInfo.IsMountain(tile.terrain) ? 2 : (int)Math.Round(AdvTerrainInfo.Height[(int)tile.terrain]) - 1;
                    }
                }
            }

            // ---------------------------------------------------------- 5. города
            private void PlacePrimaryBuildings()
            {
                // стартовые города
                int playerCount = _map.PlayerCount;
                var starts = new List<(SoqNode node, AdventureRegion region)>();
                foreach (var region in _map.Regions)
                {
                    var node = _nodeByRegion[region.id];
                    if (node.player > 0) starts.Add((node, region));
                }

                // randomizeStartingPositions: игроки распределяются по стартовым регионам случайно
                if (_layout.randomizeStartingPositions && starts.Count > 1)
                {
                    var players = new List<int>();
                    for (int i = 0; i < starts.Count; i++) players.Add(i + 1);
                    _rng.Shuffle(players);
                    for (int i = 0; i < starts.Count; i++)
                    {
                        starts[i].node.player = players[i];
                        starts[i].region.player = players[i];
                    }
                }

                foreach (var pair in starts)
                {
                    if (pair.node.player > playerCount) continue;
                    string defId = string.IsNullOrEmpty(pair.node.PrimaryBuilding) ? "AgnosticTown" : pair.node.PrimaryBuilding;
                    var town = PlaceAt(pair.region, _catalog.Get(defId) ?? _catalog.Get("AgnosticTown"), pair.region.centerX, pair.region.centerY, pair.node.player);
                    if (town == null)
                    {
                        // запасной вариант — любая свободная клетка региона
                        var alt = FindFreeTile(pair.region);
                        if (alt >= 0) town = PlaceAt(pair.region, _catalog.Get("AgnosticTown"), alt % _size, alt / _size, pair.node.player);
                    }
                    if (town == null)
                    {
                        Warn("town_" + pair.region.id, "Не удалось поставить город в " + pair.region.name + ".");
                        continue;
                    }
                    town.isTown = true;
                    _map.Starts.Add(new AdventureStart
                    {
                        player = pair.node.player,
                        x = town.x,
                        y = town.y,
                        region = pair.region.id,
                    });
                }

                // главные строения нестартовых регионов (Beacon, шахта-цель и т. п.)
                foreach (var region in _map.Regions)
                {
                    var node = _nodeByRegion[region.id];
                    if (node.player > 0 || string.IsNullOrEmpty(node.PrimaryBuilding)) continue;
                    var def = _catalog.Get(node.PrimaryBuilding);
                    if (def == null)
                    {
                        Warn("pdef_" + node.PrimaryBuilding, "Неизвестное главное строение '" + node.PrimaryBuilding + "'.");
                        continue;
                    }
                    PlaceAt(region, def, region.centerX, region.centerY, 0);
                }

                if (_map.Starts.Count < Math.Min(playerCount, starts.Count))
                    Warn("starts", "Стартовых городов меньше, чем игроков: " + _map.Starts.Count + " из " + playerCount + ".");
            }

            private int FindFreeTile(AdventureRegion region)
            {
                foreach (int idx in region.tiles)
                {
                    int x = idx % _size, y = idx / _size;
                    if (!_occupiedAt(x, y)) return idx;
                }
                return -1;
            }

            // ---------------------------------------------------------- 6. объекты
            private void PlaceBlueprintObjects()
            {
                foreach (var region in _map.Regions)
                {
                    if (!_nodeByRegion.TryGetValue(region.id, out var node) || node == null) continue;
                    if (node.region_identifier.Count == 0)
                    {
                        SpawnBlueprintRegion(region, "default");
                        continue;
                    }
                    // идентификаторы — альтернативы; берём один случайный, остальные — как fallback
                    int start = _rng.Next(node.region_identifier.Count);
                    bool any = false;
                    for (int i = 0; i < node.region_identifier.Count; i++)
                    {
                        string id = node.region_identifier[(start + i) % node.region_identifier.Count];
                        int before = _map.Objects.Count;
                        SpawnBlueprintRegion(region, id);
                        if (_map.Objects.Count > before) { any = true; break; }
                    }
                    if (!any) SpawnBlueprintRegion(region, "default");
                }
            }

            private void SpawnBlueprintRegion(AdventureRegion region, string identifier)
            {
                var bpRegion = _blueprint?.Region(identifier);
                if (bpRegion == null) return;
                var entries = new List<SoqBlueprintEntry>(bpRegion.entries);
                if (bpRegion.shuffle) _rng.Shuffle(entries);

                for (int iteration = 0; iteration < Math.Max(1, bpRegion.iterations); iteration++)
                {
                    foreach (var entry in entries)
                    {
                        int count = _rng.Next(entry.CountMin, Math.Max(entry.CountMin, entry.CountMax) + 1);
                        for (int c = 0; c < count; c++)
                        {
                            string objectId = entry.objects.Count > 0 ? entry.objects[_rng.Next(entry.objects.Count)] : null;
                            if (objectId == null) continue;
                            var def = _catalog.Get(objectId);
                            if (def == null)
                            {
                                Warn("def_" + objectId, "В каталоге нет объекта '" + objectId + "' (blueprint " + identifier + ").");
                                continue;
                            }
                            TrySpawn(region, def, entry);
                        }
                    }
                }
            }

            private bool TrySpawn(AdventureRegion region, AdvObjectDef def, SoqBlueprintEntry entry)
            {
                int placed = CountPlaced(def.id);
                if (def.unique && placed > 0) return false;
                if (def.maxPerMap > 0 && placed >= def.maxPerMap) return false;

                // кандидаты: тайлы региона, удалённые от центра не меньше distance*radius
                var candidates = new List<int>();
                float minDist = entry != null ? entry.distance * region.radius : 0f;
                for (int i = 0; i < region.tiles.Count; i++)
                {
                    int idx = region.tiles[i];
                    int x = idx % _size, y = idx / _size;
                    var tile = _map.Tiles[idx];
                    if (tile.sea) continue;
                    if (!MatchesLocation(tile, region, entry))
                        continue;
                    if (minDist > 0.5f)
                    {
                        float dx = x - region.centerX, dy = y - region.centerY;
                        if (Math.Sqrt(dx * dx + dy * dy) < minDist) continue;
                    }
                    candidates.Add(idx);
                }
                if (candidates.Count == 0) return false;
                _rng.Shuffle(candidates);

                int attempts = Math.Min(candidates.Count, 120);
                for (int a = 0; a < attempts; a++)
                {
                    int idx = candidates[a];
                    int x = idx % _size, y = idx / _size;
                    if (!CanOccupy(x, y, def.blocking)) continue;

                    var obj = Place(region, def, x, y, region.player);
                    if (obj == null) continue;

                    SpawnChildren(obj, entry);
                    float guardChance = entry != null ? entry.guardChance : 0f;
                    if (guardChance > 0 && _rng.Chance(guardChance))
                        GuardObject(obj, entry);
                    return true;
                }
                return false;
            }

            private void SpawnChildren(AdventureObject parent, SoqBlueprintEntry entry)
            {
                if (entry == null || entry.children.Count == 0) return;
                foreach (var child in entry.children)
                {
                    if (child.objects.Count == 0) continue;
                    int min = 1, max = 1;
                    var cv = child.count;
                    if (cv != null && !cv.IsNull)
                    {
                        if (cv.Kind == JsonKind.Array && cv.Count > 0)
                        {
                            min = cv[0].AsInt;
                            max = cv.Count > 1 ? cv[1].AsInt : min;
                        }
                        else min = max = cv.AsInt;
                    }
                    int n = _rng.Next(min, Math.Max(min, max) + 1);
                    for (int i = 0; i < n; i++)
                    {
                        string id = child.objects[_rng.Next(child.objects.Count)];
                        var def = _catalog.Get(id);
                        if (def == null) continue;
                        int spot = FindAdjacentFree(parent.x, parent.y, false);
                        if (spot < 0) break;
                        Place(_map.Regions[parent.region < 0 ? 0 : parent.region], def, spot % _size, spot / _size, 0);
                    }
                }
            }

            private void GuardObject(AdventureObject obj, SoqBlueprintEntry entry)
            {
                string tier = "Random";
                if (entry != null && entry.guardTier.Count > 0) tier = entry.guardTier[_rng.Next(entry.guardTier.Count)];
                var def = _catalog.Get(obj.defId);
                if (tier == "Random" && def != null) tier = def.guardTier;

                if (tier == "Random")
                {
                    float roll = _rng.NextFloat();
                    tier = roll < 0.5f ? "Low" : roll < 0.85f ? "Medium" : "High";
                }

                int spot = FindAdjacentFree(obj.x, obj.y, true);
                if (spot < 0) return;

                float difficulty = 0f;
                if (entry != null) difficulty = _rng.Range(entry.DifficultyMin, Math.Max(entry.DifficultyMin, entry.DifficultyMax));
                var guard = PlaceGuard(spot % _size, spot / _size, tier, difficulty, obj);
                if (guard != null)
                {
                    obj.guarded = true;
                    obj.guardTier = tier;
                    obj.guardDifficulty = difficulty;
                    obj.guardObjectId = guard.id;
                    _map.GuardedCount++;
                }
            }

            private AdventureObject PlaceGuard(int x, int y, string tier, float difficulty, AdventureObject owner)
            {
                // Идентификаторы — как в objects.json (GuardBandit/GuardArcher/...).
                // Раньше здесь были имена в стиле SoQ (guard_bandit), которых в каталоге нет,
                // и стражи молча не появлялись.
                string primary;
                switch ((tier ?? "Random").ToLowerInvariant())
                {
                    case "low": primary = "GuardBandit"; break;
                    case "high": primary = "GuardLeader"; break;
                    case "medium": primary = "GuardArcher"; break;
                    default: primary = _rng.Chance(0.5) ? "GuardBandit" : "GuardWolf"; break;
                }
                var def = _catalog.Get(primary);
                if (def == null) def = _catalog.Get("GuardBandit");
                if (def == null) def = FindGuardDef();
                if (def == null)
                {
                    Warn("guard_def", "В каталоге нет объектов-стражей (category=guard).");
                    return null;
                }
                var obj = Place(_map.Regions[_map.Tiles[y * _size + x].region < 0 ? 0 : _map.Tiles[y * _size + x].region], def, x, y, owner != null ? owner.player : 0);
                if (obj != null)
                {
                    obj.guardTier = tier ?? "Random";
                    obj.guardDifficulty = difficulty;
                }
                return obj;
            }

            /// <summary>Первый попавшийся объект категории guard — запасной вариант.</summary>
            private AdvObjectDef FindGuardDef()
            {
                for (int i = 0; i < _catalog.Objects.Count; i++)
                    if (_catalog.Objects[i].IsGuard) return _catalog.Objects[i];
                return null;
            }

            private int FindAdjacentFree(int x, int y, bool allowDiagonalFirst)
            {
                var offsets = allowDiagonalFirst
                    ? new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1) }
                    : new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
                foreach (var off in offsets)
                {
                    int nx = x + off.Item1, ny = y + off.Item2;
                    if (!_map.InBounds(nx, ny)) continue;
                    var tile = _map.Tiles[ny * _size + nx];
                    if (tile.sea || _occupiedAt(nx, ny)) continue;
                    return ny * _size + nx;
                }
                return -1;
            }

            private AdventureObject Place(AdventureRegion region, AdvObjectDef def, int x, int y, int player)
            {
                if (def == null) return null;
                if (!_map.InBounds(x, y)) return null;
                var tile = _map.Tiles[y * _size + x];
                if (tile.sea) return null;
                _occupied.Add(y * _size + x);
                var obj = new AdventureObject
                {
                    id = _nextObjectId++,
                    defId = def.id,
                    name = def.name,
                    category = def.category,
                    x = x,
                    y = y,
                    region = tile.region >= 0 ? tile.region : (region != null ? region.id : -1),
                    player = player,
                };
                _map.Objects.Add(obj);
                if (_placedByDef.TryGetValue(def.id, out var n)) _placedByDef[def.id] = n + 1;
                else _placedByDef[def.id] = 1;
                return obj;
            }

            private int CountPlaced(string defId)
            {
                return _placedByDef.TryGetValue(defId, out var n) ? n : 0;
            }

            private AdventureObject PlaceAt(AdventureRegion region, AdvObjectDef def, int x, int y, int player)
            {
                if (def == null) return null;
                if (!CanOccupy(x, y, def.blocking))
                {
                    // ищем ближайшую свободную клетку региона
                    int fallback = -1;
                    float best = float.MaxValue;
                    for (int i = 0; i < region.tiles.Count; i++)
                    {
                        int idx = region.tiles[i];
                        int tx = idx % _size, ty = idx / _size;
                        if (!CanOccupy(tx, ty, def.blocking)) continue;
                        float d = (tx - x) * (tx - x) + (ty - y) * (ty - y);
                        if (d < best) { best = d; fallback = idx; }
                    }
                    if (fallback < 0) return null;
                    x = fallback % _size;
                    y = fallback / _size;
                }
                return Place(region, def, x, y, player);
            }

            private bool _occupiedAt(int x, int y)
            {
                return !_map.InBounds(x, y) || _occupied.Contains(y * _size + x);
            }

            private bool CanOccupy(int x, int y, bool blocking)
            {
                if (!_map.InBounds(x, y)) return false;
                int idx = y * _size + x;
                if (_map.Tiles[idx].sea) return false;
                if (_occupiedAt(x, y)) return false;
                if (blocking)
                {
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (!_map.InBounds(nx, ny)) continue;
                            if (_occupiedAt(nx, ny)) return false;
                        }
                }
                return true;
            }

            private bool MatchesLocation(AdventureTile tile, AdventureRegion region, SoqBlueprintEntry entry)
            {
                if (entry == null || entry.location.Count == 0) return true;
                int x = tile.x, y = tile.y;
                foreach (var loc in entry.location)
                {
                    switch ((loc ?? "").ToLowerInvariant())
                    {
                        case "any":
                            return true;
                        case "outintheopen":
                            if (OpenAround(x, y, 2)) return true;
                            break;
                        case "nearmapedge":
                            if (Math.Min(Math.Min(x, _size - 1 - x), Math.Min(y, _size - 1 - y)) <= 3) return true;
                            break;
                        case "nearregionedge":
                        case "nearareaedge":
                            if (NearRegionEdge(x, y, region)) return true;
                            break;
                        case "nearroad":
                            if (NearRoad(x, y)) return true;
                            break;
                        case "edge":
                            if (Math.Min(Math.Min(x, _size - 1 - x), Math.Min(y, _size - 1 - y)) <= 1) return true;
                            break;
                        case "corner":
                            if (x <= 2 || y <= 2 || x >= _size - 3 || y >= _size - 3) return true;
                            break;
                    }
                }
                return false;
            }

            private bool OpenAround(int x, int y, int radius)
            {
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        var t = _map.Tile(x + dx, y + dy);
                        if (t == null) return false;
                        if (t.sea) return false;
                        if (t.region < 0) return false;
                    }
                return true;
            }

            private bool NearRegionEdge(int x, int y, AdventureRegion region)
            {
                foreach (var n in _map.Neighbors8(x, y))
                    if (n.sea || n.region != region.id) return true;
                return false;
            }

            private bool NearRoad(int x, int y)
            {
                if (_map.Tile(x, y) != null && _map.Tile(x, y).road) return true;
                foreach (var n in _map.Neighbors8(x, y)) if (n.road) return true;
                return false;
            }

            // ---------------------------------------------------------- 8. заполнение
            private void FillEmptyTiles()
            {
                // без регионов некуда «приписать» находку — иначе индекс 0 упадёт
                if (_map.Regions.Count == 0) return;
                var fillRegion = _blueprint?.Region("fill");
                var roadRegion = _blueprint?.Region("default");
                int pickupBudget = Math.Max(20, _landCount / 12);

                foreach (var tile in _map.AllTiles())
                {
                    if (tile.sea || _occupiedAt(tile.x, tile.y)) continue;
                    bool road = tile.road;
                    var source = road ? roadRegion : fillRegion;
                    if (source == null || source.entries.Count == 0) continue;

                    float chance = road ? 0.22f : 0.11f;
                    if (!_rng.Chance(chance)) continue;
                    if (pickupBudget <= 0) break;

                    var entry = source.entries[_rng.Next(source.entries.Count)];
                    if (entry.objects.Count == 0) continue;
                    var def = _catalog.Get(entry.objects[_rng.Next(entry.objects.Count)]);
                    if (def == null) continue;
                    // стражи и цели не «засеивают» пустую землю: они ставятся только адресно
                    if (def.IsGuard || string.Equals(def.category, "objective", StringComparison.OrdinalIgnoreCase)) continue;
                    if (def.unique && CountPlaced(def.id) > 0) continue;
                    if (def.maxPerMap > 0 && CountPlaced(def.id) >= def.maxPerMap) continue;
                    if (!CanOccupy(tile.x, tile.y, def.blocking)) continue;
                    var region = _map.Regions[tile.region < 0 ? 0 : tile.region];
                    Place(region, def, tile.x, tile.y, 0);
                    pickupBudget--;
                }
            }

            // ---------------------------------------------------------- 9. декор земли
            /// <summary>
            /// Деревья на пустой земле: в SoQ суша не бывает «лысой». Декор не занимает
            /// тайл и не мешает объектам, поэтому ставится после них и только на
            /// свободные тайлы. Плотность зависит от рельефа (лес — густо, песок — редко).
            /// </summary>
            private void ScatterDecor()
            {
                if (_map.Regions.Count == 0) return;
                var decorDefs = new List<AdvObjectDef>();
                for (int i = 0; i < _catalog.Objects.Count; i++)
                {
                    var def = _catalog.Objects[i];
                    if (string.Equals(def.category, "decor", StringComparison.OrdinalIgnoreCase))
                        decorDefs.Add(def);
                }
                if (decorDefs.Count == 0) return;

                int nextId = 1;
                foreach (var tile in _map.AllTiles())
                {
                    if (tile.sea || tile.road || _occupiedAt(tile.x, tile.y)) continue;
                    float density = DecorDensity(tile.terrain);
                    if (density <= 0f) continue;
                    // хеш от координат и сида: декор не сдвигает очередь RNG для объектов
                    if (SoqRng.Hash01(tile.x, tile.y, _map.Seed * 31 + 7) > density) continue;
                    int pick = (int)(SoqRng.Hash01(tile.x, tile.y, _map.Seed * 71 + 13) * decorDefs.Count);
                    if (pick < 0) pick = 0;
                    if (pick >= decorDefs.Count) pick = decorDefs.Count - 1;
                    var def = decorDefs[pick];
                    _map.Decor.Add(new AdventureObject
                    {
                        id = nextId++,
                        defId = def.id,
                        name = def.name,
                        category = def.category,
                        x = tile.x,
                        y = tile.y,
                        region = tile.region,
                        decor = true,
                    });
                }
            }

            private static float DecorDensity(AdvTerrain t)
            {
                switch (t)
                {
                    case AdvTerrain.ForestMix:
                    case AdvTerrain.ForestPine: return 0.26f;
                    case AdvTerrain.Autumn: return 0.16f;
                    case AdvTerrain.Marsh: return 0.13f;
                    case AdvTerrain.Grass:
                    case AdvTerrain.Fields:
                    case AdvTerrain.ColdGrass: return 0.08f;
                    case AdvTerrain.Tundra:
                    case AdvTerrain.Snow: return 0.06f;
                    case AdvTerrain.Rock:
                    case AdvTerrain.RockGreen:
                    case AdvTerrain.SnowRock: return 0.05f;
                    case AdvTerrain.Sand:
                    case AdvTerrain.Dry:
                    case AdvTerrain.SandRock: return 0.025f;
                    default: return 0.07f;
                }
            }

            // ---------------------------------------------------------- проверки
            private void Validate()
            {
                int unreachable = 0;
                var seen = new bool[_map.Tiles.Length];
                var queue = new Queue<int>();
                var startTile = -1;
                for (int i = 0; i < _map.Tiles.Length; i++)
                    if (!_map.Tiles[i].sea) { startTile = i; break; }
                if (startTile >= 0)
                {
                    queue.Enqueue(startTile);
                    seen[startTile] = true;
                    while (queue.Count > 0)
                    {
                        int cur = queue.Dequeue();
                        int x = cur % _size, y = cur / _size;
                        for (int d = 0; d < 4; d++)
                        {
                            int nx = x + AdventureMap.Dx[d], ny = y + AdventureMap.Dy[d];
                            if (!_map.InBounds(nx, ny)) continue;
                            int ni = ny * _size + nx;
                            if (_map.Tiles[ni].sea || seen[ni]) continue;
                            seen[ni] = true;
                            queue.Enqueue(ni);
                        }
                    }
                    for (int i = 0; i < _map.Tiles.Length; i++)
                        if (!_map.Tiles[i].sea && !seen[i]) unreachable++;
                }
                if (unreachable > 0)
                    Warn("unreachable", "Не связано с материком: " + unreachable + " тайлов суши.");

                if (_map.Starts.Count == 0)
                    _map.Report.Add("Стартовых позиций нет — проверьте player/primary_building в layout'е.");
                if (_map.Regions.Count == 0)
                    _map.Report.Add("Регионов нет — layout пуст.");

                _map.Report.Insert(0, _map.Summary());
            }

            private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        }
    }
}
