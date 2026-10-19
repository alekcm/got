using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IronThrone.Core.Content;
using IronThrone.Core.Game;
using UnityEngine;

namespace IronThrone.Unity
{
    /// <summary>
    /// Точка входа Unity-слоя: меню выбора эпохи/фракции/карты и игровой экран.
    /// Вся логика — в IronThrone.Core; здесь только отображение и ввод (IMGUI).
    /// </summary>
    public partial class IronThroneGame : MonoBehaviour
    {
        private enum UiScreen { Loading, Menu, Game, Error }

        private static IronThroneGame _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("Iron Throne");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<IronThroneGame>();
        }

        private ContentDb _content;
        private GameState _state;
        private HexMapTexture _map;
        private UiScreen _screen = UiScreen.Loading;
        private string _error;

        // меню
        private string _eraId = "fivekings";
        private string _factionId = "five_stark";
        private string _mapMode = "canonical";
        private readonly List<string> _advisors = new List<string>();

        // игровой экран
        private int _tab;
        private string _selectedProvince;
        private string _selectedArmy;
        private string _spyTarget;
        private string _claimImprovement;
        private bool _showHelp;
        private bool _moveMode;
        private Vector2 _sideScroll;
        private string _toast;
        private float _toastUntil;
        private Rect _mapRect;
        private Rect _mapDrawRect;
        private Texture2D _white;

        // стили
        private bool _stylesReady;
        private GUIStyle _title, _h1, _h2, _body, _small, _panel, _button, _tabStyle, _tabActive, _resource, _box;
        private Color _bg = new Color(0.055f, 0.07f, 0.085f, 1f);
        private Color _panelColor = new Color(0.10f, 0.12f, 0.14f, 1f);
        private Color _accent = new Color(0.78f, 0.63f, 0.28f, 1f);

        private void Awake()
        {
            try
            {
                _content = UnityContent.Load();
                _screen = UiScreen.Menu;
                PickDefaultFaction();
            }
            catch (Exception ex)
            {
                _error = ex.Message;
                _screen = UiScreen.Error;
                Debug.LogError("Iron Throne: " + ex);
            }
        }

        private void Update()
        {
            if (_screen != UiScreen.Game) return;
            if (_battle != null) { TickBattle(); return; }
            if (Input.GetKeyDown(KeyCode.Space)) NextTurn();
        }

        // ------------------------------------------------------------------ OnGUI
        private void OnGUI()
        {
            EnsureStyles();
            switch (_screen)
            {
                case UiScreen.Loading:
                    GUI.Label(new Rect(20, 20, 600, 40), "Загрузка Iron Throne…", _h1);
                    break;
                case UiScreen.Error:
                    GUI.Label(new Rect(40, 40, Screen.width - 80, 200), "Ошибка загрузки:\n\n" + _error, _body);
                    break;
                case UiScreen.Menu:
                    DrawMenu();
                    if (_showHelp) DrawHelp();
                    break;
                case UiScreen.Game:
                    if (_battle != null) DrawBattle();
                    else DrawGame();
                    break;
            }
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;
            _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _white.SetPixels32(new[] { new Color32(255, 255, 255, 255) });
            _white.Apply();

            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true,
                normal = { textColor = new Color(0.93f, 0.86f, 0.66f) },
            };
            _h1 = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, richText = true, normal = { textColor = _accent } };
            _h2 = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, richText = true, normal = { textColor = new Color(0.85f, 0.82f, 0.72f) } };
            _body = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, richText = true, normal = { textColor = new Color(0.86f, 0.87f, 0.85f) } };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true, richText = true, normal = { textColor = new Color(0.65f, 0.68f, 0.68f) } };
            _panel = new GUIStyle(GUI.skin.box) { normal = { background = MakeTex(_panelColor) }, padding = new RectOffset(10, 10, 8, 8) };
            _box = new GUIStyle(GUI.skin.box) { normal = { background = MakeTex(new Color(0.07f, 0.085f, 0.10f, 0.96f)) }, padding = new RectOffset(14, 14, 12, 12) };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 13, wordWrap = true, normal = { textColor = new Color(0.9f, 0.9f, 0.86f) } };
            _tabStyle = new GUIStyle(_button) { fontSize = 12 };
            _tabActive = new GUIStyle(_button)
            {
                fontSize = 12, fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.12f, 0.10f, 0.05f), background = MakeTex(_accent) },
            };
            _resource = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.95f, 0.92f, 0.8f) } };
        }

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixels32(new[] { new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(c.a * 255)) });
            t.Apply();
            return t;
        }

        // ------------------------------------------------------------------ меню
        private void PickDefaultFaction()
        {
            var first = _content.Factions.FirstOrDefault(f => f.era == _eraId && f.playable);
            if (first != null) _factionId = first.id;
            _advisors.Clear();
        }

        private void DrawMenu()
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), _bg);
            float w = Math.Min(1180, Screen.width - 40);
            float x = (Screen.width - w) / 2f;
            GUI.Label(new Rect(x, 18, w, 60), "ЖЕЛЕЗНЫЙ ТРОН", _title);
            GUI.Label(new Rect(x, 76, w, 26), "пошаговая стратегия Вестероса · две эпохи · каноническая и случайная карта", _small);

            GUILayout.BeginArea(new Rect(x, 112, w, Screen.height - 130));
            GUILayout.BeginHorizontal();

            // левая колонка: эпоха и карта
            GUILayout.BeginVertical(_panel, GUILayout.Width(320));
            GUILayout.Label("Эпоха", _h1);
            foreach (var era in _content.Eras)
            {
                if (GUILayout.Button((_eraId == era.id ? "● " : "○ ") + era.name, _button, GUILayout.Height(32)))
                {
                    _eraId = era.id;
                    PickDefaultFaction();
                }
            }
            var curEra = _content.Eras.FirstOrDefault(e => e.id == _eraId);
            if (curEra != null) GUILayout.Label(curEra.description, _small);

            GUILayout.Space(10);
            GUILayout.Label("Карта", _h1);
            if (GUILayout.Button((_mapMode == "canonical" ? "● " : "○ ") + "Каноническая", _button))
                _mapMode = "canonical";
            if (GUILayout.Button((_mapMode == "random" ? "● " : "○ ") + "Случайная (с ограничениями)", _button))
                _mapMode = "random";
            GUILayout.Label("Старки — север, Дорн — юг, Грейджои — острова. Ограничения сохраняются в обеих эпохах.", _small);

            GUILayout.Space(10);
            GUILayout.Label($"Советники ({_advisors.Count}/2)", _h1);
            var faction = _content.FactionById.TryGetValue(_factionId, out var fd) ? fd : null;
            if (faction != null)
            {
                foreach (var adv in _content.AdvisorsFor(curEra, faction))
                {
                    bool chosen = _advisors.Contains(adv.id);
                    if (GUILayout.Button((chosen ? "✓ " : "+ ") + adv.name + " — " + adv.title, chosen ? _tabActive : _button))
                    {
                        if (chosen) _advisors.Remove(adv.id);
                        else if (_advisors.Count < 2) _advisors.Add(adv.id);
                    }
                }
            }
            GUILayout.EndVertical();

            GUILayout.Space(12);

            // правая колонка: фракции
            GUILayout.BeginVertical(_panel);
            GUILayout.Label("Великий дом", _h1);
            var factions = _content.Factions.Where(f => f.era == _eraId).OrderByDescending(f => f.playable).ThenBy(f => f.difficulty).ToList();
            foreach (var f in factions)
            {
                if (!f.playable) continue;
                GUILayout.BeginHorizontal();
                var style = _factionId == f.id ? _tabActive : _button;
                if (GUILayout.Button(f.name, style, GUILayout.Width(230), GUILayout.Height(30)))
                {
                    _factionId = f.id;
                    _advisors.Clear();
                }
                GUILayout.Label($"сложность {f.difficulty}/5 · {FaithName(f.faith)}", _small);
                GUILayout.EndHorizontal();
                if (_factionId == f.id)
                {
                    GUILayout.Label(f.description, _body);
                    if (!string.IsNullOrEmpty(f.uniqueMechanicName))
                        GUILayout.Label("<b>" + f.uniqueMechanicName + ":</b> " + f.uniqueMechanicText, _body);
                    GUILayout.Space(6);
                }
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            bool canStart = faction != null && _advisors.Count == 2;
            GUI.enabled = canStart;
            if (GUILayout.Button("НАЧАТЬ ИГРУ", _h1, GUILayout.Height(44), GUILayout.Width(260))) StartNewGame();
            GUI.enabled = true;
            if (_state != null && GUILayout.Button("Продолжить партию", _button, GUILayout.Height(44), GUILayout.Width(220)))
            { _screen = UiScreen.Game; Toast("Партия продолжена: ход " + _state.Turn); }
            if (File.Exists(UnityContent.SavePath) || File.Exists(UnityContent.AutoSavePath))
            {
                if (GUILayout.Button("Загрузить сохранение", _button, GUILayout.Height(44), GUILayout.Width(220)))
                    LoadGame();
            }
            if (GUILayout.Button("Как играть", _button, GUILayout.Height(44), GUILayout.Width(180)))
                _showHelp = true;
            if (!canStart) GUILayout.Label("Выберите двух советников, чтобы начать.", _small);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private string FaithName(string id) =>
            _content.FaithById.TryGetValue(id ?? "", out var f) ? f.name : "—";

        private void StartNewGame()
        {
            try
            {
                _state = GameSetup.CreateNewGame(_content, _eraId, _factionId,
                    seed: Environment.TickCount, chosenAdvisors: new List<string>(_advisors), mapMode: _mapMode);
                TurnEngine.StartGame(_state);
                _map = HexMapTexture.Build(_state, 24);
                _selectedProvince = _state.Province(_state.Faction(_factionId)?.capital ?? "") != null
                    ? _state.Faction(_factionId).capital
                    : _state.Provinces.Keys.FirstOrDefault();
                _selectedArmy = null;
                _tab = 0;
                _sideScroll = Vector2.zero;
                _screen = UiScreen.Game;
                Toast("Партия начата. Ход 1.");
            }
            catch (Exception ex)
            {
                Toast("Ошибка старта: " + ex.Message);
                Debug.LogError(ex);
            }
        }

        // ------------------------------------------------------------------ игра
        private void DrawGame()
        {
            var f = _state.Faction(_state.PlayerFaction);
            float topH = 48, bottomH = 56;
            Fill(new Rect(0, 0, Screen.width, Screen.height), _bg);

            // верхняя панель
            Fill(new Rect(0, 0, Screen.width, topH), new Color(0.07f, 0.09f, 0.10f, 1f));
            float x = 12;
            if (f != null)
            {
                Fill(new Rect(x, 14, 20, 20), ParseColor(f.def.color));
                x += 28;
                GUI.Label(new Rect(x, 12, 260, 24), f.def.name, _h1); x += 240;
                GUI.Label(new Rect(x, 14, 300, 22), TurnEngine.DateLabel(_state), _resource); x += 250;
                GUI.Label(new Rect(x, 14, Screen.width - x - 280, 22),
                    $"Золото {f.gold}   Материалы {f.materials}   Еда {f.food}   Влияние {f.influence}   Вера {f.piety}", _resource);
                int days = Math.Max(0, _state.Settings.maxTurns - _state.Turn);
                GUI.Label(new Rect(Screen.width - 150, 14, 140, 22), $"до конца: {days}", _small);
            }

            // карта
            _mapRect = new Rect(8, topH + 8, Screen.width * 0.63f - 12, Screen.height - topH - bottomH - 16);
            GUI.Box(_mapRect, GUIContent.none, _panel);
            DrawMap(_mapRect);

            // боковая панель
            float sideX = _mapRect.xMax + 8;
            var sideRect = new Rect(sideX, topH + 8, Screen.width - sideX - 8, Screen.height - topH - bottomH - 16);
            GUILayout.BeginArea(sideRect, _panel);
            DrawTabs();
            _sideScroll = GUILayout.BeginScrollView(_sideScroll);
            switch (_tab)
            {
                case 0: DrawProvinceTab(f); break;
                case 1: DrawArmyTab(f); break;
                case 2: DrawCourtTab(f); break;
                case 3: DrawTechTab(f); break;
                case 4: DrawFocusTab(f); break;
                case 5: DrawHousesTab(f); break;
                case 6: DrawFaithTab(f); break;
                case 7: DrawIntrigueTab(f); break;
                default: DrawJournalTab(f); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // нижняя панель
            Fill(new Rect(0, Screen.height - bottomH, Screen.width, bottomH), new Color(0.07f, 0.09f, 0.10f, 1f));
            GUI.enabled = _state.Phase != GamePhase.GameOver;
            if (GUI.Button(new Rect(12, Screen.height - bottomH + 8, 220, 40), "СЛЕДУЮЩИЙ ХОД  (Space)", _tabActive))
                NextTurn();
            GUI.enabled = true;
            if (GUI.Button(new Rect(244, Screen.height - bottomH + 8, 120, 40), "Сохранить", _button)) SaveGame(false);
            if (GUI.Button(new Rect(372, Screen.height - bottomH + 8, 120, 40), "Загрузить", _button)) LoadGame();
            if (GUI.Button(new Rect(500, Screen.height - bottomH + 8, 120, 40), "В меню", _button)) { _screen = UiScreen.Menu; PickDefaultFaction(); }
            if (GUI.Button(new Rect(628, Screen.height - bottomH + 8, 44, 40), "?", _button)) _showHelp = !_showHelp;
            GUI.Label(new Rect(684, Screen.height - bottomH + 16, Screen.width - 704, 24),
                $"ход {_state.Turn} · {Season(_state.Season)} · войн: {_state.Wars.Count(w => w.active)}" +
                (f != null ? $" · освоение {f.growth:0}/{TerritorySystem.GrowthThreshold(_state, f)}" +
                             (f.growthReady ? " (готово)" : "") : ""), _small);

            if (!string.IsNullOrEmpty(_toast) && Time.time < _toastUntil)
                GUI.Label(new Rect(Screen.width / 2f - 350, Screen.height - bottomH - 34, 700, 26), _toast, _h2);

            DrawModals();
            if (_showHelp) DrawHelp();
            if (_state.Phase == GamePhase.GameOver) DrawVictory();
        }

        private static string Season(string s) => s;

        private void DrawTabs()
        {
            string[] tabs = { "Провинция", "Армия", "Двор", "Науки", "Фокусы", "Дома", "Вера", "Интриги", "Журнал" };
            GUILayout.BeginHorizontal();
            for (int i = 0; i < tabs.Length; i++)
            {
                if (GUILayout.Button(tabs[i], i == _tab ? _tabActive : _tabStyle))
                {
                    _tab = i;
                    _sideScroll = Vector2.zero;
                }
            }
            GUILayout.EndHorizontal();
        }

        private void NextTurn()
        {
            if (_state.Phase == GamePhase.GameOver) return;
            if (_state.PendingEvents.Count > 0) { Toast("Сначала примите решение по событию."); return; }
            if (_state.PendingVote != null) { Toast("Сначала завершите голосование в Малом совете."); return; }
            try
            {
                TurnEngine.EndTurn(_state);
                _selectedArmy = null;
                _moveMode = false;
                if (_state.Phase == GamePhase.GameOver)
                    Toast("Игра окончена: " + _state.FactionName(_state.VictoryWinner));
                else
                {
                    var headline = _state.Log.LastOrDefault(l => l.turn == _state.Turn && l.category == "event");
                    Toast(headline != null
                        ? $"Ход {_state.Turn}. {headline.text}"
                        : $"Ход {_state.Turn}. {TurnEngine.DateLabel(_state)}");
                    if (_state.Turn % 5 == 0) SaveGame(true);
                }
            }
            catch (Exception ex)
            {
                Toast("Ошибка хода: " + ex.Message);
                Debug.LogError(ex);
            }
        }

        private void DrawHelp()
        {
            float w = Math.Min(760, Screen.width - 80), h = Math.Min(560, Screen.height - 100);
            var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.55f));
            GUILayout.BeginArea(r, _box);
            GUILayout.Label("Как играть", _h1);
            _sideScroll = GUILayout.BeginScrollView(_sideScroll);
            GUILayout.Label(
                "<b>Ход.</b> Пробел или кнопка «Следующий ход» — конец хода. Доход, стройка, исследования идут автоматически; " +
                "события и голосования открываются модальными окнами и ждут решения.\n\n" +
                "<b>Карта.</b> Клик по гексу выбирает провинцию. Вкладка «Армия» → «Двигаться», затем клик по соседней провинции — марш. " +
                "Если в провинции стоит враг, открывается тактическое поле.\n\n" +
                "<b>Ресурсы.</b> Золото — строительство, найм, влияние на двор. Материалы — стройка. Продовольствие — найм и восполнение потерь; " +
                "потери восполняются только когда потрёпанная армия вернулась на свою землю и есть еда. Влияние — войны, законы, шпионаж, вассалы. " +
                "Вера — миссионеры и реформа.\n\n" +
                "<b>Освоение.</b> Каждый ход дом копит освоение от числа и развития провинций. Накопленное тратится на присоединение соседней ничейной " +
                "провинции (кнопка в карточке провинции) или на улучшение земли — мельницы, каменоломни, торговые ряды, башни, часовни. " +
                "Улучшения можно докупить за золото; лимит — 2 на провинцию.\n\n" +
                "<b>Столица.</b> Слоты построек зависят от уровня провинции: экономика, войска, храм, порт, особые. Постройки дают доход и модификаторы.\n\n" +
                "<b>Двор.</b> Законы предлагаются в Малом совете и принимаются голосованием; сила голоса зависит от титулов, вассалов и влияния. " +
                "Титулы можно раздавать членам дома.\n\n" +
                "<b>Шпионаж.</b> Сеть шептунов и миссии: разведка, кража технологий, саботаж, подстрекательство, убийство героя, подкуп, контрразведка.\n\n" +
                "<b>Малые дома.</b> Дары, браки, угрозы, наёмники; при высоких отношениях — вассалитет или аннексия. Дань идёт в казну.\n\n" +
                "<b>Победы.</b> Гегемония — большая часть провинций и 5 региональных столиц. Политика — признание, Железный трон и 5 мест в совете. " +
                "Экономика — торговое господство. Религия — ваша вера в большинстве провинций, вы глава веры и вера реформирована. " +
                "Если никто не победил до конца ходов, побеждает дом с наибольшим счётом.\n\n" +
                "<b>Горячие клавиши.</b> Пробел — конец хода; «?» — эта справка.", _body);
            GUILayout.Space(8);
            if (GUILayout.Button("Закрыть", _button, GUILayout.Height(34))) _showHelp = false;
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void Toast(string text)
        {
            _toast = text;
            _toastUntil = Time.time + 6f;
        }

        private void SaveGame(bool auto)
        {
            try
            {
                var path = auto ? UnityContent.AutoSavePath : UnityContent.SavePath;
                SaveSystem.SaveToFile(_state, path);
                if (!auto) Toast("Сохранено: " + Path.GetFileName(path));
            }
            catch (Exception ex) { Toast("Не удалось сохранить: " + ex.Message); }
        }

        private void LoadGame()
        {
            try
            {
                var path = File.Exists(UnityContent.SavePath) ? UnityContent.SavePath : UnityContent.AutoSavePath;
                if (!File.Exists(path)) { Toast("Сохранение не найдено."); return; }
                _state = SaveSystem.LoadFromFile(_content, path);
                _map = HexMapTexture.Build(_state, 24);
                _selectedProvince = _state.Provinces.ContainsKey(_selectedProvince ?? "") ? _selectedProvince : _state.Provinces.Keys.FirstOrDefault();
                _selectedArmy = null;
                _screen = UiScreen.Game;
                Toast("Загружено: ход " + _state.Turn);
            }
            catch (Exception ex) { Toast("Не удалось загрузить: " + ex.Message); Debug.LogError(ex); }
        }

        // ------------------------------------------------------------------ карта
        private void DrawMap(Rect area)
        {
            if (_map == null || _map.Texture == null) return;
            float texW = _map.Texture.width, texH = _map.Texture.height;
            float aspect = texW / texH;
            float w = area.width, h = w / aspect;
            if (h > area.height) { h = area.height; w = h * aspect; }
            _mapDrawRect = new Rect(area.x + (area.width - w) / 2, area.y + (area.height - h) / 2, w, h);
            GUI.DrawTexture(_mapDrawRect, _map.Texture, ScaleMode.StretchToFill);

            // владельцы провинций и армии
            var f = _state.Faction(_state.PlayerFaction);
            foreach (var p in _state.Provinces.Values)
            {
                if (p.def.settlement == null || p.def.settlement.Count < 2) continue;
                var c = _map.HexCenter(p.def.settlement[0], p.def.settlement[1]);
                var sp = MapToScreen(c);
                var owner = _state.Faction(p.controller);
                if (owner != null)
                    Fill(new Rect(sp.x - 8, sp.y - 13, 7, 7), ParseColor(owner.def.color));
            }
            foreach (var a in _state.Armies.Values.Where(a => a.alive && !a.IsEmpty))
            {
                var prov = _state.Province(a.province);
                if (prov?.def.settlement == null || prov.def.settlement.Count < 2) continue;
                var c = _map.HexCenter(prov.def.settlement[0], prov.def.settlement[1]);
                var sp = MapToScreen(c);
                var fac = _state.Faction(a.faction);
                var col = fac != null ? ParseColor(fac.def.color) : Color.white;
                Fill(new Rect(sp.x + 2, sp.y + 3, 9, 9), col);
                Fill(new Rect(sp.x + 4, sp.y + 5, 5, 5), a.faction == _state.PlayerFaction ? Color.white : new Color(0.1f, 0.1f, 0.1f));
            }

            // выделение
            if (!string.IsNullOrEmpty(_selectedProvince) && _state.Province(_selectedProvince) != null)
            {
                var p = _state.Province(_selectedProvince);
                if (p.def.settlement != null && p.def.settlement.Count >= 2)
                {
                    var sp = MapToScreen(_map.HexCenter(p.def.settlement[0], p.def.settlement[1]));
                    var gold = _accent;
                    Fill(new Rect(sp.x - 16, sp.y - 16, 32, 2), gold);
                    Fill(new Rect(sp.x - 16, sp.y + 14, 32, 2), gold);
                    Fill(new Rect(sp.x - 16, sp.y - 16, 2, 32), gold);
                    Fill(new Rect(sp.x + 14, sp.y - 16, 2, 32), gold);
                }
            }

            // клик
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && _mapDrawRect.Contains(e.mousePosition))
            {
                var rel = new Vector2(
                    (e.mousePosition.x - _mapDrawRect.x) / _mapDrawRect.width * texW,
                    (e.mousePosition.y - _mapDrawRect.y) / _mapDrawRect.height * texH);
                var provId = _map.ProvinceAt(_state, rel);
                if (!string.IsNullOrEmpty(provId))
                {
                    if (_moveMode && !string.IsNullOrEmpty(_selectedArmy))
                    {
                        TryMove(_selectedArmy, provId);
                    }
                    else
                    {
                        _selectedProvince = provId;
                        _tab = 0;
                        _sideScroll = Vector2.zero;
                    }
                    e.Use();
                }
            }
        }

        private Vector2 MapToScreen(Vector2 mapPixel) => new Vector2(
            _mapDrawRect.x + mapPixel.x / _map.Texture.width * _mapDrawRect.width,
            _mapDrawRect.y + mapPixel.y / _map.Texture.height * _mapDrawRect.height);

        private static Color ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex[0] != '#' || hex.Length < 7) return Color.gray;
            try
            {
                return new Color(
                    Convert.ToByte(hex.Substring(1, 2), 16) / 255f,
                    Convert.ToByte(hex.Substring(3, 2), 16) / 255f,
                    Convert.ToByte(hex.Substring(5, 2), 16) / 255f);
            }
            catch { return Color.gray; }
        }

        private void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white, ScaleMode.StretchToFill);
            GUI.color = old;
        }

        private void TryMove(string armyId, string target)
        {
            if (!_state.Armies.TryGetValue(armyId, out var army)) return;
            if (TryStartBattle(army, target))
            {
                _moveMode = false;
                return;
            }
            var msg = "";
            bool ok = MilitarySystem.MoveArmy(_state, army, target, out msg);
            Toast(msg ?? (ok ? "Армия выдвинулась" : "Движение невозможно"));
            _moveMode = false;
            if (ok) _tab = 1;
        }

        // ------------------------------------------------------------------ модалки
        private void DrawModals()
        {
            if (_state.PendingEvents.Count > 0)
            {
                var pending = _state.PendingEvents[0];
                if (_state.Content.EventById.TryGetValue(pending.eventId, out var ev))
                {
                    float w = Math.Min(720, Screen.width - 80), h = Math.Min(420, Screen.height - 120);
                    var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
                    Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.5f));
                    GUILayout.BeginArea(r, _box);
                    GUILayout.Label(ev.name, _title);
                    GUILayout.Label(ev.text, _body);
                    GUILayout.Space(10);
                    for (int i = 0; i < ev.choices.Count; i++)
                    {
                        var c = ev.choices[i];
                        string label = c.text + (c.cost != null && !c.cost.IsZero ? "  (" + c.cost.ToRu() + ")" : "");
                        if (GUILayout.Button(label, _button, GUILayout.Height(34)))
                        {
                            var err = EventSystem.Choose(_state, 0, i);
                            if (err != null) Toast(err);
                        }
                    }
                    GUILayout.EndArea();
                }
                else _state.PendingEvents.RemoveAt(0);
            }
            else if (_state.PendingVote != null)
            {
                var vote = _state.PendingVote;
                _state.Content.LawById.TryGetValue(vote.lawId, out var law);
                float w = Math.Min(620, Screen.width - 80), h = 320;
                var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
                Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.5f));
                GUILayout.BeginArea(r, _box);
                GUILayout.Label("Голосование Малого совета", _h1);
                GUILayout.Label(law != null ? law.name : vote.lawId, _h2);
                GUILayout.Label(law?.description ?? "", _body);
                GUILayout.Label($"Спонсор: {_state.FactionName(vote.sponsor)} · стоимость влияния: {law?.voteCost ?? 0}", _small);
                var player = _state.Faction(_state.PlayerFaction);
                if (player != null)
                {
                    GUILayout.Label($"Мой голос: {PoliticsSystem.VotePower(_state, player)} · влияние: {player.influence}", _small);
                    GUILayout.Space(6);
                    GUILayout.Label("Лоббирование (влияние → голоса в пользу закона)", _h2);
                    foreach (var other in _state.Factions.Values.Where(x => x.alive && x.id != player.id).OrderBy(x => x.def.name))
                    {
                        int unit = PoliticsSystem.LobbyUnitCost(_state, player, other);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"{other.def.name} · голос {PoliticsSystem.VotePower(_state, other)}", _small);
                        GUILayout.FlexibleSpace();
                        GUI.enabled = player.influence >= unit;
                        if (GUILayout.Button($"Лобби {unit}", _button, GUILayout.Width(130)))
                        {
                            if (PoliticsSystem.Lobby(_state, player, other.id, unit, out var lerr))
                                Toast($"{other.def.name} склоняется к «за»");
                            else Toast(lerr);
                        }
                        GUI.enabled = true;
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.Space(8);
                }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("ЗА", _tabActive, GUILayout.Height(36)))
                {
                    PoliticsSystem.SetPlayerVote(_state, "yes");
                    var res = PoliticsSystem.ResolveVote(_state, autoPlayer: true);
                    Toast(res ?? "Голосование завершено");
                }
                if (GUILayout.Button("ПРОТИВ", _button, GUILayout.Height(36)))
                {
                    PoliticsSystem.SetPlayerVote(_state, "no");
                    var res = PoliticsSystem.ResolveVote(_state, autoPlayer: true);
                    Toast(res ?? "Голосование завершено");
                }
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
            }
        }

        private void DrawVictory()
        {
            float w = Math.Min(680, Screen.width - 80), h = 300;
            var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.6f));
            GUILayout.BeginArea(r, _box);
            GUILayout.Label("ЭПОХА ЗАВЕРШЕНА", _title);
            GUILayout.Label(_state.FactionName(_state.VictoryWinner) + " — " + VictorySystem.VictoryName(_state.VictoryType), _h1);
            GUILayout.Label($"Ход {_state.Turn}. {TurnEngine.DateLabel(_state)}", _body);
            GUILayout.Space(12);
            if (GUILayout.Button("Вернуться в меню", _button, GUILayout.Height(36)))
            {
                _screen = UiScreen.Menu;
                PickDefaultFaction();
            }
            GUILayout.EndArea();
        }
    }
}
