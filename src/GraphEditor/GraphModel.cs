using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;

namespace GraphEditor
{
    /// <summary>顶点形状（全局统一）。</summary>
    public enum VertexShape
    {
        Circle,
        Square
    }

    /// <summary>边线型（全局统一）。</summary>
    public enum EdgeLineStyle
    {
        Solid,
        Dashed,
        Dotted
    }

    /// <summary>
    /// 新建顶点/边时的默认命名规则。同一个规则对顶点与边给出不同的名字：
    ///
    /// | 规则 | 顶点 | 边 |
    /// | --- | --- | --- |
    /// | <see cref="None"/> | 不命名 | 不命名 |
    /// | <see cref="Alphabetic"/> | A、B、C…Z、A1… | a、b、c…z、a1… |
    /// | <see cref="Numeric"/> | V1、V2、V3… | e1、e2、e3… |
    /// | <see cref="FromEndpoints"/> | 退化为 A、B、C… | 两端点名字拼接（如 AB） |
    /// </summary>
    public enum NamingRule
    {
        /// <summary>不命名（名字为空，不显示文字）。</summary>
        None,
        /// <summary>字母：顶点 A、B…Z、A1…；边 a、b…z、a1…。</summary>
        Alphabetic,
        /// <summary>前缀 + 数字：顶点 V1、V2、V3…；边 e1、e2、e3…。</summary>
        Numeric,
        /// <summary>仅边可用：用两端点名字拼接（如 AB）。</summary>
        FromEndpoints
    }

    /// <summary>顶点名字的显示位置。</summary>
    public enum VertexLabelPlacement
    {
        Inside,
        Above,
        Below,
        Left,
        Right
    }

    /// <summary>边名字的显示位置。</summary>
    public enum EdgeLabelPlacement
    {
        Inside,
        Above,
        Below
    }

    /// <summary>浮点矩形，使用图像坐标。</summary>
    public struct RectD
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;

        public RectD(double x, double y, double w, double h)
        {
            X = x;
            Y = y;
            Width = w;
            Height = h;
        }

        public double Right { get { return X + Width; } }
        public double Bottom { get { return Y + Height; } }

        public static RectD FromPoints(double x1, double y1, double x2, double y2)
        {
            double x = Math.Min(x1, x2);
            double y = Math.Min(y1, y2);
            return new RectD(x, y, Math.Abs(x2 - x1), Math.Abs(y2 - y1));
        }

        public bool Contains(double x, double y)
        {
            return x >= X && x <= Right && y >= Y && y <= Bottom;
        }

        public bool IntersectsWith(RectD other)
        {
            return !(other.X > Right || other.Right < X || other.Y > Bottom || other.Bottom < Y);
        }
    }

    /// <summary>一个顶点。</summary>
    public sealed class Vertex
    {
        public int Id;
        public double X;
        public double Y;
        public string Name;
        /// <summary>该顶点自身的颜色；为空表示使用画布默认顶点颜色。</summary>
        public Color? Color;

        public Vertex(int id, double x, double y, string name)
        {
            Id = id;
            X = x;
            Y = y;
            Name = name;
        }
    }

    /// <summary>一条边。</summary>
    public sealed class Edge
    {
        public int Id;
        public int V1;
        public int V2;
        public string Name;
        /// <summary>该边自身的颜色；为空表示使用画布默认边颜色。</summary>
        public Color? Color;

        public Edge(int id, int v1, int v2, string name)
        {
            Id = id;
            V1 = v1;
            V2 = v2;
            Name = name;
        }

        public bool IsLoop { get { return V1 == V2; } }
    }

    /// <summary>
    /// 图的画布数据：画布尺寸、两组全局样式（顶点样式 / 边样式）以及图元素。
    /// 顶点之间大小与样式完全相同，边之间大小与样式完全相同；只有名字和颜色可以逐个不同。
    /// </summary>
    public sealed class GraphCanvas
    {
        public double CanvasWidth;
        public double CanvasHeight;

        // 顶点样式（全局，作用于所有顶点）
        public double VertexRadius = 18.0;
        public VertexShape VertexShape = VertexShape.Circle;
        public Color DefaultVertexColor = Color.FromArgb(0x2D, 0x7D, 0xD2);

        // 边样式（全局，作用于所有边）
        public double EdgeWidth = 2.0;
        public EdgeLineStyle EdgeLineStyle = EdgeLineStyle.Solid;
        public Color DefaultEdgeColor = Color.FromArgb(0x40, 0x40, 0x40);

        // 命名规则与名字显示位置（全局）
        public NamingRule VertexNaming = NamingRule.Alphabetic;
        public NamingRule EdgeNaming = NamingRule.None;
        public VertexLabelPlacement VertexLabelPlacement = VertexLabelPlacement.Inside;
        public EdgeLabelPlacement EdgeLabelPlacement = EdgeLabelPlacement.Above;

        private readonly List<Vertex> _vertices = new List<Vertex>();
        private readonly List<Edge> _edges = new List<Edge>();
        private readonly Dictionary<int, Vertex> _vertexById = new Dictionary<int, Vertex>();
        private int _nextVertexId = 1;
        private int _nextEdgeId = 1;

        /// <summary>
        /// 画布是无限平面，本身没有尺寸。两个尺寸字段只为兼容旧代码/旧文件而保留，
        /// 不再参与绘制或命名计算 —— 新建时统一取这个占位值。
        /// </summary>
        public GraphCanvas()
            : this(1200, 800)
        {
        }

        public GraphCanvas(double width, double height)
        {
            CanvasWidth = width <= 0 ? 1200 : width;
            CanvasHeight = height <= 0 ? 800 : height;
        }

        public IList<Vertex> Vertices { get { return _vertices; } }
        public IList<Edge> Edges { get { return _edges; } }

        public Vertex FindVertex(int id)
        {
            Vertex v;
            return _vertexById.TryGetValue(id, out v) ? v : null;
        }

        public Edge FindEdge(int id)
        {
            for (int i = 0; i < _edges.Count; i++)
            {
                if (_edges[i].Id == id) return _edges[i];
            }
            return null;
        }

        public Color EffectiveColor(Vertex v)
        {
            return v.Color.HasValue ? v.Color.Value : DefaultVertexColor;
        }

        public Color EffectiveColor(Edge e)
        {
            return e.Color.HasValue ? e.Color.Value : DefaultEdgeColor;
        }

        // ---------- 增删 ----------

        public Vertex AddVertex(double x, double y)
        {
            return AddVertex(x, y, null);
        }

        public Vertex AddVertex(double x, double y, string name)
        {
            // 画布尺寸会自动扩展以容纳内容，所以顶点位置不做钳制
            Vertex v = new Vertex(_nextVertexId++, x, y,
                string.IsNullOrEmpty(name) ? NextVertexName() : name);
            _vertices.Add(v);
            _vertexById[v.Id] = v;
            return v;
        }

        public Edge AddEdge(int v1, int v2)
        {
            return AddEdge(v1, v2, null);
        }

        public Edge AddEdge(int v1, int v2, string name)
        {
            Vertex a = FindVertex(v1);
            Vertex b = FindVertex(v2);
            if (a == null || b == null) return null;
            Edge e = new Edge(_nextEdgeId++, v1, v2,
                string.IsNullOrEmpty(name) ? NextEdgeName(a, b) : name);
            _edges.Add(e);
            return e;
        }

        /// <summary>删除顶点时同时删除与其关联的边。</summary>
        public void RemoveVertex(int id)
        {
            Vertex v = FindVertex(id);
            if (v == null) return;
            _vertices.Remove(v);
            _vertexById.Remove(id);
            _edges.RemoveAll(delegate(Edge e) { return e.V1 == id || e.V2 == id; });
        }

        public void RemoveEdge(int id)
        {
            _edges.RemoveAll(delegate(Edge e) { return e.Id == id; });
        }

        /// <summary>导入时直接放置元素（保留其 Id 与名字）。</summary>
        public Vertex PlaceVertex(int id, double x, double y, string name, Color? color)
        {
            Vertex v = new Vertex(id, x, y, name);
            v.Color = color;
            _vertices.Add(v);
            _vertexById[id] = v;
            if (id >= _nextVertexId) _nextVertexId = id + 1;
            return v;
        }

        public Edge PlaceEdge(int id, int v1, int v2, string name, Color? color)
        {
            Edge e = new Edge(id, v1, v2, name);
            e.Color = color;
            _edges.Add(e);
            if (id >= _nextEdgeId) _nextEdgeId = id + 1;
            return e;
        }

        // ---------- 边界与坐标 ----------

        public double ClampX(double x)
        {
            return Math.Max(0, Math.Min(CanvasWidth, x));
        }

        public double ClampY(double y)
        {
            return Math.Max(0, Math.Min(CanvasHeight, y));
        }

        /// <summary>顶点的包围盒（图像坐标）。</summary>
        public RectD VertexBounds(Vertex v)
        {
            double r = VertexRadius;
            if (VertexShape == VertexShape.Square)
            {
                return new RectD(v.X - r, v.Y - r * 0.75, r * 2, r * 1.5);
            }
            return new RectD(v.X - r, v.Y - r, r * 2, r * 2);
        }

        /// <summary>
        /// 图内容（所有顶点与边，含名字文字的大致范围）的外接矩形。
        /// 图为空时返回宽度为 0 的矩形。
        /// </summary>
        public RectD ContentBounds(double insidePadding, double outsideLabelPadding)
        {
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            bool any = false;

            foreach (Vertex v in _vertices)
            {
                RectD b = VertexBounds(v);
                // 名字显示在顶点外侧时需要额外空间
                double pad = 0;
                if (!string.IsNullOrEmpty(v.Name))
                {
                    pad = VertexLabelPlacement == VertexLabelPlacement.Inside ? insidePadding : outsideLabelPadding;
                }
                double left = b.X - pad;
                double top = b.Y - pad;
                double right = b.Right + pad;
                double bottom = b.Bottom + pad;
                if (left < minX) minX = left;
                if (top < minY) minY = top;
                if (right > maxX) maxX = right;
                if (bottom > maxY) maxY = bottom;
                any = true;
            }

            foreach (Edge e in _edges)
            {
                PointD[] pts = EdgePolyline(e);
                double pad = string.IsNullOrEmpty(e.Name) ? 0 : insidePadding;
                for (int i = 0; i < pts.Length; i++)
                {
                    if (pts[i].X - pad < minX) minX = pts[i].X - pad;
                    if (pts[i].Y - pad < minY) minY = pts[i].Y - pad;
                    if (pts[i].X + pad > maxX) maxX = pts[i].X + pad;
                    if (pts[i].Y + pad > maxY) maxY = pts[i].Y + pad;
                    any = true;
                }
            }

            if (!any) return new RectD(0, 0, 0, 0);
            return new RectD(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// 按内容计算应有的画布尺寸：内容外接矩形加上留白。
        /// 用于「出界自动扩展、收回自动收缩」。图为空时返回最小尺寸。
        /// </summary>
        public void FitCanvasToContent(double margin, double minWidth, double minHeight, double maxSide)
        {
            RectD bounds = ContentBounds(margin, margin * 2 + VertexRadius);
            double width;
            double height;
            if (bounds.Width <= 0 && bounds.Height <= 0)
            {
                width = minWidth;
                height = minHeight;
            }
            else
            {
                width = Math.Max(bounds.Right + margin, minWidth);
                height = Math.Max(bounds.Bottom + margin, minHeight);
            }
            CanvasWidth = Math.Max(1, Math.Min(maxSide, Math.Ceiling(width)));
            CanvasHeight = Math.Max(1, Math.Min(maxSide, Math.Ceiling(height)));
        }

        // ---------- 命名 ----------

        /// <summary>
        /// 按当前命名规则为新建顶点取名；返回空串表示不命名。
        /// 字母规则 → A、B…Z、A1…；数字规则 → V1、V2、V3…
        /// </summary>
        private string NextVertexName()
        {
            switch (VertexNaming)
            {
                case NamingRule.None:
                    return "";
                case NamingRule.Numeric:
                    return NextPrefixedName("V", false, true);
                default:
                    // Alphabetic，以及 FromEndpoints（边专用）在顶点上退化为字母
                    return NextLetterName(false, true);
            }
        }

        /// <summary>
        /// 按当前命名规则为新建边取名；返回空串表示不命名。
        /// 字母规则 → a、b…z、a1…；数字规则 → e1、e2、e3…；两端点规则 → 端点名字拼接。
        /// </summary>
        private string NextEdgeName(Vertex a, Vertex b)
        {
            switch (EdgeNaming)
            {
                case NamingRule.None:
                    return "";
                case NamingRule.Numeric:
                    return NextPrefixedName("e", true, false);
                case NamingRule.Alphabetic:
                    return NextLetterName(true, false);
                default:
                    // FromEndpoints：优先用两端点名字拼接
                    return SuggestEdgeName(a, b);
            }
        }

        /// <summary>按字母序列取名：顶点 A、B…Z、A1…；边 a、b…z、a1…。</summary>
        private string NextLetterName(bool lowerCase, bool vertex)
        {
            for (int i = 0; i < 100000; i++)
            {
                string candidate = AlphabetName(i);
                if (lowerCase) candidate = candidate.ToLowerInvariant();
                bool exists = vertex ? VertexNameExists(candidate) : EdgeNameExists(candidate);
                if (!exists) return candidate;
            }
            return "";
        }

        /// <summary>按「前缀 + 递增数字」取名：顶点 V1…；边 e1…。</summary>
        private string NextPrefixedName(string prefix, bool lowerCase, bool vertex)
        {
            for (int i = 1; i < 100000; i++)
            {
                string candidate = prefix + i.ToString(CultureInfo.InvariantCulture);
                if (lowerCase) candidate = candidate.ToLowerInvariant();
                bool exists = vertex ? VertexNameExists(candidate) : EdgeNameExists(candidate);
                if (!exists) return candidate;
            }
            return "";
        }

        /// <summary>为保证名字唯一而追加编号（粘贴等场景使用）。</summary>
        public string MakeVertexNameUnique(string name)
        {
            if (string.IsNullOrEmpty(name) || !VertexNameExists(name)) return name;
            for (int k = 2; k < 100000; k++)
            {
                string candidate = name + k.ToString(CultureInfo.InvariantCulture);
                if (!VertexNameExists(candidate)) return candidate;
            }
            return name;
        }

        public string MakeEdgeNameUnique(string name)
        {
            if (string.IsNullOrEmpty(name) || !EdgeNameExists(name)) return name;
            for (int k = 2; k < 100000; k++)
            {
                string candidate = name + k.ToString(CultureInfo.InvariantCulture);
                if (!EdgeNameExists(candidate)) return candidate;
            }
            return name;
        }

        public bool VertexNameExists(string name)
        {
            for (int i = 0; i < _vertices.Count; i++)
            {
                if (string.Equals(_vertices[i].Name, name, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public bool EdgeNameExists(string name)
        {
            for (int i = 0; i < _edges.Count; i++)
            {
                if (string.Equals(_edges[i].Name, name, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>0 → A, 1 → B, ... 25 → Z, 26 → A1 ...</summary>
        public static string AlphabetName(int index)
        {
            int letter = index % 26;
            int cycle = index / 26;
            string s = ((char)('A' + letter)).ToString();
            return cycle == 0 ? s : s + cycle.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 生成不与现有名字冲突的边名，尽量使用 {v1}{v2} 形式（如 AB）。
        /// 端点没有名字时退回「字母」规则（a、b、c…），与默认命名保持一致。
        /// </summary>
        public string SuggestEdgeName(Vertex a, Vertex b)
        {
            string baseName = (a.Name ?? "") + (b.Name ?? "");
            if (baseName.Length > 0)
            {
                for (int k = 1; k <= 200; k++)
                {
                    string candidate = k == 1 ? baseName : baseName + k.ToString(CultureInfo.InvariantCulture);
                    if (!EdgeNameExists(candidate)) return candidate;
                }
            }
            // 端点没有名字（或拼接名都用完）时退回字母命名
            return NextLetterName(true, false);
        }

        /// <summary>该规则是否可用于顶点（「两端点名字」是边专用的）。</summary>
        public static bool IsVertexRule(NamingRule rule)
        {
            return rule == NamingRule.None
                || rule == NamingRule.Alphabetic
                || rule == NamingRule.Numeric;
        }

        // ---------- 几何辅助 ----------

        public static double DistanceToSegment(double px, double py, double x1, double y1, double x2, double y2, out double t)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-9)
            {
                t = 0;
                double ex = px - x1;
                double ey = py - y1;
                return Math.Sqrt(ex * ex + ey * ey);
            }
            t = ((px - x1) * dx + (py - y1) * dy) / len2;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            double cx = x1 + t * dx;
            double cy = y1 + t * dy;
            double ax = px - cx;
            double ay = py - cy;
            return Math.Sqrt(ax * ax + ay * ay);
        }

        /// <summary>同一个顶点对之间（含自环）的边序号与总数，用于多边分离。</summary>
        public int MultiEdgeIndex(Edge edge, out int total)
        {
            int index = 0;
            total = 0;
            for (int i = 0; i < _edges.Count; i++)
            {
                Edge e = _edges[i];
                bool samePair = (e.V1 == edge.V1 && e.V2 == edge.V2) || (e.V1 == edge.V2 && e.V2 == edge.V1);
                if (!samePair) continue;
                if (e.Id == edge.Id) index = total;
                total++;
            }
            return index;
        }

        /// <summary>计算一条边的绘制折线（多边时做小幅弯曲分离，自环时画成环）。</summary>
        public PointD[] EdgePolyline(Edge edge)
        {
            Vertex a = FindVertex(edge.V1);
            Vertex b = FindVertex(edge.V2);
            if (a == null || b == null) return new PointD[0];

            if (edge.IsLoop) return LoopPolyline(a);

            int total;
            int index = MultiEdgeIndex(edge, out total);
            double offset = 0;
            if (total > 1)
            {
                offset = (index - (total - 1) / 2.0) * (VertexRadius * 0.9);
            }

            if (Math.Abs(offset) < 0.01)
            {
                return new PointD[] { new PointD(a.X, a.Y), new PointD(b.X, b.Y) };
            }

            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) return new PointD[] { new PointD(a.X, a.Y), new PointD(b.X, b.Y) };
            double nx = -dy / len;
            double ny = dx / len;
            double mx = (a.X + b.X) / 2.0 + nx * offset;
            double my = (a.Y + b.Y) / 2.0 + ny * offset;

            // 采样二次贝塞尔曲线
            const int steps = 16;
            PointD[] pts = new PointD[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                double mt = 1 - t;
                pts[i] = new PointD(
                    mt * mt * a.X + 2 * mt * t * mx + t * t * b.X,
                    mt * mt * a.Y + 2 * mt * t * my + t * t * b.Y);
            }
            return pts;
        }

        private static PointD[] LoopPolyline(Vertex v)
        {
            // 自环：画在顶点右上方的一个圆环
            double rad = 15;
            double cx = v.X + 19;
            double cy = v.Y - 19;
            const int steps = 24;
            PointD[] pts = new PointD[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                double ang = Math.PI * 2 * i / steps - Math.PI / 2;
                pts[i] = new PointD(cx + rad * Math.Cos(ang), cy + rad * Math.Sin(ang));
            }
            return pts;
        }

        /// <summary>边标签位置（折线中点附近）。</summary>
        public PointD EdgeLabelPoint(Edge edge)
        {
            PointD[] pts = EdgePolyline(edge);
            if (pts.Length == 0) return new PointD(0, 0);
            if (pts.Length == 2)
            {
                return new PointD((pts[0].X + pts[1].X) / 2, (pts[0].Y + pts[1].Y) / 2);
            }
            return pts[pts.Length / 2];
        }

        /// <summary>边在中点处的方向角（度）；名字沿这个方向倾斜，保证正着读。</summary>
        public double EdgeLabelAngle(Edge edge)
        {
            PointD[] pts = EdgePolyline(edge);
            if (pts.Length < 2) return 0;
            int mid = pts.Length / 2;
            int lo = Math.Max(0, mid - 1);
            int hi = Math.Min(pts.Length - 1, mid);
            double angle = Math.Atan2(pts[hi].Y - pts[lo].Y, pts[hi].X - pts[lo].X) * 180.0 / Math.PI;
            if (angle > 90) angle -= 180;
            if (angle < -90) angle += 180;
            return angle;
        }

        /// <summary>
        /// 边名字的锚点：线段中点沿**法线**偏移 distance 之后的位置。
        /// distance 为正表示法线指向屏幕下方的一侧。
        /// </summary>
        public PointD EdgeLabelAnchor(Edge edge, double distance)
        {
            PointD p = EdgeLabelPoint(edge);
            double radians = EdgeLabelAngle(edge) * Math.PI / 180.0;
            return new PointD(
                p.X - Math.Sin(radians) * distance,
                p.Y + Math.Cos(radians) * distance);
        }

        /// <summary>把一个由若干线段组成的折线当作一条边来命中测试。</summary>
        public bool HitPolyline(PointD[] points, double x, double y, double tolerance)
        {
            if (points == null || points.Length < 2) return false;
            for (int i = 0; i < points.Length - 1; i++)
            {
                double t;
                if (DistanceToSegment(x, y, points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y, out t) <= tolerance)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 线段是否与矩形相交（Liang-Barsky 裁剪）。
        /// 注意：不能用线段的包围盒代替 —— 斜边的包围盒远大于边本身，
        /// 会让框选误选到视觉上并未相交的边。
        /// </summary>
        public static bool SegmentIntersectsRect(double x1, double y1, double x2, double y2, RectD rect)
        {
            // 完全落在矩形内（含退化线段）
            if (rect.Contains(x1, y1) || rect.Contains(x2, y2)) return true;

            double dx = x2 - x1;
            double dy = y2 - y1;
            double t0 = 0.0;
            double t1 = 1.0;

            double[] p = { -dx, dx, -dy, dy };
            double[] q = { x1 - rect.X, rect.Right - x1, y1 - rect.Y, rect.Bottom - y1 };

            for (int i = 0; i < 4; i++)
            {
                if (Math.Abs(p[i]) < 1e-9)
                {
                    // 与该边界平行：在界外就完全不相交
                    if (q[i] < 0) return false;
                    continue;
                }

                double r = q[i] / p[i];
                if (p[i] < 0)
                {
                    if (r > t1) return false;
                    if (r > t0) t0 = r;
                }
                else
                {
                    if (r < t0) return false;
                    if (r < t1) t1 = r;
                }
            }
            return true;
        }

        /// <summary>折线（按其各线段）是否与矩形相交。</summary>
        public bool PolylineIntersectsRect(PointD[] points, RectD rect)
        {
            if (points == null || points.Length == 0) return false;
            if (points.Length == 1) return rect.Contains(points[0].X, points[0].Y);
            for (int i = 0; i < points.Length - 1; i++)
            {
                if (SegmentIntersectsRect(points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y, rect)) return true;
            }
            return false;
        }

        /// <summary>点是否击中某条边。</summary>
        public bool HitEdge(Edge edge, double x, double y, double tolerance)
        {
            return HitPolyline(EdgePolyline(edge), x, y, tolerance);
        }

        /// <summary>边（其折线）是否与矩形相交，用于框选。</summary>
        public bool EdgeIntersectsRect(Edge edge, RectD rect)
        {
            return PolylineIntersectsRect(EdgePolyline(edge), rect);
        }

        /// <summary>
        /// 边的包围盒。仅用于需要整体范围的场合（例如将来的缩放到选区）；
        /// 命中与框选判断必须用 <see cref="EdgeIntersectsRect"/>，不能用包围盒，
        /// 否则斜边会被误选。
        /// </summary>
        public RectD EdgeBounds(Edge edge)
        {
            PointD[] pts = EdgePolyline(edge);
            if (pts.Length == 0) return new RectD(0, 0, 0, 0);
            double minX = pts[0].X, maxX = pts[0].X, minY = pts[0].Y, maxY = pts[0].Y;
            for (int i = 1; i < pts.Length; i++)
            {
                minX = Math.Min(minX, pts[i].X);
                maxX = Math.Max(maxX, pts[i].X);
                minY = Math.Min(minY, pts[i].Y);
                maxY = Math.Max(maxY, pts[i].Y);
            }
            return new RectD(minX, minY, maxX - minX, maxY - minY);
        }

        // ---------- 绘制辅助（顶点样式 / 边样式） ----------

        public void ApplyPenStyle(Pen pen)
        {
            switch (EdgeLineStyle)
            {
                case EdgeLineStyle.Dashed:
                    pen.DashStyle = DashStyle.Dash;
                    break;
                case EdgeLineStyle.Dotted:
                    pen.DashStyle = DashStyle.Dot;
                    break;
                default:
                    pen.DashStyle = DashStyle.Solid;
                    break;
            }
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
        }

        public static string ShapeDisplayName(VertexShape shape)
        {
            return shape == VertexShape.Square ? "方形" : "圆形";
        }

        public static string LineStyleDisplayName(EdgeLineStyle style)
        {
            switch (style)
            {
                case EdgeLineStyle.Dashed: return "虚线";
                case EdgeLineStyle.Dotted: return "点线";
                default: return "实线";
            }
        }

        /// <summary>根据背景色亮度选择可读的文字颜色。</summary>
        public static Color ContrastTextColor(Color background)
        {
            double l = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
            return l > 0.6 ? Color.FromArgb(0x20, 0x20, 0x20) : Color.White;
        }

        public static Color Darken(Color c, double factor)
        {
            return Color.FromArgb(c.A,
                (int)Math.Round(c.R * factor),
                (int)Math.Round(c.G * factor),
                (int)Math.Round(c.B * factor));
        }
    }

    public struct PointD
    {
        public double X;
        public double Y;

        public PointD(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>携带状态栏提示文本的事件参数。</summary>
    public sealed class HintTextEventArgs : EventArgs
    {
        private readonly string _text;

        public HintTextEventArgs(string text)
        {
            _text = text ?? "";
        }

        public string Text { get { return _text; } }

        public override string ToString()
        {
            return _text;
        }
    }
}
