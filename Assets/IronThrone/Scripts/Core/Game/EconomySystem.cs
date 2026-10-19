using System;
using System.Collections.Generic;
using System.Linq;
using IronThrone.Core.Content;

namespace IronThrone.Core.Game
{
    public class IncomeBreakdown
    {
        public int gold;
        public int materials;
        public int food;
        public int influence;
        public int piety;
        public List<string> lines = new List<string>();

        public override string ToString() =>
            $"+{gold} зол., +{materials} мат., +{food} прод., +{influence} вл., +{piety} веры";
    }

    public static class EconomySystem
    {
        public static IncomeBreakdown CalculateIncome(GameState state, FactionState f)
        {
            var inc = new IncomeBreakdown();
            var provinces = state.Provinces.Values.Where(p => p.controller == f.id).ToList();
            if (provinces.Count == 0) return inc;

            int gold = 0, materials = 0, food = 0, influence = 0, piety = 0;
            foreach (var p in provinces)
            {
                int lvl = p.def.level;
                int baseGold = 3 + lvl;
                int baseMat = 2 + lvl / 2;
                int baseFood = 4 + lvl;
                switch (p.def.terrain)
                {
                    case "plains": baseFood += 3; baseGold += 1; break;
                    case "forest": baseMat += 4; baseFood += 1; break;
                    case "hills": baseMat += 4; baseGold += 1; break;
                    case "mountains": baseMat += 6; baseFood -= 2; baseGold += 1; break;
                    case "desert": baseFood -= 3; baseGold += 1; break;
                    case "marsh": baseFood += 1; baseMat += 1; break;
                    case "snow": baseFood -= 2; baseMat += 2; break;
                }
                if (p.def.river) baseFood += 2;
                if (p.def.port) { baseGold += 4; }
                if (p.def.isCapital) { baseGold += 3; influence += 1; }
                if (p.def.kind == "town") baseGold += 3;
                if (p.def.kind == "castle") baseGold += 1;

                float unrestFactor = 1f - Math.Min(0.65f, p.unrest / 130f);
                if (p.raided) unrestFactor *= 0.5f;
                if (state.Council.throneHolder == f.id && p.id == "kings_landing") { baseGold += 6; influence += 1; }

                gold += (int)(baseGold * unrestFactor);
                materials += (int)(baseMat * unrestFactor);
                food += (int)(baseFood * (0.6f + 0.4f * unrestFactor));

                // постройки
                foreach (var bId in p.buildings)
                {
                    if (!state.Content.BuildingById.TryGetValue(bId, out var b)) continue;
                    foreach (var m in b.modifiers)
                    {
                        if (m == null) continue;
                        switch (m.key)
                        {
                            case ModKeys.GoldIncome: gold += (int)m.value; break;
                            case ModKeys.MaterialsIncome: materials += (int)m.value; break;
                            case ModKeys.FoodIncome: food += (int)m.value; break;
                            case ModKeys.InfluenceIncome: influence += (int)m.value; break;
                            case ModKeys.PietyIncome: piety += (int)m.value; break;
                        }
                    }
                }
            }

            float goldMult = f.ModPct(ModKeys.GoldIncome);
            float matMult = f.ModPct(ModKeys.MaterialsIncome);
            float foodMult = f.ModPct(ModKeys.FoodIncome);
            float infMult = f.ModPct(ModKeys.InfluenceIncome);
            float pieMult = f.ModPct(ModKeys.PietyIncome);

            gold = (int)(gold * goldMult) + (int)f.Mod(ModKeys.GoldIncome);
            materials = (int)(materials * matMult) + (int)f.Mod(ModKeys.MaterialsIncome);
            food = (int)(food * foodMult) + (int)f.Mod(ModKeys.FoodIncome);
            influence = (int)(influence * infMult) + (int)f.Mod(ModKeys.InfluenceIncome);
            piety = (int)(piety * pieMult) + (int)f.Mod(ModKeys.PietyIncome);

            // титулы и совет
            foreach (var kv in state.Council.seats)
            {
                if (kv.Value != f.id) continue;
                if (state.Content.TitleById.TryGetValue(kv.Key, out var title)) influence += title.influencePerTurn;
            }
            if (state.Council.throneHolder == f.id) influence += 2;

            // столичный ансамбль
            var setText = CapitalSetBonus(state, f, out var setGold, out var setFood, out var setPiety, out var setInfluence);
            if (setText != null)
            {
                gold += setGold; food += setFood; piety += setPiety; influence += setInfluence;
            }

            // вассальные малые дома платят дань
            foreach (var mh in state.MinorHouses.Values.Where(m => m.vassalOf == f.id))
            {
                var house = state.Map.HouseById.TryGetValue(mh.house, out var h) ? h : null;
                int size = state.Provinces.Values.Count(p => p.house == mh.house);
                gold += 2 + size * 2;
                food += size;
                if (house != null && house.bonuses != null)
                {
                    foreach (var b in house.bonuses)
                        if (b.key == ModKeys.InfluenceIncome) influence += (int)b.value;
                }
            }

            // вера и святыни
            if (!string.IsNullOrEmpty(f.faith) && state.Content.FaithById.TryGetValue(f.faith, out var faith))
            {
                foreach (var site in faith.holySites)
                {
                    var p = state.Province(site);
                    if (p?.controller == f.id) { piety += 3; influence += 1; }
                }
                if (state.Province(faith.headProvince)?.controller == f.id) piety += 2;
            }

            // закон о налогах и т.п. — уже в модификаторах
            gold += (int)f.Mod(ModKeys.GoldFlat);
            inc.gold = Math.Max(0, gold);
            inc.materials = Math.Max(0, materials);
            inc.food = Math.Max(0, food);
            inc.influence = Math.Max(0, influence);
            inc.piety = Math.Max(0, piety);
            f.goldIncome = inc.gold;
            f.materialsIncome = inc.materials;
            f.foodIncome = inc.food;
            f.influenceIncome = inc.influence;
            f.pietyIncome = inc.piety;
            return inc;
        }

        public static void ApplyIncome(GameState state, FactionState f)
        {
            var inc = CalculateIncome(state, f);
            f.gold += inc.gold;
            f.materials += inc.materials;
            f.food += inc.food;
            f.influence += inc.influence;
            f.piety += inc.piety;

            // инфляция цен на влияние: мягкий рост стоимости лоббирования не нужен, но логируем
            state.AddLog("economy", $"{f.def.name}: {inc}", f.id);
        }

        // ------------------------------------------------------------------ постройки
        public static List<string> SlotsFor(GameState state, ProvinceState p)
        {
            var slots = new List<string>();
            int lvl = p.def.level;
            slots.Add("economy");
            if (lvl >= 2) slots.Add("military");
            if (lvl >= 3) slots.Add("temple");
            if (lvl >= 4) slots.Add("economy");
            if (lvl >= 5) slots.Add("special");
            if (p.def.port) slots.Add("port");
            if (p.def.isCapital) { slots.Add("castle"); slots.Add("economy"); }
            foreach (var unlock in state.Settings.capitalSlots)
                if (p.def.isCapital && unlock.level <= lvl) slots.AddRange(unlock.slots);
            return slots;
        }

        public static int UsedSlots(GameState state, ProvinceState p, string slotType)
        {
            int used = 0;
            foreach (var id in p.buildings.Concat(p.buildQueue.Keys))
            {
                if (state.Content.BuildingById.TryGetValue(id, out var b) && b.slot == slotType) used++;
            }
            return used;
        }

        public static int FreeSlots(GameState state, ProvinceState p, string slotType)
        {
            return SlotsFor(state, p).Count(s => s == slotType) - UsedSlots(state, p, slotType);
        }

        /// <summary>Сколько таких построек у дома: готовые и в очереди.</summary>
        public static int CountBuilding(GameState state, FactionState f, string buildingId)
        {
            int count = 0;
            foreach (var prov in state.Provinces.Values.Where(x => x.controller == f.id))
            {
                if (prov.buildings.Contains(buildingId)) count++;
                if (prov.buildQueue.ContainsKey(buildingId)) count++;
            }
            return count;
        }

        public static bool CanBuild(GameState state, FactionState f, ProvinceState p, string buildingId, out string error)
        {
            error = null;
            if (!state.Content.BuildingById.TryGetValue(buildingId, out var b)) { error = "Неизвестная постройка"; return false; }
            if (p.controller != f.id) { error = "Провинция не под вашим контролем"; return false; }
            if (p.buildings.Contains(buildingId)) { error = "Уже построено"; return false; }
            if (p.buildQueue.ContainsKey(buildingId)) { error = "Уже строится"; return false; }
            if (b.unique && state.Provinces.Values.Any(x => x.buildings.Contains(buildingId))) { error = "Уникальная постройка уже существует"; return false; }
            if (b.maxPerEmpire < 99 && CountBuilding(state, f, buildingId) >= b.maxPerEmpire)
            { error = $"Лимит дома: не больше {b.maxPerEmpire} таких построек"; return false; }
            if (!string.IsNullOrEmpty(b.uniqueToFaction) && b.uniqueToFaction != f.house) { error = "Недоступно вашему дому"; return false; }
            if (!string.IsNullOrEmpty(b.requiresTech) && !f.HasTech(b.requiresTech)) { error = "Требуется технология"; return false; }
            if (FreeSlots(state, p, b.slot) <= 0) { error = $"Нет свободного слота «{SlotName(b.slot)}»"; return false; }
            var cost = b.cost.Scaled(f.ModPct(ModKeys.BuildCostPct));
            if (!EffectEngine.CanAfford(f, cost)) { error = "Недостаточно ресурсов: " + cost.ToRu(); return false; }
            return true;
        }

        public static bool StartBuild(GameState state, FactionState f, ProvinceState p, string buildingId, out string error)
        {
            if (!CanBuild(state, f, p, buildingId, out error)) return false;
            var b = state.Content.BuildingById[buildingId];
            EffectEngine.Pay(f, b.cost.Scaled(f.ModPct(ModKeys.BuildCostPct)));
            p.buildQueue[buildingId] = Math.Max(1, b.buildTurns);
            state.AddLog("build", $"{f.def.name}: начато строительство «{b.name}» в {p.def.name}", f.id);
            return true;
        }

        public static void ProcessBuildQueues(GameState state)
        {
            foreach (var p in state.Provinces.Values)
            {
                if (p.buildQueue.Count == 0) continue;
                foreach (var key in p.buildQueue.Keys.ToList())
                {
                    int left = p.buildQueue[key] - 1;
                    if (left <= 0)
                    {
                        p.buildQueue.Remove(key);
                        if (!p.buildings.Contains(key))
                        {
                            p.buildings.Add(key);
                            var f = state.Faction(p.controller);
                            if (f != null)
                            {
                                var b = state.Content.BuildingById.TryGetValue(key, out var bd) ? bd : null;
                                if (b != null)
                                    EffectEngine.ApplyAll(b.effects, new EffectContext
                                    {
                                        State = state, Source = f, TargetFaction = f, TargetProvince = p
                                    });
                                state.AddLog("build", $"{f.def.name}: в {p.def.name} построено «{b?.name ?? key}»", f.id);
                                if (f.isPlayer) state.Notify($"Построено: {b?.name ?? key} в {p.def.name}");
                            }
                        }
                    }
                    else p.buildQueue[key] = left;
                }
            }
        }

        public const int SuppliesGoldCost = 300;

        /// <summary>
        /// Бонусы столичного ансамбля: за заполненные однотипные слоты в столице дом получает
        /// дополнительные ресурсы. Это и есть «бонусы за постройки» из Dune: Spice Wars.
        /// </summary>
        public static string CapitalSetBonus(GameState state, FactionState f, out int gold, out int food, out int piety, out int influence)
        {
            gold = food = piety = influence = 0;
            var cap = state.Province(f?.capital);
            if (cap == null || cap.controller != f.id) return null;
            var counts = new Dictionary<string, int>();
            foreach (var id in cap.buildings)
            {
                if (!state.Content.BuildingById.TryGetValue(id, out var b)) continue;
                counts.TryGetValue(b.slot, out var n);
                counts[b.slot] = n + 1;
            }
            var parts = new List<string>();
            if (counts.TryGetValue("economy", out var eco) && eco >= 3)
            { gold += 8; food += 4; parts.Add("торговый посад (+8 золота, +4 еды)"); }
            if (counts.TryGetValue("military", out var mil) && mil >= 3)
            { gold += 2; influence += 1; parts.Add("военный квартал (+2 золота, +1 влияния)"); }
            if (counts.TryGetValue("temple", out var tem) && tem >= 2)
            { piety += 3; parts.Add("соборный двор (+3 веры)"); }
            if (counts.TryGetValue("port", out var por) && por >= 2)
            { gold += 5; parts.Add("торговая гавань (+5 золота)"); }
            if (counts.TryGetValue("special", out var spc) && spc >= 2)
            { influence += 1; piety += 1; parts.Add("двор учёных (+1 влияния, +1 веры)"); }
            return parts.Count == 0 ? null : string.Join("; ", parts);
        }

        /// <summary>Закупка припасов на рынке: золото в материалы и продовольствие.</summary>
        public static bool BuySupplies(GameState state, FactionState f, out string error)
        {
            error = null;
            if (f == null || !f.alive) { error = "Дом пал"; return false; }
            var cost = new ResourceCost { gold = SuppliesGoldCost };
            if (!EffectEngine.CanAfford(f, cost)) { error = $"Нужно {SuppliesGoldCost} золота"; return false; }
            EffectEngine.Pay(f, cost);
            int materials = (int)(120 * f.ModPct(ModKeys.MaterialsIncome));
            int food = (int)(90 * f.ModPct(ModKeys.FoodIncome));
            f.materials += Math.Max(30, materials);
            f.food += Math.Max(20, food);
            state.AddLog("economy", $"{f.def.name}: закуплены припасы (+{Math.Max(30, materials)} материалов, +{Math.Max(20, food)} еды)", f.id);
            if (f.isPlayer) state.Notify($"Закуплены припасы: +{Math.Max(30, materials)} материалов, +{Math.Max(20, food)} еды");
            return true;
        }

        public static string SlotName(string slot) => slot switch
        {
            "economy" => "экономика",
            "military" => "военное дело",
            "temple" => "храм",
            "port" => "порт",
            "castle" => "замок",
            "special" => "особый",
            _ => slot
        };

        public static void RecalculateTradeDominance(GameState state)
        {
            var ports = state.Provinces.Values.Where(p => p.def.port).ToList();
            var routes = state.Map.seaRoutes.Where(r => r.Count >= 2).ToList();
            var markets = state.Provinces.Values.Where(p => p.buildings.Contains("market") || p.buildings.Contains("trade_post")).ToList();

            foreach (var f in state.Factions.Values.Where(f => f.alive))
            {
                int myPorts = ports.Count(p => p.controller == f.id);
                int myRoutes = routes.Count(r =>
                {
                    var a = state.Province(r[0]); var b = state.Province(r[1]);
                    return a?.controller == f.id && b?.controller == f.id;
                });
                int myMarkets = markets.Count(p => p.controller == f.id);

                float portShare = ports.Count == 0 ? 0 : (float)myPorts / ports.Count;
                float routeShare = routes.Count == 0 ? 0 : (float)myRoutes / routes.Count;
                float marketShare = markets.Count == 0 ? 0 : (float)myMarkets / markets.Count;

                float dominance = portShare * 55f + routeShare * 25f + marketShare * 20f;
                dominance *= f.ModPct(ModKeys.TradeDominance);
                dominance += f.Mod(ModKeys.TradeDominance);
                f.tradeDominance = EffectEngine.Clamp(dominance, 0, 100);
                f.tradeRoutePoints = myPorts + myRoutes * 2;
            }
        }
    }
}
