// IronThrone: 3D-вид глобальной карты (стиль Songs of Conquest 2.5D), версия 2.
// География — по «Карта Вестероса.webp» (canonical_map.json из tools/regen_canonical_map.py).
// Террейн: 16 биом-классов из тайлов Resources/Art/Terrain (рантайм Texture2DArray,
// без битого импорта 22016-пиксельной ленты) + региональный tint + light-ramp.
// Декор и постройки — КАЖДЫЙ объект отдельным GameObject (лес вырубается, замок рушится).
using System;
using System.Collections.Generic;
using IronThrone.Core.Content;
using UnityEngine;

namespace IronThrone.Unity
{
    /// <summary>Запись базы спрайтов атласа (плоский JSON из tools/slice_atlas.py).</summary>
    [Serializable]
    public class AtlasSpriteEntry
    {
        public string atlas;
        public string name;
        public string cat;
        public int x, y, w, h;
    }

    [Serializable]
    public class AtlasSpriteDb
    {
        public AtlasSpriteEntry[] sprites;
    }

    public class WorldMapView : MonoBehaviour
    {
        /// <summary>
        /// Биом-классы 0..15 — индексы слоёв Texture2DArray (имена файлов в Resources/Art/Terrain).
        /// 0 grass, 1 tundra, 2 dirt, 3 dry, 4 rock_green, 5 sand, 6 sand_rock, 7 fields,
        /// 8 autumn, 9 rock, 10 snow_rock, 11 snow, 12 cold_grass, 13 forest_mix, 14 marsh, 15 forest_pine.
        /// </summary>
        public static readonly string[] TerrainTileNames =
        {
            "000_grass", "010_tundra", "024_dirt", "028_dry", "034_rock_green", "040_sand",
            "042_sand_rock", "044_fields", "050_autumn", "062_rock", "094_snow_rock", "096_snow",
            "100_cold_grass", "102_forest_mix", "104_marsh", "106_forest_pine",
        };

        /// <summary>Высота гекса по биом-классу (в долях базовой высоты).</summary>
        public static readonly float[] ClassHeight =
        {
            1.00f, 1.10f, 1.05f, 1.00f, 1.35f, 0.95f, 1.40f, 1.00f,
            1.05f, 1.60f, 1.90f, 1.30f, 1.15f, 1.20f, 0.75f, 1.25f,
        };

        const float Ppu = 46f;         // pixels-to-units декора/построек
        const float TiltDeg = 52f;     // наклон стендee (как в Songs of Conquest)
        const float SeaLevel = 0.17f;

        MapData _map;
        public MapData Map => _map;

        GameObject _root;
        Transform _decorRoot, _buildingRoot;
        Material _terrainMat, _waterMat, _decorMat, _buildingMat, _decalMat;
        readonly Dictionary<string, AtlasSpriteEntry> _buildingSprites = new Dictionary<string, AtlasSpriteEntry>();
        readonly Dictionary<string, List<AtlasSpriteEntry>> _catIndex = new Dictionary<string, List<AtlasSpriteEntry>>();
        List<AtlasSpriteEntry> _bushDry, _smallDry, _rockBare;

        public WorldMapCameraRig Rig;
        public Camera MapCamera;

        // ---------------------------------------------------------------- построение

        /// <summary>Строит всю карту. Вызывается из IronThroneGame; безопасно без арта (smoke).</summary>
        public static WorldMapView Build(MapData map)
        {
            var go = new GameObject("WorldMapView");
            var view = go.AddComponent<WorldMapView>();
            view.BuildInternal(map);
            return view;
        }

        void BuildInternal(MapData map)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            int W = map.width, H = map.height;

            _root = new GameObject("WorldMapRoot");
            _root.transform.parent = transform;

            LoadSpriteDb();
            var shallow = Resources.Load<Texture2D>("Art/ArleonAdventureWaterShallowSurfaceTexture");
            var deep = Resources.Load<Texture2D>("Art/ArleonAdventureWaterDeepSurfaceTexture");
            var decoAtlas = Resources.Load<Texture2D>("Art/StandaloneAdventureDecorationAtlas");
            var entAtlas = Resources.Load<Texture2D>("Art/PermanentMapEntityAtlas");
            var lightRamp = Resources.Load<Texture2D>("Art/AdventureCartographyLightRampTexture");

            _terrainMat = new Material(Shader.Find("IronThrone/CartographyTerrain"));
            _waterMat = new Material(Shader.Find("IronThrone/CartographyWater"));
            _decorMat = new Material(Shader.Find("IronThrone/CartographyStandee"));
            _buildingMat = new Material(Shader.Find("IronThrone/CartographyStandee"));
            _decalMat = new Material(Shader.Find("IronThrone/CartographyStandee"));

            if (decoAtlas != null) _decorMat.mainTexture = decoAtlas;
            if (entAtlas != null) _buildingMat.mainTexture = entAtlas;
            if (decoAtlas != null) _decalMat.mainTexture = decoAtlas;
            if (lightRamp != null)
            {
                _decorMat.SetTexture("_LightRamp", lightRamp);
                _buildingMat.SetTexture("_LightRamp", lightRamp);
                _decalMat.SetTexture("_LightRamp", lightRamp);
            }
            if (shallow != null) _waterMat.SetTexture("_ShallowTex", shallow);
            if (deep != null) _waterMat.SetTexture("_DeepTex", deep);
            _waterMat.SetFloat("_Tiling", 0.18f * WorldMapCoords.HexSize / 3.8f);
            _waterMat.SetFloat("_Speed", 0.35f);

            // рантайм-массив 16 тайлов террейна (вместо битого импорта ленты 128x22016)
            var terrainArray = BuildTerrainArray();
            if (terrainArray != null) _terrainMat.SetTexture("_TerrainArray", terrainArray);
            _terrainMat.SetFloat("_TileWorld", WorldMapCoords.HexWidth * 1.08f);
            if (lightRamp != null) _terrainMat.SetTexture("_LightRamp", lightRamp);

            var size = WorldMapCoords.MapSizeWorld(W, H);
            float ox = -WorldMapCoords.HexWidth * 0.5f;
            float oz = 0f;
            var origin = new Vector4(ox, oz, size.x + WorldMapCoords.HexWidth * 0.5f, size.y);
            _terrainMat.SetVector("_MapOrigin", origin);
            _waterMat.SetVector("_MapOrigin", origin);

            // классы/высоты гексов
            float[] heights = new float[W * H];
            int[] classes = new int[W * H];
            bool[] land = new bool[W * H];
            string[] regions = new string[W * H];
            for (int i = 0; i < W * H; i++) { classes[i] = -1; }
            foreach (var t in map.tiles)
            {
                if (t.c < 0 || t.r < 0 || t.c >= W || t.r >= H) continue;
                int idx = t.r * W + t.c;
                if (t.sea) { land[idx] = false; classes[idx] = -1; heights[idx] = 0f; }
                else
                {
                    int cls = BiomeClass(t.region, t.terrain, t.c, t.r);
                    land[idx] = true;
                    classes[idx] = cls;
                    regions[idx] = t.region;
                    heights[idx] = ClassHeight[cls] * WorldMapCoords.HexSize * 0.14f;
                }
            }

            BuildGround(W, H, ox, oz, origin, heights);
            BuildControl(W, H, ox, oz, origin, classes, land, regions);
            BuildWater(W, H, ox, oz, origin);
            BuildDecor(map, land, classes, regions, heights);
            BuildBuildings(map, heights);

            SetupCamera(W, H);
            SetupAtmosphere();
        }

        /// <summary>Собирает Texture2DArray из 16 тайлов 128x128 (порядок = TerrainTileNames).</summary>
        Texture2DArray BuildTerrainArray()
        {
            try
            {
                Texture2D first = Resources.Load<Texture2D>("Art/Terrain/" + TerrainTileNames[0]);
                if (first == null) return null;
                int tw = first.width, th = first.height;
                var arr = new Texture2DArray(tw, th, TerrainTileNames.Length, TextureFormat.RGBA32, false);
                bool any = false;
                for (int i = 0; i < TerrainTileNames.Length; i++)
                {
                    try
                    {
                        var tex = Resources.Load<Texture2D>("Art/Terrain/" + TerrainTileNames[i]);
                        if (tex == null) continue;
                        var px = tex.GetPixels32(); // требует isReadable: 1 в .meta
                        if (px == null || px.Length != tw * th) continue;
                        arr.SetPixels32(px, 0, i);
                        any = true;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("Terrain tile " + TerrainTileNames[i] + ": " + ex.Message);
                    }
                }
                if (!any) return null;
                arr.Apply();
                return arr;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("TerrainTextureArray: " + ex.Message);
                return null;
            }
        }

        void LoadSpriteDb()
        {
            var asset = Resources.Load<TextAsset>("Art/atlas_slices");
            if (asset == null) return;
            AtlasSpriteDb db = null;
            try { db = JsonUtility.FromJson<AtlasSpriteDb>(asset.text); }
            catch (Exception ex) { Debug.LogWarning("atlas_slices: " + ex.Message); return; }
            if (db == null || db.sprites == null) return;
            foreach (var s in db.sprites)
            {
                if (s.atlas == "StandaloneAdventureDecorationAtlas")
                {
                    if (!_catIndex.TryGetValue(s.cat ?? "misc", out var list))
                    {
                        list = new List<AtlasSpriteEntry>();
                        _catIndex[s.cat ?? "misc"] = list;
                    }
                    list.Add(s);
                }
                else if (s.atlas == "PermanentMapEntityAtlas")
                {
                    _buildingSprites[s.name] = s;
                }
            }

            // сухие/чистые подпулы: Дорн — без зелёных кустов, Железные — без мшистых камней
            _bushDry = FilterNames(Cat("bushes"), "deco_11", "deco_12", "deco_16", "deco_20");
            _smallDry = ExcludeNames(Cat("small"), "deco_56", "deco_57");
            _rockBare = ExcludeNames(Cat("rocks"), "deco_29", "deco_50");
        }

        static List<AtlasSpriteEntry> FilterNames(List<AtlasSpriteEntry> src, params string[] names)
        {
            var r = new List<AtlasSpriteEntry>();
            if (src == null) return r;
            foreach (var s in src)
                foreach (var n in names)
                    if (s.name == n) { r.Add(s); break; }
            return r;
        }

        static List<AtlasSpriteEntry> ExcludeNames(List<AtlasSpriteEntry> src, params string[] names)
        {
            var r = new List<AtlasSpriteEntry>();
            if (src == null) return r;
            foreach (var s in src)
            {
                bool skip = false;
                foreach (var n in names) if (s.name == n) { skip = true; break; }
                if (!skip) r.Add(s);
            }
            return r;
        }

        List<AtlasSpriteEntry> Cat(string name)
        {
            return _catIndex.TryGetValue(name, out var l) ? l : null;
        }

        // ---------------------------------------------------------------- биомы и регионы

        /// <summary>(регион, террейн) -> биом-класс 0..15 с шумовым разнообразием.</summary>
        public static int BiomeClass(string region, string terrain, int c, int r)
        {
            float n = Hash01(c, r, 91);
            string t = terrain ?? "";
            bool forest = t.StartsWith("forest") || t.StartsWith("wood");
            bool hills = t.StartsWith("hill");
            bool mount = t.StartsWith("mount") || t.StartsWith("peak");
            bool snow = t.StartsWith("snow") || t.StartsWith("frost");
            bool marsh = t.StartsWith("marsh") || t.StartsWith("swamp") || t.StartsWith("bog");
            bool desert = t.StartsWith("desert") || t.StartsWith("sand") || t.StartsWith("dune");
            switch (region)
            {
                case "dorne":                              // пески и песчаники, БЕЗ зелёных деревьев
                    if (hills || mount) return n < 0.75f ? 6 : 9;
                    return n < 0.70f ? 5 : (n < 0.90f ? 3 : 2);
                case "islands":                            // Железные острова — камень
                    if (hills || mount) return n < 0.60f ? 9 : 4;
                    return n < 0.55f ? 4 : (n < 0.80f ? 9 : 2);
                case "wall":                               // Стена — снег
                    return n < 0.60f ? 11 : (n < 0.85f ? 10 : 1);
                case "north":
                    if (snow) return n < 0.70f ? 11 : 10;
                    if (mount) return 10;
                    if (hills) return n < 0.50f ? 1 : (n < 0.80f ? 12 : 9);
                    if (forest) return n < 0.75f ? 15 : 12;
                    return n < 0.70f ? 12 : (n < 0.90f ? 1 : 8);
                case "riverlands":
                    if (marsh) return 14;
                    if (forest) return n < 0.70f ? 13 : 8;
                    if (hills || mount) return n < 0.60f ? 4 : 9;
                    return n < 0.75f ? 0 : (n < 0.90f ? 13 : 7);
                case "vale":
                    if (mount) return n < 0.60f ? 10 : 9;
                    if (hills) return n < 0.60f ? 4 : 9;
                    if (forest) return 13;
                    return n < 0.80f ? 0 : 4;
                case "westerlands":
                    if (mount) return 9;
                    if (hills) return n < 0.60f ? 9 : 4;
                    if (forest) return 13;
                    return n < 0.70f ? 0 : (n < 0.85f ? 7 : 2);
                case "reach":
                    if (hills || mount) return n < 0.60f ? 4 : 9;
                    if (forest) return n < 0.60f ? 13 : 0;
                    return n < 0.55f ? 0 : (n < 0.85f ? 7 : 3);
                case "stormlands":
                    if (hills || mount) return n < 0.60f ? 4 : 9;
                    if (forest) return 13;
                    return n < 0.80f ? 0 : 7;
                case "crownlands":
                    if (hills || mount) return 4;
                    if (forest) return n < 0.80f ? 13 : 0;
                    return n < 0.75f ? 0 : 7;
                default:
                    return 0;
            }
        }

        /// <summary>Региональный оттенок земли (видимое различие регионов).</summary>
        public static Color RegionTint(string region)
        {
            switch (region)
            {
                case "dorne": return new Color(1.12f, 1.02f, 0.80f);
                case "islands": return new Color(0.82f, 0.86f, 0.92f);
                case "wall": return new Color(0.92f, 0.96f, 1.08f);
                case "north": return new Color(0.86f, 0.95f, 0.92f);
                case "riverlands": return new Color(0.94f, 1.02f, 0.88f);
                case "vale": return new Color(0.92f, 0.98f, 1.06f);
                case "westerlands": return new Color(1.08f, 1.00f, 0.80f);
                case "reach": return new Color(1.06f, 1.06f, 0.84f);
                case "stormlands": return new Color(0.90f, 1.00f, 0.95f);
                case "crownlands": return new Color(1.00f, 1.00f, 0.92f);
                default: return Color.white;
            }
        }

        // ---------------------------------------------------------------- рельеф/контроль

        /// <summary>Меш рельефа: сетка с плавными высотами (staggered-билиней по высотам гексов).</summary>
        void BuildGround(int W, int H, float ox, float oz, Vector4 origin, float[] heights)
        {
            const int cellsPerHex = 2;
            int cx = W * cellsPerHex;
            int cz = H * cellsPerHex;
            float sizeX = origin.z;
            float sizeZ = origin.w;

            var verts = new Vector3[(cx + 1) * (cz + 1)];
            var uvs = new Vector2[verts.Length];
            var tris = new int[cx * cz * 6];
            int vi = 0;
            for (int j = 0; j <= cz; j++)
            {
                for (int i = 0; i <= cx; i++)
                {
                    float x = ox + sizeX * i / cx;
                    float z = oz + sizeZ * j / cz;
                    float y = SampleHeight(W, H, x, z, heights);
                    verts[vi] = new Vector3(x, y, z);
                    uvs[vi] = new Vector2((x - origin.x) / origin.z, (z - origin.y) / origin.w);
                    vi++;
                }
            }
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

            var mesh = new Mesh { name = "WorldMapGround", vertices = verts, uv = uvs, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AttachMesh("Ground", mesh, _terrainMat, 0);
        }

        float SampleHeight(int W, int H, float x, float z, float[] heights)
        {
            WorldMapCoords.HexSpaceBilinear(x, z,
                out int c00, out int r0, out int c10,
                out int c01, out int r1, out int c11,
                out float w00, out float w10, out float w01, out float w11);
            float h = 0;
            h += HeightAt(W, H, c00, r0, heights) * w00;
            h += HeightAt(W, H, c10, r0, heights) * w10;
            h += HeightAt(W, H, c01, r1, heights) * w01;
            h += HeightAt(W, H, c11, r1, heights) * w11;
            return h;
        }

        static float HeightAt(int W, int H, int c, int r, float[] heights)
        {
            if (c < 0 || r < 0 || c >= W || r >= H) return 0f;
            return heights[r * W + c];
        }

        /// <summary>
        /// Control-текстуры сплейтинга: 4xRGBA — веса 16 биом-классов,
        /// tint-текстура (RGB = региональный оттенок, A = шум) и карта берега (для пены).
        /// </summary>
        void BuildControl(int W, int H, float ox, float oz, Vector4 origin, int[] classes,
            bool[] land, string[] regions)
        {
            int tw = W * 4, th = H * 4;
            var c0 = new Color32[tw * th];
            var c1 = new Color32[tw * th];
            var c2 = new Color32[tw * th];
            var c3 = new Color32[tw * th];
            var tint = new Color32[tw * th];
            var coast = new Color32[tw * th];
            float cellW = origin.z / tw;
            float cellH = origin.w / th;

            float[] coastDist = ComputeCoastDist(W, H, land);

            for (int j = 0; j < th; j++)
            {
                for (int i = 0; i < tw; i++)
                {
                    float x = ox + cellW * (i + 0.5f);
                    float z = oz + cellH * (j + 0.5f);
                    WorldMapCoords.HexSpaceBilinear(x, z,
                        out int c00, out int r0, out int c10,
                        out int c01, out int r1, out int c11,
                        out float w00, out float w10, out float w01, out float w11);

                    float[] wts = new float[16];
                    float tr = 0, tg = 0, tb = 0, tws = 0;
                    float cv = 0;

                    Accum(W, H, c00, r0, w00, classes, regions, wts, ref tr, ref tg, ref tb, ref tws);
                    Accum(W, H, c10, r0, w10, classes, regions, wts, ref tr, ref tg, ref tb, ref tws);
                    Accum(W, H, c01, r1, w01, classes, regions, wts, ref tr, ref tg, ref tb, ref tws);
                    Accum(W, H, c11, r1, w11, classes, regions, wts, ref tr, ref tg, ref tb, ref tws);

                    cv += CoastAt(W, H, c00, r0, coastDist) * w00;
                    cv += CoastAt(W, H, c10, r0, coastDist) * w10;
                    cv += CoastAt(W, H, c01, r1, coastDist) * w01;
                    cv += CoastAt(W, H, c11, r1, coastDist) * w11;

                    if (tws > 0.0001f) { tr /= tws; tg /= tws; tb /= tws; }

                    int p = j * tw + i;
                    c0[p] = new Color32(b255(wts[0]), b255(wts[1]), b255(wts[2]), b255(wts[3]));
                    c1[p] = new Color32(b255(wts[4]), b255(wts[5]), b255(wts[6]), b255(wts[7]));
                    c2[p] = new Color32(b255(wts[8]), b255(wts[9]), b255(wts[10]), b255(wts[11]));
                    c3[p] = new Color32(b255(wts[12]), b255(wts[13]), b255(wts[14]), b255(wts[15]));
                    byte noise = (byte)(Hash(i, j, 55) & 0xFF);
                    tint[p] = new Color32(b255(tr), b255(tg), b255(tb), noise);
                    coast[p] = new Color32(b255(cv), 0, 0, 255);
                }
            }

            _terrainMat.SetTexture("_ControlA", MakeTex(tw, th, c0, "Control0"));
            _terrainMat.SetTexture("_ControlB", MakeTex(tw, th, c1, "Control1"));
            _terrainMat.SetTexture("_ControlC", MakeTex(tw, th, c2, "Control2"));
            _terrainMat.SetTexture("_ControlD", MakeTex(tw, th, c3, "Control3"));
            _terrainMat.SetTexture("_TintNoise", MakeTex(tw, th, tint, "TintNoise"));
            _waterMat.SetTexture("_CoastMap", MakeTex(tw, th, coast, "CoastMap"));
        }

        static void Accum(int W, int H, int c, int r, float w,
            int[] classes, string[] regions, float[] wts,
            ref float tr, ref float tg, ref float tb, ref float tws)
        {
            if (c < 0 || r < 0 || c >= W || r >= H) return;
            int idx = r * W + c;
            int cls = classes[idx];
            if (cls >= 0 && cls < 16) wts[cls] += w;
            var reg = regions[idx];
            if (!string.IsNullOrEmpty(reg))
            {
                var tint = RegionTint(reg);
                tr += tint.r * w; tg += tint.g * w; tb += tint.b * w; tws += w;
            }
        }

        static float CoastAt(int W, int H, int c, int r, float[] coastDist)
        {
            if (c < 0 || r < 0 || c >= W || r >= H) return 0f;
            return coastDist[r * W + c];
        }

        static float[] ComputeCoastDist(int W, int H, bool[] land)
        {
            int n = W * H;
            var dist = new float[n];
            for (int i = 0; i < n; i++)
            {
                dist[i] = land[i] ? 1f : -1f;
            }
            int[] dc = { 1, 0, -1, -1, -1, 0 };
            int[] dr = { 0, 1, 1, 0, -1, -1 };
            int[] dcOdd = { 1, 1, 0, -1, 0, 1 };
            int[] drOdd = { 0, 1, 1, 0, -1, -1 };
            var qd = new Queue<int>();
            var visited = new bool[n];
            for (int i = 0; i < n; i++) if (land[i]) { visited[i] = true; qd.Enqueue(i); }
            int level = 0;
            int qCount = qd.Count;
            while (qd.Count > 0 && level < 4)
            {
                int cur = qd.Dequeue();
                int c = cur % W, r = cur / W;
                int parity = r & 1;
                for (int k = 0; k < 6; k++)
                {
                    int nc = c + (parity == 0 ? dc[k] : dcOdd[k]);
                    int nr = r + (parity == 0 ? dr[k] : drOdd[k]);
                    if (nc < 0 || nr < 0 || nc >= W || nr >= H) continue;
                    int ni = nr * W + nc;
                    if (visited[ni]) continue;
                    visited[ni] = true;
                    dist[ni] = 0.82f - level * 0.28f;
                    qd.Enqueue(ni);
                }
                if (--qCount <= 0) { level++; qCount = qd.Count; }
            }
            for (int i = 0; i < n; i++) if (dist[i] < 0f) dist[i] = 0f;
            return dist;
        }

        /// <summary>Вес 0..1 -> байт канала control-текстуры.</summary>
        static byte b255(float v)
        {
            return (byte)(Mathf.Clamp01(v) * 255f);
        }

        static Texture2D MakeTex(int w, int h, Color32[] px, string name)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        /// <summary>Вода: один квад на уровне моря (пена и глубина — в шейдере).</summary>
        void BuildWater(int W, int H, float ox, float oz, Vector4 origin)
        {
            float x0 = ox, x1 = ox + origin.z;
            float z0 = oz, z1 = oz + origin.w;
            var verts = new[]
            {
                new Vector3(x0, SeaLevel, z0),
                new Vector3(x1, SeaLevel, z0),
                new Vector3(x1, SeaLevel, z1),
                new Vector3(x0, SeaLevel, z1),
            };
            var uvs = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
            };
            var tris = new[] { 0, 2, 1, 0, 3, 2 };
            var mesh = new Mesh { name = "WorldMapWater", vertices = verts, uv = uvs, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AttachMesh("Water", mesh, _waterMat, 10);
        }

        // ---------------------------------------------------------------- декор

        /// <summary>
        /// Декор: каждый объект — отдельный GameObject (свой меш, общий материал атласа).
        /// Наклонные стенди 52°, пулы и плотность зависят от региона и биома.
        /// </summary>
        void BuildDecor(MapData map, bool[] land, int[] classes, string[] regions, float[] heights)
        {
            _decorRoot = new GameObject("Decor").transform;
            _decorRoot.parent = _root.transform;

            var treesBig = Cat("trees_big");
            var treesPine = Cat("trees_pine");
            var treesBare = Cat("trees_bare");
            var bushes = Cat("bushes");
            var rocks = Cat("rocks");
            var fields = Cat("fields");
            var small = Cat("small");

            int W = map.width, H = map.height;
            foreach (var t in map.tiles)
            {
                if (t.sea || t.c < 0 || t.r < 0 || t.c >= W || t.r >= H) continue;
                int idx = t.r * W + t.c;
                if (!land[idx]) continue;
                int cls = classes[idx];
                string region = regions[idx] ?? t.region;
                float y = heights[idx];

                var pool = PoolFor(region, cls, treesBig, treesPine, treesBare, bushes, rocks, fields, small);
                if (pool == null || pool.Count == 0) continue;

                int count = DecorCount(cls, t.c, t.r);
                for (int k = 0; k < count; k++)
                {
                    var s = pool[(int)(Hash01(t.c, t.r, k * 7 + 3) * pool.Count) % pool.Count];
                    var world = WorldMapCoords.HexToWorld(t.c, t.r);
                    float dx = (Hash01(t.c, t.r, k * 13 + 1) - 0.5f) * WorldMapCoords.HexWidth * 0.72f;
                    float dz = (Hash01(t.c, t.r, k * 13 + 2) - 0.5f) * WorldMapCoords.RowHeight * 0.72f;
                    float sc = 0.75f + Hash01(t.c, t.r, k * 13 + 5) * 0.65f;
                    bool rocky = cls == 9 || cls == 10 || cls == 6;
                    if (rocky) sc *= 1.25f;

                    if (s.cat == "fields")
                        AddDecorObject(s, world.x + dx * 0.35f, y + 0.03f, world.z + dz * 0.35f,
                            sc * 1.15f, groundDecal: true);
                    else
                        AddDecorObject(s, world.x + dx, y - 0.05f, world.z + dz, sc, groundDecal: false);
                }
            }
        }

        /// <summary>Пул спрайтов для (регион, биом-класс): Дорн без зелёных деревьев, острова — камни.</summary>
        List<AtlasSpriteEntry> PoolFor(string region, int cls,
            List<AtlasSpriteEntry> big, List<AtlasSpriteEntry> pine, List<AtlasSpriteEntry> bare,
            List<AtlasSpriteEntry> bushes, List<AtlasSpriteEntry> rocks,
            List<AtlasSpriteEntry> fields, List<AtlasSpriteEntry> small)
        {
            bool forest = cls == 15 || cls == 13;
            bool rocky = cls == 9 || cls == 10 || cls == 6 || cls == 4;
            switch (region)
            {
                case "dorne":                              // песчаники и сухие кусты, БЕЗ зелени
                    return Union(_rockBare, _smallDry, _bushDry);
                case "islands":                            // камень и сухие ели
                    return Union(_rockBare, rocks, _smallDry, pine);
                case "wall":
                    return Union(pine, rocks, small);
                case "north":
                    return forest ? Union(pine, big, bushes) : Union(big, bushes, small, rocks);
                case "riverlands":
                    return forest ? Union(big, bushes, pine) : Union(bushes, small, fields);
                case "vale":
                    return rocky ? Union(rocks, pine, small) : Union(big, bushes, rocks);
                case "westerlands":
                    return rocky ? Union(rocks, small, bushes) : Union(big, bushes, rocks);
                case "reach":
                    return forest ? Union(big, bushes) : Union(fields, bushes, small, big);
                case "stormlands":
                    return forest ? Union(big, bushes, pine) : Union(big, bushes, rocks, small);
                case "crownlands":
                    return forest ? Union(big, bushes) : Union(bushes, small, big);
                default:
                    return Union(big, bushes, small);
            }
        }

        static List<AtlasSpriteEntry> Union(List<AtlasSpriteEntry> a, List<AtlasSpriteEntry> b,
            List<AtlasSpriteEntry> c = null, List<AtlasSpriteEntry> d = null)
        {
            var r = new List<AtlasSpriteEntry>();
            if (a != null) r.AddRange(a);
            if (b != null) r.AddRange(b);
            if (c != null) r.AddRange(c);
            if (d != null) r.AddRange(d);
            return r;
        }

        /// <summary>Плотность объектов на гекс: лес 12-18, горы 10-16, холмы 6-10, пустыня 2-5.</summary>
        static int DecorCount(int cls, int c, int r)
        {
            int j = (int)(Hash01(c, r, 77) * 7f); // 0..6
            switch (cls)
            {
                case 15: case 13: return 12 + j;      // лес
                case 10: case 9: return 10 + (j % 7); // горы/скалы
                case 6: case 4: return 6 + (j % 5);   // холмы
                case 14: return 4 + (j % 5);          // болото
                case 5: case 3: return 2 + (j % 4);   // пустыня/сухие земли
                case 1: case 12: return 3 + (j % 4);  // тундра/холодные равнины
                default: return 3 + (j % 4);          // равнины
            }
        }

        /// <summary>
        /// Один объект-стендee: квад наклонён на 52° (верх уходит на север/+Z),
        /// меш свой у каждого объекта; материал — общий по атласу.
        /// </summary>
        GameObject AddDecorObject(AtlasSpriteEntry s, float x, float y, float z,
            float scale, bool groundDecal)
        {
            var go = new GameObject((groundDecal ? "decal_" : "deco_") + s.name);
            go.transform.parent = _decorRoot;
            go.transform.position = new Vector3(x, y, z);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = BuildStandeeMesh(s, scale, groundDecal);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = groundDecal ? _decalMat : _decorMat;
            return go;
        }

        /// <summary>Меш стенди: наклонный квад с UV атласа (или плоская декаль).</summary>
        Mesh BuildStandeeMesh(AtlasSpriteEntry s, float scale, bool groundDecal)
        {
            float w = s.w / Ppu * scale;
            float h = s.h / Ppu * scale;

            Vector3 v0, v1, v2, v3;
            if (groundDecal)
            {
                float dw = w * 0.9f, dh = h * 0.9f;
                v0 = new Vector3(-dw / 2f, 0f, -dh / 2f);
                v1 = new Vector3(dw / 2f, 0f, -dh / 2f);
                v2 = new Vector3(dw / 2f, 0f, dh / 2f);
                v3 = new Vector3(-dw / 2f, 0f, dh / 2f);
            }
            else
            {
                // наклон 52°: основание в z=0, верх смещается на север (+Z) и вверх
                float rad = TiltDeg * Mathf.PI / 180f;
                float yTop = h * Mathf.Cos(rad);
                float zTop = h * Mathf.Sin(rad);
                v0 = new Vector3(-w / 2f, 0f, 0f);
                v1 = new Vector3(w / 2f, 0f, 0f);
                v2 = new Vector3(w / 2f, yTop, zTop);
                v3 = new Vector3(-w / 2f, yTop, zTop);
            }

            float aw = s.atlas == "StandaloneAdventureDecorationAtlas" ? 512f : 1024f;
            float ah = s.atlas == "StandaloneAdventureDecorationAtlas" ? 512f : 708f;
            float pad = 0.5f;
            float u0 = (s.x + pad) / aw, u1 = (s.x + s.w - pad) / aw;
            float vlo = (s.y + pad) / ah, vhi = (s.y + s.h - pad) / ah;

            var mesh = new Mesh { name = "standee_" + s.name };
            mesh.vertices = new[] { v0, v1, v2, v3 };
            mesh.uv = new[]
            {
                new Vector2(u0, vlo), new Vector2(u1, vlo),
                new Vector2(u1, vhi), new Vector2(u0, vhi),
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------------------------------------------------------- постройки

        void BuildBuildings(MapData map, float[] heights)
        {
            _buildingRoot = new GameObject("Buildings").transform;
            _buildingRoot.parent = _root.transform;

            int W = map.width, H = map.height;
            foreach (var p in map.provinces)
            {
                if (p.settlement == null || p.settlement.Count < 2) continue;
                int c = p.settlement[0], r = p.settlement[1];
                if (c < 0 || r < 0 || c >= W || r >= H) continue;

                string key = BuildingKey(p, c, r);
                if (!_buildingSprites.TryGetValue(key, out var s)) continue;

                var world = WorldMapCoords.HexToWorld(c, r);
                float y = heights[r * W + c];
                var go = new GameObject("building_" + p.id);
                go.transform.parent = _buildingRoot;
                go.transform.position = new Vector3(world.x, y - 0.08f, world.z);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = BuildStandeeMesh(s, 1.35f, groundDecal: false);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _buildingMat;
            }
        }

        /// <summary>Тип поселения: столица -> green_town, порт -> hut2, Дорн -> pyramid/temple.</summary>
        string BuildingKey(ProvinceDef p, int c, int r)
        {
            if (p.isCapital) return "green_town";
            if (p.port && _buildingSprites.ContainsKey("hut2")) return "hut2";
            string[] options;
            switch (p.region)
            {
                case "dorne":
                    options = new[] { "pyramid", "temple", "village", "hut" };
                    break;
                case "islands":
                    options = new[] { "hut", "hut2", "village" };
                    break;
                case "reach":
                    options = new[] { "village", "hut", "temple", "fountain", "hut2" };
                    break;
                default:
                    options = new[] { "village", "hut", "hut2", "temple", "fountain" };
                    break;
            }
            return options[(int)(Hash01(c, r, 17) * options.Length) % options.Length];
        }

        // ---------------------------------------------------------------- камера/атмосфера

        void SetupCamera(int W, int H)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
            }
            MapCamera = cam;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.32f, 0.42f, 0.52f, 1f);
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1200f;

            var rig = cam.GetComponent<WorldMapCameraRig>();
            if (rig == null) rig = cam.gameObject.AddComponent<WorldMapCameraRig>();
            rig.Init(cam, WorldMapCoords.MapSizeWorld(W, H));
            Rig = rig;
        }

        void SetupAtmosphere()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 60f;
            RenderSettings.fogEndDistance = 320f;
            RenderSettings.fogColor = new Color(0.36f, 0.46f, 0.55f, 1f);
        }

        GameObject AttachMesh(string name, Mesh mesh, Material mat, int order)
        {
            var go = new GameObject(name);
            go.transform.parent = _root != null ? _root.transform : null;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return go;
        }

        // ---------------------------------------------------------------- пикинг и проекция

        /// <summary>Клик (GUI-координаты) -> гекс карты.</summary>
        public bool PickTile(Vector2 guiPos, out int c, out int r)
        {
            c = r = -1;
            if (Rig == null || _map == null) return false;
            Vector3 world;
            if (!Rig.GroundPoint(guiPos, out world)) return false;
            WorldMapCoords.WorldToHex(world.x, world.z, out c, out r);
            return c >= 0 && r >= 0 && c < _map.width && r < _map.height;
        }

        /// <summary>Провинция под кликом (или null).</summary>
        public string PickProvince(Vector2 guiPos)
        {
            if (!PickTile(guiPos, out int c, out int r)) return null;
            if (_map.TileByCoord.TryGetValue((c, r), out var t)) return t.province;
            return null;
        }

        /// <summary>Мировая точка -> GUI-координаты (для маркеров IMGUI).</summary>
        public Vector2 WorldToGui(Vector3 world)
        {
            if (MapCamera == null) return new Vector2(-1000, -1000);
            var sp = MapCamera.WorldToScreenPoint(world);
            return new Vector2(sp.x, Screen.height - sp.y);
        }

        /// <summary>Позиция поселения провинции в мире (для маркеров).</summary>
        public Vector3 ProvinceMarkerWorld(ProvinceDef p)
        {
            if (p?.settlement == null || p.settlement.Count < 2)
                return new Vector3(-1000, 0, -1000);
            var w = WorldMapCoords.HexToWorld(p.settlement[0], p.settlement[1]);
            w.y = WorldMapCoords.HexSize * 0.9f;
            return w;
        }

        /// <summary>Включает/выключает видимость карты (в меню карта скрыта).</summary>
        public void SetVisible(bool visible)
        {
            if (_root != null) _root.SetActive(visible);
        }

        /// <summary>Переподключает камеру/риг (после загрузки сцены со своей камерой).</summary>
        public void ReattachCamera()
        {
            if (_map != null) SetupCamera(_map.width, _map.height);
        }

        public static uint Hash(int c, int r, int salt)
        {
            unchecked
            {
                uint h = (uint)(c * 374761393 + r * 668265263 + salt * 2246822519);
                h = (h ^ (h >> 13)) * 1274126177;
                return h ^ (h >> 16);
            }
        }

        public static float Hash01(int c, int r, int salt) => (Hash(c, r, salt) & 0xFFFFFF) / 16777215f;
    }
}
