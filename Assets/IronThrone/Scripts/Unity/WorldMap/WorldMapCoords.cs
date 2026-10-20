// IronThrone: координаты глобальной карты (гексы <-> мировые XZ).
// Математика совпадает с HexMapTexture (odd-r offset, pointy-top),
// но вместо пикселей — метры 3D-сцены.
using System;
using UnityEngine;

namespace IronThrone.Unity
{
    public static class WorldMapCoords
    {
        public const float HexSize = 1.0f;
        public const float HexWidth = 1.7320508f * HexSize; // sqrt(3)
        public const float RowHeight = 1.5f * HexSize;

        /// <summary>Центр гекса (c, r) в мировых координатах XZ.</summary>
        public static Vector3 HexToWorld(int c, int r, float y = 0f)
        {
            float x = HexWidth * (c + 0.5f * (r & 1)) + HexWidth / 2f;
            float z = RowHeight * r + HexSize;
            return new Vector3(x, y, z);
        }

        /// <summary>Размер карты в мире (w, h) с запасом на края.</summary>
        public static Vector2 MapSizeWorld(int mapWidth, int mapHeight)
        {
            return new Vector2(mapWidth * HexWidth + HexWidth / 2f,
                               mapHeight * RowHeight + HexSize * 2f);
        }

        /// <summary>Дробные гекс-координаты ряда/колонки для мировой точки.</summary>
        public static void WorldToHexF(float x, float z, out float cf, out float rf)
        {
            rf = (z - HexSize) / RowHeight;
            int r0 = FloorParity(rf);
            cf = (x - HexWidth / 2f) / HexWidth - 0.5f * (r0 & 1);
        }

        /// <summary>Ближайший гекс к мировой точке (без ограничений по карте).</summary>
        public static void WorldToHex(float x, float z, out int c, out int r)
        {
            float cf, rf;
            WorldToHexF(x, z, out cf, out rf);
            r = Mathf.RoundToInt(rf);
            float cfr = (x - HexWidth / 2f) / HexWidth - 0.5f * (r & 1);
            c = Mathf.RoundToInt(cfr);
        }

        /// <summary>
        /// Staggered-билиней: веса 4 ближайших гексов к мировой точке.
        /// Нечётные ряды сдвинуты на полгекса — каждая пара рядов интерполируется
        /// в своей колоночной решётке.
        /// </summary>
        public static void HexSpaceBilinear(float x, float z,
            out int c00, out int r0, out int c10,
            out int c01, out int r1, out int c11,
            out float w00, out float w10, out float w01, out float w11)
        {
            float rf;
            float cf;
            WorldToHexF(x, z, out cf, out rf);
            r0 = FloorParity(rf);
            r1 = r0 + 1;
            float ty = Clamp01(rf - r0);

            float cf0 = (x - HexWidth / 2f) / HexWidth - 0.5f * (r0 & 1);
            float cf1 = (x - HexWidth / 2f) / HexWidth - 0.5f * (r1 & 1);
            c00 = FloorParity(cf0);
            c10 = c00 + 1;
            float tx0 = Clamp01(cf0 - c00);
            c01 = FloorParity(cf1);
            c11 = c01 + 1;
            float tx1 = Clamp01(cf1 - c01);

            w00 = (1f - tx0) * (1f - ty);
            w10 = tx0 * (1f - ty);
            w01 = (1f - tx1) * ty;
            w11 = tx1 * ty;
            float s = w00 + w10 + w01 + w11;
            if (s > 0.0001f) { w00 /= s; w10 /= s; w01 /= s; w11 /= s; }
        }

        static int FloorParity(float v)
        {
            int f = Mathf.FloorToInt(v);
            return f;
        }

        static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }
    }
}
