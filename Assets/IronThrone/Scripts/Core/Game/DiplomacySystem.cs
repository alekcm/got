using System;
using System.Collections.Generic;
using System.Linq;
using IronThrone.Core.Content;

namespace IronThrone.Core.Game
{
    /// <summary>
    /// Дипломатия великих домов: объявление войны, мир, перемирия, военная усталость.
    /// Отношения хранятся в FactionState.relations (−100..100), войны — в GameState.Wars.
    /// </summary>
    public static class DiplomacySystem
    {
        public const int WarDeclarationInfluence = 10;
        public const int PeaceInfluence = 8;
        public const int TruceTurns = 12;

        public static string PairKey(string a, string b) =>
            string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;

        public static WarState WarBetween(GameState state, string a, string b)
        {
            if (a == null || b == null || a == b) return null;
            return state.Wars.FirstOrDefault(w => w.active && w.Involves(a) && w.Involves(b));
        }

        public static int TruceLeft(GameState state, string a, string b)
        {
            if (a == null || b == null) return 0;
            return state.Truces.TryGetValue(PairKey(a, b), out var t) ? Math.Max(0, t) : 0;
        }

        public static float Relation(GameState state, string a, string b)
        {
            var f = state.Faction(a);
            if (f != null && f.relations.TryGetValue(b, out var r)) return r;
            var g = state.Faction(b);
            if (g != null && g.relations.TryGetValue(a, out var r2)) return r2;
            return 0;
        }

        public static void AddRelation(GameState state, string a, string b, float delta)
        {
            var fa = state.Faction(a); var fb = state.Faction(b);
            if (fa != null)
            {
                fa.relations.TryGetValue(b, out var r);
                fa.relations[b] = EffectEngine.Clamp(r + delta, -100, 100);
            }
            if (fb != null)
            {
                fb.relations.TryGetValue(a, out var r);
                fb.relations[a] = EffectEngine.Clamp(r + delta, -100, 100);
            }
        }

        public static bool CanDeclareWar(GameState state, FactionState f, string targetId, out string error)
        {
            error = null;
            var target = state.Faction(targetId);
            if (f == null || !f.alive) { error = "Дом не существует"; return false; }
            if (target == null || !target.alive) { error = "Цель недоступна"; return false; }
            if (targetId == f.id) { error = "Нельзя воевать с собой"; return false; }
            if (WarBetween(state, f.id, targetId) != null) { error = "Вы уже в состоянии войны"; return false; }
            int truce = TruceLeft(state, f.id, targetId);
            if (truce > 0) { error = $"Перемирие ещё {truce} ходов"; return false; }
            if (f.influence < WarDeclarationInfluence) { error = $"Нужно {WarDeclarationInfluence} влияния"; return false; }
            return true;
        }

        public static WarState DeclareWar(GameState state, FactionState f, string targetId, bool silent = false)
        {
            if (!CanDeclareWar(state, f, targetId, out var error))
            {
                if (!silent && f != null && f.isPlayer) state.Notify(error);
                return null;
            }
            var target = state.Faction(targetId);
            f.influence -= WarDeclarationInfluence;
            var war = new WarState
            {
                attacker = f.id,
                defender = targetId,
                aggressor = f.id,
                startTurn = state.Turn,
            };
            state.Wars.Add(war);
            AddRelation(state, f.id, targetId, -45);
            // союзники цели недовольны агрессором
            foreach (var other in state.Factions.Values.Where(x => x.alive && x.id != f.id && x.id != targetId))
            {
                float rel = Relation(state, other.id, targetId);
                if (rel > 25) AddRelation(state, other.id, f.id, -12);
            }
            state.AddLog("diplomacy", $"{f.def.name} объявляет войну {target.def.name}", f.id);
            if (f.isPlayer) state.Notify($"Вы объявили войну дому {target.def.name}");
            if (target.isPlayer) state.Notify($"{f.def.name} объявляет вам войну!");
            return war;
        }

        public static bool CanMakePeace(GameState state, FactionState f, string targetId, out string error)
        {
            error = null;
            if (WarBetween(state, f.id, targetId) == null) { error = "Вы не в состоянии войны"; return false; }
            if (f.influence < PeaceInfluence) { error = $"Нужно {PeaceInfluence} влияния"; return false; }
            return true;
        }

        public static bool MakePeace(GameState state, FactionState f, string targetId, bool silent = false)
        {
            if (!CanMakePeace(state, f, targetId, out var error))
            {
                if (!silent && f != null && f.isPlayer) state.Notify(error);
                return false;
            }
            return EndWar(state, f.id, targetId, f.id, whitePeace: true);
        }

        public static bool EndWar(GameState state, string a, string b, string proposer, bool whitePeace)
        {
            var war = WarBetween(state, a, b);
            if (war == null) return false;
            var fa = state.Faction(a); var fb = state.Faction(b);
            if ((proposer == a && fa != null) || (proposer == b && fb != null))
                (proposer == a ? fa : fb).influence = Math.Max(0, (proposer == a ? fa : fb).influence - PeaceInfluence);

            war.active = false;
            state.Truces[PairKey(a, b)] = TruceTurns;
            AddRelation(state, a, b, whitePeace ? 15 : 0);
            state.AddLog("diplomacy", $"Мир между {state.FactionName(a)} и {state.FactionName(b)}", proposer);
            if (fa != null && fa.isPlayer) state.Notify($"Заключён мир с домом {fb?.def.name}");
            if (fb != null && fb.isPlayer) state.Notify($"Заключён мир с домом {fa?.def.name}");
            return true;
        }

        public const int GiftGold = 100;
        public const float GiftRelation = 10f;

        /// <summary>Дары великому дому: золото в обмен на расположение. Основа политических союзов.</summary>
        public static bool SendGift(GameState state, FactionState f, string targetId, out string error)
        {
            error = null;
            if (f == null) { error = "Нет дома"; return false; }
            var target = state.Faction(targetId);
            if (target == null || target.id == f.id) { error = "Нет такого дома"; return false; }
            if (!target.alive) { error = "Дом уже не существует"; return false; }
            if (f.gold < GiftGold) { error = $"Нужно {GiftGold} золота"; return false; }
            f.gold -= GiftGold;
            AddRelation(state, f.id, target.id, GiftRelation);
            state.AddLog("diplomacy", $"{f.def.name} отправляет дары дому {target.def.name} (+{GiftRelation:0} отношений)", f.id);
            return true;
        }

        // ------------------------------------------------------------------ ИИ
        public static void AiConsiderWars(GameState state, FactionState f)
        {
            if (!f.alive) return;
            var atWarWith = new HashSet<string>();
            foreach (var w in state.Wars.Where(w => w.active && w.Involves(f.id)))
                atWarWith.Add(w.EnemyOf(f.id));

            // мир, если воюем долго и без успеха или теряем силы
            foreach (var enemyId in atWarWith.ToList())
            {
                var war = WarBetween(state, f.id, enemyId);
                if (war == null) continue;
                float myPower = MilitarySystem.EstimateStrength(state, f);
                var enemy = state.Faction(enemyId);
                float enemyPower = enemy == null ? 0 : MilitarySystem.EstimateStrength(state, enemy);
                int myBattles = war.attacker == f.id ? war.attackerBattles : war.defenderBattles;
                int theirBattles = war.attacker == f.id ? war.defenderBattles : war.attackerBattles;
                bool tired = war.turns >= 24 && Math.Abs(myBattles - theirBattles) < 3;
                bool losing = myPower < enemyPower * 0.55f && war.turns >= 8;
                if ((tired || losing) && f.influence >= PeaceInfluence && state.Rng.NextDouble() < 0.3)
                    MakePeace(state, f, enemyId);
            }

            if (atWarWith.Count >= 2) return;
            if (f.influence < WarDeclarationInfluence + 10) return;
            if (state.Rng.NextDouble() > 0.035) return;

            // ищем слабого соседа с плохими отношениями
            var myProvinces = state.Provinces.Values.Where(p => p.controller == f.id).ToList();
            if (myProvinces.Count == 0) return;
            var border = myProvinces.SelectMany(p => p.def.neighbors).Select(id => state.Province(id))
                .Where(p => p != null && p.controller != null && p.controller != f.id)
                .Select(p => p.controller).Distinct().ToList();

            FactionState best = null; float bestScore = float.MinValue;
            foreach (var enemyId in border)
            {
                if (atWarWith.Contains(enemyId)) continue;
                var enemy = state.Faction(enemyId);
                if (enemy == null || !enemy.alive) continue;
                if (TruceLeft(state, f.id, enemyId) > 0) continue;
                float rel = Relation(state, f.id, enemyId);
                if (rel > 10) continue;
                float power = MilitarySystem.EstimateStrength(state, f) + 1f;
                float enemyPower = MilitarySystem.EstimateStrength(state, enemy) + 1f;
                float score = (power / enemyPower) * 40f - rel - enemy.vassals.Count * 5f;
                if (score > bestScore) { bestScore = score; best = enemy; }
            }
            if (best != null && bestScore > 35)
                DeclareWar(state, f, best.id);
        }

        public static void ProcessTurn(GameState state)
        {
            // перемирия истекают
            foreach (var key in state.Truces.Keys.ToList())
            {
                state.Truces[key]--;
                if (state.Truces[key] <= 0) state.Truces.Remove(key);
            }

            foreach (var war in state.Wars.Where(w => w.active))
            {
                war.turns++;
                if (!state.Factions.TryGetValue(war.attacker, out var a) || !a.alive ||
                    !state.Factions.TryGetValue(war.defender, out var d) || !d.alive)
                {
                    war.active = false;
                    continue;
                }
                // военная усталость: длинная война бьёт по беспорядкам и отношениям
                if (war.turns > 0 && war.turns % 6 == 0)
                {
                    foreach (var fid in new[] { war.attacker, war.defender })
                    {
                        var f = state.Faction(fid);
                        if (f == null) continue;
                        foreach (var p in state.Provinces.Values.Where(p => p.controller == fid))
                            p.unrest = Math.Min(100, p.unrest + 2f);
                        AddRelation(state, war.attacker, war.defender, -3);
                        if (f.isPlayer) state.Notify("Война затягивается: растёт недовольство");
                    }
                }
                // истощённые дома сами ищут мира
                if (war.turns >= 30)
                {
                    var aWorn = state.Rng.NextDouble() < 0.05;
                    if (aWorn)
                    {
                        var f = state.Faction(war.attacker);
                        if (f != null && f.influence >= PeaceInfluence && state.Rng.NextDouble() < 0.5)
                            MakePeace(state, f, war.defender);
                    }
                }
            }

            // медленный дрейф отношений к нейтральным
            foreach (var f in state.Factions.Values.Where(x => x.alive))
            {
                foreach (var otherId in f.relations.Keys.ToList())
                {
                    if (WarBetween(state, f.id, otherId) != null) continue;
                    float r = f.relations[otherId];
                    if (r > 0) f.relations[otherId] = Math.Max(0, r - 0.15f);
                    else if (r < 0) f.relations[otherId] = Math.Min(0, r + 0.15f);
                }
            }
        }

        /// <summary>Выгодно ли игроку/ИИ напасть: используется интерфейсом и ИИ.</summary>
        public static bool IsAtWar(GameState state, string a, string b) => WarBetween(state, a, b) != null;

        public static IEnumerable<FactionState> EnemiesOf(GameState state, string factionId) =>
            state.Wars.Where(w => w.active && w.Involves(factionId))
                .Select(w => state.Faction(w.EnemyOf(factionId)))
                .Where(f => f != null && f.alive);
    }
}
