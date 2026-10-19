using System;
using System.Collections.Generic;
using System.Linq;
using IronThrone.Core.Content;

namespace IronThrone.Core.Game
{
    public class SpyMissionDef
    {
        public string id;
        public string name;
        public string description;
        public ResourceCost cost = new ResourceCost();
        public float baseChance = 0.5f;
        public string targetKind = "faction"; // faction, province, hero
        public string icon;
    }

    public class SpyResult
    {
        public bool success;
        public bool exposed;
        public string text;
        public SpyResult(bool success, string text, bool exposed = false)
        { this.success = success; this.text = text; this.exposed = exposed; }
    }

    /// <summary>Шпионаж: сеть шептунов, миссии, контрразведка.</summary>
    public static class EspionageSystem
    {
        public static readonly List<SpyMissionDef> Missions = new List<SpyMissionDef>
        {
            new SpyMissionDef { id = "recon", name = "Разведка", description = "Раскрыть армии и планы выбранного дома.", cost = new ResourceCost(0,0,0,2), baseChance = 0.95f, targetKind = "faction" },
            new SpyMissionDef { id = "steal_tech", name = "Кража технологий", description = "Похитить одну изученную технологию противника.", cost = new ResourceCost(120,0,0,8), baseChance = 0.45f, targetKind = "faction" },
            new SpyMissionDef { id = "sabotage", name = "Саботаж", description = "Уничтожить постройку или сорвать строительство в провинции.", cost = new ResourceCost(80,0,0,6), baseChance = 0.55f, targetKind = "province" },
            new SpyMissionDef { id = "incite", name = "Подстрекательство", description = "Поднять беспорядки в провинции противника.", cost = new ResourceCost(60,0,0,5), baseChance = 0.7f, targetKind = "province" },
            new SpyMissionDef { id = "assassinate", name = "Убийство", description = "Покушение на героя. Провал вызовет скандал.", cost = new ResourceCost(200,0,0,12), baseChance = 0.3f, targetKind = "hero" },
            new SpyMissionDef { id = "bribe", name = "Подкуп", description = "Выкачать золото и подорвать боевой дух армий.", cost = new ResourceCost(0,0,0,9), baseChance = 0.6f, targetKind = "faction" },
            new SpyMissionDef { id = "counter", name = "Контрразведка", description = "Усилить защиту от шпионов на 3 хода.", cost = new ResourceCost(0,0,0,4), baseChance = 1f, targetKind = "self" },
            new SpyMissionDef { id = "steal_influence", name = "Тайная печать", description = "Похитить влияние из Малого совета.", cost = new ResourceCost(0,0,0,7), baseChance = 0.5f, targetKind = "faction" },
        };

        public static SpyMissionDef Mission(string id) => Missions.FirstOrDefault(m => m.id == id);

        public static int Defense(GameState state, FactionState target)
        {
            int def = 0;
            if (state.Council.seats.ContainsValue(target.id)) def += 2;
            def += (int)target.Mod(ModKeys.SpyDefense);
            def += target.counterspy;
            foreach (var p in state.Provinces.Values.Where(p => p.controller == target.id))
                foreach (var b in p.buildings)
                    if (b == "spy_network" || b == "whisperers") def += 1;
            return def;
        }

        public static int Offense(GameState state, FactionState actor)
        {
            int off = actor.spyNetwork + (int)actor.Mod(ModKeys.SpyOffense);
            if (actor.HasTitle(state, "master_of_whisperers")) off += 2;
            return off;
        }

        public static SpyResult Execute(GameState state, FactionState actor, string missionId,
            string targetFactionId, string targetProvinceId = null, string targetHeroId = null)
        {
            var mission = Mission(missionId);
            if (mission == null) return new SpyResult(false, "Неизвестная миссия");
            if (!EffectEngine.CanAfford(actor, mission.cost)) return new SpyResult(false, "Недостаточно ресурсов");
            EffectEngine.Pay(actor, mission.cost);

            var target = state.Faction(targetFactionId);
            var province = state.Province(targetProvinceId);
            if (mission.id == "counter")
            {
                actor.timedMods.Add(new TimedModifier { key = ModKeys.SpyDefense, value = 6, turnsLeft = 3, source = "контрразведка" });
                actor.counterspy += 1;
                state.AddLog("spy", $"{actor.def.name} усиливает контрразведку", actor.id);
                return new SpyResult(true, "Сеть шептунов удвоила бдительность (+6 к защите на 3 хода)");
            }
            if (target == null) return new SpyResult(false, "Нет цели");
            if (target.isPlayer && state.Rng.NextDouble() < 0.35)
                state.Notify("Ваши шептуны заметили попытку шпионажа!");

            int off = Offense(state, actor);
            int def = Defense(state, target);
            float chance = mission.baseChance + (off - def) * 0.06f;
            if (mission.targetKind == "province" && province != null) chance += 0.05f;
            chance = EffectEngine.Clamp(chance, 0.05f, 0.95f);
            bool success = state.Rng.NextDouble() < chance;

            if (!success)
            {
                bool exposed = state.Rng.NextDouble() < 0.45;
                if (exposed)
                {
                    if (actor.relations.ContainsKey(target.id))
                        actor.relations[target.id] = Math.Max(-100, actor.relations[target.id] - 20);
                    if (target.relations.ContainsKey(actor.id))
                        target.relations[actor.id] = Math.Max(-100, target.relations[actor.id] - 20);
                    actor.influence = Math.Max(0, actor.influence - 5);
                    // трон может наказать
                    if (state.Council.throneHolder == target.id)
                        actor.recognition = Math.Max(0, actor.recognition - 6);
                }
                state.AddLog("spy", $"{actor.def.name}: провал миссии «{mission.name}»" + (exposed ? " (агент раскрыт)" : ""), actor.id);
                return new SpyResult(false, "Провал" + (exposed ? ": агент раскрыт, репутация пострадала" : ". Следов не осталось."), exposed);
            }

            actor.counterspy = Math.Max(0, actor.counterspy - 0); // успешная миссия не тратит защиту
            actor.spyNetwork += state.Rng.NextDouble() < 0.25 ? 1 : 0;
            string result;
            switch (mission.id)
            {
                case "recon":
                {
                    foreach (var p in state.Provinces.Values.Where(p => p.controller == target.id))
                        p.flags.Add("revealed:" + actor.id);
                    var armies = state.Armies.Values.Where(a => a.faction == target.id && a.alive)
                        .Select(a => $"{a.name} в {state.Province(a.province)?.def.name} ({a.UnitCount} отр.)").ToList();
                    result = "Армии: " + (armies.Count > 0 ? string.Join("; ", armies) : "не обнаружены");
                    break;
                }
                case "steal_tech":
                {
                    var stolen = target.techs.Where(t => !actor.techs.Contains(t)).OrderBy(_ => state.Rng.Next()).FirstOrDefault();
                    if (stolen != null && state.Content.TechById.TryGetValue(stolen, out var tech))
                    {
                        ProgressSystem.CompleteResearchImmediately(state, actor, stolen);
                        result = $"Похищена технология «{tech.name}»";
                    }
                    else result = "У противника нечего красть";
                    break;
                }
                case "sabotage":
                {
                    province ??= state.Provinces.Values.Where(p => p.controller == target.id).OrderBy(_ => state.Rng.Next()).FirstOrDefault();
                    if (province == null) { result = "Нет подходящей цели"; break; }
                    if (province.buildings.Count > 2)
                    {
                        var b = province.buildings.OrderBy(_ => state.Rng.Next()).Last();
                        province.buildings.Remove(b);
                        var name = state.Content.BuildingById.TryGetValue(b, out var bd) ? bd.name : b;
                        province.unrest = Math.Min(100, province.unrest + 6);
                        result = $"В {province.def.name} уничтожено: {name}";
                    }
                    else if (province.buildQueue.Count > 0)
                    {
                        province.buildQueue.Clear();
                        result = $"Строительство в {province.def.name} сорвано";
                    }
                    else result = "Саботаж не удался — нечего разрушать";
                    break;
                }
                case "incite":
                {
                    province ??= state.Provinces.Values.Where(p => p.controller == target.id).OrderBy(_ => state.Rng.Next()).FirstOrDefault();
                    if (province == null) { result = "Нет цели"; break; }
                    province.unrest = Math.Min(100, province.unrest + 22);
                    result = $"В {province.def.name} вспыхивают беспорядки (+22)";
                    break;
                }
                case "assassinate":
                {
                    var hero = target.heroes.Select(id => state.Heroes.TryGetValue(id, out var h) ? h : null)
                        .Where(h => h != null && h.alive && !h.wounded).OrderBy(_ => state.Rng.Next()).FirstOrDefault();
                    if (hero == null) { result = "Все герои противника вне досягаемости"; break; }
                    if (state.Rng.NextDouble() < 0.45)
                    {
                        hero.alive = false;
                        target.heroes.Remove(hero.defId);
                        result = $"Герой {hero.name} убит!";
                        state.Notify($"Шептуны доносят: {hero.name} убит.");
                    }
                    else
                    {
                        hero.wounded = true;
                        hero.woundedTurns = 3;
                        result = $"Герой {hero.name} тяжело ранен";
                    }
                    break;
                }
                case "bribe":
                {
                    int stolen = Math.Min(target.gold, Math.Max(30, target.gold / 8));
                    target.gold -= stolen;
                    actor.gold += stolen;
                    foreach (var armyId in target.armies)
                        if (state.Armies.TryGetValue(armyId, out var army))
                            foreach (var u in army.units.Where(x => x.alive))
                                u.morale = Math.Max(0, u.morale - 8);
                    result = $"Подкуплено {stolen} золота, армии противника теряют порядок";
                    break;
                }
                case "steal_influence":
                {
                    int stolen = Math.Min(target.influence, 12);
                    target.influence -= stolen;
                    actor.influence += stolen;
                    result = stolen > 0 ? $"Похищено {stolen} влияния" : "Влияние противника иссякло";
                    break;
                }
                default:
                    result = "Готово";
                    break;
            }
            state.AddLog("spy", $"{actor.def.name} → {target.def.name}: «{mission.name}» успешно. {result}", actor.id);
            return new SpyResult(true, result);
        }

        public static void ProcessTurn(GameState state)
        {
            foreach (var f in state.Factions.Values.Where(x => x.alive))
            {
                // сеть шептунов растёт от построек и титула
                int gain = state.Settings.spyNetworkBase;
                if (f.HasTitle(state, "master_of_whisperers")) gain += 1;
                foreach (var p in state.Provinces.Values.Where(p => p.controller == f.id))
                    foreach (var b in p.buildings)
                        if (b == "spy_network" || b == "whisperers") gain += 1;
                f.spyNetwork = Math.Min(12, f.spyNetwork + (state.Rng.NextDouble() < 0.4 ? 1 : 0));
                f.counterspy = Math.Max(0, f.counterspy - 1);

                // AI шпионит
                if (!f.isPlayer && state.Rng.NextDouble() < 0.22)
                {
                    var enemies = state.Factions.Values.Where(x => x.id != f.id && x.alive)
                        .OrderByDescending(x => x.recognition + x.tradeDominance).Take(2).ToList();
                    if (enemies.Count > 0)
                    {
                        var target = enemies[state.Rng.Next(enemies.Count)];
                        var mission = f.spyNetwork > 5
                            ? Missions[state.Rng.Next(1, 6)]
                            : Missions[0];
                        string targetProv = null;
                        if (mission.targetKind == "province")
                            targetProv = state.Provinces.Values
                                .Where(p => p.controller == target.id)
                                .OrderBy(_ => state.Rng.Next()).Select(p => p.id).FirstOrDefault();
                        if (mission.targetKind == "province" && targetProv == null) continue;
                        var spyHero = mission.targetKind == "hero"
                            ? target.heroes.FirstOrDefault() : null;
                        if (mission.targetKind == "hero" && spyHero == null) continue;
                        string spyTarget = mission.targetKind == "province" ? target.id : target.id;
                        if (EffectEngine.CanAfford(f, mission.cost))
                            Execute(state, f, mission.id, spyTarget, targetProv, spyHero);
                    }
                }
            }
        }
    }

    /// <summary>Вольные города/малые дома: отношения, вассалитет, аннексия, дань, набеги.</summary>
    public static class MinorHouseSystem
    {
        public static IEnumerable<string> HousesIn(GameState state, string region = null)
        {
            return state.MinorHouses.Values
                .Where(m => !m.annexed && (region == null || state.Provinces.Values.Any(p => p.house == m.house && p.def.region == region)))
                .Select(m => m.house);
        }

        public static int Relation(GameState state, FactionState f, string house)
        {
            if (f.minorRelations.TryGetValue(house, out var r)) return r;
            return state.MinorHouses.TryGetValue(house, out var mh) ? mh.relation : 0;
        }

        public static void AddRelation(GameState state, FactionState f, string house, int delta)
        {
            int cur = Relation(state, f, house);
            f.minorRelations[house] = (int)EffectEngine.Clamp(cur + delta, -100, 100);
            if (state.MinorHouses.TryGetValue(house, out var mh)) mh.relation = f.minorRelations[house];
        }

        public static List<string> ActionsFor(GameState state, FactionState f, string house)
        {
            var list = new List<string> { "gift", "trade" };
            int rel = Relation(state, f, house);
            if (rel >= 25) list.Add("marry");
            if (rel >= 15) list.Add("hire");
            if (rel >= 40) list.Add("threaten");
            if (rel >= 70) list.Add("vassalize");
            if (rel >= 85) list.Add("annex");
            if (state.MinorHouses.TryGetValue(house, out var mh) && mh.vassalOf == f.id) list.Add("call_banners");
            return list;
        }

        public static bool CanPerform(GameState state, FactionState f, string house, string action, out string error)
        {
            error = null;
            if (!state.MinorHouses.TryGetValue(house, out var mh)) { error = "Нет такого дома"; return false; }
            if (mh.annexed) { error = "Дом уже аннексирован"; return false; }
            if (!ActionsFor(state, f, house).Contains(action)) { error = "Действие недоступно при текущих отношениях"; return false; }
            var cost = ActionCost(action);
            if (!EffectEngine.CanAfford(f, cost)) { error = "Недостаточно ресурсов: " + cost.ToRu(); return false; }
            return true;
        }

        public static ResourceCost ActionCost(string action) => action switch
        {
            "gift" => new ResourceCost(60),
            "trade" => new ResourceCost(0, 40),
            "marry" => new ResourceCost(120, 0, 60),
            "hire" => new ResourceCost(150, 0, 50),
            "threaten" => new ResourceCost(0, 0, 0, 8),
            "vassalize" => new ResourceCost(200, 0, 0, 25),
            "annex" => new ResourceCost(350, 0, 0, 40),
            "call_banners" => new ResourceCost(0, 0, 80),
            _ => new ResourceCost()
        };

        public static string Perform(GameState state, FactionState f, string house, string action, out string error)
        {
            if (!CanPerform(state, f, house, action, out error)) return null;
            var mh = state.MinorHouses[house];
            if (!EffectEngine.CanAfford(f, ActionCost(action))) { error = "Недостаточно ресурсов"; return null; }
            EffectEngine.Pay(f, ActionCost(action));
            var houseName = state.HouseName(house);
            string result = null;
            switch (action)
            {
                case "gift":
                    AddRelation(state, f, house, 14 + (int)(f.Mod(ModKeys.InfluenceIncome)));
                    result = $"Дом {houseName} принимает дары (+14 к отношениям)";
                    break;
                case "trade":
                    AddRelation(state, f, house, 8);
                    f.materials += 60;
                    result = $"Торговое соглашение с {houseName}: +8 отношений, +60 материалов";
                    break;
                case "marry":
                {
                    AddRelation(state, f, house, 22);
                    f.influence += 6;
                    mh.relation = Relation(state, f, house);
                    mh.tributeTurnsLeft = 12;
                    result = $"Брак с домом {houseName}: +22 отношений, +6 влияния, дань на год";
                    break;
                }
                case "hire":
                {
                    AddRelation(state, f, house, 4);
                    var prov = state.Provinces.Values.FirstOrDefault(p => p.house == house && p.controller == null);
                    var pid = prov?.id ?? state.Provinces.Values.FirstOrDefault(p => p.house == house)?.id ?? f.capital;
                    var unitId = state.Content.Units.FirstOrDefault(u => u.faction == "shared" && u.kind == "infantry")?.id ?? "levy";
                    if (state.Content.UnitById.TryGetValue(unitId, out var ud))
                    {
                        var army = state.Armies.Values.FirstOrDefault(a => a.faction == f.id && a.province == pid)
                                   ?? MilitarySystem.CreateArmy(state, f, f.capital, "Наёмники");
                        army.units.Add(new UnitInstance(unitId, ud.name) { hp = 100, morale = 100 });
                    }
                    result = $"Дом {houseName} присылает наёмников";
                    break;
                }
                case "threaten":
                {
                    int roll = state.Rng.Next(0, 100) + Relation(state, f, house) / 2;
                    if (roll > 55) { MakeVassal(state, f, house, forced: true); result = $"Дом {houseName} склоняется перед силой — вассалитет!"; }
                    else { AddRelation(state, f, house, -25); mh.hostile = true; result = $"Дом {houseName} отвергает угрозу и затаивает обиду"; }
                    break;
                }
                case "vassalize":
                    MakeVassal(state, f, house, forced: false);
                    result = $"Дом {houseName} присягает вам";
                    break;
                case "annex":
                    AnnexHouse(state, f, house);
                    result = $"Дом {houseName} аннексирован. Земли входят в вашу державу.";
                    break;
                case "call_banners":
                {
                    var provs = state.Provinces.Values.Where(p => p.house == house).ToList();
                    int n = Math.Max(1, provs.Count / 2);
                    var army = state.Armies.Values.FirstOrDefault(a => a.faction == f.id && a.alive)
                               ?? MilitarySystem.CreateArmy(state, f, f.capital, "Знамёна вассалов");
                    var unitId = state.Content.Units.FirstOrDefault(u => u.faction == "shared" && u.kind == "infantry")?.id ?? "levy";
                    if (state.Content.UnitById.TryGetValue(unitId, out var ud))
                        for (int i = 0; i < n; i++) army.units.Add(new UnitInstance(unitId, ud.name) { hp = 90, morale = 90 });
                    AddRelation(state, f, house, -12);
                    result = $"Дом {houseName} выставляет {n} отрядов";
                    break;
                }
            }
            state.AddLog("diplomacy", $"{f.def.name} → {houseName}: {result}", f.id);
            if (f.isPlayer) state.Notify(result);
            return result;
        }

        public static void MakeVassal(GameState state, FactionState f, string house, bool forced)
        {
            if (!state.MinorHouses.TryGetValue(house, out var mh)) return;
            mh.vassalOf = f.id;
            mh.hostile = false;
            if (!forced) AddRelation(state, f, house, 10);
            foreach (var p in state.Provinces.Values.Where(p => p.house == house))
            {
                if (p.owner == null) p.controller = f.id;
                p.unrest = Math.Max(0, p.unrest - 10);
            }
            f.vassals[house] = 0;
            state.AddLog("diplomacy", $"Дом {state.HouseName(house)} становится вассалом {f.def.name}", f.id);
        }

        public static void AnnexHouse(GameState state, FactionState f, string house)
        {
            if (!state.MinorHouses.TryGetValue(house, out var mh)) return;
            mh.annexed = true;
            mh.vassalOf = f.id;
            f.annexed.Add(house);
            foreach (var p in state.Provinces.Values.Where(p => p.house == house))
            {
                p.controller = f.id;
                p.owner = f.id;
                p.unrest = Math.Min(100, p.unrest + 12);
            }
            state.AddLog("diplomacy", $"{f.def.name} аннексирует дом {state.HouseName(house)}", f.id);
            state.Notify($"Дом {state.HouseName(house)} аннексирован");
        }

        public static void ProcessTurn(GameState state)
        {
            foreach (var f in state.Factions.Values.Where(x => x.alive))
            {
                foreach (var house in f.vassals.Keys.ToList())
                {
                    f.vassals[house]++;
                    // вассалы иногда просят свободы
                    if (state.Rng.NextDouble() < 0.02)
                    {
                        state.MinorHouses[house].vassalOf = null;
                        f.vassals.Remove(house);
                        AddRelation(state, f, house, -25);
                        state.Notify($"Дом {state.HouseName(house)} расторгает вассалитет!");
                    }
                }
            }

            // враждебные малые дома устраивают набеги
            foreach (var mh in state.MinorHouses.Values)
            {
                if (mh.annexed || string.IsNullOrEmpty(mh.vassalOf)) { }
                if (mh.annexed) continue;
                var provs = state.Provinces.Values.Where(p => p.house == mh.house && p.owner == null).ToList();
                if (provs.Count == 0) { continue; }
                if (mh.hostile && state.Rng.NextDouble() < 0.25)
                {
                    var targetProv = provs.SelectMany(p => p.def.neighbors)
                        .Select(id => state.Province(id)).Where(p => p?.controller != null && p.house != mh.house)
                        .OrderBy(_ => state.Rng.Next()).FirstOrDefault();
                    if (targetProv != null) Raid(state, targetProv, state.FactionName(targetProv.controller), $"Отряды дома {state.HouseName(mh.house)}", 0.5f);
                }
            }

            // остывание отношений (медленно; верные вассалы не теряют доверие)
            if (state.Turn % 3 == 0)
            {
                foreach (var f in state.Factions.Values)
                {
                    foreach (var key in f.minorRelations.Keys.ToList())
                    {
                        if (f.vassals.ContainsKey(key)) continue;
                        if (f.minorRelations[key] > 0) f.minorRelations[key] = Math.Max(0, f.minorRelations[key] - 1);
                    }
                }
            }
        }

        public static void Raid(GameState state, ProvinceState prov, string victimName, string raiderName, float scale = 1f)
        {
            var victim = state.Faction(prov.controller);
            int stolenGold = 0;
            if (victim != null) { stolenGold = Math.Min(victim.gold, (int)(30 * scale) + prov.def.level * 5); victim.gold -= stolenGold; }
            prov.unrest = Math.Min(100, prov.unrest + 8 * scale);
            prov.raided = true;
            prov.raidCooldown = 2;
            prov.garrison = Math.Max(0, prov.garrison - (int)(8 * scale));
            state.AddLog("raid", $"{raiderName} разоряют {prov.def.name}" + (stolenGold > 0 ? $" (−{stolenGold} золота)" : ""), prov.controller);
            if (prov.controller == state.PlayerFaction)
                state.Notify($"Набег: {raiderName} разоряют {prov.def.name}!");
        }
    }

    /// <summary>Пираты, одичалые, горные кланы и прочие лагеря набежчиков.</summary>
    public static class RaiderSystem
    {
        public static void ProcessTurn(GameState state)
        {
            foreach (var camp in state.RaiderCamps.ToList())
            {
                if (camp.cleared)
                {
                    camp.respawnTurns--;
                    if (camp.respawnTurns <= 0)
                    {
                        camp.cleared = false;
                        camp.strength = 40 + state.Rng.Next(0, 35);
                        state.AddLog("raid", $"Лагерь «{camp.name}» отстроился заново");
                    }
                    continue;
                }
                if (state.Rng.NextDouble() > state.Settings.raiderChance) continue;

                var campProv = state.Province(camp.province);
                if (campProv == null) continue;
                var candidates = state.Provinces.Values
                    .Where(p => p.controller != null && p.def.neighbors.Contains(camp.province) ||
                                (p.def.port && campProv.def.port && p.def.region == campProv.def.region))
                    .ToList();
                if (candidates.Count == 0) continue;
                var target = candidates[state.Rng.Next(candidates.Count)];
                MinorHouseSystem.Raid(state, target, state.FactionName(target.controller), RaiderName(camp.kind), 1f);
                camp.strength += 3;
            }
        }

        public static string RaiderName(string kind) => kind switch
        {
            "wildlings" => "Одичалые",
            "mountain clans" => "Горные кланы",
            "reavers" => "Железные разбойники",
            _ => "Пираты"
        };

        public static bool ClearCamp(GameState state, FactionState f, string campId, out string message)
        {
            message = null;
            var camp = state.RaiderCamps.FirstOrDefault(c => c.id == campId && !c.cleared);
            if (camp == null) { message = "Лагерь уже уничтожен"; return false; }
            var army = state.Armies.Values.FirstOrDefault(a => a.faction == f.id && a.province == camp.province && a.alive && !a.IsEmpty);
            if (army == null) { message = "В провинции нет вашей армии"; return false; }
            int power = MilitarySystem.EstimateStrength(army);
            int roll = power + state.Rng.Next(0, 40) - camp.strength;
            if (roll <= 0)
            {
                foreach (var u in army.units.Where(u => u.alive)) { u.hp = Math.Max(5, u.hp - 20); u.morale = Math.Max(0, u.morale - 10); }
                message = $"Отряды «{camp.name}» отбились. Армия понесла потери.";
                state.AddLog("raid", message, f.id);
                return false;
            }
            camp.cleared = true;
            camp.respawnTurns = 8 + state.Rng.Next(0, 8);
            int loot = 80 + camp.strength * 2;
            f.gold += loot;
            f.materials += 30;
            f.influence += 3;
            f.recognition = Math.Min(100, f.recognition + 1);
            message = $"Логово «{camp.name}» уничтожено. Добыча: {loot} золота, 30 материалов.";
            state.AddLog("raid", $"{f.def.name} уничтожает лагерь «{camp.name}» (+{loot} золота)", f.id);
            if (f.isPlayer) state.Notify(message);
            return true;
        }
    }
}
