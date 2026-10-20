// IronThrone: 2.5D-вид сгенерированной карты приключений в духе Songs of Conquest.
// Квадратная сетка (как в SoQ), наклонные стенди-спрайты объектов, плоская земля
// из 16 тайлов Resources/Art/Terrain, дороги, границы регионов, вода и миникарта.
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
        /// Тайлы рельефа в Resources/Art/Terrain нарисованы тёмными: в WorldMapView их
        /// подсвечивает ramp-шейдер. Здесь освещения нет, поэтому компенсируем цветом.
        /// </summary>
        public const float GroundBrightness = 1.5f;
        /// <summary>Сколько тайлов занимает один период текстуры (общий UV-континуум).</summary>
        public const float GroundUvScale = 0.5f;

        private static readonly string[] TerrainFiles = AdvTerrainInfo.TileNames;

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
            BuildRoadsAndBorders();
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
                    a *= a * 0.55f;
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
        private void BuildGround()
        {
            if (_map == null) return;
            var byClass = new List<AdventureTile>[TerrainFiles.Length];
            for (int i = 0; i < byClass.Length; i++) byClass[i] = new List<AdventureTile>();
            foreach (var tile in _map.AllTiles())
            {
                if (tile.sea) continue;
                int cls = Mathf.Clamp((int)tile.terrain, 0, TerrainFiles.Length - 1);
                byClass[cls].Add(tile);
            }

            var tex = new Texture2D[TerrainFiles.Length];
            for (int cls = 0; cls < TerrainFiles.Length; cls++)
                tex[cls] = Resources.Load<Texture2D>("Art/Terrain/" + TerrainFiles[cls]);

            for (int cls = 0; cls < byClass.Length; cls++)
            {
                var tiles = byClass[cls];
                if (tiles.Count == 0) continue;
                var classMat = CreateGroundMaterial(tex[cls], Color.white);
                // меш разбивается на куски: у Mesh индекс 16-битный (максимум 65535 вершин)
                const int chunk = 4000;
                for (int start = 0; start < tiles.Count; start += chunk)
                {
                    int count = Mathf.Min(chunk, tiles.Count - start);
                    var verts = new List<Vector3>(count * 4);
                    var uvs = new List<Vector2>(count * 4);
                    var colors = new List<Color32>(count * 4);
                    var tris = new List<int>(count * 6);
                    float half = TileSize * 0.5f;

                    for (int i = start; i < start + count; i++)
                    {
                        var tile = tiles[i];
                        float cx = (tile.x + 0.5f) * TileSize;
                        float cz = (tile.y + 0.5f) * TileSize;
                        var tint = GroundTint(tile);
                        int v = verts.Count;
                        verts.Add(new Vector3(cx - half, GroundY, cz - half));
                        verts.Add(new Vector3(cx + half, GroundY, cz - half));
                        verts.Add(new Vector3(cx + half, GroundY, cz + half));
                        verts.Add(new Vector3(cx - half, GroundY, cz + half));
                        // UV в мировых координатах: соседние тайлы одного класса продолжают
                        // друг друга, и карта не выглядит лоскутным одеялом 1×1.
                        float u0 = tile.x * GroundUvScale, v0 = tile.y * GroundUvScale;
                        float u1 = u0 + GroundUvScale, v1 = v0 + GroundUvScale;
                        uvs.Add(new Vector2(u0, v0));
                        uvs.Add(new Vector2(u1, v0));
                        uvs.Add(new Vector2(u1, v1));
                        uvs.Add(new Vector2(u0, v1));
                        colors.Add(tint);
                        colors.Add(tint);
                        colors.Add(tint);
                        colors.Add(tint);
                        tris.Add(v + 0); tris.Add(v + 2); tris.Add(v + 1);
                        tris.Add(v + 0); tris.Add(v + 3); tris.Add(v + 2);
                    }

                    var mesh = new Mesh { name = "ground_" + TerrainFiles[cls] };
                    mesh.vertices = verts.ToArray();
                    mesh.uv = uvs.ToArray();
                    mesh.colors32 = colors.ToArray();
                    mesh.triangles = tris.ToArray();
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();

                    _groundMat = classMat;
                    AttachMesh("ground_" + TerrainFiles[cls] + "_" + start, mesh, classMat, _groundRoot.transform);
                }
            }
        }

        private Color32 GroundTint(AdventureTile tile)
        {
            int cls = Mathf.Clamp((int)tile.terrain, 0, AdvTerrainInfo.Brightness.Length - 1);
            float bright = AdvTerrainInfo.Brightness[cls] * GroundBrightness / 1.5f;
            var region = _map.RegionAt(tile.x, tile.y);
            var baseColor = Color.white;
            if (region != null)
            {
                var rc = RegionColor(region);
                baseColor = Color.Lerp(Color.white, rc, 0.10f);
            }
            // лёгкая вариация, чтобы одинаковые тайлы не сливались
            float n = SoqRng.Hash01(tile.x, tile.y, 17) * 0.10f - 0.05f;
            return new Color(
                Mathf.Clamp01((baseColor.r + n) * bright),
                Mathf.Clamp01((baseColor.g + n) * bright),
                Mathf.Clamp01((baseColor.b + n) * bright),
                1f);
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
            float size = _map.Width * TileSize;
            var shader = Resources.Load<Shader>("Shaders/IronThroneSoqMapWater");
            if (shader == null) shader = Shader.Find("IronThrone/SoqMapWater");
            if (shader == null) return;

            _waterMat = new Material(shader);
            _ownedMaterials.Add(_waterMat);
            var shallow = Resources.Load<Texture2D>("Art/ArleonAdventureWaterShallowSurfaceTexture");
            var deep = Resources.Load<Texture2D>("Art/ArleonAdventureWaterDeepSurfaceTexture");
            if (shallow != null) _waterMat.SetTexture("_ShallowTex", shallow);
            if (deep != null) _waterMat.SetTexture("_DeepTex", deep);
            _waterMat.SetColor("_Color", new Color(0.20f, 0.38f, 0.48f, 0.94f));
            _waterMat.SetFloat("_Tiling", 0.10f);
            _waterMat.SetFloat("_Speed", 0.015f);

            var verts = new[]
            {
                new Vector3(0, 0, 0), new Vector3(size, 0, 0),
                new Vector3(size, 0, size), new Vector3(0, 0, size),
            };
            var uvs = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            var mesh = new Mesh { name = "water", vertices = verts, uv = uvs, triangles = new[] { 0, 2, 1, 0, 3, 2 } };
            mesh.RecalculateBounds();
            AttachMesh("water", mesh, _waterMat, _root.transform);
        }

        // ---------------------------------------------------------------- дороги и границы
        private void BuildRoadsAndBorders()
        {
            if (_map == null) return;
            var roadVerts = new List<Vector3>();
            var roadColors = new List<Color32>();
            var roadTris = new List<int>();
            var roadColor = new Color(0.64f, 0.49f, 0.30f);
            var borderVerts = new List<Vector3>();
            var borderColors = new List<Color32>();
            var borderTris = new List<int>();

            float half = TileSize * 0.5f;
            float roadHalf = TileSize * 0.16f;
            float borderWidth = TileSize * 0.035f;

            foreach (var tile in _map.AllTiles())
            {
                if (tile.sea) continue;
                float cx = (tile.x + 0.5f) * TileSize;
                float cz = (tile.y + 0.5f) * TileSize;
                var region = _map.RegionAt(tile.x, tile.y);
                var regionColor = RegionColor(region);

                if (tile.road)
                {
                    float ry = GroundY + 0.004f;
                    // центр перекрёстка
                    AddQuad(roadVerts, roadColors, roadTris,
                        new Vector3(cx - roadHalf, ry, cz - roadHalf),
                        new Vector3(cx + roadHalf, ry, cz + roadHalf),
                        roadColor, 0.85f);
                    // «рукава» к соседям по дороге
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = tile.x + AdventureMap.Dx[d], ny = tile.y + AdventureMap.Dy[d];
                        var nt = _map.Tile(nx, ny);
                        if (nt == null || nt.sea || !nt.road) continue;
                        float dx = AdventureMap.Dx[d], dy = AdventureMap.Dy[d];
                        float ex = cx + dx * half;
                        float ez = cz + dy * half;
                        // перпендикуляр к направлению дороги в плоскости x/z
                        float px = dy * roadHalf;
                        float pz = dx * roadHalf;
                        AddQuadRaw(roadVerts, roadColors, roadTris,
                            new Vector3(cx - px, ry, cz - pz),
                            new Vector3(ex + px, ry, ez + pz),
                            new Color(roadColor.r, roadColor.g, roadColor.b, 0.8f));
                    }
                }

                // границы: только там, где сосед — другой регион или море
                AddBorderEdge(borderVerts, borderColors, borderTris, tile, 1, 0, borderWidth, regionColor);
                AddBorderEdge(borderVerts, borderColors, borderTris, tile, -1, 0, borderWidth, regionColor);
                AddBorderEdge(borderVerts, borderColors, borderTris, tile, 0, 1, borderWidth, regionColor);
                AddBorderEdge(borderVerts, borderColors, borderTris, tile, 0, -1, borderWidth, regionColor);
            }

            if (roadTris.Count > 0)
            {
                var mesh = new Mesh { name = "roads" };
                mesh.vertices = roadVerts.ToArray();
                mesh.colors32 = roadColors.ToArray();
                mesh.triangles = roadTris.ToArray();
                var mat = CreateGroundMaterial(_white, Color.white);
                AttachMesh("roads", mesh, mat, _root.transform);
            }

            if (borderTris.Count > 0)
            {
                var mesh = new Mesh { name = "borders" };
                mesh.vertices = borderVerts.ToArray();
                mesh.colors32 = borderColors.ToArray();
                mesh.triangles = borderTris.ToArray();
                var mat = CreateGroundMaterial(_white, Color.white);
                AttachMesh("borders", mesh, mat, _root.transform);
            }
        }

        private void AddBorderEdge(List<Vector3> verts, List<Color32> colors, List<int> tris,
            AdventureTile tile, int dx, int dy, float width, Color color)
        {
            int nx = tile.x + dx, ny = tile.y + dy;
            var neighbor = _map.Tile(nx, ny);
            var selfRegion = tile.region;
            if (neighbor != null && !neighbor.sea && neighbor.region == selfRegion) return;

            float cx = (tile.x + 0.5f) * TileSize;
            float cz = (tile.y + 0.5f) * TileSize;
            float half = TileSize * 0.5f;
            float y = GroundY + 0.012f;
            Color32 c = new Color(color.r, color.g, color.b, 0.75f);

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

        private static void AddQuad(List<Vector3> verts, List<Color32> colors, List<int> tris,
            Vector3 a, Vector3 b, Color color, float alpha)
        {
            AddQuadRaw(verts, colors, tris, a, b, new Color(color.r, color.g, color.b, alpha));
        }

        // ---------------------------------------------------------------- декор
        /// <summary>Деревья на пустой земле: рисуются до объектов и чуть темнее.</summary>
        private void BuildDecor()
        {
            if (_map == null || _catalog == null || _decorRoot == null) return;
            foreach (var deco in _map.Decor)
            {
                var def = _catalog.Get(deco.defId);
                if (def == null) continue;
                var region = deco.region >= 0 && deco.region < _map.Regions.Count ? _map.Regions[deco.region] : null;
                var sprite = _catalog.SpriteFor(def, region);
                if (sprite == null) continue;

                // небольшой разброс по высоте и повороту: ряд одинаковых деревьев выглядит штампом
                float jitter = SoqRng.Hash01(deco.x, deco.y, 91);
                var mesh = BuildStandeeMesh(sprite, def.scale * (0.9f + jitter * 0.2f));
                if (mesh == null) continue;

                var go = new GameObject("decor_" + deco.defId);
                go.transform.parent = _decorRoot.transform;
                go.transform.position = new Vector3(
                    (deco.x + 0.5f + (jitter - 0.5f) * 0.25f) * TileSize,
                    GroundY + 0.005f,
                    (deco.y + 0.5f) * TileSize);
                go.transform.rotation = Quaternion.Euler(0f, (jitter * 2f - 1f) * 8f, 0f);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ObjectMaterial(sprite.atlas);
            }
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
            var colors = new[]
            {
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
            };
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
            // При открытии сцены карта должна быть видна целиком, а не «носом в тайл».
            _rig.Fit(new Vector2(size, size));
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
