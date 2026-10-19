using System;
using System.Collections.Generic;
using System.Linq;

namespace IronThrone.Core.Game
{
    public class VictoryProgress
    {
        public string faction;
        public string victoryType;
        public float value;
        public float target;
        public string description;
        public bool achieved;

        public float Percent => target <= 0 ? 0 : EffectEngine.Clamp(value / target * 100f, 0, 100);
    }

    /// <summary>Все условия победы: гегемония, политика, экономика, религия.</summary>
    public static class VictorySystem
    {
        public const int PoliticalSeatsRequired = 6;

        /// <summary>Сколько мест в совете нужно для политической победы (настройка или значение по умолчанию).</summary>
        public static int SeatsRequired(GameState state)
        {
            int need = state.Settings.victory.politicalSeats;
            return need > 0 ? need : PoliticalSeatsRequired;
        }

        public static List<VictoryProgress> Evaluate(GameState state, FactionState f)
        {
            var list = new List<VictoryProgress>();
            var v = state.Settings.victory;
            int total = Math.Max(1, state.Provinces.Count);
            int controlled = state.CountControlled(f.id);
            int capitals = state.Provinces.Values.Count(p => p.def.isCapital && p.controller == f.id);

            // Гегемония
            float share = controlled / (float)total;
            list.Add(new VictoryProgress
            {
                faction = f.id, victoryType = "hegemony",
                value = share * 100f, target = v.hegemonyProvinceShare * 100f,
                description = $"Провинции: {controlled}/{total} ({share:P0}), столицы регионов: {capitals}/{v.hegemonyRegionCapitals}",
                achieved = share >= v.hegemonyProvinceShare && capitals >= v.hegemonyRegionCapitals
            });

            // Политика
            list.Add(new VictoryProgress
            {
                faction = f.id, victoryType = "political",
                value = f.recognition,
                target = v.politicalRecognition,
                description = $"Признание: {f.recognition:0}/{v.politicalRecognition}; законов дома: {LawsSponsored(state, f.id)}/{v.politicalLaws}; мест в совете: {PoliticsSystem.SeatsBacking(state, f.id)}/{SeatsRequired(state)} " +
                              $"(своих {PoliticsSystem.SeatsOf(state, f.id)} из 3, союзных {PoliticsSystem.SeatsBacking(state, f.id) - PoliticsSystem.SeatsOf(state, f.id)})" +
                              (state.Council.throneHolder == f.id ? ", Железный трон ваш" : ", Железный трон не ваш") +
                              (PoliticsSystem.SeatsOf(state, f.id) > 3 ? " — лишние свои места не считаются" : ""),
                achieved = f.recognition >= v.politicalRecognition && state.Council.throneHolder == f.id &&
                           PoliticsSystem.SeatsOf(state, f.id) <= 3 &&
                           PoliticsSystem.SeatsBacking(state, f.id) >= SeatsRequired(state) &&
                           LawsSponsored(state, f.id) >= v.politicalLaws
            });

            // Экономика
            list.Add(new VictoryProgress
            {
                faction = f.id, victoryType = "economic",
                value = f.tradeDominance,
                target = v.economicDominance,
                description = $"Торговое господство: {f.tradeDominance:0}/{v.economicDominance}% (порты и рынки), торговых узлов: {f.tradeRoutePoints}/{v.economicRoutes}",
                achieved = f.tradeDominance >= v.economicDominance && f.tradeRoutePoints >= v.economicRoutes
            });

            // Религия: вера должна не только доминировать, но и обратить провинции,
            // которые начинали под другой верой — иначе дом с господствующей верой побеждал бы мгновенно.
            float faithShare = FaithSystem.FaithShare(state, f);
            int converted = state.Provinces.Values.Count(p => p.dominantFaith == f.faith &&
                                                             !string.IsNullOrEmpty(p.startFaith) && p.startFaith != f.faith);
            float startShare = 0f;
            state.FaithStartShare.TryGetValue(f.faith ?? "", out startShare);
            list.Add(new VictoryProgress
            {
                faction = f.id, victoryType = "religious",
                value = converted,
                target = v.religiousConversions,
                description = $"Вера «{state.FaithName(f.faith)}» доминирует в {faithShare:P0} провинций (с начала игры {startShare:P0}), " +
                              $"обращено из других вер: {converted}/{v.religiousConversions}" +
                              (FaithSystem.IsHead(state, f) ? ", вы — глава веры" : ", вы не глава веры") +
                              (f.HasFlag("faith_reformed") ? ", вера реформирована" : ", вера не реформирована"),
                achieved = faithShare >= v.religiousProvinceShare && converted >= v.religiousConversions &&
                           FaithSystem.IsHead(state, f) && f.HasFlag("faith_reformed")
            });

            return list;
        }

        /// <summary>Сколько действующих законов провёл дом — мера его политической воли.</summary>
        public static int LawsSponsored(GameState state, string factionId)
        {
            if (state == null || state.ActiveLaws == null) return 0;
            return state.ActiveLaws.Count(l => l != null && l.sponsor == factionId);
        }

        public static bool Check(GameState state)
        {
            if (state.Phase == GamePhase.GameOver) return true;
            var alive = state.Factions.Values.Where(f => f.alive).ToList();
            if (alive.Count == 1)
            {
                Declare(state, alive[0].id, "hegemony", "Последний дом, стоящий на ногах");
                return true;
            }
            foreach (var f in alive)
            {
                var progress = Evaluate(state, f);
                var win = progress.FirstOrDefault(p => p.achieved);
                if (win != null)
                {
                    Declare(state, f.id, win.victoryType, win.description);
                    return true;
                }
            }
            if (state.Turn > state.Settings.maxTurns) return DeclareScore(state);
            return false;
        }

        /// <summary>Победитель по очкам, когда лимит ходов исчерпан.</summary>
        public static bool DeclareScore(GameState state)
        {
            var alive = state.Factions.Values.Where(f => f.alive).ToList();
            if (alive.Count == 0) return false;
            var best = alive.OrderByDescending(f => Score(state, f)).First();
            Declare(state, best.id, "score", "Эпоха завершилась. Победитель определён по очкам.");
            return true;
        }

        public static void Declare(GameState state, string factionId, string type, string description)
        {
            state.VictoryWinner = factionId;
            state.VictoryType = type;
            state.Phase = GamePhase.GameOver;
            var name = state.FactionName(factionId);
            string typeRu = type switch
            {
                "hegemony" => "военная гегемония",
                "political" => "политическая победа",
                "economic" => "экономическая победа",
                "religious" => "религиозная победа",
                _ => "победа по очкам"
            };
            state.AddLog("victory", $"{name} одерживает победу ({typeRu}). {description}", factionId);
            state.Notify($"ПОБЕДА: {name} — {typeRu}");
        }

        public static int Score(GameState state, FactionState f)
        {
            int score = 0;
            score += state.CountControlled(f.id) * 12;
            score += state.Provinces.Values.Count(p => p.def.isCapital && p.controller == f.id) * 25;
            score += (int)f.recognition * 4;
            score += (int)f.tradeDominance * 2;
            score += (int)(FaithSystem.FaithShare(state, f) * 200);
            score += f.completedFocuses.Count * 15;
            score += f.techs.Count * 8;
            score += f.vassals.Count * 20;
            score += PoliticsSystem.SeatsOf(state, f.id) * 10;
            if (state.Council.throneHolder == f.id) score += 80;
            score += f.armies.Select(id => state.Armies.TryGetValue(id, out var a) ? MilitarySystem.EstimateStrength(a) : 0).Sum() / 3;
            return score;
        }

        public static string VictoryName(string type) => type switch
        {
            "hegemony" => "Гегемония",
            "political" => "Политическая победа",
            "economic" => "Экономическая победа",
            "religious" => "Религиозная победа",
            _ => "Победа по очкам"
        };
    }
}
