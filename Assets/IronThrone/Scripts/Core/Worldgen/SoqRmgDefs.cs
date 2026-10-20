using System;
using System.Collections.Generic;
using IronThrone.Core;

namespace IronThrone.Core.Worldgen
{
    /// <summary>
    /// Модель layout-файла Songs of Conquest (random-map-generator-layout).
    /// Поля и имена совпадают с JSON игры, поэтому в проект можно положить
    /// готовый SoQ-layout, и он будет прочитан. Дополнительно поддерживаются
    /// расширения: "id" у layout и "biome" у узла.
    ///
    /// Иерархия: layout -> sublуayout(s) -> узлы (регионы) -> дочерние регионы.
    /// Узлы соединяются connections (дороги, охрана).
    /// </summary>
    public sealed class SoqLayout
    {
        public string type;
        /// <summary>Наш идентификатор (в SoQ его нет; если нет — берём из name).</summary>
        public string id;
        public string name;
        public string description;
        /// <summary>Имя blueprint из того же набора данных.</summary>
        public string blueprint;
        public int playerCount = 2;
        /// <summary>Сторона карты в тайлах (в SoQ: 40/64/128/160/256).</summary>
        public int size = 64;
        public bool randomizeStartingPositions = true;
        public List<int> gameMode = new List<int>();
        /// <summary>Ограничивать ли спавн врагов у городов/поселений.</summary>
        public bool hostileRestrictions = true;
        public bool competitiveGroupedRegions = true;

        public readonly List<SoqSublayout> layouts = new List<SoqSublayout>();

        /// <summary>Ключ для выбора layout'а в UI.</summary>
        public string Key => string.IsNullOrEmpty(id) ? name : id;

        public string DisplayName => string.IsNullOrEmpty(name) ? Key : name;
    }

    /// <summary>Саблayout: один из вариантов карты внутри layout'а (для разнообразия).</summary>
    public sealed class SoqSublayout
    {
        public string name;
        public bool randomizeStartingPositions = true;
        public readonly List<SoqNode> nodes = new List<SoqNode>();
        public readonly List<SoqConnection> connections = new List<SoqConnection>();

        public string DisplayName => string.IsNullOrEmpty(name) ? "Вариант" : name;
    }

    /// <summary>Узел layout'а — регион карты.</summary>
    public sealed class SoqNode
    {
        public int id;
        public string comment;
        /// <summary>Относительное положение (доли карты), диапазон для рандомизации.</summary>
        public SoqPoint point = new SoqPoint();
        /// <summary>Доля суши, которую занимает регион (0..1); 0 — «одна площадка».</summary>
        public float fill;
        /// <summary>Игрок-владелец (1..N), 0 — ничейный.</summary>
        public int player;
        /// <summary>Группа: регионы одной группы синхронизируются по размеру.</summary>
        public int group;
        public readonly List<string> region_identifier = new List<string>();
        /// <summary>Главное строение в центре (AgnosticTown/GoldMine/Beacon/...).</summary>
        public readonly List<string> primary_building = new List<string>();
        public bool isClaimed;
        public bool hasDifficulty;
        public float difficultyMin;
        public float difficultyMax;
        public float difficultyModifier = 1f;
        public bool largeCenterArea;
        /// <summary>Шанс появления региона (0..1).</summary>
        public float spawnChance = 1f;
        /// <summary>Сколько должна вырасти родительская область до рождения этой.</summary>
        public float parentGrowthBeforeBirth = 1f;
        /// <summary>Тип дороги к родителю: 0 — нет, 1 — грунтовка, 2 — мостовая.</summary>
        public int road;
        public float guardChance;
        public string guardTier = "Random";
        /// <summary>Список tier'ов охраны (SoQ допускает несколько).</summary>
        public readonly List<string> guardTierList = new List<string>();
        /// <summary>Расширение: биом региона (north/desert/swamp/highlands/wasteland/temperate).</summary>
        public string biome;
        /// <summary>Расширение: имя региона для UI.</summary>
        public string displayName;

        public readonly List<SoqNode> children = new List<SoqNode>();

        public string Identifier => region_identifier.Count > 0 ? region_identifier[0] : "default";
        public string PrimaryBuilding => primary_building.Count > 0 ? primary_building[0] : null;
        public SoqNode Parent;
    }

    /// <summary>Связь регионов: дорога и/или охраняемый проход.</summary>
    public sealed class SoqConnection
    {
        public int group;
        public int nodeFrom;
        public int nodeTo;
        public float guardChance;
        public bool hasDifficulty;
        public float difficultyMin;
        public float difficultyMax;
        public string guardTier = "Random";
        /// <summary>0 — без дороги, 1 — грунтовка, 2 — мостовая.</summary>
        public int road = 1;
        public bool wide;
    }

    /// <summary>Диапазон координат узла (x: [0..1], y: [0..1]).</summary>
    public sealed class SoqPoint
    {
        public readonly List<float> x = new List<float>();
        public readonly List<float> y = new List<float>();

        public float XMin => x.Count > 0 ? Math.Min(x[0], x[x.Count - 1]) : 0.5f;
        public float XMax => x.Count > 0 ? Math.Max(x[0], x[x.Count - 1]) : 0.5f;
        public float YMin => y.Count > 0 ? Math.Min(y[0], y[y.Count - 1]) : 0.5f;
        public float YMax => y.Count > 0 ? Math.Max(y[0], y[y.Count - 1]) : 0.5f;

        public bool HasRange => x.Count > 0 && y.Count > 0;
    }

    /// <summary>
    /// Blueprint Songs of Conquest: что именно спавнится в регионе с данным
    /// идентификатором, сколько раз, с какой охраной и на каком удалении.
    /// </summary>
    public sealed class SoqBlueprint
    {
        public string type;
        public string name;
        public SoqStartingResources startingResources;
        public readonly List<SoqBlueprintRegion> regions = new List<SoqBlueprintRegion>();

        public SoqBlueprintRegion Region(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;
            for (int i = 0; i < regions.Count; i++)
                if (string.Equals(regions[i].identifier, identifier, StringComparison.OrdinalIgnoreCase))
                    return regions[i];
            return null;
        }
    }

    public sealed class SoqStartingResources
    {
        public int gold = 5000;
        public int wood = 5;
        public int stone = 5;
        public int ancientAmber;
        public int glimmerWeave;
        public int celestialOre;
    }

    public sealed class SoqBlueprintRegion
    {
        public string identifier;
        public string comment;
        public int iterations = 1;
        public bool shuffle = true;
        public string fallback;
        public readonly List<SoqBlueprintEntry> entries = new List<SoqBlueprintEntry>();
    }

    /// <summary>Запись blueprint'а: набор объектов + условия их появления.</summary>
    public sealed class SoqBlueprintEntry
    {
        public string comment;
        public readonly List<string> objects = new List<string>();
        /// <summary>random — любые из списка, synced — одинаковые в группе регионов.</summary>
        public string type = "random";
        public int iterations = 1;
        /// <summary>Сколько объектов за итерацию: число или [min,max].</summary>
        public JsonValue count;
        /// <summary>Минимальное удаление от центра региона (0..1 радиуса).</summary>
        public float distance;
        /// <summary>Сила охраны: число или [min,max].</summary>
        public JsonValue difficulty;
        public float guardChance;
        public readonly List<string> guardTier = new List<string>();
        public readonly List<string> location = new List<string>();
        public readonly List<SoqBlueprintChild> children = new List<SoqBlueprintChild>();

        public int CountMin => RangeValue(count, 1, 1);
        public int CountMax => RangeValue(count, 1, 1);

        public float DifficultyMin => RangeFloat(difficulty, 0f, 0f);
        public float DifficultyMax => RangeFloat(difficulty, 0f, 0f);

        private static int RangeValue(JsonValue v, int defMin, int defMax)
        {
            if (v == null || v.IsNull) return defMin;
            if (v.Kind == JsonKind.Array)
            {
                if (v.Count == 0) return defMin;
                if (v.Count == 1) return v[0].AsInt;
                return v[1].AsInt;
            }
            return v.AsInt;
        }

        private static float RangeFloat(JsonValue v, float defMin, float defMax)
        {
            if (v == null || v.IsNull) return defMin;
            if (v.Kind == JsonKind.Array)
            {
                if (v.Count == 0) return defMin;
                if (v.Count == 1) return (float)v[0].AsNumber;
                return (float)v[1].AsNumber;
            }
            return (float)v.AsNumber;
        }
    }

    /// <summary>Дочерний объект записи (например, ресурсы внутри шахты).</summary>
    public sealed class SoqBlueprintChild
    {
        public readonly List<string> objects = new List<string>();
        public JsonValue count;
    }
}
