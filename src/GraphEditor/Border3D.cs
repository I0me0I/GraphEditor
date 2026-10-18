using System;
using System.Drawing;
using System.Windows.Forms;

namespace GraphEditor
{
    /// <summary>
    /// 经典 3D 边框的绘制工具：凹陷（Sunken）效果，而不是平淡的灰线。
    /// </summary>
    internal static class Border3D
    {
        /// <summary>
        /// 在矩形内画一圈凹陷的 3D 边框（左上暗、右下亮，看起来是凹进去的）。
        /// 用 Win32 的经典 3D 边框样式绘制，比手画的灰线更有「陷下去」的立体感。
        /// </summary>
        public static void DrawSunken(Graphics g, Rectangle bounds, Color face)
        {
            if (bounds.Width < 4 || bounds.Height < 4) return;

            // BDR_SUNKENOUTER：左上深、右下浅，经典凹陷效果
            ControlPaint.DrawBorder3D(g, bounds, Border3DStyle.Sunken);

            // 再用一层更暗的内框强调凹陷（否则在高分屏上偏细）
            Color dark = ControlPaint.Dark(face, 0.35f);
            using (Pen pen = new Pen(dark))
            {
                g.DrawLine(pen, bounds.Left + 2, bounds.Top + 2, bounds.Right - 3, bounds.Top + 2);
                g.DrawLine(pen, bounds.Left + 2, bounds.Top + 2, bounds.Left + 2, bounds.Bottom - 3);
            }
        }
    }

    /// <summary>
    /// 包住标签页控件的边框容器：整体画成凹陷的 3D 效果（经典 Windows 外观），
    /// 让每个文档面板看起来像「嵌进工作区里的一块」。
    /// </summary>
    internal sealed class PaneFrame : Panel
    {
        private const int BorderThickness = 3;

        public PaneFrame()
        {
            DoubleBuffered = true;
            BackColor = SystemColors.Control;
            Padding = new Padding(BorderThickness);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Border3D.DrawSunken(e.Graphics, ClientRectangle, BackColor);
        }
    }
}
