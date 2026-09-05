using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Live2DPet.Platform.Native;

namespace Live2DPet.Platform.Window;

/// <summary>
/// 原生分层窗口（WS_EX_LAYERED）：把每帧 BGRA 位图合成到桌面最上层。
/// 自带消息循环线程；顶部把手区可拖动，其余区域默认鼠标穿透。
/// </summary>
public sealed class PetLayeredWindow : IDisposable
{
    private int _width;             // 当前画布宽度（随缩放变化）
    private int _height;            // 当前画布高度（随缩放变化）
    private readonly int _handleHeight = 30; // 顶部可拖动把手高度（px）

    private IntPtr _hwnd;
    private Thread? _thread;
    private volatile bool _running;
    private NativeMethods.WndProc _wndProc; // 必须保持引用，避免被 GC
    private readonly string _className = "Live2DPetLayered_" + Guid.NewGuid().ToString("N");

    // 离屏 DIB（位图缓存）
    private IntPtr _hdcMem;
    private IntPtr _hBitmap;
    private IntPtr _ppvBits;

    /// <summary>单击落在角色哪个部位（按角色实际包围盒分区），用于不同抚摸反应。</summary>
    public enum HitRegion { Head, Body, Feet }

    /// <summary>在角色可见区域单击（无拖拽）时触发，参数为点击落在头部/身体/脚部。</summary>
    public event Action<HitRegion>? PetClicked;
    /// <summary>在角色可见区域拖拽时触发，业务层据此播放 Flick 反应（同时窗口跟随光标移动）。</summary>
    public event Action? PetDragged;
    /// <summary>在角色可见区域双击时触发，业务层据此播放 Tap@Body 反应。</summary>
    public event Action? PetDoubleClicked;
    /// <summary>在角色可见区域右键抬起时触发，参数为屏幕坐标，业务层据此弹出右键菜单。</summary>
    public event Action<int, int>? PetRightClicked;
    /// <summary>拖拽结束（窗口位置已改变）时触发，业务层据此保存新位置。</summary>
    public event Action? PetMoved;

    private bool _clickThrough;          // 与 SetClickThrough 同步：true=整窗穿透、不交互
    private bool _pressing, _handlePress, _dragging;
    private int _downX, _downY;
    private int _dragCursorX, _dragCursorY, _dragWinX, _dragWinY;

    // 拖拽惯性：记录光标速度，松手后按摩擦衰减继续滑动；撞屏幕边则回弹
    private int _inertiaVx, _inertiaVy;          // 像素/秒
    private long _inertiaLastTick;
    private int _sampleX, _sampleY;
    private long _sampleT;                         // 上一采样点（光标屏坐标 + 高频计时戳）
    private const int InertiaTimerId = 1;
    private const int InertiaIntervalMs = 16;
    private bool _inertiaRunning;

    // 隐藏（全局快捷键一键清场）：透明度归零 + 鼠标穿透
    private float _savedOpacity = 1f;
    private bool _hidden;

    private float _opacity = 1f;         // 整体不透明度 0..1
    private bool _draggable = true;      // 是否允许拖动（false=锁定位置）
    private readonly int _initialX, _initialY; // 初始位置（负值=自动放右下角）

    private const int _alphaThreshold = 16;   // 可见像素阈值（alpha 大于此值视为角色本体）
    private const int MK_LBUTTON = 0x0001;
    private const int DragThresholdSq = 16;   // 位移超过 4px 判定为拖拽

    // 贴边吸附 + 半隐藏
    private const int DockThreshold = 24;     // 窗口边缘距屏幕边缘 < 此值判定为贴边
    public const int PeekVisible = 80;        // 半隐藏时在屏内露出的像素（露太少用户找不到）
    public const int EdgeDetectPx = 10;       // 鼠标距屏幕边缘多少像素内触发滑出
    private DockEdge _docked = DockEdge.None;
    private bool _peeked = true;              // true=完全显示；false=半隐藏

    // 角色实际绘制区域（基于帧 alpha 计算），用于贴边半隐藏时始终露出人物而非空白边
    private int _contentMinX = -1, _contentMaxX = -1, _contentMinY = -1, _contentMaxY = -1;
    private int _contentScannedW, _contentScannedH;

    public enum DockEdge { None, Left, Right, Top, Bottom }
    public DockEdge DockedEdge => _docked;
    public bool IsDocked => _docked != DockEdge.None;
    public bool IsPeeked => _peeked;
    /// <summary>是否正在被用户拖拽（供上层在拖拽期间保持满帧，避免卡顿）。</summary>
    public bool IsDragging => _dragging;

    public PetLayeredWindow(int width, int height, int initialX = -1, int initialY = -1)
    {
        _width = width;
        _height = height;
        _initialX = initialX;
        _initialY = initialY;

        // 保护：保存的窗口位置若不在当前虚拟屏幕内（多屏断开/分辨率变化），重置为右下角默认
        if (initialX >= 0 || initialY >= 0)
        {
            int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            if (initialX < vx || initialX >= vx + vw) _initialX = -1;
            if (initialY < vy || initialY >= vy + vh) _initialY = -1;
        }
    }

    public void Show()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(ThreadProc) { IsBackground = true, Name = "PetWindow" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Dispose()
    {
        _running = false;
        StopInertia();
        if (_hwnd != IntPtr.Zero)
            NativeMethods.PostMessage(_hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(1000);
        if (_hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(_hBitmap);
        if (_hdcMem != IntPtr.Zero) NativeMethods.DeleteDC(_hdcMem);
    }

    /// <summary>整体鼠标穿透开关。true=整窗穿透（含把手也无法拖动）；false=仅角色区穿透、把手可拖动。</summary>
    public void SetClickThrough(bool enable)
    {
        _clickThrough = enable;
        if (_hwnd == IntPtr.Zero) return;
        int ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        if (enable) ex |= NativeMethods.WS_EX_TRANSPARENT;
        else ex &= ~NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);
    }

    /// <summary>设置整体不透明度 0..1（作用于分层窗口的 SourceConstantAlpha）。</summary>
    public void SetOpacity(float opacity) => _opacity = Math.Clamp(opacity, 0.05f, 1f);

    /// <summary>是否允许拖动（false=锁定位置，仍可点击触发反应）。</summary>
    public void SetDraggable(bool draggable) => _draggable = draggable;

    /// <summary>调整画布尺寸（缩放）。重建 DIB 并按新尺寸重设窗口。</summary>
    public void Resize(int w, int h)
    {
        if (w <= 0 || h <= 0) return;
        if (_width == w && _height == h) return;
        _width = w;
        _height = h;

        if (_hwnd == IntPtr.Zero) return; // 窗口尚未创建，ThreadProc 会用新尺寸建窗/建 DIB

        if (_hBitmap != IntPtr.Zero) { NativeMethods.DeleteObject(_hBitmap); _hBitmap = IntPtr.Zero; }
        if (_hdcMem != IntPtr.Zero) { NativeMethods.DeleteDC(_hdcMem); _hdcMem = IntPtr.Zero; }
        CreateDib(w, h);

        NativeMethods.GetWindowRect(_hwnd, out var r);
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, r.left, r.top, w, h, NativeMethods.SWP_NOZORDER);
    }

    /// <summary>窗口当前的屏幕矩形（用于把全局光标映射到宠物本地坐标，Phase 2 鼠标跟随）。</summary>
    public NativeMethods.RECT Bounds
    {
        get
        {
            if (_hwnd == IntPtr.Zero)
                return new NativeMethods.RECT { left = 0, top = 0, right = _width, bottom = _height };
            NativeMethods.GetWindowRect(_hwnd, out var r);
            return r;
        }
    }

    /// <summary>每帧推送渲染结果（BGRA、自上而下、预乘 Alpha）。通常由渲染线程调用。</summary>
    public void PushFrame(byte[] pixels, int w, int h)
    {
        if (_hwnd == IntPtr.Zero || _hBitmap == IntPtr.Zero) return;
        if (pixels.Length != w * h * 4) return;

        Marshal.Copy(pixels, 0, _ppvBits, pixels.Length);

        UpdateLayered();
    }

    /// <summary>用当前 DIB 内容 + 当前 _opacity 重新合成一次分层窗口。
    /// 供 PushFrame 每帧调用，也供 SetHidden 在切换隐藏状态时调用——后者是为了让不透明度的变化
    /// 立刻生效，否则在渲染循环被暂停（隐藏时）会出现"旧帧卡在屏幕上"的假象。</summary>
    private void UpdateLayered()
    {
        if (_hwnd == IntPtr.Zero || _hBitmap == IntPtr.Zero || _hdcMem == IntPtr.Zero) return;

        NativeMethods.GetWindowRect(_hwnd, out var rect);
        var ppt = new NativeMethods.POINT { X = rect.left, Y = rect.top };
        var psize = new NativeMethods.SIZE { cx = _width, cy = _height };
        var pptSrc = new NativeMethods.POINT { X = 0, Y = 0 };
        var blend = new NativeMethods.BLENDFUNCTION
        {
            BlendOp = NativeMethods.AC_SRC_OVER,
            SourceConstantAlpha = (byte)(Math.Clamp(_opacity, 0f, 1f) * 255f),
            AlphaFormat = NativeMethods.AC_SRC_PREMULT
        };
        NativeMethods.UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref ppt, ref psize, _hdcMem, ref pptSrc, 0, ref blend, NativeMethods.ULW_ALPHA);
    }

    private void ThreadProc()
    {
        _wndProc = WndProcImpl;

        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW | NativeMethods.CS_DBLCLKS,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = NativeMethods.GetModuleHandle(null),
            lpszClassName = _className,
            hbrBackground = IntPtr.Zero,
            hCursor = IntPtr.Zero
        };
        NativeMethods.RegisterClassEx(ref wc);

        // 使用虚拟屏幕（覆盖所有显示器）放置，多屏下也能正确落位/找回
        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        int x = _initialX >= 0 ? Math.Clamp(_initialX, vx, Math.Max(vx, vx + vw - 80)) : Math.Max(vx, vx + vw - _width - 20);
        int y = _initialY >= 0 ? Math.Clamp(_initialY, vy, Math.Max(vy, vy + vh - 80)) : Math.Max(vy, vy + vh - _height - 20);

        int exStyle = NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        if (_clickThrough) exStyle |= NativeMethods.WS_EX_TRANSPARENT;

        _hwnd = NativeMethods.CreateWindowEx(
            exStyle,
            _className, null,
            NativeMethods.WS_POPUP | NativeMethods.WS_VISIBLE,
            x, y, _width, _height,
            IntPtr.Zero, IntPtr.Zero, NativeMethods.GetModuleHandle(null), IntPtr.Zero);

        CreateDib(_width, _height);

        while (_running && NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }
    }

    private void CreateDib(int w, int h)
    {
        IntPtr hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
        _hdcMem = NativeMethods.CreateCompatibleDC(hdcScreen);
        NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);

        var bmi = new NativeMethods.BITMAPINFO
        {
            bmiColors = new uint[3]
        };
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = w;
        bmi.bmiHeader.biHeight = -h; // top-down
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = NativeMethods.BI_RGB;
        bmi.bmiHeader.biSizeImage = (uint)(w * h * 4);

        _hBitmap = NativeMethods.CreateDIBSection(_hdcMem, ref bmi, NativeMethods.DIB_RGB_COLORS, out _ppvBits, IntPtr.Zero, 0);
        NativeMethods.SelectObject(_hdcMem, _hBitmap);
    }

    private IntPtr WndProcImpl(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_NCHITTEST:
            {
                int sx = (short)(lParam.ToInt32() & 0xFFFF);
                int sy = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                NativeMethods.GetWindowRect(hWnd, out var r);
                int lx = sx - r.left;
                int ly = sy - r.top;
                // 顶部把手区：始终可拖动（交给系统处理）
                if (ly <= _handleHeight) return (IntPtr)NativeMethods.HTCAPTION;
                // 非穿透模式下，仅命中角色可见像素才视为可交互（否则穿透到下层窗口）
                if (!_clickThrough && HitTestAlpha(lx, ly)) return (IntPtr)NativeMethods.HTCLIENT;
                return (IntPtr)NativeMethods.HTTRANSPARENT;
            }
            case NativeMethods.WM_LBUTTONDOWN:
            {
                StopInertia();   // 若惯性滑动中又按下，立即取消
                int ly = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                _pressing = true;
                if (ly <= _handleHeight)
                {
                    _handlePress = true;        // 把手区：仅拖动，不触发反应
                }
                else
                {
                    _handlePress = false;
                    _downX = (short)(lParam.ToInt32() & 0xFFFF);
                    _downY = ly;
                    NativeMethods.GetCursorPos(out var cp);
                    _dragCursorX = cp.X; _dragCursorY = cp.Y;
                    NativeMethods.GetWindowRect(hWnd, out var r);
                    _dragWinX = r.left; _dragWinY = r.top;
                    // 初始化速度采样（光标屏坐标 + 高频计时）
                    _sampleX = cp.X; _sampleY = cp.Y; _sampleT = Stopwatch.GetTimestamp();
                    _inertiaVx = 0; _inertiaVy = 0;
                }
                return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
            }
            case NativeMethods.WM_MOUSEMOVE:
            {
                if ((wParam.ToInt32() & MK_LBUTTON) != 0 && _pressing && !_handlePress)
                {
                    int lx = (short)(lParam.ToInt32() & 0xFFFF);
                    int ly = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                    if (!_dragging)
                    {
                        int dx = lx - _downX, dy = ly - _downY;
                        if (dx * dx + dy * dy > DragThresholdSq)
                        {
                            _dragging = true;
                            Undock();                 // 用户主动拖拽：取消贴边（半隐藏状态解除）
                            PetDragged?.Invoke();   // 进入拖拽 → Flick 反应
                        }
                    }
                    if (_dragging && _draggable)
                    {
                        // 手动跟随光标移动窗口（身体区是 HTCLIENT，不会自动拖拽）
                        NativeMethods.GetCursorPos(out var cp);
                        int nx = _dragWinX + (cp.X - _dragCursorX);
                        int ny = _dragWinY + (cp.Y - _dragCursorY);
                        ClampToScreen(ref nx, ref ny);   // 拖出屏幕时 clamp 回屏内，把手始终可够到
                        NativeMethods.SetWindowPos(hWnd, IntPtr.Zero, nx, ny, 0, 0,
                            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER);

                        // 采样光标速度（供松手后的惯性滑动）
                        long now = Stopwatch.GetTimestamp();
                        if (_sampleT != 0)
                        {
                            double dt = (now - _sampleT) / (double)Stopwatch.Frequency;
                            if (dt > 0.0008)
                            {
                                double ivx = (cp.X - _sampleX) / dt;
                                double ivy = (cp.Y - _sampleY) / dt;
                                _inertiaVx = (int)(_inertiaVx * 0.6 + ivx * 0.4);
                                _inertiaVy = (int)(_inertiaVy * 0.6 + ivy * 0.4);
                                _sampleX = cp.X; _sampleY = cp.Y; _sampleT = now;
                            }
                        }
                    }
                }
                return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
            }
            case NativeMethods.WM_LBUTTONUP:
            {
                if (_pressing && !_handlePress && !_dragging)
                    PetClicked?.Invoke(GetHitRegion(_downX, _downY));   // 单击 → 按部位分区反应
                else if (_dragging)
                {
                    PetMoved?.Invoke();     // 拖拽结束 → 保存新位置
                    double speed = Math.Sqrt(_inertiaVx * _inertiaVx + _inertiaVy * _inertiaVy);
                    if (speed > 80) StartInertia();   // 松手仍有速度 → 继续惯性滑动
                }
                _pressing = false; _dragging = false; _handlePress = false;
                return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
            }
            case NativeMethods.WM_LBUTTONDBLCLK:
            {
                int ly = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                if (ly > _handleHeight)
                    PetDoubleClicked?.Invoke();   // 身体区双击 → Tap@Body 反应
                return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
            }
            case NativeMethods.WM_RBUTTONUP:
            {
                NativeMethods.GetCursorPos(out var cp);
                PetRightClicked?.Invoke(cp.X, cp.Y);   // 右键 → 弹菜单（业务层 marshal 到 UI 线程）
                return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
            }
            case NativeMethods.WM_NCLBUTTONDOWN:
                // HTCAPTION 已由 WM_NCHITTEST 返回，交给系统开始拖动
                return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
            case NativeMethods.WM_TIMER:
            {
                if ((int)wParam == InertiaTimerId) { StepInertia(); return IntPtr.Zero; }
                return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
            }
            case NativeMethods.WM_DESTROY:
                NativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;
            default:
                return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    /// <summary>像素级命中：读取当前帧 DIB 的 alpha，判断 (lx,ly) 是否落在角色可见像素上。</summary>
    private bool HitTestAlpha(int lx, int ly)
    {
        if (_ppvBits == IntPtr.Zero) return false;
        if (lx < 0 || ly < 0 || lx >= _width || ly >= _height) return false;
        int offset = (ly * _width + lx) * 4 + 3; // BGRA 末位为 alpha
        return Marshal.ReadByte(_ppvBits, offset) > _alphaThreshold;
    }

    /// <summary>把窗口位置 clamp 回虚拟屏幕内（覆盖多显示器）：顶部把手区始终留在可见范围。</summary>
    private void ClampToScreen(ref int x, ref int y)
    {
        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        int margin = _handleHeight;   // 至少保留把手宽度在屏内

        // 水平：窗口可部分出屏，但至少留 margin 像素可抓
        int minX = vx + margin - _width, maxX = vx + vw - margin;
        if (minX > maxX) minX = maxX = (minX + maxX) / 2;
        x = Math.Clamp(x, minX, maxX);

        // 垂直：顶部（含把手）始终在屏内，底部可出屏
        int minY = vy, maxY = Math.Max(vy, vy + vh - margin);
        y = Math.Clamp(y, minY, maxY);
    }

    /// <summary>拖拽结束后调用：若窗口靠近屏幕边缘则吸附贴边（记录贴边方向）。</summary>
    public void SnapToEdge()
    {
        if (_hwnd == IntPtr.Zero) return;
        NativeMethods.GetWindowRect(_hwnd, out var r);
        int w = r.right - r.left, h = r.bottom - r.top;
        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        int x = r.left, y = r.top;
        DockEdge edge = DockEdge.None;
        if (r.left - vx < DockThreshold) { x = vx; edge = DockEdge.Left; }
        else if (vx + vw - r.right < DockThreshold) { x = vx + vw - w; edge = DockEdge.Right; }
        else if (r.top - vy < DockThreshold) { y = vy; edge = DockEdge.Top; }
        else if (vy + vh - r.bottom < DockThreshold) { y = vy + vh - h; edge = DockEdge.Bottom; }

        _docked = edge;
        if (edge != DockEdge.None)
        {
            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER);
        }
    }

    /// <summary>取消贴边（用户主动拖拽时调用）。</summary>
    public void Undock() => _docked = DockEdge.None;

    /// <summary>返回角色实际绘制区域在屏幕坐标系下的中心点（基于帧 alpha 扫描）。
    /// 用于让气泡等附属 UI 对齐到角色而非画布几何中心——角色通常不在画布正中。</summary>
    public (int X, int Y) GetContentCenter()
    {
        if (_hwnd == IntPtr.Zero) return (0, 0);
        EnsureContentBounds();
        int w = Math.Max(1, _width), h = Math.Max(1, _height);
        int cx = Math.Clamp((_contentMinX + _contentMaxX) / 2, 0, w - 1);
        int cy = Math.Clamp((_contentMinY + _contentMaxY) / 2, 0, h - 1);
        NativeMethods.GetWindowRect(_hwnd, out var r);
        return (r.left + cx, r.top + cy);
    }

    /// <summary>返回角色"头顶"在屏幕坐标系下的位置（基于当前帧 alpha 实时扫描，跟随动画）。
    /// 用于气泡三角指向角色头部，而非画布顶端。X 取角色包围盒水平中心，Y 取上边界。</summary>
    public (int X, int Y) GetContentHeadTop()
    {
        if (_hwnd == IntPtr.Zero) return (0, 0);
        EnsureContentBounds(force: true);
        int w = Math.Max(1, _width), h = Math.Max(1, _height);
        int cx = Math.Clamp((_contentMinX + _contentMaxX) / 2, 0, w - 1);
        int top = Math.Clamp(_contentMinY, 0, h - 1);
        NativeMethods.GetWindowRect(_hwnd, out var r);
        return (r.left + cx, r.top + top);
    }

    /// <summary>使角色包围盒缓存失效（切换模型后调用），下次访问会重新扫描当前帧 alpha，
    /// 避免沿用旧模型的位置导致气泡/贴边错位。</summary>
    public void InvalidateContentBounds()
    {
        _contentMinX = -1;
        _contentScannedW = 0;
        _contentScannedH = 0;
    }

    /// <summary>判断单击落在角色哪个部位：按角色实际包围盒垂直切分（头/身/脚）。
    /// 用于摸头 vs 戳肚子 vs 挠脚的不同反应，角色在画布任何位置都成立。</summary>
    public HitRegion GetHitRegion(int lx, int ly)
    {
        EnsureContentBounds();
        if (_contentMinX < 0) return HitRegion.Body;
        int ch = Math.Max(1, _contentMaxY - _contentMinY);
        double t = (ly - _contentMinY) / (double)ch;
        if (t < 0.33) return HitRegion.Head;
        if (t < 0.78) return HitRegion.Body;
        return HitRegion.Feet;
    }

    /// <summary>一键隐藏/显示桌宠（全局快捷键）：隐藏时透明度归零 + 鼠标穿透（不挡操作），
    /// 显示时还原不透明度并恢复设置里的穿透状态。</summary>
    public void SetHidden(bool hidden)
    {
        _hidden = hidden;
        if (_hwnd == IntPtr.Zero) return;
        if (hidden)
        {
            _savedOpacity = _opacity;
            _opacity = 0f;
            int ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE) | NativeMethods.WS_EX_TRANSPARENT;
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);
            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);
        }
        else
        {
            _opacity = _savedOpacity;
            int ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
            if (_clickThrough) ex |= NativeMethods.WS_EX_TRANSPARENT;
            else ex &= ~NativeMethods.WS_EX_TRANSPARENT;
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);
            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);
        }
        // 关键：用新透明度立即重画一次分层窗口。隐藏时让不透明度 0 立刻生效（避免渲染循环暂停后
        // 旧帧以旧透明度卡在屏幕上的"假隐藏"），显示时也无需等下一帧 Tick 即可见。
        UpdateLayered();
    }

    public bool IsHidden => _hidden;

    // ---- 拖拽惯性 ----
    private void StartInertia()
    {
        if (_inertiaRunning || _hwnd == IntPtr.Zero || !_draggable) return;
        _inertiaRunning = true;
        _inertiaLastTick = Stopwatch.GetTimestamp();
        NativeMethods.SetTimer(_hwnd, (IntPtr)InertiaTimerId, (uint)InertiaIntervalMs, IntPtr.Zero);
    }

    private void StopInertia()
    {
        if (!_inertiaRunning) return;
        _inertiaRunning = false;
        if (_hwnd != IntPtr.Zero) NativeMethods.KillTimer(_hwnd, (IntPtr)InertiaTimerId);
    }

    /// <summary>每帧（约 16ms）按当前速度平移窗口，撞屏边回弹，摩擦衰减，速度足够小则停并保存位置。</summary>
    private void StepInertia()
    {
        if (_hwnd == IntPtr.Zero) { StopInertia(); return; }
        double dt = InertiaIntervalMs / 1000.0;
        _inertiaLastTick = Stopwatch.GetTimestamp();

        NativeMethods.GetWindowRect(_hwnd, out var r);
        int tx = r.left + (int)(_inertiaVx * dt);
        int ty = r.top + (int)(_inertiaVy * dt);

        int cx = tx, cy = ty;
        ClampToScreen(ref cx, ref cy);
        if (cx != tx) _inertiaVx = (int)(-_inertiaVx * 0.4);   // 撞左右边回弹
        if (cy != ty) _inertiaVy = (int)(-_inertiaVy * 0.4);   // 撞上下边回弹

        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, cx, cy, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER);

        _inertiaVx = (int)(_inertiaVx * 0.86);
        _inertiaVy = (int)(_inertiaVy * 0.86);

        double speed = Math.Sqrt(_inertiaVx * _inertiaVx + _inertiaVy * _inertiaVy);
        if (speed < 12)
        {
            StopInertia();
            PetMoved?.Invoke();   // 惯性结束，保存最终位置（含贴边吸附）
        }
    }

    /// <summary>半隐藏切换：peek=true 完整显示，false 缩到只剩一角。仅在贴边时有效。
    /// 露出的是角色的「朝屏内一侧」像素，按角色实际绘制区域计算，确保人物始终可见。</summary>
    public void SetPeek(bool peek)
    {
        if (_hwnd == IntPtr.Zero || _docked == DockEdge.None) return;
        if (_peeked == peek) return;
        _peeked = peek;

        NativeMethods.GetWindowRect(_hwnd, out var r);
        int w = r.right - r.left, h = r.bottom - r.top;
        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        EnsureContentBounds();
        int peekX = Math.Min(PeekVisible, w);
        int peekY = Math.Min(PeekVisible, h);
        int cx0 = Math.Clamp(_contentMinX, 0, w - 1);
        int cx1 = Math.Clamp(_contentMaxX, 0, w - 1);
        int cy0 = Math.Clamp(_contentMinY, 0, h - 1);
        int cy1 = Math.Clamp(_contentMaxY, 0, h - 1);

        int x = r.left, y = r.top;
        switch (_docked)
        {
            // 完全显示：整窗贴边；半隐藏：露出角色朝屏内的那一端
            case DockEdge.Left:   x = peek ? vx            : vx - cx1 + peekX - 1; break;
            case DockEdge.Right:  x = peek ? vx + vw - w   : vx + vw - peekX - cx0; break;
            case DockEdge.Top:    y = peek ? vy            : vy - cy1 + peekY - 1; break;
            case DockEdge.Bottom: y = peek ? vy + vh - h   : vy + vh - peekY - cy0; break;
        }
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER);
    }

    /// <summary>扫描当前帧 alpha，求出角色实际绘制区域的水平/垂直边界（容差内才算角色本体）。
    /// 结果按画布尺寸缓存，仅在尺寸变化或首次时重新扫描，避免每帧开销。
    /// force=true 时忽略缓存（用于气泡等需要跟随动画当前位置的场景）。</summary>
    private void EnsureContentBounds(bool force = false)
    {
        if (!force && _contentMinX >= 0 && _contentScannedW == _width && _contentScannedH == _height)
            return;
        if (_ppvBits == IntPtr.Zero)
        {
            _contentMinX = 0; _contentMaxX = _width - 1;
            _contentMinY = 0; _contentMaxY = _height - 1;
            _contentScannedW = _width; _contentScannedH = _height;
            return;
        }

        int minX = _width, maxX = -1, minY = _height, maxY = -1;
        int stride = _width * 4;
        for (int ry = 0; ry < _height; ry++)
        {
            long rowBase = (long)ry * stride;
            for (int rx = 0; rx < _width; rx++)
            {
                byte a = Marshal.ReadByte(_ppvBits, (int)(rowBase + rx * 4 + 3));
                if (a > _alphaThreshold)
                {
                    if (rx < minX) minX = rx;
                    if (rx > maxX) maxX = rx;
                    if (ry < minY) minY = ry;
                    if (ry > maxY) maxY = ry;
                }
            }
        }
        if (maxX < 0) // 当前帧全透明（罕见），退回整窗，避免误判
        {
            minX = 0; maxX = _width - 1; minY = 0; maxY = _height - 1;
        }
        _contentMinX = minX; _contentMaxX = maxX;
        _contentMinY = minY; _contentMaxY = maxY;
        _contentScannedW = _width; _contentScannedH = _height;
    }
}
