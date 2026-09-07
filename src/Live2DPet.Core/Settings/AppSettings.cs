namespace Live2DPet.Core.Settings;

using Live2DPet.Core.Pet;

/// <summary>
/// 全局用户设置。所有字段都带默认值，保证未配置文件时程序也能启动。
/// 由 SettingsStore 负责读写 config/settings.json。
/// </summary>
public class AppSettings
{
    /// <summary>宠物昵称（用于台词里的 {name} 占位替换）。空白时回退默认昵称。</summary>
    public string PetName { get; set; } = PetDialogue.DefaultPetName;

    /// <summary>窗口整体不透明度 0..1</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>缩放倍数（0.5..2.0，拉伸合成，不改渲染分辨率）</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>鼠标跟踪强度倍率，0..2，1 为默认值</summary>
    public double TrackStrength { get; set; } = 1.0;

    /// <summary>鼠标穿透（true=整窗穿透、不交互；false=像素级命中、可点击/拖拽）</summary>
    public bool ClickThrough { get; set; } = false;

    /// <summary>是否允许拖动宠物（false=锁定位置，仍可点击触发反应）</summary>
    public bool Draggable { get; set; } = true;

    /// <summary>是否开启键盘互动（任意键触发反应）</summary>
    public bool KeyboardInteraction { get; set; } = true;

    /// <summary>眼神/头部跟随鼠标（眼珠与头朝光标方向微动）。默认开启，可关闭。</summary>
    public bool GazeFollow { get; set; } = true;

    /// <summary>状态联动微表情（开心/难过/受惊时身体与头部轻微倾斜）。默认开启，可关闭。</summary>
    public bool MoodExpression { get; set; } = true;

    /// <summary>当前模型标识（相对 assets/models 的目录路径）；空 = 自动选第一个可用模型。</summary>
    public string Model { get; set; } = "";

    /// <summary>当前表情 ID；空 = 无表情（默认脸）。仅当模型带 expression 时有效。</summary>
    public string Expression { get; set; } = "";

    /// <summary>是否开机自启（写入 HKCU\...\Run）。</summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>目标帧率（交互/光标靠近宠物时），默认 60；空闲时自动降频省电。</summary>
    public int Fps { get; set; } = 60;

    /// <summary>整点/半点报时（气泡提示）。</summary>
    public bool ChimeEnabled { get; set; } = true;

    /// <summary>休息/喝水提醒（每约 45 分钟一次）。</summary>
    public bool BreakReminder { get; set; } = true;

    /// <summary>拖到屏幕边缘时自动贴边吸附。</summary>
    public bool SnapToEdge { get; set; } = true;

    /// <summary>贴边后自动半隐藏（只露一角），鼠标划过边缘时弹出。</summary>
    public bool AutoHide { get; set; } = true;

    /// <summary>窗口位置 X，负值为"自动放右下角"</summary>
    public int PosX { get; set; } = -1;

    /// <summary>窗口位置 Y，负值为"自动放右下角"</summary>
    public int PosY { get; set; } = -1;

    /// <summary>隐藏/显示桌宠全局快捷键的修饰键（MOD_CONTROL=2 / MOD_ALT=1 / MOD_SHIFT=4 的组合）。</summary>
    public int HotkeyModifiers { get; set; } = 2;   // 默认 Ctrl

    /// <summary>隐藏/显示桌宠全局快捷键的虚拟键码（默认 VK_OEM_3=0xC0，即 ` 键）。0 = 禁用快捷键。</summary>
    public int HotkeyKey { get; set; } = 0xC0;

    /// <summary>前台窗口全屏（如游戏/全屏视频）时自动暂停键盘回应，避免打扰。默认开启。</summary>
    public bool SuppressOnFullscreen { get; set; } = true;

    /// <summary>互动/照顾/升级音效。默认开启，可关闭。</summary>
    public bool SoundEnabled { get; set; } = true;

    /// <summary>音效音量 0..100（自实现，对 WAV 做振幅缩放）。</summary>
    public int Volume { get; set; } = 80;

    /// <summary>生日（MM-dd 格式），当天会弹生日祝福；空 = 不设置。</summary>
    public string Birthday { get; set; } = "";

    /// <summary>免打扰（专注模式）：开启后抑制待机碎碎念/报时/休息提醒等环境气泡。</summary>
    public bool DndEnabled { get; set; } = false;

    /// <summary>免打扰开始时刻（距当天 0 点的分钟数，0..1439）。默认 23:00。</summary>
    public int DndStart { get; set; } = 23 * 60;

    /// <summary>免打扰结束时刻（距当天 0 点的分钟数，0..1439）。可小于开始（跨午夜）。默认 08:00。</summary>
    public int DndEnd { get; set; } = 8 * 60;

    /// <summary>用户无键鼠操作超过该分钟数，桌宠进入"打盹"待机；0 = 不启用。</summary>
    public int IdleSleepMinutes { get; set; } = 5;

    /// <summary>崩溃后自动重启（最多重试，避免死循环）。</summary>
    public bool CrashAutoRestart { get; set; } = true;

    /// <summary>启动时自动检查更新（静默；发现新版本仅弹气泡提示，不自动下载）。</summary>
    public bool CheckUpdateOnStartup { get; set; } = true;

    // ---- 番茄钟（专注陪伴）----
    /// <summary>专注时长（分钟），范围由 FocusConfig 钳位到 1..180。</summary>
    public int FocusMinutes { get; set; } = FocusSession.DefaultFocusMinutes;

    /// <summary>短休时长（分钟），范围由 FocusConfig 钳位到 1..60。</summary>
    public int BreakMinutes { get; set; } = FocusSession.DefaultBreakMinutes;

    /// <summary>专注中气泡提醒间隔（分钟），范围由 FocusConfig 钳位到 1..60。大于专注时长则途中不提醒。</summary>
    public int ReminderMinutes { get; set; } = FocusSession.DefaultReminderMinutes;

    /// <summary>每日专注目标（个番茄）。0 = 关闭每日目标庆祝提示。</summary>
    public int DailyFocusGoal { get; set; }
}
