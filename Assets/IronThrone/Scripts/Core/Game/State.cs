using System;
using System.Collections.Generic;
using System.Linq;
using IronThrone.Core.Content;

namespace IronThrone.Core.Game
{
    public enum GamePhase { Setup, PlayerTurn, Resolving, GameOver }

    public class UnitInstance
    {
        public string defId;
        public string name;
        public float hp = 100f;         // 0..100, при 0 подразделение уничтожено
        public float morale = 100f;     // 0..100
        public int xp;
        public int level = 1;
        public int upgradeLevel;        // 0..2, специализация
        public bool alive = true;

        public UnitInstance() { }
        public UnitInstance(string defId, string name) { this.defId = defId; this.name = name; }
    }

    public class HeroState
    {
        public string defId;
        public string name;
        public string faction;
        public int level = 1;
        public int xp;
        public bool alive = true;
        public bool wounded;
        public int woundedTurns;
        public string armyId;
        public int abilityCooldown;
        public Dictionary<string, int> cooldowns = new Dictionary<string, int>();
    }

    public class ArmyState
    {
        public string id;
        public string faction;
        public string name;
        public string province;
        public string heroId;
        public List<UnitInstance> units = new List<UnitInstance>();
        public int movementLeft;
        public int maxMovement;
        public string stance = "move"; // move, defend, raid, siege
        public bool alive = true;
        public int supply = 100;
        public int turnsInEnemyLand;
        public bool movedThisTurn;
        public List<string> destroyedUnits = new List<string>();
        public bool seaTravel;
        public string originProvince;

        public int StrengthTotal => units.Where(u => u.alive).Sum(u => Math.Max(1, (int)u.hp));
        public int UnitCount => units.Count(u => u.alive);
        public bool IsEmpty => UnitCount == 0;
    }

    public class ProvinceState
    {
        public string id;
        public ProvinceDef def;
        public string owner;          // фракция-владелец (или null — вольный дом)
        public string controller;     // фракция-контролёр (оккупация)
        public string house;          // дом, которому принадлежит провинция исторически
        public List<string> buildings = new List<string>();
        public Dictionary<string, int> buildQueue = new Dictionary<string, int>(); // buildingId -> turns left
        public float unrest;
        public int garrison;
        public Dictionary<string, float> faith = new Dictionary<string, float>();
        public string dominantFaith;
        public bool raided;
        public int raidCooldown;
        public int siegeProgress;
        public string besieger;
        public float development;
        public HashSet<string> flags = new HashSet<string>();
        public int turnsOwned;

        public bool IsCapital => def != null && def.isCapital;
        public string Region => def?.region;
        public string Terrain => def?.terrain;
    }

    public class LawState
    {
        public string id;
        public string sponsor;
        public int passedTurn;
    }

    public class LawVoteState
    {
        public string lawId;
        public string sponsor;
        public int openedTurn;
        public bool resolved;
        public Dictionary<string, int> votes = new Dictionary<string, int>();      // factionId -> вес голоса
        public Dictionary<string, float> lobby = new Dictionary<string, float>();  // влияние, вложенное игроком
        public Dictionary<string, string> votedFor = new Dictionary<string, string>(); // factionId -> "yes"/"no"
        public int yes;
        public int no;
        public string resultText;
    }

    public class TimedModifier
    {
        public string key;
        public float value;
        public int turnsLeft;
        public string source;
    }

    public class AdvisorState
    {
        public string defId;
        public int cooldown;
    }

    public class FactionState
    {
        public string id;
        public FactionDef def;
        public bool isPlayer;
        public bool alive = true;
        public string house;
        public string capital;

        public int gold;
        public int materials;
        public int food;
        public int influence;
        public int piety;

        public int goldIncome, materialsIncome, foodIncome, influenceIncome, pietyIncome;

        public HashSet<string> techs = new HashSet<string>();
        public Dictionary<string, int> research = new Dictionary<string, int>(); // techId -> turns left
        public string currentResearch;

        public List<AdvisorState> advisors = new List<AdvisorState>();
        public List<string> armies = new List<string>();
        public List<string> heroes = new List<string>();

        public Dictionary<string, float> relations = new Dictionary<string, float>(); // factionId -> -100..100
        public Dictionary<string, int> minorRelations = new Dictionary<string, int>(); // houseId -> -100..100
        public Dictionary<string, int> vassals = new Dictionary<string, int>();       // minor house -> turns as vassal
        public HashSet<string> annexed = new HashSet<string>();

        public Dictionary<string, float> permanentMods = new Dictionary<string, float>();
        public List<TimedModifier> timedMods = new List<TimedModifier>();

        public float recognition;
        public float tradeDominance;
        public int tradeRoutePoints;
        public float growth;
        public bool growthReady;
        public int claimedProvinces;
        public int spyNetwork;
        public int counterspy;

        public HashSet<string> flags = new HashSet<string>();
        public Dictionary<string, float> counters = new Dictionary<string, float>();

        public string currentFocus;
        public int focusTurnsLeft;
        public List<string> completedFocuses = new List<string>();
        public List<string> focusQueue = new List<string>();

        public HashSet<string> passedLaws = new HashSet<string>();
        public int lawsProposed;

        public string faith;
        public float faithPoints;

        public List<string> questStates = new List<string>(); // questId:step
        public int lastEventTurn = -1;

        public int score;

        public bool HasTech(string id) => techs.Contains(id);
        public bool HasFlag(string f) => flags.Contains(f);
        public void SetFlag(string f) => flags.Add(f);

        /// <summary>Сумма плоских модификаторов (например, +3 золота за ход).</summary>
        public float Mod(string key)
        {
            float v = 0;
            if (permanentMods.TryGetValue(key, out var p)) v += p;
            foreach (var t in timedMods)
                if (t.key == key) v += t.value;
            return v;
        }

        /// <summary>
        /// Множитель: 1 + сумма процентных модификаторов. Для ключей вида "goldIncome"
        /// читается "goldIncome.pct"; для ключей вида "buildCost.pct" — сам ключ.
        /// </summary>
        public float ModPct(string key)
        {
            string k = key.EndsWith(".pct") ? key : key + ".pct";
            float v = 1f;
            if (permanentMods.TryGetValue(k, out var p)) v += p;
            foreach (var t in timedMods)
                if (t.key == k) v += t.value;
            return Math.Max(0.05f, v);
        }
    }

    public class CouncilState
    {
        public Dictionary<string, string> seats = new Dictionary<string, string>(); // titleId -> factionId
        public string throneHolder; // фракция, контролирующая Королевскую Гавань и Железный трон
        public string hand;         // фракция-десница (лидер совета)
        public int sessionsHeld;
        public string lastAgenda;
        public int lastAgendaTurn = -99;
    }

    public class MinorHouseState
    {
        public string house;
        public int relation = 0;      // -100..100
        public string vassalOf;       // фракция-сюзерен
        public bool annexed;
        public bool hostile;
        public int tributeTurnsLeft;
        public int lastActionTurn = -99;
    }

    public class RaiderCampState
    {
        public string id;
        public string name;
        public string province;
        public string kind = "pirates"; // pirates, wildlings, mountain clans, reavers, bandits
        public int strength;
        public int respawnTurns;
        public bool cleared;
    }

    public class QuestState
    {
        public string id;
        public string faction;
        public int step;
        public bool completed;
        public bool failed;
        public int progress;
    }

    public class WarState
    {
        public string attacker;
        public string defender;
        public int startTurn;
        public int turns;
        public int attackerBattles;
        public int defenderBattles;
        public string aggressor;
        public bool active = true;

        public bool Involves(string factionId) => attacker == factionId || defender == factionId;
        public string EnemyOf(string factionId) =>
            factionId == attacker ? defender : (factionId == defender ? attacker : null);
    }

    public class PendingEventState
    {
        public string eventId;
        public string faction;
        public string province;
        public bool isGlobal;
    }

    public class LogEntry
    {
        public int turn;
        public string category;
        public string text;
        public string faction;

        public LogEntry() { }
        public LogEntry(int turn, string category, string text, string faction = null)
        {
            this.turn = turn; this.category = category; this.text = text; this.faction = faction;
        }
    }

    public class BattleResult
    {
        public string attacker;
        public string defender;
        public string province;
        public bool attackerWon;
        public int attackerLosses;
        public int defenderLosses;
        public string heroAttacker;
        public string heroDefender;
        public List<string> rounds = new List<string>();
        public string Summary =>
            $"{(attackerWon ? "Победа" : "Поражение")} в {province}: потери {attackerLosses} / {defenderLosses}";
    }

    public class GameState
    {
        public ContentDb Content;
        public MapData CustomMap;
        public MapData Map => CustomMap ?? Content.Map;
        public SettingsDef Settings => Content.Settings;
        public EraDef Era;
        public int Turn = 1;
        public int Year;
        public string Season = "Весна";
        public GamePhase Phase = GamePhase.Setup;
        public string PlayerFaction;
        public int Seed;
        public Random Rng;

        public Dictionary<string, ProvinceState> Provinces = new Dictionary<string, ProvinceState>();
        public Dictionary<string, FactionState> Factions = new Dictionary<string, FactionState>();
        public Dictionary<string, ArmyState> Armies = new Dictionary<string, ArmyState>();
        public Dictionary<string, HeroState> Heroes = new Dictionary<string, HeroState>();
        public Dictionary<string, MinorHouseState> MinorHouses = new Dictionary<string, MinorHouseState>();
        public List<RaiderCampState> RaiderCamps = new List<RaiderCampState>();
        public List<QuestState> Quests = new List<QuestState>();
        public CouncilState Council = new CouncilState();
        public List<LawState> ActiveLaws = new List<LawState>();
        public LawVoteState PendingVote;
        public List<WarState> Wars = new List<WarState>();
        public Dictionary<string, int> Truces = new Dictionary<string, int>(); // "a|b" -> ходы до истечения
        public List<PendingEventState> PendingEvents = new List<PendingEventState>();
        public List<LogEntry> Log = new List<LogEntry>();
        public List<BattleResult> Battles = new List<BattleResult>();
        public Dictionary<string, int> eventHistory = new Dictionary<string, int>();
        public HashSet<string> firedEvents = new HashSet<string>();
        public List<string> PendingNotifications = new List<string>();
        public int NextArmyId = 1;
        public int NextCampId = 1;
        public string VictoryWinner;
        public string VictoryType;

        public IEnumerable<FactionState> LivingFactions => Factions.Values.Where(f => f.alive);
        public FactionState Player => PlayerFaction != null && Factions.TryGetValue(PlayerFaction, out var f) ? f : null;

        public void AddLog(string category, string text, string faction = null)
        {
            Log.Add(new LogEntry(Turn, category, text, faction));
            if (Log.Count > 4000) Log.RemoveRange(0, 1000);
        }

        public void Notify(string text)
        {
            PendingNotifications.Add(text);
            AddLog("notify", text);
        }

        public ProvinceState Province(string id) => id != null && Provinces.TryGetValue(id, out var p) ? p : null;

        public FactionState Faction(string id) => id != null && Factions.TryGetValue(id, out var f) ? f : null;

        public string SeasonName(int turn)
        {
            int s = (turn - 1) % 12;
            if (s < 3) return "Весна";
            if (s < 6) return "Лето";
            if (s < 9) return "Осень";
            return "Зима";
        }

        public IEnumerable<ProvinceState> ProvincesOf(string faction) =>
            Provinces.Values.Where(p => p.owner == faction);

        public IEnumerable<ProvinceState> ControlledBy(string faction) =>
            Provinces.Values.Where(p => p.controller == faction);

        public int CountControlled(string faction) => Provinces.Values.Count(p => p.controller == faction);

        public int TotalProvinces => Provinces.Count;

        public string FaithName(string faithId) =>
            Content.FaithById.TryGetValue(faithId ?? "", out var f) ? f.name : "Безбожие";

        public string FactionName(string factionId) =>
            Factions.TryGetValue(factionId ?? "", out var f) ? f.def.name : "Вольные земли";

        public string HouseName(string houseId) =>
            Content.HouseById.TryGetValue(houseId ?? "", out var h) ? h.name : (houseId ?? "Неизвестный дом");
    }
}
