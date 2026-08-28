using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Live2DPet.Core.Interaction;
using Live2DPet.Core.Models;
using Live2DPet.Core.Mouse;
using Live2DPet.Core.Pet;
using Live2DPet.Core.Settings;
using Live2DPet.Platform;
using Live2DPet.Platform.Input;
using Live2DPet.Platform.Native;
using Live2DPet.Platform.Tray;
using Live2DPet.Platform.Window;
using Live2DPet.Rendering;

namespace Live2DPet.App;

/// <summary>
/// 桌宠应用核心（纯 WinForms，无 WPF）。
/// 职责：加载设置/模型 → 创建 Live2D 引擎 + 分层窗口 + 托盘 + 键盘钩子，
/// 用 System.Windows.Forms.Timer 驱动每帧渲染，并处理所有互动事件。
/// </summary>
public sealed class PetApplication : IDisposable
{
    // 隐藏宿主窗：用于把后台线程（钩子线程 / 宠物窗口线程）回调 marshal 回 UI 线程，
    // 同时作为全局快捷键（Ctrl+`）的消息接收窗口
    private readonly HiddenHostForm _uiHost;
    private System.Windows.Forms.Timer? _renderTimer;
    private Stopwatch? _renderStopwatch;
    private double _lastRender;

    private TrayManager? _tray;
    private Live2DManager? _live2D;
    private PetLayeredWindow? _petWindow;
    private KeyboardMonitor? _keyboard;
    private KeyReactionController? _keyReaction;

    private AppSettings _settings = new();
    private List<ModelInfo> _models = new();
    private ModelInfo? _currentModel;
    private List<string> _expressions = new();

    // 养成系统
    private PetState _petState = new();
    private PetStatusForm? _statusForm;
    private BubbleWindow? _bubbleWindow;
    private SoundManager? _sound;
    private System.Windows.Forms.Timer? _decayTimer;

    // 待机随机动作：低频调度，空闲时偶发播放待机动作 + 萌系气泡
    private System.Windows.Forms.Timer? _idleTimer;
    private List<string> _idleMotionGroups = new();
    private static readonly Random Rnd = new();

    // 在线时长精确累计：记录上次记账时刻，按真实经过秒数累加（避免崩溃丢整分钟）
    private DateTime _onlineStamp = DateTime.UtcNow;

    // 离开检测（打盹待机）：用户长时间无键鼠操作 → 进入睡觉状态
    private System.Windows.Forms.Timer? _sleepTimer;
    private bool _sleeping;

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

    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "config", "settings.json");
    private static string PetStatePath => Path.Combine(AppContext.BaseDirectory, "config", "petstate.json");

    /// <summary>隐藏宿主窗，作为 WinForms 消息循环的锚点（供 Application.Run 使用）。</summary>
    public Form UiHost => _uiHost;

    private static void Log(string msg)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "init.log"), $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
        }
        catch { }
    }

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

        // 养成状态：加载 + 离线衰减 + 气泡窗口（均须在 UI 线程）
        _petState = PetStateStore.Load(PetStatePath);
        _petState.ApplyOfflineDecay(DateTime.UtcNow);
        _onlineStamp = DateTime.UtcNow;
        _bubbleWindow = new BubbleWindow();
        _sound = new SoundManager(Path.Combine(AppContext.BaseDirectory, "assets", "sounds"))
        {
            Enabled = _settings.SoundEnabled,
            Volume = _settings.Volume
        };

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

        // 3) 原生分层窗口（透明置顶 + 鼠标穿透），自带消息循环线程
        _petWindow = new PetLayeredWindow(PetWidth, PetHeight, _settings.PosX, _settings.PosY);
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
        _tray.SetClickThroughChecked(_settings.ClickThrough);
        _tray.SetKeyboardInteractionChecked(_settings.KeyboardInteraction);
        _tray.SetGazeChecked(_settings.GazeFollow);
        _tray.SetAutoStartChecked(_settings.AutoStart);
        _tray.SetExpressions(_expressions, _settings.Expression);
        _tray.ToggleHideRequested += (_, _) => Ui(ToggleHide);

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

        // 8) 鼠标互动（点击/拖拽/双击/右键）→ 计分 + 反应 + 气泡
        _petWindow.PetClicked += (region) => Ui(() => OnPetClick(region));
        _petWindow.PetDragged += () => Ui(() => OnDragStart());
        _petWindow.PetDoubleClicked += () => Ui(() => Interact("Tap@Body", 3, 2, PetDialogue.DoubleTapReplies));
        _petWindow.PetMoved += () => Ui(OnPetMoved);
        _petWindow.PetRightClicked += (x, y) => Ui(() => _tray?.ShowMenuAt(x, y));

        // 9) 状态衰减定时器（每分钟）+ 启动问候 / 离线欢迎回来
        _decayTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _decayTimer.Tick += (_, _) => OnDecayTick();
        _decayTimer.Start();

        // 9.5) 待机随机动作调度器（低频，仅在空闲时偶发）
        _idleTimer = new System.Windows.Forms.Timer { Interval = 20_000 };
        _idleTimer.Tick += (_, _) => OnIdleTick();
        _idleTimer.Start();

        // 9.6) 离开检测：用户长时间无键鼠操作 → 进入"打盹"待机（每 10s 轮询一次空闲时长）
        _sleepTimer = new System.Windows.Forms.Timer { Interval = 10_000 };
        _sleepTimer.Tick += (_, _) => OnSleepCheck();
        _sleepTimer.Start();

        // 每日签到：本地日期跨天则累计天数 + 发奖励（好感/经验），同一天重复启动不重复发奖
        var loginReport = _petState.RecordDailyLogin(DateTime.Now);
        if (loginReport.IsNewDay)
        {
            int lvBefore = _petState.Level;
            _petState.AddAffection(loginReport.RewardAffection);
            _petState.AddExperience(loginReport.RewardExp);
            AnnounceLevelUp(lvBefore);          // 签到经验可能升级 → 补升级/里程碑提示
            CheckAndAnnounceAchievements();     // 签到奖励也可能解锁成就
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
            int wbAff = Math.Clamp((int)(sinceLast.TotalHours * 2), 1, 30);
            int wbExp = Math.Clamp((int)sinceLast.TotalHours, 1, 20);
            _petState.AddAffection(wbAff);
            _petState.AddExperience(wbExp);
            AnnounceLevelUp(lvBefore);          // 离线补偿经验可能升级 → 补升级/里程碑提示
            CheckAndAnnounceAchievements();     // 离线累计的互动/在线也可能解锁成就
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
            {
                _live2D?.PlayReaction(group);
                SetMood(PetMood.Happy, 1.0);   // 键盘互动 → 开心一下
                // 键盘互动 +少量好感/经验（不弹气泡，避免刷屏）
                _petState.AddAffection(1);
                _petState.AddExperience(1);
                AfterInteraction();
                PetStateStore.Save(_petState, PetStatePath);
            }
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
        _petWindow?.PushFrame(frame.Pixels, frame.Width, frame.Height);
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

    /// <summary>按设置里的缩放值，同步调整渲染视口与分层窗口尺寸（模型随视口等比缩放，无裁切）。</summary>
    private void ApplyScale()
    {
        int w = Math.Max(1, (int)Math.Round(PetWidth * _settings.Scale));
        int h = Math.Max(1, (int)Math.Round(PetHeight * _settings.Scale));
        _live2D?.Resize(w, h);
        _petWindow?.Resize(w, h);
    }

    /// <summary>把当前设置应用到桌宠窗口并持久化（设置窗/托盘改动共用入口）。</summary>
    private void ApplySettings()
    {
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
                Interact("Tap", 3, 2, PetDialogue.HeadRubLines); break;
            case PetLayeredWindow.HitRegion.Body:
                Interact("Flick", 2, 1, PetDialogue.PokeBodyLines); break;
            default:
                Interact("Tap@Body", 2, 1, PetDialogue.TouchFeetLines); break;
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

    /// <summary>设置临时情绪（覆盖基础情绪），seconds 秒后回落到由养成状态推导的基础情绪。</summary>
    private void SetMood(PetMood mood, double seconds)
    {
        _mood = mood;
        _moodUntil = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
    }

    /// <summary>一次互动：播动作 + 计好感/经验 + 弹气泡（升级/亲密度提升/里程碑解锁优先显示）。</summary>
    private void Interact(string group, int affection, int exp, string[] replies)
    {
        _lastInteraction = DateTime.UtcNow;
        SetMood(PetMood.Happy, 1.5);   // 被摸/被戳 → 开心一会儿
        _live2D?.PlayReaction(group);
        _sound?.Play(group.Equals("Flick", StringComparison.OrdinalIgnoreCase) ? "pop" : "tap");
        int levelBefore = _petState.Level;
        bool affectionUp = _petState.AddAffection(affection);
        bool leveled = _petState.AddExperience(exp);
        Say(PetDialogue.PickReaction(replies, _petState.Level));
        if (affectionUp) Say(PetDialogue.AffectionUp(_petState.AffectionName));
        if (leveled) { Say(PetDialogue.LevelUp(_petState.Level, _petState.StageName)); _sound?.Play("levelup"); }
        SayLevelupUnlocks(levelBefore);
        AfterInteraction();
        PetStateStore.Save(_petState, PetStatePath);
    }

    /// <summary>升级后若跨过里程碑等级（3/5/7/10），补一条"解锁"提示。</summary>
    private void SayLevelupUnlocks(int levelBefore)
    {
        for (int lv = levelBefore + 1; lv <= _petState.Level; lv++)
        {
            if (PetState.IsMilestoneLevel(lv))
                Say(PetDialogue.MilestoneUnlock(lv));
        }
    }

    /// <summary>互动后统一记账：累计互动次数 + 检测解锁成就（新解锁弹气泡 + 音效 + 保存）。</summary>
    private void AfterInteraction()
    {
        _petState.TotalInteractions++;
        CheckAndAnnounceAchievements();
    }

    /// <summary>若签到/离线补偿后发生升级，补弹升级 + 里程碑解锁提示（含音效）。</summary>
    private void AnnounceLevelUp(int levelBefore)
    {
        if (_petState.Level > levelBefore)
        {
            Say(PetDialogue.LevelUp(_petState.Level, _petState.StageName));
            _sound?.Play("levelup");
            SayLevelupUnlocks(levelBefore);
        }
    }

    /// <summary>检测并播报新解锁的成就（弹气泡 + 音效 + 发放奖励 + 保存）。
    /// 不计入用户互动次数（供启动期签到/离线补偿复用）。</summary>
    private void CheckAndAnnounceAchievements()
    {
        var newly = _petState.CheckAchievements();
        int lvBefore = _petState.Level;
        int totalAff = 0, totalExp = 0;
        foreach (var a in newly)
        {
            if (a.RewardAffection > 0) totalAff += a.RewardAffection;
            if (a.RewardExp > 0) totalExp += a.RewardExp;
            Say($"成就解锁「{a.Name}」：{a.Desc}{a.RewardText}");
            _sound?.Play("levelup");
        }
        if (newly.Count > 0)
        {
            if (totalAff > 0) _petState.AddAffection(totalAff);
            if (totalExp > 0) _petState.AddExperience(totalExp);
            AnnounceLevelUp(lvBefore);      // 奖励可能触发升级
            SayLevelupUnlocks(lvBefore);
            PetStateStore.Save(_petState, PetStatePath);
        }
    }

    /// <summary>拖拽开始（被拎起来瞬间）：受惊吓动作 + 惊吓台词 + 短暂"受惊"情绪。</summary>
    private void OnDragStart()
    {
        _lastInteraction = DateTime.UtcNow;
        SetMood(PetMood.Surprised, 1.3);
        _live2D?.PlayReaction("Flick");   // 受惊吓动作
        _sound?.Play("startle");
        Say(PetDialogue.Pick(PetDialogue.StartleLines));
    }

    /// <summary>在角色头顶弹文字气泡（须在 UI 线程调用）。锚点改为角色头顶（基于帧 alpha 实时扫描），
    /// 气泡三角指向人物头部，无论角色在画布哪个位置、动画中如何摆动都对齐。</summary>
    private void Say(string text)
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
        _bubbleWindow.ShowBubble(text, cx, headTop - 4);
    }

    /// <summary>当前是否处于免打扰（专注）时段：支持跨午夜区间（如 23:00 → 08:00）。</summary>
    private bool IsDndNow()
    {
        if (!_settings.DndEnabled) return false;
        int now = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
        int s = _settings.DndStart, e = _settings.DndEnd;
        if (s == e) return false;
        return s < e ? (now >= s && now < e) : (now >= s || now < e);
    }

    /// <summary>环境气泡（待机碎碎念/报时/休息提醒/低状态提醒等）：免打扰时段内抑制。</summary>
    private void SayAmbient(string text)
    {
        if (IsDndNow()) return;
        Say(text);
    }

    /// <summary>每分钟状态衰减 + 低状态提醒 + 整点/半点报时 + 休息提醒。</summary>
    private void OnDecayTick()
    {
        if (_disposed) return;
        bool wasHungry = _petState.IsHungry, wasPlay = _petState.WantsPlay, wasDirty = _petState.IsDirty;
        _petState.Decay(1.0);
        // 在线时长按真实经过秒数累加（精确，避免崩溃丢整分钟）
        var now = DateTime.UtcNow;
        long delta = (long)(now - _onlineStamp).TotalSeconds;
        if (delta > 0) _petState.TotalOnlineSeconds += delta;
        _onlineStamp = now;

        if (!wasHungry && _petState.IsHungry) SayAmbient(PetDialogue.Pick(PetDialogue.HungryLines));
        else if (!wasPlay && _petState.WantsPlay) SayAmbient(PetDialogue.Pick(PetDialogue.WantsPlayLines));
        else if (!wasDirty && _petState.IsDirty) SayAmbient(PetDialogue.Pick(PetDialogue.DirtyLines));
        _petState.LastSeen = DateTime.UtcNow;   // 周期刷新，保证下次启动计算离线时长准确
        PetStateStore.Save(_petState, PetStatePath);

        ChimeIfDue(DateTime.Now);
        UpdateBreakReminder();
    }

    /// <summary>枚举模型里适合做"待机"的动作分组：优先 Idle，否则取所有非互动分组（Tap/TapBody/Flick…）。</summary>
    private void RefreshIdleGroups()
    {
        var all = _live2D?.AvailableMotionGroups ?? Array.Empty<string>();
        var interaction = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Tap", "TapBody", "Tap@Body", "Flick", "PinchIn", "PinchOut", "Pinch", "Shake"
        };
        var idle = all.Where(g => g.Equals("Idle", StringComparison.OrdinalIgnoreCase)).ToList();
        if (idle.Count == 0)
            idle = all.Where(g => !interaction.Contains(g)).ToList();
        _idleMotionGroups = idle;
    }

    /// <summary>待机随机动作：低频触发（每次重新随机间隔，避免规律感）。
    /// 仅在空闲（近期无互动、未在拖拽、非半隐藏离屏）时偶发播放一个待机动作，并小概率配一句萌系碎碎念。
    /// 普通优先级，绝不打断用户的互动反应。</summary>
    private void OnIdleTick()
    {
        if (_disposed || _live2D == null || _idleTimer == null) return;
        _idleTimer.Interval = 20_000 + Rnd.Next(25_000);   // 下次 20~45s

        // 打盹中：偶尔冒一句睡意，不做随机动作，避免吵到用户
        if (_sleeping)
        {
            if (Rnd.Next(100) < 25) SayAmbient(PetDialogue.Pick(PetDialogue.SleepLines));
            return;
        }

        bool idle = (DateTime.UtcNow - _lastInteraction) > TimeSpan.FromSeconds(6)
                    && (_petWindow == null || (!_petWindow.IsHidden && !_petWindow.IsDragging && (!_petWindow.IsDocked || _petWindow.IsPeeked)));
        if (!idle || _idleMotionGroups.Count == 0) return;

        if (Rnd.Next(100) < 60)   // 60% 概率真的做待机动作，其余时间安静歇着
        {
            _live2D.PlayIdleMotion(_idleMotionGroups);
            if (Rnd.Next(100) < 35)   // 偶尔碎碎念，按状态"蔫/活泼"更有人味
                SayAmbient(PickIdleLine());
        }
    }

    /// <summary>按宠物当前状态选待机碎碎念：状态差→蔫，状态好→活泼，中等→普通。</summary>
    private string PickIdleLine()
    {
        if (_petState.IsHungry || _petState.WantsPlay || _petState.IsDirty)
            return PetDialogue.Pick(PetDialogue.LowStateLines);
        if (_petState.Satiety >= 70 && _petState.Mood >= 70 && _petState.Cleanliness >= 70)
            return PetDialogue.Pick(PetDialogue.HappyIdleLines);
        return PetDialogue.Pick(PetDialogue.IdleLines);
    }

    private DateTime _lastChime = DateTime.MinValue;

    /// <summary>整点/半点报时（tick 约每分钟一次，命中 minute==0/30 时弹气泡）。</summary>
    private void ChimeIfDue(DateTime now)
    {
        if (!_settings.ChimeEnabled) return;
        if (now.Minute != 0 && now.Minute != 30) return;
        if ((now - _lastChime).TotalMinutes < 50) return;
        _lastChime = now;
        if (now.Minute == 0) SayAmbient(PetDialogue.Chime(now.Hour));
        else SayAmbient(PetDialogue.ChimeHalf(now.Hour));
    }

    private int _breakTickCount;
    private const int BreakEveryTicks = 45;   // 约每 45 分钟提醒一次

    private void UpdateBreakReminder()
    {
        if (!_settings.BreakReminder) { _breakTickCount = 0; return; }
        if (++_breakTickCount < BreakEveryTicks) return;
        _breakTickCount = 0;
        SayAmbient(PetDialogue.Pick(PetDialogue.BreakReminders));
    }

    // ---- 离开检测（打盹待机）----
    private void OnSleepCheck()
    {
        if (_disposed) return;
        if (_settings.IdleSleepMinutes <= 0) { if (_sleeping) WakeUp(); return; }
        bool idle = GetIdleMinutes() >= _settings.IdleSleepMinutes;
        if (idle && !_sleeping) EnterSleep();
        else if (!idle && _sleeping) WakeUp();
    }

    /// <summary>取系统空闲分钟数（距上次键鼠输入）。</summary>
    private int GetIdleMinutes()
    {
        try
        {
            var li = new NativeMethods.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>() };
            if (NativeMethods.GetLastInputInfo(ref li))
            {
                uint now = unchecked((uint)Environment.TickCount);
                uint idle = now - li.dwTime;   // 两者同属 GetTickCount 体系，减法天然处理回绕
                return (int)(idle / 60000);
            }
        }
        catch { }
        return 0;
    }

    private void EnterSleep()
    {
        _sleeping = true;
        _lastInteraction = DateTime.UtcNow;   // 睡觉期间不触发"近期互动"满帧
        if (_idleMotionGroups.Count > 0) _live2D?.PlayIdleMotion(_idleMotionGroups);
        SayAmbient(PetDialogue.Pick(PetDialogue.SleepLines));
    }

    private void WakeUp()
    {
        _sleeping = false;
        _lastInteraction = DateTime.UtcNow;
        SetMood(PetMood.Happy, 1.5);
        _live2D?.PlayReaction("Tap");
        Say(PetDialogue.Pick(PetDialogue.WakeLines));   // 醒来提示不受免打扰抑制（用户回来后的主动反馈）
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
        _onlineStamp = DateTime.UtcNow;
        PetStateStore.Save(_petState, PetStatePath);
        Say("记忆清空啦，我们重新认识吧~");
        _sound?.Play("greet");
    }

    private void DoFeed()
    {
        _lastInteraction = DateTime.UtcNow;
        int affBefore = _petState.AffectionLevel;
        int levelBefore = _petState.Level;
        var r = _petState.Feed();
        if (r == CareResult.Success)
        {
            SetMood(PetMood.Happy, 2.0);
            _live2D?.PlayReaction("Tap");
            _sound?.Play("eat");
            Say(PetDialogue.PickReaction(PetDialogue.FeedReplies, _petState.Level));
            if (_petState.AffectionLevel > affBefore)
                Say(PetDialogue.AffectionUp(_petState.AffectionName));
            if (_petState.Level > levelBefore)
            { Say(PetDialogue.LevelUp(_petState.Level, _petState.StageName)); _sound?.Play("levelup"); }
            SayLevelupUnlocks(levelBefore);
            _petState.TotalFeeds++;
            AfterInteraction();
            PetStateStore.Save(_petState, PetStatePath);
        }
        else // CareResult.Full
        {
            _live2D?.PlayReaction("Tap");
            Say(PetDialogue.Pick(PetDialogue.FullLines));
        }
    }

    private void DoPlay()
    {
        _lastInteraction = DateTime.UtcNow;
        int affBefore = _petState.AffectionLevel;
        int levelBefore = _petState.Level;
        var r = _petState.Play();
        if (r == CareResult.Success)
        {
            SetMood(PetMood.Happy, 2.0);
            _live2D?.PlayReaction("Flick");
            _sound?.Play("play");
            Say(PetDialogue.PickReaction(PetDialogue.PlayReplies, _petState.Level));
            if (_petState.AffectionLevel > affBefore)
                Say(PetDialogue.AffectionUp(_petState.AffectionName));
            if (_petState.Level > levelBefore)
            { Say(PetDialogue.LevelUp(_petState.Level, _petState.StageName)); _sound?.Play("levelup"); }
            SayLevelupUnlocks(levelBefore);
            _petState.TotalPlays++;
            AfterInteraction();
            PetStateStore.Save(_petState, PetStatePath);
        }
        else if (r == CareResult.Hungry)
        {
            _live2D?.PlayReaction("Tap");
            Say(PetDialogue.Pick(PetDialogue.TooHungryToPlayLines));
        }
        else // CareResult.Tired
        {
            _live2D?.PlayReaction("Tap");
            Say(PetDialogue.Pick(PetDialogue.PlayEnoughLines));
        }
    }

    private void DoBathe()
    {
        _lastInteraction = DateTime.UtcNow;
        int affBefore = _petState.AffectionLevel;
        int levelBefore = _petState.Level;
        var r = _petState.Bathe();
        if (r == CareResult.Success)
        {
            SetMood(PetMood.Happy, 2.0);
            _live2D?.PlayReaction("Tap@Body");
            _sound?.Play("tap");
            Say(PetDialogue.PickReaction(PetDialogue.BatheReplies, _petState.Level));
            if (_petState.AffectionLevel > affBefore)
                Say(PetDialogue.AffectionUp(_petState.AffectionName));
            if (_petState.Level > levelBefore)
            { Say(PetDialogue.LevelUp(_petState.Level, _petState.StageName)); _sound?.Play("levelup"); }
            SayLevelupUnlocks(levelBefore);
            _petState.TotalBaths++;
            AfterInteraction();
            PetStateStore.Save(_petState, PetStatePath);
        }
        else // CareResult.Clean
        {
            _live2D?.PlayReaction("Tap");
            Say(PetDialogue.Pick(PetDialogue.CleanEnoughLines));
        }
    }

    /// <summary>打开养成面板。</summary>
    private void ShowStatus()
    {
        if (_statusForm == null || _statusForm.IsDisposed)
            _statusForm = new PetStatusForm(_petState, DoFeed, DoPlay, DoBathe);
        _statusForm.Show();
        _statusForm.Activate();
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
            ResetProgress);
        form.ShowDialog(_uiHost);
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
        // 收尾：补齐最后一段在线时长（精确到秒）
        var fin = DateTime.UtcNow;
        long fd = (long)(fin - _onlineStamp).TotalSeconds;
        if (fd > 0) _petState.TotalOnlineSeconds += fd;
        PetStateStore.Save(_petState, PetStatePath);

        // 注销全局快捷键（宿主窗即将销毁）
        try { NativeMethods.UnregisterHotKey(_uiHost.Handle, HotkeyId); } catch { }

        _decayTimer?.Stop();
        _decayTimer?.Dispose();
        _idleTimer?.Stop();
        _idleTimer?.Dispose();
        _sleepTimer?.Stop();
        _sleepTimer?.Dispose();
        _renderTimer?.Stop();
        _renderTimer?.Dispose();
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
            base.WndProc(ref m);
        }
    }
}
