using System;
using System.Collections.Generic;
using System.Linq;
using IronThrone.Core.Content;

namespace IronThrone.Core.Game
{
    /// <summary>
    /// Религия в духе Civilization: вера, распространение, десятина, святыни и религиозная победа.
    /// </summary>
    public static class FaithSystem
    {
        public static void ApplyFaithModifiers(GameState state, FactionState f, string faithId, bool remove = false)
        {
            if (!state.Content.FaithById.TryGetValue(faithId ?? "", out var faith)) return;
            float sign = remove ? -1 : 1;
            foreach (var tenet in faith.tenets)
            {
                foreach (var m in tenet.modifiers)
                {
                    f.permanentMods.TryGetValue(m.key, out var cur);
                    f.permanentMods[m.key] = cur + sign * m.value;
                }
            }
        }

        public static bool DeclareFaith(GameState state, FactionState f, string faithId, out string error)
        {
            error = null;
            if (!state.Content.FaithById.TryGetValue(faithId, out var faith)) { error = "Неизвестная вера"; return false; }
            if (faithId == f.faith) { error = "Вы уже исповедуете эту веру"; return false; }
            if (!faith.playable) { error = "Эту веру нельзя принять"; return false; }
            var cost = new ResourceCost(0, 0, 0, 30, 40);
            if (!EffectEngine.CanAfford(f, cost)) { error = "Нужно 30 влияния и 40 веры"; return false; }
            EffectEngine.Pay(f, cost);
            ApplyFaithModifiers(state, f, f.faith, remove: true);
            f.faith = faithId;
            ApplyFaithModifiers(state, f, faithId);
            foreach (var p in state.Provinces.Values.Where(p => p.controller == f.id))
                p.unrest = Math.Min(100, p.unrest + 12);
            state.AddLog("faith", $"{f.def.name} объявляет о переходе в веру «{faith.name}»", f.id);
            if (f.isPlayer) state.Notify($"Вера: {faith.name}. В провинциях растёт беспокойство.");
            return true;
        }

        public static bool SendMissionary(GameState state, FactionState f, string provinceId, out string error)
        {
            error = null;
            var p = state.Province(provinceId);
            if (p == null) { error = "Нет такой провинции"; return false; }
            var cost = new ResourceCost(20, 0, 0, 0, 15);
            if (!EffectEngine.CanAfford(f, cost)) { error = "Нужно 20 золота и 15 веры"; return false; }
            EffectEngine.Pay(f, cost);
            float spread = 8f * (1f + f.Mod(ModKeys.FaithSpread));
            EffectEngine.ShiftFaith(p, f.faith, spread / 100f);
            p.unrest = Math.Min(100, p.unrest + (p.controller == f.id ? 1 : 6));
            state.AddLog("faith", $"{f.def.name}: миссионеры в {p.def.name} (+{spread:0}% веры)", f.id);
            return true;
        }

        public static bool ReformFaith(GameState state, FactionState f, out string error)
        {
            error = null;
            if (f.HasFlag("faith_reformed")) { error = "Вера уже реформирована"; return false; }
            if (!state.Content.FaithById.TryGetValue(f.faith ?? "", out var faith)) { error = "Нет веры"; return false; }
            if (!IsHead(state, f)) { error = "Нужно контролировать святыню веры: " + faith.headProvince; return false; }
            var cost = new ResourceCost(0, 0, 0, 0, 120);
            if (!EffectEngine.CanAfford(f, cost)) { error = "Нужно 120 веры"; return false; }
            EffectEngine.Pay(f, cost);
            f.SetFlag("faith_reformed");
            f.recognition = Math.Min(100, f.recognition + 8);
            f.permanentMods.TryGetValue(ModKeys.FaithSpread, out var cur);
            f.permanentMods[ModKeys.FaithSpread] = cur + 0.35f;
            state.AddLog("faith", $"{f.def.name} реформирует веру «{faith.name}». Теперь вы — глава церкви.", f.id);
            state.Notify($"Вера реформирована: {faith.name}");
            return true;
        }

        public static bool IsHead(GameState state, FactionState f)
        {
            if (string.IsNullOrEmpty(f.faith)) return false;
            if (!state.Content.FaithById.TryGetValue(f.faith, out var faith)) return false;
            if (string.IsNullOrEmpty(faith.headProvince)) return f.HasFlag("faith_reformed");
            var p = state.Province(faith.headProvince);
            return p?.controller == f.id;
        }

        public static float FaithShare(GameState state, FactionState f)
        {
            if (state.Provinces.Count == 0) return 0;
            int count = state.Provinces.Values.Count(p => p.dominantFaith == f.faith);
            return count / (float)state.Provinces.Count;
        }

        public static void ProcessTurn(GameState state)
        {
            foreach (var p in state.Provinces.Values)
            {
                var owner = state.Faction(p.controller);
                if (owner == null) continue;
                if (!state.Content.FaithById.TryGetValue(owner.faith ?? "", out var faith)) continue;

                // храмы и святыни усиливают веру владельца
                int temples = p.buildings.Count(b => b == "sept" || b == "valyrian_shrine" || b == "godswood" || b == "drowned_shrine" || b == "fire_temple" || b == "monastery");
                float spread = (0.5f + temples * 0.9f) * (1f + owner.Mod(ModKeys.FaithSpread)) * faith.spread;
                if (p.def.isCapital) spread += 0.4f;
                if (faith.holySites.Contains(p.id)) spread += 1.2f;
                if (!string.IsNullOrEmpty(owner.currentFocus) && state.Content.FocusById.TryGetValue(owner.currentFocus, out var fo) &&
                    fo.id.Contains("faith")) spread += 0.5f;

                float current = p.faith.TryGetValue(owner.faith, out var cur) ? cur : 0;
                if (current < 92f && spread > 0)
                    EffectEngine.ShiftFaith(p, owner.faith, spread / 100f);

                // беспорядки от религиозного раздора
                if (p.dominantFaith != owner.faith && current < 25)
                    p.unrest = Math.Min(100, p.unrest + 1);
                if (p.dominantFaith == owner.faith)
                    p.unrest = Math.Max(0, p.unrest - 0.6f);
            }
            // святыни дают бонусы владельцам, даже если вера не государственная
            foreach (var faith in state.Content.Faiths)
            {
                if (string.IsNullOrEmpty(faith.headProvince)) continue;
                var p = state.Province(faith.headProvince);
                if (p?.controller == null) continue;
                var owner = state.Faction(p.controller);
                if (owner == null) continue;
                owner.influence += 1;
                if (faith.id == owner.faith) owner.piety += 2;
            }
        }

        public static IEnumerable<(string faith, float share, bool isPlayerFaith)> FaithBreakdown(GameState state, FactionState f)
        {
            var groups = state.Provinces.Values
                .Where(p => p.dominantFaith != null)
                .GroupBy(p => p.dominantFaith)
                .OrderByDescending(g => g.Count())
                .ToList();
            foreach (var g in groups)
                yield return (g.Key, g.Count() / (float)Math.Max(1, state.Provinces.Count), g.Key == f.faith);
        }
    }
}
