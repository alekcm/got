using System;
using System.Collections.Generic;
using System.Linq;
using IronThrone.Core.Content;

namespace IronThrone.Core.Game
{
    /// <summary>Ключи модификаторов, которые читают системы.</summary>
    public static class ModKeys
    {
        public const string GoldIncome = "goldIncome";
        public const string MaterialsIncome = "materialsIncome";
        public const string FoodIncome = "foodIncome";
        public const string InfluenceIncome = "influenceIncome";
        public const string PietyIncome = "pietyIncome";
        public const string GoldFlat = "goldFlat";

        public const string BuildCostPct = "buildCost.pct";
        public const string RecruitCostPct = "recruitCost.pct";
        public const string UpkeepPct = "upkeep.pct";
        public const string RecoveryCostPct = "recoveryCost.pct";

        public const string ArmyAttack = "armyAttack";
        public const string ArmyDefense = "armyDefense";
        public const string ArmyMorale = "armyMorale";
        public const string ArmyMove = "armyMove";
        public const string ArmyHp = "armyHp";
        public const string DragonPower = "dragonPower";
        public const string SiegeSpeed = "siegeSpeed";
        public const string NavalPower = "navalPower";

        public const string VotePower = "votePower";
        public const string RecognitionGain = "recognitionGain";
        public const string LobbyCostPct = "lobbyCost.pct";
        public const string SpyOffense = "spyOffense";
        public const string SpyDefense = "spyDefense";
        public const string SpyNetwork = "spyNetwork";

        public const string FaithSpread = "faithSpread";
        public const string Unrest = "unrest";
        public const string UnrestGlobalPct = "unrestGlobal.pct";
        public const string TradeDominance = "tradeDominance";
        public const string Movement = "movement";
        public const string LevySize = "levySize";
        public const string RaidLoot = "raidLoot";
        public const string HeroPower = "heroPower";
        public const string GrowthBonus = "growthBonus";
    }

    public class EffectContext
    {
        public GameState State;
        public FactionState Source;
        public FactionState TargetFaction;
        public ProvinceState TargetProvince;
        public ArmyState TargetArmy;
        public string SourceText;

        public FactionState ResolveFaction(EffectDef e)
        {
            switch ((e.target ?? "").ToLowerInvariant())
            {
                case "self":
                case "":
                    return Source;
                case "player":
                    return State.Player ?? Source;
                case "other":
                case "target":
                    return TargetFaction ?? Source;
                case "ruler":
                case "throne":
                case "throneholder":
                    return State.Faction(State.Council.throneHolder);
                case "all":
                    return Source;
                default:
                    return State.Faction(e.target) ?? Source;
            }
        }

        public ProvinceState ResolveProvince(EffectDef e)
        {
            if (!string.IsNullOrEmpty(e.province))
            {
                if (e.province == "target") return TargetProvince;
                if (e.province == "capital") return State.Province(ResolveFaction(e)?.capital);
                return State.Province(e.province);
            }
            return TargetProvince;
        }
    }

    /// <summary>Движок эффектов: один формат для технологий, законов, фокусов, событий и построек.</summary>
    public static class EffectEngine
    {
        public static void ApplyAll(IEnumerable<EffectDef> effects, EffectContext ctx, List<string> descriptions = null)
        {
            if (effects == null) return;
            foreach (var e in effects)
            {
                var d = Apply(e, ctx);
                if (!string.IsNullOrEmpty(d)) descriptions?.Add(d);
            }
        }

        public static string Apply(EffectDef e, EffectContext ctx)
        {
            if (e == null) return null;
            var state = ctx.State;
            var faction = ctx.ResolveFaction(e);
            var prov = ctx.ResolveProvince(e);

            switch ((e.op ?? "").ToLowerInvariant())
            {
                case "resource":
                {
                    if (faction == null) return null;
                    int amount = Mathf(e.amount != 0 ? e.amount : e.value);
                    var resName = e.res ?? e.key ?? "gold";
                    var ff = ctx.ResolveFaction(e) ?? ctx.Source;
                    AddResource(ff, resName, amount);
                    return $"{Sign(amount)} {ResourceName(resName)}";
                }
                case "resources":
                {
                    if (faction == null) return null;
                    AddResource(faction, "gold", e.count);
                    AddResource(faction, "materials", (int)e.amount);
                    AddResource(faction, "food", (int)e.value);
                    return null;
                }
                case "modifier":
                {
                    if (faction == null) return null;
                    faction.permanentMods.TryGetValue(e.key, out var cur);
                    faction.permanentMods[e.key] = cur + (e.value != 0 ? e.value : e.amount);
                    return e.text ?? $"{e.key} {(e.value != 0 ? e.value : e.amount):+0.##;-0.##}";
                }
                case "timedmodifier":
                case "timed_modifier":
                {
                    if (faction == null) return null;
                    faction.timedMods.Add(new TimedModifier
                    {
                        key = e.key, value = e.value != 0 ? e.value : e.amount,
                        turnsLeft = Math.Max(1, e.turns), source = e.text ?? "эффект"
                    });
                    return e.text ?? $"{e.key} на {e.turns} х.";
                }
                case "opinion":
                case "relation":
                {
                    // отношение к другой фракции
                    var other = state.Faction(e.id ?? e.target);
                    if (faction == null || other == null) return null;
                    if (!faction.relations.ContainsKey(other.id)) faction.relations[other.id] = 0;
                    faction.relations[other.id] = Clamp(faction.relations[other.id] + (e.amount != 0 ? e.amount : e.value), -100, 100);
                    return $"отношения с {other.def.name} {(e.amount >= 0 ? "+" : "")}{e.amount:0}";
                }
                case "unrest":
                {
                    if (prov != null) { prov.unrest = Clamp(prov.unrest + (e.amount != 0 ? e.amount : e.value), 0, 100); return $"беспорядки в {prov.def.name} {(e.amount >= 0 ? "+" : "")}{e.amount:0}"; }
                    if (faction != null)
                    {
                        foreach (var p in state.ProvincesOf(faction.id))
                            p.unrest = Clamp(p.unrest + (e.amount != 0 ? e.amount : e.value), 0, 100);
                        return $"беспорядки по всей державе {(e.amount >= 0 ? "+" : "")}{e.amount:0}";
                    }
                    return null;
                }
                case "faith":
                {
                    if (prov == null) return null;
                    string faith = e.id ?? faction?.faith ?? "seven";
                    float delta = (e.amount != 0 ? e.amount : e.value) / 100f;
                    ShiftFaith(prov, faith, delta);
                    return $"вера {state.FaithName(faith)} в {prov.def.name} {(delta >= 0 ? "+" : "")}{delta * 100:0}%";
                }
                case "unlocktech":
                {
                    if (faction == null) return null;
                    var ids = e.ids.Count > 0 ? e.ids : new List<string> { e.id };
                    foreach (var id in ids.Where(x => !string.IsNullOrEmpty(x)))
                    {
                        faction.techs.Add(id);
                        if (state.Content.TechById.TryGetValue(id, out var tech))
                            ApplyAll(tech.effects, new EffectContext { State = state, Source = faction }, null);
                    }
                    return null;
                }
                case "unlockunit":
                case "unlockbuilding":
                {
                    if (faction == null) return null;
                    var key = e.flag ?? "unlock:" + (e.id ?? e.key);
                    faction.SetFlag(key);
                    return null;
                }
                case "recruit":
                {
                    if (faction == null) return null;
                    string unitId = e.id ?? e.key;
                    int n = Math.Max(1, e.count);
                    if (state.Content.UnitById.TryGetValue(unitId, out var unit))
                        MilitarySystem.RecruitFree(state, faction, unit, n, prov?.id ?? faction.capital);
                    return null;
                }
                case "spawnarmy":
                {
                    if (faction == null) return null;
                    string pid = prov?.id ?? faction.capital;
                    var army = MilitarySystem.CreateArmy(state, faction, pid, e.text ?? "Армия");
                    if (e.id != null && state.Content.UnitById.ContainsKey(e.id))
                        army.units.Add(new UnitInstance(e.id, state.Content.UnitById[e.id].name) { hp = 100, morale = 100 });
                    if (e.ids.Count > 0)
                        foreach (var uid in e.ids)
                            if (state.Content.UnitById.TryGetValue(uid, out var ud))
                                army.units.Add(new UnitInstance(uid, ud.name) { hp = 100, morale = 100 });
                    return null;
                }
                case "recognition":
                {
                    if (faction == null) return null;
                    faction.recognition = Clamp(faction.recognition + (e.amount != 0 ? e.amount : e.value), 0, 100);
                    return $"признание {(e.amount >= 0 ? "+" : "")}{e.amount:0}";
                }
                case "tradedominance":
                {
                    if (faction == null) return null;
                    faction.tradeDominance = Clamp(faction.tradeDominance + (e.amount != 0 ? e.amount : e.value), 0, 100);
                    return null;
                }
                case "title":
                {
                    if (faction == null) return null;
                    string title = e.id ?? e.key;
                    if (title == "hand") state.Council.hand = faction.id;
                    if (state.Content.TitleById.ContainsKey(title))
                    {
                        state.Council.seats[title] = faction.id;
                        return $"титул «{state.Content.TitleById[title].name}»";
                    }
                    return null;
                }
                case "loyalty":
                {
                    // отношения с малым домом
                    var house = e.id ?? e.target;
                    if (faction == null || string.IsNullOrEmpty(house)) return null;
                    if (!faction.minorRelations.ContainsKey(house)) faction.minorRelations[house] = 0;
                    faction.minorRelations[house] = (int)Clamp(faction.minorRelations[house] + (e.amount != 0 ? e.amount : e.value), -100, 100);
                    return null;
                }
                case "flag":
                {
                    if (faction == null) return null;
                    faction.SetFlag(e.flag ?? e.id ?? e.key);
                    return null;
                }
                case "clearflag":
                {
                    if (faction == null) return null;
                    faction.flags.Remove(e.flag ?? e.id ?? e.key);
                    return null;
                }
                case "counter":
                {
                    if (faction == null) return null;
                    var key = e.key ?? e.id ?? "counter";
                    faction.counters.TryGetValue(key, out var cur);
                    faction.counters[key] = cur + (e.value != 0 ? e.value : e.amount);
                    return null;
                }
                case "heal":
                {
                    if (faction == null) return null;
                    foreach (var armyId in faction.armies)
                        if (state.Armies.TryGetValue(armyId, out var army))
                            foreach (var u in army.units)
                            {
                                if (!u.alive) continue;
                                u.hp = Math.Min(100, u.hp + (e.amount != 0 ? e.amount : e.value));
                                u.morale = Math.Min(100, u.morale + 15);
                            }
                    return "армии восстановлены";
                }
                case "damagearmy":
                {
                    if (faction == null) return null;
                    foreach (var armyId in faction.armies)
                        if (state.Armies.TryGetValue(armyId, out var army))
                            foreach (var u in army.units.Where(x => x.alive))
                            {
                                u.hp -= (e.amount != 0 ? e.amount : e.value);
                                u.morale = Math.Max(0, u.morale - 10);
                                if (u.hp <= 0) { u.hp = 0; u.alive = false; }
                            }
                    return "армии понесли потери";
                }
                case "granthero":
                {
                    if (faction == null) return null;
                    string heroId = e.id ?? e.key;
                    if (state.Content.HeroById.TryGetValue(heroId, out var hdef))
                    {
                        var hs = new HeroState { defId = heroId, name = hdef.name, faction = faction.id };
                        state.Heroes[heroId] = hs;
                        faction.heroes.Add(heroId);
                        return $"герой {hdef.name}";
                    }
                    return null;
                }
                case "capitalbuilding":
                {
                    if (faction == null) return null;
                    var cap = state.Province(faction.capital);
                    if (cap != null && !cap.buildings.Contains(e.id ?? e.key ?? ""))
                        cap.buildings.Add(e.id ?? e.key ?? "");
                    return null;
                }
                case "movement":
                {
                    if (faction == null) return null;
                    foreach (var armyId in faction.armies)
                        if (state.Armies.TryGetValue(armyId, out var army))
                            army.movementLeft += (int)e.amount;
                    return null;
                }
                case "vassal":
                {
                    if (faction == null) return null;
                    string house = e.id ?? e.target;
                    if (string.IsNullOrEmpty(house) || !state.MinorHouses.ContainsKey(house)) return null;
                    MinorHouseSystem.MakeVassal(state, faction, house, forced: true);
                    return $"дом {state.HouseName(house)} присягнул";
                }
                case "reveal":
                {
                    if (faction == null) return null;
                    foreach (var p in state.Provinces.Values)
                        p.flags.Add("revealed:" + faction.id);
                    return null;
                }
                case "log":
                {
                    state.AddLog("event", e.text ?? "", faction?.id);
                    return null;
                }
                default:
                    return null;
            }
        }

        private static int Mathf(float v) => (int)Math.Round(v);

        public static void AddResource(FactionState f, string res, int amount)
        {
            if (f == null || amount == 0) return;
            switch ((res ?? "gold").ToLowerInvariant())
            {
                case "gold": f.gold = Math.Max(0, f.gold + amount); break;
                case "materials": case "material": f.materials = Math.Max(0, f.materials + amount); break;
                case "food": f.food = Math.Max(0, f.food + amount); break;
                case "influence": f.influence = Math.Max(0, f.influence + amount); break;
                case "piety": case "faith": f.piety = Math.Max(0, f.piety + amount); break;
            }
        }

        public static bool CanAfford(FactionState f, ResourceCost cost)
        {
            if (f == null || cost == null) return true;
            return f.gold >= cost.gold && f.materials >= cost.materials && f.food >= cost.food &&
                   f.influence >= cost.influence && f.piety >= cost.piety;
        }

        public static void Pay(FactionState f, ResourceCost cost)
        {
            if (f == null || cost == null) return;
            f.gold -= cost.gold; f.materials -= cost.materials; f.food -= cost.food;
            f.influence -= cost.influence; f.piety -= cost.piety;
        }

        public static void ShiftFaith(ProvinceState p, string faithId, float delta)
        {
            if (p == null || string.IsNullOrEmpty(faithId)) return;
            if (!p.faith.ContainsKey(faithId)) p.faith[faithId] = 0;
            float old = p.faith[faithId];
            float newVal = Clamp(old + delta * 100f, 0, 100);
            float diff = newVal - old;
            p.faith[faithId] = newVal;
            // остальные веры сжимаются пропорционально
            float others = p.faith.Where(kv => kv.Key != faithId).Sum(kv => kv.Value);
            float targetOthers = Math.Max(0, 100 - newVal);
            if (others > 0.001f)
            {
                float k = targetOthers / others;
                foreach (var key in p.faith.Keys.Where(k2 => k2 != faithId).ToList())
                    p.faith[key] *= k;
            }
            else if (targetOthers > 0)
            {
                // растворено в "народных верованиях"
                p.faith["folk"] = (p.faith.TryGetValue("folk", out var folk) ? folk : 0) + targetOthers;
            }
            RecomputeDominantFaith(p);
        }

        public static void RecomputeDominantFaith(ProvinceState p)
        {
            string best = null; float bv = -1;
            foreach (var kv in p.faith)
                if (kv.Value > bv) { bv = kv.Value; best = kv.Key; }
            p.dominantFaith = best;
        }

        public static string ResourceName(string res) => (res ?? "").ToLowerInvariant() switch
        {
            "gold" => "золото",
            "materials" or "material" => "стройматериалы",
            "food" => "продовольствие",
            "influence" => "влияние",
            "piety" or "faith" => "вера",
            _ => res
        };

        private static string Sign(int v) => v >= 0 ? "+" + v : v.ToString();
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
    }
}
