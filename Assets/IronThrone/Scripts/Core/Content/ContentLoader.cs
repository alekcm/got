using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IronThrone.Core.Content
{
    /// <summary>
    /// Загрузка контента из папки с JSON. Работает и в Unity (Application.dataPath),
    /// и в headless-тестах (обычный путь к папке IronThroneData).
    /// </summary>
    public static class ContentLoader
    {
        public static ContentDb LoadFromDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                throw new DirectoryNotFoundException("Папка контента не найдена: " + dir);

            var db = new ContentDb();

            db.Settings = LoadFile<SettingsDef>(dir, "settings.json") ?? new SettingsDef();
            db.Eras = LoadList<EraDef>(dir, "eras.json");
            db.Factions = LoadList<FactionDef>(dir, "factions.json");
            db.Houses = LoadList<HouseDef>(dir, "houses.json");
            db.Techs = LoadList<TechDef>(dir, "technologies.json");
            if (db.Techs.Count == 0) db.Techs = LoadList<TechDef>(dir, "techs.json");
            db.Buildings = LoadList<BuildingDef>(dir, "buildings.json");
            db.Laws = LoadList<LawDef>(dir, "laws.json");
            db.Titles = LoadList<TitleDef>(dir, "titles.json");
            db.Units = LoadList<UnitDef>(dir, "units.json");
            db.Heroes = LoadList<HeroDef>(dir, "heroes.json");
            db.Advisors = LoadList<AdvisorDef>(dir, "advisors.json");
            db.Faiths = LoadList<FaithDef>(dir, "faiths.json");
            db.Focuses = LoadList<FocusDef>(dir, "focuses.json");
            db.Events = LoadList<EventDef>(dir, "events.json");
            db.Quests = LoadList<QuestDef>(dir, "quests.json");
            db.MinorHouseActions = LoadList<MinorHouseActionDef>(dir, "minorhouseactions.json");

            db.Map = LoadFile<MapData>(dir, "canonical_map.json") ?? new MapData();

            db.Index();
            ExtendedValidate(db);
            return db;
        }

        private static JsonValue Read(string dir, string file)
        {
            var path = Path.Combine(dir, file);
            return File.Exists(path) ? JsonValue.Parse(File.ReadAllText(path)) : null;
        }

        private static T LoadFile<T>(string dir, string file) where T : class
        {
            var v = Read(dir, file);
            return v == null ? null : JsonMapper.ToObject(v, typeof(T)) as T;
        }

        private static List<T> LoadList<T>(string dir, string file) where T : class
        {
            var v = Read(dir, file);
            if (v == null) return new List<T>();
            if (v.Kind != JsonKind.Array)
                return new List<T> { JsonMapper.ToObject(v, typeof(T)) as T };
            return JsonMapper.ToList<T>(v);
        }

        // ------------------------------------------------------------------ проверки
        private static void ExtendedValidate(ContentDb db)
        {
            var w = db.LoadWarnings;
            var provinceIds = new HashSet<string>(db.Map.provinces.Select(p => p.id));
            var houseIds = new HashSet<string>(db.Houses.Select(h => h.id));
            foreach (var h in db.Map.houses) houseIds.Add(h.id);
            var regionIds = new HashSet<string>(db.Map.regions.Select(r => r.id));

            foreach (var f in db.Factions)
            {
                foreach (var u in f.startUnits)
                    if (!db.UnitById.ContainsKey(u)) w.Add($"[faction] {f.id}: юнит '{u}' в startUnits не существует");
                foreach (var a in f.startArmies)
                {
                    var idx = a.IndexOf('|');
                    var list = idx >= 0 ? a.Substring(idx + 1) : a;
                    foreach (var part in list.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var uid = part.Split('*')[0].Trim();
                        if (!string.IsNullOrEmpty(uid) && !db.UnitById.ContainsKey(uid))
                            w.Add($"[faction] {f.id}: юнит '{uid}' в startArmies не существует");
                    }
                }
                foreach (var b in f.startBuildings)
                    if (!db.BuildingById.ContainsKey(b)) w.Add($"[faction] {f.id}: постройка '{b}' в startBuildings не существует");
                foreach (var l in f.availableLaws)
                    if (!db.LawById.ContainsKey(l)) w.Add($"[faction] {f.id}: закон '{l}' не существует");
                foreach (var hv in new[] { f.leaderHero, f.heirHero }.Concat(f.extraHeroes ?? new List<string>()))
                    if (!string.IsNullOrEmpty(hv) && !db.HeroById.ContainsKey(hv))
                        w.Add($"[faction] {f.id}: герой '{hv}' не существует");
                foreach (var vh in f.vassalHouses)
                    if (!houseIds.Contains(vh)) w.Add($"[faction] {f.id}: дом '{vh}' из vassalHouses отсутствует в houses.json");
                foreach (var r in f.ownedRegions)
                    if (!regionIds.Contains(r)) w.Add($"[faction] {f.id}: регион '{r}' не существует");
                foreach (var cp in f.extraProvinces)
                    if (!provinceIds.Contains(cp)) w.Add($"[faction] {f.id}: провинция '{cp}' не существует");
                foreach (var ep in f.excludeProvinces)
                    if (!provinceIds.Contains(ep)) w.Add($"[faction] {f.id}: провинция '{ep}' в excludeProvinces не существует");
                if (!string.IsNullOrEmpty(f.faith) && !db.FaithById.ContainsKey(f.faith))
                    w.Add($"[faction] {f.id}: вера '{f.faith}' не существует");
                if (!string.IsNullOrEmpty(f.techTree) && f.techTree != "shared" &&
                    !db.Techs.Any(t => t.faction == f.techTree))
                    w.Add($"[faction] {f.id}: techTree '{f.techTree}' не имеет технологий");
                if (!string.IsNullOrEmpty(f.focusTree) && !db.Focuses.Any(x => x.faction == f.focusTree))
                    w.Add($"[faction] {f.id}: focusTree '{f.focusTree}' не имеет нод фокуса");
                if (!string.IsNullOrEmpty(f.capital) && !provinceIds.Contains(f.capital))
                    w.Add($"[faction] {f.id}: столица '{f.capital}' отсутствует на карте");
            }

            foreach (var era in db.Eras)
                foreach (var fid in era.factions)
                    if (!db.FactionById.ContainsKey(fid)) w.Add($"[era] {era.id}: фракция '{fid}' не существует");

            foreach (var e in db.Events)
            {
                ValidateEffects(db, $"событие {e.id}", e.choices.SelectMany(c => c.effects ?? new List<EffectDef>()));
                foreach (var c in e.choices)
                {
                    foreach (var p in c.cost.Parts())
                        if (p.amount < 0) w.Add($"[event] {e.id}: отрицательная стоимость '{p.res}'");
                    if (c.effects == null || c.effects.Count == 0) w.Add($"[event] {e.id}: у выбора '{c.text}' нет эффектов");
                }
                if (!string.IsNullOrEmpty(e.conditions?.requiresFaith) && !db.FaithById.ContainsKey(e.conditions.requiresFaith))
                    w.Add($"[event] {e.id}: вера '{e.conditions.requiresFaith}' не существует");
            }

            foreach (var q in db.Quests)
            {
                if (!string.IsNullOrEmpty(q.faction) && !db.FactionById.ContainsKey(q.faction))
                    w.Add($"[quest] {q.id}: фракция '{q.faction}' не существует");
                foreach (var s in q.steps)
                {
                    var o = s.objective;
                    if (o != null)
                    {
                        if ((o.type == "controlProvince" || o.type == "buildProvince") && !provinceIds.Contains(o.target))
                            w.Add($"[quest] {q.id}: цель-провинция '{o.target}' не существует");
                        if (o.type == "controlRegion" && !regionIds.Contains(o.target))
                            w.Add($"[quest] {q.id}: цель-регион '{o.target}' не существует");
                        if (o.type == "build" && !db.BuildingById.ContainsKey(o.target))
                            w.Add($"[quest] {q.id}: цель-постройка '{o.target}' не существует");
                        if (o.type == "title" && !db.TitleById.ContainsKey(o.target))
                            w.Add($"[quest] {q.id}: цель-титул '{o.target}' не существует");
                        if (o.type == "tech" && !db.TechById.ContainsKey(o.target))
                            w.Add($"[quest] {q.id}: цель-технология '{o.target}' не существует");
                    }
                    ValidateEffects(db, $"квест {q.id}", s.reward);
                }
            }

            foreach (var t in db.Techs)
            {
                if (t.cost == null) t.cost = new ResourceCost();
                foreach (var p in t.cost.Parts())
                    if (p.amount < 0) w.Add($"[tech] {t.id}: отрицательная стоимость '{p.res}'");
                ValidateEffects(db, $"технология {t.id}", t.effects);
            }

            foreach (var b in db.Buildings)
            {
                if (!string.IsNullOrEmpty(b.requiresTech) && !db.TechById.ContainsKey(b.requiresTech))
                    w.Add($"[building] {b.id}: requiresTech '{b.requiresTech}' не существует");
                ValidateEffects(db, $"постройка {b.id}", b.effects);
                if (string.IsNullOrEmpty(b.slot)) w.Add($"[building] {b.id}: не указан слот");
                if (b.maxPerEmpire <= 0) w.Add($"[building] {b.id}: maxPerEmpire <= 0");
            }

            foreach (var u in db.Units)
            {
                if (!string.IsNullOrEmpty(u.requiresTech) && !db.TechById.ContainsKey(u.requiresTech))
                    w.Add($"[unit] {u.id}: requiresTech '{u.requiresTech}' не существует");
                if (u.cost == null) u.cost = new ResourceCost();
                if (u.upkeep == null) u.upkeep = new ResourceCost();
                foreach (var up in u.upgradesTo)
                    if (!db.UnitById.ContainsKey(up)) w.Add($"[unit] {u.id}: апгрейд '{up}' не существует");
            }

            foreach (var h in db.Heroes)
            {
                if (!string.IsNullOrEmpty(h.house) && !houseIds.Contains(h.house))
                    w.Add($"[hero] {h.id}: дом '{h.house}' не существует");
                if (!string.IsNullOrEmpty(h.dragonUnit) && !db.UnitById.ContainsKey(h.dragonUnit))
                    w.Add($"[hero] {h.id}: дракон '{h.dragonUnit}' не существует");
            }

            foreach (var a in db.Advisors)
            {
                if (!string.IsNullOrEmpty(a.faction) && !db.FactionById.ContainsKey(a.faction) && !db.Factions.Any(f => f.house == a.faction))
                    w.Add($"[advisor] {a.id}: фракция '{a.faction}' не существует");
                foreach (var m in a.passive) if (!IsKnownMod(m.key)) w.Add($"[advisor] {a.id}: неизвестный модификатор '{m.key}'");
                if (a.active != null) ValidateEffects(db, $"советник {a.id}", a.active.effects);
            }

            foreach (var fa in db.Faiths)
            {
                if (!string.IsNullOrEmpty(fa.headProvince) && !provinceIds.Contains(fa.headProvince))
                    w.Add($"[faith] {fa.id}: святыня '{fa.headProvince}' отсутствует на карте");
                foreach (var hs in fa.holySites)
                    if (!provinceIds.Contains(hs)) w.Add($"[faith] {fa.id}: святое место '{hs}' отсутствует на карте");
                foreach (var ten in fa.tenets)
                    foreach (var m in ten.modifiers) if (!IsKnownMod(m.key)) w.Add($"[faith] {fa.id}: неизвестный модификатор '{m.key}'");
            }

            foreach (var fo in db.Focuses)
            {
                if (!string.IsNullOrEmpty(fo.faction) && !db.FactionById.ContainsKey(fo.faction) &&
                    !db.Factions.Any(f => f.house == fo.faction))
                    w.Add($"[focus] {fo.id}: фракция '{fo.faction}' не существует");
                if (fo.faction != "shared" && !db.Factions.Any(f => f.focusTree == fo.faction))
                    w.Add($"[focus] {fo.id}: дерево '{fo.faction}' ни к одной фракции не привязано");
                foreach (var r in fo.requires)
                    if (!db.FocusById.ContainsKey(r)) w.Add($"[focus] {fo.id}: неизвестный пререквизит '{r}'");
                foreach (var x in fo.exclusive)
                    if (!db.FocusById.ContainsKey(x)) w.Add($"[focus] {fo.id}: неизвестный exclusive '{x}'");
                ValidateEffects(db, $"фокус {fo.id}", fo.effects);
            }

            foreach (var l in db.Laws)
            {
                ValidateEffects(db, $"закон {l.id}", l.effects);
                if (l.voteCost <= 0) w.Add($"[law] {l.id}: voteCost <= 0");
            }

            foreach (var t in db.Titles) ValidateEffects(db, $"титул {t.id}", t.effects);

            // карта: провинции ссылаются на дома и регионы
            foreach (var p in db.Map.provinces)
            {
                if (!string.IsNullOrEmpty(p.house) && !houseIds.Contains(p.house))
                    w.Add($"[map] провинция {p.id}: дом '{p.house}' не существует");
                if (!string.IsNullOrEmpty(p.region) && !regionIds.Contains(p.region))
                    w.Add($"[map] провинция {p.id}: регион '{p.region}' не существует");
                foreach (var n in p.neighbors)
                    if (!provinceIds.Contains(n)) w.Add($"[map] провинция {p.id}: сосед '{n}' не существует");
            }
        }

        private static void ValidateEffects(ContentDb db, string where, IEnumerable<EffectDef> effects)
        {
            if (effects == null) return;
            foreach (var e in effects)
            {
                if (e == null) continue;
                switch ((e.op ?? "").ToLowerInvariant())
                {
                    case "recruit":
                    case "spawnarmy":
                        if (!string.IsNullOrEmpty(e.id) && !db.UnitById.ContainsKey(e.id))
                            db.LoadWarnings.Add($"[{where}] неизвестный отряд '{e.id}'");
                        break;
                    case "unlockunit":
                        if (!string.IsNullOrEmpty(e.id) && !db.UnitById.ContainsKey(e.id))
                            db.LoadWarnings.Add($"[{where}] неизвестный отряд '{e.id}'");
                        break;
                    case "unlockbuilding":
                    case "capitalbuilding":
                        if (!string.IsNullOrEmpty(e.id) && !db.BuildingById.ContainsKey(e.id))
                            db.LoadWarnings.Add($"[{where}] неизвестная постройка '{e.id}'");
                        break;
                    case "unlocktech":
                        if (!string.IsNullOrEmpty(e.id) && !db.TechById.ContainsKey(e.id))
                            db.LoadWarnings.Add($"[{where}] неизвестная технология '{e.id}'");
                        break;
                    case "title":
                        if (!string.IsNullOrEmpty(e.id) && !db.TitleById.ContainsKey(e.id))
                            db.LoadWarnings.Add($"[{where}] неизвестный титул '{e.id}'");
                        break;
                    case "granthero":
                        if (!string.IsNullOrEmpty(e.id) && !db.HeroById.ContainsKey(e.id))
                            db.LoadWarnings.Add($"[{where}] неизвестный герой '{e.id}'");
                        break;
                    case "faith":
                        if (!string.IsNullOrEmpty(e.id) && !db.FaithById.ContainsKey(e.id))
                            db.LoadWarnings.Add($"[{where}] неизвестная вера '{e.id}'");
                        break;
                    case "modifier":
                    case "timedmodifier":
                        if (!IsKnownMod(e.key)) db.LoadWarnings.Add($"[{where}] неизвестный модификатор '{e.key}'");
                        break;
                }
            }
        }

        private static readonly HashSet<string> KnownMods = new HashSet<string>
        {
            "goldIncome", "foodIncome", "materialsIncome", "influenceIncome", "pietyIncome",
            "goldIncome.pct", "foodIncome.pct", "materialsIncome.pct", "influenceIncome.pct", "pietyIncome.pct",
            "armyAttack", "armyDefense", "armyMove", "armyMorale", "navalPower", "dragonPower", "heroPower",
            "spyOffense", "spyDefense", "spyNetwork", "siegeSpeed", "raidLoot", "tradeDominance",
            "recognitionGain", "votePower", "levySize", "recruitCost.pct", "upkeep.pct",
            "buildCost.pct", "lobbyCost.pct", "recoveryCost.pct", "researchSpeed",
            "faithSpread", "unrestGlobal.pct", "garrisonBonus", "siegeDefense", "growthBonus"
        };

        private static bool IsKnownMod(string key) =>
            !string.IsNullOrEmpty(key) && KnownMods.Contains(key);
    }
}
