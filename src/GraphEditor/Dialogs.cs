using System;
using System.Drawing;
using System.Windows.Forms;

namespace GraphEditor
{
    /// <summary>简单的文本输入对话框（重命名用）。</summary>
    public static class PromptDialog
    {
        /// <summary>
        /// 设计基准字体尺寸（96 DPI、9pt 系统消息字体 ≈ 6×13）。
        /// 代码里 new 出来的窗体默认是 SizeF.Empty，等于「不按比例缩放」，
        /// 于是写死的像素在 125%/150% 缩放下会把文字和按钮裁掉。
        /// </summary>
        private static readonly SizeF DesignScale = new SizeF(6F, 13F);

        public static bool ShowText(IWin32Window owner, string title, string label, string initial, out string result)
        {
            result = null;
            TextBox box;
            using (Form form = BuildDialog(title, label, initial, SystemFonts.MessageBoxFont, out box))
            {
                DialogResult dialogResult = form.ShowDialog(owner);
                if (dialogResult != DialogResult.OK) return false;
                result = box.Text;
                return true;
            }
        }

        /// <summary>
        /// 构造对话框本体。字体可注入，方便在不同缩放比例下验证布局
        /// （WinForms 的自动缩放正是按字体比例算的，换个字体就等于换了 DPI）。
        /// </summary>
        internal static Form BuildDialog(string title, string label, string initial, Font font, out TextBox textBox)
        {
            Form form = new Form();
            form.Text = title;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.StartPosition = FormStartPosition.CenterParent;
            form.MinimizeBox = false;
            form.MaximizeBox = false;
            form.ShowInTaskbar = false;
            form.Font = font;
            form.AutoScaleMode = AutoScaleMode.Font;
            form.AutoScaleDimensions = DesignScale;
            // 让窗体按内容自动调整大小：字体变大（高 DPI）时窗口跟着长，
            // 不会出现「文字比控件大、按钮被压扁」。
            form.AutoSize = true;
            form.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.AutoSize = true;
            grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 1;
            grid.RowCount = 3;
            grid.Padding = new Padding(12, 12, 12, 12);
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.Controls.Add(grid);

            Label caption = new Label();
            caption.Text = label;
            caption.AutoSize = true;
            caption.Margin = new Padding(0, 0, 0, 6);
            grid.Controls.Add(caption, 0, 0);

            TextBox box = new TextBox();
            box.Text = initial ?? "";
            box.Dock = DockStyle.Fill;
            // 最小宽度用「约 30 个字符宽」：直接由字体量出来，自然跟随 DPI
            int minWidth = TextRenderer.MeasureText(new string('W', 30), font).Width;
            box.MinimumSize = new Size(minWidth, 0);
            grid.Controls.Add(box, 0, 1);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.Margin = new Padding(0);
            grid.Controls.Add(buttons, 0, 2);

            Button ok = new Button();
            ok.Text = "确定";
            ok.DialogResult = DialogResult.OK;
            ok.AutoSize = true;
            ok.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ok.Padding = new Padding(12, 2, 12, 2);
            ok.Margin = new Padding(6, 0, 0, 0);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.AutoSize = true;
            cancel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            cancel.Padding = new Padding(12, 2, 12, 2);
            cancel.Margin = new Padding(6, 0, 0, 0);

            // 中文习惯：「确定」在左、「取消」在右（RightToLeft 流里先加的被放到最右）
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            form.AcceptButton = ok;
            textBox = box;
            return form;
        }
    }
}
