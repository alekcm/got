using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IronThrone.Core.Content;

namespace IronThrone.Core.Game
{
    // ------------------------------------------------------------------ DTO сохранения
    public class SaveData
    {
        public int version = 1;
        public string era;
        public string playerFaction;
        public string mapMode = "canonical";
        public int seed;
        public int turn;
        public int year;
        public string season;
        public string phase;
        public string victoryWinner;
        public string victoryType;
        public int nextArmyId = 1;
        public int nextCampId = 1;
        public List<FactionSave> factions = new List<FactionSave>();
        public List<ProvinceSave> provinces = new List<ProvinceSave>();
        public List<ArmySave> armies = new List<ArmySave>();
        public List<HeroSave> heroes = new List<HeroSave>();
        public List<MinorHouseSave> minorHouses = new List<MinorHouseSave>();
        public List<MinorRequestSave> minorRequests = new List<MinorRequestSave>();
        public List<CampSave> camps = new List<CampSave>();
        public List<QuestSave> quests = new List<QuestSave>();
        public List<LawSave> laws = new List<LawSave>();
        public List<WarSave> wars = new List<WarSave>();
        public Dictionary<string, int> truces = new Dictionary<string, int>();
        public Dictionary<string, int> pacts = new Dictionary<string, int>();
        public string coalitionTarget;
        public int coalitionUntil;
        public Dictionary<string, float> faithStartShare = new Dictionary<string, float>();
        public List<ScheduledEventSave> scheduledEvents = new List<ScheduledEventSave>();
        public CouncilSave council = new CouncilSave();
        public List<string> firedEvents = new List<string>();
        public Dictionary<string, int> eventHistory = new Dictionary<string, int>();
        public List<LogSave> log = new List<LogSave>();
    }

    public class FactionSave
    {
        public string id;
        public bool alive = true;
        public string capital;
        public int gold, materials, food, influence, piety;
        public int tradeRoutePoints, spyNetwork, counterspy;
        public float growth;
        public bool growthReady;
        public int claimedProvinces;
        public float recognition, tradeDominance, faithPoints;
        public string faith;
        public List<string> techs = new List<string>();
        public Dictionary<string, int> research = new Dictionary<string, int>();
        public string currentResearch;
        public List<AdvisorSave> advisors = new List<AdvisorSave>();
        public List<string> armies = new List<string>();
        public List<string> heroes = new List<string>();
        public Dictionary<string, float> relations = new Dictionary<string, float>();
        public Dictionary<string, int> minorRelations = new Dictionary<string, int>();
        public Dictionary<string, int> vassals = new Dictionary<string, int>();
        public List<string> annexed = new List<string>();
        public Dictionary<string, float> permanentMods = new Dictionary<string, float>();
        public List<TimedModSave> timedMods = new List<TimedModSave>();
        public List<string> flags = new List<string>();
        public Dictionary<string, float> counters = new Dictionary<string, float>();
        public string currentFocus;
        public int focusTurnsLeft;
        public List<string> completedFocuses = new List<string>();
        public List<string> focusQueue = new List<string>();
        public List<string> passedLaws = new List<string>();
        public int lawsProposed;
        public List<string> marriages = new List<string>();
        public string dogma;
        public List<string> questStates = new List<string>();
        public int lastEventTurn = -1;
        public int score;
        public List<IntelSave> intel = new List<IntelSave>();
    }

    public class AdvisorSave { public string defId; public int cooldown; }
    public class ScheduledEventSave { public int turn; public string eventId; public string faction; }
    public class TimedModSave { public string key; public float value; public int turnsLeft; public string source; }

    public class ProvinceSave
    {
        public string id;
        public string owner, controller, house, dominantFaith, startFaith, besieger;
        public List<string> buildings = new List<string>();
        public Dictionary<string, int> buildQueue = new Dictionary<string, int>();
        public float unrest, development;
        public int garrison, raidCooldown, siegeProgress, turnsOwned;
        public bool raided;
        public Dictionary<string, float> faith = new Dictionary<string, float>();
        public List<string> flags = new List<string>();
    }

    public class UnitSave
    {
        public string defId, name;
        public float hp, morale;
        public int xp, level, upgradeLevel;
        public bool alive;
    }

    public class ArmySave
    {
        public string id, faction, name, province, heroId, stance, originProvince;
        public List<UnitSave> units = new List<UnitSave>();
        public List<string> destroyedUnits = new List<string>();
        public int movementLeft, maxMovement, supply, turnsInEnemyLand;
        public bool alive, movedThisTurn, seaTravel;
    }

    public class HeroSave
    {
        public string defId, name, faction, armyId;
        public int level, xp, woundedTurns, abilityCooldown, reassignCooldown;
        public bool alive, wounded;
        public Dictionary<string, int> cooldowns = new Dictionary<string, int>();
    }

    /// <summary>Донесение разведки в сохранении.</summary>
    public class IntelSave
    {
        public string factionId;
        public int turn, gold, food, materials, influence;
        public int provinces, armies, units, strength, techs;
        public float recognition;
        public string capital;
        public List<string> armyLines = new List<string>();
        public List<string> wars = new List<string>();
        public List<string> lastTechs = new List<string>();
    }

    public class MinorRequestSave
    {
        public string house, faction, type;
        public int turnsLeft;
    }

    public class MinorHouseSave
    {
        public string house;
        public int relation;
        public string vassalOf;
        public bool annexed, hostile;
        public int tributeTurnsLeft, lastActionTurn;
    }

    public class CampSave
    {
        public string id, name, province, kind;
        public int strength, respawnTurns;
        public bool cleared;
    }

    public class QuestSave
    {
        public string id, faction;
        public int step, progress;
        public bool completed, failed;
    }

    public class LawSave { public string id, sponsor; public int passedTurn; }

    public class WarSave
    {
        public string attacker, defender, aggressor;
        public int startTurn, turns, attackerBattles, defenderBattles;
        public bool active = true;
    }

    public class CouncilSave
    {
        public Dictionary<string, string> seats = new Dictionary<string, string>();
        public string throneHolder, hand, lastAgenda;
        public int sessionsHeld, lastAgendaTurn;
    }

    public class LogSave { public int turn; public string category, text, faction; }

    // ------------------------------------------------------------------ система
    public static class SaveSystem
    {
        public const int Version = 1;

        public static void SaveToFile(GameState state, string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonWriter.Write(Capture(state)));
        }

        public static GameState LoadFromFile(ContentDb content, string path)
        {
            var json = JsonValue.Parse(File.ReadAllText(path));
            var data = JsonMapper.ToObject<SaveData>(json);
            return Restore(content, data);
        }

        // ------------------------------------------------------------------ захват
        public static SaveData Capture(GameState state)
        {
            var d = new SaveData
            {
                era = state.Era.id,
                playerFaction = state.PlayerFaction,
                mapMode = state.CustomMap != null ? "random" : "canonical",
                seed = state.Seed,
                turn = state.Turn,
                year = state.Year,
                season = state.Season,
                phase = state.Phase.ToString(),
                victoryWinner = state.VictoryWinner,
                victoryType = state.VictoryType,
                nextArmyId = state.NextArmyId,
                nextCampId = state.NextCampId,
                council = new CouncilSave
                {
                    seats = new Dictionary<string, string>(state.Council.seats),
                    throneHolder = state.Council.throneHolder,
                    hand = state.Council.hand,
                    lastAgenda = state.Council.lastAgenda,
                    sessionsHeld = state.Council.sessionsHeld,
                    lastAgendaTurn = state.Council.lastAgendaTurn,
                },
                truces = new Dictionary<string, int>(state.Truces),
                pacts = new Dictionary<string, int>(state.Pacts),
                coalitionTarget = state.CoalitionTarget,
                coalitionUntil = state.CoalitionUntil,
                faithStartShare = new Dictionary<string, float>(state.FaithStartShare),
                scheduledEvents = state.ScheduledEvents
                    .Select(x => new ScheduledEventSave { turn = x.turn, eventId = x.eventId, faction = x.faction })
                    .ToList(),
                eventHistory = new Dictionary<string, int>(state.eventHistory),
                firedEvents = state.firedEvents.ToList(),
            };

            foreach (var f in state.Factions.Values)
            {
                var fs = new FactionSave
                {
                    id = f.id, alive = f.alive, capital = f.capital,
                    gold = f.gold, materials = f.materials, food = f.food,
                    influence = f.influence, piety = f.piety,
                    tradeRoutePoints = f.tradeRoutePoints, spyNetwork = f.spyNetwork, counterspy = f.counterspy,
                    growth = f.growth, growthReady = f.growthReady, claimedProvinces = f.claimedProvinces,
                    recognition = f.recognition, tradeDominance = f.tradeDominance,
                    faith = f.faith, faithPoints = f.faithPoints,
                    techs = f.techs.ToList(),
                    research = new Dictionary<string, int>(f.research),
                    currentResearch = f.currentResearch,
                    armies = f.armies.ToList(),
                    heroes = f.heroes.ToList(),
                    relations = new Dictionary<string, float>(f.relations),
                    minorRelations = new Dictionary<string, int>(f.minorRelations),
                    vassals = new Dictionary<string, int>(f.vassals),
                    annexed = f.annexed.ToList(),
                    permanentMods = new Dictionary<string, float>(f.permanentMods),
                    flags = f.flags.ToList(),
                    counters = new Dictionary<string, float>(f.counters),
                    currentFocus = f.currentFocus,
                    focusTurnsLeft = f.focusTurnsLeft,
                    completedFocuses = f.completedFocuses.ToList(),
                    focusQueue = f.focusQueue.ToList(),
                    passedLaws = f.passedLaws.ToList(),
                    lawsProposed = f.lawsProposed,
                    marriages = f.marriages.ToList(),
                    dogma = f.dogma,
                    questStates = f.questStates.ToList(),
                    lastEventTurn = f.lastEventTurn,
                    score = f.score,
                };
                foreach (var a in f.advisors) fs.advisors.Add(new AdvisorSave { defId = a.defId, cooldown = a.cooldown });
                foreach (var t in f.timedMods) fs.timedMods.Add(new TimedModSave { key = t.key, value = t.value, turnsLeft = t.turnsLeft, source = t.source });
                foreach (var kv in f.intel)
                {
                    var it = kv.Value;
                    if (it == null) continue;
                    fs.intel.Add(new IntelSave
                    {
                        factionId = it.factionId, turn = it.turn,
                        gold = it.gold, food = it.food, materials = it.materials, influence = it.influence,
                        provinces = it.provinces, armies = it.armies, units = it.units, strength = it.strength,
                        techs = it.techs, recognition = it.recognition, capital = it.capital,
                        armyLines = new List<string>(it.armyLines), wars = new List<string>(it.wars),
                        lastTechs = new List<string>(it.lastTechs),
                    });
                }
                d.factions.Add(fs);
            }

            foreach (var p in state.Provinces.Values)
            {
                var ps = new ProvinceSave
                {
                    id = p.id, owner = p.owner, controller = p.controller, house = p.house,
                    dominantFaith = p.dominantFaith, startFaith = p.startFaith, besieger = p.besieger,
                    buildings = p.buildings.ToList(),
                    buildQueue = new Dictionary<string, int>(p.buildQueue),
                    unrest = p.unrest, development = p.development,
                    garrison = p.garrison, raidCooldown = p.raidCooldown,
                    siegeProgress = p.siegeProgress, turnsOwned = p.turnsOwned,
                    raided = p.raided,
                    faith = new Dictionary<string, float>(p.faith),
                    flags = p.flags.ToList(),
                };
                d.provinces.Add(ps);
            }

            foreach (var a in state.Armies.Values)
            {
                var aSave = new ArmySave
                {
                    id = a.id, faction = a.faction, name = a.name, province = a.province, heroId = a.heroId,
                    stance = a.stance, originProvince = a.originProvince,
                    destroyedUnits = a.destroyedUnits.ToList(),
                    movementLeft = a.movementLeft, maxMovement = a.maxMovement, supply = a.supply,
                    turnsInEnemyLand = a.turnsInEnemyLand,
                    alive = a.alive, movedThisTurn = a.movedThisTurn, seaTravel = a.seaTravel,
                };
                foreach (var u in a.units)
                    aSave.units.Add(new UnitSave { defId = u.defId, name = u.name, hp = u.hp, morale = u.morale, xp = u.xp, level = u.level, upgradeLevel = u.upgradeLevel, alive = u.alive });
                d.armies.Add(aSave);
            }

            foreach (var h in state.Heroes.Values)
            {
                d.heroes.Add(new HeroSave
                {
                    defId = h.defId, name = h.name, faction = h.faction, armyId = h.armyId,
                    level = h.level, xp = h.xp, woundedTurns = h.woundedTurns, abilityCooldown = h.abilityCooldown,
                    reassignCooldown = h.reassignCooldown,
                    alive = h.alive, wounded = h.wounded,
                    cooldowns = new Dictionary<string, int>(h.cooldowns),
                });
            }

            foreach (var req in state.MinorRequests)
                d.minorRequests.Add(new MinorRequestSave { house = req.house, faction = req.faction, type = req.type, turnsLeft = req.turnsLeft });

            foreach (var m in state.MinorHouses.Values)
                d.minorHouses.Add(new MinorHouseSave
                {
                    house = m.house, relation = m.relation, vassalOf = m.vassalOf, annexed = m.annexed,
                    hostile = m.hostile, tributeTurnsLeft = m.tributeTurnsLeft, lastActionTurn = m.lastActionTurn,
                });

            foreach (var c in state.RaiderCamps)
                d.camps.Add(new CampSave
                {
                    id = c.id, name = c.name, province = c.province, kind = c.kind,
                    strength = c.strength, respawnTurns = c.respawnTurns, cleared = c.cleared,
                });

            foreach (var q in state.Quests)
                d.quests.Add(new QuestSave { id = q.id, faction = q.faction, step = q.step, progress = q.progress, completed = q.completed, failed = q.failed });

            foreach (var l in state.ActiveLaws)
                d.laws.Add(new LawSave { id = l.id, sponsor = l.sponsor, passedTurn = l.passedTurn });

            foreach (var w in state.Wars)
                d.wars.Add(new WarSave
                {
                    attacker = w.attacker, defender = w.defender, aggressor = w.aggressor,
                    startTurn = w.startTurn, turns = w.turns, attackerBattles = w.attackerBattles,
                    defenderBattles = w.defenderBattles, active = w.active,
                });

            foreach (var log in state.Log.Skip(Math.Max(0, state.Log.Count - 300)))
                d.log.Add(new LogSave { turn = log.turn, category = log.category, text = log.text, faction = log.faction });

            return d;
        }

        // ------------------------------------------------------------------ восстановление
        public static GameState Restore(ContentDb content, SaveData d)
        {
            var era = content.Eras.FirstOrDefault(e => e.id == d.era)
                      ?? throw new InvalidOperationException("Неизвестная эпоха в сохранении: " + d.era);

            var state = new GameState
            {
                Content = content,
                Era = era,
                Seed = d.seed,
                Rng = new Random(d.seed * 31 + d.turn * 7919),
                Turn = d.turn,
                Year = d.year,
                Season = d.season,
                PlayerFaction = d.playerFaction,
                Phase = ParsePhase(d.phase),
                VictoryWinner = d.victoryWinner,
                VictoryType = d.victoryType,
                NextArmyId = d.nextArmyId,
                NextCampId = d.nextCampId,
            };
            if (d.mapMode == "random")
                state.CustomMap = MapGenerator.Generate(content.Map, d.seed);

            foreach (var def in state.Map.provinces)
                state.Provinces[def.id] = new ProvinceState { id = def.id, def = def, house = def.house };

            foreach (var fs in d.factions)
            {
                if (!content.FactionById.TryGetValue(fs.id, out var def)) continue;
                var f = new FactionState
                {
                    id = fs.id, def = def, alive = fs.alive, capital = fs.capital,
                    house = def.house, isPlayer = fs.id == d.playerFaction,
                    gold = fs.gold, materials = fs.materials, food = fs.food,
                    influence = fs.influence, piety = fs.piety,
                    tradeRoutePoints = fs.tradeRoutePoints, spyNetwork = fs.spyNetwork, counterspy = fs.counterspy,
                    growth = fs.growth, growthReady = fs.growthReady, claimedProvinces = fs.claimedProvinces,
                    recognition = fs.recognition, tradeDominance = fs.tradeDominance,
                    faith = fs.faith, faithPoints = fs.faithPoints,
                    techs = new HashSet<string>(fs.techs),
                    research = new Dictionary<string, int>(fs.research),
                    currentResearch = fs.currentResearch,
                    armies = new List<string>(fs.armies),
                    heroes = new List<string>(fs.heroes),
                    relations = new SortedDictionary<string, float>(fs.relations),
                    minorRelations = new SortedDictionary<string, int>(fs.minorRelations),
                    vassals = new SortedDictionary<string, int>(fs.vassals),
                    annexed = new HashSet<string>(fs.annexed),
                    permanentMods = new SortedDictionary<string, float>(fs.permanentMods),
                    flags = new HashSet<string>(fs.flags),
                    counters = new SortedDictionary<string, float>(fs.counters),
                    currentFocus = fs.currentFocus,
                    focusTurnsLeft = fs.focusTurnsLeft,
                    completedFocuses = new List<string>(fs.completedFocuses),
                    focusQueue = new List<string>(fs.focusQueue),
                    passedLaws = new HashSet<string>(fs.passedLaws),
                    lawsProposed = fs.lawsProposed,
                    marriages = new HashSet<string>(fs.marriages ?? new List<string>()),
                    dogma = fs.dogma,
                    questStates = new List<string>(fs.questStates),
                    lastEventTurn = fs.lastEventTurn,
                    score = fs.score,
                };
                foreach (var a in fs.advisors) f.advisors.Add(new AdvisorState { defId = a.defId, cooldown = a.cooldown });
                foreach (var t in fs.timedMods) f.timedMods.Add(new TimedModifier { key = t.key, value = t.value, turnsLeft = t.turnsLeft, source = t.source });
                foreach (var it in fs.intel ?? new List<IntelSave>())
                {
                    if (string.IsNullOrEmpty(it.factionId)) continue;
                    f.intel[it.factionId] = new FactionIntel
                    {
                        factionId = it.factionId, turn = it.turn,
                        gold = it.gold, food = it.food, materials = it.materials, influence = it.influence,
                        provinces = it.provinces, armies = it.armies, units = it.units, strength = it.strength,
                        techs = it.techs, recognition = it.recognition, capital = it.capital,
                        armyLines = new List<string>(it.armyLines ?? new List<string>()),
                        wars = new List<string>(it.wars ?? new List<string>()),
                        lastTechs = new List<string>(it.lastTechs ?? new List<string>()),
                    };
                }
                state.Factions[f.id] = f;
            }

            foreach (var ps in d.provinces)
            {
                if (!state.Provinces.TryGetValue(ps.id, out var p)) continue;
                p.owner = ps.owner;
                p.controller = ps.controller;
                if (!string.IsNullOrEmpty(ps.house)) p.house = ps.house;
                p.dominantFaith = ps.dominantFaith;
                p.startFaith = string.IsNullOrEmpty(ps.startFaith) ? ps.dominantFaith : ps.startFaith;
                p.besieger = ps.besieger;
                p.buildings = new List<string>(ps.buildings);
                p.buildQueue = new Dictionary<string, int>(ps.buildQueue);
                p.unrest = ps.unrest;
                p.development = ps.development;
                p.garrison = ps.garrison;
                p.raidCooldown = ps.raidCooldown;
                p.siegeProgress = ps.siegeProgress;
                p.turnsOwned = ps.turnsOwned;
                p.raided = ps.raided;
                p.faith = new SortedDictionary<string, float>(ps.faith);
                p.flags = new HashSet<string>(ps.flags);
            }

            foreach (var asv in d.armies)
            {
                var a = new ArmyState
                {
                    id = asv.id, faction = asv.faction, name = asv.name, province = asv.province, heroId = asv.heroId,
                    stance = asv.stance, originProvince = asv.originProvince,
                    destroyedUnits = new List<string>(asv.destroyedUnits),
                    movementLeft = asv.movementLeft, maxMovement = asv.maxMovement, supply = asv.supply,
                    turnsInEnemyLand = asv.turnsInEnemyLand,
                    alive = asv.alive, movedThisTurn = asv.movedThisTurn, seaTravel = asv.seaTravel,
                };
                foreach (var u in asv.units)
                    a.units.Add(new UnitInstance { defId = u.defId, name = u.name, hp = u.hp, morale = u.morale, xp = u.xp, level = u.level, upgradeLevel = u.upgradeLevel, alive = u.alive });
                state.Armies[a.id] = a;
            }

            foreach (var h in d.heroes)
            {
                var hs = new HeroState
                {
                    defId = h.defId, name = h.name, faction = h.faction, armyId = h.armyId,
                    level = h.level, xp = h.xp, woundedTurns = h.woundedTurns, abilityCooldown = h.abilityCooldown,
                    reassignCooldown = h.reassignCooldown,
                    alive = h.alive, wounded = h.wounded,
                    cooldowns = new Dictionary<string, int>(h.cooldowns),
                };
                state.Heroes[hs.defId] = hs;
            }

            foreach (var m in d.minorHouses)
                state.MinorHouses[m.house] = new MinorHouseState
                {
                    house = m.house, relation = m.relation, vassalOf = m.vassalOf, annexed = m.annexed,
                    hostile = m.hostile, tributeTurnsLeft = m.tributeTurnsLeft, lastActionTurn = m.lastActionTurn,
                };

            foreach (var req in d.minorRequests ?? new List<MinorRequestSave>())
                if (!string.IsNullOrEmpty(req.house) && !string.IsNullOrEmpty(req.faction))
                    state.MinorRequests.Add(new MinorRequestState
                    {
                        house = req.house, faction = req.faction, type = req.type, turnsLeft = req.turnsLeft,
                    });

            foreach (var c in d.camps)
                state.RaiderCamps.Add(new RaiderCampState
                {
                    id = c.id, name = c.name, province = c.province, kind = c.kind,
                    strength = c.strength, respawnTurns = c.respawnTurns, cleared = c.cleared,
                });

            foreach (var q in d.quests)
                state.Quests.Add(new QuestState { id = q.id, faction = q.faction, step = q.step, progress = q.progress, completed = q.completed, failed = q.failed });

            foreach (var l in d.laws)
                state.ActiveLaws.Add(new LawState { id = l.id, sponsor = l.sponsor, passedTurn = l.passedTurn });

            foreach (var w in d.wars)
                state.Wars.Add(new WarState
                {
                    attacker = w.attacker, defender = w.defender, aggressor = w.aggressor,
                    startTurn = w.startTurn, turns = w.turns, attackerBattles = w.attackerBattles,
                    defenderBattles = w.defenderBattles, active = w.active,
                });

            state.Truces = new SortedDictionary<string, int>(d.truces ?? new Dictionary<string, int>());
            state.Pacts = new SortedDictionary<string, int>(d.pacts ?? new Dictionary<string, int>());
            state.CoalitionTarget = d.coalitionTarget;
            state.CoalitionUntil = d.coalitionUntil;
            state.FaithStartShare = d.faithStartShare ?? new Dictionary<string, float>();
            foreach (var se in d.scheduledEvents ?? new List<ScheduledEventSave>())
                if (!string.IsNullOrEmpty(se.eventId))
                    state.ScheduledEvents.Add(new ScheduledEventState { turn = se.turn, eventId = se.eventId, faction = se.faction });
            state.Council = new CouncilState
            {
                seats = new Dictionary<string, string>(d.council.seats ?? new Dictionary<string, string>()),
                throneHolder = d.council.throneHolder,
                hand = d.council.hand,
                lastAgenda = d.council.lastAgenda,
                sessionsHeld = d.council.sessionsHeld,
                lastAgendaTurn = d.council.lastAgendaTurn,
            };
            state.firedEvents = new HashSet<string>(d.firedEvents ?? new List<string>());
            state.eventHistory = new SortedDictionary<string, int>(d.eventHistory ?? new Dictionary<string, int>());
            state.Log = (d.log ?? new List<LogSave>()).Select(l => new LogEntry(l.turn, l.category, l.text, l.faction)).ToList();
            return state;
        }

        private static GamePhase ParsePhase(string s) =>
            Enum.TryParse<GamePhase>(s, true, out var p) ? p : GamePhase.PlayerTurn;
    }
}
