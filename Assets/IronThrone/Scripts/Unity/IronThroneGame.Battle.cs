using System;
using System.Collections.Generic;
using System.Linq;
using IronThrone.Core.Battle;
using IronThrone.Core.Game;
using UnityEngine;

namespace IronThrone.Unity
{
    /// <summary>
    /// Ручной гексагональный бой. Вызывается, когда игрок двигает армию в провинцию,
    /// где стоит вражеская армия: вместо мгновенного авторазрешения открывается поле 9×7.
    /// Результат возвращается в MilitarySystem через BattleResolver.
    /// </summary>
    public partial class IronThroneGame
    {

        private Battlefield _battle;
        private ArmyState _battleAttacker;
        private ArmyState _battleDefender;
        private ProvinceState _battleProvince;
        private string _battleTarget;
        private Battlefield _preparedField;
        private Texture2D _battleTex;
        private bool _battleDirty = true;
        private string _battleMessage = "";
        private bool _battleMoveMode = true;
        private string _battleAbility;
        private Rect _battleFieldRect;
        private float _battleAiDelay;
        private const int BattleHexSize = 54;

        // ------------------------------------------------------------------ запуск
        /// <summary>Начинает ручной бой, если в целевой провинции стоит вражеская армия.</summary>
        private bool TryStartBattle(ArmyState army, string target)
        {
            var prov = _state.Province(target);
            if (prov == null || army == null) return false;
            var enemy = _state.Armies.Values.FirstOrDefault(a =>
                a.alive && a.province == target && a.faction != army.faction && !a.IsEmpty &&
                MilitarySystem.AreAtWar(_state, army.faction, a.faction));
            if (enemy == null) return false;

            bool siege = prov.garrison > 0 && prov.controller == enemy.faction;
            _battleAttacker = army;
            _battleDefender = enemy;
            _battleProvince = prov;
            _battleTarget = target;
            _battle = BattleEngine.Create(_state, army, enemy, prov, siege);
            _battleMessage = $"{army.name} против {enemy.name}";
            _battleMoveMode = true;
            _battleAbility = null;
            _battleDirty = true;
            _battleAiDelay = 0.5f;
            return true;
        }

        private BattleResult ResolvePreparedBattle(GameState state, ArmyState attacker, ArmyState defender,
            ProvinceState province, bool siege)
        {
            var field = _preparedField;
            _preparedField = null;
            if (field == null)
            {
                var auto = BattleEngine.Create(state, attacker, defender, province, siege);
                BattleEngine.RunAuto(auto);
                return BattleEngine.ApplyResult(auto, attacker, defender);
            }
            return BattleEngine.ApplyResult(field, attacker, defender);
        }

        /// <summary>Закрывает тактический экран и проводит бой через обычный MoveArmy.</summary>
        private void FinishBattle()
        {
            if (_battle == null) return;
            if (!_battle.finished)
                BattleEngine.Finish(_battle, _battle.SideAlive(0) && !_battle.SideAlive(1));

            _preparedField = _battle;
            _battle = null;
            _battleTex = null;

            MilitarySystem.Resolver = ResolvePreparedBattle;
            try
            {
                string msg;
                bool ok = MilitarySystem.MoveArmy(_state, _battleAttacker, _battleTarget, out msg);
                if (!ok && _preparedField != null)
                {
                    // движение не состоялось (например, армия уничтожена) — фиксируем итог боя вручную
                    var res = BattleEngine.ApplyResult(_preparedField, _battleAttacker, _battleDefender);
                    _preparedField = null;
                    _state.Battles.Add(res);
                    _state.AddLog("battle", $"{_state.FactionName(res.attacker)} против {_state.FactionName(res.defender)}: {res.Summary}");
                    foreach (var loser in new[] { _battleAttacker, _battleDefender })
                        if (loser != null && loser.IsEmpty) loser.alive = false;
                    msg = res.Summary;
                }
                Toast(msg ?? (ok ? "Армия выдвинулась" : "Движение невозможно"));
                if (ok) _selectedProvince = _battleTarget;
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
                Toast("Ошибка завершения боя: " + ex.Message);
            }
            finally
            {
                MilitarySystem.Resolver = null;
                _preparedField = null;
            }
            _battleAttacker = null;
            _battleDefender = null;
            _battleProvince = null;
            _battleTarget = null;
            _tab = 0;
            _battleDirty = true;
        }

        // ------------------------------------------------------------------ ход боя
        private void TickBattle()
        {
            if (_battle == null || _battle.finished) return;
            var active = BattleEngine.Active(_battle);
            if (active == null) { BattleEngine.NextUnit(_battle); _battleDirty = true; return; }
            if (active.side == 0 || active.acted) return;

            // защитник всегда под управлением ИИ — с небольшой паузой, чтобы ходы читались
            _battleAiDelay -= Time.deltaTime;
            if (_battleAiDelay > 0) return;
            _battleAiDelay = 0.45f;
            BattleEngine.AutoActivate(_battle, active);
            if (!_battle.finished) BattleEngine.EndUnitTurn(_battle);
            _battleDirty = true;
        }

        private void BattleAction(BattleUnit target)
        {
            var active = BattleEngine.Active(_battle);
            if (active == null || active.side != 0) return;
            if (_battleMoveMode)
            {
                if (BattleEngine.MoveTo(_battle, active, target.q, target.r))
                {
                    _battleMessage = $"{active.name} → {target.q},{target.r}";
                    _battleDirty = true;
                }
                return;
            }
            if (!BattleEngine.LegalTargets(_battle, active).Any(t => t == target)) return;
            string msg = BattleEngine.Attack(_battle, active, target, _battleAbility);
            _battleAbility = null;
            if (msg != null) _battleMessage = msg;
            _battleDirty = true;
            if (!_battle.finished) BattleEngine.EndUnitTurn(_battle);
        }

        // ------------------------------------------------------------------ отрисовка
        private void DrawBattle()
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), _bg);
            var b = _battle;
            if (b == null) return;

            float top = 8;
            GUI.Label(new Rect(20, top, Screen.width - 440, 34),
                $"Битва за {_battleProvince?.def.name ?? "поле"}", _h1);
            GUI.Label(new Rect(24, top + 38, Screen.width - 440, 22),
                $"Раунд {b.round}/{b.maxRounds} · {(_battleAttacker != null ? _battleAttacker.name : "атакующие")} против {(_battleDefender != null ? _battleDefender.name : "защитники")}" +
                (b.siege ? " · осада" : "") + (b.ambush ? " · засада" : ""), _small);

            var autoRect = new Rect(Screen.width - 400, top + 4, 300, 30);
            if (GUI.Button(autoRect, "Автобой", _button))
            {
                BattleEngine.RunAuto(b);
                _battleDirty = true;
            }

            // поле боя слева, панель справа
            float panelW = 360;
            var fieldArea = new Rect(8, 72, Screen.width - panelW - 24, Screen.height - 84);
            if (_battleDirty || _battleTex == null) RebuildBattleTexture();
            if (_battleTex != null)
            {
                float aspect = _battleTex.width / (float)_battleTex.height;
                float w = fieldArea.width, h = w / aspect;
                if (h > fieldArea.height) { h = fieldArea.height; w = h * aspect; }
                _battleFieldRect = new Rect(fieldArea.x + (fieldArea.width - w) / 2,
                    fieldArea.y + (fieldArea.height - h) / 2, w, h);
                GUI.DrawTexture(_battleFieldRect, _battleTex, ScaleMode.StretchToFill);
                HandleBattleClick();
            }

            GUILayout.BeginArea(new Rect(Screen.width - panelW - 8, 72, panelW, Screen.height - 84), _panel);
            DrawBattlePanel();
            GUILayout.EndArea();

            GUI.Label(new Rect(20, Screen.height - 24, Screen.width - panelW - 40, 20), _battleMessage, _small);
        }

        private void DrawBattlePanel()
        {
            var b = _battle;
            if (b == null) return;
            _sideScroll = GUILayout.BeginScrollView(_sideScroll, false, true);

            if (b.finished)
            {
                GUILayout.Label(b.attackerWon ? "ПОБЕДА" : "ПОРАЖЕНИЕ", _h1);
                GUILayout.Label(b.attackerWon
                    ? "Вражеская армия разбита. Провинция ждёт победителя."
                    : "Атакующие отступили, оставив поле боя за защитниками.", _body);
                GUILayout.Space(10);
                int aliveA = b.Side(0).Count(), aliveD = b.Side(1).Count();
                GUILayout.Label($"Осталось в строю: {aliveA} / {aliveD}", _small);
                GUILayout.Space(8);
                if (GUILayout.Button("Продолжить", _button, GUILayout.Height(40))) FinishBattle();
                GUILayout.EndScrollView();
                return;
            }

            var active = BattleEngine.Active(b);
            if (active == null)
            {
                if (GUILayout.Button("Следующий", _button)) { BattleEngine.NextUnit(b); _battleDirty = true; }
                GUILayout.EndScrollView();
                return;
            }

            GUILayout.Label(active.side == 0 ? "Ваш ход" : "Ход противника", _h2);
            GUILayout.Label($"<b>{active.name}</b> · HP {Math.Max(0, active.hp):0} · мораль {active.morale:0}", _body);
            GUILayout.Label($"движение {active.movesLeft} · защита {BattleEngine.TerrainDefense(b.Hex(active.q, active.r)?.terrain ?? BattleTerrain.Plains, active):0} от местности", _small);

            if (active.side == 0)
            {
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(_battleMoveMode ? "● Идти" : "Идти", _battleMoveMode ? _tabActive : _button))
                { _battleMoveMode = true; _battleAbility = null; _battleDirty = true; }
                if (GUILayout.Button(!_battleMoveMode ? "● Бить" : "Бить", !_battleMoveMode ? _tabActive : _button))
                { _battleMoveMode = false; _battleDirty = true; }
                GUILayout.EndHorizontal();

                // способность героя: одна на бой, на всю армию
                if (BattleEngine.CanUseHeroAbility(_battle, 0))
                {
                    string heroAb = BattleEngine.HeroAbilityId(_battle, 0);
                    if (GUILayout.Button($"Герой: {BattleEngine.HeroAbilityName(heroAb)} (раз за бой)", _tabActive))
                    {
                        string line = BattleEngine.UseHeroAbility(_battle, 0);
                        Toast(line ?? "Способность недоступна");
                        _battleDirty = true;
                        if (_battle.finished) FinishBattle();
                    }
                }
                else if (BattleEngine.CanUseHeroAbility(_battle, 1))
                {
                    GUILayout.Label($"Герой противника готовит «{BattleEngine.HeroAbilityName(BattleEngine.HeroAbilityId(_battle, 1))}».", _small);
                }

                var abilities = new[] { "volley", "charge", "rally", "heal", "dragon_breath", "shield_wall", "berserk" };
                GUILayout.Space(4);
                foreach (var ab in abilities)
                {
                    if (!active.Has(ab) || active.usedAbilities.Contains(ab)) continue;
                    string label = AbilityName(ab);
                    if (GUILayout.Button(label + " (при ударе)", _battleAbility == ab ? _tabActive : _button))
                    {
                        _battleAbility = _battleAbility == ab ? null : ab;
                        _battleMoveMode = false;
                    }
                }
                if (GUILayout.Button("Пропустить ход", _button)) { BattleEngine.EndUnitTurn(b); _battleDirty = true; }
                if (active.movesLeft == 0 && active.attacksThisRound > 0)
                    GUILayout.Label("Отряд исчерпал ход — нажмите «Пропустить».", _small);
            }
            else
            {
                GUILayout.Label("Противник обдумывает ход…", _small);
                if (GUILayout.Button("Ускорить", _button)) _battleAiDelay = 0;
            }

            GUILayout.Space(10);
            DrawBattleSide(0, "Ваши войска");
            DrawBattleSide(1, "Войска противника");

            GUILayout.Space(10);
            GUILayout.Label("Журнал", _h2);
            foreach (var line in b.log.Skip(Math.Max(0, b.log.Count - 10))) GUILayout.Label(line, _small);

            GUILayout.EndScrollView();
        }

        private void DrawBattleSide(int side, string title)
        {
            GUILayout.Label(title, _h2);
            foreach (var u in _battle.units.Where(u => u.side == side))
            {
                string state = !u.alive ? "уничтожен" : u.routed ? "бежит" :
                    BattleEngine.Active(_battle) == u ? "действует" : "";
                GUILayout.Label($"{(u.alive ? "" : "✕ ")}{u.name} — {Math.Max(0, u.hp):0} HP · {u.morale:0} мор. {state}", _small);
            }
        }

        private static string AbilityName(string ability) => ability switch
        {
            "volley" => "Залп",
            "charge" => "Натиск",
            "rally" => "Воодушевление",
            "heal" => "Полевой лекарь",
            "dragon_breath" => "Пламя дракона",
            "shield_wall" => "Стена щитов",
            "berserk" => "Берсерк",
            _ => ability
        };

        // ------------------------------------------------------------------ клик по полю
        private void HandleBattleClick()
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0) return;
            if (!_battleFieldRect.Contains(e.mousePosition)) return;
            if (_battle == null || _battle.finished || _battleTex == null) return;

            var rel = new Vector2(
                (e.mousePosition.x - _battleFieldRect.x) / _battleFieldRect.width * _battleTex.width,
                (e.mousePosition.y - _battleFieldRect.y) / _battleFieldRect.height * _battleTex.height);
            BattleHex best = null;
            float bestDist = float.MaxValue;
            foreach (var hex in _battle.hexes.Values)
            {
                var c = BattleHexCenter(hex.q, hex.r);
                float d = (c - rel).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = hex; }
            }
            if (best == null) return;
            e.Use();
            var unit = _battle.UnitAt(best.q, best.r);
            if (unit != null && unit.side != 0) BattleAction(unit);
            else if (_battleMoveMode && unit == null) BattleAction(new BattleUnit { q = best.q, r = best.r, side = 0 });
            else if (!_battleMoveMode) _battleMessage = "Выберите отряд противника";
        }

        // ------------------------------------------------------------------ текстура боя
        private const float BattleSqrt3 = 1.7320508f;

        private Vector2 BattleHexCenter(int q, int r)
        {
            float s = BattleHexSize;
            return new Vector2(BattleSqrt3 * s * (q + r / 2f) + s * 1.1f, 1.5f * s * r + s * 1.25f);
        }

        private Vector2 BattleCorner(float cx, float cy, float size, int i)
        {
            float angle = (float)(Math.PI / 180 * (60 * i - 30));
            return new Vector2(cx + size * (float)Math.Cos(angle), cy + size * (float)Math.Sin(angle));
        }

        private void RebuildBattleTexture()
        {
            _battleDirty = false;
            var b = _battle;
            if (b == null) { _battleTex = null; return; }
            int s = BattleHexSize;
            int w = Mathf.CeilToInt(BattleSqrt3 * s * (b.width + (b.height - 1) / 2f) + s * 2.2f);
            int h = Mathf.CeilToInt(1.5f * s * (b.height - 1) + s * 2.5f);
            if (w < 16 || h < 16) return;

            var px = new Color32[w * h];
            var bg = new Color32(24, 30, 34, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;

            var reachable = new HashSet<(int, int)>();
            var targets = new HashSet<(int, int)>();
            var active = b.finished ? null : BattleEngine.Active(b);
            if (active != null && active.side == 0)
            {
                if (_battleMoveMode)
                    foreach (var hx in BattleEngine.LegalMoves(b, active)) reachable.Add((hx.q, hx.r));
                else
                    foreach (var u in BattleEngine.LegalTargets(b, active)) targets.Add((u.q, u.r));
            }

            foreach (var hex in b.hexes.Values)
            {
                var c = BattleHexCenter(hex.q, hex.r);
                var fill = TerrainColor(hex.terrain);
                if (!hex.passable) fill = new Color32(20, 25, 30, 255);
                if (reachable.Contains((hex.q, hex.r))) fill = Mix(fill, new Color32(235, 205, 110, 255), 0.35f);
                if (targets.Contains((hex.q, hex.r))) fill = Mix(fill, new Color32(190, 60, 50, 255), 0.4f);
                FillHex(px, w, h, c.x, c.y, s, fill);
                if (hex.terrain == BattleTerrain.Walls) FillHex(px, w, h, c.x, c.y, s - 7f, new Color32(140, 132, 118, 255));
            }

            foreach (var u in b.units)
            {
                var c = BattleHexCenter(u.q, u.r);
                var fac = _state.Faction(u.side == 0 ? b.attackerFaction : b.defenderFaction);
                var col = fac != null ? ParseColor(fac.def.color) : Color.gray;
                var c32 = new Color32((byte)(col.r * 255), (byte)(col.g * 255), (byte)(col.b * 255), 255);
                if (!u.alive)
                {
                    FillCircle(px, w, h, c.x, c.y, s * 0.35f, new Color32(40, 40, 40, 255));
                    continue;
                }
                if (active == u)
                    FillCircle(px, w, h, c.x, c.y, s * 0.48f, new Color32(240, 205, 110, 255));
                FillCircle(px, w, h, c.x, c.y, s * 0.38f, u.routed ? Mix(c32, new Color32(90, 90, 90, 255), 0.6f) : c32);
                FillCircle(px, w, h, c.x, c.y, s * 0.20f, u.side == 0 ? new Color32(245, 245, 235, 255) : new Color32(25, 25, 25, 255));
                // полоса здоровья
                float bw = s * 0.7f;
                float bx = c.x - bw / 2f, by = c.y + s * 0.42f;
                FillRect(px, w, h, bx, by, bw, 5, new Color32(20, 20, 20, 255));
                float hpK = Mathf.Clamp01(u.hp / Mathf.Max(1f, u.maxHp));
                FillRect(px, w, h, bx, by, bw * hpK, 5,
                    hpK > 0.6f ? new Color32(90, 190, 90, 255) : hpK > 0.3f ? new Color32(220, 180, 60, 255) : new Color32(200, 70, 60, 255));
            }

            HexMapTexture.FlipRows(px, w, h);
            if (_battleTex != null) UnityEngine.Object.Destroy(_battleTex);
            _battleTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            _battleTex.SetPixels32(px);
            _battleTex.Apply();
        }

        private static Color32 TerrainColor(BattleTerrain t) => t switch
        {
            BattleTerrain.Forest => new Color32(62, 92, 54, 255),
            BattleTerrain.Hills => new Color32(122, 108, 72, 255),
            BattleTerrain.Mountains => new Color32(104, 100, 94, 255),
            BattleTerrain.River => new Color32(54, 96, 132, 255),
            BattleTerrain.Desert => new Color32(196, 172, 110, 255),
            BattleTerrain.Marsh => new Color32(88, 100, 72, 255),
            BattleTerrain.Snow => new Color32(206, 212, 214, 255),
            BattleTerrain.Walls => new Color32(146, 136, 118, 255),
            BattleTerrain.Water => new Color32(42, 74, 104, 255),
            _ => new Color32(108, 124, 84, 255)
        };

        /// <summary>Заливка pointy-top гекса: вертикальные боковые стороны и скосы к вершинам.</summary>
        private static void FillHex(Color32[] px, int w, int h, float cx, float cy, float size, Color32 color)
        {
            float halfWidth = size * 0.8660254f;
            int minX = Math.Max(0, (int)(cx - halfWidth - 1)), maxX = Math.Min(w - 1, (int)(cx + halfWidth + 1));
            int minY = Math.Max(0, (int)(cy - size - 1)), maxY = Math.Min(h - 1, (int)(cy + size + 1));
            for (int y = minY; y <= maxY; y++)
            {
                float dy = Math.Abs(y + 0.5f - cy);
                if (dy > size) continue;
                float allowed = dy <= size * 0.5f ? halfWidth : halfWidth * (1f - (dy - size * 0.5f) / (size * 0.5f));
                int from = Math.Max(minX, (int)(cx - allowed));
                int to = Math.Min(maxX, (int)(cx + allowed));
                for (int x = from; x <= to; x++) px[y * w + x] = color;
            }
        }

        private static void FillCircle(Color32[] px, int w, int h, float cx, float cy, float radius, Color32 color)
        {
            int minX = Math.Max(0, (int)(cx - radius)), maxX = Math.Min(w - 1, (int)(cx + radius));
            int minY = Math.Max(0, (int)(cy - radius)), maxY = Math.Min(h - 1, (int)(cy + radius));
            float r2 = radius * radius;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy <= r2) px[y * w + x] = color;
                }
        }

        private static void FillRect(Color32[] px, int w, int h, float x0, float y0, float bw, float bh, Color32 color)
        {
            int minX = Math.Max(0, (int)x0), maxX = Math.Min(w - 1, (int)(x0 + bw));
            int minY = Math.Max(0, (int)y0), maxY = Math.Min(h - 1, (int)(y0 + bh));
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++) px[y * w + x] = color;
        }

        private static Color32 Mix(Color32 a, Color32 b, float k) => new Color32(
            (byte)(a.r + (b.r - a.r) * k), (byte)(a.g + (b.g - a.g) * k), (byte)(a.b + (b.b - a.b) * k),
            (byte)(a.a + (b.a - a.a) * k));
    }
}
