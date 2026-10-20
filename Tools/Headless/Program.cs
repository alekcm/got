using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IronThrone.Core;
using IronThrone.Core.AI;
using IronThrone.Core.Battle;
using IronThrone.Core.Content;
using IronThrone.Core.Game;
using IronThrone.Core.Worldgen;

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

            // Аргументы: [путь к IronThroneData] [ходы] [флаги]. Флаги можно ставить
            // первыми — путь и число ходов выбираются из позиционных аргументов.
            string root = null;
            int turns = DefaultTurns;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    if (args[i] == "--seeds") i++; // значение флага — не позиционный аргумент
                    continue;
                }
                if (root == null) root = args[i];
                else if (int.TryParse(args[i], out var parsedTurns)) turns = parsedTurns;
            }
            if (root == null) root = FindContentDir();
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

            // Генератор карт Songs of Conquest: проверяем детерминизм и качество карт
            // без Unity. Запуск: Tools/Headless --soqmap [--seeds 6]
            if (args.Contains("--soqmap"))
            {
                string mapGenDir = Path.Combine(root, "MapGen");
                Console.WriteLine("Генератор карт SoQ: " + Path.GetFullPath(mapGenDir));
                var pack = SoqRmgLoader.LoadFromDirectory(mapGenDir);
                int seedCount = 4;
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == "--seeds" && int.TryParse(args[i + 1], out var parsed)) seedCount = Math.Max(1, Math.Min(32, parsed));
                bool mapsOk = SoqMapCheck.Run(pack, seedCount, Console.WriteLine);
                return mapsOk ? 0 : 1;
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

            // лагеря набежчиков: все виды должны встречаться
            try
            {
                var probe = GameSetup.CreateNewGame(db, "fivekings", "five_stark", seed: 99);
                var kinds = probe.RaiderCamps.Select(c => c.kind).Distinct().OrderBy(x => x).ToList();
                Console.WriteLine($"Лагеря набежчиков: {probe.RaiderCamps.Count} ({string.Join(", ", kinds)})");
                foreach (var need in new[] { "pirates", "mountain clans", "wildlings", "reavers" })
                    if (!kinds.Contains(need)) Console.WriteLine($"  [набежчики] не создан лагерь вида '{need}'");
            }
            catch (Exception ex) { Console.WriteLine("  [набежчики] ошибка: " + ex.Message); }

            // у каждой играбельной фракции должно быть из кого выбрать двух советников
            foreach (var era in db.Eras)
            {
                foreach (var f in db.Factions.Where(x => x.era == era.id && x.playable))
                {
                    var pool = db.AdvisorsForFaction(era, f);
                    if (pool.Count < 4)
                        Console.WriteLine($"  [советники] {f.id} ({era.id}): в пуле только {pool.Count}");
                }
            }

            if (saveTest) ok &= SaveTest(db);
            if (args.Contains("--battletest")) ok &= BattleTest(db);
            if (args.Contains("--traitstest")) ok &= TraitTest(db);
            if (args.Contains("--p0test")) ok &= P0Test(db);
            if (args.Contains("--p1test")) ok &= P1Test(db);
            if (args.Contains("--p2test")) ok &= P2Test(db);
            if (args.Contains("--p3test")) ok &= P3Test(db);
            if (args.Contains("--ambushtest")) ok &= AmbushTest(db);
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

            if (args.Contains("--debug-ai"))
            {
                int aiIdx = Array.IndexOf(args, "--debug-ai");
                string debugFaction = aiIdx + 1 < args.Length && !args[aiIdx + 1].StartsWith("--")
                    ? args[aiIdx + 1] : "five_stannis";
                var st = GameSetup.CreateNewGame(db, "fivekings", debugFaction, seed: 1337, mapMode: "canonical");
                TurnEngine.StartGame(st);
                var f = st.Faction(debugFaction);
                Console.WriteLine($"\n[debug] {f.def.name}: провинций {st.ControlledBy(f.id).Count()}, армий {f.armies.Count}");
                foreach (var aid in f.armies)
                {
                    var a = st.Armies[aid];
                    Console.WriteLine($"  армия {a.name} в {a.province}, движение {a.movementLeft}/{a.maxMovement}, юнитов {a.units.Count}");
                    var prov = st.Province(a.province);
                    foreach (var nid in prov.def.neighbors)
                        Console.WriteLine($"    суша {nid}: можно={MilitarySystem.CanMove(st, a, nid, out var why)} {why}");
                    foreach (var route in st.Map.seaRoutes.Where(r => r.Contains(a.province)))
                    {
                        var other = route[0] == a.province ? route[1] : route[0];
                        Console.WriteLine($"    море {other}: можно={MilitarySystem.CanMove(st, a, other, out var why2)} {why2}");
                    }
                }
            }

            if (args.Contains("--debug-pol"))
            {
                int pi = Array.IndexOf(args, "--debug-pol");
                string polFaction = pi + 1 < args.Length && !args[pi + 1].StartsWith("--") ? args[pi + 1] : "five_lannister";
                bool runAi = args.Contains("--ai");
                var st = GameSetup.CreateNewGame(db, "fivekings", polFaction, seed: 1337, mapMode: "canonical");
                TurnEngine.StartGame(st);
                for (int turnNo = 1; turnNo <= 90; turnNo++)
                {
                    if (runAi)
                    {
                        var pp = st.Faction(polFaction);
                        if (pp != null && pp.alive) FactionAI.TakeTurn(st, pp);
                    }
                    TurnEngine.EndTurn(st);
                    if (turnNo % 5 != 0) continue;
                    var f = st.Faction(polFaction);
                    var seats = st.Council.seats.Where(kv => kv.Value == f.id).Select(kv => kv.Key).ToList();
                    var holders = string.Join("; ", st.Council.seats.GroupBy(kv => kv.Value)
                        .Select(g => $"{st.FactionName(g.Key)} {g.Count()}"));
                    Console.WriteLine($"  [полит {turnNo}] трон: {st.FactionName(st.Council.throneHolder)}, признание: {f.recognition:0.#}, своих: {seats.Count}, поддержка: {PoliticsSystem.SeatsBacking(st, f.id)}/{VictorySystem.PoliticalSeatsRequired}, места: {holders}, влияния: {f.influence:0}, активных законов: {st.ActiveLaws.Count}, фокусов: {f.completedFocuses.Count}, войн: {st.Wars.Count(w => w.active)}, вера {f.faith}: доля {FaithSystem.FaithShare(st, f):P0}, глава {FaithSystem.IsHead(st, f)}, реформа {f.HasFlag("faith_reformed")}");
                }
            }

            if (args.Contains("--debug-econ"))
            {
                var st = GameSetup.CreateNewGame(db, "fivekings", "five_lannister", seed: 12345, mapMode: "canonical");
                TurnEngine.StartGame(st);
                for (int turnNo = 1; turnNo <= 220; turnNo++)
                {
                    TurnEngine.EndTurn(st);
                    if (turnNo % 40 != 0) continue;
                    var top = st.Factions.Values.Where(f => f.alive)
                        .OrderByDescending(f => f.tradeDominance).Take(4)
                        .Select(f => $"{f.def.shortName ?? f.def.name} {f.tradeDominance:0}%")
                        .ToList();
                    Console.WriteLine($"  [эконом {turnNo}] " + string.Join(" · ", top));
                }
            }

            if (args.Contains("--victorystats"))
            {
                VictoryStats(db, seeds: 3, turns: 220);
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

        /// <summary>Проверки P0: герои в армиях, разведка, отмена законов.</summary>
        private static bool P0Test(ContentDb db)
        {
            Console.WriteLine("\n=== P0: герои, разведка, законы ===");
            bool ok = true;
            try
            {
                var state = GameSetup.CreateNewGame(db, "fivekings", "five_lannister", seed: 31337, mapMode: "canonical");
                TurnEngine.StartGame(state);
                var f = state.Faction("five_lannister");
                var armies = f.armies.Select(id => state.Armies[id]).Where(a => a.alive).ToList();

                // 1. герой ко двору и в армию
                var free = f.heroes.Select(id => MilitarySystem.Hero(state, id))
                    .First(h => h != null && h.alive && h.armyId == null);
                bool assigned = MilitarySystem.AssignHero(state, f, free.defId, armies[0].id, out var aerr);
                bool heroOk = assigned && armies[0].heroId == free.defId && free.armyId == armies[0].id;
                Console.WriteLine($"  герой {free.name} → {armies[0].name}: {(heroOk ? "назначен" : "ОШИБКА " + aerr)}");
                ok &= heroOk;

                // 2. перевод в другую армию даёт перезарядку, третий перевод запрещён
                bool moved = MilitarySystem.AssignHero(state, f, free.defId, armies[1].id, out var merr);
                bool cooldown = moved && free.reassignCooldown == MilitarySystem.HeroReassignCooldown;
                bool blocked = !MilitarySystem.AssignHero(state, f, free.defId, armies[2].id, out _);
                Console.WriteLine($"  перевод в другую армию: {(moved ? "разрешён" : "ОШИБКА " + merr)}, " +
                                  $"перезарядка {free.reassignCooldown}, повторный перевод {(blocked ? "заблокирован" : "ОШИБКА")}");
                ok &= cooldown && blocked;

                // 3. способность героя в бою
                var prov = state.Province(armies[1].province);
                var enemy = state.Factions.Values.First(x => x.id != f.id && x.alive);
                var foeArmy = new ArmyState { id = "p0_foe", faction = enemy.id, province = prov.id, name = "тест" };
                foeArmy.units.Add(new UnitInstance("levy", "Ополчение") { hp = 100, morale = 100 });
                var field = BattleEngine.Create(state, armies[1], foeArmy, prov);
                bool canUse = BattleEngine.CanUseHeroAbility(field, 0);
                string heroLine = BattleEngine.UseHeroAbility(field, 0);
                bool usedOnce = canUse && heroLine != null && !BattleEngine.CanUseHeroAbility(field, 0);
                Console.WriteLine($"  способность героя: {(heroLine ?? "ОШИБКА")} → {(usedOnce ? "один раз за бой" : "ОШИБКА")}");
                ok &= usedOnce;

                // 4. разведка даёт донесение и бонус к атаке
                var enemy2 = state.Factions.Values.First(x => x.id != f.id && x.alive);
                EspionageSystem.Execute(state, f, "recon", enemy2.id);
                var intel = EspionageSystem.Intel(state, f.id, enemy2.id);
                bool intelOk = intel != null && EspionageSystem.HasFreshIntel(state, f.id, enemy2.id);
                Console.WriteLine($"  донесение о {enemy2.def.shortName}: {(intelOk ? $"получено (армий {intel.armies}, золото {intel.gold})" : "ОШИБКА")}");
                ok &= intelOk;

                var testArmy = new ArmyState { id = "p0_own", faction = f.id, province = prov.id, name = "тест" };
                testArmy.units.Add(new UnitInstance("men_at_arms", "Пехота дома") { hp = 100, morale = 100 });
                var withIntel = BattleEngine.Create(state, testArmy, foeArmy, prov);
                int atkWith = withIntel.units.First(u => u.side == 0).stats.attack;
                if (intel != null) intel.turn -= EspionageSystem.IntelFreshTurns + 1;
                var withoutIntel = BattleEngine.Create(state, testArmy, foeArmy, prov);
                int atkWithout = withoutIntel.units.First(u => u.side == 0).stats.attack;
                bool bonusOk = atkWith == atkWithout + 1;
                Console.WriteLine($"  атака со свежими данными {atkWith}, без них {atkWithout} → {(bonusOk ? "+1 работает" : "ОШИБКА")}");
                ok &= bonusOk;

                // 5. закон: принять, затем отменить, эффект снят
                var law = db.Laws.First(l => l.repealable && !state.ActiveLaws.Any(a => a.id == l.id)
                                            && PoliticsSystem.CanPropose(state, f, l, out _));
                float before = f.Mod(law.effects.First(e => e.op == "modifier").key);
                f.influence = 200;
                var vote = PoliticsSystem.ProposeLaw(state, f, law.id, out var lerr);
                if (vote != null) PoliticsSystem.ResolveVote(state, autoPlayer: true);
                bool passed = state.ActiveLaws.Any(a => a.id == law.id);
                string key = law.effects.First(e => e.op == "modifier").key;
                float applied = f.Mod(key);
                bool repealed = false, reverted = false;
                if (passed)
                {
                    f.influence = 200;
                    repealed = PoliticsSystem.RepealLaw(state, f, law.id, out var rerr) != null;
                    if (!repealed) Console.WriteLine("    причина отказа отмены: " + rerr);
                    if (repealed)
                    {
                        // в тесте совет голосует единогласно, чтобы проверить сам механизм отмены
                        foreach (var voter in state.Factions.Values.Where(x => x.alive))
                            state.PendingVote.votedFor[voter.id] = "yes";
                        PoliticsSystem.ResolveVote(state, autoPlayer: true);
                    }
                    repealed &= !state.ActiveLaws.Any(a => a.id == law.id);
                    reverted = Math.Abs(f.Mod(key) - before) < 0.001f;
                }
                Console.WriteLine($"  закон «{law.name}»: {(passed ? "принят" : "не принят")}, " +
                                  $"эффект {key} {before:0.##}→{applied:0.##}, отмена: {(repealed ? "прошла" : "ОШИБКА")}, " +
                                  $"эффект вернулся: {(reverted ? "да" : "ОШИБКА")}");
                ok &= passed && repealed && reverted;

                // 6. ИИ выбирает выгодный закон, а не случайный
                var ai = state.Factions.Values.First(x => !x.isPlayer && x.alive);
                var good = db.Laws.Where(l => PoliticsSystem.LawValue(state, ai, l) > 0).ToList();
                var bad = db.Laws.Where(l => PoliticsSystem.LawValue(state, ai, l) < 0).ToList();
                Console.WriteLine($"  оценка законов ИИ ({ai.def.shortName}): выгодных {good.Count}, вредных {bad.Count}, " +
                                  $"лучший «{(good.Count > 0 ? good.OrderByDescending(l => PoliticsSystem.LawValue(state, ai, l)).First().name : "—")}»");
                ok &= good.Count > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  ОШИБКА P0: " + ex);
                return false;
            }
            return ok;
        }

        /// <summary>P2: слияние и разделение армий, просьбы малых домов.</summary>
        private static bool P2Test(ContentDb db)
        {
            Console.WriteLine("\n=== P2: перестроения армий и просьбы малых домов ===");
            bool ok = true;
            try
            {
                var state = GameSetup.CreateNewGame(db, "fivekings", "five_lannister", seed: 5150, mapMode: "canonical");
                TurnEngine.StartGame(state);
                var f = state.Faction("five_lannister");
                var armies = f.armies.Select(id => state.Armies[id]).Where(a => a.alive).ToList();

                // 1. слияние: отделяем отряд, ведём в него героя и вливаем обратно
                var into = armies[0];
                MilitarySystem.ReleaseHero(state, into);
                int original = MilitarySystem.AliveUnits(into);
                var from = MilitarySystem.SplitArmy(state, f, into, 2, out var splitErr);
                int unitsFrom = from == null ? 0 : MilitarySystem.AliveUnits(from);
                var hero = f.heroes.Select(id => MilitarySystem.Hero(state, id))
                    .FirstOrDefault(h => h != null && h.alive && !h.wounded && h.reassignCooldown == 0 && h.armyId == null);
                bool heroAssigned = hero != null && from != null &&
                                    MilitarySystem.AssignHero(state, f, hero.defId, from.id, out _);
                string merr = null;
                bool merged = from != null && MilitarySystem.MergeArmies(state, f, into.id, from.id, out merr);
                bool mergedOk = merged && !from.alive && MilitarySystem.AliveUnits(into) == original &&
                                (!heroAssigned || into.heroId == hero.defId);
                Console.WriteLine($"  слияние: {original}→{original - unitsFrom}→{MilitarySystem.AliveUnits(into)} отр., " +
                                  $"герой {(heroAssigned ? (into.heroId == hero.defId ? "перешёл" : "ОШИБКА") : "не назначался")}, " +
                                  $"{(mergedOk ? "лимит строя учтён" : "ОШИБКА " + (splitErr ?? merr))}");
                ok &= mergedOk;

                // 2. разделение: новая армия без движения, нельзя отделить всё
                int beforeSplit = MilitarySystem.AliveUnits(into);
                var split = MilitarySystem.SplitArmy(state, f, into, 1, out var serr);
                bool splitOk = split != null && MilitarySystem.AliveUnits(split) == 1 &&
                               split.movementLeft == 0 && MilitarySystem.AliveUnits(into) == beforeSplit - 1;
                bool allBlocked = MilitarySystem.SplitArmy(state, f, into, MilitarySystem.AliveUnits(into), out _) == null;
                Console.WriteLine($"  разделение: {(splitOk ? $"1 отряд → {split.name}, движение 0" : "ОШИБКА " + serr)}, " +
                                  $"отделить всё {(allBlocked ? "нельзя" : "ОШИБКА")}");
                ok &= splitOk && allBlocked;

                // 3. просьбы малых домов: помочь, отказать, просрочить
                var house = state.MinorHouses.Values.First(m => !m.annexed && state.Provinces.Values.Any(p => p.house == m.house));
                var house2 = state.MinorHouses.Values.Where(m => !m.annexed && m.house != house.house &&
                                                                 state.Provinces.Values.Any(p => p.house == m.house)).First();
                f.minorRelations[house.house] = 20;
                f.minorRelations[house2.house] = 20;
                f.gold = 1000;
                var req = new MinorRequestState { house = house.house, faction = f.id, type = "gold", turnsLeft = 6 };
                state.MinorRequests.Add(req);
                bool accepted = MinorHouseSystem.AcceptRequest(state, f, req, out var aerr);
                bool acceptedOk = accepted && Math.Abs(MinorHouseSystem.Relation(state, f, house.house) - 32) < 0.01f &&
                                  !state.MinorRequests.Contains(req) && f.gold == 850;
                var req2 = new MinorRequestState { house = house2.house, faction = f.id, type = "materials", turnsLeft = 6 };
                state.MinorRequests.Add(req2);
                bool declined = MinorHouseSystem.DeclineRequest(state, f, req2, out var derr);
                bool declinedOk = declined && MinorHouseSystem.Relation(state, f, house2.house) == 14;
                var req3 = new MinorRequestState { house = house.house, faction = f.id, type = "protection", turnsLeft = 1 };
                state.MinorRequests.Add(req3);
                MinorHouseSystem.ProcessTurn(state);
                bool expiredOk = !state.MinorRequests.Contains(req3) &&
                                 MinorHouseSystem.Relation(state, f, house.house) == 29;
                Console.WriteLine($"  просьбы: помощь {(acceptedOk ? "+12 отношений, −150 золота" : "ОШИБКА " + aerr)}, " +
                                  $"отказ {(declinedOk ? "−6" : "ОШИБКА " + derr)}, просрочка {(expiredOk ? "−3" : "ОШИБКА")}");
                ok &= acceptedOk && declinedOk && expiredOk;

                // 4. зона контроля: выход из-под удара стоит +1, а атака врага — нет
                var enemyFaction = state.Factions.Values.First(x => x.id != f.id && x.alive);
                state.Wars.Add(new WarState { attacker = f.id, defender = enemyFaction.id, aggressor = f.id, active = true });
                var ownProv = state.Provinces.Values.First(p => p.controller == f.id && p.def.neighbors.Distinct().Count() >= 2);
                var nbs = ownProv.def.neighbors.Distinct().ToList();
                var foeProv = state.Province(nbs[0]);
                var exitProv = state.Province(nbs[1]);
                ownProv.controller = enemyFaction.id;
                exitProv.controller = f.id;
                var foe = new ArmyState { id = "p2_zoc_foe", faction = enemyFaction.id, province = foeProv.id, name = "засада" };
                foe.units.Add(new UnitInstance("levy", "Ополчение") { hp = 100, morale = 100 });
                state.Armies[foe.id] = foe;
                var scout = new ArmyState { id = "p2_zoc_own", faction = f.id, province = ownProv.id, name = "разъезд", movementLeft = 1, maxMovement = 2 };
                scout.units.Add(new UnitInstance("levy", "Ополчение") { hp = 100, morale = 100 });
                state.Armies[scout.id] = scout;
                int exitCost = MilitarySystem.MoveCost(state, scout, ownProv.id, exitProv.id);
                int attackCost = MilitarySystem.MoveCost(state, scout, ownProv.id, foeProv.id);
                bool zocOk = exitCost == 2 && attackCost == 1 && MilitarySystem.InEnemyZoc(state, scout, ownProv.id) &&
                             !MilitarySystem.CanMove(state, scout, exitProv.id, out _);
                Console.WriteLine($"  зона контроля: выход {exitCost}, атака {attackCost}, " +
                                  $"{(zocOk ? "прорыв возможен только через бой" : "ОШИБКА")}");
                ok &= zocOk;

                // 5. просьбы сохраняются и восстанавливаются
                var keep = new MinorRequestState { house = house.house, faction = f.id, type = "protection", turnsLeft = 4 };
                state.MinorRequests.Add(keep);
                string path = Path.Combine(Path.GetTempPath(), "ironthrone_p2_save.json");
                SaveSystem.SaveToFile(state, path);
                var loaded = SaveSystem.LoadFromFile(db, path);
                bool saveOk = loaded.MinorRequests.Any(r => r.house == keep.house && r.faction == f.id &&
                                                            r.type == "protection" && r.turnsLeft == 4);
                Console.WriteLine($"  сохранение просьб: {(saveOk ? "восстановлены" : "ОШИБКА")}");
                ok &= saveOk;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  ОШИБКА P2: " + ex);
                return false;
            }
            return ok;
        }

        /// <summary>Засады: укрытие, внезапность, контрразведка и снятие после боя.</summary>
        private static bool AmbushTest(ContentDb db)
        {
            Console.WriteLine("\n=== Засады: укрытие, внезапность, разведка ===");
            bool ok = true;
            try
            {
                var state = GameSetup.CreateNewGame(db, "fivekings", "five_lannister", seed: 9090, mapMode: "canonical");
                TurnEngine.StartGame(state);
                var f = state.Faction("five_lannister");
                var enemy = state.Factions.Values.First(x => x.id != f.id && x.alive);
                state.Wars.Add(new WarState { attacker = enemy.id, defender = f.id, aggressor = enemy.id, active = true });

                var own = state.Provinces.Values.Where(p => p.controller == f.id && p.def != null).ToList();
                var cover = own.FirstOrDefault(p => MilitarySystem.AmbushTerrain(p.def.terrain));
                var open = own.FirstOrDefault(p => !MilitarySystem.AmbushTerrain(p.def.terrain));
                if (cover == null || open == null)
                {
                    Console.WriteLine("  засада: ОШИБКА — на карте нет подходящих провинций");
                    return false;
                }

                var army = f.armies.Select(id => state.Armies[id]).First(a => a.alive);
                army.province = cover.id;
                army.stance = "move";
                army.movementLeft = army.maxMovement;
                foreach (var other in state.Armies.Values.Where(a => a.alive && a.id != army.id && a.province == cover.id).ToList())
                    other.province = state.Provinces.Values.First(p => p.controller != f.id && p != cover).id; // освобождаем место

                army.province = open.id;
                bool plainsBlocked = !MilitarySystem.CanAmbush(state, f, army, out var openErr) &&
                                     openErr != null && openErr.Contains("лес");
                army.province = cover.id;
                bool set = MilitarySystem.SetAmbush(state, f, army, out var aerr);
                bool setOk = set && army.stance == "ambush" && army.movementLeft == 0;
                Console.WriteLine($"  укрытие: {(setOk ? "засада в " + cover.def.name : "ОШИБКА " + aerr)}, " +
                                  $"на равнине {(plainsBlocked ? "нельзя" : "ОШИБКА: разрешено")}");
                ok &= setOk && plainsBlocked;

                string path = Path.Combine(Path.GetTempPath(), "ironthrone_ambush_save.json");
                SaveSystem.SaveToFile(state, path);
                var loaded = SaveSystem.LoadFromFile(db, path);
                bool saveOk = loaded.Armies.TryGetValue(army.id, out var la) && la.stance == "ambush";
                Console.WriteLine($"  сохранение: засада {(saveOk ? "восстановлена" : "ОШИБКА")}");
                ok &= saveOk;

                // враг без свежих донесений попадает в засаду
                enemy.intel.Clear();
                var foe = new ArmyState { id = "ambush_foe", faction = enemy.id, province = cover.id, name = "вражеский отряд" };
                foe.units.Add(new UnitInstance("levy", "Ополчение") { hp = 100, morale = 100 });
                state.Armies[foe.id] = foe;
                var field = BattleEngine.Create(state, foe, army, cover, false);
                bool surprise = field.ambush && field.log.Any(l => l.Contains("Засада")) &&
                                field.Side(0).All(u => u.morale <= 91f);
                Console.WriteLine($"  внезапность: {(surprise ? "сработала (+2 атаки, −10 морали врагу)" : "ОШИБКА")}");
                ok &= surprise;

                // свежая разведка раскрывает засаду
                enemy.intel[f.id] = new FactionIntel { factionId = f.id, turn = state.Turn, armyLines = new List<string>() };
                var spotted = BattleEngine.Create(state, foe, army, cover, false);
                bool revealed = !spotted.ambush && spotted.log.Any(l => l.Contains("раскрыта"));
                Console.WriteLine($"  разведка: засада {(revealed ? "раскрыта" : "ОШИБКА: не сработала")}");
                ok &= revealed;

                // после боя засада снимается
                enemy.intel.Clear();
                army.units.ForEach(u => u.hp = 100);
                MilitarySystem.Fight(state, foe, army, cover);
                bool spent = army.stance != "ambush" || !army.alive;
                Console.WriteLine($"  после боя: засада {(spent ? "снята" : "ОШИБКА: осталась")}");
                ok &= spent;

                // ИИ тоже пользуется засадами в обычной партии
                var sim = GameSetup.CreateNewGame(db, "fivekings", "five_stark", seed: 8181, mapMode: "canonical");
                TurnEngine.StartGame(sim);
                sim.Faction("five_stark").isPlayer = false; // прогон полностью на ИИ
                int aiAmbushes = 0;
                for (int i = 0; i < 60 && sim.Phase != GamePhase.GameOver; i++)
                {
                    TurnEngine.EndTurn(sim);
                    aiAmbushes += sim.Armies.Values.Count(a => a.alive && a.stance == "ambush");
                }
                Console.WriteLine($"  ИИ: армейских ходов в засаде за 60 ходов — {aiAmbushes}");
                ok &= aiAmbushes > 0;

                Console.WriteLine(ok ? "  засады: все проверки пройдены" : "  засады: есть ошибки");
            }
            catch (Exception ex)
            {
                Console.WriteLine("  ОШИБКА засад: " + ex);
                return false;
            }
            return ok;
        }

        /// <summary>P3: пулы законов по обычаям, лимиты построек, пассивки домов, баланс побед.</summary>
        private static bool P3Test(ContentDb db)
        {
            Console.WriteLine("\n=== P3: обычаи законов, лимиты построек, пассивки, баланс побед ===");
            bool ok = true;
            try
            {
                var state = GameSetup.CreateNewGame(db, "fivekings", "five_lannister", seed: 4242, mapMode: "canonical");
                TurnEngine.StartGame(state);
                var f = state.Faction("five_lannister");
                var stark = state.Faction("five_stark");
                f.influence = 200;
                f.gold = 5000;
                f.materials = 5000;

                // 1. пул законов: свой обычай доступен, чужой — нет; пустой пул = только общие законы
                var lannLaws = PoliticsSystem.AvailableLaws(state, f).Select(l => l.id).ToList();
                var starkLaws = PoliticsSystem.AvailableLaws(state, stark).Select(l => l.id).ToList();
                bool poolOk = lannLaws.Contains("law_lion_gold") && !lannLaws.Contains("law_old_gods") &&
                              starkLaws.Contains("law_old_gods") && !starkLaws.Contains("law_lion_gold");
                var savedPool = stark.def.availableLaws;
                stark.def.availableLaws = new List<string>();
                var bareLaws = PoliticsSystem.AvailableLaws(state, stark).ToList();
                bool bareOk = bareLaws.Count > 0 && bareLaws.All(l => l.common);
                stark.def.availableLaws = savedPool;
                bool proposeBlocked = !PoliticsSystem.CanPropose(state, f,
                    state.Content.Laws.First(l => l.id == "law_old_gods"), out var lawErr);
                var vote = PoliticsSystem.ProposeLaw(state, f, "law_lion_gold", out var propErr);
                bool proposeOwn = vote != null;
                Console.WriteLine($"  законы: у Ланнистеров {lannLaws.Count} (свой «Львиная казна»: {lannLaws.Contains("law_lion_gold")}), " +
                                  $"у Старков {starkLaws.Count} (чужой закрыт: {!starkLaws.Contains("law_lion_gold")}), " +
                                  $"пустой пул → {bareLaws.Count} общих ({(bareOk ? "верно" : "ОШИБКА")}), " +
                                  $"чужой закон {(proposeBlocked ? "отклонён" : "ОШИБКА " + lawErr)}, свой {(proposeOwn ? "на голосовании" : "ОШИБКА " + propErr)}");
                ok &= poolOk && bareOk && proposeBlocked && proposeOwn;
                PoliticsSystem.SetPlayerVote(state, "yes");

                // 2. лимит построек на дом: берём постройку с малым лимитом, которой у дома ещё нет
                var limited = state.Content.Buildings.FirstOrDefault(b => b.maxPerEmpire < 99 && !b.unique &&
                                                                         EconomySystem.CountBuilding(state, f, b.id) == 0);
                if (limited == null)
                {
                    Console.WriteLine("  постройки: ОШИБКА — нет постройки со свободным лимитом");
                    ok = false;
                }
                else
                {
                    var def = state.Map.provinces.First();
                    var probe = new List<ProvinceState>();
                    for (int i = 0; i <= limited.maxPerEmpire && i < 5; i++)
                    {
                        var sp = new ProvinceState { id = "p3_probe_" + i, def = def, controller = f.id };
                        state.Provinces[sp.id] = sp;
                        probe.Add(sp);
                    }
                    probe[0].buildings.Add(limited.id);                 // готовая
                    probe[1].buildQueue[limited.id] = 2;                // в очереди — тоже считается
                    int counted = EconomySystem.CountBuilding(state, f, limited.id);
                    bool limitBlocks = !EconomySystem.CanBuild(state, f, probe[2], limited.id, out var limErr) &&
                                       limErr != null && limErr.Contains("Лимит");
                    foreach (var sp in probe) state.Provinces.Remove(sp.id);
                    Console.WriteLine($"  постройки: «{limited.name}» {counted}/{limited.maxPerEmpire} (готовая + очередь), " +
                                      $"лишняя {(limitBlocks ? "запрещена" : "ОШИБКА: лимит не проверяется")}");
                    ok &= counted == 2 && limitBlocks;
                }

                // 3. именная пассивка дома реально попадает в модификаторы
                var passive = f.def.passive;
                bool hasPassive = passive != null && passive.mods.Count > 0;
                float withPassive = f.Mod(ModKeys.GoldIncome);
                float expected = passive == null ? 0f : passive.mods.Where(m => m.key == ModKeys.GoldIncome).Sum(m => m.value);
                f.def.passive = null;
                var bare = GameSetup.CreateNewGame(db, "fivekings", "five_lannister", seed: 4242, mapMode: "canonical");
                float withoutPassive = bare.Faction("five_lannister").Mod(ModKeys.GoldIncome);
                f.def.passive = passive;
                bool passiveOk = hasPassive && expected > 0 && Math.Abs(withPassive - withoutPassive - expected) < 0.01f;
                Console.WriteLine($"  пассивка: «{(passive?.name ?? "нет")}», золото {withoutPassive:0.#} → {withPassive:0.#} " +
                                  $"(ожидалось +{expected:0.#}) {(passiveOk ? "ок" : "ОШИБКА")}");
                ok &= passiveOk;

                // 4. баланс побед: четыре пути и защита от мгновенной религиозной победы
                var v = state.Settings.victory;
                var prog = VictorySystem.Evaluate(state, f);
                bool fourPaths = prog.Count == 4 &&
                                 new[] { "hegemony", "political", "economic", "religious" }.All(t => prog.Any(p => p.victoryType == t));
                bool configured = v.politicalLaws > 0 && v.economicRoutes > 0 && v.religiousConversions > 0;
                Console.WriteLine("  стартовые доли вер: " + string.Join(", ",
                    state.FaithStartShare.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:P0}")));

                var faith = f.faith;
                var faithDef = db.FaithById[faith];
                var savedHead = faithDef.headProvince;
                faithDef.headProvince = null;
                if (!f.flags.Contains("faith_reformed")) f.flags.Add("faith_reformed");
                foreach (var p in state.Provinces.Values) { p.dominantFaith = faith; p.startFaith = faith; }
                state.FaithStartShare[faith] = 1f;   // вера и так была в большинстве, обращений нет — победы быть не должно
                bool instantWin = VictorySystem.Evaluate(state, f).First(x => x.victoryType == "religious").achieved;
                string path = Path.Combine(Path.GetTempPath(), "ironthrone_p3_save.json");
                SaveSystem.SaveToFile(state, path);
                var loaded = SaveSystem.LoadFromFile(db, path);
                bool saveShare = loaded.FaithStartShare.TryGetValue(faith, out var loadedShare) && Math.Abs(loadedShare - 1f) < 0.001f;
                int i2 = 0;
                foreach (var p in state.Provinces.Values) { p.startFaith = i2++ < v.religiousConversions ? "oldgods" : faith; }
                bool grownWin = VictorySystem.Evaluate(state, f).First(x => x.victoryType == "religious").achieved;
                faithDef.headProvince = savedHead;
                bool religionOk = fourPaths && configured && !instantWin && grownWin && saveShare;
                Console.WriteLine($"  победы: путей {prog.Count}, пороги (законы {v.politicalLaws}, узлы {v.economicRoutes}, обращений {v.religiousConversions}), " +
                                  $"без обращений {(instantWin ? "ОШИБКА: сработала" : "заблокирована")}, " +
                                  $"после {v.religiousConversions} обращений {(grownWin ? "сработала" : "ОШИБКА")}, стартовая доля в сейве {(saveShare ? "есть" : "ОШИБКА")}");
                ok &= religionOk;

                // 5. ИИ больше не копит золото: закупки и подарки при излишке
                var ai = state.Factions.Values.First(x => !x.isPlayer && x.alive);
                ai.gold = 3000;
                ai.materials = 40;
                ai.food = 40;
                float aiGoldBefore = ai.gold;
                FactionAI.SpendTreasury(state, ai);
                bool spends = ai.gold < aiGoldBefore;
                Console.WriteLine($"  ИИ-казна: {aiGoldBefore:0} → {ai.gold:0} {(spends ? "(тратит)" : "ОШИБКА: копит")}");
                ok &= spends;

                Console.WriteLine(ok ? "  P3: все проверки пройдены" : "  P3: есть ошибки");
            }
            catch (Exception ex)
            {
                Console.WriteLine("  ОШИБКА P3: " + ex);
                return false;
            }
            return ok;
        }

        /// <summary>P1: пакты о ненападении, династические браки, коалиция, догматы веры, цепочки событий и квесты.</summary>
        private static bool P1Test(ContentDb db)
        {
            Console.WriteLine("\n=== P1: пакты, браки, коалиция, догматы, цепочки ===");
            bool ok = true;
            try
            {
                var state = GameSetup.CreateNewGame(db, "fivekings", "five_lannister", seed: 777, mapMode: "canonical");
                TurnEngine.StartGame(state);
                var f = state.Faction("five_lannister");
                var other = state.Faction("five_tyrell");
                f.influence = 100; f.gold = 1000;
                DiplomacySystem.AddRelation(state, f.id, other.id, 40);

                // 1. пакт о ненападении: заключается, блокирует войну, разрывается
                bool pactMade = DiplomacySystem.MakePact(state, f, other.id, out var perr);
                bool pactBlocks = !DiplomacySystem.CanDeclareWar(state, f, other.id, out var werr);
                int pactLeft = DiplomacySystem.PactLeft(state, f.id, other.id);
                bool pactBroken = DiplomacySystem.BreakPact(state, f, other.id, out var berr);
                bool canWarAfter = pactBroken && DiplomacySystem.CanDeclareWar(state, f, other.id, out _);
                Console.WriteLine($"  пакт: {(pactMade ? $"заключён на {pactLeft} ходов" : "ОШИБКА " + perr)}, " +
                                  $"война {(pactBlocks ? "заблокирована" : "ОШИБКА")}, разрыв {(pactBroken ? "разрешён" : "ОШИБКА " + berr)}, " +
                                  $"война после разрыва {(canWarAfter ? "возможна" : "ОШИБКА")}");
                ok &= pactMade && pactBlocks && pactLeft == DiplomacySystem.PactTurns && pactBroken && canWarAfter;

                // 2. династический брак: отношения, признание за ход, лимит
                f.influence = 100; f.gold = 1000;
                f.relations[other.id] = 30f;
                other.relations[f.id] = 30f;
                float relBefore = DiplomacySystem.Relation(state, f.id, other.id);
                bool married = DiplomacySystem.Marry(state, f, other.id, out var merr);
                float relAfter = DiplomacySystem.Relation(state, f.id, other.id);
                float recBefore = f.recognition;
                PoliticsSystem.ProcessTurn(state);
                float recGain = f.recognition - recBefore;
                Console.WriteLine($"  брак: {(married ? "заключён" : "ОШИБКА " + merr)}, отношения {relBefore:0}→{relAfter:0}, " +
                                  $"признание за ход +{recGain:0.##} (браков {f.marriages.Count})");
                ok &= married && relAfter >= relBefore + 19f && f.marriages.Contains(other.id) && recGain >= 0.25f;

                // 3. коалиция против лидера
                var leader = f;
                int want = (int)Math.Ceiling(state.Provinces.Count * 0.45f);
                foreach (var p in state.Provinces.Values)
                    if (state.CountControlled(leader.id) < want) p.controller = leader.id;
                state.Turn = 30;
                float pairwise = DiplomacySystem.Relation(state, "five_stark", "five_martell");
                DiplomacySystem.UpdateCoalition(state);
                bool coalition = state.CoalitionTarget == leader.id;
                float pairwiseAfter = DiplomacySystem.Relation(state, "five_stark", "five_martell");
                Console.WriteLine($"  коалиция: {(coalition ? "собрана против " + leader.def.shortName : "ОШИБКА")}, " +
                                  $"отношения малых соперников {pairwise:0}→{pairwiseAfter:0}, до хода {state.CoalitionUntil}");
                ok &= coalition && pairwiseAfter > pairwise;

                // 4. догмат веры: только после реформы и только один
                f.SetFlag("faith_reformed");
                bool dogmaFirst = FaithSystem.ChooseDogma(state, f, "dogma_sword", out var derr);
                bool dogmaSecond = !FaithSystem.ChooseDogma(state, f, "dogma_mercy", out _);
                Console.WriteLine($"  догмат: {(dogmaFirst ? "«Меч и пламя» выбран" : "ОШИБКА " + derr)}, " +
                                  $"атака {f.Mod(ModKeys.ArmyAttack):0}, повторный выбор {(dogmaSecond ? "заблокирован" : "ОШИБКА")}");
                ok &= dogmaFirst && dogmaSecond && f.Mod(ModKeys.ArmyAttack) >= 1f;

                // 5. цепочка событий: выбор назначает продолжение и оно срабатывает
                var first = db.Events.First(e => e.id == "chain_wall_1");
                EventSystem.Fire(state, first, f);
                var pending = state.PendingEvents.FindIndex(p => p.eventId == "chain_wall_1" && p.faction == f.id);
                string choose = pending >= 0 ? EventSystem.Choose(state, pending, 0) : null;
                bool scheduled = state.ScheduledEvents.Any(x => x.eventId == "chain_wall_2");
                foreach (var se in state.ScheduledEvents) se.turn = state.Turn;
                EventSystem.ProcessTurn(state);
                bool continued = state.PendingEvents.Any(p => p.eventId == "chain_wall_2" && p.faction == f.id) ||
                                 state.firedEvents.Contains("chain_wall_2");
                Console.WriteLine($"  цепочка «Тень над Стеной»: выбор {(choose == null ? "прошёл" : "ОШИБКА")}, " +
                                  $"продолжение {(scheduled ? "назначено" : "ОШИБКА")}, {(continued ? "сработало" : "ОШИБКА")}");
                ok &= choose == null && scheduled && continued;

                // 6. у каждой играбельной фракции есть свой квест
                foreach (var era in db.Eras)
                {
                    foreach (var fdef in db.Factions.Where(x => x.era == era.id && x.playable))
                    {
                        bool has = db.Quests.Any(q => q.faction == fdef.id && (q.eras.Count == 0 || q.eras.Contains(era.id)));
                        if (!has) { Console.WriteLine($"  [квесты] у {fdef.id} ({era.id}) нет квеста"); ok = false; }
                    }
                }
                Console.WriteLine($"  квесты: {db.Quests.Count}, у каждой играбельной фракции свой");

                // 7. сохранение новых полей
                string path = Path.Combine(Path.GetTempPath(), "ironthrone_p1_save.json");
                SaveSystem.SaveToFile(state, path);
                var loaded = SaveSystem.LoadFromFile(db, path);
                var lf = loaded.Faction(f.id);
                bool saveOk = lf != null && lf.marriages.Contains(other.id) && lf.dogma == "dogma_sword" &&
                              loaded.CoalitionTarget == leader.id &&
                              loaded.Pacts != null && loaded.ScheduledEvents != null;
                if (!saveOk)
                    Console.WriteLine($"    загружено: дом {(lf == null ? "нет" : lf.id)}, браков {lf?.marriages.Count ?? -1}, " +
                                      $"догмат {lf?.dogma ?? "—"}, коалиция {loaded.CoalitionTarget ?? "—"}, " +
                                      $"пактов {loaded.Pacts?.Count ?? -1}, отложенных {loaded.ScheduledEvents?.Count ?? -1}");
                Console.WriteLine($"  сохранение: брак/догмат/коалиция/пакты — {(saveOk ? "восстановлены" : "ОШИБКА")}");
                ok &= saveOk;

                // 8. ИИ реально пользуется новыми механиками в обычной партии
                var sim = GameSetup.CreateNewGame(db, "fivekings", "five_stark", seed: 8080, mapMode: "canonical");
                TurnEngine.StartGame(sim);
                for (int i = 0; i < 80; i++) TurnEngine.AdvanceTurn(sim);
                var sim2 = GameSetup.CreateNewGame(db, "conquest", "conquest_stark", seed: 9090, mapMode: "canonical");
                TurnEngine.StartGame(sim2);
                for (int i = 0; i < 80; i++) TurnEngine.AdvanceTurn(sim2);
                int pactCount = sim.Pacts.Count + sim2.Pacts.Count;
                int marriageCount = (sim.Factions.Values.Sum(x => x.marriages.Count) +
                                     sim2.Factions.Values.Sum(x => x.marriages.Count)) / 2;
                int dogmaCount = sim.Factions.Values.Count(x => !string.IsNullOrEmpty(x.dogma)) +
                                 sim2.Factions.Values.Count(x => !string.IsNullOrEmpty(x.dogma));
                int reformed = sim.Factions.Values.Count(x => x.HasFlag("faith_reformed")) +
                               sim2.Factions.Values.Count(x => x.HasFlag("faith_reformed"));
                int chainCount = sim.firedEvents.Count(id => id.StartsWith("chain_")) +
                                 sim2.firedEvents.Count(id => id.StartsWith("chain_"));
                if (pactCount == 0 || marriageCount == 0 || (reformed > 0 && dogmaCount == 0) || chainCount == 0)
                {
                    Console.WriteLine($"  [P1] ИИ не использует механики: пактов {pactCount}, браков {marriageCount}, " +
                                      $"догматов {dogmaCount}, событий цепочек {chainCount}");
                    ok = false;
                }
                Console.WriteLine($"  2 партии по 80 ходов ИИ: пактов {pactCount}, браков {marriageCount}, реформ {reformed}, догматов {dogmaCount}, " +
                                  $"событий цепочек {chainCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("  ОШИБКА P1: " + ex);
                return false;
            }
            return ok;
        }

        /// <summary>Проверяет, что черты отрядов и местность реально меняют бой, а грабёж даёт добычу.</summary>
        private static bool TraitTest(ContentDb db)
        {
            Console.WriteLine("\n=== Проверка черт отрядов ===");
            try
            {
                var state = GameSetup.CreateNewGame(db, "fivekings", "five_stark", seed: 4242, mapMode: "canonical");
                TurnEngine.StartGame(state);
                var stark = state.Faction("five_stark");
                var enemy = state.Factions.Values.First(f => f.id != stark.id && f.alive);

                Battlefield FieldAt(string provinceId)
                {
                    var prov = state.Province(provinceId);
                    var a = new ArmyState { id = "t_a_" + provinceId, faction = stark.id, province = prov.id, name = "тест" };
                    var d = new ArmyState { id = "t_d_" + provinceId, faction = enemy.id, province = prov.id, name = "тест" };
                    a.units.Add(new UnitInstance("northern_infantry", "Северная пехота") { hp = 100, morale = 100 });
                    d.units.Add(new UnitInstance("levy", "Ополчение") { hp = 100, morale = 100 });
                    return BattleEngine.Create(state, a, d, prov);
                }

                var snow = state.Provinces.Values.FirstOrDefault(p => p.def.terrain == "snow") ?? state.Province("winterfell");
                var desert = state.Provinces.Values.FirstOrDefault(p => p.def.terrain == "desert");
                var fSnow = FieldAt(snow.id);
                var fDesert = FieldAt(desert.id);
                int atkSnow = fSnow.units.First(u => u.side == 0).stats.attack;
                int atkDesert = fDesert.units.First(u => u.side == 0).stats.attack;
                bool terrainOk = atkSnow > atkDesert;
                Console.WriteLine($"  северная пехота: атака в снегу {atkSnow}, в пустыне {atkDesert} → {(terrainOk ? "бонус родной местности работает" : "ОШИБКА")}");

                // грабёж: железнорождённые берут больше, чем обычная пехота
                var victim = state.Provinces.Values.First(p => p.controller != null && p.controller != "five_greyjoy" && p.def.level >= 2);
                var raiderFaction = state.Faction(state.Factions.Values.First(f => f.house == "greyjoy").id);
                int goldBefore = raiderFaction.gold;
                var raiderArmy = new ArmyState { id = "t_raid", faction = raiderFaction.id, province = victim.id, name = "налёт" };
                raiderArmy.units.Add(new UnitInstance("ironborn_reavers", "Железнорождённые") { hp = 100, morale = 100 });
                string oldController = victim.controller;
                MilitarySystem.CaptureProvince(state, victim, raiderFaction.id, raiderArmy);
                int loot = raiderFaction.gold - goldBefore;
                victim.controller = oldController;
                bool lootOk = loot > 0;
                Console.WriteLine($"  грабёж железнорождённых: {loot} золота с провинции уровня {victim.def.level} → {(lootOk ? "добыча начисляется" : "ОШИБКА")}");

                // goldFlat: плоская прибавка к доходу
                var beforeFlat = EconomySystem.CalculateIncome(state, stark).gold;
                stark.permanentMods[ModKeys.GoldFlat] = 17f;
                var afterFlat = EconomySystem.CalculateIncome(state, stark).gold;
                bool flatOk = afterFlat - beforeFlat == 17;
                Console.WriteLine($"  доход с goldFlat +17: {beforeFlat} → {afterFlat} → {(flatOk ? "работает" : "ОШИБКА")}");

                // armyHp: процентный запас прочности отрядов
                stark.permanentMods[ModKeys.ArmyHp] = 0.25f;
                var fHp = FieldAt(snow.id);
                float maxHp = fHp.units.First(u => u.side == 0).maxHp;
                bool hpOk = Math.Abs(maxHp - 125f) < 0.1f;
                Console.WriteLine($"  armyHp +25%: максимум здоровья отряда {maxHp} → {(hpOk ? "работает" : "ОШИБКА")}");

                return terrainOk && lootOk && flatOk && hpOk;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  ОШИБКА черт: " + ex);
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
                        foreach (var d in MapGenerator.OddRNeighborOffsets(cur.Item2))
                        {
                            var n = (cur.Item1 + d.dc, cur.Item2 + d.dr);
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

        /// <summary>Сводка побед по нескольким сидам: какие типы побед вообще случаются и на каком ходу.</summary>
        private static void VictoryStats(ContentDb db, int seeds, int turns)
        {
            Console.WriteLine($"\n=== Статистика побед: {seeds} сида x все фракции, {turns} ходов ===");
            var outcomes = new Dictionary<string, int>();
            var turnsByType = new Dictionary<string, List<int>>();
            for (int seed = 0; seed < seeds; seed++)
            {
                foreach (var era in db.Eras)
                {
                    foreach (var f in db.Factions.Where(x => x.era == era.id && x.playable))
                    {
                        var advisors = db.AdvisorsFor(era, f).Select(a => a.id).Take(2).ToList();
                        var st = GameSetup.CreateNewGame(db, era.id, f.id, seed: 5000 + seed * 131 + StableHashOf(f.id),
                            chosenAdvisors: advisors, mapMode: "canonical");
                        TurnEngine.StartGame(st);
                        for (int i = 0; i < turns && st.Phase != GamePhase.GameOver; i++)
                        {
                            ResolvePending(st);
                            var p = st.Faction(f.id);
                            if (p != null && p.alive) FactionAI.TakeTurn(st, p);
                            ResolvePending(st);
                            TurnEngine.EndTurn(st);
                            ResolvePending(st);
                        }
                        string type = st.Phase == GamePhase.GameOver ? (st.VictoryType ?? "unknown") : "turn-limit";
                        if (seed == 0 && type != "turn-limit")
                        {
                            var w = st.Faction(st.VictoryWinner);
                            Console.WriteLine($"    победа: {type}, {st.VictoryWinner}, вера {w?.faith}, доля {(w != null ? FaithSystem.FaithShare(st, w) : 0):P0}, " +
                                              $"глава {(w != null && FaithSystem.IsHead(st, w))}, реформа {w?.HasFlag("faith_reformed")}, ход {st.Turn}");
                        }
                        outcomes.TryGetValue(type, out var n);
                        outcomes[type] = n + 1;
                        if (!turnsByType.TryGetValue(type, out var list)) turnsByType[type] = list = new List<int>();
                        list.Add(st.Turn);
                    }
                }
            }
            foreach (var kv in outcomes.OrderByDescending(kv => kv.Value))
            {
                var list = turnsByType[kv.Key];
                Console.WriteLine($"  {kv.Key}: {kv.Value} (средний ход {list.Average():0}, от {list.Min()} до {list.Max()})");
            }
        }

        /// <summary>Стабильный хеш (общая реализация — IronThrone.Core.Game.StableHash).</summary>
        private static int StableHashOf(string s) => StableHash.Of(s, 100000);

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
