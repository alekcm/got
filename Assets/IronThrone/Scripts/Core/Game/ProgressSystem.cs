using System;
using System.Collections.Generic;
using System.Linq;
using IronThrone.Core.Content;

namespace IronThrone.Core.Game
{
    /// <summary>Технологии и национальные фокусы.</summary>
    public static class ProgressSystem
    {
        // ------------------------------------------------------------------ технологии
        public static IEnumerable<TechDef> AvailableTechs(GameState state, FactionState f)
        {
            return state.Content.TechsFor(f.house).Where(t =>
                !f.techs.Contains(t.id) &&
                f.currentResearch != t.id &&
                t.requires.All(r => f.techs.Contains(r)));
        }

        public static bool CanResearch(GameState state, FactionState f, TechDef tech, out string error)
        {
            error = null;
            if (tech == null) { error = "Неизвестная технология"; return false; }
            if (f.techs.Contains(tech.id)) { error = "Уже изучено"; return false; }
            if (!tech.requires.All(r => f.techs.Contains(r))) { error = "Не выполнены пререквизиты"; return false; }
            if (f.currentResearch != null) { error = "Уже идёт исследование"; return false; }
            if (!EffectEngine.CanAfford(f, tech.cost)) { error = "Недостаточно ресурсов: " + tech.cost.ToRu(); return false; }
            return true;
        }

        public static bool StartResearch(GameState state, FactionState f, string techId, out string error)
        {
            if (!state.Content.TechById.TryGetValue(techId, out var tech)) { error = "Неизвестная технология"; return false; }
            if (!CanResearch(state, f, tech, out error)) return false;
            EffectEngine.Pay(f, tech.cost);
            f.currentResearch = techId;
            f.research[techId] = Math.Max(1, tech.researchTurns);
            state.AddLog("tech", $"{f.def.name} исследует «{tech.name}»", f.id);
            return true;
        }

        public static void CompleteResearchImmediately(GameState state, FactionState f, string techId)
        {
            if (!state.Content.TechById.TryGetValue(techId, out var tech)) return;
            if (f.techs.Contains(techId)) return;
            f.techs.Add(techId);
            EffectEngine.ApplyAll(tech.effects, new EffectContext { State = state, Source = f, TargetFaction = f });
            if (f.currentResearch == techId) f.currentResearch = null;
            f.research.Remove(techId);
            state.AddLog("tech", $"{f.def.name} завершает «{tech.name}»", f.id);
            if (f.isPlayer) state.Notify($"Исследование завершено: {tech.name}");
        }

        public static void ProcessResearch(GameState state)
        {
            foreach (var f in state.Factions.Values.Where(x => x.alive))
            {
                if (string.IsNullOrEmpty(f.currentResearch)) continue;
                if (!f.research.ContainsKey(f.currentResearch)) { f.currentResearch = null; continue; }
                f.research[f.currentResearch]--;
                if (f.research[f.currentResearch] <= 0)
                    CompleteResearchImmediately(state, f, f.currentResearch);
            }
        }

        // ------------------------------------------------------------------ фокусы
        public static IEnumerable<FocusDef> FocusesOf(GameState state, FactionState f)
        {
            return state.Content.Focuses.Where(fo =>
                (fo.faction == f.house || fo.faction == f.id) &&
                (fo.eras.Count == 0 || fo.eras.Contains(state.Era.id)));
        }

        public static bool IsFocusAvailable(GameState state, FactionState f, FocusDef fo, out string error)
        {
            error = null;
            if (fo == null) { error = "Неизвестный фокус"; return false; }
            if (f.completedFocuses.Contains(fo.id)) { error = "Уже выполнен"; return false; }
            if (f.currentFocus == fo.id) { error = "Уже выполняется"; return false; }
            foreach (var req in fo.requires)
                if (!f.completedFocuses.Contains(req)) { error = "Сначала выполните предыдущие фокусы"; return false; }
            foreach (var ex in fo.exclusive)
                if (f.completedFocuses.Contains(ex)) { error = "Этот путь уже закрыт"; return false; }
            return true;
        }

        public static bool StartFocus(GameState state, FactionState f, string focusId, out string error)
        {
            var fo = FocusesOf(state, f).FirstOrDefault(x => x.id == focusId);
            if (!IsFocusAvailable(state, f, fo, out error)) return false;
            f.currentFocus = fo.id;
            f.focusTurnsLeft = Math.Max(1, fo.turns);
            state.AddLog("focus", $"{f.def.name} начинает фокус «{fo.name}»", f.id);
            return true;
        }

        public static void ProcessFocus(GameState state)
        {
            foreach (var f in state.Factions.Values.Where(x => x.alive))
            {
                if (string.IsNullOrEmpty(f.currentFocus)) continue;
                f.focusTurnsLeft--;
                if (f.focusTurnsLeft > 0) continue;
                var fo = state.Content.FocusById.TryGetValue(f.currentFocus, out var def) ? def : null;
                f.completedFocuses.Add(f.currentFocus);
                f.currentFocus = null;
                if (fo != null)
                {
                    EffectEngine.ApplyAll(fo.effects, new EffectContext { State = state, Source = f, TargetFaction = f });
                    state.AddLog("focus", $"{f.def.name} завершает фокус «{fo.name}»", f.id);
                    if (f.isPlayer) state.Notify($"Фокус выполнен: {fo.name}");
                }
            }
        }

        public static FocusDef CurrentFocusDef(GameState state, FactionState f)
        {
            if (string.IsNullOrEmpty(f.currentFocus)) return null;
            return state.Content.FocusById.TryGetValue(f.currentFocus, out var d) ? d : null;
        }

        // ------------------------------------------------------------------ советники
        public static bool UseAdvisor(GameState state, FactionState f, string advisorId, out string error)
        {
            error = null;
            var slot = f.advisors.FirstOrDefault(a => a.defId == advisorId);
            if (slot == null) { error = "Советник не нанят"; return false; }
            if (slot.cooldown > 0) { error = "Способность ещё не готова"; return false; }
            var def = state.Content.AdvisorById[advisorId];
            if (!EffectEngine.CanAfford(f, def.active.cost)) { error = "Недостаточно ресурсов"; return false; }
            EffectEngine.Pay(f, def.active.cost);
            EffectEngine.ApplyAll(def.active.effects, new EffectContext { State = state, Source = f, TargetFaction = f });
            slot.cooldown = def.active.cooldown;
            state.AddLog("advisor", $"{f.def.name}: советник {def.name} использует «{def.active.name}»", f.id);
            return true;
        }

        public static void ProcessAdvisorCooldowns(GameState state)
        {
            foreach (var f in state.Factions.Values)
                foreach (var a in f.advisors)
                    if (a.cooldown > 0) a.cooldown--;
        }
    }
}
