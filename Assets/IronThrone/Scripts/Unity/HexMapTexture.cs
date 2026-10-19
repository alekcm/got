using System;
using System.Collections.Generic;
using System.Linq;
using IronThrone.Core.Content;
using IronThrone.Core.Game;
using UnityEngine;

namespace IronThrone.Unity
{
    /// <summary>
    /// Растровая отрисовка гексагональной карты в Texture2D (IMGUI-слой).
    /// Отвечает за цвета провинций, границы, поселения и попадание клика в гекс.
    /// </summary>
    public class HexMapTexture
    {

        public Texture2D Texture;
        public int HexSize = 22;
        public float HexWidth;
        public float RowHeight;
        public int Width;
        public int Height;

        // odd-r offset: смещения и индексы рёбер зависят от чётности ряда.
        private static readonly (int dc, int dr)[] DirsEven =
        {
            (1, 0), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1)
        };

        private static readonly (int dc, int dr)[] DirsOdd =
        {
            (1, 0), (1, 1), (0, 1), (-1, 0), (0, -1), (1, -1)
        };

        // Порядок направлений (E, SE, SW, W, NW, NE) совпадает с порядком рёбер 0..5:
        // ребро i соединяет вершины i и i+1, поэтому отдельных таблиц рёбер не нужно.

        private readonly Dictionary<(int, int), string> _provinceByTile = new Dictionary<(int, int), string>();

        public static HexMapTexture Build(GameState state, int hexSize = 22)
        {
            var tex = new HexMapTexture
            {
                HexSize = hexSize,
                Width = state.Map.width,
                Height = state.Map.height,
            };
            tex.HexWidth = (float)(Math.Sqrt(3) * hexSize);
            tex.RowHeight = hexSize * 1.5f;
            int w = (int)Math.Ceiling(tex.HexWidth * state.Map.width) + 2;
            int h = (int)Math.Ceiling(tex.RowHeight * state.Map.height + hexSize) + 2;
            var pixels = new Color32[w * h];
            var sea = new Color32(21, 44, 58, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = sea;

            var regionColors = new Dictionary<string, Color32>();
            foreach (var r in state.Map.regions) regionColors[r.id] = ParseColor(r.color);

            var colors = new Dictionary<string, Color32>();
            foreach (var p in state.Map.provinces)
            {
                var terrain = TerrainColor(p.terrain);
                if (!string.IsNullOrEmpty(p.region) && regionColors.TryGetValue(p.region, out var rc))
                    terrain = Mix(terrain, rc, 0.22f);
                colors[p.id] = terrain;
            }

            var tileByCoord = new Dictionary<(int, int), MapTileDef>();
            foreach (var t in state.Map.tiles) tileByCoord[(t.c, t.r)] = t;

            // заливка гексов
            foreach (var t in state.Map.tiles)
            {
                if (t.sea) continue;
                var center = tex.HexCenter(t.c, t.r);
                var color = !string.IsNullOrEmpty(t.province) && colors.TryGetValue(t.province, out var c)
                    ? c : new Color32(74, 84, 66, 255);
                FillHex(pixels, w, h, center.x, center.y, hexSize, color);
                if (!string.IsNullOrEmpty(t.province)) tex._provinceByTile[(t.c, t.r)] = t.province;
            }

            // границы провинций и береговая линия
            var border = new Color32(24, 28, 28, 255);
            var coast = new Color32(12, 30, 40, 255);
            foreach (var t in state.Map.tiles)
            {
                if (t.sea) continue;
                var center = tex.HexCenter(t.c, t.r);
                bool odd = (t.r & 1) == 1;
                var dirs = odd ? DirsOdd : DirsEven;
                for (int i = 0; i < 6; i++)
                {
                    var d = dirs[i];
                    int nc = t.c + d.dc, nr = t.r + d.dr;
                    tileByCoord.TryGetValue((nc, nr), out var nt);
                    bool draw = false;
                    Color32 col = border;
                    if (nt == null || nt.sea) { draw = true; col = coast; }
                    else if (nt.province != t.province) { draw = true; }
                    if (!draw) continue;
                    var a = tex.Corner(center.x, center.y, hexSize, i);
                    var b = tex.Corner(center.x, center.y, hexSize, (i + 1) % 6);
                    DrawLine(pixels, w, h, (int)a.x, (int)a.y, (int)b.x, (int)b.y, col, 2);
                }
            }

            // поселения и столицы
            foreach (var p in state.Map.provinces)
            {
                if (p.settlement == null || p.settlement.Count < 2) continue;
                var c = tex.HexCenter(p.settlement[0], p.settlement[1]);
                if (p.isCapital)
                {
                    FillCircle(pixels, w, h, c.x, c.y, 6.5f, new Color32(240, 205, 90, 255));
                    FillCircle(pixels, w, h, c.x, c.y, 3.2f, new Color32(80, 55, 20, 255));
                }
                else
                {
                    FillCircle(pixels, w, h, c.x, c.y, 3.1f, new Color32(30, 34, 32, 255));
                    FillCircle(pixels, w, h, c.x, c.y, 1.5f, new Color32(225, 220, 200, 255));
                }
            }

            // Texture2D хранит пиксели снизу вверх, а карта рисуется сверху вниз — переворачиваем строки,
            // иначе Вестерос окажется вверх ногами (Старки на юге).
            FlipRows(pixels, w, h);
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.filterMode = FilterMode.Bilinear;
            texture.Apply();
            tex.Texture = texture;
            return tex;
        }

        public static void FlipRows(Color32[] pixels, int w, int h)
        {
            for (int y = 0; y < h / 2; y++)
            {
                int top = y * w, bottom = (h - 1 - y) * w;
                for (int x = 0; x < w; x++)
                {
                    var tmp = pixels[top + x];
                    pixels[top + x] = pixels[bottom + x];
                    pixels[bottom + x] = tmp;
                }
            }
        }

        public Vector2 HexCenter(int c, int r) => new Vector2(
            (float)(HexWidth * (c + 0.5 * (r & 1)) + HexWidth / 2f),
            (float)(HexSize + RowHeight * r));

        public Vector2 Corner(float cx, float cy, float size, int i)
        {
            double angle = Math.PI / 180.0 * (60 * i - 30);
            return new Vector2((float)(cx + size * Math.Cos(angle)), (float)(cy + size * Math.Sin(angle)));
        }

        /// <summary>Гекс по координатам текстуры (0..width, 0..height).</summary>
        public bool TileAt(Vector2 pixel, out int col, out int row)
        {
            col = row = -1;
            float dx = pixel.x - HexWidth / 2f;
            float dy = pixel.y - HexSize;
            double q = (Math.Sqrt(3) / 3.0 * dx - 1.0 / 3.0 * dy) / HexSize;
            double r = (2.0 / 3.0 * dy) / HexSize;
            CubeRound(q, r, out int rq, out int rr);
            row = rr;
            col = (int)Math.Round(rq + (rr - (rr & 1)) / 2.0);
            if (col < 0 || col >= Width || row < 0 || row >= Height) return false;
            return true;
        }

        private static void CubeRound(double q, double r, out int rq, out int rr)
        {
            double s = -q - r;
            int x = (int)Math.Round(q), z = (int)Math.Round(r), y = (int)Math.Round(s);
            double dx = Math.Abs(x - q), dz = Math.Abs(z - r), dy = Math.Abs(y - s);
            if (dx > dy && dx > dz) x = -y - z;
            else if (dy > dz) y = -x - z;
            else z = -x - y;
            rq = x; rr = z;
        }

        public string ProvinceAt(GameState state, Vector2 pixel)
        {
            if (!TileAt(pixel, out var c, out var r)) return null;
            if (state.Map.TileByCoord.TryGetValue((c, r), out var t)) return t.province;
            return null;
        }

        // ------------------------------------------------------------------ примитивы
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
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    if (dx * dx + dy * dy <= r2) px[y * w + x] = color;
                }
        }

        private static void DrawLine(Color32[] px, int w, int h, int x0, int y0, int x1, int y1, Color32 color, int thickness)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            int guard = 0;
            while (guard++ < 4000)
            {
                for (int oy = -thickness / 2; oy <= thickness / 2; oy++)
                    for (int ox = -thickness / 2; ox <= thickness / 2; ox++)
                    {
                        int x = x0 + ox, y = y0 + oy;
                        if (x < 0 || y < 0 || x >= w || y >= h) continue;
                        px[y * w + x] = color;
                    }
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        private static Color32 TerrainColor(string terrain) => terrain switch
        {
            "plains" => new Color32(112, 140, 76, 255),
            "forest" => new Color32(62, 106, 62, 255),
            "hills" => new Color32(124, 108, 70, 255),
            "mountains" => new Color32(110, 110, 110, 255),
            "desert" => new Color32(198, 167, 110, 255),
            "marsh" => new Color32(82, 108, 87, 255),
            "snow" => new Color32(216, 224, 228, 255),
            "river" => new Color32(70, 120, 150, 255),
            _ => new Color32(100, 115, 85, 255),
        };

        private static Color32 ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex[0] != '#' || hex.Length < 7) return new Color32(120, 120, 120, 255);
            try
            {
                return new Color32(
                    Convert.ToByte(hex.Substring(1, 2), 16),
                    Convert.ToByte(hex.Substring(3, 2), 16),
                    Convert.ToByte(hex.Substring(5, 2), 16), 255);
            }
            catch { return new Color32(120, 120, 120, 255); }
        }

        private static Color32 Mix(Color32 a, Color32 b, float k) => new Color32(
            (byte)(a.r * (1 - k) + b.r * k),
            (byte)(a.g * (1 - k) + b.g * k),
            (byte)(a.b * (1 - k) + b.b * k), 255);
    }
}
