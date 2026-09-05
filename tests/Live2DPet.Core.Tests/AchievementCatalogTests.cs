using Live2DPet.Core.Pet;
using Xunit;

namespace Live2DPet.Core.Tests;

/// <summary>成就判定条件（与 PetState 统计字段联动）。</summary>
public class AchievementCatalogTests
{
    private static AchievementDef ById(string id) =>
        AchievementCatalog.All.First(a => a.Id == id);

    [Fact]
    public void AllAchievements_AreUnique()
    {
        Assert.Equal(AchievementCatalog.All.Count,
            AchievementCatalog.All.Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public void FirstTouch_UnlocksAtOneInteraction()
    {
        var s = new PetState { TotalInteractions = 1 };
        Assert.True(AchievementCatalog.IsUnlocked(ById("first_touch"), s));
        Assert.False(AchievementCatalog.IsUnlocked(ById("first_touch"), new PetState()));
    }

    [Fact]
    public void Touch100_UnlocksAt100Interactions()
    {
        var s = new PetState { TotalInteractions = 100 };
        Assert.True(AchievementCatalog.IsUnlocked(ById("touch_100"), s));
        Assert.False(AchievementCatalog.IsUnlocked(ById("touch_100"), new PetState { TotalInteractions = 99 }));
    }

    [Fact]
    public void LevelAchievements_CheckLevelThresholds()
    {
        Assert.True(AchievementCatalog.IsUnlocked(ById("level_5"), new PetState { Level = 5 }));
        Assert.False(AchievementCatalog.IsUnlocked(ById("level_5"), new PetState { Level = 4 }));
        Assert.True(AchievementCatalog.IsUnlocked(ById("level_10"), new PetState { Level = PetState.MaxLevel }));
    }

    [Fact]
    public void Affection5_UnlocksAtMaxAffectionLevel()
    {
        var s = new PetState { Affection = 1000 };   // 挚友
        Assert.True(AchievementCatalog.IsUnlocked(ById("affection_5"), s));
        Assert.False(AchievementCatalog.IsUnlocked(ById("affection_5"), new PetState { Affection = 300 }));
    }

    [Theory]
    [InlineData("feed_50", nameof(PetState.TotalFeeds), 50)]
    [InlineData("play_50", nameof(PetState.TotalPlays), 50)]
    [InlineData("bath_30", nameof(PetState.TotalBaths), 30)]
    public void CareCountAchievements_UnlockAtThreshold(string id, string stat, int threshold)
    {
        var s = new PetState();
        typeof(PetState).GetProperty(stat)!.SetValue(s, threshold);
        Assert.True(AchievementCatalog.IsUnlocked(ById(id), s));
    }

    [Theory]
    [InlineData("streak_7", 7)]
    [InlineData("streak_30", 30)]
    public void StreakAchievements_UnlockAtBestStreak(string id, int streak)
    {
        Assert.True(AchievementCatalog.IsUnlocked(ById(id), new PetState { BestStreak = streak }));
        Assert.False(AchievementCatalog.IsUnlocked(ById(id), new PetState { BestStreak = streak - 1 }));
    }

    [Fact]
    public void CheckAchievements_ReturnsOnlyNewlyUnlocked()
    {
        var s = new PetState { TotalInteractions = 1 };
        var first = s.CheckAchievements();
        Assert.Contains(first, a => a.Id == "first_touch");

        var second = s.CheckAchievements();   // 再次调用不应重复返回
        Assert.DoesNotContain(second, a => a.Id == "first_touch");
        Assert.Equal(1, s.UnlockedAchievements.Count);
    }
}
