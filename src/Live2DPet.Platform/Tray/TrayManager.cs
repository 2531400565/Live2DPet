using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Live2DPet.Platform.Native;

namespace Live2DPet.Platform.Tray;

/// <summary>
/// 系统托盘管理器（WinForms NotifyIcon + ContextMenuStrip）。
/// 提供显示/设置/退出/鼠标穿透/键盘互动/开机自启/表情选择等菜单项，
/// 并允许在任意位置弹出同一份菜单（供宠物右键菜单复用）。
///
/// 修复：桌宠窗口运行在独立的消息循环线程（PetWindow 线程），右键菜单
/// （ContextMenuStrip）默认靠 UI 线程的 IMessageFilter 检测"外部点击"来自动关闭，
/// 但该过滤器只看 UI 线程上的消息——宠物线程上的点击它根本看不到，导致菜单
/// "点哪里都关不掉"。解决办法：菜单每次显示时挂一个全局低级鼠标钩子（WH_MOUSE_LL），
/// 钩子回调跑在 UI 线程，能收到所有鼠标事件（无论来自哪个线程/窗口），
/// 一旦发现点击点不在菜单矩形内就立即关闭菜单。Closed 事件再摘钩。
/// </summary>
public sealed class TrayManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly Icon _icon;
    private readonly ToolStripMenuItem _clickThroughItem;
    private readonly ToolStripMenuItem _keyboardItem;
    private readonly ToolStripMenuItem _gazeItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _expressionItem;
    private readonly ToolStripMenuItem _hideItem;
    private bool _disposed;

    // 全局低级鼠标钩子（仅在菜单可见期间安装/卸载，菜单关闭即摘钩）
    private readonly NativeMethods.LowLevelMouseProc _mouseProc;  // 必须持有引用防止被 GC
    private IntPtr _mouseHookId = IntPtr.Zero;

    public event EventHandler? ShowPetRequested;  // 强制显示完整桌宠（取消贴边+滑出）
    public event EventHandler? ToggleHideRequested;  // 一键隐藏/显示（Ctrl+`）
    public event EventHandler? SettingsRequested;
    public event EventHandler? StatusRequested;
    public event EventHandler? ScreenshotRequested;
    public event EventHandler? AboutRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? ToggleClickThroughRequested;
    public event EventHandler? ToggleKeyboardInteractionRequested;
    public event EventHandler? ToggleGazeRequested;
    public event EventHandler? ToggleAutoStartRequested;
    public event EventHandler<string>? ExpressionSelected;
    public event EventHandler? OpenLogsRequested;   // 打开日志目录（排查用）

    public TrayManager()
    {
        _mouseProc = MouseHookCallback;

        _icon = LoadIcon();
        _notifyIcon = new NotifyIcon { Icon = _icon, Text = "Live2D 桌宠", Visible = true };

        _menu = new ContextMenuStrip();
        _menu.Items.Add(new ToolStripMenuItem("显示桌宠", null, (_, _) => ShowPetRequested?.Invoke(this, EventArgs.Empty)));
        _hideItem = new ToolStripMenuItem("隐藏桌宠 (Ctrl+`)", null, (_, _) => ToggleHideRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(_hideItem);
        _menu.Items.Add(new ToolStripMenuItem("设置...", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(new ToolStripMenuItem("养成面板...", null, (_, _) => StatusRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(new ToolStripMenuItem("截图桌宠", null, (_, _) => ScreenshotRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("关于 Live2DPet...", null, (_, _) => AboutRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(new ToolStripMenuItem("查看日志...", null, (_, _) => OpenLogsRequested?.Invoke(this, EventArgs.Empty)));

        _expressionItem = new ToolStripMenuItem("切换表情");
        _menu.Items.Add(_expressionItem);

        _clickThroughItem = new ToolStripMenuItem("鼠标穿透（关=可点击宠物）", null, (_, _) => ToggleClickThroughRequested?.Invoke(this, EventArgs.Empty))
        {
            CheckOnClick = true,
            Checked = false
        };
        _keyboardItem = new ToolStripMenuItem("键盘互动", null, (_, _) => ToggleKeyboardInteractionRequested?.Invoke(this, EventArgs.Empty))
        {
            CheckOnClick = true,
            Checked = true
        };
        _gazeItem = new ToolStripMenuItem("眼神跟随鼠标", null, (_, _) => ToggleGazeRequested?.Invoke(this, EventArgs.Empty))
        {
            CheckOnClick = true,
            Checked = true
        };
        _autoStartItem = new ToolStripMenuItem("开机自启", null, (_, _) => ToggleAutoStartRequested?.Invoke(this, EventArgs.Empty))
        {
            CheckOnClick = true,
            Checked = false
        };
        _menu.Items.Add(_clickThroughItem);
        _menu.Items.Add(_keyboardItem);
        _menu.Items.Add(_gazeItem);
        _menu.Items.Add(_autoStartItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

        _notifyIcon.ContextMenuStrip = _menu;
        _notifyIcon.DoubleClick += (_, _) => ShowPetRequested?.Invoke(this, EventArgs.Empty);

        // 菜单每次显示（无论托盘右键还是宠物右键触发）→ 挂低级鼠标钩子；
        // 关闭 → 摘钩。覆盖两条入口，统一解决"关不掉"问题。
        _menu.Opened += (_, _) => InstallMouseHook();
        _menu.Closed += (_, _) => UninstallMouseHook();
    }

    /// <summary>从当前 exe 提取图标（单文件发布也会嵌入 exe 图标），失败回退系统图标。</summary>
    private static Icon LoadIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return Icon.ExtractAssociatedIcon(path) ?? SystemIcons.Application;
        }
        catch
        {
            // 回退
        }
        return SystemIcons.Application;
    }

    /// <summary>在屏幕坐标 (screenX, screenY) 弹出菜单（宠物右键复用）。
    /// 若菜单已可见则直接关闭它（再次右键宠物 = 取消菜单）。</summary>
    public void ShowMenuAt(int screenX, int screenY)
    {
        if (_disposed) return;
        if (_menu.Visible)
        {
            _menu.Close();   // 关闭由 Closed 事件摘钩
            return;
        }
        // 提前挂钩：避免 Show 与 Opened 事件之间的微小窗口漏掉第一次外部点击
        InstallMouseHook();
        _menu.Show(screenX, screenY);
    }

    /// <summary>安装低级鼠标钩子（全局，挂到当前 UI 线程的消息泵上）。</summary>
    private void InstallMouseHook()
    {
        if (_disposed || _mouseHookId != IntPtr.Zero) return;
        _mouseHookId = NativeMethods.SetMouseHook(
            _mouseProc, NativeMethods.GetModuleHandle(null), 0);
    }

    /// <summary>卸载低级鼠标钩子。</summary>
    private void UninstallMouseHook()
    {
        if (_mouseHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }
    }

    /// <summary>低级鼠标钩子回调：检测到任意鼠标按钮在菜单矩形之外按下 → 关闭菜单。</summary>
    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _menu.Visible)
        {
            int msg = (int)wParam;
            if (msg == NativeMethods.WM_LBUTTONDOWN
                || msg == NativeMethods.WM_RBUTTONDOWN
                || msg == NativeMethods.WM_MBUTTONDOWN
                || msg == NativeMethods.WM_NCLBUTTONDOWN
                || msg == NativeMethods.WM_NCRBUTTONDOWN
                || msg == NativeMethods.WM_NCMBUTTONDOWN)
            {
                var hs = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                if (NativeMethods.GetWindowRect(_menu.Handle, out var r))
                {
                    if (hs.pt.X < r.left || hs.pt.X > r.right
                        || hs.pt.Y < r.top  || hs.pt.Y > r.bottom)
                    {
                        try { _menu.Close(); } catch { /* 关闭异常忽略，避免钩子崩溃 */ }
                    }
                }
            }
        }
        return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    /// <summary>更新表情子菜单（无表情时禁用）。currentId 为空表示"默认脸"。</summary>
    public void SetExpressions(IReadOnlyList<string> expressions, string currentId)
    {
        if (_disposed) return;
        _expressionItem.DropDownItems.Clear();

        if (expressions.Count == 0)
        {
            _expressionItem.DropDownItems.Add(new ToolStripMenuItem("(无可用表情)") { Enabled = false });
            _expressionItem.Enabled = false;
            return;
        }

        _expressionItem.Enabled = true;
        var none = new ToolStripMenuItem("(默认)") { Checked = string.IsNullOrEmpty(currentId) };
        none.Click += (_, _) => ExpressionSelected?.Invoke(this, "");
        _expressionItem.DropDownItems.Add(none);

        foreach (var exp in expressions)
        {
            var item = new ToolStripMenuItem(exp) { Checked = exp == currentId };
            item.Click += (_, _) => ExpressionSelected?.Invoke(this, exp);
            _expressionItem.DropDownItems.Add(item);
        }
    }

    public void SetTooltip(string text) { if (!_disposed) _notifyIcon.Text = text; }

    /// <summary>弹出托盘气泡提示（如被另一个实例唤醒时）。</summary>
    public void ShowBalloon(string title, string text)
    {
        if (_disposed) return;
        try
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.ShowBalloonTip(3000);
        }
        catch { /* 气泡是锦上添花，失败不影响主功能 */ }
    }

    public void SetClickThroughChecked(bool v) { if (!_disposed) _clickThroughItem.Checked = v; }

    public void SetKeyboardInteractionChecked(bool v) { if (!_disposed) _keyboardItem.Checked = v; }

    public void SetGazeChecked(bool v) { if (!_disposed) _gazeItem.Checked = v; }

    public void SetAutoStartChecked(bool v) { if (!_disposed) _autoStartItem.Checked = v; }

    /// <summary>根据隐藏状态切换菜单项文案（隐藏时显示"显示桌宠"，否则"隐藏桌宠"）。</summary>
    public void SetHiddenLabel(bool hidden)
    {
        if (_disposed) return;
        _hideItem.Text = hidden ? "显示桌宠 (Ctrl+`)" : "隐藏桌宠 (Ctrl+`)";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UninstallMouseHook();
        _notifyIcon.Visible = false;
        _menu.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}