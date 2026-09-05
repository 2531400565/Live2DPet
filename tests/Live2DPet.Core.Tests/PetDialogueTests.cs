using System.Globalization;
using Live2DPet.Core.Pet;
using Xunit;

namespace Live2DPet.Core.Tests;

/// <summary>文案库：节日/生日判定、时段问候、欢迎回来、随机选取的边界。</summary>
public class PetDialogueTests
{
    // ---- 公历节日 ----

    [Theory]
    [InlineData(2026, 1, 1, "元旦")]
    [InlineData(2026, 2, 14, "情人节")]
    [InlineData(2026, 12, 24, "平安夜")]
    [InlineData(2026, 12, 25, "圣诞")]
    public void FestivalGreeting_GregorianHoliday_ReturnsLine(int y, int m, int d, string keyword)
    {
        var line = PetDialogue.FestivalGreeting(new DateTime(y, m, d), null);
        Assert.NotNull(line);
        Assert.Contains(keyword, line);
    }

    [Fact]
    public void FestivalGreeting_OrdinaryDay_ReturnsNull()
    {
        // 2026-09-05 无农历/公历节日
        Assert.Null(PetDialogue.FestivalGreeting(new DateTime(2026, 9, 5), null));
    }

    [Fact]
    public void FestivalGreeting_Birthday_PriorityOverHoliday()
    {
        // 用户生日设为 1/1：即使当天是元旦也先弹生日
        var line = PetDialogue.FestivalGreeting(new DateTime(2026, 1, 1), "01-01");
        Assert.NotNull(line);
        Assert.Contains("生日", line);
    }

    [Fact]
    public void FestivalGreeting_LunarSpringFestival_ReturnsLine()
    {
        // 动态定位 2026 年农历正月初一，避免硬编码日期出错
        var cal = new ChineseLunisolarCalendar();
        var springFestival = Enumerable.Range(1, 60)
            .Select(d => new DateTime(2026, 2, 1).AddDays(d))
            .First(x => cal.GetMonth(x) == 1 && cal.GetDayOfMonth(x) == 1);
        var line = PetDialogue.FestivalGreeting(springFestival, null);
        Assert.NotNull(line);
        Assert.Contains("春节", line);
    }

    // ---- 时段问候 ----

    [Theory]
    [InlineData(6)]   // 早上
    [InlineData(10)]  // 上午
    [InlineData(14)]  // 下午
    [InlineData(20)]  // 晚上
    [InlineData(23)]  // 深夜
    public void GreetingFor_AlwaysReturnsSomeGreeting(int hour)
    {
        var line = PetDialogue.GreetingFor(new DateTime(2026, 9, 5, hour, 0, 0));
        Assert.False(string.IsNullOrWhiteSpace(line));
    }

    // ---- 欢迎回来分档 ----

    [Fact]
    public void WelcomeBack_Under1Hour_ReturnsShortGapLine()
    {
        Assert.Contains("一小会儿", PetDialogue.WelcomeBack(TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void WelcomeBack_LongGap_ReturnsLongingLine()
    {
        Assert.Contains("好久好久", PetDialogue.WelcomeBack(TimeSpan.FromHours(10)));
    }

    // ---- 随机选取的边界 ----

    [Fact]
    public void Pick_ReturnsMemberOfGivenLines()
    {
        string[] lines = { "a", "b", "c" };
        for (int i = 0; i < 20; i++)
            Assert.Contains(PetDialogue.Pick(lines), lines);
    }

    [Fact]
    public void PickReaction_LowLevel_NeverReturnsAdvancedLines()
    {
        // level<5 时必从给定基础台词里选（40% 高级台词只对 >=5 开放）
        string[] replies = { "基础" };
        for (int i = 0; i < 50; i++)
            Assert.Equal("基础", PetDialogue.PickReaction(replies, 3));
    }

    // ---- 等级/羁绊提示（防止文案回归）----

    [Fact]
    public void LevelUp_ContainsLevelAndStage()
    {
        var line = PetDialogue.LevelUp(10, "完全体");
        Assert.Contains("Lv.10", line);
        Assert.Contains("完全体", line);
    }

    [Fact]
    public void BondUp_ContainsBondName()
    {
        var line = PetDialogue.BondUp(1, "心之友");
        Assert.Contains("心之友", line);
    }

    [Fact]
    public void BondEternalLines_AreNonEmpty()
    {
        Assert.NotEmpty(PetDialogue.BondEternalLines);
        Assert.All(PetDialogue.BondEternalLines, l => Assert.False(string.IsNullOrWhiteSpace(l)));
    }
}
