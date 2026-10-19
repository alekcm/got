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

        public static int MoveCost(GameState state, string from, string to)
        {
            var pa = state.Province(from); var pb = state.Province(to);
            if (pa == null || pb == null) return 99;
            if (pa.def.neighbors.Contains(to)) return 1;
            return state.Settings.seaMovement;
        }

        public static bool CanMove(GameState state, ArmyState army, string target, out string reason)
        {
            reason = null;
            if (army == null || !army.alive) { reason = "Армия уничтожена"; return false; }
            if (army.movementLeft <= 0) { reason = "Нет очков движения"; return false; }
            if (!AreAdjacent(state, army.province, target)) { reason = "Провинции не граничат"; return false; }
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
            int cost = MoveCost(state, army.province, target);
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
            foreach (var f in state.Factions.Values) f.armies.Remove(army.id);
        }

        // ------------------------------------------------------------------ ход: снабжение и восстановление
        public static void TurnStart(GameState state)
        {
            foreach (var f in state.Factions.Values.Where(x => x.alive))
            {
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
