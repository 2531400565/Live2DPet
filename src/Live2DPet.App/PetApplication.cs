using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Live2DPet.Core;
using Live2DPet.Core.Interaction;
using Live2DPet.Core.Live2D;
using Live2DPet.Core.Models;
using Live2DPet.Core.Mouse;
using Live2DPet.Core.Pet;
using Live2DPet.Core.Settings;
using Live2DPet.Core.Update;
using Live2DPet.App.Update;
using Live2DPet.Platform;
using Live2DPet.Platform.Input;
using Live2DPet.Platform.Native;
using Live2DPet.Platform.Tray;
using Live2DPet.Platform.Window;
using Live2DPet.Rendering;

namespace Live2DPet.App;

/// <summary>
/// 桌宠应用核心（纯 WinForms，无 WPF）。
/// 职责：组合根——加载设置/模型 → 创建 Live2D 引擎 + 分层窗口 + 托盘 + 键盘钩子，
/// 用 System.Windows.Forms.Timer 驱动每帧渲染，并处理窗口/输入/电源等宿主级事件。
/// 互动养成反应链与低频周期调度分别委托给 <see cref="PetInteractionService"/>
/// 与 <see cref="PetScheduler"/>（二者经 <see cref="IPetHost"/> 门面访问宿主能力）。
/// </summary>
public sealed class PetApplication : IDisposable, IPetHost
{
    // 隐藏宿主窗：用于把后台线程（钩子线程 / 宠物窗口线程）回调 marshal 回 UI 线程，
    // 同时作为全局快捷键（Ctrl+`）的消息接收窗口
    private readonly HiddenHostForm _uiHost;
    private readonly UpdateService _updateService = new();
    private System.Windows.Forms.Timer? _renderTimer;
    private Stopwatch? _renderStopwatch;
    private double _lastRender;

    private TrayManager? _tray;
    private Live2DManager? _live2D;
    private PetLayeredWindow? _petWindow;
    private KeyboardMonitor? _keyboard;
    private KeyReactionController? _keyReaction;

    // v1.3 拆分：互动/养成反应链与低频周期调度各自成服务，宿主只做装配与宿主级事件
    private PetInteractionService _interaction = null!;
    private PetScheduler? _scheduler;

    // v1.3 番茄钟（专注陪伴）：纯状态机由定时器驱动，宿主只做协调与表现
    private FocusSession _focus = null!;
    private System.Windows.Forms.Timer? _focusTimer;

    private AppSettings _settings = new();
    // 已生效的昵称：与设置值比对检测"用户改了名字"，用于触发改名反应与刷新面板标题
    private string _appliedPetName = PetDialogue.DefaultPetName;
    private List<ModelInfo> _models = new();
    private ModelInfo? _currentModel;
    private List<string> _expressions = new();

    // 养成系统
    private PetState _petState = new();
    private PetStatusForm? _statusForm;
    private BubbleWindow? _bubbleWindow;
    private SoundManager? _sound;

    // 当前模型适合待机的动作分组（模型枚举结果，切换模型后刷新）
    private IReadOnlyList<string> _idleMotionGroups = Array.Empty<string>();

    private bool _clickThrough;
    private bool _keyboardEnabled = true;
    private bool _disposed;

    // 状态联动微表情：当前临时情绪 + 过期时间（近期互动/受惊会临时覆盖，过期后回落到由养成状态推导的基础情绪）
    private PetMood _mood = PetMood.Neutral;
    private DateTime _moodUntil = DateTime.MinValue;

    // 单实例激活：让"再双击一次"的第二个实例能唤醒本实例（显示桌宠 + 气泡）
    private readonly string _activateEventName = "";
    private EventWaitHandle? _activateEvent;
    private Thread? _activateWatcher;
    private volatile bool _activateRunning;

    // 自适应帧率：光标靠近或近期有交互 → 目标帧率(Fps)，否则降到约 12fps 省 CPU
    private bool _cursorNear;
    private DateTime _lastInteraction = DateTime.MinValue;
    private int ActiveIntervalMs => Math.Max(8, 1000 / Math.Clamp(_settings.Fps, 10, 144));
    private const int IdleIntervalMs = 83;
    private const int NearRadiusSq = 400 * 400;

    // 桌宠画布基准尺寸（缩放在此基础上调整）
    private const int PetWidth = 420;
    private const int PetHeight = 680;

    // ---- v1.1：DPI 感知 / 休眠唤醒 / 渲染故障自恢复 ----
    private int _dpi = 96;                    // 当前所在显示器的 DPI（100% = 96）
    private bool _suspended;                  // 系统休眠中（渲染已暂停）
    private DateTime _lastRecovery = DateTime.MinValue;   // 上次"接力重启"时刻
    private int _recoveryCount;               // 本次运行已尝试的恢复次数
    private const int MaxRecoveries = 2;      // 恢复上限，避免故障时无限重启
    private static readonly TimeSpan RecoveryCooldown = TimeSpan.FromSeconds(30);

    // 空帧（全透明）检测：驱动重置后 GL 可能不报错但一直出空帧，靠它兜底
    private DateTime _blankSince = DateTime.MinValue;
    private DateTime _lastBlankCheck = DateTime.MinValue;
    private int _framesSinceStart;

    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "config", "settings.json");
    private static string PetStatePath => Path.Combine(AppContext.BaseDirectory, "config", "petstate.json");
    private static string DialoguePath => Path.Combine(AppContext.BaseDirectory, "config", "dialogue.json");

    /// <summary>隐藏宿主窗，作为 WinForms 消息循环的锚点（供 Application.Run 使用）。</summary>
    public Form UiHost => _uiHost;

    private static void Log(string msg) => Live2DPet.Core.AppLog.Info("[app] " + msg);

    public PetApplication(string activateEventName = "")
    {
        _activateEventName = activateEventName;
        Log("ctor: begin");
        _uiHost = new HiddenHostForm(this)
        {
            ShowInTaskbar = false,
            WindowState = FormWindowState.Minimized,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000)
        };
        _ = _uiHost.Handle;   // 强制创建句柄，供 BeginInvoke 跨线程回调
        Log("ctor: handle ready");
        Initialize();
        // OpenTK 的 GameWindow 创建需要消息循环已运行，故延迟到消息循环启动后再加载模型
        _uiHost.BeginInvoke(StartLive2D);
        Log("ctor: done");
    }
    private void Initialize()
    {
        Log("init: begin");
        // 1) 加载设置 + 解析模型
        _settings = SettingsStore.Load(SettingsPath);
        _appliedPetName = NormalizePetName(_settings.PetName);   // 初始昵称先对齐，避免启动时误触发改名反应

        // 1.5) 用户自定义台词（config/dialogue.json）：首次运行自动生成带 _comment 注释的模板
        ReloadDialogue(announce: false);

        // 养成状态：加载 + 离线衰减 + 气泡窗口（均须在 UI 线程）
        _petState = PetStateStore.Load(PetStatePath);
        _petState.ApplyOfflineDecay(DateTime.UtcNow);
        _bubbleWindow = new BubbleWindow();
        _sound = new SoundManager(Path.Combine(AppContext.BaseDirectory, "assets", "sounds"))
        {
            Enabled = _settings.SoundEnabled,
            Volume = _settings.Volume
        };

        // 服务拆分（v1.3）：互动/养成反应链 + 低频周期调度；二者经 IPetHost 门面访问宿主
        _interaction = new PetInteractionService(this);
        _scheduler = new PetScheduler(this);

        _currentModel = ResolveModel();
        if (_currentModel == null)
        {
            MessageBox.Show("未找到任何 Live2D 模型。\n请把模型目录放到 assets\\models\\ 下。",
                "Live2DPet", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Exit();
            return;
        }
        Log("init: model resolved");

        // 2) Live2D 业务门面（主线程创建隐藏 GameWindow + 加载模型）
        _live2D = new Live2DManager();
        _live2D.FrameAvailable += OnFrame;
        _live2D.RenderFaulted += OnRenderFaulted;   // GL 连续失败 → 自恢复

        // 3) 原生分层窗口（透明置顶 + 鼠标穿透），自带消息循环线程
        _petWindow = new PetLayeredWindow(PetWidth, PetHeight, _settings.PosX, _settings.PosY);
        _petWindow.DpiChanged += OnPetDpiChanged;         // 拖到不同缩放的屏幕 / 改系统缩放
        _petWindow.DisplayChanged += OnDisplayChanged;    // 拔插外接屏 / 改分辨率
        _petWindow.Show();
        _petWindow.SetOpacity((float)_settings.Opacity);
        _petWindow.SetClickThrough(_settings.ClickThrough);
        _petWindow.SetDraggable(_settings.Draggable);
        _clickThrough = _settings.ClickThrough;
        _keyboardEnabled = _settings.KeyboardInteraction;

        // 4) 模型加载延迟到消息循环启动后（见 StartLive2D），OpenTK GameWindow 创建依赖消息循环

        // 5) 渲染循环（UI 线程 Timer）
        _renderStopwatch = Stopwatch.StartNew();
        _lastRender = 0;
        _renderTimer = new System.Windows.Forms.Timer { Interval = ActiveIntervalMs };
        _renderTimer.Tick += (_, _) => RenderTick();
        _renderTimer.Start();

        // 6) 系统托盘
        _tray = new TrayManager();
        _tray.SettingsRequested += (_, _) => ShowSettings();
        _tray.ExitRequested += (_, _) => Application.Exit();
        _tray.ToggleClickThroughRequested += (_, _) =>
        {
            _settings.ClickThrough = !_settings.ClickThrough;
            ApplySettings();
        };
        _tray.ToggleKeyboardInteractionRequested += (_, _) =>
        {
            _settings.KeyboardInteraction = !_settings.KeyboardInteraction;
            ApplySettings();
        };
        _tray.ToggleGazeRequested += (_, _) =>
        {
            _settings.GazeFollow = !_settings.GazeFollow;
            ApplySettings();
        };
        _tray.ToggleAutoStartRequested += (_, _) =>
        {
            _settings.AutoStart = !_settings.AutoStart;
            AutoStartManager.SetEnabled(_settings.AutoStart);
            _tray.SetAutoStartChecked(_settings.AutoStart);
            SettingsStore.Save(_settings, SettingsPath);
        };
        _tray.ExpressionSelected += (_, id) => OnExpressionSelected(id);
        _tray.ShowPetRequested += (_, _) => Ui(ShowPet);
        _tray.StatusRequested += (_, _) => ShowStatus();
        _tray.ScreenshotRequested += (_, _) => Ui(TakeScreenshot);
        _tray.AboutRequested += (_, _) => ShowAbout();
        _tray.OpenLogsRequested += (_, _) => AppLog.OpenFolder();
        _tray.ReloadDialogueRequested += (_, _) => ReloadDialogue(announce: true);
        _tray.UpdateRequested += (_, _) => ShowUpdate();
        _tray.BalloonClicked += (_, _) => ShowUpdate();
        _updateService.ShutdownRequested += () => Application.Exit();
        _tray.SetClickThroughChecked(_settings.ClickThrough);
        _tray.SetKeyboardInteractionChecked(_settings.KeyboardInteraction);
        _tray.SetGazeChecked(_settings.GazeFollow);
        _tray.SetAutoStartChecked(_settings.AutoStart);
        _tray.SetExpressions(_expressions, _settings.Expression);
        _tray.ToggleHideRequested += (_, _) => Ui(ToggleHide);

        // 10.6) 番茄钟（专注陪伴）：状态机 + 1s 驱动定时器 + 托盘菜单接线
        InitFocus();

        // 10.5) 全局快捷键：一键隐藏/显示桌宠（清场/唤回，比托盘点更顺手），组合键可在设置里改
        RegisterHotkey();

        // 10) 单实例激活：让"再双击一次"的第二个实例能唤醒本实例（显示桌宠 + 气泡）
        if (!string.IsNullOrEmpty(_activateEventName))
            StartActivateWatcher(_activateEventName);

        // 7) 全局键盘钩子
        _keyReaction = new KeyReactionController();
        _keyboard = new KeyboardMonitor();
        _keyboard.KeyActed += OnKeyActed;
        _keyboard.Start();

        // 8) 鼠标互动（点击/拖拽/双击/右键）→ 交给互动服务计分 + 反应 + 气泡
        _petWindow.PetClicked += (region) => Ui(() => OnPetClick(region));
        _petWindow.PetDragged += () => Ui(() => _interaction.DragStart());
        _petWindow.PetDoubleClicked += () => Ui(() => _interaction.Interact("Tap@Body", 3, 2, PetDialogue.DoubleTapReplies));
        _petWindow.PetMoved += () => Ui(OnPetMoved);
        _petWindow.PetRightClicked += (x, y) => Ui(() => _tray?.ShowMenuAt(x, y));

        // 9) 状态衰减/待机动作/打盹 三个低频定时器已在 PetScheduler 内自启（见上）

        // 每日签到：本地日期跨天则累计天数 + 发奖励（好感/经验），同一天重复启动不重复发奖
        var loginReport = _petState.RecordDailyLogin(DateTime.Now);
        if (loginReport.IsNewDay)
        {
            int lvBefore = _petState.Level;
            int bondBefore = _petState.BondLevel;
            _petState.AddAffection(loginReport.RewardAffection);
            _petState.AddExperience(loginReport.RewardExp);
            _interaction.AnnounceLevelUp(lvBefore, bondBefore);  // 签到经验可能升级/羁绊提升 → 补提示
            _interaction.CheckAndAnnounceAchievements();     // 签到奖励也可能解锁成就
        }

        // 启动问候 vs 离线欢迎回来 vs 每日签到 vs 节日/生日：节日/生日最优先；离开较久弹"欢迎回来"；否则今天刚签到弹签到气泡
        var sinceLast = DateTime.UtcNow - _petState.LastSeen;
        string? festival = PetDialogue.FestivalGreeting(DateTime.Now, _settings.Birthday);
        string greeting;
        if (festival != null)
        {
            greeting = festival;
        }
        else if (sinceLast > TimeSpan.FromMinutes(15))
        {
            int lvBefore = _petState.Level;
            int bondBefore = _petState.BondLevel;
            int wbAff = Math.Clamp((int)(sinceLast.TotalHours * 2), 1, 30);
            int wbExp = Math.Clamp((int)sinceLast.TotalHours, 1, 20);
            _petState.AddAffection(wbAff);
            _petState.AddExperience(wbExp);
            _interaction.AnnounceLevelUp(lvBefore, bondBefore);  // 离线补偿经验可能升级/羁绊提升 → 补提示
            _interaction.CheckAndAnnounceAchievements();     // 离线累计的互动/在线也可能解锁成就
            greeting = PetDialogue.WelcomeBack(sinceLast);
        }
        else if (loginReport.IsNewDay)
        {
            greeting = PetDialogue.DailyLogin(loginReport);
        }
        else
        {
            greeting = PetDialogue.GreetingFor(DateTime.Now);
        }
        Say(greeting);
        _sound?.Play("greet");
        _petState.LastSeen = DateTime.UtcNow;
        PetStateStore.Save(_petState, PetStatePath);
        Log("init: done");

        // 启动静默检查更新（仅弹气泡提示，不自动下载/重启）
        if (_settings.CheckUpdateOnStartup)
            _ = Task.Run(async () =>
            {
                try
                {
                    var info = await _updateService.CheckAsync();
                    if (info != null && _updateService.NeedsUpdate(info))
                        Ui(() => _tray.ShowBalloon("发现新版本 " + info.Version, "点击此处查看并更新"));
                }
                catch { /* 检查更新失败不影响主功能 */ }
            });
    }

    /// <summary>消息循环启动后加载模型并套缩放/表情。</summary>
    private void StartLive2D()
    {
        if (_disposed || _live2D == null || _currentModel == null) return;
        try
        {
            Log("StartLive2D: before Start");
            _live2D.Start(_currentModel.Dir, _currentModel.Name);
            Log("StartLive2D: after Start");
            // 取窗口真实 DPI 后再套缩放：高分屏下按 DPI 放大渲染分辨率，避免位图被系统拉伸而模糊
            int dpi = _petWindow?.Dpi ?? 0;
            if (dpi > 0 && dpi != _dpi) { _dpi = dpi; Log($"StartLive2D: dpi={dpi}"); }
            ApplyScale();
            RefreshExpressions();
            RefreshIdleGroups();
            Log("StartLive2D: done");
        }
        catch (Exception ex)
        {
            Log($"Live2D init FAILED: {ex.Message}");
            MessageBox.Show($"Live2D 初始化失败：\n{ex.Message}", "Live2DPet", MessageBoxButtons.OK, MessageBoxIcon.Error);
            CrashLog.Write(ex);
        }
    }

    private void RenderTick()
    {
        if (_disposed) return;
        double now = _renderStopwatch!.Elapsed.TotalSeconds;
        float dt = (float)(now - _lastRender);
        _lastRender = now;
        if (dt <= 0) dt = 1f / 60f;

        UpdateMouseTarget();
        UpdateDockPeek();
        _live2D?.Tick(dt);

        // 状态联动微表情：优先用近期事件（互动/受惊）触发的临时情绪，过期后回落到由养成状态推导的基础情绪
        if (_settings.MoodExpression)
        {
            PetMood eff;
            if (DateTime.UtcNow < _moodUntil) eff = _mood;
            else if (_petState.IsHungry || _petState.IsDirty || _petState.WantsPlay) eff = PetMood.Sad;
            else eff = PetMood.Neutral;
            _live2D?.ApplyMood(eff);
        }
        else
        {
            _live2D?.ApplyMood(PetMood.Neutral);
        }

        UpdateFrameInterval();
    }

    /// <summary>把后台线程回调 marshal 回 UI 线程执行。</summary>
    private void Ui(Action action)
    {
        if (_disposed) return;
        if (_uiHost.InvokeRequired)
            _uiHost.BeginInvoke(action);
        else
            action();
    }

    private void OnKeyActed(object? sender, KeyActedEventArgs e)
    {
        if (!_keyboardEnabled) return;
        // 前台窗口全屏（游戏/全屏视频）时静默，避免键盘回应打扰用户
        if (_settings.SuppressOnFullscreen && IsForegroundFullscreen()) return;
        Ui(() =>
        {
            _lastInteraction = DateTime.UtcNow;
            var group = _keyReaction?.Consider(e.VirtualKey, DateTime.UtcNow);
            if (group != null)
                _interaction.KeyboardReaction(group);   // 播动作 + 微量好感/经验 + 记账（不弹气泡）
        });
    }

    /// <summary>判断前台窗口是否为全屏（覆盖其所在显示器的完整区域），用于游戏/全屏视频时静默键盘回应。</summary>
    private static bool IsForegroundFullscreen()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        if (!NativeMethods.GetWindowRect(hwnd, out var r)) return false;

        IntPtr mon = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (mon == IntPtr.Zero) return false;
        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(mon, ref mi)) return false;

        int w = r.right - r.left;
        int h = r.bottom - r.top;
        int mw = mi.rcMonitor.right - mi.rcMonitor.left;
        int mh = mi.rcMonitor.bottom - mi.rcMonitor.top;
        // 窗口覆盖所在显示器完整区域（含盖住任务栏的情况）即视为全屏
        return w >= mw && h >= mh;
    }

    private void OnFrame(FrameData frame)
    {
        if (_framesSinceStart < int.MaxValue) _framesSinceStart++;
        _petWindow?.PushFrame(frame.Pixels, frame.Width, frame.Height);
        CheckBlankFrame(frame);
    }

    // ---- 渲染故障自恢复（v1.1）----

    /// <summary>GL 连续渲染失败（驱动重置 / 上下文丢失 / 独显切换）：记录后走"接力重启"。</summary>
    private void OnRenderFaulted(Exception ex)
    {
        AppLog.Error(ex, "渲染连续失败");
        RequestRecovery("渲染连续失败（GL 上下文可能已丢失）");
    }

    /// <summary>
    /// 空帧兜底检测：部分驱动重置后 GL 调用不报错，但一直出全透明帧（用户看到的是"桌宠消失了"）。
    /// 每 2 秒抽样一次，持续 10 秒空帧即判定异常。隐藏/刚启动/切换模型期间不参与判定。
    /// </summary>
    private void CheckBlankFrame(FrameData frame)
    {
        var now = DateTime.UtcNow;
        if (now - _lastBlankCheck < TimeSpan.FromSeconds(2)) return;
        _lastBlankCheck = now;

        if (_petWindow == null || _petWindow.IsHidden || _framesSinceStart < 30 || _suspended)
        {
            _blankSince = DateTime.MinValue;
            return;
        }

        if (!IsMostlyTransparent(frame.Pixels))
        {
            _blankSince = DateTime.MinValue;
            return;
        }

        if (_blankSince == DateTime.MinValue) { _blankSince = now; return; }
        if (now - _blankSince < TimeSpan.FromSeconds(10)) return;

        _blankSince = DateTime.MinValue;
        AppLog.Warn("[render] 连续 10 秒空帧，疑似 GL 上下文失效");
        RequestRecovery("持续空帧（疑似 GL 上下文失效）");
    }

    /// <summary>抽样判断一帧是否几乎全透明（每 64 字节取一个 alpha，够快也够准）。</summary>
    private static bool IsMostlyTransparent(byte[] pixels)
    {
        if (pixels.Length < 4) return true;
        for (int i = 3; i < pixels.Length; i += 64)
            if (pixels[i] > 8) return false;
        return true;
    }

    /// <summary>
    /// 优雅"接力重启"：先落盘当前状态、释放单实例锁，再拉起新进程，最后退出自己。
    /// 这是 GL 上下文丢失最可靠的恢复手段——Cubism SDK 的全局状态不支持安全地进程内重建，
    /// 而对常驻桌宠来说，重启一两秒、养成数据不丢，远好过一直黑屏。
    /// </summary>
    private void RequestRecovery(string reason)
    {
        if (_disposed) return;

        var now = DateTime.UtcNow;
        if (now - _lastRecovery < RecoveryCooldown)
        {
            AppLog.Warn($"[recover] 冷却中，跳过本次恢复：{reason}");
            return;
        }
        if (_recoveryCount >= MaxRecoveries)
        {
            AppLog.Warn($"[recover] 已达本次运行上限 {MaxRecoveries} 次，不再重启：{reason}");
            _tray?.ShowBalloon("Live2D 桌宠", "渲染出了问题，我已经尽力重启了几次。\n请手动重启我一下~");
            return;
        }

        _recoveryCount++;
        _lastRecovery = now;
        AppLog.Error($"[recover] {reason} → 第 {_recoveryCount} 次自恢复重启");

        try
        {
            SaveWindowPosition();
            SettingsStore.Save(_settings, SettingsPath);
            PetStateStore.Save(_petState, PetStatePath);
        }
        catch (Exception ex) { AppLog.Error(ex, "恢复前保存状态失败"); }

        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            Program.ReleaseSingleton();   // 先放锁：否则新实例会误判"已在运行"并退出自己
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "自恢复重启失败");
            return;
        }

        Application.Exit();
    }

    // ---- 系统电源 / 时间 / 显示器事件（v1.1）----

    /// <summary>系统即将休眠：立刻暂停渲染并落盘，避免休眠期间掉电丢进度、也避免唤醒时 dt 爆炸。</summary>
    private void OnSystemSuspend()
    {
        _suspended = true;
        try { _renderTimer?.Stop(); } catch { }
        try { PetStateStore.Save(_petState, PetStatePath); } catch { }
        Log("[power] 系统进入休眠：已暂停渲染并保存进度");
    }

    /// <summary>系统唤醒：重置时间基准（丢弃休眠时长）、恢复渲染，并把桌宠拉回可见区域。</summary>
    private void OnSystemResume(string source)
    {
        _suspended = false;
        // 关键：不把休眠的几小时算进 dt，否则引擎一帧推进几小时，动画与状态全部跳变
        if (_renderStopwatch != null) _lastRender = _renderStopwatch.Elapsed.TotalSeconds;
        _scheduler?.ResetOnlineStamp();      // 休眠时长不计入在线时长统计
        _blankSince = DateTime.MinValue;     // 唤醒瞬间的空帧不计入故障判定
        _lastBlankCheck = DateTime.UtcNow;
        _live2D?.ResetFaultCount();          // 唤醒时的一次抖动不算故障

        _petWindow?.EnsureOnScreen();        // 可能在另一块显示器/不同分辨率下唤醒
        if (_petWindow != null) ApplyRenderPause(_petWindow.IsHidden);
        Log($"[power] 系统已唤醒（{source}）：渲染恢复，时间基准已重置");
    }

    /// <summary>系统时间被大幅修改（手动改表 / 域同步 / 从休眠恢复后校时）：重置在线计时基准。</summary>
    private void OnSystemTimeChanged()
    {
        _scheduler?.ResetOnlineStamp();
        Log("[system] 系统时间变化：在线计时基准已重置");
    }

    /// <summary>轮询全局光标，换算成归一化跟随目标喂给 Live2D（眼神/头部跟随鼠标）。
    /// 设置关闭时传 null，让 SDK 目标点平滑回到正中（目光朝前）。</summary>
    private void UpdateMouseTarget()
    {
        if (_live2D == null || _petWindow == null) return;
        if (!NativeMethods.GetCursorPos(out var p)) return;

        var b = _petWindow.Bounds;
        int petX = b.left, petY = b.top;
        int petW = b.right - b.left, petH = b.bottom - b.top;

        int cx = petX + petW / 2, cy = petY + petH / 2;
        int dx = p.X - cx, dy = p.Y - cy;
        _cursorNear = (dx * dx + dy * dy) < NearRadiusSq;

        if (_settings.GazeFollow)
        {
            var (nx, ny) = MouseFollow.ComputeTarget(p.X, p.Y, petX, petY, petW, petH);
            _live2D.SetMouseTarget(nx, ny);
        }
        else
        {
            _live2D.SetMouseTarget(null, null);
        }
    }

    /// <summary>帧率策略：
    /// - 桌宠在屏内可见时（含拖动、光标靠近、近期互动），一律尊重用户在设置里选的帧率；
    /// - 仅当贴边半隐藏（角色大部分已在屏外）时，降到约 12fps 省 CPU，此时用户基本无感。
    /// 这样拖动到屏幕别处也始终满帧，不会突然卡成 12fps。</summary>
    private void UpdateFrameInterval()
    {
        if (_renderTimer == null) return;
        bool hidden = _petWindow != null && _petWindow.IsDocked && !_petWindow.IsPeeked && !_petWindow.IsDragging;
        int interval = hidden ? IdleIntervalMs : ActiveIntervalMs;
        if (_renderTimer.Interval != interval)
            _renderTimer.Interval = interval;
    }

    /// <summary>按设置里的缩放值 + 当前显示器 DPI，同步调整渲染视口与分层窗口尺寸。
    /// DPI 参与计算是关键：桌宠画布以 96 DPI（100%）为基准，
    /// 在 150%/200% 缩放下若仍按 96 渲染，系统会把位图拉伸 → 明显模糊、边缘发虚。</summary>
    private void ApplyScale()
    {
        double factor = _settings.Scale * (_dpi / 96.0);
        int w = Math.Max(1, (int)Math.Round(PetWidth * factor));
        int h = Math.Max(1, (int)Math.Round(PetHeight * factor));
        _live2D?.Resize(w, h);
        _petWindow?.Resize(w, h);
    }

    /// <summary>显示器 DPI 变化：更新 DPI 并按新 DPI 重建渲染分辨率与画布。</summary>
    private void OnPetDpiChanged(int dpi)
    {
        if (dpi <= 0 || dpi == _dpi) return;
        _dpi = dpi;
        Log($"dpi changed -> {dpi}");
        ApplyScale();
    }

    /// <summary>显示器配置变化（拔插外接屏 / 改分辨率）：位置已被窗口层校正，这里只负责持久化。</summary>
    private void OnDisplayChanged()
    {
        SaveWindowPosition();
        _petWindow?.InvalidateContentBounds();
    }

    /// <summary>把当前设置应用到桌宠窗口并持久化（设置窗/托盘改动共用入口）。</summary>
    private void ApplySettings()
    {
        // 昵称变化：即时刷新已开面板标题 + 给一句改名反应（无需重启）
        var petName = NormalizePetName(_settings.PetName);
        if (!string.Equals(petName, _appliedPetName, StringComparison.Ordinal))
        {
            _appliedPetName = petName;
            _settings.PetName = petName;      // 空白名字写回默认昵称，避免设置里存脏值
            _statusForm?.SetPetName(petName);
            Say($"{petName}？{petName}喜欢这个名字~");
        }

        _clickThrough = _settings.ClickThrough;
        _keyboardEnabled = _settings.KeyboardInteraction;
        ApplyScale();
        _petWindow?.SetOpacity((float)_settings.Opacity);
        _petWindow?.SetClickThrough(_settings.ClickThrough);
        _petWindow?.SetDraggable(_settings.Draggable);
        _tray?.SetClickThroughChecked(_settings.ClickThrough);
        _tray?.SetKeyboardInteractionChecked(_settings.KeyboardInteraction);
        if (_sound != null) { _sound.Enabled = _settings.SoundEnabled; _sound.Volume = _settings.Volume; }
        ReapplyHotkey();   // 快捷键可能被改了，重新注册
        SettingsStore.Save(_settings, SettingsPath);
    }

    /// <summary>扫描 assets/models 并解析要加载的模型（优先设置里存的，否则第一个）。</summary>
    private ModelInfo? ResolveModel()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "assets", "models");
        _models = ModelCatalog.Scan(root).ToList();
        if (_models.Count == 0) return null;
        return _models.FirstOrDefault(m => m.Id == _settings.Model) ?? _models[0];
    }

    /// <summary>设置窗口里选择了新模型：切换、套缩放、刷新表情、保存。</summary>
    private void OnModelSelected(ModelInfo model)
    {
        if (_currentModel?.Id == model.Id) return;
        _currentModel = model;
        _settings.Model = model.Id;
        _live2D?.SwitchModel(model.Dir, model.Name);
        _petWindow?.InvalidateContentBounds();   // 模型换了，角色包围盒缓存必须失效重算
        ApplyScale();
        RefreshExpressions();
        RefreshIdleGroups();
        SettingsStore.Save(_settings, SettingsPath);
    }

    /// <summary>重新枚举当前模型可用的表情，并应用设置里保存的表情（模型支持时）。</summary>
    private void RefreshExpressions()
    {
        _expressions = _live2D?.AvailableExpressions?.ToList() ?? new List<string>();
        if (_expressions.Count == 0)
        {
            _settings.Expression = "";
        }
        else if (!string.IsNullOrEmpty(_settings.Expression) && _expressions.Contains(_settings.Expression))
        {
            _live2D?.PlayExpression(_settings.Expression);
        }
        else
        {
            _settings.Expression = "";
        }
        _tray?.SetExpressions(_expressions, _settings.Expression);
    }

    private void OnExpressionSelected(string id)
    {
        _settings.Expression = id;
        if (string.IsNullOrEmpty(id))
            _live2D?.ResetExpression();
        else
            _live2D?.PlayExpression(id);
        _tray?.SetExpressions(_expressions, id);
        SettingsStore.Save(_settings, SettingsPath);
    }

    // 全局快捷键 ID（进程内唯一即可）
    private const int HotkeyId = 9001;

    /// <summary>分区抚摸：按点击落在头部/身体/脚部给予不同反应与萌系台词。</summary>
    private void OnPetClick(PetLayeredWindow.HitRegion region)
    {
        switch (region)
        {
            case PetLayeredWindow.HitRegion.Head:
                _interaction.Interact("Tap", 3, 2, PetDialogue.HeadRubLines); break;
            case PetLayeredWindow.HitRegion.Body:
                _interaction.Interact("Flick", 2, 1, PetDialogue.PokeBodyLines); break;
            default:
                _interaction.Interact("Tap@Body", 2, 1, PetDialogue.TouchFeetLines); break;
        }
    }

    /// <summary>全局快捷键回调：切换桌宠隐藏/显示。</summary>
    private void OnHotKey() => Ui(ToggleHide);

    /// <summary>按当前设置注册隐藏/显示快捷键（禁用则跳过）。</summary>
    private void RegisterHotkey()
    {
        if (_settings.HotkeyKey == 0) return;   // 用户选择"禁用"
        uint mods = (uint)_settings.HotkeyModifiers | NativeMethods.MOD_NOREPEAT;
        if (!NativeMethods.RegisterHotKey(_uiHost.Handle, HotkeyId, mods, _settings.HotkeyKey))
            Log("RegisterHotKey failed: " + Marshal.GetLastWin32Error());
    }

    /// <summary>设置变更后重新注册快捷键（先注销旧键再注册新键）。</summary>
    private void ReapplyHotkey()
    {
        try { NativeMethods.UnregisterHotKey(_uiHost.Handle, HotkeyId); } catch { }
        RegisterHotkey();
    }

    /// <summary>把快捷键设置转成可读文案（用于托盘气泡提示）。</summary>
    private string DescribeHotkey()
    {
        if (_settings.HotkeyKey == 0) return "托盘菜单";
        var parts = new List<string>();
        if ((_settings.HotkeyModifiers & (int)NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((_settings.HotkeyModifiers & (int)NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((_settings.HotkeyModifiers & (int)NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        string key = _settings.HotkeyKey switch
        {
            NativeMethods.VK_OEM_3 => "`",
            NativeMethods.VK_SPACE => "空格",
            NativeMethods.VK_H => "H",
            NativeMethods.VK_F8 => "F8",
            _ => ((char)_settings.HotkeyKey).ToString()
        };
        parts.Add(key);
        return string.Join(" + ", parts);
    }

    /// <summary>一键隐藏/显示桌宠：隐藏时透明+穿透不挡操作，显示时还原并弹气泡。</summary>
    private void ToggleHide()
    {
        if (_petWindow == null) return;
        bool hidden = !_petWindow.IsHidden;
        _petWindow.SetHidden(hidden);
        ApplyRenderPause(hidden);
        _tray?.SetHiddenLabel(hidden);
        _tray?.ShowBalloon("Live2D 桌宠", hidden ? $"我先躲起来啦，按 {DescribeHotkey()} 叫我出来~" : "我回来啦！");
    }

    /// <summary>彻底隐藏时暂停渲染循环省 CPU；显示时恢复（并重置 dt 基准，避免首帧跳变）。</summary>
    private void ApplyRenderPause(bool paused)
    {
        if (_renderTimer == null) return;
        if (paused)
        {
            _renderTimer.Stop();
        }
        else
        {
            _lastRender = _renderStopwatch!.Elapsed.TotalSeconds;
            _renderTimer.Start();
        }
    }

    private void SaveWindowPosition()
    {
        if (_petWindow == null) return;
        var b = _petWindow.Bounds;
        if (b.right > b.left && b.bottom > b.top)
        {
            _settings.PosX = b.left;
            _settings.PosY = b.top;
        }
        SettingsStore.Save(_settings, SettingsPath);
    }

    /// <summary>拖拽结束：保存位置 + 贴边吸附（可选半隐藏）。</summary>
    private void OnPetMoved()
    {
        SaveWindowPosition();
        if (_petWindow == null || !_settings.SnapToEdge) return;
        _petWindow.SnapToEdge();
        if (_settings.AutoHide)
            _petWindow.SetPeek(false);
    }

    /// <summary>托盘"显示桌宠"：取消贴边 + 完整显示（若处于彻底隐藏状态则先解除隐藏并恢复渲染）。</summary>
    private void ShowPet()
    {
        if (_petWindow == null) return;
        if (_petWindow.IsHidden)
        {
            _petWindow.SetHidden(false);
            ApplyRenderPause(false);
        }
        _petWindow.Undock();
        _petWindow.SetPeek(true);
    }

    /// <summary>跨实例激活事件：创建 AutoReset 事件并启动监听线程。
    /// 收到"第二个实例"的信号后，在 UI 线程把桌宠显示出来并弹气泡提示。</summary>
    private void StartActivateWatcher(string evtName)
    {
        try
        {
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, evtName);
        }
        catch
        {
            return;   // 极端情况下放弃唤醒能力，不影响主功能
        }
        _activateRunning = true;
        _activateWatcher = new Thread(() =>
        {
            while (_activateRunning && _activateEvent != null)
            {
                try { _activateEvent.WaitOne(); }   // 阻塞直到被第二个实例 Set
                catch { break; }
                if (!_activateRunning) break;
                Ui(ActivateFromOtherInstance);
            }
        })
        { IsBackground = true, Name = "ActivateWatcher" };
        _activateWatcher.Start();
    }

    /// <summary>被另一个实例唤醒：显示完整桌宠 + 托盘气泡提示。</summary>
    private void ActivateFromOtherInstance()
    {
        ShowPet();
        _tray?.ShowBalloon("Live2D 桌宠", "我已经在运行啦，找到我了吗~");
    }

    private DateTime _peekLeaveTime = DateTime.MinValue;

    /// <summary>贴边半隐藏检测：鼠标划过贴边边缘弹出，离开窗口 2 秒后缩回。</summary>
    private void UpdateDockPeek()
    {
        if (_petWindow == null || !_petWindow.IsDocked || !_settings.AutoHide) return;
        if (!NativeMethods.GetCursorPos(out var p)) return;

        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        var edge = _petWindow.DockedEdge;
        if (!_petWindow.IsPeeked)
        {
            bool atEdge = edge switch
            {
                PetLayeredWindow.DockEdge.Left => p.X <= vx + PetLayeredWindow.EdgeDetectPx,
                PetLayeredWindow.DockEdge.Right => p.X >= vx + vw - PetLayeredWindow.EdgeDetectPx,
                PetLayeredWindow.DockEdge.Top => p.Y <= vy + PetLayeredWindow.EdgeDetectPx,
                _ => p.Y >= vy + vh - PetLayeredWindow.EdgeDetectPx
            };
            if (atEdge) _petWindow.SetPeek(true);
        }
        else
        {
            var b = _petWindow.Bounds;
            bool inside = p.X >= b.left && p.X <= b.right && p.Y >= b.top && p.Y <= b.bottom;
            if (inside) _peekLeaveTime = DateTime.MinValue;
            else if (_peekLeaveTime == DateTime.MinValue) _peekLeaveTime = DateTime.UtcNow;
            else if (DateTime.UtcNow - _peekLeaveTime > TimeSpan.FromSeconds(2))
                _petWindow.SetPeek(false);
        }
    }

    /// <summary>在角色头顶弹文字气泡（须在 UI 线程调用）。锚点改为角色头顶（基于帧 alpha 实时扫描），
    /// 气泡三角指向人物头部，无论角色在画布哪个位置、动画中如何摆动都对齐。
    /// 作为 IPetHost.Say 的实现，供互动/调度服务调用。</summary>
    public void Say(string text)
    {
        if (_bubbleWindow == null || _petWindow == null) return;
        if (_petWindow.IsHidden) return;   // 彻底隐藏时不弹气泡（避免藏起来还冒字）
        // 贴边半隐藏时说话：先滑出，保证用户能同时看到桌宠和气泡（否则气泡会落在屏外）
        if (_petWindow.IsDocked && !_petWindow.IsPeeked)
            _petWindow.SetPeek(true);
        var b = _petWindow.Bounds;
        var (cx, headTop) = _petWindow.GetContentHeadTop();
        // 兜底：早期帧未渲染出有效 alpha 时回退到窗口几何参考
        if (cx <= b.left) cx = (b.left + b.right) / 2;
        if (headTop <= b.top) headTop = b.top;
        // 昵称占位统一在此收口：所有气泡文案（互动/养成/提醒）都支持 {name}
        _bubbleWindow.ShowBubble(PetDialogue.Named(text, _settings.PetName), cx, headTop - 4);
    }

    /// <summary>环境气泡（待机碎碎念/报时/休息提醒/低状态提醒等）：免打扰时段内抑制。
    /// 作为 IPetHost.SayAmbient 的实现，供互动/调度服务调用。</summary>
    public void SayAmbient(string text)
    {
        if (DndClock.IsActive(_settings, DateTime.Now)) return;
        if (_focus != null && _focus.IsActive) return;   // 专注/短休期间：抑制所有随机环境台词（自动免打扰）
        Say(text);
    }

    // ---- 番茄钟（专注陪伴）协调层 ----
    // FocusSession 是纯状态机，不碰 UI；下面这些方法是它在 WinForms 侧的"手脚"：
    // 定时器驱动状态推进、把状态/事件翻译成气泡与成就，托盘只负责菜单与事件转发。

    /// <summary>创建专注状态机、订阅其事件、接线托盘菜单、启动 1s 驱动定时器。</summary>
    private void InitFocus()
    {
        _focus = new FocusSession();
        _focus.PhaseChanged += OnFocusPhaseChanged;
        _focus.ReminderDue += OnFocusReminder;
        _focus.FocusCompleted += OnFocusCompleted;

        _tray!.StartFocusRequested += (_, _) => StartFocus();
        _tray.StartBreakRequested += (_, _) => StartBreak();
        _tray.StopFocusRequested += (_, _) => StopFocus();

        _focusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _focusTimer.Tick += (_, _) => OnFocusTick();
        _focusTimer.Start();
    }

    /// <summary>每约 1s 推进状态机并刷新托盘剩余时间。仅在 UI 线程调用。</summary>
    private void OnFocusTick()
    {
        if (_disposed || _focus == null) return;
        _focus.Update(DateTime.Now);
        _tray?.SetFocusState(_focus.Phase, _focus.Remaining);
    }

    private void StartFocus()
    {
        if (_focus == null || _focus.IsActive) return;
        _focus.StartFocus(DateTime.Now);
    }

    private void StartBreak()
    {
        if (_focus == null || _focus.IsActive) return;
        _focus.StartBreak(DateTime.Now);
    }

    private void StopFocus()
    {
        if (_focus == null || !_focus.IsActive) return;
        _focus.Stop(DateTime.Now);
    }

    /// <summary>状态切换：刷新托盘菜单，并在进入/退出各阶段时给一句对应气泡。</summary>
    private void OnFocusPhaseChanged(object? sender, FocusPhaseChangedEventArgs e)
    {
        _tray?.SetFocusState(e.To, _focus.Remaining);
        switch (e.To)
        {
            case FocusPhase.Focus:
                Say(PetDialogue.Pick(PetDialogue.FocusStartLines));
                break;
            case FocusPhase.Break:
                Say(PetDialogue.Pick(PetDialogue.BreakStartLines));
                break;
            case FocusPhase.Idle:
                if (e.From == FocusPhase.Break)
                    Say(PetDialogue.Pick(PetDialogue.BreakDoneLines));
                break;
        }
    }

    /// <summary>每 5 分钟一次的专注提醒气泡（用 Say 而非 SayAmbient，确保专注期间照常弹出）。</summary>
    private void OnFocusReminder(object? sender, FocusReminderEventArgs e)
    {
        Say(PetDialogue.Pick(PetDialogue.FocusReminderLines));
    }

    /// <summary>一次专注完成：累计次数 + 奖励 EXP + 检测专注类成就（含可能的升级/羁绊提示）。</summary>
    private void OnFocusCompleted(object? sender, FocusCompletedEventArgs e)
    {
        _petState.TotalFocusSessions++;
        _petState.AddExperience(e.RewardExp);
        Say($"{PetDialogue.Pick(PetDialogue.FocusDoneLines)} 经验 +{e.RewardExp}");
        _sound?.Play("levelup");
        _interaction.CheckAndAnnounceAchievements();   // 解锁专注成就 + 发奖励 + 可能升级提示 + 保存
    }

    /// <summary>枚举模型里适合做"待机"的动作分组：优先 Idle，否则取所有非互动分组（Tap/TapBody/Flick…）。
    /// 纯选择逻辑在 Core.IdleMotionSelector，这里只负责触发刷新（启动/换模型时）。</summary>
    private void RefreshIdleGroups()
    {
        var all = _live2D?.AvailableMotionGroups ?? Array.Empty<string>();
        _idleMotionGroups = IdleMotionSelector.Select(all);
    }

    // ---- 一键截图 / 分享 ----
    private void TakeScreenshot()
    {
        if (_petWindow == null) return;
        if (_petWindow.IsHidden) { _tray?.ShowBalloon("截图", "桌宠被隐藏啦，先按快捷键唤出我~"); return; }
        if (_petWindow.IsDocked && !_petWindow.IsPeeked) _petWindow.SetPeek(true);
        var b = _petWindow.Bounds;
        int x = b.left, y = b.top, w = b.right - b.left, h = b.bottom - b.top;
        if (w <= 0 || h <= 0) return;
        try
        {
            using var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Live2DPet");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"shot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            bmp.Save(file, ImageFormat.Png);
            try { Clipboard.SetImage((Image)bmp.Clone()); } catch { /* 剪贴板不可用忽略 */ }
            _tray?.ShowBalloon("截图已保存", $"已存到 图片\\Live2DPet\\{Path.GetFileName(file)}，并复制到剪贴板~");
            Say("咔嚓！给你拍张照~");
        }
        catch (Exception ex)
        {
            Log("screenshot failed: " + ex.Message);
            _tray?.ShowBalloon("截图失败", ex.Message);
        }
    }

    // ---- 重置养成 ----
    private void ResetProgress()
    {
        PetStateStore.Purge(PetStatePath);
        _petState = new PetState();
        _statusForm?.Close();
        _statusForm = null;
        _scheduler?.ResetOnlineStamp();   // 新状态从此刻起算在线时长
        PetStateStore.Save(_petState, PetStatePath);
        Say("记忆清空啦，我们重新认识吧~");
        _sound?.Play("greet");
    }

    /// <summary>昵称规范化：去首尾空白；空白时回退默认昵称（与 Core.PetDialogue.Named 行为一致）。</summary>
    private static string NormalizePetName(string? name)
        => string.IsNullOrWhiteSpace(name) ? PetDialogue.DefaultPetName : name.Trim();

    /// <summary>打开养成面板（喂食/陪玩/洗澡动作由互动服务执行）。</summary>
    private void ShowStatus()
    {
        if (_statusForm == null || _statusForm.IsDisposed)
            _statusForm = new PetStatusForm(_petState, _interaction.Feed, _interaction.Play, _interaction.Bathe, _appliedPetName);
        _statusForm.Show();
        _statusForm.Activate();
    }

    /// <summary>打开关于面板（模态）。</summary>
    private void ShowAbout()
    {
        using var form = new AboutForm(_appliedPetName);
        form.ShowDialog(_uiHost);
    }

    private void ShowSettings()
    {
        var form = new SettingsForm(
            _settings,
            _models,
            _currentModel?.Id ?? "",
            _expressions,
            _settings.Expression,
            ApplySettings,
            OnModelSelected,
            OnExpressionSelected,
            ResetProgress,
            BackupConfig,
            RestoreConfig);
        form.ShowDialog(_uiHost);
    }

    /// <summary>手动检查更新（托盘"检查更新…"或点击更新气泡）。manual=true 时无更新/失败会弹提示。</summary>
    private void ShowUpdate()
    {
        _ = CheckAndShowUpdateAsync(manual: true);
    }

    private async Task CheckAndShowUpdateAsync(bool manual)
    {
        var info = await _updateService.CheckAsync();
        Ui(() =>
        {
            if (info == null)
            {
                if (manual) MessageBox.Show("检查更新失败（网络错误）。", "Live2DPet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!_updateService.NeedsUpdate(info))
            {
                if (manual) MessageBox.Show("已是最新版本。", "Live2DPet", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var form = new UpdateForm(_updateService, info);
            form.ShowDialog(_uiHost);
        });
    }

    private static string ConfigDir => Path.Combine(AppContext.BaseDirectory, "config");

    /// <summary>把设置 / 养成进度 / 参数映射打包成一个 zip（换机、重装前的保险）。</summary>
    private void BackupConfig()
    {
        string suggest = ConfigBackup.DefaultExportPath();
        using var dlg = new SaveFileDialog
        {
            Title = "备份配置与养成数据",
            Filter = "Live2DPet 备份 (*.zip)|*.zip",
            FileName = Path.GetFileName(suggest),
            InitialDirectory = Path.GetDirectoryName(suggest) ?? ""
        };
        if (dlg.ShowDialog(_uiHost) != DialogResult.OK) return;

        if (ConfigBackup.Export(ConfigDir, dlg.FileName, out string err, out int count))
        {
            AppLog.Info($"[backup] 已备份 {count} 个文件 -> {dlg.FileName}");
            MessageBox.Show($"备份完成，共 {count} 个文件：\n{dlg.FileName}", "备份成功",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            AppLog.Error($"[backup] 备份失败：{err}");
            MessageBox.Show($"备份失败：\n{err}", "备份失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>从备份 zip 还原（覆盖前会二次确认，并在落盘后立即重新加载生效）。</summary>
    private void RestoreConfig()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "选择备份文件还原",
            Filter = "Live2DPet 备份 (*.zip)|*.zip|所有文件 (*.*)|*.*"
        };
        if (dlg.ShowDialog(_uiHost) != DialogResult.OK) return;

        if (MessageBox.Show("还原会覆盖当前的设置与养成进度，确定继续吗？", "还原备份",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        if (!ConfigBackup.Import(dlg.FileName, ConfigDir, out string err, out int count))
        {
            AppLog.Error($"[restore] 还原失败：{err}");
            MessageBox.Show($"还原失败：\n{err}", "还原失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        ReloadFromDisk();
        AppLog.Info($"[restore] 已从 {dlg.FileName} 还原 {count} 个文件");
        _tray?.ShowBalloon("还原完成", $"已恢复 {count} 个配置文件~");
    }

    /// <summary>
    /// 重新读取 config/dialogue.json 并应用到 <see cref="PetDialogue"/>（热更新，无需重启）。
    /// 文件首次不存在 → 自动生成带 <c>_comment</c> 注释的模板；缺失分组 → 用内置台词补齐；
    /// 文件损坏 → 回退内置台词（不改动用户文件），并在气泡/日志里说明原因。
    /// </summary>
    /// <param name="announce">是否用气泡反馈结果：启动时静默，托盘手动触发时提示。</param>
    private void ReloadDialogue(bool announce)
    {
        var overrides = DialogueOverrides.LoadOrCreate(DialoguePath, out string? error, out bool created);
        overrides.Apply();

        if (!string.IsNullOrEmpty(error))
        {
            AppLog.Warn("[dialogue] " + error);
            if (announce) Say("台词文件好像写坏了，先用内置的凑合一下吧…（详情看日志）");
            return;
        }

        if (created)
        {
            Log("dialogue: 首次运行，已生成台词模板 config/dialogue.json");
            return;   // 首次生成不打扰用户
        }

        int n = overrides.Count;
        Log($"dialogue: 自定义台词已加载（{n}/{DialogueOverrides.GroupNames.Length} 组）");
        if (announce)
            Say(n == 0 ? "台词已刷新，现在是内置台词~" : $"台词更新啦，{n} 组自定义生效~");
    }

    /// <summary>从磁盘重新加载设置与养成状态并应用到运行时（备份还原后调用）。</summary>
    private void ReloadFromDisk()
    {
        _settings = SettingsStore.Load(SettingsPath);
        _petState = PetStateStore.Load(PetStatePath);
        _appliedPetName = NormalizePetName(_settings.PetName);   // 还原后昵称可能不同，直接对齐避免误报改名
        _scheduler?.ResetOnlineStamp();   // 新状态从此刻起算在线时长
        ReloadDialogue(announce: false);  // 还原的备份里可能带着另一份 dialogue.json
        _clickThrough = _settings.ClickThrough;
        _keyboardEnabled = _settings.KeyboardInteraction;
        ApplySettings();
        RefreshExpressions();
        _tray?.SetClickThroughChecked(_settings.ClickThrough);
        _tray?.SetKeyboardInteractionChecked(_settings.KeyboardInteraction);
        _tray?.SetGazeChecked(_settings.GazeFollow);
        _tray?.SetAutoStartChecked(_settings.AutoStart);
        // 养成数据已换：关掉可能开着的旧面板，下次打开会重建
        _statusForm?.Close();
        _statusForm = null;
    }

    // ---- IPetHost 实现（互动/调度服务的宿主门面）----
    // 注意活引用语义：以下 getter 每次都返回宿主当前字段值，
    // 因此"重置养成/还原备份"替换 _petState 后，服务无需同步、自动拿到新实例。

    public PetState State => _petState;
    public AppSettings Settings => _settings;
    public Live2DManager? Live2D => _live2D;
    public SoundManager? Sound => _sound;
    public bool IsDisposed => _disposed;

    public DateTime LastInteraction
    {
        get => _lastInteraction;
        set => _lastInteraction = value;
    }

    public IReadOnlyList<string> IdleMotionGroups
    {
        get => _idleMotionGroups;
        set => _idleMotionGroups = value;
    }

    /// <summary>桌宠当前是否"可见可互动"：窗口不存在视为可互动（由服务侧兜底）。</summary>
    public bool IsPetInteractive => _petWindow == null
        || (!_petWindow.IsHidden && !_petWindow.IsDragging && (!_petWindow.IsDocked || _petWindow.IsPeeked));

    /// <summary>把当前养成状态落盘。</summary>
    public void SavePetState() => PetStateStore.Save(_petState, PetStatePath);

    /// <summary>设置临时情绪（覆盖基础情绪），seconds 秒后回落到由养成状态推导的基础情绪。</summary>
    public void SetTransientMood(PetMood mood, double seconds)
    {
        _mood = mood;
        _moodUntil = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 停止跨实例激活监听线程（先置标志再 Set 唤醒其退出）
        _activateRunning = false;
        _activateEvent?.Set();
        _activateEvent?.Dispose();

        SaveWindowPosition();
        SettingsStore.Save(_settings, SettingsPath);
        // 收尾：补齐最后一段在线时长（精确到秒），随后统一落盘
        _scheduler?.FlushOnline();
        PetStateStore.Save(_petState, PetStatePath);

        // 注销全局快捷键（宿主窗即将销毁）
        try { NativeMethods.UnregisterHotKey(_uiHost.Handle, HotkeyId); } catch { }

        _renderTimer?.Stop();
        _renderTimer?.Dispose();
        _focusTimer?.Stop();
        _focusTimer?.Dispose();
        _scheduler?.Dispose();
        _keyboard?.Dispose();
        _live2D?.Stop();
        _live2D?.Dispose();
        _petWindow?.Dispose();
        _tray?.Dispose();
        _bubbleWindow?.Dispose();
        _sound?.Dispose();
        _uiHost.Dispose();
    }

    /// <summary>隐藏宿主窗：承载 WinForms 消息循环，并接收全局快捷键 WM_HOTKEY 转发给应用。</summary>
    private sealed class HiddenHostForm : Form
    {
        private readonly PetApplication _owner;
        public HiddenHostForm(PetApplication owner) => _owner = owner;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                _owner.OnHotKey();
                m.Result = IntPtr.Zero;
                return;
            }
            switch (m.Msg)
            {
                case NativeMethods.WM_POWERBROADCAST:
                {
                    int ev = m.WParam.ToInt32();
                    if (ev == NativeMethods.PBT_APMSUSPEND) _owner.OnSystemSuspend();
                    else if (ev == NativeMethods.PBT_APMRESUMESUSPEND) _owner.OnSystemResume("用户唤醒");
                    else if (ev == NativeMethods.PBT_APMRESUMEAUTOMATIC) _owner.OnSystemResume("自动唤醒");
                    m.Result = new IntPtr(1);   // 已处理
                    return;
                }
                case NativeMethods.WM_TIMECHANGE:
                    _owner.OnSystemTimeChanged();
                    break;
                case NativeMethods.WM_DISPLAYCHANGE:
                    // 窗口层已负责把桌宠拉回可见区域；这里补一次位置持久化
                    _owner.OnDisplayChanged();
                    break;
            }
            base.WndProc(ref m);
        }
    }
}
