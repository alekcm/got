using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using IronThrone.Core.Game;

namespace IronThrone.Core.Worldgen
{
    /// <summary>
    /// Проверка генератора карт: детерминизм по сиду, различимость разных сидов,
    /// связность суши, заполненность регионов, стартовые позиции, объекты.
    /// Работает без Unity — используется headless-харнессом и smoke-тестом.
    /// </summary>
    public static class SoqMapCheck
    {
        /// <summary>
        /// Прогнать проверки. Возвращает true, если всё в порядке; отчёт пишется в log.
        /// </summary>
        public static bool Run(SoqRmgPack pack, int seeds, Action<string> log, int layoutCount = 2)
        {
            if (log == null) log = _ => { };
            if (pack == null || pack.Layouts.Count == 0)
            {
                log("Нет данных генератора карт (layout'ы не найдены).");
                return false;
            }

            bool ok = true;
            log($"Генератор карт: layout'ов {pack.Layouts.Count}, blueprint'ов {pack.Blueprints.Count}, объектов в каталоге {pack.Catalog.Objects.Count}.");
            foreach (var warning in pack.Warnings) log("  данные: " + warning);

            int layoutsToCheck = Math.Min(Math.Max(1, layoutCount), pack.Layouts.Count);
            for (int li = 0; li < layoutsToCheck; li++)
            {
                var layout = pack.Layouts[li];
                log("");
                log($"Layout «{layout.DisplayName}» ({layout.Key}), заявленный размер {layout.size}, игроков {layout.playerCount}.");
                var signatures = new Dictionary<int, string>();
                for (int i = 0; i < seeds; i++)
                {
                    int seed = 1000 + i * 7919;
                    var request = new AdventureGenRequest { LayoutKey = layout.Key, Seed = seed, SublayoutIndex = 0 };
                    var map = SoqMapGenerator.Generate(pack, request);
                    var again = SoqMapGenerator.Generate(pack, request);
                    string sig = Signature(map);
                    string sig2 = Signature(again);
                    if (sig != sig2)
                    {
                        ok = false;
                        log($"  ОШИБКА: сид {seed} не воспроизводится ({sig} != {sig2}).");
                    }
                    if (signatures.ContainsValue(sig))
                    {
                        ok = false;
                        log($"  ОШИБКА: сид {seed} дал карту, совпадающую с другим сидом.");
                    }
                    signatures[seed] = sig;

                    if (!ValidateMap(map, log, seed)) ok = false;

                    log($"  сид {seed}: {map.Width}×{map.Height}, земли {map.LandCount}, " +
                        $"областей {map.Regions.Count}, объектов {map.Objects.Count} (стражей {map.GuardedCount}), " +
                        $"декора {map.Decor.Count}, дорог {map.RoadCount}, стартов {map.Starts.Count}, сигнатура {sig}.");
                }

                if (signatures.Count > 1)
                {
                    int unique = 0;
                    var seen = new HashSet<string>();
                    foreach (var kv in signatures) if (seen.Add(kv.Value)) unique++;
                    if (unique < 2)
                    {
                        ok = false;
                        log("  ОШИБКА: разные сиды дают одинаковые карты.");
                    }
                }
            }

            log("");
            log(ok ? "Генератор карт: проверки пройдены." : "Генератор карт: есть ошибки (см. выше).");
            return ok;
        }

        /// <summary>Проверки одной карты: связность, регионы, старты, объекты.</summary>
        public static bool ValidateMap(AdventureMap map, Action<string> log, int seed)
        {
            bool ok = true;
            if (map.Width <= 0 || map.Height <= 0 || map.Tiles == null || map.Tiles.Length != map.Width * map.Height)
            {
                log($"  ОШИБКА: у карты (сид {seed}) битая сетка.");
                return false;
            }

            int land = 0, unassigned = 0;
            var seen = new bool[map.Tiles.Length];
            var queue = new Queue<int>();
            int startTile = -1;
            for (int i = 0; i < map.Tiles.Length; i++)
            {
                var t = map.Tiles[i];
                if (t.sea) continue;
                land++;
                if (t.region < 0 || t.region >= map.Regions.Count) unassigned++;
                if (startTile < 0) { startTile = i; queue.Enqueue(i); seen[i] = true; }
            }
            if (land == 0)
            {
                log($"  ОШИБКА: на карте (сид {seed}) нет суши.");
                return false;
            }
            if (unassigned > 0)
            {
                ok = false;
                log($"  ОШИБКА: {unassigned} тайлов суши без региона.");
            }

            // связность суши по 4 направлениям
            int reached = 1;
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                int x = cur % map.Width, y = cur / map.Width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + AdventureMap.Dx[d], ny = y + AdventureMap.Dy[d];
                    if (!map.InBounds(nx, ny)) continue;
                    int ni = ny * map.Width + nx;
                    if (map.Tiles[ni].sea || seen[ni]) continue;
                    seen[ni] = true;
                    reached++;
                    queue.Enqueue(ni);
                }
            }
            if (reached != land)
            {
                ok = false;
                log($"  ОШИБКА: суша не связна — достижимо {reached} из {land} тайлов.");
            }

            foreach (var region in map.Regions)
            {
                if (region.tiles.Count == 0)
                {
                    ok = false;
                    log($"  ОШИБКА: регион «{region.name}» пуст.");
                }
            }

            if (map.Starts.Count == 0)
            {
                ok = false;
                log("  ОШИБКА: нет стартовых позиций.");
            }
            else if (map.Starts.Count < map.PlayerCount)
            {
                ok = false;
                log($"  ОШИБКА: стартов {map.Starts.Count}, а игроков {map.PlayerCount}.");
            }

            // объекты: на суше, без наложений, все из каталога
            var occupied = new HashSet<int>();
            foreach (var obj in map.Objects)
            {
                if (obj.removed) continue;
                var tile = map.Tile(obj.x, obj.y);
                if (tile == null || tile.sea)
                {
                    ok = false;
                    log($"  ОШИБКА: объект {obj.defId} стоит не на суше ({obj.x},{obj.y}).");
                    continue;
                }
                int key = obj.y * map.Width + obj.x;
                if (!occupied.Add(key))
                {
                    ok = false;
                    log($"  ОШИБКА: два объекта на тайле ({obj.x},{obj.y}).");
                }
            }

            // декор: только суша и только свободные тайлы (он не должен «съедать» объекты)
            foreach (var deco in map.Decor)
            {
                var tile = map.Tile(deco.x, deco.y);
                if (tile == null || tile.sea)
                {
                    ok = false;
                    log($"  ОШИБКА: декор {deco.defId} стоит не на суше ({deco.x},{deco.y}).");
                    continue;
                }
                if (occupied.Contains(deco.y * map.Width + deco.x))
                {
                    ok = false;
                    log($"  ОШИБКА: декор перекрывает объект ({deco.x},{deco.y}).");
                }
            }

            if (map.Objects.Count < map.LandCount / 40)
            {
                ok = false;
                log($"  ОШИБКА: слишком мало объектов ({map.Objects.Count}) на {map.LandCount} тайлов суши.");
            }

            // в стартовых областях должны быть ресурсы
            foreach (var start in map.Starts)
            {
                int resources = 0;
                foreach (var obj in map.Objects)
                {
                    if (obj.region != start.region) continue;
                    if (obj.category == "resource") resources++;
                }
                if (resources == 0)
                {
                    ok = false;
                    log($"  ОШИБКА: в стартовой области игрока {start.player} нет ресурсных объектов.");
                }
            }

            return ok;
        }

        /// <summary>Короткая сигнатура карты для сравнения сидов.</summary>
        public static string Signature(AdventureMap map)
        {
            if (map == null) return "null";
            var sb = new StringBuilder(256);
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < map.Tiles.Length; i++)
                {
                    var t = map.Tiles[i];
                    uint v = (uint)(t.region + 2) * 31u + (uint)(int)t.terrain * 7u + (t.sea ? 101u : 0u) + (t.road ? 211u : 0u);
                    h = (h ^ v) * 16777619u;
                }
                foreach (var o in map.Objects)
                {
                    if (o.removed) continue;
                    uint v = (uint)(StableHash.Of(o.defId) & 0x7fffffff) + (uint)o.x * 3u + (uint)o.y * 5u + (uint)o.player * 11u;
                    h = (h ^ v) * 16777619u;
                }
                sb.Append(h.ToString("x8", CultureInfo.InvariantCulture));
            }
            sb.Append('-').Append(map.Regions.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append('-').Append(map.Objects.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append('-').Append(map.LandCount.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
