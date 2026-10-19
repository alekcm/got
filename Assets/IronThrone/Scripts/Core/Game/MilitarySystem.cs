using System;
using System.Collections.Generic;
using System.Linq;
using IronThrone.Core.Battle;
using IronThrone.Core.Content;

namespace IronThrone.Core.Game
{
    public delegate BattleResult BattleResolver(GameState state, ArmyState attacker, ArmyState defender,
        ProvinceState province, bool siege);

    public static class MilitarySystem
    {
        public static BattleResolver Resolver; // Unity подменяет на ручной бой

        // ------------------------------------------------------------------ армии
        /// <summary>Сколько отрядов помещается в одну армию: ополчение дома расширяет строй.</summary>
        public static int MaxUnitsInArmy(GameState state, FactionState f) =>
            Math.Max(1, state.Settings.armyMaxUnits + (int)f.Mod(ModKeys.LevySize));

        public static ArmyState CreateArmy(GameState state, FactionState faction, string province, string name)
        {
            if (faction.armies.Count >= Math.Max(1, state.Settings.maxArmies))
            {
                // лимит армий исчерпан: пополняем ту, что стоит здесь, иначе — любую существующую
                var existing = faction.armies.Select(id => state.Armies[id])
                    .FirstOrDefault(a => a.province == province && a.alive)
                    ?? faction.armies.Select(id => state.Armies[id]).FirstOrDefault(a => a.alive);
                if (existing != null) return existing;
            }
            var army = new ArmyState
            {
                id = "army_" + state.NextArmyId++,
                faction = faction.id,
                province = province,
                name = name,
                maxMovement = state.Settings.movementPerTurn + (int)faction.Mod(ModKeys.ArmyMove) + (int)faction.Mod(ModKeys.Movement),
                movementLeft = state.Settings.movementPerTurn + (int)faction.Mod(ModKeys.ArmyMove) + (int)faction.Mod(ModKeys.Movement),
                supply = 100,
            };
            state.Armies[army.id] = army;
            faction.armies.Add(army.id);
            return army;
        }

        public static void RecruitFree(GameState state, FactionState faction, UnitDef unit, int count, string provinceId)
        {
            var army = state.Armies.Values.FirstOrDefault(a => a.faction == faction.id && a.province == provinceId && a.alive)
                       ?? state.Armies.Values.FirstOrDefault(a => a.faction == faction.id && a.alive)
                       ?? CreateArmy(state, faction, provinceId, "Новый сбор");
            int room = Math.Max(0, MaxUnitsInArmy(state, faction) - army.units.Count);
            for (int i = 0; i < Math.Min(count, room); i++)
                army.units.Add(new UnitInstance(unit.id, unit.name) { hp = 100, morale = 100 });
        }

        /// <summary>Сколько живых отрядов в армии.</summary>
        public static int AliveUnits(ArmyState army) => army?.units.Count(u => u.alive) ?? 0;

        public static bool CanMerge(GameState state, FactionState f, ArmyState into, ArmyState from, out string error)
        {
            error = null;
            if (f == null || into == null || from == null || !into.alive || !from.alive) { error = "Армия недоступна"; return false; }
            if (into.id == from.id) { error = "Это одна и та же армия"; return false; }
            if (into.faction != f.id || from.faction != f.id) { error = "Это не ваши армии"; return false; }
            if (into.province != from.province) { error = "Армии должны стоять в одной провинции"; return false; }
            if (AliveUnits(from) == 0) { error = "В присоединяемой армии нет отрядов"; return false; }
            if (into.heroId != null && from.heroId != null) { error = "В обеих армиях есть герои: сначала отзовите одного"; return false; }
            if (MaxUnitsInArmy(state, f) - AliveUnits(into) <= 0) { error = "В армии нет места под новые отряды"; return false; }
            return true;
        }

        /// <summary>Вливает армию from в into. Перемещается не больше, чем позволяет лимит строя.</summary>
        public static bool MergeArmies(GameState state, FactionState f, string intoId, string fromId, out string error)
        {
            if (!state.Armies.TryGetValue(intoId, out var into) || !state.Armies.TryGetValue(fromId, out var from))
            {
                error = "Армия не найдена";
                return false;
            }
            if (!CanMerge(state, f, into, from, out error)) return false;
            int room = Math.Max(0, MaxUnitsInArmy(state, f) - AliveUnits(into));
            var moving = from.units.Where(u => u.alive).Take(room).ToList();
            foreach (var u in moving)
            {
                from.units.Remove(u);
                into.units.Add(u);
            }
            if (from.heroId != null && into.heroId == null)
            {
                into.heroId = from.heroId;
                var hero = Hero(state, from.heroId);
                if (hero != null) hero.armyId = into.id;
                from.heroId = null;
            }
            if (AliveUnits(from) == 0)
            {
                from.alive = false;
                f.armies.Remove(from.id);
                state.AddLog("army", $"Армия {from.name} влилась в {into.name}", f.id);
            }
            else
            {
                from.movementLeft = 0;
                state.AddLog("army", $"Из {from.name} переведены отряды в {into.name}", f.id);
            }
            into.movedThisTurn = true;
            state.Notify($"Отряды переведены в {into.name}");
            return true;
        }

        /// <summary>Отделяет count живых отрядов (начиная с самых потрёпанных) в новую армию.</summary>
        public static ArmyState SplitArmy(GameState state, FactionState f, ArmyState army, int count, out string error)
        {
            error = null;
            if (f == null || army == null || !army.alive) { error = "Армия недоступна"; return null; }
            int alive = AliveUnits(army);
            if (count < 1 || count >= alive) { error = "Нужно отделить хотя бы один отряд, оставив один в армии"; return null; }
            if (f.armies.Count >= Math.Max(1, state.Settings.maxArmies))
            {
                error = $"Лимит армий: {state.Settings.maxArmies}. Сначала слейте или расформируйте армию";
                return null;
            }
            var taken = army.units.Where(u => u.alive).OrderBy(u => u.hp).Take(count).ToList();
            var split = CreateArmy(state, f, army.province, army.name + " (отряд)");
            split.movementLeft = 0;
            split.maxMovement = army.maxMovement;
            split.supply = army.supply;
            split.stance = army.stance;
            foreach (var u in taken)
            {
                army.units.Remove(u);
                split.units.Add(u);
            }
            army.movedThisTurn = true;
            state.AddLog("army", $"От {army.name} отделена армия {split.name} ({count} отр.)", f.id);
            state.Notify($"Сформирована армия {split.name}");
            return split;
        }

        public static bool Recruit(GameState state, FactionState faction, string unitId, int count,
            string provinceId, out string error)
        {
            error = null;
            if (!state.Content.UnitById.TryGetValue(unitId, out var def)) { error = "Неизвестный отряд"; return false; }
            if (def.faction != "shared" && def.faction != faction.house) { error = "Этот отряд недоступен вашему дому"; return false; }
            if (!string.IsNullOrEmpty(def.requiresTech) && !faction.HasTech(def.requiresTech))
            { error = "Требуется технология"; return false; }
            if (faction.HasFlag("unlock:" + unitId)) { /* разблокирован фокусом */ }

            var prov = state.Province(provinceId);
            if (prov == null || prov.controller != faction.id) { error = "Набирать войска можно только на своей территории"; return false; }
            if (faction.armies.Count >= Math.Max(1, state.Settings.maxArmies) &&
                !state.Armies.Values.Any(a => a.alive && a.faction == faction.id && a.province == provinceId))
            { error = "Достигнут предел армий — наймите войска там, где уже стоит ваша армия"; return false; }

            int n = Math.Max(1, count);
            float costMod = faction.ModPct(ModKeys.RecruitCostPct);
            var cost = def.cost.Scaled(n * costMod);
            if (!EffectEngine.CanAfford(faction, cost)) { error = "Недостаточно ресурсов: " + cost.ToRu(); return false; }

            var army = state.Armies.Values.FirstOrDefault(a => a.faction == faction.id && a.province == provinceId && a.alive)
                       ?? CreateArmy(state, faction, provinceId, "Сбор в " + prov.def.name);
            int room = MaxUnitsInArmy(state, faction) - army.units.Count;
            if (room <= 0) { error = $"В армии уже {army.units.Count} отрядов — больше не поместится"; return false; }
            n = Math.Min(n, room);
            // пересчитываем цену за фактическое число отрядов
            cost = def.cost.Scaled(n * costMod);
            if (!EffectEngine.CanAfford(faction, cost)) { error = "Недостаточно ресурсов: " + cost.ToRu(); return false; }

            EffectEngine.Pay(faction, cost);
            for (int i = 0; i < n; i++)
                army.units.Add(new UnitInstance(unitId, def.name) { hp = 100, morale = 100 });
            state.AddLog("army", $"{faction.def.name} набирает {def.name} ×{n} в {prov.def.name}", faction.id);
            return true;
        }

        /// <summary>Специализация отряда: +2 к атаке и +1 к обороне за уровень (макс. 2).</summary>
        public static bool UpgradeUnit(GameState state, FactionState faction, ArmyState army, UnitInstance unit, out string error)
        {
            error = null;
            if (army == null || unit == null || !unit.alive) { error = "Отряд недоступен"; return false; }
            if (army.province == null || state.Province(army.province)?.controller != faction.id)
            { error = "Специализация возможна только на своей территории"; return false; }
            if (unit.upgradeLevel >= 2) { error = "Отряд уже полностью специализирован"; return false; }
            var cost = new ResourceCost { gold = 90 * (unit.upgradeLevel + 1), materials = 50 * (unit.upgradeLevel + 1) };
            if (!EffectEngine.CanAfford(faction, cost)) { error = "Недостаточно ресурсов: " + cost.ToRu(); return false; }
            EffectEngine.Pay(faction, cost);
            unit.upgradeLevel++;
            state.AddLog("army", $"{faction.def.name}: отряд «{unit.name}» специализирован до уровня {unit.upgradeLevel}", faction.id);
            return true;
        }

        /// <summary>Следующий отряд в цепочке прокачки, доступный дому (по технологии и фракции).</summary>
        public static UnitDef NextUpgrade(GameState state, FactionState faction, UnitInstance unit)
        {
            if (unit == null || faction == null) return null;
            if (!state.Content.UnitById.TryGetValue(unit.defId, out var def)) return null;
            foreach (var id in def.upgradesTo)
            {
                if (!state.Content.UnitById.TryGetValue(id, out var next)) continue;
                if (next.faction != "shared" && next.faction != faction.house) continue;
                if (!string.IsNullOrEmpty(next.requiresTech) && !faction.HasTech(next.requiresTech)) continue;
                return next;
            }
            return null;
        }

        /// <summary>Стоимость перевода отряда в следующий тир: 75% цены нового отряда.</summary>
        public static ResourceCost UpgradeCost(UnitInstance unit, UnitDef next) =>
            next.cost.Scaled(0.75f);

        public static bool UpgradeUnitType(GameState state, FactionState faction, ArmyState army, UnitInstance unit, out string error)
        {
            error = null;
            if (army == null || unit == null || !unit.alive) { error = "Отряд недоступен"; return false; }
            if (army.province == null || state.Province(army.province)?.controller != faction.id)
            { error = "Прокачка возможна только на своей территории"; return false; }
            var next = NextUpgrade(state, faction, unit);
            if (next == null) { error = "Для этого отряда нет доступного улучшения"; return false; }
            var cost = UpgradeCost(unit, next);
            if (!EffectEngine.CanAfford(faction, cost)) { error = "Недостаточно ресурсов: " + cost.ToRu(); return false; }
            EffectEngine.Pay(faction, cost);
            string oldName = unit.name;
            unit.defId = next.id;
            unit.name = next.name;
            unit.hp = Math.Min(100, Math.Max(unit.hp, 75));
            state.AddLog("army", $"{faction.def.name}: «{oldName}» переформирован в «{next.name}»", faction.id);
            return true;
        }

        public static int EstimateStrength(ArmyState army)
        {
            if (army == null) return 0;
            int power = 0;
            foreach (var u in army.units.Where(x => x.alive))
            {
                power += (int)(u.hp * 0.6f);
                power += 10 + u.level * 3 + u.upgradeLevel * 4;
            }
            return power;
        }

        public static int EstimateStrength(GameState state, FactionState faction)
        {
            return faction.armies.Select(id => state.Armies.TryGetValue(id, out var a) ? EstimateStrength(a) : 0).Sum();
        }

        // ------------------------------------------------------------------ движение
        public static bool AreAdjacent(GameState state, string a, string b)
        {
            var pa = state.Province(a); var pb = state.Province(b);
            if (pa == null || pb == null) return false;
            if (pa.def.neighbors.Contains(b)) return true;
            if (pa.def.port && pb.def.port)
            {
                foreach (var route in state.Map.seaRoutes)
                {
                    if (route.Count < 2) continue;
                    if ((route[0] == a && route[1] == b) || (route[0] == b && route[1] == a)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Зона контроля: провинция рядом с вражеской армией. Выход из неё стоит +1 движения,
        /// но атака самой вражеской армии — нет: врага можно атаковать, чтобы вырваться.
        /// </summary>
        public static bool InEnemyZoc(GameState state, ArmyState army, string provinceId, string exceptProvince = null)
        {
            var p = state.Province(provinceId);
            if (p == null || army == null) return false;
            // в своей земле маневр свободен; цепляет только чужбина
            if (p.controller == army.faction) return false;
            foreach (var nid in p.def.neighbors)
            {
                if (nid == exceptProvince) continue;
                foreach (var other in state.Armies.Values)
                {
                    if (!other.alive || other.province != nid || other.faction == army.faction) continue;
                    if (other.IsEmpty) continue;
                    if (AreAtWar(state, army.faction, other.faction)) return true;
                }
            }
            return false;
        }

        public static int MoveCost(GameState state, ArmyState army, string from, string to)
        {
            var pa = state.Province(from); var pb = state.Province(to);
            if (pa == null || pb == null) return 99;
            int cost = pa.def.neighbors.Contains(to) ? 1 : state.Settings.seaMovement;
            if (InEnemyZoc(state, army, from, to)) cost += 1;
            return cost;
        }

        /// <summary>Местность, где армия может укрыться: лес, холмы, горы, болота, снега.</summary>
        public static readonly string[] AmbushTerrains = { "forest", "hills", "mountains", "marsh", "snow" };

        public static bool AmbushTerrain(string terrain) =>
            !string.IsNullOrEmpty(terrain) && Array.IndexOf(AmbushTerrains, terrain) >= 0;

        /// <summary>Можно ли устроить засаду: своя провинция, укрывистая местность, остаток движения.</summary>
        public static bool CanAmbush(GameState state, FactionState f, ArmyState army, out string error)
        {
            error = null;
            if (army == null || !army.alive || f == null) { error = "Нет армии"; return false; }
            if (AliveUnits(army) == 0) { error = "В армии не осталось отрядов"; return false; }
            if (army.stance == "siege") { error = "Осадный лагерь не спрячешь"; return false; }
            var prov = state.Province(army.province);
            if (prov == null) { error = "Армия вне провинции"; return false; }
            if (prov.controller != f.id) { error = "Засаду устраивают только на своей земле"; return false; }
            if (!AmbushTerrain(prov.def.terrain))
            { error = "Для засады нужен лес, холмы, горы, болото или снега"; return false; }
            if (state.Armies.Values.Any(a => a.alive && a.id != army.id && a.province == prov.id && a.faction != f.id))
            { error = "В провинции уже стоит враг"; return false; }
            if (army.movementLeft <= 0) { error = "На засаду не осталось движения"; return false; }
            return true;
        }

        public static bool SetAmbush(GameState state, FactionState f, ArmyState army, out string error)
        {
            if (!CanAmbush(state, f, army, out error)) return false;
            army.stance = "ambush";
            army.movementLeft = 0;
            var prov = state.Province(army.province);
            state.AddLog("army", $"{f.def.name}: армия «{army.name}» устроила засаду в {prov?.def.name}", f.id);
            if (f.isPlayer) state.Notify($"Армия «{army.name}» в засаде. Враг не увидит её, пока не войдёт в провинцию.");
            return true;
        }

        /// <summary>Раскрытая засада становится обычной обороной.</summary>
        public static void LeaveAmbush(ArmyState army)
        {
            if (army != null && army.stance == "ambush") army.stance = "defend";
        }

        public static bool CanMove(GameState state, ArmyState army, string target, out string reason)
        {
            reason = null;
            if (army == null || !army.alive) { reason = "Армия уничтожена"; return false; }
            if (army.movementLeft <= 0) { reason = "Нет очков движения"; return false; }
            if (!AreAdjacent(state, army.province, target)) { reason = "Провинции не граничат"; return false; }
            int moveCost = MoveCost(state, army, army.province, target);
            if (army.movementLeft < moveCost)
            {
                reason = InEnemyZoc(state, army, army.province, target)
                    ? "Зона контроля: выход из-под удара стоит +1 движения"
                    : "Нет очков движения";
                return false;
            }
            var targetState = state.Province(target);
            if (targetState == null) { reason = "Нет такой провинции"; return false; }
            if (targetState.controller == army.faction) return true;
            if (targetState.controller != null && !AreAtWar(state, army.faction, targetState.controller))
            {
                reason = "Нужно объявить войну дому " + state.FactionName(targetState.controller);
                return false;
            }
            return true;
        }

        /// <summary>Выполняет движение, при необходимости — бой или осаду.</summary>
        public static bool MoveArmy(GameState state, ArmyState army, string target, out string message)
        {
            message = null;
            if (!CanMove(state, army, target, out message)) return false;
            var prov = state.Province(target);
            int cost = MoveCost(state, army, army.province, target);
            if (!state.Province(army.province).def.neighbors.Contains(target))
            {
                var fleetFaction = state.Faction(army.faction);
                if (fleetFaction != null)
                    cost = Math.Max(1, cost - (int)(fleetFaction.Mod(ModKeys.NavalPower) / 2f));
            }

            var enemyArmy = state.Armies.Values.FirstOrDefault(a =>
                a.alive && a.province == target && a.faction != army.faction && !a.IsEmpty &&
                AreAtWar(state, army.faction, a.faction));

            if (enemyArmy != null)
            {
                var result = Fight(state, army, enemyArmy, prov);
                message = result.Summary;
                if (!result.attackerWon)
                {
                    army.movementLeft = 0;
                    return false;
                }
            }

            army.movementLeft = Math.Max(0, army.movementLeft - cost);
            army.province = target;
            army.movedThisTurn = true;
            if (army.stance == "ambush") army.stance = "move"; // засада снимается маршем

            if (prov.controller == army.faction)
            {
                message ??= $"Армия вошла в {prov.def.name}";
                return true;
            }

            // гарнизон: небольшой сломлен сразу, крупный — осада
            if (prov.garrison > 0)
            {
                if (prov.garrison * 8 <= EstimateStrength(army))
                {
                    int losses = Math.Max(1, prov.garrison / 25);
                    for (int i = 0; i < losses; i++)
                    {
                        var u = army.units.Where(x => x.alive).OrderBy(x => x.hp).FirstOrDefault();
                        if (u != null) u.hp = Math.Max(1, u.hp - 18);
                    }
                    prov.garrison = 0;
                    CaptureProvince(state, prov, army.faction, army);
                    message ??= $"{prov.def.name} взята штурмом";
                    return true;
                }
            }
            if (prov.garrison > 0)
            {
                prov.siegeProgress = 0;
                prov.besieger = army.faction;
                message ??= $"Началась осада {prov.def.name}";
                state.AddLog("war", $"{state.FactionName(army.faction)} осаждает {prov.def.name}", army.faction);
                SiegeTick(state, prov, army);
                return true;
            }

            CaptureProvince(state, prov, army.faction, army);
            message ??= $"{prov.def.name} захвачена";
            return true;
        }

        public static bool AreAtWar(GameState state, string a, string b)
        {
            if (a == null || b == null || a == b) return false;
            var fa = state.Faction(a); var fb = state.Faction(b);
            if (fa == null || fb == null) return true; // вольные дома всегда можно атаковать
            return DiplomacySystem.WarBetween(state, a, b) != null;
        }

        public static BattleResult Fight(GameState state, ArmyState attacker, ArmyState defender, ProvinceState prov)
        {
            bool siege = prov.garrison > 0 && prov.controller == defender.faction;
            var field = BattleEngine.Create(state, attacker, defender, prov, siege);
            BattleResult result;
            if (Resolver != null && state.Factions.TryGetValue(attacker.faction, out var af) && af.isPlayer)
            {
                result = Resolver(state, attacker, defender, prov, siege);
            }
            else
            {
                BattleEngine.RunAuto(field);
                result = BattleEngine.ApplyResult(field, attacker, defender);
            }
            state.Battles.Add(result);
            LeaveAmbush(defender); // засада раскрывается, как только начался бой
            state.AddLog("battle", $"{state.FactionName(result.attacker)} против {state.FactionName(result.defender)}: {result.Summary}");
            var war = DiplomacySystem.WarBetween(state, attacker.faction, defender.faction);
            if (war != null)
            {
                bool attackerSide = war.attacker == attacker.faction;
                if (result.attackerWon)
                {
                    if (attackerSide) war.attackerBattles++; else war.defenderBattles++;
                }
                else
                {
                    if (attackerSide) war.defenderBattles++; else war.attackerBattles++;
                }
            }
            // отступление проигравшего
            var loser = result.attackerWon ? defender : attacker;
            if (loser.IsEmpty)
            {
                loser.alive = false;
                state.AddLog("battle", $"Армия {loser.name} уничтожена");
            }
            else if (prov.controller == loser.faction)
            {
                var retreat = prov.def.neighbors
                    .Select(id => state.Province(id))
                    .FirstOrDefault(p => p != null && p.controller == loser.faction && !state.Armies.Values.Any(a => a.alive && a.province == p.id && a.faction != loser.faction));
                if (retreat != null)
                {
                    loser.province = retreat.id;
                    loser.movementLeft = 0;
                }
            }
            if (result.attackerWon) CaptureOrSiegeAfterBattle(state, prov, attacker.faction);
            return result;
        }

        private static void CaptureOrSiegeAfterBattle(GameState state, ProvinceState prov, string winner)
        {
            if (prov.controller == winner) return;
            if (prov.garrison > 0 && prov.controller != winner)
            {
                prov.besieger = winner;
                prov.siegeProgress = 0;
            }
            else
            {
                var winnerArmy = state.Armies.Values.FirstOrDefault(a => a.alive && a.faction == winner && a.province == prov.id);
                CaptureProvince(state, prov, winner, winnerArmy);
            }
        }

        public static void SiegeTick(GameState state, ProvinceState prov, ArmyState besieger)
        {
            if (prov == null || besieger == null || !besieger.alive) return;
            if (prov.controller == besieger.faction || prov.garrison <= 0) return;
            int siegePower = 0;
            foreach (var u in besieger.units.Where(x => x.alive))
            {
                int basePower = 8 + u.level * 2;
                if (state.Content.UnitById.TryGetValue(u.defId, out var def) && (def.kind == "siege" || def.kind == "dragon")) basePower += 14;
                siegePower += basePower;
            }
            int defense = 10 + prov.def.level * 4 + (prov.def.isCapital ? 25 : 0);
            var defenderFaction = state.Faction(prov.controller);
            if (defenderFaction != null) defense += (int)defenderFaction.Mod(ModKeys.SiegeDefense) * 4;
            var attackerFaction = state.Faction(besieger.faction);
            float speed = 1f + (attackerFaction?.Mod(ModKeys.SiegeSpeed) ?? 0f);
            int dmg = Math.Max(3, (int)Math.Round((siegePower - defense) * Math.Max(0.5f, speed)));
            prov.siegeProgress += dmg;
            prov.besieger = besieger.faction;
            besieger.stance = "siege";
            prov.unrest = Math.Min(100, prov.unrest + 1);
            if (prov.siegeProgress >= prov.garrison)
            {
                CaptureProvince(state, prov, besieger.faction, besieger);
                state.Notify($"{prov.def.name} пала после осады!");
            }
        }

        public static void CaptureProvince(GameState state, ProvinceState prov, string factionId, ArmyState looter = null)
        {
            var f = state.Faction(factionId);
            string oldController = prov.controller;

            // военная добыча: чем богаче провинция, тем больше золота и материалов
            if (f != null && oldController != null && oldController != factionId)
            {
                int baseLoot = 15 + prov.def.level * 12 + (prov.def.isCapital ? 30 : 0);
                float lootMult = 1f + f.Mod(ModKeys.RaidLoot);
                int reavers = 0;
                if (looter != null)
                    foreach (var u in looter.units.Where(x => x.alive))
                        if (state.Content.UnitById.TryGetValue(u.defId, out var ud) && (ud.traits ?? new List<string>()).Contains("reave"))
                            reavers++;
                baseLoot += reavers * 10;
                int goldLoot = Math.Max(0, (int)Math.Round(baseLoot * lootMult));
                int matLoot = Math.Max(0, goldLoot / 2);
                f.gold += goldLoot;
                f.materials += matLoot;
                if (goldLoot > 0)
                    state.AddLog("war", $"{f.def.name}: добыча в {prov.def.name} — {goldLoot} золота, {matLoot} материалов", factionId);
                if (looter != null)
                    foreach (var u in looter.units.Where(x => x.alive))
                        u.morale = Math.Min(100, u.morale + 5);
            }
            prov.controller = factionId;
            if (f != null) prov.owner = factionId;
            prov.garrison = Math.Max(0, prov.garrison / 2);
            prov.unrest = Math.Min(100, prov.unrest + 18);
            prov.siegeProgress = 0;
            prov.besieger = null;
            prov.turnsOwned = 0;
            prov.flags.Add("captured");
            var attackerFaction = state.Faction(factionId);
            var victimFaction = state.Faction(oldController);
            if (attackerFaction != null && victimFaction != null && factionId != oldController)
            {
                if (attackerFaction.relations.ContainsKey(oldController))
                    attackerFaction.relations[oldController] = Math.Max(-100, attackerFaction.relations[oldController] - 30);
                if (victimFaction.relations.ContainsKey(factionId))
                    victimFaction.relations[factionId] = Math.Max(-100, victimFaction.relations[factionId] - 45);
                foreach (var other in state.Factions.Values.Where(x => x.id != factionId && x.id != oldController))
                    if (other.relations.ContainsKey(factionId))
                        other.relations[factionId] = Math.Max(-100, other.relations[factionId] - 4);
            }
            state.AddLog("war", $"{state.FactionName(factionId)} захватывает {prov.def.name}" +
                (oldController != null ? $" (было: {state.FactionName(oldController)})" : " (вольный дом)"), factionId);
            state.Notify($"{prov.def.name} {(oldController == null ? "присоединена" : "захвачена")}!");
            // малый дом становится враждебным
            if (prov.house != null && state.MinorHouses.TryGetValue(prov.house, out var mh))
            {
                mh.hostile = true;
                mh.relation = Math.Max(-100, mh.relation - 40);
            }
            if (f != null && prov.def.isCapital)
                state.Notify($"Столица региона {prov.def.name} под вашим контролем.");
        }

        public static void RetreatOrDisband(GameState state, ArmyState army)
        {
            if (!army.IsEmpty) return;
            army.alive = false;
            ReleaseHero(state, army);
            foreach (var f in state.Factions.Values) f.armies.Remove(army.id);
        }

        // ------------------------------------------------------------------ герои
        /// <summary>Сколько ходов герой отдыхает после перевода в другую армию.</summary>
        public const int HeroReassignCooldown = 3;

        /// <summary>Сигнатурная способность героя: то, что он даёт армии один раз за бой.</summary>
        public static readonly string[] HeroAbilities = { "dragon_breath", "heal", "volley", "charge", "shield_wall", "rally", "berserk", "flaming", "swift", "fortified" };

        public static HeroState Hero(GameState state, string heroId) =>
            !string.IsNullOrEmpty(heroId) && state.Heroes.TryGetValue(heroId, out var h) ? h : null;

        public static HeroDef HeroDef(GameState state, HeroState hero) =>
            hero != null && state.Content.HeroById.TryGetValue(hero.defId, out var d) ? d : null;

        /// <summary>Способность, которую герой приносит армии (первая из поддерживаемых).</summary>
        public static string HeroSignature(GameState state, HeroState hero)
        {
            var def = HeroDef(state, hero);
            if (def?.abilities != null)
                foreach (var ab in HeroAbilities)
                    if (def.abilities.Contains(ab)) return ab;
            // у героя без прописанных способностей всё равно есть вес в бою
            return "rally";
        }

        public static ArmyState HeroArmy(GameState state, HeroState hero) =>
            hero?.armyId != null && state.Armies.TryGetValue(hero.armyId, out var a) ? a : null;

        /// <summary>Можно ли отдать героя в эту армию. Ошибки сформулированы для игрока.</summary>
        public static bool CanAssignHero(GameState state, FactionState f, HeroState hero, ArmyState army, out string error)
        {
            error = null;
            if (hero == null || army == null) { error = "Нет героя или армии"; return false; }
            if (hero.faction != f.id) { error = "Это не ваш герой"; return false; }
            if (!hero.alive) { error = "Герой погиб"; return false; }
            if (hero.wounded) { error = $"Герой ранен — ещё {Math.Max(1, hero.woundedTurns)} ход(ов)"; return false; }
            if (army.faction != f.id || !army.alive) { error = "Это не ваша армия"; return false; }
            if (hero.armyId == army.id) { error = "Герой уже в этой армии"; return false; }
            if (hero.reassignCooldown > 0)
            { error = $"Герой не успел перестроиться — ещё {hero.reassignCooldown} ход(ов)"; return false; }
            return true;
        }

        /// <summary>Отдать героя армии. Один герой на армию; перевод из другой армии — с перезарядкой.</summary>
        public static bool AssignHero(GameState state, FactionState f, string heroId, string armyId, out string error)
        {
            error = null;
            var hero = Hero(state, heroId);
            if (!state.Armies.TryGetValue(armyId ?? "", out var army)) { error = "Нет такой армии"; return false; }
            if (!CanAssignHero(state, f, hero, army, out error)) return false;

            bool transferred = hero.armyId != null;
            ReleaseHero(state, hero, quiet: true);
            // прежний герой этой армии возвращается ко двору
            var previous = Hero(state, army.heroId);
            if (previous != null && previous != hero) ReleaseHero(state, previous, quiet: true);

            army.heroId = hero.defId;
            hero.armyId = army.id;
            if (transferred) hero.reassignCooldown = HeroReassignCooldown;

            state.AddLog("army", $"{hero.name} принимает командование: {army.name}", f.id);
            if (f.isPlayer) state.Notify($"{hero.name} {(transferred ? "переведён в" : "возглавил")} {army.name}");
            return true;
        }

        /// <summary>Вернуть героя ко двору.</summary>
        public static bool ReleaseHeroFromArmy(GameState state, FactionState f, string heroId, out string error)
        {
            error = null;
            var hero = Hero(state, heroId);
            if (hero == null || hero.faction != f.id) { error = "Это не ваш герой"; return false; }
            if (hero.armyId == null) { error = "Герой и так при дворе"; return false; }
            ReleaseHero(state, hero);
            state.AddLog("army", $"{hero.name} отозван ко двору", f.id);
            return true;
        }

        public static void ReleaseHero(GameState state, ArmyState army)
        {
            var hero = Hero(state, army?.heroId);
            if (hero != null) ReleaseHero(state, hero);
            if (army != null) army.heroId = null;
        }

        public static void ReleaseHero(GameState state, HeroState hero, bool quiet = false)
        {
            if (hero == null) return;
            if (hero.armyId != null && state.Armies.TryGetValue(hero.armyId, out var old))
            {
                if (old.heroId == hero.defId) old.heroId = null;
                if (!quiet) hero.reassignCooldown = Math.Max(hero.reassignCooldown, HeroReassignCooldown);
            }
            hero.armyId = null;
        }

        /// <summary>Герой ранен в бою: выбывает из армии на несколько ходов.</summary>
        public static void WoundHero(GameState state, HeroState hero, int turns)
        {
            if (hero == null || !hero.alive) return;
            hero.wounded = true;
            hero.woundedTurns = Math.Max(hero.woundedTurns, Math.Max(1, turns));
            ReleaseHero(state, hero, quiet: true);
            state.AddLog("army", $"Герой {hero.name} тяжело ранен и покидает армию", hero.faction);
        }

        /// <summary>Герой получает боевой опыт и, возможно, уровень.</summary>
        public static void AwardHeroExperience(GameState state, HeroState hero, int xp)
        {
            if (hero == null || !hero.alive) return;
            hero.xp += Math.Max(0, xp);
            int guard = 0;
            while (hero.level < 10 && hero.xp >= hero.level * 40 && guard++ < 20)
            {
                hero.xp -= hero.level * 40;
                hero.level++;
                state.AddLog("army", $"{hero.name} достигает {hero.level} уровня", hero.faction);
            }
        }

        // ------------------------------------------------------------------ ход: снабжение и восстановление
        public static void TurnStart(GameState state)
        {
            foreach (var f in state.Factions.Values.Where(x => x.alive))
            {
                foreach (var id in f.heroes.ToList())
                {
                    var h = Hero(state, id);
                    if (h == null) continue;
                    if (h.reassignCooldown > 0) h.reassignCooldown--;
                }
                foreach (var armyId in f.armies.ToList())
                {
                    if (!state.Armies.TryGetValue(armyId, out var army) || !army.alive) { f.armies.Remove(armyId); continue; }
                    army.movementLeft = army.maxMovement + (int)f.Mod(ModKeys.ArmyMove) + (int)f.Mod(ModKeys.Movement);
                    army.movedThisTurn = false;
                    army.maxMovement = state.Settings.movementPerTurn + (int)f.Mod(ModKeys.ArmyMove) + (int)f.Mod(ModKeys.Movement);
                    var prov = state.Province(army.province);
                    bool home = prov != null && prov.controller == f.id;
                    if (home) army.turnsInEnemyLand = 0; else army.turnsInEnemyLand++;

                    // снабжение
                    int need = army.units.Count(u => u.alive) * 1;
                    int supplyCost = Math.Max(0, need);
                    if (f.food >= supplyCost) { f.food -= supplyCost; army.supply = Math.Min(100, army.supply + 25); }
                    else { army.supply = Math.Max(0, army.supply - 35); }

                    if (army.supply <= 0)
                    {
                        foreach (var u in army.units.Where(x => x.alive)) { u.morale = Math.Max(0, u.morale - 12); u.hp = Math.Max(0, u.hp - 3); }
                        if (state.Rng.NextDouble() < 0.25 && army.units.Count > 1)
                        {
                            var deserter = army.units.Where(u => u.alive).OrderBy(_ => state.Rng.Next()).First();
                            deserter.alive = false;
                            state.AddLog("army", $"Из армии {army.name} дезертировал отряд {deserter.name}", f.id);
                        }
                    }

                    // восстановление на своей земле
                    if (home)
                    {
                        float heal = 26f * state.Settings.recoveryRate * 2f;
                        foreach (var u in army.units.Where(x => x.alive))
                            u.hp = Math.Min(100, u.hp + heal * 0.5f);
                        // восполнение потерь
                        if (army.destroyedUnits.Count > 0)
                        {
                            var lostId = army.destroyedUnits[0];
                            if (state.Content.UnitById.TryGetValue(lostId, out var lostDef))
                            {
                                var cost = lostDef.cost.Scaled(lostDef.recoveryCostFactor * f.ModPct(ModKeys.RecoveryCostPct));
                                if (f.food >= cost.food && f.gold >= cost.gold && f.materials >= cost.materials)
                                {
                                    EffectEngine.Pay(f, cost);
                                    army.destroyedUnits.RemoveAt(0);
                                    army.units.Add(new UnitInstance(lostId, lostDef.name) { hp = 55, morale = 60 });
                                    state.AddLog("army", $"В {army.name} восстановлен отряд {lostDef.name}", f.id);
                                }
                            }
                            else army.destroyedUnits.RemoveAt(0);
                        }
                        if (army.heroId != null && state.Heroes.TryGetValue(army.heroId, out var hero) && hero.wounded)
                        {
                            hero.woundedTurns--;
                            if (hero.woundedTurns <= 0) hero.wounded = false;
                        }
                    }
                }
            }
        }

        public static void TurnEnd(GameState state)
        {
            foreach (var f in state.Factions.Values.Where(x => x.alive))
            {
                // содержание армий
                int upkeep = 0;
                foreach (var id in f.armies)
                    if (state.Armies.TryGetValue(id, out var army))
                        foreach (var u in army.units.Where(x => x.alive))
                            if (state.Content.UnitById.TryGetValue(u.defId, out var def))
                                upkeep += def.upkeep.gold;
                upkeep = (int)(upkeep * f.ModPct(ModKeys.UpkeepPct));
                if (upkeep > 0)
                {
                    if (f.gold >= upkeep) f.gold -= upkeep;
                    else
                    {
                        f.gold = 0;
                        state.AddLog("economy", $"{f.def.name}: казна пуста, армия теряет порядок", f.id);
                        foreach (var id in f.armies)
                            if (state.Armies.TryGetValue(id, out var army))
                                foreach (var u in army.units.Where(x => x.alive))
                                    u.morale = Math.Max(0, u.morale - 8);
                    }
                }
                // осады со стороны этой фракции
                foreach (var p in state.Provinces.Values.Where(p => p.besieger == f.id && p.controller != f.id))
                {
                    var besiegingArmy = state.Armies.Values.FirstOrDefault(a => a.faction == f.id && a.province == p.id && a.alive);
                    if (besiegingArmy != null) SiegeTick(state, p, besiegingArmy);
                    else { p.besieger = null; p.siegeProgress = 0; }
                }
            }
            // чистка уничтоженных армий
            foreach (var army in state.Armies.Values.Where(a => a.IsEmpty))
            {
                army.alive = false;
                var owner = state.Faction(army.faction);
                owner?.armies.Remove(army.id);
            }
        }
    }
}
