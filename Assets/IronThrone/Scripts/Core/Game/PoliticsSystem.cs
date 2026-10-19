using System;
using System.Collections.Generic;
using System.Linq;
using IronThrone.Core.Content;

namespace IronThrone.Core.Game
{
    /// <summary>
    /// Малый совет, Железный трон, законы и голосования. Аналог Лансраада из Dune: Spice Wars,
    /// но с титулами Железного трона. Влияние — валюта голосований.
    /// </summary>
    public static class PoliticsSystem
    {
        public const int CoronationRecognition = 10;

        public static int SeatsOf(GameState state, string factionId) =>
            state.Council.seats.Count(kv => kv.Value == factionId);

        public static int VotePower(GameState state, FactionState f)
        {
            if (f == null) return 0;
            int power = 3;
            power += SeatsOf(state, f.id) * 2;
            power += f.vassals.Count;
            if (state.Council.throneHolder == f.id) power += 5;
            power += (int)f.Mod(ModKeys.VotePower);
            power += f.minorRelations.Values.Count(r => r >= 60) / 3;
            return Math.Max(1, power);
        }

        public static int LobbyUnitCost(GameState state, FactionState buyer, FactionState target)
        {
            float rel = 0;
            if (buyer.relations.TryGetValue(target.id, out var r)) rel = r;
            float factor = 1f + Math.Max(0, 40 - rel) / 55f;
            factor *= buyer.ModPct(ModKeys.LobbyCostPct);
            return Math.Max(3, (int)Math.Round(state.Settings.voteLobbyCost * factor));
        }

        public static IEnumerable<LawDef> AvailableLaws(GameState state, FactionState f)
        {
            foreach (var law in state.Content.Laws)
            {
                if (law.eras.Count > 0 && !law.eras.Contains(state.Era.id)) continue;
                if (f.passedLaws.Contains(law.id) && !law.repealable) continue;
                if (state.ActiveLaws.Any(l => l.id == law.id)) continue;
                if (!string.IsNullOrEmpty(law.requiresTitle) && !f.HasTitle(state, law.requiresTitle)) continue;
                yield return law;
            }
        }

        public static bool HasTitle(this FactionState f, GameState state, string titleId)
        {
            return state.Council.seats.TryGetValue(titleId, out var holder) && holder == f.id;
        }

        public static bool CanPropose(GameState state, FactionState f, LawDef law, out string error)
        {
            error = null;
            if (state.Council.throneHolder == null) { error = "Железный трон пуст — сначала захватите Королевскую Гавань"; return false; }
            if (f.id != state.Council.throneHolder && SeatsOf(state, f.id) == 0)
            { error = "Нужен титул в Малом совете или Железный трон"; return false; }
            if (law.eras.Count > 0 && !law.eras.Contains(state.Era.id)) { error = "Закон не действует в эту эпоху"; return false; }
            if (!string.IsNullOrEmpty(law.requiresTitle) && !f.HasTitle(state, law.requiresTitle))
            { error = "Нужен титул: " + (state.Content.TitleById.TryGetValue(law.requiresTitle, out var t) ? t.name : law.requiresTitle); return false; }
            if (state.ActiveLaws.Any(l => l.id == law.id)) { error = "Этот закон уже действует"; return false; }
            if (!EffectEngine.CanAfford(f, new ResourceCost { influence = law.voteCost }))
            { error = "Нужно влияние: " + law.voteCost; return false; }
            return true;
        }

        public static LawVoteState ProposeLaw(GameState state, FactionState sponsor, string lawId, out string error)
        {
            error = null;
            var law = state.Content.LawById.TryGetValue(lawId, out var l) ? l : null;
            if (law == null) { error = "Неизвестный закон"; return null; }
            if (!CanPropose(state, sponsor, law, out error)) return null;

            EffectEngine.Pay(sponsor, new ResourceCost { influence = law.voteCost });
            var vote = new LawVoteState
            {
                lawId = lawId,
                sponsor = sponsor.id,
                openedTurn = state.Turn,
            };
            state.PendingVote = vote;
            state.AddLog("politics", $"{sponsor.def.name} выносит на совет закон «{law.name}»", sponsor.id);
            if (!sponsor.isPlayer) ResolveVote(state, autoPlayer: true);
            return vote;
        }

        public static bool Lobby(GameState state, FactionState buyer, string targetFactionId, int influenceSpent, out string error)
        {
            error = null;
            if (state.PendingVote == null) { error = "Нет активного голосования"; return false; }
            if (buyer == null || !state.Factions.TryGetValue(targetFactionId, out var target)) { error = "Нет такой фракции"; return false; }
            if (!EffectEngine.CanAfford(buyer, new ResourceCost { influence = influenceSpent })) { error = "Недостаточно влияния"; return false; }
            EffectEngine.Pay(buyer, new ResourceCost { influence = influenceSpent });
            int unit = LobbyUnitCost(state, buyer, target);
            float votes = influenceSpent / (float)unit;
            if (!state.PendingVote.lobby.ContainsKey(targetFactionId)) state.PendingVote.lobby[targetFactionId] = 0;
            state.PendingVote.lobby[targetFactionId] += votes;
            state.AddLog("politics", $"{buyer.def.name} лоббирует {target.def.name} ({influenceSpent} влияния)", buyer.id);
            return true;
        }

        public static void SetPlayerVote(GameState state, string choice)
        {
            if (state.PendingVote == null || state.PlayerFaction == null) return;
            state.PendingVote.votedFor[state.PlayerFaction] = choice;
        }

        public static string ResolveVote(GameState state, bool autoPlayer = false)
        {
            var vote = state.PendingVote;
            if (vote == null) return null;
            var law = state.Content.LawById.TryGetValue(vote.lawId, out var l) ? l : null;
            if (law == null) { state.PendingVote = null; return "Закон не найден"; }

            vote.yes = 0; vote.no = 0;
            var details = new List<string>();
            foreach (var f in state.Factions.Values.Where(x => x.alive))
            {
                string choice;
                if (vote.votedFor.TryGetValue(f.id, out var forced)) choice = forced;
                else choice = DecideVote(state, f, law, vote) ? "yes" : "no";
                if (f.isPlayer && autoPlayer) choice = vote.votedFor.TryGetValue(f.id, out var pc) ? pc : (DecideVote(state, f, law, vote) ? "yes" : "no");
                int weight = VotePower(state, f);
                if (vote.lobby.TryGetValue(f.id, out var lob)) weight += (int)Math.Floor(lob);
                vote.votes[f.id] = weight;
                vote.votedFor[f.id] = choice;
                if (choice == "yes") vote.yes += weight; else vote.no += weight;
                details.Add($"{f.def.shortName ?? f.def.name}: {(choice == "yes" ? "за" : "против")} ({weight})");
            }

            bool passed = vote.yes > vote.no;
            vote.resolved = true;
            vote.resultText = $"Голосование по закону «{law.name}»: за {vote.yes}, против {vote.no} — " +
                              (passed ? "принят" : "отклонён");
            state.AddLog("politics", vote.resultText, vote.sponsor);
            state.AddLog("politics", "Голоса: " + string.Join("; ", details));

            if (passed)
            {
                ApplyLaw(state, law, vote.sponsor);
            }
            else
            {
                var sponsor = state.Faction(vote.sponsor);
                if (sponsor != null)
                {
                    sponsor.influence = Math.Max(0, sponsor.influence - 5);
                    sponsor.recognition = Math.Max(0, sponsor.recognition - 2);
                }
            }
            if (state.Player != null) state.Notify(vote.resultText);
            state.Council.lastAgenda = law.id;
            state.Council.lastAgendaTurn = state.Turn;
            state.Council.sessionsHeld++;
            state.PendingVote = null;
            return vote.resultText;
        }

        public static bool DecideVote(GameState state, FactionState voter, LawDef law, LawVoteState vote)
        {
            if (voter.id == vote.sponsor) return true;
            float score = 0;
            if (voter.relations.TryGetValue(vote.sponsor, out var rel)) score += rel * 0.45f;
            if (state.Council.throneHolder == vote.sponsor) score += 8;
            if (state.Council.throneHolder == voter.id) score += 4;
            // собственная выгода от закона
            foreach (var e in law.effects)
            {
                bool selfTarget = string.IsNullOrEmpty(e.target) || e.target == "self";
                switch ((e.op ?? "").ToLowerInvariant())
                {
                    case "resource":
                    case "recognition":
                        if (selfTarget && (e.amount > 0 || e.value > 0)) score += 7;
                        if (selfTarget && (e.amount < 0 || e.value < 0)) score -= 7;
                        break;
                    case "modifier":
                        if (e.value > 0) score += 4; else score -= 4;
                        break;
                    case "unrest":
                        if (e.amount < 0) score += 5; else score -= 5;
                        break;
                    case "title":
                        score -= 6;
                        break;
                }
            }
            if (law.effects.Any(e => e.op == "recognition" && (e.target == null || e.target == "self")))
                score -= 3; // никто не хочет усиливать чужое признание
            if (voter.def.house == "stark" && law.id.Contains("slavery")) score -= 20;
            score += state.Rng.Next(-8, 9);
            return score >= 0;
        }

        public static void ApplyLaw(GameState state, LawDef law, string sponsorId)
        {
            var sponsor = state.Faction(sponsorId);
            state.ActiveLaws.Add(new LawState { id = law.id, sponsor = sponsorId, passedTurn = state.Turn });
            // realm-wide эффекты: применяются ко всем живым фракциям
            foreach (var f in state.Factions.Values.Where(x => x.alive))
            {
                var ctx = new EffectContext { State = state, Source = f, TargetFaction = f };
                EffectEngine.ApplyAll(law.effects, ctx);
            }
            if (sponsor != null)
            {
                sponsor.passedLaws.Add(law.id);
                sponsor.recognition = Math.Min(100, sponsor.recognition + 3.5f * (1f + sponsor.Mod(ModKeys.RecognitionGain)));
                sponsor.lawsProposed++;
                sponsor.influence += 4;
            }
            state.AddLog("politics", $"Закон «{law.name}» принят Малым советом. Спонсор: {state.FactionName(sponsorId)}", sponsorId);
            if (state.Player != null) state.Notify($"Закон принят: {law.name}");
        }

        // ------------------------------------------------------------------ титулы
        public static bool GrantTitle(GameState state, string titleId, string factionId, out string error)
        {
            error = null;
            if (!state.Content.TitleById.TryGetValue(titleId, out var title)) { error = "Нет такого титула"; return false; }
            var f = state.Faction(factionId);
            if (f == null) { error = "Нет такой фракции"; return false; }
            string holder = state.Council.throneHolder;
            var actor = state.Player;
            if (actor == null) return false;
            if (actor.id != holder) { error = "Раздавать титулы может только владелец Железного трона"; return false; }
            if (SeatsOf(state, actor.id) == 0 && actor.id != holder) { error = "Недостаточно власти"; return false; }
            var cost = new ResourceCost { influence = 25 };
            if (!EffectEngine.CanAfford(actor, cost)) { error = "Нужно 25 влияния"; return false; }

            if (state.Council.seats.TryGetValue(titleId, out var oldHolder) && oldHolder != factionId)
                RemoveTitleEffects(state, title, oldHolder);

            EffectEngine.Pay(actor, cost);
            state.Council.seats[titleId] = factionId;
            if (titleId == "hand") state.Council.hand = factionId;
            ApplyTitleEffects(state, title, f);
            // благодарность
            if (f.relations.ContainsKey(actor.id))
                f.relations[actor.id] = Math.Min(100, f.relations[actor.id] + 15);
            state.AddLog("politics", $"{actor.def.name} жалует титул «{title.name}» дому {f.def.name}", actor.id);
            return true;
        }

        private static void ApplyTitleEffects(GameState state, TitleDef title, FactionState f)
        {
            foreach (var e in title.effects)
            {
                if (e.op != "modifier" && e.op != "timedmodifier") continue;
                f.permanentMods.TryGetValue(e.key, out var cur);
                f.permanentMods[e.key] = cur + e.value;
            }
        }

        private static void RemoveTitleEffects(GameState state, TitleDef title, string factionId)
        {
            var f = state.Faction(factionId);
            if (f == null) return;
            foreach (var e in title.effects)
            {
                if (e.op != "modifier" && e.op != "timedmodifier") continue;
                f.permanentMods.TryGetValue(e.key, out var cur);
                f.permanentMods[e.key] = cur - e.value;
            }
        }

        public static void OnThroneCaptured(GameState state, string factionId)
        {
            if (state.Council.throneHolder == factionId) return;
            var newHolder = state.Faction(factionId);
            if (newHolder == null) return;
            var old = state.Faction(state.Council.throneHolder);
            state.Council.throneHolder = factionId;
            state.Council.hand = factionId;
            newHolder.recognition = Math.Min(100, newHolder.recognition + CoronationRecognition);
            newHolder.influence += 15;
            state.AddLog("politics", $"{newHolder.def.name} занимает Железный трон! Коронация.", factionId);
            state.Notify($"{newHolder.def.name} захватывает Железный трон!");
            if (old != null)
            {
                old.recognition = Math.Max(0, old.recognition - 10);
                foreach (var key in state.Council.seats.Where(kv => kv.Value == old.id).Select(kv => kv.Key).ToList())
                    state.Council.seats[key] = factionId;
            }
        }

        public static void ProcessTurn(GameState state)
        {
            foreach (var f in state.Factions.Values.Where(x => x.alive))
            {
                if (state.Council.throneHolder == f.id)
                    f.recognition = Math.Min(100, f.recognition + 0.55f * (1f + f.Mod(ModKeys.RecognitionGain)));
                else
                    f.recognition = Math.Max(0, f.recognition - 0.35f);
                f.recognition = Math.Min(100, f.recognition + SeatsOf(state, f.id) * 0.12f);
            }
            // контроль над Королевской Гаванью = трон
            var kl = state.Province("kings_landing");
            if (kl?.controller != null && kl.controller != state.Council.throneHolder)
            {
                if (state.Council.throneHolder == null || kl.controller != state.Council.throneHolder)
                    OnThroneCaptured(state, kl.controller);
            }
            // AI сам выносит законы
            foreach (var f in state.Factions.Values.Where(x => x.alive && !x.isPlayer))
            {
                if (state.PendingVote != null) break;
                if (state.Turn - state.Council.lastAgendaTurn < 5) break;
                if (state.Rng.NextDouble() > 0.22) continue;
                var laws = AvailableLaws(state, f).ToList();
                if (laws.Count == 0) continue;
                var law = laws[state.Rng.Next(laws.Count)];
                ProposeLaw(state, f, law.id, out _);
            }
        }
    }
}
