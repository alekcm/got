using System;
using System.Collections.Generic;
using System.Linq;
using IronThrone.Core.AI;

namespace IronThrone.Core.Game
{
    /// <summary>Ход игры: начало года, доход, тики систем, победы.</summary>
    public static class TurnEngine
    {
        public static void StartGame(GameState state)
        {
            foreach (var f in state.Factions.Values)
                EconomySystem.ApplyIncome(state, f);
            EconomySystem.RecalculateTradeDominance(state);
            state.Phase = GamePhase.PlayerTurn;
        }

        public static void EndTurn(GameState state)
        {
            if (state.Phase == GamePhase.GameOver) return;
            // действия ИИ
            foreach (var f in state.Factions.Values.Where(x => x.alive && !x.isPlayer).ToList())
                FactionAI.TakeTurn(state, f);

            MilitarySystem.TurnEnd(state);
            if (VictorySystem.Check(state)) return;
            AdvanceTurn(state);
        }

        public static void AdvanceTurn(GameState state)
        {
            state.Turn++;
            state.Year = state.Era.startYear + (state.Turn - 1) / Math.Max(1, state.Settings.turnsPerYear);
            state.Season = state.SeasonName(state.Turn);
            state.Phase = GamePhase.PlayerTurn;
            state.PendingNotifications.Clear();

            foreach (var f in state.Factions.Values.Where(x => x.alive))
            {
                EconomySystem.ApplyIncome(state, f);
                TickTimedModifiers(f);
            }
            EconomySystem.RecalculateTradeDominance(state);
            TerritorySystem.ProcessGrowth(state);
            EconomySystem.ProcessBuildQueues(state);
            ProgressSystem.ProcessResearch(state);
            ProgressSystem.ProcessFocus(state);
            ProgressSystem.ProcessAdvisorCooldowns(state);
            MilitarySystem.TurnStart(state);
            DiplomacySystem.ProcessTurn(state);
            PoliticsSystem.ProcessTurn(state);
            EspionageSystem.ProcessTurn(state);
            MinorHouseSystem.ProcessTurn(state);
            RaiderSystem.ProcessTurn(state);
            FaithSystem.ProcessTurn(state);
            EventSystem.ProcessTurn(state);
            TickProvinces(state);
            RecomputeAlive(state);
            VictorySystem.Check(state);

            if (state.Phase != GamePhase.GameOver)
                state.AddLog("system", $"--- Ход {state.Turn}: {YearLabel(state)} ---");
        }

        public static void TickTimedModifiers(FactionState f)
        {
            for (int i = f.timedMods.Count - 1; i >= 0; i--)
            {
                f.timedMods[i].turnsLeft--;
                if (f.timedMods[i].turnsLeft <= 0) f.timedMods.RemoveAt(i);
            }
        }

        private static void TickProvinces(GameState state)
        {
            foreach (var p in state.Provinces.Values)
            {
                if (p.raidCooldown > 0)
                {
                    p.raidCooldown--;
                    if (p.raidCooldown <= 0) p.raided = false;
                }
                p.turnsOwned++;
                // беспорядки: медленно спадают при стабильном правлении
                if (p.controller != null && p.unrest > 0)
                    p.unrest = Math.Max(0, p.unrest - 0.8f);
                // восстание в провинции
                if (p.controller != null && p.unrest >= state.Settings.unrestRevoltThreshold)
                {
                    if (state.Rng.NextDouble() < 0.25)
                    {
                        var owner = state.Faction(p.controller);
                        p.garrison = Math.Max(0, p.garrison - 30);
                        p.unrest = Math.Max(0, p.unrest - 20);
                        if (owner != null)
                        {
                            owner.gold = Math.Max(0, owner.gold - 40);
                            state.AddLog("revolt", $"Восстание в {p.def.name}: гарнизон ослаблен, казна опустошена", owner.id);
                            if (owner.isPlayer) state.Notify($"Восстание в {p.def.name}!");
                        }
                        // малый дом поднимает голову
                        if (p.house != null && state.MinorHouses.TryGetValue(p.house, out var mh))
                        {
                            mh.hostile = true;
                            mh.relation = Math.Max(-100, mh.relation - 10);
                        }
                    }
                }
                // рост развития/восстановление гарнизона
                if (p.garrison < MaxGarrison(p) && p.controller != null)
                    p.garrison = Math.Min(MaxGarrison(p), p.garrison + 4);
            }
        }

        public static int MaxGarrison(ProvinceState p)
        {
            int g = 20 + p.def.level * 18 + (p.def.isCapital ? 40 : 0);
            if (p.def.terrain == "mountains") g += 20;
            return g;
        }

        private static void RecomputeAlive(GameState state)
        {
            foreach (var f in state.Factions.Values)
            {
                if (!f.alive) continue;
                bool hasLand = state.Provinces.Values.Any(p => p.controller == f.id);
                bool hasArmy = f.armies.Any(id => state.Armies.TryGetValue(id, out var a) && a.alive && !a.IsEmpty);
                if (!hasLand && !hasArmy)
                {
                    f.alive = false;
                    state.AddLog("system", $"{f.def.name} уничтожен.", f.id);
                    state.Notify($"{f.def.name} уничтожен");
                }
            }
        }

        public static string YearLabel(GameState state)
        {
            int y = state.Year;
            string era = state.Era?.yearSuffix ?? "от З.Э.";
            if (y < 0) return $"{-y} г. до З.Э.";
            if (y == 0) return "1 г. " + era;
            return $"{y + 1} г. " + era;
        }

        public static string DateLabel(GameState state) => $"{state.Season}, {YearLabel(state)}";
    }
}
