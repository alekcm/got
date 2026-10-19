using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IronThrone.Core;
using IronThrone.Core.AI;
using IronThrone.Core.Battle;
using IronThrone.Core.Content;
using IronThrone.Core.Game;

namespace IronThrone.Headless
{
    /// <summary>
    /// Headless-харнесс: загружает контент, валидирует ссылки, прогоняет обе эпохи
    /// на 220 ходов с авторазрешением боёв и печатает отчёт.
    /// </summary>
    internal static class Program
    {
        private const int DefaultTurns = 220;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            string root = args.Length > 0 ? args[0] : FindContentDir();
            int turns = args.Length > 1 && int.TryParse(args[1], out var t) ? t : DefaultTurns;
            bool quick = args.Contains("--quick");
            bool randomMap = args.Contains("--random");
            bool mapCheck = args.Contains("--mapcheck");
            bool saveTest = args.Contains("--savetest");
            if (quick) turns = Math.Min(turns, 40);

            if (root == null || !Directory.Exists(root))
            {
                Console.Error.WriteLine("Папка IronThroneData не найдена.");
                return 2;
            }

            Console.WriteLine($"Контент: {Path.GetFullPath(root)}");
            ContentDb db;
            try
            {
                db = ContentLoader.LoadFromDirectory(root);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ОШИБКА загрузки контента: " + ex);
                return 1;
            }

            PrintSummary(db);

            bool ok = true;
            if (db.LoadWarnings.Count == 0)
            {
                Console.WriteLine("Валидация: предупреждений нет.");
            }
            else
            {
                Console.WriteLine($"Валидация: {db.LoadWarnings.Count} предупреждений");
                foreach (var group in db.LoadWarnings.GroupBy(NormalizeTag).OrderBy(g => g.Key))
                {
                    Console.WriteLine($"  [{group.Key}] {group.Count()}");
                    foreach (var msg in group.Take(200)) Console.WriteLine("    - " + msg);
                    
                }
                ok = false;
            }

            if (saveTest) ok &= SaveTest(db);
            if (args.Contains("--battletest")) ok &= BattleTest(db);
            if (args.Contains("--exportmap"))
            {
                var m = MapGenerator.Generate(db.Map, 20261019);
                var outDto = new MapExport
                {
                    width = m.width, height = m.height,
                    provinces = m.provinces.Select(p => new ProvinceExport
                    {
                        id = p.id, name = p.name, region = p.region, house = p.house,
                        tiles = p.tiles, settlement = p.settlement, terrain = p.terrain, port = p.port,
                        isCapital = p.isCapital, kind = p.kind,
                    }).ToList(),
                    tiles = m.tiles.Select(t => new TileExport { c = t.c, r = t.r, sea = t.sea, province = t.province, region = t.region }).ToList(),
                };
                string mapPath = Path.Combine(Path.GetTempPath(), "ironthrone_random_map.json");
                File.WriteAllText(mapPath, JsonWriter.Write(outDto));
                Console.WriteLine("Карта выгружена: " + mapPath);
            }

            if (mapCheck)
            {
                int okMaps = 0;
                for (int seed = 1; seed <= 20; seed++)
                {
                    var problems = CheckGeneratedMap(db, seed);
                    if (problems.Count == 0) okMaps++;
                    else
                    {
                        Console.WriteLine($"  карта seed={seed}:");
                        foreach (var p in problems) Console.WriteLine("    - " + p);
                    }
                }
                Console.WriteLine($"Случайные карты: {okMaps}/20 прошли проверки");
                ok &= okMaps == 20;
            }

            foreach (var era in db.Eras)
            {
                if (!SimulateEra(db, era, turns, "canonical")) ok = false;
                if (randomMap && !SimulateEra(db, era, turns, "random")) ok = false;
            }

            Console.WriteLine(ok ? "\nИТОГ: OK" : "\nИТОГ: есть замечания (см. выше)");
            return ok ? 0 : 1;
        }

        private static string NormalizeTag(string msg)
        {
            int a = msg.IndexOf('[');
            int b = msg.IndexOf(']');
            if (a >= 0 && b > a) return msg.Substring(a + 1, b - a - 1);
            return "прочее";
        }

        private static string FindContentDir()
        {
            var candidates = new List<string>
            {
                Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/IronThrone/Resources/IronThroneData"),
                Path.Combine(Directory.GetCurrentDirectory(), "../../Assets/IronThrone/Resources/IronThroneData"),
                Path.Combine(Directory.GetCurrentDirectory(), "Assets/IronThrone/Resources/IronThroneData"),
            };
            return candidates.Select(Path.GetFullPath).FirstOrDefault(Directory.Exists);
        }

        private static void PrintSummary(ContentDb db)
        {
            Console.WriteLine("─" + new string('─', 58));
            Console.WriteLine($"Эпохи:        {db.Eras.Count}");
            Console.WriteLine($"Фракции:      {db.Factions.Count}");
            Console.WriteLine($"Дома:         {db.Houses.Count} (карта: {db.Map.houses.Count})");
            Console.WriteLine($"Провинции:    {db.Map.provinces.Count}, клетки: {db.Map.tiles.Count}");
            Console.WriteLine($"Отряды:       {db.Units.Count}");
            Console.WriteLine($"Герои:        {db.Heroes.Count}");
            Console.WriteLine($"Технологии:   {db.Techs.Count}");
            Console.WriteLine($"Фокусы:       {db.Focuses.Count}");
            Console.WriteLine($"Постройки:    {db.Buildings.Count}");
            Console.WriteLine($"Законы:       {db.Laws.Count}");
            Console.WriteLine($"Титулы:       {db.Titles.Count}");
            Console.WriteLine($"Советники:    {db.Advisors.Count}");
            Console.WriteLine($"Веры:         {db.Faiths.Count}");
            Console.WriteLine($"События:      {db.Events.Count}");
            Console.WriteLine($"Квесты:       {db.Quests.Count}");
            Console.WriteLine("─" + new string('─', 58));
        }

        private static bool SimulateEra(ContentDb db, EraDef era, int turns, string mapMode)
        {
            Console.WriteLine($"\n=== Эпоха «{era.name}» ({era.id}), {turns} ходов, карта: {mapMode} ===");
            var factionIds = era.factions
                .Where(id => db.FactionById.TryGetValue(id, out var f) && f.playable && f.era == era.id)
                .ToList();

            if (factionIds.Count == 0)
            {
                Console.WriteLine("  нет играбельных фракций в эпохе");
                return false;
            }

            bool ok = true;
            foreach (var fid in factionIds)
            {
                if (!RunGame(db, era, fid, turns, mapMode, out var report))
                {
                    ok = false;
                    Console.WriteLine(report);
                }
            }
            if (ok) Console.WriteLine($"  все {factionIds.Count} фракций отыграны без ошибок");
            return ok;
        }

        public class MapExport
        {
            public int width, height;
            public List<ProvinceExport> provinces = new List<ProvinceExport>();
            public List<TileExport> tiles = new List<TileExport>();
        }
        public class ProvinceExport
        {
            public string id, name, region, house, terrain, kind;
            public List<List<int>> tiles = new List<List<int>>();
            public List<int> settlement = new List<int>();
            public bool port, isCapital;
        }
        public class TileExport { public int c, r; public bool sea; public string province, region; }

        private static bool SaveTest(ContentDb db)
        {
            Console.WriteLine("\n=== Проверка сохранения/загрузки ===");
            try
            {
                var state = GameSetup.CreateNewGame(db, "fivekings", "five_stark", seed: 424242,
                    mapMode: "random");
                TurnEngine.StartGame(state);
                for (int i = 0; i < 12; i++)
                {
                    ResolvePending(state);
                    var p = state.Faction("five_stark");
                    if (p != null && p.alive) FactionAI.TakeTurn(state, p);
                    TurnEngine.EndTurn(state);
                }

                string path = Path.Combine(Path.GetTempPath(), "ironthrone_save.json");
                SaveSystem.SaveToFile(state, path);
                long size = new FileInfo(path).Length;
                var loaded = SaveSystem.LoadFromFile(db, path);

                bool same = loaded.Turn == state.Turn &&
                            loaded.Provinces.Count == state.Provinces.Count &&
                            loaded.Factions.Count == state.Factions.Count &&
                            loaded.Armies.Count == state.Armies.Count &&
                            loaded.Heroes.Count == state.Heroes.Count &&
                            loaded.MinorHouses.Count == state.MinorHouses.Count &&
                            loaded.CustomMap != null &&
                            loaded.CustomMap.provinces.Count == state.Map.provinces.Count &&
                            loaded.PlayerFaction == state.PlayerFaction;
                var a = state.Faction("five_stark"); var b = loaded.Faction("five_stark");
                same &= a != null && b != null && a.gold == b.gold && a.food == b.food && a.techs.Count == b.techs.Count;

                // продолжаем игру с загруженного состояния
                for (int i = 0; i < 6; i++)
                {
                    var p = loaded.Faction("five_stark");
                    if (p != null && p.alive) FactionAI.TakeTurn(loaded, p);
                    TurnEngine.EndTurn(loaded);
                }
                Console.WriteLine($"  сохранение: {size / 1024} КБ, ход {state.Turn}, загружено: {(same ? "совпадает" : "РАСХОЖДЕНИЕ")}, " +
                                  $"после 6 ходов: ход {loaded.Turn}, провинций {loaded.CountControlled("five_stark")}");
                if (!same)
                {
                    Console.WriteLine($"    было: ход {state.Turn}, провинций {state.Provinces.Count}, армий {state.Armies.Count}, героев {state.Heroes.Count}");
                    Console.WriteLine($"    стало: ход {loaded.Turn}, провинций {loaded.Provinces.Count}, армий {loaded.Armies.Count}, героев {loaded.Heroes.Count}, карта {(loaded.CustomMap != null)}");
                }
                return same;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  ОШИБКА сохранения: " + ex);
                return false;
            }
        }

        /// <summary>Прогоняет тактический бой тем же путём, что и ручной экран Unity.</summary>
        private static bool BattleTest(ContentDb db)
        {
            Console.WriteLine("\n=== Проверка тактического боя ===");
            try
            {
                var state = GameSetup.CreateNewGame(db, "fivekings", "five_stark", seed: 777, mapMode: "canonical");
                TurnEngine.StartGame(state);
                var prov = state.Province("winterfell") ?? state.Provinces.Values.First();
                var attackerFaction = state.Faction("five_stark");
                var defenderFaction = state.Factions.Values.First(f => f.id != "five_stark" && f.alive);
                var attacker = MilitarySystem.CreateArmy(state, attackerFaction, prov.id, "Тестовый авангард");
                var defender = MilitarySystem.CreateArmy(state, defenderFaction, prov.id, "Тестовый гарнизон");
                foreach (var unit in db.Units.Where(u => u.faction == "stark" || u.faction == "shared").Take(4))
                    attacker.units.Add(new UnitInstance(unit.id, unit.name) { hp = 100, morale = 100 });
                foreach (var unit in db.Units.Where(u => u.faction == "shared").Skip(2).Take(3))
                    defender.units.Add(new UnitInstance(unit.id, unit.name) { hp = 100, morale = 100 });
                if (attacker.units.Count == 0 || defender.units.Count == 0)
                {
                    Console.WriteLine("  нет подходящих отрядов для боя");
                    return false;
                }

                var field = BattleEngine.Create(state, attacker, defender, prov);
                int guard = 0;
                while (!field.finished && guard++ < 300)
                {
                    var active = BattleEngine.Active(field);
                    if (active == null) { BattleEngine.NextUnit(field); continue; }
                    BattleEngine.AutoActivate(field, active);
                    if (!field.finished) BattleEngine.EndUnitTurn(field);
                }
                if (!field.finished) BattleEngine.Finish(field, field.SideAlive(0));
                var result = BattleEngine.ApplyResult(field, attacker, defender);
                bool ok = field.finished && result.rounds.Count > 0 && result.province == prov.id;
                Console.WriteLine($"  бой: {result.Summary}, раундов {field.round}, " +
                                  $"потери {result.attackerLosses}/{result.defenderLosses}, отрядов осталось {attacker.units.Count}/{defender.units.Count}");

                // путь MilitarySystem.Fight с подменённым резолвером (как в Unity)
                int battlesBefore = state.Battles.Count;
                MilitarySystem.Resolver = (s2, a2, d2, p2, siege2) =>
                {
                    var f2 = BattleEngine.Create(s2, a2, d2, p2, siege2);
                    BattleEngine.RunAuto(f2);
                    return BattleEngine.ApplyResult(f2, a2, d2);
                };
                var result2 = MilitarySystem.Fight(state, attacker, defender, prov);
                MilitarySystem.Resolver = null;
                ok &= state.Battles.Count == battlesBefore + 1 && result2 != null;
                Console.WriteLine($"  Fight через резолвер: {result2.Summary}, записей в хронике боёв {state.Battles.Count}");
                return ok;
            }
            catch (Exception ex)
            {
                MilitarySystem.Resolver = null;
                Console.WriteLine("  ОШИБКА боя: " + ex);
                return false;
            }
        }

        private static List<string> CheckGeneratedMap(ContentDb db, int seed)
        {
            var problems = new List<string>();
            MapData map;
            try { map = MapGenerator.Generate(db.Map, seed); }
            catch (Exception ex) { problems.Add("исключение генерации: " + ex.Message); return problems; }

            if (map.provinces.Count != db.Map.provinces.Count)
                problems.Add($"провинций {map.provinces.Count}, ожидалось {db.Map.provinces.Count}");
            if (map.seaRoutes.Count < 10) problems.Add($"слишком мало морских маршрутов: {map.seaRoutes.Count}");

            foreach (var t in db.Map.provinces)
            {
                if (!map.ProvinceById.ContainsKey(t.id)) { problems.Add("нет провинции " + t.id); continue; }
                var p = map.ProvinceById[t.id];
                if (p.tiles.Count == 0) problems.Add("пустая провинция " + p.id);
                if (p.region != t.region) problems.Add($"провинция {p.id} сменила регион");
                if (p.house != t.house) problems.Add($"провинция {p.id} сменила дом");
                // связность клеток провинции
                var set = new HashSet<(int, int)>(p.tiles.Select(x => (x[0], x[1])));
                if (set.Count > 1)
                {
                    var seen = new HashSet<(int, int)>(); var q = new Queue<(int, int)>();
                    var start = set.First(); q.Enqueue(start); seen.Add(start);
                    while (q.Count > 0)
                    {
                        var cur = q.Dequeue();
                        foreach (var d in new[] { (1,0),(1,-1),(0,-1),(-1,0),(-1,1),(0,1) })
                        {
                            var n = (cur.Item1 + d.Item1, cur.Item2 + d.Item2);
                            if (set.Contains(n) && seen.Add(n)) q.Enqueue(n);
                        }
                    }
                    if (seen.Count != set.Count) problems.Add($"провинция {p.id} не связна");
                }
            }

            // позиционные ограничения
            var winterfell = map.ProvinceById.TryGetValue("winterfell", out var wf) ? wf : null;
            var sunspear = map.ProvinceById.TryGetValue("sunspear", out var ss) ? ss : null;
            var pyke = map.ProvinceById.TryGetValue("pyke", out var pk) ? pk : null;
            if (winterfell != null && winterfell.tiles[0][1] > map.height * 0.4) problems.Add("Винтерфелл не на севере");
            if (sunspear != null && sunspear.tiles[0][1] < map.height * 0.6) problems.Add("Санспир не на юге");
            if (pyke != null)
            {
                if (pyke.region != "islands") problems.Add("Пайк не в регионе островов");
                var landNeighbors = pyke.neighbors.Select(id => map.ProvinceById[id])
                    .Where(n => n.region != "islands").ToList();
                if (landNeighbors.Count > 0) problems.Add("Пайк соединён с материком");
            }
            // у каждого региона столько же провинций, сколько в каноне
            foreach (var g in db.Map.provinces.GroupBy(p => p.region))
            {
                int got = map.provinces.Count(p => p.region == g.Key);
                if (got != g.Count()) problems.Add($"регион {g.Key}: {got} провинций вместо {g.Count()}");
            }
            return problems;
        }

        private static bool RunGame(ContentDb db, EraDef era, string factionId, int turns, string mapMode, out string report)
        {
            var errors = new List<string>();
            GameState state;
            try
            {
                var advisors = db.AdvisorsFor(era, db.FactionById[factionId])
                    .OrderBy(a => string.IsNullOrEmpty(a.faction) ? 1 : 0)
                    .Select(a => a.id).Take(2).ToList();
                state = GameSetup.CreateNewGame(db, era.id, factionId, seed: 1337 + factionId.GetHashCode(), chosenAdvisors: advisors, mapMode: mapMode);
                TurnEngine.StartGame(state);
            }
            catch (Exception ex)
            {
                report = $"  [{factionId}] ОШИБКА создания партии: {ex}";
                return false;
            }

            int startGold = state.Faction(factionId)?.gold ?? 0;
            int peakProvinces = 0, battles = 0, events = 0, victories = 0;
            var rng = new Random(7);

            for (int i = 0; i < turns && state.Phase != GamePhase.GameOver; i++)
            {
                try
                {
                    ResolvePending(state);
                    var player = state.Faction(factionId);
                    if (player != null && player.alive)
                        FactionAI.TakeTurn(state, player);

                    ResolvePending(state);
                    TurnEngine.EndTurn(state);
                    ResolvePending(state);

                    var p = state.Faction(factionId);
                    if (p != null)
                    {
                        peakProvinces = Math.Max(peakProvinces, state.ControlledBy(p.id).Count());
                        if (state.PendingVote != null) { PoliticsSystem.ResolveVote(state, autoPlayer: true); victories++; }
                    }
                    battles = state.Battles.Count;
                    events = state.PendingEvents.Count;
                }
                catch (Exception ex)
                {
                    errors.Add($"ход {state.Turn}: {(errors.Count == 0 ? ex.ToString() : ex.GetType().Name + ": " + ex.Message)}");
                    if (errors.Count >= 5) break;
                }
            }

            var res = state.Faction(factionId);
            if (res == null) { report = $"  [{factionId}] фракция исчезла из состояния"; return false; }

            string status = state.Phase == GamePhase.GameOver
                ? $"игра окончена: {state.VictoryWinner ?? "нет победителя"} ({state.VictoryType})"
                : "лимит ходов";

            report = $"  [{factionId}] ходов {state.Turn - 1}, провинций {state.ControlledBy(factionId).Count()} (макс {peakProvinces}), " +
                     $"золото {startGold}→{res.gold}, армий {res.armies.Count}, освоено {res.claimedProvinces}, вассалов {res.vassals.Count}, войн {state.Wars.Count(w => w.active)}, боёв {battles}, " +
                     $"признание {res.recognition:0}, победа: {status}";
            if (errors.Count > 0)
            {
                report += "\n    ОШИБКИ:\n      " + string.Join("\n      ", errors);
                return false;
            }
            Console.WriteLine(report);
            return true;
        }

        /// <summary>Авторазрешение отложенных событий и голосований, чтобы прогон не останавливался.</summary>
        private static void ResolvePending(GameState state)
        {
            int guard = 0;
            while (state.PendingEvents.Count > 0 && guard++ < 50)
            {
                var pending = state.PendingEvents[0];
                var ev = state.Content.EventById.TryGetValue(pending.eventId, out var e) ? e : null;
                var f = state.Faction(pending.faction);
                if (ev == null || f == null || ev.choices.Count == 0)
                {
                    state.PendingEvents.RemoveAt(0);
                    continue;
                }
                int pick = 0;
                for (int i = 0; i < ev.choices.Count; i++)
                {
                    var c = ev.choices[i];
                    if (c.cost == null || c.cost.IsZero || (f.gold >= c.cost.gold && f.materials >= c.cost.materials &&
                        f.food >= c.cost.food && f.influence >= c.cost.influence && f.piety >= c.cost.piety))
                    { pick = i; break; }
                }
                var err = EventSystem.Choose(state, 0, pick);
                if (err != null) state.PendingEvents.RemoveAt(0);
            }

            if (state.PendingVote != null)
                PoliticsSystem.ResolveVote(state, autoPlayer: true);
        }
    }
}
