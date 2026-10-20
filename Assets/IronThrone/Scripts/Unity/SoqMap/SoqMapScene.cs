// IronThrone: отдельная сцена «Генератор карт SoQ».
// Здесь нет партии: сцена показывает, как работает процедурная генерация карты
// приключений (layout -> регионы -> blueprint), позволяет менять сид/размер и
// убедиться, что один и тот же сид даёт одну и ту же карту.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using IronThrone.Core.Game;
using IronThrone.Core.Worldgen;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronThrone.Unity.SoqMap
{
    /// <summary>Контроллер сцены генератора карт: данные, сид, UI, выбор тайла.</summary>
    public class SoqMapScene : MonoBehaviour
    {
        /// <summary>Имя сцены (должно совпадать с Assets/Scenes/SoQMap.unity).</summary>
        public const string SceneName = "SoQMap";
        private const string MapGenFolder = "MapGen";

        private SoqRmgPack _pack;
        private AdventureMap _map;
        private SoqMapView _view;
        private string _loadError;

        private string _seedText = "1";
        private int _layoutIndex;
        private int _sublayoutIndex = -1;
        private int _size;
        private float _landFraction = 0.70f;
        private bool _showReport;
        private bool _showHelp = true;
        private bool _showDecor = true;
        private string _status = "";
        private int _selectedX = -1;
        private int _selectedY = -1;
        private Vector2 _reportScroll;
        private Vector2 _panelScroll;
        private Texture2D _white;

        private GUIStyle _title, _h1, _body, _small, _button, _buttonActive, _panel, _box;

        public AdventureMap Map => _map;
        public SoqRmgPack Pack => _pack;

        // ---------------------------------------------------------------- жизненный цикл
        private void Awake()
        {
            _white = MakeTex(Color.white);
            LoadPack();
            Regenerate();
        }

        private void LoadPack()
        {
            try
            {
                string contentDir = UnityContent.ContentDirectory;
                if (string.IsNullOrEmpty(contentDir))
                    contentDir = Path.Combine(Application.dataPath, "IronThrone/Resources/IronThroneData");
                string dir = Path.Combine(contentDir, MapGenFolder);
                if (Directory.Exists(dir))
                {
                    _pack = SoqRmgLoader.LoadFromDirectory(dir);
                }
                else
                {
                    // запасной путь: Resources/TextAsset (если данные положили только туда)
                    _pack = SoqRmgLoader.LoadFromDocuments(LoadResourcesDocuments());
                }
                if (_pack.Layouts.Count == 0)
                    _loadError = "В " + dir + " нет ни одного layout'а. Проверьте JSON в Resources/IronThroneData/MapGen.";
            }
            catch (Exception ex)
            {
                _loadError = ex.Message;
                _pack = new SoqRmgPack();
            }
        }

        private static IEnumerable<KeyValuePair<string, string>> LoadResourcesDocuments()
        {
            var names = new[] { "layout_duel", "layout_ring", "blueprint_default", "objects" };
            foreach (var name in names)
            {
                var asset = Resources.Load<TextAsset>("IronThroneData/MapGen/" + name);
                if (asset != null)
                    yield return new KeyValuePair<string, string>(name + ".json", asset.text);
            }
        }

        // ---------------------------------------------------------------- генерация
        private int ParseSeed(string text)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
            return StableHash.Of(string.IsNullOrEmpty(text) ? "0" : text, 1000000000);
        }

        private void Regenerate()
        {
            if (_pack == null || _pack.Layouts.Count == 0) return;
            _layoutIndex = Mathf.Clamp(_layoutIndex, 0, _pack.Layouts.Count - 1);
            int seed = ParseSeed(_seedText);

            var request = new AdventureGenRequest
            {
                LayoutKey = _pack.Layouts[_layoutIndex].Key,
                Seed = seed,
                Size = _size,
                LandFraction = _landFraction,
                SublayoutIndex = _sublayoutIndex,
            };

            try
            {
                _map = SoqMapGenerator.Generate(_pack, request);
                if (_view == null) _view = SoqMapView.Build(_map, _pack.Catalog);
                else _view.Rebuild(_map, _pack.Catalog);
                _view.SetDecorVisible(_showDecor);
                _selectedX = _selectedY = -1;
                _status = $"Сид {seed} · «{_map.LayoutName}»" +
                          (string.IsNullOrEmpty(_map.SublayoutName) ? "" : " / " + _map.SublayoutName) +
                          $" · {_map.Width}×{_map.Height} · объектов {_map.Objects.Count} · деревьев {_map.Decor.Count}";
                if (_map.Report.Count > 1)
                    Debug.Log("Iron Throne / SoQ map: " + _map.Report[0]);
            }
            catch (Exception ex)
            {
                _status = "Ошибка генерации: " + ex.Message;
                Debug.LogError("Iron Throne / SoQ map: " + ex);
            }
        }

        private void RandomSeed()
        {
            int seed = (int)(DateTime.UtcNow.Ticks % 999999999L);
            if (seed < 0) seed = -seed;
            _seedText = seed.ToString(CultureInfo.InvariantCulture);
            Regenerate();
        }

        // ---------------------------------------------------------------- ввод
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.R)) Regenerate();
            else if (Input.GetKeyDown(KeyCode.N)) RandomSeed();
            else if (Input.GetKeyDown(KeyCode.Escape)) BackToMenu();
            else if (Input.GetKeyDown(KeyCode.F1)) _showHelp = !_showHelp;
        }

        private static void BackToMenu()
        {
            try { SceneManager.LoadScene(IronThroneGame.MenuSceneName); }
            catch (Exception ex) { Debug.LogWarning("Iron Throne: не удалось вернуться в меню — " + ex.Message); }
        }

        // ---------------------------------------------------------------- GUI
        private void EnsureStyles()
        {
            if (_panel != null) return;
            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20, fontStyle = FontStyle.Bold, richText = true,
                normal = { textColor = new Color(0.95f, 0.88f, 0.68f) },
            };
            _h1 = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, fontStyle = FontStyle.Bold, richText = true,
                normal = { textColor = new Color(0.86f, 0.74f, 0.42f) },
            };
            _body = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, wordWrap = true, richText = true,
                normal = { textColor = new Color(0.88f, 0.89f, 0.87f) },
            };
            _small = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, wordWrap = true, richText = true,
                normal = { textColor = new Color(0.66f, 0.70f, 0.68f) },
            };
            _panel = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(new Color(0.075f, 0.095f, 0.105f, 0.94f)) },
                padding = new RectOffset(12, 12, 10, 10),
            };
            _box = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(new Color(0.055f, 0.07f, 0.08f, 0.88f)) },
                padding = new RectOffset(10, 10, 8, 8),
            };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.9f, 0.9f, 0.86f) } };
            _buttonActive = new GUIStyle(_button)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.12f, 0.10f, 0.05f), background = MakeTex(new Color(0.78f, 0.63f, 0.28f)) },
            };
        }

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixels32(new[] { new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(c.a * 255)) });
            t.Apply();
            return t;
        }

        private void OnGUI()
        {
            EnsureStyles();
            // Фон рисуем только если карты ещё нет: иначе закрасили бы 3D-вид.
            if (_view == null)
                Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.03f, 0.035f, 1f));

            float panelW = Mathf.Min(360f, Screen.width * 0.32f);
            var panelRect = new Rect(10, 10, panelW, Screen.height - 20);
            GUI.Box(panelRect, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(panelRect.x + 6, panelRect.y + 6, panelRect.width - 12, panelRect.height - 12));

            GUILayout.Label("ГЕНЕРАТОР КАРТ SoQ", _title);
            GUILayout.Label("Карта приключений по модели Songs of Conquest: layout → регионы → blueprint.", _small);

            if (!string.IsNullOrEmpty(_loadError))
                GUILayout.Label("<b>Ошибка данных:</b> " + _loadError, _body);

            GUILayout.Space(8);
            GUILayout.Label("Сид", _h1);
            GUILayout.BeginHorizontal();
            _seedText = GUILayout.TextField(_seedText, 20, GUILayout.Width(Mathf.Max(120f, panelW - 170f)));
            if (GUILayout.Button("Случайный", _button, GUILayout.Width(90))) { RandomSeed(); }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Сгенерировать карту  (R)", _buttonActive, GUILayout.Height(30))) Regenerate();

            GUILayout.Space(8);
            GUILayout.Label("Layout", _h1);
            if (_pack != null)
            {
                for (int i = 0; i < _pack.Layouts.Count; i++)
                {
                    var layout = _pack.Layouts[i];
                    if (GUILayout.Button((i == _layoutIndex ? "● " : "○ ") + layout.DisplayName, i == _layoutIndex ? _buttonActive : _button))
                    {
                        _layoutIndex = i;
                        _sublayoutIndex = -1;
                        Regenerate();
                    }
                }
            }

            GUILayout.Space(6);
            GUILayout.Label("Размер карты", _h1);
            GUILayout.BeginHorizontal();
            int[] sizes = { 32, 48, 64, 96 };
            foreach (int size in sizes)
            {
                bool active = (_size == 0 ? CurrentLayoutSize() : _size) == size;
                if (GUILayout.Button(size.ToString(CultureInfo.InvariantCulture), active ? _buttonActive : _button, GUILayout.Width(46)))
                {
                    _size = size;
                    Regenerate();
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Доля суши", _h1);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("−", _button, GUILayout.Width(36))) { _landFraction = Mathf.Clamp(_landFraction - 0.05f, 0.5f, 0.9f); Regenerate(); }
            GUILayout.Label(Mathf.RoundToInt(_landFraction * 100f) + " %", _body, GUILayout.Width(60));
            if (GUILayout.Button("＋", _button, GUILayout.Width(36))) { _landFraction = Mathf.Clamp(_landFraction + 0.05f, 0.5f, 0.9f); Regenerate(); }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("Что получилось", _h1);
            if (_map != null)
            {
                GUILayout.Label("Размер: <b>" + _map.Width + "×" + _map.Height + "</b>", _body);
                GUILayout.Label("Земля: <b>" + _map.LandCount + "</b> тайлов, областей: <b>" + _map.Regions.Count + "</b>", _body);
                GUILayout.Label("Объектов: <b>" + _map.Objects.Count + "</b>, охраняемых: <b>" + _map.GuardedCount + "</b>", _body);
                GUILayout.Label("Деревьев (декор): <b>" + _map.Decor.Count + "</b>", _body);
                GUILayout.Label("Дорог: <b>" + _map.RoadCount + "</b>, стартов: <b>" + _map.Starts.Count + "</b>", _body);
                GUILayout.Space(4);
                for (int i = 0; i < Mathf.Min(6, _map.Starts.Count); i++)
                {
                    var start = _map.Starts[i];
                    GUILayout.Label("Игрок " + start.player + ": область «" + _map.Regions[start.region].name + "» (" + start.x + "," + start.y + ")", _small);
                }
            }

            GUILayout.Space(6);
            bool decor = GUILayout.Toggle(_showDecor, " Деревья на карте", _body);
            if (decor != _showDecor)
            {
                _showDecor = decor;
                if (_view != null) _view.SetDecorVisible(_showDecor);
            }

            GUILayout.Space(4);
            if (GUILayout.Button(_showReport ? "Скрыть отчёт генерации" : "Показать отчёт генерации", _button)) _showReport = !_showReport;
            if (_showReport && _map != null)
            {
                _reportScroll = GUILayout.BeginScrollView(_reportScroll, GUILayout.Height(150));
                foreach (var line in _map.Report) GUILayout.Label("• " + line, _small);
                GUILayout.EndScrollView();
            }

            GUILayout.Space(6);
            if (_selectedX >= 0 && _map != null)
            {
                var tile = _map.Tile(_selectedX, _selectedY);
                var region = _map.RegionAt(_selectedX, _selectedY);
                var obj = _map.ObjectAt(_selectedX, _selectedY);
                GUILayout.Label("Тайл (" + _selectedX + "," + _selectedY + ")", _h1);
                GUILayout.Label("Рельеф: " + SoqMapView.TerrainName(tile), _body);
                GUILayout.Label("Область: " + (region != null ? region.name : "—") +
                                (region != null ? " · " + AdvTerrainInfo.BiomeName(region.biome) : ""), _body);
                if (tile != null && tile.road) GUILayout.Label("Здесь проходит дорога.", _small);
                if (obj != null)
                {
                    GUILayout.Label("Объект: <b>" + obj.name + "</b> (" + obj.defId + ")", _body);
                    GUILayout.Label("Категория: " + obj.category + (obj.guarded ? " · охраняется (" + obj.guardTier + ")" : ""), _small);
                }
                else GUILayout.Label("Объектов нет.", _small);
            }
            else
            {
                GUILayout.Label("Кликните по карте, чтобы посмотреть тайл.", _small);
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("← В главное меню  (Esc)", _button, GUILayout.Height(28))) BackToMenu();
            if (_showHelp)
                GUILayout.Label("WASD/стрелки — камера, колесо — зум, средняя кнопка — тянуть. R — перегенерировать, N — новый сид.", _small);
            GUILayout.EndArea();

            DrawMapOverlay(panelRect);
        }

        private int CurrentLayoutSize()
        {
            if (_pack == null || _pack.Layouts.Count == 0) return 48;
            int index = Mathf.Clamp(_layoutIndex, 0, _pack.Layouts.Count - 1);
            return _pack.Layouts[index].size;
        }

        private void DrawMapOverlay(Rect panelRect)
        {
            if (_map == null) return;
            var mapRect = new Rect(panelRect.xMax + 8, 10, Screen.width - panelRect.xMax - 18, Screen.height - 56);

            // миникарта
            if (_view != null && _view.Minimap != null)
            {
                float miniW = Mathf.Min(230f, mapRect.width * 0.28f);
                float miniH = miniW * _map.Height / Mathf.Max(1, _map.Width);
                var mini = new Rect(mapRect.xMax - miniW - 10, mapRect.yMax - miniH - 10, miniW, miniH);
                GUI.Box(new Rect(mini.x - 4, mini.y - 4, mini.width + 8, mini.height + 8), GUIContent.none, _box);
                GUI.DrawTexture(mini, _view.Minimap, ScaleMode.ScaleToFit);
            }

            // метки стартовых городов
            if (_view != null)
            {
                foreach (var start in _map.Starts)
                {
                    var gui = _view.WorldToGui(_view.TileWorld(start.x, start.y) + new Vector3(0f, 2.4f, 0f));
                    if (gui.x < mapRect.x || gui.x > mapRect.xMax || gui.y < mapRect.y || gui.y > mapRect.yMax) continue;
                    var style = new GUIStyle(_small)
                    {
                        fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = new Color(1f, 0.95f, 0.75f) },
                    };
                    GUI.Label(new Rect(gui.x - 18, gui.y - 10, 36, 20), "P" + start.player, style);
                }
            }

            // клик по карте
            var e = Event.current;
            if (e != null && e.type == EventType.MouseDown && e.button == 0 && mapRect.Contains(e.mousePosition))
            {
                if (_view != null && _view.PickTile(e.mousePosition, out var x, out var y))
                {
                    _selectedX = x;
                    _selectedY = y;
                }
                e.Use();
            }

            // статус
            if (!string.IsNullOrEmpty(_status))
            {
                GUI.Label(new Rect(mapRect.x + 4, Screen.height - 26, mapRect.width * 0.75f, 22), _status, _small);
            }

            // подсказка о выбранном тайле
            if (_selectedX >= 0)
            {
                var tile = _map.Tile(_selectedX, _selectedY);
                string text = "(" + _selectedX + "," + _selectedY + ") " + SoqMapView.TerrainName(tile);
                GUI.Label(new Rect(mapRect.x + 4, mapRect.y + 2, mapRect.width * 0.7f, 22), text, _body);
            }
        }

        private void Fill(Rect rect, Color color)
        {
            if (_white == null) return;
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _white);
            GUI.color = old;
        }
    }
}
