using System;
using System.Collections.Generic;
using System.Linq;

namespace IronThrone.Core.Content
{
    /// <summary>
    /// Генератор случайной гексагональной карты (odd-r) с жёсткими географическими
    /// ограничениями: Север — на севере, Дорн — на юге, Железные острова — на островах
    /// в западном море. Провинции, дома и их id берутся из канонической карты, поэтому
    /// весь контент (столицы, квесты, торговые пути) остаётся валидным.
    /// </summary>
    public static class MapGenerator
    {
        // Соседние смещения зависят от чётности ряда: карта — odd-r offset, а не axial.
        private static readonly (int dc, int dr)[] OddRDirsEven =
        {
            (1, 0), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1)
        };

        private static readonly (int dc, int dr)[] OddRDirsOdd =
        {
            (1, 0), (1, 1), (0, 1), (-1, 0), (0, -1), (1, -1)
        };

        /// <summary>Смещения соседей для ряда r (odd-r offset): порядок — E, SE, SW, W, NW, NE.</summary>
        public static (int dc, int dr)[] OddRNeighborOffsets(int r) => (r & 1) == 0 ? OddRDirsEven : OddRDirsOdd;

        public static bool AreAdjacent(int c1, int r1, int c2, int r2, int width)
        {
            foreach (var d in OddRNeighborOffsets(r1))
            {
                int nc = c1 + d.dc;
                if (nc < 0) nc += width;
                if (nc >= width) nc -= width;
                if (nc == c2 && r1 + d.dr == r2) return true;
            }
            return false;
        }

        // Ориентиры регионов в долях карты (x, y). Ограничения мира держатся здесь.
        private static readonly Dictionary<string, (float x, float y)> Anchors = new Dictionary<string, (float, float)>
        {
            ["wall"] = (0.44f, 0.045f),
            ["north"] = (0.44f, 0.17f),
            ["islands"] = (0.13f, 0.47f),
            ["riverlands"] = (0.44f, 0.37f),
            ["vale"] = (0.71f, 0.33f),
            ["westerlands"] = (0.27f, 0.46f),
            ["crownlands"] = (0.60f, 0.50f),
            ["reach"] = (0.37f, 0.63f),
            ["stormlands"] = (0.66f, 0.66f),
            ["dorne"] = (0.45f, 0.86f),
        };

        public static MapData Generate(MapData canonical, int seed, int width = 48, int height = 36)
        {
            if (canonical == null || canonical.provinces.Count == 0)
                throw new InvalidOperationException("Нет канонической карты для шаблона");

            var rng = new Random(seed);
            var map = new MapData
            {
                width = width,
                height = height,
                layout = "odd-r",
                regions = canonical.regions.Select(CloneRegion).ToList(),
                houses = canonical.houses.Select(CloneHouse).ToList(),
            };

            // ---- 1. маска суши ----
            var heightField = BuildHeightField(width, height, rng);
            int targetLand = (int)(width * height * 0.39f);
            var land = ThresholdLand(heightField, targetLand);

            // Стена: гарантированная полоса суши на крайнем севере
            ForceRect(land, width, height, 9, 0, 29, 4);
            // Железные острова: архипелаг в западном море
            var islandCenters = CarveIslands(land, width, height, rng, 6, 1);

            // ---- 2. регионы ----
            var templatesByRegion = canonical.provinces
                .Where(p => !string.IsNullOrEmpty(p.region))
                .GroupBy(p => p.region)
                .ToDictionary(g => g.Key, g => g.ToList());

            var regionTargets = templatesByRegion.ToDictionary(kv => kv.Key, kv => kv.Value.Sum(p => Math.Max(3, p.tiles?.Count ?? 5)));
            int totalTiles = regionTargets.Values.Sum();
            var landTiles = new List<(int c, int r)>();
            for (int r = 0; r < height; r++)
                for (int c = 0; c < width; c++)
                    if (land[c, r]) landTiles.Add((c, r));

            // нормируем квоты под фактическое количество суши
            double scale = landTiles.Count / (double)Math.Max(1, totalTiles);
            var quotas = regionTargets.ToDictionary(kv => kv.Key, kv => (int)Math.Round(kv.Value * scale));

            var regionOf = new Dictionary<(int, int), string>();
            var anchors = new Dictionary<string, (int c, int r)>();
            foreach (var kv in Anchors)
            {
                if (!templatesByRegion.ContainsKey(kv.Key)) continue;
                var cell = NearestLand(land, width, height, kv.Value.x, kv.Value.y);
                if (cell == null) continue;
                anchors[kv.Key] = cell.Value;
                regionOf[cell.Value] = kv.Key;
            }

            // острова — строго регион islands
            if (anchors.TryGetValue("islands", out var islAnchor) && islandCenters.Count > 0)
                anchors["islands"] = islandCenters[0];

            foreach (var center in islandCenters)
                foreach (var t in Blob(center, 2, width, height))
                    if (land[t.c, t.r]) { regionOf[t] = "islands"; }

            // ограниченный рост регионов (квоты)
            GrowRegions(land, width, height, regionOf, quotas, anchors, rng);
            EnsureRegionCapacity(land, width, height, regionOf, templatesByRegion, rng);
            FixRegionContiguity(land, width, height, regionOf, anchors, rng);
            EnsureRegionCapacity(land, width, height, regionOf, templatesByRegion, rng);

            // ---- 3. провинции ----
            var provinceTiles = new Dictionary<string, List<(int c, int r)>>();
            var provinceSeeds = new Dictionary<string, (int c, int r)>();
            var usedTemplates = new HashSet<string>();

            foreach (var regionKv in templatesByRegion)
            {
                string region = regionKv.Key;
                var myTiles = regionOf.Where(kv => kv.Value == region).Select(kv => kv.Key).ToList();
                var templates = regionKv.Value;
                if (myTiles.Count == 0) continue;

                // если земли меньше, чем провинций — часть шаблонов получает по 1 клетке
                int n = Math.Min(templates.Count, myTiles.Count);
                var seeds = FarthestSeeds(myTiles, n, rng);
                // столица региона — ближайший к ориентиру шаблон
                var capTemplate = templates.FirstOrDefault(p => p.isCapital);
                if (capTemplate != null && anchors.TryGetValue(region, out var a))
                {
                    seeds = seeds.OrderBy(s => HexDistance(s.c, s.r, a.c, a.r)).ToList();
                }

                // Вороной по гексагональному расстоянию: ячейки связны по построению
                if (region == "islands")
                {
                    var centers = islandCenters;
                    int use = Math.Min(seeds.Count, centers.Count);
                    seeds = centers.Take(use).ToList();
                    while (seeds.Count < templates.Count && seeds.Count < myTiles.Count)
                    {
                        var extra = myTiles.OrderByDescending(t => seeds.Min(s => HexDistance(s.c, s.r, t.Item1, t.Item2)))
                                           .ThenBy(_ => rng.Next()).First();
                        seeds.Add(extra);
                    }
                }
                var assigned = new Dictionary<(int c, int r), int>();
                foreach (var t in myTiles)
                {
                    int best = 0, bestD = int.MaxValue;
                    for (int i = 0; i < seeds.Count; i++)
                    {
                        int d = HexDistance(t.Item1, t.Item2, seeds[i].c, seeds[i].r);
                        if (d < bestD) { bestD = d; best = i; }
                    }
                    assigned[t] = best;
                }

                var orderedTemplates = templates.OrderBy(t => t.isCapital ? 0 : 1).ThenBy(t => t.id).ToList();
                for (int i = 0; i < orderedTemplates.Count; i++)
                {
                    var def = orderedTemplates[i];
                    int idx = Math.Min(i, seeds.Count - 1);
                    var tiles = assigned.Where(kv => kv.Value == idx).Select(kv => kv.Key).ToList();
                    if (tiles.Count == 0)
                        tiles = new List<(int c, int r)> { seeds[idx] };
                    provinceTiles[def.id] = tiles;
                    provinceSeeds[def.id] = tiles[0];
                    usedTemplates.Add(def.id);
                }
            }

            RepairConnectivity(provinceTiles, rng);

            // ---- 4. сборка провинций ----
            var byId = new Dictionary<string, ProvinceDef>();
            foreach (var template in canonical.provinces)
            {
                if (!provinceTiles.TryGetValue(template.id, out var tiles) || tiles.Count == 0)
                    continue;
                var p = CloneProvince(template);
                p.tiles = tiles.Select(t => new List<int> { t.c, t.r }).ToList();
                var seedCell = provinceSeeds[template.id];
                p.settlement = new List<int> { seedCell.c, seedCell.r };
                p.coastal = tiles.Any(t => HasSeaNeighbor(land, width, height, t));
                p.port = template.port && p.coastal;
                p.neighbors = new List<string>();
                byId[p.id] = p;
                map.provinces.Add(p);
            }

            // соседи
            foreach (var p in map.provinces)
            {
                var set = new HashSet<string>();
                foreach (var t in p.tiles)
                {
                    int c = t[0], r = t[1];
                    foreach (var d in OddRNeighborOffsets(r))
                    {
                        int nc = c + d.dc, nr = r + d.dr;
                        if (nr < 0 || nr >= height) continue;
                        if (nc < 0) nc += width; // горизонтальная склейка мира
                        if (nc >= width) nc -= width;
                        var other = map.provinces.FirstOrDefault(x => x.tiles.Any(tt => tt[0] == nc && tt[1] == nr));
                        if (other != null && other.id != p.id) set.Add(other.id);
                    }
                }
                p.neighbors = set.OrderBy(x => x).ToList();
            }

            // клетки
            var provOfTile = new Dictionary<(int, int), string>();
            foreach (var p in map.provinces)
                foreach (var t in p.tiles)
                    provOfTile[(t[0], t[1])] = p.id;

            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    var tile = new MapTileDef
                    {
                        c = c,
                        r = r,
                        sea = !land[c, r],
                        coastal = land[c, r] && HasSeaNeighbor(land, width, height, (c, r)),
                    };
                    if (provOfTile.TryGetValue((c, r), out var pid))
                    {
                        var p = byId[pid];
                        tile.province = pid;
                        tile.region = p.region;
                        tile.terrain = p.terrain;
                        tile.river = p.river;
                    }
                    else if (!tile.sea)
                    {
                        // бесхозная суша: приписываем к ближайшей провинции, чтобы не терять землю
                        var nearest = map.provinces.OrderBy(p => p.tiles.Min(t => HexDistance(t[0], t[1], c, r))).FirstOrDefault();
                        if (nearest != null) { tile.province = nearest.id; tile.region = nearest.region; tile.terrain = nearest.terrain; }
                    }
                    map.tiles.Add(tile);
                }
            }

            // морские маршруты: порты к ближайшим портам в других регионах
            map.seaRoutes = BuildSeaRoutes(map);
            map.Index();
            return map;
        }

        // ------------------------------------------------------------------ рельеф
        private static float[,] BuildHeightField(int width, int height, Random rng)
        {
            var noise = new float[3][,];
            var scales = new[] { 6, 12, 22 };
            for (int o = 0; o < 3; o++)
            {
                int n = scales[o];
                var lattice = new float[n + 1, n + 1];
                for (int i = 0; i <= n; i++)
                    for (int j = 0; j <= n; j++)
                        lattice[i, j] = (float)rng.NextDouble();
                noise[o] = new float[width, height];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        float fx = x / (float)width * n;
                        float fy = y / (float)height * n;
                        int x0 = (int)fx, y0 = (int)fy;
                        float tx = fx - x0, ty = fy - y0;
                        tx = tx * tx * (3 - 2 * tx);
                        ty = ty * ty * (3 - 2 * ty);
                        float a = lattice[x0, y0], b = lattice[Math.Min(n, x0 + 1), y0];
                        float c = lattice[x0, Math.Min(n, y0 + 1)], d = lattice[Math.Min(n, x0 + 1), Math.Min(n, y0 + 1)];
                        noise[o][x, y] = (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty;
                    }
            }

            var field = new float[width, height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float nx = x / (float)(width - 1), ny = y / (float)(height - 1);
                    float continent = Math.Max(
                        Blob(nx, ny, 0.44f, 0.34f, 0.30f, 0.36f),
                        Math.Max(Blob(nx, ny, 0.58f, 0.56f, 0.28f, 0.30f),
                                 Blob(nx, ny, 0.36f, 0.70f, 0.24f, 0.26f)));
                    float n = noise[0][x, y] * 0.5f + noise[1][x, y] * 0.32f + noise[2][x, y] * 0.18f;
                    field[x, y] = continent * 0.72f + n * 0.45f;
                }
            return field;
        }

        private static float Blob(float x, float y, float cx, float cy, float rx, float ry)
        {
            float dx = (x - cx) / rx, dy = (y - cy) / ry;
            return Math.Max(0, 1f - (dx * dx + dy * dy));
        }

        private static bool[,] ThresholdLand(float[,] field, int target)
        {
            int w = field.GetLength(0), h = field.GetLength(1);
            var values = new List<float>(w * h);
            foreach (var v in field) values.Add(v);
            values.Sort();
            float threshold = values[Math.Max(0, Math.Min(values.Count - 1, values.Count - target))];
            var land = new bool[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    land[x, y] = field[x, y] >= threshold;
            return land;
        }

        private static void ForceRect(bool[,] land, int w, int h, int x0, int y0, int x1, int y1)
        {
            for (int y = Math.Max(0, y0); y <= Math.Min(h - 1, y1); y++)
                for (int x = Math.Max(0, x0); x <= Math.Min(w - 1, x1); x++)
                    land[x, y] = true;
        }

        private static List<(int c, int r)> CarveIslands(bool[,] land, int width, int height, Random rng, int count, int radius)
        {
            var centers = new List<(int c, int r)>();
            int tries = 0;
            while (centers.Count < count && tries++ < 4000)
            {
                int c = 2 + rng.Next(0, Math.Max(1, width / 3));
                int r = 4 + rng.Next(0, Math.Max(1, height - 10));
                if (land[c, r]) continue;
                // остров не должен касаться материка
                bool touchesMainland = false;
                for (int dr = -3; dr <= 3 && !touchesMainland; dr++)
                    for (int dc = -3; dc <= 3 && !touchesMainland; dc++)
                    {
                        int nc = c + dc, nr = r + dr;
                        if (nc < 0 || nr < 0 || nc >= width || nr >= height) continue;
                        if (Math.Abs(dc) <= radius && Math.Abs(dr) <= radius) continue;
                        if (land[nc, nr]) touchesMainland = true;
                    }
                if (touchesMainland) continue;
                if (centers.Any(x => HexDistance(x.c, x.r, c, r) < 6)) continue;
                centers.Add((c, r));
                foreach (var t in Blob((c, r), radius, width, height)) land[t.c, t.r] = true;
            }
            return centers;
        }

        private static IEnumerable<(int c, int r)> Blob((int c, int r) center, int radius, int width, int height)
        {
            for (int dr = -radius; dr <= radius; dr++)
                for (int dc = -radius; dc <= radius; dc++)
                {
                    int c = center.c + dc, r = center.r + dr;
                    if (c < 0 || r < 0 || c >= width || r >= height) continue;
                    if (HexDistance(center.c, center.r, c, r) > radius) continue;
                    yield return (c, r);
                }
        }

        private static (int c, int r)? NearestLand(bool[,] land, int width, int height, float fx, float fy)
        {
            int targetC = (int)(fx * width), targetR = (int)(fy * height);
            (int c, int r)? best = null; int bestDist = int.MaxValue;
            for (int r = 0; r < height; r++)
                for (int c = 0; c < width; c++)
                {
                    if (!land[c, r]) continue;
                    int d = HexDistance(c, r, targetC, targetR);
                    if (d < bestDist) { bestDist = d; best = (c, r); }
                }
            return best;
        }

        // ------------------------------------------------------------------ регионы
        private static void GrowRegions(bool[,] land, int width, int height, Dictionary<(int, int), string> regionOf,
            Dictionary<string, int> quotas, Dictionary<string, (int c, int r)> anchors, Random rng)
        {
            // 1. Вороной по ориентирам: регионы получаются связными
            var free = new List<(int c, int r)>();
            for (int r = 0; r < height; r++)
                for (int c = 0; c < width; c++)
                {
                    if (!land[c, r]) continue;
                    if (regionOf.ContainsKey((c, r))) continue;
                    string best = null; int bestD = int.MaxValue;
                    foreach (var kv in anchors)
                    {
                        int d = HexDistance(c, r, kv.Value.c, kv.Value.r);
                        int jitter = (int)(Noise(c, r, 17) * 2.5f);
                        d += jitter;
                        if (d < bestD) { bestD = d; best = kv.Key; }
                    }
                    if (best != null) regionOf[(c, r)] = best;
                    else free.Add((c, r));
                }
            foreach (var t in free)
                regionOf[t] = anchors.Keys.First();

            // 2. Балансировка квот: переносим пограничные клетки из переполненных регионов
            for (int iter = 0; iter < 4000; iter++)
            {
                var counts = new Dictionary<string, int>();
                foreach (var kv in regionOf) counts[kv.Value] = counts.TryGetValue(kv.Value, out var n) ? n + 1 : 1;

                string over = null, under = null;
                int overExcess = 0;
                foreach (var kv in quotas)
                {
                    counts.TryGetValue(kv.Key, out var c);
                    int excess = c - kv.Value;
                    if (excess > overExcess) { overExcess = excess; over = kv.Key; }
                }
                if (over == null || overExcess <= 2) break;

                int underDeficit = 0;
                foreach (var kv in quotas)
                {
                    counts.TryGetValue(kv.Key, out var c);
                    int deficit = kv.Value - c;
                    if (deficit > underDeficit) { underDeficit = deficit; under = kv.Key; }
                }
                if (under == null || underDeficit <= 2) break;

                // клетка региона over, граничащая с under, ближайшая к ориентиру under
                (int c, int r) pick = (-1, -1); int pickD = int.MaxValue;
                foreach (var kv in regionOf)
                {
                    if (kv.Value != over) continue;
                    bool touches = false;
                    foreach (var d in OddRNeighborOffsets(kv.Key.Item2))
                    {
                        int nc = kv.Key.Item1 + d.dc, nr = kv.Key.Item2 + d.dr;
                        if (nr < 0 || nr >= height) continue;
                        if (nc < 0) nc += width;
                        if (nc >= width) nc -= width;
                        if (regionOf.TryGetValue((nc, nr), out var rr) && rr == under) { touches = true; break; }
                    }
                    if (!touches) continue;
                    var a = anchors[under];
                    int dist = HexDistance(kv.Key.Item1, kv.Key.Item2, a.c, a.r);
                    if (dist < pickD) { pickD = dist; pick = kv.Key; }
                }
                if (pick.Item1 < 0) break;
                regionOf[pick] = under;
            }
        }

        private static float Noise(int c, int r, int salt)
        {
            uint h = (uint)(c * 374761393 + r * 668265263 + salt * 2246822519);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }

        /// <summary>Гарантирует, что в каждом регионе хватает земли на все его провинции.</summary>
        private static void EnsureRegionCapacity(bool[,] land, int width, int height,
            Dictionary<(int, int), string> regionOf, Dictionary<string, List<ProvinceDef>> templates, Random rng)
        {
            int guard = 0;
            while (guard++ < 400)
            {
                bool moved = false;
                foreach (var kv in templates)
                {
                    string region = kv.Key;
                    int need = kv.Value.Count;
                    var tiles = regionOf.Where(x => x.Value == region).Select(x => x.Key).ToList();
                    if (tiles.Count >= need) continue;
                    // забираем пограничную клетку у самого богатого соседа
                    var candidates = tiles.SelectMany(t => Neighbors(t, width, height))
                        .Where(n => regionOf.ContainsKey(n) && regionOf[n] != region)
                        .GroupBy(n => regionOf[n])
                        .Select(g => new { region = g.Key, tiles = g.ToList() })
                        .OrderByDescending(g => regionOf.Count(x => x.Value == g.region) - templates[g.region].Count)
                        .ToList();
                    foreach (var cand in candidates)
                    {
                        int surplus = regionOf.Count(x => x.Value == cand.region) - templates[cand.region].Count;
                        if (surplus <= 2) continue;
                        var take = cand.tiles[rng.Next(cand.tiles.Count)];
                        regionOf[take] = region;
                        moved = true;
                        break;
                    }
                }
                if (!moved) break;
            }
        }

        private static void FixRegionContiguity(bool[,] land, int width, int height, Dictionary<(int, int), string> regionOf,
            Dictionary<string, (int c, int r)> anchors, Random rng)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                bool changed = false;
                foreach (var regionKv in regionOf.GroupBy(kv => kv.Value).ToList())
                {
                    var tiles = new HashSet<(int, int)>(regionKv.Select(kv => kv.Key));
                    if (tiles.Count <= 1) continue;
                    var anchor = anchors.TryGetValue(regionKv.Key, out var a) && tiles.Contains(a) ? a : tiles.First();
                    var comp = new HashSet<(int, int)>();
                    var queue = new Queue<(int, int)>(); queue.Enqueue(anchor); comp.Add(anchor);
                    while (queue.Count > 0)
                    {
                        var t = queue.Dequeue();
                        foreach (var n in Neighbors(t, width, height))
                            if (tiles.Contains(n) && comp.Add(n)) queue.Enqueue(n);
                    }
                    if (comp.Count == tiles.Count) continue;
                    foreach (var t in tiles.Where(x => !comp.Contains(x)))
                    {
                        var nr = Neighbors(t, width, height).Where(n => regionOf.ContainsKey(n) && regionOf[n] != regionKv.Key)
                            .Select(n => regionOf[n]).ToList();
                        if (nr.Count == 0) continue;
                        regionOf[t] = nr[rng.Next(nr.Count)];
                        changed = true;
                    }
                }
                if (!changed) break;
            }
        }

        // ------------------------------------------------------------------ провинции
        private static List<(int c, int r)> FarthestSeeds(List<(int c, int r)> tiles, int n, Random rng)
        {
            var seeds = new List<(int c, int r)> { tiles[rng.Next(tiles.Count)] };
            while (seeds.Count < n)
            {
                var next = tiles.OrderByDescending(t => seeds.Min(s => HexDistance(s.c, s.r, t.c, t.r)))
                                .ThenBy(_ => rng.Next()).First();
                seeds.Add(next);
            }
            return seeds;
        }

        private static Dictionary<(int c, int r), int> AssignProvinceTiles(List<(int c, int r)> tiles,
            List<(int c, int r)> seeds, Random rng, out Dictionary<(int c, int r), int> owner)
        {
            var tileSet = new HashSet<(int, int)>(tiles);
            var result = new Dictionary<(int c, int r), int>();
            var frontier = new List<(int c, int r)>[seeds.Count];
            for (int i = 0; i < seeds.Count; i++)
            {
                frontier[i] = new List<(int, int)>();
                foreach (var d in OddRNeighborOffsets(seeds[i].r))
                {
                    var n = (seeds[i].c + d.dc, seeds[i].r + d.dr);
                    if (tileSet.Contains(n)) frontier[i].Add(n);
                }
                result[seeds[i]] = i;
            }
            int guard = 0;
            while (result.Count < tiles.Count && guard++ < tiles.Count * 8)
            {
                bool progressed = false;
                for (int i = 0; i < seeds.Count; i++)
                {
                    if (frontier[i].Count == 0) continue;
                    int pick = rng.Next(frontier[i].Count);
                    var t = frontier[i][pick];
                    frontier[i].RemoveAt(pick);
                    if (result.ContainsKey(t)) continue;
                    result[t] = i; progressed = true;
                    foreach (var d in OddRNeighborOffsets(t.r))
                    {
                        var n = (t.c + d.dc, t.r + d.dr);
                        if (tileSet.Contains(n) && !result.ContainsKey(n)) frontier[i].Add(n);
                    }
                }
                if (!progressed) break;
            }
            foreach (var t in tiles) if (!result.ContainsKey(t)) result[t] = 0;
            owner = result;
            return result;
        }

        private static void RepairConnectivity(Dictionary<string, List<(int c, int r)>> provinceTiles, Random rng)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                bool changed = false;
                foreach (var kv in provinceTiles.ToList())
                {
                    var set = new HashSet<(int, int)>(kv.Value);
                    if (set.Count <= 1) continue;
                    var seen = new HashSet<(int, int)>();
                    var comps = new List<List<(int, int)>>();
                    foreach (var t in set)
                    {
                        if (seen.Contains(t)) continue;
                        var comp = new List<(int, int)>();
                        var q = new Queue<(int, int)>(); q.Enqueue(t); seen.Add(t);
                        while (q.Count > 0)
                        {
                            var cur = q.Dequeue(); comp.Add(cur);
                            foreach (var d in OddRNeighborOffsets(cur.Item2))
                            {
                                var n = (cur.Item1 + d.dc, cur.Item2 + d.dr);
                                if (set.Contains(n) && seen.Add(n)) q.Enqueue(n);
                            }
                        }
                        comps.Add(comp);
                    }
                    if (comps.Count <= 1) continue;
                    comps.Sort((a, b) => b.Count.CompareTo(a.Count));
                    var keep = new HashSet<(int, int)>(comps[0]);
                    foreach (var comp in comps.Skip(1))
                    {
                        foreach (var t in comp)
                        {
                            var target = provinceTiles
                                .Where(x => x.Key != kv.Key && x.Value.Any(v =>
                                    Math.Abs(v.c - t.Item1) + Math.Abs(v.r - t.Item2) <= 1))
                                .OrderBy(_ => rng.Next()).Select(x => x.Key).FirstOrDefault();
                            if (target == null) continue;
                            provinceTiles[target].Add(t);
                            provinceTiles[kv.Key].Remove(t);
                            changed = true;
                        }
                    }
                }
                if (!changed) break;
            }
        }

        private static List<List<string>> BuildSeaRoutes(MapData map)
        {
            var ports = map.provinces.Where(p => p.port).ToList();
            var routes = new List<List<string>>();
            var seen = new HashSet<string>();
            foreach (var a in ports)
            {
                var nearest = ports.Where(b => b.id != a.id && b.region != a.region)
                    .OrderBy(b => DistanceTo(map, a, b)).Take(2);
                foreach (var b in nearest)
                {
                    string key = string.CompareOrdinal(a.id, b.id) < 0 ? a.id + "|" + b.id : b.id + "|" + a.id;
                    if (!seen.Add(key)) continue;
                    routes.Add(new List<string> { a.id, b.id });
                }
            }
            return routes;
        }

        private static int DistanceTo(MapData map, ProvinceDef a, ProvinceDef b)
        {
            var ta = a.tiles.FirstOrDefault(); var tb = b.tiles.FirstOrDefault();
            if (ta == null || tb == null) return 999;
            return HexDistance(ta[0], ta[1], tb[0], tb[1]);
        }

        // ------------------------------------------------------------------ утилиты
        public static int HexDistance(int c1, int r1, int c2, int r2)
        {
            int x1 = c1 - (r1 - (r1 & 1)) / 2, z1 = r1, y1 = -x1 - z1;
            int x2 = c2 - (r2 - (r2 & 1)) / 2, z2 = r2, y2 = -x2 - z2;
            return (Math.Abs(x1 - x2) + Math.Abs(y1 - y2) + Math.Abs(z1 - z2)) / 2;
        }

        private static IEnumerable<(int c, int r)> Neighbors((int c, int r) t, int width, int height)
        {
            foreach (var d in OddRNeighborOffsets(t.r))
            {
                int nc = t.c + d.dc, nr = t.r + d.dr;
                if (nr < 0 || nr >= height) continue;
                if (nc < 0) nc += width;
                if (nc >= width) nc -= width;
                yield return (nc, nr);
            }
        }

        private static bool HasSeaNeighbor(bool[,] land, int width, int height, (int c, int r) t)
        {
            foreach (var d in OddRNeighborOffsets(t.r))
            {
                int nc = t.c + d.dc, nr = t.r + d.dr;
                if (nr < 0 || nr >= height) continue;
                if (nc < 0) nc += width;
                if (nc >= width) nc -= width;
                if (!land[nc, nr]) return true;
            }
            return false;
        }

        private static RegionDef CloneRegion(RegionDef r) => new RegionDef
        {
            id = r.id, name = r.name, color = r.color, capital = r.capital
        };

        private static HouseDef CloneHouse(HouseDef h) => new HouseDef
        {
            id = h.id, name = h.name, words = h.words, color = h.color, region = h.region,
            seat = h.seat, major = h.major, description = h.description, pirate = h.pirate,
            bonuses = h.bonuses
        };

        private static ProvinceDef CloneProvince(ProvinceDef p) => new ProvinceDef
        {
            id = p.id, name = p.name, region = p.region, house = p.house, kind = p.kind,
            level = p.level, terrain = p.terrain, river = p.river, isCapital = p.isCapital,
            resource = p.resource, domain = p.domain,
        };
    }
}
