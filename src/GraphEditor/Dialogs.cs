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
                form.Font = SystemFonts.MessageBoxFont;

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
}
