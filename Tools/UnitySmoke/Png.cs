using System;
using System.IO;
using System.IO.Compression;

namespace IronThrone.UnitySmoke
{
    /// <summary>Минимальный PNG-энкодер RGBA8 — чтобы проверить, что рисует Texture2D.</summary>
    public static class Png
    {
        public static void Write(string path, int width, int height, UnityEngine.Color32[] pixels)
        {
            using var fs = File.Create(path);
            fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);

            var ihdr = new byte[13];
            WriteInt(ihdr, 0, width);
            WriteInt(ihdr, 4, height);
            ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
            Chunk(fs, "IHDR", ihdr);

            var raw = new byte[(width * 4 + 1) * height];
            int p = 0;
            // Texture2D хранит пиксели снизу вверх, PNG — сверху вниз
            for (int y = height - 1; y >= 0; y--)
            {
                raw[p++] = 0;
                for (int x = 0; x < width; x++)
                {
                    var c = pixels[y * width + x];
                    raw[p++] = c.r; raw[p++] = c.g; raw[p++] = c.b; raw[p++] = c.a;
                }
            }
            using (var ms = new MemoryStream())
            {
                using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(raw, 0, raw.Length);
                Chunk(fs, "IDAT", ms.ToArray());
            }
            Chunk(fs, "IEND", Array.Empty<byte>());
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; WriteInt(len, 0, data.Length); s.Write(len, 0, 4);
            var t = System.Text.Encoding.ASCII.GetBytes(type);
            s.Write(t, 0, 4);
            s.Write(data, 0, data.Length);
            uint crc = Crc(t, data);
            var c = new byte[4]; WriteInt(c, 0, unchecked((int)crc)); s.Write(c, 0, 4);
        }

        private static void WriteInt(byte[] b, int o, int v)
        {
            b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
        }

        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        private static uint Crc(byte[] a, byte[] b)
        {
            uint c = 0xFFFFFFFFu;
            foreach (var x in a) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
            foreach (var x in b) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }
    }
}
