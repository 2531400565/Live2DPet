using Live2DPet.Core.Pet;
using Xunit;

namespace Live2DPet.Core.Tests;

/// <summary>专注陪伴成就：初试专注（1 次）/ 专注达人（10 次）的解锁条件与目录定义。</summary>
public class AchievementCatalogFocusTests
{
    private static AchievementDef Def(string id)
        => Assert.Single(AchievementCatalog.All, a => a.Id == id);

    [Fact]
    public void Catalog_ContainsFocusAchievements()
    {
        Assert.Equal("初试专注", Def("focus_1").Name);
        Assert.Equal("专注达人", Def("focus_10").Name);
        Assert.True(Def("focus_1").RewardExp > 0);
        Assert.True(Def("focus_10").RewardExp > 0);
    }

    [Fact]
    public void Focus1_UnlocksAtOneSession()
    {
        var s = new PetState { TotalFocusSessions = 0 };
        Assert.False(AchievementCatalog.IsUnlocked(Def("focus_1"), s));
        s.TotalFocusSessions = 1;
        Assert.True(AchievementCatalog.IsUnlocked(Def("focus_1"), s));
    }

    [Fact]
    public void Focus10_UnlocksAtTenSessions()
    {
        var s = new PetState { TotalFocusSessions = 9 };
        Assert.False(AchievementCatalog.IsUnlocked(Def("focus_10"), s));
        s.TotalFocusSessions = 10;
        Assert.True(AchievementCatalog.IsUnlocked(Def("focus_10"), s));
        // 10 次时也满足"初试专注"
        Assert.True(AchievementCatalog.IsUnlocked(Def("focus_1"), s));
    }

    [Fact]
    public void FocusAchievements_IgnoredForZero()
    {
        var s = new PetState();
        Assert.False(AchievementCatalog.IsUnlocked(Def("focus_1"), s));
        Assert.False(AchievementCatalog.IsUnlocked(Def("focus_10"), s));
    }
}
