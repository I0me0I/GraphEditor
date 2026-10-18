using System;
using System.Drawing;
using System.Windows.Forms;

namespace GraphEditor
{
    /// <summary>
    /// 自绘的标签控件。
    /// 原生 <see cref="TabControl"/> 没有 BorderStyle，标签条底色也无法设置，
    /// 结果是「选中的标签」和「标签条空白处」几乎同色（都是浅灰），对比度极低。
    /// 这里接管整个绘制：标签条用较深的灰蓝底，选中标签用纯白（与下方内容连成一体），
    /// 未选中标签用中间灰，三者层次分明。
    /// </summary>
    internal sealed class DocumentTabControl : TabControl
    {
        // 配色：标签条底 / 未选中标签 / 选中标签 / 文字 / 强调线
        private static readonly Color StripColor = Color.FromArgb(0xC3, 0xC8, 0xCF);
        private static readonly Color TabNormal = Color.FromArgb(0xD5, 0xD9, 0xDE);
        private static readonly Color TabHot = Color.FromArgb(0xE4, 0xE7, 0xEB);
        private static readonly Color TabSelected = Color.White;
        private static readonly Color TextNormal = Color.FromArgb(0x33, 0x38, 0x40);
        private static readonly Color TextSelected = Color.FromArgb(0x10, 0x14, 0x1A);
        private static readonly Color Accent = Color.FromArgb(0x2D, 0x7D, 0xD2);
        private static readonly Color Edge = Color.FromArgb(0x9A, 0xA0, 0xA9);

        private int _hotIndex = -1;

        /// <summary>标签条空白处的底色（供自检核对对比度）。</summary>
        internal static Color StripBackground { get { return StripColor; } }

        /// <summary>标签底色：选中 / 未选中（供自检核对对比度）。</summary>
        internal static Color TabBackground(bool selected)
        {
            return selected ? TabSelected : TabNormal;
        }

        /// <summary>相对亮度（0=黑，1=白），用于检查对比度是否足够。</summary>
        internal static double Luminance(Color c)
        {
            return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        }

        public DocumentTabControl()
        {
            SetStyle(ControlStyles.UserPaint
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw, true);
            Multiline = false;
            ShowToolTips = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hot = HitTab(e.Location);
            if (hot != _hotIndex)
            {
                _hotIndex = hot;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hotIndex != -1)
            {
                _hotIndex = -1;
                Invalidate();
            }
        }

        private int HitTab(Point p)
        {
            for (int i = 0; i < TabCount; i++)
            {
                if (GetTabRect(i).Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            // 1) 整块铺标签条底色（页内容会被 TabPage 子控件盖住）
            using (SolidBrush strip = new SolidBrush(StripColor))
            {
                g.FillRectangle(strip, ClientRectangle);
            }

            int selected = SelectedIndex;
            int stripBottom = StripBottom();

            // 2) 先画未选中的标签，再画选中的（保证选中标签压在上层）
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < TabCount; i++)
                {
                    bool isSelected = (i == selected);
                    if ((pass == 0) == isSelected) continue;
                    DrawTab(g, i, isSelected, stripBottom);
                }
            }

            // 3) 标签条与页面之间常有一条窄缝（原生控件如此），
            //    填成与选中标签相同的底色，让「标签 → 内容」看起来是连着的。
            Rectangle display = DisplayRectangle;
            if (display.Top > stripBottom)
            {
                using (SolidBrush bridge = new SolidBrush(selected >= 0 ? TabSelected : StripColor))
                {
                    g.FillRectangle(bridge, 0, stripBottom, ClientSize.Width,
                        display.Top - stripBottom);
                }
            }

            // 4) 标签条底部一条分隔线；选中标签下方留空，和内容连成一体
            using (Pen edge = new Pen(Edge))
            {
                if (selected >= 0 && selected < TabCount)
                {
                    Rectangle sel = GetTabRect(selected);
                    g.DrawLine(edge, 0, stripBottom - 1, sel.Left, stripBottom - 1);
                    g.DrawLine(edge, sel.Right, stripBottom - 1, ClientSize.Width, stripBottom - 1);
                }
                else
                {
                    g.DrawLine(edge, 0, stripBottom - 1, ClientSize.Width, stripBottom - 1);
                }
            }

            DrawOverflowArrows(g, stripBottom);
        }

        private void DrawTab(Graphics g, int index, bool selected, int stripBottom)
        {
            Rectangle rect = GetTabRect(index);
            if (rect.Width <= 0 || rect.Height <= 0) return;

            // 标签底：选中的纯白、悬停稍亮、其余中间灰
            Color back = selected ? TabSelected : (index == _hotIndex ? TabHot : TabNormal);
            using (SolidBrush brush = new SolidBrush(back))
            {
                g.FillRectangle(brush, rect);
            }

            using (Pen edge = new Pen(selected ? Edge : Color.FromArgb(0xAF, 0xB4, 0xBB)))
            {
                g.DrawLine(edge, rect.Left, rect.Top, rect.Right - 1, rect.Top);
                g.DrawLine(edge, rect.Left, rect.Top, rect.Left, rect.Bottom - 1);
                g.DrawLine(edge, rect.Right - 1, rect.Top, rect.Right - 1, rect.Bottom - 1);
            }

            // 选中标签顶部一条强调线（现代应用常见做法）
            if (selected)
            {
                using (SolidBrush accent = new SolidBrush(Accent))
                {
                    g.FillRectangle(accent, rect.Left + 1, rect.Top + 1, rect.Width - 2, 2);
                }
            }

            // 文字
            TabPage page = TabPages[index];
            Rectangle textRect = new Rectangle(rect.Left + 6, rect.Top + 2,
                Math.Max(1, rect.Width - 12), Math.Max(1, rect.Height - 4));
            TextRenderer.DrawText(g, page.Text, Font, textRect,
                selected ? TextSelected : TextNormal,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        /// <summary>标签条底部（第一个标签的底边）。</summary>
        private int StripBottom()
        {
            if (TabCount > 0)
            {
                Rectangle first = GetTabRect(0);
                if (first.Height > 0) return first.Bottom;
            }
            return Math.Min(ClientSize.Height, ItemSize.Height + 4);
        }

        /// <summary>
        /// 标签太多、超出宽度时，原生控件会显示上下滚动按钮；自绘后这些按钮不画了，
        /// 所以这里补两个小三角，点击仍然由原生控件处理。
        /// </summary>
        private void DrawOverflowArrows(Graphics g, int stripBottom)
        {
            if (TabCount == 0) return;
            Rectangle last = GetTabRect(TabCount - 1);
            if (last.Right <= ClientSize.Width - 18) return;

            int cx = ClientSize.Width - 9;
            int top = 2;
            int bottom = Math.Max(top + 8, stripBottom - 2);
            int mid = (top + bottom) / 2;
            int upMid = (top + mid) / 2;
            int downMid = (mid + bottom) / 2;

            Point[] up = new Point[]
            {
                new Point(cx, upMid - 4), new Point(cx - 4, upMid + 3), new Point(cx + 4, upMid + 3)
            };
            Point[] down = new Point[]
            {
                new Point(cx, downMid + 4), new Point(cx - 4, downMid - 3), new Point(cx + 4, downMid - 3)
            };
            using (SolidBrush brush = new SolidBrush(TextNormal))
            {
                g.FillPolygon(brush, up);
                g.FillPolygon(brush, down);
            }
        }
    }
}
