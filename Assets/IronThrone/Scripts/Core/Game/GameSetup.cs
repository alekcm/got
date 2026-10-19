using System;
using System.Collections.Generic;
using System.Linq;
using IronThrone.Core.Content;

namespace IronThrone.Core.Game
{
    /// <summary>Создание новой игры: эпоха, фракция, советники, карта, стартовые армии.</summary>
    public static class GameSetup
    {
        public static readonly Dictionary<string, Dictionary<string, float>> RegionFaiths =
            new Dictionary<string, Dictionary<string, float>>
            {
                ["north"] = new Dictionary<string, float> { ["oldgods"] = 74, ["seven"] = 12, ["folk"] = 14 },
                ["wall"] = new Dictionary<string, float> { ["oldgods"] = 88, ["folk"] = 12 },
                ["islands"] = new Dictionary<string, float> { ["drowned"] = 82, ["seven"] = 8, ["folk"] = 10 },
                ["riverlands"] = new Dictionary<string, float> { ["seven"] = 68, ["oldgods"] = 18, ["folk"] = 14 },
                ["vale"] = new Dictionary<string, float> { ["seven"] = 82, ["folk"] = 18 },
                ["westerlands"] = new Dictionary<string, float> { ["seven"] = 80, ["folk"] = 20 },
                ["reach"] = new Dictionary<string, float> { ["seven"] = 84, ["folk"] = 16 },
                ["stormlands"] = new Dictionary<string, float> { ["seven"] = 74, ["folk"] = 26 },
                ["crownlands"] = new Dictionary<string, float> { ["seven"] = 72, ["valyrian"] = 8, ["folk"] = 20 },
                ["dorne"] = new Dictionary<string, float> { ["seven"] = 58, ["folk"] = 30, ["rhllor"] = 4, ["oldgods"] = 8 },
            };

        public static GameState CreateNewGame(ContentDb content, string eraId, string playerFactionId,
            int seed = 12345, List<string> chosenAdvisors = null, string mapMode = "canonical")
        {
            var era = content.Eras.FirstOrDefault(e => e.id == eraId);
            if (era == null) throw new InvalidOperationException("Неизвестная эпоха: " + eraId);

            var state = new GameState
            {
                Content = content,
                Era = era,
                Seed = seed,
                Rng = new Random(seed),
                Year = era.startYear,
                Turn = 1,
                PlayerFaction = playerFactionId,
                Phase = GamePhase.PlayerTurn,
            };
            state.Season = state.SeasonName(1);

            if (string.Equals(mapMode, "random", StringComparison.OrdinalIgnoreCase))
            {
                state.CustomMap = MapGenerator.Generate(content.Map, seed);
                state.AddLog("system", "Сгенерирована случайная карта Вестероса.");
            }

            CreateProvinces(state);
            CreateFactions(state, era, playerFactionId, chosenAdvisors);
            AssignOwnership(state, era);
            CreateMinorHouses(state, era);
            CreateStartingArmies(state);
            CreateCouncil(state);
            CreateRaiderCamps(state);
            CreateQuests(state, era);
            InitFaiths(state);
            state.AddLog("system", $"Начало игры: {era.name}. Игрок — {state.FactionName(playerFactionId)}.");
            return state;
        }

        private static void CreateProvinces(GameState state)
        {
            foreach (var def in state.Map.provinces)
            {
                var p = new ProvinceState
                {
                    id = def.id,
                    def = def,
                    house = def.house,
                    owner = null,
                    controller = null,
                    unrest = 8f,
                    development = def.level,
                };
                p.garrison = 20 + def.level * 18 + (def.isCapital ? 40 : 0);
                if (!string.IsNullOrEmpty(def.terrain) && def.terrain == "mountains") p.garrison += 20;
                p.faith["folk"] = 20;
                if (def.isCapital)
                {
                    p.buildings.Add("castle");
                    p.buildings.Add("market");
                    p.buildings.Add("sept");
                }
                p.flags.Add("start");
                state.Provinces[def.id] = p;
            }
        }

        private static void CreateFactions(GameState state, EraDef era, string playerId, List<string> chosenAdvisors)
        {
            var defs = state.Content.Factions.Where(f => f.era == era.id).ToList();
            if (defs.Count == 0)
                throw new InvalidOperationException("В эпохе нет фракций: " + era.id);

            foreach (var def in defs)
            {
                var f = new FactionState
                {
                    id = def.id,
                    def = def,
                    house = def.house,
                    capital = def.capital,
                    isPlayer = def.id == playerId,
                    faith = def.faith,
                    gold = state.Settings.startingResources.gold,
                    materials = state.Settings.startingResources.materials,
                    food = state.Settings.startingResources.food,
                    influence = state.Settings.startingResources.influence,
                    piety = state.Settings.startingResources.piety,
                    spyNetwork = state.Settings.spyNetworkBase,
                };
                if (def.startResources != null && !def.startResources.IsZero)
                {
                    f.gold = def.startResources.gold;
                    f.materials = def.startResources.materials;
                    f.food = def.startResources.food;
                    f.influence = def.startResources.influence;
                    f.piety = def.startResources.piety;
                }
                foreach (var b in def.bonuses)
                {
                    if (b == null || string.IsNullOrEmpty(b.key)) continue;
                    f.permanentMods.TryGetValue(b.key, out var cur);
                    f.permanentMods[b.key] = cur + b.value;
                }
                foreach (var t in state.Content.TechsFor(def.house).Where(t => t.starting))
                {
                    f.techs.Add(t.id);
                    EffectEngine.ApplyAll(t.effects, new EffectContext { State = state, Source = f }, null);
                }
                // советники: 2 из пула
                var pool = state.Content.AdvisorsForFaction(era, def).Select(a => a.id).ToList();
                var chosen = new List<string>();
                if (f.isPlayer && chosenAdvisors != null && chosenAdvisors.Count > 0)
                {
                    chosen.AddRange(chosenAdvisors.Where(pool.Contains).Take(2));
                }
                while (chosen.Count < 2 && pool.Count > chosen.Count)
                {
                    var pick = pool.Where(x => !chosen.Contains(x)).OrderBy(_ => state.Rng.Next()).FirstOrDefault();
                    if (pick == null) break;
                    chosen.Add(pick);
                }
                foreach (var advId in chosen)
                {
                    f.advisors.Add(new AdvisorState { defId = advId });
                    var adv = state.Content.AdvisorById[advId];
                    foreach (var m in adv.passive)
                    {
                        f.permanentMods.TryGetValue(m.key, out var cur);
                        f.permanentMods[m.key] = cur + m.value;
                    }
                }
                foreach (var other in defs)
                {
                    if (other.id == def.id) continue;
                    f.relations[other.id] = InitialRelation(def.house, other.house);
                }
                state.Factions[f.id] = f;
            }
        }

        private static float InitialRelation(string a, string b)
        {
            if (a == b) return 60;
            if ((a == "stark" && b == "tully") || (a == "tully" && b == "stark")) return 45;
            if ((a == "lannister" && b == "baratheon") || (a == "baratheon" && b == "lannister")) return -45;
            if ((a == "targaryen" && b == "baratheon") || (a == "baratheon" && b == "targaryen")) return -70;
            if ((a == "targaryen" && b == "greyjoy") || (a == "greyjoy" && b == "targaryen")) return -10;
            if ((a == "greyjoy" && b == "stark") || (a == "stark" && b == "greyjoy")) return -30;
            if ((a == "martell" && b == "lannister") || (a == "lannister" && b == "martell")) return -40;
            if ((a == "tyrell" && b == "lannister") || (a == "lannister" && b == "tyrell")) return 25;
            if ((a == "tyrell" && b == "martell") || (a == "martell" && b == "tyrell")) return -25;
            if ((a == "arryn" && b == "tully") || (a == "tully" && b == "arryn")) return 30;
            return 0;
        }

        private static void AssignOwnership(GameState state, EraDef era)
        {
            foreach (var f in state.Factions.Values.OrderByDescending(x => x.def.priority).ThenBy(x => x.id))
            {
                var def = f.def;
                var owned = new List<string>();
                foreach (var p in state.Provinces.Values)
                {
                    if (p.owner != null) continue;
                    if (def.excludeProvinces.Contains(p.id)) continue;
                    if (def.extraProvinces.Contains(p.id)) { owned.Add(p.id); continue; }
                    if (p.def.region == null) continue;
                    bool regionMatch = def.ownedRegions.Contains(p.def.region);
                    if (!regionMatch) continue;
                    bool isHouse = p.house == def.house;
                    bool isVassal = def.vassalHouses.Contains(p.house);
                    if (isHouse || isVassal || p.id == def.capital)
                        owned.Add(p.id);
                }
                foreach (var pid in owned)
                {
                    var p = state.Provinces[pid];
                    p.owner = f.id;
                    p.controller = f.id;
                    p.unrest = InitialUnrest(state, p, f);
                    state.AddLog("setup", $"{f.def.name} получает {p.def.name}", f.id);
                }
            }

            // фракции, у которых не оказалось провинций, получают столицу
            foreach (var f in state.Factions.Values)
            {
                if (state.Provinces.Values.Any(p => p.owner == f.id)) continue;
                if (state.Provinces.TryGetValue(f.capital, out var cap) && cap.owner == null)
                {
                    cap.owner = f.id; cap.controller = f.id;
                }
            }
        }

        private static float InitialUnrest(GameState state, ProvinceState p, FactionState f)
        {
            float unrest = 10 + state.Rng.Next(0, 12);
            // Хоары удерживают Речные земли силой
            if (f.house == "hoare" && p.def.region == "riverlands") unrest += 35;
            if (f.house == "targaryen") unrest += 5;
            if (p.def.terrain == "mountains") unrest += 6;
            if (p.def.terrain == "desert") unrest += 3;
            if (p.house != f.house && f.def.vassalHouses.Contains(p.house)) unrest += 8;
            return Math.Min(90, unrest);
        }

        private static void CreateMinorHouses(GameState state, EraDef era)
        {
            var majorHouses = new HashSet<string>(state.Factions.Values.Select(f => f.house));
            foreach (var p in state.Provinces.Values)
            {
                if (string.IsNullOrEmpty(p.house) || majorHouses.Contains(p.house)) continue;
                if (state.MinorHouses.ContainsKey(p.house)) continue;
                int rel = 0;
                if (p.house == "frey") rel = -10;
                if (p.house == "bolton") rel = -15;
                if (p.house == "florent") rel = -5;
                if (p.house == "hightower") rel = 15;
                state.MinorHouses[p.house] = new MinorHouseState { house = p.house, relation = rel };
            }
            foreach (var f in state.Factions.Values)
            {
                foreach (var h in state.Content.HouseById.Keys)
                {
                    if (string.IsNullOrEmpty(h) || majorHouses.Contains(h)) continue;
                    if (!state.MinorHouses.ContainsKey(h)) continue;
                    if (!f.minorRelations.ContainsKey(h)) f.minorRelations[h] = state.MinorHouses[h].relation;
                }
            }
        }

        private static void CreateStartingArmies(GameState state)
        {
            foreach (var f in state.Factions.Values)
            {
                string fallback = state.Provinces.Values.FirstOrDefault(p => p.owner == f.id)?.id ?? f.capital;
                if (f.def.startArmies != null && f.def.startArmies.Count > 0)
                {
                    int index = 0;
                    foreach (var spec in f.def.startArmies)
                    {
                        var parts = spec.Split('|');
                        string name = parts.Length > 1 ? parts[0] : $"Армия {f.def.shortName ?? f.def.name} {index + 1}";
                        string unitsSpec = parts.Length > 1 ? parts[1] : parts[0];
                        var army = MilitarySystem.CreateArmy(state, f, fallback, name);
                        foreach (var token in unitsSpec.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var t = token.Trim();
                            int count = 1;
                            var star = t.IndexOf('*');
                            if (star > 0)
                            {
                                int.TryParse(t.Substring(star + 1), out count);
                                t = t.Substring(0, star);
                            }
                            if (!state.Content.UnitById.TryGetValue(t, out var ud)) continue;
                            for (int i = 0; i < Math.Max(1, count); i++)
                                army.units.Add(new UnitInstance(t, ud.name) { hp = 100, morale = 100 });
                        }
                        index++;
                    }
                }
                else
                {
                    var army = MilitarySystem.CreateArmy(state, f, fallback, "Основная армия");
                    foreach (var unitId in f.def.startUnits)
                        if (state.Content.UnitById.TryGetValue(unitId, out var ud))
                            army.units.Add(new UnitInstance(unitId, ud.name) { hp = 100, morale = 100 });
                }
                foreach (var heroId in new[] { f.def.leaderHero, f.def.heirHero }.Concat(f.def.extraHeroes))
                {
                    if (string.IsNullOrEmpty(heroId) || !state.Content.HeroById.ContainsKey(heroId)) continue;
                    var hdef = state.Content.HeroById[heroId];
                    var hs = new HeroState { defId = heroId, name = hdef.name, faction = f.id };
                    state.Heroes[heroId] = hs;
                    f.heroes.Add(heroId);
                }
            }
            // лидеры присоединяются к первой армии
            foreach (var f in state.Factions.Values)
            {
                var leader = f.heroes.Select(id => state.Heroes[id]).FirstOrDefault(h => h.defId == f.def.leaderHero);
                var army = f.armies.Select(id => state.Armies[id]).FirstOrDefault();
                if (leader != null && army != null)
                {
                    army.heroId = leader.defId;
                    leader.armyId = army.id;
                }
                for (int i = 1; i < f.heroes.Count && f.armies.Count > 0; i++)
                {
                    var h = state.Heroes[f.heroes[i]];
                    var a = state.Armies[f.armies[Math.Min(i, f.armies.Count - 1)]];
                    if (a.heroId == null) { a.heroId = h.defId; h.armyId = a.id; }
                }
            }
        }

        private static void CreateCouncil(GameState state)
        {
            var kl = state.Province("kings_landing");
            if (kl?.controller != null)
            {
                state.Council.throneHolder = kl.controller;
                state.Council.hand = kl.controller;
            }
            // титулы раздаются сильнейшим домам (или держателю трона)
            var titles = state.Content.Titles;
            var ranked = state.Factions.Values
                .OrderByDescending(f => state.Provinces.Values.Count(p => p.controller == f.id))
                .ThenBy(f => f.id)
                .ToList();
            int idx = 0;
            foreach (var title in titles)
            {
                string holder = null;
                if (state.Council.throneHolder != null && idx == 0) holder = state.Council.throneHolder;
                else
                {
                    var candidate = ranked.FirstOrDefault(f => f.id != state.Council.throneHolder &&
                        !state.Council.seats.ContainsValue(f.id));
                    holder = candidate?.id;
                }
                if (holder != null)
                {
                    state.Council.seats[title.id] = holder;
                    if (title.id == "hand") state.Council.hand = holder;
                }
                idx++;
            }
            state.Council.lastAgendaTurn = -99;
        }

        private static void CreateRaiderCamps(GameState state)
        {
            var used = new HashSet<string>();
            void AddCamp(ProvinceState p, string kind, int strength)
            {
                if (p == null || !used.Add(p.id)) return;
                state.RaiderCamps.Add(new RaiderCampState
                {
                    id = "camp_" + state.NextCampId++,
                    name = RaiderCampName(kind, state.Rng),
                    province = p.id,
                    kind = kind,
                    strength = strength,
                });
            }

            // пираты и железнорождённые: побережье и острова
            foreach (var p in state.Provinces.Values
                         .Where(p => p.def.port && p.def.region != "wall")
                         .OrderBy(_ => state.Rng.Next()).Take(4))
                AddCamp(p, p.def.region == "islands" ? "reavers" : "pirates", 40 + state.Rng.Next(0, 30));

            // горные кланы: Долина и Запад
            foreach (var p in state.Provinces.Values
                         .Where(p => (p.def.region == "vale" || p.def.region == "westerlands") &&
                                     (p.def.terrain == "mountains" || p.def.terrain == "hills"))
                         .OrderBy(_ => state.Rng.Next()).Take(2))
                AddCamp(p, "mountain clans", 45 + state.Rng.Next(0, 15));

            // одичалые: у Стены
            foreach (var p in state.Provinces.Values
                         .Where(p => p.def.region == "wall")
                         .OrderBy(_ => state.Rng.Next()).Take(2))
                AddCamp(p, "wildlings", 40 + state.Rng.Next(0, 20));

            // разбойники в глубине материка
            var inland = state.Provinces.Values
                .Where(p => !p.def.port && p.def.region != "wall" && p.controller == null)
                .OrderBy(_ => state.Rng.Next()).FirstOrDefault();
            AddCamp(inland, "bandits", 35 + state.Rng.Next(0, 20));
        }

        private static string RaiderCampName(string kind, Random rng)
        {
            var pirates = new[] { "Логово Солевых Псов", "Берег Разбитых Вёсел", "Пираты Кровавого Мыса", "Гнездо Контрабандистов" };
            var wildlings = new[] { "Лагерь Одичалых", "Стоянка Теннов", "Костёр За Стеной" };
            var clans = new[] { "Кланы Лунных Гор", "Пещеры Чёрного Козла", "Логово Горных Разбойников" };
            var reavers = new[] { "Волны Утонувшего", "Стоянка Длинных Кораблей", "Морские Волки" };
            var bandits = new[] { "Шайка Кровавых Плащей", "Лагерь Разбойников", "Вольные Клинки" };
            var arr = kind == "wildlings" ? wildlings : kind == "reavers" ? reavers : kind == "mountain clans" ? clans :
                      kind == "bandits" ? bandits : pirates;
            return arr[rng.Next(arr.Length)];
        }

        private static void CreateQuests(GameState state, EraDef era)
        {
            foreach (var q in state.Content.Quests.Where(q => q.eras.Count == 0 || q.eras.Contains(era.id)))
            {
                if (q.faction != null && !state.Factions.ContainsKey(q.faction)) continue;
                state.Quests.Add(new QuestState { id = q.id, faction = q.faction, step = 0 });
            }
        }

        private static void InitFaiths(GameState state)
        {
            foreach (var p in state.Provinces.Values)
            {
                var table = RegionFaiths.TryGetValue(p.def.region ?? "", out var t) ? t : new Dictionary<string, float> { ["seven"] = 60, ["folk"] = 40 };
                foreach (var kv in table) p.faith[kv.Key] = kv.Value;
                // святыни и особые места
                if (p.id == "dragonstone") { p.faith["valyrian"] = 55; p.faith["seven"] = 25; p.faith["rhllor"] = 20; }
                if (p.id == "oldtown") { p.faith["seven"] = 92; p.faith["folk"] = 8; }
                if (p.id == "winterfell") { p.faith["oldgods"] = 90; p.faith["seven"] = 6; p.faith["folk"] = 4; }
                if (p.id == "pyke") { p.faith["drowned"] = 92; p.faith["folk"] = 8; }
                EffectEngine.RecomputeDominantFaith(p);
            }
        }
    }
}
