// IronThrone: 2.5D-вид сгенерированной карты приключений в духе Songs of Conquest.
// Квадратная сетка (как в SoQ), наклонные стенди-спрайты объектов, плоская земля
// из 16 тайлов Resources/Art/SoQ/Terrain, дороги, вода и миникарта.
// Вся география приходит из SoqMapGenerator; здесь только отрисовка.
using System;
using System.Collections.Generic;
using IronThrone.Core.Worldgen;
using UnityEngine;

namespace IronThrone.Unity.SoqMap
{
    /// <summary>Нарезка спрайта в атласе (координаты Unity: от левого нижнего угла).</summary>
    public sealed class SoqSpriteEntry
    {
        public string name;
        public string atlas;
        public int x;
        public int y;
        public int w;
        public int h;
        public float pivotX = 0.5f;
        public float pivotY;
        public float ppu = 32f;
    }

    /// <summary>Загрузка atласов и нарезок объектов из Resources.</summary>
    public static class SoqMapAssets
    {
        public const string SliceDbPath = "Art/soq_sprite_slices";

        private static bool _loaded;
        private static readonly List<SoqSpriteEntry> _sprites = new List<SoqSpriteEntry>();
        private static readonly Dictionary<string, SoqSpriteEntry> _byName =
            new Dictionary<string, SoqSpriteEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _atlasFile =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> _atlasWidth =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> _atlasHeight =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> _textures =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<SoqSpriteEntry> Sprites => _sprites;

        /// <summary>Прочитать soq_sprite_slices.json и разложить нарезки по именам.</summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var asset = Resources.Load<TextAsset>(SliceDbPath);
            if (asset == null || string.IsNullOrEmpty(asset.text))
            {
                Debug.LogWarning("Iron Throne: не найден " + SliceDbPath + " — объекты будут без спрайтов.");
                return;
            }
            try
            {
                var root = IronThrone.Core.JsonValue.Parse(asset.text);
                foreach (var a in root["atlases"].AsArray)
                {
                    string name = a["name"].AsString;
                    if (string.IsNullOrEmpty(name)) continue;
                    _atlasFile[name] = a["file"].AsString;
                    _atlasWidth[name] = a["width"].AsInt;
                    _atlasHeight[name] = a["height"].AsInt;
                }
                foreach (var s in root["sprites"].AsArray)
                {
                    var entry = new SoqSpriteEntry
                    {
                        name = s["name"].AsString,
                        atlas = s["atlas"].AsString,
                        x = s["x"].AsInt,
                        y = s["y"].AsInt,
                        w = s["w"].AsInt,
                        h = s["h"].AsInt,
                        pivotX = (float)s["pivotX"].AsNumber,
                        pivotY = (float)s["pivotY"].AsNumber,
                        ppu = (float)s["ppu"].AsNumber,
                    };
                    if (string.IsNullOrEmpty(entry.name) || entry.w <= 0 || entry.h <= 0) continue;
                    _sprites.Add(entry);
                    _byName[entry.name] = entry;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Iron Throne: ошибка чтения нарезок объектов — " + ex.Message);
            }
        }

        public static SoqSpriteEntry Sprite(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            EnsureLoaded();
            return _byName.TryGetValue(name, out var s) ? s : null;
        }

        public static Texture2D AtlasTexture(string atlas)
        {
            if (string.IsNullOrEmpty(atlas)) return null;
            EnsureLoaded();
            if (_textures.TryGetValue(atlas, out var cached)) return cached;
            if (!_atlasFile.TryGetValue(atlas, out var file) || string.IsNullOrEmpty(file)) return null;
            var tex = Resources.Load<Texture2D>(file);
            if (tex != null) _textures[atlas] = tex;
            return tex;
        }

        public static int AtlasWidth(string atlas) => _atlasWidth.TryGetValue(atlas, out var w) ? w : 0;
        public static int AtlasHeight(string atlas) => _atlasHeight.TryGetValue(atlas, out var h) ? h : 0;

        /// <summary>Добавить спрайты в каталог (генератор получает их из того же пакета).</summary>
        public static void MergeInto(AdventureCatalog catalog)
        {
            if (catalog == null) return;
            EnsureLoaded();
            if (_sprites.Count == 0) return;
            foreach (var s in _sprites)
            {
                if (catalog.SpriteByName.ContainsKey(s.name)) continue;
                var sprite = new AdventureSprite
                {
                    name = s.name,
                    atlas = s.atlas,
                    x = s.x,
                    y = s.y,
                    w = s.w,
                    h = s.h,
                    pivotX = s.pivotX,
                    pivotY = s.pivotY,
                    ppu = s.ppu,
                };
                catalog.Sprites.Add(sprite);
                catalog.SpriteByName[sprite.name] = sprite;
            }
            foreach (var kv in _atlasFile)
            {
                if (catalog.Atlas(kv.Key) != null) continue;
                catalog.Atlases.Add(new AdventureAtlas
                {
                    name = kv.Key,
                    file = kv.Value,
                    width = _atlasWidth.TryGetValue(kv.Key, out var w) ? w : 0,
                    height = _atlasHeight.TryGetValue(kv.Key, out var h) ? h : 0,
                    pixelsPerUnit = 32f,
                });
            }
        }
    }

    /// <summary>Вид карты приключений: земля, вода, дороги, границы, объекты, миникарта.</summary>
    public class SoqMapView : MonoBehaviour
    {
        /// <summary>Размер тайла в мировых единицах (тайл = 4×4).</summary>
        public const float TileSize = 4f;
        /// <summary>Наклон стенди, как в Songs of Conquest.</summary>
        public const float TiltDeg = 52f;
        /// <summary>Базовый масштаб спрайтов (пикселей на юнит).</summary>
        public const float Ppu = 32f;
        /// <summary>Поверхность земли.</summary>
        public const float GroundY = 0.02f;
        /// <summary>
        /// Во сколько раз увеличивать спрайты объектов относительно «пиксели / PPU».
        /// В SoQ шахта занимает примерно тайл, город — полтора-два; при TileSize = 4
        /// и PPU = 32 этого не хватало, и объекты выглядели точками.
        /// </summary>
        public const float ObjectScale = 1.6f;
        /// <summary>
        /// Тайлы рельефа уже нормализованы по средней яркости (tools/soq_bake_terrain.py),
        /// поэтому здесь только общая поправка на «сочность» картинки.
        /// </summary>
        public const float GroundBrightness = 1.05f;
        /// <summary>Сколько тайлов занимает один период текстуры (общий UV-континуум).</summary>
        public const float GroundUvScale = 0.5f;
        /// <summary>Разрешение control-текстур (веса биомов) в сэмплах на тайл.</summary>
        private const int ControlDetail = 4;

        private static readonly string[] TerrainFiles = AdvTerrainInfo.TileNames;
        /// <summary>Папка тайлов рельефа карты SoQ внутри Resources.</summary>
        private const string TerrainRoot = "Art/SoQ/Terrain/";

        private static readonly Color[] RegionPalette =
        {
            new Color(0.98f, 0.82f, 0.42f), new Color(0.62f, 0.84f, 0.98f),
            new Color(0.98f, 0.62f, 0.58f), new Color(0.68f, 0.92f, 0.64f),
            new Color(0.86f, 0.70f, 0.98f), new Color(0.98f, 0.90f, 0.62f),
            new Color(0.60f, 0.90f, 0.86f), new Color(0.96f, 0.66f, 0.82f),
            new Color(0.80f, 0.86f, 0.58f), new Color(0.72f, 0.76f, 0.98f),
            new Color(0.98f, 0.74f, 0.52f), new Color(0.66f, 0.80f, 0.72f),
        };

        private AdventureMap _map;
        private AdventureCatalog _catalog;
        private GameObject _root;
        private GameObject _groundRoot;
        private GameObject _objectsRoot;
        private GameObject _decorRoot;
        private bool _decorVisible = true;
        private WorldMapCameraRig _rig;
        private Camera _cam;
        private Texture2D _minimap;
        private Texture2D _white;
        private Material _groundMat;
        private Material _waterMat;
        private Texture2D _coastTex;
        private Texture2DArray _terrainArray;
        private Texture2D _macroTex;
        private Texture2D _detailTex;
        private Texture2D _roadMask;
        private Mesh _flatMesh;
        private Material _shadowMat;
        private Mesh _shadowMesh;
        private Material _objectMat;
        /// <summary>Все созданные материалы/меши: освобождаются при перегенерации.</summary>
        private readonly List<Material> _ownedMaterials = new List<Material>();
        private readonly Dictionary<string, Material> _atlasMaterials =
            new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        public AdventureMap Map => _map;
        public WorldMapCameraRig Rig => _rig;
        public Camera MapCamera => _cam;
        public Texture2D Minimap => _minimap;
        public float WorldSize => _map != null ? _map.Width * TileSize : 0f;

        public static SoqMapView Build(AdventureMap map, AdventureCatalog catalog)
        {
            var go = new GameObject("SoqMapView");
            var view = go.AddComponent<SoqMapView>();
            view.BuildInternal(map, catalog);
            return view;
        }

        private void BuildInternal(AdventureMap map, AdventureCatalog catalog)
        {
            _map = map;
            _catalog = catalog;
            if (_map == null) return;

            SoqMapAssets.EnsureLoaded();
            if (_catalog != null) SoqMapAssets.MergeInto(_catalog);

            _white = SolidTexture(Color.white);
            _shadowMat = CreateShadowMaterial();
            _shadowMesh = BuildShadowMesh();
            _root = new GameObject("SoqMapRoot");
            _root.transform.parent = transform;

            _groundRoot = new GameObject("Ground");
            _groundRoot.transform.parent = _root.transform;
            _objectsRoot = new GameObject("Objects");
            _objectsRoot.transform.parent = _root.transform;
            _decorRoot = new GameObject("Decor");
            _decorRoot.transform.parent = _root.transform;

            BuildGround();
            BuildWater();
            BuildRoads();
            BuildBorders();
            BuildDecor();
            BuildObjects();
            BuildMinimap();
            SetupCamera();
            if (_decorRoot != null) _decorRoot.SetActive(_decorVisible);
        }

        /// <summary>Перестроить карту после новой генерации.</summary>
        public void Rebuild(AdventureMap map, AdventureCatalog catalog)
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _atlasMaterials.Clear();
            for (int i = 0; i < _ownedMaterials.Count; i++)
                if (_ownedMaterials[i] != null) UnityEngine.Object.Destroy(_ownedMaterials[i]);
            _ownedMaterials.Clear();
            if (_shadowMesh != null) UnityEngine.Object.Destroy(_shadowMesh);
            if (_coastTex != null) UnityEngine.Object.Destroy(_coastTex);
            if (_macroTex != null) UnityEngine.Object.Destroy(_macroTex);
            if (_detailTex != null) UnityEngine.Object.Destroy(_detailTex);
            if (_roadMask != null) UnityEngine.Object.Destroy(_roadMask);
            _macroTex = null;
            _detailTex = null;
            if (_flatMesh != null) UnityEngine.Object.Destroy(_flatMesh);
            _coastTex = null;
            _roadMask = null;
            _flatMesh = null;
            if (_white != null) UnityEngine.Object.Destroy(_white);
            if (_minimap != null) UnityEngine.Object.Destroy(_minimap);
            _waterMat = null;
            _shadowMat = null;
            _shadowMesh = null;
            _white = null;
            _minimap = null;
            BuildInternal(map, catalog);
        }

        public void SetVisible(bool visible)
        {
            if (_root != null) _root.SetActive(visible);
        }

        /// <summary>Показать/скрыть деревья на пустой земле.</summary>
        public void SetDecorVisible(bool visible)
        {
            _decorVisible = visible;
            if (_decorRoot != null) _decorRoot.SetActive(visible);
        }

        // ---------------------------------------------------------------- материалы
        private static Texture2D SolidTexture(Color color)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixels32(new[] { new Color32((byte)(color.r * 255), (byte)(color.g * 255), (byte)(color.b * 255), (byte)(color.a * 255)) });
            t.Apply();
            return t;
        }

        private Shader LoadSpriteShader()
        {
            var shader = Resources.Load<Shader>("Shaders/IronThroneSoqMapSprite");
            if (shader == null) shader = Shader.Find("IronThrone/SoqMapSprite");
            return shader;
        }

        private Material CreateGroundMaterial(Texture2D tex, Color tint)
        {
            var shader = LoadSpriteShader();
            if (shader == null) return null;
            var mat = new Material(shader);
            _ownedMaterials.Add(mat);
            mat.mainTexture = tex != null ? tex : _white;
            mat.SetColor("_Color", tint);
            mat.SetFloat("_Cutoff", 0.01f);
            mat.SetFloat("_UseRamp", 0f);
            var ramp = Resources.Load<Texture2D>("Art/AdventureCartographyLightRampTexture");
            if (ramp != null) mat.SetTexture("_LightRamp", ramp);
            return mat;
        }

        private Material ObjectMaterial(string atlasName)
        {
            if (_atlasMaterials.TryGetValue(atlasName, out var cached)) return cached;
            var tex = SoqMapAssets.AtlasTexture(atlasName);
            var shader = LoadSpriteShader();
            if (shader == null) return null;
            var mat = new Material(shader);
            _ownedMaterials.Add(mat);
            mat.mainTexture = tex != null ? tex : _white;
            mat.SetColor("_Color", Color.white);
            mat.SetFloat("_Cutoff", 0.35f);
            mat.SetFloat("_UseRamp", 1f);
            mat.SetFloat("_RampStrength", 0.9f);
            mat.SetFloat("_RampFlat", 0.35f);
            var ramp = Resources.Load<Texture2D>("Art/AdventureCartographyLightRampTexture");
            if (ramp != null) mat.SetTexture("_LightRamp", ramp);
            _atlasMaterials[atlasName] = mat;
            return mat;
        }

        /// <summary>Мягкая тень под крупными объектами: жирный чёрный круг с прозрачным краем.</summary>
        private Material CreateShadowMaterial()
        {
            var shader = LoadSpriteShader();
            if (shader == null) return null;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a *= a * 0.38f;
                    px[y * size + x] = new Color32(0, 0, 0, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            var mat = new Material(shader);
            _ownedMaterials.Add(mat);
            mat.mainTexture = tex;
            mat.SetColor("_Color", Color.white);
            mat.SetFloat("_Cutoff", 0.04f);
            mat.SetFloat("_UseRamp", 0f);
            return mat;
        }

        private static Mesh BuildShadowMesh()
        {
            var mesh = new Mesh { name = "shadow" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f),
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private void AttachShadow(float x, float z, float diameter)
        {
            if (_shadowMat == null || _shadowMesh == null) return;
            var go = new GameObject("shadow");
            go.transform.parent = _objectsRoot.transform;
            go.transform.position = new Vector3(x, GroundY + 0.006f, z);
            go.transform.localScale = new Vector3(diameter, 1f, diameter * 0.55f);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _shadowMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _shadowMat;
        }
        // ---------------------------------------------------------------- земля

        /// <summary>
        /// Меш земли: одна сетка на всю карту. Цвет не в вертексных цветах, а
        /// в шейдере: 16 классов рельефа смешиваются по весам из control-текстур,
        /// поэтому переходы между биомами мягкие, как в SoQ.
        /// </summary>
        private void BuildGround()
        {
            if (_map == null) return;
            int W = _map.Width, H = _map.Height;

            _terrainArray = BuildTerrainArray();
            var shader = Resources.Load<Shader>("Shaders/IronThroneSoqMapGround");
            if (shader == null) shader = Shader.Find("IronThrone/SoqMapGround");
            if (shader == null) return;

            _groundMat = new Material(shader);
            _ownedMaterials.Add(_groundMat);
            if (_terrainArray != null) _groundMat.SetTexture("_TerrainArray", _terrainArray);
            _groundMat.SetFloat("_Brightness", GroundBrightness);
            _groundMat.SetColor("_SandColor", new Color(0.78f, 0.70f, 0.48f));
            _groundMat.SetColor("_WaterColor", new Color(0.07f, 0.17f, 0.20f));
            _groundMat.SetFloat("_ShoreWidth", 0.42f);
            // фактура повторяется каждые ~3.2 тайла: реже — заметна сетка,
            // чаще — теряется рисунок
            _groundMat.SetFloat("_TileScale", W / 3.2f);

            // два масштаба «погоды» карты: крупные пятна и мелкое зерно.
            // Без них земля читается как однотонная заливка, особенно когда
            // камера смотрит на карту целиком
            float world = W * TileSize;
            _macroTex = BuildNoiseTexture(128, 6, 11, "SoqGroundMacro");
            _detailTex = BuildNoiseTexture(128, 48, 29, "SoqGroundDetail");
            _groundMat.SetTexture("_MacroTex", _macroTex);
            _groundMat.SetTexture("_DetailTex", _detailTex);
            _groundMat.SetFloat("_MacroScale", world / 46f);
            _groundMat.SetFloat("_DetailScale", world / 2.6f);
            _groundMat.SetFloat("_MacroStrength", 0.11f);
            _groundMat.SetFloat("_DetailStrength", 0.07f);
            _groundMat.SetFloat("_ForestShade", 0.30f);

            BuildControlTextures(W, H);

            // сетка: 2 ячейки на тайл — рельеф не «ломается» по клеткам
            const int perTile = 2;
            int cx = W * perTile, cz = H * perTile;
            float sizeX = W * TileSize, sizeZ = H * TileSize;
            var verts = new Vector3[(cx + 1) * (cz + 1)];
            var uvs = new Vector2[verts.Length];
            int vi = 0;
            for (int j = 0; j <= cz; j++)
            {
                for (int i = 0; i <= cx; i++)
                {
                    float x = sizeX * i / cx;
                    float z = sizeZ * j / cz;
                    verts[vi] = new Vector3(x, GroundY, z);
                    // uv = координаты карты 0..1: шейдер сам решает, какой тайл рисовать
                    uvs[vi] = new Vector2((float)i / cx, (float)j / cz);
                    vi++;
                }
            }
            var tris = new int[cx * cz * 6];
            int ti = 0;
            for (int j = 0; j < cz; j++)
            {
                for (int i = 0; i < cx; i++)
                {
                    int v0 = j * (cx + 1) + i;
                    int v1 = v0 + 1;
                    int v2 = v0 + (cx + 1);
                    int v3 = v2 + 1;
                    tris[ti++] = v0; tris[ti++] = v2; tris[ti++] = v1;
                    tris[ti++] = v1; tris[ti++] = v2; tris[ti++] = v3;
                }
            }
            var mesh = new Mesh { name = "ground" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            AttachMesh("ground", mesh, _groundMat, _groundRoot.transform);
        }

        /// <summary>
        /// Texture2DArray из 16 тайлов Resources/Art/SoQ/Terrain. Это отдельный
        /// набор, испечённый из настоящих текстур SoQ (tools/soq_bake_terrain.py):
        /// тайлы основной карты партии (Art/Terrain) не меняются.
        /// </summary>
        private static Texture2DArray BuildTerrainArray()
        {
            var first = Resources.Load<Texture2D>(TerrainRoot + TerrainFiles[0]);
            if (first == null) return null;
            int tw = first.width, th = first.height;
            var arr = new Texture2DArray(tw, th, TerrainFiles.Length, TextureFormat.RGBA32, false);
            arr.filterMode = FilterMode.Bilinear;
            arr.wrapMode = TextureWrapMode.Repeat;
            for (int i = 0; i < TerrainFiles.Length; i++)
            {
                var tex = Resources.Load<Texture2D>(TerrainRoot + TerrainFiles[i]);
                if (tex == null) continue;
                var px = tex.GetPixels32();
                if (px == null || px.Length != tw * th) continue;
                arr.SetPixels32(px, 0, i);
            }
            arr.Apply();
            return arr;
        }

        /// <summary>
        /// Бесшовная (по построению — тайлится) карта value-noise: решётка
        /// cells x cells со сглаженными переходами. Мелкое зерно даёт «шум»
        /// текстуры, крупная — пятна света и тени на лугах.
        /// </summary>
        private static Texture2D BuildNoiseTexture(int size, int cells, int seed, string name)
        {
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float fy = (y + 0.5f) / size * cells;
                int y0 = Mathf.FloorToInt(fy);
                float ty = fy - y0;
                ty = ty * ty * (3f - 2f * ty);
                for (int x = 0; x < size; x++)
                {
                    float fx = (x + 0.5f) / size * cells;
                    int x0 = Mathf.FloorToInt(fx);
                    float tx = fx - x0;
                    tx = tx * tx * (3f - 2f * tx);

                    // решётка замкнута по модулю cells — текстура тайлится
                    int ax = ((x0 % cells) + cells) % cells;
                    int ay = ((y0 % cells) + cells) % cells;
                    int bx = (ax + 1) % cells;
                    int by = (ay + 1) % cells;
                    float v00 = SoqRng.Hash01(ax, ay, seed);
                    float v10 = SoqRng.Hash01(bx, ay, seed);
                    float v01 = SoqRng.Hash01(ax, by, seed);
                    float v11 = SoqRng.Hash01(bx, by, seed);
                    float v = Mathf.Lerp(Mathf.Lerp(v00, v10, tx), Mathf.Lerp(v01, v11, tx), ty);
                    byte b = (byte)(Mathf.Clamp01(v) * 255f);
                    px[y * size + x] = new Color32(b, b, b, 255);
                }
            }
            var tex = MakeTex(size, size, px, name);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }

        /// <summary>
        /// Control-текстуры: на каждый сэмпл — билинейные веса четырёх ближайших
        /// тайлов. Плюс карта берега (R = суша, G = расстояние до суши).
        /// </summary>
        private void BuildControlTextures(int W, int H)
        {
            int tw = W * ControlDetail, th = H * ControlDetail;
            var c0 = new Color32[tw * th];
            var c1 = new Color32[tw * th];
            var c2 = new Color32[tw * th];
            var c3 = new Color32[tw * th];
            var coast = new Color32[tw * th];

            var depth = ComputeWaterDepth(W, H);
            var coastField = ComputeCoastField(W, H);
            var wts = new float[TerrainFiles.Length];

            for (int j = 0; j < th; j++)
            {
                float ty = (j + 0.5f) / ControlDetail - 0.5f;   // в координатах тайлов
                int y0 = Mathf.FloorToInt(ty);
                float fy = ty - y0;
                for (int i = 0; i < tw; i++)
                {
                    float tx = (i + 0.5f) / ControlDetail - 0.5f;
                    int x0 = Mathf.FloorToInt(tx);
                    float fx = tx - x0;

                    for (int k = 0; k < wts.Length; k++) wts[k] = 0f;
                    float land = 0f;
                    AccumTile(x0, y0, (1 - fx) * (1 - fy), W, H, wts, ref land);
                    AccumTile(x0 + 1, y0, fx * (1 - fy), W, H, wts, ref land);
                    AccumTile(x0, y0 + 1, (1 - fx) * fy, W, H, wts, ref land);
                    AccumTile(x0 + 1, y0 + 1, fx * fy, W, H, wts, ref land);

                    float sum = 0f;
                    for (int k = 0; k < wts.Length; k++) sum += wts[k];
                    if (sum > 0.0001f)
                        for (int k = 0; k < wts.Length; k++) wts[k] /= sum;

                    int p = j * tw + i;
                    c0[p] = Pack4(wts, 0);
                    c1[p] = Pack4(wts, 4);
                    c2[p] = Pack4(wts, 8);
                    c3[p] = Pack4(wts, 12);

                    int cx0 = Mathf.Clamp(i / ControlDetail, 0, W - 1);
                    int cy0 = Mathf.Clamp(j / ControlDetail, 0, H - 1);
                    // глубина берётся с тайла: 0 у берега, 1 в открытом море
                    byte d = (byte)(Mathf.Clamp01(depth[cy0 * W + cx0]) * 255f);
                    // берег — сглаженное поле расстояния, а не флаг тайла:
                    // именно это убирает «лесенку» по краю островов
                    byte lf = (byte)(Mathf.Clamp01(coastField[cy0 * W + cx0]) * 255f);
                    coast[p] = new Color32(lf, d, 0, 255);
                }
            }

            _groundMat.SetTexture("_ControlA", MakeTex(tw, th, c0, "ControlA"));
            _groundMat.SetTexture("_ControlB", MakeTex(tw, th, c1, "ControlB"));
            _groundMat.SetTexture("_ControlC", MakeTex(tw, th, c2, "ControlC"));
            _groundMat.SetTexture("_ControlD", MakeTex(tw, th, c3, "ControlD"));
            if (_coastTex != null) UnityEngine.Object.Destroy(_coastTex);
            _coastTex = MakeTex(tw, th, coast, "Coast");
            _groundMat.SetTexture("_Coast", _coastTex);
        }

        private void AccumTile(int x, int y, float w, int W, int H, float[] wts, ref float land)
        {
            if (w <= 0f) return;
            var tile = _map.Tile(Mathf.Clamp(x, 0, W - 1), Mathf.Clamp(y, 0, H - 1));
            if (tile == null || tile.sea) return;
            int cls = Mathf.Clamp((int)tile.terrain, 0, wts.Length - 1);
            wts[cls] += w;
            land += w;
        }

        private static Color32 Pack4(float[] wts, int offset)
        {
            return new Color32(
                (byte)(Mathf.Clamp01(wts[offset + 0]) * 255f),
                (byte)(Mathf.Clamp01(wts[offset + 1]) * 255f),
                (byte)(Mathf.Clamp01(wts[offset + 2]) * 255f),
                (byte)(Mathf.Clamp01(wts[offset + 3]) * 255f));
        }

        /// <summary>Расстояние от моря до суши в тайлах, нормированное к 0..1.</summary>
        private float[] ComputeWaterDepth(int W, int H)
        {
            int n = W * H;
            var depth = new float[n];
            var visited = new bool[n];
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                var t = _map.Tiles[i];
                if (!t.sea) { visited[i] = true; queue.Enqueue(i); }
            }
            int level = 0;
            const int maxDist = 8;
            while (queue.Count > 0 && level <= maxDist)
            {
                int count = queue.Count;
                for (int k = 0; k < count; k++)
                {
                    int cur = queue.Dequeue();
                    int x = cur % W, y = cur / W;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + AdventureMap.Dx[d], ny = y + AdventureMap.Dy[d];
                        if (!_map.InBounds(nx, ny)) continue;
                        int ni = ny * W + nx;
                        if (visited[ni]) continue;
                        visited[ni] = true;
                        depth[ni] = (level + 1) / (float)maxDist;
                        queue.Enqueue(ni);
                    }
                }
                level++;
            }
            return depth;
        }

        /// <summary>
        /// Поле берега 0..1 (0.5 — линия воды). Считается как расстояние до линии
        /// воды в тайлах: у суши 0.5..1, у моря 0..0.5. Край острова получается
        /// плавным, а не ступеньками по клеткам.
        /// </summary>
        private float[] ComputeCoastField(int W, int H)
        {
            int n = W * H;
            var field = new float[n];
            var visited = new bool[n];
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                // база: суша = 1, море = 0; у самой воды уточним по расстоянию
                field[i] = _map.Tiles[i].sea ? 0f : 1f;
                if (IsCoastTile(i % W, i / W))
                {
                    field[i] = 0.5f;
                    visited[i] = true;
                    queue.Enqueue(i);
                }
            }
            const int spread = 3;
            int level = 0;
            while (queue.Count > 0 && level < spread)
            {
                int count = queue.Count;
                for (int k = 0; k < count; k++)
                {
                    int cur = queue.Dequeue();
                    int x = cur % W, y = cur / W;
                    bool sea = _map.Tiles[cur].sea;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + AdventureMap.Dx[d], ny = y + AdventureMap.Dy[d];
                        if (!_map.InBounds(nx, ny)) continue;
                        int ni = ny * W + nx;
                        if (visited[ni]) continue;
                        if (_map.Tiles[ni].sea != sea) continue;
                        visited[ni] = true;
                        float step = (level + 1) / (float)(spread + 1);
                        field[ni] = sea ? 0.5f - step * 0.45f : 0.5f + step * 0.45f;
                        queue.Enqueue(ni);
                    }
                }
                level++;
            }
            return field;
        }

        private bool IsCoastTile(int x, int y)
        {
            bool sea = _map.Tile(x, y) != null && _map.Tile(x, y).sea;
            for (int d = 0; d < 4; d++)
            {
                var nb = _map.Tile(x + AdventureMap.Dx[d], y + AdventureMap.Dy[d]);
                if (nb == null) continue;
                if (nb.sea != sea) return true;
            }
            return false;
        }

        private static Texture2D MakeTex(int w, int h, Color32[] px, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.name = name;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        public Color RegionColor(AdventureRegion region)
        {
            if (region == null) return Color.white;
            int index = region.id % RegionPalette.Length;
            if (index < 0) index += RegionPalette.Length;
            return RegionPalette[index];
        }

        // ---------------------------------------------------------------- вода
        private void BuildWater()
        {
            if (_map == null) return;
            float sizeX = _map.Width * TileSize;
            float sizeZ = _map.Height * TileSize;
            var shader = Resources.Load<Shader>("Shaders/IronThroneSoqMapWater");
            if (shader == null) shader = Shader.Find("IronThrone/SoqMapWater");
            if (shader == null) return;

            _waterMat = new Material(shader);
            _ownedMaterials.Add(_waterMat);
            var shallow = Resources.Load<Texture2D>("Art/ArleonAdventureWaterShallowSurfaceTexture");
            var deep = Resources.Load<Texture2D>("Art/ArleonAdventureWaterDeepSurfaceTexture");
            if (shallow != null) _waterMat.SetTexture("_ShallowTex", shallow);
            if (deep != null) _waterMat.SetTexture("_DeepTex", deep);
            if (_coastTex != null) _waterMat.SetTexture("_Coast", _coastTex);
            _waterMat.SetColor("_Color", new Color(0.09f, 0.22f, 0.24f, 0.94f));
            _waterMat.SetColor("_ShallowColor", new Color(0.26f, 0.44f, 0.40f, 1f));
            _waterMat.SetColor("_FoamColor", new Color(0.80f, 0.90f, 0.90f, 1f));
            _waterMat.SetFloat("_Tiling", 0.055f);
            _waterMat.SetFloat("_Speed", 0.010f);
            _waterMat.SetFloat("_FoamWidth", 0.075f);

            var verts = new[]
            {
                new Vector3(0, 0, 0), new Vector3(sizeX, 0, 0),
                new Vector3(sizeX, 0, sizeZ), new Vector3(0, 0, sizeZ),
            };
            var uvs = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            var mesh = new Mesh { name = "water", vertices = verts, uv = uvs, triangles = new[] { 0, 2, 1, 0, 3, 2 } };
            mesh.RecalculateBounds();
            var go = new GameObject("water");
            go.transform.parent = _root.transform;
            go.transform.position = new Vector3(0f, GroundY + 0.53f, 0f);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _waterMat;
        }

        // ---------------------------------------------------------------- дороги

        /// <summary>
        /// Дороги рисуются не отдельными квадами, а маской: в узлах дорожной сети
        /// ставится «пятно», вдоль связей — капсула, маска размывается. Поэтому
        /// перекрёстки круглые, а лента непрерывная — как в SoQ, где дорога
        /// вплавлена в землю, а не лежит поверх клеток.
        /// </summary>
        private void BuildRoads()
        {
            if (_map == null) return;
            var roadTex = Resources.Load<Texture2D>("Art/SoQ/soq_road");
            int W = _map.Width, H = _map.Height;
            const int samples = 4;                  // сэмплов маски на тайл
            int tw = W * samples, th = H * samples;
            var mask = new float[tw * th];
            float radius = TileSize * 0.175f;
            float cell = TileSize / samples;

            // узлы + капсулы вдоль связей
            foreach (var tile in _map.AllTiles())
            {
                if (tile.sea || !tile.road) continue;
                float cx = (tile.x + 0.5f) * TileSize;
                float cz = (tile.y + 0.5f) * TileSize;
                SplatDisc(mask, tw, th, cell, cx, cz, radius * 1.25f);
                for (int d = 0; d < 4; d++)
                {
                    int nx = tile.x + AdventureMap.Dx[d], ny = tile.y + AdventureMap.Dy[d];
                    var nt = _map.Tile(nx, ny);
                    if (nt == null || nt.sea || !nt.road) continue;
                    if (ny < tile.y || (ny == tile.y && nx < tile.x)) continue;
                    float ex = (nx + 0.5f) * TileSize;
                    float ez = (ny + 0.5f) * TileSize;
                    SplatCapsule(mask, tw, th, cell, cx, cz, ex, ez, radius);
                }
            }

            // размытие снимает лесенку от клеток; затем кромка поджимается,
            // чтобы полотно читалось как дорога, а не как облако пыли
            for (int pass = 0; pass < 2; pass++)
                mask = Blur(mask, tw, th);
            for (int i = 0; i < mask.Length; i++)
            {
                float v = Mathf.Clamp01((mask[i] - 0.28f) / 0.44f);
                mask[i] = v * v * (3f - 2f * v);
            }

            var px = new Color32[tw * th];
            for (int i = 0; i < px.Length; i++)
            {
                byte a = (byte)(Mathf.Clamp01(mask[i]) * 255f);
                px[i] = new Color32(a, a, a, a);
            }
            var maskTex = MakeTex(tw, th, px, "RoadMask");
            maskTex.wrapMode = TextureWrapMode.Clamp;
            _roadMask = maskTex;

            var shader = LoadSpriteShader();
            if (shader == null) return;

            var mat = new Material(shader);
            _ownedMaterials.Add(mat);
            mat.mainTexture = roadTex != null ? roadTex : _white;
            mat.SetTexture("_MaskTex", maskTex);
            mat.SetFloat("_UseMask", 1f);
            mat.SetFloat("_MaskPower", 1.0f);
            mat.SetColor("_Color", new Color(0.98f, 0.95f, 0.90f, 1f));
            mat.SetFloat("_Cutoff", 0.01f);
            mat.SetFloat("_UseRamp", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = 2450;

            // плоскость на всю карту: UV = координаты карты, текстура дороги тайлится миром
            float sizeX = W * TileSize, sizeZ = H * TileSize;
            var verts = new[]
            {
                new Vector3(0, GroundY + 0.014f, 0),
                new Vector3(sizeX, GroundY + 0.014f, 0),
                new Vector3(sizeX, GroundY + 0.014f, sizeZ),
                new Vector3(0, GroundY + 0.014f, sizeZ),
            };
            // UV: 0..1 по карте (для маски), а тайлинг фактуры — через _MainTex_ST
            var uvs = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mat.mainTextureScale = new Vector2(sizeX / (TileSize * 0.9f), sizeZ / (TileSize * 0.9f));
            var mesh = new Mesh { name = "roads", vertices = verts, uv = uvs, triangles = new[] { 0, 2, 1, 0, 3, 2 } };
            mesh.RecalculateBounds();
            AttachMesh("roads", mesh, mat, _root.transform);
        }

        /// <summary>
        /// Границы регионов: тонкая цветная линия по кромке владений (как в SoQ —
        /// цвет региона виден на стыке, где заканчивается один и начинается другой).
        /// </summary>
        private void BuildBorders()
        {
            if (_map == null) return;
            var verts = new List<Vector3>();
            var colors = new List<Color32>();
            var tris = new List<int>();
            float width = TileSize * 0.045f;
            foreach (var tile in _map.AllTiles())
            {
                if (tile.sea) continue;
                var color = RegionColor(_map.RegionAt(tile.x, tile.y));
                AddBorderEdge(verts, colors, tris, tile, 1, 0, width, color);
                AddBorderEdge(verts, colors, tris, tile, -1, 0, width, color);
                AddBorderEdge(verts, colors, tris, tile, 0, 1, width, color);
                AddBorderEdge(verts, colors, tris, tile, 0, -1, width, color);
            }
            if (tris.Count == 0) return;

            var mat = CreateGroundMaterial(_white, Color.white);
            if (mat == null) return;
            mat.SetFloat("_Cutoff", 0.01f);
            mat.SetFloat("_UseRamp", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = 2440;   // под дорогой (2450), над землёй

            var mesh = new Mesh { name = "borders" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts.ToArray();
            mesh.colors32 = colors.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateBounds();
            AttachMesh("borders", mesh, mat, _root.transform);
        }

        /// <summary>Квад вдоль кромки тайла, если сосед — другой регион или море.</summary>
        private void AddBorderEdge(List<Vector3> verts, List<Color32> colors, List<int> tris,
            AdventureTile tile, int dx, int dy, float width, Color color)
        {
            var neighbor = _map.Tile(tile.x + dx, tile.y + dy);
            // по берегу линию не рисуем: там уже работает подсветка мелководья,
            // а цветная кромка вдоль всего острова выглядит как обводка
            if (neighbor == null || neighbor.sea) return;
            if (neighbor.region == tile.region) return;

            float cx = (tile.x + 0.5f) * TileSize;
            float cz = (tile.y + 0.5f) * TileSize;
            float half = TileSize * 0.5f;
            float y = GroundY + 0.012f;
            Color32 c = new Color(color.r, color.g, color.b, 0.30f);

            if (dx != 0)
            {
                float x = cx + dx * half;
                AddQuadRaw(verts, colors, tris,
                    new Vector3(x - width, y, cz - half),
                    new Vector3(x + width, y, cz + half), c);
            }
            else
            {
                float z = cz + dy * half;
                AddQuadRaw(verts, colors, tris,
                    new Vector3(cx - half, y, z - width),
                    new Vector3(cx + half, y, z + width), c);
            }
        }

        private static void AddQuadRaw(List<Vector3> verts, List<Color32> colors, List<int> tris,
            Vector3 a, Vector3 b, Color32 color)
        {
            // a и b — противоположные углы (x/z), строим прямоугольник по земле
            int v = verts.Count;
            verts.Add(new Vector3(a.x, a.y, a.z));
            verts.Add(new Vector3(b.x, a.y, a.z));
            verts.Add(new Vector3(b.x, a.y, b.z));
            verts.Add(new Vector3(a.x, a.y, b.z));
            colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
            tris.Add(v + 0); tris.Add(v + 2); tris.Add(v + 1);
            tris.Add(v + 0); tris.Add(v + 3); tris.Add(v + 2);
        }

        private static void SplatDisc(float[] mask, int tw, int th, float cell, float cx, float cz, float radius)
        {
            int x0 = Mathf.FloorToInt((cx - radius) / cell), x1 = Mathf.CeilToInt((cx + radius) / cell);
            int y0 = Mathf.FloorToInt((cz - radius) / cell), y1 = Mathf.CeilToInt((cz + radius) / cell);
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(th - 1, y1); y++)
            {
                float wz = (y + 0.5f) * cell;
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(tw - 1, x1); x++)
                {
                    float wx = (x + 0.5f) * cell;
                    float d = Mathf.Sqrt((wx - cx) * (wx - cx) + (wz - cz) * (wz - cz));
                    float v = Mathf.Clamp01(1f - (d - radius * 0.55f) / (radius * 0.45f));
                    int i = y * tw + x;
                    if (v > mask[i]) mask[i] = v;
                }
            }
        }

        private static void SplatCapsule(float[] mask, int tw, int th, float cell,
            float ax, float az, float bx, float bz, float radius)
        {
            float len = Mathf.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            int steps = Mathf.Max(2, Mathf.CeilToInt(len / (cell * 0.5f)));
            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps;
                SplatDisc(mask, tw, th, cell, Mathf.Lerp(ax, bx, t), Mathf.Lerp(az, bz, t), radius);
            }
        }

        private static float[] Blur(float[] src, int w, int h)
        {
            var dst = new float[src.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f;
                    int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= h) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            if (nx < 0 || nx >= w) continue;
                            sum += src[ny * w + nx];
                            n++;
                        }
                    }
                    dst[y * w + x] = sum / Mathf.Max(1, n);
                }
            }
            return dst;
        }

        // ---------------------------------------------------------------- декор
        /// <summary>
        /// Декор пустой земли: деревья, кусты, камни, трава и грядки. Как в SoQ,
        /// каждый тайл получает несколько «мелочей», а лес — плотный частокол.
        /// Плоские предметы (камни/трава/грядки) лежат на земле, остальные — стенди.
        /// </summary>
        private void BuildDecor()
        {
            if (_map == null || _catalog == null || _decorRoot == null) return;
            var flatMesh = BuildFlatMesh();
            foreach (var deco in _map.Decor)
            {
                var def = _catalog.Get(deco.defId);
                if (def == null) continue;
                var region = deco.region >= 0 && deco.region < _map.Regions.Count ? _map.Regions[deco.region] : null;
                var sprite = _catalog.SpriteFor(def, region);
                if (sprite == null) continue;

                // разброс: без него ряд одинаковых деревьев выглядит штампом
                float jitter = SoqRng.Hash01(deco.x, deco.y, 91);
                float jitter2 = SoqRng.Hash01(deco.x, deco.y, 137);
                float scale = def.scale * (0.82f + jitter * 0.36f);
                float px = (deco.x + 0.5f + (jitter - 0.5f) * 0.62f) * TileSize;
                float pz = (deco.y + 0.5f + (jitter2 - 0.5f) * 0.62f) * TileSize;

                // оттенок: у леса — темнее и холоднее, у луговых деревьев — светлее;
                // плюс небольшой сдвиг в тёплый/холодный, чтобы роща не была одного цвета
                float shade = 0.70f + SoqRng.Hash01(deco.x, deco.y, 211) * 0.30f;
                float warm = SoqRng.Hash01(deco.x, deco.y, 307);
                var tint = new Color(
                    Mathf.Clamp01(shade * (0.94f + warm * 0.12f)),
                    Mathf.Clamp01(shade * (1.0f + (1f - Mathf.Abs(warm - 0.5f) * 2f) * 0.04f)),
                    Mathf.Clamp01(shade * (0.88f + (1f - warm) * 0.10f)),
                    1f);
                if (_map.RegionAt(deco.x, deco.y) != null)
                {
                    var rc = RegionColor(_map.RegionAt(deco.x, deco.y));
                    tint = Color.Lerp(tint, new Color(tint.r * rc.r, tint.g * rc.g, tint.b * rc.b, 1f), 0.18f);
                }
                bool flat = def.flat;
                Mesh mesh = flat ? flatMesh : BuildStandeeMesh(sprite, scale, tint);
                if (mesh == null) continue;

                var go = new GameObject("decor_" + deco.defId);
                go.transform.parent = _decorRoot.transform;
                go.transform.position = new Vector3(px, GroundY + (flat ? 0.008f : 0.006f), pz);
                go.transform.rotation = Quaternion.Euler(0f, (jitter * 2f - 1f) * 180f, 0f);
                if (flat)
                {
                    float ppu = sprite.ppu > 0.5f ? sprite.ppu : Ppu;
                    float w = sprite.w / ppu * scale * ObjectScale * 0.8f;
                    float h = sprite.h / ppu * scale * ObjectScale * 0.8f;
                    go.transform.localScale = new Vector3(Mathf.Max(0.3f, w), 1f, Mathf.Max(0.3f, h));
                }
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null) mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = flat ? FlatDecorMaterial(sprite) : ObjectMaterial(sprite.atlas);

                // дерево без тени «парит» над землёй; дешёвый плоский блоб
                // ставим только крупному декору, мелочь (трава, камни) не нужна
                if (!flat)
                {
                    float ppuD = sprite.ppu > 0.5f ? sprite.ppu : Ppu;
                    float footprint = sprite.w / ppuD * scale * ObjectScale;
                    if (footprint >= TileSize * 0.35f)
                        AttachShadow(px, pz + TileSize * 0.10f, footprint * 0.62f);
                }
            }
        }

        /// <summary>Плоский квад на земле для камней, травы и грядок.</summary>
        private Mesh BuildFlatMesh()
        {
            if (_flatMesh != null) return _flatMesh;
            var mesh = new Mesh { name = "flat_decor" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f),
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            _flatMesh = mesh;
            return mesh;
        }

        /// <summary>Материал плоского декора: спрайт-лист, у которого UV подставлены в меш.</summary>
        private Material FlatDecorMaterial(AdventureSprite sprite)
        {
            var mat = ObjectMaterial(sprite.atlas);
            if (mat == null) return null;
            // UV тайла кладём в меш отдельным материалом нельзя — поэтому масштабируем
            // через _MainTex_ST: так один материал обслуживает все камни/траву атласа
            var inst = new Material(mat);
            _ownedMaterials.Add(inst);
            float aw = SoqMapAssets.AtlasWidth(sprite.atlas);
            float ah = SoqMapAssets.AtlasHeight(sprite.atlas);
            if (aw > 0 && ah > 0)
            {
                inst.mainTextureScale = new Vector2(sprite.w / aw, sprite.h / ah);
                inst.mainTextureOffset = new Vector2(sprite.x / aw, sprite.y / ah);
            }
            inst.SetFloat("_Cutoff", 0.35f);
            return inst;
        }

        // ---------------------------------------------------------------- объекты
        private void BuildObjects()
        {
            if (_map == null || _catalog == null) return;
            foreach (var obj in _map.Objects)
            {
                if (obj.removed) continue;
                var def = _catalog.Get(obj.defId);
                if (def == null) continue;
                var region = obj.region >= 0 && obj.region < _map.Regions.Count ? _map.Regions[obj.region] : null;
                var sprite = _catalog.SpriteFor(def, region);
                if (sprite == null) sprite = _catalog.PlaceholderFor(def);
                if (sprite == null) continue;

                var mesh = BuildStandeeMesh(sprite, def.scale);
                if (mesh == null) continue;

                // крупные объекты получают тень — так они «стоят» на земле, а не парят
                float footprint = sprite.w / (sprite.ppu > 0.5f ? sprite.ppu : Ppu) * Mathf.Max(0.1f, def.scale) * ObjectScale;
                if (footprint >= TileSize * 0.55f)
                    AttachShadow((obj.x + 0.5f) * TileSize, (obj.y + 0.62f) * TileSize, footprint * 0.8f);

                var go = new GameObject("obj_" + obj.defId);
                go.transform.parent = _objectsRoot.transform;
                go.transform.position = new Vector3((obj.x + 0.5f) * TileSize, GroundY + 0.01f, (obj.y + 0.5f) * TileSize);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ObjectMaterial(sprite.atlas);
            }
        }

        private Mesh BuildStandeeMesh(AdventureSprite sprite, float scale)
        {
            return BuildStandeeMesh(sprite, scale, Color.white);
        }

        /// <summary>
        /// Наклонный стенди-квад. tint идёт в вертексный цвет и умножается в
        /// шейдере, поэтому один материал атласа обслуживает все оттенки —
        /// именно так деревья перестают выглядеть штампованными.
        /// </summary>
        private Mesh BuildStandeeMesh(AdventureSprite sprite, float scale, Color tint)
        {
            float ppu = sprite.ppu > 0.5f ? sprite.ppu : Ppu;
            float w = sprite.w / ppu * Mathf.Max(0.1f, scale) * ObjectScale;
            float h = sprite.h / ppu * Mathf.Max(0.1f, scale) * ObjectScale;
            float rad = TiltDeg * Mathf.PI / 180f;
            float yTop = h * Mathf.Cos(rad);
            float zTop = h * Mathf.Sin(rad);

            int atlasW = SoqMapAssets.AtlasWidth(sprite.atlas);
            int atlasH = SoqMapAssets.AtlasHeight(sprite.atlas);
            if (atlasW <= 0) atlasW = 1;
            if (atlasH <= 0) atlasH = 1;
            const float pad = 0.5f;
            float u0 = (sprite.x + pad) / atlasW;
            float u1 = (sprite.x + sprite.w - pad) / atlasW;
            float v0 = (sprite.y + pad) / atlasH;
            float v1 = (sprite.y + sprite.h - pad) / atlasH;

            var verts = new[]
            {
                new Vector3(-w * 0.5f, 0f, 0f),
                new Vector3(w * 0.5f, 0f, 0f),
                new Vector3(w * 0.5f, yTop, zTop),
                new Vector3(-w * 0.5f, yTop, zTop),
            };
            var uvs = new[]
            {
                new Vector2(u0, v0), new Vector2(u1, v0),
                new Vector2(u1, v1), new Vector2(u0, v1),
            };
            var c32 = (Color32)tint;
            var colors = new[] { c32, c32, c32, c32 };
            var mesh = new Mesh
            {
                name = "standee_" + sprite.name,
                vertices = verts,
                uv = uvs,
                colors32 = colors,
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------------------------------------------------------- миникарта
        private void BuildMinimap()
        {
            if (_map == null) return;
            var tex = new Texture2D(_map.Width, _map.Height, TextureFormat.RGBA32, false);
            var pixels = new Color32[_map.Width * _map.Height];
            var sea = new Color(0.13f, 0.23f, 0.32f);

            for (int y = 0; y < _map.Height; y++)
            {
                for (int x = 0; x < _map.Width; x++)
                {
                    var tile = _map.Tile(x, y);
                    Color c;
                    if (tile == null) c = Color.black;
                    else if (tile.sea) c = sea;
                    else
                    {
                        c = ParseHex(AdvTerrainInfo.MiniMapColor[Mathf.Clamp((int)tile.terrain, 0, AdvTerrainInfo.MiniMapColor.Length - 1)]);
                        var region = _map.RegionAt(x, y);
                        if (region != null) c = Color.Lerp(c, RegionColor(region), 0.18f);
                        if (tile.road) c = Color.Lerp(c, new Color(0.65f, 0.52f, 0.32f), 0.45f);
                    }
                    int row = _map.Height - 1 - y;
                    pixels[row * _map.Width + x] = c;
                }
            }

            // важные точки поверх рельефа: города, цели, старты (как экспорт миникарты в SoQ)
            foreach (var start in _map.Starts)
            {
                int row = _map.Height - 1 - start.y;
                if (row < 0 || row >= _map.Height) continue;
                pixels[row * _map.Width + Mathf.Clamp(start.x, 0, _map.Width - 1)] = new Color(1f, 0.92f, 0.55f);
            }
            foreach (var obj in _map.Objects)
            {
                if (obj.removed) continue;
                if (!string.Equals(obj.category, "settlement", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(obj.category, "objective", StringComparison.OrdinalIgnoreCase))
                    continue;
                int row = _map.Height - 1 - obj.y;
                if (row < 0 || row >= _map.Height) continue;
                var color = string.Equals(obj.category, "settlement", StringComparison.OrdinalIgnoreCase)
                    ? new Color(0.98f, 0.98f, 0.94f)
                    : new Color(1f, 0.45f, 0.75f);
                pixels[row * _map.Width + Mathf.Clamp(obj.x, 0, _map.Width - 1)] = color;
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            _minimap = tex;
        }

        public static Color ParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex[0] != '#') return new Color(1f, 0f, 1f);
            int r = HexByte(hex, 1), g = HexByte(hex, 3), b = HexByte(hex, 5);
            return new Color(r / 255f, g / 255f, b / 255f);
        }

        private static int HexByte(string s, int index)
        {
            if (s.Length < index + 2) return 128;
            int hi = HexDigit(s[index]), lo = HexDigit(s[index + 1]);
            return ((hi << 4) | lo) & 0xFF;
        }

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return 0;
        }

        // ---------------------------------------------------------------- камера
        private void SetupCamera()
        {
            float size = _map.Width * TileSize;
            _cam = Camera.main;
            if (_cam == null)
            {
                var camGo = new GameObject("SoQ Map Camera");
                _cam = camGo.AddComponent<Camera>();
            }
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.05f, 0.08f, 0.10f, 1f);
            _cam.nearClipPlane = 0.3f;
            _cam.farClipPlane = 900f;
            _cam.fieldOfView = WorldMapCameraRig.Fov;

            _rig = _cam.gameObject.GetComponent<WorldMapCameraRig>();
            if (_rig == null) _rig = _cam.gameObject.AddComponent<WorldMapCameraRig>();
            _rig.Init(_cam, new Vector2(size, size));

            // Открываем сцену так, как выглядит карта в самой SoQ: камера стоит
            // над стартовым городом и видит ~8–10 тайлов, а не всю карту целиком.
            // Вся карта — по клавише F (WorldMapCameraRig.Fit).
            float cx = size * 0.5f, cz = size * 0.5f;
            if (_map.Starts != null && _map.Starts.Count > 0)
            {
                var start = _map.Starts[0];
                cx = (start.x + 0.5f) * TileSize;
                cz = (start.y + 0.5f) * TileSize;
            }
            _rig.FocusOn(new Vector2(cx, cz), Mathf.Clamp(size * 0.17f, 26f, 44f));
        }

        // ---------------------------------------------------------------- утилиты
        private void AttachMesh(string name, Mesh mesh, Material mat, Transform parent)
        {
            if (mesh == null) return;
            var go = new GameObject(name);
            go.transform.parent = parent;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
        }

        /// <summary>Клик по экрану -> тайл карты (x,y) или false.</summary>
        public bool PickTile(Vector2 guiPos, out int x, out int y)
        {
            x = y = -1;
            if (_rig == null || _map == null) return false;
            if (!_rig.GroundPoint(guiPos, out var world, GroundY + 0.05f)) return false;
            x = Mathf.FloorToInt(world.x / TileSize);
            y = Mathf.FloorToInt(world.z / TileSize);
            return _map.InBounds(x, y);
        }

        public Vector3 TileWorld(int x, int y)
        {
            return new Vector3((x + 0.5f) * TileSize, GroundY, (y + 0.5f) * TileSize);
        }

        public Vector2 WorldToGui(Vector3 world)
        {
            if (_cam == null) return new Vector2(-1000, -1000);
            var sp = _cam.WorldToScreenPoint(world);
            return new Vector2(sp.x, Screen.height - sp.y);
        }

        /// <summary>Цвет биома/тайла для подсказок.</summary>
        public static string TerrainName(AdventureTile tile)
        {
            if (tile == null) return "—";
            if (tile.sea) return "Море";
            int cls = Mathf.Clamp((int)tile.terrain, 0, AdvTerrainInfo.Names.Length - 1);
            return AdvTerrainInfo.Names[cls];
        }
    }
}
