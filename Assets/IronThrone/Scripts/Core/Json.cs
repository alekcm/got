using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IronThrone.Core
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// Минималистичный JSON-парсер без внешних зависимостей.
    /// Используется и в Unity, и в headless-тестах.
    /// </summary>
    public sealed class JsonValue
    {
        public JsonKind Kind { get; private set; }
        private bool _bool;
        private double _number;
        private string _string;
        private List<JsonValue> _array;
        private Dictionary<string, JsonValue> _object;

        public static readonly JsonValue Null = new JsonValue { Kind = JsonKind.Null };

        public bool IsNull => Kind == JsonKind.Null;

        public bool AsBool => Kind == JsonKind.Bool ? _bool : Kind == JsonKind.Number ? _number != 0 : false;
        public double AsNumber => Kind == JsonKind.Number ? _number : Kind == JsonKind.Bool ? (_bool ? 1 : 0) : 0;
        public int AsInt => (int)Math.Round(AsNumber);
        public string AsString => Kind == JsonKind.String ? _string : Kind == JsonKind.Number ? _number.ToString(CultureInfo.InvariantCulture) : Kind == JsonKind.Bool ? (_bool ? "true" : "false") : "";

        public List<JsonValue> AsArray => _array ?? (_array = new List<JsonValue>());
        public Dictionary<string, JsonValue> AsObject => _object ?? (_object = new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase));
        public int Count => Kind == JsonKind.Array ? AsArray.Count : Kind == JsonKind.Object ? AsObject.Count : 0;

        public JsonValue this[string key]
        {
            get
            {
                if (Kind != JsonKind.Object) return Null;
                return AsObject.TryGetValue(key, out var v) ? v : Null;
            }
        }

        public JsonValue this[int index]
        {
            get
            {
                if (Kind != JsonKind.Array) return Null;
                return index >= 0 && index < AsArray.Count ? AsArray[index] : Null;
            }
        }

        public bool Has(string key) => Kind == JsonKind.Object && AsObject.ContainsKey(key);

        public IEnumerable<JsonValue> Items
        {
            get
            {
                if (Kind == JsonKind.Array) return AsArray;
                if (Kind == JsonKind.Object) return AsObject.Values;
                return Array.Empty<JsonValue>();
            }
        }

        public static JsonValue Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var parser = new Parser(text);
            var value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw new JsonException("Лишние символы после JSON на позиции " + parser.Position);
            return value;
        }

        public static JsonValue FromNumber(double v) => new JsonValue { Kind = JsonKind.Number, _number = v };
        public static JsonValue FromString(string v) => new JsonValue { Kind = JsonKind.String, _string = v };
        public static JsonValue FromBool(bool v) => new JsonValue { Kind = JsonKind.Bool, _bool = v };
        public static JsonValue FromArray(IEnumerable<JsonValue> items) => new JsonValue { Kind = JsonKind.Array, _array = new List<JsonValue>(items) };
        public static JsonValue FromObject(Dictionary<string, JsonValue> obj) => new JsonValue { Kind = JsonKind.Object, _object = obj };

        public override string ToString() => Kind switch
        {
            JsonKind.Null => "null",
            JsonKind.Bool => _bool ? "true" : "false",
            JsonKind.Number => _number.ToString(CultureInfo.InvariantCulture),
            JsonKind.String => _string ?? "",
            JsonKind.Array => "[" + string.Join(",", AsArray) + "]",
            _ => "{" + AsObject.Count + " keys}"
        };

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;
            public Parser(string s) { _s = s; _i = 0; }
            public int Position => _i;
            public bool AtEnd => _i >= _s.Length;

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') { _i++; continue; }
                    // допускаем комментарии // ...
                    if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')
                    {
                        while (_i < _s.Length && _s[_i] != '\n') _i++;
                        continue;
                    }
                    break;
                }
            }

            public JsonValue ParseValue()
            {
                SkipWhitespace();
                if (AtEnd) throw new JsonException("Неожиданный конец JSON");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return new JsonValue { Kind = JsonKind.String, _string = ParseString() };
                    case 't': Expect("true"); return new JsonValue { Kind = JsonKind.Bool, _bool = true };
                    case 'f': Expect("false"); return new JsonValue { Kind = JsonKind.Bool, _bool = false };
                    case 'n': Expect("null"); return Null;
                    default: return ParseNumber();
                }
            }

            private JsonValue ParseObject()
            {
                var dict = new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase);
                _i++; // {
                SkipWhitespace();
                if (!AtEnd && _s[_i] == '}') { _i++; return new JsonValue { Kind = JsonKind.Object, _object = dict }; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != '"') throw new JsonException("Ожидался ключ объекта на позиции " + _i);
                    string key = ParseString();
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != ':') throw new JsonException("Ожидалось ':' на позиции " + _i);
                    _i++;
                    dict[key] = ParseValue();
                    SkipWhitespace();
                    if (AtEnd) throw new JsonException("Незакрытый объект JSON");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; break; }
                    throw new JsonException("Ожидалось ',' или '}' на позиции " + _i);
                }
                return new JsonValue { Kind = JsonKind.Object, _object = dict };
            }

            private JsonValue ParseArray()
            {
                var list = new List<JsonValue>();
                _i++; // [
                SkipWhitespace();
                if (!AtEnd && _s[_i] == ']') { _i++; return new JsonValue { Kind = JsonKind.Array, _array = list }; }
                while (true)
                {
                    list.Add(ParseValue());
                    SkipWhitespace();
                    if (AtEnd) throw new JsonException("Незакрытый массив JSON");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; break; }
                    throw new JsonException("Ожидалось ',' или ']' на позиции " + _i);
                }
                return new JsonValue { Kind = JsonKind.Array, _array = list };
            }

            private string ParseString()
            {
                var sb = new StringBuilder();
                _i++; // "
                while (_i < _s.Length)
                {
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (_i >= _s.Length) break;
                        char e = _s[_i++];
                        switch (e)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (_i + 4 <= _s.Length)
                                {
                                    sb.Append((char)Convert.ToInt32(_s.Substring(_i, 4), 16));
                                    _i += 4;
                                }
                                break;
                            default: sb.Append(e); break;
                        }
                        continue;
                    }
                    sb.Append(c);
                }
                throw new JsonException("Незакрытая строка JSON");
            }

            private JsonValue ParseNumber()
            {
                int start = _i;
                if (!AtEnd && (_s[_i] == '-' || _s[_i] == '+')) _i++;
                while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.' || _s[_i] == 'e' || _s[_i] == 'E' || _s[_i] == '-' || _s[_i] == '+')) _i++;
                string token = _s.Substring(start, _i - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    throw new JsonException("Некорректное число '" + token + "' на позиции " + start);
                return new JsonValue { Kind = JsonKind.Number, _number = d };
            }

            private void Expect(string token)
            {
                if (_i + token.Length > _s.Length || string.CompareOrdinal(_s, _i, token, 0, token.Length) != 0)
                    throw new JsonException("Ожидалось '" + token + "' на позиции " + _i);
                _i += token.Length;
            }
        }
    }

    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }
}
