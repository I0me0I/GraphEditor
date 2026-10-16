using System;
using System.Drawing;
using System.Windows.Forms;

namespace GraphEditor
{
    /// <summary>简单的文本输入对话框（重命名用）。</summary>
    public static class PromptDialog
    {
        public static bool ShowText(IWin32Window owner, string title, string label, string initial, out string result)
        {
            result = null;
            using (Form form = new Form())
            {
                form.Text = title;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ShowInTaskbar = false;
                form.ClientSize = new Size(360, 118);
                form.Font = new Font("Microsoft YaHei UI", 9f);

                Label caption = new Label();
                caption.Text = label;
                caption.SetBounds(14, 14, 330, 20);
                form.Controls.Add(caption);

                TextBox box = new TextBox();
                box.Text = initial ?? "";
                box.SetBounds(14, 38, 330, 24);
                box.SelectAll();
                form.Controls.Add(box);

                Button ok = new Button();
                ok.Text = "确定";
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(182, 74, 76, 28);
                form.Controls.Add(ok);

                Button cancel = new Button();
                cancel.Text = "取消";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(266, 74, 76, 28);
                form.Controls.Add(cancel);

                form.AcceptButton = ok;
                form.CancelButton = cancel;

                DialogResult dialogResult = form.ShowDialog(owner);
                if (dialogResult != DialogResult.OK) return false;
                result = box.Text;
                return true;
            }
        }
    }

    /// <summary>
    /// 直接铺在工具栏里的调色盘：所有色块常显，点一下就选中，不需要下拉。
    /// </summary>
    public sealed class PaletteStrip : Control
    {
        private const int CellSize = 18;
        private const int Gap = 2;
        private const int Inset = 2;

        private int _columns = 12;
        private int _selectedIndex = -1;
        private int _hoverIndex = -1;

        public event EventHandler<Color> ColorClicked;

        public PaletteStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            RecalcSize();
        }

        /// <summary>每行显示的色块数量。</summary>
        public int Columns
        {
            get { return _columns; }
            set
            {
                _columns = Math.Max(1, value);
                RecalcSize();
            }
        }

        /// <summary>当前标记为选中的颜色；调色盘中不存在该颜色时取消选中。</summary>
        public Color SelectedColor
        {
            get
            {
                return _selectedIndex >= 0 ? CanvasControl.Palette[_selectedIndex] : Color.Empty;
            }
            set
            {
                int index = -1;
                for (int i = 0; i < CanvasControl.Palette.Length; i++)
                {
                    if (CanvasControl.Palette[i].ToArgb() == value.ToArgb())
                    {
                        index = i;
                        break;
                    }
                }
                if (index != _selectedIndex)
                {
                    _selectedIndex = index;
                    Invalidate();
                }
            }
        }

        private void RecalcSize()
        {
            int rows = (int)Math.Ceiling(CanvasControl.Palette.Length / (double)_columns);
            int width = Inset * 2 + _columns * (CellSize + Gap) - Gap;
            int height = Inset * 2 + rows * (CellSize + Gap) - Gap;
            Size = new Size(width, height);
            MinimumSize = Size;
            MaximumSize = Size;
            Invalidate();
        }

        private Rectangle CellBounds(int index)
        {
            int row = index / _columns;
            int column = index % _columns;
            return new Rectangle(
                Inset + column * (CellSize + Gap),
                Inset + row * (CellSize + Gap),
                CellSize,
                CellSize);
        }

        private int IndexAt(Point point)
        {
            for (int i = 0; i < CanvasControl.Palette.Length; i++)
            {
                if (CellBounds(i).Contains(point)) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexAt(e.Location);
            if (index != _hoverIndex)
            {
                _hoverIndex = index;
                Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverIndex != -1)
            {
                _hoverIndex = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int index = IndexAt(e.Location);
            if (index < 0) return;

            _selectedIndex = index;
            Invalidate();
            EventHandler<Color> handler = ColorClicked;
            if (handler != null) handler(this, CanvasControl.Palette[index]);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush back = new SolidBrush(Color.FromArgb(0xF0, 0xF0, 0xF0)))
            {
                g.FillRectangle(back, ClientRectangle);
            }

            for (int i = 0; i < CanvasControl.Palette.Length; i++)
            {
                Rectangle cell = CellBounds(i);
                Color color = CanvasControl.Palette[i];
                using (SolidBrush brush = new SolidBrush(color))
                {
                    g.FillRectangle(brush, cell);
                }

                if (i == _hoverIndex)
                {
                    using (Pen hover = new Pen(Color.FromArgb(0x1E, 0x88, 0xE5), 2f))
                    {
                        g.DrawRectangle(hover, cell.X - 1, cell.Y - 1, cell.Width + 1, cell.Height + 1);
                    }
                }
                else
                {
                    using (Pen border = new Pen(Color.FromArgb(0x9A, 0x9A, 0x9A)))
                    {
                        g.DrawRectangle(border, cell.X, cell.Y, cell.Width - 1, cell.Height - 1);
                    }
                }

                if (i == _selectedIndex)
                {
                    Color mark = GraphCanvas.ContrastTextColor(color);
                    using (Pen pen = new Pen(mark, 2f))
                    {
                        g.DrawLines(pen, new Point[]
                        {
                            new Point(cell.X + 3, cell.Y + CellSize / 2),
                            new Point(cell.X + CellSize / 2 - 1, cell.Bottom - 4),
                            new Point(cell.Right - 3, cell.Y + 3)
                        });
                    }
                }
            }
        }
    }

    /// <summary>调色盘辅助：生成菜单形式的调色盘（用于菜单栏的属性项）。</summary>
    public static class PaletteMenu
    {
        /// <summary>把调色盘铺成若干列的菜单项，触发时回调 onPick。</summary>
        public static void Fill(ToolStripItemCollection items, EventHandler<Color> onPick)
        {
            const int columns = 6;
            int rows = (int)Math.Ceiling(CanvasControl.Palette.Length / (double)columns);

            for (int row = 0; row < rows; row++)
            {
                ToolStripMenuItem line = new ToolStripMenuItem();
                line.AutoSize = false;
                line.Enabled = true;
                line.Text = "";
                line.Padding = Padding.Empty;
                line.Margin = Padding.Empty;

                FlowLayoutPanel strip = new FlowLayoutPanel();
                strip.AutoSize = true;
                strip.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                strip.WrapContents = false;
                strip.Margin = Padding.Empty;
                strip.Padding = Padding.Empty;

                for (int column = 0; column < columns; column++)
                {
                    int index = row * columns + column;
                    if (index >= CanvasControl.Palette.Length) break;
                    Color color = CanvasControl.Palette[index];

                    Button swatch = new Button();
                    swatch.Size = new Size(22, 22);
                    swatch.Margin = new Padding(1);
                    swatch.Padding = Padding.Empty;
                    swatch.FlatStyle = FlatStyle.Flat;
                    swatch.FlatAppearance.BorderColor = Color.FromArgb(0x88, 0x88, 0x88);
                    swatch.FlatAppearance.BorderSize = 1;
                    swatch.BackColor = color;
                    swatch.TabStop = false;
                    swatch.Tag = color;
                    swatch.Click += delegate(object sender, EventArgs e)
                    {
                        Color chosen = (Color)((Button)sender).Tag;
                        if (onPick != null) onPick(sender, chosen);
                    };
                    strip.Controls.Add(swatch);
                }

                ToolStripControlHost host = new ToolStripControlHost(strip);
                host.Padding = Padding.Empty;
                host.Margin = Padding.Empty;
                items.Add(host);
            }
        }

        /// <summary>构造一个调色盘下拉按钮。</summary>
        public static ToolStripDropDownButton CreateButton(string text, Color current, EventHandler<Color> onPick)
        {
            ToolStripDropDownButton button = new ToolStripDropDownButton(text);
            button.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            button.Image = CanvasControl.MakeSwatchImage(current, 16, false);
            button.Tag = current;
            Fill(button.DropDownItems, onPick);
            return button;
        }

        /// <summary>更新按钮上的当前颜色。</summary>
        public static void SetCurrentColor(ToolStripDropDownButton button, Color color)
        {
            button.Tag = color;
            Image old = button.Image;
            button.Image = CanvasControl.MakeSwatchImage(color, 16, false);
            if (old != null) old.Dispose();
        }
    }
}
