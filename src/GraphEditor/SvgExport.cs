using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace GraphEditor
{
    /// <summary>把图导出为 SVG 矢量图。</summary>
    public static class SvgExport
    {
        private const string FontStack = "Microsoft YaHei UI, Microsoft YaHei, Segoe UI, Arial, Helvetica, sans-serif";
        private const double VertexFontSize = 12.0;
        private const double EdgeFontSize = 11.0;
        private const double VertexStrokeWidth = 2.0;

        /// <summary>导出时内容外接矩形四周扩展的像素（留白）。</summary>
        public static double Padding = 12.0;

        public static void Export(GraphCanvas canvas, string path)
        {
            File.WriteAllText(path, Build(canvas), new UTF8Encoding(false));
        }

        /// <summary>只包含图内容的最小外接矩形，四周扩展 <see cref="Padding"/> 像素。</summary>
        public static RectD ExportBounds(GraphCanvas canvas)
        {
            RectD content = canvas.ContentBounds(VertexFontSize, VertexFontSize * 3 + canvas.VertexRadius);
            if (content.Width <= 0 && content.Height <= 0)
            {
                // 空图：给一个最小的画布
                return new RectD(0, 0, Math.Max(1, canvas.CanvasWidth), Math.Max(1, canvas.CanvasHeight));
            }
            return new RectD(
                content.X - Padding,
                content.Y - Padding,
                content.Width + Padding * 2,
                content.Height + Padding * 2);
        }

        public static string Build(GraphCanvas canvas)
        {
            RectD bounds = ExportBounds(canvas);
            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n");
            sb.Append("<!-- 由 GraphEditor 导出：只包含图内容的最小区域，四周留白 ")
              .Append(Num(Padding)).Append(" 像素 -->\n");
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\"\n");
            sb.Append("     width=\"").Append(Num(bounds.Width)).Append("\"");
            sb.Append(" height=\"").Append(Num(bounds.Height)).Append("\"");
            sb.Append(" viewBox=\"").Append(Num(bounds.X)).Append(" ").Append(Num(bounds.Y))
              .Append(" ").Append(Num(bounds.Width)).Append(" ").Append(Num(bounds.Height)).Append("\">\n");

            sb.Append("  <rect x=\"").Append(Num(bounds.X)).Append("\" y=\"").Append(Num(bounds.Y))
              .Append("\" width=\"").Append(Num(bounds.Width))
              .Append("\" height=\"").Append(Num(bounds.Height))
              .Append("\" fill=\"#FFFFFF\"/>\n");

            // 边先画，顶点后画（顶点覆盖在边上）
            sb.Append("  <g id=\"edges\">\n");
            List<Edge> edges = new List<Edge>(canvas.Edges);
            edges.Sort(delegate(Edge a, Edge b) { return a.Id.CompareTo(b.Id); });
            foreach (Edge e in edges)
            {
                AppendEdge(sb, canvas, e);
            }
            sb.Append("  </g>\n");

            sb.Append("  <g id=\"vertices\">\n");
            List<Vertex> vertices = new List<Vertex>(canvas.Vertices);
            vertices.Sort(delegate(Vertex a, Vertex b) { return a.Id.CompareTo(b.Id); });
            foreach (Vertex v in vertices)
            {
                AppendVertex(sb, canvas, v);
            }
            sb.Append("  </g>\n");

            sb.Append("</svg>\n");
            return sb.ToString();
        }

        private static void AppendEdge(StringBuilder sb, GraphCanvas canvas, Edge e)
        {
            PointD[] pts = canvas.EdgePolyline(e);
            if (pts.Length < 2) return;

            Color color = canvas.EffectiveColor(e);
            string style = "fill=\"none\" stroke=\"" + Hex(color) + "\" stroke-width=\"" + Num(canvas.EdgeWidth) + "\"";
            switch (canvas.EdgeLineStyle)
            {
                case EdgeLineStyle.Dashed:
                    style += " stroke-dasharray=\"" + Num(canvas.EdgeWidth * 4) + "," + Num(canvas.EdgeWidth * 2.5) + "\"";
                    break;
                case EdgeLineStyle.Dotted:
                    style += " stroke-linecap=\"round\" stroke-dasharray=\"0.01," + Num(canvas.EdgeWidth * 3) + "\"";
                    break;
                default:
                    style += " stroke-linecap=\"round\"";
                    break;
            }

            if (pts.Length == 2)
            {
                sb.Append("    <line x1=\"").Append(Num(pts[0].X)).Append("\" y1=\"").Append(Num(pts[0].Y))
                  .Append("\" x2=\"").Append(Num(pts[1].X)).Append("\" y2=\"").Append(Num(pts[1].Y))
                  .Append("\" ").Append(style).Append("/>\n");
            }
            else
            {
                sb.Append("    <path d=\"");
                sb.Append("M ").Append(Num(pts[0].X)).Append(" ").Append(Num(pts[0].Y));
                for (int i = 1; i < pts.Length; i++)
                {
                    sb.Append(" L ").Append(Num(pts[i].X)).Append(" ").Append(Num(pts[i].Y));
                }
                sb.Append("\" ").Append(style).Append("/>\n");
            }

            if (!string.IsNullOrEmpty(e.Name))
            {
                PointD label = canvas.EdgeLabelPoint(e);
                // 沿边方向倾斜，便于阅读；再按设置上移/下移
                PointD[] labelPts = canvas.EdgePolyline(e);
                double angle = 0;
                if (labelPts.Length >= 2)
                {
                    int mid = labelPts.Length / 2;
                    int lo = Math.Max(0, mid - 1);
                    int hi = Math.Min(labelPts.Length - 1, mid);
                    angle = Math.Atan2(labelPts[hi].Y - labelPts[lo].Y, labelPts[hi].X - labelPts[lo].X) * 180.0 / Math.PI;
                    if (angle > 90) angle -= 180;
                    if (angle < -90) angle += 180;
                }

                double offset;
                switch (canvas.EdgeLabelPlacement)
                {
                    case EdgeLabelPlacement.Inside: offset = 0; break;
                    case EdgeLabelPlacement.Below: offset = EdgeFontSize * 0.9; break;
                    default: offset = -EdgeFontSize * 0.9; break;
                }

                // 把偏移量按边的法线方向分解到 x/y 上
                double rad = angle * Math.PI / 180.0;
                double lx = label.X - Math.Sin(rad) * offset;
                double ly = label.Y + Math.Cos(rad) * offset;
                double baseline = ly + EdgeFontSize * 0.35;

                sb.Append("    <text x=\"").Append(Num(lx)).Append("\" y=\"").Append(Num(baseline))
                  .Append("\" transform=\"rotate(").Append(Num(angle)).Append(" ").Append(Num(lx)).Append(" ").Append(Num(baseline)).Append(")\"")
                  .Append(" font-family=\"").Append(FontStack).Append("\" font-size=\"").Append(Num(EdgeFontSize))
                  .Append("\" fill=\"").Append(Hex(EdgeTextColor(canvas)))
                  .Append("\" text-anchor=\"middle\"")
                  .Append(">").Append(Escape(e.Name)).Append("</text>\n");
            }
        }

        private static void AppendVertex(StringBuilder sb, GraphCanvas canvas, Vertex v)
        {
            Color fill = canvas.EffectiveColor(v);
            Color stroke = GraphCanvas.Darken(fill, 0.7);
            double r = canvas.VertexRadius;

            if (canvas.VertexShape == VertexShape.Square)
            {
                double w = r * 2;
                double h = r * 1.5;
                sb.Append("    <rect x=\"").Append(Num(v.X - w / 2)).Append("\" y=\"").Append(Num(v.Y - h / 2))
                  .Append("\" width=\"").Append(Num(w)).Append("\" height=\"").Append(Num(h))
                  .Append("\" rx=\"3\" ry=\"3\" fill=\"").Append(Hex(fill))
                  .Append("\" stroke=\"").Append(Hex(stroke))
                  .Append("\" stroke-width=\"").Append(Num(VertexStrokeWidth)).Append("\"/>\n");
            }
            else
            {
                sb.Append("    <circle cx=\"").Append(Num(v.X)).Append("\" cy=\"").Append(Num(v.Y))
                  .Append("\" r=\"").Append(Num(r))
                  .Append("\" fill=\"").Append(Hex(fill))
                  .Append("\" stroke=\"").Append(Hex(stroke))
                  .Append("\" stroke-width=\"").Append(Num(VertexStrokeWidth)).Append("\"/>\n");
            }

            if (!string.IsNullOrEmpty(v.Name))
            {
                double x = v.X;
                double y = v.Y;
                Color textColor = GraphCanvas.ContrastTextColor(fill);
                string anchor = "middle";

                // 名字显示位置：与画布内保持一致
                if (canvas.VertexLabelPlacement != VertexLabelPlacement.Inside)
                {
                    textColor = Color.FromArgb(0x20, 0x20, 0x20);
                    double gap = r + 3;
                    switch (canvas.VertexLabelPlacement)
                    {
                        case VertexLabelPlacement.Above:
                            y = v.Y - gap - 2;
                            break;
                        case VertexLabelPlacement.Below:
                            y = v.Y + gap + VertexFontSize;
                            break;
                        case VertexLabelPlacement.Left:
                            x = v.X - gap;
                            anchor = "end";
                            y = v.Y + VertexFontSize * 0.35;
                            break;
                        default: // Right
                            x = v.X + gap;
                            anchor = "start";
                            y = v.Y + VertexFontSize * 0.35;
                            break;
                    }
                }
                else
                {
                    y = v.Y + VertexFontSize * 0.35;
                }

                sb.Append("    <text x=\"").Append(Num(x)).Append("\" y=\"").Append(Num(y))
                  .Append("\" font-family=\"").Append(FontStack).Append("\" font-size=\"").Append(Num(VertexFontSize))
                  .Append("\" fill=\"").Append(Hex(textColor))
                  .Append("\" text-anchor=\"").Append(anchor)
                  .Append("\">").Append(Escape(v.Name)).Append("</text>\n");
            }
        }

        private static Color EdgeTextColor(GraphCanvas canvas)
        {
            // 名字没有背景，因此固定用深灰。浅色边（黄、青等）用边色会看不清。
            return Color.FromArgb(0x20, 0x20, 0x20);
        }

        public static string Num(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "0";
            if (Math.Abs(value - Math.Round(value)) < 1e-9 && Math.Abs(value) < 1e15)
            {
                return ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
            }
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static string Hex(Color c)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        /// <summary>把文本写成合法的 SVG 字符数据。</summary>
        public static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            StringBuilder sb = new StringBuilder(text.Length + 8);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&apos;"); break;
                    default:
                        if (c < ' ' && c != '\t' && c != '\n' && c != '\r') sb.Append(' ');
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
