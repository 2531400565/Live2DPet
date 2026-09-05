using System;
using Live2DPet.Core.Settings;
using Xunit;

namespace Live2DPet.Core.Tests;

public class DndClockTests
{
    private static AppSettings WithDnd(bool enabled, int startMin, int endMin)
    {
        var s = new AppSettings { DndEnabled = enabled, DndStart = startMin, DndEnd = endMin };
        return s;
    }

    [Fact]
    public void Disabled_AlwaysFalse()
    {
        var s = WithDnd(false, 22 * 60, 8 * 60);
        Assert.False(DndClock.IsActive(s, new DateTime(2026, 9, 5, 23, 30, 0)));
        Assert.False(DndClock.IsActive(s, new DateTime(2026, 9, 5, 12, 0, 0)));
    }

    [Fact]
    public void StartEqualsEnd_MeansNotConfigured_AlwaysFalse()
    {
        var s = WithDnd(true, 540, 540);   // 09:00 → 09:00
        Assert.False(DndClock.IsActive(s, new DateTime(2026, 9, 5, 9, 0, 0)));
        Assert.False(DndClock.IsActive(s, new DateTime(2026, 9, 5, 23, 0, 0)));
    }

    [Fact]
    public void SameDayWindow_InsideTrue_OutsideFalse()
    {
        var s = WithDnd(true, 22 * 60, 0);   // 22:00 → 24:00（同一日区间）
        Assert.True(DndClock.IsActive(s, new DateTime(2026, 9, 5, 22, 30, 0)));
        Assert.True(DndClock.IsActive(s, new DateTime(2026, 9, 5, 23, 59, 0)));
        Assert.False(DndClock.IsActive(s, new DateTime(2026, 9, 5, 21, 59, 0)));
        Assert.False(DndClock.IsActive(s, new DateTime(2026, 9, 5, 0, 1, 0)));  // 终点 00:00 为闭区间边界外
    }

    [Fact]
    public void CrossMidnightWindow_LateNightTrue_DaytimeFalse()
    {
        var s = WithDnd(true, 23 * 60, 8 * 60);   // 23:00 → 08:00
        Assert.True(DndClock.IsActive(s, new DateTime(2026, 9, 5, 23, 30, 0)));   // 起点含
        Assert.True(DndClock.IsActive(s, new DateTime(2026, 9, 5, 2, 0, 0)));      // 跨午夜中间
        Assert.True(DndClock.IsActive(s, new DateTime(2026, 9, 5, 7, 59, 0)));     // 终点前
        Assert.False(DndClock.IsActive(s, new DateTime(2026, 9, 5, 8, 0, 0)));     // 终点为开区间 → 关闭
        Assert.False(DndClock.IsActive(s, new DateTime(2026, 9, 5, 12, 0, 0)));
    }

    [Fact]
    public void SameDayNormalWindow_MorningInside_EveningOutside()
    {
        var s = WithDnd(true, 9 * 60, 12 * 60);   // 09:00 → 12:00
        Assert.True(DndClock.IsActive(s, new DateTime(2026, 9, 5, 10, 0, 0)));
        Assert.False(DndClock.IsActive(s, new DateTime(2026, 9, 5, 12, 0, 0)));   // 终点开区间
        Assert.False(DndClock.IsActive(s, new DateTime(2026, 9, 5, 8, 59, 0)));
    }
}
