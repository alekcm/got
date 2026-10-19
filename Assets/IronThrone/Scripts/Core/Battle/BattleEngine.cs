using System;
using System.Collections.Generic;
using System.Linq;
using IronThrone.Core.Content;
using IronThrone.Core.Game;

namespace IronThrone.Core.Battle
{
    public enum BattleTerrain { Plains, Forest, Hills, Mountains, River, Desert, Marsh, Snow, Walls, Water }

    public class BattleHex
    {
        public int q;
        public int r;
        public BattleTerrain terrain;
        public bool passable = true;
        public int MoveCost => terrain == BattleTerrain.Forest || terrain == BattleTerrain.Marsh ||
                               terrain == BattleTerrain.Desert || terrain == BattleTerrain.Snow ? 2 :
                               terrain == BattleTerrain.Mountains ? 3 : 1;
    }

    public class BattleUnit
    {
        public string id;
        public string name;
        public int side; // 0 attacker, 1 defender
        public string defId;
        public string kind = "infantry";
        public StatBlock stats = new StatBlock();
        public int q;
        public int r;
        public float hp = 100;
        public float maxHp = 100;
        public float morale = 100;
        public int movesLeft;
        public bool acted;
        public bool alive = true;
        public bool routed;
        public string heroName;
        public int level = 1;
        public int upgradeLevel;
        public List<string> abilities = new List<string>();
        public HashSet<string> usedAbilities = new HashSet<string>();
        public string sourceUnitId; // UnitInstance связь
        public bool defending;
        public int attacksThisRound;

        public bool Has(string ability) => abilities.Contains(ability);
    }

    public class Battlefield
    {
        public int width = 9;
        public int height = 7;
        public Dictionary<(int q, int r), BattleHex> hexes = new Dictionary<(int, int), BattleHex>();
        public List<BattleUnit> units = new List<BattleUnit>();
        public int round = 1;
        public int maxRounds = 14;
        public bool finished;
        public bool attackerWon;
        public string provinceId;
        public string attackerFaction;
        public string defenderFaction;
        public bool siege;
        public string terrain;
        public bool coastal;
        public List<string> log = new List<string>();
        public int activeIndex = -1;
        public List<BattleUnit> order = new List<BattleUnit>();
        public GameState State;
        public string attackerHero;
        public string defenderHero;

        public IEnumerable<BattleUnit> Side(int side) => units.Where(u => u.alive && !u.routed && u.side == side);
        public bool SideAlive(int side) => Side(side).Any();

        public BattleHex Hex(int q, int r) => hexes.TryGetValue((q, r), out var h) ? h : null;

        public BattleUnit UnitAt(int q, int r) => units.FirstOrDefault(u => u.alive && !u.routed && u.q == q && u.r == r);

        public void AddLog(string s)
        {
            log.Add($"[{round}] {s}");
        }
    }

    /// <summary>
    /// Пошаговый гексагональный тактический бой.
    /// Движок не зависит от Unity: его используют и авто-разрешение, и ручной бой в UI.
    /// </summary>
    public static class BattleEngine
    {
        public static readonly (int dq, int dr)[] Dirs = { (1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1) };

        public static Battlefield Create(GameState state, ArmyState attacker, ArmyState defender,
            ProvinceState province, bool siege = false)
        {
            var f = new Battlefield
            {
                State = state,
                provinceId = province?.id,
                attackerFaction = attacker?.faction,
                defenderFaction = defender?.faction,
                siege = siege,
                terrain = province?.def.terrain,
                coastal = province != null && (province.def.port || province.def.region == "islands"),
                width = 9,
                height = 7,
            };
            BuildTerrain(f, province, siege);
            PlaceArmy(f, attacker, 0, state);
            PlaceArmy(f, defender, 1, state);
            f.attackerHero = attacker?.heroId != null && state.Heroes.TryGetValue(attacker.heroId, out var ah) ? ah.name : null;
            f.defenderHero = defender?.heroId != null && state.Heroes.TryGetValue(defender.heroId, out var dh) ? dh.name : null;
            if (f.defenderHero != null)
            {
                // герой-защитник даёт ауру
                foreach (var u in f.Side(1)) { u.stats.defense += 1; u.morale = Math.Min(120, u.morale + 8); }
            }
            if (f.attackerHero != null)
                foreach (var u in f.Side(0)) u.morale = Math.Min(120, u.morale + 5);
            f.AddLog($"Битва за {province?.def.name ?? "поле"}");
            StartRound(f);
            return f;
        }

        private static void BuildTerrain(Battlefield f, ProvinceState province, bool siege)
        {
            var rng = new Random((province?.id ?? "x").GetHashCode() ^ 0x5f3a);
            string terrain = province?.def?.terrain ?? "plains";
            for (int r = 0; r < f.height; r++)
            {
                for (int q = 0; q < f.width; q++)
                {
                    var t = BattleTerrain.Plains;
                    double n = rng.NextDouble();
                    switch (terrain)
                    {
                        case "forest": t = n < 0.42 ? BattleTerrain.Forest : BattleTerrain.Plains; break;
                        case "hills": t = n < 0.4 ? BattleTerrain.Hills : BattleTerrain.Plains; break;
                        case "mountains": t = n < 0.3 ? BattleTerrain.Mountains : n < 0.55 ? BattleTerrain.Hills : BattleTerrain.Plains; break;
                        case "desert": t = n < 0.5 ? BattleTerrain.Desert : BattleTerrain.Plains; break;
                        case "marsh": t = n < 0.45 ? BattleTerrain.Marsh : BattleTerrain.Plains; break;
                        case "snow": t = n < 0.4 ? BattleTerrain.Snow : BattleTerrain.Plains; break;
                    }
                    // река по центру
                    if (province != null && province.def.river && q == 4 && n < 0.75) t = BattleTerrain.River;
                    // стены при осаде
                    if (siege && q == 6) t = BattleTerrain.Walls;
                    f.hexes[(q, r)] = new BattleHex { q = q, r = r, terrain = t, passable = true };
                }
            }
            if (siege)
            {
                for (int r = 0; r < f.height; r++) f.hexes[(6, r)].passable = false;
                // ворота
                f.hexes[(6, 3)].passable = true;
                f.hexes[(6, 3)].terrain = BattleTerrain.Walls;
            }
        }

        private static void PlaceArmy(Battlefield f, ArmyState army, int side, GameState state)
        {
            if (army == null) return;
            var alive = army.units.Where(u => u.alive).ToList();
            int n = Math.Min(alive.Count, f.height);
            for (int i = 0; i < n; i++)
            {
                var inst = alive[i];
                if (!state.Content.UnitById.TryGetValue(inst.defId, out var def)) continue;
                float hpMul = 1f + Math.Max(-0.75f, state.Faction(army.faction)?.Mod(ModKeys.ArmyHp) ?? 0f);
                int q = side == 0 ? 1 : f.width - 2;
                int r = n == 1 ? 3 : (int)Math.Round(1 + i * (f.height - 2.0) / Math.Max(1, n - 1));
                var bu = new BattleUnit
                {
                    id = army.id + "_" + i,
                    name = def.name,
                    side = side,
                    defId = def.id,
                    kind = def.kind,
                    stats = def.stats.Clone(),
                    q = q,
                    r = r,
                    hp = Math.Min(100f, inst.hp) * hpMul,
                    maxHp = 100f * hpMul,
                    morale = Math.Max(20, inst.morale),
                    level = inst.level,
                    upgradeLevel = inst.upgradeLevel,
                    sourceUnitId = inst.defId,
                    heroName = side == 0 ? f.attackerHero : f.defenderHero,
                    abilities = new List<string>(def.abilities ?? new List<string>()),
                };
                if (side == 1 && f.siege) { bu.stats.defense += 4; bu.stats.range += 0; }
                // стартовый бонус за специализацию
                bu.stats.attack += bu.upgradeLevel * 2;
                bu.stats.defense += bu.upgradeLevel;

                // бонусы дома и героя
                var factionState = state.Faction(army.faction);
                float heroAtk = 0, heroDef = 0;
                if (!string.IsNullOrEmpty(army.heroId) && state.Heroes.TryGetValue(army.heroId, out var heroState) && heroState.alive)
                {
                    if (state.Content.HeroById.TryGetValue(heroState.defId, out var heroDefn))
                    {
                        heroAtk = heroDefn.stats.attack * 0.12f;
                        heroDef = heroDefn.stats.defense * 0.12f;
                    }
                    heroAtk += heroState.level * 0.5f;
                }
                if (factionState != null)
                {
                    float heroMult = 1f + Math.Max(-0.5f, factionState.Mod(ModKeys.HeroPower));
                    bu.stats.attack += (int)Math.Round(factionState.Mod(ModKeys.ArmyAttack) + heroAtk * heroMult);
                    bu.stats.defense += (int)Math.Round(factionState.Mod(ModKeys.ArmyDefense) + heroDef * heroMult);
                    bu.morale = Math.Min(120, Math.Max(20, bu.morale + factionState.Mod(ModKeys.ArmyMorale)));

                    // флот поддерживает бой на побережье и островах
                    if (f.coastal)
                    {
                        float naval = factionState.Mod(ModKeys.NavalPower);
                        bu.stats.attack += (int)Math.Round(naval * 0.6f);
                        bu.stats.defense += (int)Math.Round(naval * 0.3f);
                    }

                    // особые свойства отряда
                    foreach (var tr in def.traits ?? new List<string>())
                    {
                        switch (tr)
                        {
                            case "reave": bu.stats.attack += 1; bu.morale = Math.Min(120, bu.morale + 5); break;
                            case "amphibious": if (f.coastal) { bu.stats.attack += 2; bu.stats.defense += 1; } break;
                            case "disciplined": bu.morale = Math.Min(120, bu.morale + 8); bu.stats.defense += 1; break;
                            case "fanatic": if (bu.morale >= 70) bu.stats.attack += 2; break;
                        }
                    }

                    // родная местность: снег у северян, пески у дорнийцев, горы у долинцев
                    if (!string.IsNullOrEmpty(def.terrainBonus) && !string.IsNullOrEmpty(f.terrain))
                    {
                        var homes = def.terrainBonus.Split(new[] { ',', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries);
                        if (homes.Contains(f.terrain))
                        {
                            bu.stats.attack += 3;
                            bu.stats.defense += 2;
                        }
                    }

                    if (def.kind == "dragon")
                    {
                        float dp = 1f + factionState.Mod(ModKeys.DragonPower);
                        bu.stats.attack = (int)Math.Round(bu.stats.attack * dp);
                        bu.stats.defense = (int)Math.Round(bu.stats.defense * (1f + (dp - 1f) * 0.5f));
                        bu.stats.hp = 100;
                    }
                }
                f.units.Add(bu);
            }
        }

        public static void StartRound(Battlefield f)
        {
            if (f.finished) return;
            if (f.round > f.maxRounds) { Finish(f, DecideByStrength(f)); return; }
            foreach (var u in f.Side(0).Concat(f.Side(1)))
            {
                ResetRound(u);
                u.acted = false;
                u.attacksThisRound = 0;
            }
            f.order = f.Side(0).Concat(f.Side(1))
                .OrderByDescending(u => u.stats.initiative + (u.Has("swift") ? 4 : 0) + f.State.Rng.Next(0, 4))
                .ToList();
            f.activeIndex = -1;
            f.AddLog($"--- Раунд {f.round} ---");
            NextUnit(f);
        }

        public static void ResetRound(BattleUnit u)
        {
            u.movesLeft = u.stats.move + (u.Has("swift") ? 1 : 0);
            u.defending = false;
        }

        public static BattleUnit Active(Battlefield f) =>
            f.activeIndex >= 0 && f.activeIndex < f.order.Count ? f.order[f.activeIndex] : null;

        public static void NextUnit(Battlefield f)
        {
            if (f.finished) return;
            f.activeIndex++;
            while (f.activeIndex < f.order.Count)
            {
                var u = f.order[f.activeIndex];
                if (u.alive && !u.routed) return;
                f.activeIndex++;
            }
            f.round++;
            if (!f.SideAlive(0) || !f.SideAlive(1))
            {
                Finish(f, !f.SideAlive(1) && f.SideAlive(0));
                return;
            }
            StartRound(f);
        }

        public static void EndUnitTurn(Battlefield f)
        {
            var u = Active(f);
            if (u != null) u.acted = true;
            NextUnit(f);
        }

        public static IEnumerable<BattleHex> LegalMoves(Battlefield f, BattleUnit unit)
        {
            if (unit == null || !unit.alive) yield break;
            // Дейкстра по очкам движения
            var dist = new Dictionary<(int, int), int> { [(unit.q, unit.r)] = 0 };
            var frontier = new List<(int q, int r)> { (unit.q, unit.r) };
            while (frontier.Count > 0)
            {
                frontier.Sort((a, b) => dist[a].CompareTo(dist[b]));
                var cur = frontier[0];
                frontier.RemoveAt(0);
                int d = dist[cur];
                if (d >= unit.movesLeft) continue;
                foreach (var nb in Neighbors(f, cur.q, cur.r))
                {
                    if (!nb.passable) continue;
                    var occ = f.UnitAt(nb.q, nb.r);
                    if (occ != null && occ.side != unit.side) continue;
                    if (occ != null && occ != unit) continue;
                    int cost = nb.MoveCost;
                    int nd = d + cost;
                    if (nd > unit.movesLeft) continue;
                    if (!dist.ContainsKey((nb.q, nb.r)) || dist[(nb.q, nb.r)] > nd)
                    {
                        dist[(nb.q, nb.r)] = nd;
                        frontier.Add((nb.q, nb.r));
                        yield return nb;
                    }
                }
            }
        }

        public static List<BattleHex> Neighbors(Battlefield f, int q, int r)
        {
            var list = new List<BattleHex>();
            foreach (var (dq, dr) in Dirs)
            {
                var h = f.Hex(q + dq, r + dr);
                if (h != null) list.Add(h);
            }
            return list;
        }

        public static bool MoveTo(Battlefield f, BattleUnit unit, int q, int r)
        {
            if (unit == null || !unit.alive || f.finished) return false;
            var target = f.Hex(q, r);
            if (target == null || !target.passable) return false;
            if (f.UnitAt(q, r) != null) return false;
            var legal = LegalMoves(f, unit).Any(h => h.q == q && h.r == r);
            if (!legal) return false;
            int cost = target.MoveCost;
            unit.q = q; unit.r = r;
            unit.movesLeft -= cost;
            if (unit.movesLeft <= 0) unit.movesLeft = 0;
            return true;
        }

        public static IEnumerable<BattleUnit> LegalTargets(Battlefield f, BattleUnit unit)
        {
            if (unit == null || !unit.alive) yield break;
            int range = unit.stats.range;
            foreach (var other in f.units)
            {
                if (!other.alive || other.routed || other.side == unit.side) continue;
                int d = HexDistance(unit.q, unit.r, other.q, other.r);
                if (d <= range) yield return other;
            }
        }

        public static int HexDistance(int q1, int r1, int q2, int r2)
        {
            // axial distance (q,r) - работает и для смещений odd-r в бою, т.к. сетка аксиальная
            int dq = q2 - q1;
            int dr = r2 - r1;
            return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2;
        }

        public static string Attack(Battlefield f, BattleUnit attacker, BattleUnit target, string ability = null)
        {
            if (f.finished || attacker == null || target == null || !attacker.alive || !target.alive) return null;
            int dist = HexDistance(attacker.q, attacker.r, target.q, target.r);
            bool ranged = attacker.stats.range > 1;
            if (!ranged && dist > 1) return "Цель слишком далеко";
            if (ranged && dist > attacker.stats.range) return "Цель вне дальности";

            var terrainDef = f.Hex(target.q, target.r)?.terrain ?? BattleTerrain.Plains;
            float defBonus = TerrainDefense(terrainDef, target) + (target.defending ? 3 : 0);
            float attack = attacker.stats.attack;
            float moraleFactor = 0.6f + 0.4f * (attacker.morale / 100f);
            float attackFactor = attack * moraleFactor;

            if (attacker.Has("charge") && f.round == 1) attackFactor *= 1.35f;
            if (attacker.Has("berserk")) attackFactor *= 1.2f;
            if (attacker.Has("shield_wall") && attacker.defending) attackFactor *= 0.95f;
            if (attacker.Has("flaming") || attacker.Has("dragon_breath")) attackFactor *= 1.25f;
            if (attacker.side == 1 && f.siege && f.Hex(attacker.q, attacker.r)?.terrain == BattleTerrain.Walls) attackFactor *= 1.15f;
            if (ability == "volley" && !attacker.usedAbilities.Contains("volley"))
            {
                attacker.usedAbilities.Add("volley");
                attackFactor *= 1.5f;
            }
            if (ability == "dragon_breath" && !attacker.usedAbilities.Contains("dragon_breath"))
            {
                attacker.usedAbilities.Add("dragon_breath");
                attackFactor *= 2.2f;
            }
            if (ability == "rally" && !attacker.usedAbilities.Contains("rally"))
            {
                attacker.usedAbilities.Add("rally");
                foreach (var ally in f.Side(attacker.side))
                    ally.morale = Math.Min(120, ally.morale + 18);
                return $"{attacker.name}: «Стройся!» — союзники воспрянули духом";
            }
            if (ability == "heal" && !attacker.usedAbilities.Contains("heal"))
            {
                attacker.usedAbilities.Add("heal");
                var worst = f.Side(attacker.side).OrderBy(u => u.hp).FirstOrDefault();
                if (worst != null) { worst.hp = Math.Min(worst.maxHp, worst.hp + 28); worst.morale = Math.Min(120, worst.morale + 6); }
                return $"{attacker.name}: лечение союзника";
            }

            // фланговая атака: союзник с противоположной стороны цели
            bool flank = f.units.Any(u => u.alive && !u.routed && u.side == attacker.side && u != attacker &&
                                          HexDistance(u.q, u.r, target.q, target.r) == 1 &&
                                          IsOpposite(attacker.q, attacker.r, u.q, u.r, target.q, target.r));
            if (flank) attackFactor *= 1.25f;

            float defFactor = 100f / (100f + Math.Max(0, target.stats.defense + defBonus) * 2.2f);
            float damage = attackFactor * defFactor * (0.85f + (float)f.State.Rng.NextDouble() * 0.3f);
            if (target.Has("fortified") && f.siege) damage *= 0.8f;

            target.hp -= damage;
            target.morale -= damage * 0.45f;
            attacker.attacksThisRound++;
            if (attacker.movesLeft > 0 && !ranged) attacker.movesLeft = 0;

            string msg = $"{attacker.name} {(ranged ? "стреляет в" : "атакует")} {target.name}: −{damage:0} (HP {Math.Max(0, target.hp):0})";
            if (flank) msg += " [фланг]";

            if (target.hp <= 0)
            {
                target.hp = 0;
                target.alive = false;
                msg += $" — {target.name} уничтожен!";
                f.AddLog(msg);
                CheckRout(f, target.side);
                if (!f.SideAlive(0) || !f.SideAlive(1)) Finish(f, f.SideAlive(0) && !f.SideAlive(1));
                return msg;
            }
            if (target.morale <= 0)
            {
                target.routed = true;
                msg += $" — {target.name} обращается в бегство!";
                CheckRout(f, target.side);
                if (!f.SideAlive(0) || !f.SideAlive(1)) Finish(f, f.SideAlive(0) && !f.SideAlive(1));
            }
            f.AddLog(msg);
            return msg;
        }

        private static bool IsOpposite(int aq, int ar, int bq, int br, int tq, int tr)
        {
            // угол между направлениями цель->a и цель->b больше 120 градусов
            double a1 = Math.Atan2(aq - tq, ar - tr);
            double a2 = Math.Atan2(bq - tq, br - tr);
            double diff = Math.Abs(a1 - a2);
            if (diff > Math.PI) diff = 2 * Math.PI - diff;
            return diff > Math.PI * 0.66;
        }

        private static void CheckRout(Battlefield f, int side)
        {
            var alive = f.Side(side).ToList();
            int total = f.units.Count(u => u.alive || u.routed);
            int routed = f.units.Count(u => u.side == side && u.routed);
            if (total > 0 && routed >= Math.Max(2, total * 0.6))
            {
                foreach (var u in alive) u.routed = true;
                f.AddLog($"Армия {(side == 0 ? "атакующих" : "защитников")} бежит с поля боя!");
            }
        }

        public static float TerrainDefense(BattleTerrain t, BattleUnit u)
        {
            switch (t)
            {
                case BattleTerrain.Forest: return 3;
                case BattleTerrain.Hills: return 4 + (u.kind == "archers" ? 1 : 0);
                case BattleTerrain.Mountains: return 6;
                case BattleTerrain.Marsh: return -2;
                case BattleTerrain.River: return -3;
                case BattleTerrain.Desert: return -1;
                case BattleTerrain.Snow: return 1;
                case BattleTerrain.Walls: return 8;
                default: return 0;
            }
        }

        public static void RunAuto(Battlefield f, int maxActions = 400)
        {
            int guard = 0;
            while (!f.finished && guard++ < maxActions)
            {
                var u = Active(f);
                if (u == null) { NextUnit(f); continue; }
                if (u.acted) { NextUnit(f); continue; }
                AiActivate(f, u);
                if (f.finished) break;
                NextUnit(f);
            }
            if (!f.finished) Finish(f, DecideByStrength(f));
        }

        /// <summary>Один ход ИИ за конкретный отряд — для пошагового ручного боя в UI.</summary>
        public static void AutoActivate(Battlefield f, BattleUnit u) => AiActivate(f, u);

        private static void AiActivate(Battlefield f, BattleUnit u)
        {
            var enemies = f.units.Where(x => x.alive && !x.routed && x.side != u.side).ToList();
            if (enemies.Count == 0) { u.acted = true; return; }

            // лечение/воодушевление при низком морали
            if (u.Has("rally") && !u.usedAbilities.Contains("rally") && u.morale < 55)
            {
                Attack(f, u, enemies[0], "rally");
                u.acted = true;
                return;
            }

            var target = enemies.OrderBy(x => HexDistance(u.q, u.r, x.q, x.r)).ThenBy(x => x.hp).First();
            int dist = HexDistance(u.q, u.r, target.q, target.r);
            if (dist > u.stats.range)
            {
                // идём к цели
                var legal = LegalMoves(f, u).ToList();
                if (legal.Count > 0)
                {
                    var best = legal.OrderBy(h => HexDistance(h.q, h.r, target.q, target.r))
                                    .ThenByDescending(h => TerrainDefense(h.terrain, u))
                                    .First();
                    MoveTo(f, u, best.q, best.r);
                }
                if (HexDistance(u.q, u.r, target.q, target.r) > u.stats.range)
                {
                    u.acted = true;
                    return;
                }
            }
            string ability = null;
            if (u.Has("dragon_breath") && !u.usedAbilities.Contains("dragon_breath") && enemies.Count >= 3) ability = "dragon_breath";
            else if (u.Has("volley") && !u.usedAbilities.Contains("volley") && enemies.Count >= 2) ability = "volley";
            Attack(f, u, target, ability);
            u.acted = true;
        }

        private static bool DecideByStrength(Battlefield f)
        {
            float a = f.Side(0).Sum(u => u.hp + u.stats.attack * 3);
            float d = f.Side(1).Sum(u => u.hp + u.stats.attack * 3);
            return a > d;
        }

        public static void Finish(Battlefield f, bool attackerWon)
        {
            if (f.finished) return;
            f.finished = true;
            f.attackerWon = attackerWon;
            f.AddLog(attackerWon ? "Атакующие победили." : "Защитники удержали позиции.");
        }

        /// <summary>Переносит потери из тактического боя в стратегические отряды.</summary>
        public static BattleResult ApplyResult(Battlefield f, ArmyState attacker, ArmyState defender)
        {
            var result = new BattleResult
            {
                attacker = f.attackerFaction,
                defender = f.defenderFaction,
                province = f.provinceId,
                attackerWon = f.attackerWon,
                heroAttacker = f.attackerHero,
                heroDefender = f.defenderHero,
                rounds = new List<string>(f.log),
            };
            int aLoss = 0, dLoss = 0;
            foreach (var bu in f.units)
            {
                ArmyState army = bu.side == 0 ? attacker : defender;
                if (army == null) continue;
                var inst = FindInstance(army, bu);
                if (inst == null) continue;
                float before = inst.hp;
                inst.hp = Math.Max(0, Math.Min(100f, bu.hp / Math.Max(1f, bu.maxHp) * 100f));
                inst.morale = Math.Max(0, bu.morale);
                inst.alive = bu.hp > 0 && !bu.routed;
                if (bu.routed) inst.hp = Math.Max(5, inst.hp * 0.5f);
                if (!inst.alive)
                {
                    if (bu.side == 0) aLoss++; else dLoss++;
                    army.units.Remove(inst);
                    if (army.destroyedUnits != null) army.destroyedUnits.Add(inst.defId);
                }
                else
                {
                    inst.xp += 10;
                    if (inst.xp >= 40 && inst.level < 5) { inst.level++; inst.xp = 0; }
                }
            }
            result.attackerLosses = aLoss;
            result.defenderLosses = dLoss;

            // опыт героев: победитель получает больше, проигравший — за упорство
            AwardHeroXp(f.State, attacker, f.attackerWon, dLoss);
            AwardHeroXp(f.State, defender, !f.attackerWon, aLoss);
            return result;
        }

        private static void AwardHeroXp(GameState state, ArmyState army, bool won, int enemyLosses)
        {
            if (state == null || army?.heroId == null) return;
            if (!state.Heroes.TryGetValue(army.heroId, out var hero) || !hero.alive) return;
            int gain = 10 + enemyLosses * 6 + (won ? 12 : 4) + (army.units.Count(u => u.alive) == 0 ? 0 : 3);
            hero.xp += gain;
            while (hero.level < 10 && hero.xp >= hero.level * 40)
            {
                hero.xp -= hero.level * 40;
                hero.level++;
                state.AddLog("hero", $"{hero.name} достигает {hero.level} уровня", army.faction);
                if (state.Faction(army.faction)?.isPlayer == true)
                    state.Notify($"{hero.name}: новый уровень ({hero.level})");
            }
        }

        private static UnitInstance FindInstance(ArmyState army, BattleUnit bu)
        {
            var candidates = army.units.Where(u => u.alive && u.defId == bu.sourceUnitId).ToList();
            if (candidates.Count == 0) return null;
            // ближайший по HP
            return candidates.OrderBy(u => Math.Abs(u.hp - bu.hp)).First();
        }
    }
}
