using System;
using System.Runtime.InteropServices;
using Live2DPet.Platform.Native;

namespace Live2DPet.Platform.Input;

/// <summary>按键被按下（含 VK 码与是否自动重复）的事件参数。</summary>
public sealed class KeyActedEventArgs : EventArgs
{
    public int VirtualKey { get; }
    public bool IsAutoRepeat { get; }

    public KeyActedEventArgs(int vk, bool isAutoRepeat)
    {
        VirtualKey = vk;
        IsAutoRepeat = isAutoRepeat;
    }
}

/// <summary>
/// 全局低级键盘钩子（WH_KEYBOARD_LL）。
///
/// 设计要点：
/// - 只"探测"按键事件（VK 码），**绝不读取/记录按键内容**，不构成 keylogging，合规且无隐私风险。
/// - WH_KEYBOARD_LL 的回调由安装它的那个线程的消息队列投递，因此必须在一个带消息泵的线程上运行。
///   这里用独立后台线程跑 GetMessage 循环；钩子回调在该线程触发，通过 KeyActed 事件抛给业务层。
/// - 业务层（App）负责把事件 marshal 回主线程再驱动模型，避免在钩子线程直接碰引擎状态。
/// </summary>
public sealed class KeyboardMonitor : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _proc;   // 必须持有引用防止被 GC
    private IntPtr _hookId = IntPtr.Zero;
    private readonly HashSet<int> _downKeys = new HashSet<int>();   // 当前按下的 vkCode 集合（单线程：仅钩子线程访问）
    private Thread? _thread;
    private int _threadId;
    private volatile bool _running;
    private bool _disposed;

    /// <summary>任意按键被按下时触发（在钩子线程上，业务层需自行 marshal 回主线程）。</summary>
    public event EventHandler<KeyActedEventArgs>? KeyActed;

    public KeyboardMonitor()
    {
        _proc = HookCallback;   // 根引用，避免回调被回收导致 AccessViolation
    }

    /// <summary>安装钩子并启动消息泵线程。可重入（已启动则忽略）。</summary>
    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "KeyboardHook" };
        _thread.Start();
    }

    private void MessageLoop()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _proc,
            NativeMethods.GetModuleHandle(null), 0);

        if (_hookId == IntPtr.Zero)
        {
            // 安装失败（罕见：权限/被策略禁止）。静默退出，不阻塞主程序。
            _running = false;
            return;
        }

        NativeMethods.MSG msg;
        while (_running && NativeMethods.GetMessage(out msg, IntPtr.Zero, 0, 0))
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            var ks = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                // WH_KEYBOARD_LL 的 KBDLLHOOKSTRUCT.flags 不含"先前键状态"位，无法靠位判断自动重复。
                // 必须自行跟踪按键状态：同一 vkCode 在收到 WM_KEYUP 前再次收到 WM_KEYDOWN 即视为自动重复，丢弃。
                bool isRepeat = !_downKeys.Add(ks.vkCode);
                if (!isRepeat)
                    KeyActed?.Invoke(this, new KeyActedEventArgs(ks.vkCode, isRepeat));
            }
            else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
            {
                _downKeys.Remove(ks.vkCode);
            }
        }
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running = false;

        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        // 唤醒阻塞在 GetMessage 的线程，使其退出循环
        if (_threadId != 0)
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread = null;
    }
}
