using Live2DPet.Core.Pet;
using Xunit;

namespace Live2DPet.Core.Tests;

/// <summary>PetState 核心逻辑：照顾限制、等级/羁绊成长、衰减、签到。</summary>
public class PetStateTests
{
    // ---- 照顾操作的限制 ----

    [Fact]
    public void Feed_WhenSatietyHigh_ReturnsFull_AndStatsUnchanged()
    {
        var s = new PetState { Satiety = 95 };
        var r = s.Feed();
        Assert.Equal(CareResult.Full, r);
        Assert.Equal(95, s.Satiety);
        Assert.Equal(0, s.TotalInteractions);
    }

    [Fact]
    public void Feed_Success_IncreasesSatietyAndRewards()
    {
        var s = new PetState { Satiety = 50 };
        var r = s.Feed();
        Assert.Equal(CareResult.Success, r);
        Assert.Equal(75, s.Satiety);
        Assert.Equal(5, s.Affection);        // AddAffection(5)
        Assert.Equal(3, s.Experience);
    }

    [Fact]
    public void Play_WhenTooHungry_ReturnsHungry()
    {
        var s = new PetState { Satiety = 10 };
        Assert.Equal(CareResult.Hungry, s.Play());
    }

    [Fact]
    public void Play_WhenMoodFull_ReturnsTired()
    {
        var s = new PetState { Satiety = 80, Mood = 95 };
        Assert.Equal(CareResult.Tired, s.Play());
    }

    [Fact]
    public void Bathe_WhenAlreadyClean_ReturnsClean()
    {
        var s = new PetState { Cleanliness = 90 };
        Assert.Equal(CareResult.Clean, s.Bathe());
    }

    // ---- 成长：等级 / 经验 ----

    [Fact]
    public void AddExperience_EnoughForMultipleLevels_LevelsUp()
    {
        var s = new PetState();   // Lv.1, 升到 Lv.2 需 50
        Assert.True(s.AddExperience(50));
        Assert.Equal(2, s.Level);
        Assert.Equal(0, s.Experience);
    }

    [Fact]
    public void AddExperience_UnderThreshold_NoLevelUp()
    {
        var s = new PetState();
        Assert.False(s.AddExperience(49));
        Assert.Equal(1, s.Level);
        Assert.Equal(49, s.Experience);
    }

    [Fact]
    public void AddExperience_NotEnoughForMultiLevel_KeepsRemainder()
    {
        var s = new PetState();   // Lv.1 需50 → Lv.2 需100
        Assert.False(s.AddExperience(120));   // 只升 1 级，余 70
        Assert.Equal(2, s.Level);
        Assert.Equal(70, s.Experience);
    }

    [Fact]
    public void AddExperience_AtMaxLevel_FlowsToBond()
    {
        var s = new PetState { Level = PetState.MaxLevel, Experience = 0 };
        Assert.False(s.AddExperience(199));   // 首级羁绊需 200
        Assert.Equal(0, s.BondLevel);
        Assert.Equal(199, s.BondExp);
        Assert.True(s.AddExperience(1));      // 升羁绊 Lv.1
        Assert.Equal(1, s.BondLevel);
        Assert.Equal("心之友", s.BondName);
    }

    [Fact]
    public void AddExperience_BondMaxed_NoLongerAccumulates()
    {
        var s = new PetState
        {
            Level = PetState.MaxLevel,
            BondLevel = PetState.MaxBondLevel,
            BondExp = 0
        };
        Assert.False(s.AddExperience(500));
        Assert.Equal(0, s.BondExp);
        Assert.Equal(PetState.MaxBondLevel, s.BondLevel);
        Assert.Equal("永恒羁绊", s.BondName);
    }

    [Fact]
    public void AddAffection_ClampsAt1000()
    {
        var s = new PetState { Affection = 995 };
        Assert.False(s.AddAffection(10));     // 995→1000 封顶，仍在 5 档（挚友），未越档
        Assert.Equal(1000, s.Affection);
        Assert.Equal(5, s.AffectionLevel);
        Assert.Equal("挚友", s.AffectionName);
    }

    [Fact]
    public void AddAffection_CrossingBoundary_RaisesAffectionLevel()
    {
        var s = new PetState { Affection = 795 };   // 4 档(亲密, <800) → 5 档(挚友, ≥800)
        Assert.True(s.AddAffection(10));
        Assert.Equal(805, s.Affection);
        Assert.Equal(5, s.AffectionLevel);
        Assert.Equal("挚友", s.AffectionName);
    }

    // ---- 衰减 / 离线 ----

    [Fact]
    public void Decay_ReducesStatsProportionally()
    {
        var s = new PetState { Satiety = 100, Mood = 100, Cleanliness = 100 };
        s.Decay(60);   // 1 小时：饱食 -2、心情 -1、清洁 -1
        Assert.Equal(98, s.Satiety);
        Assert.Equal(99, s.Mood);
        Assert.Equal(99, s.Cleanliness);
    }

    [Fact]
    public void ApplyOfflineDecay_Under5Minutes_NoChange()
    {
        var s = new PetState { Satiety = 100 };
        s.LastSeen = DateTime.UtcNow.AddMinutes(-4);
        s.ApplyOfflineDecay(DateTime.UtcNow);
        Assert.Equal(100, s.Satiety);
    }

    [Fact]
    public void ApplyOfflineDecay_Over24Hours_CappedAt24()
    {
        var s = new PetState { Satiety = 100 };
        s.LastSeen = DateTime.UtcNow.AddDays(-3);
        s.ApplyOfflineDecay(DateTime.UtcNow);
        // 24h：饱食 -48
        Assert.Equal(100 - 48, s.Satiety);
    }

    // ---- 每日签到 ----

    [Fact]
    public void RecordDailyLogin_SameDay_DoesNotRepeatReward()
    {
        var s = new PetState();
        var r1 = s.RecordDailyLogin(new DateTime(2026, 9, 5, 8, 0, 0));
        Assert.True(r1.IsNewDay);
        Assert.Equal(1, s.LoginStreak);
        var r2 = s.RecordDailyLogin(new DateTime(2026, 9, 5, 20, 0, 0));
        Assert.False(r2.IsNewDay);
        Assert.Equal(1, s.LoginStreak);
        Assert.Equal(1, s.TotalLogins);
    }

    [Fact]
    public void RecordDailyLogin_ConsecutiveDay_IncrementsStreak()
    {
        var s = new PetState();
        s.RecordDailyLogin(new DateTime(2026, 9, 4));
        var r = s.RecordDailyLogin(new DateTime(2026, 9, 5));
        Assert.True(r.IsNewDay);
        Assert.Equal(2, r.Streak);
        Assert.Equal(2, s.BestStreak);
    }

    [Fact]
    public void RecordDailyLogin_BrokenStreak_ResetsTo1()
    {
        var s = new PetState();
        s.RecordDailyLogin(new DateTime(2026, 9, 3));
        var r = s.RecordDailyLogin(new DateTime(2026, 9, 5));   // 断一天
        Assert.Equal(1, r.Streak);
        Assert.False(r.Milestone);
    }

    [Fact]
    public void RecordDailyLogin_7DayMilestone_GrantsBonusReward()
    {
        var s = new PetState();
        // 从 9/1 连签 7 天
        for (int i = 0; i < 7; i++)
        {
            var r = s.RecordDailyLogin(new DateTime(2026, 9, 1).AddDays(i));
            if (i == 6)
            {
                Assert.True(r.Milestone);
                Assert.True(r.RewardExp > 3);   // 里程碑加码
            }
        }
        Assert.Equal(7, s.LoginStreak);
        Assert.Equal(7, s.BestStreak);
    }
}
