using System;
using System.Collections.Generic;
using IronThrone.Core;

namespace IronThrone.Core.Worldgen
{
    /// <summary>
    /// Описание объекта карты приключений (аналог записи из blueprint SoQ):
    /// что рисовать, в какой категории, занимает ли тайл, как называется.
    /// Данные лежат в <c>Resources/IronThroneData/soq_objects.json</c>.
    /// </summary>
    public sealed class AdvObjectDef
    {
        /// <summary>Идентификатор объекта (как в SoQ: GoldMine, TreasureChestLevel1...).</summary>
        public string id;
        public string name;
        /// <summary>Категория: settlement, resource, dwelling, treasure, neutral, objective, guard.</summary>
        public string category = "neutral";
        /// <summary>Спрайт: имя нарезки в базе спрайтов (см. soq_sprite_slices.json).</summary>
        public string sprite;
        /// <summary>Если спрайта нет — какой заглушкой рисовать (placeholder_small/medium/large).</summary>
        public string placeholder;
        public float scale = 1f;
        public float offsetY;
        /// <summary>Занимает тайл и не даёт ставить объекты вплотную.</summary>
        public bool blocking;
        /// <summary>Единственный на карту.</summary>
        public bool unique;
        /// <summary>Уровень охраны по умолчанию (Low/Medium/High/Random).</summary>
        public string guardTier = "Random";
        /// <summary>Сколько таких объектов максимум на карту (0 — без ограничения).</summary>
        public int maxPerMap;
        /// <summary>Подсказка в интерфейсе.</summary>
        public string tooltip;
        /// <summary>Варианты спрайта по биому региона (север/пустыня/топи/...).</summary>
        public readonly List<AdvObjectVariant> variants = new List<AdvObjectVariant>();

        /// <summary>Спрайт для конкретного биома: вариант, иначе основной.</summary>
        public string SpriteForBiome(AdvBiome biome)
        {
            string key = BiomeKey(biome);
            for (int i = 0; i < variants.Count; i++)
                if (string.Equals(variants[i].biome, key, StringComparison.OrdinalIgnoreCase))
                    return variants[i].sprite;
            return sprite;
        }

        public static string BiomeKey(AdvBiome biome)
        {
            switch (biome)
            {
                case AdvBiome.North: return "north";
                case AdvBiome.Desert: return "desert";
                case AdvBiome.Swamp: return "swamp";
                case AdvBiome.Highlands: return "highlands";
                case AdvBiome.Wasteland: return "wasteland";
                default: return "temperate";
            }
        }

        public bool IsGuard => string.Equals(category, "guard", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Вариант спрайта объекта для биома.</summary>
    public sealed class AdvObjectVariant
    {
        public string biome;
        public string sprite;
    }

    /// <summary>Каталог объектов и спрайтов, собранный из JSON контента.</summary>
    public sealed class AdventureCatalog
    {
        public readonly List<AdvObjectDef> Objects = new List<AdvObjectDef>();
        public readonly Dictionary<string, AdvObjectDef> ById =
            new Dictionary<string, AdvObjectDef>(StringComparer.OrdinalIgnoreCase);

        public readonly List<AdventureSprite> Sprites = new List<AdventureSprite>();
        public readonly Dictionary<string, AdventureSprite> SpriteByName =
            new Dictionary<string, AdventureSprite>(StringComparer.OrdinalIgnoreCase);

        public readonly List<AdventureAtlas> Atlases = new List<AdventureAtlas>();

        public void Index()
        {
            ById.Clear();
            foreach (var o in Objects) if (!string.IsNullOrEmpty(o.id)) ById[o.id] = o;
            SpriteByName.Clear();
            foreach (var s in Sprites) if (!string.IsNullOrEmpty(s.name)) SpriteByName[s.name] = s;
        }

        public AdvObjectDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return ById.TryGetValue(id, out var def) ? def : null;
        }

        public AdventureSprite SpriteOf(AdvObjectDef def)
        {
            return SpriteFor(def, null);
        }

        /// <summary>Спрайт объекта с учётом биома региона.</summary>
        public AdventureSprite SpriteFor(AdvObjectDef def, AdventureRegion region)
        {
            if (def == null) return null;
            string name = region != null ? def.SpriteForBiome(region.biome) : def.sprite;
            if (!string.IsNullOrEmpty(name) && SpriteByName.TryGetValue(name, out var s)) return s;
            if (!string.IsNullOrEmpty(def.placeholder) && SpriteByName.TryGetValue(def.placeholder, out s)) return s;
            return null;
        }

        /// <summary>Заглушка по размеру (мелкая/средняя/крупная) — если арта объекта нет.</summary>
        public AdventureSprite PlaceholderFor(AdvObjectDef def)
        {
            string name = def != null && !string.IsNullOrEmpty(def.placeholder) ? def.placeholder : "PlaceholderBuildingMedium";
            if (SpriteByName.TryGetValue(name, out var s)) return s;
            // имена заглушек в базе нарезок и в старом каталоге различаются префиксом
            string[] fallbacks = { "PlaceholderBuildingMedium", "AdventurePlaceholderBuildingMedium",
                                   "PlaceholderBaseMedium", "PlaceholderBuildingSmall", "PlaceholderBaseSmall" };
            for (int i = 0; i < fallbacks.Length; i++)
                if (SpriteByName.TryGetValue(fallbacks[i], out s)) return s;
            return null;
        }

        public AdventureAtlas Atlas(string name)
        {
            for (int i = 0; i < Atlases.Count; i++)
                if (string.Equals(Atlases[i].name, name, StringComparison.OrdinalIgnoreCase)) return Atlases[i];
            return null;
        }
    }

    /// <summary>Атлас текстур объектов (файл в Resources/Art/SoQ).</summary>
    public sealed class AdventureAtlas
    {
        public string name;
        /// <summary>Имя текстуры в Resources (без расширения).</summary>
        public string file;
        public int width;
        public int height;
        public float pixelsPerUnit = 32f;
    }

    /// <summary>Нарезка спрайта в атласе. Координаты — как в Unity: (x,y) от левого нижнего угла.</summary>
    public sealed class AdventureSprite
    {
        public string name;
        public string atlas;
        public int x;
        public int y;
        public int w;
        public int h;
        public float pivotX = 0.5f;
        public float pivotY = 0f;
        public float ppu = 0f;
    }
}
