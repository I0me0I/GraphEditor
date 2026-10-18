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

        /// <summary>活动文档变化（切换标签、关闭标签、分栏）时触发。</summary>
        public event EventHandler ActiveDocumentChanged;
        public event EventHandler DocumentsChanged;

        public DocumentArea()
        {
            BackColor = Color.FromArgb(0x9E, 0xA3, 0xAA);

            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Vertical;
            _split.Panel1Collapsed = false;
            _split.Panel2Collapsed = true;
            _split.Panel1.Padding = new Padding(0);
            _split.Panel2.Padding = new Padding(0);

            ConfigureTabs(_tabsFirst);
            ConfigureTabs(_tabsSecond);
            _split.Panel1.Controls.Add(_tabsFirst);
            _split.Panel2.Controls.Add(_tabsSecond);
            Controls.Add(_split);

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
                if (!_split.Panel2Collapsed && _tabsSecond.ContainsFocus) return _tabsSecond;
                if (_tabsFirst.ContainsFocus) return _tabsFirst;
                // 没有焦点时：哪个面板有选中项就用哪个，最后退回第一个
                if (_tabsFirst.SelectedTab != null) return _tabsFirst;
                if (!_split.Panel2Collapsed && _tabsSecond.SelectedTab != null) return _tabsSecond;
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
                if (!_split.Panel2Collapsed) CollectDocuments(_tabsSecond, docs);
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
            get { return !_split.Panel2Collapsed; }
        }

        /// <summary>把文档加为活动面板的一个标签页并选中。</summary>
        public void AddDocument(DocumentView doc)
        {
            AddTo(ActiveTabs, doc);
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
                    // 整个面板空了就收拢分栏
                    if (owner.TabPages.Count == 0 && IsSplit)
                    {
                        if (owner == _tabsSecond || _tabsFirst.TabPages.Count == 0)
                        {
                            _split.Panel2Collapsed = true;
                            _split.Orientation = Orientation.Vertical;
                        }
                    }
                }
                doc.Dispose();
            }
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
            if (IsSplit)
            {
                // 已经分栏：只是把文档挪到另一个面板
                TabPage page = doc.Tag as TabPage;
                if (page == null) return;
                TabControl owner = page.Parent as TabControl;
                TabControl target = owner == _tabsFirst ? _tabsSecond : _tabsFirst;
                if (owner == target) return;
                owner.TabPages.Remove(page);
                page.Controls.Remove(doc);
                target.TabPages.Add(page);
                page.Controls.Add(doc);
                target.SelectedTab = page;
                _split.Orientation = orientation;
                RaiseDocumentsChanged();
                RaiseActiveChanged();
                return;
            }

            // 尚未分栏：把该文档挪到第二个面板，然后展开
            TabPage first = doc.Tag as TabPage;
            if (first == null) return;
            _tabsFirst.TabPages.Remove(first);
            first.Controls.Remove(doc);
            _tabsSecond.TabPages.Add(first);
            first.Controls.Add(doc);
            _tabsSecond.SelectedTab = first;

            _split.Orientation = orientation;
            _split.Panel2Collapsed = false;
            _split.SplitterDistance = orientation == Orientation.Vertical
                ? _split.Width / 2
                : _split.Height / 2;
            RaiseDocumentsChanged();
            RaiseActiveChanged();
        }

        /// <summary>合并回单面板：第二个面板的文档移回第一个。</summary>
        public void MergeToSinglePane()
        {
            if (!IsSplit) return;
            List<TabPage> moving = new List<TabPage>();
            foreach (TabPage page in _tabsSecond.TabPages) moving.Add(page);
            foreach (TabPage page in moving)
            {
                DocumentView doc = page.Tag as DocumentView;
                _tabsSecond.TabPages.Remove(page);
                page.Controls.Remove(doc);
                _tabsFirst.TabPages.Add(page);
                page.Controls.Add(doc);
            }
            if (_tabsFirst.TabPages.Count > 0)
            {
                _tabsFirst.SelectedIndex = 0;
            }
            _split.Panel2Collapsed = true;
            RaiseDocumentsChanged();
            RaiseActiveChanged();
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
            if (tabs.GetTabRect(tabs.SelectedIndex).Contains(e.Location) == false
                && tabs.SelectedIndex >= 0)
            {
                _dragDocument = null;
                return;
            }

            TabPage page = tabs.SelectedTab;
            if (page == null) { _dragDocument = null; return; }
            _dragDocument = page.Tag as DocumentView;
            _dragStartScreen = Cursor.Position;
            _dragging = false;
        }

        private void OnTabsMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragDocument == null) return;
            Point now = Cursor.Position;
            if (!_dragging
                && (Math.Abs(now.X - _dragStartScreen.X) > DragThreshold
                    || Math.Abs(now.Y - _dragStartScreen.Y) > DragThreshold))
            {
                _dragging = true;
            }
        }

        private void OnTabsMouseUp(object sender, MouseEventArgs e)
        {
            DocumentView doc = _dragDocument;
            bool dragged = _dragging;
            _dragDocument = null;
            _dragging = false;
            if (doc == null || !dragged) return;

            // 释放位置相对本控件：靠近哪条边就按哪个方向分栏
            Point client = PointToClient(Cursor.Position);
            Orientation? orientation = DropSide(client);
            if (orientation == null) return;
            SplitWith(doc, orientation.Value);
        }

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
    }
}
