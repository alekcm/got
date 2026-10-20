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
            1.36f, 1.45f, 1.69f, 1.62f, 1.67f, 1.00f,
            1.22f, 1.08f, 1.26f, 1.69f, 1.46f, 0.95f,
            1.32f, 1.70f, 1.48f, 1.70f,
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
                case AdvBiome.North: return "Север";
                case AdvBiome.Desert: return "Пустошь";
                case AdvBiome.Swamp: return "Топи";
                case AdvBiome.Highlands: return "Горы";
                case AdvBiome.Wasteland: return "Пепельные земли";
                default: return "Умеренный край";
            }
        }

        // ------------------------------------------------------------------ генерация рельефа
        /// <summary>
        /// Выбор класса рельефа для тайла: биом задаёт веса, шум делает пятна
        /// (лес массивом, пески полосами), берег получает песок/сушь.
        /// </summary>
        public static AdvTerrain Pick(AdvBiome biome, float patchNoise, float coastFactor, bool nearSea, AdvTerrain? forced)
        {
            if (forced.HasValue) return forced.Value;
            float n = patchNoise;
            switch (biome)
            {
                case AdvBiome.North:
                    // снег и сосны — основа севера; снежные скалы — редкая деталь,
                    // иначе регион превращается в белую «рябь» (текстура 094_snow_rock)
                    if (nearSea && coastFactor > 0.55f && n < 0.30f) return AdvTerrain.ColdGrass;
                    if (n < 0.45f) return AdvTerrain.Snow;
                    if (n < 0.62f) return AdvTerrain.ForestPine;
                    if (n < 0.80f) return AdvTerrain.ColdGrass;
                    if (n < 0.92f) return AdvTerrain.Tundra;
                    return AdvTerrain.SnowRock;

                case AdvBiome.Desert:
                    if (nearSea && coastFactor > 0.6f) return AdvTerrain.Sand;
                    if (n < 0.40f) return AdvTerrain.Sand;
                    if (n < 0.62f) return AdvTerrain.Dry;
                    if (n < 0.82f) return AdvTerrain.SandRock;
                    return AdvTerrain.Rock;

                case AdvBiome.Swamp:
                    if (n < 0.34f) return AdvTerrain.Marsh;
                    if (n < 0.62f) return AdvTerrain.ForestMix;
                    if (n < 0.82f) return AdvTerrain.Dirt;
                    return AdvTerrain.Grass;

                case AdvBiome.Highlands:
                    if (n < 0.30f) return AdvTerrain.Rock;
                    if (n < 0.52f) return AdvTerrain.RockGreen;
                    if (n < 0.70f) return AdvTerrain.Grass;
                    if (n < 0.86f) return AdvTerrain.ForestPine;
                    return AdvTerrain.Fields;

                case AdvBiome.Wasteland:
                    if (n < 0.30f) return AdvTerrain.Dirt;
                    if (n < 0.52f) return AdvTerrain.Dry;
                    if (n < 0.70f) return AdvTerrain.Rock;
                    if (n < 0.86f) return AdvTerrain.RockGreen;
                    return AdvTerrain.Autumn;

                default: // Temperate
                    if (nearSea && coastFactor > 0.6f && n < 0.5f) return AdvTerrain.Sand;
                    // луга и лес — основа; поля/осень/болото — редкие вкрапления
                    if (n < 0.40f) return AdvTerrain.Grass;
                    if (n < 0.64f) return AdvTerrain.ForestMix;
                    if (n < 0.76f) return AdvTerrain.Fields;
                    if (n < 0.86f) return AdvTerrain.Autumn;
                    if (n < 0.93f) return AdvTerrain.ForestPine;
                    if (n < 0.97f) return AdvTerrain.Marsh;
                    return AdvTerrain.RockGreen;
            }
        }
    }
}
