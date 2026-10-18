using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GraphEditor
{
    /// <summary>
    /// 一个文档视图：包含自己的工作区、文件路径与修改状态。
    /// 作为 <see cref="DocumentArea"/> 里的一个标签页存在（不再是独立窗口）。
    /// </summary>
    public sealed class DocumentView : UserControl
    {
        private readonly CanvasControl _canvas = new CanvasControl();
        private string _filePath;
        private bool _dirty;
        private bool _diagnosticMode;

        /// <summary>标题或修改状态变化（标签文字、父窗口标题据此刷新）。</summary>
        public event EventHandler StateChanged;

        public DocumentView(bool diagnosticMode)
        {
            _diagnosticMode = diagnosticMode;

            // 经典外观：交给系统绘制，不做自绘
            BackColor = SystemColors.Control;
            Padding = new Padding(0);
            _canvas.Dock = DockStyle.Fill;
            Controls.Add(_canvas);

            _canvas.SelectionChanged += delegate { RaiseStateChanged(); };
            _canvas.CanvasChanged += delegate { MarkDirty(); };
            _canvas.ZoomChanged += delegate { RaiseStateChanged(); };
            _canvas.HintTextChanged += delegate(object sender, HintTextEventArgs e) { RaiseStateChanged(); };
        }

        public CanvasControl CanvasArea { get { return _canvas; } }

        public GraphCanvas Canvas { get { return _canvas.Canvas; } }

        public string FilePath { get { return _filePath; } }

        public bool IsDirty { get { return _dirty; } }

        /// <summary>标签页上显示的名字。</summary>
        public string DocumentName
        {
            get { return string.IsNullOrEmpty(_filePath) ? "未命名" : Path.GetFileName(_filePath); }
        }

        /// <summary>标签页文字（带修改标记）。</summary>
        public string TabText
        {
            get { return DocumentName + (_dirty ? " *" : ""); }
        }

        private void RaiseStateChanged()
        {
            EventHandler handler = StateChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>新建空白图，采用当前默认设置（无向单重图、半径 10、名字在上方）。</summary>
        public void NewBlank()
        {
            GraphCanvas c = new GraphCanvas();
            c.VertexRadius = 10;
            c.VertexShape = VertexShape.Circle;
            c.EdgeWidth = 2;
            c.EdgeLineStyle = EdgeLineStyle.Solid;
            c.DefaultVertexColor = Color.FromArgb(0x2D, 0x7D, 0xD2);
            c.DefaultEdgeColor = Color.FromArgb(0x40, 0x40, 0x40);
            c.VertexNaming = NamingRule.Alphabetic;
            c.EdgeNaming = NamingRule.None;
            c.VertexLabelPlacement = VertexLabelPlacement.Above;
            c.EdgeLabelPlacement = EdgeLabelPlacement.Above;
            c.MultiGraph = false;
            c.Directed = false;

            Attach(c);
            _filePath = null;
            SetDirty(false);
        }

        public void Attach(GraphCanvas c)
        {
            _canvas.Canvas = c;
            _canvas.RefreshScrollRange();
            _canvas.CenterOnContent();
            RaiseStateChanged();
        }

        public void LoadFile(string path)
        {
            GraphCanvas c = GraphIo.Load(path);
            Attach(c);
            _filePath = path;
            SetDirty(false);
        }

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
            RaiseStateChanged();
        }
    }
}
