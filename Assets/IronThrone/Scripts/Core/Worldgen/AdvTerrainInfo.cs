using System;
using System.Collections.Generic;

namespace IronThrone.Core.Worldgen
{
    /// <summary>
    /// Классы рельефа. Индексы 0..15 совпадают с порядком тайлов
    /// <c>WorldMapView.TerrainTileNames</c> (Resources/Art/Terrain), поэтому
    /// слой текстуры = (int)класс.
    /// </summary>
    public enum AdvTerrain
    {
        Grass = 0,
        Tundra = 1,
        Dirt = 2,
        Dry = 3,
        RockGreen = 4,
        Sand = 5,
        SandRock = 6,
        Fields = 7,
        Autumn = 8,
        Rock = 9,
        SnowRock = 10,
        Snow = 11,
        ColdGrass = 12,
        ForestMix = 13,
        Marsh = 14,
        ForestPine = 15,
    }

    /// <summary>Биом региона: набор допустимых классов рельефа и их веса.</summary>
    public enum AdvBiome
    {
        Temperate = 0,
        North = 1,
        Desert = 2,
        Swamp = 3,
        Highlands = 4,
        Wasteland = 5,
    }

    /// <summary>Свойства классов рельефа: подписи, высота, цвет, проходимость.</summary>
    public static class AdvTerrainInfo
    {
        /// <summary>Имена файлов тайлов (Resources/Art/Terrain) — тот же порядок, что и в WorldMapView.</summary>
        public static readonly string[] TileNames =
        {
            "000_grass", "010_tundra", "024_dirt", "028_dry", "034_rock_green", "040_sand",
            "042_sand_rock", "044_fields", "050_autumn", "062_rock", "094_snow_rock", "096_snow",
            "100_cold_grass", "102_forest_mix", "104_marsh", "106_forest_pine",
        };

        public static readonly string[] Names =
        {
            "Луга", "Тундра", "Земля", "Сушь", "Зелёные скалы", "Пески",
            "Песчаник", "Поля", "Осенний лес", "Скалы", "Снежные скалы", "Снег",
            "Холодные луга", "Смешанный лес", "Болото", "Сосновый лес",
        };

        /// <summary>Высота гекса в долях базовой (как ClassHeight в WorldMapView).</summary>
        public static readonly float[] Height =
        {
            1.00f, 1.10f, 1.05f, 1.00f, 1.35f, 0.95f, 1.40f, 1.00f,
            1.05f, 1.60f, 1.90f, 1.30f, 1.15f, 1.20f, 0.75f, 1.25f,
        };

        /// <summary>Цвет для миникарты / превью (hex).</summary>
        public static readonly string[] MiniMapColor =
        {
            "#6f8f4e", "#7d8f74", "#8a7150", "#b39a63", "#5f7a45", "#d8c184",
            "#c2a777", "#a8b45a", "#8d7a3f", "#7d7a72", "#8f8f8f", "#e6eef2",
            "#8fa07a", "#4d6b3c", "#4f6152", "#3d5a38",
        };

        /// <summary>
        /// Компенсация яркости тайла: текстуры в Resources/Art/Terrain нарисованы для
        /// ramp-освещения (в WorldMapView), поэтому в среднем тёмные. Здесь — множитель,
        /// приводящий среднюю яркость тайла примерно к одному уровню (снег не «выгорает»).
        /// </summary>
        public static readonly float[] Brightness =
        {
            1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f,
            1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f,
            1.00f, 1.00f, 1.00f, 1.00f,
        };

        /// <summary>Стоимость прохода в «очках движения» (для будущих армий и для A* дорог).</summary>
        public static readonly int[] MoveCost =
        {
            2, 2, 2, 2, 4, 3, 4, 2, 2, 4, 5, 3, 2, 3, 4, 3,
        };

        public static bool IsForest(AdvTerrain t) => t == AdvTerrain.ForestMix || t == AdvTerrain.ForestPine;
        public static bool IsMountain(AdvTerrain t) => t == AdvTerrain.Rock || t == AdvTerrain.SnowRock || t == AdvTerrain.RockGreen;
        public static bool IsSandy(AdvTerrain t) => t == AdvTerrain.Sand || t == AdvTerrain.SandRock || t == AdvTerrain.Dry;
        public static bool IsSnow(AdvTerrain t) => t == AdvTerrain.Snow || t == AdvTerrain.SnowRock || t == AdvTerrain.Tundra;
        public static bool IsWet(AdvTerrain t) => t == AdvTerrain.Marsh;

        /// <summary>Проходим ли рельеф (болото и горы — да, просто дороже; вода — нет).</summary>
        public static bool IsPassable(AdvTerrain t) => true;

        /// <summary>Биом по текстовому идентификатору региона (расширение формата SoQ).</summary>
        public static AdvBiome BiomeByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return AdvBiome.Temperate;
            string s = name.ToLowerInvariant();
            if (s.Contains("north") || s.Contains("snow") || s.Contains("frost") || s.Contains("ice") || s.Contains("wall"))
                return AdvBiome.North;
            if (s.Contains("desert") || s.Contains("sand") || s.Contains("dorne") || s.Contains("dune"))
                return AdvBiome.Desert;
            if (s.Contains("swamp") || s.Contains("marsh") || s.Contains("bog") || s.Contains("bayou"))
                return AdvBiome.Swamp;
            if (s.Contains("high") || s.Contains("mountain") || s.Contains("rock") || s.Contains("vale"))
                return AdvBiome.Highlands;
            if (s.Contains("waste") || s.Contains("ash") || s.Contains("volcan") || s.Contains("burn") || s.Contains("iron"))
                return AdvBiome.Wasteland;
            return AdvBiome.Temperate;
        }

        public static string BiomeName(AdvBiome b)
        {
            switch (b)
            {
                case AdvBiome.North:
                    // Пороги — доли площади биома (AdvTerrainInfo.Pick получает
                    // ранг шума внутри биома, см. SoqMapGenerator.AssignTerrain).
                    if (nearSea && coastFactor > 0.55f && n < 0.22f) return AdvTerrain.ColdGrass;
                    if (n < 0.40f) return AdvTerrain.Snow;           // 40%
                    if (n < 0.58f) return AdvTerrain.ForestPine;     // 18%
                    if (n < 0.80f) return AdvTerrain.ColdGrass;      // 22%
                    if (n < 0.91f) return AdvTerrain.Tundra;         // 11%
                    return AdvTerrain.SnowRock;                       //  9% — снежные скалы

                case AdvBiome.Desert:
                    if (nearSea && coastFactor > 0.6f) return AdvTerrain.Sand;
                    if (n < 0.40f) return AdvTerrain.Sand;           // 40%
                    if (n < 0.62f) return AdvTerrain.Dry;            // 22%
                    if (n < 0.82f) return AdvTerrain.SandRock;       // 20%
                    return AdvTerrain.Rock;                           // 18% — скалы-месасы

                case AdvBiome.Swamp:
                    if (n < 0.35f) return AdvTerrain.Marsh;          // 35%
                    if (n < 0.62f) return AdvTerrain.ForestMix;      // 27%
                    if (n < 0.82f) return AdvTerrain.Dirt;           // 20%
                    return AdvTerrain.Grass;                          // 18%

                case AdvBiome.Highlands:
                    if (n < 0.30f) return AdvTerrain.Rock;           // 30% — горный край
                    if (n < 0.52f) return AdvTerrain.RockGreen;      // 22%
                    if (n < 0.70f) return AdvTerrain.Grass;          // 18%
                    if (n < 0.86f) return AdvTerrain.ForestPine;     // 16%
                    return AdvTerrain.Fields;                         // 14%

                case AdvBiome.Wasteland:
                    if (n < 0.30f) return AdvTerrain.Dirt;           // 30%
                    if (n < 0.52f) return AdvTerrain.Dry;            // 22%
                    if (n < 0.70f) return AdvTerrain.Rock;           // 18%
                    if (n < 0.86f) return AdvTerrain.RockGreen;      // 16%
                    return AdvTerrain.Autumn;                         // 14%

                default: // Temperate — как в SoQ: луга основа, лес — пятнами
                    if (nearSea && coastFactor > 0.6f && n < 0.5f) return AdvTerrain.Sand;
                    if (n < 0.34f) return AdvTerrain.Grass;          // 34% — открытые луга
                    if (n < 0.54f) return AdvTerrain.ForestMix;      // 20% — смешанный лес
                    if (n < 0.64f) return AdvTerrain.Fields;         // 10% — поля
                    if (n < 0.74f) return AdvTerrain.Autumn;         // 10% — осенние рощи
                    if (n < 0.86f) return AdvTerrain.ForestPine;     // 12% — сосны
                    if (n < 0.90f) return AdvTerrain.Marsh;          //  4% — болотца
                    if (n < 0.95f) return AdvTerrain.RockGreen;      //  5% — зелёные скалы
                    return AdvTerrain.Rock;                           //  5% — скалы (горные массивы)
            }
        }
    }
}
