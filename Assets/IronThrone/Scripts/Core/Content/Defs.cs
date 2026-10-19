using System;
using System.Collections.Generic;

namespace IronThrone.Core.Content
{
    // ------------------------------------------------------------------ базовые
    public class ResourceCost
    {
        public int gold;
        public int materials;
        public int food;
        public int influence;
        public int piety;

        public ResourceCost() { }
        public ResourceCost(int gold, int materials = 0, int food = 0, int influence = 0, int piety = 0)
        {
            this.gold = gold; this.materials = materials; this.food = food; this.influence = influence; this.piety = piety;
        }

        public bool IsZero => gold == 0 && materials == 0 && food == 0 && influence == 0 && piety == 0;

        public ResourceCost Scaled(float k) => new ResourceCost(
            (int)Math.Round(gold * k), (int)Math.Round(materials * k), (int)Math.Round(food * k),
            (int)Math.Round(influence * k), (int)Math.Round(piety * k));

        public IEnumerable<(string res, int amount)> Parts()
        {
            if (gold != 0) yield return ("gold", gold);
            if (materials != 0) yield return ("materials", materials);
            if (food != 0) yield return ("food", food);
            if (influence != 0) yield return ("influence", influence);
            if (piety != 0) yield return ("piety", piety);
        }

        public string ToRu()
        {
            var parts = new List<string>();
            if (gold != 0) parts.Add($"{gold} зол.");
            if (materials != 0) parts.Add($"{materials} мат.");
            if (food != 0) parts.Add($"{food} прод.");
            if (influence != 0) parts.Add($"{influence} вл.");
            if (piety != 0) parts.Add($"{piety} веры");
            return parts.Count == 0 ? "бесплатно" : string.Join(", ", parts);
        }
    }

    public class ModifierDef
    {
        public string key;
        public float value;
        public string description;

        public ModifierDef() { }
        public ModifierDef(string key, float value, string description = null)
        {
            this.key = key; this.value = value; this.description = description;
        }
    }

    /// <summary>
    /// Универсальный эффект. Набор используемых полей зависит от op.
    /// op: resource, modifier, timedModifier, opinion, unrest, faith, unlockTech, unlockUnit,
    ///     unlockBuilding, recruit, spawnArmy, recognition, tradeDominance, title, relation,
    ///     flag, heal, damageArmy, grantHero, questFlag, law, capitalBuilding, movement, reveal
    /// </summary>
    public class EffectDef
    {
        public string op;
        public string key;
        public string target;
        public string res;
        public string id;
        public string province;
        public string faction;
        public string flag;
        public string text;
        public float amount;
        public float value;
        public int count;
        public int turns;
        public List<string> ids = new List<string>();
    }

    public class EffectList
    {
        public List<EffectDef> effects = new List<EffectDef>();
        public string summary;
    }

    public class StatBlock
    {
        public int hp = 100;
        public int attack = 10;
        public int defense = 6;
        public int range = 1;
        public int move = 3;
        public int morale = 60;
        public int initiative = 10;

        public StatBlock Clone() => (StatBlock)MemberwiseClone();

        public StatBlock Plus(StatBlock o) => new StatBlock
        {
            hp = hp + o.hp, attack = attack + o.attack, defense = defense + o.defense,
            range = range + o.range, move = move + o.move, morale = morale + o.morale,
            initiative = initiative + o.initiative
        };
    }

    // ------------------------------------------------------------------ настройки
    public class VictorySettings
    {
        public float hegemonyProvinceShare = 0.55f;
        public int hegemonyRegionCapitals = 5;
        public int politicalRecognition = 100;
        public int economicDominance = 100;
        public float religiousProvinceShare = 0.7f;
        public int maxTurns = 220;
    }

    public class SlotUnlockDef
    {
        public int level;
        public List<string> slots = new List<string>();
    }

    public class SettingsDef
    {
        public int turnsPerYear = 12;
        public int maxTurns = 220;
        public VictorySettings victory = new VictorySettings();
        public ResourceCost startingResources = new ResourceCost(240, 180, 140, 24, 0);
        public ResourceCost startingNeutralResources = new ResourceCost(80, 60, 60, 0, 0);
        public int armyMaxUnits = 6;
        public int movementPerTurn = 1;
        public int roadMovement = 2;
        public int seaMovement = 2;
        public float recoveryRate = 0.5f;
        public int spyNetworkBase = 1;
        public int maxArmies = 4;
        public float unrestRevoltThreshold = 60f;
        public int armyUpkeepFood = 4;
        public int provinceBaseIncome = 6;
        public float raiderChance = 0.18f;
        public int eventChancePercent = 55;
        public int councilSeats = 7;
        public int claimGrowth = 34;
        public int claimGold = 50;
        public int claimMaterials = 30;
        public int claimInfluence = 8;
        public int provinceImprovementLimit = 2;
        public int voteLobbyCost = 8;
        public List<SlotUnlockDef> capitalSlots = new List<SlotUnlockDef>();
        public Dictionary<string, float> terrainModifiers = new Dictionary<string, float>();
        public Dictionary<string, float> seasonIncome = new Dictionary<string, float>();
    }

    public class EraDef
    {
        public string id;
        public string name;
        public string description;
        public int startYear;
        public string yearSuffix = "от З.Э.";
        public List<string> factions = new List<string>();
        public string map = "canonical";
        public List<string> globalEvents = new List<string>();
        public string intro;
    }

    public class HouseDef
    {
        public string id;
        public string name;
        public string words;
        public string color;
        public string region;
        public string seat;
        public bool major;
        public string description;
        public bool pirate;
        public ModifierDef[] bonuses;
    }

    public class FactionDef
    {
        public string id;
        public string era;
        public string house;
        public string name;
        public string shortName;
        public string color;
        public string leaderName;
        public string leaderTitle;
        public string leaderHero;
        public string heirHero;
        public List<string> extraHeroes = new List<string>();
        public string capital;
        public List<string> ownedRegions = new List<string>();
        public List<string> extraProvinces = new List<string>();
        public List<string> excludeProvinces = new List<string>();
        public int priority;
        public List<string> vassalHouses = new List<string>();
        public List<string> allyRegions = new List<string>();
        public string faith;
        public bool playable = true;
        public int difficulty;
        public List<ModifierDef> bonuses = new List<ModifierDef>();
        public List<string> traits = new List<string>();
        public string techTree;
        public string focusTree;
        public List<string> advisorPool = new List<string>();
        public List<string> startUnits = new List<string>();
        public List<string> startArmies = new List<string>();
        public List<string> startBuildings = new List<string>();
        public ResourceCost startResources;
        public string description;
        public string uniqueMechanicName;
        public string uniqueMechanicText;
        public List<string> availableLaws = new List<string>();
        public bool nightWatch;
    }

    public class TechDef
    {
        public string id;
        public string name;
        public string faction = "shared";
        public string branch = "war";
        public int tier = 1;
        public ResourceCost cost = new ResourceCost();
        public int researchTurns = 2;
        public List<string> requires = new List<string>();
        public List<EffectDef> effects = new List<EffectDef>();
        public string description;
        public string icon;
        public bool starting;
    }

    public class BuildingDef
    {
        public string id;
        public string name;
        public string slot = "economy";
        public ResourceCost cost = new ResourceCost();
        public int buildTurns = 1;
        public string requiresTech;
        public List<EffectDef> effects = new List<EffectDef>();
        public List<ModifierDef> modifiers = new List<ModifierDef>();
        public string description;
        public bool unique;
        public string uniqueToFaction;
        public int maxPerEmpire = 99;
    }

    public class LawDef
    {
        public string id;
        public string name;
        public string description;
        public List<EffectDef> effects = new List<EffectDef>();
        public List<string> eras = new List<string>();
        public int voteCost = 15;
        public string requiresTitle;
        public int cooldown = 6;
        public bool repealable = true;
        public string sponsorHint;
    }

    public class TitleDef
    {
        public string id;
        public string name;
        public string description;
        public List<EffectDef> effects = new List<EffectDef>();
        public string shortEffect;
        public int influencePerTurn = 2;
    }

    public class UnitDef
    {
        public string id;
        public string name;
        public string faction = "shared";
        public int tier = 1;
        public ResourceCost cost = new ResourceCost();
        public ResourceCost upkeep = new ResourceCost();
        public StatBlock stats = new StatBlock();
        public List<string> abilities = new List<string>();
        public List<string> upgradesTo = new List<string>();
        public string description;
        public string terrainBonus;
        public string kind = "infantry"; // infantry, cavalry, archers, siege, navy, dragon, special
        public string requiresTech;
        public float recoveryCostFactor = 0.5f;
    }

    public class HeroDef
    {
        public string id;
        public string name;
        public string house;
        public string era;
        public string description;
        public StatBlock stats = new StatBlock();
        public List<string> abilities = new List<string>();
        public List<string> traits = new List<string>();
        public string traitText;
        public string dragonUnit;
        public string image;
        public string titleRu;
    }

    public class AdvisorActiveDef
    {
        public string name;
        public string description;
        public int cooldown = 4;
        public ResourceCost cost = new ResourceCost();
        public List<EffectDef> effects = new List<EffectDef>();
        public string target = "self"; // self, faction, province, army
    }

    public class AdvisorDef
    {
        public string id;
        public string name;
        public string title;
        public string description;
        public List<string> eras = new List<string>();
        public string faction;
        public List<ModifierDef> passive = new List<ModifierDef>();
        public AdvisorActiveDef active = new AdvisorActiveDef();
        public string portrait;
        public string quote;
    }

    public class FaithTenetDef
    {
        public string name;
        public string description;
        public List<ModifierDef> modifiers = new List<ModifierDef>();
        public List<EffectDef> effects = new List<EffectDef>();
    }

    public class FaithDef
    {
        public string id;
        public string name;
        public string description;
        public string headProvince;
        public string color;
        public List<FaithTenetDef> tenets = new List<FaithTenetDef>();
        public float spread = 1f;
        public List<string> favoredTerrain = new List<string>();
        public List<string> holySites = new List<string>();
        public string bonusText;
        public bool playable = true;
    }

    public class FocusDef
    {
        public string id;
        public string name;
        public string faction;
        public List<string> eras = new List<string>();
        public string branch = "power";
        public string description;
        public int turns = 3;
        public List<string> requires = new List<string>();
        public List<string> exclusive = new List<string>();
        public List<EffectDef> effects = new List<EffectDef>();
        public string rewardText;
        public int x;
        public int y;
        public string icon;
    }

    public class EventChoiceDef
    {
        public string text;
        public string tooltip;
        public List<EffectDef> effects = new List<EffectDef>();
        public ResourceCost cost = new ResourceCost();
        public string requiresFlag;
        public float aiWeight = 1f;
        public string resultText;
    }

    public class EventConditionsDef
    {
        public string faction;
        public string era;
        public int minTurn;
        public int maxTurn;
        public string requiresTech;
        public string requiresFaith;
        public float minUnrest;
        public string requiresProvince;
        public string requiresFlag;
        public string notFlag;
        public bool isRuler;
    }

    public class EventDef
    {
        public string id;
        public string type = "random"; // random, global, era, quest
        public string name;
        public string text;
        public string image;
        public float weight = 1f;
        public int minTurn;
        public int everyTurns;
        public bool oncePerGame;
        public EventConditionsDef conditions = new EventConditionsDef();
        public List<EventChoiceDef> choices = new List<EventChoiceDef>();
        public string questline;
        public string flavor;
    }

    public class QuestObjectiveDef
    {
        public string type; // controlProvince, tech, gold, killHero, buildProvince, faithShare, vassal, title
        public string target;
        public int count = 1;
        public string text;
    }

    public class QuestStepDef
    {
        public string text;
        public QuestObjectiveDef objective = new QuestObjectiveDef();
        public List<EffectDef> reward = new List<EffectDef>();
        public string rewardText;
    }

    public class QuestDef
    {
        public string id;
        public string name;
        public string faction;
        public List<string> eras = new List<string>();
        public string description;
        public List<QuestStepDef> steps = new List<QuestStepDef>();
        public string intro;
    }

    public class MinorHouseActionDef
    {
        public string id;
        public string name;
        public string description;
        public ResourceCost cost = new ResourceCost();
        public float relationRequired;
        public List<EffectDef> effects = new List<EffectDef>();
        public string resultText;
    }

    // ------------------------------------------------------------------ карта
    public class RegionDef
    {
        public string id;
        public string name;
        public string color;
        public string capital;
    }

    public class ProvinceDef
    {
        public string id;
        public string name;
        public string region;
        public string house;
        public string kind;
        public int level;
        public List<List<int>> tiles = new List<List<int>>();
        public List<int> settlement = new List<int>();
        public string terrain;
        public bool river;
        public bool coastal;
        public bool isCapital;
        public bool port;
        public string resource;
        public List<string> neighbors = new List<string>();
        public string domain;
    }

    public class MapTileDef
    {
        public int c;
        public int r;
        public bool sea;
        public string region;
        public string province;
        public string terrain;
        public bool river;
        public bool coastal;
    }

    public class SeaRouteDef
    {
        public List<string> route = new List<string>();
    }

    public class MapData
    {
        public int width;
        public int height;
        public string layout;
        public List<RegionDef> regions = new List<RegionDef>();
        public List<ProvinceDef> provinces = new List<ProvinceDef>();
        public List<HouseDef> houses = new List<HouseDef>();
        public List<MapTileDef> tiles = new List<MapTileDef>();
        public List<List<string>> seaRoutes = new List<List<string>>();

        public Dictionary<string, ProvinceDef> ProvinceById = new Dictionary<string, ProvinceDef>();
        public Dictionary<string, HouseDef> HouseById = new Dictionary<string, HouseDef>();
        public Dictionary<string, RegionDef> RegionById = new Dictionary<string, RegionDef>();
        public Dictionary<(int c, int r), MapTileDef> TileByCoord = new Dictionary<(int, int), MapTileDef>();

        public void Index()
        {
            ProvinceById.Clear(); HouseById.Clear(); RegionById.Clear(); TileByCoord.Clear();
            foreach (var p in provinces) ProvinceById[p.id] = p;
            foreach (var h in houses) HouseById[h.id] = h;
            foreach (var r in regions) RegionById[r.id] = r;
            foreach (var t in tiles) TileByCoord[(t.c, t.r)] = t;
        }
    }
}
