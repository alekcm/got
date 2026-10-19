using System;
using System.Collections.Generic;
using System.Linq;
using IronThrone.Core.Battle;
using IronThrone.Core.Content;
using IronThrone.Core.Game;
using UnityEngine;

namespace IronThrone.Unity
{
    public partial class IronThroneGame
    {
        // ------------------------------------------------------------------ провинция
        private void DrawProvinceTab(FactionState f)
        {
            var p = _state.Province(_selectedProvince);
            if (p == null)
            {
                GUILayout.Label("Выберите провинцию на карте.", _body);
                return;
            }
            GUILayout.Label(p.def.name, _h1);
            GUILayout.Label($"{(RegionName(p.def.region))} · {TerrainName(p.def.terrain)} · уровень {p.def.level}" +
                            (p.def.isCapital ? " · столица региона" : "") + (p.def.port ? " · порт" : ""), _small);
            GUILayout.Label($"Владелец: {_state.FactionName(p.owner)}   Контроль: {_state.FactionName(p.controller)}", _body);
            GUILayout.Label($"Гарнизон: {p.garrison}/{TurnEngine.MaxGarrison(_state, p)}   Беспорядки: {p.unrest:0}%   Развитие: {p.development:0}", _body);
            if (!string.IsNullOrEmpty(p.besieger) && p.controller != p.besieger)
                GUILayout.Label($"Осада: {_state.FactionName(p.besieger)} · пробито {p.siegeProgress:0}/{Math.Max(1, p.garrison)}", _small);
            GUILayout.Label($"Вера: {FaithName(p.dominantFaith)}", _body);

            if (p.controller == f.id)
            {
                GUILayout.Space(6);
                GUILayout.Label("Постройки", _h2);
                if (p.buildings.Count == 0) GUILayout.Label("— пусто —", _small);
                foreach (var b in p.buildings.ToList())
                    GUILayout.Label("• " + BuildingName(b), _body);
                foreach (var kv in p.buildQueue.ToList())
                    GUILayout.Label($"⏳ {BuildingName(kv.Key)} — {kv.Value} ход(ов)", _small);

                GUILayout.Space(6);
                GUILayout.Label("Строительство", _h2);
                bool any = false;
                foreach (var b in _state.Content.Buildings)
                {
                    if (b.slot == TerritorySystem.ImprovementSlot) continue;
                    if (p.buildings.Contains(b.id) || p.buildQueue.ContainsKey(b.id)) continue;
                    if (!string.IsNullOrEmpty(b.uniqueToFaction) && b.uniqueToFaction != f.house) continue;
                    if (!string.IsNullOrEmpty(b.requiresTech) && !f.HasTech(b.requiresTech)) continue;
                    var cost = b.cost.Scaled(f.ModPct(ModKeys.BuildCostPct));
                    any = true;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{b.name} <color=#8a8a8a>[{EconomySystem.SlotName(b.slot)}]</color>", _body);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(cost.ToRu(), _button, GUILayout.Width(170)))
                    {
                        if (EconomySystem.StartBuild(_state, f, p, b.id, out var err)) Toast("Строительство: " + b.name);
                        else Toast(err);
                    }
                    GUILayout.EndHorizontal();
                }
                if (!any) GUILayout.Label("Нет доступных построек — нужны технологии или свободные слоты.", _small);

                GUILayout.Space(8);
                int improvCount = p.buildings.Count(id => _state.Content.BuildingById.TryGetValue(id, out var bd) && bd.slot == TerritorySystem.ImprovementSlot);
                int improvLimit = TerritorySystem.ImprovementLimit(_state, p);
                GUILayout.Label($"Улучшения земли ({improvCount}/{improvLimit})", _h2);
                GUILayout.Label($"Освоение: {f.growth:0}/{TerritorySystem.GrowthThreshold(_state, f)} +{TerritorySystem.GrowthPerTurn(_state, f):0.#}/ход" +
                                (f.growthReady ? " — готово, можно улучшить землю бесплатно" : ""), _small);
                foreach (var id in p.buildings.Where(id => _state.Content.BuildingById.TryGetValue(id, out var bd) && bd.slot == TerritorySystem.ImprovementSlot).ToList())
                    GUILayout.Label("• " + BuildingName(id), _small);
                if (improvCount < improvLimit)
                {
                    foreach (var b in TerritorySystem.Improvements(_state, f, p))
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"{b.name} <color=#8a8a8a>{b.description}</color>", _small);
                        GUILayout.FlexibleSpace();
                        bool free = f.growthReady;
                        var icost = b.cost.Scaled(f.ModPct(ModKeys.BuildCostPct));
                        if (GUILayout.Button(free ? "Освоение" : icost.ToRu(), free ? _tabActive : _button, GUILayout.Width(150)))
                        {
                            if (TerritorySystem.AddImprovement(_state, f, p, b.id, free, out var err))
                            {
                                Toast(free ? "Земля улучшена за освоение: " + b.name : "Улучшение: " + b.name);
                                _map = HexMapTexture.Build(_state, 24);
                            }
                            else Toast(err);
                        }
                        GUILayout.EndHorizontal();
                    }
                }
                else GUILayout.Label("Все улучшения земли уже стоят. Лимит растёт со столицей и присоединением провинций.", _small);

                GUILayout.Space(6);
                GUILayout.Label("Рынок", _h2);
                if (GUILayout.Button($"Закупить припасы — {EconomySystem.SuppliesGoldCost} золота → 120 материалов, 90 еды", _button))
                {
                    if (EconomySystem.BuySupplies(_state, f, out var err)) Toast("Припасы закуплены");
                    else Toast(err);
                }

                GUILayout.Space(6);
                GUILayout.Label("Найм войск", _h2);
                foreach (var u in _state.Content.Units.Where(u => (u.faction == "shared" || u.faction == f.house)))
                {
                    if (!string.IsNullOrEmpty(u.requiresTech) && !f.HasTech(u.requiresTech)) continue;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{u.name} <color=#8a8a8a>{u.kind}</color>  А{u.stats.attack}/З{u.stats.defense}", _body);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(u.cost.ToRu(), _button, GUILayout.Width(150)))
                    {
                        if (MilitarySystem.Recruit(_state, f, u.id, 1, p.id, out var err)) Toast("Набран отряд: " + u.name);
                        else Toast(err);
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(6);
                if (GUILayout.Button("Отправить миссионера (20 золота, 15 веры)", _button))
                {
                    if (FaithSystem.SendMissionary(_state, f, p.id, out var err)) Toast("Миссионер отправлен");
                    else Toast(err);
                }
            }
            else if (p.controller == null)
            {
                GUILayout.Space(8);
                GUILayout.Label("Ничейная земля", _h2);
                GUILayout.Label($"Освоение дома: {f.growth:0}/{TerritorySystem.GrowthThreshold(_state, f)} за ход +{TerritorySystem.GrowthPerTurn(_state, f):0.#}", _body);
                float need = Math.Max(0, TerritorySystem.GrowthThreshold(_state, f) - f.growth);
                if (!f.growthReady)
                    GUILayout.Label($"Нужно ещё {need:0} освоения. Присоединение стоит {TerritorySystem.ClaimCost(_state).ToRu()}.", _small);
                else
                {
                    GUILayout.Label("Освоение готово. Выберите улучшение, которое появится в провинции:", _body);
                    string pick = _claimImprovement;
                    var improvements = TerritorySystem.Improvements(_state, f, p);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(pick == null ? "● Без улучшения" : "○ Без улучшения", pick == null ? _tabActive : _button))
                        _claimImprovement = null;
                    GUILayout.EndHorizontal();
                    foreach (var b in improvements)
                    {
                        if (GUILayout.Button((pick == b.id ? "● " : "○ ") + b.name + " — " + b.description, pick == b.id ? _tabActive : _button))
                            _claimImprovement = b.id;
                    }
                    GUILayout.Space(4);
                    GUI.enabled = EffectEngine.CanAfford(f, TerritorySystem.ClaimCost(_state));
                    if (GUILayout.Button("ПРИСОЕДИНИТЬ ПРОВИНЦИЮ  (" + TerritorySystem.ClaimCost(_state).ToRu() + ")", _tabActive, GUILayout.Height(34)))
                    {
                        if (TerritorySystem.Claim(_state, f, p.id, _claimImprovement, out var err))
                        {
                            Toast("Провинция " + p.def.name + " присоединена");
                            _map = HexMapTexture.Build(_state, 24);
                            _selectedProvince = p.id;
                        }
                        else Toast(err);
                    }
                    GUI.enabled = true;
                    GUILayout.Label("Освоение копится от числа и развития ваших провинций; каждое присоединение тратит накопленное.", _small);
                }
            }
            else if (p.controller != null)
            {
                var enemy = _state.Faction(p.controller);
                GUILayout.Space(8);
                GUILayout.Label("Территория другого дома", _h2);
                bool atWar = DiplomacySystem.WarBetween(_state, f.id, p.controller) != null;
                if (atWar)
                {
                    GUILayout.Label("Вы в состоянии войны.", _small);
                    if (GUILayout.Button("Заключить мир (8 влияния)", _button))
                    {
                        if (DiplomacySystem.MakePeace(_state, f, p.controller)) Toast("Мир заключён");
                        else Toast("Не удалось заключить мир");
                    }
                }
                else if (enemy != null)
                {
                    if (GUILayout.Button("Объявить войну (10 влияния)", _button))
                    {
                        if (DiplomacySystem.DeclareWar(_state, f, p.controller) != null) Toast("Война объявлена");
                        else Toast("Не удалось объявить войну");
                    }
                }
            }
        }

        // ------------------------------------------------------------------ армия
        private void DrawArmyTab(FactionState f)
        {
            var armies = f.armies.Select(id => _state.Armies.TryGetValue(id, out var a) ? a : null)
                .Where(a => a != null && a.alive).ToList();
            if (armies.Count == 0)
            {
                GUILayout.Label("У вас нет армий. Нанимайте войска в своих провинциях.", _body);
                return;
            }
            GUILayout.Label("Армии дома", _h1);
            foreach (var a in armies)
            {
                var prov = _state.Province(a.province);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button((_selectedArmy == a.id ? "● " : "○ ") + a.name, _selectedArmy == a.id ? _tabActive : _button))
                {
                    _selectedArmy = a.id;
                    _moveMode = false;
                }
                GUILayout.Label($"{prov?.def.name ?? "в пути"} · {MilitarySystem.EstimateStrength(a)} силы · {a.UnitCount} отр.", _small);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(8);

            var army = _selectedArmy != null && _state.Armies.TryGetValue(_selectedArmy, out var sel) ? sel : null;
            if (army == null) { GUILayout.Label("Выберите армию.", _small); return; }

            GUILayout.Label(army.name, _h2);
            GUILayout.Label($"Движение: {army.movementLeft}/{army.maxMovement} · припасы {army.supply}% · строй: {StanceName(army.stance)}", _small);

            // герой армии
            GUILayout.Space(4);
            var hero = MilitarySystem.Hero(_state, army.heroId);
            if (hero != null)
            {
                string sig = MilitarySystem.HeroSignature(_state, hero);
                var hdef = MilitarySystem.HeroDef(_state, hero);
                GUILayout.Label($"<b>Герой: {hero.name}</b> — ур. {hero.level}" +
                                (sig != null ? $" · в бою: «{BattleEngine.HeroAbilityName(sig)}»" : ""), _body);
                if (hdef != null && !string.IsNullOrEmpty(hdef.traitText)) GUILayout.Label(hdef.traitText, _small);
                if (GUILayout.Button("Отозвать героя ко двору", _button, GUILayout.Width(240)))
                {
                    if (MilitarySystem.ReleaseHeroFromArmy(_state, f, hero.defId, out var herr)) Toast(hero.name + " вернулся ко двору");
                    else Toast(herr);
                }
            }
            else GUILayout.Label("Герой не назначен: в бою армия теряет способность героя и его бонусы.", _small);

            var freeHeroes = f.heroes
                .Select(id => MilitarySystem.Hero(_state, id))
                .Where(h => h != null && h.alive && h.defId != army.heroId)
                .ToList();
            if (freeHeroes.Count > 0)
            {
                GUILayout.Label("Назначить героя (один на армию):", _small);
                foreach (var h in freeHeroes)
                {
                    string where = h.armyId != null
                        ? "в " + (_state.Armies.TryGetValue(h.armyId, out var ha) ? ha.name : "другой армии")
                        : h.wounded ? $"ранен ({Math.Max(1, h.woundedTurns)})"
                        : h.reassignCooldown > 0 ? $"перестроение {h.reassignCooldown}" : "при дворе";
                    bool can = !h.wounded && h.reassignCooldown <= 0;
                    GUI.enabled = can;
                    if (GUILayout.Button($"{h.name} — {where}", _button))
                    {
                        if (MilitarySystem.AssignHero(_state, f, h.defId, army.id, out var aerr))
                            Toast(h.name + " возглавил " + army.name);
                        else Toast(aerr);
                    }
                    GUI.enabled = true;
                }
            }

            foreach (var u in army.units.Where(u => u.alive))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"• {u.name}: HP {u.hp:0}%, мораль {u.morale:0}%, ур. {u.level}, спец. {u.upgradeLevel}/2", _body);
                GUILayout.FlexibleSpace();
                var nextUnit = MilitarySystem.NextUpgrade(_state, f, u);
                if (nextUnit != null)
                {
                    var ucost = MilitarySystem.UpgradeCost(u, nextUnit);
                    if (GUILayout.Button("→ " + nextUnit.name + "  " + ucost.ToRu(), _button, GUILayout.Width(200)))
                    {
                        if (MilitarySystem.UpgradeUnitType(_state, f, army, u, out var uerr)) Toast("Отряд переформирован: " + nextUnit.name);
                        else Toast(uerr);
                    }
                }
                if (u.upgradeLevel < 2)
                {
                    var cost = new ResourceCost { gold = 90 * (u.upgradeLevel + 1), materials = 50 * (u.upgradeLevel + 1) };
                    if (GUILayout.Button("Спец. " + cost.ToRu(), _button, GUILayout.Width(170)))
                    {
                        if (MilitarySystem.UpgradeUnit(_state, f, army, u, out var err)) Toast("Отряд специализирован");
                        else Toast(err);
                    }
                }
                GUILayout.EndHorizontal();
                if (_state.Content.UnitById.TryGetValue(u.defId, out var udef))
                {
                    string tags = UnitTraitText(udef);
                    if (!string.IsNullOrEmpty(tags)) GUILayout.Label("    " + tags, _small);
                }
            }
            foreach (var u in army.units.Where(u => !u.alive))
                GUILayout.Label($"✝ {u.name} — уничтожен", _small);

            GUILayout.Space(8);
            GUILayout.Label("Перестроения", _h2);
            var samePlace = armies.Where(x => x.id != army.id && x.alive && x.province == army.province).ToList();
            foreach (var other in samePlace)
            {
                if (!other.alive) continue;
                if (GUILayout.Button($"Влить «{other.name}» ({MilitarySystem.AliveUnits(other)} отр.) в эту армию", _button))
                {
                    if (MilitarySystem.MergeArmies(_state, f, army.id, other.id, out var merr)) Toast("Армии объединены");
                    else Toast(merr);
                }
            }
            int aliveUnits = MilitarySystem.AliveUnits(army);
            bool canSplit = aliveUnits >= 2 && f.armies.Count < _state.Settings.maxArmies;
            GUILayout.BeginHorizontal();
            GUI.enabled = canSplit;
            if (GUILayout.Button("Отделить 1 отряд", _button))
            {
                var split = MilitarySystem.SplitArmy(_state, f, army, 1, out var serr);
                Toast(split != null ? "Создана армия " + split.name + " (без движения в этот ход)" : serr);
            }
            if (aliveUnits >= 4 && GUILayout.Button($"Отделить половину ({aliveUnits / 2})", _button))
            {
                var split = MilitarySystem.SplitArmy(_state, f, army, aliveUnits / 2, out var serr);
                Toast(split != null ? "Создана армия " + split.name : serr);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (aliveUnits < 2)
                GUILayout.Label("Для разделения нужно хотя бы два отряда в армии.", _small);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(army.stance == "defend" ? "Перейти к наступлению" : "Занять оборону", _button))
            {
                if (army.stance == "ambush") army.stance = "defend";
                else army.stance = army.stance == "defend" ? "move" : "defend";
            }
            if (GUILayout.Button("Двигаться (клик по карте)", _button))
            {
                _moveMode = true;
                Toast("Выберите соседнюю провинцию на карте.");
            }
            GUILayout.EndHorizontal();

            // засада: скрытая позиция в лесу, горах, холмах, болотах или снегах
            GUILayout.Space(4);
            if (army.stance == "ambush")
            {
                GUILayout.Label("Армия в засаде: враг не увидит её, пока не войдёт в провинцию. Свежая разведка противника раскрывает засаду.", _small);
                if (GUILayout.Button("Снять засаду", _button, GUILayout.Width(200)))
                {
                    MilitarySystem.LeaveAmbush(army);
                    Toast("Армия вышла из засады");
                }
            }
            else
            {
                bool canAmbush = MilitarySystem.CanAmbush(_state, f, army, out var ambErr);
                GUI.enabled = canAmbush;
                if (GUILayout.Button("Устроить засаду", _button, GUILayout.Width(200)))
                {
                    if (MilitarySystem.SetAmbush(_state, f, army, out var serr2)) Toast("Армия укрылась: засада готова");
                    else Toast(serr2);
                }
                GUI.enabled = true;
                if (!canAmbush) GUILayout.Label(ambErr, _small);
            }

            GUILayout.Space(6);
            GUILayout.Label("Соседние провинции", _h2);
            var cur = _state.Province(army.province);
            if (cur == null) return;
            var neighbors = cur.def.neighbors
                .Concat(SeaNeighbors(army.province)).Distinct()
                .Select(id => _state.Province(id)).Where(p => p != null).ToList();
            foreach (var n in neighbors)
            {
                bool can = MilitarySystem.CanMove(_state, army, n.id, out var reason);
                GUILayout.BeginHorizontal();
                GUILayout.Label(n.def.name + " — " + _state.FactionName(n.controller), _body);
                GUILayout.FlexibleSpace();
                GUI.enabled = can;
                if (GUILayout.Button("Идти", _button, GUILayout.Width(80)))
                {
                    if (MilitarySystem.MoveArmy(_state, army, n.id, out var msg)) { Toast(msg ?? "Армия выдвинулась"); _tab = 0; _selectedProvince = n.id; }
                    else Toast(msg);
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                if (!can) GUILayout.Label("  " + reason, _small);
            }
        }

        private IEnumerable<string> SeaNeighbors(string provinceId)
        {
            var p = _state.Province(provinceId);
            if (p == null || !p.def.port) yield break;
            foreach (var route in _state.Map.seaRoutes)
            {
                if (route.Count < 2) continue;
                if (route[0] == provinceId) yield return route[1];
                else if (route[1] == provinceId) yield return route[0];
            }
        }

        private static string StanceName(string stance) => stance switch
        {
            "defend" => "оборона",
            "raid" => "набег",
            "siege" => "осада",
            "ambush" => "засада",
            _ => "поход",
        };

        // ------------------------------------------------------------------ двор
        private void DrawCourtTab(FactionState f)
        {
            if (f.def.passive != null && !string.IsNullOrEmpty(f.def.passive.name))
                GUILayout.Label($"<b>Пассивка дома: {f.def.passive.name}</b> — {f.def.passive.text}", _body);
            GUILayout.Label("Малый совет", _h1);
            foreach (var title in _state.Content.Titles)
            {
                _state.Council.seats.TryGetValue(title.id, out var holderId);
                string holder = string.IsNullOrEmpty(holderId) ? "вакантно" : _state.FactionName(holderId);
                GUILayout.Label($"<b>{title.name}</b> — {holder}  <color=#8a8a8a>({title.shortEffect})</color>", _body);
            }
            if (_state.Council.throneHolder == f.id)
            {
                GUILayout.Label("Вы владеете Железным троном и раздаёте титулы (25 влияния).", _small);
                foreach (var title in _state.Content.Titles)
                {
                    _state.Council.seats.TryGetValue(title.id, out var holderId);
                    if (holderId == f.id) continue;
                    if (GUILayout.Button("Назначить: " + title.name, _button))
                    {
                        if (PoliticsSystem.GrantTitle(_state, title.id, f.id, out var err)) Toast("Титул выдан: " + title.name);
                        else Toast(err);
                    }
                }
            }
            GUILayout.Space(8);
            GUILayout.Label("Советники", _h2);
            foreach (var a in f.advisors)
            {
                var def = _state.Content.AdvisorById.TryGetValue(a.defId, out var d) ? d : null;
                if (def == null) continue;
                GUILayout.Label($"<b>{def.name}</b> — {def.title}" + (a.cooldown > 0 ? $" (перезарядка {a.cooldown})" : ""), _body);
                GUILayout.Label(def.description, _small);
                foreach (var m in def.passive) GUILayout.Label("  пассивно: " + ModText(m.key, m.value), _small);
                if (def.active != null)
                {
                    GUI.enabled = a.cooldown <= 0;
                    if (GUILayout.Button($"Умение: {def.active.name} ({def.active.cost.ToRu()})", _button))
                    {
                        if (ProgressSystem.UseAdvisor(_state, f, def.id, out var err)) Toast("Советник: " + def.active.name);
                        else Toast(err);
                    }
                    GUI.enabled = true;
                }
            }

            GUILayout.Space(8);
            GUILayout.Label("Герои", _h2);
            foreach (var hid in f.heroes)
            {
                if (!_state.Heroes.TryGetValue(hid, out var hero)) continue;
                var def = _state.Content.HeroById.TryGetValue(hid, out var hd) ? hd : null;
                if (def == null) continue;
                string where = hero.armyId != null && _state.Armies.TryGetValue(hero.armyId, out var ha)
                    ? _state.Province(ha.province)?.def.name ?? "в походе" : "при дворе";
                GUILayout.Label($"<b>{hero.name}</b> — ур. {hero.level}, {where}" + (hero.wounded ? ", ранен" : ""), _body);
                GUILayout.Label($"{def.traitText} · А{def.stats.attack}/З{def.stats.defense}", _small);
            }

            GUILayout.Space(8);
            GUILayout.Label("Столичный ансамбль", _h2);
            var capProv = _state.Province(f.capital);
            if (capProv == null || capProv.controller != f.id)
                GUILayout.Label("Столица потеряна — ансамбль не действует.", _small);
            else
            {
                var slots = EconomySystem.SlotsFor(_state, capProv);
                foreach (var group in slots.GroupBy(s => s).OrderBy(g => g.Key))
                {
                    int used = EconomySystem.UsedSlots(_state, capProv, group.Key);
                    GUILayout.Label($"{EconomySystem.SlotName(group.Key)}: {used}/{group.Count()}", _small);
                }
                var bonus = EconomySystem.CapitalSetBonus(_state, f, out _, out _, out _, out _);
                GUILayout.Label(bonus ?? "Заполните однотипные слоты столицы, чтобы получить бонус (3 экономики, 3 военных, 2 храма, 2 порта, 2 особых).", _small);
                if (GUILayout.Button("Открыть столицу для строительства", _button))
                {
                    _selectedProvince = capProv.id;
                    _tab = 0;
                }
            }

            GUILayout.Space(8);
            GUILayout.Label("Действующие законы", _h2);
            if (_state.ActiveLaws.Count == 0) GUILayout.Label("Совет пока ничего не принял.", _small);
            foreach (var ls in _state.ActiveLaws.ToList())
            {
                if (!_state.Content.LawById.TryGetValue(ls.id, out var activeLaw)) continue;
                GUILayout.Label($"<b>{activeLaw.name}</b> — {activeLaw.description}", _body);
                GUILayout.Label($"Спонсор: {_state.FactionName(ls.sponsor)}" +
                                (activeLaw.repealable ? "" : " · отмена невозможна"), _small);
                if (activeLaw.repealable)
                {
                    GUI.enabled = PoliticsSystem.CanRepeal(_state, f, activeLaw, out _);
                    if (GUILayout.Button($"Отменить закон ({PoliticsSystem.RepealCost(activeLaw)} влияния)", _button))
                    {
                        var rv = PoliticsSystem.RepealLaw(_state, f, activeLaw.id, out var rerr);
                        if (rv != null) Toast(_state.PendingVote != null ? "Голосование об отмене началось" : rerr ?? "Голосование началось");
                        else Toast(rerr);
                    }
                    GUI.enabled = true;
                }
            }

            GUILayout.Space(8);
            GUILayout.Label("Законы", _h2);
            foreach (var law in PoliticsSystem.AvailableLaws(_state, f))
            {
                GUILayout.Label($"<b>{law.name}</b> — {law.description}", _body);
                GUI.enabled = PoliticsSystem.CanPropose(_state, f, law, out _);
                if (GUILayout.Button($"Вынести на совет ({law.voteCost} влияния)", _button))
                {
                    var vote = PoliticsSystem.ProposeLaw(_state, f, law.id, out var err);
                    if (vote != null) Toast("Голосование началось: " + law.name);
                    else Toast(err);
                }
                GUI.enabled = true;
            }
        }

        // ------------------------------------------------------------------ технологии
        private void DrawTechTab(FactionState f)
        {
            GUILayout.Label("Исследования", _h1);
            if (!string.IsNullOrEmpty(f.currentResearch) &&
                _state.Content.TechById.TryGetValue(f.currentResearch, out var cur))
            {
                f.research.TryGetValue(cur.id, out var turns);
                GUILayout.Label($"Изучается: <b>{cur.name}</b> — осталось {turns} ход(ов)", _body);
            }
            else GUILayout.Label("Исследование не выбрано.", _small);

            GUILayout.Space(6);
            foreach (var t in ProgressSystem.AvailableTechs(_state, f).OrderBy(t => t.tier).ThenBy(t => t.branch))
            {
                GUILayout.Label($"<b>{t.name}</b> <color=#8a8a8a>[{BranchName(t.branch)}, ур. {t.tier}]</color> — {t.description}", _body);
                GUILayout.Label($"стоимость: {t.cost.ToRu()} · {t.researchTurns} ход(ов)", _small);
                GUI.enabled = string.IsNullOrEmpty(f.currentResearch);
                if (GUILayout.Button("Изучать", _button))
                {
                    if (ProgressSystem.StartResearch(_state, f, t.id, out var err)) Toast("Исследование: " + t.name);
                    else Toast(err);
                }
                GUI.enabled = true;
            }
        }

        // ------------------------------------------------------------------ фокусы
        private void DrawFocusTab(FactionState f)
        {
            GUILayout.Label("Национальные фокусы", _h1);
            if (!string.IsNullOrEmpty(f.currentFocus) && _state.Content.FocusById.TryGetValue(f.currentFocus, out var cur))
                GUILayout.Label($"Выполняется: <b>{cur.name}</b> — {f.focusTurnsLeft} ход(ов)", _body);
            else GUILayout.Label("Фокус не выбран. Фокусы дают постоянные бонусы дому.", _small);

            GUILayout.Space(6);
            foreach (var fo in ProgressSystem.FocusesOf(_state, f).OrderBy(x => x.branch).ThenBy(x => x.y))
            {
                bool done = f.completedFocuses.Contains(fo.id);
                bool available = ProgressSystem.IsFocusAvailable(_state, f, fo, out var reason);
                GUILayout.Label((done ? "✓ " : available ? "○ " : "🔒 ") + $"<b>{fo.name}</b> <color=#8a8a8a>[{BranchName(fo.branch)}]</color>", _body);
                GUILayout.Label(fo.description + $" · {fo.turns} ход(ов)" + (string.IsNullOrEmpty(fo.rewardText) ? "" : " · " + fo.rewardText), _small);
                if (fo.exclusive.Count > 0)
                {
                    var alternatives = fo.exclusive
                        .Where(id => _state.Content.FocusById.ContainsKey(id))
                        .Select(id => _state.Content.FocusById[id].name).ToList();
                    bool taken = fo.exclusive.Any(id => f.completedFocuses.Contains(id));
                    if (taken) GUILayout.Label("    Путь закрыт: вы уже выбрали «" + string.Join("», «", alternatives) + "».", _small);
                    else GUILayout.Label("    Взаимоисключающий выбор: " + string.Join(" / ", alternatives), _small);
                }
                if (!done && available && string.IsNullOrEmpty(f.currentFocus))
                {
                    if (GUILayout.Button("Начать фокус", _button))
                    {
                        if (ProgressSystem.StartFocus(_state, f, fo.id, out var err)) Toast("Фокус: " + fo.name);
                        else Toast(err);
                    }
                }
                else if (!done && !available) GUILayout.Label("  " + reason, _small);
            }
        }

        // ------------------------------------------------------------------ дома
        private void DrawHousesTab(FactionState f)
        {
            GUILayout.Label("Великие дома", _h1);
            if (_state.CoalitionTarget != null)
            {
                var target = _state.Faction(_state.CoalitionTarget);
                string role = target != null && target.id == f.id ? "против вас" : "против " + (target?.def.name ?? _state.CoalitionTarget);
                GUILayout.Label($"<color=#d0a050>Коалиция {role}</color> — до хода {_state.CoalitionUntil}", _body);
            }
            foreach (var other in _state.Factions.Values.Where(x => x.id != f.id && x.alive))
            {
                int rel = (int)DiplomacySystem.Relation(_state, f.id, other.id);
                bool war = DiplomacySystem.WarBetween(_state, f.id, other.id) != null;
                int pact = DiplomacySystem.PactLeft(_state, f.id, other.id);
                bool married = f.marriages.Contains(other.id);
                string tags = (war ? " <color=#c05050>· война</color>" : "") +
                              (pact > 0 ? $" <color=#70a0d0>· пакт {pact}</color>" : "") +
                              (married ? " <color=#c090c0>· брак</color>" : "");
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{other.def.name}</b> — отношение {rel}" + tags, _body);
                GUILayout.FlexibleSpace();
                if (war)
                {
                    if (GUILayout.Button("Мир", _button, GUILayout.Width(70)))
                    {
                        if (DiplomacySystem.MakePeace(_state, f, other.id)) Toast("Мир с " + other.def.name);
                        else Toast("Не удалось");
                    }
                }
                else
                {
                    if (GUILayout.Button("Дары 100", _button, GUILayout.Width(90)))
                    {
                        if (DiplomacySystem.SendGift(_state, f, other.id, out var gerr)) Toast("Дары отправлены: +10 отношений");
                        else Toast(gerr);
                    }
                    if (pact > 0)
                    {
                        if (GUILayout.Button("Разорвать пакт", _button, GUILayout.Width(110)))
                        {
                            if (DiplomacySystem.BreakPact(_state, f, other.id, out var berr)) Toast("Пакт разорван");
                            else Toast(berr);
                        }
                    }
                    else if (GUILayout.Button("Пакт 6", _button, GUILayout.Width(80)))
                    {
                        if (DiplomacySystem.MakePact(_state, f, other.id, out var perr)) Toast("Пакт о ненападении на 10 ходов");
                        else Toast(perr);
                    }
                    if (!married && GUILayout.Button("Брак 300+8", _button, GUILayout.Width(100)))
                    {
                        if (DiplomacySystem.Marry(_state, f, other.id, out var merr)) Toast("Династический брак заключён");
                        else Toast(merr);
                    }
                    if (GUILayout.Button("Война", _button, GUILayout.Width(70)))
                    {
                        if (DiplomacySystem.DeclareWar(_state, f, other.id) != null) Toast("Война с " + other.def.name);
                        else Toast("Не удалось");
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("Лагеря набежчиков", _h2);
            var camps = _state.RaiderCamps.Where(c => !c.cleared).Take(8).ToList();
            if (camps.Count == 0) GUILayout.Label("Угроз нет — лагеря разорены.", _small);
            foreach (var camp in camps)
            {
                var cp = _state.Province(camp.province);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{camp.name}</b> — {cp?.def.name} · сила {camp.strength}", _body);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Очистить", _button, GUILayout.Width(90)))
                {
                    if (RaiderSystem.ClearCamp(_state, f, camp.id, out var msg)) Toast(msg);
                    else Toast(msg);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("Малые дома и вольные города", _h2);
            GUILayout.Label("Отношения растут дарами и браками. Вассалитет требует 70, аннексия — 85.", _small);

            var houses = _state.MinorHouses.Values
                .Where(m => !m.annexed)
                .OrderByDescending(m => MinorHouseSystem.Relation(_state, f, m.house))
                .Take(18).ToList();
            foreach (var mh in houses)
            {
                var h = _state.Content.HouseById.TryGetValue(mh.house, out var hd) ? hd : null;
                int rel = MinorHouseSystem.Relation(_state, f, mh.house);
                string status = mh.vassalOf == f.id ? "вассал" : mh.vassalOf != null ? "под " + _state.FactionName(mh.vassalOf) : "вольный";
                GUILayout.Label($"<b>{h?.name ?? mh.house}</b> — {status}, отношение {rel}" + (mh.hostile ? ", враждебен" : ""), _body);
                var actions = MinorHouseSystem.ActionsFor(_state, f, mh.house);
                GUILayout.BeginHorizontal();
                foreach (var action in new[] { "gift", "trade", "marry", "vassalize", "annex" })
                {
                    if (!actions.Contains(action)) continue;
                    var cost = MinorHouseSystem.ActionCost(action);
                    if (GUILayout.Button(ActionName(action) + " (" + cost.ToRu() + ")", _button, GUILayout.Height(24)))
                    {
                        var res = MinorHouseSystem.Perform(_state, f, mh.house, action, out var err);
                        Toast(res ?? err ?? "Недоступно");
                    }
                }
                GUILayout.EndHorizontal();
            }
            if (houses.Count == 0) GUILayout.Label("Все малые дома аннексированы или отсутствуют.", _small);

            var myRequests = _state.MinorRequests.Where(r => r.faction == f.id).ToList();
            GUILayout.Space(8);
            GUILayout.Label("Просьбы малых домов", _h2);
            if (myRequests.Count == 0)
                GUILayout.Label("Пока никто не просит помощи. Просьбы появляются по мере роста отношений.", _small);
            foreach (var req in myRequests)
            {
                var cost = MinorHouseSystem.RequestCost(req.type);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{_state.HouseName(req.house)}</b> {MinorHouseSystem.RequestTitle(req.type)} · " +
                                $"{cost.ToRu()} · осталось {req.turnsLeft} х.", _body);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Помочь", _button, GUILayout.Width(80)))
                {
                    if (MinorHouseSystem.AcceptRequest(_state, f, req, out var rerr))
                        Toast($"+{MinorHouseSystem.RequestReward(req.type)} отношений с домом {_state.HouseName(req.house)}");
                    else Toast(rerr);
                }
                if (GUILayout.Button("Отказать", _button, GUILayout.Width(80)))
                {
                    if (MinorHouseSystem.DeclineRequest(_state, f, req, out var rerr)) Toast("Отказ: −6 отношений");
                    else Toast(rerr);
                }
                GUILayout.EndHorizontal();
            }
        }

        private static string ActionName(string action) => action switch
        {
            "gift" => "Дары",
            "trade" => "Торговля",
            "marry" => "Брак",
            "hire" => "Наёмники",
            "threaten" => "Угроза",
            "vassalize" => "Вассалитет",
            "annex" => "Аннексия",
            "call_banners" => "Знамёна",
            _ => action,
        };

        // ------------------------------------------------------------------ вера
        private void DrawFaithTab(FactionState f)
        {
            var faith = _state.Content.FaithById.TryGetValue(f.faith ?? "", out var fa) ? fa : null;
            GUILayout.Label("Вера", _h1);
            if (faith != null)
            {
                GUILayout.Label($"<b>{faith.name}</b>", _body);
                GUILayout.Label(faith.description, _small);
                GUILayout.Label("Бонус: " + faith.bonusText, _small);
            }
            float share = FaithSystem.FaithShare(_state, f);
            GUILayout.Label($"Доминирует в {share:P0} провинций · глава веры: {(FaithSystem.IsHead(_state, f) ? "вы" : "нет")}", _body);
            if (faith != null)
            {
                foreach (var site in faith.holySites)
                {
                    var p = _state.Province(site);
                    GUILayout.Label("• " + (p?.def.name ?? site) + " — " + _state.FactionName(p?.controller), _small);
                }
            }
            GUILayout.Space(6);
            GUI.enabled = !FaithSystem.IsHead(_state, f);
            if (GUILayout.Button("Реформа веры (120 веры, контроль святого места)", _button))
            {
                if (FaithSystem.ReformFaith(_state, f, out var err)) Toast("Вера реформирована!");
                else Toast(err);
            }
            GUI.enabled = true;

            if (f.HasFlag("faith_reformed"))
            {
                GUILayout.Space(6);
                var chosen = FaithSystem.Dogma(f.dogma);
                if (chosen != null)
                {
                    GUILayout.Label($"Догмат: <b>{chosen.name}</b> — {chosen.description}", _body);
                }
                else
                {
                    GUILayout.Label("Выберите догмат веры (один раз, навсегда):", _h2);
                    foreach (var d in FaithSystem.Dogmas)
                    {
                        if (GUILayout.Button($"{d.name} — {d.description}", _button))
                        {
                            if (FaithSystem.ChooseDogma(_state, f, d.id, out var derr)) Toast("Догмат: " + d.name);
                            else Toast(derr);
                        }
                    }
                }
            }

            GUILayout.Space(8);
            GUILayout.Label("Распространение", _h2);
            foreach (var (fid, sh, isPlayer) in FaithSystem.FaithBreakdown(_state, f).OrderByDescending(x => x.share).Take(8))
                GUILayout.Label($"{(isPlayer ? "★ " : "")}{FaithName(fid)} — {sh:P0}", _body);

            if (_selectedProvince != null)
            {
                var p = _state.Province(_selectedProvince);
                if (p != null)
                {
                    GUILayout.Space(8);
                    GUILayout.Label("Выбранная провинция: " + p.def.name, _h2);
                    foreach (var kv in p.faith.OrderByDescending(x => x.Value).Take(4))
                        GUILayout.Label($"{FaithName(kv.Key)} — {kv.Value:0}%", _small);
                    if (GUILayout.Button("Отправить миссионера (20 золота, 15 веры)", _button))
                    {
                        if (FaithSystem.SendMissionary(_state, f, p.id, out var err)) Toast("Миссионер отправлен");
                        else Toast(err);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ журнал и цели
        // ------------------------------------------------------------------ интриги
        private void DrawIntrigueTab(FactionState f)
        {
            GUILayout.Label("Шпионаж", _h1);
            var enemies = _state.Factions.Values.Where(x => x.id != f.id && x.alive).ToList();
            if (_spyTarget == null || !enemies.Any(e => e.id == _spyTarget))
                _spyTarget = enemies.OrderByDescending(e => e.recognition + e.tradeDominance).FirstOrDefault()?.id;
            GUILayout.Label($"Сеть шептунов: {f.spyNetwork} · нападение {EspionageSystem.Offense(_state, f)} · " +
                            $"защита {EspionageSystem.Defense(_state, f)}", _body);
            GUILayout.Label("Цель:", _h2);
            foreach (var e in enemies)
            {
                if (GUILayout.Button((_spyTarget == e.id ? "● " : "○ ") + e.def.name, _spyTarget == e.id ? _tabActive : _button))
                    _spyTarget = e.id;
            }
            var target = _state.Faction(_spyTarget);
            if (target == null) return;

            GUILayout.Space(6);
            GUILayout.Label("Донесения", _h2);
            var intel = EspionageSystem.Intel(_state, f.id, _spyTarget);
            if (intel == null)
                GUILayout.Label("Сведений нет. Отправьте миссию «Разведка» — донесение живёт 20 ходов.", _small);
            else
            {
                bool fresh = EspionageSystem.HasFreshIntel(_state, f.id, _spyTarget);
                GUILayout.Label(fresh
                    ? $"Свежие данные (ход {intel.turn}): +1 к атаке в бою против этого дома."
                    : $"Донесение устарело (ход {intel.turn}) — бонуса в бою нет, нужна новая разведка.", _small);
                GUILayout.Label(EspionageSystem.IntelReport(_state, intel), _small);
            }

            GUILayout.Space(6);
            foreach (var mission in EspionageSystem.Missions)
            {
                string extra = "";
                if (mission.targetKind == "province")
                {
                    var p = _state.Province(_selectedProvince);
                    if (p == null || p.controller != target.id) continue;
                    extra = " · " + p.def.name;
                }
                if (mission.targetKind == "hero" && target.heroes.Count == 0) continue;
                GUILayout.Label($"<b>{mission.name}</b>{extra} — {mission.description}", _body);
                GUILayout.Label($"стоимость: {mission.cost.ToRu()} · шанс ≈ {mission.baseChance:P0}", _small);
                GUI.enabled = EffectEngine.CanAfford(f, mission.cost);
                if (GUILayout.Button("Выполнить", _button))
                {
                    string prov = mission.targetKind == "province" ? _selectedProvince : null;
                    string hero = mission.targetKind == "hero" ? target.heroes.FirstOrDefault() : null;
                    var res = EspionageSystem.Execute(_state, f, mission.id, target.id, prov, hero);
                    Toast(res.text);
                }
                GUI.enabled = true;
            }
        }

        private void DrawJournalTab(FactionState f)
        {
            GUILayout.Label("Цели победы", _h1);
            foreach (var progress in VictorySystem.Evaluate(_state, f))
            {
                GUILayout.Label($"<b>{VictorySystem.VictoryName(progress.victoryType)}</b> — {progress.value:0}/{progress.target:0}", _body);
                GUILayout.Label(progress.description, _small);
            }

            GUILayout.Space(8);
            GUILayout.Label("Квесты дома", _h2);
            if (_state.Quests.Count == 0) GUILayout.Label("Нет активных квестов.", _small);
            foreach (var qs in _state.Quests)
            {
                if (qs.completed || qs.failed) continue;
                if (!_state.Content.QuestById.TryGetValue(qs.id, out var qd)) continue;
                GUILayout.Label($"<b>{qd.name}</b> — шаг {Math.Min(qs.step + 1, qd.steps.Count)}/{qd.steps.Count}", _body);
                if (qs.step < qd.steps.Count) GUILayout.Label(qd.steps[qs.step].text, _small);
            }

            GUILayout.Space(8);
            GUILayout.Label("Хроника", _h2);
            var entries = _state.Log.Skip(Math.Max(0, _state.Log.Count - 40)).Reverse().ToList();
            foreach (var e in entries)
            {
                string color = e.category == "battle" || e.category == "war" ? "#c98a6a" :
                    e.category == "victory" ? "#e5c86a" :
                    e.category == "event" ? "#8fb0c9" : "#b8b8b0";
                GUILayout.Label($"<color={color}>[{e.turn}] {e.text}</color>", _small);
            }
        }

        // ------------------------------------------------------------------ имена
        private string BuildingName(string id) =>
            _state.Content.BuildingById.TryGetValue(id, out var b) ? b.name : id;

        private string RegionName(string id) =>
            _state.Map.RegionById.TryGetValue(id ?? "", out var r) ? r.name : id;

        private static string TerrainName(string t) => t switch
        {
            "plains" => "равнины",
            "forest" => "лес",
            "hills" => "холмы",
            "mountains" => "горы",
            "desert" => "пустыня",
            "marsh" => "болота",
            "snow" => "снега",
            "river" => "река",
            _ => t ?? "—",
        };

        private static string UnitTraitText(UnitDef def)
        {
            var parts = new List<string>();
            foreach (var tr in def.traits ?? new List<string>())
            {
                switch (tr)
                {
                    case "reave": parts.Add("грабёж: после победы берёт добычу"); break;
                    case "amphibious": parts.Add("морская пехота: сильнее на побережье"); break;
                    case "disciplined": parts.Add("дисциплина: выше мораль и стойкость"); break;
                    case "fanatic": parts.Add("фанатики: ярость при высокой морали"); break;
                    default: parts.Add(tr); break;
                }
            }
            if (!string.IsNullOrEmpty(def.terrainBonus))
                parts.Add("родная местность: " + def.terrainBonus.Replace(",", ", "));
            if (def.upkeep != null && def.upkeep.gold > 0)
                parts.Add($"содержание {def.upkeep.gold} золота/ход");
            return string.Join(" · ", parts);
        }

        private static string BranchName(string b) => b switch
        {
            "war" => "война",
            "economy" => "экономика",
            "intrigue" => "интриги",
            "faith" => "вера",
            "power" => "власть",
            "defense" => "оборона",
            "navy" => "флот",
            _ => b,
        };

        private static string ModText(string key, float value) => key switch
        {
            "goldIncome" => $"+{value:0} золота/ход",
            "foodIncome" => $"+{value:0} еды/ход",
            "materialsIncome" => $"+{value:0} материалов/ход",
            "influenceIncome" => $"+{value:0} влияния/ход",
            "pietyIncome" => $"+{value:0} веры/ход",
            "armyAttack" => $"+{value:0} атаке армий",
            "armyDefense" => $"+{value:0} обороне армий",
            "armyMorale" => $"+{value:0} морали",
            "spyOffense" => $"+{value:0} шпионажу",
            "spyDefense" => $"+{value:0} контрразведке",
            "navalPower" => $"+{value:0} флоту",
            "dragonPower" => $"+{value:P0} драконам",
            "buildCost.pct" => $"{value:P0} к стоимости строек",
            "recruitCost.pct" => $"{value:P0} к стоимости найма",
            "unrestGlobal.pct" => $"{value:P0} к беспорядкам",
            _ => $"{key} {value:+0.##;-0.##}",
        };
    }
}
