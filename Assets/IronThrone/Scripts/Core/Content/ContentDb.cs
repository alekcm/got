using System;
using System.Collections.Generic;
using System.Linq;

namespace IronThrone.Core.Content
{
    /// <summary>Все статические данные игры (контент), загруженные из JSON.</summary>
    public class ContentDb
    {
        public SettingsDef Settings = new SettingsDef();
        public List<EraDef> Eras = new List<EraDef>();
        public List<FactionDef> Factions = new List<FactionDef>();
        public List<HouseDef> Houses = new List<HouseDef>();
        public List<TechDef> Techs = new List<TechDef>();
        public List<BuildingDef> Buildings = new List<BuildingDef>();
        public List<LawDef> Laws = new List<LawDef>();
        public List<TitleDef> Titles = new List<TitleDef>();
        public List<UnitDef> Units = new List<UnitDef>();
        public List<HeroDef> Heroes = new List<HeroDef>();
        public List<AdvisorDef> Advisors = new List<AdvisorDef>();
        public List<FaithDef> Faiths = new List<FaithDef>();
        public List<FocusDef> Focuses = new List<FocusDef>();
        public List<EventDef> Events = new List<EventDef>();
        public List<QuestDef> Quests = new List<QuestDef>();
        public List<MinorHouseActionDef> MinorHouseActions = new List<MinorHouseActionDef>();
        public MapData Map = new MapData();

        public readonly Dictionary<string, EraDef> EraById = new Dictionary<string, EraDef>();
        public readonly Dictionary<string, FactionDef> FactionById = new Dictionary<string, FactionDef>();
        public readonly Dictionary<string, HouseDef> HouseById = new Dictionary<string, HouseDef>();
        public readonly Dictionary<string, TechDef> TechById = new Dictionary<string, TechDef>();
        public readonly Dictionary<string, BuildingDef> BuildingById = new Dictionary<string, BuildingDef>();
        public readonly Dictionary<string, LawDef> LawById = new Dictionary<string, LawDef>();
        public readonly Dictionary<string, TitleDef> TitleById = new Dictionary<string, TitleDef>();
        public readonly Dictionary<string, UnitDef> UnitById = new Dictionary<string, UnitDef>();
        public readonly Dictionary<string, HeroDef> HeroById = new Dictionary<string, HeroDef>();
        public readonly Dictionary<string, AdvisorDef> AdvisorById = new Dictionary<string, AdvisorDef>();
        public readonly Dictionary<string, FaithDef> FaithById = new Dictionary<string, FaithDef>();
        public readonly Dictionary<string, FocusDef> FocusById = new Dictionary<string, FocusDef>();
        public readonly Dictionary<string, EventDef> EventById = new Dictionary<string, EventDef>();
        public readonly Dictionary<string, QuestDef> QuestById = new Dictionary<string, QuestDef>();
        public readonly Dictionary<string, MinorHouseActionDef> MinorActionById = new Dictionary<string, MinorHouseActionDef>();

        public readonly List<string> LoadWarnings = new List<string>();

        public void Index()
        {
            IndexList(Eras, EraById, e => e.id);
            IndexList(Factions, FactionById, f => f.id);
            IndexList(Houses, HouseById, h => h.id);
            IndexList(Techs, TechById, t => t.id);
            IndexList(Buildings, BuildingById, b => b.id);
            IndexList(Laws, LawById, l => l.id);
            IndexList(Titles, TitleById, t => t.id);
            IndexList(Units, UnitById, u => u.id);
            IndexList(Heroes, HeroById, h => h.id);
            IndexList(Advisors, AdvisorById, a => a.id);
            IndexList(Faiths, FaithById, f => f.id);
            IndexList(Focuses, FocusById, f => f.id);
            IndexList(Events, EventById, e => e.id);
            IndexList(Quests, QuestById, q => q.id);
            IndexList(MinorHouseActions, MinorActionById, a => a.id);
            Map.Index();
            Validate();
        }

        private void IndexList<T>(List<T> list, Dictionary<string, T> dict, Func<T, string> key)
        {
            dict.Clear();
            foreach (var item in list)
            {
                var k = key(item);
                if (string.IsNullOrEmpty(k)) continue;
                if (dict.ContainsKey(k))
                {
                    LoadWarnings.Add($"Дублирующийся id '{k}' в {typeof(T).Name}, вторая запись проигнорирована.");
                    continue;
                }
                dict[k] = item;
            }
        }

        private void Validate()
        {
            // ссылочная целостность контента: не падаем, но копим предупреждения
            foreach (var f in Factions)
            {
                foreach (var u in f.startUnits)
                    if (!UnitById.ContainsKey(u)) LoadWarnings.Add($"Фракция {f.id}: неизвестный юнит '{u}' в startUnits");
                foreach (var a in f.advisorPool)
                    if (!AdvisorById.ContainsKey(a)) LoadWarnings.Add($"Фракция {f.id}: неизвестный советник '{a}'");
                foreach (var h in new[] { f.leaderHero, f.heirHero })
                    if (!string.IsNullOrEmpty(h) && !HeroById.ContainsKey(h))
                        LoadWarnings.Add($"Фракция {f.id}: неизвестный герой '{h}'");
                if (!string.IsNullOrEmpty(f.capital) && !Map.ProvinceById.ContainsKey(f.capital))
                    LoadWarnings.Add($"Фракция {f.id}: столица '{f.capital}' отсутствует на карте");
            }
            foreach (var t in Techs)
                foreach (var r in t.requires)
                    if (!TechById.ContainsKey(r)) LoadWarnings.Add($"Технология {t.id}: неизвестный пререквизит '{r}'");
            foreach (var fo in Focuses)
                foreach (var r in fo.requires)
                    if (!FocusById.ContainsKey(r)) LoadWarnings.Add($"Фокус {fo.id}: неизвестный пререквизит '{r}'");
        }

        public IEnumerable<TechDef> TechsFor(string factionHouse) =>
            Techs.Where(t => t.faction == "shared" || t.faction == factionHouse);

        public IEnumerable<FocusDef> FocusesFor(string factionId) =>
            Focuses.Where(f => f.faction == factionId);

        public IEnumerable<AdvisorDef> AdvisorsFor(EraDef era, FactionDef faction) =>
            Advisors.Where(a => (a.eras.Count == 0 || a.eras.Contains(era.id)) &&
                                (string.IsNullOrEmpty(a.faction) || a.faction == faction.id || a.faction == faction.house));

        /// <summary>Советники, из которых дом реально выбирает двух: faction.advisorPool, иначе общие.</summary>
        public List<AdvisorDef> AdvisorsForFaction(EraDef era, FactionDef faction)
        {
            if (faction == null) return new List<AdvisorDef>();
            var pool = faction.advisorPool
                .Where(id => AdvisorById.ContainsKey(id))
                .Select(id => AdvisorById[id])
                .Where(a => a.eras.Count == 0 || (era != null && a.eras.Contains(era.id)))
                .ToList();
            if (pool.Count == 0)
                pool = Advisors.Where(a => string.IsNullOrEmpty(a.faction) &&
                                           (a.eras.Count == 0 || (era != null && a.eras.Contains(era.id)))).ToList();
            return pool;
        }
    }
}
