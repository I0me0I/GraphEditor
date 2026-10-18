using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GraphEditor
{
    /// <summary>标签拖放的贴靠位置：决定预览画在哪一半、文档落到哪一侧。</summary>
    public enum DropPlacement
    {
        None,
        Left,
        Right,
        Top,
        Bottom
    }

    /// <summary>
    /// 文档区域：每个文档是一个标签页；面板可以反复对半拆分，
    /// 最多同时平铺 <see cref="MaxPanes"/> 个面板（每个面板是一个独立的标签控件）。
    ///
    /// 结构是一棵小树：叶子是「面板」（Panel + TabControl），
    /// 分支是 SplitContainer。把标签拖到某个面板的边缘，就拆开那个面板；
    /// 某个面板的标签被移空/关空时，它会被自动收掉，兄弟节点顶替它的位置。
    /// </summary>
    public sealed class DocumentArea : UserControl
    {
        /// <summary>最多同时显示的面板数（4 = 2 层对半拆分）。</summary>
        private const int MaxPanes = 4;

        /// <summary>拖动标签时，距离面板边缘多少像素以内算「拖到边缘要拆分」。</summary>
        private const int DropEdgeZone = 48;

        /// <summary>按下标签后移动多少像素才算拖动（避免误触）。</summary>
        private const int DragThreshold = 8;

        /// <summary>一个面板：承载面板 + 其中的标签控件。</summary>
        private sealed class Pane
        {
            public Panel Host;
            public TabControl Tabs;
        }

        private Control _root;               // 面板树根：Pane.Host 或 SplitContainer
        private Pane _active;
        private readonly List<Pane> _panes = new List<Pane>();

        private DocumentView _dragDocument;
        private Point _dragStartScreen;
        private bool _dragging;
        private DropPlacement _dropPlacement;
        private Rectangle _dropHintScreen = Rectangle.Empty;

        /// <summary>活动文档变化（切换标签、关闭标签、拆分/合并）时触发。</summary>
        public event EventHandler ActiveDocumentChanged;
        public event EventHandler DocumentsChanged;

        public DocumentArea()
        {
            // 与 Windows STATIC/对话框一致的系统灰。
            // 不要用 AppWorkspace —— 那是 MDI 时代的深灰（#ABABAB），
            // 会被 SplitContainer 继承成「更深的分割条」。
            BackColor = SystemColors.Control;

            CreatePane(true);
        }

        // ================= 面板树 =================

        private Pane CreatePane(bool makeRoot)
        {
            Pane pane = new Pane();
            pane.Host = new Panel();
            pane.Tabs = new TabControl();

            pane.Host.BackColor = SystemColors.Control;
            pane.Host.Padding = new Padding(0);
            pane.Host.Dock = DockStyle.Fill;

            pane.Tabs.Dock = DockStyle.Fill;
            ConfigureTabs(pane.Tabs);
            pane.Host.Controls.Add(pane.Tabs);

            _panes.Add(pane);

            if (makeRoot)
            {
                _root = pane.Host;
                Controls.Add(pane.Host);
                _active = pane;
            }
            return pane;
        }

        private void ConfigureTabs(TabControl tabs)
        {
            // 全部使用 WinForms 默认外观，不做任何自绘
            tabs.Dock = DockStyle.Fill;

            // 标签页的拖动拆分：在标签条上按下 → 移动 → 在边缘附近松开
            tabs.MouseDown += OnTabsMouseDown;
            tabs.MouseMove += OnTabsMouseMove;
            tabs.MouseUp += OnTabsMouseUp;
            tabs.SelectedIndexChanged += delegate
            {
                RaiseActiveChanged();
                FocusActiveCanvas(tabs);
            };
            tabs.Enter += delegate { ActivatePaneOf(tabs); };

            // 安全网：万一拖动途中失去鼠标捕获（Alt+Tab、被别的窗口抢走），
            // 别把拖动框留在屏幕上。
            tabs.MouseCaptureChanged += delegate
            {
                if (!tabs.Capture) HideDropHint();
            };
        }

        private void ActivatePaneOf(TabControl tabs)
        {
            Pane pane = PaneOf(tabs);
            if (pane != null && pane != _active)
            {
                _active = pane;
            }
        }

        private Pane PaneOf(TabControl tabs)
        {
            for (int i = 0; i < _panes.Count; i++)
            {
                if (_panes[i].Tabs == tabs) return _panes[i];
            }
            return null;
        }

        private static void FocusActiveCanvas(TabControl tabs)
        {
            TabPage page = tabs.SelectedTab;
            if (page == null) return;
            DocumentView doc = page.Tag as DocumentView;
            if (doc != null && doc.CanvasArea.CanFocus) doc.CanvasArea.Focus();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            // 窗口一显示就把焦点交给画布，避免标签页一直带着虚线焦点框
            if (Visible && _active != null) FocusActiveCanvas(_active.Tabs);
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

        /// <summary>按视觉顺序（左→右、上→下）遍历所有面板。</summary>
        private void CollectPanes(Control node, List<Pane> into)
        {
            if (node == null) return;
            SplitContainer split = node as SplitContainer;
            if (split != null)
            {
                CollectPanes(ChildOf(split.Panel1), into);
                CollectPanes(ChildOf(split.Panel2), into);
                return;
            }
            Pane pane = PaneByHost(node);
            if (pane != null && !into.Contains(pane)) into.Add(pane);
        }

        private static Control ChildOf(SplitterPanel panel)
        {
            if (panel.Controls.Count == 0) return null;
            return panel.Controls[0];
        }

        private Pane PaneByHost(Control host)
        {
            for (int i = 0; i < _panes.Count; i++)
            {
                if (_panes[i].Host == host) return _panes[i];
            }
            return null;
        }

        private List<Pane> OrderedPanes()
        {
            List<Pane> list = new List<Pane>();
            CollectPanes(_root, list);
            // 兜底：树里没覆盖到的面板（理论上不会发生）也加进来
            foreach (Pane pane in _panes)
            {
                if (!list.Contains(pane)) list.Add(pane);
            }
            return list;
        }

        private Pane ActivePane
        {
            get
            {
                if (_active != null && _panes.Contains(_active)) return _active;
                List<Pane> list = OrderedPanes();
                return list.Count > 0 ? list[0] : null;
            }
        }

        public DocumentView ActiveDocument
        {
            get
            {
                Pane pane = ActivePane;
                if (pane == null) return null;
                TabPage page = pane.Tabs.SelectedTab;
                return page == null ? null : page.Tag as DocumentView;
            }
        }

        /// <summary>所有已打开的文档（按面板与标签顺序）。</summary>
        public List<DocumentView> AllDocuments
        {
            get
            {
                List<DocumentView> docs = new List<DocumentView>();
                foreach (Pane pane in OrderedPanes())
                {
                    foreach (TabPage page in pane.Tabs.TabPages)
                    {
                        DocumentView doc = page.Tag as DocumentView;
                        if (doc != null) docs.Add(doc);
                    }
                }
                return docs;
            }
        }

        public int DocumentCount
        {
            get { return AllDocuments.Count; }
        }

        /// <summary>面板数量（1 表示没有拆分）。</summary>
        public int PaneCount
        {
            get { return OrderedPanes().Count; }
        }

        /// <summary>是否处于拆分状态（多于一个面板）。</summary>
        public bool IsSplit
        {
            get { return PaneCount > 1; }
        }

        // ================= 文档增删 =================

        /// <summary>把文档加为活动面板的一个标签页并选中。</summary>
        public void AddDocument(DocumentView doc)
        {
            Pane pane = ActivePane;
            if (pane == null) pane = CreatePane(false);
            AddTo(pane.Tabs, doc);
            _active = pane;
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
                    if (owner.TabPages.Count == 0)
                    {
                        Pane empty = PaneOf(owner);
                        if (empty != null) RemovePane(empty);
                    }
                }
                doc.Dispose();
            }
            RelayoutPanes();
            RaiseDocumentsChanged();
            RaiseActiveChanged();
            return true;
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

        // ================= 拆分 / 收拢 =================

        /// <summary>
        /// 把文档放到指定贴靠位置：文档落在光标所在一侧，
        /// 那个面板被拆成两半（左/右或上/下）。
        /// </summary>
        /// <summary>
        /// 把文档放到指定贴靠位置：拆开**目标面板**（默认是活动面板），
        /// 文档落在光标所在一侧。
        ///
        /// 拆分规则与用户的模型一致：
        ///   根（整个工作区）可以拆成左右或上下 2 份；
        ///   每份可以整个使用，也可以再拆成 2 份；
        ///   已经拆过两次的部分不再拆分 —— 也就是最多 2 层、4 个面板。
        /// </summary>
        public void ApplyDrop(DocumentView doc, DropPlacement place)
        {
            ApplyDropOn(doc, ActivePane, place);
        }

        /// <summary>把文档放到 target 面板的指定一侧。</summary>
        private void ApplyDropOn(DocumentView doc, Pane target, DropPlacement place)
        {
            if (doc == null || target == null || place == DropPlacement.None) return;

            TabPage page = doc.Tag as TabPage;
            if (page == null) return;
            TabControl source = page.Parent as TabControl;
            if (source == null) return;
            Pane sourcePane = PaneOf(source);

            // 目标面板里只剩被拖的这一份文档时，拆开没有意义（等于原地不动）
            if (source == target.Tabs && target.Tabs.TabPages.Count <= 1) return;
            if (!CanSplitPane(target)) return;

            Orientation orientation = (place == DropPlacement.Left || place == DropPlacement.Right)
                ? Orientation.Vertical
                : Orientation.Horizontal;
            bool newFirst = (place == DropPlacement.Left || place == DropPlacement.Top);

            // 先记下目标面板在树里的位置，再把它摘下来
            Control oldParent = target.Host.Parent;
            int oldIndex = oldParent != null ? oldParent.Controls.GetChildIndex(target.Host) : -1;
            bool wasRoot = ReferenceEquals(target.Host, _root);

            Pane created = CreatePane(false);
            MovePage(page, doc, created.Tabs);
            created.Tabs.SelectedTab = page;

            if (oldParent != null) oldParent.Controls.Remove(target.Host);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = orientation;
            split.Panel1.Padding = new Padding(0);
            split.Panel2.Padding = new Padding(0);

            Control first = newFirst ? created.Host : target.Host;
            Control second = newFirst ? target.Host : created.Host;
            first.Dock = DockStyle.Fill;
            second.Dock = DockStyle.Fill;
            split.Panel1.Controls.Add(first);
            split.Panel2.Controls.Add(second);

            AttachNode(split, oldParent, oldIndex, wasRoot);
            _active = created;

            // 源面板被搬空了（从别处拖过来的最后一份）→ 收掉它
            if (sourcePane != null && sourcePane != target && source.TabPages.Count == 0)
            {
                RemovePane(sourcePane);
            }

            RelayoutPanes();
            ApplySplitterDistance(split);
            RaiseDocumentsChanged();
            RaiseActiveChanged();
        }

        /// <summary>面板在树里的深度：根面板 0，根拆分出的两半 1，再下一层 2。</summary>
        private int DepthOf(Pane pane)
        {
            int depth = 0;
            Control node = pane.Host;
            while (node != null)
            {
                SplitterPanel panel = node.Parent as SplitterPanel;
                if (panel == null) break;
                SplitContainer split = panel.Parent as SplitContainer;
                if (split == null) break;
                depth++;
                node = split;
            }
            return depth;
        }

        /// <summary>
        /// 这个面板还能不能再拆：深度小于 2（根 0 → 两半 1 → 再拆 2）
        /// 且总数没超过上限。

        /// <summary>该面板是否可拆（还要考虑被拖的文档在哪儿）。</summary>
        private bool CanSplitTargetOf(Pane target)
        {
            if (target == null) return false;
            if (PaneCount >= MaxPanes) return false;
            if (DepthOf(target) >= 2) return false;
            // 目标面板只有一份文档，且它就是要被拖走的那份 → 拆不了
            if (_dragDocument != null)
            {
                TabPage dragPage = _dragDocument.Tag as TabPage;
                if (dragPage != null && dragPage.Parent == target.Tabs && target.Tabs.TabPages.Count <= 1) return false;
            }
            return true;
        }

        /// <summary>活动面板现在是否还能再拆（供菜单判断可用性）。</summary>
        public bool CanSplitActivePane
        {
            get
            {
                Pane pane = ActivePane;
                if (pane == null) return false;
                if (PaneCount >= MaxPanes) return false;
                return DepthOf(pane) < 2;
            }
        }

        /// </summary>
        private bool CanSplitPane(Pane pane)
        {
            if (pane == null) return false;
            if (PaneCount >= MaxPanes) return false;
            return DepthOf(pane) < 2;
        }


        /// <summary>文档所在面板里的文档数。</summary>
        private int documentCountOfPane(TabControl tabs)
        {
            return tabs.TabPages.Count;
        }

        /// <summary>
        /// 把文档移动到另一个文档所在的面板（拖到面板中间时的行为）。
        /// 源面板因此变空时会被自动收掉。
        /// </summary>
        public bool MoveDocumentToPane(DocumentView doc, DocumentView targetDocument)
        {
            if (doc == null || targetDocument == null) return false;
            TabPage targetPage = targetDocument.Tag as TabPage;
            if (targetPage == null) return false;
            TabControl targetTabs = targetPage.Parent as TabControl;
            Pane target = PaneOf(targetTabs);
            if (target == null) return false;
            return MoveIntoPane(target, doc);
        }

        private bool MoveIntoPane(Pane target, DocumentView doc)
        {
            TabPage page = doc.Tag as TabPage;
            if (page == null) return false;
            TabControl source = page.Parent as TabControl;
            if (source == null || source == target.Tabs) return false;

            Pane sourcePane = PaneOf(source);
            MovePage(page, doc, target.Tabs);
            target.Tabs.SelectedTab = page;
            _active = target;

            if (sourcePane != null && source.TabPages.Count == 0) RemovePane(sourcePane);

            RelayoutPanes();
            RaiseDocumentsChanged();
            RaiseActiveChanged();
            return true;
        }

        /// <summary>
        /// 把节点挂到指定容器的指定位置；parent 为 null 表示它是根。
        /// 调用前必须先把它从原父容器上摘下来，避免循环引用。
        /// </summary>
        private void AttachNode(Control node, Control parent, int index, bool wasRoot)
        {
            node.Dock = DockStyle.Fill;

            if (wasRoot || parent == null)
            {
                Controls.Add(node);
                _root = node;
                return;
            }

            parent.Controls.Add(node);
            if (index >= 0) parent.Controls.SetChildIndex(node, index);
            if (ReferenceEquals(_root, null)) _root = node;
        }

        /// <summary>移除一个空面板：它所处的 SplitContainer 由兄弟节点顶替。</summary>
        private void RemovePane(Pane pane)
        {
            if (pane == null) return;
            if (_panes.Count <= 1) return;      // 最后一个面板保持不动

            SplitContainer parent = pane.Host.Parent as SplitterPanel == null
                ? null
                : ((SplitterPanel)pane.Host.Parent).Parent as SplitContainer;
            if (parent == null)
            {
                RemovePaneBookkeeping(pane);
                return;
            }

            SplitterPanel ownPanel = (SplitterPanel)pane.Host.Parent;
            SplitterPanel otherPanel = ownPanel == parent.Panel1 ? parent.Panel2 : parent.Panel1;
            Control sibling = ChildOf(otherPanel);
            if (sibling == null)
            {
                RemovePaneBookkeeping(pane);
                return;
            }

            // 先记下 SplitContainer 的位置，再把兄弟摘出来
            Control splitParent = parent.Parent;
            int splitIndex = splitParent != null ? splitParent.Controls.GetChildIndex(parent) : -1;
            bool splitWasRoot = ReferenceEquals(parent, _root);

            ownPanel.Controls.Remove(pane.Host);
            otherPanel.Controls.Remove(sibling);
            if (splitParent != null) splitParent.Controls.Remove(parent);

            AttachNode(sibling, splitParent, splitIndex, splitWasRoot);

            _panes.Remove(pane);
            pane.Host.Dispose();
            parent.Dispose();
        }

        private void RemovePaneBookkeeping(Pane pane)
        {
            if (pane == null) return;
            _panes.Remove(pane);
            if (_active == pane)
            {
                List<Pane> rest = OrderedPanes();
                _active = rest.Count > 0 ? rest[0] : null;
            }
        }

        /// <summary>
        /// 把文档移到另一个面板，形成拆分（保留的旧接口：拆开活动面板，落到右侧/下方）。
        /// </summary>
        public void SplitWith(DocumentView doc, Orientation orientation)
        {
            ApplyDrop(doc, orientation == Orientation.Vertical ? DropPlacement.Right : DropPlacement.Bottom);
        }

        /// <summary>合并回单面板：所有面板的文档都并到第一个面板里。</summary>
        public void MergeToSinglePane()
        {
            List<Pane> panes = OrderedPanes();
            if (panes.Count <= 1) return;

            Pane first = panes[0];
            for (int i = 1; i < panes.Count; i++)
            {
                List<TabPage> moving = new List<TabPage>();
                foreach (TabPage page in panes[i].Tabs.TabPages) moving.Add(page);
                foreach (TabPage page in moving)
                {
                    DocumentView doc = page.Tag as DocumentView;
                    MovePage(page, doc, first.Tabs);
                }
            }

            // 拆掉整棵树，只留第一个面板
            Control oldRoot = _root;
            _root = first.Host;
            first.Host.Dock = DockStyle.Fill;
            Controls.Add(first.Host);       // 会自动从旧父容器上摘下来
            if (oldRoot != null && !ReferenceEquals(oldRoot, first.Host)) oldRoot.Dispose();

            for (int i = panes.Count - 1; i >= 1; i--)
            {
                _panes.Remove(panes[i]);
            }

            _active = first;
            if (first.Tabs.TabPages.Count > 0) first.Tabs.SelectedIndex = 0;

            RelayoutPanes();
            RaiseDocumentsChanged();
            RaiseActiveChanged();
        }

        private void ApplySplitterDistance(SplitContainer split)
        {
            try
            {
                int extent = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
                if (extent <= 0) extent = split.Orientation == Orientation.Vertical ? Width : Height;
                int half = Math.Max(1, extent / 2);
                int min = Math.Max(1, extent / 5);
                int max = Math.Max(min, extent - min);
                split.SplitterDistance = Math.Min(Math.Max(half, min), max);
            }
            catch (ArgumentOutOfRangeException)
            {
                // 极端尺寸下 SplitContainer 会拒绝，忽略
            }
            catch (InvalidOperationException)
            {
            }
        }

        /// <summary>结构变化后强制重排一次，避免留下「没铺满」的空隙。</summary>
        private void RelayoutPanes()
        {
            PerformLayout();
            foreach (Pane pane in _panes)
            {
                if (pane.Host.Dock != DockStyle.Fill) pane.Host.Dock = DockStyle.Fill;
                pane.Host.PerformLayout();
                pane.Tabs.PerformLayout();
                TabPage page = pane.Tabs.SelectedTab;
                if (page != null) page.PerformLayout();
                foreach (DocumentView doc in DocumentsIn(pane))
                {
                    doc.PerformLayout();
                    if (doc.CanvasArea != null) doc.CanvasArea.PerformLayout();
                }
            }
            Invalidate();
        }

        private static List<DocumentView> DocumentsIn(Pane pane)
        {
            List<DocumentView> docs = new List<DocumentView>();
            foreach (TabPage page in pane.Tabs.TabPages)
            {
                DocumentView doc = page.Tag as DocumentView;
                if (doc != null) docs.Add(doc);
            }
            return docs;
        }

        // ================= 导航 =================

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

        /// <summary>激活指定文档（切到它所在的标签页与面板）。</summary>
        public void Activate(DocumentView doc)
        {
            if (doc == null) return;
            TabPage page = doc.Tag as TabPage;
            if (page == null) return;
            TabControl owner = page.Parent as TabControl;
            if (owner == null) return;
            owner.SelectedTab = page;
            Pane pane = PaneOf(owner);
            if (pane != null) _active = pane;
            page.Focus();
            RaiseActiveChanged();
        }

        // ================= 拖动标签 =================

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
            ActivatePaneOf(tabs);
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

            Point client = PointToClient(now);
            DropPlacement place = HitDropPlacement(client);
            _dropPlacement = place;

            bool possible = place != DropPlacement.None && CanSplitCurrent();
            if (!possible)
            {
                tabs.Cursor = Cursors.No;
                HideDropHint();
                return;
            }

            // 光标用系统默认箭头，落点由虚线框表示
            tabs.Cursor = Cursors.Default;
            ShowDropHint(DropRectangleFor(TargetPaneOf(client), place));
        }

        private void OnTabsMouseUp(object sender, MouseEventArgs e)
        {
            TabControl tabs = (TabControl)sender;
            DocumentView doc = _dragDocument;
            bool dragged = _dragging;
            DropPlacement place = _dropPlacement;
            Point client = PointToClient(Cursor.Position);

            _dragDocument = null;
            _dragging = false;
            _dropPlacement = DropPlacement.None;
            tabs.Capture = false;
            tabs.Cursor = Cursors.Default;
            HideDropHint();

            if (doc == null || !dragged) return;

            // 拖到面板中间（不在边缘）：把标签移到那个面板里
            Pane dropTarget = TargetPaneOf(client);
            if (place == DropPlacement.None)
            {
                if (dropTarget != null) MoveIntoPane(dropTarget, doc);
                return;
            }
            if (!CanSplitCurrent()) return;

            // 拖到哪个面板就往哪个面板里拆
            Pane target = TargetPaneOf(client);
            if (target != null) _active = target;
            ApplyDrop(doc, place);
        }

        /// <summary>是否正在拖动标签（用于 Esc 取消）。</summary>
        public bool IsDraggingTab
        {
            get { return _dragDocument != null && _dragging; }
        }

        /// <summary>拖动过程中按 Esc 取消（不放手也能放弃本次拆分）。</summary>
        internal void CancelDrag()
        {
            _dragDocument = null;
            _dragging = false;
            _dropPlacement = DropPlacement.None;
            foreach (Pane pane in _panes)
            {
                pane.Tabs.Capture = false;
                pane.Tabs.Cursor = Cursors.Default;
            }
            HideDropHint();
        }

        /// <summary>当前是否可以再拆一个面板。</summary>
        private bool CanSplitCurrent()
        {
            Pane target = TargetPaneOf(PointToClient(Cursor.Position));
            return CanSplitTargetOf(target);
        }
        /// <summary>
        /// 鼠标所在的贴靠区（相对该面板的左/右/上/下）；不在边缘返回 None。
        /// 必须区分左右与上下，否则预览永远画在同一侧。
        /// </summary>
        private DropPlacement HitDropPlacement(Point client)
        {
            Pane target = TargetPaneOf(client);
            Rectangle bounds = target != null
                ? RectangleToClient(target.Host.RectangleToScreen(target.Host.ClientRectangle))
                : ClientRectangle;

            int left = client.X - bounds.Left;
            int right = bounds.Right - client.X;
            int top = client.Y - bounds.Top;
            int bottom = bounds.Bottom - client.Y;

            int min = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
            if (min > DropEdgeZone) return DropPlacement.None;

            if (min == left) return DropPlacement.Left;
            if (min == right) return DropPlacement.Right;
            if (min == top) return DropPlacement.Top;
            return DropPlacement.Bottom;
        }

        /// <summary>客户区坐标处最上层的面板；没有则退回活动面板。</summary>
        private Pane TargetPaneOf(Point client)
        {
            Point screen = PointToScreen(client);
            foreach (Pane pane in OrderedPanes())
            {
                Rectangle hostScreen = pane.Host.RectangleToScreen(pane.Host.ClientRectangle);
                if (hostScreen.Contains(screen)) return pane;
            }
            return ActivePane;
        }

        /// <summary>预览应覆盖的矩形（客户区坐标）：就是文档将要落到的那个半区。</summary>
        private Rectangle DropRectangle(DropPlacement place)
        {
            return DropRectangleFor(ActivePane, place);
        }

        private Rectangle DropRectangleFor(Pane pane, DropPlacement place)
        {
            Rectangle bounds = pane != null
                ? RectangleToClient(pane.Host.RectangleToScreen(pane.Host.ClientRectangle))
                : ClientRectangle;

            int halfW = Math.Max(1, bounds.Width / 2);
            int halfH = Math.Max(1, bounds.Height / 2);
            switch (place)
            {
                case DropPlacement.Left:
                    return new Rectangle(bounds.Left, bounds.Top, halfW, Math.Max(1, bounds.Height));
                case DropPlacement.Right:
                    return new Rectangle(bounds.Left + halfW, bounds.Top,
                        Math.Max(1, bounds.Width - halfW), Math.Max(1, bounds.Height));
                case DropPlacement.Top:
                    return new Rectangle(bounds.Left, bounds.Top, Math.Max(1, bounds.Width), halfH);
                case DropPlacement.Bottom:
                    return new Rectangle(bounds.Left, bounds.Top + halfH,
                        Math.Max(1, bounds.Width), Math.Max(1, bounds.Height - halfH));
                default:
                    return bounds;
            }
        }

        /// <summary>
        /// 拖动预览：用系统的「可逆虚线框」（ControlPaint.DrawReversibleFrame），
        /// 和资源管理器拖动时的虚线框完全一样，不做自绘填充。
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
    }
}
