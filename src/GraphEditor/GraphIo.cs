using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace GraphEditor
{
    /// <summary>
    /// 极简 JSON 读写器（不依赖任何第三方库，兼容 .NET Framework 4.x）。
    /// 既用于 .graph 自定义格式的读写，也用于对格式做容错解析。
    /// </summary>
    public sealed class JsonValue
    {
        public enum Kind
        {
            Null,
            Bool,
            Number,
            String,
            Array,
            Object
        }

        private readonly Kind _kind;
        private readonly bool _bool;
        private readonly double _number;
        private readonly string _string;
        private readonly List<JsonValue> _items;
        private readonly Dictionary<string, JsonValue> _map;

        private JsonValue(Kind kind)
        {
            _kind = kind;
            if (kind == Kind.Array) _items = new List<JsonValue>();
            if (kind == Kind.Object) _map = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
        }

        private JsonValue(bool value)
        {
            _kind = Kind.Bool;
            _bool = value;
        }

        private JsonValue(double value)
        {
            _kind = Kind.Number;
            _number = value;
        }

        private JsonValue(string value)
        {
            _kind = Kind.String;
            _string = value;
        }

        public Kind ValueKind { get { return _kind; } }

        public static JsonValue NewObject() { return new JsonValue(Kind.Object); }
        public static JsonValue NewArray() { return new JsonValue(Kind.Array); }
        public static JsonValue From(bool value) { return new JsonValue(value); }
        public static JsonValue From(double value) { return new JsonValue(value); }
        public static JsonValue From(string value)
        {
            return value == null ? new JsonValue(Kind.Null) : new JsonValue(value);
        }

        public void Add(string key, JsonValue value)
        {
            if (_kind != Kind.Object) throw new InvalidOperationException("不是 JSON 对象");
            _map[key] = value ?? new JsonValue(Kind.Null);
        }

        public void Add(JsonValue value)
        {
            if (_kind != Kind.Array) throw new InvalidOperationException("不是 JSON 数组");
            _items.Add(value ?? new JsonValue(Kind.Null));
        }

        public bool TryGet(string key, out JsonValue value)
        {
            value = null;
            if (_kind != Kind.Object) return false;
            return _map.TryGetValue(key, out value);
        }

        public JsonValue Get(string key)
        {
            JsonValue v;
            return TryGet(key, out v) ? v : null;
        }

        public IList<JsonValue> Items
        {
            get { return _kind == Kind.Array ? (IList<JsonValue>)_items : new List<JsonValue>(); }
        }

        // ---------- 取值辅助（带默认值，容错） ----------

        public string AsString(string fallback)
        {
            if (_kind == Kind.String) return _string;
            if (_kind == Kind.Number) return _number.ToString(CultureInfo.InvariantCulture);
            if (_kind == Kind.Bool) return _bool ? "true" : "false";
            return fallback;
        }

        public double AsDouble(double fallback)
        {
            if (_kind == Kind.Number) return _number;
            if (_kind == Kind.String)
            {
                double d;
                if (double.TryParse(_string, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            }
            return fallback;
        }

        public int AsInt(int fallback)
        {
            double d = AsDouble(fallback);
            if (double.IsNaN(d) || double.IsInfinity(d)) return fallback;
            return (int)Math.Round(d);
        }

        public bool AsBool(bool fallback)
        {
            if (_kind == Kind.Bool) return _bool;
            if (_kind == Kind.Number) return Math.Abs(_number) > 0.5;
            if (_kind == Kind.String)
            {
                string s = _string.Trim().ToLowerInvariant();
                if (s == "true" || s == "1" || s == "yes") return true;
                if (s == "false" || s == "0" || s == "no") return false;
            }
            return fallback;
        }

        // ---------- 序列化 ----------

        public string ToJson()
        {
            StringBuilder sb = new StringBuilder();
            Write(sb, 0);
            return sb.ToString();
        }

        private void Write(StringBuilder sb, int indent)
        {
            switch (_kind)
            {
                case Kind.Null:
                    sb.Append("null");
                    break;
                case Kind.Bool:
                    sb.Append(_bool ? "true" : "false");
                    break;
                case Kind.Number:
                    sb.Append(FormatNumber(_number));
                    break;
                case Kind.String:
                    WriteString(sb, _string);
                    break;
                case Kind.Array:
                    if (_items.Count == 0) { sb.Append("[]"); break; }
                    sb.Append('[');
                    for (int i = 0; i < _items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        NewLine(sb, indent + 1);
                        _items[i].Write(sb, indent + 1);
                    }
                    NewLine(sb, indent);
                    sb.Append(']');
                    break;
                case Kind.Object:
                    if (_map.Count == 0) { sb.Append("{}"); break; }
                    sb.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, JsonValue> pair in _map)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        NewLine(sb, indent + 1);
                        WriteString(sb, pair.Key);
                        sb.Append(": ");
                        pair.Value.Write(sb, indent + 1);
                    }
                    NewLine(sb, indent);
                    sb.Append('}');
                    break;
            }
        }

        private static void NewLine(StringBuilder sb, int indent)
        {
            sb.Append('\n');
            sb.Append(' ', indent * 2);
        }

        public static string FormatNumber(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
            if (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15)
            {
                return ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
            }
            return d.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        public static JsonValue Parse(string text)
        {
            if (text == null) throw new FormatException("内容为空");
            int pos = 0;
            JsonValue v = ParseValue(text, ref pos);
            SkipWhitespace(text, ref pos);
            if (pos < text.Length) throw new FormatException("JSON 结尾存在多余字符（位置 " + pos + "）");
            return v;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') i++;
                else break;
            }
        }

        private static JsonValue ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 意外结束");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return From(ParseString(s, ref i));
                case 't':
                    Expect(s, ref i, "true");
                    return From(true);
                case 'f':
                    Expect(s, ref i, "false");
                    return From(false);
                case 'n':
                    Expect(s, ref i, "null");
                    return new JsonValue(Kind.Null);
                default:
                    return From(ParseNumber(s, ref i));
            }
        }

        private static JsonValue ParseObject(string s, ref int i)
        {
            JsonValue obj = NewObject();
            i++; // {
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return obj; }
            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("对象键必须是以双引号包围的字符串（位置 " + i + "）");
                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("对象键后缺少 ':'（位置 " + i + "）");
                i++;
                obj.Add(key, ParseValue(s, ref i));
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return obj; }
                throw new FormatException("对象格式错误（位置 " + i + "）");
            }
        }

        private static JsonValue ParseArray(string s, ref int i)
        {
            JsonValue arr = NewArray();
            i++; // [
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return arr; }
            while (true)
            {
                arr.Add(ParseValue(s, ref i));
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return arr; }
                throw new FormatException("数组格式错误（位置 " + i + "）");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            i++; // 起始引号
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("字符串未闭合");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (i >= s.Length) throw new FormatException("转义符未完成");
                    char e = s[i++];
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
                            if (i + 4 > s.Length) throw new FormatException("\\u 转义不完整");
                            int code;
                            if (!int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                throw new FormatException("\\u 转义非法");
                            sb.Append((char)code);
                            i += 4;
                            break;
                        default:
                            throw new FormatException("不支持的转义字符 \\" + e);
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++;
                else break;
            }
            if (start == i) throw new FormatException("期望一个数字（位置 " + start + "）");
            double d;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException("数字格式非法: " + s.Substring(start, i - start));
            return d;
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException("期望 '" + word + "'（位置 " + i + "）");
            i += word.Length;
        }
    }

    /// <summary>.graph 自定义格式（JSON 文本）的读写。</summary>
    public static class GraphIo
    {
        public const string FileExtension = ".graph";
        public const string FileFilter = "图文件 (*.graph)|*.graph|所有文件 (*.*)|*.*";
        public const string CurrentVersion = "1.0";
        public const string FormatId = "GraphEditor.Graph";

        public static string Serialize(GraphCanvas c)
        {
            JsonValue root = JsonValue.NewObject();
            root.Add("format", JsonValue.From(FormatId));
            root.Add("version", JsonValue.From(CurrentVersion));

            JsonValue canvas = JsonValue.NewObject();
            canvas.Add("width", JsonValue.From(c.CanvasWidth));
            canvas.Add("height", JsonValue.From(c.CanvasHeight));
            root.Add("canvas", canvas);

            // 顶点组样式（全局）
            JsonValue vs = JsonValue.NewObject();
            vs.Add("radius", JsonValue.From(c.VertexRadius));
            vs.Add("shape", JsonValue.From(c.VertexShape == VertexShape.Square ? "square" : "circle"));
            vs.Add("defaultColor", JsonValue.From(ColorToHex(c.DefaultVertexColor)));
            vs.Add("naming", JsonValue.From(NamingToToken(c.VertexNaming)));
            vs.Add("labelPlacement", JsonValue.From(VertexPlacementToToken(c.VertexLabelPlacement)));
            root.Add("vertexStyle", vs);

            // 边组样式（全局）
            JsonValue es = JsonValue.NewObject();
            es.Add("width", JsonValue.From(c.EdgeWidth));
            es.Add("lineStyle", JsonValue.From(LineStyleToToken(c.EdgeLineStyle)));
            es.Add("defaultColor", JsonValue.From(ColorToHex(c.DefaultEdgeColor)));
            es.Add("naming", JsonValue.From(NamingToToken(c.EdgeNaming)));
            es.Add("labelPlacement", JsonValue.From(EdgePlacementToToken(c.EdgeLabelPlacement)));
            root.Add("edgeStyle", es);

            JsonValue vertices = JsonValue.NewArray();
            List<Vertex> sortedVertices = new List<Vertex>(c.Vertices);
            sortedVertices.Sort(delegate(Vertex a, Vertex b) { return a.Id.CompareTo(b.Id); });
            foreach (Vertex v in sortedVertices)
            {
                JsonValue item = JsonValue.NewObject();
                item.Add("id", JsonValue.From((double)v.Id));
                item.Add("name", JsonValue.From(v.Name ?? ""));
                item.Add("x", JsonValue.From(v.X));
                item.Add("y", JsonValue.From(v.Y));
                if (v.Color.HasValue) item.Add("color", JsonValue.From(ColorToHex(v.Color.Value)));
                vertices.Add(item);
            }
            root.Add("vertices", vertices);

            JsonValue edges = JsonValue.NewArray();
            List<Edge> sortedEdges = new List<Edge>(c.Edges);
            sortedEdges.Sort(delegate(Edge a, Edge b) { return a.Id.CompareTo(b.Id); });
            foreach (Edge e in sortedEdges)
            {
                JsonValue item = JsonValue.NewObject();
                item.Add("id", JsonValue.From((double)e.Id));
                item.Add("name", JsonValue.From(e.Name ?? ""));
                item.Add("from", JsonValue.From((double)e.V1));
                item.Add("to", JsonValue.From((double)e.V2));
                if (e.Color.HasValue) item.Add("color", JsonValue.From(ColorToHex(e.Color.Value)));
                edges.Add(item);
            }
            root.Add("edges", edges);

            return root.ToJson();
        }

        public static GraphCanvas Deserialize(string text)
        {
            JsonValue root = JsonValue.Parse(text);
            if (root.ValueKind != JsonValue.Kind.Object)
                throw new FormatException("根节点必须是 JSON 对象，这可能不是 GraphEditor 的 .graph 文件。");

            JsonValue format = root.Get("format");
            if (format != null && format.ValueKind == JsonValue.Kind.String
                && !string.Equals(format.AsString(""), FormatId, StringComparison.Ordinal))
            {
                throw new FormatException("文件格式标识不匹配: " + format.AsString(""));
            }

            double width = 1200;
            double height = 800;
            JsonValue canvas = root.Get("canvas");
            if (canvas != null)
            {
                width = canvas.Get("width") != null ? canvas.Get("width").AsDouble(1200) : 1200;
                height = canvas.Get("height") != null ? canvas.Get("height").AsDouble(800) : 800;
            }

            GraphCanvas c = new GraphCanvas(Clamp(width, 100, 10000), Clamp(height, 100, 10000));

            JsonValue vs = root.Get("vertexStyle");
            if (vs != null)
            {
                if (vs.Get("radius") != null) c.VertexRadius = Clamp(vs.Get("radius").AsDouble(18), 4, 200);
                if (vs.Get("shape") != null)
                {
                    c.VertexShape = string.Equals(vs.Get("shape").AsString("circle"), "square", StringComparison.OrdinalIgnoreCase)
                        ? VertexShape.Square : VertexShape.Circle;
                }
                Color vc;
                if (vs.Get("defaultColor") != null && TryParseColor(vs.Get("defaultColor").AsString(""), out vc))
                    c.DefaultVertexColor = vc;
                if (vs.Get("naming") != null)
                {
                    NamingRule rule = TokenToNaming(vs.Get("naming").AsString("alphabetic"));
                    // 「两端点名字」是边专用的；旧文件里若顶点写了它，退化为字母命名
                    c.VertexNaming = GraphCanvas.IsVertexRule(rule) ? rule : NamingRule.Alphabetic;
                }
                if (vs.Get("labelPlacement") != null) c.VertexLabelPlacement = TokenToVertexPlacement(vs.Get("labelPlacement").AsString("inside"));
            }

            JsonValue es = root.Get("edgeStyle");
            if (es != null)
            {
                if (es.Get("width") != null) c.EdgeWidth = Clamp(es.Get("width").AsDouble(2), 0.5, 40);
                if (es.Get("lineStyle") != null) c.EdgeLineStyle = TokenToLineStyle(es.Get("lineStyle").AsString("solid"));
                Color ec;
                if (es.Get("defaultColor") != null && TryParseColor(es.Get("defaultColor").AsString(""), out ec))
                    c.DefaultEdgeColor = ec;
                // 缺省与「新建图」的默认一致：边默认不命名
                if (es.Get("naming") != null) c.EdgeNaming = TokenToNaming(es.Get("naming").AsString("none"));
                if (es.Get("labelPlacement") != null) c.EdgeLabelPlacement = TokenToEdgePlacement(es.Get("labelPlacement").AsString("above"));
            }

            JsonValue vertices = root.Get("vertices");
            if (vertices != null && vertices.ValueKind == JsonValue.Kind.Array)
            {
                foreach (JsonValue item in vertices.Items)
                {
                    if (item.ValueKind != JsonValue.Kind.Object) continue;
                    JsonValue idNode = item.Get("id");
                    if (idNode == null) continue;
                    int id = idNode.AsInt(0);
                    if (id <= 0) continue;
                    double x = item.Get("x") != null ? item.Get("x").AsDouble(0) : 0;
                    double y = item.Get("y") != null ? item.Get("y").AsDouble(0) : 0;
                    string name = item.Get("name") != null ? item.Get("name").AsString("") : "";
                    Color? color = null;
                    Color parsed;
                    if (item.Get("color") != null && TryParseColor(item.Get("color").AsString(""), out parsed))
                        color = parsed;
                    c.PlaceVertex(id, c.ClampX(x), c.ClampY(y), name, color);
                }
            }

            JsonValue edges = root.Get("edges");
            if (edges != null && edges.ValueKind == JsonValue.Kind.Array)
            {
                foreach (JsonValue item in edges.Items)
                {
                    if (item.ValueKind != JsonValue.Kind.Object) continue;
                    JsonValue idNode = item.Get("id");
                    if (idNode == null) continue;
                    int id = idNode.AsInt(0);
                    if (id <= 0) continue;
                    int from = item.Get("from") != null ? item.Get("from").AsInt(0) : 0;
                    int to = item.Get("to") != null ? item.Get("to").AsInt(0) : 0;
                    // 端点不存在时丢弃该边，避免产生悬空引用
                    if (c.FindVertex(from) == null || c.FindVertex(to) == null) continue;
                    string name = item.Get("name") != null ? item.Get("name").AsString("") : "";
                    Color? color = null;
                    Color parsed;
                    if (item.Get("color") != null && TryParseColor(item.Get("color").AsString(""), out parsed))
                        color = parsed;
                    c.PlaceEdge(id, from, to, name, color);
                }
            }

            return c;
        }

        public static string ColorToHex(Color c)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        public static bool TryParseColor(string text, out Color color)
        {
            color = Color.Black;
            if (string.IsNullOrEmpty(text)) return false;
            string s = text.Trim();
            if (s.StartsWith("#", StringComparison.Ordinal)) s = s.Substring(1);
            if (s.Length != 6 && s.Length != 8) return false;
            int value;
            if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return false;
            if (s.Length == 6)
            {
                color = Color.FromArgb((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
            }
            else
            {
                color = Color.FromArgb((value >> 24) & 0xFF, (value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
            }
            return true;
        }

        public static string LineStyleToToken(EdgeLineStyle style)
        {
            switch (style)
            {
                case EdgeLineStyle.Dashed: return "dashed";
                case EdgeLineStyle.Dotted: return "dotted";
                default: return "solid";
            }
        }

        public static string NamingToToken(NamingRule rule)
        {
            switch (rule)
            {
                case NamingRule.None: return "none";
                case NamingRule.Numeric: return "numeric";
                case NamingRule.FromEndpoints: return "fromEndpoints";
                default: return "alphabetic";
            }
        }

        public static NamingRule TokenToNaming(string token)
        {
            if (string.Equals(token, "none", StringComparison.OrdinalIgnoreCase)) return NamingRule.None;
            if (string.Equals(token, "numeric", StringComparison.OrdinalIgnoreCase)) return NamingRule.Numeric;
            if (string.Equals(token, "fromEndpoints", StringComparison.OrdinalIgnoreCase)) return NamingRule.FromEndpoints;
            return NamingRule.Alphabetic;
        }

        public static string VertexPlacementToToken(VertexLabelPlacement placement)
        {
            switch (placement)
            {
                case VertexLabelPlacement.Above: return "above";
                case VertexLabelPlacement.Below: return "below";
                case VertexLabelPlacement.Left: return "left";
                case VertexLabelPlacement.Right: return "right";
                default: return "inside";
            }
        }

        public static VertexLabelPlacement TokenToVertexPlacement(string token)
        {
            if (string.Equals(token, "above", StringComparison.OrdinalIgnoreCase)) return VertexLabelPlacement.Above;
            if (string.Equals(token, "below", StringComparison.OrdinalIgnoreCase)) return VertexLabelPlacement.Below;
            if (string.Equals(token, "left", StringComparison.OrdinalIgnoreCase)) return VertexLabelPlacement.Left;
            if (string.Equals(token, "right", StringComparison.OrdinalIgnoreCase)) return VertexLabelPlacement.Right;
            return VertexLabelPlacement.Inside;
        }

        public static string EdgePlacementToToken(EdgeLabelPlacement placement)
        {
            switch (placement)
            {
                case EdgeLabelPlacement.Inside: return "inside";
                case EdgeLabelPlacement.Below: return "below";
                default: return "above";
            }
        }

        public static EdgeLabelPlacement TokenToEdgePlacement(string token)
        {
            if (string.Equals(token, "inside", StringComparison.OrdinalIgnoreCase)) return EdgeLabelPlacement.Inside;
            if (string.Equals(token, "below", StringComparison.OrdinalIgnoreCase)) return EdgeLabelPlacement.Below;
            return EdgeLabelPlacement.Above;
        }

        public static EdgeLineStyle TokenToLineStyle(string token)
        {
            if (string.Equals(token, "dashed", StringComparison.OrdinalIgnoreCase)) return EdgeLineStyle.Dashed;
            if (string.Equals(token, "dotted", StringComparison.OrdinalIgnoreCase)) return EdgeLineStyle.Dotted;
            return EdgeLineStyle.Solid;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return min;
            return Math.Max(min, Math.Min(max, value));
        }

        // ---------- 文件读写 ----------

        public static void Save(GraphCanvas canvas, string path)
        {
            string text = Serialize(canvas);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        public static GraphCanvas Load(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            return Deserialize(text);
        }
    }
}
