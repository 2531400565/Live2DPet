using System;

namespace Live2DPet.Core.Pet;

/// <summary>番茄钟（专注陪伴）状态。</summary>
public enum FocusPhase
{
    /// <summary>空闲：未开始专注。</summary>
    Idle,

    /// <summary>专注中（默认 25 分钟）。</summary>
    Focus,

    /// <summary>短休中（默认 5 分钟，专注完成后自动进入，也可手动开始）。</summary>
    Break
}

/// <summary>
/// 番茄钟状态机：只负责<b>计时与状态流转</b>，不依赖 WinForms / 引擎 / UI / 任何外部服务。
/// 由宿主（PetApplication）用一个低频定时器以当前时间驱动 <see cref="Update"/>（传入 <c>now</c>），
/// 自身不读墙钟，因此完全可单元测试。
/// 流程：Idle →（开始专注）Focus(25min) → 每 5min 气泡提醒 → 完成奖励 EXP → Break(5min) → Idle。
/// </summary>
public sealed class FocusSession
{
    /// <summary>默认专注时长（分钟）。</summary>
    public const int DefaultFocusMinutes = 25;

    /// <summary>默认短休时长（分钟）。</summary>
    public const int DefaultBreakMinutes = 5;

    /// <summary>默认气泡提醒间隔（分钟）。</summary>
    public const int DefaultReminderMinutes = 5;

    /// <summary>每次专注完成发放的 EXP 奖励。</summary>
    public const int FocusRewardExp = 10;

    private DateTime _now = DateTime.MinValue;          // 最近一次 Update 的时钟，供 Elapsed/Remaining 使用
    private DateTime _nextReminderAt = DateTime.MinValue;

    /// <summary>专注时长。</summary>
    public TimeSpan FocusDuration { get; }

    /// <summary>短休时长。</summary>
    public TimeSpan BreakDuration { get; }

    /// <summary>气泡提醒间隔。</summary>
    public TimeSpan ReminderInterval { get; }

    /// <summary>当前状态。</summary>
    public FocusPhase Phase { get; private set; } = FocusPhase.Idle;

    /// <summary>当前状态起始时刻。</summary>
    public DateTime PhaseStartedAt { get; private set; } = DateTime.MinValue;

    /// <summary>本次运行内已完成的专注次数（内存计数，不持久化；持久总量见 PetState.TotalFocusSessions）。</summary>
    public int CompletedFocusCount { get; private set; }

    /// <summary>是否处于专注或短休中（即"免打扰"生效的窗口）。</summary>
    public bool IsActive => Phase != FocusPhase.Idle;

    /// <summary>是否正在专注。</summary>
    public bool IsFocusing => Phase == FocusPhase.Focus;

    /// <summary>是否正在短休。</summary>
    public bool IsBreaking => Phase == FocusPhase.Break;

    /// <summary>
    /// 构造。时长/间隔参数非法（&lt;=0）时回退到各自默认值，保证永远可用。
    /// </summary>
    public FocusSession(
        int focusMinutes = DefaultFocusMinutes,
        int breakMinutes = DefaultBreakMinutes,
        int reminderMinutes = DefaultReminderMinutes)
    {
        if (focusMinutes <= 0) focusMinutes = DefaultFocusMinutes;
        if (breakMinutes <= 0) breakMinutes = DefaultBreakMinutes;
        if (reminderMinutes <= 0) reminderMinutes = DefaultReminderMinutes;
        FocusDuration = TimeSpan.FromMinutes(focusMinutes);
        BreakDuration = TimeSpan.FromMinutes(breakMinutes);
        ReminderInterval = TimeSpan.FromMinutes(reminderMinutes);
    }

    /// <summary>状态切换（From → To）。</summary>
    public event EventHandler<FocusPhaseChangedEventArgs>? PhaseChanged;

    /// <summary>每 5 分钟一次的气泡提醒（仅在 Focus 阶段触发）。</summary>
    public event EventHandler<FocusReminderEventArgs>? ReminderDue;

    /// <summary>一次专注完成（发放 EXP 奖励、累计次数）。仅在 Focus → Break 时触发一次。</summary>
    public event EventHandler<FocusCompletedEventArgs>? FocusCompleted;

    /// <summary>已流逝时间（Idle 时为零）。</summary>
    public TimeSpan Elapsed => IsActive ? (_now - PhaseStartedAt) : TimeSpan.Zero;

    /// <summary>剩余时间（Idle 时为零；其他阶段为当前阶段剩余）。</summary>
    public TimeSpan Remaining => Phase switch
    {
        FocusPhase.Focus => Max(TimeSpan.Zero, FocusDuration - Elapsed),
        FocusPhase.Break => Max(TimeSpan.Zero, BreakDuration - Elapsed),
        _ => TimeSpan.Zero
    };

    /// <summary>当前阶段完成进度 0..1。</summary>
    public double Progress01 => Phase switch
    {
        FocusPhase.Focus => Clamp01(Elapsed.TotalSeconds / FocusDuration.TotalSeconds),
        FocusPhase.Break => Clamp01(Elapsed.TotalSeconds / BreakDuration.TotalSeconds),
        _ => 0.0
    };

    /// <summary>开始专注（仅 Idle 时可触发；已在专注/短休则忽略）。</summary>
    public void StartFocus(DateTime now)
    {
        if (Phase != FocusPhase.Idle) return;
        Enter(FocusPhase.Focus, now);
    }

    /// <summary>开始短休（仅 Idle 时可触发；常用于"先歇会儿"）。专注中的短休请用 Stop 后重开。</summary>
    public void StartBreak(DateTime now)
    {
        if (Phase != FocusPhase.Idle) return;
        Enter(FocusPhase.Break, now);
    }

    /// <summary>停止当前专注/短休，回到 Idle（<b>不会</b>发放专注奖励，也不累计次数）。</summary>
    public void Stop(DateTime now)
    {
        if (Phase == FocusPhase.Idle) return;
        Enter(FocusPhase.Idle, now);
    }

    /// <summary>
    /// 外部定时器每约 1s 调用一次：按 <paramref name="now"/> 推进状态，触发提醒/完成事件。
    /// 要求 <paramref name="now"/> 单调不减（与墙钟一致即可；同一时刻重复调用安全）。
    /// </summary>
    public void Update(DateTime now)
    {
        _now = now;
        switch (Phase)
        {
            case FocusPhase.Focus:
                if (now >= PhaseStartedAt + FocusDuration)
                {
                    CompletedFocusCount++;
                    FocusCompleted?.Invoke(this, new FocusCompletedEventArgs(FocusRewardExp, CompletedFocusCount));
                    Enter(FocusPhase.Break, now);   // 完成提示先发，再切到短休并提示"短休开始"，体验更顺
                }
                else
                {
                    while (_nextReminderAt != DateTime.MinValue
                           && now >= _nextReminderAt
                           && _nextReminderAt < PhaseStartedAt + FocusDuration)
                    {
                        ReminderDue?.Invoke(this, new FocusReminderEventArgs(ReminderIndex(_nextReminderAt), Remaining));
                        _nextReminderAt += ReminderInterval;
                    }
                }
                break;

            case FocusPhase.Break:
                if (now >= PhaseStartedAt + BreakDuration)
                    Enter(FocusPhase.Idle, now);
                break;

            case FocusPhase.Idle:
            default:
                break;
        }
    }

    private void Enter(FocusPhase phase, DateTime now)
    {
        FocusPhase from = Phase;
        Phase = phase;
        PhaseStartedAt = now;
        _now = now;
        _nextReminderAt = (phase == FocusPhase.Focus) ? now + ReminderInterval : DateTime.MinValue;
        PhaseChanged?.Invoke(this, new FocusPhaseChangedEventArgs(from, phase, now));
    }

    private int ReminderIndex(DateTime at)
        => (int)Math.Round((at - PhaseStartedAt).TotalMinutes / ReminderInterval.TotalMinutes);

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a >= b ? a : b;

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
}

/// <summary>状态切换事件参数。</summary>
public sealed class FocusPhaseChangedEventArgs : EventArgs
{
    public FocusPhase From { get; }
    public FocusPhase To { get; }
    public DateTime At { get; }
    public FocusPhaseChangedEventArgs(FocusPhase from, FocusPhase to, DateTime at)
    {
        From = from;
        To = to;
        At = at;
    }
}

/// <summary>气泡提醒事件参数（第几次 5 分钟提醒 + 剩余时长）。</summary>
public sealed class FocusReminderEventArgs : EventArgs
{
    /// <summary>第几个提醒（1 表示第 5 分钟，2 表示第 10 分钟……）。</summary>
    public int Index { get; }

    /// <summary>触发时剩余时长。</summary>
    public TimeSpan Remaining { get; }

    public FocusReminderEventArgs(int index, TimeSpan remaining)
    {
        Index = index;
        Remaining = remaining;
    }
}

/// <summary>专注完成事件参数。</summary>
public sealed class FocusCompletedEventArgs : EventArgs
{
    /// <summary>本次专注发放的 EXP 奖励。</summary>
    public int RewardExp { get; }

    /// <summary>完成后的累计专注次数（含本次）。</summary>
    public int TotalCompleted { get; }

    public FocusCompletedEventArgs(int rewardExp, int totalCompleted)
    {
        RewardExp = rewardExp;
        TotalCompleted = totalCompleted;
    }
}
