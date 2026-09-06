using System;
using System.Collections.Generic;
using Live2DPet.Core.Pet;
using Xunit;

namespace Live2DPet.Core.Tests;

/// <summary>番茄钟（专注陪伴）纯状态机：状态流转 / 5 分钟提醒 / 完成奖励 / 免打扰窗口。
/// 完全不依赖 WinForms —— 用显式 DateTime 驱动 Update，确定性可测。</summary>
public class FocusSessionTests
{
    private static readonly DateTime T0 = new(2026, 6, 1, 9, 0, 0);
    private static DateTime At(int minutes) => T0 + TimeSpan.FromMinutes(minutes);

    // ---- 初始状态 ----

    [Fact]
    public void NewSession_StartsIdle()
    {
        var fs = new FocusSession();
        Assert.Equal(FocusPhase.Idle, fs.Phase);
        Assert.False(fs.IsActive);
        Assert.Equal(TimeSpan.Zero, fs.Remaining);
        Assert.Equal(0.0, fs.Progress01);
    }

    [Fact]
    public void IllegalDurations_FallBackToDefaults()
    {
        var fs = new FocusSession(focusMinutes: 0, breakMinutes: -5, reminderMinutes: 0);
        Assert.Equal(TimeSpan.FromMinutes(FocusSession.DefaultFocusMinutes), fs.FocusDuration);
        Assert.Equal(TimeSpan.FromMinutes(FocusSession.DefaultBreakMinutes), fs.BreakDuration);
        Assert.Equal(TimeSpan.FromMinutes(FocusSession.DefaultReminderMinutes), fs.ReminderInterval);
    }

    // ---- 开始 / 停止 ----

    [Fact]
    public void StartFocus_FromIdle_EnterFocus()
    {
        var fs = new FocusSession();
        fs.StartFocus(T0);
        Assert.Equal(FocusPhase.Focus, fs.Phase);
        Assert.True(fs.IsFocusing);
        Assert.True(fs.IsActive);
        Assert.Equal(T0, fs.PhaseStartedAt);
    }

    [Fact]
    public void StartFocus_WhileActive_Ignored()
    {
        var fs = new FocusSession();
        fs.StartFocus(T0);
        fs.StartFocus(T0 + TimeSpan.FromMinutes(3));   // 再次开始应被忽略
        Assert.Equal(FocusPhase.Focus, fs.Phase);
        Assert.Equal(T0, fs.PhaseStartedAt);            // 起点不变
    }

    [Fact]
    public void StartBreak_FromIdle_EnterBreak()
    {
        var fs = new FocusSession();
        fs.StartBreak(T0);
        Assert.Equal(FocusPhase.Break, fs.Phase);
        Assert.True(fs.IsBreaking);
    }

    [Fact]
    public void StartBreak_WhileActive_Ignored()
    {
        var fs = new FocusSession();
        fs.StartFocus(T0);
        fs.StartBreak(T0 + TimeSpan.FromMinutes(1));   // 专注中不能开短休
        Assert.Equal(FocusPhase.Focus, fs.Phase);
    }

    [Fact]
    public void Stop_FromIdle_Ignored()
    {
        var fs = new FocusSession();
        fs.Stop(T0);                                    // 本就空闲
        Assert.Equal(FocusPhase.Idle, fs.Phase);
    }

    [Fact]
    public void Stop_BeforeCompletion_NoRewardAndBackToIdle()
    {
        var fs = new FocusSession();
        int completed = 0;
        fs.FocusCompleted += (_, _) => completed++;
        fs.StartFocus(T0);
        fs.Stop(T0 + TimeSpan.FromMinutes(3));          // 提前停止
        Assert.Equal(FocusPhase.Idle, fs.Phase);
        Assert.False(fs.IsActive);
        Assert.Equal(0, completed);
        Assert.Equal(0, fs.CompletedFocusCount);
    }

    // ---- 5 分钟提醒 ----

    [Fact]
    public void Reminder_FiresEveryFiveMinutesDuringFocus()
    {
        var fs = new FocusSession();
        var indices = new List<int>();
        fs.ReminderDue += (_, e) => indices.Add(e.Index);
        fs.StartFocus(T0);
        fs.Update(At(5));
        fs.Update(At(10));
        fs.Update(At(15));
        fs.Update(At(20));
        Assert.Equal(new[] { 1, 2, 3, 4 }, indices);    // 第 5/10/15/20 分钟各一次
    }

    [Fact]
    public void Reminder_NotFiredAtCompletionMinute()
    {
        var fs = new FocusSession();
        int count = 0;
        fs.ReminderDue += (_, _) => count++;
        fs.StartFocus(T0);
        fs.Update(At(25));                              // 第 25 分钟是完成时刻，不应再弹提醒
        Assert.Equal(0, count);
    }

    [Fact]
    public void Reminder_IdempotentForSameInstant()
    {
        var fs = new FocusSession();
        int count = 0;
        fs.ReminderDue += (_, _) => count++;
        fs.StartFocus(T0);
        fs.Update(At(5));
        fs.Update(At(5));                              // 同一时刻重复推进：不重复弹
        Assert.Equal(1, count);
    }

    [Fact]
    public void Reminder_OnlyDuringFocus_NotDuringBreak()
    {
        var fs = new FocusSession();
        int count = 0;
        fs.ReminderDue += (_, _) => count++;
        fs.StartFocus(T0);
        fs.Update(At(25));                             // → Break
        fs.Update(At(27));                             // 短休期间不应弹提醒
        Assert.Equal(0, count);
    }

    // ---- 完成 → Break → Idle ----

    [Fact]
    public void FocusCompletes_AwardsExpAndEntersBreak()
    {
        var fs = new FocusSession();
        int reward = -1;
        int total = -1;
        fs.FocusCompleted += (_, e) => { reward = e.RewardExp; total = e.TotalCompleted; };
        fs.StartFocus(T0);
        fs.Update(At(25));
        Assert.Equal(FocusPhase.Break, fs.Phase);
        Assert.True(fs.IsBreaking);
        Assert.Equal(1, fs.CompletedFocusCount);
        Assert.Equal(FocusSession.FocusRewardExp, reward);
        Assert.Equal(1, total);
    }

    [Fact]
    public void Break_AutoEndsBackToIdle()
    {
        var fs = new FocusSession();
        fs.StartFocus(T0);
        fs.Update(At(25));                             // → Break
        fs.Update(At(30));                             // 短休结束 → Idle
        Assert.Equal(FocusPhase.Idle, fs.Phase);
        Assert.False(fs.IsActive);
    }

    [Fact]
    public void MultipleSessions_AccumulateCount()
    {
        var fs = new FocusSession();
        int total = 0;
        fs.FocusCompleted += (_, e) => total = e.TotalCompleted;
        fs.StartFocus(T0);
        fs.Update(At(25));                             // 第 1 次完成 → Break
        fs.Update(At(30));                             // → Idle
        fs.StartFocus(At(30));                         // 再开一轮
        fs.Update(At(55));                             // 第 2 次完成
        Assert.Equal(2, fs.CompletedFocusCount);
        Assert.Equal(2, total);
    }

    // ---- 进度 / 时长 ----

    [Fact]
    public void ElapsedRemainingProgress_ComputedFromClock()
    {
        var fs = new FocusSession();
        fs.StartFocus(T0);
        fs.Update(At(10));
        Assert.Equal(TimeSpan.FromMinutes(10), fs.Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(15), fs.Remaining);
        Assert.InRange(fs.Progress01, 0.39, 0.41);
    }

    [Fact]
    public void Elapsed_ZeroWhenIdle()
    {
        var fs = new FocusSession();
        Assert.Equal(TimeSpan.Zero, fs.Elapsed);
        Assert.Equal(TimeSpan.Zero, fs.Remaining);
    }

    // ---- 状态切换事件序列 ----

    [Fact]
    public void PhaseChanged_Sequence_IdleToFocusToBreakToIdle()
    {
        var fs = new FocusSession();
        var seq = new List<(FocusPhase From, FocusPhase To)>();
        fs.PhaseChanged += (_, e) => seq.Add((e.From, e.To));
        fs.StartFocus(T0);
        fs.Update(At(25));                             // Focus → Break
        fs.Update(At(30));                             // Break → Idle
        Assert.Equal(3, seq.Count);
        Assert.Equal((FocusPhase.Idle, FocusPhase.Focus), seq[0]);
        Assert.Equal((FocusPhase.Focus, FocusPhase.Break), seq[1]);
        Assert.Equal((FocusPhase.Break, FocusPhase.Idle), seq[2]);
    }
}
