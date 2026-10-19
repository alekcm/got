using System;
using System.Collections.Generic;
using System.Linq;
using IronThrone.Core.Content;

namespace IronThrone.Core.Game
{
    /// <summary>
    /// Рост территорий (аналог роста города в Age of Wonders 4).
    /// Каждый ход дом накапливает «освоение»: чем больше развитых провинций и построек,
    /// тем быстрее. Накопленное освоение тратится на присоединение соседней ничейной
    /// провинции; вместе с провинцией выбирается одно провинциальное улучшение
    /// (постройка со слотом «province»), которое сразу в ней появляется.
    /// </summary>
    public static class TerritorySystem
    {
        public const string ImprovementSlot = "province";

        /// <summary>Сколько освоения даёт дом за ход.</summary>
        public static float GrowthPerTurn(GameState state, FactionState f)
        {
            if (f == null || !f.alive) return 0f;
            float total = 0f;
            foreach (var p in state.Provinces.Values.Where(p => p.controller == f.id))
            {
                float value = 0.7f + 0.2f * p.def.level + 0.18f * p.buildings.Count;
                if (p.def.isCapital) value += 0.8f;
                if (p.unrest > 45f) value *= 0.6f;
                if (p.raided) value *= 0.75f;
                total += value;
            }
            total *= 1f + f.Mod(ModKeys.GrowthBonus) / 100f;
            return (float)Math.Round(total, 2);
        }

        /// <summary>Порог, за которым можно присоединить одну провинцию.</summary>
        public static int GrowthThreshold(GameState state, FactionState f) =>
            Math.Max(20, state.Settings.claimGrowth + 8 * f.vassals.Count);

        /// <summary>Провинции, которые дом может присоединить прямо сейчас.</summary>
        public static List<ProvinceState> Claimable(GameState state, FactionState f)
        {
            var result = new List<ProvinceState>();
            if (f == null || !f.alive) return result;
            var owned = state.Provinces.Values.Where(p => p.controller == f.id).ToList();
            var seen = new HashSet<string>();
            foreach (var p in owned)
            {
                foreach (var nid in p.def.neighbors.Concat(SeaNeighbors(state, p.id)))
                {
                    if (!seen.Add(nid)) continue;
                    var n = state.Province(nid);
                    if (n == null || n.controller != null) continue;
                    result.Add(n);
                }
            }
            return result.OrderByDescending(p => p.def.level).ThenBy(p => p.def.name).ToList();
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

        /// <summary>Провинциальные улучшения, доступные дому.</summary>
        public static List<BuildingDef> Improvements(GameState state, FactionState f, ProvinceState province = null)
        {
            return state.Content.Buildings
                .Where(b => b.slot == ImprovementSlot)
                .Where(b => string.IsNullOrEmpty(b.uniqueToFaction) || b.uniqueToFaction == f?.house)
                .Where(b => string.IsNullOrEmpty(b.requiresTech) || (f != null && f.HasTech(b.requiresTech)))
                .Where(b => province == null || !province.buildings.Contains(b.id))
                .ToList();
        }

        public static int ImprovementLimit(GameState state, ProvinceState p) =>
            Math.Max(1, state.Settings.provinceImprovementLimit + (p.def.isCapital ? 1 : 0));

        public static bool CanClaim(GameState state, FactionState f, string provinceId, out string error)
        {
            error = null;
            if (f == null || !f.alive) { error = "Дом пал"; return false; }
            var p = state.Province(provinceId);
            if (p == null) { error = "Нет такой провинции"; return false; }
            if (p.controller != null) { error = "Провинция уже занята"; return false; }
            if (!f.growthReady) return false;
            if (!CanClaimAdjacency(state, f, provinceId)) { error = "Провинция не граничит с вашими землями"; return false; }
            var cost = ClaimCost(state);
            if (!EffectEngine.CanAfford(f, cost)) { error = "Недостаточно ресурсов: " + cost.ToRu(); return false; }
            return true;
        }

        public static ResourceCost ClaimCost(GameState state) =>
            new ResourceCost { gold = state.Settings.claimGold, materials = state.Settings.claimMaterials, influence = state.Settings.claimInfluence };

        private static bool CanClaimAdjacency(GameState state, FactionState f, string provinceId)
        {
            foreach (var p in state.Provinces.Values.Where(p => p.controller == f.id))
            {
                if (p.def.neighbors.Contains(provinceId)) return true;
                if (p.def.port)
                {
                    foreach (var route in state.Map.seaRoutes)
                    {
                        if (route.Count < 2) continue;
                        if ((route[0] == p.id && route[1] == provinceId) || (route[1] == p.id && route[0] == provinceId))
                            return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Присоединяет провинцию и ставит в неё выбранное улучшение.</summary>
        public static bool Claim(GameState state, FactionState f, string provinceId, string improvementId, out string error)
        {
            if (!CanClaim(state, f, provinceId, out error)) return false;
            var p = state.Province(provinceId);
            if (!string.IsNullOrEmpty(improvementId))
            {
                if (!state.Content.BuildingById.TryGetValue(improvementId, out var b) || b.slot != ImprovementSlot)
                { error = "Неизвестное улучшение"; return false; }
                if (b.unique && state.Provinces.Values.Any(x => x.buildings.Contains(improvementId)))
                { error = "Такое улучшение уже есть в державе"; return false; }
                if (!string.IsNullOrEmpty(b.uniqueToFaction) && b.uniqueToFaction != f.house)
                { error = "Улучшение недоступно вашему дому"; return false; }
            }

            var cost = ClaimCost(state);
            if (!EffectEngine.CanAfford(f, cost)) { error = "Недостаточно ресурсов: " + cost.ToRu(); return false; }
            EffectEngine.Pay(f, cost);
            f.growth = Math.Max(0f, f.growth - GrowthThreshold(state, f));

            p.controller = f.id;
            p.owner = f.id;
            p.unrest = Math.Max(10f, p.unrest + 12f);
            p.garrison = Math.Max(p.garrison, 12 + p.def.level * 6);
            f.claimedProvinces++;

            if (!string.IsNullOrEmpty(improvementId))
            {
                p.buildings.Add(improvementId);
                var bdef = state.Content.BuildingById[improvementId];
                EffectEngine.ApplyAll(bdef.effects, new EffectContext
                {
                    State = state, Source = f, TargetFaction = f, TargetProvince = p
                });
            }
            state.AddLog("territory", $"{f.def.name}: присоединена провинция {p.def.name}", f.id);
            if (f.isPlayer) state.Notify($"Освоена провинция {p.def.name}");
            return true;
        }

        /// <summary>Тратит накопленное освоение на одно улучшение земли.</summary>
        public static bool SpendGrowthPoint(GameState state, FactionState f)
        {
            if (f == null || !f.growthReady) return false;
            f.growth = Math.Max(0f, f.growth - GrowthThreshold(state, f));
            f.growthReady = f.growth >= GrowthThreshold(state, f);
            return true;
        }

        /// <summary>
        /// Улучшение в своей провинции: либо за освоение (free = true, им же растят земли),
        /// либо за золото и материалы, если освоение ещё не накопилось.
        /// </summary>
        public static bool AddImprovement(GameState state, FactionState f, ProvinceState p, string improvementId,
            bool free, out string error)
        {
            error = null;
            if (p == null || p.controller != f.id) { error = "Провинция не под вашим контролем"; return false; }
            if (!state.Content.BuildingById.TryGetValue(improvementId, out var b) || b.slot != ImprovementSlot)
            { error = "Неизвестное улучшение"; return false; }
            if (p.buildings.Contains(improvementId)) { error = "Уже построено"; return false; }
            if (b.unique && state.Provinces.Values.Any(x => x.buildings.Contains(improvementId)))
            { error = "Такое улучшение уже есть в державе"; return false; }
            if (b.maxPerEmpire < 99 && EconomySystem.CountBuilding(state, f, improvementId) >= b.maxPerEmpire)
            { error = $"Лимит дома: не больше {b.maxPerEmpire} таких улучшений"; return false; }
            if (p.buildings.Count(x => state.Content.BuildingById.TryGetValue(x, out var bd) && bd.slot == ImprovementSlot) >= ImprovementLimit(state, p))
            { error = $"Лимит улучшений: {ImprovementLimit(state, p)}"; return false; }
            if (!string.IsNullOrEmpty(b.uniqueToFaction) && b.uniqueToFaction != f.house) { error = "Недоступно вашему дому"; return false; }
            if (!string.IsNullOrEmpty(b.requiresTech) && !f.HasTech(b.requiresTech)) { error = "Требуется технология"; return false; }
            if (free)
            {
                if (!f.growthReady)
                { error = $"Нужно {GrowthThreshold(state, f)} освоения (сейчас {f.growth:0})"; return false; }
                SpendGrowthPoint(state, f);
            }
            else
            {
                var cost = b.cost.Scaled(f.ModPct(ModKeys.BuildCostPct));
                if (!EffectEngine.CanAfford(f, cost)) { error = "Недостаточно ресурсов: " + cost.ToRu(); return false; }
                EffectEngine.Pay(f, cost);
            }
            p.buildings.Add(improvementId);
            EffectEngine.ApplyAll(b.effects, new EffectContext
            {
                State = state, Source = f, TargetFaction = f, TargetProvince = p
            });
            state.AddLog("territory", $"{f.def.name}: в {p.def.name} улучшение «{b.name}»", f.id);
            return true;
        }

        /// <summary>Тик роста: накапливает освоение, обновляет флаг готовности.</summary>
        public static void ProcessGrowth(GameState state)
        {
            foreach (var f in state.Factions.Values.Where(x => x.alive))
            {
                f.growth = Math.Min(GrowthThreshold(state, f) * 2f, f.growth + GrowthPerTurn(state, f));
                f.growthReady = f.growth >= GrowthThreshold(state, f);
            }
        }
    }
}
