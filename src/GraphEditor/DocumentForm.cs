using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GraphEditor
{
    /// <summary>
    /// 一个文档窗口（MDI 子窗口）：包含自己的工作区、文件路径与修改状态。
    /// 菜单与状态栏由 MDI 父窗口统一持有，操作总是作用于「当前活动文档」。
    /// </summary>
    internal sealed class DocumentForm : Form
    {
        private readonly CanvasControl _canvas = new CanvasControl();
        private string _filePath;
        private bool _dirty;
        private bool _diagnosticMode;

        /// <summary>文档标题/修改状态发生变化时触发（父窗口据此刷新标题与状态栏）。</summary>
        public event EventHandler StateChanged;

        public DocumentForm(bool diagnosticMode)
        {
            _diagnosticMode = diagnosticMode;

            Text = "未命名";
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(940, 640);
            MinimumSize = new Size(420, 340);
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;

            Panel host = new Panel();
            host.Dock = DockStyle.Fill;
            host.Padding = new Padding(6);
            host.BackColor = Color.FromArgb(0x9E, 0xA3, 0xAA);
            _canvas.Dock = DockStyle.Fill;
            host.Controls.Add(_canvas);
            Controls.Add(host);

            _canvas.SelectionChanged += delegate { RaiseStateChanged(); };
            _canvas.CanvasChanged += delegate { MarkDirty(); };
            _canvas.ZoomChanged += delegate { RaiseStateChanged(); };
            _canvas.HintTextChanged += delegate(object sender, HintTextEventArgs e)
            {
                RaiseStateChanged();
            };

            _canvas.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F12 && _diagnosticMode)
                {
                    RaiseStateChanged();
                    e.Handled = true;
                }
            };

            // 子窗口真正布局完成后（客户区尺寸已确定）再按工作区大小定一次视图
            Shown += delegate { _canvas.RefreshScrollRange(); };
        }

        /// <summary>工作区。</summary>
        public CanvasControl CanvasArea { get { return _canvas; } }

        /// <summary>关联的图模型（未附着时为 null）。</summary>
        public GraphCanvas Canvas { get { return _canvas.Canvas; } }

        /// <summary>当前文件路径；未保存过时为 null。</summary>
        public string FilePath
        {
            get { return _filePath; }
        }

        public bool IsDirty
        {
            get { return _dirty; }
        }

        /// <summary>窗口标题里显示的名字。</summary>
        public string DocumentName
        {
            get { return string.IsNullOrEmpty(_filePath) ? "未命名" : Path.GetFileName(_filePath); }
        }

        private void RaiseStateChanged()
        {
            EventHandler handler = StateChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>新建空白图（沿用与「新建」一致的一组默认样式）。</summary>
        public void NewBlank()
        {
            GraphCanvas c = new GraphCanvas(800, 600);
            c.VertexRadius = 18;
            c.VertexShape = VertexShape.Circle;
            c.EdgeWidth = 2;
            c.EdgeLineStyle = EdgeLineStyle.Solid;
            c.DefaultVertexColor = Color.FromArgb(0x2D, 0x7D, 0xD2);
            c.DefaultEdgeColor = Color.FromArgb(0x40, 0x40, 0x40);
            c.VertexNaming = NamingRule.Alphabetic;
            c.EdgeNaming = NamingRule.None;
            c.VertexLabelPlacement = VertexLabelPlacement.Inside;
            c.EdgeLabelPlacement = EdgeLabelPlacement.Above;
            c.MultiGraph = true;
            c.Directed = true;

            Attach(c);
            _filePath = null;
            SetDirty(false);
        }

        /// <summary>把已有的图模型挂到本窗口（打开文件时使用）。</summary>
        public void Attach(GraphCanvas c)
        {
            _canvas.Canvas = c;
            _canvas.RefreshScrollRange();
            _canvas.CenterOnContent();
            UpdateTitle();
        }

        public bool LoadFile(string path)
        {
            GraphCanvas c = GraphIo.Load(path);
            Attach(c);
            _filePath = path;
            SetDirty(false);
            return true;
        }

        /// <summary>保存到当前路径；没有路径或 forceAskPath 时弹另存为。</summary>
        public bool Save(bool forceAskPath)
        {
            string path = _filePath;
            if (forceAskPath || string.IsNullOrEmpty(path))
            {
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Title = "保存图文件";
                    dialog.Filter = GraphIo.FileFilter;
                    dialog.DefaultExt = "graph";
                    dialog.FileName = string.IsNullOrEmpty(path) ? "graph1.graph" : Path.GetFileName(path);
                    if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return false;
                    path = dialog.FileName;
                }
            }

            GraphIo.Save(_canvas.Canvas, path);
            _filePath = path;
            SetDirty(false);
            RaiseStateChanged();
            return true;
        }

        /// <summary>导出为 SVG；返回导出的路径，取消时返回 null。</summary>
        public string ExportSvg()
        {
            string suggested = string.IsNullOrEmpty(_filePath)
                ? "graph1.svg"
                : Path.GetFileNameWithoutExtension(_filePath) + ".svg";
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "导出为 SVG";
                dialog.Filter = "SVG 矢量图 (*.svg)|*.svg|所有文件 (*.*)|*.*";
                dialog.DefaultExt = "svg";
                dialog.FileName = suggested;
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return null;

                SvgExport.Export(_canvas.Canvas, dialog.FileName);
                return dialog.FileName;
            }
        }

        /// <summary>有未保存修改时询问；返回 false 表示用户取消了当前操作。</summary>
        public bool ConfirmDiscardChanges()
        {
            if (_diagnosticMode) return true;
            if (!_dirty) return true;
            DialogResult result = MessageBox.Show(FindForm(),
                "“" + DocumentName + "”有未保存的修改，是否保存？",
                "GraphEditor",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            if (result == DialogResult.Cancel) return false;
            if (result == DialogResult.Yes)
            {
                try
                {
                    return Save(false);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(FindForm(), "保存失败：" + Environment.NewLine + ex.Message,
                        "GraphEditor", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }
            return true;
        }

        public void MarkDirty()
        {
            SetDirty(true);
        }

        public void SetDirty(bool dirty)
        {
            _dirty = dirty;
            UpdateTitle();
            RaiseStateChanged();
        }

        private void UpdateTitle()
        {
            Text = DocumentName + (_dirty ? " *" : "");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            // MDI 父窗口关闭时统一处理，这里只处理「用户关闭单个文档」
            if (e.CloseReason == CloseReason.UserClosing && !ConfirmDiscardChanges())
            {
                e.Cancel = true;
            }
        }
    }
}
