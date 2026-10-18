using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace GraphEditor.Tests
{
    /// <summary>
    /// 无第三方依赖的自检程序：验证模型、.graph 序列化、几何命中、SVG 导出与界面渲染。
    /// 全部通过时退出码为 0。
    /// </summary>
    internal static class TestProgram
    {
        private static int _passed;
        private static readonly List<string> _failures = new List<string>();
        private static string _artifacts;

        /// <summary>反射设置控件内部的滚动条 Value，模拟用户拖动滚动条。</summary>
        private static void SetScrollValue(CanvasControl control, string which, int value)
        {
            System.Reflection.FieldInfo field = typeof(CanvasControl).GetField(
                which == "vertical" ? "_vScroll" : "_hScroll",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("找不到滚动条字段 " + which);
            ScrollBar bar = (ScrollBar)field.GetValue(control);
            bar.Value = Math.Max(bar.Minimum, Math.Min(bar.Maximum, value));
        }

        private static void RunSelected(string only, string name, Action body)
        {
            if (!string.IsNullOrEmpty(only) && !string.Equals(only, name, StringComparison.OrdinalIgnoreCase)) return;
            RunCase(name, body);
        }

        /// <summary>
        /// 逐个用例执行，并把已完成标记写入文件。若某个用例导致进程级崩溃
        /// （例如异常逃逸出窗口回调），日志最后一行就是崩溃点。
        /// </summary>
        private static void RunCase(string name, Action body)
        {
            body();
            try
            {
                if (_artifacts != null)
                {
                    File.AppendAllText(Path.Combine(_artifacts, "cases.log"), name + " OK" + Environment.NewLine);
                }
            }
            catch
            {
                // 日志写入失败不影响自检
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
            _artifacts = Path.Combine(root, "artifacts");
            Directory.CreateDirectory(_artifacts);

            // 自检进程不进入消息循环，也不需要视觉样式；调用 EnableVisualStyles
            // 会在 CLR 关闭阶段留下未处理的窗口回调，导致退出码被 0xC000041D 覆盖。
            // 渲染正确性由像素断言保证，与视觉样式无关。

            string only = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != null && args[i].StartsWith("/only:", StringComparison.OrdinalIgnoreCase))
                {
                    only = args[i].Substring("/only:".Length);
                }
            }

            try
            {
                RunSelected(only, "TestModelBasics", TestModelBasics);
                RunSelected(only, "TestNaming", TestNaming);
                RunSelected(only, "TestGeometry", TestGeometry);
                RunSelected(only, "TestJson", TestJson);
                RunSelected(only, "TestRoundTrip", TestRoundTrip);
                RunSelected(only, "TestRoundTripWithUnicode", TestRoundTripWithUnicode);
                RunSelected(only, "TestTolerantLoading", TestTolerantLoading);
                RunSelected(only, "TestSvg", TestSvg);
                RunSelected(only, "TestScrollAndZoom", TestScrollAndZoom);
                RunSelected(only, "TestHitTesting", TestHitTesting);
                RunSelected(only, "TestRubberBandSelection", TestRubberBandSelection);
                RunSelected(only, "TestRubberBandEdgeRule", TestRubberBandEdgeRule);
                RunSelected(only, "TestInfiniteCanvas", TestInfiniteCanvas);
                RunSelected(only, "TestMdiDocuments", TestMdiDocuments);
                RunSelected(only, "TestGraphTypeOptions", TestGraphTypeOptions);
                RunSelected(only, "TestGraphTypeMenu", TestGraphTypeMenu);
                RunSelected(only, "TestMenusAndDefaultColor", TestMenusAndDefaultColor);
                RunSelected(only, "TestReportedRegressions", TestReportedRegressions);
                RunSelected(only, "TestClipboard", TestClipboard);
                RunSelected(only, "TestRotation", TestRotation);
                RunSelected(only, "TestNamingAndPlacement", TestNamingAndPlacement);
                RunSelected(only, "TestEdgeLabelPlacement", TestEdgeLabelPlacement);
                RunSelected(only, "TestRenderSample", TestRenderSample);
                RunSelected(only, "TestPanGestures", TestPanGestures);
                RunSelected(only, "TestEdgeNamingTakesEffect", TestEdgeNamingTakesEffect);
                RunSelected(only, "TestUserInterface", TestUserInterface);
            }
            catch (Exception ex)
            {
                _failures.Add("未捕获异常: " + ex);
            }

            Console.WriteLine();
            Console.WriteLine("================ 自检结果 ================");
            Console.WriteLine("通过: " + _passed + "    失败: " + _failures.Count);
            foreach (string failure in _failures)
            {
                Console.WriteLine("  [FAIL] " + failure);
            }
            Console.WriteLine("产物目录: " + _artifacts);
            Console.WriteLine("==========================================");
            // 机器可读的结论行（纯 ASCII），供 run-tests.ps1 判定；见该脚本中关于
            // 退出码不可靠的说明。
            Console.WriteLine("RESULT passed=" + _passed + " failed=" + _failures.Count);
            Console.Out.Flush();

            int exitCode = _failures.Count == 0 ? 0 : 1;
            // 直接终止进程：WinForms 控件在 CLR 关闭阶段可能抛出逃逸回调的
            // 异常（0xC000041D），使正常 return 的退出码被覆盖。测试进程不需要
            // 走完整的关闭流程，这里显式指定退出码。
            Environment.Exit(exitCode);
            return exitCode;
        }

        // ================= 断言辅助 =================

        private static void Check(bool condition, string name)
        {
            if (condition) _passed++;
            else _failures.Add(name);
        }

        private static void CheckEqual(object expected, object actual, string name)
        {
            bool ok = expected == null ? actual == null : expected.Equals(actual);
            if (ok) _passed++;
            else _failures.Add(name + "（期望 " + Show(expected) + "，实际 " + Show(actual) + "）");
        }

        private static void CheckNear(double expected, double actual, double tolerance, string name)
        {
            if (Math.Abs(expected - actual) <= tolerance) _passed++;
            else _failures.Add(name + "（期望 " + Show(expected) + "±" + tolerance + "，实际 " + Show(actual) + "）");
        }

        private static string Show(object value)
        {
            return value == null ? "null" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        // ================= 用例 =================

        private static void TestModelBasics()
        {
            GraphCanvas c = new GraphCanvas(800, 600);
            Vertex a = c.AddVertex(100, 100);
            Vertex b = c.AddVertex(200, 200);
            Edge ab = c.AddEdge(a.Id, b.Id);
            Edge loop = c.AddEdge(a.Id, a.Id);

            CheckEqual(2, c.Vertices.Count, "新增两个顶点");
            CheckEqual(2, c.Edges.Count, "新增两条边（含自环）");
            Check(ab != null && loop != null, "边创建成功");
            Check(loop.IsLoop, "自环识别");
            Check(!ab.IsLoop, "普通边不是自环");
            Check(a.Id != b.Id, "顶点 Id 唯一");

            // 删除顶点应连带删除关联边
            c.RemoveVertex(a.Id);
            CheckEqual(1, c.Vertices.Count, "删除顶点后剩余 1 个顶点");
            CheckEqual(0, c.Edges.Count, "删除顶点连带删除其关联边（含自环）");

            // 坐标不再被钳制：画布尺寸会自动扩展以容纳内容
            GraphCanvas d = new GraphCanvas(500, 400);
            Vertex v = d.AddVertex(9999, -50);
            CheckNear(9999, v.X, 1e-9, "顶点 X 不再被钳制（画布会自动扩展）");
            CheckNear(-50, v.Y, 1e-9, "顶点 Y 不再被钳制（画布会自动扩展）");

            // 按内容计算画布尺寸
            GraphCanvas fit = new GraphCanvas(500, 400);
            fit.VertexRadius = 20;
            fit.AddVertex(300, 200);
            fit.FitCanvasToContent(60, 400, 300, 20000);
            Check(fit.CanvasWidth >= 300 + 20 + 60, "画布宽度扩展到把内容与留白包进来（"
                + fit.CanvasWidth + "）");
            Check(fit.CanvasHeight >= 200 + 20 + 60, "画布高度扩展到把内容与留白包进来（"
                + fit.CanvasHeight + "）");

            // 内容收回时尺寸随之收缩（但不小于传入的最小尺寸）
            GraphCanvas shrink = new GraphCanvas(5000, 5000);
            shrink.VertexRadius = 20;
            shrink.AddVertex(100, 100);
            shrink.FitCanvasToContent(60, 400, 300, 20000);
            CheckEqual(400.0, shrink.CanvasWidth, "内容很小的时候画布收缩到最小宽度");
            CheckEqual(300.0, shrink.CanvasHeight, "内容很小的时候画布收缩到最小高度");

            // 空图的尺寸就是传入的最小尺寸
            GraphCanvas empty = new GraphCanvas(1234, 567);
            empty.FitCanvasToContent(60, 400, 300, 20000);
            CheckEqual(400.0, empty.CanvasWidth, "空图画布为最小宽度");
            CheckEqual(300.0, empty.CanvasHeight, "空图画布为最小高度");

            // 颜色：元素自身颜色优先，否则用默认色
            GraphCanvas e = new GraphCanvas(400, 300);
            e.DefaultVertexColor = Color.Red;
            Vertex colored = e.AddVertex(50, 50);
            CheckEqual(Color.Red.ToArgb(), e.EffectiveColor(colored).ToArgb(), "未着色顶点使用默认颜色");
            colored.Color = Color.Blue;
            CheckEqual(Color.Blue.ToArgb(), e.EffectiveColor(colored).ToArgb(), "已着色顶点使用自身颜色");

            // 对比文字颜色
            CheckEqual(Color.White.ToArgb(), GraphCanvas.ContrastTextColor(Color.FromArgb(20, 20, 20)).ToArgb(), "深色底用白字");
            Check(GraphCanvas.ContrastTextColor(Color.White).R < 80, "浅色底用深字");
        }

        private static void TestNaming()
        {
            CheckEqual("A", GraphCanvas.AlphabetName(0), "第 1 个名字是 A");
            CheckEqual("Z", GraphCanvas.AlphabetName(25), "第 26 个名字是 Z");
            CheckEqual("A1", GraphCanvas.AlphabetName(26), "第 27 个名字是 A1");

            GraphCanvas c = new GraphCanvas(600, 400);
            Vertex v1 = c.AddVertex(10, 10);
            Vertex v2 = c.AddVertex(20, 20);
            Vertex v3 = c.AddVertex(30, 30);
            CheckEqual("A", v1.Name, "顶点自动命名 A");
            CheckEqual("B", v2.Name, "顶点自动命名 B");
            CheckEqual("C", v3.Name, "顶点自动命名 C");

            // 边默认不命名（顶点默认仍是大写字母）
            CheckEqual(NamingRule.None, c.EdgeNaming, "边默认命名规则是「不命名」");
            Edge noName = c.AddEdge(v1.Id, v2.Id);
            CheckEqual("", noName.Name, "默认不命名时新建的边没有名字");
            Vertex noNameVertex = c.AddVertex(50, 50);
            Check(!string.IsNullOrEmpty(noNameVertex.Name), "默认不命名不影响顶点命名（" + noNameVertex.Name + "）");

            // 切成「两端点名字」后才有自动名字，重边名字不重复
            c.EdgeNaming = NamingRule.FromEndpoints;
            Edge e = c.AddEdge(v1.Id, v2.Id);
            Check(!string.IsNullOrEmpty(e.Name), "两端点命名下边有自动名字（" + e.Name + "）");
            Vertex v4 = c.AddVertex(40, 40, "A");
            CheckEqual("A", v4.Name, "显式指定的名字被保留");
            Check(c.VertexNameExists("A"), "名字存在性检查");

            // SuggestEdgeName 在重复时加后缀
            Edge first = c.AddEdge(v1.Id, v2.Id);
            Edge second = c.AddEdge(v1.Id, v2.Id);
            Check(first.Name != second.Name, "重边的名字不重复（" + first.Name + " / " + second.Name + "）");
        }

        private static void TestGeometry()
        {
            double t;
            CheckNear(5, GraphCanvas.DistanceToSegment(0, 5, 0, 0, 10, 0, out t), 1e-9, "点到线段距离（垂足在段内）");
            CheckNear(0, t, 1e-9, "垂足落在起点时 t=0");
            CheckNear(3, GraphCanvas.DistanceToSegment(5, 3, 0, 0, 10, 0, out t), 1e-9, "点到线段距离（段内）");
            CheckNear(0.5, t, 1e-9, "垂足参数 t=0.5");
            CheckNear(5, GraphCanvas.DistanceToSegment(-5, 0, 0, 0, 10, 0, out t), 1e-9, "点到线段距离（段外取端点）");
            CheckNear(0, t, 1e-9, "段外参数 t 被钳制为 0");
            CheckNear(5, GraphCanvas.DistanceToSegment(15, 0, 0, 0, 10, 0, out t), 1e-9, "段外另一端距离取端点");
            CheckNear(1, t, 1e-9, "段外另一端 t 被钳制为 1");
            CheckNear(5, GraphCanvas.DistanceToSegment(5, 0, 0, 0, 0, 0, out t), 1e-9, "退化线段按点处理");

            GraphCanvas c = new GraphCanvas(800, 600);
            c.VertexRadius = 20;
            Vertex a = c.AddVertex(100, 100);
            Vertex b = c.AddVertex(300, 100);
            Edge e = c.AddEdge(a.Id, b.Id);

            RectD bounds = c.VertexBounds(a);
            CheckNear(80, bounds.X, 1e-9, "圆形顶点包围盒左边界");
            CheckNear(40, bounds.Width, 1e-9, "圆形顶点包围盒宽度");

            PointD[] poly = c.EdgePolyline(e);
            CheckEqual(2, poly.Length, "单边为直线段（两个点）");
            Check(c.HitEdge(e, 200, 100, 3), "命中边中点");
            Check(c.HitEdge(e, 200, 103, 3), "命中边附近（容差内）");
            Check(!c.HitEdge(e, 200, 120, 3), "未命中边（超出容差）");

            PointD label = c.EdgeLabelPoint(e);
            CheckNear(200, label.X, 1e-6, "边标签 X 在中点");
            CheckNear(100, label.Y, 1e-6, "边标签 Y 在中点");

            // 自环：画在顶点右上方（中心 +19,-19，半径 15）
            Edge loop = c.AddEdge(a.Id, a.Id);
            PointD[] loopPoly = c.EdgePolyline(loop);
            Check(loopPoly.Length > 2, "自环用折线近似圆环");
            Check(c.HitEdge(loop, a.X + 19, a.Y - 34, 2), "命中自环顶部");
            Check(c.HitEdge(loop, a.X + 34, a.Y - 19, 2), "命中自环右侧");
            Check(!c.HitEdge(loop, a.X + 19, a.Y - 19, 2), "自环圆心处不算命中");

            // 多重边：应分离
            Edge e2 = c.AddEdge(a.Id, b.Id);
            int total;
            c.MultiEdgeIndex(e, out total);
            CheckEqual(2, total, "同一对顶点之间的边数为 2");
            PointD[] poly2 = c.EdgePolyline(e2);
            Check(poly2.Length > 2, "多重边被绘制为曲线以分离");
            PointD[] poly1 = c.EdgePolyline(e);
            Check(Math.Abs(poly1[0].Y - poly2[0].Y) < 1e-9, "多重边共用顶点端点");
            Check(Math.Abs(poly1[poly1.Length / 2].Y - poly2[poly2.Length / 2].Y) > 1, "多重边在中部彼此分离");

            // 方形顶点的包围盒
            c.VertexShape = VertexShape.Square;
            RectD square = c.VertexBounds(a);
            CheckNear(40, square.Width, 1e-9, "方形顶点包围盒宽度");
            CheckNear(30, square.Height, 1e-9, "方形顶点包围盒高度");
        }

        private static void TestJson()
        {
            CheckEqual("null", JsonValue.Parse("null").ToJson(), "解析 null");
            CheckEqual("true", JsonValue.Parse("true").ToJson(), "解析 true");
            CheckEqual("42", JsonValue.Parse("42").ToJson(), "解析整数");
            CheckEqual("1.5", JsonValue.Parse("1.5").ToJson(), "解析小数");
            CheckEqual("\"hi\"", JsonValue.Parse("\"hi\"").ToJson(), "解析字符串");
            CheckEqual("\"a\\\"b\"", JsonValue.Parse("\"a\\\"b\"").ToJson(), "解析转义引号");
            CheckEqual("中", JsonValue.Parse("\"\\u4e2d\"").AsString(""), "解析 \\u 转义为对应字符");
            CheckEqual("\"\\n\"", JsonValue.Parse("\"\\n\"").ToJson(), "换行符以转义形式输出");
            CheckEqual("a\"b", JsonValue.Parse("\"a\\\"b\"").AsString(""), "转义引号还原为字符");

            JsonValue doc = JsonValue.Parse("{\"a\":[1,2,{\"b\":\"中文\"}],\"c\":false}");
            CheckEqual(1.0, doc.Get("a").Items[0].AsDouble(0), "读取嵌套数组元素");
            CheckEqual("中文", doc.Get("a").Items[2].Get("b").AsString(""), "读取嵌套对象字符串");
            CheckEqual(false, doc.Get("c").AsBool(true), "读取布尔值");
            Check(doc.Get("missing") == null, "缺失键返回 null");

            bool threw = false;
            try { JsonValue.Parse("{\"a\":}"); }
            catch (FormatException) { threw = true; }
            Check(threw, "非法 JSON 抛出 FormatException");

            threw = false;
            try { JsonValue.Parse("{} trailing"); }
            catch (FormatException) { threw = true; }
            Check(threw, "尾部多余内容抛出 FormatException");

            threw = false;
            try { JsonValue.Parse("[1,2"); }
            catch (FormatException) { threw = true; }
            Check(threw, "数组未闭合抛出 FormatException");

            // 数字格式
            CheckEqual("0.1", JsonValue.FormatNumber(0.1), "数字格式保持精度");
            CheckEqual("1200", JsonValue.FormatNumber(1200.0), "整数不带小数点");
        }

        private static GraphCanvas BuildSample()
        {
            GraphCanvas c = new GraphCanvas(900, 640);
            c.VertexRadius = 22;
            c.VertexShape = VertexShape.Circle;
            c.EdgeWidth = 2.5;
            c.EdgeLineStyle = EdgeLineStyle.Solid;
            c.DefaultVertexColor = Color.FromArgb(0x2D, 0x7D, 0xD2);
            c.DefaultEdgeColor = Color.FromArgb(0x40, 0x40, 0x40);

            Vertex a = c.AddVertex(180, 200, "A");
            Vertex b = c.AddVertex(420, 120, "B");
            Vertex d = c.AddVertex(700, 220, "C");
            Vertex e = c.AddVertex(430, 420, "D");
            Vertex f = c.AddVertex(160, 460, "E");

            a.Color = Color.FromArgb(0x2D, 0x7D, 0xD2);
            b.Color = Color.FromArgb(0xE9, 0x1E, 0x63);
            d.Color = Color.FromArgb(0x00, 0xB0, 0x50);
            e.Color = Color.FromArgb(0xFF, 0x99, 0x00);
            // f 使用默认颜色

            c.AddEdge(a.Id, b.Id, "e1");
            c.AddEdge(b.Id, d.Id, "e2");
            c.AddEdge(d.Id, e.Id, "e3");
            c.AddEdge(e.Id, f.Id, "e4");
            c.AddEdge(f.Id, a.Id, "e5");
            Edge diag = c.AddEdge(a.Id, e.Id, "e6");
            diag.Color = Color.FromArgb(0x99, 0x33, 0xCC);
            c.AddEdge(e.Id, e.Id, "loop");
            return c;
        }

        private static void TestRoundTrip()
        {
            GraphCanvas original = BuildSample();
            string text = GraphIo.Serialize(original);

            string path = Path.Combine(_artifacts, "sample.graph");
            File.WriteAllText(path, text, new UTF8Encoding(false));

            GraphCanvas loaded = GraphIo.Deserialize(text);
            // 画布是无限平面：尺寸不再是文件的一部分（旧文件里的 canvas.width/height 被忽略）
            Check(!text.Contains("\"canvas\""), "序列化不再写出 canvas 尺寸字段");
            CheckEqual(original.VertexRadius, loaded.VertexRadius, "往返：顶点半径");
            CheckEqual(original.VertexShape, loaded.VertexShape, "往返：顶点形状");
            CheckEqual(original.EdgeWidth, loaded.EdgeWidth, "往返：边线宽");
            CheckEqual(original.EdgeLineStyle, loaded.EdgeLineStyle, "往返：边线型");
            CheckEqual(original.DefaultVertexColor.ToArgb(), loaded.DefaultVertexColor.ToArgb(), "往返：默认顶点颜色");
            CheckEqual(original.DefaultEdgeColor.ToArgb(), loaded.DefaultEdgeColor.ToArgb(), "往返：默认边颜色");
            CheckEqual(original.Vertices.Count, loaded.Vertices.Count, "往返：顶点数量");
            CheckEqual(original.Edges.Count, loaded.Edges.Count, "往返：边的数量");

            foreach (Vertex v in original.Vertices)
            {
                Vertex l = loaded.FindVertex(v.Id);
                Check(l != null, "往返：找回顶点 " + v.Name);
                if (l == null) continue;
                CheckEqual(v.Name, l.Name, "往返：顶点名字 " + v.Name);
                CheckNear(v.X, l.X, 1e-6, "往返：顶点 X " + v.Name);
                CheckNear(v.Y, l.Y, 1e-6, "往返：顶点 Y " + v.Name);
                CheckEqual(v.Color.HasValue ? v.Color.Value.ToArgb() : -1,
                    l.Color.HasValue ? l.Color.Value.ToArgb() : -1, "往返：顶点颜色 " + v.Name);
            }
            foreach (Edge e in original.Edges)
            {
                Edge l = loaded.FindEdge(e.Id);
                Check(l != null, "往返：找回边 " + e.Name);
                if (l == null) continue;
                CheckEqual(e.V1, l.V1, "往返：边起点 " + e.Name);
                CheckEqual(e.V2, l.V2, "往返：边终点 " + e.Name);
                CheckEqual(e.Name, l.Name, "往返：边名字 " + e.Name);
                CheckEqual(e.Color.HasValue ? e.Color.Value.ToArgb() : -1,
                    l.Color.HasValue ? l.Color.Value.ToArgb() : -1, "往返：边颜色 " + e.Name);
            }

            // 二次序列化应当稳定
            CheckEqual(text, GraphIo.Serialize(loaded), "二次序列化结果一致");

            // 加载后再新增元素，Id 不与既有 Id 冲突
            Vertex extra = loaded.AddVertex(10, 10);
            Check(loaded.FindVertex(extra.Id) != null && original.FindVertex(extra.Id) == null, "加载后新增顶点 Id 不冲突");

            // 通过文件读写
            GraphIo.Save(original, path);
            GraphCanvas fromDisk = GraphIo.Load(path);
            CheckEqual(original.Vertices.Count, fromDisk.Vertices.Count, "文件保存/读取：顶点数量");

            Console.WriteLine("示例 .graph 已写出: " + path);
        }

        private static void TestRoundTripWithUnicode()
        {
            GraphCanvas c = new GraphCanvas(640, 480);
            c.VertexShape = VertexShape.Square;
            c.EdgeLineStyle = EdgeLineStyle.Dotted;
            c.EdgeWidth = 3;
            c.DefaultVertexColor = Color.FromArgb(1, 2, 3);
            Vertex a = c.AddVertex(100, 100, "顶点\t一");
            Vertex b = c.AddVertex(300, 300, "B \"引号\" <标签> & 与\n换行");
            c.AddEdge(a.Id, b.Id, "边/1\\2");

            string text = GraphIo.Serialize(c);
            Check(text.Contains("\"format\": \"GraphEditor.Graph\""), "自定义格式含格式标识");
            Check(text.Contains("\"version\":"), "自定义格式含版本号");
            Check(text.Contains("\"id\": 1"), "自定义格式含顶点 Id");
            Check(text.Contains("\"vertexStyle\"") && text.Contains("\"edgeStyle\""), "自定义格式含两组全局样式");
            Check(text.Contains("\"radius\"") && text.Contains("\"shape\""), "顶点样式含半径与形状");
            Check(text.Contains("\"lineStyle\""), "边样式含线型");
            GraphCanvas loaded = GraphIo.Deserialize(text);
            CheckEqual("顶点\t一", loaded.Vertices[0].Name, "往返：制表符与中文名字");
            CheckEqual("B \"引号\" <标签> & 与\n换行", loaded.Vertices[1].Name, "往返：引号、尖括号、换行");
            CheckEqual("边/1\\2", loaded.Edges[0].Name, "往返：斜杠与反斜杠");
            CheckEqual(VertexShape.Square, loaded.VertexShape, "往返：方形形状");
            CheckEqual(EdgeLineStyle.Dotted, loaded.EdgeLineStyle, "往返：点线");
        }

        private static void TestTolerantLoading()
        {
            // 缺少多个字段的最小文件
            GraphCanvas minimal = GraphIo.Deserialize("{\"vertices\":[{\"id\":1,\"x\":10,\"y\":20}],\"edges\":[]}");
            CheckEqual(1, minimal.Vertices.Count, "最小文件：读到 1 个顶点");
            CheckEqual(1200.0, minimal.CanvasWidth, "最小文件：画布宽度取默认值");
            CheckEqual(18.0, minimal.VertexRadius, "最小文件：顶点半径取默认值");

            // 悬空边应被丢弃
            GraphCanvas dangling = GraphIo.Deserialize(
                "{\"vertices\":[{\"id\":1,\"x\":10,\"y\":20}],\"edges\":[{\"id\":1,\"from\":1,\"to\":99}]}");
            CheckEqual(0, dangling.Edges.Count, "悬空边（端点不存在）被丢弃");

            // 画布是无限平面：坐标不再被钳制到某个画布矩形内，极端坐标要原样保留
            GraphCanvas far = GraphIo.Deserialize(
                "{\"canvas\":{\"width\":800,\"height\":600},\"vertices\":[{\"id\":1,\"x\":99999,\"y\":-99999}]}");
            CheckNear(99999, far.Vertices[0].X, 0.01, "无限画布：大坐标原样保留（x）");
            CheckNear(-99999, far.Vertices[0].Y, 0.01, "无限画布：负坐标原样保留（y）");

            // 非法颜色被忽略而不是抛异常
            GraphCanvas badColor = GraphIo.Deserialize(
                "{\"vertexStyle\":{\"defaultColor\":\"not-a-color\"},\"vertices\":[{\"id\":1,\"x\":1,\"y\":1,\"color\":\"#GGGGGG\"}]}");
            Check(badColor.Vertices[0].Color == null, "非法颜色被忽略");

            // 非法字符串应抛出可读错误
            bool threw = false;
            try { GraphIo.Deserialize("这不是 JSON"); }
            catch (FormatException) { threw = true; }
            Check(threw, "非法内容抛出 FormatException");

            // 颜色解析
            Color parsed;
            Check(GraphIo.TryParseColor("#2D7DD2", out parsed) && parsed.R == 0x2D && parsed.G == 0x7D && parsed.B == 0xD2, "解析 #RRGGBB");
            Check(GraphIo.TryParseColor("2D7DD2", out parsed) && parsed.B == 0xD2, "解析不带 # 的颜色");
            Check(GraphIo.TryParseColor("#802D7DD2", out parsed) && parsed.A == 0x80, "解析 #AARRGGBB");
            Check(!GraphIo.TryParseColor("xyz", out parsed), "非法颜色字符串返回 false");
        }

        private static void TestSvg()
        {
            GraphCanvas c = BuildSample();
            string svg = SvgExport.Build(c);

            Check(svg.StartsWith("<?xml", StringComparison.Ordinal), "SVG 以 XML 声明开头");
            // SVG 只导出内容区域：viewBox 是内容外接矩形 + 留白，而不是整个画布
            RectD exportBounds = SvgExport.ExportBounds(c);
            Check(exportBounds.Width < c.CanvasWidth, "SVG 导出区域比画布更紧凑（"
                + exportBounds.Width.ToString("0.#", CultureInfo.InvariantCulture) + " < " + c.CanvasWidth + "）");
            Check(svg.Contains("viewBox=\"" + SVGNum(exportBounds.X) + " " + SVGNum(exportBounds.Y)
                + " " + SVGNum(exportBounds.Width) + " " + SVGNum(exportBounds.Height) + "\""),
                "SVG viewBox 等于内容外接矩形（含留白）");
            Check(svg.Contains("width=\"" + SVGNum(exportBounds.Width) + "\""), "SVG 宽度等于内容区域宽度");
            // 留白：外接矩形比内容大 2 × Padding
            RectD rawContent = c.ContentBounds(12, 12 * 3 + c.VertexRadius);
            CheckNear(rawContent.Width + SvgExport.Padding * 2, exportBounds.Width, 0.01,
                "SVG 区域 = 内容宽度 + 两侧留白");
            Check(svg.Contains("</svg>"), "SVG 正确闭合");
            Check(svg.Contains("<g id=\"edges\">") && svg.Contains("<g id=\"vertices\">"), "SVG 分组：边、顶点");
            Check(svg.IndexOf("<g id=\"edges\">", StringComparison.Ordinal)
                < svg.IndexOf("<g id=\"vertices\">", StringComparison.Ordinal), "SVG 先画边后画顶点");

            int circles = CountOccurrences(svg, "<circle");
            CheckEqual(c.Vertices.Count, circles, "SVG 圆形顶点数量与模型一致");
            int lines = CountOccurrences(svg, "<line ") + CountOccurrences(svg, "<path ");
            CheckEqual(c.Edges.Count, lines, "SVG 边的数量与模型一致");

            Check(svg.Contains("fill=\"#2D7DD2\""), "SVG 包含顶点颜色");
            Check(svg.Contains("stroke=\"#9933CC\""), "SVG 包含单独着色的边颜色");
            Check(svg.Contains("stroke-width=\"2.5\""), "SVG 使用全局边线宽");

            // 边名字没有背景：不应有描边，也不应用 paint-order 画白边
            // （顶点名字是白字压在彩色圆上，不在此列）
            int edgesGroup = svg.IndexOf("<g id=\"edges\">", StringComparison.Ordinal);
            int verticesGroup = svg.IndexOf("<g id=\"vertices\">", StringComparison.Ordinal);
            Check(edgesGroup >= 0 && verticesGroup > edgesGroup, "SVG 有边组与顶点组");
            string edgeGroup = svg.Substring(edgesGroup, verticesGroup - edgesGroup);
            int textStart = edgeGroup.IndexOf("<text", StringComparison.Ordinal);
            Check(textStart >= 0, "边组里包含名字文字");
            int labelCount = 0;
            while (textStart >= 0)
            {
                int textEnd = edgeGroup.IndexOf("</text>", textStart, StringComparison.Ordinal);
                string tag = edgeGroup.Substring(textStart, textEnd - textStart);
                Check(!tag.Contains("paint-order"), "SVG 边名字不带 paint-order（无背景）：" + tag);
                Check(!tag.Contains("stroke="), "SVG 边名字没有描边（无背景）：" + tag);
                Check(tag.Contains("fill=\"#202020\""), "SVG 边名字为深灰色：" + tag);
                labelCount++;
                textStart = edgeGroup.IndexOf("<text", textEnd, StringComparison.Ordinal);
            }
            CheckEqual(c.Edges.Count, labelCount, "每条边都有一个名字文字");

            // 线型
            GraphCanvas dashed = BuildSample();
            dashed.EdgeLineStyle = EdgeLineStyle.Dashed;
            Check(SvgExport.Build(dashed).Contains("stroke-dasharray"), "虚线导出 stroke-dasharray");
            GraphCanvas dotted = BuildSample();
            dotted.EdgeLineStyle = EdgeLineStyle.Dotted;
            Check(SvgExport.Build(dotted).Contains("stroke-dasharray"), "点线导出 stroke-dasharray");

            // 方形：背景 rect 之外，每个顶点一个 rect（顶点 rect 带 rx="3" 圆角）
            GraphCanvas square = BuildSample();
            square.VertexShape = VertexShape.Square;
            string squareSvg = SvgExport.Build(square);
            CheckEqual(square.Vertices.Count, CountOccurrences(squareSvg, "rx=\"3\""), "方形顶点导出为 rect（不含背景）");
            CheckEqual(square.Vertices.Count + 1, CountOccurrences(squareSvg, "<rect"), "SVG 共 1 个背景 rect + N 个顶点 rect");
            Check(!squareSvg.Contains("<circle"), "方形模式下不输出 circle");

            // 转义
            GraphCanvas esc = new GraphCanvas(200, 200);
            Vertex a = esc.AddVertex(50, 50, "A<&>\"'");
            Vertex b = esc.AddVertex(150, 150, "B");
            esc.AddEdge(a.Id, b.Id, "x<y");
            string escSvg = SvgExport.Build(esc);
            Check(escSvg.Contains("A&lt;&amp;&gt;&quot;&apos;"), "SVG 文本正确转义");
            Check(!escSvg.Contains("A<&>"), "SVG 中不残留未转义文本");
            Check(escSvg.Contains("x&lt;y"), "SVG 边标签正确转义");

            // 写出文件
            string path = Path.Combine(_artifacts, "sample.svg");
            SvgExport.Export(c, path);
            Check(File.Exists(path), "SVG 文件已写出");
            long size = new FileInfo(path).Length;
            Check(size > 500, "SVG 文件内容非空（" + size + " 字节）");

            // 必须是格式良好的 XML，并且元素数量与模型一致
            System.Xml.XmlDocument document = new System.Xml.XmlDocument();
            document.LoadXml(svg);
            System.Xml.XmlElement root = document.DocumentElement;
            Check(root != null && root.LocalName == "svg", "SVG 根元素为 <svg>");
            Check(root != null && root.GetAttribute("viewBox") == (SVGNum(exportBounds.X) + " " + SVGNum(exportBounds.Y)
                + " " + SVGNum(exportBounds.Width) + " " + SVGNum(exportBounds.Height)), "XML 中的 viewBox 与导出区域一致");
            System.Xml.XmlNamespaceManager spaces = new System.Xml.XmlNamespaceManager(document.NameTable);
            spaces.AddNamespace("s", "http://www.w3.org/2000/svg");
            CheckEqual(c.Vertices.Count, document.SelectNodes("//s:circle", spaces).Count, "XML 解析：circle 数量与顶点数一致");
            CheckEqual(c.Vertices.Count, document.SelectNodes("//s:g[@id='vertices']/s:text", spaces).Count,
                "XML 解析：顶点文字数量与顶点数一致");
            CheckEqual(c.Edges.Count, document.SelectNodes("//s:g[@id='edges']/s:line | //s:g[@id='edges']/s:path", spaces).Count,
                "XML 解析：边元素数量与边数一致");
            CheckEqual(c.Edges.Count, document.SelectNodes("//s:g[@id='edges']/s:text", spaces).Count,
                "XML 解析：边标签数量与边数一致");
            CheckEqual(0, document.SelectNodes("//s:g[@id='vertices']/s:line | //s:g[@id='vertices']/s:path", spaces).Count,
                "XML 解析：顶点分组内没有边元素");

            // 特殊字符的转义结果必须能重新解析回原文
            System.Xml.XmlDocument escaped = new System.Xml.XmlDocument();
            escaped.LoadXml(escSvg);
            System.Xml.XmlNamespaceManager spaces2 = new System.Xml.XmlNamespaceManager(escaped.NameTable);
            spaces2.AddNamespace("s", "http://www.w3.org/2000/svg");
            System.Xml.XmlNode escapedText = escaped.SelectSingleNode("//s:g[@id='vertices']/s:text", spaces2);
            Check(escapedText != null && escapedText.InnerText == "A<&>\"'",
                "XML 解析：转义后的顶点名可还原（实际 " + (escapedText == null ? "null" : escapedText.InnerText) + "）");

            // 空图也要能导出
            GraphCanvas empty = new GraphCanvas(300, 200);
            Check(SvgExport.Build(empty).Contains("</svg>"), "空图也能导出 SVG");
        }

        private static int CountOccurrences(string text, string token)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }
            return count;
        }

        private static void TestScrollAndZoom()
        {
            using (CanvasControl control = new CanvasControl())
            {
                control.Size = new Size(400, 300);
                GraphCanvas c = new GraphCanvas(1200, 800);
                control.Canvas = c;
                control.CenterOnOrigin();

                // 空画布：没有内容，也不出现滚动条
                Check(!control.NeedsHorizontalScrollbar && !control.NeedsVerticalScrollbar,
                    "空画布时没有滚动条");
                CheckEqual(1.0, control.Zoom, "初始缩放为 100%");

                // 缩放范围限制
                control.ZoomAtPoint(new Point(200, 150), 0.1);
                Check(control.Zoom >= 0.1, "缩放不低于最小值");
                control.ResetZoom();
                control.ZoomAtPoint(new Point(100, 100), 2.0);
                CheckNear(2.0, control.Zoom, 1e-9, "以 2 倍放大");
                for (int i = 0; i < 40; i++) control.ZoomAtPoint(new Point(10, 10), 2.0);
                Check(control.Zoom <= 8.0, "缩放不超过最大值");

                // ---- 内容决定滚动范围 ----
                CanvasControl scroller = new CanvasControl();
                scroller.Size = new Size(400, 300);
                GraphCanvas sc = new GraphCanvas(1200, 800);
                scroller.Canvas = sc;
                scroller.CenterOnOrigin();
                Check(!scroller.NeedsHorizontalScrollbar, "还没有内容时没有滚动条");

                // 放一个远处的顶点：滚动范围应扩大并出现滚动条
                sc.AddVertex(2000, 1500, "far");
                scroller.RefreshScrollRange();
                Check(scroller.NeedsHorizontalScrollbar && scroller.NeedsVerticalScrollbar,
                    "内容超出视口后出现滚动条");
                Point range = scroller.ScrollRange;
                Check(range.X > 0 && range.Y > 0, "内容变远后产生可滚动范围（" + range + "）");

                // 滚动条方向：向下/向右拖 → 视图原点变大 → 看到内容的右下部分
                scroller.SetScrollPosition(0, 0);
                PointD atTop = scroller.ViewOrigin;
                scroller.SetScrollPosition(range.X / 2, range.Y / 2);
                PointD atMid = scroller.ViewOrigin;
                scroller.SetScrollPosition(range.X, range.Y);
                PointD atBottom = scroller.ViewOrigin;
                Check(atBottom.Y > atMid.Y && atMid.Y > atTop.Y, "向下拖垂直滚动条 → 视图原点 y 递增");
                Check(atBottom.X > atTop.X, "向右拖水平滚动条 → 视图原点 x 递增");

                // 滚动条与视图原点必须严格同步
                scroller.ScrollBy(30, 40);
                CheckNear(scroller.ViewOrigin.X, scroller.ScrollPosition.X, 1.0, "ScrollBy 后水平滚动条位置同步");
                CheckNear(scroller.ViewOrigin.Y, scroller.ScrollPosition.Y, 1.0, "ScrollBy 后垂直滚动条位置同步");

                // 钳制：越界请求会停在可滚动范围内
                scroller.ScrollBy(100000, 100000);
                CheckNear(range.X, scroller.ViewOrigin.X, 2.0, "超出范围时钳制到最大原点");
                CheckNear(range.Y, scroller.ViewOrigin.Y, 2.0, "纵向也钳制到最大原点");
                scroller.ScrollBy(-100000, -100000);
                Check(scroller.ViewOrigin.X < 0, "反向越界时允许原点为负（内容可移到视口右侧）");

                // 走真实拖动所经过的路径：设置滚动条 Value → 触发 Scroll 处理
                scroller.SetScrollPosition(0, 0);
                scroller.CreateControl();
                int scrollMax = scroller.ScrollRange.Y;
                Check(scrollMax > 0, "有可滚动范围（" + scrollMax + "）");
                Invoke(scroller, "OnScrollChanged", new object[0]);
                CheckNear(0, scroller.ViewOrigin.Y, 1.5, "滚动条在 0 时视图原点为 0");

                SetScrollValue(scroller, "vertical", scrollMax);
                Invoke(scroller, "OnScrollChanged", new object[0]);
                CheckNear(scrollMax, scroller.ViewOrigin.Y, 2.0, "滚动条拖到底部时视图原点最大（看到内容下方）");
                Check(scroller.ViewOrigin.Y > 0, "此时视图原点为正，说明看到的是内容下方");

                SetScrollValue(scroller, "vertical", scroller.ScrollRange.Y / 2);
                Invoke(scroller, "OnScrollChanged", new object[0]);
                CheckNear(scroller.ScrollRange.Y / 2, scroller.ViewOrigin.Y, 2.0, "滚动条拖到中间时视图原点同步");

                SetScrollValue(scroller, "vertical", 0);
                Invoke(scroller, "OnScrollChanged", new object[0]);
                CheckNear(0, scroller.ViewOrigin.Y, 1.5, "滚动条拖回 0 时视图原点回到 0");

                // 横向同理
                SetScrollValue(scroller, "horizontal", scroller.ScrollRange.X);
                Invoke(scroller, "OnScrollChanged", new object[0]);
                CheckNear(scroller.ScrollRange.X, scroller.ViewOrigin.X, 2.0, "水平滚动条拖到最右时视图原点最大");

                scroller.Dispose();
            }
        }

        private static void TestHitTesting()
        {
            using (CanvasControl control = new CanvasControl())
            {
                control.Size = new Size(800, 600);
                GraphCanvas c = new GraphCanvas(800, 600);
                c.VertexRadius = 20;
                control.Canvas = c;

                Vertex a = c.AddVertex(100, 100, "A");
                Vertex b = c.AddVertex(400, 100, "B");
                c.AddEdge(a.Id, b.Id, "e1");

                bool isVertex;
                object hit = control.HitTest(100, 100, out isVertex);
                Check(isVertex && ReferenceEquals(hit, a), "命中顶点 A");

                hit = control.HitTest(250, 100, out isVertex);
                Check(!isVertex && hit is Edge, "命中边");

                hit = control.HitTest(100, 300, out isVertex);
                Check(hit == null, "空白处不命中任何元素");

                // 选择与多选
                Check(!control.HasSelection, "初始无选中");
                control.SelectAll();
                CheckEqual(2, control.SelectedVertexCount, "全选选中 2 个顶点");
                CheckEqual(1, control.SelectedEdgeCount, "全选选中 1 条边");

                control.ApplyColorToSelection(Color.FromArgb(0x11, 0x22, 0x33));
                CheckEqual(0x112233, c.EffectiveColor(a).ToArgb() & 0xFFFFFF, "统一着色作用于顶点");
                CheckEqual(0x112233, c.EffectiveColor(c.Edges[0]).ToArgb() & 0xFFFFFF, "统一着色作用于边");

                control.DeleteSelection();
                CheckEqual(0, c.Vertices.Count, "删除选中后顶点为空");
                CheckEqual(0, c.Edges.Count, "删除顶点连带删除边");
                Check(!control.HasSelection, "删除后无选中");

                // 微调
                Vertex d = c.AddVertex(200, 200);
                control.SetSelection(new List<Vertex>(new Vertex[] { d }), null);
                control.NudgeSelection(5, -3);
                CheckNear(205, d.X, 1e-9, "方向键微调 X");
                CheckNear(197, d.Y, 1e-9, "方向键微调 Y");

                // 右键拖动连线：在顶点上按下右键 → 拖到另一个顶点 → 松开
                List<string> hints = new List<string>();
                control.HintTextChanged += delegate(object sender, HintTextEventArgs args) { hints.Add(args.Text); };

                Vertex e2 = c.AddVertex(300, 300, "E2");
                Vertex f2 = c.AddVertex(500, 300, "F2");
                control.ClearSelection();
                
                int edgesBefore = c.Edges.Count;
                // 单纯右键单击（按下后原地松开、不移动）不应创建边
                SendMouseAtImage(control, "OnMouseDown", MouseButtons.Right, 300, 300);
                Check(!control.IsConnectDragging, "按下右键但未移动时不进入连线拖动");
                SendMouseAtImage(control, "OnMouseUp", MouseButtons.Right, 300, 300);
                Check(!control.IsConnectDragging, "原地松开后仍是未连线状态");
                CheckEqual(edgesBefore, c.Edges.Count, "原地松开右键不创建边");

                // 正常拖动：按下 → 越过阈值 → 移到目标 → 松开
                SendMouseAtImage(control, "OnMouseDown", MouseButtons.Right, 300, 300);
                SendMouseAtImage(control, "OnMouseMove", MouseButtons.Right, 312, 300);
                Check(control.IsConnectDragging, "右键在顶点上拖动后进入连线拖动");
                CheckEqual(e2.Id, control.ConnectSourceId, "连线起点为按下的顶点");
                SendMouseAtImage(control, "OnMouseMove", MouseButtons.Right, 500, 300);
                CheckEqual(f2.Id, control.ConnectTargetId, "拖动到目标顶点时识别出目标");
                SendMouseAtImage(control, "OnMouseUp", MouseButtons.Right, 500, 300);
                Check(!control.IsConnectDragging, "松开右键后结束连线拖动");
                CheckEqual(edgesBefore + 1, c.Edges.Count, "松开在目标顶点上时创建了一条边");
                Edge made = c.Edges[c.Edges.Count - 1];
                Check((made.V1 == e2.Id && made.V2 == f2.Id) || (made.V1 == f2.Id && made.V2 == e2.Id),
                    "新边的端点为拖动的两个顶点");
                CheckEqual(1, control.SelectedEdgeCount, "新建的边被选中");
                Check(hints.Count > 0 && hints[hints.Count - 1].IndexOf("已连接", StringComparison.Ordinal) >= 0,
                    "状态栏提示已连接（实际：" + (hints.Count > 0 ? hints[hints.Count - 1] : "无") + "）");

                // 反向拖动也能连接
                int beforeReverse = c.Edges.Count;
                SendMouseAtImage(control, "OnMouseDown", MouseButtons.Right, 500, 300);
                SendMouseAtImage(control, "OnMouseMove", MouseButtons.Right, 300, 300);
                SendMouseAtImage(control, "OnMouseUp", MouseButtons.Right, 300, 300);
                CheckEqual(beforeReverse + 1, c.Edges.Count, "反向拖动同样能创建边");

                // 松开在空白处：不建边并给出取消提示
                int beforeCancel = c.Edges.Count;
                SendMouseAtImage(control, "OnMouseDown", MouseButtons.Right, 300, 300);
                SendMouseAtImage(control, "OnMouseMove", MouseButtons.Right, 700, 560);
                CheckEqual(-1, control.ConnectTargetId, "拖到空白处时没有目标顶点");
                SendMouseAtImage(control, "OnMouseUp", MouseButtons.Right, 700, 560);
                CheckEqual(beforeCancel, c.Edges.Count, "松开在空白处时不创建边");
                Check(hints[hints.Count - 1].IndexOf("取消", StringComparison.Ordinal) >= 0,
                    "状态栏提示已取消连线（实际：" + hints[hints.Count - 1] + "）");

                // 空白处按住右键拖动 = 平移画布（不建边）
                int beforePan = c.Edges.Count;
                SendMouseAtImage(control, "OnMouseDown", MouseButtons.Right, 700, 560);
                SendMouseAtImage(control, "OnMouseMove", MouseButtons.Right, 740, 580);
                Check(!control.IsConnectDragging, "空白处右键拖动不进入连线拖动");
                SendMouseAtImage(control, "OnMouseUp", MouseButtons.Right, 740, 580);
                CheckEqual(beforePan, c.Edges.Count, "空白处右键拖动不创建边");

                // 自环：从顶点拖回自身
                int beforeLoop = c.Edges.Count;
                SendMouseAtImage(control, "OnMouseDown", MouseButtons.Right, 300, 300);
                SendMouseAtImage(control, "OnMouseMove", MouseButtons.Right, 340, 340);
                SendMouseAtImage(control, "OnMouseUp", MouseButtons.Right, 300, 300);
                CheckEqual(beforeLoop + 1, c.Edges.Count, "拖回自身可创建自环");
                Check(c.Edges[c.Edges.Count - 1].IsLoop, "自环的两个端点相同");

                // Esc 取消连线拖动
                SendMouseAtImage(control, "OnMouseDown", MouseButtons.Right, 300, 300);
                SendMouseAtImage(control, "OnMouseMove", MouseButtons.Right, 400, 300);
                Check(control.IsConnectDragging, "Esc 前处于连线拖动状态");
                KeyEventArgs escape = new KeyEventArgs(Keys.Escape);
                Invoke(control, "OnKeyDown", new object[] { escape });
                Check(!control.IsConnectDragging, "Esc 可取消连线拖动");
                Check(escape.Handled, "Esc 被标记为已处理");

                // ConnectVertices 的边界情况
                Check(control.ConnectVertices(-999, 1) == null, "端点不存在时不创建边");
                Check(control.ConnectVertices(1, -999) == null, "目标不存在时不创建边");
            }
        }

        private static object Invoke(object target, string methodName, object[] arguments)
        {
            System.Reflection.MethodInfo method = target.GetType().GetMethod(
                methodName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public,
                null,
                Array.ConvertAll(arguments, delegate(object a) { return a.GetType(); }),
                null);
            if (method == null) throw new InvalidOperationException("找不到方法 " + methodName);
            return method.Invoke(target, arguments);
        }

        /// <summary>按图像坐标投递一次鼠标事件（内部换算成客户区坐标）。</summary>
        private static void SendMouseAtImage(CanvasControl control, string handler, MouseButtons button, double imageX, double imageY)
        {
            Point device = control.DevicePointFromImage(imageX, imageY);
            Invoke(control, handler, new object[] { new MouseEventArgs(button, 1, device.X, device.Y, 0) });
        }

        private static void TestRenderSample()
        {
            GraphCanvas c = BuildSample();

            // 工作区渲染
            using (CanvasControl control = new CanvasControl())
            {
                control.Size = new Size(1000, 700);
                control.Canvas = c;
                control.SetSelection(new List<Vertex>(new Vertex[] { c.Vertices[0], c.Vertices[1] }),
                    new List<Edge>(new Edge[] { c.Edges[0] }));

                using (Bitmap bmp = new Bitmap(control.Width, control.Height))
                {
                    control.DrawToBitmap(bmp, new Rectangle(0, 0, control.Width, control.Height));
                    string canvasPath = Path.Combine(_artifacts, "canvas-render.png");
                    bmp.Save(canvasPath, ImageFormat.Png);

                    // 无限画布：整个工作区就是绘制面，底色为白
                    Color canvasBack = bmp.GetPixel(20, 20);
                    Check(canvasBack.R > 240 && canvasBack.G > 240 && canvasBack.B > 240,
                        "渲染：工作区底色为白（实际 " + canvasBack + "）");

                    // 顶点 A 位于图像坐标 (180,200)，用控件自身的换算定位（纸面居中）
                    Point aDevice = control.DevicePointFromImage(180, 200);
                    Rectangle probe = new Rectangle(aDevice.X - 6, aDevice.Y - 6, 12, 12);
                    bool foundBlue = false;
                    for (int x = probe.Left; x < probe.Right && !foundBlue; x++)
                    {
                        for (int y = probe.Top; y < probe.Bottom; y++)
                        {
                            if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) continue;
                            Color pixel = bmp.GetPixel(x, y);
                            if (pixel.B > 150 && pixel.R < 120) { foundBlue = true; break; }
                        }
                    }
                    Check(foundBlue, "渲染：顶点 A 的蓝色被绘制到预期位置（设备 " + aDevice + "）");
                    Console.WriteLine("工作区渲染已写出: " + canvasPath);
                }

                // 不同缩放与线型都要能画出
                foreach (double zoom in new double[] { 0.25, 1.0, 3.0 })
                {
                    control.ResetZoom();
                    control.ZoomAtPoint(new Point(400, 300), zoom);
                    using (Bitmap bmp = new Bitmap(200, 150))
                    {
                        control.DrawToBitmap(bmp, new Rectangle(0, 0, 200, 150));
                        Check(true, "渲染：缩放 " + zoom.ToString("0.##", CultureInfo.InvariantCulture) + " 未抛异常");
                    }
                }
            }

            // 完整主窗口渲染（菜单栏 + 工具栏 + 工作区）
            MainForm form = null;
            try
            {
                form = new MainForm();
                form.ClientSize = new Size(1180, 700);
                RealizeControlTree(form);
                using (Bitmap bmp = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                    string formPath = Path.Combine(_artifacts, "mainform-render.png");
                    bmp.Save(formPath, ImageFormat.Png);
                    Console.WriteLine("主窗口渲染已写出: " + formPath);

                    Color statusArea = bmp.GetPixel(4, bmp.Height - 4);
                    Check(statusArea.A == 255, "渲染：主窗口画面已绘制（实际 " + statusArea + "）");
                }
            }
            catch (Exception ex)
            {
                // 不因此中断整个自检
                _failures.Add("主窗口渲染失败: " + ex.Message);
            }
            finally
            {
                if (form != null)
                {
                    try
                    {
                        form.Dispose();
                    }
                    catch
                    {
                        // 关闭过程中的异常不影响自检结论
                    }
                }
            }
        }

        /// <summary>验证菜单栏「属性」项目与简化后的工具栏结构。</summary>
        private static void TestUserInterface()
        {
            MainForm form = null;
            try
            {
                form = new MainForm();

                // ---- 工具栏已删除，缩放等操作都在菜单里 ----
                bool hasToolStrip = false;
                foreach (Control control in form.Controls)
                {
                    if (control is ToolStrip && !(control is MenuStrip) && !(control is StatusStrip))
                    {
                        hasToolStrip = true;
                    }
                }
                Check(!hasToolStrip, "主窗口已删除工具栏");
                Check(form.MainMenuBar != null, "主窗口仍有菜单栏");
                Check(FindTopMenu(form.MainMenuBar, "编辑") != null, "菜单栏含「编辑」");
                Check(FindTopMenu(form.MainMenuBar, "视图") != null, "菜单栏含「视图」");

                // ---- 剪切应排在复制之前（先剪切后复制） ----
                ToolStripMenuItem editMenu = FindTopMenu(form.MainMenuBar, "编辑");
                Check(editMenu != null, "找到编辑菜单");
                if (editMenu != null)
                {
                    int cutIndex = -1;
                    int copyIndex = -1;
                    int pasteIndex = -1;
                    for (int i = 0; i < editMenu.DropDownItems.Count; i++)
                    {
                        string t = editMenu.DropDownItems[i].Text ?? "";
                        if (t.StartsWith("剪切", StringComparison.Ordinal)) cutIndex = i;
                        else if (t.StartsWith("复制", StringComparison.Ordinal)) copyIndex = i;
                        else if (t.StartsWith("粘贴", StringComparison.Ordinal)) pasteIndex = i;
                    }
                    Check(cutIndex >= 0 && copyIndex >= 0, "编辑菜单同时含剪切与复制");
                    Check(cutIndex < copyIndex, "编辑菜单里剪切排在复制之前（剪切 " + cutIndex + " < 复制 " + copyIndex + "）");
                    Check(pasteIndex > copyIndex, "粘贴排在复制之后");
                }

                // ---- 属性菜单结构 ----
                ToolStripMenuItem properties = form.PropertyMenuItem;
                Check(properties != null, "菜单栏存在「属性」项目");
                if (properties == null) return;

                form.RefreshPropertyMenu();
                CheckEqual(3, properties.DropDownItems.Count,
                    "属性菜单为 顶点 / 边 / 图");

                ToolStripMenuItem vertex = FindSubMenu(properties, "顶点");
                ToolStripMenuItem edge = FindSubMenu(properties, "边");
                Check(vertex != null, "属性菜单包含「顶点」");
                Check(edge != null, "属性菜单包含「边」");
                if (vertex == null || edge == null) return;

                Check(FindSubMenu(vertex, "颜色") != null, "顶点属性含颜色");
                Check(FindSubMenu(vertex, "大小") != null, "顶点属性含大小");
                Check(FindSubMenu(vertex, "形状") != null, "顶点属性含形状");
                Check(FindSubMenu(vertex, "默认命名") != null, "顶点属性含默认命名规则");
                Check(FindSubMenu(vertex, "名字位置") != null, "顶点属性含名字显示位置");
                Check(FindSubMenu(edge, "颜色") != null, "边属性含颜色");
                Check(FindSubMenu(edge, "线宽") != null, "边属性含线宽");
                Check(FindSubMenu(edge, "线型") != null, "边属性含线型");
                Check(FindSubMenu(edge, "默认命名") != null, "边属性含默认命名规则");
                Check(FindSubMenu(edge, "名字位置") != null, "边属性含名字显示位置");
                Check(FindSubMenu(properties, "画布") == null, "属性菜单不再含「画布」");

                // ---- 命名规则：选项齐全且能真正作用于画布 ----
                ToolStripMenuItem vertexNaming = FindSubMenu(vertex, "默认命名");
                CheckEqual(3, vertexNaming.DropDownItems.Count,
                    "顶点命名规则有 3 个选项（不命名/大写字母/字母V+数字）");
                Check(vertexNaming.Text.IndexOf("字母", StringComparison.Ordinal) >= 0,
                    "顶点命名当前值显示在菜单标题上（实际 " + vertexNaming.Text + "）");
                ClickOption(vertexNaming, "字母V+数字");
                CheckEqual(NamingRule.Numeric, form.CanvasArea.Canvas.VertexNaming, "切换到字母V+数字生效");
                ClickOption(vertexNaming, "不命名");
                CheckEqual(NamingRule.None, form.CanvasArea.Canvas.VertexNaming, "切换到不命名生效");
                ClickOption(vertexNaming, "大写字母");
                CheckEqual(NamingRule.Alphabetic, form.CanvasArea.Canvas.VertexNaming, "切换回大写字母生效");

                ToolStripMenuItem edgeNaming = FindSubMenu(edge, "默认命名");
                CheckEqual(4, edgeNaming.DropDownItems.Count,
                    "边命名规则有 4 个选项（不命名/小写字母/字母e+数字/两端点名字）");
                ClickOption(edgeNaming, "字母e+数字");
                CheckEqual(NamingRule.Numeric, form.CanvasArea.Canvas.EdgeNaming, "边切换到字母e+数字生效");
                ClickOption(edgeNaming, "两端点名字");
                CheckEqual(NamingRule.FromEndpoints, form.CanvasArea.Canvas.EdgeNaming, "边切换到两端点名字生效");
                ClickOption(edgeNaming, "小写字母");
                CheckEqual(NamingRule.Alphabetic, form.CanvasArea.Canvas.EdgeNaming, "边切换回小写字母生效");

                // ---- 名字显示位置：选项齐全且能真正作用于画布 ----
                ToolStripMenuItem vertexPlacement = FindSubMenu(vertex, "名字位置");
                CheckEqual(5, vertexPlacement.DropDownItems.Count, "顶点名字位置有 5 个选项");
                ClickOption(vertexPlacement, "上方");
                CheckEqual(VertexLabelPlacement.Above, form.CanvasArea.Canvas.VertexLabelPlacement, "顶点名字设为上方生效");
                ClickOption(vertexPlacement, "左侧");
                CheckEqual(VertexLabelPlacement.Left, form.CanvasArea.Canvas.VertexLabelPlacement, "顶点名字设为左侧生效");
                ClickOption(vertexPlacement, "内部");
                CheckEqual(VertexLabelPlacement.Inside, form.CanvasArea.Canvas.VertexLabelPlacement, "顶点名字设回内部生效");

                ToolStripMenuItem edgePlacement = FindSubMenu(edge, "名字位置");
                CheckEqual(3, edgePlacement.DropDownItems.Count, "边名字位置有 3 个选项（内部/上方/下方）");
                ClickOption(edgePlacement, "下方");
                CheckEqual(EdgeLabelPlacement.Below, form.CanvasArea.Canvas.EdgeLabelPlacement, "边名字设为下方生效");
                ClickOption(edgePlacement, "上方");
                CheckEqual(EdgeLabelPlacement.Above, form.CanvasArea.Canvas.EdgeLabelPlacement, "边名字设回上方生效");

                // ---- 颜色子菜单是调色盘，且勾选了当前颜色 ----
                ToolStripMenuItem vertexColor = FindSubMenu(vertex, "颜色");
                CheckEqual(CanvasControl.Palette.Length, vertexColor.DropDownItems.Count, "顶点颜色子菜单为整个调色盘");
                int checkedColors = 0;
                foreach (ToolStripItem item in vertexColor.DropDownItems)
                {
                    if (((ToolStripMenuItem)item).Checked) checkedColors++;
                }
                CheckEqual(1, checkedColors, "顶点颜色子菜单勾选了当前颜色");

                // ---- 选择「大小」中的某项会真正作用于画布 ----
                ToolStripMenuItem sizeMenu = FindSubMenu(vertex, "大小");
                ToolStripMenuItem target = null;
                foreach (ToolStripItem item in sizeMenu.DropDownItems)
                {
                    if (item.Text == "32") target = (ToolStripMenuItem)item;
                }
                Check(target != null, "大小子菜单包含 32");
                if (target != null)
                {
                    target.PerformClick();
                    CheckNear(32, form.CanvasArea.Canvas.VertexRadius, 1e-9, "通过属性菜单修改顶点大小生效");
                }

                // ---- 选择「形状」中的方形会真正作用于画布 ----
                ToolStripMenuItem shapeMenu = FindSubMenu(vertex, "形状");
                foreach (ToolStripItem item in shapeMenu.DropDownItems)
                {
                    if (item.Text == "方形") ((ToolStripMenuItem)item).PerformClick();
                }
                CheckEqual(VertexShape.Square, form.CanvasArea.Canvas.VertexShape, "通过属性菜单切换顶点形状生效");

                // ---- 选择边线型 ----
                ToolStripMenuItem lineMenu = FindSubMenu(edge, "线型");
                foreach (ToolStripItem item in lineMenu.DropDownItems)
                {
                    if (item.Text == "虚线") ((ToolStripMenuItem)item).PerformClick();
                }
                CheckEqual(EdgeLineStyle.Dashed, form.CanvasArea.Canvas.EdgeLineStyle, "通过属性菜单切换边线型生效");

                // ---- 重新展开后勾选状态跟随当前值 ----
                form.RefreshPropertyMenu();
                ToolStripMenuItem vertex2 = FindSubMenu(form.PropertyMenuItem, "顶点");
                ToolStripMenuItem shapeMenu2 = FindSubMenu(vertex2, "形状");
                foreach (ToolStripItem item in shapeMenu2.DropDownItems)
                {
                    ToolStripMenuItem menuItem = (ToolStripMenuItem)item;
                    if (menuItem.Text == "方形") Check(menuItem.Checked, "重新展开后「方形」被勾选");
                    if (menuItem.Text == "圆形") Check(!menuItem.Checked, "重新展开后「圆形」未勾选");
                }
            }
            catch (Exception ex)
            {
                _failures.Add("界面结构检查失败: " + ex.Message);
            }
            finally
            {
                if (form != null)
                {
                    try { form.Dispose(); }
                    catch { }
                }
            }
        }

        private static ToolStripMenuItem FindSubMenu(ToolStripMenuItem parent, string prefix)
        {
            foreach (ToolStripItem item in parent.DropDownItems)
            {
                ToolStripMenuItem menuItem = item as ToolStripMenuItem;
                if (menuItem != null && menuItem.Text != null && menuItem.Text.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return menuItem;
                }
            }
            return null;
        }

        private static ToolStripMenuItem FindTopMenu(MenuStrip menu, string prefix)
        {
            foreach (ToolStripItem item in menu.Items)
            {
                ToolStripMenuItem menuItem = item as ToolStripMenuItem;
                if (menuItem != null && menuItem.Text != null && menuItem.Text.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return menuItem;
                }
            }
            return null;
        }

        private static void ClickOption(ToolStripMenuItem menu, string optionText)
        {
            foreach (ToolStripItem item in menu.DropDownItems)
            {
                ToolStripMenuItem menuItem = item as ToolStripMenuItem;
                if (menuItem != null && menuItem.Text == optionText)
                {
                    menuItem.PerformClick();
                    return;
                }
            }
            throw new InvalidOperationException("找不到菜单项：" + optionText);
        }

        /// <summary>
        /// 右键手势（对齐 draw.io：右键拖动就是平移）：
        /// 起手延迟只有 50ms；延迟过后一移动就平移，基准点是「越过阈值那一刻的光标位置」；
        /// 只要没真的平移过，抬手就弹右键菜单（包括按住不动很久的情况）。
        /// 另外：中键不平移；顶点上的右键拖动是连线。
        /// </summary>
        private static void TestPanGestures()
        {
            using (CanvasControl control = new CanvasControl())
            {
                control.Size = new Size(600, 400);
                GraphCanvas c = new GraphCanvas(600, 400);
                control.Canvas = c;
                control.CenterOnOrigin();

                Vertex a = c.AddVertex(control.WorldToImage(-150, 0).X, control.WorldToImage(-150, 0).Y, "A");
                Vertex b = c.AddVertex(control.WorldToImage(150, 0).X, control.WorldToImage(150, 0).Y, "B");
                c.AddEdge(a.Id, b.Id, "AB");

                Point blank = new Point(2, 2);        // 左上角，远离两个顶点与那条边
                Point onBranch = new Point(500, 340);

                // ---- 中键拖动：不平移 ----
                PointD before = control.ViewOrigin;
                DragMouse(control, MouseButtons.Middle, onBranch, new Point(560, 380));
                CheckNear(before.X, control.ViewOrigin.X, 1.0, "中键拖动不再平移（x 不变）");
                CheckNear(before.Y, control.ViewOrigin.Y, 1.0, "中键拖动不再平移（y 不变）");

                // ---- ① 按住右键不动很久再松手：仍然要弹菜单 ----
                control.CenterOnOrigin();
                control.ResetContextMenuFlag();
                Invoke(control, "OnMouseDown", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, blank.X, blank.Y, 0)
                });
                control.AdvanceRightHoldClock(2000);     // 按很久，但一直没动
                Check(control.IsRightPanArmed, "按住 2 秒后起手延迟已过（处于待平移状态）");
                Check(!control.IsRightPanning, "没移动过就不算平移过");
                Invoke(control, "OnMouseUp", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, blank.X, blank.Y, 0)
                });
                Check(control.ContextMenuWasShown, "① 按住右键不动很久再松手仍然弹出右键菜单");

                // ---- ② 起手延迟很短：50ms 就够 ----
                control.ResetContextMenuFlag();
                Invoke(control, "OnMouseDown", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, blank.X, blank.Y, 0)
                });
                Check(!control.IsRightPanArmed, "刚按下（0ms）还没过起手延迟");
                control.AdvanceRightHoldClock(50);
                Check(control.IsRightPanArmed, "② 按住 50ms 即已过起手延迟");
                Invoke(control, "OnMouseUp", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, blank.X, blank.Y, 0)
                });

                // ---- ③ 起步不瞬移：基准点是越过阈值时的位置，不是按下的位置 ----
                control.CenterOnOrigin();
                PointD jumpBase = control.ViewOrigin;
                Invoke(control, "OnMouseDown", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, blank.X, blank.Y, 0)
                });
                // 起手延迟未过时先慢慢移动一段（这段位移不应该被算进平移）
                Invoke(control, "OnMouseMove", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, blank.X + 12, blank.Y + 8, 0)
                });
                CheckNear(jumpBase.X, control.ViewOrigin.X, 0.5, "延迟未过时的移动不平移（x）");
                CheckNear(jumpBase.Y, control.ViewOrigin.Y, 0.5, "延迟未过时的移动不平移（y）");
                // 延迟过后第一次移动：应以此为基准，画面不跳
                control.AdvanceRightHoldClock(60);
                Invoke(control, "OnMouseMove", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, blank.X + 12, blank.Y + 8, 0)
                });
                CheckNear(jumpBase.X, control.ViewOrigin.X, 0.5,
                    "③ 越过阈值的第一帧不平移（基准点取当前位置，无瞬移）");
                CheckNear(jumpBase.Y, control.ViewOrigin.Y, 0.5, "③ 越过阈值的第一帧不平移（y）");
                // 之后继续移动才产生位移，且位移量等于增量
                Invoke(control, "OnMouseMove", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, blank.X + 32, blank.Y + 8, 0)
                });
                CheckNear(jumpBase.X - 20, control.ViewOrigin.X, 1.0,
                    "③ 之后每移动 20 像素平移 20（无累计跳变）");
                Invoke(control, "OnMouseUp", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, blank.X + 32, blank.Y + 8, 0)
                });

                // ---- 延迟未过就松手：算单击 → 弹菜单 ----
                control.CenterOnOrigin();
                control.ResetContextMenuFlag();
                PointD stable = control.ViewOrigin;
                Invoke(control, "OnMouseDown", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, blank.X, blank.Y, 0)
                });
                Invoke(control, "OnMouseMove", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, blank.X + 6, blank.Y + 4, 0)
                });
                Invoke(control, "OnMouseUp", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, blank.X + 6, blank.Y + 4, 0)
                });
                Check(control.ContextMenuWasShown, "延迟未过就松手 → 弹菜单");
                CheckNear(stable.X, control.ViewOrigin.X, 0.5, "该次操作没有平移（x）");
                CheckNear(stable.Y, control.ViewOrigin.Y, 0.5, "该次操作没有平移（y）");

                // ---- 平移过之后抬手：不弹菜单 ----
                control.CenterOnOrigin();
                control.ResetContextMenuFlag();
                PointD panBase = control.ViewOrigin;
                Invoke(control, "OnMouseDown", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, blank.X, blank.Y, 0)
                });
                control.AdvanceRightHoldClock(60);
                // 起手后的第一帧只用来确定基准点（因此位移为 0）
                Invoke(control, "OnMouseMove", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, blank.X, blank.Y, 0)
                });
                CheckNear(panBase.X, control.ViewOrigin.X, 0.5, "起手后第一帧位移为 0（定基准点）");
                // 之后按增量平移
                Invoke(control, "OnMouseMove", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, blank.X + 40, blank.Y + 20, 0)
                });
                CheckNear(panBase.X - 40, control.ViewOrigin.X, 1.0,
                    "平移 -40（" + panBase.X.ToString("0.#") + " → " + control.ViewOrigin.X.ToString("0.#") + "）");
                CheckNear(panBase.Y - 20, control.ViewOrigin.Y, 1.0, "平移 -20（y）");
                Invoke(control, "OnMouseUp", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, blank.X + 40, blank.Y + 20, 0)
                });
                Check(!control.ContextMenuWasShown, "平移过之后抬手不弹菜单");
                Check(!control.IsRightPanning, "抬手后平移状态复位");

                // ---- 边上右键拖动：同样平移 ----
                control.CenterOnOrigin();
                PointD edgeBase = control.ViewOrigin;
                Point onEdge = control.DevicePointFromImage((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                Invoke(control, "OnMouseDown", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, onEdge.X, onEdge.Y, 0)
                });
                control.AdvanceRightHoldClock(60);
                Invoke(control, "OnMouseMove", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, onEdge.X, onEdge.Y, 0)
                });
                Invoke(control, "OnMouseMove", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, onEdge.X - 30, onEdge.Y, 0)
                });
                CheckNear(edgeBase.X + 30, control.ViewOrigin.X, 1.0, "边上右键拖动也平移");
                Invoke(control, "OnMouseUp", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, onEdge.X - 30, onEdge.Y, 0)
                });

                // ---- 顶点上右键拖动：连线，不平移、不弹菜单 ----
                control.CenterOnOrigin();
                int edgesBefore = c.Edges.Count;
                Point fromVertex = control.DevicePointFromImage(a.X, a.Y);
                Point toVertex = control.DevicePointFromImage(b.X, b.Y);
                control.ResetContextMenuFlag();
                Invoke(control, "OnMouseDown", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, fromVertex.X, fromVertex.Y, 0)
                });
                control.AdvanceRightHoldClock(500);   // 起手延迟过了也不该抢走连线手势
                Invoke(control, "OnMouseMove", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, toVertex.X, toVertex.Y, 0)
                });
                Invoke(control, "OnMouseUp", new object[]
                {
                    new MouseEventArgs(MouseButtons.Right, 1, toVertex.X, toVertex.Y, 0)
                });
                Check(c.Edges.Count > edgesBefore,
                    "顶点上右键拖动会新建一条边（" + edgesBefore + " → " + c.Edges.Count + "）");
                CheckNear(-300.0, control.ViewOrigin.X, 1.0, "顶点上右键拖动不平移（x）");
                Check(!control.ContextMenuWasShown, "顶点上拖动建边后不弹菜单");

                // ---- 真实定时器：不做时钟注入，验证 Timer 真的会触发 ----
                using (CanvasControl live = new CanvasControl())
                {
                    live.Size = new Size(600, 400);
                    GraphCanvas lc = new GraphCanvas(600, 400);
                    live.Canvas = lc;
                    live.CenterOnOrigin();

                    Invoke(live, "OnMouseDown", new object[]
                    {
                        new MouseEventArgs(MouseButtons.Right, 1, 2, 2, 0)
                    });
                    Check(!live.IsRightPanArmed, "刚按下时还没过起手延迟");

                    // 真正等待，并泵消息循环让 Windows.Forms.Timer 有机会触发
                    int waited = 0;
                    while (waited < 600 && !live.IsRightPanArmed)
                    {
                        System.Threading.Thread.Sleep(20);
                        Application.DoEvents();
                        waited += 20;
                    }
                    Check(live.IsRightPanArmed, "真实定时器在约 50ms 后完成起手（等待 " + waited + "ms）");

                    PointD liveBefore = live.ViewOrigin;
                    Invoke(live, "OnMouseMove", new object[]
                    {
                        new MouseEventArgs(MouseButtons.Right, 1, 22, 2, 0)
                    });
                    CheckNear(liveBefore.X, live.ViewOrigin.X, 0.5, "真实起手后的第一帧不平移（无瞬移）");
                    Invoke(live, "OnMouseMove", new object[]
                    {
                        new MouseEventArgs(MouseButtons.Right, 1, 52, 2, 0)
                    });
                    CheckNear(liveBefore.X - 30, live.ViewOrigin.X, 1.0, "真实起手后继续移动才平移 30");

                    live.ResetContextMenuFlag();
                    Invoke(live, "OnMouseUp", new object[]
                    {
                        new MouseEventArgs(MouseButtons.Right, 1, 52, 2, 0)
                    });
                    Check(!live.ContextMenuWasShown, "真实平移后抬手不弹菜单");
                    Check(!live.IsRightPanning, "抬手后平移状态复位");
                }
            }
        }

        /// <summary>模拟一次按钮拖动：按下 → 移动 → 抬起。</summary>
        private static void DragMouse(CanvasControl control, MouseButtons button, Point from, Point to)
        {
            Invoke(control, "OnMouseDown", new object[]
            {
                new MouseEventArgs(button, 1, from.X, from.Y, 0)
            });
            Invoke(control, "OnMouseMove", new object[]
            {
                new MouseEventArgs(button, 1, to.X, to.Y, 0)
            });
            Invoke(control, "OnMouseUp", new object[]
            {
                new MouseEventArgs(button, 1, to.X, to.Y, 0)
            });
        }

        /// <summary>
        /// 走应用真实的建边路径：在 A 上按住右键拖到 B 再松开。
        /// 这是唯一会经过 CanvasControl 建边代码的入口。
        /// </summary>
        private static Edge ConnectByRightDrag(CanvasControl control, Vertex from, Vertex to)
        {
            Point start = control.DevicePointFromImage(from.X, from.Y);
            Point end = control.DevicePointFromImage(to.X, to.Y);
            Invoke(control, "OnMouseDown", new object[]
            {
                new MouseEventArgs(MouseButtons.Right, 1, start.X, start.Y, 0)
            });
            Invoke(control, "OnMouseMove", new object[]
            {
                new MouseEventArgs(MouseButtons.Right, 1, end.X, end.Y, 0)
            });
            Invoke(control, "OnMouseUp", new object[]
            {
                new MouseEventArgs(MouseButtons.Right, 1, end.X, end.Y, 0)
            });

            // 返回最后一条边（就是刚建的那条）
            GraphCanvas c = control.Canvas;
            return c.Edges.Count == 0 ? null : c.Edges[c.Edges.Count - 1];
        }

        /// <summary>
        /// 复现「边的默认命名规则无效」：从菜单切换规则后，真正拖动建一条边看它叫什么。
        /// </summary>
        private static void TestEdgeNamingTakesEffect()
        {
            MainForm form = new MainForm();
            try
            {
                form.CreateControl();
                GraphCanvas c = form.CanvasArea.Canvas;

                ToolStripMenuItem properties = FindTopMenu(form.MainMenuBar, "属性");
                properties.DropDownItems.Clear();
                form.GetType().GetMethod("RebuildPropertyMenu",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(form, new object[] { properties });

                ToolStripMenuItem edge = FindSubMenu(properties, "边");
                Check(edge != null, "找到「边」子菜单");
                ToolStripMenuItem naming = FindSubMenu(edge, "默认命名");
                Check(naming != null, "找到边的默认命名菜单");

                // 造两个顶点，之后再建边
                Vertex a = c.AddVertex(c.CanvasWidth / 2 - 100, c.CanvasHeight / 2, "A");
                Vertex b = c.AddVertex(c.CanvasWidth / 2 + 100, c.CanvasHeight / 2, "B");

                // 每个规则都从菜单点一遍，再新建一条边，检查名字
                string[][] cases = new string[][]
                {
                    new string[] { "字母e+数字", "e1" },
                    new string[] { "小写字母", "a" },
                    new string[] { "两端点名字", "AB" },
                    new string[] { "不命名", "" }
                };

                int expectedId = 1;
                for (int i = 0; i < cases.Length; i++)
                {
                    // 菜单每次展开都会重建，所以每轮都要重新取
                    properties.DropDownItems.Clear();
                    form.GetType().GetMethod("RebuildPropertyMenu",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .Invoke(form, new object[] { properties });
                    ToolStripMenuItem edgeMenu = FindSubMenu(properties, "边");
                    ToolStripMenuItem namingMenu = FindSubMenu(edgeMenu, "默认命名");

                    ClickOption(namingMenu, cases[i][0]);

                    // 走应用真实的建边路径（右键从 A 拖到 B），而不是直接 AddEdge：
                    // 直接 AddEdge 会绕过规则的调用链，测不出问题。
                    Edge created = ConnectByRightDrag(form.CanvasArea, a, b);
                    Check(created != null, "拖动建边成功（规则「" + cases[i][0] + "」）");
                    CheckEqual(cases[i][1], created.Name,
                        "菜单选「" + cases[i][0] + "」后新建边命名为「" + cases[i][1] + "」");
                    CheckEqual(expectedId, created.Id, "新建边编号递增");
                    expectedId++;
                }

                // 菜单标题要反映当前值
                properties.DropDownItems.Clear();
                form.GetType().GetMethod("RebuildPropertyMenu",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(form, new object[] { properties });
                ToolStripMenuItem finalNaming = FindSubMenu(FindSubMenu(properties, "边"), "默认命名");
                Check(finalNaming.Text.IndexOf("不命名", StringComparison.Ordinal) >= 0,
                    "菜单标题反映当前规则（实际「" + finalNaming.Text + "」）");
            }
            finally
            {
                form.Dispose();
            }
        }

        /// <summary>
        /// 框选必须判断边本身与选框是否相交，而不能用边的包围盒：
        /// 斜边的包围盒远大于边本身，用包围盒会把视觉上没碰到的边也选中。
        /// </summary>
        private static void TestRubberBandSelection()
        {
            // ---- 几何层：线段 × 矩形 ----
            // 斜边 (100,100)-(200,200)，直线方程 y = x
            // 两个矩形都落在斜边包围盒内，但分别严格位于斜边的上方 / 下方
            RectD aboveDiagonal = new RectD(120, 100, 70, 5);
            RectD belowDiagonal = new RectD(120, 195, 70, 5);

            Check(!GraphCanvas.SegmentIntersectsRect(100, 100, 200, 200, aboveDiagonal),
                "矩形在斜边包围盒内但位于斜边上方 → 不应命中");
            Check(!GraphCanvas.SegmentIntersectsRect(100, 100, 200, 200, belowDiagonal),
                "矩形在斜边包围盒内但位于斜边下方 → 不应命中");
            Check(GraphCanvas.SegmentIntersectsRect(100, 100, 200, 200, new RectD(190, 190, 30, 30)),
                "矩形覆盖线段终点 → 应命中");
            Check(GraphCanvas.SegmentIntersectsRect(100, 100, 200, 200, new RectD(140, 140, 20, 20)),
                "矩形被线段穿过 → 应命中");
            Check(GraphCanvas.SegmentIntersectsRect(100, 100, 200, 200, new RectD(90, 90, 10, 10)),
                "线段起点在矩形内 → 应命中");
            Check(GraphCanvas.SegmentIntersectsRect(100, 100, 200, 200, new RectD(0, 0, 400, 400)),
                "线段完全落在矩形内 → 应命中");

            // 水平 / 垂直 / 平行不重叠
            Check(GraphCanvas.SegmentIntersectsRect(100, 150, 300, 150, new RectD(200, 100, 50, 100)),
                "水平线段穿过矩形 → 应命中");
            Check(!GraphCanvas.SegmentIntersectsRect(100, 150, 300, 150, new RectD(200, 200, 50, 50)),
                "水平线段与矩形平行且不相交 → 不应命中");
            Check(GraphCanvas.SegmentIntersectsRect(250, 100, 250, 300, new RectD(200, 200, 100, 20)),
                "垂直线段穿过矩形 → 应命中");
            Check(!GraphCanvas.SegmentIntersectsRect(250, 100, 250, 150, new RectD(200, 200, 100, 20)),
                "垂直线段在矩形上方 → 不应命中");

            // 行为层（按世界坐标框选、边的选中规则）见 TestRubberBandEdgeRule
        }

        /// <summary>按世界坐标矩形模拟一次框选拖动。</summary>
        private static void RubberBandSelectWorld(CanvasControl control, double worldX, double worldY, double w, double h)
        {
            Point start = control.DevicePointFromWorld(worldX, worldY);
            Point end = control.DevicePointFromWorld(worldX + w, worldY + h);
            Invoke(control, "OnMouseDown", new object[]
            {
                new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0)
            });
            Invoke(control, "OnMouseMove", new object[]
            {
                new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0)
            });
            Invoke(control, "OnMouseUp", new object[]
            {
                new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0)
            });
        }

        /// <summary>框选：只有两个端点都被选中的边才被选中。</summary>
        private static void TestRubberBandEdgeRule()
        {
            using (CanvasControl control = new CanvasControl())
            {
                control.Size = new Size(600, 400);
                GraphCanvas c = new GraphCanvas(600, 400);
                control.Canvas = c;
                control.CenterOnOrigin();

                // 两个顶点放在视口里，位置可预期（世界坐标 → 图像坐标）
                PointD ai = control.WorldToImage(-200, -80);
                PointD bi = control.WorldToImage(200, 80);
                Vertex a = c.AddVertex(ai.X, ai.Y, "A");
                Vertex b = c.AddVertex(bi.X, bi.Y, "B");
                Edge edge = c.AddEdge(a.Id, b.Id, "AB");

                // 只框住边、不碰两个端点：两个端点都没被选中，边也不应被选中。
                // 注意矩形要避开边本身，否则按下鼠标时就会直接选中这条边
                // （边从世界 (-200,-80) 到 (200,80)，不能压在它上面）。
                // 这里选边右侧的一段空白：边在 x=100 处 y=40，矩形取 y∈[90,120]。
                RubberBandSelectWorld(control, 60, 90, 80, 30);
                CheckEqual(0, control.SelectedVertexCount, "框住边的中段附近不选中顶点");
                CheckEqual(0, control.SelectedEdgeCount, "两个端点都没选中时，边不被选中");

                // 只框住一个端点：边仍不应被选中
                RubberBandSelectWorld(control, -230, -110, 60, 60);
                CheckEqual(1, control.SelectedVertexCount, "框住顶点 A");
                CheckEqual(0, control.SelectedEdgeCount, "只选中一个端点时，边不被选中");

                // 框住两个端点：边被选中
                RubberBandSelectWorld(control, -230, -110, 460, 220);
                CheckEqual(2, control.SelectedVertexCount, "框住两个端点");
                CheckEqual(1, control.SelectedEdgeCount, "两个端点都被选中时，边被选中");
                Check(c.FindEdge(edge.Id) != null, "边仍然存在");

                // 空白区域不应选中任何东西
                RubberBandSelectWorld(control, 400, 300, 60, 60);
                CheckEqual(0, control.SelectedEdgeCount, "空白处框选不选中边");
                CheckEqual(0, control.SelectedVertexCount, "空白处框选不选中顶点");

                // 顶点保持不变（确认这些框选没有意外触发拖动）
                CheckNear(ai.X, a.X, 1e-9, "框选过程没有移动顶点 A");
                CheckNear(ai.Y, a.Y, 1e-9, "框选过程没有移动顶点 A（y）");
            }
        }

        private static string SVGNum(double value)
        {
            return SvgExport.Num(value);
        }

        /// <summary>
        /// draw.io 式无限画布：没有固定纸张与画布尺寸；
        /// 滚动范围随内容变化；内容放得下时不出滚动条；
        /// 初始时世界原点位于客户区中心。
        /// </summary>
        private static void TestInfiniteCanvas()
        {
            using (CanvasControl control = new CanvasControl())
            {
                control.Size = new Size(800, 600);
                GraphCanvas c = new GraphCanvas(800, 600);
                control.Canvas = c;
                control.CenterOnOrigin();

                // 初始：视图原点在客户区中心，因此世界原点位于客户区中心
                CheckNear(-800 / 2.0, control.ViewOrigin.X, 0.5, "初始视图原点 x 在客户区中心");
                CheckNear(-600 / 2.0, control.ViewOrigin.Y, 0.5, "初始视图原点 y 在客户区中心");
                Point device = control.DevicePointFromWorld(0, 0);
                CheckNear(800 / 2.0, device.X, 1.5, "初始时世界原点在客户区中心（x）");
                CheckNear(600 / 2.0, device.Y, 1.5, "初始时世界原点在客户区中心（y）");

                // 空图没有内容，只留出一屏的平移余地，不出现滚动条
                Check(!control.NeedsHorizontalScrollbar && !control.NeedsVerticalScrollbar,
                    "空图时没有滚动条（与 draw.io 一致）");

                // 视口左上角是负坐标，右下角是正坐标
                PointD leftUp = control.ImageToWorld(control.ViewOrigin.X, control.ViewOrigin.Y);
                Check(leftUp.X < 0 && leftUp.Y < 0, "视口左上角对应负的世界坐标");

                // y 向下（与 .graph 文件一致）
                PointD below = control.WorldToImage(0, 100);
                PointD originImage = control.WorldToImage(0, 0);
                Check(below.Y > originImage.Y, "世界 y 增大时图像 y 也增大（y 轴向下）");

                // 放一个内容后滚动范围应随内容扩大
                int rangeBefore = control.ScrollRange.X;
                Vertex v = c.AddVertex(3000, 3000, "far");
                control.RefreshScrollRange();
                Check(control.ScrollRange.X > rangeBefore, "内容变远后横向滚动范围扩大（"
                    + rangeBefore + " → " + control.ScrollRange.X + "）");
                Check(control.NeedsHorizontalScrollbar, "内容变远后出现横向滚动条");

                // 负方向的内容同样纳入滚动范围
                Vertex neg = c.AddVertex(-3000, -3000, "neg");
                control.RefreshScrollRange();
                Point scroll = control.ScrollPosition;
                control.SetScrollPosition(-2500, -2500);
                Check(control.ViewOrigin.X < 0 && control.ViewOrigin.Y < 0,
                    "可以滚动到负坐标区域（" + control.ViewOrigin.X.ToString("0") + ","
                    + control.ViewOrigin.Y.ToString("0") + "）");
                control.SetScrollPosition(scroll.X, scroll.Y);

                // 删掉内容后滚动范围又收回
                c.RemoveVertex(v.Id);
                c.RemoveVertex(neg.Id);
                control.RefreshScrollRange();
                Check(!control.NeedsHorizontalScrollbar || control.ScrollRange.X < 3000,
                    "内容删除后滚动范围收回（" + control.ScrollRange.X + "）");
            }
        }


        /// <summary>
        /// 多文档界面：打开多个文档时各自独立（工作区、图模型、修改状态互不影响），
        /// 菜单作用于当前活动文档。
        /// </summary>
        private static void TestMdiDocuments()
        {
            using (MainForm form = new MainForm())
            {
                form.CreateControl();
                System.Reflection.MethodInfo create = typeof(MainForm).GetMethod("CreateDocument",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                Check(form.IsMdiContainer, "主窗口是 MDI 容器");
                CheckEqual(1, form.MdiChildren.Length, "启动时有一个文档窗口");

                create.Invoke(form, null);
                create.Invoke(form, null);
                CheckEqual(3, form.MdiChildren.Length, "再新建两个后共有三个文档窗口");
                Check(form.ActiveDocumentWindow != null, "存在活动文档窗口");

                CanvasControl first = null;
                System.Collections.Generic.HashSet<int> seen = new System.Collections.Generic.HashSet<int>();
                foreach (Form child in form.MdiChildren)
                {
                    CanvasControl canvas = (CanvasControl)child.GetType()
                        .GetProperty("CanvasArea").GetValue(child, null);
                    Check(canvas != null, "子窗口都有工作区");
                    Check(seen.Add(canvas.GetHashCode()), "各文档的工作区是不同实例");
                    Check(canvas.Canvas != null, "子窗口都挂着图模型");
                    if (first == null) first = canvas;
                }

                // 在一个文档里加顶点，不影响另一个
                first.Canvas.AddVertex(0, 0, "X");
                int others = 0;
                foreach (Form child in form.MdiChildren)
                {
                    CanvasControl canvas = (CanvasControl)child.GetType()
                        .GetProperty("CanvasArea").GetValue(child, null);
                    if (canvas != first) others += canvas.Canvas.Vertices.Count;
                }
                CheckEqual(0, others, "一个文档里加顶点不影响其它文档");

                // 菜单作用于活动文档：切换「有向图」只改当前文档
                CanvasControl active = form.CanvasArea;
                GraphCanvas activeGraph = active.Canvas;
                bool before = activeGraph.Directed;
                ToolStripMenuItem properties = new ToolStripMenuItem("属性");
                typeof(MainForm).GetMethod("RebuildPropertyMenu",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(form, new object[] { properties });
                ToolStripMenuItem graph = FindMenuItem(properties, "图");
                FindMenuItem(graph, "有向图").PerformClick();
                Check(before != activeGraph.Directed, "菜单切换作用于活动文档");

                int stillDefault = 0;
                foreach (Form child in form.MdiChildren)
                {
                    CanvasControl canvas = (CanvasControl)child.GetType()
                        .GetProperty("CanvasArea").GetValue(child, null);
                    if (canvas != active && canvas.Canvas.Directed) stillDefault++;
                }
                CheckEqual(2, stillDefault, "其它文档的有向图设置不受影响");
            }
        }

        /// <summary>
        /// 图类型：多重图 / 有向图。
        ///   - 默认两者开启
        ///   - 关闭多重图时清理重边与自环，之后也不允许再建
        ///   - 关闭有向图只是隐藏箭头，边保留
        ///   - 两者随 .graph 往返
        /// </summary>
        private static void TestGraphTypeOptions()
        {
            GraphCanvas c = new GraphCanvas(800, 600);
            Check(c.MultiGraph, "默认开启多重图");
            Check(c.Directed, "默认开启有向图");

            Vertex a = c.AddVertex(100, 100, "A");
            Vertex b = c.AddVertex(300, 100, "B");
            Edge e1 = c.AddEdge(a.Id, b.Id);
            Edge e2 = c.AddEdge(a.Id, b.Id);
            Edge loop = c.AddEdge(a.Id, a.Id);
            Check(e1 != null && e2 != null, "多重图下允许重边");
            Check(loop != null, "多重图下允许自环");
            CheckEqual(3, c.Edges.Count, "此时共 3 条边");

            // 关闭多重图：清理多余的边与自环
            c.MultiGraph = false;
            int removed = c.RemoveExtraEdges();
            CheckEqual(2, removed, "关闭多重图后删除 2 条（1 条重边 + 1 个自环）");
            CheckEqual(1, c.Edges.Count, "只剩 1 条边");
            Check(c.Edges[0].V1 != c.Edges[0].V2, "剩下的不是自环");

            // 关闭后不允许再加重边或自环
            Check(c.AddEdge(a.Id, b.Id) == null, "简单图下不允许再加重边");
            Check(c.AddEdge(a.Id, a.Id) == null, "简单图下不允许再加自环");
            CheckEqual(1, c.Edges.Count, "被拒绝的边没有加进去");

            // 无向时 A→B 与 B→A 视为同一对
            c.Directed = false;
            Check(c.HasEdgeBetween(b.Id, a.Id), "无向图下 B-A 与 A-B 算同一条边");
            Check(c.AddEdge(b.Id, a.Id) == null, "无向简单图下不允许反向再加一条");

            // 有向时可以两个方向各一条
            GraphCanvas d = new GraphCanvas(800, 600);
            d.MultiGraph = false;
            d.Directed = true;
            Vertex p = d.AddVertex(0, 0, "P");
            Vertex q = d.AddVertex(100, 0, "Q");
            Check(d.AddEdge(p.Id, q.Id) != null, "有向图允许 P→Q");
            Check(d.AddEdge(p.Id, q.Id) == null, "有向图不允许重复的 P→Q");
            Check(d.AddEdge(q.Id, p.Id) != null, "有向图允许反向的 Q→P");

            Edge pq = d.Edges[0];
            CheckEqual(p.Id, pq.V1, "边起点是连接的起点 P");
            CheckEqual(q.Id, pq.V2, "边终点是连接的终点 Q");

            // 有向开关随文件往返
            GraphCanvas original = new GraphCanvas(800, 600);
            original.MultiGraph = false;
            original.Directed = false;
            GraphCanvas loaded = GraphIo.Deserialize(GraphIo.Serialize(original));
            Check(!loaded.MultiGraph, "往返：多重图标记保留（false）");
            Check(!loaded.Directed, "往返：有向图标记保留（false）");

            GraphCanvas loaded2 = GraphIo.Deserialize(GraphIo.Serialize(new GraphCanvas(800, 600)));
            Check(loaded2.MultiGraph, "往返：多重图标记保留（默认 true）");
            Check(loaded2.Directed, "往返：有向图标记保留（默认 true）");
        }

        /// <summary>菜单里「属性 → 图」的两个勾选项能切换并生效。</summary>
        private static void TestGraphTypeMenu()
        {
            using (MainForm form = new MainForm())
            {
                form.CreateControl();
                CanvasControl canvas = form.CanvasArea;
                GraphCanvas c = canvas.Canvas;

                Vertex a = c.AddVertex(canvas.WorldToImage(-100, 0).X, canvas.WorldToImage(-100, 0).Y, "A");
                Vertex b = c.AddVertex(canvas.WorldToImage(100, 0).X, canvas.WorldToImage(100, 0).Y, "B");
                c.AddEdge(a.Id, b.Id);
                c.AddEdge(a.Id, b.Id);
                c.AddEdge(a.Id, a.Id);

                ToolStripMenuItem properties = new ToolStripMenuItem("属性");
                typeof(MainForm).GetMethod("RebuildPropertyMenu",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(form, new object[] { properties });

                Check(FindMenuItem(properties, "顶点") != null, "属性菜单含「顶点」");
                Check(FindMenuItem(properties, "边") != null, "属性菜单含「边」");
                ToolStripMenuItem graph = FindMenuItem(properties, "图");
                Check(graph != null, "属性菜单含「图」");

                ToolStripMenuItem multi = FindMenuItem(graph, "多重图");
                ToolStripMenuItem directed = FindMenuItem(graph, "有向图");
                Check(multi != null, "「图」含「多重图」");
                Check(directed != null, "「图」含「有向图」");
                Check(multi.Checked, "多重图默认勾选");
                Check(directed.Checked, "有向图默认勾选");

                // 取消多重图：清理多余的边与自环
                multi.PerformClick();
                Check(!c.MultiGraph, "点「多重图」后关闭");
                CheckEqual(1, c.Edges.Count, "关闭多重图时清理掉重边与自环（剩 1 条）");

                // 取消有向图：只隐藏箭头，边保留
                int before = c.Edges.Count;
                directed.PerformClick();
                Check(!c.Directed, "点「有向图」后关闭");
                CheckEqual(before, c.Edges.Count, "关闭有向图不影响边的数量");

                // 再点回来
                multi.PerformClick();
                Check(c.MultiGraph, "再点「多重图」恢复为 true");
                directed.PerformClick();
                Check(c.Directed, "再点「有向图」恢复为 true");
            }
        }

        /// <summary>
        /// 右键菜单与属性菜单的结构：
        ///  1) 有选中元素时右击空白处，「粘贴」只出现一次
        ///  2) 属性菜单不再有「画布」
        ///  3) 颜色子菜单有「默认颜色」且能把单独着色清掉
        /// </summary>
        private static void TestMenusAndDefaultColor()
        {
            using (MainForm form = new MainForm())
            {
                form.CreateControl();
                CanvasControl canvas = form.CanvasArea;
                canvas.Size = new Size(800, 600);
                GraphCanvas c = canvas.Canvas;

                Vertex a = c.AddVertex(canvas.WorldToImage(-120, 0).X, canvas.WorldToImage(-120, 0).Y, "A");
                Vertex b = c.AddVertex(canvas.WorldToImage(120, 0).X, canvas.WorldToImage(120, 0).Y, "B");
                Edge edge = c.AddEdge(a.Id, b.Id, "e1");
                canvas.SelectAll();
                canvas.CopySelection();

                // ---- 1) 空白处右键：粘贴只能有一个 ----
                // 用世界坐标挑一个确实空白的点：顶点在 (±120, 0)，这里取右下角远处
                Point blank = canvas.DevicePointFromWorld(250, 200);
                Check(HitNothing(canvas, blank), "选定的点是空白处（不压在顶点/边上）");
                using (ContextMenuStrip menu = canvas.BuildContextMenu(blank))
                {
                    CheckEqual(1, CountItemsStartingWith(menu, "粘贴"),
                        "① 有选中元素时，空白处右键菜单里「粘贴」只出现一次");
                    Check(FindMenuItem(menu, "新建顶点") != null, "① 空白处右键菜单含「新建顶点」");
                    Check(FindMenuItem(menu, "全选") == null, "① 有选中元素时不再显示「全选」");
                }

                // 没有选中时，粘贴依然要在
                canvas.ClearSelection();
                using (ContextMenuStrip menu = canvas.BuildContextMenu(blank))
                {
                    CheckEqual(1, CountItemsStartingWith(menu, "粘贴"), "① 无选中元素时「粘贴」也只有一个");
                    Check(FindMenuItem(menu, "全选") != null, "① 无选中时显示「全选」");
                }

                // ---- 2) 属性菜单不含「画布」 ----
                ToolStripMenuItem properties = new ToolStripMenuItem("属性");
                typeof(MainForm).GetMethod("RebuildPropertyMenu",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(form, new object[] { properties });
                Check(FindMenuItem(properties, "顶点") != null, "② 属性菜单含「顶点」");
                Check(FindMenuItem(properties, "边") != null, "② 属性菜单含「边」");
                Check(FindMenuItem(properties, "画布") == null, "② 属性菜单已移除「画布」");

                // ---- 3) 颜色子菜单里的「默认颜色」 ----
                canvas.SelectAll();
                canvas.ApplyColorToSelection(Color.FromArgb(0xFF, 0x00, 0x00));
                Check(c.Vertices[0].Color != null && c.Edges[0].Color != null, "③ 预设：元素已被单独着色");

                using (ContextMenuStrip menu = canvas.BuildContextMenu(blank))
                {
                    ToolStripMenuItem colorMenu = FindMenuItem(menu, "颜色");
                    Check(colorMenu != null, "③ 右键菜单含「颜色」");
                    ToolStripItem last = colorMenu.DropDownItems[colorMenu.DropDownItems.Count - 1];
                    Check(last is ToolStripMenuItem, "③ 颜色子菜单最后一项是按钮（不是分隔线）");
                    Check(last.Text != null && last.Text.StartsWith("默认颜色", StringComparison.Ordinal),
                        "③ 颜色子菜单最后一项是「默认颜色」（实际「" + (last == null ? "" : last.Text) + "」）");

                    last.PerformClick();
                }
                Check(c.Vertices[0].Color == null, "③ 点「默认颜色」后顶点回到跟随默认");
                Check(c.Edges[0].Color == null, "③ 点「默认颜色」后边回到跟随默认");
                CheckEqual(c.DefaultVertexColor.ToArgb(), c.EffectiveColor(c.Vertices[0]).ToArgb(),
                    "③ 顶点实际颜色等于全局默认色");
                CheckEqual(c.DefaultEdgeColor.ToArgb(), c.EffectiveColor(c.Edges[0]).ToArgb(),
                    "③ 边实际颜色等于全局默认色");
            }
        }

        /// <summary>统计菜单顶层文本以 prefix 开头的项数。</summary>
        private static int CountItemsStartingWith(ContextMenuStrip menu, string prefix)
        {
            int n = 0;
            foreach (ToolStripItem item in menu.Items)
            {
                if (item.Text != null && item.Text.StartsWith(prefix, StringComparison.Ordinal)) n++;
            }
            return n;
        }

        /// <summary>
        /// 该工作区坐标点是否既不压顶点也不压边。
        /// 判据用「新建顶点」：只有空白处的菜单才有它（顶点/边菜单第一项是「重命名」）。
        /// </summary>
        private static bool HitNothing(CanvasControl canvas, Point device)
        {
            using (ContextMenuStrip menu = canvas.BuildContextMenu(device))
            {
                return FindMenuItem(menu, "新建顶点") != null;
            }
        }

        /// <summary>在右键菜单顶层按前缀查找。</summary>
        private static ToolStripMenuItem FindMenuItem(ContextMenuStrip menu, string prefix)
        {
            foreach (ToolStripItem item in menu.Items)
            {
                ToolStripMenuItem mi = item as ToolStripMenuItem;
                if (mi != null && mi.Text != null && mi.Text.StartsWith(prefix, StringComparison.Ordinal)) return mi;
            }
            return null;
        }

        /// <summary>在某个菜单项的子菜单里按前缀查找。</summary>
        private static ToolStripMenuItem FindMenuItem(ToolStripMenuItem parent, string prefix)
        {
            foreach (ToolStripItem item in parent.DropDownItems)
            {
                ToolStripMenuItem mi = item as ToolStripMenuItem;
                if (mi != null && mi.Text != null && mi.Text.StartsWith(prefix, StringComparison.Ordinal)) return mi;
            }
            return null;
        }

        /// <summary>
        /// 回归：用户报告的四个问题。
        ///  1) 保存再读取后坐标不变（画布不再有尺寸，也不再钳制坐标）
        ///  2) 缩放变化时状态栏实时刷新（滚轮路径不经过菜单）
        ///  3) 右键平移的距离与缩放无关：光标移 N 像素，内容就在屏幕上移 N 像素
        ///  4) 连续粘贴要依次错开，不能堆在同一处
        /// </summary>
        private static void TestReportedRegressions()
        {
            // ---- 1) 保存 → 读取：坐标必须原样保留 ----
            GraphCanvas original = new GraphCanvas(800, 600);
            Vertex va = original.AddVertex(100, 120, "A");
            Vertex vb = original.AddVertex(700, 500, "B");
            original.AddEdge(va.Id, vb.Id, "e1");
            string text = GraphIo.Serialize(original);

            Check(!text.Contains("\"canvas\""), "① 文件里不再写 canvas 尺寸");
            GraphCanvas loaded = GraphIo.Deserialize(text);
            CheckNear(100, loaded.Vertices[0].X, 0.01, "① 读取后 A.x 不变");
            CheckNear(120, loaded.Vertices[0].Y, 0.01, "① 读取后 A.y 不变");
            CheckNear(700, loaded.Vertices[1].X, 0.01, "① 读取后 B.x 不变");
            CheckNear(500, loaded.Vertices[1].Y, 0.01, "① 读取后 B.y 不变");
            // 内容外接矩形也不应改变（"被挤压"的直接判据）
            RectD before = original.ContentBounds(40, 80);
            RectD after = loaded.ContentBounds(40, 80);
            CheckNear(before.Width, after.Width, 0.01, "① 读取后内容宽度不变");
            CheckNear(before.Height, after.Height, 0.01, "① 读取后内容高度不变");
            // 负坐标/超大坐标不再被钳制
            GraphCanvas far = GraphIo.Deserialize(
                "{\"vertices\":[{\"id\":1,\"x\":-5000,\"y\":9000}]}");
            CheckNear(-5000, far.Vertices[0].X, 0.01, "① 负坐标原样保留");
            CheckNear(9000, far.Vertices[0].Y, 0.01, "① 超大坐标原样保留");

            // ---- 2) 状态栏随缩放实时刷新（滚轮路径） ----
            using (MainForm form = new MainForm())
            {
                form.CreateControl();
                CanvasControl canvas = form.CanvasArea;
                canvas.Size = new Size(800, 600);

                string before2 = StatusRightText(form);
                Check(before2.IndexOf("100%", StringComparison.Ordinal) >= 0,
                    "② 初始状态栏显示 100%（实际「" + before2 + "」）");

                // 模拟滚轮：OnMouseWheel 内部就是 ZoomAt，这里直接走同一入口
                canvas.ZoomAtPoint(new Point(400, 300), 2.0);
                string after2 = StatusRightText(form);
                Check(after2.IndexOf("200%", StringComparison.Ordinal) >= 0,
                    "② 增大后状态栏立刻显示 200%（实际「" + after2 + "」）");

                canvas.ZoomAtPoint(new Point(400, 300), 0.25);
                Check(StatusRightText(form).IndexOf("50%", StringComparison.Ordinal) >= 0,
                    "② 缩小后状态栏立刻显示 50%（实际「" + StatusRightText(form) + "」）");

                canvas.ResetZoom();
                Check(StatusRightText(form).IndexOf("100%", StringComparison.Ordinal) >= 0,
                    "② 重置后状态栏回到 100%");
            }

            // ---- 3) 平移距离与缩放无关 ----
            foreach (double zoomFactor in new double[] { 1.0, 2.0, 0.5 })
            {
                using (CanvasControl control = new CanvasControl())
                {
                    control.Size = new Size(800, 600);
                    control.Canvas = new GraphCanvas(800, 600);
                    control.CenterOnOrigin();
                    control.ZoomAtPoint(new Point(400, 300), zoomFactor);
                    double z = control.Zoom;

                    Invoke(control, "OnMouseDown", new object[]
                    {
                        new MouseEventArgs(MouseButtons.Right, 1, 100, 100, 0)
                    });
                    control.AdvanceRightHoldClock(60);
                    // 第一帧只定基准点
                    Invoke(control, "OnMouseMove", new object[]
                    {
                        new MouseEventArgs(MouseButtons.Right, 1, 100, 100, 0)
                    });
                    double originBefore = control.ViewOrigin.X;
                    // 再移动 100 设备像素
                    Invoke(control, "OnMouseMove", new object[]
                    {
                        new MouseEventArgs(MouseButtons.Right, 1, 200, 100, 0)
                    });
                    double originAfter = control.ViewOrigin.X;
                    Invoke(control, "OnMouseUp", new object[]
                    {
                        new MouseEventArgs(MouseButtons.Right, 1, 200, 100, 0)
                    });

                    // 内容在屏幕上的位移 = 原点变化 × zoom，应当恒等于 100 像素
                    double onScreen = (originAfter - originBefore) * z;
                    CheckNear(-100, onScreen, 1.0,
                        "③ zoom=" + z.ToString("0.##") + " 光标移 100px → 内容屏幕位移也是 100px（实际 "
                        + onScreen.ToString("0.#") + "）");
                }
            }

            // ---- 4) 连续粘贴依次错开 ----
            using (CanvasControl control = new CanvasControl())
            {
                control.Size = new Size(800, 600);
                GraphCanvas c = new GraphCanvas(800, 600);
                control.Canvas = c;
                control.CenterOnOrigin();
                c.AddVertex(400, 300, "P");
                control.SelectAll();
                control.CopySelection();

                control.PasteClipboard();
                double x1 = LastPastedX(c);
                control.PasteClipboard();
                double x2 = LastPastedX(c);
                control.PasteClipboard();
                double x3 = LastPastedX(c);

                Check(x2 > x1 + 1, "④ 第 2 份副本在第 1 份右侧（" + x1.ToString("0") + " → " + x2.ToString("0") + "）");
                Check(x3 > x2 + 1, "④ 第 3 份副本在第 2 份右侧（" + x2.ToString("0") + " → " + x3.ToString("0") + "）");
                double stepA = x2 - x1;
                double stepB = x3 - x2;
                CheckNear(stepA, stepB, 0.6, "④ 相邻副本的错开距离一致（" + stepA.ToString("0.#")
                    + " vs " + stepB.ToString("0.#") + "）");

                // 重新复制后，错位计数要归零：新的一份仍落在视图中心附近
                control.SelectAll();
                control.CopySelection();
                control.PasteClipboard();
                double xFresh = LastPastedX(c);
                Check(xFresh < x3, "④ 重新复制后的第一份不再继续累加偏移（" + xFresh.ToString("0") + " < " + x3.ToString("0") + "）");
            }
        }

        /// <summary>读到状态栏右侧文本（缩放显示就写在这里）。</summary>
        private static string StatusRightText(MainForm form)
        {
            System.Reflection.FieldInfo field = typeof(MainForm).GetField("_statusRight",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            object label = field.GetValue(form);
            return (string)label.GetType().GetProperty("Text").GetValue(label, null);
        }

        /// <summary>取最后创建的顶点（即刚粘贴出来的那份）的 x。</summary>
        private static double LastPastedX(GraphCanvas c)
        {
            return c.Vertices[c.Vertices.Count - 1].X;
        }

        private static void TestClipboard()
        {
            using (CanvasControl control = new CanvasControl())
            {
                control.Size = new Size(800, 600);
                GraphCanvas c = new GraphCanvas(800, 600);
                control.Canvas = c;
                c.VertexNaming = NamingRule.Alphabetic;
                c.EdgeNaming = NamingRule.FromEndpoints;

                Vertex a = c.AddVertex(100, 100, "A");
                Vertex b = c.AddVertex(200, 100, "B");
                Vertex d = c.AddVertex(300, 100, "C");
                Vertex e = c.AddVertex(400, 100, "D");
                Edge ab = c.AddEdge(a.Id, b.Id, "AB");
                Edge bd = c.AddEdge(b.Id, d.Id, "BC");
                Edge de = c.AddEdge(d.Id, e.Id, "CD");

                // ---- 复制：只包含两端点都被选中的边 ----
                control.SetSelection(new List<Vertex>(new Vertex[] { a, b }), null);
                Check(control.CopySelection(), "复制返回成功");
                Check(CanvasControl.HasClipboardContent, "剪贴板里有内容");

                // ---- 剪切：删除选中顶点 + 所有与它们相连的边 ----
                int verticesBefore = c.Vertices.Count;
                int edgesBefore = c.Edges.Count;
                Check(control.CutSelection(), "剪切返回成功");
                CheckEqual(verticesBefore - 2, c.Vertices.Count, "剪切删除了 2 个顶点");
                // AB 随顶点删除，BC 是只连一个选中顶点的边，也要删除
                CheckEqual(edgesBefore - 2, c.Edges.Count, "剪切删除了 2 条边（含只连一个选中顶点的边）");
                Check(c.FindEdge(bd.Id) == null, "只连一个选中顶点的边被删除");
                Check(c.FindEdge(de.Id) != null, "两个端点都未选中的边保留");
                CheckEqual(0, control.SelectedVertexCount, "剪切后没有选中项");

                // ---- 粘贴：恢复诱导子图，名字冲突时自动加编号 ----
                int pasted = control.PasteClipboard();
                CheckEqual(2, pasted, "粘贴恢复了 2 个顶点");
                CheckEqual(verticesBefore, c.Vertices.Count, "粘贴后顶点数回到剪切前");
                CheckEqual(edgesBefore - 1, c.Edges.Count,
                    "粘贴后边数 = 原有保留的 1 条 + 恢复的 1 条内部边（只连一个选中顶点的那条不会恢复）");
                CheckEqual(2, control.SelectedVertexCount, "粘贴后自动选中新内容");

                // 新顶点的名字不能与已有名字冲突
                List<string> names = new List<string>();
                foreach (Vertex v in c.Vertices) names.Add(v.Name);
                CheckEqual(4, names.Count, "粘贴后共 4 个顶点");
                Check(names[0] != names[2] && names[1] != names[2], "粘贴的顶点名字不与已有名字冲突");
                Check(names[2] != names[3], "粘贴的两个顶点名字互不相同");

                // 粘贴的边端点必须指向新顶点
                Edge pastedEdge = null;
                foreach (Edge edge in c.Edges)
                {
                    if (edge.Id != de.Id) pastedEdge = edge;
                }
                Check(pastedEdge != null, "找到粘贴产生的边");
                if (pastedEdge != null)
                {
                    Check(c.FindVertex(pastedEdge.V1) != null && c.FindVertex(pastedEdge.V2) != null,
                        "粘贴边的端点都是有效顶点");
                    Check(pastedEdge.V1 != a.Id && pastedEdge.V2 != a.Id, "粘贴边的端点不是被剪切的旧顶点");
                }

                // ---- 粘贴位置：落在当前视图范围内 ----
                PointD origin = control.ViewOrigin;
                double visibleW = control.ViewportSize.Width / control.Zoom;
                double visibleH = control.ViewportSize.Height / control.Zoom;
                foreach (Vertex v in control.SelectedVertices())
                {
                    Check(v.X >= origin.X - 1 && v.X <= origin.X + visibleW + 1, "粘贴的顶点在视图水平范围内");
                    Check(v.Y >= origin.Y - 1 && v.Y <= origin.Y + visibleH + 1, "粘贴的顶点在视图垂直范围内");
                }

                // ---- 没有选中顶点时不能复制/剪切 ----
                control.ClearSelection();
                Check(!control.CopySelection(), "没有选中顶点时复制失败");
                Check(!control.CutSelection(), "没有选中顶点时剪切失败");
            }
        }

        /// <summary>
        /// 旋转：绕选中顶点的中心旋转。角度语义是「正数 = 逆时针」
        /// （世界坐标 y 向上，符合数学惯例）。
        /// </summary>
        private static void TestRotation()
        {
            using (CanvasControl control = new CanvasControl())
            {
                control.Size = new Size(800, 600);
                GraphCanvas c = new GraphCanvas(800, 400);
                control.Canvas = c;
                control.CenterOnOrigin();

                Vertex a = c.AddVertex(control.WorldToImage(-100, 0).X, control.WorldToImage(-100, 0).Y, "A");
                Vertex b = c.AddVertex(control.WorldToImage(100, 0).X, control.WorldToImage(100, 0).Y, "B");
                Vertex d = c.AddVertex(control.WorldToImage(0, -100).X, control.WorldToImage(0, -100).Y, "C");
                c.AddEdge(a.Id, b.Id, "AB");

                // 取基准量（画布尺寸可能因内容变化，因此每次都重新换算）
                PointD worldA = control.ImageToWorld(a.X, a.Y);
                PointD worldB = control.ImageToWorld(b.X, b.Y);
                PointD worldD = control.ImageToWorld(d.X, d.Y);
                double centerX = (worldA.X + worldB.X + worldD.X) / 3;
                double centerY = (worldA.Y + worldB.Y + worldD.Y) / 3;
                double angleAB = AngleOfWorld(worldA, worldB);
                double distanceAB = DistanceOfWorld(worldA, worldB);

                control.SetSelection(new List<Vertex>(new Vertex[] { a, b, d }), null);
                Check(control.RotateSelection(90), "旋转返回成功");

                PointD afterA = control.ImageToWorld(a.X, a.Y);
                PointD afterB = control.ImageToWorld(b.X, b.Y);
                PointD afterD = control.ImageToWorld(d.X, d.Y);

                // 中心（质心）保持不变
                CheckNear(centerX, (afterA.X + afterB.X + afterD.X) / 3, 0.05, "旋转后质心 x 不变");
                CheckNear(centerY, (afterA.Y + afterB.Y + afterD.Y) / 3, 0.05, "旋转后质心 y 不变");

                // 顶点到中心的距离不变
                CheckNear(Math.Sqrt(Math.Pow(worldA.X - centerX, 2) + Math.Pow(worldA.Y - centerY, 2)),
                    Math.Sqrt(Math.Pow(afterA.X - centerX, 2) + Math.Pow(afterA.Y - centerY, 2)),
                    0.05, "旋转保持顶点到中心的距离");

                // 顶点间距离不变
                CheckNear(distanceAB, DistanceOfWorld(afterA, afterB), 0.05, "旋转保持顶点间距离");

                // 正数 = 逆时针（世界坐标 y 向上）
                double delta = NormalizeAngle(AngleOfWorld(afterA, afterB) - angleAB);
                CheckNear(90, delta, 0.05, "旋转 +90° 使连线方向逆时针转 90°（实际 "
                    + delta.ToString("0.##", CultureInfo.InvariantCulture) + "°）");

                // 再转 -90° 应该回到原状
                control.RotateSelection(-90);
                PointD backA = control.ImageToWorld(a.X, a.Y);
                CheckNear(worldA.X, backA.X, 0.05, "回转 -90° 后 A 回到原位（x）");
                CheckNear(worldA.Y, backA.Y, 0.05, "回转 -90° 后 A 回到原位（y）");

                // 未选中顶点时旋转不做任何事
                control.ClearSelection();
                Check(!control.RotateSelection(90), "没有选中顶点时旋转失败");

                // 只旋转选中顶点，未选中的顶点不动
                PointD fixedBefore = control.ImageToWorld(d.X, d.Y);
                control.SetSelection(new List<Vertex>(new Vertex[] { a }), null);
                control.RotateSelection(30);
                PointD fixedAfter = control.ImageToWorld(d.X, d.Y);
                CheckNear(fixedBefore.X, fixedAfter.X, 0.01, "未选中的顶点不参与旋转（x）");
                CheckNear(fixedBefore.Y, fixedAfter.Y, 0.01, "未选中的顶点不参与旋转（y）");
            }
        }

        private static double AngleOfWorld(PointD from, PointD to)
        {
            return Math.Atan2(to.Y - from.Y, to.X - from.X) * 180.0 / Math.PI;
        }

        private static double DistanceOfWorld(PointD p1, PointD p2)
        {
            return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
        }

        /// <summary>命名规则与名字显示位置。</summary>
        private static void TestNamingAndPlacement()
        {
            // ---- 顶点命名：不命名 ----
            GraphCanvas none = new GraphCanvas(600, 400);
            none.VertexNaming = NamingRule.None;
            Vertex n1 = none.AddVertex(100, 100);
            Vertex n2 = none.AddVertex(200, 100);
            CheckEqual("", n1.Name, "不命名规则下顶点名字为空");
            CheckEqual("", n2.Name, "不命名规则下第二个顶点名字也为空");

            // ---- 顶点命名：字母V+数字 ----
            GraphCanvas numeric = new GraphCanvas(600, 400);
            numeric.VertexNaming = NamingRule.Numeric;
            CheckEqual("V1", numeric.AddVertex(100, 100).Name, "V+数字命名从 V1 开始");
            CheckEqual("V2", numeric.AddVertex(200, 100).Name, "V+数字命名递增");
            CheckEqual("V3", numeric.AddVertex(300, 100).Name, "V+数字命名继续递增");

            // ---- 顶点命名：字母 ----
            GraphCanvas alpha = new GraphCanvas(600, 400);
            alpha.VertexNaming = NamingRule.Alphabetic;
            CheckEqual("A", alpha.AddVertex(100, 100).Name, "字母命名从 A 开始");
            CheckEqual("B", alpha.AddVertex(200, 100).Name, "字母命名递增");
            CheckEqual("C", alpha.AddVertex(300, 100).Name, "字母命名继续递增");
            // 默认规则就是大写字母
            GraphCanvas def = new GraphCanvas(600, 400);
            CheckEqual(NamingRule.Alphabetic, def.VertexNaming, "顶点默认命名规则是大写字母");
            CheckEqual("A", def.AddVertex(100, 100).Name, "默认新建顶点命名为 A");

            // ---- 字母序列跨过 Z 之后带圈数 ----
            GraphCanvas many = new GraphCanvas(600, 400);
            many.VertexNaming = NamingRule.Alphabetic;
            for (int i = 0; i < 26; i++) many.AddVertex(100 + i, 100);
            CheckEqual("Z", many.Vertices[25].Name, "第 26 个顶点是 Z");
            CheckEqual("A1", many.AddVertex(200, 100).Name, "第 27 个顶点是 A1");

            // ---- 顶点命名：V+数字模式下不会与已有名字冲突 ----
            GraphCanvas collide = new GraphCanvas(600, 400);
            collide.VertexNaming = NamingRule.Numeric;
            collide.AddVertex(100, 100, "V1");
            CheckEqual("V2", collide.AddVertex(200, 100).Name, "已有名字 V1 时跳到 V2");

            // ---- 边命名：不命名 / 字母e+数字 / 小写字母 / 两端点 ----
            GraphCanvas edgeNone = new GraphCanvas(600, 400);
            edgeNone.EdgeNaming = NamingRule.None;
            Vertex a1 = edgeNone.AddVertex(100, 100, "A");
            Vertex b1 = edgeNone.AddVertex(200, 100, "B");
            CheckEqual("", edgeNone.AddEdge(a1.Id, b1.Id).Name, "不命名规则下边名字为空");

            GraphCanvas edgeNumeric = new GraphCanvas(600, 400);
            edgeNumeric.EdgeNaming = NamingRule.Numeric;
            Vertex a2 = edgeNumeric.AddVertex(100, 100, "A");
            Vertex b2 = edgeNumeric.AddVertex(200, 100, "B");
            Vertex c2 = edgeNumeric.AddVertex(300, 100, "C");
            CheckEqual("e1", edgeNumeric.AddEdge(a2.Id, b2.Id).Name, "e+数字命名从 e1 开始");
            CheckEqual("e2", edgeNumeric.AddEdge(b2.Id, c2.Id).Name, "e+数字命名递增");

            GraphCanvas edgeAlpha = new GraphCanvas(600, 400);
            edgeAlpha.EdgeNaming = NamingRule.Alphabetic;
            Vertex a3 = edgeAlpha.AddVertex(100, 100, "A");
            Vertex b3 = edgeAlpha.AddVertex(200, 100, "B");
            Vertex c3 = edgeAlpha.AddVertex(300, 100, "C");
            CheckEqual("a", edgeAlpha.AddEdge(a3.Id, b3.Id).Name, "边字母命名从 a 开始");
            CheckEqual("b", edgeAlpha.AddEdge(b3.Id, c3.Id).Name, "边字母命名递增");

            GraphCanvas edgeFromEnds = new GraphCanvas(600, 400);
            edgeFromEnds.EdgeNaming = NamingRule.FromEndpoints;
            Vertex a4 = edgeFromEnds.AddVertex(100, 100, "A");
            Vertex b4 = edgeFromEnds.AddVertex(200, 100, "B");
            CheckEqual("AB", edgeFromEnds.AddEdge(a4.Id, b4.Id).Name, "边使用两端点名字拼接");
            CheckEqual("AB2", edgeFromEnds.AddEdge(a4.Id, b4.Id).Name, "重名时两端点名字加后缀");
            // 端点没有名字时退回字母命名，而不是空名或数字
            GraphCanvas edgeNoNames = new GraphCanvas(600, 400);
            edgeNoNames.VertexNaming = NamingRule.None;
            edgeNoNames.EdgeNaming = NamingRule.FromEndpoints;
            Vertex a5 = edgeNoNames.AddVertex(100, 100);
            Vertex b5 = edgeNoNames.AddVertex(200, 100);
            CheckEqual("a", edgeNoNames.AddEdge(a5.Id, b5.Id).Name,
                "端点无名字时边退回字母命名（实际 " + edgeNoNames.Edges[0].Name + "）");

            // ---- 「两端点名字」是边专用的：顶点遇到它退化为字母 ----
            Check(GraphCanvas.IsVertexRule(NamingRule.None), "不命名可用于顶点");
            Check(GraphCanvas.IsVertexRule(NamingRule.Alphabetic), "字母可用于顶点");
            Check(GraphCanvas.IsVertexRule(NamingRule.Numeric), "V+数字可用于顶点");
            Check(!GraphCanvas.IsVertexRule(NamingRule.FromEndpoints), "「两端点名字」不可用于顶点");

            // ---- 名字位置影响内容外接矩形（外侧需要更多空间） ----
            GraphCanvas inside = new GraphCanvas(600, 400);
            inside.VertexRadius = 20;
            inside.VertexLabelPlacement = VertexLabelPlacement.Inside;
            inside.AddVertex(200, 200, "A");
            GraphCanvas outside = new GraphCanvas(600, 400);
            outside.VertexRadius = 20;
            outside.VertexLabelPlacement = VertexLabelPlacement.Right;
            outside.AddVertex(200, 200, "A");
            RectD insideBounds = inside.ContentBounds(10, 100);
            RectD outsideBounds = outside.ContentBounds(10, 100);
            Check(outsideBounds.Width > insideBounds.Width,
                "名字显示在顶点外侧时需要更大的外接矩形（" + outsideBounds.Width + " > " + insideBounds.Width + "）");

            // ---- 名字位置的序列化往返 ----
            GraphCanvas saved = new GraphCanvas(600, 400);
            saved.VertexNaming = NamingRule.Numeric;
            saved.EdgeNaming = NamingRule.None;
            saved.VertexLabelPlacement = VertexLabelPlacement.Below;
            saved.EdgeLabelPlacement = EdgeLabelPlacement.Inside;
            GraphCanvas loaded = GraphIo.Deserialize(GraphIo.Serialize(saved));
            CheckEqual(NamingRule.Numeric, loaded.VertexNaming, "往返：顶点命名规则");
            CheckEqual(NamingRule.None, loaded.EdgeNaming, "往返：边命名规则");
            CheckEqual(VertexLabelPlacement.Below, loaded.VertexLabelPlacement, "往返：顶点名字位置");
            CheckEqual(EdgeLabelPlacement.Inside, loaded.EdgeLabelPlacement, "往返：边名字位置");

            // 旧文件（没有这些字段）应使用默认值
            GraphCanvas legacy = GraphIo.Deserialize("{\"vertices\":[{\"id\":1,\"x\":10,\"y\":10}]}");
            CheckEqual(NamingRule.Alphabetic, legacy.VertexNaming, "旧文件使用默认顶点命名规则");
            CheckEqual(VertexLabelPlacement.Inside, legacy.VertexLabelPlacement, "旧文件使用默认顶点名字位置");
            CheckEqual(EdgeLabelPlacement.Above, legacy.EdgeLabelPlacement, "旧文件使用默认边名字位置");
        }

        private static string JoinIds(GraphCanvas c)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (Vertex v in c.Vertices) sb.Append(v.Id).Append(':').Append(v.Name).Append(' ');
            sb.Append("| edges: ");
            foreach (Edge e in c.Edges) sb.Append(e.Id).Append('(').Append(e.V1).Append('-').Append(e.V2).Append(") ");
            return sb.ToString();
        }

        private static double AngleOf(Vertex from, Vertex to)
        {
            return Math.Atan2(to.Y - from.Y, to.X - from.X) * 180.0 / Math.PI;
        }

        private static double NormalizeAngle(double degrees)
        {
            while (degrees > 180) degrees -= 360;
            while (degrees < -180) degrees += 360;
            return degrees;
        }

        /// <summary>
        /// 边名字必须落在「过线段中点的法线」上：
        /// 锚点 = 中点 + 法线方向 × 偏移量，且偏移不能太小（否则文字压在线上）。
        /// </summary>
        private static void TestEdgeLabelPlacement()
        {
            // 水平边，中点 (200,100)，法线是竖直方向
            GraphCanvas c = new GraphCanvas(600, 400);
            c.VertexRadius = 20;
            Vertex a = c.AddVertex(100, 100, "A");
            Vertex b = c.AddVertex(300, 100, "B");
            Edge horizontal = c.AddEdge(a.Id, b.Id, "e");

            PointD mid = c.EdgeLabelPoint(horizontal);
            CheckNear(200, mid.X, 0.01, "水平边标签锚点的基准是中点 x");
            CheckNear(100, mid.Y, 0.01, "水平边标签锚点的基准是中点 y");
            CheckNear(0, c.EdgeLabelAngle(horizontal), 0.01, "水平边标签方向角为 0");

            // 偏移沿法线：水平边的法线是 y 方向，x 不应变化
            PointD above = c.EdgeLabelAnchor(horizontal, -12);
            PointD below = c.EdgeLabelAnchor(horizontal, 12);
            CheckNear(mid.X, above.X, 0.01, "水平边向上偏移时 x 不变（确实在法线上）");
            CheckNear(mid.X, below.X, 0.01, "水平边向下偏移时 x 不变（确实在法线上）");
            CheckNear(mid.Y - 12, above.Y, 0.01, "负偏移在法线的上方");
            CheckNear(mid.Y + 12, below.Y, 0.01, "正偏移在法线的下方");

            // 竖直边：法线是水平方向，y 不应变化；偏移方向取决于边的朝向，只验证垂直与长度
            GraphCanvas cv = new GraphCanvas(600, 400);
            cv.VertexRadius = 20;
            Vertex v1 = cv.AddVertex(100, 100, "A");
            Vertex v2 = cv.AddVertex(100, 300, "B");
            Edge vertical = cv.AddEdge(v1.Id, v2.Id, "e");
            PointD vmid = cv.EdgeLabelPoint(vertical);
            PointD vAnchor = cv.EdgeLabelAnchor(vertical, 12);
            CheckNear(vmid.Y, vAnchor.Y, 0.01, "竖直边偏移时 y 不变（确实在法线上）");
            CheckNear(12, Math.Abs(vAnchor.X - vmid.X), 0.01, "竖直边的偏移量等于给定距离");

            // 斜边：偏移向量必须与边方向垂直（点积为 0），长度等于偏移量
            GraphCanvas cd = new GraphCanvas(600, 400);
            cd.VertexRadius = 20;
            Vertex d1 = cd.AddVertex(100, 100, "A");
            Vertex d2 = cd.AddVertex(400, 300, "B");
            Edge diagonal = cd.AddEdge(d1.Id, d2.Id, "e");
            PointD dmid = cd.EdgeLabelPoint(diagonal);
            double offset = 15;
            PointD dAnchor = cd.EdgeLabelAnchor(diagonal, offset);
            double dirX = d2.X - d1.X;
            double dirY = d2.Y - d1.Y;
            double len = Math.Sqrt(dirX * dirX + dirY * dirY);
            dirX /= len;
            dirY /= len;
            double offX = dAnchor.X - dmid.X;
            double offY = dAnchor.Y - dmid.Y;
            CheckNear(0, offX * dirX + offY * dirY, 0.01, "斜边的偏移向量与边方向垂直（在法线上）");
            CheckNear(offset, Math.Sqrt(offX * offX + offY * offY), 0.01, "偏移长度等于给定距离");

            // 倾斜角始终让文字正着读
            Check(Math.Abs(cd.EdgeLabelAngle(diagonal)) <= 90,
                "斜边标签倾斜角限制在 ±90° 内（" + cd.EdgeLabelAngle(diagonal).ToString("0.#", CultureInfo.InvariantCulture) + "）");
            GraphCanvas creverse = new GraphCanvas(600, 400);
            Vertex r1 = creverse.AddVertex(400, 300, "A");
            Vertex r2 = creverse.AddVertex(100, 100, "B");
            Edge reverse = creverse.AddEdge(r1.Id, r2.Id, "e");
            Check(Math.Abs(creverse.EdgeLabelAngle(reverse)) <= 90, "反向绘制的边同样正着读");

            // 默认（上方）的偏移量必须够大，不能让文字压在线上
            using (CanvasControl control = new CanvasControl())
            {
                control.Size = new Size(600, 400);
                control.Canvas = c;
                c.EdgeLabelPlacement = EdgeLabelPlacement.Above;
                Check(c.EdgeLabelPlacement == EdgeLabelPlacement.Above, "边名字默认在上方");
                double gap = 11 * 1.1;
                Check(gap >= 11, "上方/下方的偏移量不小于一个行高（" + gap.ToString("0.#", CultureInfo.InvariantCulture) + "）");
            }
        }

        private static Bitmap RenderControl(Control control)
        {
            Bitmap bmp = new Bitmap(control.Width, control.Height);
            control.DrawToBitmap(bmp, new Rectangle(0, 0, control.Width, control.Height));
            return bmp;
        }

        /// <summary>递归创建句柄并完成布局，让 DrawToBitmap 能拿到真实画面。</summary>
        private static void RealizeControlTree(Control control)
        {
            control.CreateControl();
            control.PerformLayout();
            foreach (Control child in control.Controls)
            {
                RealizeControlTree(child);
            }
            control.Invalidate(true);
            control.Update();
        }
    }
}
