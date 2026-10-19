using System;

namespace IronThrone.Core.Game
{
    /// <summary>
    /// Стабильный хеш строки. Обычный GetHashCode в .NET рандомизирован между запусками,
    /// из-за чего одинаковые сиды давали разные партии. Для воспроизводимых прогонов
    /// и одинаковых боёв при перезагрузке используем явную функцию.
    /// </summary>
    public static class StableHash
    {
        public static int Of(string s)
        {
            if (s == null) return 0;
            int h = 17;
            foreach (char c in s) h = unchecked(h * 31 + c);
            return Math.Abs(h);
        }

        /// <summary>Хеш в диапазоне [0, mod).</summary>
        public static int Of(string s, int mod)
        {
            if (mod <= 0) return 0;
            return Of(s) % mod;
        }
    }
}
