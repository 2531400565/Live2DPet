using System;
using System.Collections.Generic;

namespace Live2DPet.Core.Pet;

/// <summary>照顾操作的返回结果：成功，或因为某状态已满/过低而被拒绝（供上层弹对应萌系提示）。</summary>
public enum CareResult
{
    Success,  // 照顾成功
    Full,     // 已经很饱，吃不下了
    Tired,    // 玩够啦 / 累了
    Hungry,   // 太饿，玩不动
    Clean     // 已经很干净，不用洗澡
}


/// <summary>桌宠情绪（用于状态联动微表情）：中性 / 开心 / 难过 / 受惊。</summary>
public enum PetMood
{
    Neutral,
    Happy,
    Sad,
    Surprised
}


/// <summary>每日登录签到结果（由 RecordDailyLogin 返回，供上层发奖 + 弹气泡）。</summary>
public sealed class LoginReport
{
    /// <summary>今天是否已经签过到（已签到则不再重复发奖）。</summary>
    public bool IsNewDay;
    /// <summary>是否是"认识的第一天"（连续=1）。</summary>
    public bool IsFirstDay;
    /// <summary>是否达成 7 天整数倍里程碑（额外奖励）。</summary>
    public bool Milestone;
    /// <summary>当前连续天数。</summary>
    public int Streak;
    /// <summary>本次签到奖励的好感度。</summary>
    public int RewardAffection;
    /// <summary>本次签到奖励的经验。</summary>
    public int RewardExp;
}


/// <summary>
/// 桌宠养成状态：好感度 / 饱食 / 心情 / 清洁 / 等级 / 经验。
/// 纯逻辑（无引擎/Win32 依赖），负责计分、等级/亲密度计算、离线衰减。
/// 由 PetStateStore 持久化到 config/petstate.json。
/// </summary>
public sealed class PetState
{
    /// <summary>好感度 0..1000</summary>
    public int Affection { get; set; }

    /// <summary>饱食度 0..100</summary>
    public int Satiety { get; set; } = 80;

    /// <summary>心情 0..100</summary>
    public int Mood { get; set; } = 80;

    /// <summary>清洁度 0..100</summary>
    public int Cleanliness { get; set; } = 80;

    /// <summary>宠物等级（1..MaxLevel）</summary>
    public int Level { get; set; } = 1;

    /// <summary>当前等级已累积经验</summary>
    public int Experience { get; set; }

    /// <summary>上次保存时间（UTC），用于离线衰减。</summary>
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>上次签到的本地日期（yyyy-MM-dd），用于判定"今天是否已签到"。</summary>
    public string LastLoginDay { get; set; } = "";

    /// <summary>连续签到天数（断签次日重置为 1）。</summary>
    public int LoginStreak { get; set; }

    /// <summary>累计启动天数。</summary>
    public int TotalLogins { get; set; }

    /// <summary>历史最长连续天数。</summary>
    public int BestStreak { get; set; }

    // ---- 统计（成就系统用）----
    /// <summary>累计互动次数（摸/戳/双击/喂食/玩耍/洗澡/键盘回应）。</summary>
    public int TotalInteractions { get; set; }
    /// <summary>累计喂食次数。</summary>
    public int TotalFeeds { get; set; }
    /// <summary>累计玩耍次数。</summary>
    public int TotalPlays { get; set; }
    /// <summary>累计洗澡次数。</summary>
    public int TotalBaths { get; set; }
    /// <summary>累计在线时长（秒），用于统计面板。</summary>
    public long TotalOnlineSeconds { get; set; }
    /// <summary>已解锁成就的 id 列表（持久化）。</summary>
    public List<string> UnlockedAchievements { get; set; } = new();

    public const int MaxLevel = 10;

    /// <summary>等级里程碑：到达这些等级时解锁额外内容（台词更亲昵 + 里程碑提示）。</summary>
    public static readonly int[] MilestoneLevels = { 3, 5, 7, 10 };

    /// <summary>某等级是否为里程碑等级（3/5/7/10）。</summary>
    public static bool IsMilestoneLevel(int level) => Array.IndexOf(MilestoneLevels, level) >= 0;

    // ---- 亲密度（好感等级）----
    public int AffectionLevel => Affection switch
    {
        < 200 => 1,
        < 400 => 2,
        < 600 => 3,
        < 800 => 4,
        _ => 5
    };

    public string AffectionName => AffectionLevel switch
    {
        1 => "陌生",
        2 => "认识",
        3 => "熟悉",
        4 => "亲密",
        _ => "挚友"
    };

    // ---- 成长阶段 ----
    public string StageName => Level switch
    {
        < 3 => "幼年期",
        < 6 => "成长期",
        < 9 => "成熟期",
        _ => "完全体"
    };

    /// <summary>升到下一级所需经验（每级递增）。</summary>
    public int ExpToNext => Level >= MaxLevel ? 0 : Level * 50;

    /// <summary>增加好感度，返回是否发生亲密度等级提升。</summary>
    public bool AddAffection(int amount)
    {
        int before = AffectionLevel;
        Affection = Math.Clamp(Affection + amount, 0, 1000);
        return AffectionLevel > before;
    }

    /// <summary>增加经验，返回是否升级（可能连升多级）。</summary>
    public bool AddExperience(int amount)
    {
        if (Level >= MaxLevel) return false;
        Experience += amount;
        bool leveled = false;
        while (Level < MaxLevel && Experience >= ExpToNext)
        {
            Experience -= ExpToNext;
            Level++;
            leveled = true;
        }
        if (Level >= MaxLevel) Experience = 0;
        return leveled;
    }

    // ---- 照顾操作（带限制，返回 CareResult 供上层决定弹什么气泡）----
    /// <summary>喂食：饱食度已高则拒绝（"已经很饱了"）。否则+25饱食并加好感/经验。</summary>
    public CareResult Feed()
    {
        if (Satiety >= 90) return CareResult.Full;
        Satiety = Math.Clamp(Satiety + 25, 0, 100);
        AddAffection(5);
        AddExperience(3);
        return CareResult.Success;
    }

    /// <summary>玩耍：太饿或心情已满则拒绝。否则+25心情、-8饱食并加好感/经验。</summary>
    public CareResult Play()
    {
        if (Satiety <= 12) return CareResult.Hungry;
        if (Mood >= 90) return CareResult.Tired;
        Mood = Math.Clamp(Mood + 25, 0, 100);
        Satiety = Math.Clamp(Satiety - 8, 0, 100);
        AddAffection(5);
        AddExperience(3);
        return CareResult.Success;
    }

    /// <summary>洗澡：清洁度已高则拒绝（"已经香喷喷啦"）。否则+30清洁并加好感/经验。</summary>
    public CareResult Bathe()
    {
        if (Cleanliness >= 90) return CareResult.Clean;
        Cleanliness = Math.Clamp(Cleanliness + 30, 0, 100);
        AddAffection(5);
        AddExperience(3);
        return CareResult.Success;
    }

    // ---- 衰减（minutes 为经过的分钟数）----
    public void Decay(double minutes)
    {
        Satiety = Math.Clamp(Satiety - (int)Math.Round(minutes / 30.0), 0, 100);
        Mood = Math.Clamp(Mood - (int)Math.Round(minutes / 45.0), 0, 100);
        Cleanliness = Math.Clamp(Cleanliness - (int)Math.Round(minutes / 60.0), 0, 100);
    }

    /// <summary>离线衰减：离线超过 5 分钟才开始算，最多按 24 小时封顶。</summary>
    public void ApplyOfflineDecay(DateTime nowUtc)
    {
        var offline = nowUtc - LastSeen;
        if (offline <= TimeSpan.FromMinutes(5)) return;
        var capped = TimeSpan.FromHours(24);
        if (offline > capped) offline = capped;
        Decay(offline.TotalMinutes);
    }

    // ---- 状态判定（供气泡提醒/表情切换）----
    public bool IsHungry => Satiety <= 30;
    public bool WantsPlay => Mood <= 30;
    public bool IsDirty => Cleanliness <= 30;

    // ---- 每日签到（连续天数 + 奖励）----
    /// <summary>
    /// 记录一次启动登录：若本地日期与上次不同（跨天），则累计启动天数、更新连续天数、发奖励。
    /// 同一天重复启动不重复发奖。返回 LoginReport 供上层弹气泡。
    /// </summary>
    public LoginReport RecordDailyLogin(DateTime localNow)
    {
        var today = localNow.ToString("yyyy-MM-dd");
        var r = new LoginReport { IsNewDay = false, Streak = LoginStreak };
        if (LastLoginDay == today) return r;   // 今天已签到，直接返回

        r.IsNewDay = true;
        TotalLogins++;

        // 连续天数：昨天也登录过则 +1，否则断签重置为 1
        var yesterday = localNow.Date.AddDays(-1).ToString("yyyy-MM-dd");
        LoginStreak = (LastLoginDay == yesterday) ? LoginStreak + 1 : 1;
        BestStreak = Math.Max(BestStreak, LoginStreak);
        LastLoginDay = today;

        r.Streak = LoginStreak;
        r.IsFirstDay = LoginStreak == 1;

        // 基础奖励 + 7 天里程碑额外奖励
        int aff = 5, exp = 3;
        if (LoginStreak % 7 == 0) { aff += 10; exp += 5; r.Milestone = true; }
        r.RewardAffection = aff;
        r.RewardExp = exp;
        return r;
    }

    // ---- 成就 ----
    /// <summary>检测并解锁新成就，返回本次新解锁的成就列表（供上层弹提示）。已解锁的不会重复返回。</summary>
    public List<AchievementDef> CheckAchievements()
    {
        var newly = new List<AchievementDef>();
        foreach (var a in AchievementCatalog.All)
        {
            if (UnlockedAchievements.Contains(a.Id)) continue;
            if (AchievementCatalog.IsUnlocked(a, this))
            {
                UnlockedAchievements.Add(a.Id);
                newly.Add(a);
            }
        }
        return newly;
    }
}
