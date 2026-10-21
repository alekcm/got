using System;
using System.Collections.Generic;

namespace IronThrone.Core.Worldgen
{
    /// <summary>
    /// Детерминированный генератор случайных чисел для генерации карт.
    /// Не зависит от версии .NET (никакого System.Random): один и тот же сид
    /// даёт одну и ту же карту в редакторе, в билде и в headless-проверках.
    /// Алгоритм — SplitMix64.
    /// </summary>
    public sealed class SoqRng
    {
        private ulong _state;
        private readonly ulong _origin;

        public int Seed { get; }

        public SoqRng(int seed)
        {
            Seed = seed;
            _origin = unchecked((ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0x165667B19E3779F9UL);
            _state = _origin;
            // прогреваем состояние, чтобы близкие сиды давали заметно разные карты
            NextULong();
            NextULong();
        }

        public ulong NextULong()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Дробное число [0,1).</summary>
        public double NextDouble()
        {
            return (NextULong() >> 11) * (1.0 / 9007199254740992.0);
        }

        public float NextFloat() => (float)NextDouble();

        /// <summary>Целое [0, maxExclusive).</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            return (int)(NextULong() % (ulong)maxExclusive);
        }

        /// <summary>Целое [min, maxExclusive).</summary>
        public int Next(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            return min + Next(maxExclusive - min);
        }

        public float Range(float min, float max) => min + (float)NextDouble() * (max - min);

        public bool Chance(double probability) => probability > 0 && NextDouble() < probability;

        public T Pick<T>(IList<T> list)
        {
            if (list == null || list.Count == 0) return default(T);
            return list[Next(list.Count)];
        }

        public void Shuffle<T>(IList<T> list)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        /// <summary>Взвешенный выбор индекса по массиву весов.</summary>
        public int WeightedIndex(IList<float> weights)
        {
            if (weights == null || weights.Count == 0) return 0;
            float total = 0f;
            for (int i = 0; i < weights.Count; i++) total += weights[i] > 0 ? weights[i] : 0f;
            if (total <= 0f) return Next(weights.Count);
            float roll = (float)NextDouble() * total;
            for (int i = 0; i < weights.Count; i++)
            {
                float w = weights[i] > 0 ? weights[i] : 0f;
                if (roll < w) return i;
                roll -= w;
            }
            return weights.Count - 1;
        }

        // ------------------------------------------------------------------ пространственный шум
        // Стабильный хеш координат: используется для «шума» рельефа и декора.
        public static uint Hash(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + salt * 2246822519);
                h = (h ^ (h >> 13)) * 1274126177;
                return h ^ (h >> 16);
            }
        }

        public static float Hash01(int x, int y, int salt) => (Hash(x, y, salt) & 0xFFFFFF) / 16777215f;

        /// <summary>
        /// Стабильный «рощевой» шум по координатам тайла — без состояния и без
        /// зависимости от порядка обхода. Нужен, чтобы декор собирался куртинами.
        /// </summary>
        public static float GroveNoise(int x, int y, int salt)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            int fx = 0x9E37, fy = 0x85EB;
            for (int o = 0; o < 3; o++)
            {
                int ix = (x << (o + 1)) / 3;   // грубая сетка на первой октаве
                int iy = (y << (o + 1)) / 3;
                float v = Hash01(ix, iy, salt + fx * 7 + fy * 13 + o * 101);
                sum += v * amp;
                norm += amp;
                amp *= 0.55f;
                fx = fx * 31 + 17;
                fy = fy * 17 + 31;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>
        /// Простой value-noise с бикубической интерполяцией. Решётка строится из
        /// перестановок, зависящих от сида, поэтому шум воспроизводим.
        /// </summary>
        public sealed class Noise
        {
            private readonly int[] _perm = new int[512];

            public Noise(SoqRng rng)
            {
                var table = new int[256];
                for (int i = 0; i < 256; i++) table[i] = i;
                for (int i = 255; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    int t = table[i];
                    table[i] = table[j];
                    table[j] = t;
                }
                for (int i = 0; i < 512; i++) _perm[i] = table[i & 255];
            }

            private float Value(int x, int y)
            {
                int h = _perm[(_perm[x & 255] + (y & 255)) & 511];
                // h — число 0..255; раньше здесь было (h & 0xFFFF) / 65535f, из-за чего
                // шум всегда был ≈ 0.002 и рельеф получался однотонным.
                return h / 255f;
            }

            private static float Smooth(float t) => t * t * (3f - 2f * t);

            /// <summary>Шум [0,1).</summary>
            public float Eval(float x, float y)
            {
                int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
                float tx = Smooth(x - x0), ty = Smooth(y - y0);
                float a = Value(x0, y0), b = Value(x0 + 1, y0);
                float c = Value(x0, y0 + 1), d = Value(x0 + 1, y0 + 1);
                return (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty;
            }

            /// <summary>Фрактальный шум (несколько октав), [0,1).</summary>
            public float Fbm(float x, float y, int octaves = 3, float persistence = 0.5f)
            {
                float sum = 0f, amp = 1f, norm = 0f, freq = 1f;
                for (int o = 0; o < octaves; o++)
                {
                    sum += Eval(x * freq, y * freq) * amp;
                    norm += amp;
                    amp *= persistence;
                    freq *= 2.03f;
                }
                return norm > 0 ? sum / norm : 0f;
            }
        }
    }
}
