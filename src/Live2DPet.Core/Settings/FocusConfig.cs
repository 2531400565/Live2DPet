using System;
using Live2DPet.Core.Pet;

namespace Live2DPet.Core.Settings;

/// <summary>
/// 番茄钟设置的统一范围约束：防止手改 settings.json 写入离谱值导致体验异常。
/// 用户走设置界面时被 NumericUpDown 限制；这里是"读盘兜底"的单一路径。
/// </summary>
public static class FocusConfig
{
    /// <summary>专注时长上限（分钟）。</summary>
    public const int MaxFocusMinutes = 180;

    /// <summary>短休时长上限（分钟）。</summary>
    public const int MaxBreakMinutes = 60;

    /// <summary>提醒间隔上限（分钟）。</summary>
    public const int MaxReminderMinutes = 60;

    /// <summary>每日目标上限（个番茄）。0 = 关闭。</summary>
    public const int MaxDailyGoal = 24;

    /// <summary>把四个专注相关设置规整到合法范围：&lt;=0 或越界回退默认值。</summary>
    public static AppSettings Normalize(AppSettings s)
    {
        s.FocusMinutes = ClampMin(s.FocusMinutes, FocusSession.DefaultFocusMinutes, MaxFocusMinutes);
        s.BreakMinutes = ClampMin(s.BreakMinutes, FocusSession.DefaultBreakMinutes, MaxBreakMinutes);
        s.ReminderMinutes = ClampMin(s.ReminderMinutes, FocusSession.DefaultReminderMinutes, MaxReminderMinutes);
        s.DailyFocusGoal = Math.Max(0, Math.Min(s.DailyFocusGoal, MaxDailyGoal));
        return s;
    }

    private static int ClampMin(int value, int fallback, int max)
        => value <= 0 ? fallback : Math.Min(value, max);
}
