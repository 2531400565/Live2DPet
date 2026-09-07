using System;
using System.IO;
using System.Text.Json;
using Live2DPet.Core.Pet;
using Live2DPet.Core.Settings;
using Xunit;

namespace Live2DPet.Core.Tests;

/// <summary>
/// A1 番茄钟设置化 + 每日目标 的 Core 单测：
/// FocusConfig 范围钳位 / SettingsStore 载入归一化 / PetState 每日计数 / 每日目标庆祝台词。
/// </summary>
public class FocusDailyTests
{
    // ---- FocusConfig 钳位 ----
    [Fact]
    public void Normalize_ZeroOrNegative_FallsBackToDefaults()
    {
        var s = new AppSettings { FocusMinutes = 0, BreakMinutes = -3, ReminderMinutes = 0, DailyFocusGoal = -7 };
        FocusConfig.Normalize(s);
        Assert.Equal(FocusSession.DefaultFocusMinutes, s.FocusMinutes);
        Assert.Equal(FocusSession.DefaultBreakMinutes, s.BreakMinutes);
        Assert.Equal(FocusSession.DefaultReminderMinutes, s.ReminderMinutes);
        Assert.Equal(0, s.DailyFocusGoal);
    }

    [Fact]
    public void Normalize_Oversized_CappedAtMax()
    {
        var s = new AppSettings { FocusMinutes = 9999, BreakMinutes = 500, ReminderMinutes = 90, DailyFocusGoal = 99 };
        FocusConfig.Normalize(s);
        Assert.Equal(FocusConfig.MaxFocusMinutes, s.FocusMinutes);
        Assert.Equal(FocusConfig.MaxBreakMinutes, s.BreakMinutes);
        Assert.Equal(FocusConfig.MaxReminderMinutes, s.ReminderMinutes);
        Assert.Equal(FocusConfig.MaxDailyGoal, s.DailyFocusGoal);
    }

    [Fact]
    public void Normalize_ValidValues_KeptUnchanged()
    {
        var s = new AppSettings { FocusMinutes = 30, BreakMinutes = 10, ReminderMinutes = 5, DailyFocusGoal = 8 };
        FocusConfig.Normalize(s);
        Assert.Equal(30, s.FocusMinutes);
        Assert.Equal(10, s.BreakMinutes);
        Assert.Equal(5, s.ReminderMinutes);
        Assert.Equal(8, s.DailyFocusGoal);
    }

    // ---- SettingsStore 载入即归一化（手改 JSON 的兜底）----
    [Fact]
    public void SettingsStore_Load_NormalizesOutOfRangeFocusValues()
    {
        var path = Path.Combine(Path.GetTempPath(), "l2dp_focuscfg_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """{"FocusMinutes":999,"BreakMinutes":0,"DailyFocusGoal":-3}""");
            var loaded = SettingsStore.Load(path);
            Assert.Equal(FocusConfig.MaxFocusMinutes, loaded.FocusMinutes);
            Assert.Equal(FocusSession.DefaultBreakMinutes, loaded.BreakMinutes);
            Assert.Equal(0, loaded.DailyFocusGoal);
            Assert.Equal(FocusSession.DefaultReminderMinutes, loaded.ReminderMinutes);   // 缺省字段照常默认
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ---- PetState 每日计数 ----
    [Fact]
    public void RecordFocus_FirstOfDay_StartsFromOne()
    {
        var st = new PetState();
        var now = new DateTime(2026, 9, 7, 10, 30, 0);
        var r = st.RecordFocusCompleted(now);

        Assert.True(r.IsNewDay);
        Assert.Equal(1, r.CountToday);
        Assert.Equal(1, st.FocusDoneToday);
        Assert.Equal("2026-09-07", st.FocusDay);
    }

    [Fact]
    public void RecordFocus_SameDay_IncrementsWithoutReset()
    {
        var st = new PetState();
        var day = new DateTime(2026, 9, 7, 9, 0, 0);
        st.RecordFocusCompleted(day);
        var r = st.RecordFocusCompleted(day.AddHours(2));   // 同一天不同时刻

        Assert.False(r.IsNewDay);
        Assert.Equal(2, r.CountToday);
        Assert.Equal(2, st.FocusDoneToday);
        Assert.Equal(0, st.TotalFocusSessions);   // 终身计数由上层另行维护，二者解耦
    }

    [Fact]
    public void RecordFocus_NextDay_ResetsCounter()
    {
        var st = new PetState();
        st.RecordFocusCompleted(new DateTime(2026, 9, 7, 23, 59, 0));
        var r = st.RecordFocusCompleted(new DateTime(2026, 9, 8, 0, 5, 0));   // 跨天

        Assert.True(r.IsNewDay);
        Assert.Equal(1, r.CountToday);
        Assert.Equal(1, st.FocusDoneToday);
        Assert.Equal("2026-09-08", st.FocusDay);
    }

    // ---- 每日目标庆祝台词 ----
    [Fact]
    public void DailyGoalLines_NonEmpty_NoBlankLines()
    {
        Assert.NotEmpty(PetDialogue.DailyGoalLines);
        foreach (var line in PetDialogue.DailyGoalLines)
            Assert.False(string.IsNullOrWhiteSpace(line));
    }

    [Fact]
    public void DailyGoalLines_NameToken_SubstitutedBySay()
    {
        // 台词组里应含 {name} 占位（至少一句），Named 会统一替换为昵称
        Assert.Contains(PetDialogue.DailyGoalLines, l => l.Contains(PetDialogue.NameToken, StringComparison.Ordinal));

        var named = PetDialogue.Named(PetDialogue.DailyGoalLines[0], "皮皮");
        Assert.False(named.Contains(PetDialogue.NameToken, StringComparison.Ordinal));
        Assert.NotEqual(PetDialogue.DailyGoalLines[0], named);   // 确实发生了替换
    }
}
