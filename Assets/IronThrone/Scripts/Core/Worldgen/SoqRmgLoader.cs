using System;
using System.Collections.Generic;
using System.IO;
using IronThrone.Core;

namespace IronThrone.Core.Worldgen
{
    /// <summary>
    /// Загрузка данных генератора карт: SoQ-layout'ы, blueprint'ы, каталог объектов
    /// и база нарезки спрайтов. Формат layout/blueprint повторяет официальный
    /// Songs of Conquest (songsofconquest.com/modding/rmg), поэтому сюда можно
    /// положить и настоящий SoQ-файл.
    /// </summary>
    public static class SoqRmgLoader
    {
        public const string LayoutType = "random-map-generator-layout";
        public const string BlueprintType = "random-map-generator-blueprint";

        /// <summary>Загрузить весь набор из папки (все *.json с layout/blueprint/каталогом).</summary>
        public static SoqRmgPack LoadFromDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                var empty = new SoqRmgPack();
                empty.Warnings.Add("Папка генератора карт не найдена: " + dir);
                return empty;
            }

            var files = Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            var documents = new List<KeyValuePair<string, string>>();
            foreach (var file in files)
            {
                try
                {
                    documents.Add(new KeyValuePair<string, string>(Path.GetFileName(file), File.ReadAllText(file)));
                }
                catch (Exception ex)
                {
                    var failed = new SoqRmgPack();
                    failed.Warnings.Add(Path.GetFileName(file) + ": не прочитан — " + ex.Message);
                    return failed;
                }
            }
            return LoadFromDocuments(documents);
        }

        /// <summary>
        /// Загрузить набор из уже прочитанных документов (Unity: Resources/TextAsset,
        /// headless: файлы). Имя документа используется только в сообщениях.
        /// </summary>
        public static SoqRmgPack LoadFromDocuments(IEnumerable<KeyValuePair<string, string>> documents)
        {
            var pack = new SoqRmgPack();
            if (documents == null) return pack;

            foreach (var doc in documents)
            {
                JsonValue root;
                try
                {
                    root = JsonValue.Parse(doc.Value);
                }
                catch (Exception ex)
                {
                    pack.Warnings.Add(doc.Key + ": ошибка JSON — " + ex.Message);
                    continue;
                }

                foreach (var item in EnumerateDocuments(root))
                {
                    string type = item["type"].AsString;
                    if (type == LayoutType) pack.Layouts.Add(ParseLayout(item, doc.Key, pack.Warnings));
                    else if (type == BlueprintType) pack.Blueprints.Add(ParseBlueprint(item, pack.Warnings));
                    else if (item.Has("objects") && item["objects"].Kind == JsonKind.Array) ParseCatalog(item, pack.Catalog, pack.Warnings);
                    else if (item.Has("sprites") || item.Has("atlases")) ParseSprites(item, pack.Catalog, pack.Warnings);
                }
            }

            pack.Catalog.Index();
            pack.Layouts.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return pack;
        }

        private static IEnumerable<JsonValue> EnumerateDocuments(JsonValue root)
        {
            if (root == null) yield break;
            if (root.Kind == JsonKind.Array)
            {
                foreach (var item in root.AsArray)
                    if (item != null && item.Kind == JsonKind.Object) yield return item;
                yield break;
            }
            if (root.Kind == JsonKind.Object) yield return root;
        }

        // ------------------------------------------------------------------ layout
        public static SoqLayout ParseLayout(JsonValue v, string source, List<string> warnings)
        {
            var layout = new SoqLayout
            {
                type = v["type"].AsString,
                id = v["id"].AsString,
                name = v["name"].AsString,
                description = v["description"].AsString,
                blueprint = v["blueprint"].AsString,
                playerCount = Int(v, "playerCount", 2),
                size = Int(v, "size", 64),
                randomizeStartingPositions = Bool(v, "randomizeStartingPositions", true),
                hostileRestrictions = Bool(v, "hostileRestrictions", true),
                competitiveGroupedRegions = Bool(v, "competitiveGroupedRegions", true),
            };
            var modes = v["gameMode"];
            if (modes.Kind == JsonKind.Array)
                foreach (var m in modes.AsArray) layout.gameMode.Add(m.AsInt);

            var subs = v["layouts"];
            if (subs.Kind == JsonKind.Array)
            {
                foreach (var s in subs.AsArray)
                    if (s != null && s.Kind == JsonKind.Object) layout.layouts.Add(ParseSublayout(s, warnings));
            }
            // допускаем «плоский» файл: узлы прямо в корне
            if (layout.layouts.Count == 0 && v["nodes"].Kind == JsonKind.Array)
                layout.layouts.Add(ParseSublayout(v, warnings));

            if (layout.layouts.Count == 0)
                warnings.Add((source) + ": layout без узлов — пропущен");
            if (string.IsNullOrEmpty(layout.name) && string.IsNullOrEmpty(layout.id))
                layout.name = Path.GetFileNameWithoutExtension(source);
            if (string.IsNullOrEmpty(layout.id))
                layout.id = Slugify(layout.name);
            if (layout.size < 16) layout.size = 16;
            if (layout.size > 128) layout.size = 128;
            if (layout.playerCount < 1) layout.playerCount = 1;
            return layout;
        }

        private static SoqSublayout ParseSublayout(JsonValue v, List<string> warnings)
        {
            var sub = new SoqSublayout
            {
                name = v["name"].AsString,
                randomizeStartingPositions = Bool(v, "randomizeStartingPositions", true),
            };
            var nodes = v["nodes"];
            if (nodes.Kind == JsonKind.Array)
                foreach (var n in nodes.AsArray)
                {
                    if (n == null || n.Kind != JsonKind.Object) continue;
                    var node = ParseNode(n, warnings);
                    sub.nodes.Add(node);
                }
            var conns = v["connections"];
            if (conns.Kind == JsonKind.Array)
                foreach (var c in conns.AsArray)
                {
                    if (c == null || c.Kind != JsonKind.Object) continue;
                    sub.connections.Add(ParseConnection(c));
                }
            return sub;
        }

        public static SoqNode ParseNode(JsonValue v, List<string> warnings)
        {
            var node = new SoqNode
            {
                id = Int(v, "id", 0),
                comment = v["comment"].AsString,
                fill = Float(v, "fill", 0f),
                player = Int(v, "player", 0),
                group = Int(v, "group", 0),
                isClaimed = Bool(v, "isClaimed", false),
                difficultyModifier = Float(v, "difficultyModifier", 1f),
                largeCenterArea = Bool(v, "largeCenterArea", false),
                spawnChance = Float(v, "spawnChance", 1f),
                parentGrowthBeforeBirth = Float(v, "parentGrowthBeforeBirth", 1f),
                road = Int(v, "road", 0),
                guardChance = Float(v, "guardChance", 0f),
                biome = v["biome"].AsString,
                displayName = v["displayName"].AsString,
            };
            ReadStringList(v["region_identifier"], node.region_identifier);
            ReadStringList(v["primary_building"], node.primary_building);
            ReadStringList(v["guardTier"], node.guardTierList);
            if (node.guardTierList.Count > 0) node.guardTier = node.guardTierList[0];

            var diff = v["difficulty"];
            if (diff.Kind == JsonKind.Array && diff.Count > 0)
            {
                node.hasDifficulty = true;
                node.difficultyMin = (float)diff[0].AsNumber;
                node.difficultyMax = diff.Count > 1 ? (float)diff[1].AsNumber : node.difficultyMin;
            }
            else if (diff.Kind == JsonKind.Number)
            {
                node.hasDifficulty = true;
                node.difficultyMin = node.difficultyMax = (float)diff.AsNumber;
            }

            var point = v["point"];
            if (point.Kind == JsonKind.Object)
            {
                ReadFloatList(point["x"], node.point.x);
                ReadFloatList(point["y"], node.point.y);
            }

            var children = v["children"];
            if (children.Kind == JsonKind.Array)
                foreach (var c in children.AsArray)
                {
                    if (c == null || c.Kind != JsonKind.Object) continue;
                    var child = ParseNode(c, warnings);
                    child.Parent = node;
                    node.children.Add(child);
                }
            return node;
        }

        private static SoqConnection ParseConnection(JsonValue v)
        {
            var conn = new SoqConnection
            {
                group = Int(v, "group", 0),
                nodeFrom = Int(v, "nodeFrom", 0),
                nodeTo = Int(v, "nodeTo", 0),
                guardChance = Float(v, "guardChance", 0f),
                road = Int(v, "road", 1),
                wide = Bool(v, "wide", false),
            };
            var tiers = new List<string>();
            ReadStringList(v["guardTier"], tiers);
            if (tiers.Count > 0) conn.guardTier = tiers[0];
            var diff = v["difficulty"];
            if (diff.Kind == JsonKind.Array && diff.Count > 0)
            {
                conn.hasDifficulty = true;
                conn.difficultyMin = (float)diff[0].AsNumber;
                conn.difficultyMax = diff.Count > 1 ? (float)diff[1].AsNumber : conn.difficultyMin;
            }
            return conn;
        }

        // ------------------------------------------------------------------ blueprint
        public static SoqBlueprint ParseBlueprint(JsonValue v, List<string> warnings)
        {
            var bp = new SoqBlueprint
            {
                type = v["type"].AsString,
                name = v["name"].AsString,
            };
            var res = v["startingResources"];
            if (res.Kind == JsonKind.Object)
            {
                bp.startingResources = new SoqStartingResources
                {
                    gold = Int(res, "gold", 5000),
                    wood = Int(res, "wood", 5),
                    stone = Int(res, "stone", 5),
                    ancientAmber = Int(res, "ancientAmber", 0),
                    glimmerWeave = Int(res, "glimmerWeave", 0),
                    celestialOre = Int(res, "celestialOre", 0),
                };
            }
            var regions = v["regions"];
            if (regions.Kind == JsonKind.Array)
                foreach (var r in regions.AsArray)
                {
                    if (r == null || r.Kind != JsonKind.Object) continue;
                    bp.regions.Add(ParseBlueprintRegion(r, warnings));
                }
            if (string.IsNullOrEmpty(bp.name)) bp.name = "default";
            return bp;
        }

        private static SoqBlueprintRegion ParseBlueprintRegion(JsonValue v, List<string> warnings)
        {
            var region = new SoqBlueprintRegion
            {
                identifier = v["identifier"].AsString,
                comment = v["comment"].AsString,
                iterations = Int(v, "iterations", 1),
                shuffle = Bool(v, "shuffle", true),
                fallback = v["fallback"].AsString,
            };
            if (string.IsNullOrEmpty(region.identifier))
            {
                warnings.Add("blueprint: запись региона без identifier — пропущена");
                return region;
            }
            var entries = v["entries"];
            if (entries.Kind == JsonKind.Array)
                foreach (var e in entries.AsArray)
                {
                    if (e == null || e.Kind != JsonKind.Object) continue;
                    region.entries.Add(ParseBlueprintEntry(e, warnings));
                }
            return region;
        }

        private static SoqBlueprintEntry ParseBlueprintEntry(JsonValue v, List<string> warnings)
        {
            var entry = new SoqBlueprintEntry
            {
                comment = v["comment"].AsString,
                type = string.IsNullOrEmpty(v["type"].AsString) ? "random" : v["type"].AsString,
                iterations = Int(v, "iterations", 1),
                count = v["count"],
                distance = Float(v, "distance", 0f),
                difficulty = v["difficulty"],
                guardChance = Float(v, "guardChance", 0f),
            };
            ReadStringList(v["objects"], entry.objects);
            ReadStringList(v["guardTier"], entry.guardTier);
            ReadStringList(v["location"], entry.location);
            if (entry.objects.Count == 0)
                warnings.Add("blueprint: запись без objects (" + (entry.comment ?? "?") + ")");
            var children = v["children"];
            if (children.Kind == JsonKind.Array)
                foreach (var c in children.AsArray)
                {
                    if (c == null || c.Kind != JsonKind.Object) continue;
                    var child = new SoqBlueprintChild { count = c["count"] };
                    ReadStringList(c["objects"], child.objects);
                    entry.children.Add(child);
                }
            return entry;
        }

        // ------------------------------------------------------------------ объекты
        private static void ParseCatalog(JsonValue root, AdventureCatalog catalog, List<string> warnings)
        {
            foreach (var o in root["objects"].AsArray)
            {
                if (o == null || o.Kind != JsonKind.Object) continue;
                var def = new AdvObjectDef
                {
                    id = o["id"].AsString,
                    name = o["name"].AsString,
                    category = string.IsNullOrEmpty(o["category"].AsString) ? "neutral" : o["category"].AsString,
                    sprite = o["sprite"].AsString,
                    placeholder = o["placeholder"].AsString,
                    scale = Float(o, "scale", 1f),
                    offsetY = Float(o, "offsetY", 0f),
                    blocking = Bool(o, "blocking", true),
                    unique = Bool(o, "unique", false),
                    guardTier = string.IsNullOrEmpty(o["guardTier"].AsString) ? "Random" : o["guardTier"].AsString,
                    maxPerMap = Int(o, "maxPerMap", 0),
                    tooltip = o["tooltip"].AsString,
                };
                if (string.IsNullOrEmpty(def.id))
                {
                    warnings.Add("каталог: объект без id — пропущен");
                    continue;
                }
                if (string.IsNullOrEmpty(def.name)) def.name = def.id;
                var variants = o["variants"];
                if (variants.Kind == JsonKind.Array)
                {
                    foreach (var v in variants.AsArray)
                    {
                        if (v == null || v.Kind != JsonKind.Object) continue;
                        var variant = new AdvObjectVariant
                        {
                            biome = v["biome"].AsString,
                            sprite = v["sprite"].AsString,
                        };
                        if (!string.IsNullOrEmpty(variant.sprite)) def.variants.Add(variant);
                    }
                }
                catalog.Objects.Add(def);
            }
        }

        private static void ParseSprites(JsonValue root, AdventureCatalog catalog, List<string> warnings)
        {
            var atlases = root["atlases"];
            if (atlases.Kind == JsonKind.Array)
                foreach (var a in atlases.AsArray)
                {
                    if (a == null || a.Kind != JsonKind.Object) continue;
                    var atlas = new AdventureAtlas
                    {
                        name = a["name"].AsString,
                        file = a["file"].AsString,
                        width = Int(a, "width", 0),
                        height = Int(a, "height", 0),
                        pixelsPerUnit = Float(a, "pixelsPerUnit", 32f),
                    };
                    if (string.IsNullOrEmpty(atlas.name)) continue;
                    catalog.Atlases.Add(atlas);
                }

            var sprites = root["sprites"];
            if (sprites.Kind == JsonKind.Array)
                foreach (var s in sprites.AsArray)
                {
                    if (s == null || s.Kind != JsonKind.Object) continue;
                    var sprite = new AdventureSprite
                    {
                        name = s["name"].AsString,
                        atlas = s["atlas"].AsString,
                        x = Int(s, "x", 0),
                        y = Int(s, "y", 0),
                        w = Int(s, "w", 0),
                        h = Int(s, "h", 0),
                        pivotX = Float(s, "pivotX", 0.5f),
                        pivotY = Float(s, "pivotY", 0f),
                        ppu = Float(s, "ppu", 0f),
                    };
                    if (string.IsNullOrEmpty(sprite.name) || sprite.w <= 0 || sprite.h <= 0)
                    {
                        warnings.Add("спрайты: некорректная запись '" + sprite.name + "'");
                        continue;
                    }
                    catalog.Sprites.Add(sprite);
                }
        }

        // ------------------------------------------------------------------ утилиты
        private static int Int(JsonValue v, string key, int def)
        {
            var j = v[key];
            return j.IsNull ? def : j.AsInt;
        }

        private static float Float(JsonValue v, string key, float def)
        {
            var j = v[key];
            return j.IsNull ? def : (float)j.AsNumber;
        }

        private static bool Bool(JsonValue v, string key, bool def)
        {
            var j = v[key];
            return j.IsNull ? def : j.AsBool;
        }

        private static void ReadStringList(JsonValue v, List<string> target)
        {
            if (v == null || v.IsNull) return;
            if (v.Kind == JsonKind.Array)
            {
                foreach (var item in v.AsArray)
                {
                    var s = item.AsString;
                    if (!string.IsNullOrEmpty(s)) target.Add(s);
                }
                return;
            }
            var single = v.AsString;
            if (!string.IsNullOrEmpty(single)) target.Add(single);
        }

        private static void ReadFloatList(JsonValue v, List<float> target)
        {
            if (v == null || v.IsNull) return;
            if (v.Kind == JsonKind.Array)
            {
                foreach (var item in v.AsArray) target.Add((float)item.AsNumber);
                return;
            }
            target.Add((float)v.AsNumber);
        }

        public static string Slugify(string s)
        {
            if (string.IsNullOrEmpty(s)) return "layout";
            var sb = new System.Text.StringBuilder();
            foreach (char c in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == ' ' || c == '-' || c == '_') sb.Append('_');
            }
            return sb.ToString().Trim('_');
        }
    }

    /// <summary>Все данные генератора: layout'ы, blueprint'ы, каталог объектов и спрайтов.</summary>
    public sealed class SoqRmgPack
    {
        public readonly List<SoqLayout> Layouts = new List<SoqLayout>();
        public readonly List<SoqBlueprint> Blueprints = new List<SoqBlueprint>();
        public readonly AdventureCatalog Catalog = new AdventureCatalog();
        public readonly List<string> Warnings = new List<string>();

        public SoqLayout Layout(string key)
        {
            if (string.IsNullOrEmpty(key)) return Layouts.Count > 0 ? Layouts[0] : null;
            for (int i = 0; i < Layouts.Count; i++)
            {
                var l = Layouts[i];
                if (string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(l.name, key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(l.id, key, StringComparison.OrdinalIgnoreCase))
                    return l;
            }
            return Layouts.Count > 0 ? Layouts[0] : null;
        }

        public SoqBlueprint Blueprint(string name)
        {
            if (Blueprints.Count == 0) return null;
            if (string.IsNullOrEmpty(name)) return Blueprints[0];
            for (int i = 0; i < Blueprints.Count; i++)
                if (string.Equals(Blueprints[i].name, name, StringComparison.OrdinalIgnoreCase)) return Blueprints[i];
            return Blueprints[0];
        }

        public string[] LayoutKeys()
        {
            var keys = new string[Layouts.Count];
            for (int i = 0; i < Layouts.Count; i++) keys[i] = Layouts[i].Key;
            return keys;
        }
    }
}
