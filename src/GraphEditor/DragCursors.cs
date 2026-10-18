using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GraphEditor
{
    /// <summary>
    /// 拖动标签时用的光标：箭头 + 一个「文档」小图标，
    /// 让用户一眼看出「我正在拖动一个文档」，而不是普通的箭头。
    /// </summary>
    internal static class DragCursors
    {
        private static Cursor _document;
        private static Cursor _documentForbidden;

        /// <summary>箭头 + 文档图标（可以放置）。</summary>
        public static Cursor Document
        {
            get
            {
                if (_document == null) _document = Create(false);
                return _document;
            }
        }

        /// <summary>箭头 + 带禁止标记的文档图标（不能放置）。</summary>
        public static Cursor DocumentForbidden
        {
            get
            {
                if (_documentForbidden == null) _documentForbidden = Create(true);
                return _documentForbidden;
            }
        }

        private static Cursor Create(bool forbidden)
        {
            // 32×32 是光标的标准尺寸；在 100%~200% 缩放下都还算清楚
            const int size = 32;
            Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // ---- 左下角的箭头（标准指针形状）----
                Point[] arrow = new Point[]
                {
                    new Point(0, 0),
                    new Point(0, 17),
                    new Point(4, 13),
                    new Point(7, 20),
                    new Point(10, 19),
                    new Point(7, 12),
                    new Point(12, 12)
                };
                using (SolidBrush white = new SolidBrush(Color.White))
                using (Pen outline = new Pen(Color.FromArgb(0x20, 0x20, 0x20), 1.4f))
                {
                    // 先画一圈偏移的描边，再填白色，得到有轮廓的指针
                    Point[] shadow = new Point[arrow.Length];
                    for (int i = 0; i < arrow.Length; i++)
                    {
                        shadow[i] = new Point(arrow[i].X + 1, arrow[i].Y + 1);
                    }
                    g.FillPolygon(white, shadow);
                    g.DrawPolygon(outline, shadow);
                    g.FillPolygon(white, arrow);
                    g.DrawPolygon(outline, arrow);
                }

                // ---- 右下角的文档图标 ----
                Rectangle page = new Rectangle(15, 12, 15, 18);
                using (SolidBrush fill = new SolidBrush(Color.White))
                using (Pen border = new Pen(forbidden ? Color.FromArgb(0xA0, 0x00, 0x00)
                                                      : Color.FromArgb(0x1F, 0x5F, 0xA8), 1.4f))
                {
                    g.FillRectangle(fill, page);
                    g.DrawRectangle(border, page);

                    // 几行文字，示意「文档」
                    using (Pen line = new Pen(forbidden ? Color.FromArgb(0xA0, 0x00, 0x00)
                                                        : Color.FromArgb(0x60, 0x90, 0xC8), 1f))
                    {
                        g.DrawLine(line, page.Left + 3, page.Top + 5, page.Right - 3, page.Top + 5);
                        g.DrawLine(line, page.Left + 3, page.Top + 9, page.Right - 3, page.Top + 9);
                        g.DrawLine(line, page.Left + 3, page.Top + 13, page.Right - 5, page.Top + 13);
                    }
                }

                if (forbidden)
                {
                    // 红色斜杠
                    using (Pen ban = new Pen(Color.FromArgb(0xD0, 0x10, 0x10), 2.4f))
                    {
                        ban.StartCap = LineCap.Round;
                        ban.EndCap = LineCap.Round;
                        g.DrawLine(ban, page.Left + 2, page.Top + 2, page.Right - 2, page.Bottom - 2);
                    }
                }
            }

            IntPtr handle = bitmap.GetHicon();
            bitmap.Dispose();
            // 光标在整个进程生命周期内复用，句柄无需立即释放
            return new Cursor(handle);
        }
    }
}
