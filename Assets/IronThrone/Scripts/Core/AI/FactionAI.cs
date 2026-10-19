using System;
using System.Collections.Generic;
using System.Linq;
using IronThrone.Core.Content;
using IronThrone.Core.Game;

namespace IronThrone.Core.AI
{
    /// <summary>Простой утилитарный ИИ: экономика, армия, экспансия, дипломатия, вера.</summary>
    public static class FactionAI
    {
        public static void TakeTurn(GameState state, FactionState f)
        {
            if (!f.alive) return;
            try
            {
                ChooseResearch(state, f);
                ChooseFocus(state, f);
                Build(state, f);
                Recruit(state, f);
                DiplomacySystem.AiConsiderWars(state, f);
                MoveArmies(state, f);
                ClaimLand(state, f);
                UpgradeArmies(state, f);
                SpendTreasury(state, f);
                MinorDiplomacy(state, f);
                UseAdvisors(state, f);
                ManageTitles(state, f);
                CourtSupport(state, f);
                FaithActions(state, f);
            }
            catch (Exception ex)
            {
                state.AddLog("ai", $"{f.def.name}: ошибка ИИ — {ex.Message}", f.id);
            }
        }

        private static void ChooseResearch(GameState state, FactionState f)
        {
            if (f.currentResearch != null) return;
            var options = ProgressSystem.AvailableTechs(state, f).ToList();
            if (options.Count == 0) return;
            var pick = options.OrderByDescending(t => ScoreTech(f, t)).First();
            if (EffectEngine.CanAfford(f, pick.cost))
                ProgressSystem.StartResearch(state, f, pick.id, out _);
        }

        private static float ScoreTech(FactionState f, TechDef t)
        {
            float s = t.tier;
            foreach (var e in t.effects)
            {
                if (e.op == "modifier")
                {
                    if (e.key.Contains("Income")) s += e.value;
                    if (e.key.Contains("Army")) s += e.value * 2;
                    if (e.key.Contains("Vote")) s += e.value;
                    if (e.key.Contains("Spy")) s += e.value;
                }
                if (e.op == "unlockunit" || e.op == "recruit") s += 2;
            }
            if (f.def.traits.Contains("war") && t.branch == "war") s += 2;
            if (f.def.traits.Contains("trade") && t.branch == "economy") s += 2;
            if (f.def.traits.Contains("intrigue") && t.branch == "intrigue") s += 2;
            if (f.def.traits.Contains("faith") && t.branch == "faith") s += 2;
            return s;
        }

        /// <summary>
        /// ИИ выбирает фокус по ценности наград, чертам дома и обстановке. Небольшой разброс
        /// нужен, чтобы в разных партиях дом шёл разными путями (в том числе по веткам-развилкам).
        /// </summary>
        private static void ChooseFocus(GameState state, FactionState f)
        {
            if (f.currentFocus != null) return;
            var options = ProgressSystem.FocusesOf(state, f)
                .Where(fo => ProgressSystem.IsFocusAvailable(state, f, fo, out _))
                .ToList();
            if (options.Count == 0) return;

            float Score(FocusDef fo)
            {
                float s = 1f;
                foreach (var e in fo.effects)
                {
                    float v = e.amount != 0 ? e.amount : e.value;
                    switch ((e.op ?? "").ToLowerInvariant())
                    {
                        case "modifier":
                            if (e.key != null && e.key.Contains("Income")) s += v * 0.8f;
                            if (e.key != null && e.key.Contains("Army")) s += v * 1.4f;
                            if (e.key != null && e.key.Contains("Spy")) s += v;
                            if (e.key != null && e.key.Contains("Trade") || (e.key != null && e.key.Contains("Gold"))) s += v * 0.4f;
                            break;
                        case "resource": s += v / 150f; break;
                        case "recognition": s += v / 12f; break;
                        case "title": s += 3f; break;
                        case "capitalbuilding": s += 2f; break;
                        case "recruit": s += e.count * 1.5f; break;
                    }
                }
                if (f.def.traits.Contains("war") && fo.branch == "power") s += 2f;
                if (f.def.traits.Contains("trade") && fo.branch == "economy") s += 2f;
                if (f.def.traits.Contains("intrigue") && fo.branch == "intrigue") s += 2f;
                if (f.def.traits.Contains("faith") && fo.branch == "faith") s += 2f;
                if (f.def.traits.Contains("raid") && fo.branch == "power") s += 1f;
                return s;
            }

            var top = options.OrderByDescending(Score).ThenBy(_ => state.Rng.Next()).Take(2).ToList();
            var pick = top.Count == 1 || state.Rng.NextDouble() < 0.7 ? top[0] : top[1];
            ProgressSystem.StartFocus(state, f, pick.id, out _);
        }

        private static void Build(GameState state, FactionState f)
        {
            var provs = state.Provinces.Values.Where(p => p.controller == f.id).ToList();
            if (provs.Count == 0) return;
            if (state.Rng.NextDouble() > 0.65) return;

            var candidates = new List<(ProvinceState p, BuildingDef b, float score)>();
            foreach (var p in provs)
            {
                foreach (var b in state.Content.Buildings)
                {
                    if (!EconomySystem.CanBuild(state, f, p, b.id, out _)) continue;
                    float score = 0;
                    foreach (var m in b.modifiers)
                    {
                        if (m.key.Contains("Income")) score += m.value;
                        if (m.key.Contains("Army")) score += m.value * 3;
                        if (m.key.Contains("Spy")) score += m.value * 2;
                        if (m.key.Contains("Vote")) score += m.value * 2;
                        if (m.key.Contains("Faith")) score += m.value * 1.5f;
                    }
                    score += b.effects.Count * 1.5f;
                    if (p.def.isCapital) score += 2;
                    if (p.unrest > 40 && b.id.Contains("guard") || b.id.Contains("sept")) score += 3;
                    if (b.unique) score += 2;
                    candidates.Add((p, b, score));
                }
            }
            if (candidates.Count == 0) return;
            var best = candidates.OrderByDescending(c => c.score + (float)state.Rng.NextDouble()).First();
            EconomySystem.StartBuild(state, f, best.p, best.b.id, out _);
        }

        /// <summary>
        /// Найм войск. Дом набирает до нескольких отрядов за ход, пока есть золото и еда,
        /// поэтому казна не копится мёртвым грузом, а армии восстанавливаются после потерь.
        /// </summary>
        private static void Recruit(GameState state, FactionState f)
        {
            var owned = state.Provinces.Values.Where(p => p.controller == f.id).ToList();
            if (owned.Count == 0) return;

            var options = state.Content.Units
                .Where(u => (u.faction == "shared" || u.faction == f.house) &&
                            (string.IsNullOrEmpty(u.requiresTech) || f.HasTech(u.requiresTech)))
                .OrderByDescending(u => u.stats.attack + u.stats.defense)
                .ToList();
            if (options.Count == 0) return;

            int perArmy = MilitarySystem.MaxUnitsInArmy(state, f);
            int maxUnits = perArmy * Math.Max(1, state.Settings.maxArmies);
            int recruits = 0;
            for (int round = 0; round < 3; round++)
            {
                int totalUnits = f.armies.Select(id => state.Armies.TryGetValue(id, out var a) ? a.UnitCount : 0).Sum();
                if (totalUnits >= maxUnits) return;
                if (f.armies.Count >= state.Settings.maxArmies && totalUnits >= state.Settings.armyMaxUnits * f.armies.Count) return;

                var army = f.armies.Select(id => state.Armies.TryGetValue(id, out var a) ? a : null)
                    .Where(a => a != null && a.alive && a.UnitCount < perArmy)
                    .OrderByDescending(a => a.UnitCount).FirstOrDefault();

                string provinceId = army?.province;
                if (provinceId == null || state.Province(provinceId)?.controller != f.id)
                {
                    provinceId = owned.OrderByDescending(p => p.def.isCapital)
                        .ThenByDescending(p => p.def.level).Select(p => p.id).FirstOrDefault();
                }
                if (provinceId == null) return;

                // берём самый сильный отряд, который дом может себе позволить
                UnitDef pick = null;
                foreach (var u in options)
                    if (f.gold >= u.cost.gold * 1.25f && f.food >= u.cost.food * 1.25f &&
                        f.materials >= u.cost.materials * 1.25f)
                    { pick = u; break; }
                if (pick == null) return;

                if (!MilitarySystem.Recruit(state, f, pick.id, 1, provinceId, out _)) return;
                recruits++;
                if (f.gold < 120 && f.food < 120) return;
            }
            if (recruits > 0 && f.isPlayer == false) { /* журнал уже пишет MilitarySystem */ }
        }

        private static void MoveArmies(GameState state, FactionState f)
        {
            foreach (var armyId in f.armies.ToList())
            {
                if (!state.Armies.TryGetValue(armyId, out var army) || !army.alive) continue;
                while (army.movementLeft > 0 && army.alive && !army.IsEmpty)
                {
                    var cur = state.Province(army.province);
                    if (cur == null) break;
                    var options = cur.def.neighbors
                        .Concat(SeaNeighbors(state, army.province))
                        .Distinct()
                        .Where(id => state.Province(id) != null)
                        .Select(id => state.Province(id))
                        .Where(p => MilitarySystem.CanMove(state, army, p.id, out _))
                        .ToList();
                    if (options.Count == 0) break;
                    var scored = options.Select(p => (p, score: ScoreTarget(state, f, army, p)))
                        .OrderByDescending(t => t.score).ToList();
                    if (scored[0].score <= 0) break;
                    if (scored[0].score < 3 && state.Rng.NextDouble() < 0.4) break;
                    int before = army.movementLeft;
                    if (!MilitarySystem.MoveArmy(state, army, scored[0].p.id, out var msg))
                    {
                        if (army.movementLeft == before) break;
                    }
                    if (msg != null) state.AddLog("army", $"{f.def.name}: {msg}", f.id);
                }
            }
        }

        private static IEnumerable<string> SeaNeighbors(GameState state, string provinceId)
        {
            var p = state.Province(provinceId);
            if (p == null || !p.def.port) yield break;
            foreach (var route in state.Map.seaRoutes)
            {
                if (route.Count < 2) continue;
                if (route[0] == provinceId) yield return route[1];
                else if (route[1] == provinceId) yield return route[0];
            }
        }

        private static float ScoreTarget(GameState state, FactionState f, ArmyState army, ProvinceState p)
        {
            if (p.controller == f.id) return -5;
            float score = 2f;
            if (p.controller == null) score += 6; // вольные земли
            else
            {
                var owner = state.Faction(p.controller);
                if (owner == null) score += 4;
                else
                {
                    float rel = f.relations.TryGetValue(owner.id, out var r) ? r : 0;
                    score += 4 - rel / 25f;
                    if (rel > 40) score -= 10;
                }
            }
            if (p.def.isCapital) score += 6;
            if (p.def.region != null && state.Provinces.Values.Count(x => x.def.region == p.def.region && x.house == f.house) > 0)
                score += 3;
            if (p.def.port) score += 1;
            int defenders = state.Armies.Values.Where(a => a.alive && a.province == p.id && a.faction != f.id)
                .Select(a => MilitarySystem.EstimateStrength(a)).Sum();
            defenders += p.garrison;
            int myPower = MilitarySystem.EstimateStrength(army);
            if (defenders > myPower) score -= 12;
            else if (defenders > 0) score -= 2;
            if (p.controller == null && p.garrison > myPower) score -= 6;
            return score;
        }

        private static void ClaimLand(GameState state, FactionState f)
        {
            if (!f.growthReady) return;
            var claims = TerritorySystem.Claimable(state, f);
            if (claims.Count == 0)
            {
                // свободных земель нет — вкладываем освоение в улучшения своих провинций
                foreach (var prov in state.Provinces.Values.Where(x => x.controller == f.id)
                             .OrderByDescending(x => x.def.isCapital).ThenByDescending(x => x.def.level))
                {
                    int count = prov.buildings.Count(id => state.Content.BuildingById.TryGetValue(id, out var bd) && bd.slot == TerritorySystem.ImprovementSlot);
                    if (count >= TerritorySystem.ImprovementLimit(state, prov)) continue;
                    var improv = TerritorySystem.Improvements(state, f, prov).FirstOrDefault();
                    if (improv == null) continue;
                    if (TerritorySystem.AddImprovement(state, f, prov, improv.id, true, out _)) return;
                }
                return;
            }
            var target = claims.OrderByDescending(p => p.def.isCapital)
                .ThenByDescending(p => p.def.level)
                .FirstOrDefault();
            if (target == null) return;
            var improvements = TerritorySystem.Improvements(state, f, target);
            string pick = improvements.FirstOrDefault(b => b.maxPerEmpire > 1 || !state.Provinces.Values.Any(x => x.buildings.Contains(b.id)))?.id;
            TerritorySystem.Claim(state, f, target.id, pick, out _);
        }

        private static void UpgradeArmies(GameState state, FactionState f)
        {
            if (f.gold < 320 || f.materials < 120) return;
            int done = 0;
            foreach (var army in f.armies.Select(id => state.Armies.TryGetValue(id, out var a) ? a : null).Where(a => a != null && a.alive))
            {
                if (done >= 2) break;
                if (state.Province(army.province)?.controller != f.id) continue;
                var unit = army.units.Where(u => u.alive).FirstOrDefault(u => MilitarySystem.NextUpgrade(state, f, u) != null);
                if (unit == null) continue;
                if (MilitarySystem.UpgradeUnitType(state, f, army, unit, out _)) done++;
            }
        }

        /// <summary>Богатые дома вкладывают излишки в землю, а не копят золото до бесконечности.</summary>
        private static void SpendTreasury(GameState state, FactionState f)
        {
            if (f.gold >= 900 && (f.materials < 220 || f.food < 220) && f.gold >= EconomySystem.SuppliesGoldCost)
                EconomySystem.BuySupplies(state, f, out _);
            if (f.gold < 600) return;
            int bought = 0;
            foreach (var prov in state.Provinces.Values.Where(x => x.controller == f.id)
                         .OrderByDescending(x => x.def.isCapital).ThenByDescending(x => x.def.level))
            {
                if (bought >= 2) break;
                int count = prov.buildings.Count(id => state.Content.BuildingById.TryGetValue(id, out var bd) && bd.slot == TerritorySystem.ImprovementSlot);
                if (count >= TerritorySystem.ImprovementLimit(state, prov)) continue;
                var improv = TerritorySystem.Improvements(state, f, prov).FirstOrDefault();
                if (improv == null) continue;
                if (TerritorySystem.AddImprovement(state, f, prov, improv.id, false, out _)) bought++;
            }

            // столичный ансамбль: достраиваем слоты столицы, чтобы собирать комплекты построек
            var cap = state.Province(f.capital);
            if (cap != null && cap.controller == f.id && f.gold >= 500)
            {
                int built = 0;
                var candidates = state.Content.Buildings
                    .Where(b => b.slot != TerritorySystem.ImprovementSlot && !string.IsNullOrEmpty(b.slot))
                    .Where(b => string.IsNullOrEmpty(b.uniqueToFaction) || b.uniqueToFaction == f.house)
                    .Where(b => string.IsNullOrEmpty(b.requiresTech) || f.HasTech(b.requiresTech))
                    .Where(b => !cap.buildings.Contains(b.id) && !cap.buildQueue.ContainsKey(b.id))
                    .OrderByDescending(b => cap.buildings.Count(id =>
                        state.Content.BuildingById.TryGetValue(id, out var bd) && bd.slot == b.slot))
                    .ThenBy(b => b.cost.gold)
                    .ToList();
                foreach (var b in candidates)
                {
                    if (built >= 2) break;
                    if (f.gold < b.cost.gold * 1.5f) continue;
                    if (EconomySystem.StartBuild(state, f, cap, b.id, out _)) built++;
                }
            }
        }

        private static void MinorDiplomacy(GameState state, FactionState f)
        {
            var houses = state.MinorHouses.Values.Where(mh => !mh.annexed && mh.vassalOf == null).ToList();
            if (houses.Count == 0) return;
            foreach (var mh in houses.OrderByDescending(m => MinorHouseSystem.Relation(state, f, m.house)).Take(3))
            {
                int rel = MinorHouseSystem.Relation(state, f, mh.house);
                if (rel >= 70 && f.influence >= 30 && state.Rng.NextDouble() < 0.25)
                {
                    MinorHouseSystem.Perform(state, f, mh.house, "vassalize", out _);
                    continue;
                }
                if (rel < 62 && f.gold > 260 && state.Rng.NextDouble() < 0.3)
                    MinorHouseSystem.Perform(state, f, mh.house, "gift", out _);
            }
        }

        private static void UseAdvisors(GameState state, FactionState f)
        {
            foreach (var a in f.advisors)
            {
                if (a.cooldown > 0) continue;
                if (state.Rng.NextDouble() < 0.18)
                    ProgressSystem.UseAdvisor(state, f, a.defId, out _);
            }
        }

        /// <summary>Политический двор: дары домам, которые держат места в совете или могут поддержать.</summary>
        private static void CourtSupport(GameState state, FactionState f)
        {
            if (f.gold < 700) return;
            var throne = state.Council.throneHolder == f.id;
            if (!throne && PoliticsSystem.SeatsOf(state, f.id) < 2) return;
            if (state.Rng.NextDouble() > 0.3) return;

            var candidates = state.Factions.Values
                .Where(x => x.id != f.id && x.alive)
                .Where(x => DiplomacySystem.WarBetween(state, f.id, x.id) == null)
                .Select(x => (faction: x, relation: DiplomacySystem.Relation(state, f.id, x.id),
                              seat: state.Council.seats.ContainsValue(x.id)))
                .Where(t => t.relation < 60)
                .OrderByDescending(t => t.seat)
                .ThenByDescending(t => t.relation)
                .ToList();
            if (candidates.Count == 0) return;
            var pick = candidates[0];
            DiplomacySystem.SendGift(state, f, pick.faction.id, out _);
        }

        /// <summary>Вера: миссионеры и реформа, если дом контролирует святыню.</summary>
        private static void FaithActions(GameState state, FactionState f)
        {
            if (string.IsNullOrEmpty(f.faith)) return;
            if (!f.HasFlag("faith_reformed") && f.piety >= 120 && FaithSystem.IsHead(state, f))
            {
                FaithSystem.ReformFaith(state, f, out _);
                return;
            }
            if (f.piety < 30 || state.Rng.NextDouble() > 0.3) return;
            var target = state.Provinces.Values
                .Where(p => p.controller == f.id)
                .OrderBy(p => p.faith.TryGetValue(f.faith, out var v) ? v : 0)
                .FirstOrDefault();
            if (target != null) FaithSystem.SendMissionary(state, f, target.id, out _);
        }

        private static void ManageTitles(GameState state, FactionState f)
        {
            if (state.Council.throneHolder != f.id) return;
            if (state.Rng.NextDouble() > 0.08) return;
            // раздаёт титулы дружественным домам
            var friendly = state.Factions.Values
                .Where(x => x.id != f.id && x.alive &&
                            DiplomacySystem.Relation(state, f.id, x.id) > 20 &&
                            DiplomacySystem.WarBetween(state, f.id, x.id) == null)
                .OrderByDescending(x => DiplomacySystem.Relation(state, f.id, x.id))
                .ThenByDescending(x => x.recognition)
                .FirstOrDefault();
            var vacant = state.Content.Titles.FirstOrDefault(t =>
                !state.Council.seats.ContainsKey(t.id) || state.Council.seats[t.id] != f.id);
            if (vacant == null || f.influence < 25) return;
            // три места корона держит за собой, остальные раздаёт сторонникам
            int own = PoliticsSystem.SeatsOf(state, f.id);
            string receiver = own >= 3 && friendly != null ? friendly.id : f.id;
            PoliticsSystem.GrantTitle(state, vacant.id, receiver, out _, actorId: f.id);
        }
    }
}
