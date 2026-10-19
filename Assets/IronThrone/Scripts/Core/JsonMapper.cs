using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace IronThrone.Core
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class JsonNameAttribute : Attribute
    {
        public string Name { get; }
        public JsonNameAttribute(string name) { Name = name; }
    }

    /// <summary>
    /// Небольшой рефлексивный маппер JSON -> POCO. Поддерживает вложенные объекты,
    /// списки, массивы, словари, перечисления по имени и JsonValue для сырых данных.
    /// </summary>
    public static class JsonMapper
    {
        public static T ToObject<T>(JsonValue value) where T : new() => (T)ToObject(value, typeof(T));

        public static object ToObject(JsonValue value, Type type)
        {
            if (type == typeof(JsonValue)) return value;
            if (value == null || value.IsNull)
            {
                return type.IsValueType && Nullable.GetUnderlyingType(type) == null ? Activator.CreateInstance(type) : null;
            }

            var nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null) type = nullable;

            if (type == typeof(string)) return value.AsString;
            if (type == typeof(bool)) return value.AsBool;
            if (type == typeof(int)) return value.AsInt;
            if (type == typeof(long)) return (long)value.AsNumber;
            if (type == typeof(float)) return (float)value.AsNumber;
            if (type == typeof(double)) return value.AsNumber;
            if (type == typeof(decimal)) return (decimal)value.AsNumber;
            if (type == typeof(short)) return (short)value.AsInt;
            if (type == typeof(byte)) return (byte)value.AsInt;
            if (type.IsEnum)
            {
                if (value.Kind == JsonKind.Number) return Enum.ToObject(type, value.AsInt);
                var s = value.AsString;
                if (string.IsNullOrEmpty(s)) return Activator.CreateInstance(type);
                try { return Enum.Parse(type, s, true); }
                catch { return Activator.CreateInstance(type); }
            }

            if (type.IsArray)
            {
                var elem = type.GetElementType();
                var arr = Array.CreateInstance(elem, value.Count);
                for (int i = 0; i < value.Count; i++) arr.SetValue(ToObject(value[i], elem), i);
                return arr;
            }

            if (typeof(IDictionary).IsAssignableFrom(type) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)))
            {
                var args = type.GetGenericArguments();
                var dict = (IDictionary)Activator.CreateInstance(type);
                foreach (var kv in value.AsObject)
                    dict[kv.Key] = ToObject(kv.Value, args[1]);
                return dict;
            }

            if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>) || type.GetGenericTypeDefinition() == typeof(IList<>)))
            {
                var elem = type.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elem));
                foreach (var item in value.AsArray) list.Add(ToObject(item, elem));
                return list;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
            {
                var elem = type.GetGenericArguments()[0];
                var set = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elem));
                foreach (var item in value.AsArray) set.Add(ToObject(item, elem));
                var ctor = type.GetConstructor(new[] { typeof(IEnumerable<>).MakeGenericType(elem) });
                if (ctor != null) return ctor.Invoke(new object[] { set });
                var inst = Activator.CreateInstance(type);
                var add = type.GetMethod("Add");
                foreach (var item in set) add.Invoke(inst, new[] { item });
                return inst;
            }

            if (value.Kind != JsonKind.Object) return Activator.CreateInstance(type);

            var obj = Activator.CreateInstance(type);
            BindMembers(obj, value, type);
            return obj;
        }

        private static void BindMembers(object obj, JsonValue value, Type type)
        {
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.IsInitOnly) continue;
                var name = f.GetCustomAttribute<JsonNameAttribute>()?.Name ?? f.Name;
                var jv = Find(value, name, f.Name);
                if (jv == null) continue;
                try { f.SetValue(obj, ToObject(jv, f.FieldType)); }
                catch (Exception ex) { throw new JsonException($"Поле {type.Name}.{f.Name}: {ex.Message}"); }
            }
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanWrite) continue;
                var name = p.GetCustomAttribute<JsonNameAttribute>()?.Name ?? p.Name;
                var jv = Find(value, name, p.Name);
                if (jv == null) continue;
                try { p.SetValue(obj, ToObject(jv, p.PropertyType)); }
                catch (Exception ex) { throw new JsonException($"Свойство {type.Name}.{p.Name}: {ex.Message}"); }
            }
        }

        private static JsonValue Find(JsonValue obj, string firstName, string secondName)
        {
            if (obj.Has(firstName)) return obj[firstName];
            if (obj.Has(secondName)) return obj[secondName];
            // tolerate snake_case <-> camelCase
            var snake = ToSnake(firstName);
            if (obj.Has(snake)) return obj[snake];
            foreach (var key in obj.AsObject.Keys)
            {
                if (string.Equals(Normalize(key), Normalize(firstName), StringComparison.OrdinalIgnoreCase))
                    return obj[key];
            }
            return null;
        }

        private static string Normalize(string s) => s.Replace("_", "").Replace("-", "").ToLowerInvariant();

        private static string ToSnake(string s)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsUpper(s[i]))
                {
                    if (i > 0) sb.Append('_');
                    sb.Append(char.ToLowerInvariant(s[i]));
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        public static List<T> ToList<T>(JsonValue array)
        {
            var list = new List<T>();
            foreach (var item in array.AsArray) list.Add((T)ToObject(item, typeof(T)));
            return list;
        }

        public static Dictionary<string, T> ToMap<T>(JsonValue obj)
        {
            var dict = new Dictionary<string, T>();
            foreach (var kv in obj.AsObject) dict[kv.Key] = (T)ToObject(kv.Value, typeof(T));
            return dict;
        }
    }
}
