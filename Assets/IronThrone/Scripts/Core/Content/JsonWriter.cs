using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace IronThrone.Core.Content
{
    /// <summary>
    /// Минимальный JSON-сериализатор по публичным полям. Используется сохранениями:
    /// DTO-классы состоят из простых полей, списков и словарей со строковыми ключами.
    /// </summary>
    public static class JsonWriter
    {
        public static string Write(object value)
        {
            var sb = new StringBuilder(1 << 16);
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object v, int depth)
        {
            if (depth > 32) { sb.Append("null"); return; }
            if (v == null) { sb.Append("null"); return; }

            if (v is string s) { WriteString(sb, s); return; }
            if (v is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (v is Enum e) { sb.Append('"').Append(e.ToString()).Append('"'); return; }
            if (v is int || v is long || v is short || v is byte) { sb.Append(Convert.ToInt64(v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is float f) { sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); return; }
            if (v is double d) { sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return; }
            if (v is decimal m) { sb.Append(m.ToString(CultureInfo.InvariantCulture)); return; }

            if (v is IDictionary dict)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, Convert.ToString(kv.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    WriteValue(sb, kv.Value, depth + 1);
                }
                sb.Append('}');
                return;
            }

            if (v is IEnumerable list)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item, depth + 1);
                }
                sb.Append(']');
                return;
            }

            sb.Append('{');
            bool f2 = true;
            foreach (var field in v.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var fv = field.GetValue(v);
                if (fv == null) continue;
                if (!f2) sb.Append(',');
                f2 = false;
                WriteString(sb, field.Name);
                sb.Append(':');
                WriteValue(sb, fv, depth + 1);
            }
            sb.Append('}');
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (ch < 32) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
