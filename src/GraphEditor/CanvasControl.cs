using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace GraphEditor
{
    /// <summary>
    /// 工作区：负责绘制图、缩放、滚动条以及全部鼠标交互
    /// （选中 / 多选 / 拖动 / 右键菜单 / 连接模式）。
    /// </summary>
    public sealed class CanvasControl : Control
    {
        // 调色盘（画布右键菜单与工具栏共用）
        public static readonly Color[] Palette = new Color[]
        {
            Color.FromArgb(0x00, 0x00, 0x00), Color.FromArgb(0x40, 0x40, 0x40), Color.FromArgb(0x80, 0x80, 0x80), Color.FromArgb(0xC0, 0xC0, 0xC0),
            Color.FromArgb(0xFF, 0xFF, 0xFF), Color.FromArgb(0x7F, 0x33, 0x00), Color.FromArgb(0xC0, 0x50, 0x00), Color.FromArgb(0xFF, 0x99, 0x00),
            Color.FromArgb(0xFF, 0xD9, 0x66), Color.FromArgb(0xFF, 0xFF, 0x00), Color.FromArgb(0x99, 0xCC, 0x00), Color.FromArgb(0x00, 0xB0, 0x50),
            Color.FromArgb(0x00, 0x80, 0x40), Color.FromArgb(0x00, 0xB0, 0xB0), Color.FromArgb(0x00, 0xCC, 0xFF), Color.FromArgb(0x00, 0x70, 0xC0),
            Color.FromArgb(0x2D, 0x7D, 0xD2), Color.FromArgb(0x00, 0x33, 0x99), Color.FromArgb(0x66, 0x33, 0xCC), Color.FromArgb(0x99, 0x33, 0xCC),
            Color.FromArgb(0xE9, 0x1E, 0x63), Color.FromArgb(0xFF, 0x00, 0x00), Color.FromArgb(0xCC, 0x00, 0x00), Color.FromArgb(0xFF, 0x66, 0x99)
        };

        private const double MinZoom = 0.1;
        private const double MaxZoom = 8.0;
        private const int MinCanvasSide = 100;
        private const int MaxCanvasSide = 10000;
        private const double HitTolerance = 7.0;
        private const float VertexFontSize = 12f;
        private const float EdgeFontSize = 11f;
        private const int ScrollbarThickness = 17;
        /// <summary>
        /// 右键平源的「起手延迟」。右键按下后先等这么久：
        /// 期间松开 = 单击 → 弹右键菜单；超过它再移动 = 开始平移。
        ///
        /// 设得很短（50ms）是为了让平移手感接近 draw.io —— 那边右键拖动
        /// 直接就是平移，没有「要按住一会儿」的感觉；这里只用极小的一段时间
        /// 把「单纯单击」与「拖动」区分开。
        /// </summary>
        private const int RightHoldMilliseconds = 50;

        /// <summary>检查「起手延迟是否已过」的定时器间隔。</summary>
        private const int RightHoldTimerInterval = 10;

        /// <summary>判定「拖动」的位移阈值（左键：框选与移动）。故意设得小，让框选更跟手。</summary>
        private const int DragThreshold = 4;

        private GraphCanvas _canvas;
        private double _zoom = 1.0;
        private double _offsetX;
        private double _offsetY;

        private readonly VScrollBar _vScroll = new VScrollBar();
        private readonly HScrollBar _hScroll = new HScrollBar();
        private bool _scrollbarsVisible;

        private readonly HashSet<int> _selectedVertices = new HashSet<int>();
        private readonly HashSet<int> _selectedEdges = new HashSet<int>();

        private enum DragMode
        {
            None,
            MoveSelection,
            RubberBand
        }

        private DragMode _drag = DragMode.None;
        private Point _dragStartDevice;
        private PointD _dragStartWorld;
        private double _contentDragX;
        private double _contentDragY;
        private bool _dragMoved;
        private Dictionary<int, PointD> _moveOrigins;
        private RectD _rubberBand;

        // 右键平移的状态机：
        //   _rightPressed      按下未松开
        //   _rightHoldElapsed  已按住的毫秒数（由定时器累计）
        //   _rightMoveArming  起手延迟已过，再移动就开始平移
        //   _rightPanning     本次右键按住期间真的平移过（用它决定抬手是否弹菜单）
        //   _rightPanOrigin   平移基准点：**越过阈值那一刻的光标位置**，不是按下时的位置，
        //                     否则起步阶段会把按下点到阈值点的距离一次性算进去（瞬移）
        private bool _rightPressed;
        private bool _rightMoveArming;
        private bool _rightPanning;
        private int _rightHoldElapsedMs;
        private Point _rightPanOrigin;
        private double _rightPanOriginX;
        private double _rightPanOriginY;
        private readonly Timer _rightHoldTimer = new Timer();
        // 右键拖动连线：在顶点上按住右键并移动即进入连线拖动。
        private bool _connectDragging;
        private int _connectSourceId = -1;
        private int _connectTargetId = -1;
        private Point _connectPointerDevice;

        private int _hoverVertexId = -1;
        private int _hoverEdgeId = -1;

        public event EventHandler SelectionChanged;
        public event EventHandler CanvasChanged;
        /// <summary>缩放比例发生变化（滚轮、菜单、程序化设置都会触发）。</summary>
        public event EventHandler ZoomChanged;
        /// <summary>需要向状态栏推送一句提示。</summary>
        public event EventHandler<HintTextEventArgs> HintTextChanged;

        /// <summary>缩放变化时通知外部（状态栏据此实时刷新）。</summary>
        private void RaiseZoomChanged()
        {
            EventHandler handler = ZoomChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public CanvasControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Color.White;
            Size = new Size(600, 400);

            _vScroll.Visible = false;
            _hScroll.Visible = false;
            _vScroll.Scroll += delegate { OnScrollChanged(); };
            _hScroll.Scroll += delegate { OnScrollChanged(); };
            Controls.Add(_vScroll);
            Controls.Add(_hScroll);

            // 长按检测：右键按下后由定时器累计按住时长，到点即进入平移模式。
            // 用定时器而不是只在鼠标移动时判断，是为了「按住不动也算长按」。
            _rightHoldTimer.Interval = RightHoldTimerInterval;
            _rightHoldTimer.Tick += delegate { CheckRightHold(); };
        }

        /// <summary>
        /// 起手延迟检查（定时器回调，也是自检的注入点）。
        /// 延迟一到只是「武装」：真正开始平移要等用户移动鼠标（见 OnMouseMove），
        /// 这样单纯长按不动再松开仍然算单击，会把右键菜单弹出来。
        /// </summary>
        private void CheckRightHold()
        {
            if (!_rightPressed || _rightMoveArming) return;
            // 起点是顶点时，按住属于连线手势，不做平移
            if (_connectSourceId >= 0) return;

            _rightHoldElapsedMs += RightHoldTimerInterval;
            if (_rightHoldElapsedMs >= RightHoldMilliseconds)
            {
                _rightMoveArming = true;
            }
        }

        /// <summary>
        /// 自检用：把「已按住多久」推进指定毫秒，等价于定时器又跑了若干次。
        /// </summary>
        public void AdvanceRightHoldClock(int milliseconds)
        {
            _rightHoldElapsedMs += milliseconds;
            CheckRightHold();
        }

        /// <summary>自检用：起手延迟是否已过（可以开始平移）。</summary>
        public bool IsRightPanArmed { get { return _rightMoveArming; } }

        /// <summary>自检用：本次右键按住期间是否真的平移过。</summary>
        public bool IsRightPanning { get { return _rightPanning; } }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _rightHoldTimer.Stop();
                _rightHoldTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        public GraphCanvas Canvas
        {
            get { return _canvas; }
            set
            {
                _canvas = value;
                ClearSelectionInternal();
                EndConnectDrag();
                ResetView();
                Invalidate();
                RaiseSelectionChanged();
            }
        }

        public double Zoom { get { return _zoom; } }

        /// <summary>内容的外接矩形（图像坐标）；没有内容时为 null。</summary>
        public RectD? ContentExtent
        {
            get
            {
                if (_canvas == null) return null;
                if (_canvas.Vertices.Count == 0 && _canvas.Edges.Count == 0) return null;
                return _canvas.ContentBounds(40, 80);
            }
        }

        /// <summary>内容在视口里放不下时，对应的方向需要滚动条。</summary>
        private bool NeedsScroll(bool horizontal)
        {
            RectD? extent = ContentExtent;
            if (extent == null) return false;
            RectD b = extent.Value;
            if (horizontal)
            {
                return b.X < _offsetX || b.Right > _offsetX + ClientSize.Width / _zoom;
            }
            return b.Y < _offsetY || b.Bottom > _offsetY + ClientSize.Height / _zoom;
        }

        /// <summary>当前是否需要横向 / 纵向滚动条（内容超出可视区时为 true）。</summary>
        public bool NeedsHorizontalScrollbar { get { return NeedsScroll(true); } }
        public bool NeedsVerticalScrollbar { get { return NeedsScroll(false); } }
        public int HorizontalScrollMaximum { get { return MaxOffsetX; } }
        public int VerticalScrollMaximum { get { return MaxOffsetY; } }

        public int SelectedVertexCount { get { return _selectedVertices.Count; } }
        public int SelectedEdgeCount { get { return _selectedEdges.Count; } }

        /// <summary>是否正在用右键从一个顶点拖出连线。</summary>
        public bool IsConnectDragging { get { return _connectDragging; } }
        /// <summary>连线拖动起点顶点 Id；未在拖动时为 -1。</summary>
        public int ConnectSourceId { get { return _connectDragging ? _connectSourceId : -1; } }
        /// <summary>连线拖动当前指向的顶点 Id；没有指向顶点时为 -1。</summary>
        public int ConnectTargetId { get { return _connectDragging ? _connectTargetId : -1; } }

        /// <summary>当前可视工作区尺寸（已扣除滚动条）。</summary>
        public Size ViewportSize { get { return new Size(ViewportWidth, ViewportHeight); } }

        /// <summary>
        /// 视图原点：当前显示在工作区左上角的图像坐标。
        /// <c>_offsetX/_offsetY</c> 就是图像坐标（绘制时按「先缩放、再平移 −原点」映射），
        /// 因此这里直接返回。
        /// </summary>
        public PointD ViewOrigin { get { return new PointD(_offsetX, _offsetY); } }

        /// <summary>滚动条当前位置（设备像素）。</summary>
        public Point ScrollPosition { get { return new Point(_hScroll.Value, _vScroll.Value); } }

        /// <summary>滚动条可用的最大位置。</summary>
        public Point ScrollRange { get { return new Point(MaxOffsetX, MaxOffsetY); } }

        /// <summary>诊断用：可滚动上限的计算输入。</summary>
        public string ScrollDebug
        {
            get
            {
                if (_canvas == null) return "canvas=null";
                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "canvas={0}x{1} zoom={2:0.###} viewport={3}x{4} maxX={5} maxY={6} scrollbars={7}",
                    _canvas.CanvasWidth, _canvas.CanvasHeight, _zoom,
                    ViewportWidth, ViewportHeight, MaxOffsetX, MaxOffsetY, _scrollbarsVisible);
            }
        }

        /// <summary>设置视图原点（图像坐标）。会立即生效，不依赖滚动条状态。</summary>
        public void SetScrollPosition(int x, int y)
        {
            _offsetX = x;
            _offsetY = y;
            ClampOffsets();
            UpdateLayoutScrollbars(false);
            Invalidate();
        }

        /// <summary>把图像坐标换算为工作区客户区坐标，供测试与外部定位使用。</summary>
        public Point DevicePointFromImage(double imageX, double imageY)
        {
            PointF device = ImageToDevice(imageX, imageY);
            return new Point((int)Math.Round(device.X), (int)Math.Round(device.Y));
        }

        // ================= 坐标与滚动 =================
        //
        // 采用 draw.io 式的无限画布：没有固定纸张、没有画布尺寸，图元素可以落在
        // 任意坐标上，滚动条只反映「视口在平面上的位置」。
        //
        // 图像坐标 = 世界坐标 + 固定原点偏移。目前原点偏移为 (0,0)：
        // 初始视图把 (0,0) 放在客户区中心，x 向右、y 向下（与 .graph 文件一致）。

        /// <summary>画布原点在世界坐标中的位置（原点偏移）。</summary>
        public double CanvasOriginX { get { return 0; } }
        public double CanvasOriginY { get { return 0; } }

        /// <summary>世界坐标 → 图像坐标。</summary>
        public PointD WorldToImage(double worldX, double worldY)
        {
            return new PointD(worldX + CanvasOriginX, worldY + CanvasOriginY);
        }

        /// <summary>图像坐标 → 世界坐标。</summary>
        public PointD ImageToWorld(double imageX, double imageY)
        {
            return new PointD(imageX - CanvasOriginX, imageY - CanvasOriginY);
        }

        /// <summary>当前视口左上角对应的世界坐标。</summary>
        public PointD ViewOriginWorld
        {
            get { return ImageToWorld(_offsetX, _offsetY); }
        }

        /// <summary>世界坐标 → 设备坐标。</summary>
        public Point DevicePointFromWorld(double worldX, double worldY)
        {
            PointD img = WorldToImage(worldX, worldY);
            return new Point(
                (int)Math.Round((img.X - _offsetX) * _zoom),
                (int)Math.Round((img.Y - _offsetY) * _zoom));
        }

        private Matrix CanvasTransform()
        {
            // 图像坐标 → 设备坐标：先缩放，再平移 −视口原点。
            Matrix m = new Matrix();
            m.Scale((float)_zoom, (float)_zoom);
            m.Translate((float)-_offsetX, (float)-_offsetY);
            return m;
        }

        private PointF DeviceToImage(Point p)
        {
            return new PointF(
                (float)(p.X / _zoom + _offsetX),
                (float)(p.Y / _zoom + _offsetY));
        }

        private PointF ImageToDevice(double x, double y)
        {
            return new PointF((float)((x - _offsetX) * _zoom), (float)((y - _offsetY) * _zoom));
        }

        /// <summary>鼠标位置 → 世界坐标。</summary>
        private PointD DeviceToWorld(Point p)
        {
            PointF img = DeviceToImage(p);
            return ImageToWorld(img.X, img.Y);
        }



        private int ViewportWidth
        {
            get { return Math.Max(1, ClientSize.Width - (_scrollbarsVisible ? ScrollbarThickness : 0)); }
        }

        private int ViewportHeight
        {
            get { return Math.Max(1, ClientSize.Height - (_scrollbarsVisible ? ScrollbarThickness : 0)); }
        }

        /// <summary>
        /// 视图原点的可取值范围（图像坐标）。draw.io 式无限画布：范围由内容与视口共同决定。
        ///
        /// 范围内**不能**再并入当前视图，否则「拖动 → 范围扩大 → 能拖更远」会自激，
        /// 滚动条上限会随平移无限增长。这里只依赖内容与视口尺寸，平移过程中保持稳定。
        ///
        /// 空图时以原点为中心各方向留一屏（因此原点可以居中）；
        /// 有内容时在内容外侧各留一个视口，便于把内容拖到视野中间。
        /// </summary>
        private void GetScrollBounds(out double minX, out double minY, out double maxX, out double maxY)
        {
            double viewW = Math.Max(1, ClientSize.Width) / _zoom;
            double viewH = Math.Max(1, ClientSize.Height) / _zoom;

            // 空图：以原点为中心各留一屏
            minX = -viewW;
            maxX = viewW;
            minY = -viewH;
            maxY = viewH;

            if (_canvas != null && (_canvas.Vertices.Count > 0 || _canvas.Edges.Count > 0))
            {
                RectD bounds = _canvas.ContentBounds(40, 80);
                minX = Math.Min(minX, bounds.X - viewW);
                minY = Math.Min(minY, bounds.Y - viewH);
                maxX = Math.Max(maxX, bounds.Right + viewW);
                maxY = Math.Max(maxY, bounds.Bottom + viewH);
            }
        }

        /// <summary>视图原点的最大取值（图像坐标）。</summary>
        private int MaxOffsetX
        {
            get
            {
                double minX;
                double minY;
                double maxX;
                double maxY;
                GetScrollBounds(out minX, out minY, out maxX, out maxY);
                return (int)Math.Ceiling(maxX);
            }
        }

        private int MaxOffsetY
        {
            get
            {
                double minX;
                double minY;
                double maxX;
                double maxY;
                GetScrollBounds(out minX, out minY, out maxX, out maxY);
                return (int)Math.Ceiling(maxY);
            }
        }

        /// <summary>视图原点的最小取值（图像坐标）。</summary>
        private int MinOffsetX
        {
            get
            {
                double minX;
                double minY;
                double maxX;
                double maxY;
                GetScrollBounds(out minX, out minY, out maxX, out maxY);
                return (int)Math.Floor(minX);
            }
        }

        private int MinOffsetY
        {
            get
            {
                double minX;
                double minY;
                double maxX;
                double maxY;
                GetScrollBounds(out minX, out minY, out maxX, out maxY);
                return (int)Math.Floor(minY);
            }
        }

        /// <summary>把视图原点钳制在可滚动范围内。</summary>
        private void ClampOffsets()
        {
            _offsetX = Math.Max(MinOffsetX, Math.Min(MaxOffsetX, _offsetX));
            _offsetY = Math.Max(MinOffsetY, Math.Min(MaxOffsetY, _offsetY));
        }

        /// <summary>重置视图：100% 缩放，世界原点位于客户区中心。</summary>
        public void ResetView()
        {
            _zoom = 1.0;
            CenterOnOrigin();
            RaiseZoomChanged();
        }

        /// <summary>把世界原点放到客户区中心。</summary>
        public void CenterOnOrigin()
        {
            _offsetX = -ClientSize.Width / (2.0 * _zoom);
            _offsetY = -ClientSize.Height / (2.0 * _zoom);
            UpdateLayoutScrollbars(true);
            Invalidate();
        }

        /// <summary>
        /// 让内容居中（内容放得下时完全居中，放不下时把内容中心对齐到视口中心）。
        /// </summary>
        public void CenterOnContent()
        {
            RectD? extent = ContentExtent;
            if (extent == null)
            {
                CenterOnOrigin();
                return;
            }
            RectD b = extent.Value;
            _offsetX = (b.X + b.Right) / 2.0 - ClientSize.Width / (2.0 * _zoom);
            _offsetY = (b.Y + b.Bottom) / 2.0 - ClientSize.Height / (2.0 * _zoom);
            UpdateLayoutScrollbars(true);
            Invalidate();
        }

        /// <summary>
        /// 内容变化后刷新滚动范围（无限画布：滚动范围跟着内容走）。
        /// </summary>
        public void RefreshScrollRange()
        {
            UpdateLayoutScrollbars(true);
            Invalidate();
        }

        private void UpdateLayoutScrollbars(bool updateVisible)
        {
            int vw = ViewportWidth;
            int vh = ViewportHeight;

            if (updateVisible)
            {
                // 只有内容真的超出视口时才出现滚动条（与 draw.io 一致）
                bool visible = NeedsScroll(true) || NeedsScroll(false);
                if (visible != _scrollbarsVisible)
                {
                    _scrollbarsVisible = visible;
                    _vScroll.Visible = visible;
                    _hScroll.Visible = visible;
                }
                vw = ViewportWidth;
                vh = ViewportHeight;
            }

            int minX = MinOffsetX;
            int minY = MinOffsetY;
            int maxX = MaxOffsetX;
            int maxY = MaxOffsetY;

            _hScroll.SetBounds(0, ClientSize.Height - ScrollbarThickness, Math.Max(0, vw), ScrollbarThickness);
            _vScroll.SetBounds(ClientSize.Width - ScrollbarThickness, 0, ScrollbarThickness, Math.Max(0, vh));

            // 滚动条取值范围是 [min, max]（图像坐标）
            _hScroll.Minimum = minX;
            _hScroll.LargeChange = Math.Max(1, vw);
            _hScroll.SmallChange = Math.Max(1, vw / 10);
            _hScroll.Maximum = maxX + _hScroll.LargeChange - 1;

            _vScroll.Minimum = minY;
            _vScroll.LargeChange = Math.Max(1, vh);
            _vScroll.SmallChange = Math.Max(1, vh / 10);
            _vScroll.Maximum = maxY + _vScroll.LargeChange - 1;

            // 滚动条位置即视图原点（图像坐标）：向下拖 → 原点下移 → 看到内容下方部分。
            ApplyView();
        }

        /// <summary>设定原点后同步滚动条位置。滚动条值即视图原点（图像坐标）。</summary>
        private void ApplyView()
        {
            int minX = MinOffsetX;
            int minY = MinOffsetY;
            _offsetX = Math.Max(minX, Math.Min(MaxOffsetX, _offsetX));
            _offsetY = Math.Max(minY, Math.Min(MaxOffsetY, _offsetY));

            _hScroll.Value = Math.Min(_hScroll.Maximum,
                Math.Max(_hScroll.Minimum, (int)Math.Round(_offsetX)));
            _vScroll.Value = Math.Min(_vScroll.Maximum,
                Math.Max(_vScroll.Minimum, (int)Math.Round(_offsetY)));
        }

        private void OnScrollChanged()
        {
            _offsetX = _hScroll.Value;
            _offsetY = _vScroll.Value;
            ClampOffsets();
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateLayoutScrollbars(true);
            Invalidate();
        }

        // ================= 缩放 =================

        public void ZoomBy(double factor)
        {
            Point center = new Point(ViewportWidth / 2, ViewportHeight / 2);
            ZoomAt(center, factor);
        }

        /// <summary>以工作区上的某个点为锚点缩放（保持该点指向的图像位置不变）。</summary>
        public void ZoomAtPoint(Point deviceAnchor, double factor)
        {
            ZoomAt(deviceAnchor, factor);
        }

        public void ResetZoom()
        {
            if (_canvas == null) return;
            _zoom = 1.0;
            _offsetX = 0;
            _offsetY = 0;
            UpdateLayoutScrollbars(true);
            Invalidate();
            RaiseZoomChanged();
        }

        /// <summary>直接设定缩放比例（会重新计算滚动范围），供诊断与外部控制使用。</summary>
        public void SetZoom(double zoom)
        {
            if (_canvas == null) return;
            _zoom = Math.Max(MinZoom, Math.Min(MaxZoom, zoom));
            UpdateLayoutScrollbars(true);
            CenterView();
            UpdateLayoutScrollbars(true);
            Invalidate();
            RaiseZoomChanged();
        }

        private void ZoomAt(Point deviceAnchor, double factor)
        {
            if (_canvas == null) return;
            double newZoom = Math.Max(MinZoom, Math.Min(MaxZoom, _zoom * factor));
            if (Math.Abs(newZoom - _zoom) < 1e-9) return;

            // 保持鼠标指向的图像点不动：device = (image − origin) × zoom
            double imgX = deviceAnchor.X / _zoom + _offsetX;
            double imgY = deviceAnchor.Y / _zoom + _offsetY;
            _zoom = newZoom;
            // 反解新原点，使同一点仍落在同一设备位置
            _offsetX = imgX - deviceAnchor.X / _zoom;
            _offsetY = imgY - deviceAnchor.Y / _zoom;

            UpdateLayoutScrollbars(true);
            Invalidate();
            RaiseZoomChanged();
        }

        /// <summary>画布尺寸变化后保持视图合理。</summary>
        public void OnCanvasResized()
        {
            UpdateLayoutScrollbars(true);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Focus();
            double steps = e.Delta / 120.0;
            double factor = Math.Pow(1.15, steps);
            ZoomAt(e.Location, factor);
        }

        // ================= 选中状态 =================

        private void ClearSelectionInternal()
        {
            _selectedVertices.Clear();
            _selectedEdges.Clear();
        }

        public void ClearSelection()
        {
            if (_selectedVertices.Count == 0 && _selectedEdges.Count == 0) return;
            ClearSelectionInternal();
            Invalidate();
            RaiseSelectionChanged();
        }

        public void SelectAll()
        {
            if (_canvas == null) return;
            ClearSelectionInternal();
            foreach (Vertex v in _canvas.Vertices) _selectedVertices.Add(v.Id);
            foreach (Edge e in _canvas.Edges) _selectedEdges.Add(e.Id);
            Invalidate();
            RaiseSelectionChanged();
        }

        public List<Vertex> SelectedVertices()
        {
            List<Vertex> list = new List<Vertex>();
            if (_canvas == null) return list;
            foreach (int id in _selectedVertices)
            {
                Vertex v = _canvas.FindVertex(id);
                if (v != null) list.Add(v);
            }
            return list;
        }

        public List<Edge> SelectedEdges()
        {
            List<Edge> list = new List<Edge>();
            if (_canvas == null) return list;
            foreach (int id in _selectedEdges)
            {
                Edge e = _canvas.FindEdge(id);
                if (e != null) list.Add(e);
            }
            return list;
        }

        public bool HasSelection { get { return _selectedVertices.Count > 0 || _selectedEdges.Count > 0; } }

        public void SetSelection(List<Vertex> vertices, List<Edge> edges)
        {
            ClearSelectionInternal();
            if (vertices != null)
            {
                foreach (Vertex v in vertices) _selectedVertices.Add(v.Id);
            }
            if (edges != null)
            {
                foreach (Edge e in edges) _selectedEdges.Add(e.Id);
            }
            Invalidate();
            RaiseSelectionChanged();
        }

        /// <summary>给所有选中元素统一着色。</summary>
        public void ApplyColorToSelection(Color color)
        {
            if (!HasSelection) return;
            foreach (Vertex v in SelectedVertices()) v.Color = color;
            foreach (Edge e in SelectedEdges()) e.Color = color;
            NotifyCanvasChanged();
            Invalidate();
        }

        /// <summary>删除所有选中的顶点与边（删除顶点会一并删除关联边）。</summary>
        public void DeleteSelection()
        {
            if (!HasSelection || _canvas == null) return;
            foreach (int id in new List<int>(_selectedVertices)) _canvas.RemoveVertex(id);
            foreach (int id in new List<int>(_selectedEdges)) _canvas.RemoveEdge(id);
            ClearSelectionInternal();
            UpdateLayoutScrollbars(true);
            NotifyCanvasChanged();
            Invalidate();
            RaiseSelectionChanged();
        }

        public void NudgeSelection(double dx, double dy)
        {
            if (_selectedVertices.Count == 0 || _canvas == null) return;
            foreach (Vertex v in SelectedVertices())
            {
                v.X += dx;
                v.Y += dy;
            }
            NotifyCanvasChanged();
            Invalidate();
        }

        // ================= 复制 / 剪切 / 粘贴 =================

        /// <summary>剪贴板里的一个顶点。</summary>
        private sealed class ClipboardVertex
        {
            public int OldId;
            public double X;
            public double Y;
            public string Name;
            public Color? Color;
        }

        private sealed class ClipboardEdge
        {
            public int V1;
            public int V2;
            public string Name;
            public Color? Color;
        }

        /// <summary>应用内剪贴板（同一实例内可用；不写入系统剪贴板）。</summary>
        private static List<ClipboardVertex> _clipboardVertices;
        private static List<ClipboardEdge> _clipboardEdges;

        /// <summary>
        /// 连续粘贴的序号：每次粘贴递增，用于把副本依次错开。
        /// 重新复制/剪切时归零，因此换一份内容后第一份仍落在视图中心。
        /// </summary>
        private int _pasteCascadeIndex;

        public static bool HasClipboardContent
        {
            get { return _clipboardVertices != null && _clipboardVertices.Count > 0; }
        }

        /// <summary>
        /// 复制选中的顶点集所诱导的子图：选中的顶点，加上两端点都被选中的边
        /// （显式选中的边也算）。返回是否真的复制了内容。
        /// </summary>
        public bool CopySelection()
        {
            if (_canvas == null || _selectedVertices.Count == 0) return false;

            List<ClipboardVertex> vertices = new List<ClipboardVertex>();
            foreach (Vertex v in SelectedVertices())
            {
                ClipboardVertex item = new ClipboardVertex();
                item.OldId = v.Id;
                item.X = v.X;
                item.Y = v.Y;
                item.Name = v.Name;
                item.Color = v.Color;
                vertices.Add(item);
            }

            List<ClipboardEdge> edges = new List<ClipboardEdge>();
            foreach (Edge e in _canvas.Edges)
            {
                bool bothSelected = _selectedVertices.Contains(e.V1) && _selectedVertices.Contains(e.V2);
                bool explicitlySelected = _selectedEdges.Contains(e.Id);
                if (!bothSelected && !explicitlySelected) continue;

                ClipboardEdge item = new ClipboardEdge();
                item.V1 = e.V1;
                item.V2 = e.V2;
                item.Name = e.Name;
                item.Color = e.Color;
                edges.Add(item);
            }

            _clipboardVertices = vertices;
            _clipboardEdges = edges;
            // 换了一份内容：下一次粘贴重新从视图中心开始错位计数
            _pasteCascadeIndex = 0;
            SetHintText("已复制 " + vertices.Count + " 个顶点、" + edges.Count + " 条边");
            return true;
        }

        /// <summary>
        /// 剪切：复制诱导子图后删除选中的顶点，并删除所有与选中顶点相连的边
        /// （仅一端被选中的边也会被删除）。
        /// </summary>
        public bool CutSelection()
        {
            if (_canvas == null || _selectedVertices.Count == 0) return false;
            int vertexCount = _selectedVertices.Count;
            if (!CopySelection()) return false;

            // 先统计将被删除的边（含只连一个选中顶点的边）
            int removedEdges = 0;
            foreach (Edge e in new List<Edge>(_canvas.Edges))
            {
                if (_selectedVertices.Contains(e.V1) || _selectedVertices.Contains(e.V2))
                {
                    removedEdges++;
                }
            }

            foreach (int id in new List<int>(_selectedVertices)) _canvas.RemoveVertex(id);
            foreach (int id in new List<int>(_selectedEdges)) _canvas.RemoveEdge(id);

            ClearSelectionInternal();
            UpdateLayoutScrollbars(true);
            NotifyCanvasChanged();
            Invalidate();
            RaiseSelectionChanged();
            SetHintText("已剪切 " + vertexCount + " 个顶点（连带删除 " + removedEdges + " 条边）");
            return true;
        }

        /// <summary>
        /// 连续粘贴的错位步长（图像坐标）。第一份粘贴在视图中心，第 n 份再往右下
        /// 错开 (n-1) 倍步长，避免多份副本完全重叠。
        /// 步长取内容尺寸的 6%，并限制在合理区间内，这样大图、小图都不会叠住。
        /// </summary>
        private double PasteCascadeStep(double minX, double minY, double maxX, double maxY)
        {
            double w = Math.Max(1.0, maxX - minX);
            double h = Math.Max(1.0, maxY - minY);
            double span = Math.Max(w, h);
            double step = span * 0.06;
            if (step < 24) step = 24;
            if (step > 120) step = 120;
            return step;
        }
        /// <summary>把剪贴板内容粘贴到当前图，返回新建的元素数量（顶点）。</summary>
        public int PasteClipboard()
        {
            if (_canvas == null || _clipboardVertices == null || _clipboardVertices.Count == 0) return 0;

            // 粘贴位置：原内容整体平移，使其落在当前视图中心附近
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            foreach (ClipboardVertex v in _clipboardVertices)
            {
                if (v.X < minX) minX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.X > maxX) maxX = v.X;
                if (v.Y > maxY) maxY = v.Y;
            }

            // 粘贴位置：第一份落在当前视图中心，之后每一份再往右下错开一点，
            // 否则连续粘贴会全部叠在同一处（看起来像只粘贴了一次）。
            //
            // 基准点用 ClientSize 而不是 ViewportWidth：第一次粘贴后内容变大、
            // 滚动条出现，ViewportWidth 会从 800 缩到 783；若用它算中心，每份
            // 副本都会被往回"拽"一点点，正好把级联错位抵消掉。
            double step = PasteCascadeStep(minX, minY, maxX, maxY);
            PointD target = ViewOrigin;
            double targetX = target.X + ClientSize.Width / (2.0 * _zoom);
            double targetY = target.Y + ClientSize.Height / (2.0 * _zoom);
            double offsetX = targetX - (minX + maxX) / 2.0 + step * _pasteCascadeIndex;
            double offsetY = targetY - (minY + maxY) / 2.0 + step * _pasteCascadeIndex;
            _pasteCascadeIndex++;

            Dictionary<int, int> idMap = new Dictionary<int, int>();
            List<Vertex> created = new List<Vertex>();
            foreach (ClipboardVertex item in _clipboardVertices)
            {
                Vertex v = _canvas.AddVertex(item.X + offsetX, item.Y + offsetY,
                    _canvas.MakeVertexNameUnique(item.Name));
                v.Color = item.Color;
                idMap[item.OldId] = v.Id;
                created.Add(v);
            }

            List<Edge> createdEdges = new List<Edge>();
            foreach (ClipboardEdge item in _clipboardEdges)
            {
                int v1;
                int v2;
                // 端点可能不在剪贴板里（显式选中的边），此时丢弃
                if (!idMap.TryGetValue(item.V1, out v1)) continue;
                if (!idMap.TryGetValue(item.V2, out v2)) continue;
                Edge e = _canvas.AddEdge(v1, v2, _canvas.MakeEdgeNameUnique(item.Name));
                if (e != null)
                {
                    e.Color = item.Color;
                    createdEdges.Add(e);
                }
            }

            ClearSelectionInternal();
            foreach (Vertex v in created) _selectedVertices.Add(v.Id);
            foreach (Edge e in createdEdges) _selectedEdges.Add(e.Id);

            UpdateLayoutScrollbars(true);
            NotifyCanvasChanged();
            Invalidate();
            RaiseSelectionChanged();
            SetHintText("已粘贴 " + created.Count + " 个顶点、" + createdEdges.Count + " 条边");
            return created.Count;
        }

        // ================= 旋转 =================

        /// <summary>
        /// 绕选中顶点的中心旋转（角度为度，正数表示视觉上的顺时针）。
        /// 在世界坐标（y 向上）里做旋转，因此把图像坐标的 y 取反后再转。
        /// 只旋转选中的顶点；未选中顶点上的边会跟着端点移动。
        /// </summary>
        public bool RotateSelection(double degrees)
        {
            if (_canvas == null || _selectedVertices.Count == 0) return false;
            List<Vertex> vertices = SelectedVertices();

            // 转到世界坐标（y 向上）
            double cx = 0;
            double cy = 0;
            foreach (Vertex v in vertices)
            {
                PointD w = ImageToWorld(v.X, v.Y);
                cx += w.X;
                cy += w.Y;
            }
            cx /= vertices.Count;
            cy /= vertices.Count;

            double radians = degrees * Math.PI / 180.0;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);

            foreach (Vertex v in vertices)
            {
                PointD w = ImageToWorld(v.X, v.Y);
                double dx = w.X - cx;
                double dy = w.Y - cy;
                // y 向上的坐标系里，正角度按标准旋转矩阵即为视觉顺时针
                double nx = cx + dx * cos - dy * sin;
                double ny = cy + dx * sin + dy * cos;
                PointD img = WorldToImage(nx, ny);
                v.X = img.X;
                v.Y = img.Y;
            }

            UpdateLayoutScrollbars(true);
            NotifyCanvasChanged();
            Invalidate();
            SetHintText("已旋转 " + degrees.ToString("0.##", CultureInfo.InvariantCulture) + "°（"
                + vertices.Count + " 个顶点）");
            return true;
        }

        private void NotifyCanvasChanged()
        {
            EventHandler handler = CanvasChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void RaiseSelectionChanged()
        {
            EventHandler handler = SelectionChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void SetHintText(string text)
        {
            EventHandler<HintTextEventArgs> handler = HintTextChanged;
            if (handler != null) handler(this, new HintTextEventArgs(text));
        }

        // ================= 右键拖动连线 =================

        /// <summary>在指定顶点上开始连线拖动（由右键按下触发）。</summary>
        private void BeginConnectDrag(int vertexId, Point device)
        {
            _connectDragging = true;
            _connectSourceId = vertexId;
            _connectTargetId = -1;
            _connectPointerDevice = device;
            Cursor = Cursors.Cross;
            Invalidate();
        }

        /// <summary>结束连线拖动（不创建边）。</summary>
        private void EndConnectDrag()
        {
            if (!_connectDragging && _connectSourceId < 0) return;
            _connectDragging = false;
            _connectSourceId = -1;
            _connectTargetId = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        /// <summary>把拖动位置更新为图像坐标，并重新计算目标顶点。</summary>
        private void UpdateConnectPointer(Point device)
        {
            _connectPointerDevice = device;
            PointF img = DeviceToImage(device);
            Vertex target = HitTestVertex(img);
            int targetId = target == null ? -1 : target.Id;
            if (targetId != _connectTargetId)
            {
                _connectTargetId = targetId;
            }
            Invalidate();
        }

        /// <summary>在 source → target 之间创建一条边，并选中它。返回新边。</summary>
        public Edge ConnectVertices(int sourceId, int targetId)
        {
            if (_canvas == null) return null;
            Vertex source = _canvas.FindVertex(sourceId);
            Vertex target = _canvas.FindVertex(targetId);
            if (source == null || target == null) return null;

            // 不要在这里预生成名字：AddEdge 收到显式名字就会跳过 NextEdgeName，
            // 那样「属性 → 边的默认命名」规则（含两端点名字）就永远不会生效。
            Edge edge = _canvas.AddEdge(source.Id, target.Id);
            if (edge == null) return null;

            ClearSelectionInternal();
            _selectedEdges.Add(edge.Id);
            NotifyCanvasChanged();
            Invalidate();
            RaiseSelectionChanged();
            return edge;
        }

        // ================= 命中测试 =================

        /// <summary>命中测试（图像坐标）：返回顶点或边，两者都未命中时为 null。</summary>
        public object HitTest(double imageX, double imageY)
        {
            bool isVertex;
            return HitTest(imageX, imageY, out isVertex);
        }

        public object HitTest(double imageX, double imageY, out bool isVertex)
        {
            PointF p = new PointF((float)imageX, (float)imageY);
            Vertex v = HitTestVertex(p);
            if (v != null)
            {
                isVertex = true;
                return v;
            }
            Edge e = HitTestEdge(p);
            isVertex = false;
            return e;
        }

        private Vertex HitTestVertex(PointF img)
        {
            if (_canvas == null) return null;
            for (int i = _canvas.Vertices.Count - 1; i >= 0; i--)
            {
                Vertex v = _canvas.Vertices[i];
                RectD b = _canvas.VertexBounds(v);
                double pad = 2.0 / Math.Max(0.2, _zoom);
                if (img.X >= b.X - pad && img.X <= b.Right + pad && img.Y >= b.Y - pad && img.Y <= b.Bottom + pad)
                {
                    return v;
                }
            }
            return null;
        }

        private Edge HitTestEdge(PointF img)
        {
            if (_canvas == null) return null;
            double tolerance = HitTolerance / Math.Max(0.2, _zoom);
            for (int i = _canvas.Edges.Count - 1; i >= 0; i--)
            {
                Edge e = _canvas.Edges[i];
                if (_canvas.HitEdge(e, img.X, img.Y, tolerance)) return e;
            }
            return null;
        }

        // ================= 鼠标 =================

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_canvas == null) return;
            Focus();

            PointF img = DeviceToImage(e.Location);

            if (e.Button == MouseButtons.Right)
            {
                _rightPressed = true;
                _rightPanning = false;
                _rightMoveArming = false;
                _rightHoldElapsedMs = 0;
                _rightPanOrigin = e.Location;
                _rightPanOriginX = _offsetX;
                _rightPanOriginY = _offsetY;
                _dragMoved = false;
                _dragStartDevice = e.Location;
                _contentDragX = _offsetX;
                _contentDragY = _offsetY;
                _drag = DragMode.None;
                // 记录按下的顶点：此处先不改状态，等真正拖动起来再决定是连线还是平移，
                // 这样单纯的右键单击仍然会弹出菜单。
                Vertex pressedVertex = HitTestVertex(img);
                _connectSourceId = pressedVertex == null ? -1 : pressedVertex.Id;
                _connectTargetId = -1;
                _connectDragging = false;
                // 起手计时：50ms 后「武装」，此后一旦移动就开始平移
                _rightHoldTimer.Start();
                return;
            }

            if (e.Button != MouseButtons.Left) return;

            Vertex hitVertex = HitTestVertex(img);
            Edge hitEdge = hitVertex == null ? HitTestEdge(img) : null;

            _dragMoved = false;
            _dragStartDevice = e.Location;
            _dragStartWorld = DeviceToWorld(e.Location);

            if (hitVertex != null)
            {
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    if (!_selectedVertices.Remove(hitVertex.Id)) _selectedVertices.Add(hitVertex.Id);
                }
                else if (!_selectedVertices.Contains(hitVertex.Id))
                {
                    ClearSelectionInternal();
                    _selectedVertices.Add(hitVertex.Id);
                }
                BeginMoveSelection();
            }
            else if (hitEdge != null)
            {
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    if (!_selectedEdges.Remove(hitEdge.Id)) _selectedEdges.Add(hitEdge.Id);
                }
                else if (!_selectedEdges.Contains(hitEdge.Id))
                {
                    ClearSelectionInternal();
                    _selectedEdges.Add(hitEdge.Id);
                }
                BeginMoveSelection();
            }
            else
            {
                if ((ModifierKeys & Keys.Control) != Keys.Control) ClearSelectionInternal();
                _drag = DragMode.RubberBand;
                _rubberBand = new RectD(img.X, img.Y, 0, 0);
            }

            Invalidate();
            RaiseSelectionChanged();
        }

        private void BeginMoveSelection()
        {
            _drag = DragMode.MoveSelection;
            _moveOrigins = new Dictionary<int, PointD>();
            foreach (int id in _selectedVertices)
            {
                Vertex v = _canvas.FindVertex(id);
                // 记录世界坐标，移动量在世界坐标里叠加
                if (v != null) _moveOrigins[id] = ImageToWorld(v.X, v.Y);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_canvas == null) return;

            PointD world = DeviceToWorld(e.Location);
            int dxDevice = e.Location.X - _dragStartDevice.X;
            int dyDevice = e.Location.Y - _dragStartDevice.Y;
            // 左键仍用位移阈值判断「拖动」；右键的平移资格由长按时间决定，
            // 这里不再参与判定（见 CheckRightHold）。
            if (!_rightPressed
                && !_dragMoved
                && (Math.Abs(dxDevice) > DragThreshold || Math.Abs(dyDevice) > DragThreshold))
            {
                _dragMoved = true;
            }

            // 右键优先处理：它不设置 _drag，必须在"无拖动"的提前返回之前判断。
            // 在顶点上按住并移动 = 拉出连线（与起手延迟无关）；
            // 在空白处或边上按住 = 起手延迟过后一开始移动就平移。
            if (_rightPressed)
            {
                if (_connectDragging)
                {
                    UpdateConnectPointer(e.Location);
                    return;
                }
                if (_connectSourceId >= 0)
                {
                    // 起点是顶点：一有移动就升级为连线拖动
                    if (Math.Abs(dxDevice) > DragThreshold || Math.Abs(dyDevice) > DragThreshold)
                    {
                        BeginConnectDrag(_connectSourceId, e.Location);
                        UpdateConnectPointer(e.Location);
                    }
                    return;
                }

                if (_rightMoveArming)
                {
                    if (!_rightPanning)
                    {
                        // 刚开始平移：以「此刻的光标位置」为基准，而不是按下时的位置。
                        // 否则起手阶段的位移会在这一帧被一次性算进去，画面瞬移。
                        _rightPanning = true;
                        _rightPanOrigin = e.Location;
                        _rightPanOriginX = _offsetX;
                        _rightPanOriginY = _offsetY;
                        Cursor = Cursors.SizeAll;
                    }
                    // 光标位移是设备像素，视图原点是图像坐标，两者相差一个 zoom。
                    // 必须除以 zoom：否则放大时画面比光标跑得快、缩小时跑得慢。
                    _offsetX = _rightPanOriginX - (e.Location.X - _rightPanOrigin.X) / _zoom;
                    _offsetY = _rightPanOriginY - (e.Location.Y - _rightPanOrigin.Y) / _zoom;
                    UpdateLayoutScrollbars(true);
                    Invalidate();
                }
                return;
            }

            if (_drag == DragMode.None)
            {
                UpdateHover(e.Location);
                return;
            }

            switch (_drag)
            {
                case DragMode.MoveSelection:
                    if (_dragMoved && _moveOrigins != null)
                    {
                        // 位移量在世界坐标里计算（y 向上），再换算回图像坐标存放
                        double dxWorld = world.X - _dragStartWorld.X;
                        double dyWorld = world.Y - _dragStartWorld.Y;
                        foreach (KeyValuePair<int, PointD> pair in _moveOrigins)
                        {
                            Vertex v = _canvas.FindVertex(pair.Key);
                            if (v == null) continue;
                            PointD img = WorldToImage(pair.Value.X + dxWorld, pair.Value.Y + dyWorld);
                            v.X = img.X;
                            v.Y = img.Y;
                        }
                                    Invalidate();
                    }
                    break;

                case DragMode.RubberBand:
                    // 选框用图像坐标（y 向下），保证矩形求交等逻辑不受坐标系影响
                    _rubberBand = RectD.FromPoints(
                        WorldToImage(_dragStartWorld.X, _dragStartWorld.Y).X,
                        WorldToImage(_dragStartWorld.X, _dragStartWorld.Y).Y,
                        WorldToImage(world.X, world.Y).X,
                        WorldToImage(world.X, world.Y).Y);
                    ApplyRubberBandSelection();
                    Invalidate();
                    break;
            }
        }

        private void UpdateHover(Point device)
        {
            PointF img = DeviceToImage(device);
            Vertex v = HitTestVertex(img);
            Edge ed = v == null ? HitTestEdge(img) : null;
            int newVertex = v == null ? -1 : v.Id;
            int newEdge = ed == null ? -1 : ed.Id;
            if (newVertex != _hoverVertexId || newEdge != _hoverEdgeId)
            {
                _hoverVertexId = newVertex;
                _hoverEdgeId = newEdge;
                Invalidate();
            }
        }

        private void ApplyRubberBandSelection()
        {
            ClearSelectionInternal();

            // 先选出与选框相交的顶点
            foreach (Vertex v in _canvas.Vertices)
            {
                if (_rubberBand.IntersectsWith(_canvas.VertexBounds(v))) _selectedVertices.Add(v.Id);
            }

            // 只有两个端点都被选中的边才算被选中（与复制/剪切的诱导子图语义一致）
            foreach (Edge e in _canvas.Edges)
            {
                if (_selectedVertices.Contains(e.V1) && _selectedVertices.Contains(e.V2))
                {
                    _selectedEdges.Add(e.Id);
                }
            }

            RaiseSelectionChanged();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_canvas == null) return;

            if (e.Button == MouseButtons.Right)
            {
                bool wasPanning = _rightPanning;
                bool wasConnecting = _connectDragging;
                int sourceId = _connectSourceId;
                PointF img = DeviceToImage(e.Location);
                Vertex releaseTarget = HitTestVertex(img);

                _rightHoldTimer.Stop();
                _rightPressed = false;
                _rightMoveArming = false;
                _rightPanning = false;
                _rightHoldElapsedMs = 0;

                if (wasConnecting)
                {
                    EndConnectDrag();
                    Vertex source = _canvas.FindVertex(sourceId);
                    // 松开时若落在顶点上（含自身，形成自环）则建边，否则视为放弃
                    if (releaseTarget != null && source != null)
                    {
                        ConnectVertices(source.Id, releaseTarget.Id);
                        SetHintText("已连接 " + source.Name + " → " + releaseTarget.Name);
                    }
                    else
                    {
                        SetHintText("已取消连线（松开时不在顶点上）");
                    }
                    return;
                }

                Cursor = Cursors.Default;
                // 只要这次按住期间**没有真的平移过**，抬手就弹菜单 —— 包括
                // 「按住很久但一直没动」的情况（那种情况用户显然是想开菜单）。
                if (!wasPanning)
                {
                    ShowContextMenu(e.Location);
                }
                return;
            }

            if (e.Button != MouseButtons.Left) return;

            DragMode finished = _drag;
            bool moved = _dragMoved;
            _drag = DragMode.None;
            _moveOrigins = null;
            UpdateHover(e.Location);

            if (finished == DragMode.MoveSelection && moved)
            {
                // 拖动结束后内容位置变了，刷新滚动范围
                NotifyCanvasChanged();
            }
            if (finished == DragMode.RubberBand)
            {
                _rubberBand = new RectD(0, 0, 0, 0);
            }
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (_canvas == null || e.Button != MouseButtons.Left) return;
            PointF img = DeviceToImage(e.Location);
            Vertex v = HitTestVertex(img);
            if (v != null)
            {
                string newName;
                if (PromptDialog.ShowText(this.FindForm(), "重命名顶点", "顶点名称：", v.Name, out newName))
                {
                    v.Name = newName;
                    NotifyCanvasChanged();
                    Invalidate();
                }
                return;
            }
            Edge edge = HitTestEdge(img);
            if (edge != null)
            {
                string newName;
                if (PromptDialog.ShowText(this.FindForm(), "重命名边", "边名称：", edge.Name, out newName))
                {
                    edge.Name = newName;
                    NotifyCanvasChanged();
                    Invalidate();
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_canvas == null) return;

            if (e.KeyCode == Keys.Escape)
            {
                if (_connectDragging)
                {
                    EndConnectDrag();
                    SetHintText("已取消连线");
                    _rightHoldTimer.Stop();
                    _rightPressed = false;
                    _rightMoveArming = false;
                    _rightPanning = false;
                    _rightHoldElapsedMs = 0;
                    e.Handled = true;
                    return;
                }
                ClearSelection();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back)
            {
                DeleteSelection();
                e.Handled = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.A)
            {
                SelectAll();
                e.Handled = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.C)
            {
                CopySelection();
                e.Handled = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.X)
            {
                CutSelection();
                e.Handled = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.V)
            {
                PasteClipboard();
                e.Handled = true;
                return;
            }

            double step = (e.Shift ? 10.0 : 1.0) / Math.Max(0.2, _zoom);
            switch (e.KeyCode)
            {
                case Keys.Left: NudgeSelection(-step, 0); e.Handled = true; break;
                case Keys.Right: NudgeSelection(step, 0); e.Handled = true; break;
                case Keys.Up: NudgeSelection(0, -step); e.Handled = true; break;
                case Keys.Down: NudgeSelection(0, step); e.Handled = true; break;
            }
        }

        // ================= 右键菜单 =================

        /// <summary>自检用：记录最近一次右键菜单是在哪个位置弹出的（null 表示还没弹过）。</summary>
        private Point? _lastContextMenuAt;

        public bool ContextMenuWasShown { get { return _lastContextMenuAt.HasValue; } }

        public void ResetContextMenuFlag()
        {
            _lastContextMenuAt = null;
        }

        private void ShowContextMenu(Point device)
        {
            _lastContextMenuAt = device;
            ContextMenuStrip menu = BuildContextMenu(device);
            menu.Show(this, device);
        }

        /// <summary>
        /// 按给定位置应当显示的右键菜单（不弹出）。抽成独立方法便于自检菜单结构。
        /// </summary>
        internal ContextMenuStrip BuildContextMenu(Point device)
        {
            PointF img = DeviceToImage(device);
            Vertex vertex = HitTestVertex(img);
            Edge edge = vertex == null ? HitTestEdge(img) : null;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = true;
            if (vertex != null)
            {
                // 右键已选中的元素时保持多选，否则只选中当前元素
                if (!_selectedVertices.Contains(vertex.Id))
                {
                    ClearSelectionInternal();
                    _selectedVertices.Add(vertex.Id);
                    Invalidate();
                    RaiseSelectionChanged();
                }

                ToolStripMenuItem connectHint = new ToolStripMenuItem("按住右键拖动可连接到其他顶点");
                connectHint.Enabled = false;
                menu.Items.Add(connectHint);
                menu.Items.Add(new ToolStripSeparator());

                AddRenameAndColor(menu);
                AddEditItems(menu);
                menu.Items.Add(new ToolStripSeparator());
                AddDeleteItem(menu);

                if (_selectedVertices.Count > 1)
                {
                    ToolStripMenuItem info = new ToolStripMenuItem("已选中 " + _selectedVertices.Count + " 个顶点");
                    info.Enabled = false;
                    menu.Items.Insert(0, info);
                }
            }
            else if (edge != null)
            {
                if (!_selectedEdges.Contains(edge.Id))
                {
                    ClearSelectionInternal();
                    _selectedEdges.Add(edge.Id);
                    Invalidate();
                    RaiseSelectionChanged();
                }
                AddRenameAndColor(menu);
                AddEditItems(menu);
                menu.Items.Add(new ToolStripSeparator());
                AddDeleteItem(menu);
            }
            else
            {
                ToolStripMenuItem create = new ToolStripMenuItem("新建顶点(&N)");
                finalWorldPoint = DeviceToWorld(device);
                create.Click += delegate
                {
                    PointD placed = WorldToImage(finalWorldPoint.X, finalWorldPoint.Y);
                    Vertex v = _canvas.AddVertex(Math.Round(placed.X), Math.Round(placed.Y));
                    ClearSelectionInternal();
                    _selectedVertices.Add(v.Id);
                        NotifyCanvasChanged();
                    Invalidate();
                    RaiseSelectionChanged();
                };
                menu.Items.Add(create);

                if (HasSelection)
                {
                    // 有选中元素时，粘贴由 AddEditItems 统一提供（剪切/复制/粘贴/旋转一组）。
                    // 这里**不能再加一个**，否则右键菜单里会出现两个「粘贴」。
                    menu.Items.Add(new ToolStripSeparator());
                    ToolStripMenuItem connectHint = new ToolStripMenuItem("按住右键从顶点拖动可连接");
                    connectHint.Enabled = false;
                    menu.Items.Add(connectHint);
                    menu.Items.Add(new ToolStripSeparator());

                    AddRenameAndColor(menu);
                    AddEditItems(menu);
                    menu.Items.Add(new ToolStripSeparator());
                    AddDeleteItem(menu);
                }
                else
                {
                    ToolStripMenuItem paste = new ToolStripMenuItem("粘贴(&P)");
                    paste.ShortcutKeyDisplayString = "Ctrl+V";
                    paste.Enabled = HasClipboardContent;
                    paste.Click += delegate { PasteClipboard(); };
                    menu.Items.Add(paste);
                    menu.Items.Add(new ToolStripSeparator());

                    ToolStripMenuItem selectAll = new ToolStripMenuItem("全选(&A)");
                    selectAll.Click += delegate { SelectAll(); };
                    menu.Items.Add(selectAll);
                    menu.Items.Add(new ToolStripSeparator());
                    ToolStripMenuItem resetView = new ToolStripMenuItem("重置视图(&R)");
                    resetView.Click += delegate { ResetZoom(); CenterView(); };
                    menu.Items.Add(resetView);
                }
            }

            return menu;
        }

        private PointD finalWorldPoint;

        /// <summary>复制 / 剪切 / 粘贴 / 旋转。</summary>
        private void AddEditItems(ContextMenuStrip menu)
        {
            bool hasVertices = _selectedVertices.Count > 0;

            ToolStripMenuItem cut = new ToolStripMenuItem("剪切(&T)");
            cut.ShortcutKeyDisplayString = "Ctrl+X";
            cut.Enabled = hasVertices;
            cut.Click += delegate { CutSelection(); };
            menu.Items.Add(cut);

            ToolStripMenuItem copy = new ToolStripMenuItem("复制(&C)");
            copy.ShortcutKeyDisplayString = "Ctrl+C";
            copy.Enabled = hasVertices;
            copy.Click += delegate { CopySelection(); };
            menu.Items.Add(copy);

            ToolStripMenuItem paste = new ToolStripMenuItem("粘贴(&P)");
            paste.ShortcutKeyDisplayString = "Ctrl+V";
            paste.Enabled = HasClipboardContent;
            paste.Click += delegate { PasteClipboard(); };
            menu.Items.Add(paste);

            ToolStripMenuItem rotate = new ToolStripMenuItem("旋转(&R)");
            rotate.Enabled = hasVertices;
            AddRotateItem(rotate, "顺时针 15°", 15);
            AddRotateItem(rotate, "顺时针 90°", 90);
            AddRotateItem(rotate, "顺时针 180°", 180);
            AddRotateItem(rotate, "逆时针 90°", -90);
            AddRotateItem(rotate, "逆时针 15°", -15);
            rotate.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem custom = new ToolStripMenuItem("自定义角度(&A)…");
            custom.Click += delegate
            {
                string text;
                if (!PromptDialog.ShowText(FindForm(), "旋转选中的顶点",
                    "旋转角度（度，正数为顺时针）：", "45", out text)) return;
                double degrees;
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out degrees))
                {
                    SetHintText("角度无法识别：" + text);
                    return;
                }
                RotateSelection(degrees);
            };
            rotate.DropDownItems.Add(custom);
            menu.Items.Add(rotate);
        }

        private void AddRotateItem(ToolStripMenuItem parent, string text, double degrees)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += delegate { RotateSelection(degrees); };
            parent.DropDownItems.Add(item);
        }

        private void AddRenameAndColor(ContextMenuStrip menu)
        {
            ToolStripMenuItem rename = new ToolStripMenuItem("重命名(&R)…");
            rename.Click += delegate { RenameSelection(); };
            menu.Items.Add(rename);

            ToolStripMenuItem color = new ToolStripMenuItem("颜色(&O)");
            foreach (Color c in Palette)
            {
                ToolStripMenuItem item = new ToolStripMenuItem();
                item.Text = GraphIo.ColorToHex(c);
                item.Image = MakeSwatchImage(c, 14, IsCurrentSelectionColor(c));
                Color captured = c;
                item.Click += delegate { ApplyColorToSelection(captured); };
                color.DropDownItems.Add(item);
            }

            // 默认颜色：清掉逐个着色，让选中的元素回到「跟随全局默认」。
            // 和上面的调色盘点选不同，这个不是某一种具体颜色。
            color.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem useDefault = new ToolStripMenuItem("默认颜色（跟随全局）(&D)");
            useDefault.Image = MakeSwatchImage(DefaultColorForSelection(), 14, SelectionUsesDefaultColor());
            useDefault.Click += delegate { ApplyDefaultColorToSelection(); };
            color.DropDownItems.Add(useDefault);

            menu.Items.Add(color);
        }

        /// <summary>
        /// 选中的元素「跟随全局默认」时应该呈现的颜色。同时选中顶点和边时以顶点为准
        /// （顶点与边的全局默认色是两套）。
        /// </summary>
        private Color DefaultColorForSelection()
        {
            if (_selectedVertices.Count > 0) return _canvas.DefaultVertexColor;
            if (_selectedEdges.Count > 0) return _canvas.DefaultEdgeColor;
            return _canvas.DefaultVertexColor;
        }

        /// <summary>选中的元素是否全部处于「默认颜色」状态（即都没有单独设色）。</summary>
        private bool SelectionUsesDefaultColor()
        {
            int total = 0;
            foreach (Vertex v in SelectedVertices())
            {
                total++;
                if (v.Color != null) return false;
            }
            foreach (Edge e in SelectedEdges())
            {
                total++;
                if (e.Color != null) return false;
            }
            return total > 0;
        }

        /// <summary>把选中元素的单独着色清除，恢复为跟随全局默认色。</summary>
        public void ApplyDefaultColorToSelection()
        {
            if (!HasSelection) return;
            foreach (Vertex v in SelectedVertices()) v.Color = null;
            foreach (Edge e in SelectedEdges()) e.Color = null;
            NotifyCanvasChanged();
            Invalidate();
            SetHintText("已恢复默认颜色（跟随全局设置）");
        }

        private void AddDeleteItem(ContextMenuStrip menu)
        {
            string text = "删除(&D)";
            if (_selectedVertices.Count + _selectedEdges.Count > 1)
            {
                text = "删除选中的 " + (_selectedVertices.Count + _selectedEdges.Count) + " 项(&D)";
            }
            ToolStripMenuItem delete = new ToolStripMenuItem(text);
            delete.ShortcutKeyDisplayString = "Delete";
            delete.Click += delegate { DeleteSelection(); };
            menu.Items.Add(delete);
        }

        private bool IsCurrentSelectionColor(Color c)
        {
            int match = 0;
            int total = 0;
            foreach (Vertex v in SelectedVertices())
            {
                total++;
                if (_canvas.EffectiveColor(v).ToArgb() == c.ToArgb()) match++;
            }
            foreach (Edge e in SelectedEdges())
            {
                total++;
                if (_canvas.EffectiveColor(e).ToArgb() == c.ToArgb()) match++;
            }
            return total > 0 && match == total;
        }

        /// <summary>重命名当前选中的顶点或边（供右键菜单与编辑菜单共用）。</summary>
        public void RenameSelection()
        {            List<Vertex> vertices = SelectedVertices();
            List<Edge> edges = SelectedEdges();

            // 同时选中顶点和边时，优先处理顶点；只选中边时处理边。
            if (vertices.Count > 0)
            {
                if (vertices.Count > 1)
                {
                    string prefix;
                    if (!PromptDialog.ShowText(FindForm(), "重命名选中的顶点",
                        "名称前缀（将依次编号，例如 X1、X2…）：", vertices[0].Name, out prefix)) return;
                    for (int i = 0; i < vertices.Count; i++)
                    {
                        vertices[i].Name = prefix + (i + 1).ToString(CultureInfo.InvariantCulture);
                    }
                }
                else
                {
                    string name;
                    if (!PromptDialog.ShowText(FindForm(), "重命名顶点", "顶点名称：", vertices[0].Name, out name)) return;
                    vertices[0].Name = name;
                }
                NotifyCanvasChanged();
                Invalidate();
                return;
            }

            if (edges.Count > 0)
            {
                if (edges.Count > 1)
                {
                    string prefix;
                    if (!PromptDialog.ShowText(FindForm(), "重命名选中的边",
                        "名称前缀（将依次编号）：", edges[0].Name, out prefix)) return;
                    for (int i = 0; i < edges.Count; i++)
                    {
                        edges[i].Name = prefix + (i + 1).ToString(CultureInfo.InvariantCulture);
                    }
                }
                else
                {
                    string name;
                    if (!PromptDialog.ShowText(FindForm(), "重命名边", "边名称：", edges[0].Name, out name)) return;
                    edges[0].Name = name;
                }
                NotifyCanvasChanged();
                Invalidate();
            }
        }

        public static Image MakeSwatchImage(Color color, int size, bool checkedMark)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(color);
                using (Pen border = new Pen(Color.FromArgb(0x60, 0x60, 0x60)))
                {
                    g.DrawRectangle(border, 0, 0, size - 1, size - 1);
                }
                if (checkedMark)
                {
                    Color mark = GraphCanvas.ContrastTextColor(color);
                    using (Pen pen = new Pen(mark, 2f))
                    {
                        g.DrawLines(pen, new Point[]
                        {
                            new Point(3, size / 2),
                            new Point(size / 2 - 1, size - 4),
                            new Point(size - 3, 3)
                        });
                    }
                }
            }
            return bmp;
        }

        // ================= 视图辅助 =================

        public void CenterView()
        {
            if (_canvas == null) return;
            // 原点居中：让图像中心对准视口中心（原点为图像坐标）
            _offsetX = (_canvas.CanvasWidth - ViewportWidth / _zoom) / 2.0;
            _offsetY = (_canvas.CanvasHeight - ViewportHeight / _zoom) / 2.0;
            UpdateLayoutScrollbars(true);
            ApplyView();
            Invalidate();
        }

        /// <summary>让视图恰好容纳整张图像。</summary>
        public void ZoomToFit()
        {
            if (_canvas == null) return;
            RectD? extent = ContentExtent;
            if (extent == null)
            {
                // 空图没有可适应的内容，回到 100% 并居中即可
                ResetZoom();
                CenterOnOrigin();
                return;
            }
            RectD b = extent.Value;
            double zx = ViewportWidth / Math.Max(1.0, b.Width);
            double zy = ViewportHeight / Math.Max(1.0, b.Height);
            _zoom = Math.Max(MinZoom, Math.Min(MaxZoom, Math.Min(zx, zy) * 0.95));
            UpdateLayoutScrollbars(true);
            CenterOnContent();
            RaiseZoomChanged();
        }

        public void ScrollBy(int dx, int dy)
        {
            _offsetX += dx;
            _offsetY += dy;
            UpdateLayoutScrollbars(false);
        }

        // ================= 绘制 =================

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // draw.io 式无限画布：整个工作区就是画布，没有纸张、没有边界
            using (SolidBrush back = new SolidBrush(BackColor))
            {
                g.FillRectangle(back, ClientRectangle);
            }

            if (_canvas == null) return;

            Matrix saved = g.Transform;
            g.Transform = CanvasTransform();

            DrawGraph(g);

            g.Transform = saved;

            DrawRubberBand(g);
            DrawConnectDragPreview(g);
        }

        private void DrawGraph(Graphics g)
        {
            double inv = 1.0 / _zoom;

            // ---- 边的选中 / 悬停高亮（先统一铺一层，避免相互遮挡） ----
            foreach (Edge edge in _canvas.Edges)
            {
                bool selected = _selectedEdges.Contains(edge.Id);
                bool hover = _hoverEdgeId == edge.Id;
                if (!selected && !hover) continue;
                Color haloColor = selected ? Color.FromArgb(0x1E, 0x88, 0xE5) : Color.FromArgb(0x90, 0xCA, 0xF9);
                double extra = selected ? 6 : 5;
                using (Pen halo = new Pen(haloColor, (float)((_canvas.EdgeWidth + extra) * inv)))
                {
                    halo.StartCap = LineCap.Round;
                    halo.EndCap = LineCap.Round;
                    halo.LineJoin = LineJoin.Round;
                    DrawEdgeGeometry(g, edge, halo);
                }
            }

            // ---- 边本体 ----
            foreach (Edge edge in _canvas.Edges)
            {
                Color color = _canvas.EffectiveColor(edge);
                using (Pen pen = new Pen(color, (float)(_canvas.EdgeWidth * inv)))
                {
                    _canvas.ApplyPenStyle(pen);
                    DrawEdgeGeometry(g, edge, pen);
                }
            }

            // ---- 边名字（无背景，沿边方向倾斜，位置在中点法线上） ----
            if (_zoom > 0.45)
            {
                using (Font font = new Font("Microsoft YaHei UI", (float)(EdgeFontSize * inv)))
                using (StringFormat format = new StringFormat())
                using (SolidBrush textBrush = new SolidBrush(EdgeLabelTextColor()))
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;

                    foreach (Edge edge in _canvas.Edges)
                    {
                        if (string.IsNullOrEmpty(edge.Name)) continue;
                        if (_canvas.EdgePolyline(edge).Length < 2) continue;

                        // 锚点取折线中点，倾斜角取该处的切线方向
                        double angle = _canvas.EdgeLabelAngle(edge);

                        // 沿法线偏移（上/下方），保证名字落在「过中点的法线」上
                        PointD anchor = _canvas.EdgeLabelAnchor(edge, EdgeLabelOffset());

                        Matrix saved = g.Transform;
                        g.TranslateTransform((float)anchor.X, (float)anchor.Y);
                        g.RotateTransform((float)angle);
                        g.DrawString(edge.Name, font, textBrush, 0, 0, format);
                        g.Transform = saved;
                    }
                }
            }

            // ---- 顶点 ----
            foreach (Vertex v in _canvas.Vertices)
            {
                Color fill = _canvas.EffectiveColor(v);
                bool selected = _selectedVertices.Contains(v.Id);
                bool hover = _hoverVertexId == v.Id;
                double r = _canvas.VertexRadius;

                if (selected || hover)
                {
                    Color haloColor = selected ? Color.FromArgb(0x1E, 0x88, 0xE5) : Color.FromArgb(0x90, 0xCA, 0xF9);
                    float extra = (float)((selected ? 6 : 5) * inv);
                    using (SolidBrush halo = new SolidBrush(haloColor))
                    {
                        DrawVertexGeometry(g, v, r + extra, halo, null);
                    }
                }

                Color stroke = GraphCanvas.Darken(fill, 0.7);
                using (SolidBrush brush = new SolidBrush(fill))
                using (Pen pen = new Pen(stroke, (float)(2.0 * inv)))
                {
                    DrawVertexGeometry(g, v, r, brush, pen);
                }
            }

            // ---- 顶点名字（位置由「属性」设置决定） ----
            if (_zoom > 0.35)
            {
                using (Font font = new Font("Microsoft YaHei UI", (float)(VertexFontSize * inv)))
                using (StringFormat format = new StringFormat())
                using (SolidBrush textBrush = new SolidBrush(Color.Black))
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;

                    foreach (Vertex v in _canvas.Vertices)
                    {
                        if (string.IsNullOrEmpty(v.Name)) continue;
                        Color fill = _canvas.EffectiveColor(v);
                        double r = _canvas.VertexRadius;
                        RectangleF rect;
                        if (_canvas.VertexLabelPlacement == VertexLabelPlacement.Inside)
                        {
                            textBrush.Color = GraphCanvas.ContrastTextColor(fill);
                            rect = new RectangleF(
                                (float)(v.X - r), (float)(v.Y - r * 1.2),
                                (float)(r * 2), (float)(r * 2.4));
                        }
                        else
                        {
                            textBrush.Color = Color.FromArgb(0x20, 0x20, 0x20);
                            double gap = r + 3;
                            SizeF size = g.MeasureString(v.Name, font);
                            float w = size.Width + 4;
                            float h = size.Height + 2;
                            switch (_canvas.VertexLabelPlacement)
                            {
                                case VertexLabelPlacement.Above:
                                    rect = new RectangleF((float)v.X - w / 2, (float)(v.Y - gap) - h, w, h);
                                    break;
                                case VertexLabelPlacement.Below:
                                    rect = new RectangleF((float)v.X - w / 2, (float)(v.Y + gap), w, h);
                                    break;
                                case VertexLabelPlacement.Left:
                                    rect = new RectangleF((float)(v.X - gap) - w, (float)v.Y - h / 2, w, h);
                                    break;
                                default: // Right
                                    rect = new RectangleF((float)(v.X + gap), (float)v.Y - h / 2, w, h);
                                    break;
                            }
                        }
                        g.DrawString(v.Name, font, textBrush, rect, format);
                    }
                }
            }
        }

        /// <summary>
        /// 边名字相对线段中点的法线偏移（图像坐标，正数指向屏幕下方）。
        /// 名字没有背景，所以上/下方都移出足够距离，避免压在线上。
        /// </summary>
        private double EdgeLabelOffset()
        {
            switch (_canvas.EdgeLabelPlacement)
            {
                case EdgeLabelPlacement.Inside:
                    return 0;
                case EdgeLabelPlacement.Below:
                    return EdgeFontSize * 1.1;
                default:
                    return -EdgeFontSize * 1.1;
            }
        }

        /// <summary>边名字的文字颜色：用深灰色，去掉背景后照样清晰可读。</summary>
        private static Color EdgeLabelTextColor()
        {
            return Color.FromArgb(0x20, 0x20, 0x20);
        }

        private void DrawEdgeGeometry(Graphics g, Edge edge, Pen pen)
        {
            PointD[] pts = _canvas.EdgePolyline(edge);
            if (pts.Length < 2) return;
            if (pts.Length == 2)
            {
                g.DrawLine(pen, (float)pts[0].X, (float)pts[0].Y, (float)pts[1].X, (float)pts[1].Y);
            }
            else
            {
                PointF[] converted = new PointF[pts.Length];
                for (int i = 0; i < pts.Length; i++)
                {
                    converted[i] = new PointF((float)pts[i].X, (float)pts[i].Y);
                }
                g.DrawLines(pen, converted);
            }

            // 有向图：在终点（V2 一侧）画箭头。无向图只是不画箭头，边本身保留。
            if (_canvas.Directed) DrawArrowHead(g, pen, edge, pts);
        }

        /// <summary>
        /// 在折线末端按局部方向画一个实心箭头。
        /// 折线是「顶点中心到顶点中心」，所以箭头要沿折线往回退到终点顶点的边界上，
        /// 否则会被后来绘制的顶点盖住。
        /// </summary>
        private void DrawArrowHead(Graphics g, Pen pen, Edge edge, PointD[] pts)
        {
            if (pts.Length < 2) return;

            // 尺寸随线宽与缩放变化，但换算回图像坐标，保证视觉上恒定
            double inv = 1.0 / _zoom;
            double size = (6.0 + _canvas.EdgeWidth * 1.6) * inv;
            double half = size * 0.5;

            // 终点顶点边界：直线边要沿折线精确退到圆外，自环的近端本来就在圆外
            double guard = edge.IsLoop ? 0 : _canvas.VertexRadius + size * 0.5;

            // 沿折线从末端往回走 guard 的距离，插值出箭头尖端与方向
            // （不能只按折线顶点回退：直线边只有两个端点，退一格就跑到另一头了）
            double remaining = guard;
            double tipX = pts[pts.Length - 1].X;
            double tipY = pts[pts.Length - 1].Y;
            double dx = pts[pts.Length - 1].X - pts[pts.Length - 2].X;
            double dy = pts[pts.Length - 1].Y - pts[pts.Length - 2].Y;
            double dirLen = Math.Sqrt(dx * dx + dy * dy);
            if (dirLen > 1e-9) { dx /= dirLen; dy /= dirLen; }

            for (int i = pts.Length - 1; i > 0 && remaining > 0; i--)
            {
                double sx = pts[i].X - pts[i - 1].X;
                double sy = pts[i].Y - pts[i - 1].Y;
                double segLen = Math.Sqrt(sx * sx + sy * sy);
                if (segLen < 1e-9) continue;

                if (segLen >= remaining)
                {
                    // 尖端落在这一段内部
                    double t = remaining / segLen;
                    tipX = pts[i].X - sx * t;
                    tipY = pts[i].Y - sy * t;
                    dx = sx / segLen;
                    dy = sy / segLen;
                    remaining = 0;
                }
                else
                {
                    remaining -= segLen;
                    tipX = pts[i - 1].X;
                    tipY = pts[i - 1].Y;
                }
            }

            if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;

            // 箭头底边中点（沿反方向退一段）
            double bx = tipX - dx * size;
            double by = tipY - dy * size;
            // 垂直于方向的单位向量
            double px = -dy;
            double py = dx;

            PointF[] tri = new PointF[]
            {
                new PointF((float)tipX, (float)tipY),
                new PointF((float)(bx + px * half), (float)(by + py * half)),
                new PointF((float)(bx - px * half), (float)(by - py * half))
            };

            using (SolidBrush brush = new SolidBrush(pen.Color))
            {
                g.FillPolygon(brush, tri);
            }
        }

        private void DrawVertexGeometry(Graphics g, Vertex v, double radius, Brush brush, Pen pen)
        {
            if (_canvas.VertexShape == VertexShape.Square)
            {
                double w = radius * 2;
                double h = radius * 1.5;
                RectangleF rect = new RectangleF((float)(v.X - w / 2), (float)(v.Y - h / 2), (float)w, (float)h);
                if (brush != null) g.FillRectangle(brush, rect);
                if (pen != null) g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            }
            else
            {
                RectangleF rect = new RectangleF((float)(v.X - radius), (float)(v.Y - radius), (float)(radius * 2), (float)(radius * 2));
                if (brush != null) g.FillEllipse(brush, rect);
                if (pen != null) g.DrawEllipse(pen, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }

        private void DrawRubberBand(Graphics g)
        {
            if (_drag != DragMode.RubberBand || _rubberBand.Width <= 0 && _rubberBand.Height <= 0) return;
            PointF p1 = ImageToDevice(_rubberBand.X, _rubberBand.Y);
            PointF p2 = ImageToDevice(_rubberBand.Right, _rubberBand.Bottom);
            RectangleF rect = new RectangleF(p1.X, p1.Y, p2.X - p1.X, p2.Y - p1.Y);
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(40, 0x1E, 0x88, 0xE5)))
            using (Pen pen = new Pen(Color.FromArgb(0x1E, 0x88, 0xE5)))
            {
                pen.DashStyle = DashStyle.Dash;
                g.FillRectangle(fill, rect);
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }

        /// <summary>
        /// 右键拖动连线时的预览：从起点顶点中心到鼠标位置画一条直线段，
        /// 当鼠标悬停在某个顶点上时高亮该顶点，表示松开即可连接。
        /// </summary>
        private void DrawConnectDragPreview(Graphics g)
        {
            if (!_connectDragging) return;
            Vertex source = _canvas.FindVertex(_connectSourceId);
            if (source == null) return;

            PointF from = ImageToDevice(source.X, source.Y);
            PointF to = _connectPointerDevice;
            Vertex target = _connectTargetId >= 0 ? _canvas.FindVertex(_connectTargetId) : null;

            Color accent = Color.FromArgb(0x1E, 0x88, 0xE5);
            if (target != null) accent = Color.FromArgb(0x00, 0xA0, 0x50);

            // 指向目标顶点时把线收在顶点边缘，视觉上更明确
            if (target != null)
            {
                double radius = _canvas.VertexRadius * _zoom;
                double dx = to.X - from.X;
                double dy = to.Y - from.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len > radius + 1)
                {
                    to = new PointF(
                        (float)(to.X - dx / len * radius),
                        (float)(to.Y - dy / len * radius));
                }
            }

            using (Pen outer = new Pen(Color.FromArgb(120, 255, 255, 255), 5f))
            {
                outer.StartCap = LineCap.Round;
                outer.EndCap = LineCap.Round;
                g.DrawLine(outer, from, to);
            }
            using (Pen pen = new Pen(accent, 2.5f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.DashStyle = target != null ? DashStyle.Solid : DashStyle.Dash;
                g.DrawLine(pen, from, to);
            }

            // 起点标记
            using (SolidBrush brush = new SolidBrush(accent))
            {
                g.FillEllipse(brush, from.X - 3.5f, from.Y - 3.5f, 7f, 7f);
            }

            // 目标顶点高亮环
            if (target != null)
            {
                PointF device = ImageToDevice(target.X, target.Y);
                float r = (float)(_canvas.VertexRadius * _zoom) + 5f;
                using (Pen ring = new Pen(accent, 3f))
                {
                    g.DrawEllipse(ring, device.X - r, device.Y - r, r * 2, r * 2);
                }
            }

            // 鼠标旁的提示
            string text = target != null
                ? "松开右键：连接到 " + target.Name
                : "拖到目标顶点后松开右键";
            using (Font font = new Font("Microsoft YaHei UI", 9f))
            {
                SizeF size = g.MeasureString(text, font);
                float bx = _connectPointerDevice.X + 14;
                float by = _connectPointerDevice.Y + 14;
                RectangleF box = new RectangleF(bx, by, size.Width + 12, size.Height + 8);
                Color boxBack = target != null
                    ? Color.FromArgb(238, 0xE8, 0xF7, 0xEE)
                    : Color.FromArgb(238, 0xFF, 0xF8, 0xE1);
                using (SolidBrush back = new SolidBrush(boxBack))
                using (Pen border = new Pen(accent))
                using (SolidBrush fore = new SolidBrush(Color.FromArgb(0x30, 0x30, 0x30)))
                {
                    g.FillRectangle(back, box);
                    g.DrawRectangle(border, box.X, box.Y, box.Width, box.Height);
                    g.DrawString(text, font, fore, box.X + 6, box.Y + 4);
                }
            }
        }

        // ================= 图像尺寸 =================

        public static bool ValidateCanvasSize(int width, int height, out string error)
        {
            error = null;
            if (width < MinCanvasSide || width > MaxCanvasSide)
            {
                error = "宽度必须在 " + MinCanvasSide + " ~ " + MaxCanvasSide + " 之间。";
                return false;
            }
            if (height < MinCanvasSide || height > MaxCanvasSide)
            {
                error = "高度必须在 " + MinCanvasSide + " ~ " + MaxCanvasSide + " 之间。";
                return false;
            }
            return true;
        }
    }
}
