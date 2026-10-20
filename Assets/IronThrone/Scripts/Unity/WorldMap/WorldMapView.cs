// IronThrone: 3D-вид глобальной карты (стиль Songs of Conquest 2.5D).
// Строит меш рельефа со сплейтингом по control-текстурам, воду с пеной,
// декор (деревья/камни) и постройки из атласов, нарезанных tools/slice_atlas.py.
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
        /// <summary>Индексы слоёв TerrainTextureArray: plains, forest, hills, mountains, desert, marsh, snow, dirt.</summary>
        public static readonly int[] TerrainLayers = { 4, 106, 24, 62, 40, 104, 96, 0 };
        /// <summary>Высота гекса по каналу рельефа.</summary>
        public static readonly float[] ChannelHeight = { 0.14f, 0.15f, 0.32f, 0.58f, 0.14f, 0.06f, 0.14f, 0.10f };

        const float Ppu = 32f; // pixels-to-units из спрайтов игры
        const float SeaLevel = 0.045f;

        MapData _map;
        public MapData Map => _map;

        GameObject _root;
        Material _terrainMat, _waterMat, _decorMat, _buildingMat;
        readonly Dictionary<string, AtlasSpriteEntry> _buildingSprites = new Dictionary<string, AtlasSpriteEntry>();
        readonly Dictionary<string, List<AtlasSpriteEntry>> _catIndex = new Dictionary<string, List<AtlasSpriteEntry>>();

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
            var terrainArray = Resources.Load<Texture2DArray>("Art/CartographyAdventureTerrainTextureArray");
            var shallow = Resources.Load<Texture2D>("Art/ArleonAdventureWaterShallowSurfaceTexture");
            var deep = Resources.Load<Texture2D>("Art/ArleonAdventureWaterDeepSurfaceTexture");
            var decoAtlas = Resources.Load<Texture2D>("Art/StandaloneAdventureDecorationAtlas");
            var entAtlas = Resources.Load<Texture2D>("Art/PermanentMapEntityAtlas");

            _terrainMat = new Material(Shader.Find("IronThrone/CartographyTerrain"));
            _waterMat = new Material(Shader.Find("IronThrone/CartographyWater"));
            _decorMat = new Material(Shader.Find("Unlit/Transparent Cutout"));
            _buildingMat = new Material(Shader.Find("Unlit/Transparent Cutout"));

            if (terrainArray != null) _terrainMat.SetTexture("_TerrainArray", terrainArray);
            if (shallow != null) _waterMat.SetTexture("_ShallowTex", shallow);
            if (deep != null) _waterMat.SetTexture("_DeepTex", deep);
            if (decoAtlas != null) _decorMat.mainTexture = decoAtlas;
            if (entAtlas != null) _buildingMat.mainTexture = entAtlas;

            // индексы слоёв рельефа в атласе
            _terrainMat.SetVector("_LayersA", new Vector4(
                TerrainLayers[0], TerrainLayers[1], TerrainLayers[2], TerrainLayers[3]));
            _terrainMat.SetVector("_LayersB", new Vector4(
                TerrainLayers[4], TerrainLayers[5], TerrainLayers[6], TerrainLayers[7]));
            _terrainMat.SetFloat("_Tiling", 0.22f);
            _terrainMat.SetColor("_Tint", new Color(0.98f, 0.97f, 0.92f, 1f));
            _waterMat.SetFloat("_Tiling", 0.18f);
            _waterMat.SetFloat("_Speed", 0.35f);

            var size = WorldMapCoords.MapSizeWorld(W, H);
            float ox = -WorldMapCoords.HexWidth * 0.5f;
            float oz = 0f;
            var origin = new Vector4(ox, oz, size.x + WorldMapCoords.HexWidth * 0.5f, size.y);
            _terrainMat.SetVector("_MapOrigin", origin);
            _waterMat.SetVector("_MapOrigin", origin);

            // высоты гексов
            float[] heights = new float[W * H];
            int[] channels = new int[W * H];
            bool[] land = new bool[W * H];
            for (int i = 0; i < W * H; i++) { channels[i] = -1; }
            foreach (var t in map.tiles)
            {
                if (t.c < 0 || t.r < 0 || t.c >= W || t.r >= H) continue;
                int idx = t.r * W + t.c;
                if (t.sea) { land[idx] = false; channels[idx] = -1; heights[idx] = 0f; }
                else
                {
                    int ch = TerrainChannel(t.terrain);
                    land[idx] = true;
                    channels[idx] = ch;
                    heights[idx] = ChannelHeight[ch];
                }
            }

            BuildGround(W, H, ox, oz, origin, heights);
            BuildControl(W, H, ox, oz, origin, channels, land);
            BuildWater(W, H, ox, oz, origin, land);
            BuildDecor(map, land, channels);
            BuildBuildings(map);

            SetupCamera(W, H);
            SetupAtmosphere();
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
        }

        List<AtlasSpriteEntry> Cat(string name)
        {
            return _catIndex.TryGetValue(name, out var l) ? l : null;
        }

        static int TerrainChannel(string t)
        {
            if (string.IsNullOrEmpty(t)) return 0;
            t = t.ToLowerInvariant();
            if (t.StartsWith("plains") || t.StartsWith("grass") || t.StartsWith("field")) return 0;
            if (t.StartsWith("forest") || t.StartsWith("wood")) return 1;
            if (t.StartsWith("hill")) return 2;
            if (t.StartsWith("mount") || t.StartsWith("peak")) return 3;
            if (t.StartsWith("desert") || t.StartsWith("sand") || t.StartsWith("dune") || t.StartsWith("steppe")) return 4;
            if (t.StartsWith("marsh") || t.StartsWith("swamp") || t.StartsWith("bog")) return 5;
            if (t.StartsWith("snow") || t.StartsWith("tundra") || t.StartsWith("frost")) return 6;
            return 7;
        }

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

        /// <summary>Control-текстуры сплейтинга + карта береговой близости (для пены).</summary>
        void BuildControl(int W, int H, float ox, float oz, Vector4 origin, int[] channels, bool[] land)
        {
            int tw = W * 4, th = H * 4;
            var ca = new Color32[tw * th];
            var cb = new Color32[tw * th];
            var coast = new Color32[tw * th];
            float cellW = origin.z / tw;
            float cellH = origin.w / th;

            // расстояние до суши по гексам (BFS) для пены
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

                    float a0 = 0, a1 = 0, a2 = 0, a3 = 0;
                    float b0 = 0, b1 = 0, b2 = 0, b3 = 0;
                    float cv = 0;

                    Accum(channels, W, H, c00, r0, w00, ref a0, ref a1, ref a2, ref a3, ref b0, ref b1, ref b2, ref b3);
                    Accum(channels, W, H, c10, r0, w10, ref a0, ref a1, ref a2, ref a3, ref b0, ref b1, ref b2, ref b3);
                    Accum(channels, W, H, c01, r1, w01, ref a0, ref a1, ref a2, ref a3, ref b0, ref b1, ref b2, ref b3);
                    Accum(channels, W, H, c11, r1, w11, ref a0, ref a1, ref a2, ref a3, ref b0, ref b1, ref b2, ref b3);

                    cv += CoastAt(W, H, c00, r0, coastDist) * w00;
                    cv += CoastAt(W, H, c10, r0, coastDist) * w10;
                    cv += CoastAt(W, H, c01, r1, coastDist) * w01;
                    cv += CoastAt(W, H, c11, r1, coastDist) * w11;

                    int p = j * tw + i;
                    ca[p] = new Color32(b255(a0), b255(a1), b255(a2), b255(a3));
                    cb[p] = new Color32(b255(b0), b255(b1), b255(b2), b255(b3));
                    coast[p] = new Color32(b255(cv), 0, 0, 255);
                }
            }

            var ta = MakeTex(tw, th, ca, "ControlA");
            var tb = MakeTex(tw, th, cb, "ControlB");
            var tc = MakeTex(tw, th, coast, "CoastMap");
            _terrainMat.SetTexture("_ControlA", ta);
            _terrainMat.SetTexture("_ControlB", tb);
            _waterMat.SetTexture("_CoastMap", tc);
        }

        static void Accum(int[] channels, int W, int H, int c, int r, float w,
            ref float a0, ref float a1, ref float a2, ref float a3,
            ref float b0, ref float b1, ref float b2, ref float b3)
        {
            if (c < 0 || r < 0 || c >= W || r >= H) return;
            int ch = channels[r * W + c];
            switch (ch)
            {
                case 0: a0 += w; break;
                case 1: a1 += w; break;
                case 2: a2 += w; break;
                case 3: a3 += w; break;
                case 4: b0 += w; break;
                case 5: b1 += w; break;
                case 6: b2 += w; break;
                case 7: b3 += w; break;
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
            // BFS от суши в море: чем дальше, тем меньше близость
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
        void BuildWater(int W, int H, float ox, float oz, Vector4 origin, bool[] land)
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

        void BuildDecor(MapData map, bool[] land, int[] channels)
        {
            int W = map.width, H = map.height;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            var treesBig = Cat("trees_big");
            var treesPine = Cat("trees_pine");
            var treesBare = Cat("trees_bare");
            var bushes = Cat("bushes");
            var rocks = Cat("rocks");
            var fields = Cat("fields");
            var small = Cat("small");

            foreach (var t in map.tiles)
            {
                if (t.sea || t.c < 0 || t.r < 0 || t.c >= W || t.r >= H) continue;
                int idx = t.r * W + t.c;
                if (!land[idx]) continue;
                int ch = channels[idx];
                float y = ChannelHeight[ch];

                var pool = PickPool(ch, treesBig, treesPine, treesBare, bushes, rocks, fields, small);
                if (pool == null || pool.Count == 0) continue;

                int count = DecorCount(ch);
                for (int k = 0; k < count; k++)
                {
                    var s = pool[(int)(Hash01(t.c, t.r, k * 7 + 3) * pool.Count) % pool.Count];
                    float hx = WorldMapCoords.HexToWorld(t.c, t.r).x;
                    float hz = WorldMapCoords.HexToWorld(t.c, t.r).z;
                    float dx = (Hash01(t.c, t.r, k * 13 + 1) - 0.5f) * 1.25f;
                    float dz = (Hash01(t.c, t.r, k * 13 + 2) - 0.5f) * 1.1f;
                    float sc = 0.85f + Hash01(t.c, t.r, k * 13 + 5) * 0.55f;
                    if (s.cat == "fields")
                        AddGroundDecal(verts, uvs, tris, s, hx + dx * 0.3f, y + 0.015f, hz + dz * 0.3f, 1.35f);
                    else
                        AddBillboard(verts, uvs, tris, s, hx + dx, y - 0.02f, hz + dz, sc);
                }
            }

            if (verts.Count > 0)
            {
                var mesh = new Mesh { name = "WorldMapDecor" };
                mesh.vertices = verts.ToArray();
                mesh.uv = uvs.ToArray();
                mesh.triangles = tris.ToArray();
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                AttachMesh("Decor", mesh, _decorMat, 20);
            }
        }

        static List<AtlasSpriteEntry> PickPool(int ch,
            List<AtlasSpriteEntry> big, List<AtlasSpriteEntry> pine, List<AtlasSpriteEntry> bare,
            List<AtlasSpriteEntry> bushes, List<AtlasSpriteEntry> rocks,
            List<AtlasSpriteEntry> fields, List<AtlasSpriteEntry> small)
        {
            switch (ch)
            {
                case 1: return Union(big, pine, bushes);          // лес
                case 2: return Union(rocks, pine, bushes);        // холмы
                case 3: return Union(rocks, bare);                // горы
                case 4: return rocks;                              // пустыня
                case 5: return Union(bare, bushes);                // болото
                case 6: return Union(bare, pine, bushes);          // снег
                case 7: return rocks;                              // берег
                default: return Union(fields, bushes, small);      // равнины
            }
        }

        static List<AtlasSpriteEntry> Union(List<AtlasSpriteEntry> a, List<AtlasSpriteEntry> b, List<AtlasSpriteEntry> c = null)
        {
            var r = new List<AtlasSpriteEntry>();
            if (a != null) r.AddRange(a);
            if (b != null) r.AddRange(b);
            if (c != null) r.AddRange(c);
            return r;
        }

        static int DecorCount(int ch)
        {
            switch (ch)
            {
                case 1: return 3;  // лес — густо
                case 2: return 2;
                case 3: return 2;
                case 5: return 1;
                case 6: return 1;
                case 0: return 1;
                default: return 1;
            }
        }

        /// <summary>Плоская декаль на земле (поля, заросли) — лежит на рельефе.</summary>
        void AddGroundDecal(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            AtlasSpriteEntry s, float x, float y, float z, float scale)
        {
            float w = s.w / Ppu * scale;
            float h = s.h / Ppu * scale;
            int vi = verts.Count;
            verts.Add(new Vector3(x - w / 2f, y, z - h / 2f));
            verts.Add(new Vector3(x + w / 2f, y, z - h / 2f));
            verts.Add(new Vector3(x + w / 2f, y, z + h / 2f));
            verts.Add(new Vector3(x - w / 2f, y, z + h / 2f));

            float aw = s.atlas == "StandaloneAdventureDecorationAtlas" ? 512f : 1024f;
            float ah = s.atlas == "StandaloneAdventureDecorationAtlas" ? 512f : 708f;
            float pad = 0.5f;
            float u0 = (s.x + pad) / aw, u1 = (s.x + s.w - pad) / aw;
            float v0 = (s.y + pad) / ah, v1 = (s.y + s.h - pad) / ah;
            uvs.Add(new Vector2(u0, v0));
            uvs.Add(new Vector2(u1, v0));
            uvs.Add(new Vector2(u1, v1));
            uvs.Add(new Vector2(u0, v1));

            tris.Add(vi + 0); tris.Add(vi + 2); tris.Add(vi + 1);
            tris.Add(vi + 0); tris.Add(vi + 3); tris.Add(vi + 2);
        }

        /// <summary>Квад-стенди (как в Songs of Conquest): лицом к камере с юга.</summary>
        void AddBillboard(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            AtlasSpriteEntry s, float x, float y, float z, float scale)
        {
            float w = s.w / Ppu * scale;
            float h = s.h / Ppu * scale;
            float x0 = x - w / 2f, x1 = x + w / 2f;
            float y0 = y, y1 = y + h;
            int vi = verts.Count;
            verts.Add(new Vector3(x0, y0, z));
            verts.Add(new Vector3(x1, y0, z));
            verts.Add(new Vector3(x1, y1, z));
            verts.Add(new Vector3(x0, y1, z));

            // UV атласа (с полу-пиксельным отступом)
            float aw = s.atlas == "StandaloneAdventureDecorationAtlas" ? 512f : 1024f;
            float ah = s.atlas == "StandaloneAdventureDecorationAtlas" ? 512f : 708f;
            float pad = 0.5f;
            float u0 = (s.x + pad) / aw, u1 = (s.x + s.w - pad) / aw;
            float v0 = (s.y + pad) / ah, v1 = (s.y + s.h - pad) / ah;
            uvs.Add(new Vector2(u0, v0));
            uvs.Add(new Vector2(u1, v0));
            uvs.Add(new Vector2(u1, v1));
            uvs.Add(new Vector2(u0, v1));

            // лицом к -Z (камера с юга)
            tris.Add(vi + 0); tris.Add(vi + 2); tris.Add(vi + 1);
            tris.Add(vi + 0); tris.Add(vi + 3); tris.Add(vi + 2);
        }

        // ---------------------------------------------------------------- постройки

        void BuildBuildings(MapData map)
        {
            int W = map.width, H = map.height;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            foreach (var p in map.provinces)
            {
                if (p.settlement == null || p.settlement.Count < 2) continue;
                int c = p.settlement[0], r = p.settlement[1];
                if (c < 0 || r < 0 || c >= W || r >= H) continue;

                string key = "village";
                if (p.isCapital) key = "green_town";
                else if (p.port && _buildingSprites.ContainsKey("hut2")) key = "hut2";
                else
                {
                    var options = new[] { "village", "hut", "hut2", "temple", "fountain", "pyramid" };
                    key = options[(int)(Hash01(c, r, 17) * options.Length) % options.Length];
                }
                if (!_buildingSprites.TryGetValue(key, out var s)) continue;

                var world = WorldMapCoords.HexToWorld(c, r);
                float y = 0.14f;
                AddBillboard(verts, uvs, tris, s, world.x, y - 0.02f, world.z, 1.7f);
            }

            if (verts.Count > 0)
            {
                var mesh = new Mesh { name = "WorldMapBuildings" };
                mesh.vertices = verts.ToArray();
                mesh.uv = uvs.ToArray();
                mesh.triangles = tris.ToArray();
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                AttachMesh("Buildings", mesh, _buildingMat, 25);
            }
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
            cam.clearColor = new Color(0.32f, 0.42f, 0.52f, 1f);
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 400f;

            var rig = cam.GetComponent<WorldMapCameraRig>();
            if (rig == null) rig = cam.gameObject.AddComponent<WorldMapCameraRig>();
            rig.Init(cam, WorldMapCoords.MapSizeWorld(W, H));
            Rig = rig;
        }

        void SetupAtmosphere()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 26f;
            RenderSettings.fogEndDistance = 95f;
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
            w.y = 0.9f;
            return w;
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
