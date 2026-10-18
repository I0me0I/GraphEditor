using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace GraphEditor
{
    /// <summary>
    /// MDI 父窗口：菜单栏 + 状态栏，操作总是作用于「当前活动文档」。
    /// 文档以标签页形式摆放（<see cref="DocumentArea"/>），支持拖动标签分栏。
    /// </summary>
    public sealed class MainForm : Form
    {
        private readonly DocumentArea _area = new DocumentArea();
        private ToolStripMenuItem _windowMenu;
        private readonly MenuStrip _menu = new MenuStrip();
        private readonly StatusStrip _status = new StatusStrip();
        private readonly ToolStripStatusLabel _statusLeft = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel _statusRight = new ToolStripStatusLabel();

        private readonly ToolStripMenuItem _menuDelete = new ToolStripMenuItem("删除选中(&D)");
        private readonly ToolStripMenuItem _menuRename = new ToolStripMenuItem("重命名选中(&M)…");
        private readonly ToolStripMenuItem _menuDeselect = new ToolStripMenuItem("取消选择(&U)");
        private readonly ToolStripMenuItem _menuCopy = new ToolStripMenuItem("复制(&C)");
        private readonly ToolStripMenuItem _menuCut = new ToolStripMenuItem("剪切(&T)");
        private readonly ToolStripMenuItem _menuPaste = new ToolStripMenuItem("粘贴(&P)");
        private readonly ToolStripMenuItem _menuRotate = new ToolStripMenuItem("旋转(&R)");

        private bool _diagnosticMode;
        private int _untitledCounter;

        /// <summary>当前活动的文档；没有文档时为 null。</summary>
        private DocumentView ActiveDocument
        {
            get { return _area.ActiveDocument; }
        }

        /// <summary>当前活动文档的工作区；没有文档时为 null。</summary>
        private CanvasControl _canvas
        {
            get
            {
                DocumentView doc = ActiveDocument;
                return doc == null ? null : doc.CanvasArea;
            }
        }

        public MainForm()
            : this(null)
        {
        }

        /// <param name="fileToOpen">启动时要打开的 .graph 文件，可为 null。</param>
        public MainForm(string fileToOpen)
        {
            BuildWindow();
            NewDocument(false);
            _diagnosticMode = TryTakeDiagnosticSwitches();

            if (!string.IsNullOrEmpty(fileToOpen))
            {
                OpenStartupFile(fileToOpen);
            }
        }

        /// <summary>打开命令行指定的文件；失败时提示并保持空白文档。</summary>
        private void OpenStartupFile(string path)
        {
            try
            {
                DocumentView doc = ActiveDocument;
                if (doc == null) { doc = CreateDocument(); }
                doc.LoadFile(path);
                SetHint("已打开 " + Path.GetFileName(path)
                    + "（顶点 " + doc.Canvas.Vertices.Count + "，边 " + doc.Canvas.Edges.Count + "）。");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "无法打开文件：" + Environment.NewLine + path + Environment.NewLine + Environment.NewLine + ex.Message,
                    "GraphEditor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ================= 窗口骨架 =================

        private void BuildWindow()
        {
            Text = "GraphEditor — 图论图编辑器";
            Font = SystemFonts.MessageBoxFont;   // 系统默认字体，随 Windows 语言/DPI 走
            ClientSize = new Size(1180, 760);
            StartPosition = FormStartPosition.CenterScreen;
            // 子窗口最小 420×340，这里保证父窗口放得下它再加上菜单/状态栏
            MinimumSize = new Size(560, 560);
            KeyPreview = true;

            BuildMenu();
            BuildStatusBar();
            ApplyApplicationIcon();

            // 文档区域铺满菜单与状态栏之间的空间
            _area.Dock = DockStyle.Fill;
            Controls.Add(_area);

            // 菜单栏与文档区之间加一条 1px 分隔线：
            // 菜单栏（#F4F4F4）与标签条（#F0F0F0）只差 1.6%，靠系统色区分不出来，
            // 经典 Windows 程序的做法是用一条 ControlDark 的线划开边界。
            Panel menuSeparator = new Panel();
            menuSeparator.Dock = DockStyle.Top;
            menuSeparator.Height = 1;
            menuSeparator.BackColor = SystemColors.ControlDark;
            menuSeparator.TabStop = false;
            Controls.Add(menuSeparator);

            Controls.Add(_menu);
            Controls.Add(_status);
            MainMenuStrip = _menu;

            _area.ActiveDocumentChanged += delegate { OnActiveDocumentChanged(); };
            _area.DocumentsChanged += delegate { UpdateParentTitle(); };
            _area.NewDocumentRequested += delegate { NewDocument(false); };
        }

        /// <summary>供自检使用：菜单栏。</summary>
        public MenuStrip MainMenuBar { get { return _menu; } }

        /// <summary>供自检使用：当前活动文档的工作区。</summary>
        public CanvasControl CanvasArea { get { return _canvas; } }

        /// <summary>供自检使用：当前活动的文档窗口。</summary>
        public DocumentView ActiveDocumentView { get { return ActiveDocument; } }

        /// <summary>供自检使用：文档区域。</summary>
        public DocumentArea DocumentArea { get { return _area; } }

        private void BuildMenu()
        {
            _menu.Font = Font;

            // ---- 文件 ----
            ToolStripMenuItem file = new ToolStripMenuItem("文件(&F)");
            file.DropDownItems.Add(MenuItem("新建(&N)", Keys.Control | Keys.N, delegate { NewDocument(true); }));
            file.DropDownItems.Add(MenuItem("打开(&O)…", Keys.Control | Keys.O, delegate { OpenDocument(); }));
            file.DropDownItems.Add(MenuItem("关闭(&C)", Keys.Control | Keys.W, delegate { CloseDocument(); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(MenuItem("保存(&S)", Keys.Control | Keys.S, delegate { SaveDocument(false); }));
            file.DropDownItems.Add(MenuItem("另存为(&A)…", Keys.Control | Keys.Shift | Keys.S, delegate { SaveDocument(true); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(MenuItem("导出为 SVG(&E)…", Keys.Control | Keys.E, delegate { ExportSvg(); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(MenuItem("退出(&X)", Keys.Alt | Keys.F4, delegate { Close(); }));

            // ---- 编辑 ----
            ToolStripMenuItem edit = new ToolStripMenuItem("编辑(&E)");
            _menuCut.ShortcutKeys = Keys.Control | Keys.X;
            _menuCut.Click += delegate { _canvas.CutSelection(); };
            edit.DropDownItems.Add(_menuCut);
            _menuCopy.ShortcutKeys = Keys.Control | Keys.C;
            _menuCopy.Click += delegate { _canvas.CopySelection(); };
            edit.DropDownItems.Add(_menuCopy);
            _menuPaste.ShortcutKeys = Keys.Control | Keys.V;
            _menuPaste.Click += delegate { _canvas.PasteClipboard(); };
            edit.DropDownItems.Add(_menuPaste);
            edit.DropDownItems.Add(new ToolStripSeparator());
            _menuDelete.ShortcutKeys = Keys.Delete;
            _menuDelete.Click += delegate { _canvas.DeleteSelection(); };
            edit.DropDownItems.Add(_menuDelete);
            _menuRename.Click += delegate { _canvas.RenameSelection(); };
            edit.DropDownItems.Add(_menuRename);
            edit.DropDownItems.Add(MenuItem("全选(&A)", Keys.Control | Keys.A, delegate { _canvas.SelectAll(); }));
            _menuDeselect.Click += delegate { _canvas.ClearSelection(); };
            edit.DropDownItems.Add(_menuDeselect);
            edit.DropDownItems.Add(new ToolStripSeparator());
            _menuRotate.DropDownOpening += delegate { RebuildRotateMenu(); };
            edit.DropDownItems.Add(_menuRotate);

            // ---- 视图 ----
            ToolStripMenuItem view = new ToolStripMenuItem("视图(&V)");
            view.DropDownItems.Add(MenuItem("放大(&I)", Keys.Control | Keys.Oemplus, delegate { _canvas.ZoomBy(1.25); UpdateStatusRight(); }));
            view.DropDownItems.Add(MenuItem("缩小(&O)", Keys.Control | Keys.OemMinus, delegate { _canvas.ZoomBy(1 / 1.25); UpdateStatusRight(); }));
            view.DropDownItems.Add(MenuItem("实际大小(&1)", Keys.Control | Keys.D1, delegate { _canvas.ResetZoom(); UpdateStatusRight(); }));
            view.DropDownItems.Add(MenuItem("适应窗口(&F)", Keys.Control | Keys.D9, delegate { _canvas.ZoomToFit(); UpdateStatusRight(); }));
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add(MenuItem("重置视图(&R)", Keys.Control | Keys.D0,
                delegate { _canvas.ResetView(); UpdateStatusRight(); }));
            view.DropDownItems.Add(MenuItem("回到原点(&C)", Keys.None,
                delegate { _canvas.CenterOnOrigin(); UpdateStatusRight(); SetHint("视图已回到原点。"); }));
            view.DropDownItems.Add(MenuItem("居中显示内容(&M)", Keys.None,
                delegate { _canvas.CenterOnContent(); UpdateStatusRight(); SetHint("已把内容移到视野中央。"); }));

            // ---- 属性 ----
            ToolStripMenuItem properties = new ToolStripMenuItem("属性(&P)");
            properties.DropDownOpening += delegate { RebuildPropertyMenu(properties); };

            // ---- 帮助 ----
            ToolStripMenuItem help = new ToolStripMenuItem("帮助(&H)");
            help.DropDownItems.Add(MenuItem("操作提示(&T)…", Keys.F1, delegate { ShowHelp(); }));
            help.DropDownItems.Add(MenuItem("关于 GraphEditor(&A)…", Keys.None, delegate { ShowAbout(); }));

            _menu.Items.Add(file);
            _menu.Items.Add(edit);
            _menu.Items.Add(view);
            _menu.Items.Add(properties);

            // ---- 窗口（标签页 / 分栏 / 文档列表）----
            _windowMenu = new ToolStripMenuItem("窗口(&W)");
            _windowMenu.DropDownOpening += delegate { RebuildWindowMenu(); };
            _menu.Items.Add(_windowMenu);
            _menu.Items.Add(help);
        }

        /// <summary>重建「窗口」菜单：分栏命令 + 已打开文档列表（当前文档打勾）。</summary>
        private void RebuildWindowMenu()
        {
            _windowMenu.DropDownItems.Clear();

            ToolStripMenuItem sideBySide = new ToolStripMenuItem("左右分栏(&V)");
            sideBySide.Enabled = _area.CanSplitActivePane;
            sideBySide.Click += delegate
            {
                DocumentView doc = ActiveDocument;
                if (doc != null) _area.SplitWith(doc, Orientation.Vertical);
            };
            _windowMenu.DropDownItems.Add(sideBySide);

            ToolStripMenuItem stacked = new ToolStripMenuItem("上下分栏(&H)");
            stacked.Enabled = _area.CanSplitActivePane;
            stacked.Click += delegate
            {
                DocumentView doc = ActiveDocument;
                if (doc != null) _area.SplitWith(doc, Orientation.Horizontal);
            };
            _windowMenu.DropDownItems.Add(stacked);

            ToolStripMenuItem merge = new ToolStripMenuItem("合并为单栏(&M)");
            merge.Enabled = _area.IsSplit;
            merge.Click += delegate { _area.MergeToSinglePane(); };
            _windowMenu.DropDownItems.Add(merge);

            _windowMenu.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem close = new ToolStripMenuItem("关闭当前文档(&W)");
            close.ShortcutKeyDisplayString = "Ctrl+W";
            close.Enabled = _area.DocumentCount > 0;
            close.Click += delegate { CloseDocument(); };
            _windowMenu.DropDownItems.Add(close);

            ToolStripMenuItem next = new ToolStripMenuItem("下一个文档(&N)");
            next.Click += delegate { _area.ActivateNext(true); };
            _windowMenu.DropDownItems.Add(next);
            ToolStripMenuItem prev = new ToolStripMenuItem("上一个文档(&P)");
            prev.Click += delegate { _area.ActivateNext(false); };
            _windowMenu.DropDownItems.Add(prev);

            if (_area.DocumentCount > 0)
            {
                _windowMenu.DropDownItems.Add(new ToolStripSeparator());
                DocumentView active = ActiveDocument;
                foreach (DocumentView doc in _area.AllDocuments)
                {
                    ToolStripMenuItem item = new ToolStripMenuItem(doc.DocumentName);
                    item.Checked = (doc == active);
                    DocumentView captured = doc;
                    item.Click += delegate { _area.Activate(captured); };
                    _windowMenu.DropDownItems.Add(item);
                }
            }
        }

        private static ToolStripMenuItem MenuItem(string text, Keys shortcut, EventHandler handler)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            if (shortcut != Keys.None)
            {
                item.ShortcutKeys = shortcut;
                item.ShowShortcutKeys = true;
            }
            item.Click += handler;
            return item;
        }

        /// <summary>供自检使用：「属性」菜单项。</summary>
        public ToolStripMenuItem PropertyMenuItem
        {
            get
            {
                foreach (ToolStripItem item in _menu.Items)
                {
                    ToolStripMenuItem menuItem = item as ToolStripMenuItem;
                    if (menuItem != null && menuItem.Text != null && menuItem.Text.StartsWith("属性", StringComparison.Ordinal))
                    {
                        return menuItem;
                    }
                }
                return null;
            }
        }

        /// <summary>供自检使用：重建「属性」菜单内容。</summary>
        public void RefreshPropertyMenu()
        {
            ToolStripMenuItem properties = PropertyMenuItem;
            if (properties != null) RebuildPropertyMenu(properties);
        }

        private void RebuildRotateMenu()
        {
            _menuRotate.DropDownItems.Clear();
            _menuRotate.Enabled = _canvas.SelectedVertexCount > 0;
            AddRotateEntry("顺时针 15°", 15);
            AddRotateEntry("顺时针 90°", 90);
            AddRotateEntry("顺时针 180°", 180);
            AddRotateEntry("逆时针 90°", -90);
            AddRotateEntry("逆时针 15°", -15);
            _menuRotate.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem custom = new ToolStripMenuItem("自定义角度(&A)…");
            custom.Click += delegate { PromptRotate(); };
            _menuRotate.DropDownItems.Add(custom);
        }

        private void AddRotateEntry(string text, double degrees)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += delegate { _canvas.RotateSelection(degrees); };
            _menuRotate.DropDownItems.Add(item);
        }

        private void PromptRotate()
        {
            string text;
            if (!PromptDialog.ShowText(this, "旋转选中的顶点",
                "旋转角度（度，正数为顺时针）：", "45", out text)) return;
            double degrees;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out degrees))
            {
                SetHint("角度无法识别：" + text);
                return;
            }
            _canvas.RotateSelection(degrees);
        }

        /// <summary>
        /// 重建「属性」菜单：顶点属性（含命名规则与名字位置）、边属性、画布。
        /// 每次展开时重建，保证勾选状态与当前画布一致。
        /// </summary>
        private void RebuildPropertyMenu(ToolStripMenuItem properties)
        {
            properties.DropDownItems.Clear();
            GraphCanvas c = _canvas.Canvas;
            if (c == null) return;

            // ---- 顶点属性（一组全局样式，作用于所有顶点）----
            ToolStripMenuItem vertex = new ToolStripMenuItem("顶点");
            vertex.DropDownItems.Add(GroupLabel("作用范围：所有顶点"));
            vertex.DropDownItems.Add(new ToolStripSeparator());
            vertex.DropDownItems.Add(BuildColorMenu("颜色(&C)", c.DefaultVertexColor, OnDefaultVertexColor));
            vertex.DropDownItems.Add(BuildChoiceMenu("大小(&S)", ToText(c.VertexRadius),
                new string[] { "8", "10", "12", "14", "16", "18", "20", "24", "28", "32", "40", "48", "60" },
                delegate(string text)
                {
                    double value;
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return;
                    c.VertexRadius = value;
                    MarkDirty();
                    _canvas.RefreshScrollRange();
                    SetHint("所有顶点的大小已设为 " + text + "。");
                }));
            vertex.DropDownItems.Add(BuildChoiceMenu("形状(&H)", GraphCanvas.ShapeDisplayName(c.VertexShape),
                new string[] { "圆形", "方形" },
                delegate(string text)
                {
                    c.VertexShape = text == "方形" ? VertexShape.Square : VertexShape.Circle;
                    MarkDirty();
                    _canvas.Invalidate();
                    SetHint("所有顶点的形状已设为 " + text + "。");
                }));
            vertex.DropDownItems.Add(BuildChoiceMenu("默认命名(&N)", VertexNamingDisplay(c.VertexNaming),
                new string[] { "不命名", "大写字母", "字母V+数字" },
                delegate(string text)
                {
                    c.VertexNaming = VertexNamingFromDisplay(text);
                    MarkDirty();
                    SetHint("新建顶点的命名规则已设为「" + text + "」（不影响已有顶点）");
                }));
            vertex.DropDownItems.Add(BuildChoiceMenu("名字位置(&L)", VertexPlacementDisplay(c.VertexLabelPlacement),
                new string[] { "内部", "上方", "下方", "左侧", "右侧" },
                delegate(string text)
                {
                    c.VertexLabelPlacement = VertexPlacementFromDisplay(text);
                    MarkDirty();
                    _canvas.RefreshScrollRange();
                    SetHint("顶点名字显示位置已设为「" + text + "」");
                }));

            // ---- 边属性（另一组全局样式，作用于所有边）----
            ToolStripMenuItem edge = new ToolStripMenuItem("边");
            edge.DropDownItems.Add(GroupLabel("作用范围：所有边"));
            edge.DropDownItems.Add(new ToolStripSeparator());
            edge.DropDownItems.Add(BuildColorMenu("颜色(&C)", c.DefaultEdgeColor, OnDefaultEdgeColor));
            edge.DropDownItems.Add(BuildChoiceMenu("线宽(&W)", ToText(c.EdgeWidth),
                new string[] { "1", "1.5", "2", "2.5", "3", "4", "5", "6", "8", "10" },
                delegate(string text)
                {
                    double value;
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return;
                    c.EdgeWidth = value;
                    MarkDirty();
                    _canvas.Invalidate();
                    SetHint("所有边的线宽已设为 " + text + "。");
                }));
            edge.DropDownItems.Add(BuildChoiceMenu("线型(&L)", GraphCanvas.LineStyleDisplayName(c.EdgeLineStyle),
                new string[] { "实线", "虚线", "点线" },
                delegate(string text)
                {
                    c.EdgeLineStyle = text == "虚线" ? EdgeLineStyle.Dashed
                        : text == "点线" ? EdgeLineStyle.Dotted : EdgeLineStyle.Solid;
                    MarkDirty();
                    _canvas.Invalidate();
                    SetHint("所有边的线型已设为 " + text + "。");
                }));
            edge.DropDownItems.Add(BuildChoiceMenu("默认命名(&N)", EdgeNamingDisplay(c.EdgeNaming),
                new string[] { "不命名", "小写字母", "字母e+数字", "两端点名字" },
                delegate(string text)
                {
                    c.EdgeNaming = EdgeNamingFromDisplay(text);
                    MarkDirty();
                    SetHint("新建边的命名规则已设为「" + text + "」（不影响已有的边）");
                }));
            edge.DropDownItems.Add(BuildChoiceMenu("名字位置(&L)", EdgePlacementDisplay(c.EdgeLabelPlacement),
                new string[] { "内部", "上方", "下方" },
                delegate(string text)
                {
                    c.EdgeLabelPlacement = text == "内部" ? EdgeLabelPlacement.Inside
                        : text == "下方" ? EdgeLabelPlacement.Below : EdgeLabelPlacement.Above;
                    MarkDirty();
                    _canvas.Invalidate();
                    SetHint("边名字显示位置已设为「" + text + "」");
                }));

            // ---- 图类型（多重图 / 有向图）----
            ToolStripMenuItem graph = new ToolStripMenuItem("图");
            graph.DropDownItems.Add(GroupLabel("作用范围：当前文档"));
            graph.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem multiGraph = new ToolStripMenuItem("多重图(&M)");
            multiGraph.Checked = c.MultiGraph;
            multiGraph.Click += delegate
            {
                c.MultiGraph = !c.MultiGraph;
                if (!c.MultiGraph)
                {
                    int removed = c.RemoveExtraEdges();
                    _canvas.RefreshScrollRange();
                    SetHint(removed > 0
                        ? "已关闭多重图：删除了 " + removed + " 条多余的边（重边与自环）"
                        : "已关闭多重图：同一对顶点之间最多一条边，且不允许自环");
                }
                else
                {
                    SetHint("已开启多重图：允许重边与自环");
                }
                MarkDirty();
                _canvas.Invalidate();
            };
            graph.DropDownItems.Add(multiGraph);

            ToolStripMenuItem directed = new ToolStripMenuItem("有向图(&D)");
            directed.Checked = c.Directed;
            directed.Click += delegate
            {
                c.Directed = !c.Directed;
                if (!c.Directed)
                {
                    // 变成无向之后 A→B 与 B→A 就是同一条边；单重图下要删掉多余的那条
                    int removed = c.MultiGraph ? 0 : c.RemoveExtraEdges();
                    SetHint(removed > 0
                        ? "已关闭有向图：隐藏箭头，并删除了 " + removed + " 条重复的边（A→B 与 B→A 合并为一条）"
                        : "已关闭有向图：隐藏箭头（边保留）");
                }
                else
                {
                    SetHint("已开启有向图：边在终点显示箭头");
                }
                MarkDirty();
                _canvas.Invalidate();
                _canvas.RefreshScrollRange();
            };
            graph.DropDownItems.Add(directed);

            properties.DropDownItems.Add(vertex);
            properties.DropDownItems.Add(edge);
            properties.DropDownItems.Add(graph);
        }

        private static ToolStripMenuItem GroupLabel(string text)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Enabled = false;
            return item;
        }

        /// <summary>顶点的命名规则显示文本（只有「不命名 / 字母 / 前缀+数字」三种）。</summary>
        private static string VertexNamingDisplay(NamingRule rule)
        {
            switch (rule)
            {
                case NamingRule.None: return "不命名";
                case NamingRule.Numeric: return "字母V+数字";
                default: return "大写字母";
            }
        }

        private static NamingRule VertexNamingFromDisplay(string text)
        {
            if (text == "不命名") return NamingRule.None;
            if (text == "字母V+数字") return NamingRule.Numeric;
            return NamingRule.Alphabetic;
        }

        /// <summary>边的命名规则显示文本（多出「两端点名字」）。</summary>
        private static string EdgeNamingDisplay(NamingRule rule)
        {
            switch (rule)
            {
                case NamingRule.None: return "不命名";
                case NamingRule.Numeric: return "字母e+数字";
                case NamingRule.FromEndpoints: return "两端点名字";
                default: return "小写字母";
            }
        }

        private static NamingRule EdgeNamingFromDisplay(string text)
        {
            if (text == "不命名") return NamingRule.None;
            if (text == "字母e+数字") return NamingRule.Numeric;
            if (text == "两端点名字") return NamingRule.FromEndpoints;
            return NamingRule.Alphabetic;
        }

        private static string VertexPlacementDisplay(VertexLabelPlacement placement)
        {
            switch (placement)
            {
                case VertexLabelPlacement.Above: return "上方";
                case VertexLabelPlacement.Below: return "下方";
                case VertexLabelPlacement.Left: return "左侧";
                case VertexLabelPlacement.Right: return "右侧";
                default: return "内部";
            }
        }

        private static VertexLabelPlacement VertexPlacementFromDisplay(string text)
        {
            if (text == "上方") return VertexLabelPlacement.Above;
            if (text == "下方") return VertexLabelPlacement.Below;
            if (text == "左侧") return VertexLabelPlacement.Left;
            if (text == "右侧") return VertexLabelPlacement.Right;
            return VertexLabelPlacement.Inside;
        }

        private static string EdgePlacementDisplay(EdgeLabelPlacement placement)
        {
            switch (placement)
            {
                case EdgeLabelPlacement.Inside: return "内部";
                case EdgeLabelPlacement.Below: return "下方";
                default: return "上方";
            }
        }

        private ToolStripMenuItem BuildColorMenu(string text, Color current, EventHandler<Color> onPick)
        {
            ToolStripMenuItem menu = new ToolStripMenuItem(text);
            menu.Image = CanvasControl.MakeSwatchImage(current, 16, false);
            foreach (Color color in CanvasControl.Palette)
            {
                ToolStripMenuItem item = new ToolStripMenuItem();
                item.Text = GraphIo.ColorToHex(color);
                bool isCurrent = color.ToArgb() == current.ToArgb();
                item.Image = CanvasControl.MakeSwatchImage(color, 14, isCurrent);
                item.Checked = isCurrent;
                Color captured = color;
                item.Click += delegate(object sender, EventArgs e) { onPick(sender, captured); };
                menu.DropDownItems.Add(item);
            }
            return menu;
        }

        private static ToolStripMenuItem BuildChoiceMenu(string text, string current, string[] options, Action<string> onPick)
        {
            ToolStripMenuItem menu = new ToolStripMenuItem(text);
            menu.Text = text + "（" + current + "）";
            foreach (string option in options)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(option);
                item.Checked = string.Equals(option, current, StringComparison.Ordinal);
                string captured = option;
                item.Click += delegate { onPick(captured); };
                menu.DropDownItems.Add(item);
            }
            return menu;
        }

        private static string ToText(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private void OnDefaultVertexColor(object sender, Color c)
        {
            if (_canvas.Canvas == null) return;
            _canvas.Canvas.DefaultVertexColor = c;
            MarkDirty();
            _canvas.Invalidate();
            SetHint("所有顶点的默认颜色已设为 " + GraphIo.ColorToHex(c) + "（未单独着色的顶点会跟随变化）。");
        }

        private void OnDefaultEdgeColor(object sender, Color c)
        {
            if (_canvas.Canvas == null) return;
            _canvas.Canvas.DefaultEdgeColor = c;
            MarkDirty();
            _canvas.Invalidate();
            SetHint("所有边的默认颜色已设为 " + GraphIo.ColorToHex(c) + "（未单独着色的边会跟随变化）。");
        }

        // ================= 状态栏 =================

        private void BuildStatusBar()
        {
            _status.Font = Font;
            _statusLeft.Spring = true;
            _statusLeft.TextAlign = ContentAlignment.MiddleLeft;
            _statusRight.TextAlign = ContentAlignment.MiddleRight;
            _status.Items.Add(_statusLeft);
            _status.Items.Add(_statusRight);
            SetHint("在工作区空白处右击可新建顶点；在顶点上按住右键拖动即可连线；滚轮缩放，Ctrl+C/X/V 复制剪切粘贴。");
        }

        private void SetHint(string text)
        {
            _statusLeft.Text = text;
        }

        private string DescribeSelection()
        {
            if (_canvas == null) return "无文档";
            int v = _canvas.SelectedVertexCount;
            int e = _canvas.SelectedEdgeCount;
            if (v == 0 && e == 0) return "未选中";
            if (e == 0) return "已选中 " + v + " 个顶点";
            if (v == 0) return "已选中 " + e + " 条边";
            return "已选中 " + v + " 个顶点、" + e + " 条边";
        }

        private void UpdateSelectionUi()
        {
            bool hasDoc = _canvas != null;
            bool has = hasDoc && _canvas.HasSelection;
            bool hasVertices = hasDoc && _canvas.SelectedVertexCount > 0;
            _menuDelete.Enabled = has;
            _menuRename.Enabled = has;
            _menuDeselect.Enabled = has;
            _menuCopy.Enabled = hasVertices;
            _menuCut.Enabled = hasVertices;
            _menuRotate.Enabled = hasVertices;
            _menuPaste.Enabled = CanvasControl.HasClipboardContent && hasDoc;
            UpdateStatusRight();
        }

        private void UpdateStatusRight()
        {
            if (_canvas == null)
            {
                _statusRight.Text = "无文档";
                return;
            }
            _statusRight.Text = DescribeSelection() + "    缩放 "
                + (_canvas.Zoom * 100).ToString("0", CultureInfo.InvariantCulture) + "%"
                + "    文档 " + _area.DocumentCount;
        }

        // ================= 文档（MDI） =================

        /// <summary>
        /// 新建一个文档子窗口。打开文件也走这里 —— 每个文档一个窗口，
        /// 互不影响（各自持有自己的工作区、路径与修改状态）。
        /// </summary>
        private DocumentView CreateDocument()
        {
            DocumentView doc = new DocumentView(_diagnosticMode);
            doc.UntitledNumber = ++_untitledCounter;   // 未命名1、未命名2、……
            doc.CanvasArea.SelectionChanged += delegate { if (doc == ActiveDocument) UpdateSelectionUi(); };
            doc.CanvasArea.ZoomChanged += delegate { if (doc == ActiveDocument) UpdateStatusRight(); };
            doc.CanvasArea.HintTextChanged += delegate(object sender, HintTextEventArgs e)
            {
                if (doc == ActiveDocument) SetHint(e.Text);
            };
            doc.CanvasArea.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                HandleDocumentKey(e);
            };
            doc.NewBlank();
            _area.AddDocument(doc);
            return doc;
        }

        private void NewDocument(bool askSave)
        {
            CreateDocument();
            SetHint("已新建空白文档。画布是无限平面，滚动条随内容出现。");
            UpdateSelectionUi();
        }

        private void OpenDocument()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "打开图文件";
                dialog.Filter = GraphIo.FileFilter;
                dialog.DefaultExt = "graph";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    DocumentView doc = CreateDocument();
                    doc.LoadFile(dialog.FileName);
                    SetHint("已打开 " + Path.GetFileName(dialog.FileName)
                        + "（顶点 " + doc.Canvas.Vertices.Count + "，边 " + doc.Canvas.Edges.Count + "）。");
                    UpdateSelectionUi();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "无法打开该文件：" + Environment.NewLine + ex.Message,
                        "GraphEditor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>关闭当前文档（会先询问保存）。</summary>
        private void CloseDocument()
        {
            DocumentView doc = ActiveDocument;
            if (doc != null) _area.CloseDocument(doc);
        }

        private bool SaveDocument(bool forceAskPath)
        {
            DocumentView doc = ActiveDocument;
            if (doc == null) return false;
            try
            {
                if (!doc.Save(forceAskPath)) return false;
                SetHint("已保存到 " + doc.FilePath);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + Environment.NewLine + ex.Message,
                    "GraphEditor", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void ExportSvg()
        {
            DocumentView doc = ActiveDocument;
            if (doc == null) return;
            try
            {
                string path = doc.ExportSvg();
                if (string.IsNullOrEmpty(path)) return;
                RectD bounds = SvgExport.ExportBounds(doc.Canvas);
                SetHint("已导出 SVG：" + path + "（内容区域 "
                    + ToText(bounds.Width) + " × " + ToText(bounds.Height) + "，含 "
                    + ToText(SvgExport.Padding) + " 像素留白）");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + Environment.NewLine + ex.Message,
                    "GraphEditor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>子窗口标题/修改状态变化时刷新父窗口标题与状态栏。</summary>
        private void OnDocumentStateChanged(DocumentView doc)
        {
            if (doc == ActiveDocument)
            {
                UpdateSelectionUi();
                UpdateParentTitle();
            }
        }

        /// <summary>切换活动文档（标签切换 / 分栏后）时刷新标题、菜单与状态栏。</summary>
        private void OnActiveDocumentChanged()
        {
            UpdateSelectionUi();
            UpdateParentTitle();
        }

        private void UpdateParentTitle()
        {
            DocumentView doc = ActiveDocument;
            if (doc == null)
            {
                Text = "GraphEditor — 图论图编辑器";
                return;
            }
            Text = "GraphEditor — " + doc.TabText;
        }

        private void HandleDocumentKey(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.S) { SaveDocument(false); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.O) { OpenDocument(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.N) { NewDocument(true); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.W) { CloseDocument(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.Tab) { _area.ActivateNext(true); e.Handled = true; }
            else if (e.KeyCode == Keys.F12 && _diagnosticMode) { DumpStateSnapshot(); e.Handled = true; }
        }


        /// <summary>拖动标签分栏时按 Esc 取消。</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape && _area.IsDraggingTab)
            {
                _area.CancelDrag();
                e.Handled = true;
            }
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;

            // 关闭主窗口前先逐个询问未保存的文档
            foreach (DocumentView doc in _area.AllDocuments.ToArray())
            {
                if (!doc.ConfirmDiscardChanges())
                {
                    e.Cancel = true;
                    return;
                }
            }
        }

        private void MarkDirty()
        {
            DocumentView doc = ActiveDocument;
            if (doc != null) doc.MarkDirty();
            UpdateStatusRight();
        }

        private void SetDirty(bool dirty)
        {
            DocumentView doc = ActiveDocument;
            if (doc != null) doc.SetDirty(dirty);
            UpdateParentTitle();
        }

        // ================= 诊断支持（开发期使用） =================

        /// <summary>
        /// 诊断开关（仅用于开发期排查，正常使用不会触发）：
        ///   /dump:路径    把滚动映射与画布尺寸写入文件
        ///   /open:路径    启动时直接打开一个 .graph 文件
        ///   /diag         在状态栏实时显示滚动/视图读数
        ///   /diaglog:路径 每 120ms 把视图状态追加写入文件
        ///   /diagshot:路径 启动后把工作区渲染成 PNG
        ///   /zoom:倍率    设定初始缩放
        ///   /scroll:x,y   设定初始滚动条位置
        /// 返回 true 表示处于诊断模式（此时关闭窗口不提示保存）。
        /// </summary>
        private bool TryTakeDiagnosticSwitches()
        {
            string target = null;
            string graphToOpen = null;
            string diagLog = null;
            string scrollSpec = null;
            string zoomSpec = null;
            string shotPath = null;
            bool readout = false;
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] == null) continue;
                if (arguments[i].StartsWith("/dump:", StringComparison.OrdinalIgnoreCase))
                {
                    target = arguments[i].Substring("/dump:".Length);
                }
                else if (arguments[i].StartsWith("/open:", StringComparison.OrdinalIgnoreCase))
                {
                    graphToOpen = arguments[i].Substring("/open:".Length);
                }
                else if (arguments[i].StartsWith("/diaglog:", StringComparison.OrdinalIgnoreCase))
                {
                    diagLog = arguments[i].Substring("/diaglog:".Length);
                }
                else if (arguments[i].StartsWith("/diagshot:", StringComparison.OrdinalIgnoreCase))
                {
                    shotPath = arguments[i].Substring("/diagshot:".Length);
                }
                else if (arguments[i].StartsWith("/scroll:", StringComparison.OrdinalIgnoreCase))
                {
                    scrollSpec = arguments[i].Substring("/scroll:".Length);
                }
                else if (arguments[i].StartsWith("/zoom:", StringComparison.OrdinalIgnoreCase))
                {
                    zoomSpec = arguments[i].Substring("/zoom:".Length);
                }
                else if (string.Equals(arguments[i], "/diag", StringComparison.OrdinalIgnoreCase))
                {
                    readout = true;
                }
            }

            if (!string.IsNullOrEmpty(graphToOpen))
            {
                string file = graphToOpen;
                Shown += delegate { OpenPathForDiagnostics(file); };
            }

            if (!string.IsNullOrEmpty(target))
            {
                string finalTarget = target;
                Shown += delegate { WriteMetricsDump(finalTarget); };
            }

            if (!string.IsNullOrEmpty(scrollSpec) || !string.IsNullOrEmpty(zoomSpec))
            {
                string scrollFinal = scrollSpec;
                string zoomFinal = zoomSpec;
                Shown += delegate { ApplyViewSpec(zoomFinal, scrollFinal); };
            }

            if (!string.IsNullOrEmpty(shotPath))
            {
                string shotFinal = shotPath;
                Shown += delegate
                {
                    Application.DoEvents();
                    SaveCanvasSnapshot(shotFinal);
                };
            }

            if (readout)
            {
                Timer readoutTimer = new Timer();
                readoutTimer.Interval = 120;
                readoutTimer.Tick += delegate { UpdateScrollReadout(); };
                Shown += delegate { readoutTimer.Start(); UpdateScrollReadout(); };
            }

            if (!string.IsNullOrEmpty(diagLog))
            {
                string logPath = diagLog;
                Timer logTimer = new Timer();
                logTimer.Interval = 120;
                logTimer.Tick += delegate { AppendDiagLog(logPath); };
                Shown += delegate { logTimer.Start(); };
            }

            return !string.IsNullOrEmpty(graphToOpen) || !string.IsNullOrEmpty(target) || readout
                || !string.IsNullOrEmpty(diagLog);
        }

        private void OpenPathForDiagnostics(string path)
        {
            try
            {
                DocumentView doc = ActiveDocument;
                if (doc == null) doc = CreateDocument();
                doc.LoadFile(path);
                SetHint("已打开 " + Path.GetFileName(path)
                    + "（顶点 " + doc.Canvas.Vertices.Count + "，边 " + doc.Canvas.Edges.Count + "）。");
            }
            catch (Exception ex)
            {
                SetHint("诊断打开失败：" + ex.Message);
            }
        }

        private void ApplyViewSpec(string zoomSpec, string scrollSpec)
        {
            if (_canvas.Canvas == null) return;

            double zoom;
            if (!string.IsNullOrEmpty(zoomSpec)
                && double.TryParse(zoomSpec, NumberStyles.Float, CultureInfo.InvariantCulture, out zoom))
            {
                _canvas.ZoomToFit();
                _canvas.SetZoom(zoom);
            }

            if (!string.IsNullOrEmpty(scrollSpec))
            {
                string[] parts = scrollSpec.Split(',');
                int x;
                int y;
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), out x)
                    && int.TryParse(parts[1].Trim(), out y))
                {
                    _canvas.SetScrollPosition(x, y);
                }
            }

            UpdateStatusRight();
            UpdateScrollReadout();
        }

        /// <summary>状态栏实时读数：视图原点、滚动条位置、可滚动范围。</summary>
        private void UpdateScrollReadout()
        {
            if (_canvas.Canvas == null) return;
            PointD origin = _canvas.ViewOrigin;
            Point scroll = _canvas.ScrollPosition;
            Point range = _canvas.ScrollRange;
            _statusRight.Text = string.Format(CultureInfo.InvariantCulture,
                "视图原点=({0:0},{1:0})  滚动条=({2},{3})  可滚动上限=({4},{5})  画布={6}x{7}  缩放={8:0}%",
                origin.X, origin.Y, scroll.X, scroll.Y, range.X, range.Y,
                _canvas.Canvas.CanvasWidth, _canvas.Canvas.CanvasHeight, _canvas.Zoom * 100);
            _statusLeft.Spring = false;
            _statusRight.Spring = true;
            _status.Invalidate();
        }

        private void AppendDiagLog(string path)
        {
            if (_canvas.Canvas == null) return;
            try
            {
                PointD origin = _canvas.ViewOrigin;
                Point scroll = _canvas.ScrollPosition;
                Point range = _canvas.ScrollRange;
                string line = string.Format(CultureInfo.InvariantCulture,
                    "{0:HH:mm:ss.fff} origin=({1:0.##},{2:0.##}) bar=({3},{4}) max=({5},{6}) zoom={7:0.###} canvas={8}x{9}\n",
                    DateTime.Now, origin.X, origin.Y, scroll.X, scroll.Y, range.X, range.Y, _canvas.Zoom,
                    _canvas.Canvas.CanvasWidth, _canvas.Canvas.CanvasHeight);
                File.AppendAllText(path, line, new System.Text.UTF8Encoding(false));
            }
            catch
            {
                // 诊断日志失败不影响使用
            }
        }

        /// <summary>诊断模式下按 F12：把当前视图状态写入程序目录的 scroll-state.txt。</summary>
        private void DumpStateSnapshot()
        {
            if (_canvas.Canvas == null) return;
            try
            {
                PointD origin = _canvas.ViewOrigin;
                Point scroll = _canvas.ScrollPosition;
                Point range = _canvas.ScrollRange;
                Size viewport = _canvas.ViewportSize;
                double visibleHeight = viewport.Height / _canvas.Zoom;
                double visibleWidth = viewport.Width / _canvas.Zoom;

                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scroll-state.txt");
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("# 状态快照 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("zoom=" + _canvas.Zoom.ToString("0.####", CultureInfo.InvariantCulture));
                sb.AppendLine("viewport=" + viewport.Width + "x" + viewport.Height);
                sb.AppendLine("canvas=" + _canvas.Canvas.CanvasWidth.ToString("0", CultureInfo.InvariantCulture)
                    + "x" + _canvas.Canvas.CanvasHeight.ToString("0", CultureInfo.InvariantCulture));
                sb.AppendLine("viewOrigin=(" + origin.X.ToString("0.##", CultureInfo.InvariantCulture)
                    + "," + origin.Y.ToString("0.##", CultureInfo.InvariantCulture) + ")");
                sb.AppendLine("scrollBar=(" + scroll.X + "," + scroll.Y + ")");
                sb.AppendLine("scrollMax=(" + range.X + "," + range.Y + ")");
                sb.AppendLine("visibleImageTop=" + origin.Y.ToString("0.##", CultureInfo.InvariantCulture));
                sb.AppendLine("visibleImageBottom=" + (origin.Y + visibleHeight).ToString("0.##", CultureInfo.InvariantCulture));
                sb.AppendLine("visibleImageLeft=" + origin.X.ToString("0.##", CultureInfo.InvariantCulture));
                sb.AppendLine("visibleImageRight=" + (origin.X + visibleWidth).ToString("0.##", CultureInfo.InvariantCulture));
                sb.AppendLine("selectedVertices=" + _canvas.SelectedVertexCount);
                sb.AppendLine("selectedEdges=" + _canvas.SelectedEdgeCount);
                File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));

                SetHint("状态已写入 " + path + "（视图顶部对应图像 y="
                    + origin.Y.ToString("0", CultureInfo.InvariantCulture) + "）");
            }
            catch (Exception ex)
            {
                SetHint("状态导出失败：" + ex.Message);
            }
        }

        private void SaveCanvasSnapshot(string path)
        {
            try
            {
                Size size = _canvas.ClientSize;
                if (size.Width <= 0 || size.Height <= 0) size = _canvas.Size;
                using (Bitmap bmp = new Bitmap(size.Width, size.Height))
                {
                    _canvas.DrawToBitmap(bmp, new Rectangle(0, 0, size.Width, size.Height));
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }

                DumpStateSnapshot();
                SetHint("工作区快照已写入 " + path + "（尺寸 " + size.Width + "×" + size.Height + "）");
            }
            catch (Exception ex)
            {
                SetHint("快照失败：" + ex.Message);
            }
        }

        private void WriteMetricsDump(string target)
        {
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("clientWidth=" + ClientSize.Width);
                sb.AppendLine("menuHeight=" + _menu.Height);
                sb.AppendLine("statusHeight=" + _status.Height);
                sb.AppendLine("canvasClient=" + _canvas.ClientSize.Width + "x" + _canvas.ClientSize.Height);
                sb.AppendLine("--- scroll ---");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "canvas={0}x{1} viewport={2} zoom={3:0.###} scrollRange={4}",
                    _canvas.Canvas.CanvasWidth, _canvas.Canvas.CanvasHeight,
                    _canvas.ViewportSize, _canvas.Zoom, _canvas.ScrollRange));
                WriteScrollProbe(sb, "scroll=0", 0, 0);
                WriteScrollProbe(sb, "scrollX=10%", _canvas.ScrollRange.X / 10, 0);
                WriteScrollProbe(sb, "scrollX=50%", _canvas.ScrollRange.X / 2, 0);
                WriteScrollProbe(sb, "scrollX=100%", _canvas.ScrollRange.X, 0);
                WriteScrollProbe(sb, "scrollY=100%", 0, _canvas.ScrollRange.Y);
                sb.AppendLine("scrollBarValue=" + _canvas.ScrollPosition);
                sb.AppendLine("originNow=" + _canvas.ViewOrigin.X.ToString("0.##", CultureInfo.InvariantCulture)
                    + "," + _canvas.ViewOrigin.Y.ToString("0.##", CultureInfo.InvariantCulture));

                File.WriteAllText(target, sb.ToString(), new System.Text.UTF8Encoding(false));
            }
            catch
            {
                // 诊断失败不影响正常使用
            }
        }

        private void WriteScrollProbe(System.Text.StringBuilder sb, string label, int x, int y)
        {
            _canvas.SetScrollPosition(x, y);
            PointD origin = _canvas.ViewOrigin;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-14} bar={1} origin=({2:0.##},{3:0.##})",
                label, _canvas.ScrollPosition, origin.X, origin.Y));
        }

        // ================= 帮助 =================

        private void ShowHelp()
        {
            string text =
                "工作区\r\n" +
                "  • 空白处右击 → 新建顶点 / 粘贴；鼠标滚轮缩放；空白处或边上右键拖动平移画布。\r\n" +
                "  • 左键点击选中顶点或边；按住 Ctrl 左键点击可多选；在空白处按住左键拖动可框选。\r\n" +
                "  • 按住左键拖动选中的顶点即可移动（关联的边会自动跟随）。\r\n" +
                "  • 连线：在顶点上按住右键拖动到另一个顶点，松开即建立一条边。\r\n" +
                "    拖动中鼠标旁有提示；拖到空白处松开或按 Esc 取消；拖回自身即自环。\r\n" +
                "  • 未拖动的右键单击仍会弹出菜单。\r\n" +
                "  • 双击顶点或边可直接重命名；Delete 删除选中；方向键微调位置（Shift 加速）。\r\n" +
                "\r\n" +
                "复制 / 剪切 / 粘贴 / 旋转（右键菜单或编辑菜单）\r\n" +
                "  • 复制：复制选中顶点集所诱导的子图（选中的顶点 + 两端点都被选中的边）。\r\n" +
                "  • 剪切：在复制之后删除选中的顶点，并删除所有与它们相连的边\r\n" +
                "    （只连一个选中顶点的边也会被删除）。\r\n" +
                "  • 粘贴：把剪贴板内容放到当前视图中心，名字冲突时自动加编号。\r\n" +
                "  • 旋转：绕选中顶点的中心旋转，可选 15°/90°/180° 或输入自定义角度（正数为顺时针）。\r\n" +
                "  • 快捷键：Ctrl+C 复制、Ctrl+X 剪切、Ctrl+V 粘贴。\r\n" +
                "\r\n" +
                "画布尺寸（自动）\r\n" +
                "  • 图内容超出画布时自动扩展；内容收回后（拖动结束、删除、粘贴等时机）自动收缩空白。\r\n" +
                "  • 也可用「视图 → 按内容调整画布」手动触发一次。\r\n" +
                "  • 导出 SVG 时只保留包含图内容的最小矩形，并在四周留出少量空白。\r\n" +
                "\r\n" +
                "菜单栏\r\n" +
                "  • 文件：新建 / 打开 / 保存 / 另存为 / 导出为 SVG。\r\n" +
                "  • 编辑：复制 / 剪切 / 粘贴 / 删除 / 重命名 / 全选 / 旋转。\r\n" +
                "  • 视图：放大 / 缩小 / 实际大小 / 适应窗口 / 重置视图 / 按内容调整画布。\r\n" +
                "  • 属性：顶点与边的颜色、大小/线宽、形状/线型、默认命名规则、名字显示位置。\r\n" +
                "  • 滚动条方向与 Windows 一致：向下拖垂直滚动条，看到图像下方的部分。\r\n" +
                "\r\n" +
                "说明：同一组内（所有顶点，或所有边）的大小与样式始终一致，只有名字和颜色可以逐个不同。";

            MessageBox.Show(this, text, "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowAbout()
        {
            MessageBox.Show(this,
                "GraphEditor\r\n版本 1.0\r\n\r\n用于编辑图论中的图（顶点与边）的 Windows 程序。\r\n" +
                "自定义文件格式：.graph（JSON 文本）\r\n支持导出：SVG 矢量图（仅图内容区域）",
                "关于 GraphEditor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>载入窗口/任务栏图标（优先使用 exe 自带图标）。</summary>
        private void ApplyApplicationIcon()
        {
            try
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                // 忽略：下面再尝试从文件载入
            }

            if (Icon != null) return;

            string[] relative =
            {
                Path.Combine("assets", "GraphEditor.ico"),
                Path.Combine("..", "assets", "GraphEditor.ico"),
                Path.Combine("..", "..", "assets", "GraphEditor.ico")
            };
            foreach (string candidate in relative)
            {
                try
                {
                    string full = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, candidate));
                    if (!File.Exists(full)) continue;
                    Icon = new Icon(full);
                    return;
                }
                catch
                {
                    // 继续尝试下一个候选路径
                }
            }
        }
    }
}
