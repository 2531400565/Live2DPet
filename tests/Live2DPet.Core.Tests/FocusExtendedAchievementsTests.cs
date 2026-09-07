using System.Linq;
using Live2DPet.Core.Pet;
using Xunit;

namespace Live2DPet.Core.Tests;

/// <summary>B1'：专注成就延伸 focus_25 / focus_100 的目录与解锁判定。</summary>
public class FocusExtendedAchievementsTests
{
    private static AchievementDef ById(string id)
        => AchievementCatalog.All.First(a => a.Id == id);

    [Fact]
    public void Catalog_ContainsNewFocusAchievements()
    {
        Assert.Contains(AchievementCatalog.All, a => a.Id == "focus_25" && a.Name == "心无旁骛");
        Assert.Contains(AchievementCatalog.All, a => a.Id == "focus_100" && a.Name == "专注大师");
    }

    [Fact]
    public void Focus25_UnlocksExactlyAt25()
    {
        var s = new PetState { TotalFocusSessions = 24 };
        Assert.False(AchievementCatalog.IsUnlocked(ById("focus_25"), s));

        s.TotalFocusSessions = 25;
        Assert.True(AchievementCatalog.IsUnlocked(ById("focus_25"), s));
    }

    [Fact]
    public void Focus100_UnlocksExactlyAt100()
    {
        var s = new PetState { TotalFocusSessions = 99 };
        Assert.False(AchievementCatalog.IsUnlocked(ById("focus_100"), s));

        s.TotalFocusSessions = 100;
        Assert.True(AchievementCatalog.IsUnlocked(ById("focus_100"), s));
    }

    [Fact]
    public void CheckAchievements_UnlocksAllFocusTiersAt100()
    {
        var s = new PetState { TotalFocusSessions = 100 };
        var newly = s.CheckAchievements();
        Assert.Contains(newly, a => a.Id == "focus_100");
        Assert.Contains(newly, a => a.Id == "focus_25");
        Assert.Contains(newly, a => a.Id == "focus_10");
        Assert.Contains(newly, a => a.Id == "focus_1");   // 100 次时低档专注成就一并解锁
    }
}
