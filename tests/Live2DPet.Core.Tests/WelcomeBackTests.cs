using System;
using System.IO;
using Live2DPet.Core.Pet;
using Xunit;

namespace Live2DPet.Core.Tests;

/// <summary>
/// 功能⑤ Welcome Back 单测：启动/退出时刻字段持久化 + 首启/欢迎回来文案的 {name} 令牌约定。
/// </summary>
public class WelcomeBackTests
{
    // ---- PetState.LastLaunchTime / LastExitTime ----

    [Fact]
    public void PetState_NewInstance_TimesDefaultToMinValue()
    {
        var s = new PetState();
        Assert.Equal(DateTime.MinValue, s.LastLaunchTime);
        Assert.Equal(DateTime.MinValue, s.LastExitTime);
    }

    [Fact]
    public void PetState_Times_RoundTripThroughStore()
    {
        var launch = new DateTime(2026, 9, 6, 20, 0, 0, DateTimeKind.Utc);
        var exit = new DateTime(2026, 9, 6, 21, 30, 0, DateTimeKind.Utc);
        var s = new PetState { LastLaunchTime = launch, LastExitTime = exit };

        string path = TempStatePath();
        try
        {
            PetStateStore.Save(s, path);
            var loaded = PetStateStore.Load(path);
            Assert.Equal(launch, loaded.LastLaunchTime);
            Assert.Equal(exit, loaded.LastExitTime);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void PetState_Times_MissingInOldJson_LoadsAsMinValue()
    {
        // 旧存档没有这两个字段 → 反序列化应得到 MinValue（兼容，不崩）
        string path = TempStatePath();
        try
        {
            File.WriteAllText(path, "{\"Affection\":1,\"Level\":3}");
            var loaded = PetStateStore.Load(path);
            Assert.Equal(DateTime.MinValue, loaded.LastLaunchTime);
            Assert.Equal(DateTime.MinValue, loaded.LastExitTime);
        }
        finally
        {
            TryDelete(path);
        }
    }

    // ---- WelcomeBack 文案含 {name} 令牌（Say 时统一替换为宠物昵称）----

    [Fact]
    public void WelcomeBack_Under1Hour_CarriesNameTokenAndKeyword()
    {
        string line = PetDialogue.WelcomeBack(TimeSpan.FromMinutes(30));
        Assert.Contains("一小会儿", line);
        Assert.Contains(PetDialogue.NameToken, line);
    }

    [Fact]
    public void WelcomeBack_Hours1To3_CarriesNameToken()
    {
        string line = PetDialogue.WelcomeBack(TimeSpan.FromHours(2));
        Assert.Contains("欢迎回来", line);
        Assert.Contains(PetDialogue.NameToken, line);
    }

    [Fact]
    public void WelcomeBack_Hours3To8_CarriesNameToken()
    {
        string line = PetDialogue.WelcomeBack(TimeSpan.FromHours(5));
        Assert.Contains("好久不见", line);
        Assert.Contains(PetDialogue.NameToken, line);
    }

    [Fact]
    public void WelcomeBack_LongGap_CarriesNameTokenAndKeyword()
    {
        string line = PetDialogue.WelcomeBack(TimeSpan.FromHours(10));
        Assert.Contains("好久好久", line);
        Assert.Contains(PetDialogue.NameToken, line);
    }

    [Fact]
    public void WelcomeBack_Named_ReplacesTokenWithPetName()
    {
        string line = PetDialogue.Named(PetDialogue.WelcomeBack(TimeSpan.FromHours(10)), "小埋");
        Assert.DoesNotContain(PetDialogue.NameToken, line);
        Assert.Contains("小埋", line);
    }

    // ---- 当天首启（每日签到）与早安问候文案含 {name} ----

    [Fact]
    public void DailyLogin_AllBranches_CarryNameToken()
    {
        Assert.Contains(PetDialogue.NameToken, PetDialogue.DailyLogin(new LoginReport { IsFirstDay = true, Streak = 1 }));
        Assert.Contains(PetDialogue.NameToken, PetDialogue.DailyLogin(new LoginReport { Streak = 3 }));
        Assert.Contains(PetDialogue.NameToken, PetDialogue.DailyLogin(new LoginReport { Milestone = true, Streak = 7 }));
    }

    [Fact]
    public void DailyLogin_Milestone_KeepsStreakNumber()
    {
        string line = PetDialogue.DailyLogin(new LoginReport { Milestone = true, Streak = 7 });
        Assert.Contains("7", line);
        Assert.Contains(PetDialogue.NameToken, line);
    }

    [Fact]
    public void GreetingFor_Morning_CarriesNameToken()
    {
        for (int h = 5; h <= 8; h++)
        {
            string line = PetDialogue.GreetingFor(new DateTime(2026, 9, 6, h, 0, 0));
            Assert.Contains(PetDialogue.NameToken, line);   // 早安问候融入昵称
        }
    }

    [Fact]
    public void GreetingFor_Morning_Named_ProducesReadableText()
    {
        string raw = PetDialogue.GreetingFor(new DateTime(2026, 9, 6, 7, 0, 0));
        string line = PetDialogue.Named(raw, "小埋");
        Assert.DoesNotContain(PetDialogue.NameToken, line);
        Assert.Contains("小埋", line);
    }

    // ---- helpers ----

    private static string TempStatePath()
        => Path.Combine(Path.GetTempPath(), "l2dpet_wb_state_" + Guid.NewGuid().ToString("N") + ".json");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 清理失败不致命 */ }
    }
}
