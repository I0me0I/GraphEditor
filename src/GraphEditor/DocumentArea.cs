using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GraphEditor
{
    /// <summary>
    /// 文档区域：用标签页（TabControl）在多个文档之间切换，标签页铺满工作区；
    /// 把标签拖到窗口的上下左右边缘，可以分裂成两块并排（或上下）的面板。
    /// 这是现代 Windows 应用（记事本 / Edge / VSCode）的做法，替代旧的层叠式 MDI。
    /// </summary>
    public sealed class DocumentArea : UserControl
    {
        private readonly SplitContainer _split = new SplitContainer();
        private readonly TabControl _tabsFirst = new TabControl();
        private readonly TabControl _tabsSecond = new TabControl();

        /// <summary>拖动标签时，距离边缘多少像素以内算「拖到边缘要分栏」。</summary>
        private const int DropEdgeZone = 48;

        /// <summary>按下标签后移动多少像素才算拖动（避免误触）。</summary>
        private const int DragThreshold = 8;

        private DocumentView _dragDocument;
        private Point _dragStartScreen;
        private bool _dragging;
        private Orientation? _dropSide;
        private DropHintForm _dropHint;
        private bool _isSplit;

        /// <summary>活动文档变化（切换标签、关闭标签、分栏）时触发。</summary>
        public event EventHandler ActiveDocumentChanged;
        public event EventHandler DocumentsChanged;

        public DocumentArea()
        {
            BackColor = Color.FromArgb(0x9E, 0xA3, 0xAA);

            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Vertical;
            _split.Panel1.Padding = new Padding(0);
            _split.Panel2.Padding = new Padding(0);

            ConfigureTabs(_tabsFirst);
            ConfigureTabs(_tabsSecond);

            // 单栏模式下不使用 SplitContainer：标签控件直接铺满本控件。
            // 折叠/展开 SplitContainer 偶尔会留下「没铺满」的空隙，直接把布局切换掉更可靠。
            EnterSingleMode();

            _tabsFirst.SelectedIndexChanged += delegate { RaiseActiveChanged(); };
            _tabsSecond.SelectedIndexChanged += delegate { RaiseActiveChanged(); };
        }

        private void ConfigureTabs(TabControl tabs)
        {
            tabs.Dock = DockStyle.Fill;
            tabs.Appearance = TabAppearance.Normal;
            tabs.Multiline = false;
            tabs.ShowToolTips = true;
            tabs.Padding = new Point(12, 4);

            // 标签页的拖动分栏：在标签条上按下 → 移动 → 在边缘附近松开
            tabs.MouseDown += OnTabsMouseDown;
            tabs.MouseMove += OnTabsMouseMove;
            tabs.MouseUp += OnTabsMouseUp;

            // 安全网：万一拖动途中失去鼠标捕获（Alt+Tab、被别的窗口抢走），
            // 别把半透明预览层留在屏幕上。
            tabs.MouseCaptureChanged += delegate
            {
                if (!tabs.Capture && _dropHint != null && _dropHint.Visible) HideDropHint();
            };
        }

        private void RaiseActiveChanged()
        {
            EventHandler handler = ActiveDocumentChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void RaiseDocumentsChanged()
        {
            EventHandler handler = DocumentsChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>当前活动的标签页控件（优先用最近获得焦点的那个）。</summary>
        private TabControl ActiveTabs
        {
            get
            {
                if (_isSplit && _tabsSecond.ContainsFocus) return _tabsSecond;
                if (_tabsFirst.ContainsFocus) return _tabsFirst;
                // 没有焦点时：哪个面板有选中项就用哪个，最后退回第一个
                if (_tabsFirst.SelectedTab != null) return _tabsFirst;
                if (_isSplit && _tabsSecond.SelectedTab != null) return _tabsSecond;
                return _tabsFirst;
            }
        }

        public DocumentView ActiveDocument
        {
            get
            {
                TabControl tabs = ActiveTabs;
                TabPage page = tabs.SelectedTab;
                return page == null ? null : page.Tag as DocumentView;
            }
        }

        /// <summary>所有已打开的文档（按面板与标签顺序）。</summary>
        public List<DocumentView> AllDocuments
        {
            get
            {
                List<DocumentView> docs = new List<DocumentView>();
                CollectDocuments(_tabsFirst, docs);
                if (_isSplit) CollectDocuments(_tabsSecond, docs);
                return docs;
            }
        }

        private static void CollectDocuments(TabControl tabs, List<DocumentView> into)
        {
            foreach (TabPage page in tabs.TabPages)
            {
                DocumentView doc = page.Tag as DocumentView;
                if (doc != null) into.Add(doc);
            }
        }

        public int DocumentCount
        {
            get { return AllDocuments.Count; }
        }

        /// <summary>是否处于分栏状态（两个面板）。</summary>
        public bool IsSplit
        {
            get { return _isSplit; }
        }

        /// <summary>把文档加为活动面板的一个标签页并选中。</summary>
        public void AddDocument(DocumentView doc)
        {
            AddTo(ActiveTabs, doc);
            RelayoutPanes();
            RaiseDocumentsChanged();
            RaiseActiveChanged();
        }

        private void AddTo(TabControl tabs, DocumentView doc)
        {
            TabPage page = new TabPage(doc.TabText);
            doc.Dock = DockStyle.Fill;
            page.Controls.Add(doc);
            page.Tag = doc;
            doc.Tag = page;

            doc.StateChanged += delegate
            {
                page.Text = doc.TabText;
                RaiseDocumentsChanged();
                RaiseActiveChanged();
            };

            tabs.TabPages.Add(page);
            tabs.SelectedTab = page;
        }

        /// <summary>关闭一个文档（会先询问保存）。返回是否真的关闭了。</summary>
        public bool CloseDocument(DocumentView doc)
        {
            if (doc == null) return false;
            if (!doc.ConfirmDiscardChanges()) return false;

            TabPage page = doc.Tag as TabPage;
            if (page != null)
            {
                page.Controls.Remove(doc);
                TabControl owner = page.Parent as TabControl;
                if (owner != null)
                {
                    owner.TabPages.Remove(page);
                    // 某个面板空了就收拢回单栏
                    if (owner.TabPages.Count == 0 && _isSplit)
                    {
                        MergeToSinglePane();
                    }
                }
                doc.Dispose();
            }
            RelayoutPanes();
            RaiseDocumentsChanged();
            RaiseActiveChanged();
            return true;
        }

        /// <summary>
        /// 把文档移到另一个面板，形成分栏。
        /// 目标面板已经有内容时，把文档加进去；否则用它初始化新面板。
        /// </summary>
        public void SplitWith(DocumentView doc, Orientation orientation)
        {
            if (doc == null) return;

            TabPage page = doc.Tag as TabPage;
            if (page == null) return;
            TabControl owner = page.Parent as TabControl;
            if (owner == null) return;

            if (_isSplit)
            {
                // 已经分栏：把文档挪到另一个面板
                TabControl target = owner == _tabsFirst ? _tabsSecond : _tabsFirst;
                if (owner == target) return;
                MovePage(page, doc, target);
                _split.Orientation = orientation;
                _split.SplitterDistance = SafeSplitterDistance(orientation);
                target.SelectedTab = page;
            }
            else
            {
                // 尚未分栏：先把文档挪到第二个标签控件，再切换到双栏布局
                MovePage(page, doc, _tabsSecond);
                EnterSplitMode(orientation);
                _tabsSecond.SelectedTab = page;
            }

            RelayoutPanes();
            RaiseDocumentsChanged();
            RaiseActiveChanged();
        }

        /// <summary>合并回单面板：第二个面板的文档移回第一个。</summary>
        public void MergeToSinglePane()
        {
            if (!_isSplit) return;

            List<TabPage> moving = new List<TabPage>();
            foreach (TabPage page in _tabsSecond.TabPages) moving.Add(page);
            foreach (TabPage page in moving)
            {
                DocumentView doc = page.Tag as DocumentView;
                MovePage(page, doc, _tabsFirst);
            }

            EnterSingleMode();
            if (_tabsFirst.TabPages.Count > 0) _tabsFirst.SelectedIndex = 0;

            RelayoutPanes();
            RaiseDocumentsChanged();
            RaiseActiveChanged();
        }

        private static void MovePage(TabPage page, DocumentView doc, TabControl target)
        {
            TabControl owner = page.Parent as TabControl;
            if (owner == target) return;
            if (owner != null) owner.TabPages.Remove(page);
            if (doc != null) page.Controls.Remove(doc);
            target.TabPages.Add(page);
            if (doc != null)
            {
                doc.Dock = DockStyle.Fill;
                page.Controls.Add(doc);
            }
        }

        /// <summary>单栏模式：标签控件直接铺满本控件，完全不经过 SplitContainer。</summary>
        private void EnterSingleMode()
        {
            _isSplit = false;

            if (_split.Parent != null)
            {
                _split.Panel1.Controls.Remove(_tabsFirst);
                Controls.Remove(_split);
            }
            if (_tabsFirst.Parent != this)
            {
                _tabsFirst.Dock = DockStyle.Fill;
                Controls.Add(_tabsFirst);
            }
            _tabsFirst.Visible = true;
            _tabsFirst.BringToFront();
        }

        /// <summary>双栏模式：两个标签控件各占 SplitContainer 的一半。</summary>
        private void EnterSplitMode(Orientation orientation)
        {
            if (_tabsFirst.Parent == this) Controls.Remove(_tabsFirst);

            if (_tabsFirst.Parent != _split.Panel1)
            {
                _tabsFirst.Dock = DockStyle.Fill;
                _split.Panel1.Controls.Add(_tabsFirst);
            }
            if (_tabsSecond.Parent != _split.Panel2)
            {
                _tabsSecond.Dock = DockStyle.Fill;
                _split.Panel2.Controls.Add(_tabsSecond);
            }

            _split.Orientation = orientation;
            _split.Panel1Collapsed = false;
            _split.Panel2Collapsed = false;
            _split.Dock = DockStyle.Fill;

            if (_split.Parent != this)
            {
                Controls.Add(_split);
                _split.BringToFront();
            }

            _isSplit = true;
            _tabsFirst.Visible = true;
            _tabsSecond.Visible = true;

            _split.SplitterDistance = SafeSplitterDistance(orientation);
        }

        private int SafeSplitterDistance(Orientation orientation)
        {
            int extent = orientation == Orientation.Vertical ? _split.Width : _split.Height;
            if (extent <= 0) extent = orientation == Orientation.Vertical ? Width : Height;
            int half = Math.Max(1, extent / 2);
            int min = Math.Max(1, extent / 5);
            int max = Math.Max(min, extent - min);
            return Math.Min(Math.Max(half, min), max);
        }

        /// <summary>切换到下一个 / 上一个文档。</summary>
        public void ActivateNext(bool forward)
        {
            List<DocumentView> docs = AllDocuments;
            if (docs.Count < 2) return;
            DocumentView current = ActiveDocument;
            int index = docs.IndexOf(current);
            if (index < 0) index = 0;
            int next = forward ? (index + 1) % docs.Count : (index - 1 + docs.Count) % docs.Count;
            Activate(docs[next]);
        }

        /// <summary>激活指定文档（切到它所在的标签页）。</summary>
        public void Activate(DocumentView doc)
        {
            TabPage page = doc.Tag as TabPage;
            if (page == null) return;
            TabControl owner = page.Parent as TabControl;
            if (owner == null) return;
            owner.SelectedTab = page;
            page.Focus();
            RaiseActiveChanged();
        }

        // ================= 拖动标签分栏 =================

        private void OnTabsMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            TabControl tabs = (TabControl)sender;

            // 只处理「按在标签条上」的情况（点标签正文不算）
            if (tabs.SelectedIndex < 0 || !tabs.GetTabRect(tabs.SelectedIndex).Contains(e.Location))
            {
                _dragDocument = null;
                return;
            }

            TabPage page = tabs.SelectedTab;
            if (page == null) { _dragDocument = null; return; }
            _dragDocument = page.Tag as DocumentView;
            _dragStartScreen = Cursor.Position;
            _dragging = false;
            _dropSide = null;
            // 捕获鼠标：拖到窗口边缘（甚至出了标签条）仍能收到 Move/Up
            tabs.Capture = true;
        }

        private void OnTabsMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragDocument == null) return;
            TabControl tabs = (TabControl)sender;

            Point now = Cursor.Position;
            if (!_dragging
                && (Math.Abs(now.X - _dragStartScreen.X) > DragThreshold
                    || Math.Abs(now.Y - _dragStartScreen.Y) > DragThreshold))
            {
                _dragging = true;
            }
            if (!_dragging) return;

            // 拖到边缘时：换光标 + 显示将要占用的范围
            Orientation? side = DropSide(PointToClient(now));
            _dropSide = side;
            tabs.Cursor = side == null
                ? Cursors.No
                : (side == Orientation.Vertical ? Cursors.SizeWE : Cursors.SizeNS);

            if (side == null) HideDropHint();
            else ShowDropHint(DropRectangle(side.Value));
        }

        private void OnTabsMouseUp(object sender, MouseEventArgs e)
        {
            TabControl tabs = (TabControl)sender;
            DocumentView doc = _dragDocument;
            bool dragged = _dragging;
            Orientation? side = _dropSide;

            _dragDocument = null;
            _dragging = false;
            _dropSide = null;
            tabs.Capture = false;
            tabs.Cursor = Cursors.Default;
            HideDropHint();

            if (doc == null || !dragged || side == null) return;
            SplitWith(doc, side.Value);
        }

        /// <summary>是否正在拖动标签（用于 Esc 取消）。</summary>
        public bool IsDraggingTab
        {
            get { return _dragDocument != null && _dragging; }
        }

        /// <summary>拖动过程中按 Esc 取消（不放手也能放弃本次分栏）。</summary>
        internal void CancelDrag()
        {
            _dragDocument = null;
            _dragging = false;
            _dropSide = null;
            _tabsFirst.Capture = false;
            _tabsSecond.Capture = false;
            _tabsFirst.Cursor = Cursors.Default;
            _tabsSecond.Cursor = Cursors.Default;
            HideDropHint();
        }

        /// <summary>拖动时鼠标所在的贴靠边（左右 → 并排；上下 → 分栏）；不在边缘返回 null。</summary>
        private Orientation? DropSide(Point client)
        {
            int left = client.X;
            int right = Width - client.X;
            int top = client.Y;
            int bottom = Height - client.Y;

            int min = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
            if (min > DropEdgeZone) return null;

            if (min == left || min == right) return Orientation.Vertical;      // 左右 → 并排
            return Orientation.Horizontal;                                      // 上下 → 分栏
        }

        /// <summary>给定分栏方向时，预览应该覆盖的客户区矩形（占一半）。</summary>
        private Rectangle DropRectangle(Orientation orientation)
        {
            if (orientation == Orientation.Vertical)
            {
                return new Rectangle(0, 0, Math.Max(1, Width / 2), Math.Max(1, Height));
            }
            return new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height / 2));
        }

        private void ShowDropHint(Rectangle clientRect)
        {
            if (_dropHint == null || _dropHint.IsDisposed)
            {
                _dropHint = new DropHintForm();
            }
            Rectangle screen = RectangleToScreen(clientRect);
            if (_dropHint.Bounds != screen) _dropHint.Bounds = screen;
            if (!_dropHint.Visible) _dropHint.Show(this);
        }

        private void HideDropHint()
        {
            if (_dropHint != null && !_dropHint.IsDisposed && _dropHint.Visible)
            {
                _dropHint.Hide();
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            // 分栏宽度跟着窗口收缩时，SplitterDistance 可能有越界风险，钳制一下
            ClampSplitterDistance();
        }

        private void ClampSplitterDistance()
        {
            if (_split.Panel2Collapsed) return;
            try
            {
                int extent = _split.Orientation == Orientation.Vertical ? _split.Width : _split.Height;
                int min = Math.Max(1, extent / 5);
                int max = Math.Max(min, extent - min);
                int wanted = _split.SplitterDistance;
                if (wanted < min) _split.SplitterDistance = min;
                else if (wanted > max) _split.SplitterDistance = max;
            }
            catch (ArgumentOutOfRangeException)
            {
                // 极端尺寸下 SplitContainer 会拒绝，忽略即可
            }
            catch (InvalidOperationException)
            {
            }
        }

        /// <summary>
        /// 结构变化（分栏/合并/关闭标签）后强制重排一次。
        /// 只依赖 WinForms 的自动布局偶尔会留下「标签铺不满工作区」的空隙。
        /// </summary>
        private void RelayoutPanes()
        {
            _split.PerformLayout();
            RelayoutTabs(_tabsFirst);
            if (!_split.Panel2Collapsed) RelayoutTabs(_tabsSecond);

            foreach (DocumentView doc in AllDocuments)
            {
                if (doc.Dock != DockStyle.Fill) doc.Dock = DockStyle.Fill;
                doc.PerformLayout();
                if (doc.CanvasArea != null) doc.CanvasArea.PerformLayout();
            }
            PerformLayout();
            Invalidate();
        }

        private static void RelayoutTabs(TabControl tabs)
        {
            tabs.PerformLayout();
            TabPage page = tabs.SelectedTab;
            if (page != null) page.PerformLayout();
            tabs.Invalidate();
        }

        /// <summary>
        /// 拖动标签时显示的范围预览：一个无边框、半透明、不抢焦点的浮层，
        /// 盖在「松开后文档将要占据的区域」上。
        /// </summary>
        private sealed class DropHintForm : Form
        {
            public DropHintForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                BackColor = Color.FromArgb(0x00, 0x78, 0xD4);
                Opacity = 0.25;
                TopMost = true;
                // 注意：不能设 Enabled = false，那样 Show() 会抛异常。
                // 拖动期间标签控件持有鼠标捕获，事件不会落到这个浮层上。
            }

            protected override bool ShowWithoutActivation
            {
                get { return true; }
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    // 不抢焦点、不显示在 Alt+Tab
                    cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE
                    cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW
                    return cp;
                }
            }
        }
    }
}
