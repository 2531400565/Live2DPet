using System.Runtime.InteropServices;

namespace Live2DPet.Platform.Native;

/// <summary>
/// 桌面窗口所需的 Win32 API 封装。
/// 仅暴露本项目实际用到的少量函数，避免猜测/滥用 API。
/// </summary>
public static class NativeMethods
{
    // 窗口样式
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_VISIBLE = 0x10000000;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);

    // 扩展窗口样式索引
    public const int GWL_EXSTYLE = -20;
    public const int GWL_WNDPROC = -4;

    // 扩展窗口样式位
    public const int WS_EX_TRANSPARENT = 0x00000020;  // 鼠标穿透
    public const int WS_EX_TOPMOST = 0x00000008;      // 置顶
    public const int WS_EX_LAYERED = 0x00080000;       // 分层（透明关键）
    public const int WS_EX_TOOLWINDOW = 0x00000080;   // 不在任务栏显示
    public const int WS_EX_NOACTIVATE = 0x08000000;   // 不抢焦点

    // SetWindowPos 标志
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;

    public const int CW_USEDEFAULT_int = unchecked((int)0x80000000);

    // 特殊窗口句柄
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    // 窗口消息
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_DESTROY = 0x0002;
    public const int WM_CLOSE = 0x0010;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_LBUTTONDBLCLK = 0x0203;   // 双击（需窗口类带 CS_DBLCLKS）
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_SYSCOMMAND = 0x0112;
    public const int WM_NCLBUTTONDOWN = 0x00A1;

    // WM_NCHITTEST / 系统命令
    public const int HTTRANSPARENT = -1; // 鼠标穿透到下层窗口
    public const int HTCLIENT = 1;       // 客户区（接收鼠标消息，用于点击/拖拽交互）
    public const int HTCAPTION = 2;      // 当作标题栏（可拖动）
    public const int SC_MOVE = 0xF010;

    // UpdateLayeredWindow
    public const uint ULW_ALPHA = 0x00000002;
    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_PREMULT = 0x01;

    // 类样式
    public const int CS_HREDRAW = 0x0002;
    public const int CS_VREDRAW = 0x0001;
    public const int CS_DBLCLKS = 0x0008;

    // DIB
    public const int BI_RGB = 0;
    public const uint DIB_RGB_COLORS = 0;

    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;
    // 虚拟屏幕（覆盖所有显示器）：用于多屏下正确放置与 clamp，避免只盯着主屏
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;

    // ---- 结构体 ----
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public int style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPStr)] public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public int message;
        public IntPtr wParam;
        public IntPtr lParam;
        public int time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight; // 负数表示自上而下（top-down）
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public uint[] bmiColors; // 32bpp 时仅占位
    }

    // ---- 委托 ----
    public delegate IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // ---- 函数 ----
    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string? lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, int wMsgFilterMin, int wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref POINT ppt, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    // ---- 前台窗口 + 显示器信息（检测全屏/游戏，避免键盘回应打扰用户）----
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;   // 显示器完整区域
        public RECT rcWork;      // 工作区（去掉任务栏）
        public int dwFlags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    // 窗口样式位：有标题栏（非全屏）判断用
    public const int WS_CAPTION = 0x00C00000;

    // GDI
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateDIBSection(
        IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    // ---- 低级键盘钩子（全局按键探测，不记录内容）----
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYUP = 0x0105;
    public const int WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public int vkCode;       // 虚拟键码
        public int scanCode;     // 硬件扫描码
        public int flags;        // LLKHF_* 位：bit0=扩展键, bit4=注入事件, bit5=Alt 按下。低位键盘钩子里没有"自动重复"位，需用按键状态集合自行跟踪
        public int time;
        public IntPtr dwExtraInfo;
    }

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostThreadMessage(int idThread, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern int GetCurrentThreadId();

    // ---- 低级鼠标钩子（让右键菜单能跨线程检测外部点击并关闭）----
    public const int WH_MOUSE_LL = 14;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_NCRBUTTONDOWN = 0x00A4;
    public const int WM_NCMBUTTONDOWN = 0x00A7;

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static extern IntPtr SetMouseHook(LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    // ---- 全局快捷键（一键隐藏/显示桌宠）----
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_NOREPEAT = 0x4000;
    public const int VK_OEM_3 = 0xC0;   // ` / ~ （主键盘左上角）
    public const int VK_SPACE = 0x20;   // 空格
    public const int VK_H = 0x48;       // H 键
    public const int VK_F8 = 0x77;      // F8

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ---- 用户空闲检测（用于"打盹"待机）：取系统上次输入（键鼠）时间 ----
    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;   // 上次输入时的 tick（毫秒，GetTickCount 体系）
    }

    [DllImport("user32.dll")]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    // ---- 定时器（用于拖拽惯性滑动，原生消息循环线程用 Win32 定时器驱动动画）----
    public const int WM_TIMER = 0x0113;
    public const uint WM_TIMER_KILL = 0;

    [DllImport("user32.dll")]
    public static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerProc);

    [DllImport("user32.dll")]
    public static extern bool KillTimer(IntPtr hWnd, IntPtr nIDEvent);
}
