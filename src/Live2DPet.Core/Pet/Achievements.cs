using System;
using System.Collections.Generic;

namespace Live2DPet.Core.Pet;

/// <summary>单个成就定义。</summary>
public sealed record AchievementDef(string Id, string Name, string Desc, int RewardAffection = 0, int RewardExp = 0)
{
    /// <summary>解锁时发放的奖励文案（如无奖励则为空）。</summary>
    public string RewardText =>
        (RewardAffection, RewardExp) switch
        {
            (> 0, > 0) => $"（奖励：好感+{RewardAffection}、经验+{RewardExp}）",
            (> 0, _) => $"（奖励：好感+{RewardAffection}）",
            (_, > 0) => $"（奖励：经验+{RewardExp}）",
            _ => ""
        };
}

/// <summary>
/// 成就目录：定义全部成就 + 判定条件（纯逻辑，无引擎/Win32 依赖，可单测）。
/// 达成状态（已解锁的成就 id 列表）持久化在 PetState.UnlockedAchievements 里。
/// </summary>
public static class AchievementCatalog
{
    public static readonly IReadOnlyList<AchievementDef> All = new[]
    {
        new AchievementDef("first_touch", "初次见面", "第一次和桌宠互动", 5, 2),
        new AchievementDef("touch_100", "百次陪伴", "累计互动 100 次", 10, 5),
        new AchievementDef("affection_5", "挚友", "亲密度达到最高（挚友）", 20, 10),
        new AchievementDef("level_5", "茁壮成长", "升到 Lv.5", 15, 0),
        new AchievementDef("level_10", "完全体", "升到 Lv.10（满级）", 30, 0),
        new AchievementDef("feed_50", "大胃王", "累计喂食 50 次", 10, 5),
        new AchievementDef("play_50", "活力四射", "累计玩耍 50 次", 10, 5),
        new AchievementDef("bath_30", "香喷喷", "累计洗澡 30 次", 8, 4),
        new AchievementDef("streak_7", "一周之约", "最长连续陪伴 7 天", 15, 10),
        new AchievementDef("streak_30", "长情陪伴", "最长连续陪伴 30 天", 30, 20),
        new AchievementDef("focus_1", "初试专注", "完成 1 次专注陪伴", 5, 5),
        new AchievementDef("focus_10", "专注达人", "累计专注陪伴 10 次", 15, 15),
    };

    /// <summary>判断某成就当前是否达成。</summary>
    public static bool IsUnlocked(AchievementDef a, PetState s) => a.Id switch
    {
        "first_touch" => s.TotalInteractions >= 1,
        "touch_100" => s.TotalInteractions >= 100,
        "affection_5" => s.AffectionLevel >= 5,
        "level_5" => s.Level >= 5,
        "level_10" => s.Level >= 10,
        "feed_50" => s.TotalFeeds >= 50,
        "play_50" => s.TotalPlays >= 50,
        "bath_30" => s.TotalBaths >= 30,
        "streak_7" => s.BestStreak >= 7,
        "streak_30" => s.BestStreak >= 30,
        "focus_1" => s.TotalFocusSessions >= 1,
        "focus_10" => s.TotalFocusSessions >= 10,
        _ => false
    };
}
