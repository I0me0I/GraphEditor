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
    /// <summary>标签拖放的贴靠位置：决定预览画在哪一半、文档落到哪一侧。</summary>
    public enum DropPlacement
    {
        None,
        Left,
        Right,
        Top,
        Bottom
    }

    public sealed class DocumentArea : UserControl
    {
        private readonly SplitContainer _split = new SplitContainer();
        private readonly TabControl _tabsFirst = new TabControl();
        private readonly TabControl _tabsSecond = new TabControl();
        private readonly Panel _hostFirst = new Panel();
        private readonly Panel _hostSecond = new Panel();

        /// <summary>拖动标签时，距离边缘多少像素以内算「拖到边缘要分栏」。</summary>
        private const int DropEdgeZone = 48;

        /// <summary>按下标签后移动多少像素才算拖动（避免误触）。</summary>
        private const int DragThreshold = 8;

        private DocumentView _dragDocument;
        private Point _dragStartScreen;
        private bool _dragging;
        private DropPlacement _dropPlacement;
        private Rectangle _dropHintScreen = Rectangle.Empty;
        private bool _isSplit;

        /// <summary>活动文档变化（切换标签、关闭标签、分栏）时触发。</summary>
        public event EventHandler ActiveDocumentChanged;
        public event EventHandler DocumentsChanged;

        public DocumentArea()
        {
            // 使用与 Windows STATIC/对话框一致的系统灰。
            // 不要用 AppWorkspace —— 那是 MDI 时代的深灰（#ABABAB），
            // 会被 SplitContainer 继承成「更深的分割条」。
            BackColor = SystemColors.Control;

            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Vertical;
            _split.Panel1.Padding = new Padding(0);
            _split.Panel2.Padding = new Padding(0);

            ConfigureTabs(_tabsFirst);
            ConfigureTabs(_tabsSecond);

            ConfigureHost(_hostFirst, _tabsFirst);
            ConfigureHost(_hostSecond, _tabsSecond);

            // 单栏模式下不使用 SplitContainer：标签控件直接铺满本控件。
            // 折叠/展开 SplitContainer 偶尔会留下「没铺满」的空隙，直接把布局切换掉更可靠。
            EnterSingleMode();

            _tabsFirst.SelectedIndexChanged += delegate { RaiseActiveChanged(); };
            _tabsSecond.SelectedIndexChanged += delegate { RaiseActiveChanged(); };
        }

        private void ConfigureTabs(TabControl tabs)
        {
            // 全部使用 WinForms 默认外观，不做任何自绘
            tabs.Dock = DockStyle.Fill;

            // 标签页的拖动分栏：在标签条上按下 → 移动 → 在边缘附近松开
            tabs.MouseDown += OnTabsMouseDown;
            tabs.MouseMove += OnTabsMouseMove;
            tabs.MouseUp += OnTabsMouseUp;

            // 安全网：万一拖动途中失去鼠标捕获（Alt+Tab、被别的窗口抢走），
            // 别把拖动框留在屏幕上。
            tabs.MouseCaptureChanged += delegate
            {
                if (!tabs.Capture) HideDropHint();
            };
        }

        /// <summary>
        /// 给每个文档面板套一层「1px 黑色边框」：黑底 + 1px 内边距，
        /// 让标签控件四周露出一圈纯黑细边。全部由框架绘制，没有自绘代码。
        /// （Panel.BorderStyle.FixedSingle 画的是 SystemColors.WindowFrame #646464，不是黑色。）
        /// </summary>
        private static void ConfigureHost(Panel host, TabControl tabs)
        {
            host.BackColor = Color.Black;
            host.Padding = new Padding(1);
            host.Dock = DockStyle.Fill;
            tabs.Dock = DockStyle.Fill;
            host.Controls.Add(tabs);
        }

        private static void FocusActiveCanvas(TabControl tabs)
        {
            TabPage page = tabs.SelectedTab;
            if (page == null) return;
            DocumentView doc = page.Tag as DocumentView;
            if (doc != null && doc.CanvasArea.CanFocus) doc.CanvasArea.Focus();
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
        /// 把文档放到指定的贴靠位置（左/右/上/下），形成分栏。
        /// 文档会落在光标所在一侧；如果移动导致某个面板空了，就收拢回单栏，
        /// 不会留下空白的一半。
        /// </summary>
        public void ApplyDrop(DocumentView doc, DropPlacement place)
        {
            if (doc == null || place == DropPlacement.None) return;

            TabPage page = doc.Tag as TabPage;
            if (page == null) return;
            TabControl source = page.Parent as TabControl;
            if (source == null) return;

            TabControl other = source == _tabsFirst ? _tabsSecond : _tabsFirst;

            // 文档落到「另一个」面板，然后按落点决定哪边在左/上
            MovePage(page, doc, other);
            other.SelectedTab = page;

            Orientation orientation =
                (place == DropPlacement.Left || place == DropPlacement.Right)
                    ? Orientation.Vertical
                    : Orientation.Horizontal;
            bool placedFirst = (place == DropPlacement.Left || place == DropPlacement.Top);

            if (!_isSplit)
            {
                // 只有一份文档时无法分栏：放回原处
                if (source.TabPages.Count == 0)
                {
                    MovePage(page, doc, source);
                    RelayoutPanes();
                    RaiseDocumentsChanged();
                    RaiseActiveChanged();
                    return;
                }
                EnterSplitMode(orientation);
            }
            else
            {
                // 源面板空了 → 收拢回单栏（拖回来不会留下空白）
                if (source.TabPages.Count == 0)
                {
                    MergeToSinglePane();
                    return;
                }
                _split.Orientation = orientation;
            }

            // 让「文档所在的那一侧」出现在左/上（或右/下）
            SetPaneOrder(placedFirst ? other : source, placedFirst ? source : other);
            _split.SplitterDistance = SafeSplitterDistance(orientation);

            RelayoutPanes();
            RaiseDocumentsChanged();
            RaiseActiveChanged();
        }

        /// <summary>按给定顺序把两个标签控件放进 SplitContainer 的左右（上下）面板。</summary>
        private void SetPaneOrder(TabControl first, TabControl second)
        {
            if (first == second) return;

            Panel firstHost = HostFor(first);
            Panel secondHost = HostFor(second);

            bool already = _split.Panel1.Controls.Count == 1
                && _split.Panel1.Controls[0] == firstHost
                && _split.Panel2.Controls.Count == 1
                && _split.Panel2.Controls[0] == secondHost;
            if (already) return;

            _split.Panel1.Controls.Remove(firstHost);
            _split.Panel1.Controls.Remove(secondHost);
            _split.Panel2.Controls.Remove(firstHost);
            _split.Panel2.Controls.Remove(secondHost);

            firstHost.Dock = DockStyle.Fill;
            secondHost.Dock = DockStyle.Fill;
            _split.Panel1.Controls.Add(firstHost);
            _split.Panel2.Controls.Add(secondHost);

            firstHost.Visible = true;
            secondHost.Visible = true;
        }


        /// <summary>
        /// 把文档移到另一个面板，形成分栏（保留的旧接口：只指定方向，落到右侧/下方）。
        /// </summary>
        public void SplitWith(DocumentView doc, Orientation orientation)
        {
            ApplyDrop(doc, orientation == Orientation.Vertical ? DropPlacement.Right : DropPlacement.Bottom);
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
                _split.Panel1.Controls.Remove(_hostFirst);
                _split.Panel2.Controls.Remove(_hostSecond);
                Controls.Remove(_split);
            }
            if (_hostFirst.Parent != this)
            {
                _hostFirst.Dock = DockStyle.Fill;
                Controls.Add(_hostFirst);
            }
            _hostFirst.Visible = true;
            _hostFirst.BringToFront();
        }

        /// <summary>双栏模式：两个标签控件各占 SplitContainer 的一半。</summary>
        private void EnterSplitMode(Orientation orientation)
        {
            if (_hostFirst.Parent == this) Controls.Remove(_hostFirst);

            if (_hostFirst.Parent != _split.Panel1)
            {
                _hostFirst.Dock = DockStyle.Fill;
                _split.Panel1.Controls.Add(_hostFirst);
            }
            if (_hostSecond.Parent != _split.Panel2)
            {
                _hostSecond.Dock = DockStyle.Fill;
                _split.Panel2.Controls.Add(_hostSecond);
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
            _hostFirst.Visible = true;
            _hostSecond.Visible = true;

            _split.SplitterDistance = SafeSplitterDistance(orientation);
        }

        /// <summary>标签控件对应的承载面板（黑底 + 1px 内边距 = 1px 黑色边框）。</summary>
        private Panel HostFor(TabControl tabs)
        {
            return tabs == _tabsSecond ? _hostSecond : _hostFirst;
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
            _dropPlacement = DropPlacement.None;
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

            // 拖到边缘时：换光标（箭头 + 文档图标）+ 高亮将要落到的位置
            DropPlacement place = HitDropPlacement(PointToClient(now));
            _dropPlacement = place;

            // 只有一份文档时分栏没有意义，直接显示禁止
            bool possible = place != DropPlacement.None && CanSplitCurrent();
            if (!possible)
            {
                tabs.Cursor = Cursors.No;
                HideDropHint();
                return;
            }

            // 光标与拖动框都用系统默认样式（经典 Windows 拖动反馈）
            // 具体落到哪一侧由虚线拖动框表示。
            tabs.Cursor = Cursors.Default;
            ShowDropHint(DropRectangle(place));
        }

        private void OnTabsMouseUp(object sender, MouseEventArgs e)
        {
            TabControl tabs = (TabControl)sender;
            DocumentView doc = _dragDocument;
            bool dragged = _dragging;
            DropPlacement place = _dropPlacement;

            _dragDocument = null;
            _dragging = false;
            _dropPlacement = DropPlacement.None;
            tabs.Capture = false;
            tabs.Cursor = Cursors.Default;
            HideDropHint();

            if (doc == null || !dragged || place == DropPlacement.None) return;
            if (!CanSplitCurrent()) return;
            ApplyDrop(doc, place);
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
            _dropPlacement = DropPlacement.None;
            _tabsFirst.Capture = false;
            _tabsSecond.Capture = false;
            _tabsFirst.Cursor = Cursors.Default;
            _tabsSecond.Cursor = Cursors.Default;
            HideDropHint();
        }

        /// <summary>当前是否有足够多的文档可以分栏（只有一份时没意义）。</summary>
        private bool CanSplitCurrent()
        {
            return DocumentCount > 1;
        }

        /// <summary>
        /// 拖动时鼠标所在的贴靠区（左/右/上/下）；不在边缘返回 None。
        /// 必须区分左右与上下，否则预览永远画在同一侧。
        /// </summary>
        private DropPlacement HitDropPlacement(Point client)
        {
            int left = client.X;
            int right = Width - client.X;
            int top = client.Y;
            int bottom = Height - client.Y;

            int min = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
            if (min > DropEdgeZone) return DropPlacement.None;

            if (min == left) return DropPlacement.Left;
            if (min == right) return DropPlacement.Right;
            if (min == top) return DropPlacement.Top;
            return DropPlacement.Bottom;
        }

        /// <summary>预览应覆盖的客户区矩形：就是文档将要落到的那个半区。</summary>
        private Rectangle DropRectangle(DropPlacement place)
        {
            int halfW = Math.Max(1, Width / 2);
            int halfH = Math.Max(1, Height / 2);
            switch (place)
            {
                case DropPlacement.Left: return new Rectangle(0, 0, halfW, Math.Max(1, Height));
                case DropPlacement.Right: return new Rectangle(halfW, 0, Math.Max(1, Width - halfW), Math.Max(1, Height));
                case DropPlacement.Top: return new Rectangle(0, 0, Math.Max(1, Width), halfH);
                case DropPlacement.Bottom: return new Rectangle(0, halfH, Math.Max(1, Width), Math.Max(1, Height - halfH));
                default: return new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height));
            }
        }

        /// <summary>
        /// 拖动预览：用系统的「可逆虚线框」（ControlPaint.DrawReversibleFrame），
        /// 和资源管理器/经典 Windows 程序拖动时的虚线框完全一样，不做自绘填充。
        /// </summary>
        private void ShowDropHint(Rectangle clientRect)
        {
            Rectangle screen = RectangleToScreen(clientRect);
            if (screen == _dropHintScreen) return;

            HideDropHint();
            _dropHintScreen = screen;
            ControlPaint.DrawReversibleFrame(screen, SystemColors.ControlText, FrameStyle.Dashed);
        }

        private void HideDropHint()
        {
            if (_dropHintScreen == Rectangle.Empty) return;
            ControlPaint.DrawReversibleFrame(_dropHintScreen, SystemColors.ControlText, FrameStyle.Dashed);
            _dropHintScreen = Rectangle.Empty;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            // 窗口一显示就把焦点交给画布，避免标签页一直带着虚线焦点框
            if (Visible) FocusActiveCanvas(ActiveTabs);
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

    }
}
