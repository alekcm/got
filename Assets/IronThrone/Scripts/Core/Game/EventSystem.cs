using System;
using System.Collections.Generic;
using System.Linq;
using IronThrone.Core.Content;

namespace IronThrone.Core.Game
{
    /// <summary>Случайные события, глобальные ивенты и цепочки квестов.</summary>
    public static class EventSystem
    {
        public static bool Fits(GameState state, EventDef ev, FactionState f)
        {
            if (ev.conditions == null) return true;
            var c = ev.conditions;
            if (!string.IsNullOrEmpty(c.faction) && c.faction != f.id && c.faction != f.house) return false;
            if (!string.IsNullOrEmpty(c.era) && c.era != state.Era.id) return false;
            if (ev.minTurn > 0 && state.Turn < ev.minTurn) return false;
            if (c.minTurn > 0 && state.Turn < c.minTurn) return false;
            if (c.maxTurn > 0 && state.Turn > c.maxTurn) return false;
            if (!string.IsNullOrEmpty(c.requiresTech) && !f.HasTech(c.requiresTech)) return false;
            if (!string.IsNullOrEmpty(c.requiresFaith) && f.faith != c.requiresFaith) return false;
            if (c.minUnrest > 0 && !state.ProvincesOf(f.id).Any(p => p.unrest >= c.minUnrest)) return false;
            if (!string.IsNullOrEmpty(c.requiresFlag) && !f.HasFlag(c.requiresFlag)) return false;
            if (!string.IsNullOrEmpty(c.notFlag) && f.HasFlag(c.notFlag)) return false;
            if (!string.IsNullOrEmpty(c.requiresProvince) && state.Province(c.requiresProvince)?.controller != f.id) return false;
            if (c.isRuler && state.Province("kings_landing")?.controller != f.id) return false;
            return true;
        }

        public static void ProcessTurn(GameState state)
        {
            // 0. отложенные события цепочек
            FireScheduled(state);

            // 1. глобальные ивенты эпохи: разово на пороговом ходу и далее по периоду
            var eraGlobals = state.Era.globalEvents ?? new List<string>();
            foreach (var ev in state.Content.Events.Where(e => e.type == "global"))
            {
                if (eraGlobals.Count > 0 && !eraGlobals.Contains(ev.id)) continue;
                if (state.Turn < ev.minTurn) continue;
                if (ev.oncePerGame && state.firedEvents.Contains(ev.id)) continue;
                bool threshold = state.Turn == ev.minTurn;
                bool periodic = ev.everyTurns > 0 && ev.everyTurns < 500 && state.Turn % ev.everyTurns == 0;
                if (threshold || periodic) TriggerGlobal(state, ev);
            }

            // 2. случайные события для фракций
            foreach (var f in state.Factions.Values.Where(x => x.alive))
            {
                if (state.Rng.Next(0, 100) > state.Settings.eventChancePercent) continue;
                var pool = state.Content.Events
                    .Where(e => e.type == "random" && Fits(state, e, f))
                    .Where(e => !e.oncePerGame || !state.firedEvents.Contains(e.id))
                    .ToList();
                if (pool.Count == 0) continue;
                float total = pool.Sum(e => Math.Max(0.01f, e.weight));
                double roll = state.Rng.NextDouble() * total;
                EventDef picked = pool[0];
                foreach (var e in pool)
                {
                    roll -= Math.Max(0.01f, e.weight);
                    if (roll <= 0) { picked = e; break; }
                }
                Fire(state, picked, f);
            }

            ProcessQuests(state);
        }

        /// <summary>Цепочка: выбор варианта может назначить продолжение через несколько ходов.</summary>
        public static void Schedule(GameState state, string eventId, string factionId, int delay)
        {
            if (string.IsNullOrEmpty(eventId)) return;
            state.ScheduledEvents.Add(new ScheduledEventState
            {
                turn = state.Turn + Math.Max(1, delay),
                eventId = eventId,
                faction = factionId,
            });
            string title = state.Content.EventById.TryGetValue(eventId, out var next) ? next.name : eventId;
            state.AddLog("event", "Слух о грядущем: " + title, factionId);
        }

        private static void FireScheduled(GameState state)
        {
            if (state.ScheduledEvents.Count == 0) return;
            foreach (var se in state.ScheduledEvents.Where(x => x.turn <= state.Turn).ToList())
            {
                state.ScheduledEvents.Remove(se);
                var f = state.Faction(se.faction);
                if (f == null || !f.alive) continue;
                if (!state.Content.EventById.TryGetValue(se.eventId, out var ev)) continue;
                if (!Fits(state, ev, f)) continue;
                if (ev.oncePerGame && state.firedEvents.Contains(ev.id)) continue;
                Fire(state, ev, f);
                // цепочка может продолжаться и без выбора игрока
                if (f.isPlayer) continue;
            }
        }

        public static void Fire(GameState state, EventDef ev, FactionState f)
        {
            state.firedEvents.Add(ev.id);
            state.eventHistory[ev.id] = state.Turn;
            state.PendingEvents.Add(new PendingEventState { eventId = ev.id, faction = f.id });
            state.AddLog("event", $"Событие для {f.def.name}: {ev.name}", f.id);
            if (f.isPlayer)
            {
                state.Notify("Событие: " + ev.name);
                return;
            }
            // ИИ выбирает вариант сам
            Choose(state, state.PendingEvents.Count - 1, BestChoiceForAi(state, ev));
        }

        public static void TriggerGlobal(GameState state, EventDef ev)
        {
            if (ev.oncePerGame && state.firedEvents.Contains(ev.id)) return;
            state.firedEvents.Add(ev.id);
            var choice = ev.choices.FirstOrDefault();
            state.AddLog("event", $"Глобальное событие: {ev.name}", null);
            state.Notify("Глобальное событие: " + ev.name);
            if (choice != null)
            {
                foreach (var f in state.Factions.Values.Where(x => x.alive))
                {
                    var ctx = new EffectContext { State = state, Source = f, TargetFaction = f };
                    EffectEngine.ApplyAll(choice.effects, ctx);
                }
            }
        }

        private static int BestChoiceForAi(GameState state, EventDef ev)
        {
            int best = 0; float bestScore = float.MinValue;
            for (int i = 0; i < ev.choices.Count; i++)
            {
                var c = ev.choices[i];
                float score = c.aiWeight;
                foreach (var e in c.effects)
                {
                    if (e.op == "resource" && e.amount > 0) score += e.amount / 50f;
                    if (e.op == "resource" && e.amount < 0) score += e.amount / 50f;
                    if (e.op == "unrest" && e.amount < 0) score += 1;
                    if (e.op == "unrest" && e.amount > 0) score -= 1;
                    if (e.op == "modifier" && e.value > 0) score += 2;
                }
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        public static string Choose(GameState state, int pendingIndex, int choiceIndex)
        {
            if (pendingIndex < 0 || pendingIndex >= state.PendingEvents.Count) return null;
            var pending = state.PendingEvents[pendingIndex];
            var ev = state.Content.EventById.TryGetValue(pending.eventId, out var e) ? e : null;
            var f = state.Faction(pending.faction);
            if (ev == null || f == null) { state.PendingEvents.RemoveAt(pendingIndex); return null; }
            if (choiceIndex < 0 || choiceIndex >= ev.choices.Count) choiceIndex = 0;
            var choice = ev.choices[choiceIndex];
            if (choice.cost != null && !EffectEngine.CanAfford(f, choice.cost))
            {
                return "Недостаточно ресурсов для этого выбора";
            }
            if (choice.cost != null) EffectEngine.Pay(f, choice.cost);
            var ctx = new EffectContext { State = state, Source = f, TargetFaction = f };
            var desc = new List<string>();
            EffectEngine.ApplyAll(choice.effects, ctx, desc);
            state.PendingEvents.RemoveAt(pendingIndex);
            if (!string.IsNullOrEmpty(choice.followUpEvent))
                Schedule(state, choice.followUpEvent, f.id, choice.followUpDelay);
            state.AddLog("event", $"{f.def.name}: {ev.name} — {choice.text}" +
                (desc.Count > 0 ? " (" + string.Join(", ", desc) + ")" : ""), f.id);
            if (f.isPlayer)
                state.Notify((choice.resultText ?? ev.name) + (desc.Count > 0 ? ": " + string.Join(", ", desc) : ""));
            return null;
        }

        // ------------------------------------------------------------------ квесты
        public static void ProcessQuests(GameState state)
        {
            foreach (var qs in state.Quests.Where(q => !q.completed && !q.failed))
            {
                var def = state.Content.QuestById.TryGetValue(qs.id, out var d) ? d : null;
                if (def == null) { qs.failed = true; continue; }
                var f = qs.faction != null ? state.Faction(qs.faction) : state.Player;
                if (f == null || !f.alive) { qs.failed = true; continue; }
                if (qs.step >= def.steps.Count) { qs.completed = true; continue; }
                var step = def.steps[qs.step];
                if (ObjectiveMet(state, f, step.objective))
                {
                    var desc = new List<string>();
                    EffectEngine.ApplyAll(step.reward, new EffectContext { State = state, Source = f, TargetFaction = f }, desc);
                    qs.step++;
                    if (qs.step >= def.steps.Count) qs.completed = true;
                    state.AddLog("quest", $"{f.def.name}: квест «{def.name}» — шаг выполнен. {step.rewardText}", f.id);
                    if (f.isPlayer) state.Notify($"Квест «{def.name}»: {step.rewardText ?? "шаг выполнен"}");
                }
            }
        }

        private static bool ObjectiveMet(GameState state, FactionState f, QuestObjectiveDef o)
        {
            if (o == null) return true;
            switch ((o.type ?? "").ToLowerInvariant())
            {
                case "controlprovince":
                    return state.Province(o.target)?.controller == f.id;
                case "controlregion":
                    return state.Provinces.Values.Count(p => p.def.region == o.target && p.controller == f.id) >= Math.Max(1, o.count);
                case "tech":
                    return f.HasTech(o.target);
                case "gold":
                    return f.gold >= o.count;
                case "build":
                    return state.Provinces.Values.Any(p => p.controller == f.id && p.buildings.Contains(o.target));
                case "vassal":
                    return string.IsNullOrEmpty(o.target) ? f.vassals.Count >= Math.Max(1, o.count) : f.vassals.ContainsKey(o.target);
                case "faithshare":
                    return FaithSystem.FaithShare(state, f) >= (o.count > 0 ? o.count / 100f : 0.3f);
                case "title":
                    return f.HasTitle(state, o.target);
                case "army":
                    return MilitarySystem.EstimateStrength(state, f) >= o.count;
                case "killhero":
                    return f.counters.TryGetValue("killed:" + o.target, out var v) && v > 0;
                case "flag":
                    return f.HasFlag(o.target);
                default:
                    return false;
            }
        }
    }
}
