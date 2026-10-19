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

        public const int PactInfluence = 6;
        public const int PactTurns = 10;
        public const int PactBreakInfluence = 15;
        public const int MarriageGold = 300;
        public const int MarriageInfluence = 8;
        public const int MaxMarriages = 2;
        public const float CoalitionShare = 0.4f;
        public const int CoalitionTurns = 25;

        /// <summary>Стоимость объявления войны: против лидера коалиции дворы сговорчивее.</summary>
        public static int WarCost(GameState state, string targetId) =>
            targetId != null && targetId == state.CoalitionTarget
                ? Math.Max(4, WarDeclarationInfluence - 4)
                : WarDeclarationInfluence;

        // ------------------------------------------------------------------ пакты о ненападении
        public static int PactLeft(GameState state, string a, string b)
        {
            if (a == null || b == null) return 0;
            return state.Pacts.TryGetValue(PairKey(a, b), out var t) ? Math.Max(0, t) : 0;
        }

        public static bool HasPact(GameState state, string a, string b) => PactLeft(state, a, b) > 0;

        public static bool CanMakePact(GameState state, FactionState f, string targetId, out string error)
        {
            error = null;
            var target = state.Faction(targetId);
            if (f == null || !f.alive) { error = "Дом не существует"; return false; }
            if (target == null || !target.alive) { error = "Цель недоступна"; return false; }
            if (target.id == f.id) { error = "Нельзя заключить пакт с собой"; return false; }
            if (WarBetween(state, f.id, targetId) != null) { error = "Сначала заключите мир"; return false; }
            if (HasPact(state, f.id, targetId)) { error = $"Пакт уже действует: {PactLeft(state, f.id, targetId)} ходов"; return false; }
            if (Relation(state, f.id, targetId) < 0) { error = "Отношения враждебны — сначала дары или мир"; return false; }
            if (f.influence < PactInfluence) { error = $"Нужно {PactInfluence} влияния"; return false; }
            return true;
        }

        public static bool MakePact(GameState state, FactionState f, string targetId, out string error)
        {
            if (!CanMakePact(state, f, targetId, out error))
            {
                if (f != null && f.isPlayer) state.Notify(error);
                return false;
            }
            var target = state.Faction(targetId);
            f.influence -= PactInfluence;
            state.Pacts[PairKey(f.id, targetId)] = PactTurns;
            AddRelation(state, f.id, targetId, 8);
            state.AddLog("diplomacy", $"Пакт о ненападении между {f.def.name} и {target.def.name} на {PactTurns} ходов", f.id);
            if (f.isPlayer) state.Notify($"Пакт о ненападении с домом {target.def.name} заключён на {PactTurns} ходов");
            if (target.isPlayer) state.Notify($"{f.def.name} предлагает и заключает пакт о ненападении");
            return true;
        }

        /// <summary>Разорвать пакт можно, но это стоит влияния и репутации.</summary>
        public static bool BreakPact(GameState state, FactionState f, string targetId, out string error)
        {
            error = null;
            if (!HasPact(state, f.id, targetId)) { error = "Пакта нет"; return false; }
            if (f.influence < PactBreakInfluence) { error = $"Разрыв пакта стоит {PactBreakInfluence} влияния"; return false; }
            f.influence -= PactBreakInfluence;
            state.Pacts.Remove(PairKey(f.id, targetId));
            AddRelation(state, f.id, targetId, -30);
            foreach (var other in state.Factions.Values.Where(x => x.alive && x.id != f.id && x.id != targetId))
                AddRelation(state, other.id, f.id, -8);
            state.AddLog("diplomacy", $"{f.def.name} разрывает пакт о ненападении с {state.FactionName(targetId)}. Дворы это запомнят.", f.id);
            if (f.isPlayer) state.Notify("Пакт разорван: отношения испорчены");
            return true;
        }

        // ------------------------------------------------------------------ династические браки
        public static int MarriageCount(FactionState f) => f?.marriages.Count ?? 0;

        public static bool CanMarry(GameState state, FactionState f, string targetId, out string error)
        {
            error = null;
            var target = state.Faction(targetId);
            if (f == null || !f.alive) { error = "Дом не существует"; return false; }
            if (target == null || !target.alive) { error = "Цель недоступна"; return false; }
            if (target.id == f.id) { error = "Нельзя жениться на себе"; return false; }
            if (f.marriages.Contains(targetId)) { error = "С этим домом вы уже в родстве"; return false; }
            if (f.marriages.Count >= MaxMarriages) { error = $"Больше {MaxMarriages} браков дом не потянет"; return false; }
            if (target.marriages.Count >= MaxMarriages) { error = "У этого дома уже довольно брачных узлов"; return false; }
            if (WarBetween(state, f.id, targetId) != null) { error = "Во время войны о свадьбе не может быть речи"; return false; }
            if (Relation(state, f.id, targetId) < 25) { error = "Отношения слишком холодны (нужно 25+)"; return false; }
            if (f.gold < MarriageGold) { error = $"Свадьба стоит {MarriageGold} золота"; return false; }
            if (f.influence < MarriageInfluence) { error = $"Нужно {MarriageInfluence} влияния"; return false; }
            return true;
        }

        public static bool Marry(GameState state, FactionState f, string targetId, out string error)
        {
            if (!CanMarry(state, f, targetId, out error))
            {
                if (f != null && f.isPlayer) state.Notify(error);
                return false;
            }
            var target = state.Faction(targetId);
            f.gold -= MarriageGold;
            f.influence -= MarriageInfluence;
            f.marriages.Add(targetId);
            target.marriages.Add(f.id);
            AddRelation(state, f.id, targetId, 20);
            state.AddLog("diplomacy", $"Династический брак: {f.def.name} и {target.def.name} породнились", f.id);
            if (f.isPlayer) state.Notify($"Брак с домом {target.def.name}: +20 отношений, +0.3 признания за ход");
            if (target.isPlayer) state.Notify($"{f.def.name} предлагает династический брак — и получает согласие");
            return true;
        }

        /// <summary>Признание за ход от брачных союзов: по +0.3, но не больше +0.6 за ход.</summary>
        public static float MarriageRecognition(FactionState f) =>
            f == null ? 0f : Math.Min(0.6f, f.marriages.Count * 0.3f);

        // ------------------------------------------------------------------ коалиция против лидера
        public static void UpdateCoalition(GameState state)
        {
            var leader = state.LivingFactions.OrderByDescending(f => state.CountControlled(f.id)).FirstOrDefault();
            float share = leader == null || state.Provinces.Count == 0
                ? 0f
                : state.CountControlled(leader.id) / (float)state.Provinces.Count;

            if (state.CoalitionTarget != null)
            {
                var current = state.Faction(state.CoalitionTarget);
                bool alive = current != null && current.alive;
                bool stillDangerous = alive && state.Turn < state.CoalitionUntil &&
                                      state.CountControlled(current.id) / (float)Math.Max(1, state.Provinces.Count) > 0.3f;
                if (alive && stillDangerous) return;
                state.CoalitionTarget = null;
                state.CoalitionUntil = 0;
                state.AddLog("diplomacy", "Коалиция распалась: общая угроза миновала", null);
                return;
            }

            if (leader == null || state.Turn < 20 || share < CoalitionShare) return;

            state.CoalitionTarget = leader.id;
            state.CoalitionUntil = state.Turn + CoalitionTurns;
            var members = state.LivingFactions.Where(x => x.id != leader.id).ToList();
            for (int i = 0; i < members.Count; i++)
                for (int j = i + 1; j < members.Count; j++)
                    AddRelation(state, members[i].id, members[j].id, 12);
            foreach (var m in members) AddRelation(state, m.id, leader.id, -10);
            state.AddLog("diplomacy", $"Коалиция против {leader.def.name}: дома объединяются перед общей угрозой", null);
            if (state.Player != null)
            {
                if (state.Player.id == leader.id) state.Notify("Ваше усиление пугает соседей: против вас собирается коалиция");
                else state.Notify($"{leader.def.name} слишком силён — сложилась коалиция, война с ним дешевле");
            }
        }

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
            int pact = PactLeft(state, f.id, targetId);
            if (pact > 0) { error = $"Пакт о ненападении ещё {pact} ходов"; return false; }
            int cost = WarCost(state, targetId);
            if (f.influence < cost) { error = $"Нужно {cost} влияния"; return false; }
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
            f.influence -= WarCost(state, targetId);
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
                if (PactLeft(state, f.id, enemyId) > 0) continue;
                float rel = Relation(state, f.id, enemyId);
                if (rel > 10) continue;
                float power = MilitarySystem.EstimateStrength(state, f) + 1f;
                float enemyPower = MilitarySystem.EstimateStrength(state, enemy) + 1f;
                float score = (power / enemyPower) * 40f - rel - enemy.vassals.Count * 5f;
                if (enemyId == state.CoalitionTarget) score += 30f;
                if (score > bestScore) { bestScore = score; best = enemy; }
            }
            if (best != null && bestScore > 35)
                DeclareWar(state, f, best.id);
        }

        /// <summary>Пакты и браки: ИИ укрепляет тыл, прежде чем воевать.</summary>
        public static void AiConsiderDiplomacy(GameState state, FactionState f)
        {
            if (f == null || !f.alive || f.isPlayer) return;
            var others = state.LivingFactions.Where(x => x.id != f.id).ToList();

            // династический брак с самым дружественным домом
            if (f.marriages.Count < MaxMarriages && f.gold > MarriageGold + 100 && f.influence > MarriageInfluence + 6)
            {
                var groom = others
                    .Where(x => !f.marriages.Contains(x.id) && x.marriages.Count < MaxMarriages)
                    .Where(x => WarBetween(state, f.id, x.id) == null)
                    .OrderByDescending(x => Relation(state, f.id, x.id))
                    .FirstOrDefault();
                if (groom != null && Relation(state, f.id, groom.id) >= 35 && state.Rng.NextDouble() < 0.18)
                    Marry(state, f, groom.id, out _);
            }

            // пакт о ненападении: с сильным соседом, пока заняты другой войной
            if (f.influence >= PactInfluence + 2 && state.Rng.NextDouble() < 0.2)
            {
                int wars = state.Wars.Count(w => w.active && w.Involves(f.id));
                var partner = others
                    .Where(x => !HasPact(state, f.id, x.id) && WarBetween(state, f.id, x.id) == null)
                    .Where(x => Relation(state, f.id, x.id) >= 0)
                    .OrderByDescending(x => MilitarySystem.EstimateStrength(state, x))
                    .FirstOrDefault();
                if (partner != null && (wars > 0 || MilitarySystem.EstimateStrength(state, partner) > MilitarySystem.EstimateStrength(state, f)))
                    MakePact(state, f, partner.id, out _);
            }
        }

        public static void ProcessTurn(GameState state)
        {
            // перемирия и пакты истекают
            foreach (var key in state.Truces.Keys.ToList())
            {
                state.Truces[key]--;
                if (state.Truces[key] <= 0) state.Truces.Remove(key);
            }
            foreach (var key in state.Pacts.Keys.ToList())
            {
                state.Pacts[key]--;
                if (state.Pacts[key] <= 0)
                {
                    state.Pacts.Remove(key);
                    state.AddLog("diplomacy", "Срок пакта о ненападении истёк", null);
                }
            }

            UpdateCoalition(state);
            foreach (var f in state.Factions.Values.Where(x => x.alive).ToList())
                AiConsiderDiplomacy(state, f);

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
                    float floor = f.marriages.Contains(otherId) ? 25f : 0f;
                    if (r > floor) f.relations[otherId] = Math.Max(floor, r - 0.15f);
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
